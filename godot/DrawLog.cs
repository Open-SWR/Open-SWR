using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

/// <summary>
/// Draw log for diffing against the Frida draw trace of the original. Off unless env
/// OPENSWR_DRAWLOG = a file path; then one JSON line per draw call, field names as the Frida records where the meaning matches.
/// Coordinates are logical 640 x 480 screen pixels (canvas transform of the drawing item, plus any DrawSetTransform); bitmap sizes
/// are the original sizes.
/// Events:
/// - bm_blit / bm_kblit: a bitmap drawn opaque / colour-keyed: res "module:2:id" (null for art not from the install), dx, dy,
///   dw, dh (dst rect), sx, sy, w, h (src rect in original pixels), key (palette index, keyed only); with res null, src names
///   the art's origin (composed:..., mod:..., edata:..., rowpic:..., movie:..., or the drawing node's parent class for node textures);
///   no src only for a texture nobody tagged;
/// - drawtext: s, r [left, top, right, bottom], fmt (DrawText flags, decimal), font (openswr font index; -1 = Godot's own font: map
///   names and counts, Label / Button / LineEdit / RichTextLabel text), c (RGB as "red,green,blue", decimal);
/// - rectangle: a solid fill as GDI Rectangle with NULL_PEN: r [left, top, right + 1, bottom + 1], brush (RGB), pen null;
/// - moveto / lineto: a 1-px line, lineto with pen (RGB); end point exclusive as in GDI; outlines and polylines as one
///   moveto + lineto per segment;
/// - ellipse (openswr, no trace event): a circle / arc of the galaxy map, r = bounding box, brush (filled) or pen (outline).
/// Every record also carries seq, ev, t (ms) and node (the drawing node's class). Godot nodes that draw themselves (Label, Button,
/// LineEdit, RichTextLabel, TextureRect, ColorRect, Sprite2D) are logged from their draw signal, hooked for every node in the tree
/// while the log is on.
/// Known differences from the Frida trace (a diff tool maps these):
/// - colours sit inline as RRGGBB (rectangle: a = alpha when below 1); the trace has separate brush / pen / textcolor events
///   with COLORREF layout (flag byte 2, then blue, green, red);
/// - dx / dy are screen coordinates; the trace's are relative to the destination bitmap;
/// - res sits inline; the trace has it on bm_new;
/// - w / h are always real sizes; in the trace 0 means the full bitmap;
/// - t is engine ms (Time.GetTicksMsec); the trace uses epoch ms;
/// - strings are JSON-escaped by System.Text.Json.
/// </summary>
public static class DrawLog
{
    static readonly StreamWriter? Out = Open();
    public static readonly bool On = Out is not null;
    static readonly Dictionary<Texture2D, (string? Res, Vector2I Size, int Key, string? Src)> Tags = [];
    static readonly HashSet<ulong> Watched = []; // a node re-added to the tree (FrontEnd.Show) keeps its one handler
    static long _seq;

    static DrawLog() => Hook();

