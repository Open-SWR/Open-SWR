using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent holding of systems a swarm has taken: how many troops stay (Hold).</summary>
public class OpenSWRAiHoldTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static readonly ImmutableHashSet<OpenSWRController.V7> Hold = OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Hold);

    static (GameState Game, OpenSWRController Ai, StarSystem Taken) Game(int share, int troops)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { IssueTrace = [], Features = Hold };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        // the system "just taken": an own populated system with the given own share and own troops there
        var taken = game.Systems.Where(s => s.Owner == Owner.Empire && s.Populated).OrderBy(s => s.Key).First();
        var troop = game.Definitions.Troops.First(t => t.Empire != 0).Common.Ref;
        foreach (var t in taken.Contents.Where(x => Family.IsTroop(x.Type)).ToList()) Objects.Destroy(game, t, DestroyedReason.Scrapped);
        for (int i = 0; i < troops; i++) Objects.Create(game, troop, Owner.Empire, taken, completed: true);
        game.Clock.Flush(game);
        taken.Support = 100 - share; // the Alliance's support; the Empire's share is the rest (set after the flush, which moves it)
        return (game, ai, taken);
    }

    /// <summary>A won assault at own share 25 % (requirement 4) leaves 4 troops: with 6 landed, 2 re-embark.</summary>
    [GameFact]
    public void ShareTwentyFiveLeavesFour()
    {
        var (game, ai, taken) = Game(25, 6);
        Assert.Equal(4, ai.HoldRequirement(game, taken));
        int left = 6 - ai.SwarmSpareForTest(game, taken).Count;
        output.WriteLine($"left {left}, garrison keep {ai.KeepOf(taken)}");
        Assert.Equal(Math.Max(4, ai.KeepOf(taken)), left); // the requirement (or the garrison rule's keep, when higher)
    }

    /// <summary>At own share 55 % (requirement 1) the floor is SwarmGarrison, 2.</summary>
    [GameFact]
    public void ShareFiftyFiveLeavesTwo()
    {
        var (game, ai, taken) = Game(55, 6);
        Assert.Equal(1, ai.HoldRequirement(game, taken));
        Assert.Equal(Math.Max(2, ai.KeepOf(taken)), 6 - ai.SwarmSpareForTest(game, taken).Count);
    }

    /// <summary>With 3 landed and requirement 4 all 3 stay and the group returns to reload (rule 4).</summary>
    [GameFact]
    public void FewerThanTheRequirementStayAndTheGroupReturns()
    {
        var (game, ai, taken) = Game(25, 3);
        Assert.Empty(ai.SwarmSpareForTest(game, taken));
        var ship = game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity >= 2).OrderBy(r => r.MaintenanceCost).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, taken, completed: true);
        Objects.Create(game, ship, Owner.Empire, fleet, completed: true);
        game.Clock.Flush(game);
        ai.AddSwarmGroup(taken, taken, fleet); // its target was taken (now own)
        ai.RefreshValues(game);
        ai.DayDue();
        ai.Attack(game, new OpenSWRController.Known(game, Owner.Empire), [], [], ai.Sectors(game));
        var g = Assert.Single(ai.Groups, x => x.Fleet == fleet.Ref);
        output.WriteLine($"base {g.Staging?.Ref} (taken {Family.Of(taken.Ref)}:{SdRef.IndexOf(taken.Ref)})");
        Assert.NotSame(taken, g.Staging);
        Assert.Equal(3, taken.Contents.Count(x => Family.IsTroop(x.Type) && x.Owner == Owner.Empire));
    }

    /// <summary>With the flag off, the floor is SwarmGarrison and play is the default (the flag is off by default).</summary>
    [GameFact]
    public void FlagOffIsTheDefault()
    {
        Assert.DoesNotContain(OpenSWRController.V7.Hold, OpenSWRController.DefaultFeatures);
        Assert.True(Enum.GetValues<OpenSWRController.V7>().Where(f => f <= OpenSWRController.V7.Hold).All(OpenSWRController.AllFeatures.Contains));
        var (game, ai, taken) = Game(55, 6);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Hold);
        Assert.Equal(Math.Max(2, ai.KeepOf(taken)), 6 - ai.SwarmSpareForTest(game, taken).Count); // SwarmGarrison or the keep
    }
}
