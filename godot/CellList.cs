using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data;

/// <summary>
/// Vertical list scroll bar from an RCDATA skin, shared by the battle windows and the object
/// windows: the bar inside the list's right edge, Thickness wide; parts at their original size centred across it, up button at
/// the top, down button at the bottom (pressed image while held). Inferred: track tiled between the buttons; thumb (start cap,
/// stretched middle, end cap), length and position proportional to the visible part. openswr: the whole bar only when the rows
/// do not fit (player report); the list keeps the bar's column empty.
/// </summary>
public static class ScrollBar
{
    static Vector2 Dim(ushort id) => Assets.BitmapSize(id, "STRATEGY.DLL");

    static void Part(CanvasItem c, ushort id, float cx, float y, float h)
    {
        if (Assets.TryBitmap(id, "STRATEGY.DLL") is not { } t) return;
        var at = new Rect2(cx - Dim(id).X / 2, y, Dim(id).X, h);
        c.DrawTextureRect(t, at, false);
        DrawLog.Bitmap(c, t, at);
    }

    /// <summary>Draws the bar; <paramref name="pressed"/> -1 up / +1 down button held.</summary>
    public static void Draw(CanvasItem c, ScrollSkin k, Rect2 list, int first, int visible, int total, int pressed)
    {
        if (total <= visible) return;
        float cx = list.End.X - k.Thickness / 2f;
        var (top, bottom) = Track(k, list);
        Part(c, pressed < 0 ? k.UpPressed : k.Up, cx, list.Position.Y, Dim(k.Up).Y);
        Part(c, pressed > 0 ? k.DownPressed : k.Down, cx, bottom, Dim(k.Down).Y);
        float th = Dim(k.Track).Y;
        var t = Assets.Strategy(k.Track);
        for (float y = top; y < bottom; y += th)
        {
            float part = Math.Min(th, bottom - y);
            var at = new Rect2(cx - Dim(k.Track).X / 2, y, Dim(k.Track).X, part);
            c.DrawTextureRectRegion(t, at, new Rect2(0, 0, t.GetWidth(), t.GetHeight() * part / th));
            DrawLog.Bitmap(c, t, at, new Rect2(0, 0, Dim(k.Track).X, part));
        }
        var (y0, len) = Thumb(k, list, first, visible, total);
        float caps = Dim(k.ThumbStart).Y + Dim(k.ThumbEnd).Y;
        Part(c, k.ThumbStart, cx, y0, Dim(k.ThumbStart).Y);
        Part(c, k.ThumbMiddle, cx, y0 + Dim(k.ThumbStart).Y, len - caps);
        Part(c, k.ThumbEnd, cx, y0 + len - Dim(k.ThumbEnd).Y, Dim(k.ThumbEnd).Y);
    }

    // track between the buttons
    static (float Top, float Bottom) Track(ScrollSkin k, Rect2 list) => (list.Position.Y + Dim(k.Up).Y, list.End.Y - Dim(k.Down).Y);

    /// <summary>Thumb top and length (rows overflow: <paramref name="total"/> &gt; <paramref name="visible"/>); drawing and hit tests share it.</summary>
    public static (float Y, float Length) Thumb(ScrollSkin k, Rect2 list, int first, int visible, int total)
    {
        var (top, bottom) = Track(k, list);
        return OpenSWR.Data.UiMath.Thumb(top, bottom, MinThumb(k), first, visible, total);
    }

    static float MinThumb(ScrollSkin k) => Dim(k.ThumbStart).Y + Dim(k.ThumbEnd).Y + Dim(k.ThumbMiddle).Y;

    /// <summary>First row for a thumb dragged to top <paramref name="y"/> (inverse of <see cref="Thumb"/>, unclamped).</summary>
    public static int FirstAt(ScrollSkin k, Rect2 list, float y, int visible, int total)
    {
        var (top, bottom) = Track(k, list);
        return OpenSWR.Data.UiMath.FirstAt(top, bottom, MinThumb(k), y, visible, total);
    }

    /// <summary>Point inside the bar's column.</summary>
    public static bool InBar(ScrollSkin k, Rect2 list, Vector2 p) => p.X >= list.End.X - k.Thickness && p.X < list.End.X && p.Y >= list.Position.Y && p.Y < list.End.Y;

    /// <summary>Room the bar takes at the list's right edge: its parts are centred on the skin thickness and may be wider.</summary>
    public static float Width(ScrollSkin k) => new[] { k.Thickness, Dim(k.Track).X, Dim(k.Up).X, Dim(k.Down).X }.Max();

    /// <summary>-1 up button, +1 down button, 0 elsewhere.</summary>
    public static int Button(ScrollSkin k, Rect2 list, Vector2 p)
    {
        if (p.X < list.End.X - k.Thickness || p.X >= list.End.X) return 0;
        return p.Y >= list.Position.Y && p.Y < list.Position.Y + Dim(k.Up).Y ? -1 : p.Y >= list.End.Y - Dim(k.Down).Y && p.Y < list.End.Y ? 1 : 0;
    }
}

/// <summary>
/// Cell list ("CoolDragList" in the original): fixed-size cells in columns, scroll bar from a skin, click
/// selects (Ctrl toggles), double click / right click / drag start reported to the owner. Cells are drawn by the owner's
/// <see cref="DrawCell"/>. Single-selection lists ignore Ctrl (finders).
/// </summary>
public partial class CellList : Control
{
    public Vector2 Cell { get; set; }
    public bool Single { get; init; }
    public bool Draggable { get; init; }
    public Action<CellList, int, Rect2>? DrawCell { get; set; }
    public Action? Pressed { get; set; }           // any press (focus the window)
    public Action? SelectionChanged { get; set; }
    public Action<int>? Activated { get; set; }    // double click (notification 0x309)
    public Action<Vector2>? ContextMenu { get; set; }
    public Action<Vector2, int>? DragStarted { get; set; } // press point (global), cell
    readonly ScrollSkin? _skin;
    int _count, _first, _pressed;
    Vector2? _press;
    float? _grab; // thumb drag: press point below the thumb top
    public readonly SortedSet<int> Selected = [];

