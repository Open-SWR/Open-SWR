using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Sim;

/// <summary>
/// The right panel of the tactical screen: child (482, 28) 149 x 236, one page at a time (blank,
/// ship, fighter group, task-force ship, missions, maneuvers &amp; tactics, game options, withdraw
/// confirmation) on its background bitmap; texts, bars, system icons, the officer strip with the TACTICAL RCDATA 1350 horizontal
/// scroll bar, buttons with TEXTTACT tooltips. Page and object come from <see cref="TacticalUi.Page"/>; the controls are rebuilt when
/// they change. Text font and colours Inferred (Arial 8 px, the own side's label colour).
/// </summary>
public partial class TacticalPanel : Control
{
    readonly TacticalScreen _s;
    TacticalPage? _page;
    object? _object;
    TacticalMissionNumber _mission;
    TacticalTactic _tactic;
    readonly List<(HudButton Button, TacticalMissionNumber Value)> _radios = [];
    /// <summary>The maneuver missions in diagram order (the maneuver diagrams follow each other).</summary>
    static readonly TacticalMissionNumber[] ManeuverMissions = [TacticalMissionNumber.LeftHook, TacticalMissionNumber.RightHook, TacticalMissionNumber.Hammer, TacticalMissionNumber.Anvil, TacticalMissionNumber.Stop];
    HudButton? _tacticToggle, _orders1, _orders2, _withdraw, _immediate, _observe;

