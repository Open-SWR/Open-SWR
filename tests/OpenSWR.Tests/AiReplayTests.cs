using System.Text.Json;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>
/// Live check: runs 1-8 with the computer side's controller live (planners 1, 5, 7, 14 of rounds 4-5, the rest
/// stubs) and the human side's recorded commands injected at their recorded ticks (0x280 speed values from the raw trace). The
/// AI's UI draws come from the run's recorded UI-generator chain with the skip rule. Compares the AI's executed commands
/// (tick, type, targets, ArgRef, production fields) with the recorded AI commands and reports the match depth and the first
/// divergence with the planner that recorded each command. Needs the raw traces (OPENSWR_TRACE_DIR).
/// </summary>
public class AiReplayTests(ITestOutputHelper output)
{
    const string TraceDir = "OPENSWR_TRACE_DIR";
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>Runs by id: seed, raw trace, command vector (runs 1 / 7 and 3 / 9 / 10 / 11 share a seed).</summary>
    static readonly Dictionary<int, (int Seed, string Trace, string Vector)> Runs = new()
    {
        [1] = (874334749, "rng-20261016-140714.jsonl", "commands-874334749.jsonl"),
        [2] = (401859171, "rng-20261016-141308.jsonl", "commands-401859171.jsonl"),
        [3] = (532947196, "rng-20261016-160931.jsonl", "commands-532947196.jsonl"),
        [4] = (22611212, "rng-20261018-173319.jsonl", "commands-22611212.jsonl"),
        [5] = (420082270, "rng-20261018-190905.jsonl", "commands-420082270.jsonl"),
        [6] = (99647702, "rng-20261018-191139.jsonl", "commands-99647702.jsonl"),
        [7] = (874334749, "rng-20261018-200219.jsonl", "commands-874334749-forced.jsonl"),
        [8] = (807887694, "rng-20261018-200315.jsonl", "commands-807887694.jsonl"),
        [9] = (532947196, "rng-20261018-223709.jsonl", "commands-532947196-run9.jsonl"),
        [10] = (532947196, "rng-20261018-224440.jsonl", "commands-532947196-run10.jsonl"),
        [11] = (532947196, "rng-20261018-225644.jsonl", "commands-532947196-run11.jsonl"),
        [12] = (532947196, "rng-20261020-101403.jsonl", "commands-532947196-run12.jsonl"),
        [13] = (532947196, "rng-20261020-181346.jsonl", "commands-532947196-run13.jsonl"),
        [14] = (421765374, "rng-20261021-233902.jsonl", "commands-421765374.jsonl"),
    };

    /// <summary>Runs whose trace has `ai_step` for every planner: the position-exact draw check. Run 11 steps planners 5 / 6 only, run 10 none.</summary>
    static readonly HashSet<int> Stepped = [12, 13, 14];

    /// <summary>
    /// The recorded UI-generator chain fed to the AI: skip n <= 1 and advisor-sized draws (n != k, n not in {10, 20}); the next
    /// event must have n = k. At a divergence (or the chain's end) the generator continues from the last recorded state.
    /// </summary>
    sealed class Chain(List<(int N, int Before, int Result, int Tick)> events)
    {
        int _pos, _start = -1;
        readonly Dictionary<int, int> _ours = [];
        readonly HashSet<int> _skipped = [];
        /// <summary>Draws carry the think tick of the next `ai_step`; false = the run has none (size check only).</summary>
        public bool Positioned { get; init; }
        /// <summary>Served draws before the first one whose recorded think tick differs from ours (game-start draws excluded).</summary>
        public int PositionExact { get; private set; } = -1;
        public string? PositionDivergence { get; private set; }
        UiRandom? _fallback;
        public int Used { get; private set; }
        public string? Divergence { get; private set; }
        public List<int> Requests { get; } = [];
        /// <summary>The tick being stepped (for the divergence report).</summary>
        public int Tick { get; set; }
        public Func<string>? Ctx { get; set; }
        static readonly string? DrawLog = Environment.GetEnvironmentVariable("OPENSWR_DRAWLOG");

