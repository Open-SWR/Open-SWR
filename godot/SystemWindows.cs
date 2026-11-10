using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using TextItemSize = OpenSWR.Data.TextItemSize;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// System sub-window base: full-size background bitmap, title = the system's name, minimise / go-to-sector buttons, and cell lists
/// filled from the player side's view of the system (<see cref="ScreenViews"/>). Object pictures go in the cells. The title strip is
/// drawn from the owner skin, and the fleet window also has its cover. Other per-owner decoration bitmaps are left out. A list's
/// selection is the window's selection (objects, object mode). Right click opens the object menu. Dragging starts a drop on another window.
/// </summary>
public abstract partial class SystemWindow : GameWindow
{
    protected readonly StarSystem Sys;
    readonly ushort _background;
    protected CellList? Active; // the list the selection comes from

    protected SystemWindow(MainScreen screen, WindowAddress id, StarSystem s, ushort background, Vector2 size) : base(screen, id, size)
    {
        (Sys, _background) = (s, background);
    }

    protected override bool SubWindow => true;
    public override StarSystem System => Sys;
    protected override string Title => Screen.DisplayName(Sys.Definition.Common);

    /// <summary>The skin's side: the view owner of the system (the fleet window overrides it).</summary>
    protected virtual SimOwner SkinOwner => Knowledge.See(Game, Sys, Side).Owner;

    /// <summary>
    /// Background, then the owner skin's title strip (231 x 16 at (2, 2): A 10299 / 10200, E 10201 / 10302, neutral 10303 / 10304; the
    /// first while focused, Inferred from the art), then the title.
    /// </summary>
    public override void _Draw()
    {
        var back = Assets.Strategy(_background);
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        bool focused = Screen.Windows.Focused == this;
        int strip = SkinOwner switch { SimOwner.Alliance => focused ? StrategyBitmaps.TitleStripAllianceFocused : StrategyBitmaps.TitleStripAlliance, SimOwner.Empire => focused ? StrategyBitmaps.TitleStripEmpireFocused : StrategyBitmaps.TitleStripEmpire, _ => focused ? StrategyBitmaps.TitleStripNeutralFocused : StrategyBitmaps.TitleStripNeutral };
        var stripImage = Assets.Strategy((ushort)strip);
        DrawTextureRect(stripImage, new Rect2(2, 2, 231, 16), false);
        DrawLog.Bitmap(this, stripImage, new Rect2(2, 2, 231, 16));
        DrawCover();
        DrawTitle();
    }

    /// <summary>Drawn over the background before the title (the fleet window's cover; the other sub-windows have none).</summary>
    protected virtual void DrawCover() { }

    /// <summary>A cell list wired to the window: focus on press, selection goes to the window's, right click opens the object menu, drag.</summary>
    protected CellList List(Rect2 r, Vector2 cell, ushort skin, Func<int, GameObject?> item, Action<CellList, int, Rect2> draw, bool draggable = false)
    {
        var list = new CellList(skin) { Cell = cell, Position = r.Position, Size = r.Size, Draggable = draggable, DrawCell = draw };
        list.Pressed = () => { Screen.Windows.Focus(this); Active = list; };
        list.ContextMenu = at => { if (Selection.Count > 0) Screen.OpenObjectMenu(Selection, at); };
        list.DragStarted = (start, i) => { if (item(i) is { } o) Screen.BeginDrag(this, start, PictureTexture(o)?.Texture, PictureTexture(o)?.Size); };
        AddChild(list);
        return list;
    }

    protected virtual GameObject? ItemOf(CellList list, int i) => null;

    public override List<(GameObject Object, GroupMode Mode)> Selection =>
        Active is null ? [] : [.. Active.Selected.Select(i => ItemOf(Active, i)).Where(o => o is not null).Select(o => (o!, GroupMode.Object))];

    protected static Texture2D? Bitmap(bool gokres, int id)
    {
        return Assets.TryBitmap((ushort)id, gokres ? "GOKRES.DLL" : "STRATEGY.DLL");
    }


    (Texture2D Texture, Vector2 Size)? PictureTexture(GameObject o)
    {
        var layers = ModArt.Layers(Game, o, ScreenViews.Picture(Game, ScreenViews.View(Game, o, Side), Side, small: false, overlays: false));
        return layers.Count > 0 ? layers[0] : null;
    }

    /// <summary>Object picture centred in <paramref name="r"/>: every layer at its logical size.</summary>
    protected void DrawPicture(CanvasItem c, ViewObject v, Rect2 r, bool small)
    {
        foreach (var (t, size) in ModArt.Layers(Game, v.Object, ScreenViews.Picture(Game, v, Side, small, overlays: true)))
            CellList.Centred(c, t, size, r);
    }

    /// <summary>
    /// Cell: picture in the upper part, the name under it (font 9, white; Inferred), selection: <paramref name="frame"/> (61 x 25) centred
    /// over the picture and the name in <paramref name="selectedText"/> (planet window), else a white rectangle (Inferred).
    /// </summary>
    protected void DrawObjectCell(CellList list, int i, Rect2 r, ViewObject v, bool small, float pictureHeight, int frame = 0, Color? selectedText = null, bool wrap = false)
    {
        bool selected = list.Selected.Contains(i);
        var picture = new Rect2(r.Position, new Vector2(r.Size.X, pictureHeight));
        DrawPicture(list, v, picture, small);
        if (selected && frame != 0) CellList.Centred(list, Bitmap(false, frame), new Vector2(61, 25), picture);
        var text = selected ? selectedText ?? Colors.White : Colors.White;
        string name = Screen.ObjectName(v.Object);
        if (wrap)
        {
            // openswr (player request): the name right under the picture (25 high, centred in pictureHeight; -2 eats the font's top leading), centred, wrapped at spaces
            float top = (pictureHeight + 25) / 2 - 2;
            Assets.DrawCellText(list, new Rect2(r.Position.X, r.Position.Y + top, r.Size.X, r.Size.Y - top), name, 9, text, TextFormat.CentredWrap);
        }
        else
        {
            var format = Assets.TextWidth(9, name) > r.Size.X - 5 ? TextFormat.Row : TextFormat.CentredRow; // (centred text rect = cell - 5 px) too long: left-aligned, so only its end is cut (openswr)
            Assets.DrawCellText(list, new Rect2(r.Position.X, r.Position.Y + pictureHeight, r.Size.X, r.Size.Y - pictureHeight), name, 9, text, format);
        }
        if (selected && frame == 0) { list.DrawRect(r.Grow(-1), Colors.White, filled: false); DrawLog.Frame(list, r.Grow(-1), Colors.White); }
    }