    public TacticalPanel(TacticalScreen screen)
    {
        _s = screen;
        Position = new Vector2(482, 28);
        Size = new Vector2(149, 236);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>A TACTICAL bitmap; none when the install lacks it (every id used here exists in the original TACTICAL.DLL).</summary>
    static Texture2D? Bmp(int id)
    {
        return Assets.TryBitmap((ushort)id, TacticalScreen.Dll);
    }
    TacticalUi Ui => _s.Ui;

    HudButton Button(int x, int y, int w, int h, int up, int down, int tooltip, Action? command = null, int? disabled = null)
    {
        var (u, dn) = (Bmp(up), Bmp(down));
        var dis = disabled is { } d ? Bmp(d) : null;
        var b = new HudButton { Up = u, Down = dn, CheckedImage = dn, DisabledImage = dis, Command = command, TooltipText = tooltip == 0 ? "" : Assets.Tip(TacticalScreen.Text(tooltip)), FocusMode = FocusModeEnum.None };
        b.Place(x, y, w, h);
        AddChild(b);
        return b;
    }

    void Tip(int x, int y, int w, int h, int tooltip) =>
        AddChild(new Control { Position = new Vector2(x, y), Size = new Vector2(w, h), TooltipText = Assets.Tip(TacticalScreen.Text(tooltip)), MouseFilter = MouseFilterEnum.Pass });

    /// <summary>The unit a ship page shows and the group whose orders a page edits (the unit's UI task force, or the fighter group).</summary>
    TacticalUnit? Unit => _object as TacticalUnit;
    TacticalGroup? Group => _object as TacticalGroup ?? Unit?.UiGroup;
    /// <summary>The page drawn: the withdraw page while it shows as a child window of the game options page (the withdraw confirmation is not a page switch), else the UI page.</summary>
    TacticalPage Shown => Ui.WithdrawShown ? TacticalPage.WithdrawConfirm : Ui.Page;
    int Side => _object switch { TacticalUnit u => u.Side, TacticalGroup g => g.Side, _ => _s.Battle.Local };

    public void Refresh()
    {
        if (Shown != _page || Ui.PageObject != _object) Build();
        bool orders = Ui.OrdersEnabled(Side);
        if (_orders1 is not null) _orders1.Disabled = !orders;
        if (_orders2 is not null) _orders2.Disabled = !orders;
        if (_withdraw is not null) _withdraw.Disabled = !_s.Battle.RetreatPossible(_s.Battle.Local);
        if (_immediate is not null) _immediate.Disabled = !Ui.ImmediateEnabled;
        if (_observe is not null)
        {
            _observe.Disabled = _s.Battle.SideState(_s.Battle.Local) != TacticalSideState.Fighting;
            _observe.Checked = !Ui.InCommand; // Inferred: checked while observing
            _observe.TooltipText = Assets.Tip(TacticalScreen.Text(Ui.InCommand ? TacticalTexts.ObserveOrTakeCommand : TacticalTexts.TakeCommandOrObserve));
        }
        foreach (var (button, value) in _radios) button.Checked = value == _mission;
        if (_tacticToggle is not null)
        {
            _tacticToggle.Checked = _tactic == TacticalTactic.Surround;
            _tacticToggle.TooltipText = Assets.Tip(TacticalScreen.Text(_tactic == TacticalTactic.Surround ? TacticalTexts.StandOffOrSurround : TacticalTexts.SurroundOrStandOff));
        }
        QueueRedraw();
    }

    /// <summary>The page's controls, rebuilt on each page switch.</summary>
    void Build()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        _radios.Clear();
        _tacticToggle = _orders1 = _orders2 = _withdraw = _immediate = _observe = null;
        (_page, _object) = (Shown, Ui.PageObject);
        _mission = Group?.UiMission ?? TacticalMissionNumber.None;
        _tactic = Group?.Tactic ?? TacticalTactic.StandOff;
        switch (_page)
        {
            case TacticalPage.Ship or TacticalPage.TaskForceShip:
                bool tf = _page == TacticalPage.TaskForceShip;
                Button(7, 10, 11, 53, TacticalBitmaps.PreviousShipUp, TacticalBitmaps.PreviousShipDown, tf ? TacticalTexts.PreviousShipInTaskForce : TacticalTexts.PreviousShip, () => Ui.Cycle(false));
                Button(132, 10, 11, 53, TacticalBitmaps.NextShipUp, TacticalBitmaps.NextShipDown, tf ? TacticalTexts.NextShipInTaskForce : TacticalTexts.NextShip, () => Ui.Cycle(true));
                Tip(34, 81, 37, 10, TacticalTexts.HullIntegrity);
                Tip(103, 81, 37, 10, TacticalTexts.ShieldStrength);
                for (int i = 0; i < 5; i++) Tip(10 + 27 * i, 103, 22, 15, TacticalTexts.SystemPowerBase + i);
                OrderButtons();
                break;
            case TacticalPage.FighterGroup:
                OrderButtons();
                break;
            case TacticalPage.Missions:
                bool a = _s.Battle.Local == 0;
                bool fighters = Group?.Fighters == true;
                // ids checked: every id exists in TACTICAL.DLL
                Radio(18, 129, 46, 26, a ? TacticalBitmaps.AttackCapitalShipsAllianceUp : TacticalBitmaps.AttackCapitalShipsEmpireUp, a ? TacticalBitmaps.AttackCapitalShipsAllianceDown : TacticalBitmaps.AttackCapitalShipsEmpireDown, TacticalMissionNumber.AttackCapitalShips, true, null); // Attack Capital Ships
                Radio(86, 129, 46, 26, a ? TacticalBitmaps.RecoverAllianceUp : TacticalBitmaps.RecoverEmpireUp, a ? TacticalBitmaps.RecoverAllianceDown : TacticalBitmaps.RecoverEmpireDown, TacticalMissionNumber.Recover, fighters, a ? TacticalBitmaps.RecoverAllianceDisabled : TacticalBitmaps.RecoverEmpireDisabled); // Recover
                Radio(18, 159, 46, 26, a ? TacticalBitmaps.AttackFightersAllianceUp : TacticalBitmaps.AttackFightersEmpireUp, a ? TacticalBitmaps.AttackFightersAllianceDown : TacticalBitmaps.AttackFightersEmpireDown, TacticalMissionNumber.AttackFighters, true, null); // Attack Fighters
                // Attack Death Star: an Alliance player's fighter group while the attack is possible
                Radio(86, 159, 46, 26, TacticalBitmaps.AttackDeathStarUp, TacticalBitmaps.AttackDeathStarDown, TacticalMissionNumber.AttackDeathStar, a && fighters && _s.Battle.AttackPossible, TacticalBitmaps.AttackDeathStarDisabled);
                Button(66, 202, 27, 25, TacticalBitmaps.AssignUp, TacticalBitmaps.AssignDown, TacticalTexts.AssignMission, () => { var m = _mission; Ui.ClosePage(); Ui.AssignMission(m); });
                Button(106, 202, 27, 25, TacticalBitmaps.CancelUp, TacticalBitmaps.CancelDown, TacticalTexts.Cancel, Ui.ClosePage);
                break;
            case TacticalPage.Tactics:
                Radio(13, 159, 28, 28, TacticalBitmaps.LeftHookUp, TacticalBitmaps.LeftHookDown, TacticalMissionNumber.LeftHook, true, null, TacticalTexts.LeftHook);
                Radio(43, 159, 28, 28, TacticalBitmaps.RightHookUp, TacticalBitmaps.RightHookDown, TacticalMissionNumber.RightHook, true, null, TacticalTexts.RightHook);
                Radio(13, 129, 28, 28, TacticalBitmaps.HammerUp, TacticalBitmaps.HammerDown, TacticalMissionNumber.Hammer, true, null, TacticalTexts.Hammer);
                Radio(43, 129, 28, 28, TacticalBitmaps.AnvilUp, TacticalBitmaps.AnvilDown, TacticalMissionNumber.Anvil, true, null, TacticalTexts.Anvil);
                Radio(75, 144, 28, 28, TacticalBitmaps.StopUp, TacticalBitmaps.StopDown, TacticalMissionNumber.Stop, true, null, TacticalTexts.Stop);
                _tacticToggle = Button(111, 132, 24, 52, TacticalBitmaps.TacticToggleUp, TacticalBitmaps.TacticToggleDown, TacticalTexts.SurroundOrStandOff, () => _tactic = _tactic == TacticalTactic.Surround ? TacticalTactic.StandOff : TacticalTactic.Surround);
                var oldMission = _mission;
        TacticalTactic oldTactic = _tactic;
                Button(66, 202, 27, 25, TacticalBitmaps.AssignUp, TacticalBitmaps.AssignDown, TacticalTexts.AssignManeuver, () =>
                {
                    var m = _mission;
        TacticalTactic t = _tactic;
                    Ui.ClosePage();
                    if (m != oldMission) Ui.AssignMission(m);
                    if (t != oldTactic) Ui.AssignTactic(t);
                });
                Button(106, 202, 27, 25, TacticalBitmaps.CancelUp, TacticalBitmaps.CancelDown, TacticalTexts.Cancel, Ui.ClosePage);
                // a maneuver radio starts checked only when the current UI mission is one (Inferred)
                if (_mission is not (TacticalMissionNumber.LeftHook or TacticalMissionNumber.RightHook or TacticalMissionNumber.Hammer or TacticalMissionNumber.Anvil or TacticalMissionNumber.Stop)) _mission = TacticalMissionNumber.None;
                break;
            case TacticalPage.Options:
                _withdraw = Button(18, 19, 48, 34, TacticalBitmaps.WithdrawUp, TacticalBitmaps.WithdrawDown, TacticalTexts.Withdraw, Ui.WithdrawPressed);
                _immediate = Button(14, 93, 44, 25, TacticalBitmaps.ImmediateResultUp, TacticalBitmaps.ImmediateResultDown, TacticalTexts.ImmediateResult, () => Ui.Command(TacticalCommand.Abort));
                _observe = Button(87, 93, 45, 25, TacticalBitmaps.ObserveUp, TacticalBitmaps.ObserveDown, TacticalTexts.ObserveOrTakeCommand, () => Ui.Command(TacticalCommand.ComputerControl));
                Button(23, 184, 52, 33, TacticalBitmaps.OptionsScreenUp, TacticalBitmaps.OptionsScreenDown, TacticalTexts.PanelGameOptions, () =>
                {
                    if (!_s.Battle.Paused) _s.TogglePause();
                    _s.OptionsRequested?.Invoke();
                });
                Button(97, 184, 29, 33, TacticalBitmaps.CloseUp, TacticalBitmaps.CloseDown, TacticalTexts.PanelClose, () => { Ui.ShowPage(TacticalPage.Empty); Ui.Command(TacticalCommand.CloseOptions); });
                break;
            case TacticalPage.WithdrawConfirm:
                Button(66, 202, 27, 25, TacticalBitmaps.AssignUp, TacticalBitmaps.AssignDown, TacticalTexts.ConfirmWithdraw, () => Ui.WithdrawAnswer(true));
                Button(106, 202, 27, 25, TacticalBitmaps.CancelUp, TacticalBitmaps.CancelDown, TacticalTexts.Cancel, () => Ui.WithdrawAnswer(false));
                break;
        }
    }

