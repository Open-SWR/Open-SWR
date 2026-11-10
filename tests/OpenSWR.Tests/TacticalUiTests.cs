using OpenSWR.Sim;
using static OpenSWR.Tests.TacticalTests;

namespace OpenSWR.Tests;

/// <summary>The UI controller, its modes and order tables, commands, boxes and cameras.</summary>
public class TacticalUiTests
{
    /// <summary>
    /// Three Alliance ships against two Imperial ones, the Alliance local and in command; the Empire computer-controlled. The own
    /// ships leave their setup task force first, so the tests start from free HUD slots.
    /// </summary>
    static (TacticalBattle B, TacticalUi Ui, List<TacticalUnit> A, List<TacticalUnit> E) Setup(int ships = 3)
    {
        var (_, b, a, e) = Scene(new Script([], []), [.. Enumerable.Repeat(0x14000042u, ships)], [0x14000085, 0x14000085]);
        b.Computer[1] = true;
        foreach (var u in a) b.LeaveTaskForce(u);
        b.RunQueued();
        Assert.All(b.Ui.TaskForces, Assert.Null);
        return (b, b.Ui, a, e);
    }

    static string K(IEnumerable<object> xs) => string.Join(",", xs.Select(x => x is TacticalUnit u ? u.Key : ((TacticalNavPoint)x).Key));

    static void Left(TacticalUi ui, object? hit, bool ctrl = false) { ui.LeftDown(hit, ctrl); ui.Frame(); }
    static void Right(TacticalUi ui, object? hit, bool ctrl = false) { ui.RightDown(hit, ctrl); ui.Frame(); }
    static void Do(TacticalUi ui, int command) { ui.Command(command); ui.Frame(); }

    static TacticalNavPoint ShownNav(TacticalBattle b, TacticalUi ui, int i = 0)
    {
        if (!ui.NavShown[0]) Do(ui, 0x13); // nav button 4 -> list 0 (set 1)
        return b.NavPoints.Where(p => p.Set == 1).ElementAt(i);
    }

    /// <summary>Selecting: a plain left click passes through the empty pick ( -> idle) and selects: own -> own-ship mode (page 0x16), enemy -> enemy mode (marked), nav point -> nav mode; hidden nav points cannot be picked; observing, a right click only selects.</summary>
    [GameFact]
    public void LeftClickSelects()
    {
        var (b, ui, a, e) = Setup();
        Left(ui, a[1]);
        Assert.Equal((0xE, a[1], 0x16), (ui.Current.Id, ui.Info, ui.Page));
        Assert.Equal(TacticalBox.Selected, ui.Box(a[1]));
        Left(ui, e[0]);
        Assert.Equal((0xF, TacticalBox.Marked), (ui.Current.Id, ui.Box(e[0])));
        Left(ui, null);
        Assert.Equal((0, 0x15), (ui.Current.Id, ui.Page));
        Left(ui, b.NavPoints[0]); // hidden: nothing after the empty pick
        Assert.Equal(0, ui.Current.Id);
        var n = ShownNav(b, ui);
        Left(ui, n);
        Assert.Equal((0xD, TacticalNavStyle.Other), (ui.Current.Id, ui.NavStyle(n)));
        Left(ui, a[0]);
        Do(ui, 0x7B); // observe
        Assert.False(ui.InCommand);
        Right(ui, e[1]);
        Assert.Equal(0xF, ui.Current.Id);
        Assert.Empty(b.QueuedKinds());
    }

    /// <summary>Own-ship mode, own unit: ctrl 0 = escort (S's unassigned task force, else a new one; voice 0x77; replace 0x36; mission 1), S leaving a numbered task force; ctrl 1 = multi-selection {S, U}.</summary>
    [GameFact]
    public void OwnShipEscortsAndMultiSelects()
    {
        var (b, ui, a, _) = Setup();
        Left(ui, a[0]);
        Do(ui, 2 + 3); // empty slot 4 in own-ship mode -> numbered task force of {S}
        Assert.Equal(4, ui.Current.Id);
        var numbered = ui.TaskForces[3]!;
        Left(ui, a[0]);
        Right(ui, a[1]);
        var t = a[0].UiGroup!;
        Assert.NotSame(numbered, t);
        Assert.Null(ui.TaskForces[3]); // its last member left: slot freed
        Assert.Equal((K([a[1]]), 1, 0xE), (K(t.UiTargets), t.UiMission, ui.Current.Id));
        Assert.Equal([0x77], ui.Voices);
        Right(ui, a[2]); // T is now unassigned: kept
        Assert.Same(t, a[0].UiGroup);
        // as read: replace (0x36), then the setter's rule 2 (old UI mission 1) clears the UI target list again; at the dispatch the
        // 0x0A handler (old mission 1) clears the simulation list after the 0x36 has set it
        Assert.Empty(t.UiTargets);
        b.RunQueued();
        Assert.Empty(b.TargetList(t));
        Right(ui, a[1], ctrl: true);
        var multi = Assert.IsType<TacticalUi.MultiMode>(ui.Current);
        Assert.Equal([a[0], a[1]], multi.Selection);
    }