        public int Rand(int k)
        {
            if (k <= 1) return 0;
            Requests.Add(k);
            if (Tick > 0)
            {
                if (_start < 0) _start = _pos;
                _ours[Tick] = _ours.GetValueOrDefault(Tick) + 1;
            }
            if (_fallback is null)
            {
                while (_pos < events.Count)
                {
                    var e = events[_pos];
                    if (e.N <= 1 || e.N != k && e.N is not (10 or 20)) { _skipped.Add(_pos++); continue; }
                    if (e.N == k)
                    {
                        // tick, chain position, n, result, OPENSWR_STEP_DUMP step index: a size-only match hides shifted rand(10)s
                        if (DrawLog is { } f) File.AppendAllText(f, $"{Tick} {_pos} {k} {e.Result} {Ctx?.Invoke()}{Environment.NewLine}");
                        if (Positioned && Tick > 0 && PositionDivergence is null && e.Tick != Tick)
                            (PositionExact, PositionDivergence) = (Used, $"draw {Used} (tick {Tick}): recorded think tick {e.Tick}");
                        _pos++;
                        Used++;
                        return e.Result;
                    }
                    Divergence = $"draw {Used} (tick {Tick}): AI rand({k}), recorded n = {e.N}";
                    break;
                }
                Divergence ??= $"draw {Used}: chain exhausted";
                if (PositionDivergence is null) (PositionExact, PositionDivergence) = (Used, Divergence);
                int state = _pos < events.Count ? events[_pos].Before : events[^1].Before;
                _fallback = new UiRandom(() => state);
            }
            return _fallback.Rand(k);
        }

        /// <summary>
        /// First think tick ( <= last) whose draw count differs from the recorded one; null = none. Game start excluded; recorded =
        /// the events the chain served, then (after its position) those of n 10 / 20 (advisor-sized ones are skipped).
        /// </summary>
        public int? CountDivergence(int last)
        {
            if (!Positioned || _start < 0) return null;
            var recorded = events.Select((e, i) => (e, i)).Skip(_start).Where(x => x.i < _pos ? !_skipped.Contains(x.i) : x.e.N is 10 or 20)
                .GroupBy(x => x.e.Tick).ToDictionary(g => g.Key, g => g.Count());
            return Enumerable.Range(1, last).Cast<int?>().FirstOrDefault(t => _ours.GetValueOrDefault(t!.Value) != recorded.GetValueOrDefault(t!.Value));
        }
    }

    sealed record Issued(int Tick, int Type, uint[] Targets, uint ArgRef, uint[] Fields)
    {
        public Command? Source { get; init; }
        public int Planner { get; set; }
        public override string ToString() =>
            (Planner > 0 ? $"[p{Planner}] " : "") + $"tick {Tick} {Type:X} [{string.Join(", ", Targets.Select(t => t.ToString("X8")))}] → {ArgRef:X} {(Fields.Length > 0 ? $"[{Fields[0]:X8} ×{Fields[1]}]" : "")}";
        public bool SameCommand(Issued o) => Type == o.Type && Targets.SequenceEqual(o.Targets) && ArgRef == o.ArgRef && Fields.SequenceEqual(o.Fields);
    }

    static uint Hex(JsonElement e) => Convert.ToUInt32(e.GetString(), 16);
    static uint[] ProductFields(int type, IEnumerable<uint> fields) => type is >= 0x210 and <= 0x212 ? [.. fields.Take(2)] : [];

    /// <summary>Recorded AI command -> planner by the mapping rules (ambiguous rules list the candidates).</summary>
    static string PlannerOf(Issued c) => c.Type switch
    {
        0x203 => "14",
        0x205 => "acknowledgement",
        0x200 => Family.Of(c.Targets[0]) is 0x22 or 0x23 or 0x24 ? "3 (defence scrap)" : "1–3 (F / H / K scrap)",
        0x214 => Family.Of(c.Targets[0]) switch { 0xA0 => "1–3 (construction)", 0xA2 => Family.Of(c.ArgRef) is >= 8 and < 0x10 ? "10 (fleet fighters)" : "7 / 11", _ => "5 / 6" },
        0x210 => Family.Of(c.Fields[0]) switch { 0x2C or 0x2D or 0x2A => "1–3", 0x28 => "7 / 10 / 11", _ => "5 / 6" },
        0x211 => Family.Of(c.Fields[0]) is >= 0x1C and < 0x20 ? "7 / 10" : "11",
        0x212 => Family.Of(c.Fields[0]) is >= 0x3C ? "5" : "6",
        0x240 or 0x250 => "4 (mission operations)",
        0x260 or 0x261 or 0x262 or 0x263 or 0x242 => "12 / 5 (characters)",
        0x201 when c.Targets.All(t => Family.IsTroop(t)) => "6 (troop move)",
        0x201 when c.Targets.All(t => Family.Of(t) is >= 0x1C and < 0x20) => "7 (fighter move)",
        _ => "8 / 9 / 11 / 13 / operations (moves, rounds 6–7)",
    };

