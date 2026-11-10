using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Common part of the 470 x 331 battle windows: STRATEGY.DLL bitmaps (colour key 252) drawn into logical rects,
/// A / E variant by the viewing side, text colour A RGB(255, 0, 0) / E RGB(0, 255, 0), battle music from MDATA/MDATA.3xx (WAV)
/// while open. Placed centred in the main screen's map opening (Inferred). Keys come from the main screen (<see cref="HandleKey"/>).
/// </summary>
public abstract partial class BattleWindow : Control
{
    protected static readonly Color White = Colors.White, Black = Colors.Black;
    protected readonly bool A;
    protected Color Ink => A ? new Color(1, 0, 0) : new Color(0, 1, 0);
    readonly AudioStreamPlayer _music = new() { Bus = "Music" };
    readonly List<FrontButton> _tabs = [];
    protected int Tab { get; private set; }
    /// <summary>Runs once when the window closes (after it left the tree).</summary>
    public Action? Closed { get; set; }

    protected BattleWindow(bool alliance)
    {
        A = alliance;
        Position = new Vector2(52 + (558 - 470) / 2, 30 + (386 - 331) / 2); // centred in the frame's map opening (Inferred)
        Size = new Vector2(470, 331);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_music);
    }

    protected static Texture2D? Pic(int id)
    {
        return Assets.TryBitmap((ushort)id, "STRATEGY.DLL");
    }

    protected void Blit(int id, float x, float y, float w, float h)
    {
        if (Pic(id) is { } t) { DrawTextureRect(t, new Rect2(x, y, w, h), false); DrawLog.Bitmap(this, t, new Rect2(x, y, w, h)); }
    }

    /// <summary>Text with the 1-pixel drop shadow: black at (x + 1, y + 1), then the colour at (x, y).</summary>
    protected void Shadowed(Rect2 rect, string text, int font, Color colour, TextFormat format)
    {
        Assets.DrawText(this, new Rect2(rect.Position + Vector2.One, rect.Size), text, font, Black, format);
        Assets.DrawText(this, rect, text, font, colour, format);
    }

    /// <summary>
    /// A unit cell of the strike and battle-report lists: an
    /// empty-column text (font 6, top at y + 10, centred) or the viewer's picture centred with the name (font 10, white, centred,
    /// top at the picture's bottom y = h + (H - h) / 2), both by the cell-list rule (inset, clipped). <paramref name="small"/> = GOKRES
    /// picture (122 x 50) for units, else the personnel form (61 x 25).
    /// </summary>
    protected static void DrawUnitCell(CellList c, Rect2 r, GameState game, Func<SdCommon, string> name, SimOwner viewer, GameObject? unit, int text, bool small)
    {
        if (text != 0) Assets.DrawCellText(c, Line(r, 10, 6), Assets.Stra(text), 6, White, TextFormat.Centred);
        if (unit is null) return;
        var layers = ModArt.Layers(game, unit, ScreenViews.Picture(game, ScreenViews.View(game, unit, viewer), viewer, small, overlays: true));
        foreach (var (t, size) in layers) CellList.Centred(c, t, size, r);
        float h = layers.Count > 0 ? layers[0].Size.Y : 0;
        Assets.DrawCellText(c, Line(r, h + (r.Size.Y - h) / 2, 10), Label(game, name, unit), 10, White, TextFormat.Centred);
    }

    /// <summary>One top-aligned text line of the cell at y + dy.</summary>
    static Rect2 Line(Rect2 r, float dy, int font) => new(r.Position.X, r.Position.Y + dy, r.Size.X, Assets.LineHeight(font));

    string? _track;

    /// <summary>Starts the window's music once it is in the tree.</summary>
    protected void PlayMusic(string file) => _track = file;

    public override void _Ready()
    {
        if (_track is not { } file || !FrontSettings.MusicOn) return;
        try
        {
            _music.Stream = AudioStreamWav.LoadFromBuffer(File.ReadAllBytes(Path.Combine(Assets.GameDir, "MDATA", file)));
            _music.Play();
        }
        catch (IOException) { }
    }

    protected FrontButton Button(int x, int y, int w, int h, int up, int down, int disabled = 0, int tooltip = 0, Action? command = null)
    {
        var b = new FrontButton { Up = Pic(up), Down = Pic(down), DisabledImage = disabled == 0 ? null : Pic(disabled), Command = command };
        if (tooltip != 0) b.TooltipText = Assets.Tip(Assets.Stra(tooltip));
        b.Place(x, y, w, h);
        AddChild(b);
        return b;
    }

    /// <summary>Radio tab: the down image while selected.</summary>
    protected void AddTab(int x, int y, int w, int h, int up, int down, int tooltip)
    {
        int index = _tabs.Count;
        var b = Button(x, y, w, h, up, down, 0, tooltip, () => Select(index));
        b.CheckedImage = b.Down;
        b.Sound = Assets.Click; // battle-start 1-4, report 0x6F-0x72, strike 0x65-0x68
        _tabs.Add(b);
    }

    protected void Select(int index)
    {
        Tab = index;
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Checked = i == index;
        QueueRedraw();
    }

    protected void Close()
    {
        if (!IsInsideTree()) return;
        GetParent().RemoveChild(this);
        QueueFree();
        Closed?.Invoke();
    }

    /// <summary>Keys while open; true = consumed (the window takes every key: the strategy screen waits behind it).</summary>
    public virtual bool HandleKey(Key key)
    {
        if (_tabs.Count > 0 && key is Key.Left or Key.Right)
            Select((Tab + (key == Key.Left ? _tabs.Count - 1 : 1)) % _tabs.Count);
        return true;
    }

    /// <summary>Name of a unit, character or fleet from its definition record.</summary>
    protected static string Label(GameState game, Func<SdCommon, string> name, GameObject o) => o switch
    {
        Character c => name(c.Definition.Common),
        StarSystem s => name(s.Definition.Common),
        _ => game.Definitions.Resolve(o.Type) is { } r ? name(r.Common) : $"{o.Type:X8}",
    };

}