    /// <summary>Own-ship mode, enemy unit and nav points: ctrl 0 = mission 0 + replace (voice 0x3B with no nav points); ctrl 1 = add / remove (Escort: mission 0 + replace); not attackable -> enemy mode; nav ctrl 0 = replace (voice 0x21), ctrl 1 = add / remove; Space = the nearest enemy capital ship.</summary>
    [GameFact]
    public void OwnShipAttacksAndNavigates()
    {
        var (b, ui, a, e) = Setup();
        Left(ui, a[0]);
        Right(ui, e[1]);
        var t = a[0].UiGroup!;
        Assert.Equal((K([e[1]]), 0), (K(t.UiTargets), t.UiMission));
        Assert.Equal([0x3B], ui.Voices);
        Right(ui, e[0], ctrl: true);
        Assert.Equal([e[1], e[0]], t.UiTargets);
        Right(ui, e[1], ctrl: true);
        Assert.Equal([e[0]], t.UiTargets);
        Assert.Equal(TacticalBox.Primary, ui.Box(e[0]));
        var n0 = ShownNav(b, ui, 0);
        var n1 = ShownNav(b, ui, 1);
        Right(ui, n0);
        Assert.Equal((K([n0]), 0), (K(t.UiNav), t.UiTargets.Count));
        Assert.Equal(0x21, ui.Voices[^1]);
        Right(ui, n1, ctrl: true);
        Assert.Equal([n0, n1], t.UiNav);
        Assert.Equal((TacticalNavStyle.First, TacticalNavStyle.Other), (ui.NavStyle(n0), ui.NavStyle(n1)));
        Right(ui, n0, ctrl: true);
        Assert.Equal([n1], t.UiNav);
        Do(ui, 0x20); // Space
        Assert.Equal([b.Nearest(a[0].Position, 0)!], t.UiTargets);
        Assert.False(t.UiTargets[0].Fighter);
        e[1].U.State = 7;
        Right(ui, e[1]);
        Assert.Equal(0xF, ui.Current.Id);
    }

    /// <summary>The mission page makes a new task force for S every time (voice 0x77); the tactic too (0x3D); next / previous walk the side's capital ships with wrap; S inactive -> idle.</summary>
    [GameFact]
    public void OwnShipMissionsAndCycling()
    {
        var (b, ui, a, _) = Setup();
        Left(ui, a[0]);
        ui.AssignMission(5);
        var first = a[0].UiGroup!;
        ui.AssignMission(4);
        Assert.NotSame(first, a[0].UiGroup);
        Assert.Equal((4, 0x77), (a[0].UiGroup!.UiMission, ui.Voices[^1]));
        ui.AssignTactic(1);
        Assert.Equal(1, a[0].UiGroup!.Tactic);
        ui.Cycle(false);
        ui.Frame();
        Assert.Equal(a[2], ((TacticalUi.OwnShipMode)ui.Current).S);
        ui.Cycle(true);
        ui.Frame();
        Assert.Equal(a[0], ((TacticalUi.OwnShipMode)ui.Current).S);
        a[0].U.State = 11;
        ui.Frame();
        Assert.Equal(0, ui.Current.Id);
    }

