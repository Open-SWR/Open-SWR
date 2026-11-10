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
/// Encyclopedia window (470 x 330 centred in the map rect; scope: layout and navigation): index page
/// (title 6210, label 6211, name box with type-ahead, caption = the topic's tooltip, list of entries sorted by name, topic group
/// tabs filtering by family range) and entry page (ENCYTEXT RCDATA text of entry id = picture number + 4096, picture,
/// previous / next stepping through the list); page switch Display Topic / Index on the strip, close.
/// Inferred: topic button images (pairs 10339...10364 read from the art: up = the dark one), the entry text box and fonts, the
/// ship database = families [20, 32). The entry picture is the EData file named by the ENCYBMAP string of the picture id
/// (ENCYBMAP.DLL holds no bitmaps). Mod records are listed by their mod name; their page shows the
/// mod's description and picture (or none).
/// </summary>
public partial class EncyclopediaWindow : GameWindow
{
    public sealed record Entry(uint Ref, int StringId, string Name, bool Mod = false)
    {
        public int Id => ModArt.PictureNumber(StringId) + PictureIds.EncyclopediaEntryBase;
        public uint Family => OpenSWR.Sim.Family.Of(Ref);
    }

    /// <summary>The All topic lists every family: [0, 256).</summary>
    const uint AllFamiliesFirst = 0, AllFamiliesEnd = 256;

    // topic id, x, tooltip, family range, A (up, down), E (up, down)
    static readonly (int Id, int Tip, uint Lo, uint Hi, int AUp, int ADown, int EUp, int EDown)[] Topics =
    [
        (ControlIds.AllDatabasesTopic, StrategyTexts.AllDatabases, AllFamiliesFirst, AllFamiliesEnd, StrategyBitmaps.AllDatabasesCategoryUp, StrategyBitmaps.AllDatabasesCategoryDown, StrategyBitmaps.AllDatabasesCategoryUp, StrategyBitmaps.AllDatabasesCategoryDown),
        (ControlIds.SystemDatabaseTopic, StrategyTexts.SystemDatabase, Families.System, Families.Abode, StrategyBitmaps.SystemsCategoryUp, StrategyBitmaps.SystemsCategoryDown, StrategyBitmaps.SystemsCategoryUp, StrategyBitmaps.SystemsCategoryDown),
        (ControlIds.ShipDatabaseTopic, StrategyTexts.ShipDatabase, Families.CapitalShip, Families.Headquarters, StrategyBitmaps.FightersCategoryAllianceUp, StrategyBitmaps.FightersCategoryAllianceDown, StrategyBitmaps.CapitalShipsCategoryEmpireUp, StrategyBitmaps.CapitalShipsCategoryEmpireDown),
        (ControlIds.FacilitiesDatabaseTopic, StrategyTexts.FacilitiesDatabase, Families.Headquarters, Families.MajorCharacter, StrategyBitmaps.FacilitiesCategoryUp, StrategyBitmaps.FacilitiesCategoryDown, StrategyBitmaps.FacilitiesCategoryUp, StrategyBitmaps.FacilitiesCategoryDown),
        (ControlIds.MissionsDatabaseTopic, StrategyTexts.MissionsDatabase, Families.Mission, Families.Sector, StrategyBitmaps.MissionsCategoryUp, StrategyBitmaps.MissionsCategoryDown, StrategyBitmaps.MissionsCategoryUp, StrategyBitmaps.MissionsCategoryDown),
        (ControlIds.TroopDatabaseTopic, StrategyTexts.TroopDatabase, Families.Troop, Families.CapitalShip, StrategyBitmaps.TroopsCategoryAllianceUp, StrategyBitmaps.TroopsCategoryAllianceDown, StrategyBitmaps.TroopsCategoryEmpireUp, StrategyBitmaps.TroopsCategoryEmpireDown),
        (ControlIds.PersonnelDatabaseTopic, StrategyTexts.PersonnelDatabase, Families.MajorCharacter, Families.Mission, StrategyBitmaps.PersonnelCategoryAllianceUp, StrategyBitmaps.PersonnelCategoryAllianceDown, StrategyBitmaps.PersonnelCategoryEmpireUp, StrategyBitmaps.PersonnelCategoryEmpireDown),
    ];

