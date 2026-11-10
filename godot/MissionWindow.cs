using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Mission window "Create Mission" (259 x 355, centred in the map rect): the end of the mission
/// targeting flow. Two pages on radio tabs Mission / Personnel (Left / Right switch). Mission page: type drop-down
/// over <see cref="MissionEligibility.WindowTypes"/> (passing types only, rank order, first selected), list button,
/// the target's picture and name (drawn once). Personnel page: Agents (the Team) and Decoys lists (cells 107 x 59, skin 10748,
/// multi-selection), the move buttons or a double click move the selected cells to the end of the other list (the type list is not
/// rebuilt). Begin Mission / Enter: the selected type into the command, validate + submit, window closes; Cancel / Close / Esc: 
/// nothing sent. Inferred: the member name under the picture (font 9).
/// </summary>
public partial class MissionWindow : GameWindow
{
    public const WindowKind WindowId = WindowKind.Mission;
    readonly List<MissionRecord> _types;
    readonly List<Agent> _team, _decoys;
    readonly GameObject _target;
    int _page = ControlIds.MissionPage;
    DropDownBox _box = null!;
    FrontButton _typeButton = null!, _toDecoys = null!, _toAgents = null!, _tabMission = null!, _tabPersonnel = null!;
    CellList _agents = null!, _decoyList = null!;

    public MissionWindow(MainScreen screen, List<MissionRecord> types, List<Agent> team, GameObject target) : base(screen, WindowId, new Vector2(259, 355))
    {
        (_types, _team, _decoys, _target) = (types, [.. team], [], target);
        var r = screen.MapRect;
        Position = (r.Position + (r.Size - Size) / 2).Floor();
    }

    protected override string Title => StrategyText.CreateMission;

    static Texture2D? Bmp(int id, string module = "STRATEGY.DLL")
    {
        return id is <= 0 or > 65535 ? null : Assets.TryBitmap((ushort)id, module);
    }

    static Vector2 Dim(int id, string module = "STRATEGY.DLL") => Assets.BitmapSize((ushort)id, module);

    FrontButton Add(int up, int down, int x, int y, int tip, Action command)
    {
        var b = new FrontButton { Up = Bmp(up), Down = Bmp(down), Command = command, TooltipText = Assets.Tip(Assets.Stra(tip)) };
        b.CheckedImage = b.Down;
        var size = Dim(up);
        b.Place(x, y, (int)size.X, (int)size.Y);
        AddChild(b);
        return b;
    }

    public override void _Ready()
    {
        base._Ready();
        _tabMission = Add(A ? StrategyBitmaps.MissionTabAllianceUp : StrategyBitmaps.MissionTabEmpireUp, A ? StrategyBitmaps.MissionTabAllianceDown : StrategyBitmaps.MissionTabEmpireDown, 7, 20, 0, () => SetPage(ControlIds.MissionPage));
        _tabPersonnel = Add(A ? StrategyBitmaps.PersonnelTabAllianceUp : StrategyBitmaps.PersonnelTabEmpireUp, A ? StrategyBitmaps.PersonnelTabAllianceDown : StrategyBitmaps.PersonnelTabEmpireDown, 137, 20, 0, () => SetPage(ControlIds.PersonnelPage));
        _box = new DropDownBox { Position = new Vector2(35, 62), Size = new Vector2(200, 113), Count = _types.Count, DrawItem = DrawType, TooltipText = Assets.Tip(StrategyText.SelectedMission),
            // Centred, word-wrapped, unclipped, font 4, white; item text offset (0, 0): the name at the cell top, centred,
            // word-wrapped, over the picture; own item colours grey / white; pop-up 200 x 117, one 113-px row
            ItemText = i => (Screen.DisplayName(_types[i].Common), 0), Format = new TextFormat { Center = true, WordBreak = true, NoClip = true }, Font = 4, Colour = Colors.White,
            RowColours = (Color.Color8(128, 128, 128), Colors.White), RowHeight = 113, VisibleRows = 1 };
        AddChild(_box);
        _typeButton = Add(StrategyBitmaps.DropDownUp, StrategyBitmaps.DropDownDown, 101, 174, StrategyTexts.MissionTypes, () => _box.Open());
        _agents = Members(new Vector2(8, 93), _team, true);
        _decoyList = Members(new Vector2(136, 93), _decoys, false);
        _toDecoys = Add(StrategyBitmaps.ToDecoysUp, StrategyBitmaps.ToDecoysDown, 120, 136, StrategyTexts.AgentsToDecoys, () => Move(_agents, _team, _decoys));
        _toAgents = Add(StrategyBitmaps.ToAgentsUp, StrategyBitmaps.ToAgentsDown, 120, 221, StrategyTexts.DecoysToAgents, () => Move(_decoyList, _decoys, _team));
        Add(StrategyBitmaps.EncyclopediaUp, StrategyBitmaps.EncyclopediaDown, 33, 320, StrategyTexts.MessageEncyclopedia, () => Screen.OpenEncyclopedia(null, _types[_box.Selected].Common.Ref));
        Add(StrategyBitmaps.DoneUp, StrategyBitmaps.DoneDown, 102, 320, StrategyTexts.BeginMission, Begin);
        Add(StrategyBitmaps.CancelUp, StrategyBitmaps.CancelDown, 170, 320, StrategyTexts.MissionCancel, () => Screen.Windows.Close(this));
        SetPage(ControlIds.MissionPage);
    }

