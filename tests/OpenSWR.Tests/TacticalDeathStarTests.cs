using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using static OpenSWR.Tests.TacticalTests;

namespace OpenSWR.Tests;

/// <summary>The Death Star in tactical battles (scene, charge, superlaser, the Alliance run, computer rules, withdraw, hangar, results).</summary>
public class TacticalDeathStarTests
{
    const uint DeathStar = 0x18000088, XWing = 0x1C000003, Ship = 0x14000042;

    static uint Tie => Definitions.Fighters.First(r => r.Empire != 0 && r.Hyperdrive == 0).Common.Ref;

    /// <summary>A bare system with an Alliance fleet and an Empire fleet of the given classes; <paramref name="extra"/> adds to the system before the battle.</summary>
    internal static (GameState Game, TacticalBattle B, Script Script) Make(Owner local, uint[] alliance, uint[] empire, Action<GameState, StarSystem>? extra = null, Script? script = null)
    {
        script ??= new Script([], []);
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var sectors = Definitions.Sectors.ToDictionary(r => r.Common.Index, r => new Sector(r));
        var r0 = Definitions.Systems[0];
        var x = new StarSystem(r0, sectors[r0.SectorIndex]) { Explored = true };
        game.Systems.Add(x);
        var a = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        foreach (var t in alliance) a.Add(new GameObject(t, Owner.Alliance, game.NextId()));
        var e = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        foreach (var t in empire) e.Add(new GameObject(t, Owner.Empire, game.NextId()));
        extra?.Invoke(game, x);
        return (game, new TacticalBattle(game, x, local, script.Draw), script);
    }

    static GameObject DeathStarOf(StarSystem x) => x.Contents.OfType<Fleet>().SelectMany(f => f.Contents).First(o => o.Type == DeathStar);

    internal static Action<GameState, StarSystem> XWings(int n, int ties = 0, bool shield = false) => (g, x) =>
    {
        for (int i = 0; i < n; i++) x.Add(new GameObject(XWing, Owner.Alliance, g.NextId()));
        for (int i = 0; i < ties; i++) DeathStarOf(x).Add(new GameObject(Tie, Owner.Empire, g.NextId()));
        if (shield) x.Add(new GameObject(Definitions.DefenseFacilities.First(r => Family.Of(r.Common.Ref) == 0x25).Common.Ref, Owner.Empire, g.NextId()));
    };

    static void Steps(TacticalBattle b, int n) { for (int i = 0; i < n; i++) b.Step(); }

    // ---- the scene ----

    /// <summary>
    /// An unfinished Death Star (the shipyard's) is the scene: not alive, never charging, side index 1 may retreat
    /// without a unit; an Empire player's panel starts with the button enabled (constructor charge 100), command 0xF enters mode 0x11
    /// and the same frame's refresh leaves it (not alive) and removes the panel.
    /// </summary>
    [GameFact]
    public void UnfinishedSceneNeitherChargesNorFiresButMayRetreat()
    {
        BattleEngine.RetreatAllowedOverride = (_, _) => true;
        try
        {
            var (_, b, _) = Make(Owner.Empire, [Ship], [], (g, x) =>
            {
                var mm = x.Managers[(int)ManufacturingKind.Ship] = new ManufacturingManager(x, ManufacturingKind.Ship, g.NextId());
                var building = new GameObject(DeathStar, Owner.Empire, g.NextId());
                (building.Completed, building.Usable) = (false, false);
                mm.Orders.Add(building);
            });
            Assert.True(b.HasScene);
            Assert.True(b.SceneUnfinished);
            Assert.False(b.SceneAlive);
            Assert.True(b.RetreatPossible(1)); // no Empire unit: the unfinished scene alone
            Assert.True(b.Ui.DeathStarPanel);
            Assert.Equal(100, b.Ui.PanelValue);
            b.Scene!.Charge = 50f;
            b.Step();
            Assert.Equal(50f, b.Scene.Charge); // no charge while unfinished
            b.Ui.Command(0xF);
            b.Ui.Frame();
            Assert.IsType<TacticalUi.IdleMode>(b.Ui.Current); // 50 < 100: the entry test failed
            b.Scene.Charge = 100f;
            b.Ui.Command(0xF);
            b.Ui.Frame();
            Assert.IsType<TacticalUi.IdleMode>(b.Ui.Current); // entered, then left by the refresh
            Assert.False(b.Ui.DeathStarPanel);
        }
        finally { BattleEngine.RetreatAllowedOverride = null; }
    }