    /// <summary>Commands of the implemented planners (production 1-3, 5-7, 10, 11, names 14): not acknowledgements, missions, character orders or unit / fleet moves of rounds 6-8.</summary>
    static bool Implemented(Issued c) => c.Type is not (0x205 or 0x240 or 0x250 or 0x260 or 0x261 or 0x262 or 0x263 or 0x270)
        && !(c.Type == 0x201 && PlannerOf(c).StartsWith("8 "));

    sealed record Result(List<Issued> Recorded, List<Issued> Actual, Chain Chain, List<string> Log, int LastTick);

    static Result Run(int run, Owner player, int throughDay)
    {
        var (seed, trace, vector) = Runs[run];
        string dir = Path.Combine(AppContext.BaseDirectory, "Vectors");
        var ai = player == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        var records = File.ReadLines(Path.Combine(dir, vector)).Select(l => JsonDocument.Parse(l).RootElement.Clone())
            .Where(r => r.GetProperty("day").GetInt32() <= throughDay).ToList();
        var draws = new List<(int N, int Before, int Result, int Tick)>();
        int unticked = 0; // draws waiting for the next ai_step's tick
        var speeds = new Queue<(int Tick, Owner Side, uint Value)>();
        foreach (var line in File.ReadLines(Path.Combine(Environment.GetEnvironmentVariable(TraceDir)!, trace)))
        {
            bool step = line.Contains("\"ev\":\"ai_step\"");
            if (step)
            {
                if (unticked == 0) continue;
                int t = JsonDocument.Parse(line).RootElement.GetProperty("tick").GetInt32();
                for (int i = draws.Count - unticked; i < draws.Count; i++) draws[i] = draws[i] with { Tick = t };
                unticked = 0;
                continue;
            }
            if (!line.Contains("\"0x6b14d8\"") && !line.Contains("\"0x280\"")) continue;
            var e = JsonDocument.Parse(line).RootElement;
            string ev = e.GetProperty("ev").GetString()!;
            if (ev == "rand" && e.GetProperty("gen").GetString() == "0x6b14d8")
            {
                draws.Add((e.GetProperty("n").GetInt32(), (int)e.GetProperty("before").GetInt64(), e.GetProperty("result").GetInt32(), -1));
                unticked++;
            }
            else if (ev == "command" && e.GetProperty("type").GetString() == "0x280")
            {
                var raw = e.GetProperty("raw");
                speeds.Enqueue((e.GetProperty("tick").GetInt32(), (Owner)raw[1].GetInt32(), raw[4].GetUInt32()));
            }
        }
        var chain = new Chain(draws) { Positioned = Stepped.Contains(run) };
        var game = NewGame.Create(new GameSettings(player, 1, 0, 0, seed), Definitions);
        var controller = new ComputerController(ai) { Rand = chain.Rand, IssueTrace = [], StepTrace = [] };
        game.Controllers[ai] = controller;
        chain.Ctx = () => controller.StepTrace!.Count.ToString();
        game.Clock.CommandTrace = [];
        var objects = new Dictionary<uint, GameObject>();
        void Index()
        {
            foreach (var s in game.Systems) { objects.TryAdd(s.Ref, s); foreach (var m in s.Managers) objects.TryAdd(m.Ref, m); }
            foreach (var o in game.Characters.Concat(game.Created)) objects.TryAdd(o.Ref, o);
        }
        var log = new List<string>();
        var recorded = records.Where(r => r.GetProperty("side").ValueKind == JsonValueKind.Number && r.GetProperty("side").GetInt32() == (int)ai)
            .Select(r => new Issued(r.GetProperty("tick").GetInt32(), (int)Hex(r.GetProperty("type")),
                [.. r.GetProperty("targets").EnumerateArray().Select(t => Hex(t[0]))], Hex(r.GetProperty("arg_ref")) is var a && a != 2 ? a : 0,
                ProductFields((int)Hex(r.GetProperty("type")), r.GetProperty("fields").EnumerateArray().Select(f => f.GetUInt32())))).ToList();
        int lastTick = records.Max(r => r.GetProperty("tick").GetInt32());
        uint sequence = player == Owner.Alliance ? 1u : 0x77359401u;
        var actual = new List<Issued>();
        int seen = 0;
        // Game start: side 1 first; the human side's advisor draws its seven catalogue lists on the same generator
        if (player == Owner.Alliance) WorldModel.CatalogueLists(game, player, chain.Rand);
        Controller.StartGame(game);
        if (player == Owner.Empire) WorldModel.CatalogueLists(game, player, chain.Rand);
        while (game.Clock.Tick < lastTick && !game.GameOver)
        {
            int tick = game.Clock.Tick + 1;
            Index();
            foreach (var r in records.Where(r => r.GetProperty("tick").GetInt32() == tick))
            {
                int type = (int)Hex(r.GetProperty("type"));
                Command? c = null;
                if (type == 0x280)
                {
                    var (at, side, value) = speeds.Dequeue();
                    Assert.Equal(tick, at);
                    c = new Command(0x280, side) { ArgInt = value };
                }
                else if (r.GetProperty("side").GetInt32() == (int)player)
                {
                    var targets = r.GetProperty("targets").EnumerateArray().Select(t => (objects.GetValueOrDefault(Hex(t[0])), t[1].GetInt32())).ToList();
                    uint arg = Hex(r.GetProperty("arg_ref"));
                    if (targets.Any(t => t.Item1 is null) || arg != 2 && !objects.ContainsKey(arg)) { log.Add($"tick {tick}: human {type:X} refers to an unknown object, skipped"); continue; }
                    List<GameObject> decoys = [];
                    if (type == 0x240 && r.GetProperty("events")[0].GetProperty("lists")[1] is var second)
                        decoys = [.. second.EnumerateArray().Select(t => objects.GetValueOrDefault(Hex(t[0]))).OfType<GameObject>()];
                    c = new Command(type, player)
                    {
                        Targets = [.. targets.Select(t => (t.Item1!, t.Item2))], ArgRef = arg == 2 ? null : objects[arg], Decoys = decoys,
                        ArgInt = r.GetProperty("arg_int").GetUInt32(), Fields = [.. r.GetProperty("fields").EnumerateArray().Select(f => f.GetUInt32())],
                    };
                }
                if (c is null) continue;
                c.Sequence = sequence++;
                game.Clock.Submit(c);
            }
            chain.Tick = tick;
            game.Clock.Step(game);
            foreach (var c in game.Clock.CommandTrace.Skip(seen).Where(c => c.Side == ai && c.Type >= 0x200))
                actual.Add(new Issued(game.Clock.Tick, c.Type, [.. c.Targets.Select(t => t.Target.Ref)], c.ArgRef?.Ref ?? 0, ProductFields(c.Type, c.Fields)) { Source = c });
            seen = game.Clock.CommandTrace.Count;
        }
        foreach (var (planner, c, result) in controller.IssueTrace!)
            if (!result.Success) log.Add($"planner {planner} {c.Type:X} refused {result} [{string.Join(",", c.Targets.Select(t => t.Target.Ref.ToString("X")))}] → {c.ArgRef?.Ref:X} type {(c.Fields.Length > 2 ? c.Fields[2] : 0):X} user {(c.Fields.Length > 0 ? c.Fields[0] : 0)}");
        if (Environment.GetEnvironmentVariable("OPENSWR_STEP_DUMP") is { } dump) // per-think issue steps, file prefix
            File.WriteAllLines(dump + run + ".txt", controller.StepTrace!.Select((t, i) => $"{i} {t.Tick} p{t.Planner} ph{t.Phase:X} c{t.Command:X} {(t.Done ? "done" : "-")} {(t.Dirty ? "DIRTY" : "")}"));
        var by = controller.IssueTrace!.Where(t => t.Result.Success).ToDictionary(t => t.Command, t => t.Planner);
        foreach (var a in actual) a.Planner = by.GetValueOrDefault(a.Source!, 0);
        return new Result(recorded, actual, chain, log, game.Clock.Tick);
    }