    public CellList(ushort skin)
    {
        _skin = skin == 0 ? null : Assets.Skin(skin);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            Selected.RemoveWhere(i => i >= value);
            QueueRedraw();
        }
    }

    float BarWidth => _skin is { } k ? ScrollBar.Width(k) : 0;
    // columns from the full list width: planet window 222 / 70 = 3 with the bar in the 12 px left over (Inferred rule)
    int Columns => Math.Max(1, (int)(Size.X / Cell.X));
    int VisibleRows => Math.Max(1, (int)(Size.Y / Cell.Y));
    int Rows => (_count + Columns - 1) / Columns;
    public int First { get => _first; set { _first = Math.Clamp(value, 0, Math.Max(0, Rows - VisibleRows)); QueueRedraw(); } }

    // a cell never runs under the scroll bar (the fleet window's 91-px list holds 91-px cells)
    Rect2 CellRect(int i) => new((i % Columns) * Cell.X, (i / Columns - _first) * Cell.Y, Math.Min(Cell.X, Size.X - BarWidth), Cell.Y);

    public int At(Vector2 p)
    {
        if (p.X >= Size.X - BarWidth || p.X < 0 || p.Y < 0) return -1;
        int col = (int)(p.X / Cell.X), row = (int)(p.Y / Cell.Y) + _first;
        int i = row * Columns + col;
        return col < Columns && i < _count ? i : -1;
    }

    /// <summary>Selects one cell alone and scrolls it into view.</summary>
    public void Select(int i)
    {
        Selected.Clear();
        if (i >= 0 && i < _count)
        {
            Selected.Add(i);
            int row = i / Columns;
            if (row < _first) First = row;
            else if (row >= _first + VisibleRows) First = row - VisibleRows + 1;
        }
        QueueRedraw();
        SelectionChanged?.Invoke();
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                First--;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                First++;
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } b:
            {
                Pressed?.Invoke();
                if (_skin is { } k && Rows > VisibleRows && ScrollBar.InBar(k, Bounds, b.Position))
                {
                    if (ScrollButton(k, b.Position) is var dir && dir != 0)
                    {
                        _pressed = dir;
                        First += dir;
                    }
                    else if (b.ButtonIndex == MouseButton.Left)
                    {
                        var (y0, len) = ScrollBar.Thumb(k, Bounds, _first, VisibleRows, Rows);
                        if (b.Position.Y < y0) First -= VisibleRows; // track above / below the thumb: a page
                        else if (b.Position.Y >= y0 + len) First += VisibleRows;
                        else _grab = b.Position.Y - y0;
                    }
                    break;
                }
                int i = At(b.Position);
                if (b.ButtonIndex == MouseButton.Left) _press = b.Position;
                if (i < 0) break;
                if (b.DoubleClick && b.ButtonIndex == MouseButton.Left)
                {
                    Activated?.Invoke(i);
                    _press = null;
                }
                else if (b.CtrlPressed && !Single && b.ButtonIndex == MouseButton.Left) { } // toggled on release
                else if (!Selected.Contains(i) || Single)
                {
                    Selected.Clear();
                    Selected.Add(i);
                    SelectionChanged?.Invoke();
                }
                QueueRedraw();
                break;
            }
            case InputEventMouseMotion m when _grab is { } grab && _skin is { } k && Input.IsMouseButtonPressed(MouseButton.Left):
                First = ScrollBar.FirstAt(k, Bounds, m.Position.Y - grab, VisibleRows, Rows);
                break;
            case InputEventMouseMotion m when _press is { } start && Draggable && Input.IsMouseButtonPressed(MouseButton.Left):
                if (At(start) is var cell && cell >= 0 && (Selected.Contains(cell) || m.CtrlPressed))
                {
                    if (Selected.Add(cell)) SelectionChanged?.Invoke();
                    DragStarted?.Invoke(start + GetGlobalRect().Position, cell);
                }
                _press = null;
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } up:
                _pressed = 0;
                _grab = null;
                if (_press is not null && up.CtrlPressed && !Single && At(up.Position) is var t && t >= 0)
                {
                    if (!Selected.Remove(t)) Selected.Add(t);
                    SelectionChanged?.Invoke();
                }
                _press = null;
                QueueRedraw();
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } r when At(r.Position) >= 0:
                ContextMenu?.Invoke(GetGlobalMousePosition());
                break;
            default:
                return;
        }
        AcceptEvent();
    }

    Rect2 Bounds => new(Vector2.Zero, Size);
    int ScrollButton(ScrollSkin k, Vector2 p) => ScrollBar.Button(k, Bounds, p);

    public override void _Draw()
    {
        for (int i = _first * Columns; i < _count && i < (_first + VisibleRows + 1) * Columns; i++)
            DrawCell?.Invoke(this, i, CellRect(i));
        if (_skin is { } k) ScrollBar.Draw(this, k, new Rect2(Vector2.Zero, Size), _first, VisibleRows, Rows, _pressed);
    }

    /// <summary>Draws a STRATEGY / GOKRES bitmap at its logical size centred in a rect (missing ids skipped).</summary>
    public static void Centred(CanvasItem c, Texture2D? t, Vector2 size, Rect2 r)
    {
        if (t is null) return;
        var at = new Rect2(r.Position + ((r.Size - size) / 2).Floor(), size);
        c.DrawTextureRect(t, at, false);
        DrawLog.Bitmap(c, t, at);
    }
}