    /// <summary>A radio tab button: the down image while selected.</summary>
    protected FrontButton Tab(int x, int y, int w, int h, int up, int down, int tooltip, Action select)
    {
        var b = new FrontButton { Up = Bitmap(false, up), Down = Bitmap(false, down), Command = select, TooltipText = tooltip == 0 ? "" : Assets.Tip(Assets.Stra(tooltip)) };
        b.CheckedImage = b.Down;
        b.Place(x, y, w, h);
        AddChild(b);
        return b;
    }

    /// <summary>T_dest of the planet / manufacturing / missions windows: the cell object under the point, else the system (Inferred for 9 / 11).</summary>
    public override GameObject? DestAt(Vector2 local) => CellAt(local) ?? Sys;
    public override GameObject? MoveAt(Vector2 local) => Sys;

    protected GameObject? CellAt(Vector2 local)
    {
        foreach (var list in GetChildren().OfType<CellList>().Where(l => l.Visible))
            if (new Rect2(list.Position, list.Size).HasPoint(local) && list.At(local - list.Position) is var i && i >= 0) return ItemOf(list, i);
        return null;
    }
}

/// <summary>
/// System planet window (235 x 304, background 10577): tabs 1 Personnel / 2 Trooper Regiments / 3 Fighter
/// Squadrons / 4 Planetary Shields / 5 Planetary Batteries; list at (7, 81) 222 x 210, cells 70 x 70, skin 10700, large
/// pictures + names; tabs 1-3 draggable. Tabs 4 and 5 use 10553 / 10550 when any exist, else 10555 / 10552. Tabs 1-3 use the triplet
/// of the view owner: for A, id - 6 / - 5 / - 4, and for E, id - 3 / - 2 / - 1 (up has / down / up none). A neutral system uses the
/// grey id for all three. Selected cell: frame 10578 (owner 1) or 10579 (owner 2, 3) over the picture (Inferred role), name in red / green / cyan.
/// </summary>
public partial class PlanetWindow : SystemWindow
{
    int _tab = ControlIds.PlanetPersonnelTab;
    List<ViewObject> _items = [];
    readonly List<FrontButton> _tabs = [];
    CellList _list = null!;

    public PlanetWindow(MainScreen screen, WindowAddress id, StarSystem s) : base(screen, id, s, StrategyBitmaps.PlanetWindowBackground, new Vector2(235, 304)) { }

    public override ushort Icon => StrategyBitmaps.PlanetUnitsIconAlliance;

    public override void _Ready()
    {
        base._Ready();
        _list = List(new Rect2(7, 81, 222, 210), new Vector2(70, 70), StrategyRcData.PlanetWindowScrollSkin, i => _items.ElementAtOrDefault(i).Object,
            (l, i, r) => DrawObjectCell(l, i, r, _items[i], small: false, 40, SkinOwner == SimOwner.Alliance ? StrategyBitmaps.PlanetCellSelectedAlliance : StrategyBitmaps.PlanetCellSelectedEmpire,
                SkinOwner switch { SimOwner.Alliance => new Color(1, 0, 0), SimOwner.Empire => new Color(0, 1, 0), _ => new Color(0, 1, 1) }, wrap: true), draggable: true);
        Active = _list;
        int[] xs = [28, 64, 100, 136, 172];
        int[] tips = [StrategyTexts.PlanetPersonnel, StrategyTexts.PlanetTrooperRegiments, StrategyTexts.PlanetFighterSquadrons, StrategyTexts.PlanetShields, StrategyTexts.PlanetBatteries];
        for (int t = ControlIds.PlanetPersonnelTab; t <= ControlIds.PlanetBatteriesTab; t++)
        {
            int tab = t;
            _tabs.Add(Tab(xs[t - 1], 20, 36, 33, 0, 0, tips[t - 1], () => SelectTab(tab)));
        }
        SelectTab(ControlIds.PlanetPersonnelTab);
    }

    protected override GameObject? ItemOf(CellList list, int i) => _items.ElementAtOrDefault(i).Object;

    void SelectTab(int tab)
    {
        _tab = tab;
        _list.Selected.Clear();
        _list.First = 0;
        Refresh();
    }

    public override void Refresh()
    {
        if (Screen.Game is null || _tabs.Count == 0) return;
        _items = ScreenViews.Planet(Game, Sys, _tab, Side);
        _list.Count = _items.Count;
        for (int t = ControlIds.PlanetPersonnelTab; t <= ControlIds.PlanetBatteriesTab; t++)
        {
            bool any = ScreenViews.Planet(Game, Sys, t, Side).Count > 0;
            var (up, down) = t switch
            {
                ControlIds.PlanetShieldsTab => (any ? StrategyBitmaps.ShieldsTabUp : StrategyBitmaps.ShieldsTabNone, StrategyBitmaps.ShieldsTabDown),
                ControlIds.PlanetBatteriesTab => (any ? StrategyBitmaps.BatteriesTabUp : StrategyBitmaps.BatteriesTabNone, StrategyBitmaps.BatteriesTabDown),
                _ => TabImages(new[] { StrategyBitmaps.PersonnelTabNeutral, StrategyBitmaps.TrooperRegimentsTabNeutral, StrategyBitmaps.FighterSquadronsTabNeutral }[t - 1], any),
            };
            var b = _tabs[t - 1];
            (b.Up, b.Down, b.CheckedImage) = (Bitmap(false, up), Bitmap(false, down), Bitmap(false, down));
            b.Checked = t == _tab;
        }
        _list.QueueRedraw();
        QueueRedraw();
    }

    (int, int) TabImages(int id, bool any) => SkinOwner switch
    {
        SimOwner.Alliance => (any ? id - 6 : id - 4, id - 5),
        SimOwner.Empire => (any ? id - 3 : id - 1, id - 2),
        _ => (id, id),
    };

    public override void SelectObject(GameObject o)
    {
        for (int t = ControlIds.PlanetPersonnelTab; t <= ControlIds.PlanetBatteriesTab; t++)
            if (ScreenViews.Planet(Game, Sys, t, Side).FindIndex(v => v.Object == o) is var i && i >= 0)
            {
                SelectTab(t);
                _list.Select(i);
                return;
            }
    }

