using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using TextItemSize = OpenSWR.Data.TextItemSize;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Status window (window id 0x1A, 379 x 272, centred in the map rect, fixed): a snapshot built at open
/// (<see cref="StatusPage"/>; no refresh). Background by player side and view owner, page title (15, 18), name (242, 137), picture
/// centred on (307, 64), list (18, 47) 208 x 204 with skin 10650: object pages = one tall cell of label / value rows (label x 0, value
/// x 103, font 10, word break), Galaxy Overview / Objectives = picture rows 40 high (picture column 36). Encyclopedia 0x66 (greyed for
/// the side and null keys; closes the window first) and Close 0x65; Esc closes. Inferred: the scroll step (one 14-px line), picture
/// rows scaled into the 36 x 40 column.
/// </summary>
public partial class StatusWindow : GameWindow
{
    public const int WindowId = 0x1A;
    readonly GameObject? _object;
    readonly bool _overview;
    readonly StatusPage _page;
    RowList? _rows;
    CellList? _items;

    /// <summary><paramref name="o"/> null with <paramref name="overview"/> = the Galaxy Overview (side key 0xF1), null alone = Objectives.</summary>
    public StatusWindow(MainScreen screen, GameObject? o, bool overview) : base(screen, WindowId, new Vector2(379, 272))
    {
        (_object, _overview) = (o, overview);
        var game = screen.Game!;
        _page = o is not null ? StatusPage.Build(game, screen.Side, o, screen.ObjectName, screen.DisplayName)
            : overview ? StatusPage.Overview(game, screen.Side, screen.DisplayName) : StatusPage.Objectives(game, screen.Side);
        var r = screen.MapRect;
        Position = (r.Position + (r.Size - Size) / 2).Floor();
    }

    protected override bool Framed => false;
    protected override string Title => _page.Title == 0 ? "" : Assets.Stra(_page.Title);

    static Texture2D? Bmp(int id, string module = "STRATEGY.DLL")
    {
        return id is <= 0 or > 0xFFFF ? null : Assets.TryBitmap((ushort)id, module);
    }

    static Vector2 Dim(int id, string module = "STRATEGY.DLL") => Assets.BitmapSize((ushort)id, module);

    public override void _Ready()
    {
        var enc = new FrontButton { Up = Bmp(11552), Down = Bmp(11553), DisabledImage = Bmp(11612), TooltipText = Assets.Tip(Assets.Stra(5381)), Disabled = _object is null,
            Command = () => { var o = _object!; Screen.Windows.Close(this); Screen.OpenEncyclopedia(o, 0); } };
        enc.Place(258, 218, 32, 31);
        AddChild(enc);
        var close = new FrontButton { Up = Bmp(10370), Down = Bmp(10371), TooltipText = Assets.Tip(Assets.Stra(6484)), Command = () => Screen.Windows.Close(this) };
        close.Place(324, 218, (int)Dim(10370).X, (int)Dim(10370).Y);
        AddChild(close);
        var area = new Rect2(18, 47, 208, 204);
        if (_object is not null)
        {
            _rows = new RowList(_page.Rows) { Position = area.Position, Size = area.Size };
            _rows.Pressed = () => Screen.Windows.Focus(this);
            AddChild(_rows);
            return;
        }
        _items = new CellList(10650) { Cell = new Vector2(208 - (Assets.Skin(10650)?.Thickness ?? 0), 40), Position = area.Position, Size = area.Size, Count = _page.Items.Count };
        _items.DrawCell = DrawItem;
        _items.Pressed = () => Screen.Windows.Focus(this);
        _items.SelectionChanged = () => _items.Selected.Clear(); // not selectable
        AddChild(_items);
    }

    void DrawItem(CellList list, int i, Rect2 r)
    {
        var item = _page.Items[i];
        string module = item.Gokres ? "GOKRES.DLL" : "STRATEGY.DLL";
        if (Bmp(item.Picture, module) is { } t)
        {
            var size = Dim(item.Picture, module);
            float k = Math.Min(1, Math.Min(36 / size.X, 40 / size.Y));
            CellList.Centred(list, t, (size * k).Floor(), new Rect2(r.Position, new Vector2(36, r.Size.Y)));
        }
        string text = item.Text != 0 ? Assets.Stra(item.Text) : item.Value;
        Assets.DrawCellText(list, new Rect2(r.Position.X + 40, r.Position.Y, r.Size.X - 40, r.Size.Y), text, 10, Colors.White, TextFormat.Row);
    }

    /// <summary>Background: null / side key 11554 (A) / 11558 (E); else by view owner O: A 11554 / 11555 / 11556, E 11557 / 11558 / 11559.</summary>
    int Background()
    {
        if (_object is null) return A ? 11554 : 11558;
        var owner = ScreenViews.View(Game, _object, Side).Owner;
        if (_object is StarSystem s) owner = Knowledge.See(Game, s, Side).Owner;
        return (A, owner) switch
        {
            (true, SimOwner.Alliance) => 11554, (true, SimOwner.Empire) => 11555, (true, _) => 11556,
            (false, SimOwner.Alliance) => 11557, (false, SimOwner.Empire) => 11558, _ => 11559,
        };
    }

    string NameText => _object is null ? Assets.Stra(_overview ? 12563 : 12564) : Screen.ObjectName(_object);