/// <summary>
/// Battle-start window (window type 6): frame 10710 / 10711 over picture 10712 / 10713 (summary, image 0x32) or
/// the dimmed 10714 / 10715 (lists, image 0x33); title template 0x7020, description 0x7021-0x7025 by owner / BattleReady (side
/// names by placeholder kind); tabs
/// summary / Alliance forces / Imperial forces / system assets; buttons retreat (enabled only when retreat is possible), simulate,
/// take command. Retreat answers 10000.0, simulate 0.0 (<see cref="OpenSWR.Sim.Ai.HumanController.BattleAnswer"/>), take command
/// answer 2 with float 0.0 (the tactical battle). No key shortcut: one of the buttons must be clicked.
/// List scroll bar: skin RCDATA 10748; mouse wheel or its buttons scroll.
/// </summary>
public partial class BattleStartWindow : BattleWindow
{
    readonly GameState _game;
    readonly StarSystem _system;
    readonly SimOwner _side;
    readonly Func<SdCommon, string> _name;
    readonly string _title, _description;
    readonly CellList _list;
    List<ScreenViews.BattleStartRow> _rows = [];

    public BattleStartWindow(GameState game, StarSystem s, SimOwner side, Func<SdCommon, string> name, bool retreat, Action<double> answer, Action takeCommand) : base(side == SimOwner.Alliance)
    {
        (_game, _system, _side, _name) = (game, s, side, name);
        var enemy = side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;
        object sys = Label(game, name, s), own = Assets.Side(A), other = Assets.Side(!A);
        SimOwner o = s.Owner, b = s.BattleReady;
        _title = Assets.Message(0x7020, sys);
        _description = o == side ? (b == side ? Assets.Message(0x7022, own, sys, other) : Assets.Message(0x7021, other, sys, own))
            : o is SimOwner.None or SimOwner.Neutral ? Assets.Message(0x7025, own, other, sys)
            : b == o ? Assets.Message(0x7024, other, own, sys) : Assets.Message(0x7023, own, sys, other);
        int[] ys = A ? [0, 60, 122, 184] : [0, 63, 126, 189];
        int[][] images = A ? [[10728, 10729], [10730, 10731], [10732, 10733], [10734, 10735]] : [[10738, 10739], [10740, 10741], [10742, 10743], [10744, 10745]];
        for (int i = 0; i < 4; i++)
            AddTab(A ? 418 : 426, (A ? 21 : 17) + ys[i], A ? 41 : 44, A ? 41 : 45, images[i][0], images[i][1], 6434 + i);
        void Answer(double f)
        {
            answer(f); // before Close: its Closed callback resumes the clock
            Close();
        }
        var r = A ? (10974, 10975, 10976) : (10971, 10972, 10973);
        Button(12, 296, 134, 27, r.Item1, r.Item2, r.Item3, 6474, () => Answer(10000.0)).Disabled = !retreat;
        var sim = A ? (10722, 10723, 10724) : (10716, 10717, 10718);
        Button(146, 296, 134, 27, sim.Item1, sim.Item2, sim.Item3, 6438, () => Answer(0.0));
        var tac = A ? (10725, 10726, 10727) : (10719, 10720, 10721);
        Button(280, 296, 134, 27, tac.Item1, tac.Item2, tac.Item3, 6439, () =>
        {
            takeCommand(); // before Close, like Answer
            Close();
        });
        // list: (83, 40) 256 x 234, cells 240 x 40, one row per cell at y = 40 * i; skin 10748; rebuilt per tab
        _list = new CellList(10748) { Position = new Vector2(83, 40), Size = new Vector2(256, 234), Cell = new Vector2(240, 40), DrawCell = DrawRow, Visible = false };
        AddChild(_list);
        Select(0);
        PlayMusic(A ? "MDATA.307" : "MDATA.313");
    }

