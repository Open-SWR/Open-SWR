using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent decoys taken from the idle special-force pool.</summary>
public class OpenSWRAiDecoyTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static (GameState Game, OpenSWRController Ai, OpenSWRController.Known K) Game()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { Features = OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Decoys), PivotEvents = [] };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai, new OpenSWRController.Known(game, Owner.Empire));
    }

    static List<Agent> Forces(GameState game, int n, bool completed = true)
    {
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var type = Definitions.SpecialForces.First(f => f.Empire != 0).Common.Ref;
        return [.. Enumerable.Range(0, n).Select(_ => (Agent)Objects.Create(game, type, Owner.Empire, at, completed: completed))];
    }

    static bool Any(List<Agent> t, List<Agent> d) => true;

    /// <summary>Rules 1 and 2: a character sabotage on an enemy system with no known detector assumes one and takes 2 special-force decoys when 2 are free.</summary>
    [GameFact]
    public void CharacterSabotageTakesTwoDecoys()
    {
        var (game, ai, k) = Game();
        var target = game.Systems.First(s => Knowledge.See(game, s, Owner.Empire).Owner == Owner.Alliance);
        var known = ai.Assume(game, k, target, OpenSWRController.CovertKnown.None, ai.MedianSystem(game, k));
        output.WriteLine($"assumed detectors {known.Count}");
        Assert.True(known.Count >= OpenSWRController.AssumedDetectors);
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Existing && !c.Captured);
        var sf = Forces(game, 2);
        var team = ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [agent], [], sf, Any, out _, k, 200, null);
        Assert.NotNull(team);
        Assert.Equal(2, team!.Value.Decoys.Count);
        // none free: the mission still goes
        Assert.Empty(ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [agent], [], [], Any, out _, k, 200, null)!.Value.Decoys);
    }

    /// <summary>Rule 2: a major-character abduction takes 3 decoys; a minor-character abduction stops at character + 2.</summary>
    [GameFact]
    public void AbductionDecoysByTarget()
    {
        var (game, ai, k) = Game();
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Existing && !c.Captured);
        var sf = Forces(game, 4);
        var known = new OpenSWRController.CovertKnown([], [new OpenSWRController.Detector(10)]);
        var major = game.Characters.First(c => c.Owner == Owner.Alliance && Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter);
        var minor = game.Characters.First(c => c.Owner == Owner.Alliance && Family.Of(c.Type) is not (>= Families.MajorCharacter and < Families.MinorCharacter));
        Assert.Equal(3, ai.AgentDecoys(game, MissionKind.Abduction, major, known, [agent], [], sf, Any, out _, k, 120, null)!.Value.Decoys.Count);
        var small = ai.AgentDecoys(game, MissionKind.Abduction, minor, known, [agent], [], sf, Any, out _, k, 30, null)!.Value;
        Assert.Equal(OpenSWRController.MaxTeamSmall, small.Team.Count + small.Decoys.Count);
        Assert.Equal(2, small.Decoys.Count);
    }

    /// <summary>Rule 3: special-force espionage takes 1 decoy only while idle special forces exceed 25 % of the pool.</summary>
    [GameFact]
    public void SpecialForceEspionageDecoyFromTheIdlePool()
    {
        var (game, ai, k) = Game();
        var target = game.Systems.First(s => Knowledge.See(game, s, Owner.Empire).Owner == Owner.Alliance);
        var known = new OpenSWRController.CovertKnown([], [new OpenSWRController.Detector(10)]);
        int all = game.Created.Count(o => o.Owner == Owner.Empire && o.Existing && Family.IsSpecialForce(o.Type));
        // a pool mostly in training (not idle): idle share at most 25 %
        Forces(game, 4 * all + 8, completed: false);
        var spy = Forces(game, 1)[0];
        var decoy = Forces(game, 1);
        int Decoys() => ai.AgentDecoys(game, MissionKind.Espionage, target, known, [spy], [], decoy, Any, out _)!.Value.Decoys.Count;
        int total = game.Created.Count(o => o.Owner == Owner.Empire && o.Existing && Family.IsSpecialForce(o.Type));
        int idle = game.Created.OfType<Agent>().Count(o => o.Owner == Owner.Empire && o.Existing && Family.IsSpecialForce(o.Type) && !o.OnMission && o.Completed);
        output.WriteLine($"idle {idle} of {total}");
        Assert.True(idle <= 0.25 * total);
        Assert.Equal(0, Decoys());
        // more idle special forces than 25 %
        Forces(game, total);
        Assert.Equal(1, Decoys());
    }

    /// <summary>Rule 4: a special force reserved for a running pivot is never another mission's decoy.</summary>
    [GameFact]
    public void PivotReservationIsNoDecoy()
    {
        var (game, ai, k) = Game();
        var major = game.Characters.First(c => c.Owner == Owner.Alliance && Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter && Knowledge.Location(game, c, Owner.Empire) is StarSystem);
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Existing && !c.Captured);
        var sf = Forces(game, 4);
        ai.StartPivots(game, [new OpenSWRController.Covert(MissionKind.Assassination, major, [agent], 200, 0.5, [.. sf.Take(2)])]);
        Assert.Single(ai.Pivots);
        ai.ReserveForPivots(game, k, []);
        output.WriteLine($"reserved {ai.Reserved.Count}");
        Assert.Equal(3, ai.Reserved.Count);
        var target = game.Systems.First(s => Knowledge.See(game, s, Owner.Empire).Owner == Owner.Alliance);
        var free = game.Created.OfType<Agent>().Where(a => a.Owner == Owner.Empire && a.Existing && Family.IsSpecialForce(a.Type) && !a.OnMission).ToList();
        var known = new OpenSWRController.CovertKnown([], [new OpenSWRController.Detector(10)]);
        var team = ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [agent], [], free, Any, out _, k, 200, null)!.Value;
        output.WriteLine($"decoys {team.Decoys.Count} of {free.Count} free");
        Assert.DoesNotContain(team.Decoys, ai.Reserved.ContainsKey);
        // the pivot's own mission may use them
        var own = ai.AgentDecoys(game, MissionKind.Assassination, major, known, [agent], [], [.. ai.Reserved.Keys], Any, out _, k, 120, null)!.Value;
        Assert.NotEmpty(own.Decoys);
    }
}