    /// <summary>
    /// With fighter superiority in the world list the attack is possible and side index 0's strength carries the bonus; a
    /// Death Star shield facility makes it not attackable (attack not possible, no bonus), but the computer Empire still fires the
    /// superlaser at turn 0.
    /// </summary>
    [GameFact]
    public void ShieldBlocksTheAttackButNotTheSuperlaser()
    {
        foreach (bool shield in new[] { false, true })
        {
            var (_, b, _) = Make(Owner.Alliance, [Ship], [DeathStar], XWings(2, shield: shield));
            Assert.Equal(!shield, b.AttackPossible);
            b.Step();
            Assert.Equal(!shield, b.Sides[0].Strength > 20000000.0);
            Assert.True(b.Sides[1].Strength >= 20000000.0);
            Assert.Equal(0f, b.Scene!.Charge);
            Assert.Equal(1, b.Queued(0x44, 3));
        }
    }

    // ---- charge and superlaser ----

    /// <summary>
    /// Worked example (computer Empire): turn 0 fires at the first capital-ship candidate A (charge 0, 0x44 stamp 3, message 3
    /// stamp 4), the beam at turn 4, A destroyed at turn 5 without a draw; the charge reaches exactly 100.0 after 300 turns, and the
    /// targeting turn 300 fires at B.
    /// </summary>
    [GameFact]
    public void ComputerSuperlaserKillsAtTurnFiveAndAgainAtTurn300()
    {
        var (_, b, script) = Make(Owner.Alliance, [Ship, Ship], [DeathStar]);
        Assert.True(b.Computer[1]);
        int draws = script.Log.Count;
        b.Step(); // turn 0
        var cands = b.Candidates(1);
        var (a, second) = (cands[0], cands[1]);
        Assert.Equal(0f, b.Scene!.Charge);
        Assert.Equal(1, b.Queued(0x44, 3));
        Assert.Equal(1, b.Queued(3, 4));
        Steps(b, 4); // turns 1-4
        Assert.Equal(4, Assert.Single(b.Superlasers).Turn);
        Assert.Equal(2, a.State);
        b.Step(); // turn 5
        Assert.Equal(11, a.State);
        Assert.Equal(draws, script.Log.Count); // no draw
        Steps(b, 293); // turns 6-298
        b.Step(); // turn 299: 299 increments
        Assert.True(b.Scene.Charge < 100f);
        b.Step(); // turn 300: 100.0 exactly, a targeting turn
        Assert.Equal(0f, b.Scene.Charge);
        Assert.Equal(1, b.Queued(0x44, 303));
        Steps(b, 5);
        Assert.Equal(11, second.State);
    }

    /// <summary>A target no longer active at the dispatch takes nothing (no beam, no destroy); the charge is spent anyway.</summary>
    [GameFact]
    public void InactiveTargetTakesNothing()
    {
        var (_, b, _) = Make(Owner.Alliance, [Ship], [DeathStar]);
        b.Step();
        var a = b.Units.Single(u => u.Side == 0);
        double hull = a.Hull;
        a.U.State = 9;
        Steps(b, 5);
        Assert.Equal(9, a.State);
        Assert.Equal(hull, a.Hull);
        Assert.Empty(b.Superlasers);
        Assert.Equal(5 * (1000f * 0.33333334f * 0.001f), b.Scene!.Charge, 4);
    }

    /// <summary>
    /// Player: command 0xF enters mode 0x11 (cursor 0x2A); a right click on an enemy fighter fires (voice 0x111) and returns
    /// to idle with the charge spent; at 100 again, Space fires at the enemy capital ship nearest to the scene point (-W/2, 0, 0).
    /// </summary>
    [GameFact]
    public void PlayerFiresAtAnyEnemyUnit()
    {
        var (_, b, _) = Make(Owner.Empire, [Ship], [DeathStar], XWings(1));
        var ui = b.Ui;
        Assert.True(ui.DeathStarPanel);
        var (ship, wing) = (b.Units.Single(u => u.Side == 0 && !u.Fighter), b.Units.Single(u => u.Side == 0 && u.Fighter));
        ui.Command(0xF);
        ui.Frame();
        Assert.IsType<TacticalUi.SuperlaserMode>(ui.Current);
        Assert.True(ui.TargetCursor);
        ui.RightDown(wing, false);
        ui.Frame();
        Assert.IsType<TacticalUi.IdleMode>(ui.Current);
        Assert.False(ui.TargetCursor);
        Assert.Contains(0x111, ui.Voices);
        Assert.Equal(0, ui.PanelValue);
        b.RunQueued();
        Assert.Equal(11, wing.State);
        b.Scene!.Charge = 100f;
        ui.Command(0xF);
        ui.Frame();
        ui.Command(0x20);
        ui.Frame();
        b.RunQueued();
        Assert.Equal(11, ship.State);
        Assert.True(ui.DeathStarPanel);
    }