    CellList Members(Vector2 at, List<Agent> list, bool agents)
    {
        var l = new CellList(StrategyRcData.MissionListScrollSkin) { Cell = new Vector2(107, 59), Position = at, Size = new Vector2(108, 213) };
        l.DrawCell = (c, i, r) => DrawMember(c, list[i], r, c.Selected.Contains(i));
        l.Pressed = () => Screen.Windows.Focus(this);
        l.Activated = i => // double click: the double-clicked cell moves (with the selection, Inferred)
        {
            l.Selected.Add(i);
            if (agents) Move(_agents, _team, _decoys);
            else Move(_decoyList, _decoys, _team);
        };
        AddChild(l);
        return l;
    }

    /// <summary>Move buttons: the selected cells to the end of the other list; the command's lists follow; moved cells deselected.</summary>
    void Move(CellList from, List<Agent> source, List<Agent> into)
    {
        var moved = from.Selected.Where(i => i < source.Count).Select(i => source[i]).ToList();
        foreach (var a in moved) source.Remove(a);
        into.AddRange(moved);
        from.Selected.Clear();
        _agents.Count = _team.Count;
        _decoyList.Count = _decoys.Count;
        _agents.QueueRedraw();
        _decoyList.QueueRedraw();
    }

    void SetPage(int page)
    {
        _page = page;
        _box.Close();
        bool mission = page == ControlIds.MissionPage;
        (_box.Visible, _typeButton.Visible) = (mission, mission);
        (_agents.Visible, _decoyList.Visible, _toDecoys.Visible, _toAgents.Visible) = (!mission, !mission, !mission, !mission);
        (_tabMission.Checked, _tabPersonnel.Checked) = (mission, !mission);
        _agents.Count = _team.Count;
        _decoyList.Count = _decoys.Count;
        QueueRedraw();
    }

    /// <summary>Begin Mission / Enter: Team, Decoys, target and the selected type: validate + submit (no confirmation); the window closes.</summary>
    void Begin()
    {
        var command = new Command(CommandType.CreateMission, Side)
        {
            Targets = [.. _team.Select(a => ((GameObject)a, GroupMode.Object))], Decoys = [.. _decoys], ArgRef = _target, Fields = [0, 0, _types[_box.Selected].Common.Ref],
        };
        Screen.Windows.Close(this);
        Screen.Submit(command);
    }

