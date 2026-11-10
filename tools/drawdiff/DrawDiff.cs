using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenSWR.Data;

namespace OpenSWR.DrawDiff;

/// <summary>
/// One drawn thing, normalised from either log: kind bitmap / text / fill / line. X / Y = screen position (null: not resolved);
/// W / H = drawn size. Attribute strings are compared as they are; null means the log has no value.
/// </summary>
public sealed class Item
{
    public required string Kind { get; init; }
    public string? Res { get; init; }
    /// <summary>Text: "length:first 8 hex of SHA-256 (UTF-8)" (<see cref="Strings.Hash"/>); the match key.</summary>
    public string? TextHash { get; init; }
    /// <summary>Text as logged, null when the log holds only the hash.</summary>
    public string? Text { get; init; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public int W { get; init; }
    public int H { get; init; }
    /// <summary>Bitmap source rect "sx,sy wxh".</summary>
    public string? Src { get; init; }
    public int? Key { get; init; }
    public int? Fmt { get; init; }
    /// <summary>"height/weight/face".</summary>
    public string? Font { get; init; }
    /// <summary>RGB hex (text colour, brush, pen of a line); "iN" = palette index; "none" = null brush / pen.</summary>
    public string? Colour { get; init; }
    /// <summary>Outline pen of a filled rectangle.</summary>
    public string? Pen { get; init; }
    /// <summary>Where it was drawn (original: target bitmap or DC; openswr: node class). Context only, not compared.</summary>
    public string Where { get; set; } = "";
    /// <summary>ms since the first record of the log.</summary>
    public long T { get; init; }

    public string Group => Kind switch
    {
        "bitmap" => $"bitmap {Res ?? "(not from the install)"}",
        "text" => $"text {TextHash}",
        _ => $"{Kind} {W}x{H}",
    };

    public IEnumerable<(string Name, string? Value)> Attributes()
    {
        yield return ("size", $"{W}x{H}");
        if (Kind == "bitmap") { yield return ("src", Src); yield return ("key", Key?.ToString(CultureInfo.InvariantCulture)); }
        if (Kind == "text") { yield return ("fmt", Fmt is { } f ? $"0x{f:x}" : null); yield return ("font", Font); }
        yield return ("colour", Colour);
        if (Kind is "fill" or "ellipse") yield return ("pen", Pen);
    }

    public string Signature => Group + "|" + string.Join("|", Attributes().Select(a => a.Value));
    public string Full => Signature + "|" + Position;
    public string Position => X is { } x && Y is { } y ? $"({x},{y})" : "(?)";

    public string Describe()
    {
        var s = new StringBuilder();
        if (Kind == "text") s.Append(Text is null ? TextHash : Strings.Quote(Text)).Append(' ');
        s.Append("at ").Append(Position);
        foreach (var (n, v) in Attributes()) if (v is not null) s.Append(' ').Append(n).Append(' ').Append(v);
        return s.ToString();
    }
}

public static class Strings
{
    /// <summary>Length plus the first 8 hex digits of SHA-256 over UTF-8: vectors keep no original strings.</summary>
    public static string Hash(string s) =>
        $"{s.Length}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..8]}";

    public static string Quote(string s) => JsonSerializer.Serialize(s);

    /// <summary>COLORREF "0x02BBGGRR" / "0x00BBGGRR" as RGB hex; 0x01 flag (PALETTEINDEX) as "iN".</summary>
    public static string? ColorRef(string? hex)
    {
        if (hex is null) return null;
        uint v = uint.Parse(hex.StartsWith("0x") ? hex[2..] : hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        if (v >> 24 == 1) return $"i{v & 0xffff}";
        return $"{v & 0xff:x2}{(v >> 8) & 0xff:x2}{(v >> 16) & 0xff:x2}";
    }

    public static int Num(JsonElement e, string name, int def = 0) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? (int)Math.Round(p.GetDouble()) : def;

    public static double? Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;

    public static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    public static int[]? Ints(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray().Select(x => (int)Math.Round(x.GetDouble())).ToArray() : null;

    /// <summary>Records of a JSON-lines log; lines that do not parse (a cut-off last line) are skipped.</summary>
    public static IEnumerable<JsonElement> Records(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonElement e;
            try { using var d = JsonDocument.Parse(line); e = d.RootElement.Clone(); }
            catch (JsonException) { continue; }
            if (e.ValueKind == JsonValueKind.Object) yield return e;
        }
    }

    /// <summary>Text hash of a record: "sh" (already hashed) or the hash of "s".</summary>
    public static (string? Hash, string? Text) TextOf(JsonElement e) =>
        Str(e, "sh") is { } h ? (h, null) : Str(e, "s") is { } s ? (Hash(s), s) : (null, null);
}

/// <summary>
/// Reader for the Frida draw trace of the original (tools/frida/draw_agent.js). Bitmap objects get screen origins where the chain
/// resolves (<see cref="Resolve"/>); GDI drawing takes its font / colours / brush / pen from the DC state at the time.
/// </summary>
public sealed class TraceReader
{
    /// <summary>A drawing target: a bitmap object, the screen, or a DC whose bitmap and position are not in the trace.</summary>
    sealed class Surf(string name, string kind, int w, int h, string? res)
    {
        public readonly string Name = name, Kind = kind;
        public readonly int W = w, H = h;
        public readonly string? Res = res;
        public (int X, int Y)? Origin;
        /// <summary>The surface this origin was derived from (null: a screen root or not placed).</summary>
        public Surf? From;
        public bool Root;
        public bool Composite => Res is null && Kind is "canvas" or "section";
        public string Describe() => Root ? $"screen ({Name})" : $"{Name} {Kind} {W}x{H}" + (Origin is { } o ? $" @({o.X},{o.Y})" : "");
    }