    // ---- the Alliance run ----

    /// <summary>
    /// Worked example: one X-wing squadron (hull 96, field 48, agility 6), no commander; draws r1 = 4, r3 = 62, r1 = 9, r2 = 5,
    /// r3 = 5 -> success with the hull back to 96; run started at the dispatch of turn s = 5, the outcome at turn s + 123: k = 0, the
    /// squadron back at full strength, the Death Star destroyed, movie MDATA.201, mission 0.
    /// </summary>
    [GameFact]
    public void RunWorkedExample()
    {
        var script = new Script([], [4, 62, 9, 5, 5]);
        var (_, b, _) = Make(Owner.Alliance, [], [DeathStar], XWings(1), script);
        b.Computer[1] = false;
        var g = b.Ui.Fighters[0]!;
        var w = Assert.Single(g.Units);
        Assert.Equal(96, w.HullMax);
        Assert.Equal(48, w.U.Torpedo);
        Assert.Equal(6, b.Maneuverability(w));
        Assert.True(b.AttackPossible);
        b.MissionMessage(g, 6);
        Assert.True(b.DeathStarRun);
        Assert.False(b.AttackPossible);
        var run = Assert.IsType<TacticalRun>(g.Object);
        Assert.True(run.Pending);
        b.Step(); // turn 0: init
        Assert.Equal(2, run.State);
        b.RequestRun(g, 5); // stamp 4: the dispatch of turn 5
        Steps(b, 5); // turns 1-5
        Assert.Equal([(1, 10, 4), (1, 100, 62), (1, 10, 9), (1, 12, 5), (1, 100, 5)], script.Log);
        Assert.Equal(9, run.State);
        Assert.Equal(10, w.State);
        Assert.True(w.Hidden);
        Assert.Equal(96, w.Hull);
        Assert.True(b.Sides[0].Strength > 20000000.0); // a run in progress keeps the Alliance's bonus
        Steps(b, 122); // turns 6-127
        Assert.Equal(9, run.State);
        Assert.True(run.Pending);
        Assert.True(b.SceneAlive);
        b.Step(); // turn 128 = s + 123
        Assert.Equal(2, w.State);
        Assert.False(w.Hidden);
        Assert.Equal(96, w.Hull);
        Assert.Equal(0, b.SceneHull);
        Assert.False(b.SceneAlive);
        Assert.Equal(201, b.Movie);
        Assert.True(b.Paused);
        Assert.Equal(0, g.Mission);
        Assert.False(b.DeathStarRun);
        Assert.Same(g, b.Ui.Fighters[0]);
        Assert.Equal(5, script.Log.Count);
    }

    /// <summary>State 6 with n = 5: the first 2 members destroyed, 3 restored; the Death Star's docked TIEs destroyed.</summary>
    [GameFact]
    public void SuccessLosesHalfTheSquadrons()
    {
        var script = new Script([], [0, 1, 1]); // setup dock rand(0, 8); visit 1: r1 = 1 (no hit), r3 = 1 (success)
        var (_, b, _) = Make(Owner.Alliance, [], [DeathStar], XWings(5, ties: 2), script);
        var g = b.Ui.Fighters[0]!;
        var members = g.Units.ToList();
        Assert.Equal(5, members.Count);
        var ties = b.Units.Where(u => u.Side == 1).ToList();
        Assert.All(ties, t => Assert.Equal(1, t.State));
        b.MissionMessage(g, 6);
        b.Step();
        b.RunTo(g, 5);
        Assert.Equal(9, ((TacticalRun)g.Object!).State);
        Assert.Equal(3, script.Log.Count);
        b.RunTo(g, 6);
        Assert.Equal([11, 11, 2, 2, 2], members.Select(m => m.State));
        Assert.All(ties, t => Assert.Equal(11, t.State));
        Assert.Equal(0, b.SceneHull);
    }

