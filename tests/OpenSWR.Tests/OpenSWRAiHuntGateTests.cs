using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent objectives that can work: the Hunt gate and the HQ siege, and their save fields.</summary>
public class OpenSWRAiHuntGateTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 Hunt = OpenSWRController.V7.Hunt, Siege = OpenSWRController.V7.Siege;

    static (GameState Game, OpenSWRController Ai) Game(Owner side, OpenSWRController.V7 add)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { Features = OpenSWRController.DefaultFeatures.Add(add), IssueTrace = [] };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    static StarSystem HqSystem(GameState game) => game.Created.Where(o => Family.Of(o.Type) is >= Families.Headquarters and < Families.IonCannon).MinBy(o => o.Key)!.System!;

    /// <summary>
    /// The gate refuses an abduction of Vader by an agent with EnhCombat 120 against Vader's 220 (gate 0.25): the estimate is at most
    /// T(ABDCMSTB, 120 - 220), and the target then waits.
    /// </summary>
    [GameFact]
    public void VaderAbductionRefusedAtGate()
    {
        var (game, ai) = Game(Owner.Alliance, Hunt);
        var vader = game.Known.Vader!;
        var coruscant = (StarSystem)Missions.Location(vader)!; // where Vader sits (his home system)
        Knowledge.Update(game, coruscant, 10, Owner.Alliance);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Alliance);
        var agent = game.Characters.Where(c => c.Owner == Owner.Alliance && c.Existing && !c.OnMission).OrderBy(c => c.Key).First(c => c.Ref != Refs.Luke);
        agent.Skills[SkillIndex.Combat] = 120;
        (agent.ForceRank, agent.ForceBonus) = (0, 0);
        int vaderCombat = Missions.Enhanced(game, vader, SkillIndex.Combat);
        output.WriteLine($"agent {Family.Of(agent.Ref)}:{SdRef.IndexOf(agent.Ref)} EnhCombat {Missions.Enhanced(game, agent, SkillIndex.Combat)}, Vader EnhCombat {vaderCombat} (definition base {vader.Definition.Skills.Combat.Base})");
        Assert.Equal(120, Missions.Enhanced(game, agent, SkillIndex.Combat));
        var known = ai.KnownAt(game, k, coruscant, true, null);
        double estimate = ai.HuntEstimate(game, k, [agent], [], vader, known);
        int cap = Math.Clamp(Missions.Table(game, "ABDCMSTB", 120 - 220) ?? 0, 0, 100);
        output.WriteLine($"estimate {estimate:F3}, T(ABDCMSTB, -100) = {cap}");
        Assert.True(estimate <= cap / 100.0);
        Assert.True(estimate < OpenSWRController.MinHuntSuccess);
        Assert.Same(coruscant, Knowledge.Location(game, vader, Owner.Alliance));
        Assert.DoesNotContain(ai.HuntAbductions(game, k, [agent], new HashSet<GameObject?>(), []), x => x.Target == vader);
    }

    /// <summary>The gate passes an abduction of Mon Mothma alone in a system with no enemy units (Vader as the agent).</summary>
    [GameFact]
    public void MothmaAlonePasses()
    {
        var (game, ai) = Game(Owner.Empire, Hunt);
        var mothma = game.Known.Mothma!;
        var vader = game.Known.Vader!;
        var empty = game.Systems.Where(s => s.Owner != Owner.Alliance && !s.Contents.Any(x => x.Owner == Owner.Alliance && (x is Fleet { Contents.Count: > 0 } || Family.Of(x.Type) is >= Families.Troop and < Families.MajorCharacter))).OrderBy(s => s.Key).First();
        Objects.SetParent(game, mothma, empty);
        Knowledge.Update(game, empty, 10, Owner.Empire);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Same(empty, Knowledge.Location(game, mothma, Owner.Empire));
        Assert.DoesNotContain(k.Contents(empty), v => v.Owner == Owner.Alliance && v.Object != mothma);
        var abductions = ai.HuntAbductions(game, k, [vader], new HashSet<GameObject?>(), []);
        output.WriteLine(string.Join(", ", abductions.Select(x => $"{Family.Of(x.Target.Ref)}:{SdRef.IndexOf(x.Target.Ref)} {x.Success:F3}")));
        var x = Assert.Single(abductions, a => a.Target == mothma);
        Assert.True(x.Success >= OpenSWRController.MinHuntSuccess);
        Assert.Contains(vader, x.Team);
    }

    /// <summary>An HQ siege group (Empire) loads at least the attack troops plus the garrison requirement at its share there.</summary>
    [GameFact]
    public void HqSiegeLoadsAttackTroopsPlusRequirement()
    {
        var (game, ai) = Game(Owner.Empire, Siege);
        var hq = HqSystem(game);
        Knowledge.Update(game, hq, 10, Owner.Empire);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        int attack = ai.AttackTroopsFor(k, hq), requirement = Systems.BaseRequirement(game, 100 - Knowledge.See(game, hq, Owner.Empire).Support);
        output.WriteLine($"attack troops {attack}, requirement {requirement}");
        Assert.True(requirement > 0);
        Assert.Equal(attack + requirement, ai.SiegeTroopsFor(game, k, hq));
    }

    /// <summary>With both flags off, play is the default (they are off in the default; the default CSV check is aimatch's).</summary>
    [GameFact]
    public void FlagsOffAreTheDefault()
    {
        Assert.False(OpenSWRController.DefaultFeatures.Overlaps([Hunt, Siege]));
        Assert.Equal(OpenSWRController.MinHuntSuccess, new OpenSWRController(Owner.Empire).HuntGate);
    }

    /// <summary>The optional objective state (HQ candidates, hunt spies and waits, the siege target) survives save, load, save byte-equal.</summary>
    [GameFact]
    public void ObjectiveStateRoundTrips()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 200001, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { Features = OpenSWRController.DefaultFeatures.Add(Hunt).Add(Siege).Add(OpenSWRController.V7.Guard) };
        game.Controllers[Owner.Empire] = ai;
        int ui = 200000;
        var rand = new UiRandom(() => ui);
        game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = n => rand.Rand(n) };
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        while (game.Day < 150 && !game.GameOver) game.Clock.Step(game);
        var j = ai.Save().ToJsonString();
        output.WriteLine(j.Length > 400 ? j[..400] : j);
        Assert.Contains("\"hqInit\"", j);
        Assert.Contains("\"hqCandidates\"", j);
        Assert.Contains("\"huntSpies\"", j);
        var copy = new OpenSWRController(Owner.Empire) { Features = ai.Features };
        copy.Load(game, 0, ai.Save());
        Assert.Equal(j, copy.Save().ToJsonString());
        using var a = new MemoryStream();
        SaveGame.Save(game, a);
        using var b = new MemoryStream(a.ToArray());
        var loaded = SaveGame.Load(b, Definitions, _ => _ => 0);
        using var c = new MemoryStream();
        SaveGame.Save(loaded, c);
        string Text(byte[] x) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(x), "\"uiRngState\":-?[0-9]+", "");
        Assert.Equal(Text(a.ToArray()), Text(c.ToArray()));
    }
}
