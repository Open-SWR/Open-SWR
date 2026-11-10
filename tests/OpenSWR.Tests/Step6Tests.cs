using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent teams: the join rule, View.Updated, and intel before acting.</summary>
public class Step6Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    /// <summary>The default without Agents and Decoys: these tests cover the team paths only.</summary>
    static readonly ImmutableHashSet<OpenSWRController.V7> Base = OpenSWRController.DefaultFeatures.Except([OpenSWRController.V7.Agents, OpenSWRController.V7.Decoys, OpenSWRController.V7.Swarm]);

    static (GameState Game, OpenSWRController Ai) Game(Owner side, ImmutableHashSet<OpenSWRController.V7> features)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { IssueTrace = [], Features = features, IntelAges = [] };
        var other = side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        game.Controllers[side] = ai;
        game.Controllers[other] = new HumanController(other);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        return (game, ai);
    }

    static uint Kind(Command c) => Family.Of(c.Fields[2]);

    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    static GameState Load(byte[] save)
    {
        using var stream = new MemoryStream(save);
        return SaveGame.Load(stream, Definitions, _ => _ => 0);
    }

    static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");

    /// <summary>A team with a known detector takes a special-force decoy when the decoy's worth gain is at least MinJoinGain of the target's value.</summary>
    [GameFact]
    public void DecoyJoinsAtMinJoinGain()
    {
        var (game, ai) = Game(Owner.Empire, Base.Add(OpenSWRController.V7.Teams));
        game.Clock.Step(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var sf = (Agent)Objects.Create(game, Definitions.SpecialForces.Where(f => f.Empire != 0).OrderByDescending(f => f.Skills.Espionage.Base).First().Common.Ref,
            Owner.Empire, at, completed: true);
        var agent = game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.Captured).OrderBy(c => c.Key).First();
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        var known = new OpenSWRController.CovertKnown([new OpenSWRController.Detector(30, 50)], []);
        foreach (int value in new[] { 50, 200, 1000, 5000 })
        {
            int attempt = ai.Attempt(game, k, 0x69, agent, target);
            double alone = OpenSWRController.TeamWorth(game, [agent], [attempt], value, known, []);
            double decoyed = OpenSWRController.TeamWorth(game, [agent], [attempt], value, known, [sf]);
            var (team, decoys, worth) = ai.GrowTeam(game, k, 0x69, target, value, known, null, agent, [sf], (_, _) => true);
            output.WriteLine($"value {value}: worth {alone:F2} → with decoy {decoyed:F2} (gain {decoyed - alone:F2}, min {OpenSWRController.MinJoinGain * value:F2}); decoys {decoys.Count}");
            bool joins = decoyed > alone && decoyed - alone >= OpenSWRController.MinJoinGain * value;
            Assert.Equal(joins, decoys.Contains(sf));
            Assert.Equal([agent], team);
            if (value == 5000) Assert.True(joins, "the decoy does not join a high-value target");
        }
        // no detector known: no decoy
        Assert.Empty(ai.GrowTeam(game, k, 0x69, target, 5000, OpenSWRController.CovertKnown.None, null, agent, [sf], (_, _) => true).Decoys);
    }

    /// <summary>An espionage team stays within MaxTeamSmall (3): the cap, and every espionage the AI orders with Teams and Intel over 200 days.</summary>
    [GameFact]
    public void EspionageTeamWithinThree()
    {
        Assert.Equal(3, OpenSWRController.MaxTeamSmall);
        var (game, ai) = Game(Owner.Empire, Base.Add(OpenSWRController.V7.Teams).Add(OpenSWRController.V7.Intel));
        Assert.Equal(3, OpenSWRController.TeamCap(0x52, game.Systems[0]));
        game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 200 && !game.GameOver) game.Clock.Step(game);
        var spying = ai.IssueTrace!.Where(t => t.Result.Success && t.Command.Type == 0x240 && Kind(t.Command) == 0x52).Select(t => t.Command).ToList();
        output.WriteLine($"espionage orders {spying.Count}");
        Assert.NotEmpty(spying);
        Assert.All(spying, c => Assert.InRange(c.Targets.Count + c.Decoys.Count, 1, OpenSWRController.MaxTeamSmall));
    }

    /// <summary>View.Updated: a frozen-view write sets it to the day; a live view counts as today; it survives save -> load -> save byte-equal.</summary>
    [GameFact]
    public void ViewUpdatedSetAndSaved()
    {
        var (game, _) = Game(Owner.Alliance, Base);
        while (game.Day < 5) game.Clock.Step(game);
        var s = game.Systems.First(x => x.Owner == Owner.Empire && !Knowledge.IsLive(x, Owner.Alliance));
        int before = Knowledge.UpdatedDay(game, s, Owner.Alliance);
        Assert.True(before < game.Day);
        Knowledge.Update(game, s, 10, Owner.Alliance); // a full knowledge update (espionage's mode 10) writes the frozen view
        Assert.Equal(game.Day, game.Views[(s, Owner.Alliance)].Updated);
        Assert.Equal(game.Day, Knowledge.UpdatedDay(game, s, Owner.Alliance));
        var own = game.Systems.First(x => x.Owner == Owner.Alliance);
        Assert.True(Knowledge.IsLive(own, Owner.Alliance));
        Assert.Equal(game.Day, Knowledge.UpdatedDay(game, own, Owner.Alliance));
        int day = game.Day;
        var save = Save(game);
        Assert.Contains("\"updated\":", System.Text.Encoding.UTF8.GetString(save));
        var loaded = Load(save);
        Assert.Equal(day, loaded.Views[(loaded.Systems.First(x => x.Ref == s.Ref), Owner.Alliance)].Updated);
        Assert.Equal(Text(save), Text(Save(loaded)));
    }

    /// <summary>
    /// Intel, look before acting: a covert target at a system without a fresh view waits, its system leads the Intel targets, and
    /// an idle espionage special force goes there first; no covert mission goes to a system whose view is not fresh.
    /// </summary>
    [GameFact]
    public void StaleCovertTargetGetsEspionageFirst()
    {
        var (game, ai) = Game(Owner.Empire, Base);
        game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
        StarSystem? At(GameObject t) => t as StarSystem ?? Knowledge.Location(game, t, Owner.Empire) as StarSystem;
        OpenSWRController.Known k;
        OpenSWRController.SectorMap map;
        List<Character> free;
        List<StarSystem> stale;
        // the first decision day with a covert candidate at a system whose view is not fresh
        do
        {
            for (int day = game.Day; game.Day == day;) game.Clock.Step(game);
            k = new OpenSWRController.Known(game, Owner.Empire);
            map = ai.Sectors(game);
            free = [.. game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.Captured && !c.OnMission && c.CommandKind == 0).OrderBy(c => c.Key)];
            var plain = ai.CovertCandidates(game, k, map, free, new HashSet<GameObject?>(), rescueOnly: false);
            stale = [.. plain.Select(x => At(x.Target)).OfType<StarSystem>().Where(s => !ai.Fresh(game, s)).Distinct()];
        }
        while (stale.Count == 0 && game.Day < 400 && !game.GameOver);
        output.WriteLine($"day {game.Day}: stale candidate systems {stale.Count}");
        Assert.NotEmpty(stale);

        ai.Features = ai.Features.Add(OpenSWRController.V7.Intel);
        var gated = ai.CovertCandidates(game, k, map, free, new HashSet<GameObject?>(), rescueOnly: false);
        Assert.All(gated, x => Assert.True(ai.Fresh(game, At(x.Target)!)));
        var targets = ai.IntelTargets(game, map);
        Assert.Equal(stale.OrderBy(s => s.Key), targets.Take(stale.Count).OrderBy(s => s.Key));

        // an idle espionage special force at an own system: the next decision sends it to a look-before-acting system
        var espionage = OpenSWRController.TypeOf(game, 0x52)!;
        var type = Definitions.SpecialForces.First(f => f.Empire != 0 && f.MissionMask != 0 && espionage.AllowsSpecialForce(f.MissionMask)).Common.Ref;
        Objects.Create(game, type, Owner.Empire, game.Systems.First(s => s.Owner == Owner.Empire), completed: true);
        int from = ai.IssueTrace!.Count;
        int ages = ai.IntelAges!.Count, today = game.Day;
        while (game.Day == today) game.Clock.Step(game);
        var orders = ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240).Select(t => t.Command).ToList();
        foreach (var c in orders) output.WriteLine($"{Kind(c):X2} → {c.ArgRef?.Ref:X8} by {c.Targets[0].Target.Ref:X8}");
        Assert.Contains(orders, c => Kind(c) is 0x52 or 0x54 && Family.IsSpecialForce(c.Targets[0].Target.Type) && stale.Contains(c.ArgRef as StarSystem));
        Assert.All(ai.IntelAges.Skip(ages).Where(a => a.Decision is "covert" or "attack"), a => Assert.InRange(a.Age, 0, OpenSWRController.FreshDays));
    }
    /// <summary>
    /// Intel, coverage: the systems in reach older than CoverDays come stalest first, and the next decision's idle espionage
    /// special force goes to the first Intel target it may spy on.
    /// </summary>
    [GameFact]
    public void CoverageGoesToTheStalest()
    {
        var (game, ai) = Game(Owner.Empire, Base.Add(OpenSWRController.V7.Intel));
        game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 150) game.Clock.Step(game);
        var map = ai.Sectors(game);
        var frozen = ai.IntelReach(game, map).Where(s => !Knowledge.IsLive(s, Owner.Empire) && game.Views.ContainsKey((s, Owner.Empire))).ToList();
        output.WriteLine($"day {game.Day}: in reach {ai.IntelReach(game, map).Count}, frozen with a view {frozen.Count}");
        Assert.True(frozen.Count >= 2);
        // no game effect: Updated is read only by the openswr controller
        var (older, newer) = (frozen[^1], frozen[0]);
        game.Views[(older, Owner.Empire)].Updated = game.Day - 140;
        game.Views[(newer, Owner.Empire)].Updated = game.Day - 100;
        var targets = ai.IntelTargets(game, map);
        Assert.True(targets.IndexOf(older) >= 0 && targets.IndexOf(older) < targets.IndexOf(newer));
        var cover = targets.SkipWhile(s => ai.ViewAge(game, s) <= OpenSWRController.CoverDays).Select(s => ai.ViewAge(game, s)).ToList();
        Assert.Equal(cover.OrderDescending(), cover); // the coverage part: stalest first
        output.WriteLine("targets " + string.Join(", ", targets.Select(s => $"{s.Ref:X8} age {ai.ViewAge(game, s)}")));

        var espionage = OpenSWRController.TypeOf(game, 0x52)!;
        var type = Definitions.SpecialForces.First(f => f.Empire != 0 && f.MissionMask != 0 && espionage.AllowsSpecialForce(f.MissionMask)).Common.Ref;
        var spy = (Agent)Objects.Create(game, type, Owner.Empire, game.Systems.First(s => s.Owner == Owner.Empire), completed: true);
        bool Spyable(StarSystem s) => new uint[] { 0x52, 0x54 }.Any(kind => OpenSWRController.TypeOf(game, kind) is { } t
            && MissionEligibility.Validate(game, 0x240, Owner.Empire, t.Common.Ref, [spy], [], s) is null);
        var spyable = game.Systems.Where(Spyable).ToHashSet();
        int from = ai.IssueTrace!.Count, today = game.Day;
        while (game.Day == today) game.Clock.Step(game);
        var sent = ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240 && t.Command.Targets.Any(x => x.Target == spy)).Select(t => t.Command).ToList();
        Assert.Single(sent);
        // the decision's own Intel list: its look-before-acting systems (rebuilt in the decision) come before the coverage part
        // (since the issue-1 fix this run has look-first systems at day 150, so the expected target is read after the decision)
        var after = ai.IntelTargets(game, map);
        output.WriteLine("after " + string.Join(", ", after.Select(s => $"{s.Ref:X8} age {ai.ViewAge(game, s)}")));
        var first = after.First(spyable.Contains);
        output.WriteLine($"spy → {sent[0].ArgRef?.Ref:X8}, first spyable target {first.Ref:X8}");
        Assert.Same(first, sent[0].ArgRef);
    }

    /// <summary>GitHub issue 1: on day 1 an enemy system the side never looked at (setup views included) is not fresh; a live one is.</summary>
    [GameFact]
    public void UnscoutedEnemySystemIsNotFreshOnDayOne()
    {
        var (game, ai) = Game(Owner.Empire, Base.Add(OpenSWRController.V7.Intel));
        Assert.Equal(1, game.Day);
        var enemy = game.Systems.Where(s => s.Owner == Owner.Alliance && !Knowledge.IsLive(s, Owner.Empire)).ToList();
        Assert.NotEmpty(enemy);
        Assert.All(enemy, s => Assert.True(ai.Unscouted(game, s)));
        Assert.All(enemy, s => Assert.False(ai.Fresh(game, s)));
        var own = game.Systems.First(s => s.Owner == Owner.Empire && Knowledge.IsLive(s, Owner.Empire));
        Assert.True(ai.Fresh(game, own));
    }
}
