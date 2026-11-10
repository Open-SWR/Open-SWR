using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Measurement of enemy troop fleets heading for own systems. The Respond rules were reverted.</summary>
public class Step12Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static int Capacity(GameState game, Fleet f) =>
        f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).Sum(x => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type)?.TroopCapacity ?? 0));

    /// <summary>An Enroute enemy fleet with troop room, shown at an own system, makes a threat to that system (the measurement's sighting).</summary>
    [GameFact]
    public void EnrouteFleetWithTroopsIsAThreat()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire);
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        var target = game.Systems.Where(s => s.Owner == Owner.Empire && s.Populated).OrderBy(s => s.Key).First();
        var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Alliance && f.Usable && !f.Enroute
            && Capacity(game, f) > 0 && Orders.Speed(game, f) > 0)
            .OrderByDescending(f => Orders.TravelDays(game, (StarSystem)f.Container!, target, Orders.Speed(game, f))).First();
        Fleets.Move(game, fleet, target);
        game.Clock.Step(game);
        Assert.True(fleet.Enroute);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.DoesNotContain(ai.Threats(game, k), t => t.System == target); // not seen yet
        Knowledge.Update(game, target, 10, Owner.Empire); // the watch's update (WatchEspionageShowsIncomingFleets runs the real mission)
        k = new OpenSWRController.Known(game, Owner.Empire);
        var threat = Assert.Single(ai.Threats(game, k), t => t.System == target);
        output.WriteLine($"threat: strength {threat.Strength}, troops {threat.Troops}, landing in {threat.LandingDays} days");
        Assert.Contains(fleet, threat.Fleets);
        Assert.True(threat.Troops > 0 && threat.LandingDays >= 1);
    }

    /// <summary>The measurement changes nothing: the same orders for 120 days with it on and off.</summary>
    [GameFact]
    public void MeasurementChangesNothing()
    {
        List<string> Orders(bool measure)
        {
            var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
            var ai = new OpenSWRController(Owner.Empire) { IssueTrace = [], AssaultLossesSeen = measure ? [] : null };
            game.Controllers[Owner.Empire] = ai;
            game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
            foreach (var s in game.Sides.Values) s.Speed = 12;
            game.BattleResults = _ => null;
            while (game.Day < 120 && !game.GameOver) game.Clock.Step(game);
            return [.. ai.IssueTrace!.Select(t => $"{t.Command.Type:X} {string.Join(",", t.Command.Targets.Select(x => x.Target.Ref))} {t.Command.ArgRef?.Ref} {string.Join(",", t.Command.Fields)}")];
        }
        Assert.Equal(Orders(false), Orders(true));
    }
}
