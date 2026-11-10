using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent Bombard rule: own fleets with bombardment bombard enemy garrisons.</summary>
public class OpenSWRAiBombardTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 Bombard = OpenSWRController.V7.Bombard;

    /// <summary>
    /// A new game (Alliance side) with the Alliance's weakest fleet with bombardment moved into an Empire-held system whose view shows
    /// Empire troops, no Empire fleet (moved out) and Alliance support below 50 %.
    /// </summary>
    static (GameState Game, OpenSWRController Ai, Fleet Fleet, StarSystem Target) Setup()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Alliance) { Features = OpenSWRController.DefaultFeatures.Add(Bombard), IssueTrace = [] };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        var k0 = new OpenSWRController.Known(game, Owner.Alliance);
        var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Alliance && f.Usable && !f.Enroute && Combat.FleetBombard(game, f) > 0)
            .OrderBy(f => k0.Strength(f, Owner.Alliance)).ThenBy(f => f.Key).First();
        var target = game.Systems.Where(s => s.Owner == Owner.Empire && s.Support < 50
            && s.Contents.Any(x => Family.IsTroop(x.Type) && x.Owner == Owner.Empire && x.Existing)).OrderBy(s => s.Key).First();
        // the Empire's fleets there leave for another Empire system: a weak (empty) orbit
        var elsewhere = game.Systems.Where(s => s.Owner == Owner.Empire && s != target).OrderBy(s => s.Key).First();
        foreach (var f in target.Contents.OfType<Fleet>().Where(f => f.Owner == Owner.Empire).ToList()) Objects.SetParent(game, f, elsewhere);
        Objects.SetParent(game, fleet, target);
        Knowledge.RecomputeSystem(game, target); // the own fleet makes the system's view live (no flush: no arrival handling)
        return (game, ai, fleet, target);
    }

    static List<Command> Bombards(OpenSWRController ai) => [.. ai.IssueTrace!.Where(x => x.Result.Success && x.Command.Type is >= CommandType.Bombard and <= CommandType.BombardDestroy).Select(x => x.Command)];

    /// <summary>A fleet with bombardment in an enemy system with a weak orbit bombards (the bombard order at that system, the fleet a target).</summary>
    [GameFact]
    public void WeakOrbitBombards()
    {
        var (game, ai, fleet, target) = Setup();
        ai.Bombard(game, new OpenSWRController.Known(game, Owner.Alliance), []);
        output.WriteLine($"fleet {Family.Of(fleet.Ref)}:{SdRef.IndexOf(fleet.Ref)} bombard {Combat.FleetBombard(game, fleet)} at {Family.Of(target.Ref)}:{SdRef.IndexOf(target.Ref)}");
        var c = Assert.Single(Bombards(ai));
        Assert.Equal(CommandType.Bombard, c.Type);
        Assert.Same(target, c.ArgRef);
        Assert.Contains(c.Targets, t => t.Target == fleet);
        Assert.Equal(1, ai.Bombards);
    }

    /// <summary>A known enemy fleet stronger than the bombarding fleets blocks the bombardment.</summary>
    [GameFact]
    public void StrongEnemyFleetBlocks()
    {
        var (game, ai, fleet, target) = Setup();
        var k0 = new OpenSWRController.Known(game, Owner.Empire);
        var enemy = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Empire && f.Usable && !f.Enroute)
            .OrderByDescending(f => k0.Strength(f, Owner.Empire)).ThenBy(f => f.Key).First();
        Objects.SetParent(game, enemy, target);
        Knowledge.RecomputeSystem(game, target);
        var k = new OpenSWRController.Known(game, Owner.Alliance);
        output.WriteLine($"enemy fleet strength {k.Strength(enemy, Owner.Empire)}, own {k.Strength(fleet, Owner.Alliance)}");
        Assert.True(k.Strength(enemy, Owner.Empire) > k.Strength(fleet, Owner.Alliance));
        ai.Bombard(game, k, []);
        Assert.Empty(Bombards(ai));
    }

    /// <summary>The 5-day cooldown: no second bombardment of the system before <see cref="OpenSWRController.BombardDays"/> days, one after.</summary>
    [GameFact]
    public void CooldownHolds()
    {
        var (game, ai, fleet, target) = Setup();
        OpenSWRController.Known K() => new(game, Owner.Alliance);
        // the clock's arrival handling of a fleet set into the system by hand makes it Enroute: each check takes a fresh fleet
        var fresh = new Queue<Fleet>(game.Systems.SelectMany(s => s.Contents.OfType<Fleet>())
            .Where(f => f != fleet && f.Owner == Owner.Alliance && f.Usable && !f.Enroute && Combat.FleetBombard(game, f) > 0).OrderBy(f => f.Key));
        void Fresh()
        {
            Objects.SetParent(game, fresh.Dequeue(), target);
            Knowledge.RecomputeSystem(game, target);
        }
        ai.Bombard(game, K(), []);
        Assert.Single(Bombards(ai));
        ai.Bombard(game, K(), []); // same day
        Assert.Single(Bombards(ai));
        int first = game.Day;
        // both sides are human controllers: the AI's orders never run, nothing is bombarded
        while (game.Day < first + OpenSWRController.BombardDays - 1) game.Clock.Step(game);
        Fresh();
        ai.Bombard(game, K(), []);
        Assert.Single(Bombards(ai));
        while (game.Day < first + OpenSWRController.BombardDays) game.Clock.Step(game);
        Fresh();
        output.WriteLine($"day {first} → {game.Day}, {Bombards(ai).Count} bombardments");
        ai.Bombard(game, K(), []);
        Assert.Equal(2, Bombards(ai).Count);
        Assert.All(Bombards(ai), c => Assert.Same(target, c.ArgRef));
    }

    /// <summary>The flag is off in the default (the flag-off byte-equal CSV is aimatch's check), and the cooldown survives save, then load.</summary>
    [GameFact]
    public void OffByDefaultAndSaved()
    {
        Assert.DoesNotContain(Bombard, OpenSWRController.DefaultFeatures);
        Assert.True(OpenSWRController.ParseFeatures("Bombard") is { } parsed && parsed.SetEquals([Bombard])); // aimatch --features
        var (game, ai, _, _) = Setup();
        ai.Bombard(game, new OpenSWRController.Known(game, Owner.Alliance), []);
        var j = ai.Save().ToJsonString();
        Assert.Contains("\"bombarded\"", j);
        var copy = new OpenSWRController(Owner.Alliance) { Features = ai.Features };
        copy.Load(game, 0, ai.Save());
        Assert.Equal(j, copy.Save().ToJsonString());
    }
}