    public override void _Draw()
    {
        var back = Bmp(Background())!;
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        bool manager = _object is ManufacturingManager;
        // text items: fixed size, not clipped
        Assets.DrawTextItem(this, new Rect2(15, 18, 211, manager ? 36 : 18), Title, 5, Colors.White, manager ? TextFormat.CentredWrap : new TextFormat { Center = true, SingleLine = true }, TextItemSize.Fixed);
        Assets.DrawTextItem(this, new Rect2(242, 137, 130, 44), NameText, 5, Colors.White, TextFormat.CentredWrap, TextItemSize.Fixed);
        foreach (var (t, size) in Picture())
        {
            var at = new Rect2(new Vector2(307 - (int)(size.X / 2), 64 - (int)(size.Y / 2)), size);
            DrawTextureRect(t, at, false);
            DrawLog.Bitmap(this, t, at);
        }
    }

    /// <summary>Picture: null / side key 11590; missions GOKRES p (owner 1) / p + 0x1000; managers GOKRES 263 / 262 / 264; else the object picture with overlays in the small = 1 form (GOKRES p, no 11500 frame).</summary>
    List<(Texture2D Texture, Vector2 Size)> Picture()
    {
        (Texture2D, Vector2)? one = _object switch
        {
            null => ModArt.Install(false, 11590),
            Mission m when (ScreenViews.View(Game, m, Side).Owner == SimOwner.Alliance ? 0 : 0x1000) is var form =>
                ModArt.Picture(Game.Definitions, m.Record.Common.Ref, form) ?? ModArt.Install(true, ModArt.PictureNumber(m.Record.Common.StringId) + form),
            ManufacturingManager mm => ModArt.Install(true, StatusPage.ManagerPicture(mm)),
            _ => null,
        };
        if (_object is null or Mission or ManufacturingManager) return one is { } x ? [x] : [];
        return ModArt.Layers(Game, _object!, ScreenViews.Picture(Game, ScreenViews.View(Game, _object!, Side), Side, small: true, overlays: true));
    }

    /// <summary>The object page's single tall cell: rows of (label, value), each as high as its taller text, scrolled by lines.</summary>
    partial class RowList : Control
    {
        readonly List<(string Label, string Value)> _rows;
        readonly OpenSWR.Data.ScrollSkin? _skin = Assets.Skin(10650);
        float _offset, _height;
        float? _grab; // thumb drag: mouse y minus thumb top
        int _pressed;
        public Action? Pressed;
        const float Step = 14;

        public RowList(List<StatusRow> rows)
        {
            _rows = [.. rows.Select(r => ((r.Label == 0 ? "" : Assets.Stra(r.Label)) + (r.Colon ? ":" : ""), (r.Text != 0 ? Assets.Stra(r.Text) : "") + r.Value))];
            MouseFilter = MouseFilterEnum.Stop;
            ClipContents = true;
        }

        float Bar => _skin?.Thickness ?? 0;

        static float TextHeight(string s, float width)
        {
            if (s == "") return Assets.LineHeight(10);
            return Assets.Lines(10, s, (int)width).Count * Assets.LineHeight(10);
        }

        public override void _Ready() => _height = _rows.Sum(r => Math.Max(TextHeight(r.Label, 103), TextHeight(r.Value, 207 - 103 - Bar)));

        void Scroll(float d)
        {
            _offset = Math.Clamp(_offset + d, 0, Math.Max(0, _height - Size.Y));
            QueueRedraw();
        }

        Rect2 Bounds => new(Vector2.Zero, Size);

        // scroll bar in Step units over the pixel offset (as drawn)
        (int First, int Visible, int Total) Lines => ((int)(_offset / Step), (int)(Size.Y / Step), (int)Math.Ceiling(_height / Step));

        // the bar is drawn (and reacts) only while the rows overflow
        OpenSWR.Data.ScrollSkin? BarSkin => Lines is var (_, visible, total) && total > visible ? _skin : null;

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }: Scroll(-Step); break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }: Scroll(Step); break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } b:
                    Pressed?.Invoke();
                    if (BarSkin is not { } k || !ScrollBar.InBar(k, Bounds, b.Position)) break;
                    var (first, visible, total) = Lines;
                    if (ScrollBar.Button(k, Bounds, b.Position) is var dir && dir != 0)
                    {
                        _pressed = dir;
                        Scroll(dir * Step);
                    }
                    else if (ScrollBar.Thumb(k, Bounds, first, visible, total) is var (y0, len))
                    {
                        if (b.Position.Y < y0) Scroll(-visible * Step); // track above / below the thumb: a page
                        else if (b.Position.Y >= y0 + len) Scroll(visible * Step);
                        else _grab = b.Position.Y - y0;
                    }
                    break;
                case InputEventMouseMotion m when _grab is { } grab && BarSkin is { } skin && Input.IsMouseButtonPressed(MouseButton.Left):
                    var (_, vis, tot) = Lines;
                    Scroll(ScrollBar.FirstAt(skin, Bounds, m.Position.Y - grab, vis, tot) * Step - _offset);
                    break;
                case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                    _pressed = 0;
                    _grab = null;
                    QueueRedraw();
                    break;
                default: return;
            }
            AcceptEvent();
        }

        public override void _Draw()
        {
            float y = -_offset, valueWidth = 207 - 103 - Bar;
            foreach (var (label, value) in _rows)
            {
                float h = Math.Max(TextHeight(label, 103), TextHeight(value, valueWidth));
                if (y + h > 0 && y < Size.Y)
                {
                    Assets.DrawText(this, new Rect2(0, y, 103, h), label, 10, Colors.White, TextFormat.Wrap);
                    Assets.DrawText(this, new Rect2(103, y, valueWidth, h), value, 10, Colors.White, TextFormat.Wrap);
                }
                y += h;
            }
            if (_skin is { } k && Lines is var (first, visible, total))
                ScrollBar.Draw(this, k, Bounds, first, visible, total, _pressed);
        }
    }
}
