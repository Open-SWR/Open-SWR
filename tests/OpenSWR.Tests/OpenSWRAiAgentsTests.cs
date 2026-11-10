using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent agent rules: which special forces can take which missions, pool training, pivots, retries, and ranking.</summary>
public class OpenSWRAiAgentsTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    /// <summary>Agents without Decoys: these tests cover the agent rules only.</summary>
    static readonly ImmutableHashSet<OpenSWRController.V7> Agents = OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Agents).Remove(OpenSWRController.V7.Decoys);

    static (GameState Game, OpenSWRController Ai) Game(Owner side, bool control = true)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { IssueTrace = [], Features = Agents, PivotEvents = [], RetryRecords = [] };
        var other = side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        game.Controllers[side] = control ? ai : new HumanController(side);
        game.Controllers[other] = new HumanController(other);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        return (game, ai);
    }

    /// <summary>Rule 1: an invented special-force type gets exactly the kinds whose mission mask accepts it, and none for a side it lacks.</summary>
    [GameFact]
    public void CapabilitiesFromMasks()
    {
        var (game, _) = Game(Owner.Alliance, control: false);
        var espionage = OpenSWRController.TypeOf(game, MissionKind.Espionage)!;
        uint bit = Enumerable.Range(0, 32).Select(k => (uint)Math.Pow(2, k)).First(espionage.AllowsSpecialForce); // its lowest mission bit
        var invented = Definitions.SpecialForces[0] with { MissionMask = bit, Alliance = 1, Empire = 0 };
        var expected = new MissionKind[] { MissionKind.Espionage, MissionKind.Reconnaissance, MissionKind.InciteUprising, MissionKind.Rescue, MissionKind.Abduction, MissionKind.Assassination, MissionKind.Sabotage }.Where(k => OpenSWRController.TypeOf(game, k) is { } m && m.AllowsSpecialForce(bit)).ToList();
        var kinds = OpenSWRController.KindsFor(game, Owner.Alliance, invented);
        output.WriteLine($"bit {bit}: kinds {string.Join(",", kinds.Select(k => ((uint)k).ToString()))}");
        Assert.Equal(expected, kinds);
        Assert.Contains(MissionKind.Espionage, kinds);
        Assert.Empty(OpenSWRController.KindsFor(game, Owner.Empire, invented));
        Assert.Empty(OpenSWRController.KindsFor(game, Owner.Alliance, invented with { MissionMask = 0 }));
    }

    /// <summary>Rule 2 (user decision): with the pool short of its target, espionage types come first and every buildable type gets a training order.</summary>
    [GameFact]
    public void ShortPoolTrainsEveryTypeEspionageFirst()
    {
        var (game, ai) = Game(Owner.Empire);
        game.Clock.Step(game);
        var types = ai.AgentTypes(game);
        var shortfall = ai.PoolShortfall(game);
        output.WriteLine("shortfall " + string.Join(", ", shortfall.Select(x => $"{Family.Of(x.Type.Record.Common.Ref)}:{SdRef.IndexOf(x.Type.Record.Common.Ref)}{(x.Type.Espionage ? " esp" : "")} {x.Missing}")));
        Assert.Equal(types.Count, shortfall.Count);
        int lastEspionage = shortfall.FindLastIndex(x => x.Type.Espionage), firstOther = shortfall.FindIndex(x => !x.Type.Espionage);
        Assert.True(lastEspionage >= 0 && (firstOther < 0 || lastEspionage < firstOther));
        List<uint> Ordered() => [.. ai.IssueTrace!.Where(t => t.Result.Success && t.Command.Type == CommandType.ProduceTroop && Family.IsSpecialForce(t.Command.Fields[0])).Select(t => t.Command.Fields[0])];
        while (game.Day < 400 && !types.All(t => Ordered().Contains(t.Record.Common.Ref))) game.Clock.Step(game);
        var ordered = Ordered();
        output.WriteLine($"day {game.Day}");
        output.WriteLine("ordered " + string.Join(", ", ordered.Select(r => r.ToString())));
        Assert.All(types, t => Assert.Contains(t.Record.Common.Ref, ordered));
    }

    /// <summary>Rule 3: a vulnerable major character starts a pivot, and the pivot ends within PivotDays.</summary>
    [GameFact]
    public void PivotStartsAndEnds()
    {
        var (game, ai) = Game(Owner.Empire, control: false);
        game.Clock.Step(game);
        var major = game.Characters.First(c => c.Owner == Owner.Alliance && Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter && Knowledge.Location(game, c, Owner.Empire) is StarSystem);
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Existing);
        var others = game.Systems.Where(s => s.Owner == Owner.Alliance).Take(4)
            .Select((s, i) => new OpenSWRController.Covert(MissionKind.Sabotage, s, [agent], 5 + i, 0.7)).ToList();
        var pivot = new OpenSWRController.Covert(MissionKind.Abduction, major, [agent], 200, OpenSWRController.MinChance / 100.0, []);
        ai.StartPivots(game, [.. others, pivot]);
        Assert.Single(ai.Pivots);
        int start = game.Day;
        while (ai.Pivots.Count > 0 && game.Day <= start + OpenSWRController.PivotDays)
        {
            game.Clock.Step(game);
            ai.EndPivots(game);
        }
        output.WriteLine(string.Join("; ", ai.PivotEvents!.Select(e => $"day {e.Day} {(e.Started ? "start" : "end")} {e.Reason}")));
        Assert.Empty(ai.Pivots);
        Assert.InRange(ai.PivotEvents!.Last().Day - start, 0, OpenSWRController.PivotDays);
        // a low chance starts none
        ai.StartPivots(game, [.. others, pivot with { Success = 0.1 }]);
        Assert.Empty(ai.Pivots);
    }

    /// <summary>Rule 4: a retry after a foil waits without decoys and goes with them (max(1, known detectors), within the cap).</summary>
    [GameFact]
    public void RetryWaitsWithoutDecoys()
    {
        var (game, ai) = Game(Owner.Empire, control: false);
        game.Clock.Step(game);
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        var type = Definitions.SpecialForces.First(f => f.Empire != 0).Common.Ref;
        var forces = Enumerable.Range(0, 3).Select(_ => (Agent)Objects.Create(game, type, Owner.Empire, at, completed: true)).ToList();
        var known = new OpenSWRController.CovertKnown([new OpenSWRController.Detector(30, 50), new OpenSWRController.Detector(30, 50)], []);
        bool Any(List<Agent> t, List<Agent> d) => true;
        // no foil: a special-force agent needs no decoy
        Assert.NotNull(ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [forces[0]], [], [], Any, out _));
        ai.MarkFoiledTarget(target);
        Assert.Null(ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [forces[0]], [], [], Any, out int shortBy));
        Assert.Equal(2, shortBy);
        var go = ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [forces[0]], [], forces.Skip(1), Any, out _);
        Assert.NotNull(go);
        Assert.Equal(2, go!.Value.Decoys.Count);
        // the cap: a team of 3 has no room, so no decoy is needed
        Assert.NotNull(ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [.. forces], [], [], Any, out _));
    }

    /// <summary>Rule 5: a 65 % candidate ranks before a higher-worth 45 % one.</summary>
    [GameFact]
    public void RankByChanceThenWorth()
    {
        var (game, _) = Game(Owner.Empire, control: false);
        var s = game.Systems.Take(3).ToList();
        var low = new OpenSWRController.Covert(MissionKind.Sabotage, s[0], [], 100, 0.45);
        var high = new OpenSWRController.Covert(MissionKind.Sabotage, s[1], [], 10, 0.65);
        var higher = new OpenSWRController.Covert(MissionKind.Sabotage, s[2], [], 20, 0.62);
        Assert.Equal([higher, high, low], OpenSWRController.Rank([low, high, higher]));
    }

    /// <summary>Rule 7 (user decision): a sabotage goes to the character when its worth is higher, with at least 1 special-force decoy against a known detector.</summary>
    [GameFact]
    public void SabotageGoesToTheBetterCharacterWithADecoy()
    {
        var (game, ai) = Game(Owner.Empire, control: false);
        game.Clock.Step(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var at = game.Systems.First(s => s.Owner == Owner.Empire);
        var target = game.Systems.First(s => s.Owner == Owner.Alliance);
        var force = (Agent)Objects.Create(game, Definitions.SpecialForces.First(f => f.Empire != 0).Common.Ref, Owner.Empire, at, completed: true);
        force.Skills[SkillIndex.Espionage] = force.Skills[SkillIndex.Combat] = 1; // a weak special force
        var known = new OpenSWRController.CovertKnown([new OpenSWRController.Detector(5, 0)], []);
        var characters = game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.Captured).Cast<Agent>().ToList();
        var pick = ai.FirstAgent(game, k, MissionKind.Sabotage, target, 200, known, null, characters, [force], _ => true);
        Assert.NotNull(pick);
        var agent = Assert.IsAssignableFrom<Character>(pick!.Value.Team[0]);
        var team = ai.AgentDecoys(game, MissionKind.Sabotage, target, known, [agent], [], [force], (_, _) => true, out _);
        Assert.NotNull(team);
        Assert.Contains(force, team!.Value.Decoys);
        // no detector known: no decoy wanted
        Assert.Empty(ai.AgentDecoys(game, MissionKind.Sabotage, target, OpenSWRController.CovertKnown.None, [agent], [], [force], (_, _) => true, out _)!.Value.Decoys);
    }

    /// <summary>The Agents state (decoy shortfall, foiled targets, planned systems, pivots) survives save, load, save byte-equal mid-game.</summary>
    [GameFact]
    public void AgentsStateSurvivesSaveAndLoad()
    {
        var (game, ai) = Game(Owner.Empire);
        game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
        while ((game.Day < 200 || ai.PlannedAt.Count == 0) && game.Day < 800 && !game.GameOver) game.Clock.Step(game);
        Assert.NotEmpty(ai.PlannedAt);
        output.WriteLine($"day {game.Day}: decoy shortfall {ai.DecoyShort}, pivots {ai.Pivots.Count}, planned {ai.PlannedAt.Count}");
        // what the run left empty is set through the hooks
        if (ai.DecoyShort == 0) ai.DecoyShort = 2;
        ai.MarkFoiledTarget(game.Systems.First(s => s.Owner == Owner.Alliance));
        if (ai.Pivots.Count == 0)
        {
            var major = game.Characters.First(c => c.Owner == Owner.Alliance && Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter && Knowledge.Location(game, c, Owner.Empire) is StarSystem);
            var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Existing);
            ai.StartPivots(game, [new OpenSWRController.Covert(MissionKind.Abduction, major, [agent], 200, 0.5, [])]);
        }
        Assert.NotEmpty(ai.Pivots);
        static byte[] Save(GameState g)
        {
            using var stream = new MemoryStream();
            SaveGame.Save(g, stream);
            return stream.ToArray();
        }
        static GameState Load(byte[] save)
        {
            using var stream = new MemoryStream(save);
            return SaveGame.Load(stream, Definitions, _ => _ => 0);
        }
        static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");
        var save = Save(game);
        var text = System.Text.Encoding.UTF8.GetString(save);
        foreach (var key in new[] { "decoyShort", "foiledTargets", "plannedAt", "pivots" }) Assert.Contains($"\"{key}\":", text);
        var loaded = Load(save);
        Assert.Equal(Text(save), Text(Save(loaded)));
        var back = (OpenSWRController)loaded.Controllers[Owner.Empire];
        Assert.Equal(ai.Pivots, back.Pivots);
        Assert.Equal(ai.DecoyShort, back.DecoyShort);
    }
}