    /// <summary>
    /// Every squadron emptied (r1 = 10, r2 = t) -> failure (state 10); at Get Immediate Result the run members are still in
    /// state 10: F[6] = max(1, trunc(hull / 8)) = 1, F[5] none; state 7 destroys every member, deletes the group, movie MDATA.202.
    /// </summary>
    [GameFact]
    public void FailureDestroysEveryMember()
    {
        var (_, b, script) = Make(Owner.Alliance, [], [DeathStar], XWings(3));
        var g = b.Ui.Fighters[0]!;
        var members = g.Units.ToList();
        b.MissionMessage(g, 6);
        b.Step();
        b.RunTo(g, 5);
        Assert.Equal(10, ((TacticalRun)g.Object!).State);
        Assert.Equal(6, script.Log.Count);
        Assert.All(members, m => Assert.Equal(0, m.Hull));
        var r = b.Result();
        Assert.All(members, m => Assert.Equal(new FighterResult(1, null), r.Fighters[m.Object]));
        b.RunTo(g, 7);
        Assert.All(members, m => Assert.Equal(11, m.State));
        Assert.DoesNotContain(g, b.Groups);
        Assert.False(b.DeathStarRun);
        Assert.Equal(202, b.Movie);
        Assert.True(b.SceneAlive);
    }

    /// <summary>
    /// World withdraw of side index 0, in state 8: posts 0x3C(G, 8) before the 0x0A; a pending success is cancelled:
    /// everyone restored, the Death Star survives, mission 3.
    /// </summary>
    [GameFact]
    public void AbortRestoresEveryone()
    {
        var (_, b, _) = Make(Owner.Alliance, [], [DeathStar], XWings(1), new Script([], [1, 1]));
        var g = b.Ui.Fighters[0]!;
        var w = g.Units.Single();
        b.MissionMessage(g, 6);
        b.Step();
        b.RunQueued(); // the ctor's request 2
        b.RunTo(g, 5);
        Assert.Equal(9, ((TacticalRun)g.Object!).State);
        b.Withdraw(0);
        var kinds = b.QueuedKinds();
        Assert.True(kinds.IndexOf(0x3C) < kinds.IndexOf(0x0A));
        b.RunQueued();
        Assert.Equal(2, w.State);
        Assert.False(w.Hidden);
        Assert.Equal(3, g.Mission);
        Assert.True(b.SceneAlive);
        Assert.False(b.DeathStarRun);
    }

    /// <summary>Flight: the group flies to (-W/2, 0, 0) (r = 0.2) and on arrival sets done and requests the run (state 5).</summary>
    [GameFact]
    public void FlightArrivalRequestsTheRun()
    {
        var (_, b, _) = Make(Owner.Alliance, [], [DeathStar], XWings(1));
        var g = b.Ui.Fighters[0]!;
        b.MissionMessage(g, 6);
        b.Step(); // init: state 2
        g.Nav!.Point = b.ScenePoint + new V3(0.5, 0, 0);
        b.Frame(TacticalBattle.FrameMs);
        Assert.False(g.Object!.Done);
        for (int i = 0; i < 40 && !g.Object!.Done; i++) b.Frame(TacticalBattle.FrameMs);
        Assert.True(g.Object!.Done);
        Assert.True(((TacticalRun)g.Object).Pending);
        Assert.Equal(new V3(-1, 0, 0), g.Nav.Heading);
    }

    // ---- the computer Alliance ----

    /// <summary>
    /// A computer Alliance with fighter superiority orders mission 6 on the group of its first list unit at its first
    /// targeting turn (side counter := 1); the 0x0A at turn 4 creates the run and resets the counter. Without fighters (attack not possible,
    /// the list is empty) it withdraws when it may retreat.
    /// </summary>
    [GameFact]
    public void ComputerAllianceOrdersTheRunOrWithdraws()
    {
        var (_, b, _) = Make(Owner.Empire, [], [DeathStar], XWings(2));
        Assert.True(b.Computer[0]);
        var g = b.Ui.Fighters.FirstOrDefault(x => x is not null) ?? b.Groups.First(x => x.Side == 0 && x.Fighters);
        b.Step();
        Assert.Equal(6, g.UiMission);
        Assert.False(b.DeathStarRun);
        Steps(b, 4);
        Assert.Equal(6, g.Mission);
        Assert.True(b.DeathStarRun);
        BattleEngine.RetreatAllowedOverride = (_, _) => true;
        try
        {
            var (_, b2, _) = Make(Owner.Empire, [Ship], [DeathStar]);
            b2.Step();
            Assert.Equal(2, b2.SideState(0));
        }
        finally { BattleEngine.RetreatAllowedOverride = null; }
    }