    /// <summary>Text lines (2, 51) / (2, 63), white font 4: the tab's title and the count (Inferred).</summary>
    public override void _Draw()
    {
        base._Draw();
        Assets.DrawText(this, new Rect2(2, 51, 231, 12), Assets.Stra(new[] { StrategyTexts.PlanetPersonnel, StrategyTexts.PlanetTrooperRegiments, StrategyTexts.PlanetFighterSquadrons, StrategyTexts.PlanetShields, StrategyTexts.PlanetBatteries }[_tab - 1]), 4, Colors.White, TextFormat.CentredRow);
        Assets.DrawText(this, new Rect2(2, 63, 228, 14), _items.Count.ToString(), 4, Colors.White, TextFormat.CentredRow);
    }
}

/// <summary>
/// Fleet window (235 x 304, background 10770): fleet list at (4, 29) 91 x 266, cells 91 x 50, skin 10699 (every
/// fleet at the system, any owner; the first one is selected at open, as observed in the original; double click expands a fleet's
/// capital ships beneath it, Inferred); info area: the single
/// selected fleet's name and picture 10425 / 10475 with the Enroute / Damaged overlays; tab bar Ships / Fighter Squadrons / Troop
/// Regiments / Personnel (images of the shown fleets' side); contents list 200 at (101, 127) 133 x 164, cells 125 x 50; the Ships tab lists
/// the selected fleets' capital ships alphabetically by name (as observed in the original).
/// </summary>
public partial class FleetWindow : SystemWindow
{
    readonly HashSet<GameObject> _expanded = [];
    List<(ViewObject Item, bool Ship)> _rows = [];
    List<ViewObject> _contents = [];
    Presence[] _masks = [], _contentMasks = []; // presence masks of the rows and contents cells
    static readonly Presence[] TabPresence = [new(Fighters: true), new(Troops: true), new(Characters: true)]; // tabs of fighters, troops and characters
    int _tab = ControlIds.FleetShipsTab;
    CellList _fleets = null!, _list = null!;
    readonly List<FrontButton> _tabs = [];

    public FleetWindow(MainScreen screen, WindowAddress id, StarSystem s) : base(screen, id, s, StrategyBitmaps.FleetWindowBackground, new Vector2(235, 304)) { }

    public override ushort Icon => StrategyBitmaps.FleetsIconAlliance;

    public override void _Ready()
    {
        base._Ready();
        _fleets = List(new Rect2(4, 29, 91, 266), new Vector2(91, 50), StrategyRcData.FleetWindowScrollSkin, i => _rows.ElementAtOrDefault(i).Item.Object, DrawRow, draggable: true);
        _fleets.Activated = i => { if (!_rows[i].Ship) { if (!_expanded.Remove(_rows[i].Item.Object)) _expanded.Add(_rows[i].Item.Object); Refresh(); } };
        _fleets.SelectionChanged = () => { KeepPlayerSide(); _list.Selected.Clear(); _list.First = 0; Refresh(); };
        _list = List(new Rect2(101, 127, 133, 164), new Vector2(125, 50), StrategyRcData.FleetWindowScrollSkin, i => _contents.ElementAtOrDefault(i).Object, DrawContents, draggable: true);
        for (int t = 0; t < 4; t++)
        {
            int tab = ControlIds.FleetShipsTab + t;
            _tabs.Add(Tab(99 + 1 + 32 * t, 96, 31, 28, 0, 0, new[] { StrategyTexts.FleetShips, StrategyTexts.FleetFighterSquadrons, StrategyTexts.FleetTroopRegiments, StrategyTexts.FleetPersonnel }[t], () => { _tab = tab; _list.Selected.Clear(); _list.First = 0; Refresh(); }));
        }
        Active = _fleets;
        Refresh();
        if (_rows.Count > 0) _fleets.Select(0); // the first fleet is selected at open (observed in the original)
    }

    protected override GameObject? ItemOf(CellList list, int i) =>
        list == _fleets ? _rows.ElementAtOrDefault(i).Item.Object : _contents.ElementAtOrDefault(i).Object;

    /// <summary>Skin: no fleet gives the system's view owner; else the player side if a fleet's view owner is it, else the enemy side.</summary>
    protected override SimOwner SkinOwner =>
        !_rows.Any(r => !r.Ship) ? base.SkinOwner : _rows.Any(r => !r.Ship && r.Item.Owner == Side) ? Side : Side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;

    List<GameObject> SelectedFleets => [.. _fleets.Selected.Select(i => _rows.ElementAtOrDefault(i)).Where(r => r.Item.Object is Fleet).Select(r => r.Item.Object)];

    /// <summary>The selected rows' objects: fleets and capital ships (contents).</summary>
    List<GameObject> SelectedRows => [.. _fleets.Selected.Where(i => i < _rows.Count).Select(i => _rows[i].Item.Object)];

    /// <summary>Selection rule: with a player-side row selected, the other sides' selected rows are deselected.</summary>
    void KeepPlayerSide()
    {
        if (_fleets.Selected.Any(i => i < _rows.Count && _rows[i].Item.Owner == Side))
            _fleets.Selected.RemoveWhere(i => i >= _rows.Count || _rows[i].Item.Owner != Side);
    }

