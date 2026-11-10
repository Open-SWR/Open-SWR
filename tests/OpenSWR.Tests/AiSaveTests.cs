using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>
/// The computer controller's save: save gives load gives save byte-equal with live computer controllers, and the
/// continuation check: a game saved at day D continues the same way in memory with the load effects applied (branch A) and loaded
/// into a fresh <see cref="GameState"/> (branch B), draw for draw, command for command, fact for fact, and saves equal after.
/// </summary>
public class AiSaveTests(ITestOutputHelper output)
{
    const int TicksPerDay = 12;
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>A computer-vs-computer game whose controllers draw from <paramref name="ui"/>[side] (swappable per branch).</summary>
    internal static GameState NewGame(int seed, int galaxy, int difficulty, Dictionary<Owner, UiRandom> ui)
    {
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, galaxy, difficulty, 0, seed), Definitions);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int state = seed ^ (int)side;
            ui[side] = new UiRandom(() => state);
            game.Controllers[side] = new ComputerController(side) { Rand = n => ui[side].Rand(n) };
            game.Sides[side].Speed = TicksPerDay;
        }
        game.BattleResults = _ => null;
        return game;
    }

    internal static void RunDays(GameState game, int days)
    {
        int end = game.Day + days;
        while (game.Day < end && !game.GameOver) game.Clock.Step(game);
    }

    internal static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    /// <summary>The save text without the UI generator state (the load's catalogue lists draw).</summary>
    static string Text(byte[] save) => Regex.Replace(System.Text.Encoding.UTF8.GetString(save), "\"uiRngState\":-?[0-9]+", "");

    /// <summary>
    /// <see cref="Text"/> with the timer slots, the views and the objects in key order: those tables' order is not state (a game that dropped
    /// dead slots / staged objects at a load inserts later entries elsewhere, audit 2026-10-26 #16); the draws, commands and facts
    /// checked before are equal.
    /// </summary>
    static string Normalized(byte[] save)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(save)!;
        root["uiRngState"] = 0;
        void Sort(string name, Func<System.Text.Json.Nodes.JsonNode, (string, long)> key)
        {
            var list = root[name]!.AsArray();
            var sorted = list.OrderBy(x => key(x!).Item1, StringComparer.Ordinal).ThenBy(x => key(x!).Item2).Select(x => x!.DeepClone()).ToList();
            list.Clear();
            foreach (var x in sorted) list.Add(x);
        }
        Sort("timers", t => ((string)t["type"]!, (uint)t["target"]!));
        Sort("views", v => (((uint)v["object"]!).ToString("D10"), v["side"] is { } side ? (long)Enum.Parse<Owner>((string)side!) : 0));
        Sort("objects", o => (((uint)o["type"]!).ToString("D10"), (uint)o["id"]!)); // first-reference order, follows the views
        return root.ToJsonString();
    }

    static GameState Load(byte[] save, Func<Owner, Func<int, int>>? rand = null)
    {
        using var stream = new MemoryStream(save);
        return SaveGame.Load(stream, Definitions, rand);
    }

    [GameFact]
    public void SaveLoadSaveIsByteEqualWithComputerControllers()
    {
        var ui = new Dictionary<Owner, UiRandom>();
        var game = NewGame(12345, 2, 2, ui);
        foreach (int day in new[] { 100, 250 })
        {
            RunDays(game, day - game.Day);
            var first = Save(game);
            var loaded = Load(first, side => n => 0);
            Assert.Equal(Text(first), Text(Save(loaded)));
            // the AI continued: ops, tasks and planner states survive the load
            foreach (var side in new[] { Owner.Alliance, Owner.Empire })
            {
                var (a, b) = ((ComputerController)game.Controllers[side], (ComputerController)loaded.Controllers[side]);
                Assert.True(b.Started);
                Assert.Equal(a.Model.Ops.Keys, b.Model.Ops.Keys);
                Assert.Equal(a.Model.FleetOps.Keys, b.Model.FleetOps.Keys);
                Assert.Equal(a.Model.CharacterOps.Keys, b.Model.CharacterOps.Keys);
                Assert.Equal(a.Model.Tasks.Select(t => t.Key), b.Model.Tasks.Select(t => t.Key));
                Assert.Equal(a.Planners.Select(p => (p.Type, p.Phase, p.State, p.Status, p.Priority)), b.Planners.Select(p => (p.Type, p.Phase, p.State, p.Status, p.Priority)));
            }
            output.WriteLine($"day {game.Day}: {first.Length} bytes, ops {string.Join(" / ", game.Controllers.Values.Cast<ComputerController>().Select(c => $"{c.Model.Ops.Count} + {c.Model.FleetOps.Count} + {c.Model.CharacterOps.Count}"))}");
        }
    }

    [GameTheory]
    [InlineData(100)]
    [InlineData(600)]
    public void LoadContinuesLikeTheGameWithTheLoadEffects(int day)
    {
        var uiA = new Dictionary<Owner, UiRandom>();
        var a = NewGame(12345, 3, 2, uiA);
        RunDays(a, day - a.Day);
        var save = Save(a);

        // the same UI-generator state in both branches
        foreach (var side in uiA.Keys.ToList()) uiA[side] = new UiRandom(() => 4242 + (int)side);
        var uiB = new Dictionary<Owner, UiRandom> { [Owner.Alliance] = new(() => 4242 + 1), [Owner.Empire] = new(() => 4242 + 2) };
        foreach (var side in uiA.Keys) (uiA[side].Trace, uiB[side].Trace) = ([], []);

        Controller.AfterLoad(a);
        var b = Load(save, side => n => uiB[side].Rand(n));
        Assert.Equal(uiA.Values.Select(u => u.Trace!.Count), uiB.Values.Select(u => u.Trace!.Count));

        var log = new Dictionary<GameState, List<string>> { [a] = [], [b] = [] };
        foreach (var g in new[] { a, b })
        {
            var list = log[g];
            g.FactTrace = [];
            g.Random.Trace = [];
            g.BattleResults = _ => null;
            g.Clock.Executed = (c, r) => list.Add($"{g.Clock.Tick} {c.Side} {(int)c.Type} {c.Sequence} [{string.Join(",", c.Targets.Select(t => t.Target.Ref.ToString()))}] {c.ArgRef?.Ref} {(int)r.Category}/{(int)r.Code}");
        }
        RunDays(a, 200);
        RunDays(b, 200);
        foreach (var side in uiA.Keys) Assert.Equal(uiA[side].Trace!, uiB[side].Trace!);
        Assert.Equal(log[a], log[b]);
        Assert.Equal(a.FactTrace!, b.FactTrace!);
        Assert.Equal(a.Random.Trace!, b.Random.Trace!);
        Assert.Equal(Normalized(Save(a)), Normalized(Save(b)));
        output.WriteLine($"day {a.Day}: UI draws {string.Join(" / ", uiA.Values.Select(u => u.Trace!.Count))}, commands {log[a].Count}, facts {a.FactTrace!.Count}");
    }
}