    readonly List<Entry> _catalogue;
    List<Entry> _list = [];
    int _topic = ControlIds.AllDatabasesTopic, _page = 1; // 1 index, 2 entry
    Entry? _entry;
    readonly List<(int Id, FrontButton Button)> _topics = [];
    FrontButton _display = null!, _index = null!, _prev = null!, _next = null!;
    CellList _rows = null!;
    NameField _name = null!;
    bool _typing; // a type-ahead select posts no command notification: the name box keeps the typed text
    TextBox _text = null!;

    public EncyclopediaWindow(MainScreen screen, Entry? start) : base(screen, WindowKind.Encyclopedia, new Vector2(470, 330))
    {
        var r = screen.MapRect;
        Position = (r.Position + (r.Size - Size) / 2).Floor();
        _catalogue = Catalogue(screen);
        _entry = start is null ? null : _catalogue.FirstOrDefault(e => e.Ref == start.Ref) ?? start;
    }

    protected override bool Framed => false;
    protected override string Title => "";

    /// <summary>The global catalogue (Inferred): one entry per definition with a name, keyed by SdRef.</summary>
    static List<Entry> Catalogue(MainScreen screen)
    {
        var d = screen.Game!.Definitions;
        IEnumerable<SdCommon> all = d.Systems.Select(x => x.Common).Concat(d.CapitalShips.Select(x => x.Common)).Concat(d.Fighters.Select(x => x.Common))
            .Concat(d.Troops.Select(x => x.Common)).Concat(d.Facilities.Select(x => x.Common)).Concat(d.DefenseFacilities.Select(x => x.Common))
            .Concat(d.MajorCharacters.Select(x => x.Common)).Concat(d.MinorCharacters.Select(x => x.Common)).Concat(d.SpecialForces.Select(x => x.Common))
            .Concat(d.Missions.Select(x => x.Common));
        return [.. all.Where(c => c.StringId != 0 || c.StringModule == GameDefinitions.ModStringModule).Select(c => Of(c, screen.DisplayName))
            .GroupBy(e => e.Ref).Select(g => g.First())];
    }

    /// <summary>The entry of an object's definition, or of a definition key.</summary>
    public static Entry? EntryOf(GameState game, GameObject? o, uint definition, Func<SdCommon, string> name)
    {
        SdCommon? c = o switch
        {
            StarSystem s => s.Definition.Common,
            Character ch => ch.Definition.Common,
            Mission m => m.Record.Common,
            null => game.Definitions.Resolve(definition)?.Common,
            _ => game.Definitions.Resolve(o.Type)?.Common,
        };
        return c is { } x ? Of(x, name) : null;
    }

    static Entry Of(SdCommon c, Func<SdCommon, string> name) => new(c.Ref, c.StringId, name(c), c.StringModule == GameDefinitions.ModStringModule);

    static Texture2D? Bmp(int id, string module = "STRATEGY.DLL")
    {
        return id is <= 0 or > 65535 ? null : Assets.TryBitmap((ushort)id, module);
    }