    public override void Refresh()
    {
        if (Screen.Game is null || _tabs.Count == 0) return;
        var selected = SelectedRows;
        _rows = [];
        foreach (var f in ScreenViews.Fleets(Game, Sys, Side))
        {
            _rows.Add((f, false));
            if (_expanded.Contains(f.Object)) _rows.AddRange(ScreenViews.Ships(Game, f.Object, Side).Select(x => (x, true)));
        }
        _fleets.Count = _rows.Count;
        _masks = [.. _rows.Select(r => ScreenViews.PresenceMask(Game, r.Item.Object, Side))];
        _fleets.Selected.Clear();
        foreach (var f in selected) if (_rows.FindIndex(r => r.Item.Object == f) is var i && i >= 0) _fleets.Selected.Add(i);
        // openswr: the selected fleets left while others remain: the first fleet, as at open
        if (selected.Count > 0 && _fleets.Selected.Count == 0 && _rows.FindIndex(r => !r.Ship) is var first && first >= 0)
        {
            _fleets.Selected.Add(first);
            _list.Selected.Clear();
            _list.First = 0;
        }
        // tab enabling: the OR of the selected rows' masks; a single selected ship row moves an active Ships tab to the fighters tab
        var mask = _fleets.Selected.Where(i => i < _masks.Length).Aggregate(Presence.None, (m, i) => m.Or(_masks[i]));
        if (_tab == ControlIds.FleetShipsTab && _fleets.Selected.Count == 1 && _rows[_fleets.Selected.Min].Ship) _tab = ControlIds.FleetFightersTab;
        // Ships tab: the selected fleet's capital ships in alphabetical order of their names (observed; ties keep key order)
        _contents = ScreenViews.FleetContents(Game, SelectedRows, _tab, Side);
        if (_tab == ControlIds.FleetShipsTab) _contents = [.. _contents.OrderBy(v => Screen.ObjectName(v.Object), StringComparer.OrdinalIgnoreCase)];
        _contentMasks = [.. _contents.Select(v => Family.IsCapitalShip(v.Object.Type) ? ScreenViews.PresenceMask(Game, v.Object, Side) : Presence.None)];
        _list.Count = _contents.Count;
        bool one = PanelSide == SimOwner.Alliance; // the tab group of the panel side (Inferred)
        // tabs disabled (state 64) without the Fighters / Troops / Characters presence flag: the greyed up image (up + 3: 10413-10415 / 10463-10465 by viewing; Inferred)
        for (int t = 0; t < 4; t++)
        {
            var b = _tabs[t];
            bool off = t > 0 && !mask.Includes(TabPresence[t - 1]);
            int up = (one ? StrategyBitmaps.FleetTabAllianceUpBase : StrategyBitmaps.FleetTabEmpireUpBase) + t + (off ? 3 : 0), down = (one ? StrategyBitmaps.FleetTabAllianceDownBase : StrategyBitmaps.FleetTabEmpireDownBase) + t;
            (b.Up, b.Down, b.CheckedImage) = (Bitmap(false, up), Bitmap(false, down), Bitmap(false, down));
            b.Disabled = off;
            b.Checked = ControlIds.FleetShipsTab + t == _tab;
            b.Visible = _fleets.Selected.Count > 0;
        }
        _list.Visible = _fleets.Selected.Count > 0; // openswr: no detail panel without a selection (every fleet gone)
        _fleets.QueueRedraw();
        _list.QueueRedraw();
        QueueRedraw();
    }

    public override void SelectObject(GameObject o)
    {
        var fleet = o is Fleet ? o : o.Container is Fleet f ? f : o.Container?.Container is Fleet g ? g : null;
        if (fleet is null) return;
        if (o != fleet) _expanded.Add(fleet);
        Refresh();
        _fleets.Select(_rows.FindIndex(r => r.Item.Object == fleet));
        Active = _fleets;
        if (o == fleet) return;
        int tab = Family.IsCapitalShip(o.Type) ? ControlIds.FleetShipsTab : Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters ? ControlIds.FleetFightersTab : Family.IsTroop(o.Type) ? ControlIds.FleetTroopsTab : ControlIds.FleetPersonnelTab;
        _tab = tab;
        Refresh();
        _list.Select(_contents.FindIndex(v => v.Object == o));
        Active = _list;
    }

    /// <summary>T_dest: the cell under the point in either list, else the system; T_move: a fleet-list cell, a contents cell only on the Ships tab, else the system.</summary>
    public override GameObject? DestAt(Vector2 local) => CellAt(local) ?? Sys;
    public override GameObject? MoveAt(Vector2 local)
    {
        if (new Rect2(_fleets.Position, _fleets.Size).HasPoint(local) && _fleets.At(local - _fleets.Position) is var i && i >= 0) return _rows[i].Item.Object;
        if (_tab == ControlIds.FleetShipsTab && new Rect2(_list.Position, _list.Size).HasPoint(local) && _list.At(local - _list.Position) is var j && j >= 0) return _contents[j].Object;
        // openswr deviation: elsewhere in the contents list (the fighter / troop / personnel tabs) the single selected fleet, not the system
        if (new Rect2(_list.Position, _list.Size).HasPoint(local) && SelectedFleets is [var fleet]) return fleet;
        return Sys;
    }

    /// <summary>
    /// Fleet-list row (91 x 50): fleet cell = picture (overlays first) at (5, 5), dotted white
    /// connector (2, 6)-(5, 6), expanded (2, 6)-(6, 6) and (2, 6)-(2, 50); ship row = picture at (5, 15), connector (2, 0)-(2, 50)
    /// with a following ship row, else (2, 0)-(2, 6) and (2, 6)-(5, 6); then the name, the presence icons at y 33 and, selected,
    /// 10401 / 10451 (by owner) over it at (0, 0).
    /// </summary>
    void DrawRow(CellList l, int i, Rect2 r)
    {
        var (v, ship) = _rows[i];
        var p = r.Position;
        DrawLayers(l, v, p + (ship ? new Vector2(5, 15) : new Vector2(5, 5)));
        void Dots(int x0, int y0, int x1, int y1) => Dotted(l, p + new Vector2(x0, y0), p + new Vector2(x1, y1));
        if (!ship)
        {
            bool expanded = _expanded.Contains(v.Object);
            Dots(2, 6, expanded ? 6 : 5, 6);
            if (expanded) Dots(2, 6, 2, 50);
        }
        else if (i + 1 < _rows.Count && _rows[i + 1].Ship) Dots(2, 0, 2, 50);
        else { Dots(2, 0, 2, 6); Dots(2, 6, 5, 6); }
        // shortcut: the name's rect is not specced in the original. Ship rows put the name above the picture, fleets put it under. Icons are drawn over.
        var name = Screen.ObjectName(v.Object);
        var text = ship ? new Rect2(p.X, p.Y, r.Size.X, 15) : new Rect2(p.X, p.Y + 30, r.Size.X, 20);
        Assets.DrawCellText(l, text, name, 10, Colors.White, Assets.TextWidth(10, name) > r.Size.X - 5 ? TextFormat.Row : TextFormat.CentredRow);
        DrawPresence(l, p, _masks.ElementAtOrDefault(i), v.Owner, 33);
        if (l.Selected.Contains(i)) DrawBitmap(l, v.Owner == SimOwner.Alliance ? StrategyBitmaps.FleetRowSelectedAlliance : StrategyBitmaps.FleetRowSelectedEmpire, p);
    }

    /// <summary>Contents cell (125 x 50): picture at (28, 4), a capital ship's presence icons at y 23, the name under it; selected: 10420 / 10470 (panel side) at (1, 1).</summary>
    void DrawContents(CellList l, int i, Rect2 r)
    {
        var v = _contents[i];
        var p = r.Position;
        DrawLayers(l, v, p + new Vector2(28, 4));
        var name = Screen.ObjectName(v.Object);
        Assets.DrawCellText(l, new Rect2(p.X, p.Y + 34, r.Size.X, 16), name, 9, Colors.White, Assets.TextWidth(9, name) > r.Size.X - 5 ? TextFormat.Row : TextFormat.CentredRow);
        DrawPresence(l, p, _contentMasks.ElementAtOrDefault(i), v.Owner, 23);
        if (l.Selected.Contains(i)) DrawBitmap(l, PanelSide == SimOwner.Alliance ? StrategyBitmaps.ContentsSelectedAlliance : StrategyBitmaps.ContentsSelectedEmpire, p + new Vector2(1, 1));
    }