    /// <summary>Multi-selection: ctrl 1 adds / removes (one left -> own-ship mode); own U ctrl 0 -> numbered task force in the first free slot escorting U (mission 1, TF voice) -> TF mode; enemy with a slot -> voice 0x3C + k, pending target, TF mode.</summary>
    [GameFact]
    public void MultiSelectionMakesNumberedTaskForces()
    {
        var (b, ui, a, e) = Setup();
        Left(ui, a[0]);
        Left(ui, a[1], ctrl: true);
        Left(ui, a[2], ctrl: true);
        var multi = (TacticalUi.MultiMode)ui.Current;
        Assert.Equal([a[0], a[1], a[2]], multi.Selection);
        Assert.All(a, u => Assert.Equal(TacticalBox.Selected, ui.Box(u))); // Every selected unit is yellow
        Left(ui, a[2], ctrl: true);
        Assert.Equal(0x10, ui.Current.Id);
        Right(ui, a[2]); // escort a[2]: slot 0
        Assert.Equal(1, ui.Current.Id);
        var t = ui.TaskForces[0]!;
        Assert.Equal((K([a[0], a[1]]), K([a[2]]), 1), (K(t.UiMembers), K(t.UiTargets), t.UiMission));
        Assert.Equal([0x78], ui.Voices);
        Assert.Equal([0x30, 0x33, 0x34, 0x0A], b.QueuedKinds());
        Left(ui, a[2]);
        Left(ui, a[1], ctrl: true);
        Right(ui, e[0]); // slot 1 (0 taken)
        Assert.Equal(2, ui.Current.Id);
        Assert.Equal(0x3C + 1, ui.Voices[^1]);
        Assert.Equal([e[0]], ui.TaskForces[1]!.UiTargets);
        Assert.Equal([a[2], a[1]], ui.TaskForces[1]!.UiMembers);
        Assert.DoesNotContain(a[1], t.UiMembers);
    }

    /// <summary>Without a free slot: enemy -> every selected unit its own task force with add-target (voice 0x84) -> own-ship mode of the first; nav likewise; mission -> each its own task force; own U ctrl 0 -> own-ship mode of U.</summary>
    [GameFact]
    public void MultiSelectionWithoutAFreeSlot()
    {
        var (b, ui, a, e) = Setup();
        var spare = Tf(b, a[2]);
        for (int i = 0; i < 8; i++) ui.Bind(i, spare);
        Left(ui, a[0]);
        Left(ui, a[1], ctrl: true);
        Right(ui, e[0]);
        Assert.Equal((0xE, 0x84), (ui.Current.Id, ui.Voices[^1]));
        Assert.NotSame(a[0].UiGroup, a[1].UiGroup);
        Assert.Equal([e[0]], a[0].UiGroup!.UiTargets);
        Assert.Equal([e[0]], a[1].UiGroup!.UiTargets);
        Left(ui, a[1], ctrl: true);
        ui.AssignMission(5);
        Assert.Equal((0xE, 5, 5), (ui.Current.Id, a[0].UiGroup!.UiMission, a[1].UiGroup!.UiMission));
        Left(ui, a[1], ctrl: true);
        Right(ui, a[2]);
        Assert.Equal(a[2], ((TacticalUi.OwnShipMode)ui.Current).S);
    }

    /// <summary>Task-force mode, own units: a member ctrl 0 splits (the others form a numbered task force escorting it); a non-member ctrl 0 = escort (mission 1, voice 0x78 + k); ctrl 1 = remove target (mission 0) / join (0x37, 0x33) / leave; the last member leaving -> idle.</summary>
    [GameFact]
    public void TaskForceModeOwnUnits()
    {
        var (b, ui, a, _) = Setup();
        Left(ui, a[0]);
        Left(ui, a[1], ctrl: true);
        Do(ui, 2); // empty slot 1 -> TF mode 1
        var t = ui.TaskForces[0]!;
        Assert.Equal(1, ui.Current.Id);
        Right(ui, a[2]);
        Assert.Equal((K([a[2]]), 1, 0x78), (K(t.UiTargets), t.UiMission, ui.Voices[^1]));
        Right(ui, a[2], ctrl: true);
        Assert.Equal((0, 0), (t.UiTargets.Count, t.UiMission));
        b.RunQueued();
        Right(ui, a[2], ctrl: true); // join
        Assert.Equal([a[0], a[1], a[2]], t.UiMembers);
        Assert.Equal([0x33], b.QueuedKinds().Skip(b.QueuedKinds().Count - 1));
        Right(ui, a[0]); // split: a[1], a[2] -> slot 2 escorting a[0]
        Assert.Equal(2, ui.Current.Id);
        Assert.Equal([a[0]], t.UiMembers);
        Assert.Equal([a[0]], ui.TaskForces[1]!.UiTargets);
        Do(ui, 2);
        Right(ui, a[0], ctrl: true); // the last member leaves
        Assert.Equal(0, ui.Current.Id);
        Assert.Null(ui.TaskForces[0]);
    }