    sealed class Dc(string id, string kind)
    {
        public readonly string Id = id, Kind = kind;
        public int W, H;
        public Surf? Selected, Own;
        public string Font = SystemFont;
        public string TextColour = "000000";
        public string Brush = "ffffff", Pen = "000000";
        public int CurX, CurY;
    }

    /// <summary>B's origin in T's coordinates; Capture = opaque blit of a region of B into T (T taken from B).</summary>
    readonly record struct Edge(Surf B, Surf T, int Ox, int Oy, bool Capture);

    const string SystemFont = "16/700/System"; // stock SYSTEM_FONT, the default font of a DC (LOGFONT height 16, weight 700)

    readonly Dictionary<string, Surf> _bms = [];
    readonly Dictionary<string, Surf> _gdiBitmaps = [];
    readonly Dictionary<string, Dc> _dcs = [];
    readonly Dictionary<string, string> _fonts = [], _brushes = [], _pens = [];
    readonly List<Edge> _edges = [];
    readonly List<(Item Item, Surf Surf, int Lx, int Ly)> _items = [];
    string? _lastSection;
    long? _t0;

    public static List<Item> Read(IEnumerable<string> lines, (long From, long To)? window = null)
    {
        var r = new TraceReader();
        foreach (var e in Strings.Records(lines)) r.Add(e);
        r.Resolve();
        var items = new List<Item>();
        foreach (var (item, surf, lx, ly) in r._items)
        {
            if (window is { } w && (item.T < w.From || item.T > w.To)) continue;
            if (surf.Origin is { } o) { item.X = o.X + lx; item.Y = o.Y + ly; }
            item.Where = surf.Describe();
            items.Add(item);
        }
        return items;
    }

    Surf Bm(string? tag) => tag is not null && _bms.TryGetValue(tag, out var s) ? s : new Surf(tag ?? "?", "unknown", 0, 0, null);

    Dc DcOf(string? id)
    {
        id ??= "?";
        return _dcs.TryGetValue(id, out var d) ? d : _dcs[id] = new Dc(id, "unknown");
    }

    Surf SurfOf(Dc d) => d.Selected ?? (d.Own ??= new Surf(d.Id, d.Kind, d.W, d.H, null));

    /// <summary>
    /// A paint DC that receives the backbuffer (a 640 x 480 section) unshifted (x == sx, y == sy) is the top-level window, at screen
    /// (0, 0); partial repaints included (Inferred: child windows never get the whole backbuffer copied in place).
    /// </summary>
    void ScreenIf(Surf b, Dc d, int x, int y, int sx, int sy)
    {
        if (d.Kind != "paint" || !b.Composite || b.Kind != "section" || (b.W, b.H) != (640, 480) || x != sx || y != sy) return;
        var s = SurfOf(d);
        s.Root = true;
        s.Origin = (0, 0);
    }