    /// <summary>Panel side (for the cover): the player side when a player-side row is selected, else the other side.</summary>
    SimOwner PanelSide => _fleets.Selected.Any(i => i < _rows.Count && _rows[i].Item.Owner == Side) ? Side : Side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;

    void DrawLayers(CanvasItem c, ViewObject v, Vector2 at)
    {
        foreach (var (t, size) in ModArt.Layers(Game, v.Object, ScreenViews.Picture(Game, v, Side, small: false, overlays: true)))
        {
            c.DrawTextureRect(t, new Rect2(at, size), false);
            DrawLog.Bitmap(c, t, new Rect2(at, size));
        }
    }

    static void DrawPresence(CanvasItem c, Vector2 cell, Presence mask, SimOwner owner, int y)
    {
        foreach (var (id, x) in ScreenViews.PresenceIcons(mask, owner)) DrawBitmap(c, id, cell + new Vector2(x, y));
    }

    static void DrawBitmap(CanvasItem c, int id, Vector2 at)
    {
        if (Bitmap(false, id) is not { } t) return;
        var r = new Rect2(at, Assets.BitmapSize((ushort)id, "STRATEGY.DLL"));
        c.DrawTextureRect(t, r, false);
        DrawLog.Bitmap(c, t, r);
    }

    /// <summary>1-px dotted white pen (PS_DOT), end point exclusive as GDI.</summary>
    static void Dotted(CanvasItem c, Vector2 a, Vector2 b)
    {
        var half = new Vector2(0.5f, 0.5f);
        c.DrawDashedLine(a + half, b + half - (b - a).Normalized(), Colors.White, 1, 1);
        DrawLog.Segment(c, a, b, Colors.White);
    }

    static readonly Dictionary<int, Texture2D?> Panels = [];

    /// <summary>
    /// Cover (WM_PAINT): with a selection, the panel side's 132 x 266 panel (A 10407 / E 10457) opaque at
    /// (97, 29); then the info picture at (100, 42): one selected ship row gives its 122 x 50 picture, else selected fleets give 10425 /
    /// 10475 by panel side, centred, y 0, with the last selected fleet's Enroute / Damaged overlays first. Without a selection the
    /// background 10770 stays whole.
    /// </summary>
    protected override void DrawCover()
    {
        if (_fleets is null || _fleets.Selected.Count == 0) return;
        bool a = PanelSide == SimOwner.Alliance;
        int id = a ? StrategyBitmaps.FleetPanelAlliance : StrategyBitmaps.FleetPanelEmpire;
        if (!Panels.TryGetValue(id, out var panel))
            Panels[id] = panel = Assets.Indexed((ushort)id, "STRATEGY.DLL") is { } b ? Assets.Texture(b, -1) : null;
        if (panel is not null)
        {
            DrawTextureRect(panel, new Rect2(97, 29, 132, 266), false);
            DrawLog.Bitmap(this, panel, new Rect2(97, 29, 132, 266));
        }
        if (_fleets.Selected.Count == 1 && _fleets.Selected.Min < _rows.Count && _rows[_fleets.Selected.Min] is { Ship: true } row)
        {
            foreach (var (t, size) in ModArt.Layers(Game, row.Item.Object, ScreenViews.Picture(Game, row.Item, Side, small: true, overlays: true)))
            {
                DrawTextureRect(t, new Rect2(100, 42, size), false);
                DrawLog.Bitmap(this, t, new Rect2(100, 42, size));
            }
            return;
        }
        var fleets = SelectedFleets;
        if (fleets.Count == 0) return;
        var v = ScreenViews.View(Game, fleets[^1], Side);
        foreach (var (pic, on) in new[] { (a ? StrategyBitmaps.FleetEnrouteOverlayAlliance : StrategyBitmaps.FleetEnrouteOverlayEmpire, v.Enroute), (a ? StrategyBitmaps.FleetDamagedOverlayAlliance : StrategyBitmaps.FleetDamagedOverlayEmpire, v.Damaged), (a ? StrategyBitmaps.FleetPictureAlliance : StrategyBitmaps.FleetPictureEmpire, true) })
            if (on) DrawBitmap(this, pic, new Vector2(100 + (125 - Assets.BitmapSize((ushort)pic, "STRATEGY.DLL").X) / 2, 42));
    }

    /// <summary>Info text: the name of the single selected fleet or ship, (100, 29) 128 x 25, font 5, left / top, clipped to its fixed size.</summary>
    public override void _Draw()
    {
        base._Draw();
        if (_fleets.Selected.Count != 1 || _fleets.Selected.Min >= _rows.Count) return;
        Assets.DrawTextItem(this, new Rect2(100, 29, 128, 25), Screen.ObjectName(_rows[_fleets.Selected.Min].Item.Object), 5, Colors.White, TextFormat.TopLeft, TextItemSize.FixedClipped);
    }
}

/// <summary>
/// System missions window (235 x 304, background 11165): mission list 10 at (5, 24) 94 x 275, cells 90 x 50, skin
/// 10748, first entry selected at open (both owners' missions of the agents at the system); name (109, 25) and description
/// (109, 91) (Inferred: the mission's target and phase); tabs Agents / Decoys (11560 / 11561, 11562 / 11563) at (105, 127);
/// member list at (107, 145) 117 x 147, cells 115 x 43, not draggable. Right click on a mission opens the Abort command.
/// </summary>
public partial class MissionsWindow : SystemWindow
{
    List<Mission> _missions = [];
    List<Agent> _members = [];
    bool _decoys;
    CellList _list = null!, _team = null!;
    FrontButton _agents = null!, _decoyTab = null!;

    public MissionsWindow(MainScreen screen, WindowAddress id, StarSystem s) : base(screen, id, s, StrategyBitmaps.MissionsWindowBackground, new Vector2(235, 304)) { }

    public override ushort Icon => StrategyBitmaps.MissionsIconAlliance;

    /// <summary>Skin side: the side of the missions there, not the view owner.</summary>
    protected override SimOwner SkinOwner => ScreenViews.MissionSide(Game, Sys, Side);

