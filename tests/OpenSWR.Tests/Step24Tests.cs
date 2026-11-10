using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>HuntStill (off by default): Hunt only targets that sit still, and the choice of abductor.</summary>
public class Step24Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 Hunt = OpenSWRController.V7.Hunt, Still = OpenSWRController.V7.HuntStill;

    /// <summary>
    /// Mon Mothma alone in a system without Alliance units; a weak Empire agent in that system and Vader elsewhere. Without HuntStill
    /// the abductor is Vader (highest EnhCombat); with it the near agent (0 travel days). Mothma in an Alliance fleet there: HuntStill
    /// skips her (the view's parent is the fleet, not the system).
    /// </summary>
    [GameFact]
    public void StillTargetNearAbductor()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        var mothma = game.Characters.First(c => c.Ref == 0x30000240);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        // the near agent: a free Empire character sitting in a system without Alliance units, away from Vader (moving one would make it Enroute)
        bool Empty(StarSystem s) => s.Owner != Owner.Alliance && s != Missions.Location(vader)
            && !s.Contents.Any(x => x.Owner == Owner.Alliance && (x is Fleet { Contents.Count: > 0 } || Family.Of(x.Type) is >= 0x10 and < 0x30));
        var near = game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.Enroute && !c.OnMission && !c.Captured && c.Injury == 0 && c.CommandKind == 0
            && c.Ref is not Refs.Vader and not Refs.Palpatine && c.Container is StarSystem s && Empty(s)).OrderBy(c => c.Key).First();
        var empty = (StarSystem)near.Container!;
        near.Skills[SkillIndex.Combat] = 100;
        (near.ForceRank, near.ForceBonus) = (0, 0);
        Objects.SetParent(game, mothma, empty);
        Knowledge.Update(game, empty, 10, Owner.Empire);
        game.Clock.Flush(game);
        int vaderDays = OpenSWRController.HuntDays(game, [vader], empty);
        output.WriteLine($"near {near.Ref:X8} EnhCombat {Missions.Enhanced(game, near, SkillIndex.Combat)}, Vader EnhCombat {Missions.Enhanced(game, vader, SkillIndex.Combat)}, {vaderDays} days away");
        Assert.True(vaderDays > 0);
        Assert.True(Missions.Enhanced(game, vader, SkillIndex.Combat) > Missions.Enhanced(game, near, SkillIndex.Combat));
        Assert.Same(empty, Knowledge.ViewParent(game, mothma, Owner.Empire));

        // every other Empire agent (the special forces of the pool) is marked used: the choice is between the two characters
        var forces = game.Created.OfType<Agent>().Where(o => o.Owner == Owner.Empire && o != near && o != vader).Cast<GameObject>().ToHashSet();
        var type = OpenSWRController.TypeOf(game, 0x62)!;
        output.WriteLine($"near valid: {MissionEligibility.Validate(game, 0x240, Owner.Empire, type.Common.Ref, [near], [], mothma)?.ToString() ?? "yes"}, days {OpenSWRController.HuntDays(game, [near], empty)}");
        OpenSWRController.Covert Pick(params OpenSWRController.V7[] features)
        {
            var ai = new OpenSWRController(Owner.Empire) { Features = OpenSWRController.DefaultFeatures.Union(features) };
            var abductions = ai.HuntAbductions(game, new OpenSWRController.Known(game, Owner.Empire), [near, vader], new HashSet<GameObject?>(), [.. forces]);
            output.WriteLine($"{string.Join(",", features)}: " + string.Join(", ", abductions.Select(x => $"{x.Target.Ref:X8} {x.Success:F3} team {string.Join(" ", x.Team.Select(a => a.Ref.ToString("X8")))}")));
            return Assert.Single(abductions, a => a.Target == mothma);
        }
        Assert.Contains(vader, Pick(Hunt).Team);
        var still = Pick(Hunt, Still);
        Assert.Contains(near, still.Team);
        Assert.DoesNotContain(vader, still.Team);
        Assert.True(still.Success >= OpenSWRController.MinHuntSuccess);

        // in a fleet: the view's parent is the fleet, so HuntStill skips her
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, empty, completed: true);
        Objects.SetParent(game, mothma, fleet);
        Knowledge.Update(game, empty, 10, Owner.Empire);
        game.Clock.Flush(game);
        Assert.Same(fleet, Knowledge.ViewParent(game, mothma, Owner.Empire));
        var ai2 = new OpenSWRController(Owner.Empire) { Features = OpenSWRController.DefaultFeatures.Add(Hunt).Add(Still) };
        Assert.DoesNotContain(ai2.HuntAbductions(game, new OpenSWRController.Known(game, Owner.Empire), [near, vader], new HashSet<GameObject?>(), [.. forces]), x => x.Target == mothma);
    }

    /// <summary>HuntStill is off in the default and sits above the old int range without changing All.</summary>
    [GameFact]
    public void FlagOffInTheDefault()
    {
        Assert.DoesNotContain(Still, OpenSWRController.DefaultFeatures);
        Assert.DoesNotContain(Still, OpenSWRController.AllFeatures);
        Assert.Equal(20, OpenSWRController.MaxHuntDays);
    }
}
