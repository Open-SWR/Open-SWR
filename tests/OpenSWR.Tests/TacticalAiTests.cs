using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using static OpenSWR.Tests.TacticalTests;

namespace OpenSWR.Tests;

/// <summary>Missions, the target refresh, the tactical scorer / evaluator and the computer side.</summary>
public class TacticalAiTests
{
    static bool Has(CapitalShipRecord r, Func<Weapons, uint> w) => new[] { r.Fore, r.Aft, r.Port, r.Starboard }.Any(x => w(x) > 0);
    static uint AllianceShip(Func<CapitalShipRecord, bool> pick) =>
        Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18 && pick(r)).Common.Ref;
    static uint LaserShip => AllianceShip(r => Has(r, w => w.LaserCannon));
    static uint UnarmedShip => AllianceShip(r => !Has(r, w => w.LaserCannon) && !Has(r, w => w.IonCannon) && !Has(r, w => w.Turbolaser));

    /// <summary>
    /// Target refresh from the group's nav point: a laser leader -> the nearest enemy fighter, an ion / turbolaser leader -> the
    /// nearest enemy capital ship, an unarmed one -> nothing; no fallback to the other type.
    /// </summary>
    [GameFact]
    public void RefreshTakesTheNearestEnemyOfTheLeadersType()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042, LaserShip, UnarmedShip], [0x14000085, 0x14000085]);
        for (int i = 0; i < 2; i++) b0.System.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var a = battle.Units.Where(u => u.Side == 0).ToList();
        var ships = battle.Units.Where(u => u.Side == 1 && !u.Fighter).ToList();
        var ties = battle.Units.Where(u => u.Side == 1 && u.Fighter).ToList();
        var (heavy, laser, unarmed) = (a[0].Group!, a[1].Group!, a[2].Group!);
        Assert.Equal(3, new[] { heavy, laser, unarmed }.Distinct().Count());
        foreach (var g in new[] { heavy, laser, unarmed }) g.Nav!.Point = (0, 0, 0);
        Place(ships[0], 0, 40);
        Place(ships[1], 0, 20);
        Place(ties[0], 0, 10);
        Place(ties[1], 0, 5);
        foreach (var g in new[] { heavy, laser, unarmed }) battle.RefreshTargets(g);
        Assert.Equal([ships[1]], heavy.NearestEnemy);
        Assert.Equal([ties[1]], laser.NearestEnemy);
        Assert.Empty(unarmed.NearestEnemy);
        foreach (var s in ships) s.U.State = 11;
        foreach (var t in ties) t.U.State = 9;
        battle.RefreshTargets(heavy);
        battle.RefreshTargets(laser);
        Assert.Empty(heavy.NearestEnemy); // fighters left, but no fallback
        Assert.Empty(laser.NearestEnemy);
    }

    /// <summary>
    /// Candidates: a capital ship in state 4 stays (active), a fighter in state 10 (inside a Death Star run) is dropped; equal
    /// threats by their threat field unless a character aboard adds 1.0. TTK distance term: + max(1, |t - nav| / group speed) *
    /// 0.001. Assignment: the UI target list at once, 0x36 stamped with the stamp base, the simulation target four turns later.
    /// </summary>
    [GameFact]
    public void TacticalCandidatesScorerAndAssignment()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085, 0x14000085]);
        for (int i = 0; i < 2; i++) b0.System.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var a = battle.Units.Single(u => u.Side == 0);
        var e = battle.Units.Where(u => u.Side == 1 && !u.Fighter).ToList();
        var ties = battle.Units.Where(u => u.Side == 1 && u.Fighter).ToList();
        e[1].U.State = 4;
        ties[0].U.State = 10;
        battle.BuildCandidates(0);
        var c = battle.Candidates(0);
        Assert.Equal([e[0], e[1], ties[1]], c);
        e[1].U.Carried.Add(e[1].Object);
        battle.BuildCandidates(0);
        Assert.Equal([e[1], e[0], ties[1]], battle.Candidates(0));

        var g = a.Group!;
        g.Nav!.Point = (0, 0, 0);
        g.Nav.GroupSpeed = 2;
        Place(e[0], 0, 0);
        double acc = 0, at = battle.TimeToKill(e[0].U, g.G, ref acc);
        Assert.True(at > 0);
        Place(e[0], 0, 30);
        acc = 0;
        Assert.Equal(at + 15 * 0.001, battle.TimeToKill(e[0].U, g.G, ref acc), 9);
        Place(e[0], 0, 1);
        acc = 0;
        Assert.Equal(at + 1 * 0.001, battle.TimeToKill(e[0].U, g.G, ref acc), 9);

        battle.Targeting(0);
        Assert.Single(g.UiTargets);
        Assert.Equal(1, battle.Queued(TacticalBattle.ReplaceTargets, 3));
        Assert.Empty(battle.TargetList(g));
        battle.RunQueued();
        Assert.Equal(g.UiTargets, battle.TargetList(g));
    }

    /// <summary>
    /// Timeline, computer side from turn 0: turn 0 the rebuild posts 0x32 for the setup task forces and 0x37 / 0x30 / 0x33 for
    /// new ones (nobody moved), the targeting assigns the old groups; turn 4 the old groups are deleted and the new ones get their
    /// members; turn 6 the targeting assigns the new groups (UI list, 0x36 stamped 9); turn 10 the 0x36 runs (simulation target).
    /// </summary>
    [GameFact]
    public void ComputerRebuildsAndTargetsOnItsTimeline()
    {
        var (_, battle, _, e) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000085, 0x14000085, 0x14000086]);
        battle.SetComputer(1, true);
        var old = battle.Groups.Where(g => g.Side == 1 && !g.Fighters).ToList();
        var at = e.Select(u => u.Position).ToList();
        Assert.NotEmpty(old);
        battle.Step(); // turn 0
        Assert.Equal(old.Count, battle.Queued(0x32, 3));
        var fresh = battle.Groups.Where(g => g.Side == 1 && !g.Fighters && !old.Contains(g)).ToList();
        Assert.Equal(old.Count, fresh.Count);
        Assert.All(fresh, g => Assert.Empty(g.Units));
        Assert.All(old, g => Assert.Single(g.UiTargets));
        Assert.Equal(at, e.Select(u => u.Position));
        while (battle.Turn < 5) battle.Step(); // turns 1-4
        Assert.All(old, g => Assert.DoesNotContain(g, battle.Groups));
        Assert.All(fresh, g => Assert.NotEmpty(g.Units));
        Assert.All(fresh, g => Assert.Empty(g.UiTargets));
        while (battle.Turn < 7) battle.Step(); // turns 5-6
        Assert.All(fresh, g => Assert.Single(g.UiTargets));
        Assert.Equal(fresh.Count, battle.Queued(TacticalBattle.ReplaceTargets, 9));
        Assert.All(fresh, g => Assert.Empty(battle.TargetList(g)));
        while (battle.Turn < 11) battle.Step(); // turns 7-10
        Assert.All(fresh, g => Assert.Equal(g.UiTargets, battle.TargetList(g)));
    }

    /// <summary>openswr deviation: a modded side with five fighter types still gives every squadron a group (four HUD slots; the fifth type joins slot 0).</summary>
    [GameFact]
    public void AFifthFighterTypeJoinsAGroupInsteadOfSittingIdle()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        foreach (var r in Definitions.Fighters.Take(5)) b0.System.Add(new GameObject(r.Common.Ref, Owner.Alliance, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var fighters = battle.Units.Where(u => u.Side == 0 && u.Fighter).ToList();
        Assert.Equal(5, fighters.Count);
        Assert.All(fighters, u => Assert.NotNull(u.Group));
        Assert.All(battle.Ui.Fighters, g => Assert.NotNull(g));
    }

    /// <summary>
    /// Example timing: mission 4 set at turn 10 (0x0A stamped 13) runs at turn 14; that turn's step picks the first wanted
    /// fighter of the side's candidates (done, UI list, 0x36 stamped 17); turns 15-17 nothing; turn 18 the target is set and done
    /// cleared; then the skip test keeps it.
    /// </summary>
    [GameFact]
    public void AttackFightersPicksAndWaitsForTheReplace()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        for (int i = 0; i < 2; i++) b0.System.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        battle.Computer[1] = false;
        var wing = battle.Ui.Fighters[0]!;
        while (battle.Turn < 10) battle.Step();
        Assert.True(battle.SetMission(wing, 4));
        Assert.Equal(1, battle.Queued(0x0A, 13));
        while (battle.Turn < 14) battle.Step();
        Assert.Null(wing.Object);
        battle.Step(); // turn 14
        var tie = battle.Candidates(0).First(u => u.Fighter);
        Assert.Equal((4, true), (wing.Mission, wing.Object!.Done));
        Assert.Equal([tie], wing.UiTargets);
        Assert.Equal(1, battle.Queued(TacticalBattle.ReplaceTargets, 17));
        while (battle.Turn < 18) battle.Step();
        Assert.Equal(1, battle.Queued(TacticalBattle.ReplaceTargets));
        battle.Step(); // turn 18
        Assert.Equal([tie], battle.TargetList(wing));
        Assert.Equal(0, battle.Queued(TacticalBattle.ReplaceTargets));
        battle.Step();
        Assert.Equal(0, battle.Queued(TacticalBattle.ReplaceTargets)); // kept by the skip test (TTK below 300)
    }

    /// <summary>
    /// Fighter rule 4: the first frame that finds the group's target dead sets the target-dead flag and still moves; from the
    /// next frame the group's nav point stays put; 0x34 leaves the flag, 0x35 clears it and the nav point is driven again.
    /// </summary>
    [GameFact]
    public void FighterGroupStopsAfterItsTargetDies()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        for (int i = 0; i < 2; i++) b0.System.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        battle.Computer[1] = false;
        var wing = battle.Ui.Fighters[0]!;
        var ties = battle.Units.Where(u => u.Side == 1 && u.Fighter).ToList();
        battle.Order(wing, TacticalBattle.ReplaceTargets, ties[0]);
        battle.RunQueued();
        wing.Nav!.Point = (0, 0, 0);
        wing.Nav.GroupSpeed = 10; // set per turn by the group update; no turn runs here
        Place(ties[0], 0, 60);
        Place(ties[1], 0, -60);
        battle.Frame(TacticalBattle.FrameMs);
        var moved = wing.NavPoint;
        Assert.NotEqual((V3)(0, 0, 0), moved);
        ties[0].U.State = 11;
        battle.Frame(TacticalBattle.FrameMs); // finds it dead: notification, still moves this frame
        Assert.NotEqual(moved, wing.NavPoint);
        var held = wing.NavPoint;
        for (int i = 0; i < 5; i++) battle.Frame(TacticalBattle.FrameMs);
        Assert.Equal(held, wing.NavPoint);
        battle.Order(wing, TacticalBattle.AddTarget, ties[1]); // 0x34 does not clear the target-dead flag
        battle.RunQueued();
        battle.Frame(TacticalBattle.FrameMs);
        Assert.Equal(held, wing.NavPoint);
        battle.Order(wing, TacticalBattle.RemoveTarget, ties[0]); // 0x35 clears it; ties[1] becomes current
        battle.RunQueued();
        Assert.Equal([ties[1]], battle.TargetList(wing));
        battle.Frame(TacticalBattle.FrameMs);
        Assert.NotEqual(held, wing.NavPoint);
    }

    /// <summary>Worked examples at W = 115: Left / Right Hook, Hammer, Anvil, and side 1's Left Hook.</summary>
    [GameFact]
    public void HookPickersFollowTheWorkedExamples()
    {
        var (_, battle, a, e) = Scene(new Script([], []), [.. Enumerable.Repeat(0x14000042u, 5)], [0x14000085, 0x14000085, 0x14000085]);
        Assert.Equal(115, battle.Size);
        var g = a[0].Group!;
        V3 Pick(TacticalGroup grp, int m, V3 n, V3 c, params (TacticalUnit U, V3 At)[] targets)
        {
            battle.MissionMessage(grp, m);
            grp.Nav!.Point = n;
            grp.G.Target = targets.Length > 0 ? targets[0].U.U : null;
            grp.More.Clear();
            foreach (var (u, p) in targets) u.Position = p;
            foreach (var (u, _) in targets.Skip(1)) grp.More.Add(u);
            battle.Hook(grp, c);
            Assert.True(grp.Object!.Hooked);
            return grp.UiNav.Single().Position;
        }
        V3 n = (0, 0, -57.5), c = (0, 0, 57.5);
        Assert.Equal(new V3(-28.75, 0, 28.75), Pick(g, 7, n, c, (e[0], (-10, 0, 57.5)), (e[1], (5, 0, 57.5))));
        Assert.Equal(TacticalBattle.ReplaceNavPoints, battle.QueuedKinds()[^1]);
        Assert.Equal(new V3(28.75, 0, 28.75), Pick(g, 8, n, c, (e[0], (-10, 0, 57.5)), (e[1], (10, 0, 57.5))));
        Assert.Equal(new V3(0, -28.75, 28.75), Pick(g, 9, n, c, (e[0], (0, 8, 57.5))));
        Assert.Equal(new V3(0, 28.75, 28.75), Pick(g, 10, n, c, (e[0], (0, 8, 57.5))));
        // side 1, d = (0, 0, -115): Left Hook D = (1, 0, 0)
        var h = e[2].Group!;
        Assert.Equal(new V3(28.75, 0, -28.75), Pick(h, 7, (0, 0, 57.5), (0, 0, -57.5), (a[1], (10, 0, -57.5))));
    }

    /// <summary>
    /// Recover: the X-wing reserves a place on the first active own ship with room (0x2C: carrier assigned), docks within 2.0
    /// of the launch point (0x1A: state 5, carrier field = 4, hangar count + 1, landed list, its emptied group deleted). With only full hangars: the "no carrier" voice, the fighter button back and
    /// mission 0.
    /// </summary>
    [GameFact]
    public void RecoverReservesDocksOrGivesUp()
    {
        uint carrier = AllianceShip(r => r.FighterCapacity >= 1 && r.TotalAttackStrength > 0), plain = AllianceShip(r => r.FighterCapacity == 0);
        foreach (bool room in new[] { true, false })
        {
            var (game, b0, _, _) = Scene(new Script([], []), [room ? carrier : plain], [0x14000085]);
            b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
            var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
            battle.Computer[1] = false;
            var ship = battle.Units.Single(u => u.Side == 0 && !u.Fighter);
            var x = battle.Units.Single(u => u.Side == 0 && u.Fighter);
            var wing = x.Group!;
            Assert.True(battle.SetMission(wing, 2));
            Assert.Null(battle.Ui.Fighters[0]);
            battle.RunQueued();
            x.Position = ship.Position - new V3(0, ship.HalfHeight, 0); // the hangar's launch point
            battle.Step(); // turn 0
            if (!room)
            {
                Assert.Equal([0x8B], battle.Ui.Voices);
                Assert.Same(wing, battle.Ui.Fighters[0]);
                Assert.Equal(0, wing.UiMission);
                Assert.Equal(1, battle.Queued(0x0A, 3));
                continue;
            }
            Assert.Equal(1, x.Recovery);
            Assert.Equal(1, battle.Queued(0x2C, 3));
            while (battle.Turn < 5) battle.Step(); // turn 4: 0x2C, then the step posts 0x1A
            Assert.Equal((3, ship.U), (x.Recovery, x.U.Carrier));
            Assert.Equal(1, battle.Queued(0x1A, 7));
            while (battle.Turn < 9) battle.Step();
            Assert.Equal((4, 5, 1), (x.Recovery, x.State, ship.U.Docked));
            Assert.Empty(wing.Units);
            Assert.DoesNotContain(wing, battle.Groups);
            Assert.DoesNotContain(x, battle.Taking);
        }
    }

    /// <summary>
    /// Observe makes the local side computer-controlled; its next targeting rebuilds (old buttons freed, the new
    /// task forces bound to buttons k + 1, the old groups gone four turns later); back to command nothing is rebuilt; Observe again
    /// rebuilds again.
    /// </summary>
    [GameFact]
    public void ObserveRebuildsAndRebindsTheButtons()
    {
        var (_, battle, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000085]);
        var ui = battle.Ui;
        var before = ui.TaskForces.ToArray();
        Assert.NotNull(before[0]);
        ui.Command(0x7B);
        ui.Frame();
        Assert.True(battle.Computer[0]);
        Assert.False(ui.InCommand);
        battle.Step();
        Assert.NotNull(ui.TaskForces[0]);
        Assert.NotSame(before[0], ui.TaskForces[0]);
        Assert.Same(ui.TaskForces[0], a[0].UiGroup);
        while (battle.Turn < 5) battle.Step();
        Assert.DoesNotContain(before[0], battle.Groups);
        var second = ui.TaskForces.ToArray();
        ui.Command(0x7B);
        ui.Frame();
        Assert.False(battle.Computer[0]);
        for (int i = 0; i < 8; i++) battle.Step();
        Assert.Equal(second, ui.TaskForces);
        ui.Command(0x7B);
        ui.Frame();
        battle.Step(); // the countdown stood still while human (2 left): the rebuild comes at the second computer turn
        Assert.Same(second[0], ui.TaskForces[0]);
        battle.Step();
        Assert.NotSame(second[0], ui.TaskForces[0]);
    }

    /// <summary>The computer withdraw waits while a Death Star run is in progress (a mission-6 object lives, DAT 0x6BC978).</summary>
    [GameFact]
    public void DeathStarRunBlocksTheComputerWithdraw()
    {
        BattleEngine.RetreatAllowedOverride = (_, _) => true;
        try
        {
            foreach (bool run in new[] { false, true })
            {
                var (game, b0, _, _) = Scene(new Script([], []), [.. Enumerable.Repeat(0x14000042u, 4)], [0x14000085]);
                b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
                var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
                Assert.True(battle.Computer[1]);
                battle.Units.Single(u => u.Side == 1).U.Hull = 0; // own / E = 0 (no controllers: threshold 0)
                if (run) battle.MissionMessage(battle.Ui.Fighters[0]!, 6);
                Assert.Equal(run, battle.DeathStarRun);
                battle.Step();
                Assert.Equal(run ? 0 : 2, battle.SideState(1));
            }
        }
        finally { BattleEngine.RetreatAllowedOverride = null; }
    }
}