    /// <summary>
    /// Prints the comparison; returns the number of leading AI commands that match exactly (tick included) and the number of the
    /// recorded commands of the implemented planners (1, 5 special forces, 7 system fighters, 14) matched in order, ticks ignored.
    /// </summary>
    (int Exact, int Implemented) Report(Result r, string run)
    {
        int i = 0;
        while (i < r.Recorded.Count && i < r.Actual.Count && r.Recorded[i].Tick == r.Actual[i].Tick && r.Recorded[i].SameCommand(r.Actual[i])) i++;
        output.WriteLine($"{run}: {r.Recorded.Count} recorded AI commands, {r.Actual.Count} issued, {i} match exactly; UI draws used {r.Chain.Used}"
            + $" of {r.Chain.Requests.Count} requested; draw divergence: {r.Chain.Divergence ?? "none"}");
        if (r.Chain.Positioned) // draws carry their think tick
            output.WriteLine($"  draws position-exact to {(r.Chain.PositionExact < 0 ? r.Chain.Used : r.Chain.PositionExact)} ({r.Chain.PositionDivergence ?? "no divergence"});"
                + $" first tick with a different draw count: {r.Chain.CountDivergence(r.LastTick)?.ToString() ?? "none"}");
        if (i < Math.Max(r.Recorded.Count, r.Actual.Count))
            output.WriteLine($"  first divergence: recorded {(i < r.Recorded.Count ? $"{r.Recorded[i]} (planner {PlannerOf(r.Recorded[i])})" : "(end)")} | issued {(i < r.Actual.Count ? r.Actual[i] : "(end)")}");
        var implemented = r.Recorded.Where(Implemented).ToList();
        var mine = r.Actual.Where(Implemented).ToList();
        int k = 0;
        while (k < implemented.Count && k < mine.Count && implemented[k].SameCommand(mine[k])) k++;
        output.WriteLine($"  implemented-planner commands in order (ticks ignored): {k} of {implemented.Count} match"
            + (k < Math.Max(implemented.Count, mine.Count) ? $"; next recorded {(k < implemented.Count ? implemented[k].ToString() : "(end)")} | issued {(k < mine.Count ? mine[k] : "(end)")}" : ""));
        foreach (var c in r.Actual.Take(20)) output.WriteLine($"  issued {c}");
        foreach (var c in r.Recorded.Take(20)) output.WriteLine($"  recorded {c} (planner {PlannerOf(c)})");
        foreach (var l in r.Log.Take(10)) output.WriteLine("  " + l);
        return (i, k);
    }

