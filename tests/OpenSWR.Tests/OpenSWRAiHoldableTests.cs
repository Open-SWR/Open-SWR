using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent targets that can be held (Holdable) and diplomacy toward own low-share systems (Pacify).</summary>
public class OpenSWRAiHoldableTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static readonly ImmutableHashSet<OpenSWRController.V7> Both = OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Holdable).Add(OpenSWRController.V7.Pacify);

    static (GameState Game, OpenSWRController Ai) Game()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { Features = Both };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    /// <summary>Holdable: a target at own share 20 % is skipped by a fleet with room 2; one at 35 % is taken.</summary>
    [GameFact]
    public void TargetShare()
    {
        var (game, ai) = Game();
        var s = game.Systems.Where(x => x.Owner == Owner.Alliance).OrderBy(x => x.Key).First();
        Knowledge.Update(game, s, 10, Owner.Empire);
        var ship = game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity == 2).OrderBy(r => r.Common.Ref).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, game.Systems.First(x => x.Owner == Owner.Empire), completed: true);
        Objects.Create(game, ship, Owner.Empire, fleet, completed: true);
        game.Clock.Flush(game);
        var view = game.Views[(s, Owner.Empire)];
        var k = new OpenSWRController.Known(game, Owner.Empire);
        view.Support = 80; // the Alliance's support: the Empire's share 20 %
        Assert.False(ai.HoldableTarget(game, k, fleet, s));
        view.Support = 65; // the Empire's share 35 %
        Assert.True(ai.HoldableTarget(game, k, fleet, s));
    }

    /// <summary>Pacify: an own system at share 15 % (no uprising) is the first diplomacy target (before the neutral ones); one in uprising is not a target.</summary>
    [GameFact]
    public void PacifyFirstNotInUprising()
    {
        var (game, ai) = Game();
        var own = game.Systems.Where(x => x.Owner == Owner.Empire && x.Populated).OrderBy(x => x.Key).ToList();
        var (low, rising) = (own[0], own[1]);
        low.Support = 85; // the Empire's share 15 %
        rising.Support = 90;
        rising.Uprising = true;
        var targets = ai.PacifyTargets(game);
        output.WriteLine("pacify " + string.Join(", ", targets.Select(t => $"{Family.Of(t.Ref)}:{SdRef.IndexOf(t.Ref)} {t.SupportFor(Owner.Empire)}")));
        Assert.Same(low, targets[0]);
        Assert.DoesNotContain(rising, targets);
        Assert.All(targets, t => Assert.Equal(Owner.Empire, t.Owner)); // only own systems: the neutral ones come after
    }

    /// <summary>With both flags off, play is the default (the flags are off in the default).</summary>
    [GameFact]
    public void FlagsOffAreTheDefault()
    {
        Assert.False(OpenSWRController.DefaultFeatures.Overlaps([OpenSWRController.V7.Holdable, OpenSWRController.V7.Pacify]));
        Assert.True(Enum.GetValues<OpenSWRController.V7>().Where(f => f <= OpenSWRController.V7.Pacify).All(OpenSWRController.AllFeatures.Contains));
    }
}