    public override bool HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter: Begin(); return true;
            case Key.Left or Key.Right: SetPage(_page == ControlIds.MissionPage ? ControlIds.PersonnelPage : ControlIds.MissionPage); return true;
        }
        return false;
    }

    /// <summary>Headless demos: page, type (family), the first agent as decoy, Begin.</summary>
    public void Demo(bool personnel, uint family, bool decoy, bool begin)
    {
        if (_types.FindIndex(t => Family.Of(t.Common.Ref) == family) is var i and >= 0) _box.Select(i);
        if (decoy && _team.Count > 1)
        {
            _agents.Selected.Add(_team.Count - 1);
            Move(_agents, _team, _decoys);
        }
        SetPage(personnel ? ControlIds.PersonnelPage : ControlIds.MissionPage);
        if (begin) Begin();
    }

    public void DemoOpenList() => _box.Open();

    void DrawType(CanvasItem c, int i, Rect2 r)
    {
        var t = _types[i];
        int form = A ? 0 : PictureIds.GokresEmpireForm;
        if ((ModArt.Picture(Game.Definitions, t.Common.Ref, form) ?? ModArt.Install(true, ModArt.PictureNumber(t.Common.StringId) + form)) is var (pic, size))
            CellList.Centred(c, pic, size, r);
    }

    /// <summary>Object picture with overlays: agent / decoy cells pass small = 0, the target picture 1.</summary>
    void DrawLayers(CanvasItem c, GameObject o, Rect2 r, bool small = false)
    {
        foreach (var (t, size) in ModArt.Layers(Game, o, ScreenViews.Picture(Game, ScreenViews.View(Game, o, Side), Side, small, overlays: true)))
            CellList.Centred(c, t, size, r);
    }

    void DrawMember(CanvasItem c, Agent a, Rect2 r, bool selected)
    {
        DrawLayers(c, a, new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y - 14)));
        Assets.DrawCellText(c, new Rect2(r.Position.X, r.End.Y - 14, r.Size.X, 14), Screen.ObjectName(a), 9, Colors.White, TextFormat.CentredRow);
        if (selected) { c.DrawRect(r.Grow(-1), Colors.White, filled: false); DrawLog.Frame(c, r.Grow(-1), Colors.White); }
    }

    public override void _Draw()
    {
        bool mission = _page == ControlIds.MissionPage;
        var back = Bmp(mission ? StrategyBitmaps.MissionPageBackground : StrategyBitmaps.PersonnelPageBackground)!;
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        int strip = A ? StrategyBitmaps.ProductionTitleStripAlliance : StrategyBitmaps.ProductionTitleStripEmpire;
        var stripImage = Bmp(strip)!;
        DrawTextureRect(stripImage, new Rect2(new Vector2(2, 2), Dim(strip)), false);
        DrawLog.Bitmap(this, stripImage, new Rect2(new Vector2(2, 2), Dim(strip)));
        Assets.DrawText(this, new Rect2(5, 2, 230, 16), Title, 5, Colors.Black, TextFormat.Row);
        if (!mission)
        {
            int h1 = A ? StrategyBitmaps.AgentsHeaderAlliance : StrategyBitmaps.AgentsHeaderEmpire, h2 = A ? StrategyBitmaps.DecoysHeaderAlliance : StrategyBitmaps.DecoysHeaderEmpire;
            var (t1, t2) = (Bmp(h1)!, Bmp(h2)!);
            DrawTextureRect(t1, new Rect2(8, 65, 108, 27), false);
            DrawLog.Bitmap(this, t1, new Rect2(8, 65, 108, 27));
            DrawTextureRect(t2, new Rect2(136, 65, 108, 27), false);
            DrawLog.Bitmap(this, t2, new Rect2(136, 65, 108, 27));
            return;
        }
        Assets.DrawText(this, new Rect2(37, 195, 185, 14), StrategyText.MissionTarget, 4, Colors.White, TextFormat.Row);
        var area = new Rect2(51, 211, 165, 79);
        if (_target is StarSystem s)
        {
            var e = ScreenViews.SectorEntry(Game, s, Side);
            var (pic, size) = ModArt.SystemPicture(Game.Definitions, s, e.Picture, e.Destroyed);
            CellList.Centred(this, pic, size, area);
        }
        else DrawLayers(this, _target, area, small: true);
        Assets.DrawText(this, new Rect2(37, 294, 185, 16), Screen.ObjectName(_target), 5, Colors.White, TextFormat.Row);
    }
}