    /// <summary>"Maneuvers &amp; Tactics" (12, 210): the maneuvers page and "Missions" (80, 210): the missions page. The tooltip is TEXTTACT 56781. Its "&amp;" is stripped, so the original shows "Maneuvers  Tactics" with two spaces (Inferred). TEXTTACT 44473 has the same text and seems unused.</summary>
    void OrderButtons()
    {
        _orders1 = Button(12, 210, 58, 22, TacticalBitmaps.ManeuversUp, TacticalBitmaps.ManeuversDown, TacticalTexts.ManeuversAndTactics, () => Ui.ShowPage(TacticalPage.Tactics, _object));
        _orders2 = Button(80, 210, 58, 22, TacticalBitmaps.MissionsUp, TacticalBitmaps.MissionsDown, TacticalTexts.Missions, () => Ui.ShowPage(TacticalPage.Missions, _object));
    }

    void Radio(int x, int y, int w, int h, int up, int down, TacticalMissionNumber value, bool enabled, int? disabled, int tooltip = 0)
    {
        var b = Button(x, y, w, h, up, down, tooltip, () => _mission = value, disabled);
        b.Disabled = !enabled;

        _radios.Add((b, value));
    }

    /// <summary>
    /// Right-panel texts: white, font 8 on the ship, fighter and task-force ship pages, font 10 on the withdraw page. Text items are clipped and not fixed in size, so each is measured on
    /// every text set and drawn whole from the top-left at (x, y). The w x h rect is not used. A centred item loses its centring and starts at x - 1.
    /// </summary>
    void Text(int x, int y, int w, int h, string text, TextFormat format = default, int font = 8) =>
        Assets.DrawTextItem(this, new Rect2(x, y, w, h), text, font, Colors.White, format, new TextItemSize(Clipped: true));

