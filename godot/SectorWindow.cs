using System.Collections.Generic;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Sector window (235 x 360, not draggable, no minimise): per system of the sector (the player
/// side's views, <see cref="ScreenViews.Sector"/>) the picture 14000 + p (Destroyed: 10240) with the Uprising overlay 905 / 906,
/// the HQ marker 904, the name (font 10, owner colour), the energy / raw-material / support bars with tooltips and the four group
/// icons. Click selects (Ctrl toggles), double click on an icon opens its system sub-window, right click: object menu with the
/// group modes, drag. Button swaps the window to the other slot. Background: grey, slightly translucent (observed in the original),
/// frame drawn by <see cref="UiFrame"/>.
/// </summary>
public partial class SectorWindow : GameWindow
{
    readonly Sector _sector;
    List<SectorSystem> _systems = [];
    readonly List<(StarSystem System, GroupMode Mode)> _selected = [];
    Vector2? _press;
    bool _dragging;

    public SectorWindow(MainScreen screen, WindowAddress id, Sector sector) : base(screen, id, new Vector2(235, 360))
    {
        _sector = sector;
        TooltipText = " "; // enables _GetTooltip
        Refresh();
    }

    public Sector Sector => _sector;
    protected override string Title => Screen.DisplayName(_sector.Definition.Common);
    public override GameObject? Subject => null;

    public override void _Ready()
    {
        base._Ready();
        AddChild(new Darkened()); // the main-screen picture under the window, darkened
        Button(StrategyBitmaps.SwapSectorSlotUp, StrategyBitmaps.SwapSectorSlotDown, (int)Size.X - 14 - 3 - 14, 3, () => Screen.Windows.SwapSector(this), sound: Assets.Click); // click 608 = openswr (the original is silent)
    }

    public override void Refresh()
    {
        if (Screen.Game is null) return;
        _systems = ScreenViews.Sector(Game, _sector, Side);
        _selected.RemoveAll(s => !_systems.Any(x => x.System == s.System && (s.Mode == GroupMode.Picture || IconOf(x, s.Mode).Shown)));
        QueueRedraw();
    }

    static SectorIcon IconOf(SectorSystem s, GroupMode mode) => s.Icons.First(i => i.Mode == mode);

    // Quadrant cell top-left around the picture centre (px + 18, py + 18).
    static Vector2 IconAt(SectorSystem s, GroupMode mode)
    {
        float cx = s.X + 18, cy = s.Y + 18;
        return mode switch { GroupMode.Facilities => new(cx - 28, cy - 19), GroupMode.Ground => new(cx - 28, cy + 1), GroupMode.Fleets => new(cx + 1, cy - 19), _ => new(cx + 1, cy + 1) };
    }

    static Rect2 Picture(SectorSystem s) => new(s.X, s.Y, 37, 37);

    /// <summary>Icon images (normal, selected) by mode and colour side: A 10771 + 2i, E 10779 + 2i, neutral 10787 + 2i (facilities and ground modes only).</summary>
    static ushort IconImage(GroupMode mode, SimOwner colour, bool selected)
    {
        int i = mode switch { GroupMode.Facilities => 0, GroupMode.Ground => 1, GroupMode.Fleets => 2, _ => 3 };
        int b = colour switch { SimOwner.Alliance => StrategyBitmaps.FacilitiesIconAlliance, SimOwner.Empire => StrategyBitmaps.FacilitiesIconEmpire, _ => StrategyBitmaps.FacilitiesIconNeutral };
        return (ushort)(b + 2 * i + (selected ? 1 : 0));
    }

    /// <summary>Hit test: the icons first (they lie over the picture; Inferred order), then the picture rect (the original tests its opaque pixels).</summary>
    (StarSystem System, GroupMode Mode)? Hit(Vector2 p)
    {
        foreach (var s in _systems)
            foreach (var icon in s.Icons.Where(i => i.Shown))
                if (new Rect2(IconAt(s, icon.Mode), new Vector2(28, 19)).HasPoint(p)) return (s.System, icon.Mode);
        foreach (var s in _systems)
            if (Picture(s).HasPoint(p)) return (s.System, GroupMode.Picture);
        return null;
    }

    public override List<(GameObject Object, GroupMode Mode)> Selection => [.. _selected.Select(s => ((GameObject)s.System, s.Mode))];

    /// <summary>T_dest: the system whose picture is under the point; T_move: the system whose item rect contains it.</summary>
    public override GameObject? DestAt(Vector2 local) => _systems.FirstOrDefault(s => Picture(s).HasPoint(local))?.System;
    // openswr deviation (product owner): the own fleet icon takes the drop as a fleet (ScreenViews.IconFleet), units go aboard
    public override GameObject? MoveAt(Vector2 local) =>
        Hit(local) is not { } hit ? null : hit.Mode == GroupMode.Fleets && ScreenViews.IconFleet(hit.System, Side) is { } fleet ? fleet : hit.System;

    public override void SelectObject(GameObject o)
    {
        if (o is not StarSystem s) return;
        _selected.Clear();
        _selected.Add((s, GroupMode.Picture));
        QueueRedraw();
    }

    protected override void WindowInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } b:
            {
                var hit = Hit(b.Position);
                if (b.ButtonIndex == MouseButton.Left) (_press, _dragging) = (b.Position, false);
                if (hit is not { } item) break;
                if (b.DoubleClick && b.ButtonIndex == MouseButton.Left && item.Mode != GroupMode.Picture)
                {
                    _selected.Clear();
                    _selected.Add(item);
                    Screen.OpenSystemWindow(item.System, item.Mode, GetGlobalMousePosition());
                    Screen.SubWindowTip(item.Mode);
                    _press = null;
                }
                else if (!b.CtrlPressed && !_selected.Contains(item))
                {
                    _selected.Clear();
                    _selected.Add(item);
                }
                QueueRedraw();
                break;
            }
            case InputEventMouseMotion m when _press is { } start && Input.IsMouseButtonPressed(MouseButton.Left) && !_dragging:
                if (Hit(start) is { } pressed && (_selected.Contains(pressed) || m.CtrlPressed))
                {
                    if (!_selected.Contains(pressed)) _selected.Add(pressed);
                    _dragging = true;
                    var (image, size) = DragImage(pressed);
                    Screen.BeginDrag(this, start + GetGlobalRect().Position, image, size);
                }
                else _press = null;
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } up:
                if (!_dragging && _press is not null && up.CtrlPressed && Hit(up.Position) is { } toggled)
                {
                    if (!_selected.Remove(toggled)) _selected.Add(toggled);
                    QueueRedraw();
                }
                _press = null;
                _dragging = false;
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } r when _selected.Count > 0 && Hit(r.Position) is not null:
                Screen.OpenObjectMenu(Selection, GetGlobalMousePosition());
                break;
        }
    }

    (Texture2D, Vector2) DragImage((StarSystem System, GroupMode Mode) item)
    {
        var s = _systems.First(x => x.System == item.System);
        if (item.Mode == GroupMode.Picture) return ModArt.SystemPicture(Game.Definitions, s.System, s.Picture, s.Destroyed);
        ushort icon = IconImage(item.Mode, IconOf(s, item.Mode).Colour, false);
        return (Assets.Strategy(icon), Assets.BitmapSize(icon, "STRATEGY.DLL"));
    }

    static Color OwnerColour(SimOwner o, bool destroyed) => destroyed ? new Color(0, 1, 1) : o switch
    {
        SimOwner.Alliance => new Color(1, 0, 0), SimOwner.Empire => new Color(0, 1, 0), _ => new Color(0, 1, 1),
    };

    public override void _Draw()
    {
        UiFrame.Draw(this, new Rect2(Vector2.Zero, Size), fill: false);
        DrawTitle();
        foreach (var s in _systems)
        {
            var pic = Picture(s);
            var (texture, size) = ModArt.SystemPicture(Game.Definitions, s.System, s.Picture, s.Destroyed);
            CellList.Centred(this, texture, size, pic);
            if (s.Uprising && s.Owner is SimOwner.Alliance or SimOwner.Empire) Overlay((ushort)(s.Owner == SimOwner.Alliance ? StrategyBitmaps.UprisingOverlayAlliance : StrategyBitmaps.UprisingOverlayEmpire), pic);
            if (s.Headquarters) Overlay(StrategyBitmaps.HeadquartersOverlay, pic);
            if (_selected.Contains((s.System, GroupMode.Picture))) DrawRect(pic, Colors.White, filled: false); // selected picture (own marking, Inferred)
            int h = (int)Assets.LineHeight(10);
            Assets.DrawText(this, new Rect2(s.X + 18 - 50, s.Y + 48, 100, h), Screen.DisplayName(s.System.Definition.Common), 10, OwnerColour(s.Owner, s.Destroyed), TextFormat.CentredRow);
            if (!s.Destroyed)
            {
                if (s.Explored)
                {
                    Blocks(new Rect2(s.X, s.Y + 39, 3 * s.Energy, 3), s.UsedEnergy, s.Energy, Colors.White, new Color(0, 0, 1));
                    Blocks(new Rect2(s.X, s.Y + 43, 3 * s.Raw, 3), s.UsedRaw, s.Raw, new Color(1, 1, 0), Color.Color8(249, 92, 15));
                }
                if (s.Owner != SimOwner.None && s.Populated)
                    Bar(new Rect2(s.X, s.Y + 47, 37, 3), s.Support, 100, UiFrame.SideColour(A), UiFrame.SideColour(!A));
            }
            foreach (var icon in s.Icons.Where(i => i.Shown))
            {
                bool selected = _selected.Contains((s.System, icon.Mode)) && icon.Colour == Side; // only the player's colour has a selected look; other icons show no selection (openswr: no white box)
                var image = IconImage(icon.Mode, icon.Colour, selected);
                Overlay(image, new Rect2(IconAt(s, icon.Mode), Assets.BitmapSize(image, "STRATEGY.DLL")));
            }
        }
    }

    /// <summary>
    /// CoolwinStatusBar: value part in the first colour, the rest in the second (observed in the original: energy white used / blue unused, raw
    /// yellow used / red-orange unused; support: the Alliance share red, the Empire share green, i.e. the player's share first).
    /// </summary>
    void Bar(Rect2 r, int value, int max, Color first, Color rest)
    {
        if (r.Size.X <= 0) return;
        Fill(r, rest);
        // value width in whole pixels (rounding Inferred)
        int v = max > 0 ? (int)(r.Size.X * Mathf.Clamp(value, 0, max) / max) : 0;
        if (v > 0) Fill(new Rect2(r.Position, new Vector2(v, r.Size.Y)), first);
    }

    void Fill(Rect2 r, Color c) { DrawRect(r, c); DrawLog.Fill(this, r, c); }

    void Overlay(ushort image, Rect2 at) { var t = Assets.Strategy(image); DrawTextureRect(t, at, false); DrawLog.Bitmap(this, t, at); }

    /// <summary>
    /// CoolwinStatusBar slot bar: 3 * total wide; per slot i a grey RGB (128, 128, 128)
    /// column at x = 3i, then two pixels in the first colour if i &lt; used, else in the second. No closing line at x = 3 * total.
    /// </summary>
    void Blocks(Rect2 r, int used, int total, Color first, Color rest)
    {
        if (total <= 0) return;
        Bar(new Rect2(r.Position, new Vector2(3 * total, r.Size.Y)), 3 * Mathf.Clamp(used, 0, total), 3 * total, first, rest);
        var grey = Color.Color8(128, 128, 128);
        for (int i = 0; i < total; i++)
        {
            var column = new Rect2(r.Position.X + 3 * i, r.Position.Y, 1, r.Size.Y);
            DrawRect(column, grey);
            DrawLog.Line(this, column, grey);
        }
    }

    /// <summary>Bar tooltips: 6178 "Energy Consumption" / 6179 "Raw Materials" + " " + used + "/" + total; 6177 "Popular Support" (order Inferred).</summary>
    public override string _GetTooltip(Vector2 at)
    {
        foreach (var s in _systems.Where(s => !s.Destroyed))
        {
            if (s.Explored && new Rect2(s.X, s.Y + 39, 3 * s.Energy, 3).Grow(1).HasPoint(at)) return $"{StrategyText.EnergyBar}{StrategyText.BarSeparator}{s.UsedEnergy}{StrategyText.BarSlash}{s.Energy}";
            if (s.Explored && new Rect2(s.X, s.Y + 43, 3 * s.Raw, 3).Grow(1).HasPoint(at)) return $"{StrategyText.RawMaterialsBar}{StrategyText.BarSeparator}{s.UsedRaw}{StrategyText.BarSlash}{s.Raw}";
            if (s.Owner != SimOwner.None && s.Populated && new Rect2(s.X, s.Y + 47, 37, 3).Grow(1).HasPoint(at)) return $"{StrategyText.PopularSupportBar}{StrategyText.BarSeparator}{s.Support}";
        }
        return "";
    }
}