    FrontButton Btn(Rect2 r, int up, int down, int tip, Action command)
    {
        var b = new FrontButton { Up = Bmp(up), Down = Bmp(down), Command = command, TooltipText = tip == 0 ? "" : UiFrame.Label(tip) };
        b.CheckedImage = b.Down;
        b.Place((int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y);
        AddChild(b);
        return b;
    }

    public override void _Ready()
    {
        var close = Btn(A ? new Rect2(423, 25, 32, 31) : new Rect2(426, 21, 44, 41), A ? StrategyBitmaps.PanelCloseAllianceUp : StrategyBitmaps.PanelCloseEmpireUp, A ? StrategyBitmaps.PanelCloseAllianceDown : StrategyBitmaps.PanelCloseEmpireDown, StrategyTexts.Close, () => Screen.Windows.Close(this));
        _display = Btn(A ? new Rect2(423, 93, 32, 31) : new Rect2(426, 89, 44, 41), A ? StrategyBitmaps.DisplayTopicAllianceUp : StrategyBitmaps.DisplayTopicEmpireUp, A ? StrategyBitmaps.DisplayTopicAllianceDown : StrategyBitmaps.DisplayTopicEmpireDown, StrategyTexts.DisplayTopic, () => ShowEntry(_entry ?? Selected));
        _index = Btn(A ? new Rect2(423, 147, 32, 31) : new Rect2(426, 143, 44, 41), A ? StrategyBitmaps.IndexAllianceUp : StrategyBitmaps.IndexEmpireUp, A ? StrategyBitmaps.IndexAllianceDown : StrategyBitmaps.IndexEmpireDown, StrategyTexts.EncyclopediaIndex, ShowIndex);
        foreach (var b in new[] { close, _display, _index }) b.Sound = Assets.Click; // close, display and index buttons click
        _prev = Btn(new Rect2(28, 14, 21, 17), StrategyBitmaps.PreviousTopicUp, StrategyBitmaps.PreviousTopicDown, StrategyTexts.PreviousTopic, () => Step(-1));
        _next = Btn(new Rect2(380, 14, 21, 17), StrategyBitmaps.NextTopicUp, StrategyBitmaps.NextTopicDown, StrategyTexts.NextTopic, () => Step(1));
        for (int i = 0; i < Topics.Length; i++)
        {
            var t = Topics[i];
            _topics.Add((t.Id, Btn(new Rect2(36 + 52 * i, 78, 49, 41), A ? t.AUp : t.EUp, A ? t.ADown : t.EDown, t.Tip, () => SetTopic(t.Id))));
        }
        _rows = new CellList(StrategyRcData.PanelScrollSkin) { Single = true, Cell = new Vector2(330, 20), Position = new Vector2(36, 137), Size = new Vector2(350, 160), DrawCell = DrawRow };
        _rows.Pressed = () => Screen.Windows.Focus(this);
        _rows.SelectionChanged = () => { if (!_typing && Selected is { } e && _name.Text != e.Name) _name.SetText(e.Name); }; // text set: no text changed notification
        _rows.Activated = i => ShowEntry(_list[i]);
        AddChild(_rows);
        // a CoolStringField like the finder boxes: 245 x 18 at (143, 45), System font, white, cleared at open
        _name = new NameField(new Vector2(143, 45), new Vector2(245, 18))
        {
            TextChanged = TypeAhead, Submitted = () => { if (Selected is { } e) ShowEntry(e); },
            OtherKey = k => { if (k.Keycode != Key.Escape) return false; Screen.Windows.Close(this); return true; }, // as the finder boxes
        };
        _name.Clear();
        AddChild(_name);
        _text = new TextBox { Position = new Vector2(17, 234), Size = new Vector2(395, 85) };
        AddChild(_text);
        Fill();
        if (_entry is not null) ShowEntry(_entry);
        else ShowIndex();
    }

    Entry? Selected => _rows.Selected.Count == 1 ? _list.ElementAtOrDefault(_rows.Selected.Min) : null;

    void SetTopic(int id)
    {
        _topic = id;
        Fill();
        TypeAhead(_name.Text);
    }

    void Fill()
    {
        var t = Topics.First(x => x.Id == _topic);
        _list = [.. _catalogue.Where(e => e.Family >= t.Lo && e.Family < t.Hi).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
        _rows.Count = _list.Count;
        _rows.Selected.Clear();
        _rows.First = 0;
        foreach (var (id, b) in _topics) b.Checked = id == _topic;
        QueueRedraw();
    }

    void TypeAhead(string typed)
    {
        int i = Finders.TypeAhead([.. _list.Select(e => new FinderRow(Screen.Game!.Staging, e.Name))], typed);
        _typing = true;
        if (i >= 0) { if (!_rows.Selected.Contains(i)) _rows.Select(i); } // an already selected row is not re-selected or scrolled
        else { _rows.Selected.Clear(); _rows.QueueRedraw(); }
        _typing = false;
    }

    void ShowIndex()
    {
        _page = 1;
        Update();
        _name.CallDeferred(Control.MethodName.GrabFocus);
    }

    void ShowEntry(Entry? e)
    {
        if (e is null) { ShowIndex(); return; }
        _entry = e;
        _page = 2;
        // a mod description wins; mod records never read the install's texts (their StringId is 0)
        var bytes = e.Mod || Game.Definitions.Descriptions.ContainsKey(e.Ref) ? null : Assets.RcData(e.Id, "ENCYTEXT.DLL");
        _text.Text = Game.Definitions.Descriptions.GetValueOrDefault(e.Ref)
            ?? (bytes is null ? "" : global::System.Text.Encoding.Latin1.GetString(bytes).TrimEnd('\0').Replace("\r", "").Trim('\n'));
        Update();
    }

    /// <summary>Previous / next buttons: the neighbouring entry of the list.</summary>
    void Step(int dir)
    {
        int i = _entry is null ? -1 : _list.FindIndex(x => x.Ref == _entry.Ref);
        if (i + dir >= 0 && i + dir < _list.Count) ShowEntry(_list[i + dir]);
    }

    void Update()
    {
        bool index = _page == 1;
        foreach (var (_, b) in _topics) b.Visible = index;
        (_rows.Visible, _name.Visible, _text.Visible, _prev.Visible, _next.Visible) = (index, index, !index, !index, !index);
        (_display.Checked, _index.Checked) = (!index, index);
        QueueRedraw();
    }

    /// <summary>
    /// Entry picture: missions / fleets the entry id (+ 8192 for side 2), others the entry id; the
    /// ENCYBMAP string of that id names the EData file; none: no picture. Mod records: their mod picture.
    /// </summary>
    Texture2D? Picture(Entry e, out Vector2 size)
    {
        if (ModArt.Picture(Game.Definitions, e.Ref, 0) is var (mod, modSize)) { size = modSize; return mod; }
        int id = e.Family is >= Families.Mission and < Families.Sector or >= Families.Fleet and < Families.Troop ? e.Id + (A ? 0 : PictureIds.EncyclopediaEmpireOffset) : e.Id;
        var t = e.Mod ? null : Assets.EData(id);
        size = t?.GetSize() ?? Vector2.Zero;
        return t;
    }

    public override bool HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.I when key.AltPressed && _page == 2: ShowIndex(); return true;
            case Key.Left or Key.Right when _page == 2: Step(key.Keycode == Key.Left ? -1 : 1); return true;
            case Key.Enter or Key.KpEnter when _page == 1 && Selected is { } e: ShowEntry(e); return true;
        }
        return false;
    }