    static string T(int id) => TacticalScreen.Text(id);

    string Line(List<object> parts) => string.Join(" ", parts.Select(p => p is int id ? T(id) : p is TacticalUnit u ? _s.Label(u.Object) : ""));

    /// <summary>A 0-100 bar (fill colour Inferred).</summary>
    void Bar(int x, int y, int w, int h, double value)
    {
        var (r, c) = (new Rect2(x, y, (float)(w * Math.Clamp(value, 0, 100) / 100), h), new Color(0.2f, 0.85f, 0.2f));
        DrawRect(r, c);
        DrawLog.Fill(this, r, c);
    }

    public override void _Draw()
    {
        int bg = _page switch { TacticalPage.Ship or TacticalPage.TaskForceShip => TacticalBitmaps.ShipPage, TacticalPage.FighterGroup => TacticalBitmaps.FighterGroupPage, TacticalPage.Missions => TacticalBitmaps.MissionsPage, TacticalPage.Tactics => TacticalBitmaps.ManeuversPage, TacticalPage.Options => TacticalBitmaps.GameOptionsPage, TacticalPage.WithdrawConfirm => TacticalBitmaps.WithdrawPage, _ => _s.Battle.Local == 0 ? TacticalBitmaps.BlankPageAlliance : TacticalBitmaps.BlankPageEmpire };
        DrawTextureRect(Bmp(bg), new Rect2(0, 0, 149, 236), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(bg), new Rect2(0, 0, 149, 236));
        switch (_page)
        {
            case TacticalPage.Ship or TacticalPage.TaskForceShip when Unit is { } u: DrawShip(u); break;
            case TacticalPage.FighterGroup when _object is TacticalGroup g: DrawFighters(g); break;
            case TacticalPage.Missions: DrawDiagram(_s.Battle.Local == 0 ? _mission switch { TacticalMissionNumber.AttackCapitalShips => TacticalBitmaps.AttackCapitalShipsDiagramAlliance, TacticalMissionNumber.Recover => TacticalBitmaps.RecoverDiagramAlliance, TacticalMissionNumber.AttackFighters => TacticalBitmaps.AttackFightersDiagramAlliance, TacticalMissionNumber.AttackDeathStar => TacticalBitmaps.AttackDeathStarDiagram, _ => TacticalBitmaps.NoMissionDiagram } : _mission switch { TacticalMissionNumber.AttackCapitalShips => TacticalBitmaps.AttackCapitalShipsDiagramEmpire, TacticalMissionNumber.Recover => TacticalBitmaps.RecoverDiagramEmpire, TacticalMissionNumber.AttackFighters => TacticalBitmaps.AttackFightersDiagramEmpire, _ => TacticalBitmaps.NoMissionDiagram }); break; // order of 2151-2157 Inferred
            case TacticalPage.Tactics: if (Array.IndexOf(ManeuverMissions, _mission) is var d and >= 0) DrawDiagram(TacticalBitmaps.ManeuverDiagramBase + d); break;
            case TacticalPage.WithdrawConfirm:
                // two text items: format 0 (the line feed in 56771 gives two lines, no wrap). Each rect is the measured text, so nothing is cut.
                Text(20, 28, 0, 0, T(TacticalTexts.WithdrawConfirmation), TextFormat.TopLeft, 10);
                Text(14, 92, 0, 0, T(TacticalTexts.WithdrawQuestion), TextFormat.TopLeft, 10);
                break;
        }
    }

