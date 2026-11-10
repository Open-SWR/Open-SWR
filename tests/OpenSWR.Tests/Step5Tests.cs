using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent character rules: recruitment, covert experience counts, and teams.</summary>
public class Step5Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static (GameState Game, OpenSWRController Ai) Game(Owner side, ImmutableHashSet<OpenSWRController.V7> features)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { IssueTrace = [], Features = features };
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

    /// <summary>Recruit: an idle character with a recruitable character of the side available gets a recruitment mission before any other mission of the decision.</summary>
    [GameFact]
    public void RecruitmentFirst()
    {
        var (game, ai) = Game(Owner.Alliance, OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Recruit));
        Assert.Contains(game.Characters, c => Finders.DefinitionSide(c) == Owner.Alliance && !c.Existing && !c.Destroyed);
        game.Clock.Step(game);
        var missions = ai.IssueTrace!.Where(t => t.Result.Success && t.Command.Type == 0x240).Select(t => t.Command).ToList();
        foreach (var c in missions) output.WriteLine($"{Kind(c):X2} by {c.Targets[0].Target.Ref:X8}");
        int recruit = missions.FindIndex(c => Kind(c) == 0x55);
        Assert.True(recruit >= 0, "no recruitment");
        Assert.All(missions.Take(recruit), c => Assert.Equal(0x57u, Kind(c))); // only "hold home" (subdue) comes before
    }

    /// <summary>
    /// Recruit count: counted once at the first decision; a change of the live character list afterwards does not change it; the
    /// running recruitment missions never exceed what is left; after RecruitTotal successes no recruitment mission is ordered; the
    /// counts survive save -> load -> save byte-equal.
    /// </summary>
    [GameFact]
    public void RecruitCount()
    {
        var (game, ai) = Game(Owner.Alliance, OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Recruit));
        game.Clock.Step(game);
        int total = ai.RecruitTotal!.Value;
        Assert.True(total > 0);
        // the live list changes: one character not yet in play is gone (destroyed); the count stays
        var gone = game.Characters.First(c => Finders.DefinitionSide(c) == Owner.Alliance && !c.Existing && !c.Destroyed);
        gone.Destroyed = true;
        game.Controllers[Owner.Empire] = new ComputerController(Owner.Empire) { Rand = _ => 0 };
        while (game.Day < 300 && !game.GameOver)
        {
            game.Clock.Step(game);
            int running = game.Missions.Count(m => m.Owner == Owner.Alliance && !m.Deleted && m.CompletionStatus == 0 && m.Kind == 0x55);
            Assert.True(running <= Math.Max(0, total - ai.Recruited) + 0, $"day {game.Day}: {running} running, {total - ai.Recruited} left");
        }
        Assert.Equal(total, ai.RecruitTotal);
        output.WriteLine($"total {total}, recruited {ai.Recruited}");
        // the counts in the save
        var save = Save(game);
        Assert.Contains("\"recruitTotal\":", System.Text.Encoding.UTF8.GetString(save));
        var loaded = (OpenSWRController)Load(save).Controllers[Owner.Alliance];
        Assert.Equal((ai.RecruitTotal, ai.Recruited), (loaded.RecruitTotal, loaded.Recruited));
        static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");
        Assert.Equal(Text(save), Text(Save(Load(save))));

        // after RecruitTotal successes: no recruitment mission
        var (fresh, ai2) = Game(Owner.Alliance, OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Recruit));
        fresh.Clock.Step(fresh);
        ai2.SetRecruitCounts(5, 5);
        int from = ai2.IssueTrace!.Count;
        while (fresh.Day < 100) fresh.Clock.Step(fresh);
        Assert.DoesNotContain(ai2.IssueTrace.Skip(from), t => t.Result.Success && t.Command.Type == 0x240 && Kind(t.Command) == 0x55);
    }

    /// <summary>CovertExperience: after 5 failed abductions (rate 0, below the minimum chance) no abduction is ordered; the counts survive save -> load -> save byte-equal.</summary>
    [GameFact]
    public void FailedAbductionsStopAbductions()
    {
        var (game, ai) = Game(Owner.Empire, OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.CovertExperience));
        for (int i = 0; i < OpenSWRController.ExperienceSeen; i++) ai.RecordExperience(0x62, false);
        Assert.Equal(0, ai.ObservedRate(0x62));
        while (game.Day < 200 && !game.GameOver) game.Clock.Step(game);
        Assert.DoesNotContain(ai.IssueTrace!, t => t.Result.Success && t.Command.Type == 0x240 && Kind(t.Command) == 0x62);
        // the counts in the save
        var save = Save(game);
        Assert.Contains("\"covertSeen\":", System.Text.Encoding.UTF8.GetString(save));
        var loaded = Load(save);
        static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");
        Assert.Equal(Text(save), Text(Save(loaded)));
        Assert.Equal((0, OpenSWRController.ExperienceSeen), ((OpenSWRController)loaded.Controllers[Owner.Empire]).Experience(0x62));
    }

    /// <summary>The decoy estimate is the table: T(TDECOYTB, or FDECOYTB through a fleet, EnhancedEspionage - the unit's value - pct(the defender's espionage, G 0xE04)).</summary>
    [GameFact]
    public void DecoyEstimateIsTheTable()
    {
        var (game, _) = Game(Owner.Alliance, OpenSWRController.DefaultFeatures);
        var at = game.Systems.First(s => s.Owner == Owner.Alliance);
        var force = (Agent)Objects.Create(game, Definitions.SpecialForces.First(f => f.Alliance != 0).Common.Ref, Owner.Alliance, at, completed: true);
        int esp = Missions.Enhanced(game, force, SkillIndex.Espionage);
        foreach (bool fleet in new[] { false, true })
        {
            var d = new OpenSWRController.Detector(10, DefenderEspionage: 40, Fleet: fleet);
            int key = esp - 10 - 40 * game.Params.General(0xE04) / 100;
            double expected = Math.Clamp(Missions.Table(game, fleet ? "FDECOYTB" : "TDECOYTB", key) ?? 0, 0, 100) / 100.0;
            output.WriteLine($"fleet {fleet}: key {key}, p {expected}");
            Assert.Equal(expected, OpenSWRController.DecoyChance(game, [force], d), 6);
        }
    }

    /// <summary>
    /// Teams (greedy, mixed, cost per member, caps): with one detecting unit and two eligible special forces the team grows only
    /// while worth rises (each step by at least MinJoinGain of the target's value); a character with two special forces against two detecting units is the sole agent, every
    /// special force a decoy; no decoy without a known detector; a sabotage team never exceeds MaxTeamSmall; a major character's
    /// abduction may take more when worth rises.
    /// </summary>
    [GameFact]
    public void GreedyTeams()
    {
        var (game, ai) = Game(Owner.Empire, OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Teams));
        game.Clock.Step(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var forces = Definitions.SpecialForces.Where(f => f.Empire != 0).OrderByDescending(f => f.Skills.Espionage.Base).First().Common.Ref;
        var sf = Enumerable.Range(0, 2).Select(_ => (Agent)Objects.Create(game, forces, Owner.Empire, at, completed: true)).ToList();
        var chars = game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.Captured).OrderBy(c => c.Key).ToList();
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        var detector = new OpenSWRController.CovertKnown([new OpenSWRController.Detector(30, 50)], []);
        bool Any(List<Agent> t, List<Agent> d) => true;

        // a special-force agent and the other special force: grows only while worth rises
        var (team, decoys, worth) = ai.GrowTeam(game, k, 0x69, target, 400, detector, null, sf[0], [sf[1]], Any);
        double single = OpenSWRController.TeamWorth(game, [sf[0]], [ai.Attempt(game, k, 0x69, sf[0], target)], 400, detector, []);
        output.WriteLine($"special forces: team {team.Count}, decoys {decoys.Count}, worth {single:F1} → {worth:F1}");
        Assert.True(worth >= single);
        double minGain = OpenSWRController.MinJoinGain * 400; // the join rule
        if (team.Count + decoys.Count > 1) Assert.True(worth - single >= minGain);
        else Assert.True(OpenSWRController.TeamWorth(game, [sf[0]], [ai.Attempt(game, k, 0x69, sf[0], target)], 400, detector, [sf[1]]) - single < minGain
            && OpenSWRController.TeamWorth(game, sf, [.. sf.Select(a => ai.Attempt(game, k, 0x69, a, target))], 400, detector, []) - single < minGain);

        // a character with two special forces: the character is the only agent, the special forces decoys
        var two = new OpenSWRController.CovertKnown([new OpenSWRController.Detector(30, 50), new OpenSWRController.Detector(30, 50)], []); // a decoy for each
        (team, decoys, worth) = ai.GrowTeam(game, k, 0x69, target, 5000, two, null, chars[0], sf, Any);
        output.WriteLine($"mixed: agents {string.Join(",", team.Select(a => a.Ref.ToString("X8")))}, decoys {decoys.Count}");
        Assert.All(team, a => Assert.IsAssignableFrom<Character>(a));
        Assert.Equal(2, decoys.Count);
        Assert.All(decoys, a => Assert.True(Family.IsSpecialForce(a.Type)));

        // no decoy without a known detector
        (_, decoys, _) = ai.GrowTeam(game, k, 0x69, target, 5000, OpenSWRController.CovertKnown.None, null, chars[0], sf, Any);
        Assert.Empty(decoys);

        // the small cap: espionage and sabotage teams never exceed MaxTeamSmall
        Assert.Equal(OpenSWRController.MaxTeamSmall, OpenSWRController.TeamCap(0x52, target));
        (team, decoys, _) = ai.GrowTeam(game, k, 0x69, target, 100000, detector, null, chars[0], [.. chars.Skip(1), .. sf], Any);
        Assert.InRange(team.Count + decoys.Count, 1, OpenSWRController.MaxTeamSmall);

        // a major character's abduction: no cap beyond worth
        var major = game.Characters.First(c => c.Owner == Owner.Alliance && Family.Of(c.Type) is >= 0x30 and < 0x38);
        Assert.Equal(int.MaxValue, OpenSWRController.TeamCap(0x62, major));
        var more = Enumerable.Range(0, 3).Select(_ => (Agent)Objects.Create(game, forces, Owner.Empire, at, completed: true)).ToList();
        // (a member joins at a gain of MinJoinGain of the value; some value and detector count takes more than the small cap)
        int largest = 0;
        foreach (int n in new[] { 3, 5, 8 })
            foreach (int value in new[] { 300, 1000, 3000, 10000 })
            {
                var known = new OpenSWRController.CovertKnown([.. Enumerable.Repeat(new OpenSWRController.Detector(30, 50), n)], []);
                (team, decoys, _) = ai.GrowTeam(game, k, 0x62, major, value, known, null, chars[0], [.. chars.Skip(1), .. sf, .. more], Any);
                output.WriteLine($"major abduction, {n} detectors, value {value}: agents {team.Count}, decoys {decoys.Count}");
                largest = Math.Max(largest, team.Count + decoys.Count);
            }
        Assert.True(largest > OpenSWRController.MaxTeamSmall);
    }

    /// <summary>
    /// Teams, the agent choice as a worth check: for a sabotage, an eligible idle special force whose worth is at least the best
    /// character's is chosen; with a lower worth the character is.
    /// </summary>
    [GameFact]
    public void SabotageGoesToASpecialForce()
    {
        var (game, ai) = Game(Owner.Empire, OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Teams));
        game.Clock.Step(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        var force = (Agent)Objects.Create(game, Definitions.SpecialForces.Where(f => f.Empire != 0).OrderByDescending(f => f.Skills.Espionage.Base + f.Skills.Combat.Base).First().Common.Ref,
            Owner.Empire, at, completed: true);
        var characters = game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.Captured).Cast<Agent>().ToList();
        // the special force between the characters: its sabotage skills at the characters' median, so some characters are better, some worse
        var skills = characters.Select(c => (c.Skills[SkillIndex.Espionage] + c.Skills[SkillIndex.Combat]) / 2).Order().ToList();
        force.Skills[SkillIndex.Espionage] = force.Skills[SkillIndex.Combat] = skills[skills.Count / 2];
        int forcePicked = 0, characterPicked = 0;
        // no guards (a character's skill wins) and known guards (a lost character costs its value, a special force its maintenance)
        foreach (var known in new[] { 0, 2, 5, 10, 20 }.Select(v => v == 0 ? OpenSWRController.CovertKnown.None
                     : new OpenSWRController.CovertKnown([.. Enumerable.Repeat(new OpenSWRController.Detector(v, 0), 3)], [])))
        {
            double Worth(Agent a) => OpenSWRController.Worth(game, 0x69, [a], ai.Attempt(game, k, 0x69, a, target), 200, known);
            bool Passes(Agent a) => Worth(a) > 0 && OpenSWRController.Success(game, [a], ai.Attempt(game, k, 0x69, a, target), known) >= OpenSWRController.MinCovertSuccess;
            output.WriteLine($"guards {known.Count}: special force {Worth(force):F1}; characters " + string.Join(", ", characters.Select(c => $"{Worth(c):F1}")));
            if (!Passes(force)) continue;
            foreach (var c in characters.Where(Passes))
            {
                var pick = ai.FirstAgent(game, k, 0x69, target, 200, known, null, [c], [force], _ => true);
                Assert.NotNull(pick);
                if (Worth(force) >= Worth(c))
                {
                    Assert.Same(force, pick!.Value.Team[0]);
                    forcePicked++;
                }
                else
                {
                    Assert.Same(c, pick!.Value.Team[0]);
                    characterPicked++;
                }
            }
        }
        output.WriteLine($"special force chosen {forcePicked}, character chosen {characterPicked}");
        Assert.True(forcePicked > 0 && characterPicked > 0, "both directions not exercised");
    }
}
