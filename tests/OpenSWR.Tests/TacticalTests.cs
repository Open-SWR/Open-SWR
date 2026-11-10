using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Host flow, placement and the simulation core.</summary>
public class TacticalTests
{
    static GameDefinitions? _definitions;
    internal static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>
    /// A ready game with a battle at an empty neutral system: two Imperial ships (the first carrying TIE squadrons that cannot jump,
    /// docked), an Alliance fleet of two ships and an X-wing squadron directly in the system.
    /// </summary>
    static (GameState Game, StarSystem System) Battle()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var x = game.Systems.First(s => !s.Contents.Any(o => o.Existing) && s.Owner == Owner.Neutral);
        var carrier = Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && r.FighterCapacity >= 2 && r.WeaponRechargeRate > 0);
        var heavy = Definitions.CapitalShips.Where(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18).MaxBy(r => r.TotalAttackStrength)!;
        var rebel = Definitions.CapitalShips.Where(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18 && r.TotalAttackStrength > 0).ToList();
        var empire = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        var ship = empire.Add(new GameObject(carrier.Common.Ref, Owner.Empire, game.NextId()));
        var tie = Definitions.Fighters.First(r => r.Empire != 0 && r.Hyperdrive == 0).Common.Ref;
        ship.Add(new GameObject(tie, Owner.Empire, game.NextId()));
        ship.Add(new GameObject(tie, Owner.Empire, game.NextId()));
        empire.Add(new GameObject(heavy.Common.Ref, Owner.Empire, game.NextId()));
        var alliance = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        alliance.Add(new GameObject(rebel[0].Common.Ref, Owner.Alliance, game.NextId()));
        alliance.Add(new GameObject(rebel[^1].Common.Ref, Owner.Alliance, game.NextId()));
        x.Add(new GameObject(Definitions.Fighters.First(r => r.Alliance != 0 && r.Hyperdrive != 0).Common.Ref, Owner.Alliance, game.NextId()));
        game.Clock.Flush(game); // the setup's queued events run first (the host runs after dispatch)
        (x.BattlePending, x.BattleToHost, game.BattleRequired, x.BattleReady) = (true, true, true, Owner.Empire);
        return (game, x);
    }

    static string State(GameState game, StarSystem x) => string.Join("; ", Combat.Units(x).Concat(x.Contents.OfType<Fleet>())
        .Select(o => $"{o.Ref:X8} p{o.Container?.Ref:X8} d{o.Destroyed} h{o.HullDamage} q{o.SquadSizeDamage} c{string.Join("", Enumerable.Range(0, 6).Select(o.Critical))}"))
        + $" won {x.BattleWon} battle {x.Battle}";

    /// <summary>
    /// "Take command" (answer 2) holds the host in the tactical state with the clock paused; Get
    /// Immediate Result at turn 0 (S[6] = 1, S[0..5] = 0, the setup records) makes the application return withdraw without writing
    /// BattleWon, and the automatic battle of host state 8 then gives exactly the plain automatic battle: the same engine draws, the
    /// same state events, the same game objects and the same game-generator draws after the battle (none during it).
    /// </summary>
    [GameFact]
    public void ImmediateResultEqualsTheAutomaticBattle()
    {
        List<(string, int, int)> Run(bool tactical, out string state, out List<Fact> facts, out List<(string, int, int)> gameDraws)
        {
            var (game, x) = Battle();
            var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance) { AskBattles = tactical };
            game.Controllers[Owner.Alliance] = human;
            game.FactTrace = facts = [];
            game.Random.Trace = gameDraws = [];
            var trace = new List<(string, int, int)>();
            BattleEngine.Trace = trace;
            try
            {
                Combat.HostBattles(game);
                if (tactical)
                {
                    Assert.Same(x, game.HostedBattle);
                    Assert.Null(game.Tactical);
                    (human.BattleAnswer, human.TakeCommand) = (0.0, true);
                    Combat.HostBattles(game);
                    var t = Assert.IsType<TacticalBattle>(game.Tactical);
                    Assert.Same(x, game.HostedBattle);
                    Assert.True(x.Battle);
                    Assert.Empty(trace);
                    Assert.Empty(gameDraws); // the tactical battle never steps the game generator
                    int tick = game.Clock.Tick;
                    game.Clock.Step(game); // host state 0x14: no clock advance while the battle is not done
                    Assert.Equal(tick, game.Clock.Tick);
                    Assert.Throws<InvalidOperationException>(() => SaveGame.Save(game, new MemoryStream()));
                    t.Abort();
                    var r = t.Result();
                    Assert.Equal([0, 0, 0, 0, 0, 0, 1], r.S);
                    Assert.False(r.Auto);
                    game.Clock.Step(game);
                    Assert.Null(game.Tactical);
                    Assert.Null(game.HostedBattle);
                }
                Assert.False(x.Battle);
                Assert.NotEmpty(trace);
                state = State(game, x);
                return trace;
            }
            finally { BattleEngine.Trace = null; }
        }
        var auto = Run(false, out var autoState, out var autoFacts, out var autoDraws);
        var tac = Run(true, out var tacState, out var tacFacts, out var tacDraws);
        Assert.Equal(auto, tac);
        Assert.Equal(autoState, tacState);
        Assert.Equal(autoFacts, tacFacts);
        Assert.Equal(autoDraws, tacDraws); // the battle's strategic consequences (autorouting) draw alike
    }

    // ---- simulation core ----

    /// <summary>Scripted C-rand draws: fire chance (0, 2) answers from <paramref name="fire"/> (then 1), others from <paramref name="rest"/> (then hi); every request logged.</summary>
    internal sealed class Script(IEnumerable<int> fire, IEnumerable<int> rest)
    {
        readonly Queue<int> _fire = new(fire), _rest = new(rest);
        public List<(int Lo, int Hi, int Result)> Log { get; } = [];
        public int Draw(int lo, int hi)
        {
            int r = lo == 0 && hi == 2 ? (_fire.Count > 0 ? _fire.Dequeue() : 1) : _rest.Count > 0 ? _rest.Dequeue() : hi;
            if (!(lo == 0 && hi == 2)) Log.Add((lo, hi, r));
            return r;
        }
    }

    /// <summary>A bare system with an Alliance and an Empire fleet of the given classes (Alliance first in the world list); both sides human.</summary>
    internal static (GameState Game, TacticalBattle Battle, List<TacticalUnit> Alliance, List<TacticalUnit> Empire) Scene(Script script, uint[] alliance, uint[] empire, Action<GameObject>? setup = null)
    {
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var sectors = Definitions.Sectors.ToDictionary(r => r.Common.Index, r => new Sector(r));
        var r0 = Definitions.Systems[0];
        var x = new StarSystem(r0, sectors[r0.SectorIndex]) { Explored = true };
        game.Systems.Add(x);
        var a = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        foreach (var t in alliance) { var o = a.Add(new GameObject(t, Owner.Alliance, game.NextId())); setup?.Invoke(o); }
        var e = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        foreach (var t in empire) { var o = e.Add(new GameObject(t, Owner.Empire, game.NextId())); setup?.Invoke(o); }
        var battle = new TacticalBattle(game, x, Owner.Alliance, script.Draw);
        battle.Computer[1] = false;
        return (game, battle, [.. battle.Units.Where(u => u.Side == 0)], [.. battle.Units.Where(u => u.Side == 1)]);
    }

    internal static void Place(TacticalUnit u, double x, double z) => u.Position = (x, 0, z);

    /// <summary>A task force of the units, its messages run at once (no turn latency).</summary>
    internal static TacticalGroup Tf(TacticalBattle b, params TacticalUnit[] units)
    {
        var g = b.CreateTaskForce(units);
        b.RunQueued();
        return g;
    }

    const double Base = 0.26666668f;

    /// <summary>
    /// Timing: a shot decided in turn 0 (stamp base 3) shows its turbolaser beam (0x0E, stamp 3) at the start of turn 4,
    /// hits (0x13, stamp 4) at turn 5; the ion display (0x0F) runs at turn 5 and its hit (0x12) at turn 6. Aimed values:
    /// turbolaser count * base * 1.0, ion count * base * 1.0 against a ship. The shooter's arc is spent (not ready, queued). No draw
    /// on the game generator.
    /// </summary>
    [GameFact]
    public void ShotsHitFiveAndSixTurnsLater()
    {
        var script = new Script([0], []);
        // Empire: Super Star Destroyer with ShieldCH 4 (no recharge) and WeaponCH 4 (never ready, never fires)
        var (game, battle, a, e) = Scene(script, [0x14000042], [0x14000086], o => { if (o.Owner == Owner.Empire) { o.SetCritical(0, 4); o.SetCritical(1, 4); } });
        long draws = game.Random.Draws;
        var (shooter, target) = (a[0], e[0]);
        Place(shooter, 0, 0);
        Place(target, 0, 30); // fore arc, within the turbolaser (60) and ion (35) ranges
        Tf(battle, shooter).G.Target = target.U;
        double s0 = target.Shield;
        Assert.Equal(1200, s0);
        battle.Step(); // turn 0: the decision
        Assert.Equal(1, battle.Queued(0x0E, 3));
        Assert.Equal(1, battle.Queued(0x13, 4));
        Assert.Equal(1, battle.Queued(0x0F, 4));
        Assert.Equal(1, battle.Queued(0x12, 5));
        Assert.False(shooter.U.Ready[0]);
        Assert.Contains(0, shooter.U.Queue);
        for (int turn = 1; turn <= 4; turn++) battle.Step();
        Assert.Equal(s0, target.Shield);
        Assert.Equal([(4, 0x0E, shooter, target)], battle.Shots.Select(s => (s.Turn, s.Kind, s.From, s.To)));
        battle.Step(); // turn 5: turbolaser hit
        Assert.Equal(s0 - 70 * Base, target.Shield, 3);
        Assert.Equal((5, 0x0F, shooter, target), (battle.Shots[^1].Turn, battle.Shots[^1].Kind, battle.Shots[^1].From, battle.Shots[^1].To));
        battle.Step(); // turn 6: ion hit
        Assert.Equal(s0 - 70 * Base - 50 * Base, target.Shield, 3);
        Assert.Equal(draws, game.Random.Draws);
        Assert.Equal(7, battle.Turn);
        Assert.Equal(10, battle.Stamp);
    }

    /// <summary>Arc of a target: heading +z; ahead -> 0 (fore), +x -> 1 (starboard), behind -> 2 (aft), -x -> 3 (port).</summary>
    [GameFact]
    public void ArcByGeometry()
    {
        var (_, _, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000086]);
        var u = a[0];
        Place(u, 0, 0);
        u.Heading = (0, 1);
        int Arc(double x, double z) { Place(a[1], x, z); return TacticalBattle.ArcOf(u, a[1]); }
        Assert.Equal([0, 1, 2, 3, 0, 1], new[] { Arc(0, 10), Arc(10, 0), Arc(0, -10), Arc(-10, 0), Arc(5, 10), Arc(10, 5) });
    }

    /// <summary>
    /// Aimed shot: out of every weapon's range -> nothing fires; the laser only within its own range (17): a frigate (turbolaser
    /// 80 / range 60, laser 60 / range 17) at 30 fires its turbolaser only, at 15 also the laser.
    /// </summary>
    [GameFact]
    public void AimedShotTakesTheWeaponsInRange()
    {
        var script = new Script([0, 0, 0], []);
        var (_, battle, a, e) = Scene(script, [0x14000043], [0x14000086], o => { if (o.Owner == Owner.Empire) o.SetCritical(1, 4); });
        var (u, t) = (a[0], e[0]);
        Tf(battle, u).G.Target = t.U;
        Place(u, 0, 0);
        Place(t, 0, 70);
        battle.Step();
        Assert.Equal(0, battle.Queued(0x11) + battle.Queued(0x13));
        Place(t, 0, 30);
        battle.Step();
        Assert.Equal((0, 1), (battle.Queued(0x11), battle.Queued(0x13))); // turbolaser 0x13, laser 0x11
        u.U.Ready[0] = true;
        Place(t, 0, 15);
        battle.Step();
        Assert.Equal((1, 2), (battle.Queued(0x11), battle.Queued(0x13)));
    }

    /// <summary>
    /// The fire query takes enemy fighters only, walking from the shooter toward lower x first,
    /// then higher x, each walk ending at the first entry beyond the range (example: shooter at 0, fighters at -3, -1, +1, +5, range
    /// 4 -> -1, -3, +1); capital ships are never candidates.
    /// </summary>
    [GameFact]
    public void FireQueryWalksTheFighters()
    {
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var sectors = Definitions.Sectors.ToDictionary(r => r.Common.Index, r => new Sector(r));
        var r0 = Definitions.Systems[0];
        var x = new StarSystem(r0, sectors[r0.SectorIndex]) { Explored = true };
        game.Systems.Add(x);
        x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId())).Add(new GameObject(0x14000042, Owner.Alliance, game.NextId()));
        x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId())).Add(new GameObject(0x14000085, Owner.Empire, game.NextId()));
        for (int i = 0; i < 4; i++) x.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, x, Owner.Alliance, new Script([], []).Draw);
        var shooter = battle.Units.First(u => u.Side == 0);
        var ship = battle.Units.First(u => u.Side == 1 && !u.Fighter);
        var f = battle.Units.Where(u => u.Fighter).ToList();
        Place(shooter, 0, 0);
        Place(ship, 0.5, 0);
        double[] xs = [-3, -1, 1, 5];
        for (int i = 0; i < 4; i++) Place(f[i], xs[i], 0);
        Assert.Equal([f[1], f[0], f[2]], battle.FireQuery(shooter, 4));
    }

    /// <summary>
    /// Hits: shield held -> crit 0 when rand 0...100 <= max(1, 100 * d / s0); broken -> the whole overflow on the hull, then rand
    /// 0...100 + 100 * delta hull / hull in the bands; d at least 1; hull <= 0 -> state 11, hull 0, no draw.
    /// </summary>
    [GameFact]
    public void HitFormulas()
    {
        var script = new Script([], [30, 31, 100, 0]);
        var (_, battle, a, e) = Scene(script, [0x14000042], [0x14000085]);
        var (t, shooter) = (e[0], a[0]);
        t.U.Shield = 100;
        battle.Hit(t, 30, shooter, ratio: true); // p = 30, r = 30 -> crit 0
        Assert.Equal(70, t.Shield);
        Assert.Equal(1, battle.Queued(0x1E, battle.Stamp));
        battle.Hit(t, 21, shooter, ratio: true); // p = 30, r = 31 -> none
        Assert.Equal(1, battle.Queued(0x1E));
        (t.U.Shield, t.U.Hull) = (10, 200);
        battle.Hit(t, 60, shooter, ratio: true); // hull 150; x = -25 + 100 = 75 -> k = 1
        Assert.Equal((0.0, 150.0), (t.Shield, t.Hull));
        Assert.Equal(1, battle.Queued(0x1F));
        battle.Hit(t, 0.2, shooter, ratio: true); // d := 1 -> hull 149; x = -0.67 + 0 -> none
        Assert.Equal(149, t.Hull);
        Assert.Equal([(0, 100, 30), (0, 100, 31), (0, 100, 100), (0, 100, 0)], script.Log);
        battle.Hit(t, 1000, shooter, ratio: true);
        Assert.Equal((11, 0.0), (t.State, t.Hull));
        Assert.Equal(4, script.Log.Count); // the killing hit draws nothing
    }

    /// <summary>Fighter target: d / max(0.1, target agility / shooter agility) and no crit draw (shield or hull).</summary>
    [GameFact]
    public void FighterTargetsTakeTheAgilityRatio()
    {
        var script = new Script([], []);
        var (game, battle, _, _) = Scene(script, [0x14000042], [0x14000085]);
        // a squadron of X-wings (maneuverability 5 + 1) against a shooter of agility min(9, 2 + 1) = 3: d / 2
        battle.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        var b = new TacticalBattle(game, battle.System, Owner.Alliance, script.Draw);
        var wing = b.Units.Single(u => u.Fighter);
        var ship = b.Units.First(u => u.Side == 1);
        double s0 = wing.Shield;
        b.Hit(wing, 10, ship, ratio: true);
        double ratio = BattleEngine.Agility(wing.U, b.Sides[0]) / BattleEngine.Agility(ship.U, b.Sides[1]);
        Assert.Equal(s0 - 10 / ratio, wing.Shield, 6);
        Assert.Empty(script.Log);
    }

    /// <summary>
    /// Step 6: a crit message is posted with the stamp base during the dispatch of turn m that applies the hit
    /// (base m + 3), so it acts at turn m + 4: a turbolaser shot decided at turn 0 hits at turn 5 and its shield crit acts at turn 9.
    /// </summary>
    [GameFact]
    public void CritActsFourTurnsAfterTheHit()
    {
        var script = new Script([0], [0]);
        var (_, battle, a, e) = Scene(script, [0x14000042], [0x14000086], o => { if (o.Owner == Owner.Empire) o.SetCritical(1, 4); });
        var (shooter, t) = (a[0], e[0]);
        Place(shooter, 0, 0);
        Place(t, 0, 30);
        Tf(battle, shooter).G.Target = t.U;
        battle.Step(); // turn 0: the decision
        for (int turn = 1; turn <= 5; turn++) battle.Step();
        // turn 5: the hit, rand 0 <= p -> crit 0 posted with base 8
        Assert.Equal(1, battle.Queued(0x1E, 8));
        for (int turn = 6; turn <= 8; turn++) battle.Step();
        Assert.Equal(0, t.U.ShieldCh);
        battle.Step(); // turn 9
        Assert.Equal(1, t.U.ShieldCh);
    }

    /// <summary>
    /// Ion hit: the excess over the shield never reaches the hull; per whole point k = rand 1...10: 1 tractor-down + rand 30...50,
    /// 2 engine-down + rand 30...50, 3...10 arc (k - 3) div 2 down; message 0x16 applies them (arc not ready, energy 0, queued); the
    /// engine-down time stops the ship (speed 0) and runs down by 1.0 per turn.
    /// </summary>
    [GameFact]
    public void IonHitDisablesWithoutHullDamage()
    {
        var script = new Script([], [1, 40, 2, 35, 9]);
        var (_, battle, a, e) = Scene(script, [0x14000042], [0x14000085]);
        var t = e[0];
        double hull = t.Hull;
        t.U.Shield = 5;
        battle.IonHit(t, 8, a[0]);
        Assert.Equal((0.0, hull), (t.Shield, t.Hull));
        Assert.Equal([(1, 10, 1), (30, 50, 40), (1, 10, 2), (30, 50, 35), (1, 10, 9)], script.Log);
        Assert.Equal(1, battle.Queued(0x16, battle.Stamp));
        for (int i = 0; i < 5; i++) battle.Step(); // stamp 3 runs at turn 4 (the fifth step), then the unit update counts down once
        Assert.Equal((34.0, 39.0), (t.U.EngineDown, t.U.TractorDown));
        Assert.False(t.U.Ready[3]);
        Assert.Equal(0, BattleEngine.Speed(t.U, battle.Sides[1]));
    }

    /// <summary>
    /// The side without active units ends the battle: S[1] (side 2 eliminated), the 4000 ms end countdown, then done; the result
    /// (automatic flag 0) wins the battle for the Alliance in the application.
    /// </summary>
    [GameFact]
    public void EliminationEndsAfterTheCountdown()
    {
        var (game, battle, a, e) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        battle.Hit(e[0], 1e6, a[0], ratio: true);
        battle.Advance(1);
        Assert.True(battle.Ending);
        Assert.Equal([0, 1, 0, 0, 0, 0, 0], battle.S);
        battle.Advance(3998);
        Assert.False(battle.Done);
        battle.Advance(2);
        Assert.True(battle.Done);
        var r = battle.Result();
        Assert.False(r.Auto);
        Assert.Equal(0, r.Ships[e[0].Object].Hull);
        Combat.ApplyBattle(game, battle.System, r);
        Assert.Equal(Owner.Alliance, battle.System.BattleWon);
        Assert.True(e[0].Object.Destroyed);
    }

    /// <summary>
    /// Finish re-dock at a normal end (after S): a launched fighter (state 2) docks in the first own capital ship with room
    /// (state 1, carrier = F[5]); H +0 := 0. Get Immediate Result (abort) skips it: the fighter stays launched (F[5] none).
    /// </summary>
    [GameFact]
    public void FinishRedocksFightersExceptOnAbort()
    {
        var carrier = Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18 && r.FighterCapacity >= 1).Common.Ref;
        TacticalBattle Make(out TacticalUnit wing, out TacticalUnit ship, out TacticalUnit enemy)
        {
            var (game, b0, _, _) = Scene(new Script([], []), [carrier], [0x14000085]);
            b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
            var b = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
            (wing, ship, enemy) = (b.Units.Single(u => u.Fighter), b.Units.Single(u => u.Side == 0 && !u.Fighter), b.Units.Single(u => u.Side == 1));
            return b;
        }
        var battle = Make(out var wing, out var ship, out var enemy);
        Assert.Equal(2, wing.State);
        battle.Hit(enemy, 1e6, ship, ratio: true);
        battle.Advance(1);
        Assert.Equal((1, false), (wing.State, battle.Ui.InCommand));
        Assert.Same(ship.Object, battle.Result().Fighters[wing.Object].Carrier);
        battle = Make(out wing, out _, out _);
        battle.Abort();
        Assert.Equal(2, wing.State);
        Assert.Null(battle.Result().Fighters[wing.Object].Carrier);
    }

    /// <summary>The tactical strength adds the unit list for both sides; side index 1 also the pending list.</summary>
    [GameFact]
    public void StrengthCountsFightersOnBothSides()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        var before = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        var after = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        before.Step();
        after.Step();
        Assert.True(after.Sides[0].Strength > before.Sides[0].Strength);
    }

    /// <summary>
    /// Target orders: replace (0x36), add (0x34), remove (0x35) change the UI target list at once and the simulation list
    /// by their group message at the dispatch (stamp base 3 -> turn 4); nearest enemy (capital ships first); the
    /// current target out of every range (r = 2), the fire decision walks the nearest-enemy list.
    /// The ship has no laser, so the refresh took the enemy capital ship nearest to the nav point, e[1].
    /// </summary>
    [GameFact]
    public void TargetOrdersChangeTheUiListAtOnceAndTheSimulationListLater()
    {
        var (_, battle, a, e) = Scene(new Script([1, 1, 1, 1, 0], []), [0x14000042], [0x14000085, 0x14000086, 0x14000084], o => { if (o.Owner == Owner.Empire) o.SetCritical(1, 4); });
        var g = Tf(battle, a[0]);
        Place(a[0], 0, 0);
        Place(e[0], 0, 200);
        Place(e[1], 0, 30);
        Place(e[2], 0, 300);
        Assert.Equal(e[1], battle.Nearest(a[0].Position, 0));
        Assert.True(battle.Order(g, TacticalBattle.ReplaceTargets, e[0]));
        Assert.True(battle.Order(g, TacticalBattle.AddTarget, e[1]));
        Assert.True(battle.Order(g, TacticalBattle.AddTarget, e[2]));
        Assert.True(battle.Order(g, TacticalBattle.RemoveTarget, e[2]));
        Assert.Equal([e[0], e[1]], g.UiTargets);
        Assert.Empty(battle.TargetList(g));
        Assert.Equal([0x36, 0x34, 0x34, 0x35], battle.QueuedKinds());
        for (int i = 0; i < 4; i++) battle.Step();
        Assert.Empty(battle.TargetList(g));
        battle.Step(); // turn 4: the stamp-3 messages, then the fire: e[0] out of every range (r = 2) -> aimed at e[1]
        Assert.Equal([e[1]], g.NearestEnemy);
        Assert.Equal([e[0], e[1]], battle.TargetList(g));
        Assert.Equal(1, battle.Queued(0x13, 8));
    }

    /// <summary>
    /// A whole battle with both sides computer-controlled (rebuild, targeting every 6 turns with the tactical evaluator) and the
    /// lines moved closer: it runs on its own generator to the end and the end countdown, and its result applies;
    /// the game generator is untouched.
    /// </summary>
    [GameFact]
    public void ComputerSidesFightToTheEnd()
    {
        var (game, x) = Battle();
        long draws = game.Random.Draws;
        var rand = new GameRandom(4242);
        var battle = new TacticalBattle(game, x, Owner.Alliance, (lo, hi) => rand.Between(lo, hi));
        battle.SetComputer(0, true);
        foreach (var u in battle.Units.Where(u => u.Side == 1)) u.Position = (u.Position.X, 0, u.Position.Z - battle.Size + 30);
        for (int i = 0; i < 3000 && !battle.Done; i++) battle.Advance(1000);
        Assert.True(battle.Done);
        Assert.NotEmpty(battle.Shots);
        Assert.Equal(1, battle.S[1] + battle.S[3]);
        Assert.Equal(draws, game.Random.Draws);
        Combat.ApplyBattle(game, x, battle.Result());
        Assert.NotEqual(Owner.None, x.BattleWon);
    }

    /// <summary>
    /// Withdraw: the countdown is at least 30000 ms (30 turns); then side state 3 and, the ship standing on
    /// its own start line (within 8.0), the next frame starts its jump: state 6 and message 0x2D (stamp 33 -> turn 34: state 7), 4000 ms
    /// of hyperspace frames (state 8, message 0x2E stamp 41 -> turn 42: state 9, withdrawn); the side's units withdrawn -> S[0] (side 1
    /// withdrew) and S[3].
    /// </summary>
    [GameFact]
    public void WithdrawWaitsThirtyTurnsThenJumps()
    {
        BattleEngine.RetreatAllowedOverride = (_, _) => true;
        try
        {
            var (_, battle, a, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
            Tf(battle, a[0]);
            Assert.True(battle.RetreatPossible(0));
            battle.Withdraw(0);
            Assert.Equal(2, battle.SideState(0));
            Assert.False(battle.RetreatPossible(0));
            while (battle.Turn < 29) battle.Advance(TacticalBattle.FrameMs);
            Assert.Equal(2, battle.SideState(0));
            while (a[0].State == 2) battle.Advance(TacticalBattle.FrameMs);
            Assert.Equal((3, 30, 6), (battle.SideState(0), battle.Turn, a[0].State));
            while (a[0].State == 6) battle.Advance(TacticalBattle.FrameMs);
            Assert.Equal((7, 35), (a[0].State, battle.Turn));
            while (a[0].State == 7) battle.Advance(TacticalBattle.FrameMs);
            Assert.Equal((8, 39), (a[0].State, battle.Turn)); // the 80th frame (at 38000 ms) and turn 38 in the same call
            while (a[0].State == 8) battle.Advance(TacticalBattle.FrameMs);
            Assert.Equal((9, 43), (a[0].State, battle.Turn));
            Assert.True(battle.Ending);
            Assert.Equal([1, 0, 0, 1, 0, 0, 0], battle.S);
        }
        finally { BattleEngine.RetreatAllowedOverride = null; }
    }

    /// <summary>
    /// Type index from the SD string number: every shipped capital ship, fighter and the Death Star has its own index
    /// (none falls back to 0 except record 0); MC30c / Viscount / Liberator = 11 / 12 / 13, the Super Star Destroyer 0x15, the
    /// Alliance fighters X-, Y-, A-, B-wing = 29-32, TIE Fighter 33.
    /// </summary>
    [GameFact]
    public void TypeIndexFromTheStringTable()
    {
        int I(uint t) => BattleEngine.TypeIndex(Definitions, t);
        var all = Definitions.CapitalShips.Select(r => r.Common.Ref).Concat(Definitions.Fighters.Select(r => r.Common.Ref)).ToList();
        var index = all.Select(I).ToList();
        Assert.Equal(38, index.Distinct().Count());
        Assert.Equal(38, all.Count);
        Assert.Equal(0, I(Definitions.CapitalShips[0].Common.Ref));
        Assert.Equal((11, 12, 13, 0x15), (I(0x1400004D), I(0x1400004B), I(0x1400004C), I(0x14000086)));
        Assert.Equal((29, 31, 33), (I(0x1C000003), I(0x1C000001), I(0x1C000005)));
        var alliance = Definitions.Fighters.Where(r => r.Alliance != 0).Select(r => I(r.Common.Ref)).Order();
        Assert.Equal([29, 30, 31, 32], alliance);
    }

    /// <summary>
    /// Nav-point grid for W = 115 (3 against 5 ships): 105 points; set s has spacing s * 115 / 8 (14.375 ... 57.5), set 1 the
    /// origin and 26 more, sets 2-4 26 each; layer 0 below the plane (y &lt; 0), 1 on it, 2 above; all hidden at the start.
    /// </summary>
    [GameFact]
    public void NavGridOfFourCubes()
    {
        var (_, battle, _, _) = Scene(new Script([], []), [0x14000042, 0x14000042, 0x14000042], [0x14000085, 0x14000085, 0x14000085, 0x14000085, 0x14000085]);
        var p = battle.NavPoints;
        Assert.Equal(105, p.Count);
        Assert.Equal([27, 26, 26, 26], Enumerable.Range(1, 4).Select(s => p.Count(x => x.Set == s)));
        for (int s = 1; s <= 4; s++)
        {
            double d = s * 115 / 8.0;
            Assert.Equal(d, p.Where(x => x.Set == s).Max(x => x.Position.X));
            Assert.All(p.Where(x => x.Set == s), x => Assert.Contains(x.Position.Y, new[] { -d, 0, d }));
            Assert.Equal((9, s == 1 ? 9 : 8, 9), (p.Count(x => x.Set == s && x.Layer == 0), p.Count(x => x.Set == s && x.Layer == 1), p.Count(x => x.Set == s && x.Layer == 2)));
        }
        Assert.All(p, x => Assert.Equal(Math.Sign(x.Position.Y) + 1, x.Layer));
        Assert.Contains(p, x => x.Set == 1 && x.Position == V3.Zero);
        Assert.All(p, x => Assert.True(x.Hidden));
        Assert.Equal(105, p.Select(x => x.Key).Distinct().Count());
    }

    // ---- movement ----

    /// <summary>
    /// Worked table (no officers, no crits, no drag): speed = 1 + Unk3C * 0.24 (capital) / 0.4 (fighter), M and turn rate (M * pi/18
    /// when M &gt; 1; the Super Star Destroyer capped to pi/60); SpeedCH 2 halves the speed; engines down -> speed 0, maneuverability 1.
    /// </summary>
    [GameFact]
    public void KinematicsFromTheWorkedTable()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000043, 0x14000048], [0x14000085, 0x14000086]);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId())); // X-wing
        b0.System.Add(new GameObject(0x1C000001, Owner.Alliance, game.NextId())); // A-wing
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var u = battle.Units.ToDictionary(t => t.Object.Type);
        (uint Type, double Speed, double M, double Turn)[] table =
        [
            (0x14000085, 1.96, 2, 2 * Math.PI / 18), // Imperial I Star Destroyer
            (0x14000043, 1.72, 3, Math.PI / 6), // Nebulon-B
            (0x14000048, 3.40, 5, 5 * Math.PI / 18), // Corellian Gunship
            (0x14000086, 1.48, 2, Math.PI / 60), // Super Star Destroyer (type 0x15)
            (0x1C000003, 4.20, 6, Math.PI / 3), // X-wing
            (0x1C000001, 5.40, 9, Math.PI / 2), // A-wing
        ];
        foreach (var (type, speed, m, turn) in table)
        {
            Assert.Equal(speed, battle.Speed(u[type]), 6);
            Assert.Equal(m, battle.Maneuverability(u[type]), 6);
            Assert.Equal(turn, u[type].TurnRate, 9);
        }
        var isd = u[0x14000085];
        isd.U.SpeedCh = 2;
        Assert.Equal(0.98, battle.Speed(isd), 6);
        isd.U.EngineDown = 30;
        Assert.Equal((0.0, 1.0), (battle.Speed(isd), battle.Maneuverability(isd)));
        Assert.False(battle.CanJump(isd));
    }

    /// <summary>
    /// Placement and edges with the setup task forces: 3 against 5 ships -> W = 115; lines at z = -/+ 57.5; three equal
    /// ships make one task force (g = 1): the leader at x = 0, the others in its line slots (n = 2 -> x - 12, n = 3 -> x + 12), on HUD
    /// button 1; five make two (g = 2, stride 2): leaders at heights 0 and +8, the fifth ship the second group's slot 3; side 1's
    /// slots mirror x; no HUD slots for side 1; no messages; clamp +/- (2/3)*W.
    /// </summary>
    [GameFact]
    public void PlacementAndEdges()
    {
        var (_, battle, a, e) = Scene(new Script([], []), [0x14000042, 0x14000042, 0x14000042], [0x14000085, 0x14000085, 0x14000085, 0x14000085, 0x14000085]);
        Assert.Equal(115, battle.Size);
        Assert.Equal([0.0, -12, 12], a.Select(u => u.Position.X));
        Assert.All(a, u => Assert.Equal((-57.5, 0.0), (u.Position.Z, u.Position.Y)));
        Assert.Same(battle.Ui.TaskForces[0], a[0].Group);
        Assert.All(a, u => Assert.Same(a[0].Group, u.UiGroup));
        Assert.Equal([a[0], a[1], a[2]], a[0].Group!.Units);
        Assert.Equal([0.0, 12, 0, 12, -12], e.Select(u => u.Position.X));
        Assert.Equal([0.0, 0, 8, 8, 8], e.Select(u => u.Position.Y));
        Assert.All(e, u => Assert.Equal((57.5, -1.0), (u.Position.Z, u.Heading.Z)));
        Assert.Equal((e[0].Group, e[2].Group), (e[1].Group, e[4].Group));
        Assert.NotSame(e[0].Group, e[2].Group);
        Assert.Empty(battle.QueuedKinds());
        a[0].Position = (0, 0, -100); // the frame clamp: +/- (2/3)*W = +/- 76.67
        battle.Frame(TacticalBattle.FrameMs);
        Assert.Equal(-115 * (double)(float)(2.0 / 3), a[0].Position.Z, 9);
    }

    /// <summary>
    /// Slot offsets: line (spacing 12, ISD-sized members, side 0 heading +z): n = 2 -> (12, 0, 0), 3 -> (-12, 0, 0), 4 -> 
    /// (24, 0, 0), 5 -> (-24, 0, 0); side 1 mirrors x; sphere n = 1 -> (0, 0, 12.5), 2 -> (0, 0, -12.5), 7 -> 12.5/sqrt 3 * (1, 1, 1), 23 -> 
    /// (0, 0, 15.625); fighter n = 1 -> 0, others of length 1.
    /// </summary>
    [GameFact]
    public void SlotOffsets()
    {
        var (_, battle, a, _) = Scene(new Script([], []), [0x14000085], [0x14000085]);
        var u = a[0];
        battle.SetBox(u, 1.18, 0.54, 2.14);
        Assert.Equal((0.5, 2.44), (u.HalfHeight, Math.Round(u.Size, 2)));
        V3 Line(int key, int n) => TacticalBattle.SlotOffset(new Navigator(2, key) { MaxCount = n }, n, u);
        Assert.Equal([V3.Zero, new V3(12, 0, 0), new V3(-12, 0, 0), new V3(24, 0, 0), new V3(-24, 0, 0)], Enumerable.Range(1, 5).Select(n => Line(1, n) + V3.Zero));
        Assert.Equal(new V3(-12, 0, 0), Line(1000001, 2) + V3.Zero);
        V3 Sphere(int n) => TacticalBattle.SlotOffset(new Navigator(1, 1), n, u);
        Assert.Equal(new V3(0, 0, 12.5), Sphere(1));
        Assert.Equal(new V3(0, 0, -12.5), Sphere(2));
        double k = 12.5 / Math.Sqrt(3);
        foreach (double c in new[] { Sphere(7).X, Sphere(7).Y, Sphere(7).Z }) Assert.Equal(k, c, 9);
        Assert.Equal(new V3(0, 0, 15.625), Sphere(23));
        var fighter = new Navigator(3, 1);
        Assert.Equal(V3.Zero, TacticalBattle.SlotOffset(fighter, 1, u));
        for (int n = 2; n < 8; n++) Assert.Equal(1, TacticalBattle.SlotOffset(fighter, n, u).Length, 9);
    }

    /// <summary>
    /// Weapon band, one ISD against a Nebulon-B: pieces [0, 17] 2920, (17, 35] 3040, (35, 50] 1040, (50, 60] -960; A = 0,
    /// best (17, 35] -> band [17, 35]. Viscount against the Nebulon-B: A = 70 - 60 = 10, band [17, 40]. Mode 1:
    /// band [0, 2.5].
    /// </summary>
    [GameFact]
    public void WeaponBandExamples()
    {
        var (_, battle, a, e) = Scene(new Script([], []), [0x14000085, 0x1400004B], [0x14000043]);
        var (isd, viscount, nebulon) = (Tf(battle, a[0]), Tf(battle, a[1]), e[0]);
        isd.G.Target = viscount.G.Target = nebulon.U;
        var pieces = battle.Pieces(isd, nebulon, out double rt, out double rm);
        Assert.Equal((60.0, 50.0), (rt, rm));
        Assert.Equal([(0.0, 17.0), (17, 35), (35, 50), (50, 60)], pieces.Select(p => (p.Lo, p.Hi)));
        double[] values = [2920, 3040, 1040, -960];
        for (int i = 0; i < 4; i++) Assert.InRange(pieces[i].V, values[i] - 5, values[i] + 5); // the float factors (0.167 for 1/6)
        battle.UpdateBand(isd);
        Assert.Equal((17.0, 35.0, 23.0), (isd.Range.Lo, isd.Range.Hi, isd.Range.Distance));
        battle.UpdateBand(viscount);
        Assert.Equal((17.0, 40.0, 10.0), (viscount.Range.Lo, viscount.Range.Hi, viscount.Range.Distance));
        Assert.True(battle.OrderValue(isd, TacticalBattle.SetMode, 1));
        for (int i = 0; i < 5; i++) battle.Step(); // 0x3D stamp 3 runs at turn 4: the mode-1 range object, sphere formation
        Assert.Equal((0.0, 2.5, 5.0, 1), (isd.Range.Lo, isd.Range.Hi, isd.Range.Distance, isd.Formation));
    }

    /// <summary>
    /// Fighter band, X-wings against TIE fighters: one piece [0, 12] with the net value 432 (1152 - 720); A = 0 gives the field value 4,
    /// band [5, 12] (lo raised to 5).
    /// </summary>
    [GameFact]
    public void FighterBandExample()
    {
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var sectors = Definitions.Sectors.ToDictionary(r => r.Common.Index, r => new Sector(r));
        var r0 = Definitions.Systems[0];
        var x = new StarSystem(r0, sectors[r0.SectorIndex]) { Explored = true };
        game.Systems.Add(x);
        x.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        x.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, x, Owner.Alliance, new Script([], []).Draw);
        var (wing, tie) = (battle.Units.Single(u => u.Side == 0), battle.Units.Single(u => u.Side == 1));
        var g = wing.Group!;
        g.G.Target = tie.U;
        var pieces = battle.Pieces(g, tie, out _, out _);
        Assert.Equal((0.0, 12.0), (pieces.Single().Lo, pieces.Single().Hi));
        Assert.Equal(432, pieces.Single().V, 0);
        battle.UpdateBand(g);
        Assert.Equal((5.0, 12.0, 4.0), (g.Range.Lo, g.Range.Hi, g.Range.Distance));
    }

    /// <summary>Turning: ISD turn rate 2pi/18, error 90 deg, 50 ms frame -> f = 0.0111, 1.0 deg per frame; f >= 1 -> the desired heading.</summary>
    [Fact]
    public void TurnStepExample()
    {
        var (h, theta) = TacticalBattle.TurnStep(new V3(0, 0, 1), new V3(1, 0, 0), Math.PI / 2, 2 * Math.PI / 18, 50);
        Assert.Equal(-Math.PI / 180, theta, 9); // toward +x: negative about y
        Assert.Equal(Math.Sin(Math.PI / 180), h.X, 9);
        Assert.Equal((new V3(1, 0, 0), Math.PI / 2), TacticalBattle.TurnStep(new V3(0, 0, -1), new V3(1, 0, 0), Math.PI / 2, 100, 50));
    }

    /// <summary>
    /// Vertical separation: two ISDs (h = 0.5) level at y = 0, 2.5 apart (inside each other's query box, not overlapping),
    /// both moving at e = 1.96: the lower key descends, the higher climbs, 0.4 * 1.96 * 0.05 = 0.0392 per 50 ms frame each, until the
    /// gap is 1.0 (13 frames ~ 0.64 s). Nothing moves before turn counter 3.
    /// </summary>
    [GameFact]
    public void VerticalSeparation()
    {
        var (_, battle, a, _) = Scene(new Script([], []), [0x14000085, 0x14000085], [0x14000085]);
        foreach (var u in a) { battle.SetBox(u, 1.18, 0.54, 2.14); Tf(battle, u); }
        a[0].Position = (0, 0, 0);
        a[1].Position = (2.5, 0, 0);
        battle.Advance(2000); // turns 0-2: no frame update yet
        Assert.Equal((V3)(0, 0, 0), a[0].Position);
        foreach (var u in a) u.Group!.Nav!.Point = u.Position + new V3(0, 0, 100);
        for (int i = 0; i < 12; i++) battle.Frame(TacticalBattle.FrameMs);
        Assert.InRange(a[1].Position.Y - a[0].Position.Y, 0.9, 0.99);
        Assert.Equal(-12 * 0.4 * 1.96 * 0.05, a[0].Position.Y, 6);
        battle.Frame(TacticalBattle.FrameMs);
        Assert.Equal(1.0, a[1].Position.Y - a[0].Position.Y, 9);
        Assert.True(a[0].Position.Y < 0 && a[1].Position.Y > 0);
        double y0 = a[0].Position.Y;
        battle.Frame(TacticalBattle.FrameMs);
        Assert.Equal(y0, a[0].Position.Y);
    }

    /// <summary>
    /// Pursuit: a task force of one ISD with a Nebulon-B as target (no fire, the Nebulon holds): the nav point advances
    /// at 0.95 * 1.96 = 1.862 u/s toward the target until L <= 33 (inside the band [17, 35]: lo <= L + 2 <= hi), from 103 apart after
    /// about 70 / 1.862 ~ 38 s after the order's message (turn 4); the ship follows its slot.
    /// </summary>
    [GameFact]
    public void TaskForceClosesToItsBand()
    {
        var (_, battle, a, e) = Scene(new Script([], []), [0x14000085], [0x14000043]);
        var g = Tf(battle, a[0]);
        Assert.True(battle.Order(g, TacticalBattle.ReplaceTargets, e[0])); // the simulation list at turn 4
        Assert.Equal(103, battle.Size);
        double L() => (e[0].Position - g.NavPoint).Length;
        int stopped = -1;
        for (int t = 0; t < 80; t++)
        {
            battle.Advance(1000);
            if (stopped < 0 && g.Nav!.Velocity.IsZero && battle.Turn > 6) stopped = battle.Turn;
        }
        Assert.InRange(L(), 32.9, 33.0);
        Assert.InRange(stopped, 41, 44);
        Assert.InRange((g.NavPoint - a[0].Position).Length, 0, 1.0);
        Assert.Equal(51.5, e[0].Position.Z);
    }

    /// <summary>
    /// Nav points: 0x3A (replace) with a grid point changes the UI list at once and the simulation list one turn after
    /// its stamp; the formation moves to the point (line mover, r = 1) and drops it from the simulation and the UI list on arrival
    /// 0x09 sets the nav point at once when it runs.
    /// </summary>
    [GameFact]
    public void NavPointOrders()
    {
        var (_, battle, a, _) = Scene(new Script([], []), [0x14000085], [0x14000043]);
        var g = Tf(battle, a[0]);
        var wp = battle.NavPoints.First(p => p.Set == 1 && p.Layer == 1 && p.Position.X > 0 && p.Position.Z < 0);
        Assert.Equal(new V3(12.875, 0, -12.875), wp.Position);
        Assert.True(battle.OrderNav(g, TacticalBattle.ReplaceNavPoints, wp));
        Assert.Equal([wp], g.UiNav);
        for (int i = 0; i < 4; i++) battle.Step();
        Assert.Empty(g.Waypoints);
        battle.Step(); // turn 4: stamp 3 dispatched
        Assert.Equal([wp], g.Waypoints);
        for (int t = 0; t < 60 && g.Waypoints.Count > 0; t++) battle.Advance(1000);
        Assert.Empty(g.Waypoints);
        Assert.Empty(g.UiNav);
        Assert.InRange((g.NavPoint - wp.Position).Length, 0, 1.0);
        Assert.True(battle.OrderPoint(g, new V3(-30, 0, -30)));
        battle.Advance(5000);
        Assert.Equal(-30, g.NavPoint.X, 0);
    }

    // ---- order core ----

    /// <summary>
    /// Create from a list: the first unit creates (0x30), the others join (0x33), a unit in another task force leaves it first
    /// (0x37); then the pending target (0x34) and nav point (0x38); UI members and the group reference at once, simulation members at the dispatch.
    /// </summary>
    [GameFact]
    public void CreateFromAListPostsInOrder()
    {
        var (_, battle, a, e) = Scene(new Script([], []), [0x14000042, 0x14000042, 0x14000042], [0x14000085]);
        var old = Tf(battle, a[2]);
        var g = battle.CreateTaskForce([a[0], a[1], a[2]]);
        Assert.True(battle.Order(g, TacticalBattle.AddTarget, e[0]));
        Assert.True(battle.OrderNav(g, TacticalBattle.AddNavPoint, battle.NavPoints[0]));
        Assert.Equal([0x37, 0x30, 0x37, 0x33, 0x37, 0x33, 0x34, 0x38], battle.QueuedKinds()); // each leaves its task force first
        Assert.Equal([a[0], a[1], a[2]], g.UiMembers);
        Assert.All(a, u => Assert.Same(g, u.UiGroup));
        Assert.Empty(g.Units);
        Assert.Same(old, a[2].Group);
        Assert.Empty(old.UiMembers);
        battle.RunQueued();
        Assert.Equal([a[0], a[1], a[2]], g.Units);
        Assert.All(a, u => Assert.Same(g, u.Group));
        Assert.Empty(old.Units);
        Assert.Equal((a[0], a[0], a[0]), (g.SimLeader, g.UiLeader, g.Nav!.Leader));
        Assert.Equal(a[0].Position, g.NavPoint);
    }

    /// <summary>A numbered task force of the local side left without UI members frees its slot; a new task force for its last member too.</summary>
    [GameFact]
    public void TheLastMemberLeavingFreesTheSlot()
    {
        var (_, battle, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000085]);
        var g = battle.CreateTaskForce([a[0], a[1]]);
        battle.Ui.Bind(2, g);
        Assert.Equal(2, battle.Ui.SlotOf(g));
        Assert.Null(battle.Ui.Unassigned(a[0]));
        battle.LeaveTaskForce(a[1]);
        Assert.Same(g, battle.Ui.TaskForces[2]);
        var h = battle.CreateTaskForce(a[0]);
        Assert.Null(battle.Ui.TaskForces[2]);
        Assert.Same(h, battle.Ui.Unassigned(a[0]));
    }

    /// <summary>
    /// Mission setter: 2 (Recover) only for fighter groups, 6 (Attack Death Star) only for side-0 fighter groups
    /// (refused: UI mission kept); 1 / 9 / 11 clear the UI nav list only, 0 / 4 / 5 both lists; leaving Escort clears the UI targets
    /// and sets tactic 1 (0x3D); 10 clears the simulation nav list; m = 2 / 6 frees the fighter group's HUD slot; message 0x0A sets
    /// the simulation mission at the dispatch; a computer group of the other side on mission 2 / 6 refuses m != 0.
    /// </summary>
    [GameFact]
    public void MissionSetterRules()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId()));
        b0.System.Add(new GameObject(0x1C000005, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var ship = battle.Units.Single(u => u.Side == 0 && !u.Fighter);
        var enemy = battle.Units.Single(u => u.Side == 1 && !u.Fighter);
        var wing = battle.Units.Single(u => u.Side == 0 && u.Fighter).Group!;
        var ties = battle.Units.Single(u => u.Side == 1 && u.Fighter).Group!;
        Assert.Same(wing, battle.Ui.Fighters[0]);
        var g = Tf(battle, ship);
        var nav = battle.NavPoints[5];
        Assert.False(battle.SetMission(g, 2));
        Assert.False(battle.SetMission(g, 6));
        Assert.Equal(0, g.UiMission);
        battle.Order(g, TacticalBattle.ReplaceTargets, enemy);
        battle.OrderNav(g, TacticalBattle.ReplaceNavPoints, nav);
        battle.RunQueued();
        Assert.True(battle.SetMission(g, 1));
        Assert.Equal([enemy], g.UiTargets);
        Assert.Empty(g.UiNav);
        Assert.Equal(2, g.Tactic);
        Assert.True(battle.SetMission(g, 0)); // leaving Escort: targets cleared, tactic 1
        Assert.Empty(g.UiTargets);
        Assert.Equal(1, g.Tactic);
        Assert.Equal([0x0A, 0x3D, 0x0A], battle.QueuedKinds());
        battle.RunQueued();
        Assert.Equal((0, 1, 1), (g.Mission, g.AppliedMode, g.Formation)); // sphere after 0x3D mode 1
        Assert.Empty(g.Waypoints); // the 0x0A handler of mission 1 cleared the simulation nav list
        battle.OrderNav(g, TacticalBattle.ReplaceNavPoints, nav);
        battle.RunQueued();
        Assert.Equal([nav], g.Waypoints);
        Assert.True(battle.SetMission(g, 10));
        Assert.Empty(g.Waypoints);
        Assert.True(battle.SetMission(wing, 6));
        Assert.Null(battle.Ui.Fighters[0]);
        Assert.False(battle.SetMission(ties, 6));
        Assert.True(battle.SetMission(ties, 2));
        Assert.False(battle.SetMission(ties, 4)); // computer group of the other side on mission 2
        Assert.True(battle.SetMission(ties, 0));
    }
}