    void DrawDiagram(int id) { DrawTextureRect(Bmp(id), new Rect2(14, 14, 124, 106), false); if (DrawLog.On) DrawLog.Bitmap(this, Bmp(id), new Rect2(14, 14, 124, 106)); }

    /// <summary>
    /// Ship page (ship / task-force ship): picture 2001 + type index (96 x 39) at (26, 17); name, task-force line, orders and tactics lines; hull and
    /// shield bars; the five system icons (bases 1201, 1206, 1211, 1216, 1221 + level); the officer strip with the 1350 scroll bar.
    /// </summary>
    void DrawShip(TacticalUnit u)
    {
        if (!u.Fighter) DrawTextureRect(Bmp(TacticalBitmaps.UnitPictureBase + u.TypeIndex), new Rect2(26, 17, 96, 39), false);
        if (DrawLog.On && !u.Fighter) DrawLog.Bitmap(this, Bmp(TacticalBitmaps.UnitPictureBase + u.TypeIndex), new Rect2(26, 17, 96, 39));
        Text(26, 7, 97, 9, _s.Label(u.Object));
        int slot = Ui.SlotOf(u.UiGroup);
        Text(26, 52, 97, 9, slot >= 0 ? T(TacticalTexts.TaskForceNumber) + (slot + 1) : T(TacticalTexts.TaskForceUnassigned));
        Bar(34, 81, 37, 10, u.HullMax > 0 ? 100 * u.Hull / u.HullMax : 0);
        Bar(103, 81, 37, 10, u.ShieldMax > 0 ? 100 * u.Shield / u.ShieldMax : 0);
        int[] bases = [TacticalBitmaps.ShieldRechargeIconBase, TacticalBitmaps.WeaponRechargeIconBase, TacticalBitmaps.TractorBeamIconBase, TacticalBitmaps.EngineIconBase, TacticalBitmaps.HyperdriveIconBase];
        var levels = Ui.SystemLevels(u);
        for (int i = 0; i < 5; i++) DrawTextureRect(Bmp(bases[i] + levels[i]), new Rect2(10 + 27 * i, 103, 22, 15), false);
        if (DrawLog.On) for (int i = 0; i < 5; i++) DrawLog.Bitmap(this, Bmp(bases[i] + levels[i]), new Rect2(10 + 27 * i, 103, 22, 15));
        OfficerStrip(u);
        Text(15, 182, 121, 9, Line(Ui.OrdersLine(u.Side, u.UiGroup, u.Group, u)));
        if (u.Side == _s.Battle.Local) Text(15, 193, 121, 9, T(TacticalTexts.Tactics).TrimEnd() + " " + T((u.UiGroup?.Tactic ?? TacticalTactic.StandOff) == TacticalTactic.Surround ? TacticalTexts.Surround : TacticalTexts.StandOff));
    }