    void Add(JsonElement e)
    {
        string ev = Strings.Str(e, "ev") ?? "";
        long t = e.TryGetProperty("t", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt64() : 0;
        if (ev is "meta" or "hook" or "module") return;
        _t0 ??= t;
        t -= _t0.Value;
        switch (ev)
        {
            case "bm_new":
            {
                string tag = Strings.Str(e, "bm")!, kind = Strings.Str(e, "kind")!;
                var s = _bms[tag] = new Surf(tag, kind, Math.Abs(Strings.Num(e, "w")), Math.Abs(Strings.Num(e, "h")), Strings.Str(e, "res"));
                // the DIB section's GDI bitmap is created just before the object (CreateDIBSection inside the constructor)
                if (kind == "section" && _lastSection is not null) { _gdiBitmaps[_lastSection] = s; _lastSection = null; }
                break;
            }
            case "bm_free": _bms.Remove(Strings.Str(e, "bm")!); break;
            case "bitmap":
            {
                string obj = Strings.Str(e, "obj")!;
                if (Strings.Str(e, "kind") == "section") _lastSection = obj;
                _gdiBitmaps[obj] = new Surf(obj, Strings.Str(e, "kind") ?? "bitmap", Math.Abs(Strings.Num(e, "w")), Math.Abs(Strings.Num(e, "h")), null);
                break;
            }
            case "dc": _dcs[Strings.Str(e, "dc")!] = new Dc(Strings.Str(e, "dc")!, Strings.Str(e, "kind") + (Strings.Str(e, "from") is { } f ? "<" + DcOf(f).Kind : "")); break;
            case "paint":
            {
                var r = Strings.Ints(e, "r") ?? [0, 0, 0, 0];
                _dcs[Strings.Str(e, "dc")!] = new Dc(Strings.Str(e, "dc")!, "paint") { W = r[2] - r[0], H = r[3] - r[1] };
                break;
            }
            case "font":
                _fonts[Strings.Str(e, "font")!] = $"{Strings.Num(e, "height")}/{Strings.Num(e, "weight")}/{Strings.Str(e, "face")}";
                break;
            case "brush": _brushes[Strings.Str(e, "obj")!] = Strings.ColorRef(Strings.Str(e, "color"))!; break;
            case "pen": _pens[Strings.Str(e, "obj")!] = Strings.Num(e, "style") == 5 ? "none" : Strings.ColorRef(Strings.Str(e, "color"))!; break;
            case "select": Select(DcOf(Strings.Str(e, "dc")), Strings.Str(e, "obj") ?? ""); break;
            case "textcolor": DcOf(Strings.Str(e, "dc")).TextColour = Strings.ColorRef(Strings.Str(e, "color")) ?? "000000"; break;
            case "bm_blit":
            case "bm_kblit":
            {
                Surf b = Bm(Strings.Str(e, "src")), d = Bm(Strings.Str(e, "dst"));
                int dx = Strings.Num(e, "dx"), dy = Strings.Num(e, "dy"), sx = Strings.Num(e, "sx"), sy = Strings.Num(e, "sy");
                _edges.Add(new Edge(b, d, dx - sx, dy - sy, ev == "bm_blit"));
                if (b.Res is not null) AddBitmap(b, d, dx, dy, sx, sy, Strings.Num(e, "w"), Strings.Num(e, "h"), ev == "bm_kblit" ? Strings.Num(e, "key") : null, t);
                break;
            }
            // bm_kdraw is skipped: it runs bm_kblit with the source's own key, and that call is traced too
            case "bm_draw":
            {
                var dc = DcOf(Strings.Str(e, "dc"));
                Surf b = Bm(Strings.Str(e, "bm")), d = SurfOf(dc);
                int x = Strings.Num(e, "x"), y = Strings.Num(e, "y"), sx = Strings.Num(e, "sx"), sy = Strings.Num(e, "sy");
                ScreenIf(b, dc, x, y, sx, sy);
                _edges.Add(new Edge(b, d, x - sx, y - sy, false));
                if (b.Res is not null) AddBitmap(b, d, x, y, sx, sy, Strings.Num(e, "w"), Strings.Num(e, "h"), null, t);
                break;
            }
            case "bitblt":
            {
                // a bitmap object's own DC copied to a window: places that object (the bm_draw copy is recorded above as well)
                if (Strings.Str(e, "src") is not { } src || DcOf(src).Selected is not { Composite: true } b) break;
                var dc = DcOf(Strings.Str(e, "dst"));
                int x = Strings.Num(e, "x"), y = Strings.Num(e, "y"), sx = Strings.Num(e, "sx"), sy = Strings.Num(e, "sy");
                ScreenIf(b, dc, x, y, sx, sy);
                _edges.Add(new Edge(b, SurfOf(dc), x - sx, y - sy, false));
                break;
            }
            case "bm_fill":
            {
                var d = Bm(Strings.Str(e, "dst"));
                Place(new Item { Kind = "fill", W = Strings.Num(e, "w"), H = Strings.Num(e, "h"), Colour = $"i{Strings.Num(e, "c")}", T = t },
                    d, Strings.Num(e, "x"), Strings.Num(e, "y"));
                break;
            }
            case "drawtext":
            {
                int fmt = int.Parse(Strings.Str(e, "fmt")![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if ((fmt & 0x400) != 0) break; // DT_CALCRECT measures, draws nothing
                var dc = DcOf(Strings.Str(e, "dc"));
                var r = Strings.Ints(e, "r") ?? [0, 0, 0, 0];
                var (hash, text) = Strings.TextOf(e);
                Place(new Item { Kind = "text", TextHash = hash, Text = text, W = r[2] - r[0], H = r[3] - r[1], Fmt = fmt, Font = dc.Font, Colour = dc.TextColour, T = t },
                    SurfOf(dc), r[0], r[1]);
                break;
            }
            case "rectangle":
            {
                var dc = DcOf(Strings.Str(e, "dc"));
                var r = Strings.Ints(e, "r") ?? [0, 0, 0, 0];
                Place(new Item { Kind = "fill", W = r[2] - r[0], H = r[3] - r[1], Colour = dc.Brush, Pen = dc.Pen, T = t }, SurfOf(dc), r[0], r[1]);
                break;
            }
            case "moveto": { var dc = DcOf(Strings.Str(e, "dc")); dc.CurX = Strings.Num(e, "x"); dc.CurY = Strings.Num(e, "y"); break; }
            case "lineto":
            {
                var dc = DcOf(Strings.Str(e, "dc"));
                int x = Strings.Num(e, "x"), y = Strings.Num(e, "y");
                Place(new Item { Kind = "line", W = Math.Abs(x - dc.CurX), H = Math.Abs(y - dc.CurY), Colour = dc.Pen, T = t },
                    SurfOf(dc), Math.Min(x, dc.CurX), Math.Min(y, dc.CurY));
                dc.CurX = x; dc.CurY = y;
                break;
            }
        }
    }

    void Select(Dc dc, string obj)
    {
        if (_fonts.TryGetValue(obj, out var f)) dc.Font = f;
        else if (_brushes.TryGetValue(obj, out var b)) dc.Brush = b;
        else if (_pens.TryGetValue(obj, out var p)) dc.Pen = p;
        else if (_gdiBitmaps.TryGetValue(obj, out var s))
        {
            dc.Selected = s;
            if (!s.Composite) { dc.W = s.W; dc.H = s.H; dc.Selected = null; dc.Own = new Surf($"{dc.Id}:{s.Name}", $"{dc.Kind} {s.Kind}", s.W, s.H, null); }
        }
        else if (obj.StartsWith("stock", StringComparison.Ordinal) && int.TryParse(obj[5..], out int n))
        {
            switch (n)
            {
                case 0: dc.Brush = "ffffff"; break;
                case 1: dc.Brush = "c0c0c0"; break;
                case 2: dc.Brush = "808080"; break;
                case 3: dc.Brush = "404040"; break;
                case 4: dc.Brush = "000000"; break;
                case 5: dc.Brush = "none"; break;
                case 6: dc.Pen = "ffffff"; break;
                case 7: dc.Pen = "000000"; break;
                case 8: dc.Pen = "none"; break;
                case 13: dc.Font = SystemFont; break;
                case >= 10 and <= 17: dc.Font = obj; break;
            }
        }
        // anything else (a raw handle: the DC's original bitmap / font put back) changes nothing that is drawn later
    }

    void AddBitmap(Surf b, Surf d, int dx, int dy, int sx, int sy, int w, int h, int? key, long t)
    {
        if (w == 0) w = b.W - sx; // 0 = the full source: from sx / sy to the bitmap edge (Inferred)
        if (h == 0) h = b.H - sy;
        Place(new Item { Kind = "bitmap", Res = b.Res, W = w, H = h, Src = $"{sx},{sy} {w}x{h}", Key = key, T = t }, d, dx, dy);
    }

    void Place(Item item, Surf surf, int lx, int ly) => _items.Add((item, surf, lx, ly));

    /// <summary>
    /// Screen origins of the bitmap objects, to a fixed point. Forward: a composite (canvas / section) drawn into placed targets at one
    /// position only is at that position (canvas -> backbuffer -> screen). Backward: a canvas filled by an opaque blit of a region of a
    /// placed composite is at that region (a button canvas taking its background from the backbuffer, Inferred from the blit shape).
    /// </summary>
    void Resolve()
    {
        while (Step(forward: true) || Step(forward: false)) { }
        Unplace();
    }

    /// <summary>
    /// A composite whose draws into placed targets disagree on its position (it was placed by the targets resolved first) is not
    /// placed, nor is anything placed from it.
    /// </summary>
    void Unplace()
    {
        var bad = _edges.Where(e => e.B.Composite && !e.B.Root && e.B.Origin is not null && e.T.Origin is not null)
            .GroupBy(e => e.B, e => (e.T.Origin!.Value.X + e.Ox, e.T.Origin!.Value.Y + e.Oy))
            .Where(g => g.Distinct().Count() > 1).Select(g => g.Key).ToList();
        var placed = _edges.SelectMany(e => new[] { e.B, e.T }).Distinct().Where(s => s.Origin is not null && !s.Root).ToList();
        foreach (var s in bad) { s.Origin = null; s.From = null; }
        for (bool changed = bad.Count > 0; changed;)
        {
            changed = false;
            foreach (var s in placed.Where(s => s.Origin is not null && s.From is { Origin: null }))
            {
                s.Origin = null;
                s.From = null;
                changed = true;
            }
        }
    }

    bool Step(bool forward)
    {
        bool changed = false;
        var groups = forward
            ? _edges.Where(e => e.B.Composite && e.B.Origin is null && e.T.Origin is not null)
                .GroupBy(e => e.B, e => (At: (e.T.Origin!.Value.X + e.Ox, e.T.Origin!.Value.Y + e.Oy), From: e.T))
            : _edges.Where(e => e.Capture && e.B.Composite && e.T.Composite && e.T.Origin is null && e.B.Origin is not null)
                .GroupBy(e => e.T, e => (At: (e.B.Origin!.Value.X - e.Ox, e.B.Origin!.Value.Y - e.Oy), From: e.B));
        foreach (var g in groups.ToList())
        {
            var at = g.Select(x => x.At).Distinct().ToList();
            if (at.Count != 1) continue;
            g.Key.Origin = at[0];
            g.Key.From = g.First().From;
            changed = true;
        }
        return changed;
    }
}

/// <summary>Reader for the openswr draw log (godot/DrawLog.cs): positions are screen coordinates already.</summary>
public static class OpenSWRLogReader
{
    /// <param name="alias">openswr res id -> original res id (e.g. the other side's skin of the same frame).</param>
    public static List<Item> Read(IEnumerable<string> lines, IReadOnlyDictionary<string, string>? alias = null)
    {
        var items = new List<Item>();
        long? t0 = null;
        (int X, int Y) cur = (0, 0);
        foreach (var e in Strings.Records(lines))
        {
            long t = e.TryGetProperty("t", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt64() : 0;
            t0 ??= t;
            t -= t0.Value;
            string node = Strings.Str(e, "node") ?? "";
            switch (Strings.Str(e, "ev"))
            {
                case "bm_blit":
                case "bm_kblit":
                {
                    // art not from the install: its origin tag is the match key, never present in the original
                    string? res = Strings.Str(e, "res") ?? (Strings.Str(e, "src") is { } tag ? "src:" + tag : null);
                    if (res is not null && alias is not null && alias.TryGetValue(res, out var a)) res = a;
                    items.Add(new Item
                    {
                        Kind = "bitmap", Res = res, X = Strings.Num(e, "dx"), Y = Strings.Num(e, "dy"), W = Strings.Num(e, "dw"), H = Strings.Num(e, "dh"),
                        Src = $"{Strings.Num(e, "sx")},{Strings.Num(e, "sy")} {Strings.Num(e, "w")}x{Strings.Num(e, "h")}",
                        Key = e.TryGetProperty("key", out _) ? Strings.Num(e, "key") : null, Where = node, T = t,
                    });
                    break;
                }
                case "drawtext":
                {
                    var r = Strings.Ints(e, "r") ?? [0, 0, 0, 0];
                    var (hash, text) = Strings.TextOf(e);
                    int font = Strings.Num(e, "font");
                    items.Add(new Item
                    {
                        Kind = "text", TextHash = hash, Text = text, X = r[0], Y = r[1], W = r[2] - r[0], H = r[3] - r[1],
                        Fmt = int.Parse(Strings.Str(e, "fmt")![2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        Font = font switch
                        {
                            -1 => "?/?/godot", // Godot's own font (Label, Button, LineEdit, RichTextLabel, DrawString)
                            TextLayout.SystemFont => "16/700/System",
                            >= 0 and < TextLayout.SystemFont => $"{TextLayout.Height(font)}/{TextLayout.Weight(font)}/Arial",
                            _ => $"?/?/font{font}",
                        },
                        Colour = Strings.Str(e, "c"), Where = node, T = t,
                    });
                    break;
                }
                case "rectangle":
                {
                    var r = Strings.Ints(e, "r") ?? [0, 0, 0, 0];
                    string brush = Strings.Str(e, "brush") ?? "none";
                    if (Strings.Dbl(e, "a") is { } alpha) brush += string.Create(CultureInfo.InvariantCulture, $"@{alpha:0.##}");
                    items.Add(new Item
                    {
                        Kind = "fill", X = r[0], Y = r[1], W = r[2] - r[0], H = r[3] - r[1], Colour = brush, Pen = Strings.Str(e, "pen") ?? "none",
                        Where = node, T = t,
                    });
                    break;
                }
                case "ellipse": // openswr only (galaxy map circles and arcs)
                {
                    var r = Strings.Ints(e, "r") ?? [0, 0, 0, 0];
                    items.Add(new Item
                    {
                        Kind = "ellipse", X = r[0], Y = r[1], W = r[2] - r[0], H = r[3] - r[1], Colour = Strings.Str(e, "brush") ?? "none",
                        Pen = Strings.Str(e, "pen") ?? "none", Where = node, T = t,
                    });
                    break;
                }
                case "moveto": cur = (Strings.Num(e, "x"), Strings.Num(e, "y")); break;
                case "lineto":
                {
                    int x = Strings.Num(e, "x"), y = Strings.Num(e, "y");
                    items.Add(new Item
                    {
                        Kind = "line", X = Math.Min(x, cur.X), Y = Math.Min(y, cur.Y), W = Math.Abs(x - cur.X), H = Math.Abs(y - cur.Y),
                        Colour = Strings.Str(e, "pen"), Where = node, T = t,
                    });
                    cur = (x, y);
                    break;
                }
            }
        }
        return items;
    }
}

public sealed record Finding(string Class, string Group, Item? Original, Item? OpenSWR, int OriginalCount, int OpenSWRCount, string Detail);

public sealed class Report
{
    public required List<Finding> Findings { get; init; }
    public int Matched { get; init; }
    public int OriginalDraws { get; init; }
    public int OriginalUnique { get; init; }
    /// <summary>Original draws with a resolved screen position.</summary>
    public int OriginalPlaced { get; init; }
    public int OpenSWRDraws { get; init; }
    public int OpenSWRUnique { get; init; }

    public int Count(string cls) => Findings.Count(f => f.Class == cls);

    public string Summary =>
        $"matched {Matched}, missing {Count("missing")}, extra {Count("extra")}, moved {Count("moved")}, different {Count("different")}";

    public string Text(string originalName = "original", string openSWRName = "openswr")
    {
        var s = new StringBuilder();
        s.AppendLine($"drawdiff: {originalName} vs {openSWRName}");
        s.AppendLine($"original: {OriginalDraws} draws ({OriginalPlaced} placed), {OriginalUnique} unique; openswr: {OpenSWRDraws} draws, {OpenSWRUnique} unique");
        s.AppendLine(Summary);
        s.AppendLine("unique records are compared; counts (xN) are how often each was drawn. Original positions (?) did not resolve.");
        foreach (var g in Findings.GroupBy(f => f.Group).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            s.AppendLine().Append('[').Append(g.Key).Append(']');
            var text = g.Select(f => f.Original?.Text ?? f.OpenSWR?.Text).FirstOrDefault(x => x is not null);
            if (text is not null) s.Append(' ').Append(Strings.Quote(text));
            s.AppendLine();
            foreach (var f in g.OrderBy(f => ClassOrder(f.Class)))
            {
                s.Append("  ").Append(f.Class.PadRight(10)).Append(f.Detail);
                if (f.Original is { } o) s.Append($"  [original x{f.OriginalCount} in {o.Where}]");
                if (f.OpenSWR is { } r) s.Append($"  [openswr x{f.OpenSWRCount} in {r.Where}]");
                s.AppendLine();
            }
        }
        return s.ToString();
    }

    static int ClassOrder(string c) => c switch { "different" => 0, "moved" => 1, "missing" => 2, _ => 3 };
}

public static class Differ
{
    public static Report Compare(IReadOnlyList<Item> original, IReadOnlyList<Item> openSWR)
    {
        var a = Unique(original);
        var b = Unique(openSWR);
        var findings = new List<Finding>();
        int matched = 0;
        var bByGroup = b.GroupBy(x => x.Item.Group).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var ga in a.GroupBy(x => x.Item.Group))
        {
            var left = ga.ToList();
            var right = bByGroup.Remove(ga.Key, out var r) ? r : [];
            // exact records first
            foreach (var x in left.ToList())
            {
                int i = right.FindIndex(y => y.Item.Full == x.Item.Full);
                if (i < 0) continue;
                matched++;
                left.Remove(x);
                right.RemoveAt(i);
            }
            // then the closest pairs: same size first, then fewest differing attributes, then the shortest distance
            var pairs = (from x in left from y in right
                         let d = Differences(x.Item, y.Item)
                         orderby d.Any(n => n.StartsWith("size", StringComparison.Ordinal)) ? 1 : 0, d.Count, Distance(x.Item, y.Item)
                         select (x, y, d)).ToList();
            var usedA = new HashSet<Item>();
            var usedB = new HashSet<Item>();
            foreach (var (x, y, d) in pairs)
            {
                if (usedA.Contains(x.Item) || usedB.Contains(y.Item)) continue;
                usedA.Add(x.Item);
                usedB.Add(y.Item);
                bool placed = x.Item.X is not null && y.Item.X is not null;
                string delta = placed ? $" delta ({y.Item.X - x.Item.X},{y.Item.Y - x.Item.Y})" : "";
                if (d.Count == 0 && !placed) { matched++; continue; } // position unknown on the original side, all else equal
                if (d.Count == 0)
                    findings.Add(new Finding("moved", ga.Key, x.Item, y.Item, x.Count, y.Count, $"{x.Item.Position} -> {y.Item.Position}{delta}"));
                else
                    findings.Add(new Finding("different", ga.Key, x.Item, y.Item, x.Count, y.Count,
                        string.Join(", ", d) + $" at {x.Item.Position} -> {y.Item.Position}{delta}"));
            }
            foreach (var x in left.Where(x => !usedA.Contains(x.Item)))
                findings.Add(new Finding("missing", ga.Key, x.Item, null, x.Count, 0, x.Item.Describe()));
            foreach (var y in right.Where(y => !usedB.Contains(y.Item)))
                findings.Add(new Finding("extra", ga.Key, null, y.Item, 0, y.Count, y.Item.Describe() + Scaled(y.Item)));
        }
        foreach (var y in bByGroup.Values.SelectMany(v => v))
            findings.Add(new Finding("extra", y.Item.Group, null, y.Item, 0, y.Count, y.Item.Describe() + Scaled(y.Item)));
        return new Report
        {
            Findings = findings, Matched = matched, OriginalDraws = original.Count, OriginalPlaced = original.Count(i => i.X is not null), OriginalUnique = a.Count, OpenSWRDraws = openSWR.Count, OpenSWRUnique = b.Count,
        };
    }

    /// <summary>The original never scales a bitmap: an openswr draw whose size is not its source size is flagged even without a match.</summary>
    static string Scaled(Item y) =>
        y.Kind == "bitmap" && y.Src is { } src && !src.EndsWith($" {y.W}x{y.H}", StringComparison.Ordinal) ? $" (scaled: drawn {y.W}x{y.H} from {src})" : "";

    static List<(Item Item, int Count)> Unique(IReadOnlyList<Item> items) =>
        items.GroupBy(i => i.Full).Select(g => (g.First(), g.Count())).ToList();

    static List<string> Differences(Item a, Item b) =>
        a.Attributes().Zip(b.Attributes()).Where(p => p.First.Value != p.Second.Value)
            .Select(p => $"{p.First.Name} {p.First.Value ?? "-"} -> {p.Second.Value ?? "-"}").ToList();

    static int Distance(Item a, Item b) =>
        a.X is { } ax && b.X is { } bx ? Math.Abs(ax - bx) + Math.Abs(a.Y!.Value - b.Y!.Value) : 0;
}

/// <summary>
/// Test vectors: the records the diff reads, with every string "s" replaced by its hash "sh" and return addresses dropped; no
/// palettes, no pixel data. Works on both logs.
/// </summary>
public static class Vector
{
    static readonly HashSet<string> Kept =
    [
        "bm_new", "bm_free", "bm_blit", "bm_kblit", "bm_fill", "bm_draw", "bitblt", "bitmap", "dc", "paint", "select", "font", "brush", "pen",
        "textcolor", "drawtext", "rectangle", "moveto", "lineto", "ellipse",
    ];

    /// <summary>
    /// With a window: the state alive at its start comes first, reduced (live bitmap objects, the DCs used and their last
    /// selections, text colours, and each distinct placement draw between live objects once, after that state), then every kept
    /// record of the window. Draws before the window that show something are dropped.
    /// </summary>
    public static IEnumerable<string> Make(IEnumerable<string> lines, (long From, long To)? window = null)
    {
        var recs = Strings.Records(lines).Where(e => Strings.Str(e, "ev") is { } ev && Kept.Contains(ev)).ToList();
        if (recs.Count == 0) yield break;
        long t0 = T(recs[0]);
        bool inside(JsonElement e) => window is not { } w || (T(e) - t0 >= w.From && T(e) - t0 <= w.To);
        int start = recs.FindIndex(inside), end = recs.FindLastIndex(inside);
        if (start < 0) yield break;
        foreach (int i in Before(recs, start)) yield return Write(recs[i]);
        for (int i = start; i <= end; i++) yield return Write(recs[i]);
    }

    static long T(JsonElement e) => e.TryGetProperty("t", out var tp) && tp.ValueKind == JsonValueKind.Number ? tp.GetInt64() : 0;

    /// <summary>Indices of the records before <paramref name="start"/> the window needs: state first, then placement draws.</summary>
    static List<int> Before(List<JsonElement> recs, int start)
    {
        var bmNew = new Dictionary<string, int>();     // live object tag -> its bm_new
        var composite = new HashSet<string>();         // live tags that are canvases / sections not from a resource
        var sectionOf = new Dictionary<int, int>();    // bm_new of a section -> its CreateDIBSection bitmap record
        var ownerOf = new Dictionary<int, int>();      // that bitmap record -> the bm_new
        var def = new Dictionary<string, int>();       // GDI object -> the record defining it now
        var dcDef = new Dictionary<string, int>();
        var selects = new Dictionary<(string Dc, string Cat), (int Select, int? Def)>();
        var textColour = new Dictionary<string, int>();
        // placement draws: B placed by / placing T (TraceReader edges), with the objects and DCs they need
        var edges = new Dictionary<string, (int Index, string B, string T, bool Capture, string[] Bms, string[] Dcs)>();
        var selected = new Dictionary<string, int>(); // DC -> the bitmap record selected in it
        int? lastSection = null;
        // a DC target: the bitmap selected in it, else its kind (paint DCs of the many repaints collapse)
        string Target(string? dc) => dc is null ? "?" : selected.TryGetValue(dc, out int b) ? $"b{b}"
            : dcDef.TryGetValue(dc, out int d) ? Strings.Str(recs[d], "ev") + "/" + Strings.Str(recs[d], "kind") : "?";
        // the graph node of a DC target: the section object selected in it, else the DC itself
        string Node(string? dc) => dc is not null && selected.TryGetValue(dc, out int b) && ownerOf.TryGetValue(b, out int o)
            ? Strings.Str(recs[o], "bm")! : "dc:" + dc;
        for (int i = 0; i < start; i++)
        {
            var e = recs[i];
            string ev = Strings.Str(e, "ev")!;
            string? S(string n) => Strings.Str(e, n);
            switch (ev)
            {
                case "bm_new":
                    bmNew[S("bm")!] = i;
                    if (S("res") is null && S("kind") is "canvas" or "section") composite.Add(S("bm")!); else composite.Remove(S("bm")!);
                    if (S("kind") == "section" && lastSection is { } ls) { sectionOf[i] = ls; ownerOf[ls] = i; lastSection = null; }
                    break;
                case "bm_free": bmNew.Remove(S("bm")!); composite.Remove(S("bm")!); break;
                case "bitmap":
                    def[S("obj")!] = i;
                    if (S("kind") == "section") lastSection = i;
                    break;
                case "font": def[S("font")!] = i; break;
                case "brush" or "pen": def[S("obj")!] = i; break;
                case "dc" or "paint": dcDef[S("dc")!] = i; break;
                case "select":
                    if (Category(S("obj") ?? "") is { } cat) selects[(S("dc")!, cat)] = (i, def.TryGetValue(S("obj")!, out int d) ? d : null);
                    if (Category(S("obj") ?? "") == "bmp") selected[S("dc")!] = def.TryGetValue(S("obj")!, out int b) ? b : -1;
                    break;
                case "textcolor": textColour[S("dc")!] = i; break;
                case "bm_blit" or "bm_kblit" when composite.Contains(S("src") ?? ""):
                {
                    int ox = Strings.Num(e, "dx") - Strings.Num(e, "sx"), oy = Strings.Num(e, "dy") - Strings.Num(e, "sy");
                    edges[$"{ev}|{S("src")}|{bmNew[S("src")!]}|{S("dst")}|{(S("dst") is { } dt && bmNew.TryGetValue(dt, out int n) ? n : -1)}|{ox}|{oy}"] =
                        (i, S("src")!, S("dst") ?? "", ev == "bm_blit", [S("src")!, S("dst") ?? ""], []);
                    break;
                }
                case "bm_draw" when composite.Contains(S("bm") ?? ""):
                {
                    int ox = Strings.Num(e, "x") - Strings.Num(e, "sx"), oy = Strings.Num(e, "y") - Strings.Num(e, "sy");
                    edges[$"{ev}|{S("bm")}|{bmNew[S("bm")!]}|{Target(S("dc"))}|{ox}|{oy}"] = (i, S("bm")!, Node(S("dc")), false, [S("bm")!], [S("dc") ?? ""]);
                    break;
                }
                // only a section object's own DC places anything (TraceReader); one draw per distinct source, target kind and offset
                case "bitblt" when S("src") is { } src && selected.TryGetValue(src, out int sb) && ownerOf.ContainsKey(sb):
                {
                    int ox = Strings.Num(e, "x") - Strings.Num(e, "sx"), oy = Strings.Num(e, "y") - Strings.Num(e, "sy");
                    bool inPlace = Strings.Num(e, "x") == Strings.Num(e, "sx") && Strings.Num(e, "y") == Strings.Num(e, "sy");
                    edges[$"{ev}|{sb}|{Target(S("dst"))}|{ox}|{oy}|{inPlace}"] = (i, Strings.Str(recs[ownerOf[sb]], "bm")!, Node(S("dst")), false, [], [S("src")!, S("dst") ?? ""]);
                    break;
                }
            }
        }

        // what the window refers to
        var bms = new HashSet<string>();
        var dcs = new HashSet<string>();
        for (int i = start; i < recs.Count; i++)
            foreach (var n in new[] { "bm", "src", "dst", "dc" })
                if (Strings.Str(recs[i], n) is { } v) { if (v.StartsWith("dc", StringComparison.Ordinal)) dcs.Add(v); else bms.Add(v); }
        // the placement draws those objects depend on: their draws into targets (and the targets' draws, onwards), and the captures
        // filling them from placed objects (and those objects' placement); the objects must still be the same ones at the window start
        var valid = edges.Values.Where(x => !x.Bms.Any(b => b != "" && !(bmNew.TryGetValue(b, out int n) && n < x.Index)) && x.Dcs.All(dcDef.ContainsKey)).ToList();
        var queue = new Queue<string>(bms.Concat(dcs.Select(Node)));
        var seen = new HashSet<string>(queue);
        var placement = new List<int>();
        while (queue.Count > 0)
        {
            string x = queue.Dequeue();
            foreach (var e in valid)
            {
                string? next = e.B == x ? e.T : e.Capture && e.T == x ? e.B : null;
                if (next is null) continue;
                placement.Add(e.Index);
                bms.UnionWith(e.Bms.Where(b => b != ""));
                dcs.UnionWith(e.Dcs);
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }
        placement = placement.Distinct().ToList();
        var keep = new HashSet<int>();
        foreach (var d in dcs)
        {
            if (dcDef.TryGetValue(d, out int i)) keep.Add(i);
            if (textColour.TryGetValue(d, out int c)) keep.Add(c);
            foreach (var ((dc, _), (sel, defIndex)) in selects)
            {
                if (dc != d) continue;
                keep.Add(sel);
                if (defIndex is not { } di) continue;
                keep.Add(di);
                if (ownerOf.TryGetValue(di, out int owner)) bms.Add(Strings.Str(recs[owner], "bm")!);
            }
        }
        foreach (var b in bms)
        {
            if (!bmNew.TryGetValue(b, out int i)) continue;
            keep.Add(i);
            if (sectionOf.TryGetValue(i, out int sec)) keep.Add(sec);
        }
        // state in trace order, then the placement draws (they see the DC state of the window start)
        var result = keep.Order().ToList();
        result.AddRange(placement.Order());
        return result;
    }

    /// <summary>Kind of a selected GDI object: font, brush, pen, bmp; null for raw handles.</summary>
    static string? Category(string obj) =>
        obj.StartsWith("font", StringComparison.Ordinal) ? "font" :
        obj.StartsWith("brush", StringComparison.Ordinal) ? "brush" :
        obj.StartsWith("pen", StringComparison.Ordinal) ? "pen" :
        obj.StartsWith("bmp", StringComparison.Ordinal) ? "bmp" :
        obj.StartsWith("stock", StringComparison.Ordinal) && int.TryParse(obj[5..], out int n)
            ? n switch { <= 5 => "brush", <= 8 => "pen", >= 10 and <= 17 => "font", _ => null } : null;

    static string Write(JsonElement e)
    {
        using var ms = new MemoryStream();
        using (var j = new Utf8JsonWriter(ms))
        {
            j.WriteStartObject();
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name is "ra" or "seq" or "rop" or "mode") continue; // not read by the diff
                if (p.Name == "s" && p.Value.ValueKind == JsonValueKind.String) { j.WriteString("sh", Strings.Hash(p.Value.GetString()!)); continue; }
                p.WriteTo(j);
            }
            j.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
    /// <summary>"a..b" (ms from the first record).</summary>
    public static (long, long) ParseWindow(string s)
    {
        var parts = s.Split("..");
        if (parts.Length != 2) throw new FormatException($"window '{s}': expected t0..t1 (ms)");
        return (long.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}
