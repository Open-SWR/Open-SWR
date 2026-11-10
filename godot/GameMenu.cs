using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data;

/// <summary>
/// Shared 2-px window frame: STRATEGY bitmaps 10100-10107, grey outer and white inner line. Piece
/// placement read from the pixels: corners 10101 top-left, 10100 top-right, 10103 bottom-left, 10102 bottom-right; edges 10105 top,
/// 10106 bottom, 10107 left, 10104 right. Interior fill black (Inferred; observed in the original).
/// </summary>
public static class UiFrame
{
    public static void Draw(CanvasItem c, Rect2 r, bool fill = true)
    {
        if (fill) { c.DrawRect(r, Colors.Black); DrawLog.Fill(c, r, Colors.Black); }
        Vector2 p = r.Position, e = r.End;
        void Piece(ushort id, Rect2 at) { var t = Assets.Strategy(id); c.DrawTextureRect(t, at, false); DrawLog.Bitmap(c, t, at); }
        Piece(StrategyBitmaps.WindowFrameTop, new Rect2(p.X + 2, p.Y, r.Size.X - 4, 2));
        Piece(StrategyBitmaps.WindowFrameBottom, new Rect2(p.X + 2, e.Y - 2, r.Size.X - 4, 2));
        Piece(StrategyBitmaps.WindowFrameLeft, new Rect2(p.X, p.Y + 2, 2, r.Size.Y - 4));
        Piece(StrategyBitmaps.WindowFrameRight, new Rect2(e.X - 2, p.Y + 2, 2, r.Size.Y - 4));
        Piece(StrategyBitmaps.WindowFrameTopLeft, new Rect2(p, new Vector2(2, 2)));
        Piece(StrategyBitmaps.WindowFrameTopRight, new Rect2(e.X - 2, p.Y, 2, 2));
        Piece(StrategyBitmaps.WindowFrameBottomLeft, new Rect2(p.X, e.Y - 2, 2, 2));
        Piece(StrategyBitmaps.WindowFrameBottomRight, new Rect2(e - new Vector2(2, 2), new Vector2(2, 2)));
    }

    /// <summary>Text colours (PALETTERGB): side colour (side 1 red, side 2 green), highlight white, disabled grey.</summary>
    public static Color SideColour(bool sideOne) => sideOne ? new Color(1, 0, 0) : new Color(0, 1, 0);
    public static readonly Color Disabled = Color.Color8(128, 128, 128);

    /// <summary>Text paragraph (= panel line) under the mouse, or -1 (wrapped lines stay in their paragraph).</summary>
    public static int Paragraph(RichTextLabel text)
    {
        var p = text.GetLocalMousePosition();
        if (!new Rect2(Vector2.Zero, text.Size).HasPoint(p)) return -1;
        for (int i = text.GetParagraphCount() - 1; i >= 0; i--)
            if (text.GetParagraphOffset(i) <= p.Y) return i;
        return -1;
    }

    /// <summary>
    /// A TEXTSTRA string as the tooltip control shows it ("&" stripped); also for Godot labels, which do not run the DrawText
    /// emulation. Text drawn through <see cref="Assets.DrawText"/> takes the raw string (prefixes processed there).
    /// </summary>
    public static string Label(int id) => OpenSWR.Data.TextLayout.Tooltip(Assets.Stra(id));
}

/// <summary>
/// Popup list / game menu window (the filter picker's popup list
/// shares the behaviour): hover highlights, 500 ms hover on a group row opens its submenu (placed left of
/// the parent, right when that would be negative; top at the row top - 3 unless it would leave the owner, else bottom at the
/// row bottom + 3), left release on an enabled row without a submenu runs the command and closes (a group row sends nothing), a
/// click outside closes, Enter = click on the highlighted row, Esc closes the submenu / the menu. Text in the side colour,
/// highlighted white, disabled grey; a disabled row shows DisabledImage, an enabled one Image (HotImage when highlighted,
/// Inferred), a checked row the CheckImage. Built as a full-screen overlay so outside clicks close it and never reach the map.
/// </summary>
public partial class GameMenu : Control
{
    /// <summary>A row: command id (filter picker: the mode), text, images, state, submenu rows.</summary>
    public sealed record Entry(int Id, string Text, ushort Image, ushort HotImage, ushort DisabledImage, ushort CheckImage, bool Enabled, bool Checked, List<Entry> Children)
    {
        public static Entry From(MenuRow r) => new(r.Item.Id, Assets.Stra(r.Item.Label), r.Item.Image, r.Item.HotImage, r.Item.DisabledImage,
            r.Item.CheckImage, r.Enabled, r.Checked, [.. r.Children.Select(From)]);
    }