    /// <summary>The demo path: show tab <paramref name="tab"/> (the window takes no keys).</summary>
    public void Demo(int tab) => Select(tab);

    /// <summary>The demo / test path: the button of the answer, clicked.</summary>
    public void Press(bool retreat) => GetChildren().OfType<FrontButton>().ElementAt(4 + (retreat ? 0 : 1)).Click();

    /// <summary>The demo path: take command clicked.</summary>
    public void TakeCommand() => GetChildren().OfType<FrontButton>().ElementAt(6).Click();

    /// <summary>
    /// A list row (<see cref="ScreenViews.BattleStartRows"/>): the picture (icon form p + 0x4000 with overlays, the
    /// player's view) blitted at the cell's top-left + (x, 0), not centred; the text by the cell-list rule (list text offset 1, item
    /// offset (x, 0)): centred rows format 1 ( +/- 2 inset), the others the list default 4 (left, top: no DT_SINGLELINE), clipped. Every
    /// row is in a state with no selected picture.
    /// </summary>
    void DrawRow(CellList c, int i, Rect2 r)
    {
        var row = _rows[i];
        if (row.PictureX >= 0 && row.Object is { } o)
        {
            var layers = ModArt.Layers(_game, o, ScreenViews.Picture(_game, ScreenViews.View(_game, o, _side), _side, small: false, overlays: true));
            if (layers.Count > 0)
            {
                var at = new Rect2(r.Position + new Vector2(row.PictureX, 0), layers[0].Size);
                foreach (var (t, size) in layers) CellList.Centred(c, t, size, at); // one composed picture (a mod PNG: the layers under it, then the PNG)
            }
        }
        string text = row.StringId != 0 ? Assets.Stra(row.StringId) : row.Object is Fleet f ? $"{Label(_game, _name, f)} {f.Id:X}" : Label(_game, _name, row.Object!);
        Assets.DrawCellText(c, new Rect2(r.Position + new Vector2(row.TextX, 0), r.Size), text, row.Font, White, row.Centred ? TextFormat.Centred : new TextFormat { VCenter = true });
    }

    public override void _Draw()
    {
        bool summary = Tab == 0;
        if (_list.Visible == summary || !summary && _shownTab != Tab)
        {
            (_list.Visible, _shownTab) = (!summary, Tab);
            _rows = summary ? [] : ScreenViews.BattleStartRows(_system, Tab);
            _list.Count = _rows.Count;
            _list.First = 0;
        }
        Blit(summary ? (A ? 10712 : 10713) : (A ? 10714 : 10715), 12, 13, 400, 310);
        Blit(A ? 10710 : 10711, 0, 0, 470, 331);
        if (summary)
        {
            Assets.DrawText(this, new Rect2(0, 16, 400, Assets.LineHeight(11)), _title, 11, Ink, TextFormat.Centred);
            Shadowed(new Rect2(36, 216, 328, 80), _description, 6, Ink, TextFormat.Wrap);
        }
    }

    int _shownTab = -1;

    /// <summary>No key shortcut: keys are swallowed while the window is open.</summary>
    public override bool HandleKey(Key key) => true;
}

/// <summary>
/// Post-battle report window (window type 8): frame 10335 / 10336, side strip 10820 / 10821, outcome picture
/// and music by BattleWon (w) and BattleWithdrew (m). Tabs: 0x6F image 1 (title, second line, summary), 0x70 /
/// 0x71 Alliance / Imperial units with the filter group (capital ships, fighters, troops: image 2 Operational / Destroyed;
/// personnel: image 3 Survivors / Captured / Killed), 0x72 image 4 with the go-to buttons (the fleet button only
/// with an own fleet there). Texts from templates 0x7001-0x7016 by the message builders. OK / Esc close (answer 3), Left / Right tabs.
/// Inferred: r = BattleReady; dest = the destination of the viewer's first unit that left (side-1 viewer only); page backgrounds
/// 10761 / 10762 / 10763; the rows are the units (and characters) present at the battle start, by fate; unit list scroll bar skin RCDATA 10653 (one offset for all columns, Inferred).
/// </summary>
public partial class BattleReportWindow : BattleWindow
{
    readonly GameState _game;
    readonly BattleReport _report;
    readonly Func<SdCommon, string> _name;
    readonly List<GameObject> _units;
    readonly int _picture;
    readonly string _title, _second, _body, _textAc, _textB8;
    readonly FrontButton _system;
    readonly FrontButton? _fleet;
    readonly List<FrontButton> _filters = [];
    readonly SimOwner _viewer;
    readonly CellList _list;
    readonly List<(GameObject? Unit, int Text)> _cells = [];
    int _filter, _shownTab = -1;

