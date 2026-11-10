using OpenSWR.Data.Dat;
using OpenSWR.Data.Mods;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>
/// Unit roles and doctrine of the computer opponent. The roles come from definition stats
/// only: the mod tests add invented units (their stats here are made up, not game data) to the install's definitions.
/// </summary>
public class DoctrineTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>The install's definitions plus one mod of the given records.</summary>
    static GameDefinitions Modded(string records)
    {
        string dir = Path.Combine(Path.GetTempPath(), "openswr-doctrine-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "m"));
            File.WriteAllText(Path.Combine(dir, "m", "mod.json"), "{\"id\": \"m\", \"version\": \"1\"}");
            File.WriteAllText(Path.Combine(dir, "m", "defs.json"), $"{{\"records\": [{records}]}}");
            return ModLoader.Load(GameDir.Path, dir, ["m"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    static (GameState Game, OpenSWRController Ai) Game(GameDefinitions definitions, Owner side)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), definitions);
        var ai = new OpenSWRController(side) { IssueTrace = [], Features = OpenSWRController.DefaultFeatures.Union(OpenSWRController.Doctrine) };
        var other = side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        game.Controllers[side] = ai;
        game.Controllers[other] = new HumanController(other);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    // an invented picket: lasers only on every arc, a fast weapon recharge; every other role stat 0
    static string Picket(uint from) => $$"""
        {"op": "add", "type": "capitalShip", "id": "picket", "from": "{{FamilyNames.BaseId(from)}}", "name": "Test Picket", "alliance": 1, "empire": 0,
         "maintenanceCost": 20, "researchOrder": 0, "refinedMaterialCost": 10, "weaponRechargeRate": 50,
         "fore": {"turbolaser": 0, "ionCannon": 0, "laserCannon": 4}, "aft": {"turbolaser": 0, "ionCannon": 0, "laserCannon": 4},
         "port": {"turbolaser": 0, "ionCannon": 0, "laserCannon": 4}, "starboard": {"turbolaser": 0, "ionCannon": 0, "laserCannon": 4},
         "fighterCapacity": 0, "troopCapacity": 0, "bombardment": 0, "interdictionStrength": 0}
        """;

    /// <summary>
    /// Mod test: an invented anti-fighter ship gets the screen role, is the best screen per maintenance, and is the ship the
    /// doctrine builds when a group lacks a screen (the other roles met or less short).
    /// </summary>
    [GameFact]
    public void InventedShipGetsItsRoleAndIsBuiltWhenShort()
    {
        uint from = Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) == Families.CapitalShip).Common.Ref;
        var d = Modded(Picket(from));
        uint picket = d.CapitalShips.Single(r => r.Common.Ref != 0 && !Definitions.CapitalShips.Any(b => b.Common.Ref == r.Common.Ref)).Common.Ref;
        var (game, ai) = Game(d, Owner.Alliance);
        var roles = ai.BuildableRoles(game);
        Assert.True(roles.ContainsKey(picket), "the invented ship is not buildable");
        var r = roles[picket];
        output.WriteLine($"picket: main {r.Main}, " + string.Join(" ", r.Scores.Select(e => $"{e.Key}={e.Value:F2}")));
        Assert.Equal(UnitRole.Screen, r.Main);
        Assert.False(r.Dominated);
        Assert.Equal(picket, ai.BestFor(game, UnitKind.CapitalShip, UnitRole.Screen, 1000)!.Common.Ref);
        // a group with a large line fleet at its staging system (line met) and no screening ship: the screen short
        var target = game.Systems.Where(s => s.Owner == Owner.Empire).OrderBy(s => s.Key).First();
        var staging = game.Systems.Where(s => s.Owner == Owner.Alliance).OrderBy(s => s.Key).First();
        ai = new OpenSWRController(Owner.Alliance) { Features = OpenSWRController.Doctrine }; // no groups of its own
        var heavy = roles.Values.Where(x => x.Kind == UnitKind.CapitalShip && x.Main == UnitRole.Line).MaxBy(x => x.Maintenance)!.Type;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, staging, completed: true);
        for (int i = 0; i < 20; i++) Objects.Create(game, heavy, Owner.Alliance, fleet, completed: true);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Alliance);
        ai.AddGroup(target, staging);
        var shortfall = ai.RoleShortfall(game, k);
        output.WriteLine("shortfall " + string.Join(" ", shortfall.Select(e => $"{e.Key}={e.Value:F2}")));
        Assert.Equal(1, shortfall[UnitRole.Screen]);
        Assert.Equal(0, shortfall[UnitRole.Line]);
        if (shortfall.GetValueOrDefault(UnitRole.Durability) == 0) // no interdictor slot open
            Assert.Equal(picket, ai.DoctrineShip(game, k, 1000)!.Common.Ref);
    }

    /// <summary>
    /// Space roles first: an invented warship with heavy bombardment stays a line ship (bombardment is a capability), and an
    /// invented copy of it without bombardment but slightly tougher does not dominate it (bombardment protects); the copy with
    /// bombardment and less toughness is dominated by the original (bombardment does not decide between bombarding warships).
    /// </summary>
    [GameFact]
    public void BombardmentIsACapability()
    {
        uint from = Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) == Families.CapitalShip).Common.Ref;
        string Ship(string id, int bombardment, int hull) => $$"""
            {"op": "add", "type": "capitalShip", "id": "{{id}}", "from": "{{FamilyNames.BaseId(from)}}", "name": "Test {{id}}", "alliance": 1, "empire": 0,
             "maintenanceCost": 40, "researchOrder": 0, "refinedMaterialCost": 10, "weaponRechargeRate": 60, "hull": {{hull}}, "bombardment": {{bombardment}},
             "fore": {"turbolaser": 10, "ionCannon": 0, "laserCannon": 0}, "aft": {"turbolaser": 10, "ionCannon": 0, "laserCannon": 0},
             "port": {"turbolaser": 10, "ionCannon": 0, "laserCannon": 0}, "starboard": {"turbolaser": 10, "ionCannon": 0, "laserCannon": 0},
             "fighterCapacity": 0, "troopCapacity": 0, "interdictionStrength": 0}
            """;
        var d = Modded(string.Join(", ", Ship("bomber", 20, 3000), Ship("plain", 0, 3100), Ship("weak", 30, 2900)));
        var added = d.CapitalShips.Where(r => !Definitions.CapitalShips.Any(b => b.Common.Ref == r.Common.Ref)).OrderBy(r => r.Common.Ref).Select(r => r.Common.Ref).ToList();
        var (game, ai) = Game(d, Owner.Alliance);
        var roles = ai.BuildableRoles(game);
        var (bomber, plain, weak) = (roles[added[0]], roles[added[1]], roles[added[2]]);
        output.WriteLine($"bomber {bomber.Main} {bomber.Dominated}, plain {plain.Main} {plain.Dominated}, weak {weak.Main} {weak.Dominated}");
        Assert.Equal(UnitRole.Line, bomber.Main);
        Assert.False(bomber.Dominated);
        Assert.True(weak.Dominated);
    }

    /// <summary>
    /// Fighter yard (DoctrineFighterYard): over 300 days, a ship manager that is a fighter yard at a decision orders no capital
    /// ship there, only fighters (into its carrier group); each carrier group has at most one yard.
    /// </summary>
    [GameFact]
    public void FighterYardTakesOnlyFighters()
    {
        var (game, ai) = Game(Definitions, Owner.Empire);
        ai.Features = OpenSWRController.DefaultFeatures.Remove(OpenSWRController.V7.DoctrineProduction).Add(OpenSWRController.V7.DoctrineFighterYard); // the default before DoctrineProduction, which this test was written for
        game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
        int yardDecisions = 0, fighterOrders = 0;
        while (game.Day < 300 && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count, decided = ai.Decided;
            var yards = ai.FighterYards(game);
            Assert.Equal(yards.Values.Count, yards.Values.Select(f => f.Container).Distinct().Count());
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            yardDecisions += yards.Count;
            foreach (var t in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.ProduceShip && yards.ContainsKey((ManufacturingManager)t.Command.Targets[0].Target)))
            {
                Assert.True(Family.Of(t.Command.Fields[0]) is >= Families.Fighter and < Families.Headquarters, $"day {game.Day}: a capital ship at a fighter yard");
                fighterOrders++;
            }
        }
        output.WriteLine($"fighter-yard manager-decisions {yardDecisions}, fighter orders there {fighterOrders}");
        Assert.True(yardDecisions > 0);
    }

    /// <summary>Dominated units: an invented gun weaker and dearer than a buildable one is marked dominated and never chosen, nor built over 150 days.</summary>
    [GameFact]
    public void DominatedUnitsAreNeverBuilt()
    {
        var gun = Definitions.DefenseFacilities.Where(r => r.Alliance != 0 && r.AttackStrength > 0 && r.ResearchOrder == 0).OrderBy(r => r.Common.Ref).First();
        string records = $$"""
            {"op": "add", "type": "facility", "id": "dud", "from": "{{FamilyNames.BaseId(gun.Common.Ref)}}", "name": "Test Dud", "alliance": 1, "empire": 1,
             "maintenanceCost": {{gun.MaintenanceCost + 3}}, "attackStrength": {{Math.Max(1, gun.AttackStrength / 4)}}, "shieldStrength": 0,
             "bombardmentDefense": 0, "researchOrder": 0, "refinedMaterialCost": 10}
            """;
        var d = Modded(records);
        uint dud = d.DefenseFacilities.Single(r => !Definitions.DefenseFacilities.Any(b => b.Common.Ref == r.Common.Ref)).Common.Ref;
        var (game, ai) = Game(d, Owner.Alliance);
        var roles = ai.BuildableRoles(game);
        Assert.True(roles.ContainsKey(dud));
        Assert.True(roles[dud].Dominated);
        Assert.NotEqual(dud, ai.BestFor(game, UnitKind.Defence, UnitRole.Gun, 1000)!.Common.Ref);
        game.Controllers[Owner.Empire] = new ComputerController(Owner.Empire) { Rand = _ => 0 };
        while (game.Day < 150 && !game.GameOver) game.Clock.Step(game);
        Assert.DoesNotContain(ai.IssueTrace!, t => t.Result.Success && t.Command.Type == CommandType.ProduceFacility && t.Command.Fields[0] == dud);
        Assert.DoesNotContain(game.Created, o => o.Type == dud && o.Owner == Owner.Alliance);
    }

    /// <summary>Doctrine template: the screen need follows the known enemy fighters at the target (one screen per 5 squadrons x 1.2, at least 1).</summary>
    [GameFact]
    public void TemplateFollowsTheKnownEnemy()
    {
        var (game, _) = Game(Definitions, Owner.Empire);
        var ai = new OpenSWRController(Owner.Empire) { Features = OpenSWRController.Doctrine }; // no groups of its own
        var target = game.Systems.Where(s => s.Owner == Owner.Alliance && !s.Contents.OfType<Fleet>().Any(f => f.Usable)).OrderBy(s => s.Key).First();
        var staging = game.Systems.Where(s => s.Owner == Owner.Empire).OrderBy(s => s.Key).First();
        ai.AddGroup(target, staging);
        // the side sees the target: an own fleet there
        var eye = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, target, completed: true);
        // two screening ships there (main role screen, from the computed roles)
        uint screen = ai.BuildableRoles(game).Values.Where(x => x.Kind == UnitKind.CapitalShip && x.Main == UnitRole.Screen).MinBy(x => x.Type)!.Type;
        for (int i = 0; i < 2; i++) Objects.Create(game, screen, Owner.Empire, eye, completed: true);
        // and enough line ships to keep the screens below MaxScreenShare of the group's maintenance
        uint heavy = ai.BuildableRoles(game).Values.Where(x => x.Kind == UnitKind.CapitalShip && x.Main == UnitRole.Line).MaxBy(x => x.Maintenance)!.Type;
        for (int i = 0; i < 10; i++) Objects.Create(game, heavy, Owner.Empire, eye, completed: true);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        double none = ai.RoleShortfall(game, k).GetValueOrDefault(UnitRole.Screen);
        uint fighter = Definitions.Fighters.First(r => r.Alliance != 0).Common.Ref;
        for (int i = 0; i < 12; i++) Objects.Create(game, fighter, Owner.Alliance, target, completed: true);
        game.Clock.Flush(game);
        k = new OpenSWRController.Known(game, Owner.Empire);
        var roles = ai.BuildableRoles(game);
        // the group's ships: own fleets at its staging system and at its target
        int screensThere = game.Systems.Where(x => x == staging || x == target).SelectMany(x => x.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Empire && f.Existing)
            .SelectMany(f => f.Contents).Count(x => roles.TryGetValue(x.Type, out var r) && r.Main == UnitRole.Screen);
        double many = ai.RoleShortfall(game, k).GetValueOrDefault(UnitRole.Screen);
        output.WriteLine($"screen shortfall without fighters {none:F2}, with 12 squadrons {many:F2} (screens there {screensThere})");
        int squadrons = k.Contents(target).Count(v => v.Owner == Owner.Alliance && v.In(Families.Fighter, Families.Headquarters)); // the 12 and any already there
        Assert.True(squadrons >= 12);
        int wanted = (int)Math.Ceiling(1.2 * squadrons / OpenSWRController.SquadronsPerScreen);
        Assert.True(screensThere >= 2);
        Assert.Equal(0, none, 3); // one wanted, two or more there
        Assert.Equal((double)(wanted - screensThere) / wanted, many, 3);
        Assert.True(many > 0);
    }
}
