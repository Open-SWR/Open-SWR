using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent troop fit to targets (TroopFit) and the landing troop gate (LandGate).</summary>
public class Step18Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static readonly ImmutableHashSet<OpenSWRController.V7> Both = OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.TroopFit).Add(OpenSWRController.V7.LandGate);

    static (GameState Game, OpenSWRController Ai) Game()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { IssueTrace = [], Features = Both };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    static uint Troop(GameState game, Owner side) => game.Definitions.Troops.Where(t => (side == Owner.Alliance ? t.Alliance : t.Empire) != 0).OrderBy(t => t.MaintenanceCost).First().Common.Ref;

    /// <summary>The known enemy troops at an Alliance system set to <paramref name="n"/> (real troops, view refreshed).</summary>
    static void EnemyTroops(GameState game, StarSystem s, int n)
    {
        foreach (var t in s.Contents.Where(x => Family.IsTroop(x.Type)).ToList()) Objects.Destroy(game, t, 0x14);
        for (int i = 0; i < n; i++) Objects.Create(game, Troop(game, Owner.Alliance), Owner.Alliance, s, completed: true);
        game.Clock.Flush(game);
        Knowledge.Update(game, s, 10, Owner.Empire);
    }

    static Fleet FleetWithRoom(GameState game, StarSystem at, int room, int aboard = 0)
    {
        var ship = game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity == room).OrderBy(r => r.Common.Ref).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, at, completed: true);
        var x = Objects.Create(game, ship, Owner.Empire, fleet, completed: true);
        for (int i = 0; i < aboard; i++) Objects.Create(game, Troop(game, Owner.Empire), Owner.Empire, x, completed: true);
        game.Clock.Flush(game);
        return fleet;
    }

    /// <summary>TroopFit rule 1: a fleet with room 2 skips a target needing 6 and takes one needing 2.</summary>
    [GameFact]
    public void RoomFitsTarget()
    {
        var (game, ai) = Game();
        var targets = game.Systems.Where(s => s.Owner == Owner.Alliance).OrderBy(s => s.Key).Take(2).ToList();
        EnemyTroops(game, targets[0], 3); // 1.5 x 3 -> 5, + 1 = 6
        EnemyTroops(game, targets[1], 0); // at least 2
        var fleet = FleetWithRoom(game, game.Systems.First(s => s.Owner == Owner.Empire), 2);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Equal(6, ai.AttackTroopsFor(k, targets[0]));
        Assert.Equal(2, ai.AttackTroopsFor(k, targets[1]));
        Assert.False(ai.FitsRoom(game, k, fleet, targets[0]));
        Assert.True(ai.FitsRoom(game, k, fleet, targets[1]));
    }

    /// <summary>TroopFit rule 2: a short group at a base without spare troops moves its base to a system with spare troops.</summary>
    [GameFact]
    public void ShortGroupReloadsWhereTroopsAre()
    {
        var (game, ai) = Game();
        var own = game.Systems.Where(s => s.Owner == Owner.Empire).OrderBy(s => s.Key).ToList();
        var (empty, full) = (own[0], own.First(s => s != own[0] && s.Populated));
        foreach (var t in empty.Contents.Where(x => Family.IsTroop(x.Type)).ToList()) Objects.Destroy(game, t, 0x14);
        for (int i = 0; i < 8; i++) Objects.Create(game, Troop(game, Owner.Empire), Owner.Empire, full, completed: true);
        var fleet = FleetWithRoom(game, empty, game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity >= 4).Min(r => (int)r.TroopCapacity));
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        ai.AddSwarmGroup(target, empty, fleet);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var reload = ai.ReloadBase(game, k, ai.Groups[^1], fleet, []);
        output.WriteLine($"base {empty.Ref:X8} → {reload?.Ref:X8}");
        Assert.NotNull(reload);
        Assert.True(ai.SwarmSpareForTest(game, reload!).Count >= 1);
    }

    /// <summary>LandGate rule 3: a landing with 2 troops against a need of 3 is held, one with 3 goes.</summary>
    [GameFact]
    public void LandingNeedsTheTroops()
    {
        var (game, ai) = Game();
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        EnemyTroops(game, target, 1); // 1.5 -> 2, + 1 = 3
        var k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Equal(3, ai.AttackTroopsFor(k, target));
        int room = game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity >= 3).Min(r => (int)r.TroopCapacity);
        var two = FleetWithRoom(game, target, room, aboard: 2);
        var three = FleetWithRoom(game, target, room, aboard: 3);
        Assert.False(ai.LandAllowed(k, [two], target));
        Assert.True(ai.LandAllowed(k, [three], target));
        ai.Features = ai.Features.Remove(OpenSWRController.V7.LandGate);
        Assert.True(ai.LandAllowed(k, [two], target));
    }

    /// <summary>With both flags off, play is the default (the flags are off in the default).</summary>
    [GameFact]
    public void FlagsOffAreTheDefault()
    {
        Assert.False(OpenSWRController.DefaultFeatures.Overlaps([OpenSWRController.V7.TroopFit, OpenSWRController.V7.LandGate]));
        Assert.True(Enum.GetValues<OpenSWRController.V7>().Where(f => f <= OpenSWRController.V7.LandGate).All(OpenSWRController.AllFeatures.Contains));
    }
}