    public BattleReportWindow(GameState game, BattleReport report, SimOwner side, Func<SdCommon, string> name, List<GameObject> units,
        Action<StarSystem> goSystem, Fleet? fleet, Action<Fleet> goFleet) : base(side == SimOwner.Alliance)
    {
        (_game, _report, _name, _units, _viewer) = (game, report, name, units, side);
        _list = new CellList(10653) { Single = true, Position = new Vector2(37, 136), Size = new Vector2(347, 166), DrawCell = DrawCell, Visible = false };
        AddChild(_list);
        var x = report.System;
        SimOwner other = side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;
        SimOwner w = report.Won is SimOwner.Alliance or SimOwner.Empire ? report.Won : SimOwner.None, m = x.BattleWithdrew;
        SimOwner u = Knowledge.See(game, x, side).Owner, r = x.BattleReady;
        object own = Assets.Side(A), enemy = Assets.Side(!A), sys = Label(game, name, x);
        string dest = !A ? "" : units.Where(o => o.Owner == side && !o.Destroyed && o.System is { } at && at != x).Select(o => Label(game, name, o.System!)).FirstOrDefault() ?? "";
        static string Join(params string[] parts) => string.Join(" ", parts.Where(p => p != "").Select(p => p.Trim()));
        bool neutral = u is SimOwner.None or SimOwner.Neutral;
        string music;
        _title = Assets.Message(0x7001, sys);
        if (w == side) // victory
        {
            var (xt, a) = neutral ? (0x7007, enemy) : r == u && u == side ? (0x7016, own) : r == u ? (0x7015, own) : u == side ? (0x7005, enemy) : (0x7006, own);
            _second = Assets.Message(0x7002, own);
            _body = Join(Assets.Message(xt, sys, a), Assets.Message(m == SimOwner.None ? 0x700B : 0x700C, enemy));
            (_textAc, _textB8) = (Assets.Message(0x7012, own, sys), Assets.Message(0x7013));
            (_picture, music) = A ? (m == SimOwner.Empire ? 10758 : 10759, "MDATA.308") : (m == SimOwner.Alliance ? 10757 : 10760, "MDATA.314");
        }
        else if (w != SimOwner.None) // defeat
        {
            int yt = neutral ? 0x700A : r == u && u == side ? 0x7015 : r == u ? 0x7016 : u == side ? 0x7008 : 0x7009;
            _second = Assets.Message(0x7003, own);
            _body = Join(Assets.Message(yt, sys, enemy), m == SimOwner.None ? Assets.Message(0x700B, own) : Assets.Message(0x700D, own, dest));
            _textAc = m == SimOwner.None ? Assets.Message(0x7010, own, sys) : Assets.Message(0x7011, own, sys, dest);
            _textB8 = Assets.Message(m == SimOwner.None ? 0x7014 : 0x7013);
            (_picture, music) = (m == side ? (A ? 10757 : 10758) : (A ? 10760 : 10759), A ? "MDATA.309" : "MDATA.315");
        }
        else // no victor
        {
            _second = Assets.Message(0x7004, sys);
            string tail = m == SimOwner.None ? Assets.Message(0x700F, own, enemy)
                : m == side ? Join(Assets.Message(0x700D, own, dest), Assets.Message(0x700B, enemy))
                : Join(Assets.Message(0x700B, own), Assets.Message(0x700C, enemy));
            _body = Join(Assets.Message(0x700E, own, enemy), tail);
            _textAc = m == side ? Assets.Message(0x7011, own, sys, dest) : Assets.Message(0x7010, own, sys);
            _textB8 = Assets.Message(m == side ? 0x7013 : 0x7014);
            (_picture, music) = (11619, A ? "MDATA.309" : "MDATA.315");
        }
        int[] ys = A ? [0, 54, 108, 162] : [0, 59, 118, 177];
        int[][] images = A ? [[10728, 10729], [10730, 10731], [10732, 10733], [10791, 10792]] : [[10738, 10739], [10740, 10741], [10742, 10743], [10520, 10521]];
        int[] tips = [6434, 6455, 6456, 6457];
        for (int i = 0; i < 4; i++) AddTab(A ? 418 : 426, (A ? 88 : 89) + ys[i], A ? 41 : 44, 41, images[i][0], images[i][1], tips[i]);
        // filter group (id 100) at (130, 59): capital ships, fighters, troops, personnel
        int[][] filters = A ? [[10348, 10347], [10354, 10353], [10352, 10351], [10346, 10345]] : [[10360, 10359], [10364, 10363], [10362, 10361], [10358, 10357]];
        int[] filterTips = [6440, 6441, 6448, 6449];
        for (int i = 0; i < 4; i++)
        {
            int index = i;
            var b = Button(130 + 49 * i, 59, A ? 49 : 44, A ? 42 : 41, filters[i][0], filters[i][1], 0, filterTips[i], () => SelectFilter(index));
            b.CheckedImage = b.Down;
            _filters.Add(b);
        }
        // OK 0xC9 clicks like the tabs; the filters and go-to buttons are silent; the second 0x72 button is not built
        (A ? Button(423, 25, 32, 31, 10370, 10371, command: Close) : Button(426, 17, 44, 41, 10376, 10377, command: Close)).Sound = Assets.Click;
        _system = Button(29, 182, 169, 96, 10764, A ? 10767 : 10768, command: () => { Close(); goSystem(report.System); });
        if (fleet is not null) _fleet = Button(229, 182, 169, 96, A ? 10765 : 10766, A ? 10767 : 10768, command: () => { Close(); goFleet(fleet); });
        SelectFilter(0);
        Select(0);
        PlayMusic(music);
    }