    /// <summary>Task-force mode, enemy units and nav points: ctrl 0 = mission 0 + replace (voice 0x3C + k with no nav points); ctrl 1 = add / remove, not attackable -> enemy mode; nav ctrl 0 = replace (voice 0x22 + k), ctrl 1 = add / remove; the button again -> idle; UI mission 3 -> idle; next / previous cycle the info unit.</summary>
    [GameFact]
    public void TaskForceModeEnemiesAndNavs()
    {
        var (b, ui, a, e) = Setup();
        Left(ui, a[0]);
        Left(ui, a[1], ctrl: true);
        Do(ui, 3); // slot 2
        var t = ui.TaskForces[1]!;
        Right(ui, e[0]);
        Assert.Equal((K([e[0]]), 0x3C + 1), (K(t.UiTargets), ui.Voices[^1]));
        Right(ui, e[1], ctrl: true);
        Assert.Equal([e[0], e[1]], t.UiTargets);
        Assert.Equal((TacticalBox.Primary, TacticalBox.Marked, TacticalBox.Marked), (ui.Box(e[0]), ui.Box(e[1]), ui.Box(a[0] == ui.Info ? a[1] : a[0])));
        Right(ui, e[0], ctrl: true);
        Assert.Equal([e[1]], t.UiTargets);
        e[0].U.State = 8;
        Right(ui, e[0], ctrl: true);
        Assert.Equal(0xF, ui.Current.Id);
        Do(ui, 3);
        var n = ShownNav(b, ui, 3);
        Right(ui, n);
        Assert.Equal((K([n]), 0, 0x22 + 1), (K(t.UiNav), t.UiTargets.Count, ui.Voices[^1]));
        Right(ui, n, ctrl: true);
        Assert.Empty(t.UiNav);
        var info = ui.Info;
        ui.Cycle(true);
        ui.Frame();
        Assert.NotEqual(info, ui.Info);
        Assert.Equal(2, ui.Current.Id);
        Do(ui, 3);
        Assert.Equal(0, ui.Current.Id);
        Do(ui, 3);
        b.Withdraw(0);
        ui.Frame();
        Assert.Equal(0, ui.Current.Id);
    }

