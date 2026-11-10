using System;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;

/// <summary>
/// Drop-down selection box ("CoolSelectionBoxClass"): shows the current item's cell (drawn by the
/// owner); <see cref="Open"/> (the owner's list button, command 1000) shows a pop-up list of every item. The pop-up
/// (<see cref="OpenSWR.Data.DropDownLayout"/>): the box's width, height = <see cref="RowHeight"/> *
/// <see cref="VisibleRows"/> + 4, directly below the box, above it when it would pass the bottom bound (Inferred: the
/// screen, 0 ... 480; the pop-up's top and bottom bounds are not verified); inside it the cell list at (2, 2), (w - 4) x (h - 4), cells (w - 4) x row height,
/// scroll skin 10650. The item bitmaps (box size, picture centred both ways) are drawn at the cell origin, clipped by the list. A
/// click on an item selects it, closes the pop-up and calls <see cref="Changed"/> (WM_COMMAND 0x29B); a click outside closes it.
/// Initial item = the first. Inferred: the pop-up's black background and the frame 10598 drawn centred behind the current item's
/// cell in the pop-up. Text: the owner draws the picture; the box draws the item text. The current item is painted with the box
/// format <see cref="Format"/> in the box colour; its rect is the box moved by the item's text offset (the box's own offset is 0),
/// no inset, not clipped (both users' formats carry DT_NOCLIP). Pop-up rows: cell-list cells (list
/// default format := the box's, text offset (0, 0)), so a centred text loses 2 px each side; colours the box colour unless the
/// owner gives its items own colours (<see cref="RowColours"/>: normal / selected).
/// </summary>
public partial class DropDownBox : Control
{
    public int Count { get; set; }
    public int Selected { get; private set; }
    /// <summary>The item's picture (the owner's cell drawing without text).</summary>
    public Action<CanvasItem, int, Rect2>? DrawItem { get; set; }
    /// <summary>The item's text and its text offset y.</summary>
    public Func<int, (string Text, float Y)>? ItemText { get; set; }
    public TextFormat Format { get; set; } = TextFormat.Row;
    /// <summary>Pop-up row height (defaults to the box height).</summary>
    public int RowHeight { get; set; }
    /// <summary>Visible pop-up rows.</summary>
    public int VisibleRows { get; set; } = 1;
    public int Font { get; set; } = 10;
    public Color Colour { get; set; } = Colors.White;
    /// <summary>Items with their own colours: normal, selected; null = the box colour.</summary>
    public (Color Normal, Color Selected)? RowColours { get; set; }
    public Action? Changed { get; set; }
    Popup? _popup;

    public DropDownBox() => MouseFilter = MouseFilterEnum.Ignore;

    public void Select(int i)
    {
        Selected = Math.Clamp(i, 0, Math.Max(0, Count - 1));
        QueueRedraw();
    }

    /// <summary>
    /// Pop-up bounds: top 0, bottom SM_CYFULLSCREEN taken at construction = 480 - caption height; Inferred:
    /// 461 (480 - SM_CYCAPTION 19 of the default metrics; less with a visible taskbar). No horizontal bound.
    /// </summary>
    public const int PopupBottom = 461;

    public bool IsOpen => _popup is not null;

    /// <summary>Command 1000: the pop-up list over the window layer (not clipped by the window).</summary>
    public void Open()
    {
        if (_popup is not null || Count == 0) return;
        var box = GetGlobalRect();
        var p = OpenSWR.Data.DropDownLayout.Of(box.Position.X, box.Position.Y, Size.X, Size.Y, RowHeight > 0 ? RowHeight : (int)Size.Y, VisibleRows, 0, PopupBottom);
        _popup = new Popup(this, p) { Size = new Vector2(p.Width, p.Height) };
        Node host = this;
        while (host is not GameWindow && host.GetParent() is { } up) host = up;
        (host.GetParent() ?? host).AddChild(_popup); // the window layer, above the window
        _popup.GlobalPosition = new Vector2(p.X, p.Y);
    }

    public void Close()
    {
        _popup?.QueueFree();
        _popup = null;
    }

    public override void _ExitTree() => Close();

    void Pick(int i)
    {
        Close();
        Select(i);
        Changed?.Invoke();
    }

    public override void _Draw()
    {
        if (Count == 0) return;
        DrawItem?.Invoke(this, Selected, new Rect2(Vector2.Zero, Size));
        if (ItemText?.Invoke(Selected) is var (text, y)) Assets.DrawText(this, new Rect2(0, y, Size.X, Size.Y), text, Font, Colour, Format);
    }

    /// <summary>A pop-up row: the item bitmap (box size) at the cell origin, the text by the cell-list rule (text offset (0, 0)); <paramref name="selected"/> = the box's current item (Inferred: hover not modelled).</summary>
    void DrawRow(CanvasItem c, int i, Rect2 r, bool selected)
    {
        DrawItem?.Invoke(c, i, new Rect2(r.Position, Size));
        if (ItemText?.Invoke(i) is not var (text, y)) return;
        var colour = RowColours is var (normal, sel) ? (selected ? sel : normal) : Colour;
        Assets.DrawCellText(c, new Rect2(r.Position.X, r.Position.Y + y, r.Size.X, r.Size.Y), text, Font, colour, Format, offset: 0);
    }

    /// <summary>The pop-up: black panel with the item list; a press outside closes it (and is consumed).</summary>
    partial class Popup : Control
    {
        readonly DropDownBox _box;
        readonly OpenSWR.Data.DropDownLayout.Popup _layout;
        public Popup(DropDownBox box, OpenSWR.Data.DropDownLayout.Popup layout)
        {
            (_box, _layout) = (box, layout);
            MouseFilter = MouseFilterEnum.Stop;
        }

        public override void _Ready()
        {
            var l = _layout;
            var list = new CellList(10650) { Single = true, Cell = new Vector2(l.CellWidth, l.CellHeight), Position = new Vector2(l.ListX, l.ListY), Size = new Vector2(l.ListWidth, l.ListHeight) };
            list.Count = _box.Count;
            list.DrawCell = (l, i, r) =>
            {
                if (i == _box.Selected) CellList.Centred(l, Assets.Strategy(10598), Assets.BitmapSize(10598, "STRATEGY.DLL"), r);
                _box.DrawRow(l, i, r, i == _box.Selected);
            };
            list.SelectionChanged = () => { if (list.Selected.Count == 1) _box.Pick(list.Selected.Min); };
            AddChild(list);
            list.First = _box.Selected;
        }

        public override void _Draw() { DrawRect(new Rect2(Vector2.Zero, Size), Colors.Black); DrawLog.Fill(this, new Rect2(Vector2.Zero, Size), Colors.Black); }

        public override void _Input(InputEvent e)
        {
            if (e is InputEventMouseButton { Pressed: true } b && !GetGlobalRect().HasPoint(b.Position))
            {
                GetViewport().SetInputAsHandled();
                _box.Close();
            }
            else if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                GetViewport().SetInputAsHandled();
                _box.Close();
            }
        }
    }
}