    void SelectFilter(int index)
    {
        _filter = index;
        for (int i = 0; i < _filters.Count; i++) _filters[i].Checked = i == index;
        Fill();
    }

    /// <summary>The demo path: filter <paramref name="index"/> (3 = personnel).</summary>
    public void DemoFilter(int index) => SelectFilter(index);

    /// <summary>
    /// Unit list: a cell list (37, 136) 347 x 166, skin 10653, the strike cells: 165 x 70 in two columns (Operational / Destroyed) or, for personnel, 110 x 50 in three (Survivors / Captured / Killed),
    /// rows filled in parallel by <see cref="ScreenViews.ReportCells"/> (empty-column texts 34086 / 6464 / 6465; Captured entries past
    /// the longer of the other two columns not shown, as the original).
    /// </summary>
    void Fill()
    {
        _cells.Clear();
        if (Tab is 1 or 2)
        {
            var k = Tab == 1 ? SimOwner.Alliance : SimOwner.Empire;
            var rows = _units.Where(u => u.Owner == k && InFilter(u)).ToList();
            var columns = Fates.Select(f => rows.Where(f.Fate).ToList()).ToList();
            _cells.AddRange(_filter == 3 ? ScreenViews.ReportCells(columns[0], columns[1], columns[2]) : ScreenViews.ReportCells(columns[0], null, columns[1]));
        }
        _list.Cell = _filter == 3 ? new Vector2(110, 50) : new Vector2(165, 70);
        _list.Count = _cells.Count;
        _list.First = 0;
        QueueRedraw();
    }

    (int X, int Head, Func<GameObject, bool> Fate)[] Fates => _filter == 3
        ? [(37, 6452, u => !u.Destroyed && !(u is Character { Captured: true })), (149, 6453, u => u is Character { Captured: true }), (260, 6454, u => u.Destroyed)]
        : [(37, 6450, u => !u.Destroyed), (204, 6451, u => u.Destroyed)];

    void DrawCell(CellList c, int i, Rect2 r) => DrawUnitCell(c, r, _game, _name, _viewer, _cells[i].Unit, _cells[i].Text, small: _filter != 3);

    public override bool HandleKey(Key key)
    {
        if (key == Key.Escape) Close();
        else base.HandleKey(key);
        return true;
    }

    bool InFilter(GameObject o) => _filter switch
    {
        0 => Family.Of(o.Type) is >= 0x14 and < 0x1C,
        1 => Family.Of(o.Type) is >= 0x1C and < 0x20,
        2 => Family.IsTroop(o.Type),
        _ => o is Character || Family.IsSpecialForce(o.Type),
    };

