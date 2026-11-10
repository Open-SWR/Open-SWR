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
/// Mission window "Create Mission" (window id 0xC, 259 x 355, centred in the map rect): the end of the 0x240
/// targeting flow. Two pages on radio tabs 0x97 Mission / 0x98 Personnel (Left / Right switch). Mission page: type drop-down
/// over <see cref="MissionEligibility.WindowTypes"/> (passing types only, rank order, first selected), list button 0x68,
/// the target's picture and name (drawn once). Personnel page: Agents (the Team) and Decoys lists (cells 107 x 59, skin 10748,
/// multi-selection), 0xCA / 0xCB or a double click move the selected cells to the end of the other list (the type list is not
/// rebuilt). Begin Mission / Enter: the selected type into the command, validate + submit, window closes; Cancel / Close / Esc: 
/// nothing sent. Inferred: the member name under the picture (font 9).
/// </summary>
public partial class MissionWindow : GameWindow
{
    public const int WindowId = 0xC;
    readonly List<MissionRecord> _types;
    readonly List<Agent> _team, _decoys;
    readonly GameObject _target;
    int _page = 0x97;
    DropDownBox _box = null!;
    FrontButton _typeButton = null!, _toDecoys = null!, _toAgents = null!, _tabMission = null!, _tabPersonnel = null!;
    CellList _agents = null!, _decoyList = null!;

    public MissionWindow(MainScreen screen, List<MissionRecord> types, List<Agent> team, GameObject target) : base(screen, WindowId, new Vector2(259, 355))
    {
        (_types, _team, _decoys, _target) = (types, [.. team], [], target);
        var r = screen.MapRect;
        Position = (r.Position + (r.Size - Size) / 2).Floor();
    }

    protected override string Title => Assets.Stra(34055);

    static Texture2D? Bmp(int id, string module = "STRATEGY.DLL")
    {
        return id is <= 0 or > 0xFFFF ? null : Assets.TryBitmap((ushort)id, module);
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
        _tabMission = Add(A ? 11103 : 11105, A ? 11104 : 11106, 7, 20, 0, () => SetPage(0x97));
        _tabPersonnel = Add(A ? 11107 : 11109, A ? 11108 : 11110, 137, 20, 0, () => SetPage(0x98));
        _box = new DropDownBox { Position = new Vector2(35, 62), Size = new Vector2(200, 113), Count = _types.Count, DrawItem = DrawType, TooltipText = Assets.Tip(Assets.Stra(34080)),
            // Centred, word-wrapped, unclipped, font 4, white; item text offset (0, 0): the name at the cell top, centred,
            // word-wrapped, over the picture; own item colours grey / white; pop-up 200 x 117, one 113-px row
            ItemText = i => (Screen.DisplayName(_types[i].Common), 0), Format = new TextFormat { Center = true, WordBreak = true, NoClip = true }, Font = 4, Colour = Colors.White,
            RowColours = (Color.Color8(128, 128, 128), Colors.White), RowHeight = 113, VisibleRows = 1 };
        AddChild(_box);
        _typeButton = Add(10606, 10607, 101, 174, 34064, () => _box.Open());
        _agents = Members(new Vector2(8, 93), _team, true);
        _decoyList = Members(new Vector2(136, 93), _decoys, false);
        _toDecoys = Add(11117, 11118, 120, 136, 34053, () => Move(_agents, _team, _decoys));
        _toAgents = Add(11119, 11120, 120, 221, 34054, () => Move(_decoyList, _decoys, _team));
        Add(10592, 10593, 33, 320, 32817, () => Screen.OpenEncyclopedia(null, _types[_box.Selected].Common.Ref));
        Add(10594, 10595, 102, 320, 34050, Begin);
        Add(10596, 10597, 170, 320, 34051, () => Screen.Windows.Close(this));
        SetPage(0x97);
    }

    CellList Members(Vector2 at, List<Agent> list, bool agents)
    {
        var l = new CellList(10748) { Cell = new Vector2(107, 59), Position = at, Size = new Vector2(108, 213) };
        l.DrawCell = (c, i, r) => DrawMember(c, list[i], r, c.Selected.Contains(i));
        l.Pressed = () => Screen.Windows.Focus(this);
        l.Activated = i => // notification 0x309: the double-clicked cell moves (with the selection, Inferred)
        {
            l.Selected.Add(i);
            if (agents) Move(_agents, _team, _decoys);
            else Move(_decoyList, _decoys, _team);
        };
        AddChild(l);
        return l;
    }

    /// <summary>0xCA / 0xCB: the selected cells to the end of the other list; the command's lists follow; moved cells deselected.</summary>
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
        bool mission = page == 0x97;
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
        var command = new Command(0x240, Side)
        {
            Targets = [.. _team.Select(a => ((GameObject)a, 0))], Decoys = [.. _decoys], ArgRef = _target, Fields = [0, 0, _types[_box.Selected].Common.Ref],
        };
        Screen.Windows.Close(this);
        Screen.Submit(command);
    }

    public override bool HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.Enter or Key.KpEnter: Begin(); return true;
            case Key.Left or Key.Right: SetPage(_page == 0x97 ? 0x98 : 0x97); return true;
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
        SetPage(personnel ? 0x98 : 0x97);
        if (begin) Begin();
    }

    public void DemoOpenList() => _box.Open();

    void DrawType(CanvasItem c, int i, Rect2 r)
    {
        var t = _types[i];
        int form = A ? 0 : 0x1000;
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
        bool mission = _page == 0x97;
        var back = Bmp(mission ? 11100 : 11101)!;
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        int strip = A ? 10801 : 10802;
        var stripImage = Bmp(strip)!;
        DrawTextureRect(stripImage, new Rect2(new Vector2(2, 2), Dim(strip)), false);
        DrawLog.Bitmap(this, stripImage, new Rect2(new Vector2(2, 2), Dim(strip)));
        Assets.DrawText(this, new Rect2(5, 2, 230, 16), Title, 5, Colors.Black, TextFormat.Row);
        if (!mission)
        {
            int h1 = A ? 11121 : 11123, h2 = A ? 11122 : 11124;
            var (t1, t2) = (Bmp(h1)!, Bmp(h2)!);
            DrawTextureRect(t1, new Rect2(8, 65, 108, 27), false);
            DrawLog.Bitmap(this, t1, new Rect2(8, 65, 108, 27));
            DrawTextureRect(t2, new Rect2(136, 65, 108, 27), false);
            DrawLog.Bitmap(this, t2, new Rect2(136, 65, 108, 27));
            return;
        }
        Assets.DrawText(this, new Rect2(37, 195, 185, 14), Assets.Stra(34052), 4, Colors.White, TextFormat.Row);
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