    public override void _Ready()
    {
        base._Ready();
        _list = List(new Rect2(5, 24, 94, 275), new Vector2(90, 50), StrategyRcData.MissionListScrollSkin, i => _missions.ElementAtOrDefault(i), (l, i, r) =>
        {
            // cell = the mission picture only; the name is the text at (109, 25)
            DrawPicture(l, ScreenViews.View(Game, _missions[i], Side), r, small: false);
            if (l.Selected.Contains(i)) { l.DrawRect(r.Grow(-1), Colors.White, filled: false); DrawLog.Frame(l, r.Grow(-1), Colors.White); }
        });
        _list.SelectionChanged = Refresh;
        _team = List(new Rect2(107, 145, 117, 147), new Vector2(115, 43), 0, i => _members.ElementAtOrDefault(i), (l, i, r) =>
            DrawObjectCell(l, i, r, ScreenViews.View(Game, _members[i], Side), small: false, 26));
        _agents = Tab(105, 127, 61, 16, StrategyBitmaps.AgentsTabUp, StrategyBitmaps.AgentsTabDown, StrategyTexts.AgentsTab, () => { _decoys = false; Refresh(); });
        _decoyTab = Tab(105 + 61, 127, 61, 16, StrategyBitmaps.DecoysTabUp, StrategyBitmaps.DecoysTabDown, StrategyTexts.DecoysTab, () => { _decoys = true; Refresh(); });
        Active = _list;
        Refresh();
        if (_missions.Count > 0) _list.Select(0);
    }

    protected override GameObject? ItemOf(CellList list, int i) => list == _list ? _missions.ElementAtOrDefault(i) : _members.ElementAtOrDefault(i);

    Mission? Current => _list.Selected.Count == 1 ? _missions.ElementAtOrDefault(_list.Selected.Min) : null;

    public override void Refresh()
    {
        if (Screen.Game is null || _agents is null) return;
        var keep = Current;
        _missions = ScreenViews.Missions(Game, Sys, Side);
        _list.Count = _missions.Count;
        if (keep is not null && _missions.IndexOf(keep) is var k && k >= 0) { _list.Selected.Clear(); _list.Selected.Add(k); }
        _members = Current is { } m ? ScreenViews.MissionMembers(m, _decoys) : [];
        _team.Count = _members.Count;
        (_agents.Checked, _decoyTab.Checked) = (!_decoys, _decoys);
        _list.QueueRedraw();
        _team.QueueRedraw();
        QueueRedraw();
    }

    public override void SelectObject(GameObject o)
    {
        int i = o is Mission m ? _missions.IndexOf(m) : _missions.FindIndex(m => m.Members.Contains(o));
        if (i >= 0) _list.Select(i);
    }

    public override void _Draw()
    {
        base._Draw();
        if (Current is not { } m) return;
        Assets.DrawText(this, new Rect2(109, 25, 113, 16), Screen.ObjectName(m), 5, Colors.White, TextFormat.CentredRow);
        string target = m.Objective is { } o ? Screen.ObjectName(o) : "";
        Assets.DrawText(this, new Rect2(109, 91, 113, 32), target, 10, Colors.White, TextFormat.CentredRow);
    }
}

/// <summary>
/// System manufacturing window (226 x 304, background 10297): tabs Manufacturing (three manager rows: title,
/// destination, queue count + product picture or "No ... being built", progress bar = points / cost of the current unit) and the
/// facility tabs (cells 69 x 40, facility pictures by kind and state, Mines tab with empty deposit cells 9005 up to the raw-material
/// count). A row click selects the manager (Ctrl toggles); a second click on the selected shipyard row's product renames (rename command);
/// rows drag to a destination (the destination command); right click opens the manager menu.
/// </summary>
public partial class ManufacturingWindow : SystemWindow
{
    static readonly (int Id, int X, int Tip, uint Lo, uint Hi, int Has, int None, int Down)[] Tabs =
    [
        (ControlIds.ShipyardsTab, 39, StrategyTexts.ShipyardsTab, Families.Shipyard, Families.TrainingFacility, StrategyBitmaps.ShipyardsTabUp, StrategyBitmaps.ShipyardsTabNone, StrategyBitmaps.ShipyardsTabDown),
        (ControlIds.TrainingFacilitiesTab, 77, StrategyTexts.TrainingFacilitiesTab, Families.TrainingFacility, Families.ConstructionYard, StrategyBitmaps.TrainingFacilitiesTabUp, StrategyBitmaps.TrainingFacilitiesTabNone, StrategyBitmaps.TrainingFacilitiesTabDown),
        (ControlIds.ConstructionYardsTab, 115, StrategyTexts.ConstructionYardsTab, Families.ConstructionYard, Families.ManufacturingEnd, StrategyBitmaps.ConstructionYardsTabUp, StrategyBitmaps.ConstructionYardsTabNone, StrategyBitmaps.ConstructionYardsTabDown),
        (ControlIds.RefineriesTab, 152, StrategyTexts.RefineriesTab, Families.Refinery, Families.RefineryEnd, StrategyBitmaps.RefineriesTabUp, StrategyBitmaps.RefineriesTabNone, StrategyBitmaps.RefineriesTabDown),
        (ControlIds.MinesTab, 190, StrategyTexts.MinesTab, Families.Mine, Families.Refinery, StrategyBitmaps.MinesTabUp, StrategyBitmaps.MinesTabNone, StrategyBitmaps.MinesTabDown),
    ];
    int _tab = ControlIds.ManufacturingTab;
    List<ManufacturingManager> _managers = [];
    readonly List<ManufacturingManager> _selected = [];
    List<ViewObject> _items = [];
    int _deposits;
    CellList _list = null!;
    FrontButton _manufacturing = null!;
    readonly List<FrontButton> _tabs = [];
    Vector2? _press;
    bool _dragging;

    public ManufacturingWindow(MainScreen screen, WindowAddress id, StarSystem s) : base(screen, id, s, StrategyBitmaps.ManufacturingWindowBackground, new Vector2(226, 304)) { }

    public override ushort Icon => StrategyBitmaps.FacilitiesIconAlliance;