    public override void _Draw()
    {
        bool units = Tab is 1 or 2;
        _system.Visible = Tab == 3;
        if (_fleet is not null) _fleet.Visible = Tab == 3;
        foreach (var f in _filters) f.Visible = units;
        if (_list.Visible != units || units && _shownTab != Tab)
        {
            (_list.Visible, _shownTab) = (units, Tab);
            Fill();
        }
        float h11 = Assets.LineHeight(11), h4 = Assets.LineHeight(4);
        Blit(Tab == 0 ? _picture : Tab == 3 ? 10763 : _filter == 3 ? 10762 : 10761, 12, 13, 400, 310);
        Blit(A ? 10335 : 10336, 0, 0, 470, 331);
        Blit(A ? 10820 : 10821, 412, 0, 58, 330);
        switch (Tab)
        {
            case 0:
                Assets.DrawText(this, new Rect2(12, 18, 400, h11), _title, 11, Ink, TextFormat.Centred);
                Assets.DrawText(this, new Rect2(12, 225, 400, h11), _second, 11, Ink, TextFormat.Centred);
                Shadowed(new Rect2(24, 255, 352, 70), _body, 6, Ink, TextFormat.Wrap);
                break;
            case 1 or 2:
                // caption = the selected tab's label, font 6 centred at (12, 40), red for 0x70 / green for 0x71
                Assets.DrawText(this, new Rect2(12, 40, 400, Assets.LineHeight(6)), Assets.Stra(Tab == 1 ? 6455 : 6456), 6,
                    Tab == 1 ? new Color(1, 0, 0) : new Color(0, 1, 0), TextFormat.Centred);
                Assets.DrawText(this, new Rect2(86, 70, 300, h4), Assets.Stra(6482), 4, White, TextFormat.Row);
                Assets.DrawText(this, new Rect2(37, 99, 347, h4), Assets.Stra(new[] { 6440, 6441, 6448, 6449 }[_filter]), 4, White, TextFormat.Row);
                float width = _filter == 3 ? 110 : 165;
                foreach (var (x, head, _) in Fates) Assets.DrawText(this, new Rect2(x, 118, width, h4), Assets.Stra(head), 4, White, TextFormat.Row);
                break;
            default:
                Shadowed(new Rect2(24, 40, 328, 80), _textAc, 6, Ink, TextFormat.Wrap);
                Shadowed(new Rect2(24, 130, 328, 40), _textB8, 7, Ink, TextFormat.Wrap);
                Assets.DrawText(this, new Rect2(29, 278, 169, h4), Label(_game, _name, _report.System), 4, White, TextFormat.Centred);
                if (_fleet is not null) Assets.DrawText(this, new Rect2(229, 278, 169, h4), Assets.Stra(6466), 4, White, TextFormat.Centred);
                break;
        }
    }
}

/// <summary>
/// Bombardment / assault result window (window type 0x1C) on a type-0xF message:
/// opened by the attacker's kind-7 add (observed in the original) and from the message window for any type-0xF message, the defender's too
/// (observed in the original); modeless, the clock keeps running. Frame 10335 / 10336, side strip at x 412 (A) / 415 (E), picture at
/// (12, 13), title (font 11) and body (font 6, shadowed); tabs 0x65 summary, 0x66 attacker, 0x67 system,
/// 0x68 display (image 5) with the go-to buttons 0xB (29, 182) / 0xC (226, 182) 169 x 96; tab images by the attacker
/// side (a 0 after a load names side 2). Unit list (37, 136) 347 x 166: cells 165 x 70 in two columns (Operational 6450 / Destroyed
/// 6451) or, for personnel, 110 x 50 in three (Survivors 6452 / Captured 6453 / Killed 6454), rows filled in parallel, an empty
/// column shows one text cell (34086 for lists 1 / 3, 6465 for list 2); filter group 0x96 by family range (0x97 capital ships,
/// 0x98 fighters, 0x99 manufacturing, 0x9A defences, 0x9B troops, 0x9C personnel; default 0x97 bombardment / 0x9B assault). Cell =
/// the player's view picture centred plus its name below (GOKRES p 122 x 50 with overlays, personnel p + 0x4000 61 x 25; name
/// font 9 Inferred; column heads above the list at y 117, Inferred); summary page black inside the opening under the picture. OK (command 10) / Esc / Enter close; then <see cref="Reopen"/> runs (the message window's
/// list on its remembered tab); a go-to clears it. A loaded message has no lists (empty tabs).
/// </summary>
public partial class StrikeResultWindow : BattleWindow
{
    readonly GameState _game;
    readonly Func<SdCommon, string> _name;
    readonly GameMessage _m;
    readonly SimOwner _viewer;
    readonly List<FrontButton> _filters = [];
    readonly FrontButton _system, _fleets;
    readonly CellList _list;
    /// <summary>The current cells: a unit, a text cell (string id) or blank.</summary>
    readonly List<(GameObject? Unit, int Text)> _cells = [];
    int _filter;
    /// <summary>Runs after OK / Esc (not after a go-to): the remembered message window.</summary>
    public Action? Reopen { get; set; }

    static readonly (uint Lo, uint Hi)[] Ranges = [(0x14, 0x1C), (0x1C, 0x20), (0x28, 0x30), (0x22, 0x28), (0x10, 0x14), (0x30, 0x40)];

