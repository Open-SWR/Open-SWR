using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Assault resolver (duels, collateral, landing) and the bombardment shot and hit formulas.</summary>
public class CombatTests(Xunit.Abstractions.ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static (bool, bool, bool, bool, bool, bool, bool) FleetFlagsOf(Fleet f) => (f.Real, f.Reserve, f.NewFleet, f.Battle, f.Blockade, f.Bombard, f.Assault);

    /// <summary>A fresh game and a system without Existing contents (neutral, unpopulated: no defenders, no defences).</summary>
    static (GameState Game, StarSystem System, List<(string Site, int N, int Result)> Draws) Scene()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var system = game.Systems.First(s => !s.Contents.Any(x => x.Existing) && s.Owner == Owner.Neutral);
        (system.Energy, system.RawMaterials) = (0, 0);
        var draws = new List<(string, int, int)>();
        game.Random.Trace = draws;
        return (game, system, draws);
    }

    /// <summary>An Empire fleet in the system with the Assault flag and one capital ship carrying <paramref name="troops"/> Army regiments.</summary>
    static (Fleet Fleet, GameObject Ship, List<GameObject> Troops) Attacker(GameState game, StarSystem s, int troops)
    {
        var fleet = s.Add(new Fleet(SdRef.Make(Family.Fleet, 4), Owner.Empire, game.NextId()) { Real = true, Assault = true });
        var ship = fleet.Add(new GameObject(Definitions.CapitalShips.First(r => r.Empire != 0).Common.Ref, Owner.Empire, game.NextId()));
        var list = Enumerable.Range(0, troops).Select(_ => ship.Add(new GameObject(Refs.EmpireArmy, Owner.Empire, game.NextId()))).ToList();
        return (fleet, ship, list);
    }

    // Assault pre-check: no Usable fleet with the Blockade flag gives (8, 7); no troop aboard gives (8, 5); a non-fleet
    // target fails the fleet resolver with (1, 1); 0x203 takes one target only, (1, 0x15)
    [GameFact]
    public void AssaultPreCheck()
    {
        var (game, s, _) = Scene();
        var (empty, _, _) = Attacker(game, s, 0);
        var (fleet, ship, _) = Attacker(game, s, 1);
        OrderResult Run(int type, params GameObject[] targets) =>
            Orders.Execute(game, new Command(type, Owner.Empire) { Targets = [.. targets.Select(t => (t, 0))], ArgRef = s });
        Assert.Equal(new OrderResult(8, 7), Run(0x234, fleet));
        empty.Blockade = true;
        Assert.Equal(new OrderResult(0x90, 6), Run(0x234, empty)); // the system check (0x90, 6) runs in the pre-check, before the troop check
        s.Blockade = true;
        Assert.Equal(new OrderResult(8, 5), Run(0x234, empty));
        fleet.Blockade = true;
        Assert.Equal(new OrderResult(1, 1), Run(0x234, fleet, ship));
        Assert.Equal(new OrderResult(1, 0x15), Run(0x203, fleet, empty));
        s.Destroyed = true; // system-event slots resolve direct: (1, 0x12), the system check's (1, 0x22) is unreachable
        Assert.Equal(new OrderResult(1, 0x12), Run(0x234, fleet));
        Assert.Equal(new OrderResult(1, 0x12), Run(0x220, fleet));
    }

    // Duel: v = r + (attack + leadA div 30) - (defense + leadD div 30); v >= 6 kills the defender, v <= 4 kills the attacker
    [GameTheory]
    [InlineData(6, 2, 0, 2, 0, 1)]
    [InlineData(5, 2, 0, 2, 0, 0)]
    [InlineData(4, 2, 0, 2, 0, -1)]
    [InlineData(4, 2, 60, 2, 0, 1)]   // leadership 60 div 30 = 2
    [InlineData(5, 2, 0, 2, 59, -1)]  // 59 div 30 = 1
    [InlineData(0, 10, 0, 4, 0, 1)]
    public void DuelFormula(int r, int attack, int leadA, int defense, int leadD, int expected)
    {
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 1), Definitions);
        Assert.Equal(expected, Combat.Duel(game, r, attack, leadA, defense, leadD));
    }

    /// <summary>Without defenders, the first ceildiv(60, 10) = 6 troops of the walk land, with no draws.</summary>
    [GameFact]
    public void LandingTakesUpToSixTroops()
    {
        var (game, s, draws) = Scene();
        var (fleet, ship, troops) = Attacker(game, s, 8);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Empire);
        game.Controllers[Owner.Empire] = human;
        Combat.Assault(game, s);
        Assert.Empty(draws);
        Assert.Equal(troops.Take(6), s.Contents.Where(x => Family.IsTroop(x.Type)));
        Assert.Equal(troops.Skip(6), ship.Contents);
        Assert.Equal((true, false, false, false, false, false, false), FleetFlagsOf(fleet));
        Assert.Equal(1, human.AssaultAlarms); // openswr: the invasion alert
    }

    // openswr deviation (player request): troops taking an unpopulated system give it the owner's full support, so no smuggling
    [GameFact]
    public void CapturedUnpopulatedSystemHasFullSupport()
    {
        var (game, s, _) = Scene();
        Assert.False(s.Populated);
        Assert.Equal(0, s.Support);
        var troop = s.Add(new GameObject(Refs.AllianceArmy, Owner.Alliance, game.NextId()));
        Systems.StoreControl(game, s, Systems.Control(game, s));
        Assert.Equal((Owner.Alliance, 100, 0), (s.Owner, s.Support, s.SmugglingPercent));
        troop.Owner = Owner.Empire;
        Systems.StoreControl(game, s, Systems.Control(game, s));
        Assert.Equal((Owner.Empire, 0, 0), (s.Owner, s.Support, s.SmugglingPercent));
    }

    /// <summary>One duel (roll(D0 - 1) = roll(0), no step; roll(10)), one collateral chance(8), outcome by the duel rule.</summary>
    [GameFact]
    public void OneDuelThenCollateralAndLanding()
    {
        var (game, s, draws) = Scene();
        s.Owner = Owner.Alliance;
        var defender = s.Add(new GameObject(Refs.AllianceArmy, Owner.Alliance, game.NextId()));
        var (_, ship, troops) = Attacker(game, s, 1);
        Combat.Assault(game, s);
        Assert.Equal([(DrawSite.DuelPick, 1, 0), (DrawSite.Duel, 11, draws[1].Result), (DrawSite.Collateral, 100, draws[2].Result)], draws);
        int Sd(GameObject o, SdField field) => Definitions.Resolve(o.Type)!.Get(field);
        int v = Combat.Duel(game, draws[1].Result, Sd(troops[0], SdField.TroopAttackStrength), 0, Sd(defender, SdField.TroopDefenseStrength), 0);
        Assert.Equal(v > 0, defender.Destroyed);
        Assert.Equal(v < 0, troops[0].Destroyed);
        Assert.Equal(v > 0, troops[0].Container == s); // the winner lands
        Assert.Equal(v == 0, troops[0].Container == ship && !troops[0].Destroyed); // a destroyed troop stays aboard until its 0x372
    }

    /// <summary>Collateral damage: t = (F - H) + (Energy != 0) + (RawMaterial != 0); r = roll(t - 1) picks a facility, Energy, RawMaterial.</summary>
    [GameFact]
    public void CollateralPick()
    {
        var (game, s, draws) = Scene();
        Assert.False(Combat.Collateral(game, s));                     // t = 0: no draw
        s.Energy = 2;
        Assert.True(Combat.Collateral(game, s));                      // t = 1: roll(0), no step -> Energy - 1
        Assert.Equal((1, 0), (s.Energy, s.RawMaterials));
        var mine = s.Add(new GameObject(Refs.Mine, Owner.Neutral, game.NextId()));
        Assert.True(Combat.Collateral(game, s));                      // t = 2: r = 0 the Mine, r = 1 Energy
        Assert.Equal([(DrawSite.CollateralPick, 1, 0), (DrawSite.CollateralPick, 2, draws[1].Result)], draws);
        Assert.Equal(draws[1].Result == 0 ? (true, 1) : (false, 0), (mine.Destroyed, s.Energy));
        if (mine.Destroyed) Assert.Equal(0xE, mine.DestroyedReason);
    }

    // Bombardment shots = max(0, B - Sh); a shot hits iff defense < between(G 0x602 = 1, G 0x603 = 10)
    [Theory]
    [InlineData(10, 3, 7)]
    [InlineData(3, 10, 0)]
    [InlineData(5, 5, 0)]
    public void Shots(int bombard, int shields, int expected) => Assert.Equal(expected, Combat.Shots(bombard, shields));

    [GameFact]
    public void HitTest()
    {
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;
        foreach (int defense in new[] { 0, 5, 9, 10 })
        {
            bool hit = Combat.Hits(game, defense);
            Assert.Equal((DrawSite.BombardHit, 10), (draws[^1].Site, draws[^1].N));
            Assert.Equal(defense < 1 + draws[^1].Result, hit);
        }
        Assert.True(Combat.Hits(game, 0));
        Assert.False(Combat.Hits(game, 11));
    }

    // ---- synthetic galaxy: a bare game state with chosen systems (not ready: no notifications) ----

    static (GameState Game, List<StarSystem> Systems) Galaxy(int count)
    {
        var game = new GameState(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var sectors = Definitions.Sectors.ToDictionary(r => r.Common.Index, r => new Sector(r));
        var systems = Definitions.Systems.Take(count).Select(r => new StarSystem(r, sectors[r.SectorIndex]) { Explored = true }).ToList();
        game.Systems.AddRange(systems);
        return (game, systems);
    }

    static CapitalShipRecord Ship(Func<CapitalShipRecord, bool> pick) =>
        Definitions.CapitalShips.First(r => Family.Of(r.Common.Ref) < 0x18 && pick(r));
    static int Field(uint type, SdField field) => Definitions.Resolve(type)!.Get(field);

    /// <summary>The admiral bonus needs the admiral's ship unit active (state 2); retreated or destroyed gives 1.0.</summary>
    [Fact]
    public void AdmiralBonusNeedsAnActiveShip()
    {
        var ship = new BattleEngine.Unit(new GameObject(0x14000001, Owner.Alliance, 1), 0);
        var side = new BattleEngine.Side(0, 0, false) { AdmiralShip = ship, AdmiralValue = 5 };
        Assert.Equal(5, side.Admiral);
        foreach (int state in new[] { 1, 4, 5, 6, 9, 0xB })
        {
            ship.State = state;
            Assert.Equal(1.0, side.Admiral);
        }
        Assert.Equal(1.0, new BattleEngine.Side(0, 0, false).Admiral);
    }

    /// <summary>The CRT qsort used for the destination list keeps its own order for ties (shortsort: max to the end; partition: median to lo).</summary>
    [Fact]
    public void QsortTies()
    {
        var small = new[] { (1, 'a'), (1, 'b'), (0, 'c') };
        Fleets.Qsort(small, (x, y) => x.Item1.CompareTo(y.Item1));
        Assert.Equal("cba", new string([.. small.Select(x => x.Item2)])); // stable would give c a b
        var equal = Enumerable.Range(0, 10).ToArray();
        Fleets.Qsort(equal, (_, _) => 0);
        Assert.Equal([5, 1, 2, 3, 4, 0, 6, 7, 8, 9], equal);           // n > 8: median swapped to lo, nothing else moves
        var mixed = new[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5, 8, 9, 7, 9 };
        Fleets.Qsort(mixed, (x, y) => x.CompareTo(y));
        Assert.Equal(mixed.Order(), mixed);
    }

    /// <summary>Destination search: stay, then own unblockaded systems by distance, then own fleets; fighters are never sent to blockaded, neutral or enemy systems. Flag 0 closes the object's own parent.</summary>
    [GameFact]
    public void DestinationSearchStages()
    {
        var (game, s) = Galaxy(6);
        var from = s[0];
        from.Owner = Owner.Alliance;
        var byDistance = s.Skip(1).OrderBy(x => Orders.Distance(from, x)).ToList();
        Assert.Equal(byDistance.Count, byDistance.Select(x => Orders.Distance(from, x)).Distinct().Count());
        Assert.Equal([from, .. byDistance], Fleets.Distances(game, from));
        // nearest neutral, then Empire blockaded, then two Empire systems
        var (neutral, blockaded, near, far, fleetHome) = (byDistance[0], byDistance[1], byDistance[2], byDistance[3], byDistance[4]);
        neutral.Owner = Owner.Neutral;
        (blockaded.Owner, blockaded.Blockade) = (Owner.Empire, true);
        (near.Owner, far.Owner, fleetHome.Owner) = (Owner.Empire, Owner.Empire, Owner.Alliance);
        var fighterType = Definitions.Fighters.First(r => r.Empire != 0).Common.Ref;
        var fighter = from.Add(new GameObject(fighterType, Owner.Empire, game.NextId()));

        Assert.Equal(near, Fleets.Search(game, fighter, from, null, flag: false, stay: true));  // stage 3, nearest own
        near.Blockade = far.Blockade = true;
        var fleet = fleetHome.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        var carrier = fleet.Add(new GameObject(Ship(r => Field(r.Common.Ref, SdField.FighterCapacity) > 0 && r.Empire != 0).Common.Ref, Owner.Empire, game.NextId()));
        Assert.Equal(carrier, Fleets.Search(game, fighter, from, null, flag: false, stay: true)); // stage 4: the fleet's carrier
        fleet.Remove();
        Assert.Null(Fleets.Search(game, fighter, from, null, flag: false, stay: true));          // 5-7 refuse a fighter

        var empireFleet = from.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        // flag 0 closes the fleet's own system (1, 0x29), so it leaves (run 9 day 164): no own unblockaded system or
        // own fleet here, so stage 5, the nearest own blockaded system
        Assert.Equal(blockaded, Fleets.Search(game, empireFleet, from, null, flag: false, stay: true));
        Assert.Equal(from, Fleets.Search(game, empireFleet, from, null, flag: true, stay: true)); // flag != 0: stage 1 accepts it (0x204)
    }

    /// <summary>Bombardment value: ship value B - B*HullDamage div Hull (0 with ion left over); fighter value b - b*damage div SquadSize.</summary>
    [GameFact]
    public void BombardValues()
    {
        var (game, _) = Galaxy(1);
        var record = Ship(r => Field(r.Common.Ref, SdField.Bombardment) > 0);
        int b = Field(record.Common.Ref, SdField.Bombardment), hull = Field(record.Common.Ref, SdField.Hull);
        var ship = new GameObject(record.Common.Ref, Owner.Empire, game.NextId()) { HullDamage = hull / 3 };
        Assert.Equal(b - b * (hull / 3) / hull, Combat.ShipBombard(game, ship));
        ship.IonAccumulator = 1;
        Assert.Equal(0, Combat.ShipBombard(game, ship));
        var bomber = Definitions.Fighters.First(r => Field(r.Common.Ref, SdField.FighterBombardment) > 0);
        var squadron = new GameObject(bomber.Common.Ref, Owner.Empire, game.NextId()) { SquadSizeDamage = 5 };
        int fb = Field(bomber.Common.Ref, SdField.FighterBombardment), size = Field(bomber.Common.Ref, SdField.SquadronSize);
        Assert.Equal(fb - fb * 5 / size, Combat.FighterBombard(game, squadron));
    }

    /// <summary>Ion and turbolaser absorbed by the shields, the rest by the hull; then the critical-hit roll.</summary>
    [GameFact]
    public void AccumulatorsHitShieldsThenHull()
    {
        var (game, _) = Galaxy(1);
        var record = Ship(r => Field(r.Common.Ref, SdField.ShieldStrength) > 0 && Field(r.Common.Ref, SdField.Hull) > 10);
        int shield = Field(record.Common.Ref, SdField.ShieldStrength), hull = Field(record.Common.Ref, SdField.Hull);
        var ship = new GameObject(record.Common.Ref, Owner.Empire, game.NextId()) { IonAccumulator = shield + 3, LaserAccumulator = 5 };
        Combat.ApplyAccumulators(game, ship);
        Assert.Equal((3, 0, shield, 5), (ship.IonAccumulator, ship.LaserAccumulator, ship.ShieldsAbsorbed, ship.HullDamage));
        Assert.Equal(0, Combat.ShipBombard(game, ship));
    }

    /// <summary>
    /// Critical hit: x = 0 -> no draw; h <= 0 -> destroyed (0xF), no draw; else roll(50) + 100*x div (h + x) >= 50 -> roll(50) picks
    /// the CSCRHTTB kind (2-5 CH + 1, 6 hyperdrive modifier, 7 destroyed).
    /// </summary>
    [GameFact]
    public void CriticalHitThresholds()
    {
        var (game, _) = Galaxy(1);
        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;
        var record = Ship(r => Field(r.Common.Ref, SdField.Hull) >= 10);
        int hull = Field(record.Common.Ref, SdField.Hull);
        GameObject NewShip(int damage) => new(record.Common.Ref, Owner.Empire, game.NextId()) { HullDamage = damage };

        Combat.CriticalHit(game, NewShip(0), 0);
        Assert.Empty(draws);
        var dead = NewShip(hull);
        Combat.CriticalHit(game, dead, 1);
        Assert.Empty(draws);
        Assert.Equal((true, 0xF), (dead.Destroyed, dead.DestroyedReason));

        for (int i = 0; i < 200; i++)
        {
            // h = x: 100*x div 2x = 50 -> always the table roll; h = hull - 1, x = 1: 100 div hull < 50 unless the roll does it
            bool half = i % 2 == 0;
            int x = half ? hull / 2 : 1;
            var ship = NewShip(half ? hull - hull / 2 : 1);
            draws.Clear();
            Combat.CriticalHit(game, ship, x);
            draws.RemoveAll(d => d.Site == DrawSite.Timer); // a new crit starts the repair timer 0x380: roll(0), no step
            int h = hull - ship.HullDamage;
            Assert.Equal((DrawSite.CriticalHit, 51), (draws[0].Site, draws[0].N));
            bool table = draws[0].Result + 100 * x / (h + x) >= 50;
            Assert.Equal(table ? 2 : 1, draws.Count);
            if (half) Assert.True(table);
            int kind = table ? Missions.Table(game, "CSCRHTTB", draws[1].Result) ?? 0 : 1;
            var expected = kind switch
            {
                >= 2 and <= 5 => default(CriticalHitCounts).With(kind - 2, 1),
                6 => Field(record.Common.Ref, SdField.Hyperdrive) != 0 ? new CriticalHitCounts(0, 0, 0, 0, 1, 0)
                    : Field(record.Common.Ref, SdField.HyperdriveIfDamaged) != 0 ? new CriticalHitCounts(0, 0, 0, 0, 0, 1) : default,
                _ => default,
            };
            Assert.Equal(expected, ship.CriticalHits);
            Assert.Equal(kind == 7, ship.Destroyed);
        }
    }

    /// <summary>A synthetic battle record applied to a system's fleets and fighters.</summary>
    [GameFact]
    public void BattleResultApplication()
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var shipRecord = Ship(r => Field(r.Common.Ref, SdField.FighterCapacity) >= 2 && Field(r.Common.Ref, SdField.Hyperdrive) != 0 && r.Empire != 0);
        int hull = Field(shipRecord.Common.Ref, SdField.Hull), capacity = Field(shipRecord.Common.Ref, SdField.FighterCapacity);
        var fighterType = Definitions.Fighters.First(r => r.Empire != 0).Common.Ref;
        var allianceFighterType = Definitions.Fighters.First(r => r.Alliance != 0).Common.Ref;
        var fleet = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        var ship = fleet.Add(new GameObject(shipRecord.Common.Ref, Owner.Empire, game.NextId()));
        var stuck = fleet.Add(new GameObject(shipRecord.Common.Ref, Owner.Empire, game.NextId()) { HullDamage = 7 });
        var aboard = Enumerable.Range(0, capacity + 1).Select(_ => ship.Add(new GameObject(fighterType, Owner.Empire, game.NextId()))).ToList();
        var enemy = x.Add(new GameObject(allianceFighterType, Owner.Alliance, game.NextId()) { SquadSizeDamage = 2 });
        x.BattleReady = Owner.Empire;

        var fighters = new Dictionary<GameObject, FighterResult>
        {
            [aboard[0]] = new(12 - 3, ship),  // damaged, stays aboard
            [aboard[1]] = new(0, ship),       // destroyed (0xC)
            [enemy] = new(12, ship),          // repaired; carrier of another owner -> stays in the system
        };
        foreach (var f in aboard.Skip(2)) fighters[f] = new(12, ship);
        var ships = new Dictionary<GameObject, ShipResult>
        {
            [ship] = new(hull - 10, Speed: 3, Shield: 2, Hyperdrive: 1, Tractor: 0, Weapon: 1),
            [stuck] = new(1, 1, 1, 3, 1, 1), // hyperdrive hits beyond primary + backup: nothing written
        };
        var result = new BattleResult(Auto: true, S: [0, 1, 0, 1, 0, 0, 0], fighters, ships, new Dictionary<GameObject, DeathStarResult>());
        Combat.ApplyBattle(game, x, result);

        Assert.Equal(Owner.Alliance, x.BattleWon); // S[1], S[3] both != 0 -> opp(BattleReady)
        Assert.Equal(3, aboard[0].SquadSizeDamage);
        Assert.Equal((true, 0xC), (aboard[1].Destroyed, aboard[1].DestroyedReason));
        Assert.Equal((0, x), (enemy.SquadSizeDamage, enemy.Container));
        Assert.Equal(10, ship.HullDamage);
        Assert.Equal(new CriticalHitCounts(2, 1, 0, 3, 1, 0), ship.CriticalHits);
        Assert.Equal((7, default(CriticalHitCounts)), (stuck.HullDamage, stuck.CriticalHits));
        // capacity + 1 - 1 destroyed = capacity Existing aboard: no overflow
        Assert.Equal(aboard.Where(f => !f.Destroyed), ship.Contents.Where(f => f.Existing)); // the destroyed one stays aboard until its 0x372

        // withdraw: undecided, not automatic, S[6] != 0 -> BattleWon not written
        x.BattleWon = Owner.Empire;
        Combat.ApplyBattle(game, x, result with { Auto = false, S = [1, 0, 0, 0, 0, 0, 1] });
        Assert.Equal(Owner.Empire, x.BattleWon);
        // automatic: the fallback on S[0] / S[2]
        Combat.ApplyBattle(game, x, result with { S = [1, 0, 0, 0, 0, 0, 1] });
        Assert.Equal(Owner.Alliance, x.BattleWon);
    }

    /// <summary>
    /// Battle set clears the battle loss records; the 0x300 of a destroyed unit adds its CombatValue to
    /// its owner's record by DestroyedReason (0xC battle, 0xE assault, 0xF bombardment, others nowhere).
    /// </summary>
    [GameFact]
    public void LossRecordsFromDestroyedUnits()
    {
        var (game, s, _) = Scene();
        s.BattleLoss[1] = 99;
        Combat.SetBattle(game, s, true);
        Assert.Equal([0, 0, 0], s.BattleLoss);
        var shipType = Ship(r => r.Empire != 0 && Field(r.Common.Ref, SdField.CombatValue) != 0).Common.Ref;
        var allianceShipType = Ship(r => r.Alliance != 0 && Field(r.Common.Ref, SdField.CombatValue) != 0).Common.Ref;
        foreach (var (type, owner, reason) in new[] { (shipType, Owner.Empire, 0xC), (shipType, Owner.Empire, 0xC), (allianceShipType, Owner.Alliance, 0xC),
            (shipType, Owner.Empire, 0xE), (shipType, Owner.Empire, 0xF), (shipType, Owner.Empire, 5) })
            Objects.Destroy(game, Objects.Create(game, type, owner, s, completed: true), reason);
        game.Clock.Flush(game);
        int e = Field(shipType, SdField.CombatValue), a = Field(allianceShipType, SdField.CombatValue);
        Assert.Equal([a, 2 * e, 0], s.BattleLoss);
        Assert.Equal([0, e, 0], s.AssaultLoss);
        Assert.Equal([0, e, 0], s.BombardLoss);
    }

    /// <summary>BattleWon 0 with not both sides present: no winner and no loser, so no Autorouting. Battle := 0 clears the fleet Battle flags.</summary>
    [GameFact]
    public void BattleEndWithoutWinner()
    {
        var (game, s, _) = Scene();
        var fleet = s.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()) { Real = true, Battle = true });
        (s.Battle, s.BattleWon, s.BattleReady) = (true, Owner.None, Owner.None);
        Combat.EndBattle(game, s);
        Assert.False(s.Battle);
        Assert.Equal((true, false, false, false, false, false, false), FleetFlagsOf(fleet));
        Assert.False(fleet.Autorouting); // opp(0) = 1 would have autorouted the Alliance fleet
    }

    /// <summary>
    /// Ship speed: Hyperdrive - Hyperdrive * primary modifier, if 0 HyperdriveIfDamaged - HyperdriveIfDamaged * backup
    /// modifier, if 0 then 100; 0 unless Existing. Fighters ignore the modifiers.
    /// </summary>
    [GameFact]
    public void ShipSpeedFromHyperdriveModifiers()
    {
        var (game, _) = Galaxy(1);
        var type = Ship(r => Field(r.Common.Ref, SdField.Hyperdrive) != 0 && Field(r.Common.Ref, SdField.HyperdriveIfDamaged) != 0).Common.Ref;
        int primary = Field(type, SdField.Hyperdrive), backup = Field(type, SdField.HyperdriveIfDamaged);
        var ship = new GameObject(type, Owner.Empire, game.NextId());
        Assert.Equal(primary, Orders.Speed(game, ship));
        ship.SetCritical(4, 1);
        Assert.Equal(backup, Orders.Speed(game, ship));
        ship.SetCritical(5, 1);
        Assert.Equal(100, Orders.Speed(game, ship));
        ship.SetCritical(4, 0);
        Assert.Equal(primary, Orders.Speed(game, ship));
        ship.Existing = false;
        Assert.Equal(0, Orders.Speed(game, ship));

        var fighterType = Definitions.Fighters.First(r => Field(r.Common.Ref, SdField.Hyperdrive) != 0).Common.Ref;
        var fighter = new GameObject(fighterType, Owner.Empire, game.NextId());
        fighter.SetCritical(4, 1);
        Assert.Equal(Field(fighterType, SdField.Hyperdrive), Orders.Speed(game, fighter));
    }

    /// <summary>Fighters over FighterCapacity aboard (Existing) are moved to the system, the first Usable ones in key order.</summary>
    [GameFact]
    public void BattleFighterOverflow()
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var shipRecord = Ship(r => Field(r.Common.Ref, SdField.FighterCapacity) >= 1 && r.Empire != 0);
        int capacity = Field(shipRecord.Common.Ref, SdField.FighterCapacity), hull = Field(shipRecord.Common.Ref, SdField.Hull);
        var fleet = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        var ship = fleet.Add(new GameObject(shipRecord.Common.Ref, Owner.Empire, game.NextId()));
        var fighterType = Definitions.Fighters.First(r => r.Empire != 0).Common.Ref;
        var aboard = Enumerable.Range(0, capacity + 2).Select(_ => ship.Add(new GameObject(fighterType, Owner.Empire, game.NextId()))).ToList();
        var result = new BattleResult(true, [1, 0, 0, 0, 0, 0, 0], aboard.ToDictionary(f => f, _ => new FighterResult(12, ship)),
            new Dictionary<GameObject, ShipResult> { [ship] = new(hull, 0, 0, 0, 0, 0) }, new Dictionary<GameObject, DeathStarResult>());
        Combat.ApplyBattle(game, x, result);
        Assert.Equal(aboard.Take(2), x.Contents.Where(o => Family.Of(o.Type) is >= 0x1C and < 0x20));
        Assert.Equal(aboard.Skip(2), ship.Contents);
    }

    /// <summary>
    /// Automatic battle loop (no original vectors yet): the engine's own generator (fixed seed) makes the result repeatable and
    /// leaves the game generator alone; two strong ships eliminate one weak enemy without a withdraw (thresholds 0); the record
    /// covers every unit, so the application accepts it.
    /// </summary>
    [GameFact]
    public void AutomaticBattleLoop()
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        SdField[] arcs = [SdField.ForeTurbolaser, SdField.ForeIonCannon, SdField.ForeLaserCannon, SdField.AftTurbolaser, SdField.AftIonCannon,
            SdField.AftLaserCannon, SdField.PortTurbolaser, SdField.PortIonCannon, SdField.PortLaserCannon, SdField.StarboardTurbolaser,
            SdField.StarboardIonCannon, SdField.StarboardLaserCannon];
        int Armed(CapitalShipRecord r) => arcs.Sum(f => Field(r.Common.Ref, f));
        var strong = Definitions.CapitalShips.Where(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && Field(r.Common.Ref, SdField.WeaponRechargeRate) > 0).MaxBy(Armed)!;
        var weak = Definitions.CapitalShips.Where(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18 && Armed(r) > 0).MinBy(Armed)!;
        var empire = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        empire.Add(new GameObject(strong.Common.Ref, Owner.Empire, game.NextId()));
        empire.Add(new GameObject(strong.Common.Ref, Owner.Empire, game.NextId()));
        var alliance = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        var victim = alliance.Add(new GameObject(weak.Common.Ref, Owner.Alliance, game.NextId()));
        x.BattleReady = Owner.Empire;
        long draws = game.Random.Draws;

        var a = BattleEngine.Run(game, x);
        var b = BattleEngine.Run(game, x);
        Assert.Equal(draws, game.Random.Draws);
        Assert.Equal(a.S, b.S);
        Assert.Equal(a.Ships, b.Ships);
        Assert.Equal([0, 0, 0, 1, 0, 0, 0], a.S); // side 1 eliminated
        Assert.Equal(0, a.Ships[victim].Hull);
        Assert.Equal(3, a.Ships.Count);
        Combat.ApplyBattle(game, x, a);
        Assert.Equal(Owner.Empire, x.BattleWon);
        Assert.True(victim.Destroyed);
    }

    // Issue #18: a fleet moving to the battle system sits there Enroute (EnrouteActive from departure), so neither it
    // nor its ships are battle participants for the report
    [GameFact]
    public void ReportUnitsSkipFleetsInHyperspace()
    {
        var (game, s) = Galaxy(2);
        var x = s[0];
        var ship = Ship(r => r.Alliance != 0 && Field(r.Common.Ref, SdField.Hyperdrive) != 0);
        var present = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId())).Add(new GameObject(ship.Common.Ref, Owner.Alliance, game.NextId()));
        var incoming = s[1].Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        var moving = incoming.Add(new GameObject(ship.Common.Ref, Owner.Alliance, game.NextId()));
        Fleets.Move(game, incoming, x);
        Assert.Same(x, incoming.Container);
        Assert.True(moving.Existing && !moving.Usable);
        var units = Combat.Units(x);
        Assert.Contains(present, units);
        Assert.DoesNotContain(moving, units);
    }

    // A battle with no recorded result runs the engine. The battle builds no message row (type 1 is not listed).
    [GameFact]
    public void AutomaticBattleIsHostedAndNotListed()
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var strong = Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && Field(r.Common.Ref, SdField.WeaponRechargeRate) > 0 && Field(r.Common.Ref, SdField.ForeTurbolaser) > 0);
        var weak = Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18);
        x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId())).Add(new GameObject(strong.Common.Ref, Owner.Empire, game.NextId()));
        x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId())).Add(new GameObject(weak.Common.Ref, Owner.Alliance, game.NextId()));
        (x.BattlePending, x.BattleToHost) = (true, true);
        game.BattleTrace = [];
        var log = new Messages(game, Owner.Alliance, c => $"{c.Ref:X}");
        Combat.HostBattles(game);
        Assert.Single(game.BattleTrace); // hosted: the battle report windows read it
        Assert.DoesNotContain(log.Update(), e => e.Type == 1 || e.Text is not null);
        // The host walk cleared BattleToHost; a system still (or again) BattlePending without a rise is not hosted again
        Assert.False(x.BattleToHost);
        int hosted = 0;
        game.BattleResults = _ => { hosted++; return null; };
        x.BattlePending = true;
        Combat.HostBattles(game);
        Assert.Equal(0, hosted);
    }

    // A fleet turning un-Usable while Enroute and Existing in a BattlePending system, owner != BattleWon,
    // sets BattleWithdrew once; the next BattlePending rise clears it; BattleToHost is set on every rise
    [GameFact]
    public void BattleWithdrewRecordsTheFirstLeavingSide()
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var fleet = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        Combat.SetBattlePending(game, x, true);
        Assert.True(x.BattleToHost);
        x.BattleWon = Owner.Empire;
        (fleet.Enroute, fleet.Usable) = (true, false);
        Fleets.SystemContentUsableChanged(game, x, fleet);
        Assert.Equal(Owner.Alliance, x.BattleWithdrew);
        var other = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        (other.Enroute, other.Usable) = (true, false);
        Fleets.SystemContentUsableChanged(game, x, other);
        Assert.Equal(Owner.Alliance, x.BattleWithdrew); // still 0 only once
        Combat.SetBattlePending(game, x, false);
        Combat.SetBattlePending(game, x, true);
        Assert.Equal(Owner.None, x.BattleWithdrew);
    }

    // A real departure (0x201) out of a BattlePending system writes BattleWithdrew on the departure system: the
    // 0x301 hook runs on the parent captured at post time, before the 0x320 parent change
    [GameFact]
    public void DepartureFromABattleSetsBattleWithdrew()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 12345), Definitions);
        var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>())
            .First(f => f.Owner == Owner.Alliance && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Usable));
        var from = fleet.System!;
        var to = game.Systems.First(s => s != from && s.Owner == Owner.Alliance);
        var enemy = from.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()));
        enemy.Add(new GameObject(Ship(r => r.Empire != 0).Common.Ref, Owner.Empire, game.NextId()));
        Combat.SetBattlePending(game, from, true);
        from.BattleWon = Owner.Empire;
        OrderResult? result = null;
        Orders.Submit(game, new Command(0x201, Owner.Alliance) { Targets = [(fleet, 0)], ArgRef = to }, r => result = r);
        while (game.Clock.DispatchOne(game)) { }
        Assert.True(result?.Success);
        Assert.Equal(Owner.Alliance, from.BattleWithdrew);
        Assert.Equal(Owner.None, to.BattleWithdrew);
    }

    // A human controller with a battle window holds the host in state 6 (clock paused, save refused) until it
    // answers; the answer is the side's decision float, released after the battle
    [GameFact]
    public void HumanBattleRequestWaitsForTheAnswer()
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var ship = Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18);
        x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId())).Add(new GameObject(ship.Common.Ref, Owner.Empire, game.NextId()));
        x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId())).Add(new GameObject(ship.Common.Ref, Owner.Alliance, game.NextId()));
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance) { AskBattles = true };
        game.Controllers[Owner.Alliance] = human;
        int hosted = 0;
        game.BattleResults = _ => { hosted++; return null; };
        (x.BattlePending, x.BattleToHost, game.BattleRequired) = (true, true, true);
        int tick = game.Clock.Tick;
        game.Clock.Step(game);
        game.Clock.Step(game);
        Assert.Equal(tick + 1, game.Clock.Tick); // only the first step advanced the clock
        Assert.Same(x, game.HostedBattle);
        Assert.Same(x, human.BattleRequest);
        Assert.Equal(0, hosted);
        Assert.Throws<InvalidOperationException>(() => SaveGame.Save(game, new MemoryStream()));
        human.BattleAnswer = 10000.0;
        Assert.Equal(10000.0, human.BattleFloat(game, x));
        game.Clock.Step(game);
        Assert.Equal(1, hosted);
        Assert.Null(game.HostedBattle);
        Assert.Null(human.BattleRequest);
        Assert.Null(human.BattleAnswer);
    }

    /// <summary>
    /// Worked example under the corrected rules: two 0x14000046 (A1, A2; side 1, one fleet, one laser group) against one
    /// 0x14000081 (E), decision floats 0. The first eight battle-generator draws after the 16-step warm-up are 79, 70, 35, 61, 1, 73,
    /// 52, 8. E's group keeps A1 while its TTK &lt; 300: A1 shield crits at steps 3, 38, 47, 69, first hull hit at 123; E shield crits
    /// at 31, 228. With retreat allowed side 1 withdraws in step 266 (A1 destroyed, its destroy the last draw; A2 retreats;
    /// S[0], S[3]; 534 draws); without, A2's first hull hit is in step 411 and the last draw an A2 hull draw in step 810 (S[3]; 1621).
    /// </summary>
    [GameTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutomaticBattleWorkedExample(bool retreat)
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var a = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()));
        var ship1 = a.Add(new GameObject(0x14000046, Owner.Alliance, game.NextId()));
        var ship2 = a.Add(new GameObject(0x14000046, Owner.Alliance, game.NextId()));
        var enemy = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId())).Add(new GameObject(0x14000081, Owner.Empire, game.NextId()));
        var trace = new List<(string, int, int)>();
        var crits = new List<(long Step, GameObject Unit, int K)>();
        (BattleEngine.Trace, BattleEngine.CritTrace, BattleEngine.RetreatAllowedOverride) = (trace, crits, (_, _) => retreat);
        try
        {
            var r = BattleEngine.Run(game, x);
            Assert.Equal([79, 70, 35, 61, 1, 73, 52, 8], trace.Take(8).Select(t => t.Item3));
            string one = $"{ship1.Ref:X8}", two = $"{ship2.Ref:X8}", e = $"{enemy.Ref:X8}";
            Assert.Equal([$"{one} 1 s", $"{e} 1 s", $"{one} 2 s", $"{e} 2 s", $"{one} 3 s", $"{e} 3 s"], trace.Take(6).Select(t => t.Item1));
            long[] ShieldCrits(GameObject o) => [.. crits.Where(c => c.Unit == o && c.K == 0).Select(c => c.Step).Take(4)];
            long FirstHull(string unit) => long.Parse(trace.First(t => t.Item1.StartsWith(unit) && t.Item1.EndsWith('h')).Item1.Split(' ')[1]);
            Assert.Equal([3, 38, 47, 69], ShieldCrits(ship1));
            Assert.Equal([31, 228], ShieldCrits(enemy).Take(2));
            Assert.Equal(123, FirstHull(one));
            Assert.Equal(500, r.Ships[enemy].Hull);
            if (retreat)
            {
                Assert.Equal([1, 0, 0, 1, 0, 0, 0], r.S);
                Assert.Equal(534, trace.Count);
                Assert.Equal($"{one} 266 h", trace[^1].Item1);
                Assert.Equal(new ShipResult(0, 3, 4, 1, 3, 4), r.Ships[ship1]);
                Assert.Equal((360, 3), (r.Ships[ship2].Hull, r.Ships[ship2].Shield));
                Assert.Equal(2, r.Ships[enemy].Shield);
            }
            else
            {
                Assert.Equal([0, 0, 0, 1, 0, 0, 0], r.S);
                Assert.Equal(1621, trace.Count);
                Assert.Equal(411, FirstHull(two));
                Assert.StartsWith($"{two} 810 h", trace[^1].Item1);
                Assert.Equal(4, r.Ships[enemy].Shield);
                Assert.Equal(0, r.Ships[ship1].Hull);
                Assert.Equal(0, r.Ships[ship2].Hull);
            }
        }
        finally { (BattleEngine.Trace, BattleEngine.CritTrace, BattleEngine.RetreatAllowedOverride) = (null, null, null); }
    }

    /// <summary>
    /// Death Star battle examples: one Empire fleet with the Death Star 0x18000088 against one Alliance fleet with 0x14000046 (example 1); plus an
    /// X-wing squadron directly in the system (example 2); plus a Usable Death Star shield facility (example 3, shielded).
    /// </summary>
    static (GameState Game, StarSystem System, GameObject DeathStar, GameObject Ship, GameObject? XWing) DeathStarBattle(bool xwing, bool shield = false)
    {
        var (game, s) = Galaxy(1);
        var x = s[0];
        var ds = x.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId())).Add(new GameObject(0x18000088, Owner.Empire, game.NextId()));
        var ship = x.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId())).Add(new GameObject(0x14000046, Owner.Alliance, game.NextId()));
        var wing = xwing ? x.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId())) : null;
        if (shield) x.Add(new GameObject(Definitions.DefenseFacilities.First(r => Family.Of(r.Common.Ref) == 0x25).Common.Ref, Owner.Empire, game.NextId()));
        x.BattleReady = Owner.Empire;
        return (game, x, ds, ship, wing);
    }

    static BattleResult RunTraced(GameState game, StarSystem x, bool retreat, List<(string Site, int N, int Result)> trace, List<string>? steps = null)
    {
        (BattleEngine.Trace, BattleEngine.StateTrace, BattleEngine.RetreatAllowedOverride) = (trace, steps, (_, _) => retreat);
        try { return BattleEngine.Run(game, x); }
        finally { (BattleEngine.Trace, BattleEngine.StateTrace, BattleEngine.RetreatAllowedOverride) = (null, null, null); }
    }

    // Example 1: the superlaser destroys 0x14000046 in step 1 (draws 79, 70)
    [GameFact]
    public void DeathStarSuperlaserFiresInStepOne()
    {
        var (game, x, ds, ship, _) = DeathStarBattle(xwing: false);
        var trace = new List<(string Site, int N, int Result)>();
        var steps = new List<string>();
        var r = RunTraced(game, x, false, trace, steps);
        Assert.Equal([79, 70], trace.Select(t => t.Result));
        Assert.Single(steps);
        Assert.Equal([0, 0, 0, 1, 0, 0, 0], r.S);
        Assert.Equal(new DeathStarResult(25000, true), r.DeathStars[ds]);
        Combat.ApplyBattle(game, x, r);
        Assert.Equal(Owner.Empire, x.BattleWon);
        Assert.True(ship.Destroyed);
        Assert.Equal(0xC, ship.DestroyedReason);
        Assert.False(ds.Destroyed);
        Assert.Equal(0, ds.HullDamage);
    }

    // Example 2: the X-wing formation destroys the Death Star at its third visit (8 draws, step 2)
    [GameFact]
    public void XWingAttackDestroysTheDeathStar()
    {
        var (game, x, ds, ship, wing) = DeathStarBattle(xwing: true);
        var trace = new List<(string Site, int N, int Result)>();
        var steps = new List<string>();
        var r = RunTraced(game, x, false, trace, steps);
        Assert.Equal([100, 100, 9, 99, 9, 99, 9, 99], trace.Select(t => t.N));
        Assert.Equal([79, 70, 3, 61, 0, 72, 4, 8], trace.Select(t => t.Result)); // r1 = 4, 1, 5; r3 = 62, 73, 9
        Assert.Equal(2, steps.Count);
        Assert.Equal([0, 1, 0, 0, 0, 0, 0], r.S);
        Assert.Equal(new DeathStarResult(0, false), r.DeathStars[ds]);
        Assert.Equal(12, r.Fighters[wing!].Left);
        Combat.ApplyBattle(game, x, r);
        Assert.Equal(Owner.Alliance, x.BattleWon);
        Assert.True(ship.Destroyed);
        Assert.True(ds.Destroyed);
        Assert.Equal(0xC, ds.DestroyedReason);
        Assert.Equal(25000, ds.HullDamage);
    }

    // Example 3: a Death Star shield facility stops the attack (no rand(9) / rand(99)); the stall
    // rule (side index 0 compared at -1) destroys the X-wing
    [GameFact]
    public void ShieldedDeathStarIsNotAttacked()
    {
        var (game, x, ds, _, wing) = DeathStarBattle(xwing: true, shield: true);
        var trace = new List<(string Site, int N, int Result)>();
        var r = RunTraced(game, x, false, trace);
        Assert.Equal([79, 70], trace.Take(2).Select(t => t.Result));
        Assert.DoesNotContain(trace, t => t.N is 9 or 99);
        Assert.Equal([0, 0, 0, 1, 0, 0, 0], r.S);
        Assert.Equal(0, r.Fighters[wing!].Left);
        Assert.True(r.DeathStars[ds].Alive);
    }

    // The ship manager's unfinished Death Star is registered after the fleet's,
    // so it is the scene: no superlaser, side 2 can retreat with no unit able to jump, and the finished one takes its hull (repair)
    [GameFact]
    public void UnfinishedDeathStarIsTheScene()
    {
        var (game, x, ds, ship, _) = DeathStarBattle(xwing: false);
        ds.HullDamage = 1000;
        var mm = x.Managers[(int)ManufacturingKind.Ship] = new ManufacturingManager(x, ManufacturingKind.Ship, game.NextId());
        var building = new GameObject(0x18000088, Owner.Empire, game.NextId());
        (building.Completed, building.Usable) = (false, false);
        mm.Orders.Add(building);
        var trace = new List<(string Site, int N, int Result)>();
        var steps = new List<string>();
        var r = RunTraced(game, x, true, trace, steps);
        Assert.Empty(trace);
        Assert.Contains("side 2 str 0 low 0 stall 1 state 5", steps[0]); // withdrew in step 1: retreat possible without a unit
        Assert.Equal([0, 1, 0, 0, 0, 0, 0], r.S); // the scene is not alive: side 2 has no unit
        Assert.Equal(new DeathStarResult(25000, true), r.DeathStars[ds]);
        Combat.ApplyBattle(game, x, r);
        Assert.Equal(0, ds.HullDamage);
        Assert.False(ship.Destroyed);
    }
}