    void DrawRow(CellList list, int i, Rect2 r)
    {
        bool selected = list.Selected.Contains(i);
        Assets.DrawCellText(list, new Rect2(r.Position.X + 4, r.Position.Y, r.Size.X - 6, r.Size.Y), _list[i].Name, 10, selected ? Colors.White : Color.Color8(120, 120, 120), TextFormat.Row);
    }

    public override void _Draw()
    {
        DrawTextureRect(Bmp(_page == 1 ? StrategyBitmaps.EncyclopediaIndexPage : StrategyBitmaps.EncyclopediaEntryPage)!, new Rect2(12, _page == 1 ? 13 : 14, 400, 306), false); // index 10338 (list panel art), entry 10337 (Inferred by the art)
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(_page == 1 ? StrategyBitmaps.EncyclopediaIndexPage : StrategyBitmaps.EncyclopediaEntryPage), new Rect2(12, _page == 1 ? 13 : 14, 400, 306));
        if (_page == 2 && _entry is { } e)
        {
            if (Picture(e, out var size) is { } pic) CellList.Centred(this, pic, size, new Rect2(12, 31, 400, 200));
            Assets.DrawText(this, new Rect2(52, 14, 326, 16), e.Name, 5, Colors.White, TextFormat.CentredRow);
        }
        DrawTextureRect(Bmp(A ? StrategyBitmaps.PanelFrameAlliance : StrategyBitmaps.PanelFrameEmpire)!, new Rect2(0, 0, 470, 331), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(A ? StrategyBitmaps.PanelFrameAlliance : StrategyBitmaps.PanelFrameEmpire), new Rect2(0, 0, 470, 331));
        DrawTextureRect(Bmp(A ? StrategyBitmaps.EncyclopediaStripAlliance : StrategyBitmaps.EncyclopediaStripEmpire)!, new Rect2(412, 0, 58, 330), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(A ? StrategyBitmaps.EncyclopediaStripAlliance : StrategyBitmaps.EncyclopediaStripEmpire), new Rect2(412, 0, 58, 330));
        if (_page != 1) return;
        // text items, all unclipped: title fixed width 350 centred, font 5; the label 6211
        // measured top-left (Inferred: font 4 as the finders' label); caption fixed width 283, left, font 5
        Assets.DrawTextItem(this, new Rect2(36, 14, 350, 0), StrategyText.EncyclopediaTitle, 5, Colors.White, TextFormat.Centred, new TextItemSize(FixedWidth: true));
        Assets.DrawTextItem(this, new Rect2(36, 48, 0, 0), StrategyText.EncyclopediaTopic, 4, Colors.White, TextFormat.TopLeft, new TextItemSize());
        Assets.DrawTextItem(this, new Rect2(40, 119, 283, 0), Assets.Stra(Topics.First(t => t.Id == _topic).Tip), 5, Colors.White, TextFormat.TopLeft, new TextItemSize(FixedWidth: true));
    }
}