    public StrikeResultWindow(GameState game, Func<SdCommon, string> name, SimOwner viewer, GameMessage m, Action<StarSystem, bool> goTo) : base(viewer == SimOwner.Alliance)
    {
        (_game, _name, _m, _viewer) = (game, name, m, viewer);
        bool one = m.Attacker == SimOwner.Alliance; // a 0 names side 2
        int[] ys = A ? [0, 53, 108, 162] : [0, 59, 118, 177];
        int[] second = A ? (one ? [10730, 10731] : [10732, 10733]) : (one ? [10740, 10741] : [10742, 10743]);
        int[] third = m.Defender switch
        {
            SimOwner.Alliance => A ? [10730, 10731] : [10740, 10741],
            SimOwner.Empire => A ? [10732, 10733] : [10742, 10743],
            _ => A ? [10736, 10737] : [10746, 10747],
        };
        int[][] images = [A ? [10728, 10729] : [10738, 10739], second, third, A ? [10791, 10792] : [10520, 10521]];
        int[] tips = [6468, one ? 6455 : 6456, 6467, 6457];
        _list = new CellList(10653) { Single = true, Position = new Vector2(37, 136), Size = new Vector2(347, 166), DrawCell = DrawCell, Visible = false };
        AddChild(_list);
        for (int i = 0; i < 4; i++) AddTab(A ? 418 : 426, (A ? 88 : 89) + ys[i], A ? 41 : 44, 41, images[i][0], images[i][1], tips[i]);
        (A ? Button(423, 25, 32, 31, 10370, 10371, command: Ok) : Button(426, 21, 44, 41, 10376, 10377, command: Ok)).Sound = Assets.Click; // OK 10
        var s = Messages.Find(game, m.Subject) as StarSystem;
        void Go(bool fleets)
        {
            Reopen = null; // the memory cleared, the message window not reopened
            Close();
            if (s is not null) goTo(s, fleets);
        }
        _system = Button(29, 182, 169, 96, 10764, A ? 10767 : 10768, command: () => Go(false));  // 0xB
        _fleets = Button(226, 182, 169, 96, A ? 10765 : 10766, A ? 10767 : 10768, command: () => Go(true)); // 0xC
        // labels, white, vertically centred: 0xB the player's view name of the subject system (6469 without a view), 0xC 6466
        bool seen = s is not null && (Knowledge.IsLive(s, viewer) || game.Views.ContainsKey((s, viewer)));
        foreach (var (b, label) in new[] { (_system, seen ? Label(game, name, s!) : Assets.Stra(6469)), (_fleets, Assets.Stra(6466)) })
        {
            var text = new Control { Size = b.Size, MouseFilter = MouseFilterEnum.Ignore }; // over the button's image
            text.Draw += () => Assets.DrawText(text, new Rect2(Vector2.Zero, text.Size), label, 4, White, new TextFormat { Center = true, VCenter = true, SingleLine = true, NoClip = true }); // button label: unclipped
            b.AddChild(text);
        }
        (int X, int[] A, int[] E, int Tip)[] filters =
        [
            (0, [10348, 10347], [10360, 10359], 6440), (62, [10354, 10353], [10364, 10363], 6441), (124, [10344, 10343], [10356, 10355], 6197),
            (186, [10342, 10341], [10342, 10341], 6470), (248, [10352, 10351], [10362, 10361], 6448), (312, [10346, 10345], [10358, 10357], 6449),
        ];
        foreach (var (x, a, e, tip) in filters)
        {
            int index = _filters.Count;
            var images2 = A ? a : e;
            var b = Button(36 + x, 59, 49, 41, images2[0], images2[1], 0, tip, () => SelectFilter(index));
            b.CheckedImage = b.Down;
            _filters.Add(b);
        }
        SelectFilter(m.Assault ? 4 : 0); // 0x9B for an assault, else 0x97
        Select(0);
    }

    /// <summary>OK (10) / Esc: close, then the remembered message window's list.</summary>
    void Ok()
    {
        var reopen = Reopen;
        Close();
        reopen?.Invoke();
    }

    void SelectFilter(int index)
    {
        _filter = index;
        for (int i = 0; i < _filters.Count; i++) _filters[i].Checked = i == index;
        Fill();
    }