    /// <summary>
    /// Officer strip (13, 126) 124 x 45 with the horizontal scroll bar of skin TACTICAL RCDATA 1350 at its bottom (thickness word 0;
    /// geometry Inferred): the unit's officer records, each its TACTICAL icon (the record's picture number: bitmap; none: not
    /// drawn) left to right at its own size, as many as fit (scrolling Inferred, not built).
    /// </summary>
    void OfficerStrip(TacticalUnit u)
    {
        if (Assets.Module(TacticalScreen.Dll).Find(ResId.RcData, TacticalRcData.OfficerStripScrollSkin) is not { } r) return;
        var k = ScrollSkin.Decode(r.Data.Span);
        float y = 126 + 45 - k.Thickness, x0 = 13, x1 = 13 + 124;
        float at = x0;
        foreach (var o in Ui.Officers(u))
        {
            int icon = _s.Definitions.Resolve(o.Type) is { } rec ? TacticalUi.OfficerIcon(ModArt.PictureNumber(rec.Common.StringId)) : 0;
            if (icon == 0 || Bmp(icon) is not { } tex) continue;
            var size = Assets.BitmapSize((ushort)icon, TacticalScreen.Dll);
            if (at + size.X > x1) break;
            DrawTextureRect(tex, new Rect2(at, 126, size.X, Math.Min(size.Y, 45 - k.Thickness)), false);
            DrawLog.Bitmap(this, tex, new Rect2(at, 126, size.X, Math.Min(size.Y, 45 - k.Thickness)));
            at += size.X;
        }
        Vector2 Dim(ushort id) => Assets.BitmapSize(id, TacticalScreen.Dll);
        float left = Dim(k.Up).X, right = Dim(k.Down).X;
        var track = Bmp(k.Track);
        for (float x = x0 + left; x < x1 - right; x += Dim(k.Track).X)
        {
            float part = Math.Min(Dim(k.Track).X, x1 - right - x);
            DrawTextureRectRegion(track, new Rect2(x, y, part, k.Thickness), new Rect2(0, 0, track.GetWidth() * part / Dim(k.Track).X, track.GetHeight()));
            if (DrawLog.On) DrawLog.Bitmap(this, track, new Rect2(x, y, part, k.Thickness), new Rect2(0, 0, part, Dim(k.Track).Y));
        }
        DrawTextureRect(Bmp(k.Up), new Rect2(x0, y, left, k.Thickness), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(k.Up), new Rect2(x0, y, left, k.Thickness));
        DrawTextureRect(Bmp(k.Down), new Rect2(x1 - right, y, right, k.Thickness), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(k.Down), new Rect2(x1 - right, y, right, k.Thickness));
    }

    /// <summary>Fighter page: name 56712 + c, orders and tactics lines, picture 2001 + the first type's index (102 x 107), the count line of that type.</summary>
    void DrawFighters(TacticalGroup g)
    {
        Text(26, 9, 97, 9, T(TacticalTexts.GroupNameBase + Math.Max(0, g.Colour)), TextFormat.Centred);
        var first = g.UiMembers.FirstOrDefault();
        Text(20, 20, 102, 9, Line(Ui.OrdersLine(g.Side, g, g, first)));
        Text(20, 31, 102, 9, T(TacticalTexts.Tactics).TrimEnd() + " " + T(g.Tactic == TacticalTactic.Surround ? TacticalTexts.Surround : TacticalTexts.StandOff));
        if (first is null) return;
        DrawTextureRect(Bmp(TacticalBitmaps.UnitPictureBase + first.TypeIndex), new Rect2(23, 59, 102, 107), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(TacticalBitmaps.UnitPictureBase + first.TypeIndex), new Rect2(23, 59, 102, 107));
        int label = first.TypeIndex is >= TacticalTypeIndex.FirstFighter and <= TacticalTypeIndex.LastFighter
            ? TacticalTexts.FighterCountBase + first.TypeIndex - TacticalTypeIndex.FirstFighter : TacticalTexts.GenericFighterCount;
        Text(23, 146, 102, 9, $"{T(label)} {g.UiMembers.Count(u => u.Object.Type == first.Object.Type && u.Active)}", TextFormat.Centred);
    }
}