    public override void _Ready()
    {
        base._Ready();
        _list = List(new Rect2(8, 77, 222 - 4, 225), new Vector2(69, 40), 0, i => _items.ElementAtOrDefault(i).Object, (l, i, r) =>
        {
            if (i >= _items.Count) CellList.Centred(l, Bitmap(false, StrategyBitmaps.EmptyDeposit), new Vector2(67, 35), r);
            else
            {
                int pic = ScreenViews.FacilityPicture(_items[i]);
                CellList.Centred(l, Bitmap(false, pic), new Vector2(67, 35), r);
                // selected image: the owner-coloured frame over the centred picture (selected state, Inferred)
                if (l.Selected.Contains(i)) CellList.Centred(l, Bitmap(false, SkinOwner switch { SimOwner.Alliance => StrategyBitmaps.FacilitySelectedAlliance, SimOwner.Empire => StrategyBitmaps.FacilitySelectedEmpire, _ => StrategyBitmaps.FacilitySelectedNeutral }), new Vector2(67, 35), r);
            }
        });
        _manufacturing = Tab(0, 20, 36, 33, 0, 0, StrategyTexts.Manufacturing, () => SelectTab(ControlIds.ManufacturingTab));
        foreach (var t in Tabs)
        {
            int id = t.Id;
            _tabs.Add(Tab(t.X, 20, 36, 33, t.Has, t.Down, t.Tip, () => SelectTab(id)));
        }
        SelectTab(ControlIds.ManufacturingTab);
    }

    protected override GameObject? ItemOf(CellList list, int i) => _items.ElementAtOrDefault(i).Object;

    void SelectTab(int tab)
    {
        _tab = tab;
        _list.Visible = tab != ControlIds.ManufacturingTab;
        _list.Selected.Clear();
        _list.First = 0;
        Active = tab == ControlIds.ManufacturingTab ? null : _list;
        Refresh();
    }

    public override void Refresh()
    {
        if (Screen.Game is null || _tabs.Count == 0) return;
        _managers = ScreenViews.ManagerRows(Sys);
        _selected.RemoveAll(m => !_managers.Contains(m));
        var owner = Knowledge.See(Game, Sys, Side).Owner;
        int up = owner switch { SimOwner.Alliance => StrategyBitmaps.ManufacturingTabAllianceUp, SimOwner.Empire => StrategyBitmaps.ManufacturingTabEmpireUp, _ => StrategyBitmaps.ManufacturingTabNeutralUp }, down = up - 1;
        (_manufacturing.Up, _manufacturing.Down, _manufacturing.CheckedImage) = (Bitmap(false, up), Bitmap(false, down), Bitmap(false, down));
        _manufacturing.Checked = _tab == ControlIds.ManufacturingTab;
        for (int i = 0; i < Tabs.Length; i++)
        {
            var t = Tabs[i];
            bool has = ScreenViews.Facilities(Game, Sys, t.Lo, t.Hi, Side).Count > 0;
            _tabs[i].Up = Bitmap(false, has ? t.Has : t.None);
            _tabs[i].Checked = t.Id == _tab;
        }
        if (_tab != ControlIds.ManufacturingTab)
        {
            var t = Tabs.First(x => x.Id == _tab);
            _items = ScreenViews.Facilities(Game, Sys, t.Lo, t.Hi, Side);
            _deposits = _tab == ControlIds.MinesTab ? Math.Max(0, Sys.RawMaterials - _items.Count) : 0; // raw material: the real value (views keep none, Inferred)
            _list.Count = _items.Count + _deposits;
        }
        _list.QueueRedraw();
        QueueRedraw();
    }

    static Rect2 Row(int k) => new(8, 57 + 81 * k, 222, 81);

    // panel origin of row k: header 10291... drawn at (55, 56 + 81k), its key column / row 0 = the white outline (player screenshot; Inferred)
    static Vector2 Panel(int k) => new(55, 56 + 81 * k);

    // product picture (61 x 25) right of the "Building" line (player screenshot; the (40, 15) spot overlaps the text; Inferred)
    static Rect2 Product(int k) => new(Panel(k) + new Vector2(95, 28), new Vector2(61, 25));

    public override List<(GameObject Object, GroupMode Mode)> Selection =>
        _tab == ControlIds.ManufacturingTab ? [.. _selected.Select(m => ((GameObject)m, GroupMode.Object))] : base.Selection;

    public override void SelectObject(GameObject o)
    {
        if (o is ManufacturingManager m) { SelectTab(ControlIds.ManufacturingTab); _selected.Clear(); _selected.Add(m); QueueRedraw(); return; }
        foreach (var t in Tabs)
            if (ScreenViews.Facilities(Game, Sys, t.Lo, t.Hi, Side).FindIndex(v => v.Object == o) is var i && i >= 0)
            {
                SelectTab(t.Id);
                _list.Select(i);
                return;
            }
    }

    ManufacturingManager? RowAt(Vector2 p)
    {
        for (int k = 0; k < _managers.Count; k++) if (Row(k).HasPoint(p)) return _managers[k];
        return null;
    }