    /// <summary>Fill: the tab's three lists by fate, filtered by family; the columns in parallel rows.</summary>
    void Fill()
    {
        _cells.Clear();
        if (_m.Strike is { } l && Tab is 1 or 2)
        {
            var (lo, hi) = Ranges[_filter];
            List<GameObject> F(List<GameObject> x) => [.. x.Where(o => Family.Of(o.Type) >= lo && Family.Of(o.Type) < hi)];
            // list 1 operational / survivors, list 2 destroyed / killed, list 3 captured
            var (l1, l2, l3) = Tab == 1 ? (F(l[1]), F(l[3]), F(l[4])) : (F(l[0]), F(l[2]), F(l[5]));
            (List<GameObject> List, int Empty)[] columns = _filter == 5 ? [(l1, 34086), (l3, 34086), (l2, 6465)] : [(l1, 34086), (l2, 6465)];
            int rows = Math.Max(1, columns.Max(c => c.List.Count));
            for (int r = 0; r < rows; r++)
                foreach (var (list, empty) in columns)
                    _cells.Add(r < list.Count ? (list[r], 0) : (null, r == 0 && list.Count == 0 ? empty : 0));
        }
        _list.Cell = _filter == 5 ? new Vector2(110, 50) : new Vector2(165, 70);
        _list.Count = _cells.Count;
        _list.First = 0;
        QueueRedraw();
    }

    // Filters 0x97-0x9B use the GOKRES picture (122 x 50, "small" in our naming); only personnel 0x9C uses the 61 x 25 form.
    void DrawCell(CellList c, int i, Rect2 r) => DrawUnitCell(c, r, _game, _name, _viewer, _cells[i].Unit, _cells[i].Text, small: _filter != 5);

    public override bool HandleKey(Key key)
    {
        if (key is Key.Escape or Key.Enter or Key.KpEnter) Ok();
        else base.HandleKey(key);
        return true;
    }

    public override void _Draw()
    {
        foreach (var f in _filters) f.Visible = Tab is 1 or 2;
        (_system.Visible, _fleets.Visible) = (Tab == 3, Tab == 3);
        bool list = Tab is 1 or 2;
        if (_list.Visible != list || list && _shownTab != Tab)
        {
            (_list.Visible, _shownTab) = (list, Tab);
            Fill(); // switching to 0x66 / 0x67 refills with the current filter
        }
        if (Tab == 0)
        {
            var t = Pic(_m.Picture);
            DrawRect(new Rect2(12, 13, 400, 310), Black); DrawLog.Fill(this, new Rect2(12, 13, 400, 310), Black); // the page DIB is zero-filled: index 0 = black under 11164's lower rows
            Blit(_m.Picture, 12, 13, 400, t is not null && t.GetHeight() == 200 ? 200 : 310); // 11164 is 400 x 200
        }
        else Blit(Tab == 3 ? 10763 : 10761, 12, 13, 400, 310); // Inferred backgrounds
        Blit(A ? 10335 : 10336, 0, 0, 470, 331);
        Blit(A ? 10820 : 10821, A ? 412 : 415, 0, 58, 330);
        if (Tab == 0)
        {
            Assets.DrawText(this, new Rect2(12, 13, 400, Assets.LineHeight(11)), MessageFormat.Title(_m), 11, Ink, TextFormat.Centred);
            Shadowed(new Rect2(25, 225, 350, 90), MessageFormat.Body(_m), 6, Ink, TextFormat.Wrap);
            return;
        }
        if (Tab == 3) return;
        float h4 = Assets.LineHeight(4);
        int[] heads = _filter == 5 ? [6452, 6453, 6454] : [6450, 6451];
        float width = _filter == 5 ? 110 : 165;
        for (int i = 0; i < heads.Length; i++)
            Assets.DrawText(this, new Rect2(37 + i * width, 117, width, h4), Assets.Stra(heads[i]), 4, White, TextFormat.CentredRow);
    }

    int _shownTab = -1;
}

/// <summary>
/// Battle wait window (window kind 7): 380 x 200, background 10769, the literal "Battle in Progress..."
/// (shown while TEXTSTRA's version fields are 0, this install; shortcut: the TEXTSTRA 0x1A00 branch of translated text DLLs is not
/// read) in font 0xB, red (A) / green (E), centred over the width at y 14. No buttons, keys pass to the main screen, clicks are
/// taken (Inferred). Open from the start window's decision until the report window opens. Position: centred in the map rect
/// (Inferred, as the 0x468 windows).
/// </summary>
public partial class BattleWaitWindow : Control
{
    const string Text = "Battle in Progress...";
    readonly bool _a;

    public BattleWaitWindow(Rect2 map, bool alliance)
    {
        _a = alliance;
        Size = new Vector2(380, 200);
        Position = (map.Position + (map.Size - Size) / 2).Floor();
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var back = Assets.Strategy(10769);
        DrawTextureRect(back, new Rect2(Vector2.Zero, Size), false);
        DrawLog.Bitmap(this, back, new Rect2(Vector2.Zero, Size));
        Assets.DrawText(this, new Rect2(0, 14, Size.X, Assets.LineHeight(11)), Text, 11, _a ? new Color(1, 0, 0) : new Color(0, 1, 0), TextFormat.Centred);
    }
}