    /// <summary>A laid-out list: size, and per row its rect (relative), icon x and text x.</summary>
    public sealed class Level(List<Entry> rows, Vector2 size, List<(Rect2 Row, float IconX, float TextX)> geometry)
    {
        public readonly List<Entry> Rows = rows;
        public readonly List<(Rect2 Row, float IconX, float TextX)> Geometry = geometry;
        public Rect2 Rect = new(Vector2.Zero, size);
        public int Hot = -1;
        public double HoverTime;
        public Rect2 RowRect(int i) => new(Rect.Position + Geometry[i].Row.Position, Geometry[i].Row.Size);
    }

    readonly List<Level> _levels = [];
    readonly bool _sideOne;
    readonly Action<int> _command;
    readonly Func<List<Entry>, Level> _layout;
    readonly (ushort Normal, ushort Hot)? _arrow;
    readonly bool _clickOpens;
    public Action? Closed { get; set; }

    /// <summary>
    /// Game menu from built RCDATA rows at the click point (flipped up / left when it would leave the
    /// owner). <paramref name="clickOpens"/>: a group row opens its submenu on a click instead of the hover timer (the filter
    /// picker, observed in the original). <paramref name="layout"/> = null: the game menu layout (<see cref="MenuLayout"/>); <paramref name="arrow"/> =
    /// submenu arrow bitmaps (none: a small triangle, own drawing).
    /// </summary>
    public GameMenu(List<Entry> rows, Vector2 at, bool sideOne, Action<int> command, Func<List<Entry>, Level>? layout = null, (ushort, ushort)? arrow = null,
        bool clickOpens = false)
    {
        (_sideOne, _command, _layout, _arrow, _clickOpens) = (sideOne, command, layout ?? MenuLayout, arrow, clickOpens);
        Size = new Vector2(640, 480);
        MouseFilter = MouseFilterEnum.Stop;
        var l = _layout(rows);
        var size = l.Rect.Size;
        if (at.Y + size.Y > Size.Y) at.Y -= size.Y;
        if (at.X + size.X > Size.X) at.X -= size.X;
        l.Rect.Position = at;
        _levels.Add(l);
        // every level over the darkened main-screen picture
        _dark = new Darkened { Rects = () => _levels.Select(x => x.Rect) };
        AddChild(_dark);
    }

    readonly Darkened _dark;

    public GameMenu(List<MenuRow> rows, Vector2 at, bool sideOne, Action<int> command) : this([.. rows.Select(Entry.From)], at, sideOne, command) { }

    static Vector2 Img(ushort id) => id == 0 ? Vector2.Zero : (Vector2)Assets.BitmapSize(id, "STRATEGY.DLL");

    /// <summary>
    /// Game menu layout: icon column w0 = the largest of row image / check image of a checked row / 20 for a submenu;
    /// row width w0 + 12 + text width, menu width the largest; one row height for all = max(image height + 4, text height + 4);
    /// rows from y 2, text at x w0 + 6, vertically centred; height = sum rows + 2. Font: the stock System font.
    /// </summary>
    /// <summary>Row font of every popup menu and the filter picker: no font is set, so the menu DC's default, System (Inferred).</summary>
    public const int RowFont = OpenSWR.Data.TextLayout.SystemFont;

    public static Level MenuLayout(List<Entry> rows)
    {
        float w0 = 0, imageH = 0, width = 0;
        foreach (var r in rows)
        {
            w0 = Mathf.Max(w0, Img(r.Image).X);
            if (r.Checked) w0 = Mathf.Max(w0, Img(r.CheckImage).X);
            if (r.Children.Count > 0) w0 = Mathf.Max(w0, 20);
            imageH = Mathf.Max(imageH, Img(r.Image).Y);
        }
        foreach (var r in rows) width = Mathf.Max(width, w0 + 12 + OpenSWR.Data.TextLayout.DrawnWidth(RowFont, r.Text));
        width = Mathf.Ceil(width);
        float h = Mathf.Max(imageH + 4, Assets.LineHeight(RowFont) + 4);
        var geometry = rows.Select((_, i) => (new Rect2(0, 2 + i * h, width, h), 3f, w0 + 6)).ToList();
        return new Level(rows, new Vector2(width, h * rows.Count + 2), geometry);
    }

    void OpenSub(int i)
    {
        var parent = _levels[^1];
        var sub = _layout(parent.Rows[i].Children);
        var row = parent.RowRect(i);
        float x = parent.Rect.Position.X - sub.Rect.Size.X + 3;
        if (x < 0) x = parent.Rect.End.X - 3;
        float y = row.Position.Y - 3;
        if (y + sub.Rect.Size.Y > Size.Y) y = row.End.Y - sub.Rect.Size.Y + 3;
        sub.Rect.Position = new Vector2(x, y);
        _levels.Add(sub);
        QueueRedraw();
    }