    protected override void WindowInput(InputEvent e)
    {
        if (_tab != ControlIds.ManufacturingTab) return;
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right } b when RowAt(b.Position) is { } m:
                if (b.ButtonIndex == MouseButton.Left) (_press, _dragging) = (b.Position, false);
                // second click on the selected shipyard row's product name renames it (rename entry; own dialog)
                if (b.ButtonIndex == MouseButton.Left && !b.CtrlPressed && _selected is [var only] && only == m && m.Kind == ManufacturingKind.Ship
                    && Knowledge.IsLive(m, Side) && m.Orders.Count > 0 && Product(_managers.IndexOf(m)).HasPoint(b.Position))
                {
                    Screen.Dispatch((int)CommandType.SetProductName, [(m, GroupMode.Object)]);
                    break;
                }
                if (!b.CtrlPressed && !_selected.Contains(m)) { _selected.Clear(); _selected.Add(m); }
                QueueRedraw();
                break;
            case InputEventMouseMotion mm when _press is { } start && !_dragging && Input.IsMouseButtonPressed(MouseButton.Left)
                && new Rect2(8, 57, 222, 225).HasPoint(start) && RowAt(start) is { } pressed && _selected.Contains(pressed):
                _dragging = true;
                // drag image for 1 / 2 / 3 selected rows (A 10305-10307, E 10308-10310). Another side's rows drag too, and their destination command fails the owner check.
                int image = SkinOwner switch { SimOwner.Alliance => StrategyBitmaps.ManagerDragAlliance, SimOwner.Empire => StrategyBitmaps.ManagerDragEmpire, _ => 0 };
                Screen.BeginDrag(this, start + GetGlobalRect().Position, image == 0 ? null : Bitmap(false, image + Math.Min(_selected.Count, 3) - 1), new Vector2(36, 36));
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } up:
                if (!_dragging && _press is not null && up.CtrlPressed && RowAt(up.Position) is { } t)
                {
                    if (!_selected.Remove(t)) _selected.Add(t);
                    QueueRedraw();
                }
                (_press, _dragging) = (null, false);
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } r when RowAt(r.Position) is not null && _selected.Count > 0:
                Screen.OpenObjectMenu(Selection, GetGlobalMousePosition());
                break;
        }
    }

    public override void _Draw()
    {
        base._Draw();
        if (_tab != ControlIds.ManufacturingTab) return;
        var kinds = new[] { (StrategyTexts.ShipConstruction, StrategyTexts.NoShipsBuilt, StrategyTexts.Building), (StrategyTexts.TroopsInTraining, StrategyTexts.NoTroopsTraining, StrategyTexts.Training), (StrategyTexts.FacilitiesUnderConstruction, StrategyTexts.NoFacilitiesBuilt, StrategyTexts.Building) };
        // left strip 10298 (46 x 226, colour-keyed: per kind facility picture 46 x 46 + count box 46 x 16 at y 48, kinds at y 0 / 81 / 161)
        // with "active:total" of each facility type; position from a player screenshot, Inferred
        var strip = new Vector2(6, 72);
        if (Bitmap(false, StrategyBitmaps.FacilityCountStrip) is { } stripImage) { DrawTextureRect(stripImage, new Rect2(strip, new Vector2(46, 226)), false); DrawLog.Bitmap(this, stripImage, new Rect2(strip, new Vector2(46, 226))); }
        for (int kind = 0; kind < 3; kind++)
        {
            var facilities = ScreenViews.Facilities(Game, Sys, Families.Shipyard + (uint)kind, Families.TrainingFacility + (uint)kind, Side).Where(f => !f.Destroyed).ToList();
            Assets.DrawText(this, new Rect2(strip + new Vector2(0, new[] { 0, 81, 161 }[kind] + 48), new Vector2(46, 16)), $"{facilities.Count(f => f.Usable)}:{facilities.Count}", 10, Colors.White, TextFormat.CentredRow);
        }
        for (int k = 0; k < _managers.Count; k++)
        {
            var m = _managers[k];
            var p = Panel(k);
            int kind = m.Kind switch { ManufacturingKind.Ship => 0, ManufacturingKind.Training => 1, _ => 2 };
            var (title, empty, building) = kinds[kind];
            // openswr (player screenshot): black shadow 3 px right / below, white outline around header and box (cut top-right corner
            // like the header bitmap), box interior shows the background
            DrawRect(new Rect2(p + new Vector2(163, 3), new Vector2(3, 76)), Colors.Black);
            DrawLog.Fill(this, new Rect2(p + new Vector2(163, 3), new Vector2(3, 76)), Colors.Black);
            DrawRect(new Rect2(p + new Vector2(3, 76), new Vector2(163, 3)), Colors.Black);
            DrawLog.Fill(this, new Rect2(p + new Vector2(3, 76), new Vector2(163, 3)), Colors.Black);
            Vector2[] outline = [p + new Vector2(0.5f, 0.5f), p + new Vector2(156.5f, 0.5f), p + new Vector2(161.5f, 5.5f), p + new Vector2(161.5f, 75.5f), p + new Vector2(0.5f, 75.5f), p + new Vector2(0.5f, 0.5f)];
            DrawPolyline(outline, Colors.White);
            DrawLog.Polyline(this, outline, Colors.White);
            DrawLine(p + new Vector2(0, 13.5f), p + new Vector2(162, 13.5f), Colors.White);
            DrawLog.Segment(this, p + new Vector2(0, 13.5f), p + new Vector2(162, 13.5f), Colors.White);
            // header bar 162 x 13, the first image while the row is selected
            int header = SkinOwner switch { SimOwner.Alliance => StrategyBitmaps.ManagerHeaderAlliance, SimOwner.Empire => StrategyBitmaps.ManagerHeaderEmpire, _ => StrategyBitmaps.ManagerHeaderNeutral } + (_selected.Contains(m) ? 0 : 1);
            if (Bitmap(false, header) is { } bar0) { DrawTextureRect(bar0, new Rect2(p, new Vector2(162, 13)), false); DrawLog.Bitmap(this, bar0, new Rect2(p, new Vector2(162, 13))); }
            Assets.DrawText(this, new Rect2(p + new Vector2(5, 1), new Vector2(155, 14)), Assets.Stra(title), 10, Colors.White, TextFormat.Row);
            // lines (player screenshot): "No ... being built" or the product name, then "Building: n", then "Destination: ..." above the bar
            // another side's manager: its frozen view (queue count, TargetKey); the view keeps no queue contents, so no product
            // name, picture or progress (Inferred)
            bool live = Knowledge.IsLive(m, Side);
            int count = Knowledge.QueueCount(Game, m, Side);
            if (count == 0)
                Assets.DrawText(this, new Rect2(p + new Vector2(5, 20), new Vector2(155, 14)), Assets.Stra(empty), 10, Colors.White, TextFormat.Row);
            else if (!live)
                Assets.DrawText(this, new Rect2(p + new Vector2(5, 49), new Vector2(90, 14)), Assets.Stra(building) + count, 10, Colors.White, TextFormat.Row);
            else
            {
                var v = ScreenViews.View(Game, m.Orders[0], Side) with { Completed = true };
                Assets.DrawText(this, new Rect2(p + new Vector2(5, 20), new Vector2(155, 14)), Screen.ObjectName(m.Orders[0]), 10, Colors.White, TextFormat.Row);
                Assets.DrawText(this, new Rect2(p + new Vector2(5, 49), new Vector2(90, 14)), Assets.Stra(building) + m.Orders.Count, 10, Colors.White, TextFormat.Row);
                // product picture (large form)
                DrawPicture(this, v, Product(k), small: false);
            }
            Assets.DrawText(this, new Rect2(p + new Vector2(5, 59), new Vector2(155, 14)), StrategyText.Destination + (Knowledge.TargetKey(m, Side) is { } target ? Screen.ObjectName(target) : ""), 10, Colors.White, TextFormat.Row);
            // progress bar at (56, 127 / 208 / 289), 160 x 4: black track, fill = points of the current unit / its cost
            // (fill colour from the player screenshot)
            var bar = new Rect2(56, 127 + 81 * k, 160, 4);
            DrawRect(bar, Colors.Black);
            DrawLog.Fill(this, bar, Colors.Black);
            int max = !live || m.Orders.Count == 0 ? 0 : Economy.Required(Game, m);
            if (max <= 0) continue;
            var done = new Rect2(bar.Position, new Vector2(bar.Size.X * Math.Clamp(m.CompletedPoints, 0, max) / max, 4));
            DrawRect(done, Color.Color8(255, 255, 110));
            DrawLog.Fill(this, done, Color.Color8(255, 255, 110));
        }
    }
}