    /// <summary>Fighter-group mode: commands 0xB + c (used slots; toggle -> idle), page 0x17; enemy ctrl 0 = mission 0 + replace (voice 0x44 + c), ctrl 1 = add / remove; own = escort; nav = replace (voice 0x2A + c); mission 6 without a Death Star -> idle; mission 2 frees the slot -> idle.</summary>
    [GameFact]
    public void FighterGroupMode()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085, 0x14000085]);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        var b = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var ui = b.Ui;
        var e = b.Units.Where(u => u.Side == 1).ToList();
        var ship = b.Units.Single(u => u.Side == 0 && !u.Fighter);
        Do(ui, 0xC); // Blue: empty
        Assert.Equal(0, ui.Current.Id);
        Do(ui, 0xB);
        Assert.Equal((9, 0x17), (ui.Current.Id, ui.Page));
        var g = ui.Fighters[0]!;
        Right(ui, e[0]);
        Assert.Equal((K([e[0]]), 0x44), (K(g.UiTargets), ui.Voices[^1]));
        Right(ui, e[1], ctrl: true);
        Right(ui, e[0], ctrl: true);
        Assert.Equal([e[1]], g.UiTargets);
        Right(ui, ship);
        Assert.Equal((K([ship]), 1), (K(g.UiTargets), g.UiMission));
        var n = ShownNav(b, ui);
        Right(ui, n);
        Assert.Equal((K([n]), 0x2A), (K(g.UiNav), ui.Voices[^1]));
        ui.AssignMission(6);
        Assert.Equal(0, ui.Current.Id);
        Do(ui, 0xB);
        Do(ui, 0xB);
        Assert.Equal(0, ui.Current.Id);
        Do(ui, 0xB);
        ui.AssignMission(2);
        Assert.Null(ui.Fighters[0]);
        ui.Frame();
        Assert.Equal(0, ui.Current.Id);
    }

    /// <summary>An empty task-force button reacts only in own-ship / multi mode; nav buttons toggle list 3 ... 0; highlight marks a side; Observe flips in command and the computer flag; Withdraw -> observing (side state 2); Get Immediate Result only while the opponent is in state 0.</summary>
    [GameFact]
    public void Commands()
    {
        BattleEngine.RetreatAllowedOverride = (_, _) => true;
        try
        {
            var (b, ui, a, e) = Setup();
            ui.PressTaskForce(0);
            ui.Frame();
            Assert.Equal(0, ui.Current.Id);
            Assert.False(ui.TaskForceEnabled(0));
            Do(ui, 0x10);
            Assert.Equal((true, false), (ui.NavShown[3], ui.NavShown[0]));
            Assert.All(b.NavPoints.Where(p => p.Set == 4), p => Assert.False(p.Hidden));
            Do(ui, 0x15);
            Assert.Equal(TacticalBox.Marked, ui.Box(e[0]));
            Do(ui, 0x7B);
            Assert.Equal((false, true), (ui.InCommand, b.Computer[0]));
            Do(ui, 0x7B);
            Assert.Equal((true, false), (ui.InCommand, b.Computer[0]));
            b.Withdraw(1);
            Do(ui, 0x7C);
            Assert.False(b.Done);
            Do(ui, 0x7A);
            Assert.Equal((false, 2), (ui.InCommand, b.SideState(0)));
            Left(ui, e[0]); // observing: selection only
            Right(ui, a[0]);
            Assert.Equal(0xE, ui.Current.Id);
        }
        finally { BattleEngine.RetreatAllowedOverride = null; }
    }

    /// <summary>Page switch: 0x15 always; order pages lock the panel (Inferred) until closed; page 0 = the previous page.</summary>
    [GameFact]
    public void PageSwitch()
    {
        var (_, ui, a, _) = Setup();
        Left(ui, a[0]);
        ui.ShowPage(0x19, a[0]);
        Assert.True(ui.Locked);
        Left(ui, a[1]); // own-ship mode of a[1]: page 0x15 by the empty pick (clears the lock), then 0x16
        Assert.Equal((0x16, a[1]), (ui.Page, ui.PageObject));
        ui.ShowPage(0x1B);
        ui.ShowPage(0x16, a[0]);
        Assert.Equal(0x1B, ui.Page);
        ui.ClosePage();
        Assert.Equal((0x16, false), (ui.Page, ui.Locked));
        ui.ShowPage(0x1B);
        BattleEngine.RetreatAllowedOverride = (_, _) => true;
        try
        {
            var (b2, ui2, _, _) = Setup();
            ui2.ShowPage(0x1B);
            ui2.WithdrawPressed(); // 0x1C is 0x1B's child window, 0x1B stays shown and locked
            Assert.Equal((0x1B, true, true), (ui2.Page, ui2.Locked, ui2.WithdrawShown));
            ui2.WithdrawAnswer(false);
            Assert.Equal((0x1B, true, false), (ui2.Page, ui2.Locked, ui2.WithdrawShown));
            ui2.WithdrawPressed();
            ui2.WithdrawAnswer(true);
            ui2.Frame(); // 0x7A unlocks
            Assert.Equal((0x1B, false, 2), (ui2.Page, ui2.Locked, b2.SideState(b2.Local)));
        }
        finally { BattleEngine.RetreatAllowedOverride = null; }
        ui.WithdrawPressed(); // may not retreat: nothing
        Assert.False(ui.WithdrawShown);
        ui.ShowPage(0x15);
        ui.ShowPage(0x1C); // 0x1C does not lock
        Assert.False(ui.Locked);
        ui.ShowPage(0x1A, a[0]);
        Assert.True(ui.Locked);
    }

    /// <summary>Orbit camera: reset pitch 30 / yaw -30 (Alliance), f 0.2, D = 1.7*W; zoom x 0.9 with z -1, step = clamp(z, 1, 5) degrees, yaw wrapped; remember / reset; subject; follow camera on a task force (field 0.1 within [0.005, 0.2]) until its subject is gone.</summary>
    [GameFact]
    public void Cameras()
    {
        var (b, ui, a, e) = Setup();
        var c = ui.Camera;
        Assert.Equal((-30, 30, 0.2, 5), (c.Yaw, c.Pitch, c.Field, c.Zoom));
        Assert.Equal(1.7 * b.Size, c.Distance);
        Assert.Equal(1.7 * b.Size, (c.Pose().Eye - c.Pose().At).Length, 9);
        c.Operate(TacticalCamera.ZoomIn);
        c.Operate(TacticalCamera.ZoomIn);
        Assert.Equal((0.2 * 0.9 * 0.9, 3), (c.Field, c.Zoom));
        c.Operate(TacticalCamera.RotateLeft);
        Assert.Equal(-33, c.Yaw);
        for (int i = 0; i < 60; i++) c.Operate(TacticalCamera.RotateLeft);
        Assert.Equal(-33 - 180 + 360, c.Yaw);
        c.Operate(TacticalCamera.Remember);
        c.Operate(TacticalCamera.TiltUp);
        c.Operate(TacticalCamera.ResetView);
        Assert.Equal((147, 30), (c.Yaw, c.Pitch));
        Left(ui, a[0]);
        c.Operate(TacticalCamera.ResetSubject, ui.Current.Subject);
        Assert.Same(a[0], c.Subject);
        Assert.Equal(a[0].Position, c.Pose().At);
        Do(ui, 2); // TF 1 of a[0]
        Right(ui, e[0]);
        b.RunQueued();
        c.FollowGroup(0, ui);
        Assert.Equal((a[0], e[0], 0.1), (c.Follow!.Subject, c.Follow.LookAt, c.Follow.Field));
        Assert.Equal(TacticalBox.Hidden, ui.Box(a[0]));
        for (int i = 0; i < 8; i++) c.Operate(TacticalCamera.ZoomOut);
        Assert.Equal(0.2, c.Follow.Field);
        c.Operate(TacticalCamera.RotateLeft); // ignored
        Assert.Equal(147, c.Yaw);
        a[0].U.State = 11;
        ui.Frame();
        Assert.Null(c.Follow);
    }

    /// <summary>
    /// System icons: v = trunc(100 * rate / base), levels by v &gt; 74 / 49 / 24 / != 0: ShieldCH 1 -> 75 (4), 2 -> 50 (3), 3 -> 
    /// 25 (2), 4 -> 0 (0); engines above 100 with the bonus still 4; one of two hyperdrives out -> 50 (3).
    /// </summary>
    [GameFact]
    public void SystemIconLevels()
    {
        var (b, ui, a, _) = Setup(1);
        var u = a[0];
        Assert.Equal(4, ui.SystemLevels(u)[3]);
        int[] expected = [4, 4, 3, 2, 0];
        for (int k = 0; k <= 4; k++)
        {
            u.U.ShieldCh = k;
            Assert.Equal(expected[k], ui.SystemLevels(u)[0]);
        }
        (u.U.Hyperdrives, u.U.HyperCh) = (2, 1);
        Assert.Equal(3, ui.SystemLevels(u)[4]);
    }

    /// <summary>A reached nav point leaves the group's UI list and the simulation list at once.</summary>
    [GameFact]
    public void ReachedNavPointLeavesBothLists()
    {
        var (b, ui, a, _) = Setup(1);
        var g = b.CreateTaskForce(a[0]);
        b.RunQueued();
        var p = ShownNav(b, ui);
        b.OrderNav(g, TacticalBattle.AddNavPoint, p);
        b.RunQueued();
        Assert.Equal([p], g.UiNav);
        g.Nav!.Point = p.Position;
        b.Frame(TacticalBattle.FrameMs);
        Assert.Empty(g.UiNav);
        Assert.Empty(g.Waypoints);
    }

    /// <summary>
    /// Officer strip: the ship's records in setup order (characters, then troop regiments, then special forces aboard),
    /// each taking a unit key (the ship's next fighter keys follow them); icons by the SD string number, unknown -> 0.
    /// </summary>
    [GameFact]
    public void OfficerStripListsTheRecordsAboard()
    {
        Assert.Equal((2201, 2310, 2308, 0), (TacticalUi.OfficerIcon(0xA40), TacticalUi.OfficerIcon(0x440), TacticalUi.OfficerIcon(0x540), TacticalUi.OfficerIcon(0x123)));
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        var ship = b0.System.Contents.OfType<Fleet>().Single(f => f.Owner == Owner.Alliance).Contents.Single();
        var sf = ship.Add(new GameObject(Definitions.SpecialForces.First(r => r.Alliance != 0).Common.Ref, Owner.Alliance, game.NextId()));
        var troop = ship.Add(new GameObject(Definitions.Troops.First(r => r.Alliance != 0).Common.Ref, Owner.Alliance, game.NextId()));
        var wing = ship.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        var free = b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        var b = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var unit = b.Units.Single(u => u.Object == ship);
        Assert.Equal([troop, sf], b.Ui.Officers(unit));
        Assert.Equal(2, unit.U.Passengers);
        int Key(GameObject o) => b.Units.Single(u => u.Object == o).U.Key;
        Assert.Equal((1, 2, 5), (Key(ship), Key(wing), Key(free))); // ship, its fighter, its records 3 / 4, then the system's fighter
    }
}
