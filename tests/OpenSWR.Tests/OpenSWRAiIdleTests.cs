using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent exploration trials and espionage budget, with idle special forces.</summary>
public class OpenSWRAiIdleTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static (GameState Game, OpenSWRController Ai) Game(OpenSWRController.V7 extra, bool control)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { IssueTrace = [], Features = OpenSWRController.DefaultFeatures.Add(extra) };
        game.Controllers[Owner.Empire] = control ? ai : new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = control ? new ComputerController(Owner.Alliance) { Rand = _ => 0 } : new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    /// <summary>ExplorePrior: with no record the cap is the base estimate; after 5 failures out of 5 ended it is base x 5 / 10.</summary>
    [GameFact]
    public void PriorCapFromTheBase()
    {
        var (_, ai) = Game(OpenSWRController.V7.ExplorePrior, control: false);
        Assert.Equal(0.6, ai.PriorCap(MissionKind.Abduction, 0.6), 9);
        for (int i = 0; i < 5; i++) ai.RecordExperience(MissionKind.Abduction, false);
        Assert.Equal(0.6 * 5 / 10, ai.PriorCap(MissionKind.Abduction, 0.6), 9);
    }

    /// <summary>ExploreTrial: at most one trial per kind per side within TrialDays, each with a special-force agent.</summary>
    [GameFact]
    public void OneTrialPerKindWithASpecialForce()
    {
        var (game, ai) = Game(OpenSWRController.V7.ExploreTrial, control: true);
        var trials = new List<(MissionKind Kind, int Day, Command Command)>();
        while (game.Day < 500 && !game.GameOver)
        {
            int day = game.Day, from = ai.IssueTrace!.Count;
            var seen = new[] { MissionKind.InciteUprising, MissionKind.Rescue, MissionKind.Abduction }.ToDictionary(kind => kind, kind => ai.Experience(kind).Ended);
            // strong special forces, so the base estimate passes the minimum chance (a test setup: the natural game seldom offers one)
            foreach (var f in game.Created.OfType<Agent>().Where(a => a.Owner == Owner.Empire && a.Existing && Family.IsSpecialForce(a.Type)))
                f.Skills[SkillIndex.Combat] = f.Skills[SkillIndex.Espionage] = f.Skills[SkillIndex.Leadership] = 250;
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            foreach (var t in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.CreateMission && Family.Of(t.Command.Fields[2]) is (uint)MissionKind.InciteUprising or (uint)MissionKind.Rescue or (uint)MissionKind.Abduction))
                if (seen[(MissionKind)Family.Of(t.Command.Fields[2])] < OpenSWRController.ExperienceSeen) trials.Add(((MissionKind)Family.Of(t.Command.Fields[2]), day, t.Command));
        }
        output.WriteLine("trials: " + string.Join(", ", trials.Select(t => $"{(uint)t.Kind} day {t.Day}")));
        Assert.NotEmpty(trials);
        Assert.All(trials, t => Assert.True(t.Command.Targets.All(x => Family.IsSpecialForce(x.Target.Type)), "a character agent on a trial"));
        foreach (var g in trials.GroupBy(t => t.Kind))
            Assert.All(g.Zip(g.Skip(1)), p => Assert.True(p.Second.Day - p.First.Day >= OpenSWRController.TrialDays, $"{(uint)g.Key}: trials on days {p.First.Day} and {p.Second.Day}"));
    }

    /// <summary>PoolUseful: a type serving only kinds with no passing candidate gets no training order; a reconnaissance type none once nothing unexplored is in reach.</summary>
    [GameFact]
    public void PoolUsefulDropsUselessTypes()
    {
        var (game, ai) = Game(OpenSWRController.V7.PoolUseful, control: false);
        var types = ai.AgentTypes(game);
        var espionage = types.First(t => t.Kinds.Contains(MissionKind.Espionage));
        var recon = types.First(t => t.Kinds.Contains(MissionKind.Reconnaissance) && !t.Kinds.Contains(MissionKind.Espionage));
        var covert = types.First(t => !t.Kinds.Contains(MissionKind.Espionage) && !t.Kinds.Contains(MissionKind.Reconnaissance));
        Assert.True(ai.Useful(espionage, game.Day, unexplored: false));
        Assert.True(ai.Useful(recon, game.Day, unexplored: true));
        Assert.False(ai.Useful(recon, game.Day, unexplored: false));
        Assert.False(ai.Useful(covert, game.Day, unexplored: false)); // no passing candidate yet
        var shortfall = ai.PoolShortfall(game);
        output.WriteLine("shortfall " + string.Join(", ", shortfall.Select(x => $"{Family.Of(x.Type.Record.Common.Ref)}:{SdRef.IndexOf(x.Type.Record.Common.Ref)} {x.Missing}")));
        Assert.DoesNotContain(shortfall, x => x.Type.Record.Common.Ref == covert.Record.Common.Ref);
        Assert.Contains(shortfall, x => x.Type.Record.Common.Ref == espionage.Record.Common.Ref);
    }

    /// <summary>EspionageIdle: the espionage budget doubles its per-sector part only while idle espionage-capable special forces exceed 25 % of them.</summary>
    [GameFact]
    public void EspionageBudgetDoublesAboveTheIdleShare()
    {
        var (game, ai) = Game(OpenSWRController.V7.EspionageIdle, control: false);
        var map = ai.Sectors(game);
        var spyType = ai.AgentTypes(game).First(t => t.Kinds.Contains(MissionKind.Espionage)).Record.Common.Ref;
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var spies = game.Created.Count(o => o.Owner == Owner.Empire && o.Existing && o.Type == spyType);
        // mostly in training: idle share at most 25 %
        for (int i = 0; i < 4 * spies + 8; i++) Objects.Create(game, spyType, Owner.Empire, at, completed: false);
        Assert.False(ai.EspionageIdleOn(game));
        int low = ai.SpyBudget(game, map);
        int sectors = ai.IntelReach(game, map).Select(s => s.Sector).Distinct().Count();
        int total = game.Created.Count(o => o.Owner == Owner.Empire && o.Existing && o.Type == spyType);
        for (int i = 0; i < total; i++) Objects.Create(game, spyType, Owner.Empire, at, completed: true);
        Assert.True(ai.EspionageIdleOn(game));
        int high = ai.SpyBudget(game, map);
        output.WriteLine($"sectors {sectors}: budget {low} → {high}");
        Assert.Equal(low + sectors, high);
        // without the flag: no doubling
        ai.Features = ai.Features.Remove(OpenSWRController.V7.EspionageIdle);
        Assert.Equal(low, ai.SpyBudget(game, map));
    }
}