    [GameTheory(TraceDir)]
    [InlineData(1, Owner.Empire, 999, 170, 32)]  // all 170 recorded commands, all 5712 draws
    [InlineData(2, Owner.Empire, 999, 147, 34)]  // all recorded commands
    [InlineData(3, Owner.Alliance, 999, 155, 68)]  // all 155 recorded commands, all 15435 recorded draws
    [InlineData(4, Owner.Empire, 999, 40, 16)]  // all recorded commands (run 4, AI hooks)
    [InlineData(5, Owner.Empire, 999, 36, 13)]  // all recorded commands (run 5, game A)
    [InlineData(6, Owner.Alliance, 999, 146, 20)]  // all recorded commands (run 6, game B)
    [InlineData(7, Owner.Empire, 999, 33, 15)]  // all recorded commands (run 7, run 1's galaxy, forced seed)
    [InlineData(8, Owner.Alliance, 999, 426, 71)]  // all 426 recorded commands, all 10392 draws (object ops wait for their decoys)
    [InlineData(9, Owner.Alliance, 999, 634, 200)]  // all 634 recorded commands, all 9695 draws (run 9, run 3's seed, unhooked)
    [InlineData(10, Owner.Alliance, 999, 676, 188)]  // all 676 recorded commands, all 10779 draws (run 10, run 3's seed, AI hooks v2)
    [InlineData(11, Owner.Alliance, 999, 45, 18)]  // all recorded commands, all 408 draws (run 11, run 3's seed, ai_step)
    [InlineData(12, Owner.Alliance, 999, 437, 174)]  // all 437 recorded commands, all 12205 draws position-exact (run 12, run 3's seed, all-planner steps)
    [InlineData(13, Owner.Alliance, 999, 1081, 417)]  // all 1081 recorded commands, all 13934 draws position-exact (run 13, run 3's seed, all AI hooks)
    [InlineData(14, Owner.Empire, 999, 596, 117)]  // all 596 recorded commands, all 11804 draws position-exact (run 14, seed 421765374, AI Alliance, all AI hooks)
    public void ComputerSideAgainstTheTrace(int run, Owner player, int throughDay, int exact, int implemented)
    {
        var r = Run(run, player, throughDay);
        var (e, k) = Report(r, $"run {run} (seed {Runs[run].Seed}) days 1–{throughDay}");
        Assert.True(e >= exact, $"exact match depth {e} < {exact}");
        Assert.True(k >= implemented, $"implemented-planner depth {k} < {implemented}");
    }
}