    // ---- withdraw and the scene hangar ----

    /// <summary>
    /// The Empire's withdraw with a live scene counts down 150 turns; at state 3 message 0x45 (four turns later the scene is
    /// withdrawn and takes its launchable TIEs along, state 5); an Empire player's Death Star panel is removed at state 3.
    /// </summary>
    [GameFact]
    public void SceneWithdrawsAfter150Turns()
    {
        foreach (var local in new[] { Owner.Alliance, Owner.Empire })
        {
            var (_, b, _) = Make(local, [Ship], [DeathStar], XWings(0, ties: 1));
            var tie = b.Units.Single(u => u.Side == 1);
            b.Withdraw(1);
            Steps(b, 149);
            Assert.Equal(2, b.SideState(1));
            Assert.Equal(local == Owner.Empire, b.Ui.DeathStarPanel);
            b.Step();
            Assert.True(b.SideState(1) >= 3);
            Assert.False(b.Ui.DeathStarPanel);
            Assert.Equal(1, b.Queued(0x45));
            Assert.True(b.SceneAlive);
            Steps(b, 4);
            Assert.False(b.SceneAlive);
            if (local == Owner.Alliance) Assert.Equal(5, tie.State);
        }
    }

    /// <summary>
    /// The scene hangar launches only for a local Empire with the scene alive: at the scene point (-W/2, 0, 0); a Recover
    /// group docks there (0x1A while the scene is alive). Against a local Alliance the TIEs stay docked.
    /// </summary>
    [GameFact]
    public void SceneHangarLaunchesOnlyForAnEmpirePlayer()
    {
        var (_, alliance, _) = Make(Owner.Alliance, [Ship], [DeathStar], XWings(0, ties: 2), new Script([], [0]));
        Steps(alliance, 40);
        Assert.All(alliance.Units.Where(u => u.Side == 1), t => Assert.Equal(1, t.State));

        var (_, b, _) = Make(Owner.Empire, [Ship], [DeathStar], XWings(0, ties: 2), new Script([], [0]));
        Steps(b, 10); // first launch time 3 + 0 + 5 = 8: inserted at turn 5, launched at turn 9
        var tie = b.Units.First(u => u.Side == 1 && u.State == 2);
        Assert.Equal(b.ScenePoint, tie.Position);
        var g = tie.Group!;
        Assert.True(b.SetMission(g, 2));
        b.RunQueued();
        Steps(b, 12); // reserve (0x2C), assigned, within 2.0 -> 0x1A, docked
        Assert.Equal(5, tie.State);
    }

    /// <summary>
    /// The placement faces every unit, the scene hangar's docked TIEs too, along its fighter group's heading ((0, 0, -1) for
    /// side index 1); launch writes no facing, so a scene TIE flies off with it.
    /// </summary>
    [GameFact]
    public void SceneTiesLaunchWithTheirGroupHeading()
    {
        var (_, b, _) = Make(Owner.Empire, [Ship], [DeathStar], XWings(0, ties: 2), new Script([], [0]));
        var ties = b.Units.Where(u => u.Side == 1 && u.State == 1).ToList();
        Assert.Equal(2, ties.Count);
        Assert.All(ties, t => Assert.Equal((new V3(0, 0, -1), new V3(0, 0, -1)), (t.Facing, t.Group!.Nav!.Heading)));
        Steps(b, 10); // turns only, no frames: first launch inserted at turn 5, launched at turn 9
        Assert.Equal(new V3(0, 0, -1), ties.Single(t => t.State == 2).Facing);
    }

    // ---- results ----

    /// <summary>Get Immediate Result with a live Death Star: the automatic follow-up builds a new scene with charge 100 (its superlaser fires again in step 1).</summary>
    [GameFact]
    public void ImmediateResultRebuildsTheScene()
    {
        var (game, b, _) = Make(Owner.Alliance, [Ship], [DeathStar]);
        b.Step();
        Assert.Equal(0f, b.Scene!.Charge);
        b.Abort();
        Assert.Equal(100f, BattleEngine.Build(game, b.System)[1].Scene!.Charge);
    }
}