    /// <summary>The log file, or null (off) when the env var is unset or the file cannot be opened: drawing must not break.</summary>
    static StreamWriter? Open()
    {
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DRAWLOG") is not { Length: > 0 } path) return null;
        try { return new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true }; }
        catch (System.Exception e) { GD.PushError($"OPENSWR_DRAWLOG: cannot open {path}: {e.Message}; draw log off"); return null; }
    }

    /// <summary>Remember where an install bitmap texture came from (key -1: opaque; type 302 = an advisor delta frame).</summary>
    public static void Register(Texture2D t, string module, ushort id, Vector2I size, int key, int type = 2)
    {
        if (On) Tags[t] = ($"{module.ToLowerInvariant()}:{type}:{id}", size, key, null);
    }

    /// <summary>Remember the origin of a texture that is not an install bitmap (logged as res null + src; key -1: opaque).</summary>
    public static void Tag(Texture2D t, string src, int key = -1)
    {
        if (On) Tags[t] = (null, (Vector2I)t.GetSize(), key, src);
    }

    /// <summary>
    /// Bitmap draw; <paramref name="src"/> in original bitmap pixels, null = the whole bitmap; <paramref name="xf"/> = the draw
    /// transform (DrawSetTransform); <paramref name="origin"/> = src tag for an untagged texture.
    /// </summary>
    public static void Bitmap(CanvasItem c, Texture2D? t, Rect2 dst, Rect2? src = null, Transform2D? xf = null, string? origin = null)
    {
        if (!On || t is null) return;
        bool known = Tags.TryGetValue(t, out var tag);
        var size = known ? tag.Size : (Vector2I)t.GetSize();
        var s = src ?? new Rect2(0, 0, size.X, size.Y);
        var d = Screen(c, dst, xf);
        var f = Begin(known && tag.Key >= 0 ? "bm_kblit" : "bm_blit", c);
        f.Append(",\"res\":").Append(tag.Res is { } res ? Json(res) : "null");
        if ((known ? tag.Src : origin) is { } from) f.Append(",\"src\":").Append(Json(from));
        Num(f, "dx", d.Position.X); Num(f, "dy", d.Position.Y); Num(f, "dw", d.Size.X); Num(f, "dh", d.Size.Y);
        Num(f, "sx", s.Position.X); Num(f, "sy", s.Position.Y); Num(f, "w", s.Size.X); Num(f, "h", s.Size.Y);
        if (known && tag.Key >= 0) Num(f, "key", tag.Key);
        End(f);
    }

    public static void Text(CanvasItem c, Rect2 rect, string text, int font, Color colour, OpenSWR.Data.TextFormat format, Transform2D? xf = null)
    {
        if (!On) return;
        var f = Begin("drawtext", c);
        f.Append(",\"s\":").Append(Json(text)).Append(",\"r\":").Append(Ltrb(Screen(c, rect, xf), 0));
        f.Append(",\"fmt\":").Append(format.Win32).Append(",\"font\":").Append(font).Append(",\"c\":\"").Append(Rgb(colour)).Append('"');
        End(f);
    }

    /// <summary>Godot DrawString at baseline <paramref name="at"/> in Godot's own font (font -1).</summary>
    public static void String(CanvasItem c, Font font, Vector2 at, string text, int size, Color colour, Transform2D? xf = null)
    {
        if (!On) return;
        Text(c, new Rect2(at.X, at.Y - font.GetAscent(size), font.GetStringSize(text, fontSize: size)), text, -1, colour, default, xf);
    }

    public static void Fill(CanvasItem c, Rect2 rect, Color brush, Transform2D? xf = null)
    {
        if (!On) return;
        var f = Begin("rectangle", c);
        f.Append(",\"r\":").Append(Ltrb(Screen(c, rect, xf), 1)).Append(",\"brush\":\"").Append(Rgb(brush)).Append("\",\"pen\":null");
        if (brush.A < 1) Num(f, "a", brush.A);
        End(f);
    }

    /// <summary>Vertical or horizontal 1-px line covering <paramref name="rect"/> (width or height 1), as MoveTo + LineTo.</summary>
    public static void Line(CanvasItem c, Rect2 rect, Color pen)
    {
        if (!On) return;
        bool vertical = rect.Size.X <= 1;
        Segment(c, rect.Position, vertical ? new Vector2(rect.Position.X, rect.End.Y) : new Vector2(rect.End.X, rect.Position.Y), pen);
    }

    /// <summary>A line from <paramref name="a"/> to <paramref name="b"/> (local points) as MoveTo + LineTo.</summary>
    public static void Segment(CanvasItem c, Vector2 a, Vector2 b, Color pen, Transform2D? xf = null)
    {
        if (!On) return;
        var x = c.GetGlobalTransformWithCanvas() * (xf ?? Transform2D.Identity);
        var f = Begin("moveto", c);
        Num(f, "x", (x * a).X); Num(f, "y", (x * a).Y);
        End(f);
        f = Begin("lineto", c);
        Num(f, "x", (x * b).X); Num(f, "y", (x * b).Y);
        f.Append(",\"pen\":\"").Append(Rgb(pen)).Append('"');
        End(f);
    }

    /// <summary>A polyline through local points (Godot DrawPolyline), one segment per pair.</summary>
    public static void Polyline(CanvasItem c, Vector2[] points, Color pen, Transform2D? xf = null)
    {
        if (!On) return;
        for (int i = 1; i < points.Length; i++) Segment(c, points[i - 1], points[i], pen, xf);
    }

    /// <summary>An unfilled rect (Godot DrawRect filled: false) as its four edges.</summary>
    public static void Frame(CanvasItem c, Rect2 r, Color pen, Transform2D? xf = null)
    {
        if (!On) return;
        Polyline(c, [r.Position, new Vector2(r.End.X, r.Position.Y), r.End, new Vector2(r.Position.X, r.End.Y), r.Position], pen, xf);
    }

    /// <summary>A circle or arc (openswr ev ellipse): <paramref name="filled"/> = brush, else pen.</summary>
    public static void Ellipse(CanvasItem c, Vector2 centre, float radius, Color colour, bool filled, Transform2D? xf = null)
    {
        if (!On) return;
        var f = Begin("ellipse", c);
        f.Append(",\"r\":").Append(Ltrb(Screen(c, new Rect2(centre - Vector2.One * radius, Vector2.One * 2 * radius), xf), 0));
        f.Append(filled ? ",\"brush\":\"" : ",\"pen\":\"").Append(Rgb(colour)).Append('"');
        End(f);
    }

    static Rect2 Screen(CanvasItem c, Rect2 r, Transform2D? xf = null) => c.GetGlobalTransformWithCanvas() * (xf is { } x ? x * r : r);

    /// <summary>Hooks the draw signal of every self-drawing node in the tree, now and as nodes enter (log on only).</summary>
    static void Hook()
    {
        if (!On || Engine.GetMainLoop() is not SceneTree tree) return;
        tree.NodeAdded += Watch;
        Walk(tree.Root);

        static void Walk(Node n)
        {
            Watch(n);
            foreach (var child in n.GetChildren()) Walk(child);
        }
    }

    static void Watch(Node n)
    {
        if (n is not (Label or Button or LineEdit or RichTextLabel or TextureRect or ColorRect or Sprite2D) || !Watched.Add(n.GetInstanceId())) return;
        switch (n)
        {
            case Label l: l.Draw += () => Text(l, new Rect2(Vector2.Zero, l.Size), l.Text, -1, Tint(l, l.LabelSettings?.FontColor ?? l.GetThemeColor("font_color")), default); break;
            case Button b: b.Draw += () => Text(b, new Rect2(Vector2.Zero, b.Size), b.Text, -1, Tint(b, b.GetThemeColor("font_color")), default); break;
            case LineEdit e: e.Draw += () => Text(e, new Rect2(Vector2.Zero, e.Size), e.Text, -1, Tint(e, e.GetThemeColor("font_color")), default); break;
            case RichTextLabel r: r.Draw += () => Text(r, new Rect2(Vector2.Zero, r.Size), r.GetParsedText(), -1, Tint(r, r.GetThemeColor("default_color")), default); break;
            case TextureRect t: t.Draw += () => Bitmap(t, t.Texture, new Rect2(Vector2.Zero, t.Size), origin: Origin(t)); break;
            case ColorRect r when r.Material is null: r.Draw += () => Fill(r, new Rect2(Vector2.Zero, r.Size), r.Color); break; // shader rects (zoom outline invert) have no fill colour
            case Sprite2D s:
                s.Draw += () =>
                {
                    if (s.Texture is not { } tex) return;
                    var size = tex.GetSize();
                    Bitmap(s, tex, new Rect2((s.Centered ? -size / 2 : Vector2.Zero) + s.Offset, size), origin: Origin(s));
                };
                break;
        }
    }

    /// <summary>Text colour as drawn: times SelfModulate and the Modulate of the item and its canvas-item ancestors.</summary>
    static Color Tint(CanvasItem c, Color colour)
    {
        colour *= c.SelfModulate;
        for (Node? n = c; n is CanvasItem i; n = n.GetParent()) colour *= i.Modulate;
        return colour;
    }

    /// <summary>src of an untagged node texture: the parent's class (e.g. AdvisorDisplay sprite frames, TacticalScreen view).</summary>
    static string Origin(Node n) => n.GetParent()?.GetType().Name ?? n.GetType().Name;

    static StringBuilder Begin(string ev, CanvasItem c) =>
        new StringBuilder("{\"seq\":").Append(_seq++).Append(",\"ev\":\"").Append(ev).Append("\",\"t\":").Append(Time.GetTicksMsec())
            .Append(",\"node\":").Append(Json(c.GetType().Name));

    static void End(StringBuilder f)
    {
        Out!.WriteLine(f.Append('}').ToString()); // main thread only (all drawing)
    }

    static void Num(StringBuilder f, string name, float v) => f.Append(",\"").Append(name).Append("\":").Append(v.ToString(CultureInfo.InvariantCulture));

    static string Ltrb(Rect2 r, int extra) => string.Create(CultureInfo.InvariantCulture,
        $"[{r.Position.X},{r.Position.Y},{r.End.X + extra},{r.End.Y + extra}]");

    static string Rgb(Color c) => $"{c.R8},{c.G8},{c.B8}";

    static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);
}
