using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Step 37 (`RimFirst`): expansion into rim systems out of the enemy's reach.</summary>
public class Step37Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 RimFirst = OpenSWRController.V7.RimFirst;

    static (GameState Game, OpenSWRController Ai) AllianceAiGame(ImmutableHashSet<OpenSWRController.V7> features)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Alliance) { Features = features };
        game.Controllers[Owner.Alliance] = ai;
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    /// <summary>
    /// An Alliance fleet with a troop aboard in orbit of a neutral unpopulated rim system with energy, far from every Empire system
    /// and fleet: the system is a drop target, the drop lands one troop by a plain move, the system becomes the Alliance's and keeps
    /// the troop through the following decisions (garrison 1), and it then wants a shield and a gun.
    /// </summary>
    [GameFact]
    public void DropTakesAndKeepsASafeRimSystem()
    {
        var (game, ai) = AllianceAiGame(OpenSWRController.DefaultFeatures.Add(RimFirst));
        var k0 = new OpenSWRController.Known(game, Owner.Alliance);
        var known = game.Systems.Where(x => Knowledge.See(game, x, Owner.Alliance).Owner == Owner.Empire || k0.Fleets(x, Owner.Empire).Count > 0).ToList();
        var rim = game.Systems.Where(x => !x.Sector.IsCore && !x.Populated && x.Owner == Owner.Neutral && x.Energy > 0).ToList();
        output.WriteLine($"known enemy places {known.Count}, rim unpopulated neutral with energy {rim.Count}");
        var s = rim.Where(x => known.All(e => Orders.TravelDays(game, e, x, 100) > OpenSWRController.ReachDays)).OrderBy(x => x.Key).First();
        var shipType = game.Definitions.CapitalShips.Where(r => r.Alliance != 0 && r.TroopCapacity >= 1).OrderBy(r => r.MaintenanceCost).ThenBy(r => r.Common.Ref).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, s, completed: true);
        var ship = Objects.Create(game, shipType, Owner.Alliance, fleet, completed: true);
        var troopType = game.Created.First(o => o.Owner == Owner.Alliance && Family.IsTroop(o.Type)).Type;
        var troop = Objects.Create(game, troopType, Owner.Alliance, ship, completed: true);
        game.Clock.Flush(game);
        s.Explored = true;
        Knowledge.RecomputeSystem(game, s); // the own fleet makes the system's view live
        var k = new OpenSWRController.Known(game, Owner.Alliance);
        Assert.Contains(s, ai.RimDropTargets(game, k));

        ai.DayDue(); // a fresh day's budget
        ai.RimDrops(game, k, []);
        var drop = Assert.Single(ai.Outbox, c => c.Type == 0x201 && c.Targets.Any(t => t.Target == troop));
        Assert.Equal(s, drop.ArgRef);
        Assert.True(Orders.Validate(game, drop).Success);
        Assert.Equal([s.Ref], ai.RimDropped);

        for (int i = 0; i < 400 && s.Owner != Owner.Alliance; i++) game.Clock.Step(game);
        Assert.Equal(Owner.Alliance, s.Owner);
        int day = game.Day;
        while (game.Day < day + 30) game.Clock.Step(game);
        output.WriteLine($"day {game.Day}: contents {string.Join(",", s.Contents.Select(y => Family.Of(y.Type).ToString("X")))}");
        Assert.Equal(Owner.Alliance, s.Owner); // the troop stays (garrison 1), so Swarm does not take it away
        Assert.Contains(troop, s.Contents);
        // the rim defence sites: a shield and a gun ordered there (energy room permitting), so none is wanted any more
        foreach (uint family in new uint[] { 0x24, 0x23 }) Assert.Contains(s.Contents, y => y.Owner == Owner.Alliance && y.Existing && Family.Of(y.Type) == family);
        Assert.DoesNotContain(ai.RimDefenceSites(game), x => x.Site == s);
    }

    /// <summary>RimFirst is off in the default.</summary>
    [Fact]
    public void FlagOffInTheDefault()
    {
        Assert.True(OpenSWRController.ParseFeatures("RimFirst") is { } parsed && parsed.SetEquals([RimFirst])); // aimatch --features
        Assert.DoesNotContain(RimFirst, OpenSWRController.DefaultFeatures);
    }
}