    /// <summary>Highlights row <paramref name="row"/> of the top level, optionally opening its submenu at once (headless demos).</summary>
    public void Demo(int row, bool openSub)
    {
        _levels[^1].Hot = row;
        if (openSub && _levels[^1].Rows[row].Children.Count > 0) OpenSub(row);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _dark.QueueRedraw(); // follows the open levels
        var top = _levels[^1];
        if (_clickOpens || top.Hot < 0 || top.Rows[top.Hot].Children.Count == 0) return;
        top.HoverTime += delta;
        if (top.HoverTime >= 0.5) OpenSub(top.Hot);
    }

    (int Level, int Row) Hit(Vector2 p)
    {
        for (int k = _levels.Count - 1; k >= 0; k--)
        {
            var l = _levels[k];
            if (!l.Rect.HasPoint(p)) continue;
            for (int i = 0; i < l.Rows.Count; i++) if (l.RowRect(i).HasPoint(p)) return (k, i);
            return (k, -1);
        }
        return (-1, -1);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseMotion m:
                var (k, i) = Hit(m.Position);
                if (k < 0) break;
                while (_levels.Count - 1 > k && (i < 0 || _levels[k].Hot != i)) _levels.RemoveAt(_levels.Count - 1); // leaving a group row closes its submenu
                if (_levels[k].Hot != i) (_levels[k].Hot, _levels[k].HoverTime) = (i, 0);
                QueueRedraw();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right, Pressed: false } b:
                var (level, row) = Hit(b.Position);
                if (level < 0)
                {
                    ClickedAway = true;
                    Close();
                }
                else if (row >= 0) Choose(level, row);
                break;
        }
        AcceptEvent();
    }

    /// <summary>Keys while open: Enter = click on the highlighted row, Esc closes the submenu / the menu; Up / Down / Right (own additions).</summary>
    public void HandleKey(Key key)
    {
        var top = _levels[^1];
        switch (key)
        {
            case Key.Escape when _levels.Count > 1: _levels.RemoveAt(_levels.Count - 1); break;
            case Key.Escape: Close(); return;
            case Key.Up: top.Hot = (top.Hot + top.Rows.Count - 1) % top.Rows.Count; break;
            case Key.Down: top.Hot = (top.Hot + 1) % top.Rows.Count; break;
            case Key.Right or Key.Left when top.Hot >= 0 && top.Rows[top.Hot].Children.Count > 0: OpenSub(top.Hot); break;
            case Key.Enter or Key.KpEnter when top.Hot >= 0: Choose(_levels.Count - 1, top.Hot); return;
        }
        QueueRedraw();
    }

    void Choose(int level, int index)
    {
        var row = _levels[level].Rows[index];
        if (row.Children.Count > 0 && _clickOpens && row.Enabled)
        {
            while (_levels.Count - 1 > level) _levels.RemoveAt(_levels.Count - 1);
            _levels[level].Hot = index;
            OpenSub(index);
            return;
        }
        if (!row.Enabled || row.Children.Count > 0) return;
        Close();
        _command(row.Id);
    }

    /// <summary>Closed by a button release outside the menu (the filter picker's map click).</summary>
    public bool ClickedAway { get; private set; }

    public void Close()
    {
        QueueFree();
        var closed = Closed;
        Closed = null;
        closed?.Invoke();
    }

    public override void _Draw()
    {
        var colour = UiFrame.SideColour(_sideOne);
        foreach (var l in _levels)
        {
            UiFrame.Draw(this, l.Rect, fill: false);
            for (int i = 0; i < l.Rows.Count; i++)
            {
                var r = l.Rows[i];
                var rect = l.RowRect(i);
                var (_, iconX, textX) = l.Geometry[i];
                bool hot = i == l.Hot;
                ushort image = r.Checked && r.CheckImage != 0 ? r.CheckImage
                    : !r.Enabled ? (r.DisabledImage != 0 ? r.DisabledImage : r.Image)
                    : hot && r.HotImage != 0 ? r.HotImage : r.Image;
                if (image != 0)
                {
                    var size = Img(image);
                    DrawTextureRect(Assets.Strategy(image), new Rect2(rect.Position + new Vector2(iconX, (rect.Size.Y - size.Y) / 2), size), false);
                }
                var text = !r.Enabled ? UiFrame.Disabled : hot ? Colors.White : colour;
                Assets.DrawText(this, new Rect2(rect.Position.X + textX, rect.Position.Y, rect.Size.X - textX, rect.Size.Y), r.Text, RowFont, text, TextFormat.Row);
                if (r.Children.Count == 0) continue;
                if (_arrow is { } arrow)
                {
                    ushort a = hot ? arrow.Hot : arrow.Normal;
                    var size = Img(a);
                    DrawTextureRect(Assets.Strategy(a), new Rect2(rect.Position + new Vector2(6, (rect.Size.Y - size.Y) / 2), size), false);
                }
                else
                {
                    var c = rect.Position + new Vector2(textX / 2, rect.Size.Y / 2);
                    DrawColoredPolygon([c + new Vector2(3, -4), c + new Vector2(-3, 0), c + new Vector2(3, 4)], text); // points left: submenus open to the left
                }
            }
        }
    }
}
