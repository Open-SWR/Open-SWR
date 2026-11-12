using System.Diagnostics;
using OpenSWR.AiMatch;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

const string Usage = """
    usage:
      aimatch [--games N] [--days D] [--galaxy 1|2|3] [--difficulty 0|1|2] [--seed S] [--csv out.csv] [--features all|none|list] [--slices F] [--parallel P]
    The original game's folder comes from env OPENSWR_GAME_DIR. Each seed is played twice: the OpenSWR AI as Alliance, then as
    Empire, against the original AI. Seeds: S, S+1, … with --seed, else a fixed default list (reproducible runs).
    --hunt-gate G: Hunt's objective-abduction gate (step 22; default OpenSWRController.MinHuntSuccess, 0.25).
    --yard-ratio R: EconomyFirst's construction-yard ratio r (yards wanted = max(2, ⌈r × owned systems⌉); default 0.75).
    --parallel P: up to P games at once (default 1). Every output (text, CSV, slices CSV) is written in game order, as a serial run.
    --slices F: the version 9 time slices (every 100 days, per side) as CSV to F, and the openswr-minus-original table.
    --features: the OpenSWR AI's version 7 steps, comma-separated (Experience, LateEconomy, Fighters, Watch, Predict), or all / none.
    Defaults: --games 3 --days 400 --galaxy 1 --difficulty 1 --features Fighters,Watch,LateEconomy,DoctrineDefence,Recruit,CovertExperience,Agents,Decoys,Swarm,DoctrineProduction,Wings (the controller's default).
    """;

int games = 3, days = 400, galaxy = 1, difficulty = 1, parallel = 1;
int? seed = null;
string? csv = null, slicesCsv = null;
var features = OpenSWR.Sim.Ai.OpenSWRController.DefaultFeatures;
double yardRatio = OpenSWR.Sim.Ai.OpenSWRController.DefaultYardRatio;
double huntGate = OpenSWR.Sim.Ai.OpenSWRController.MinHuntSuccess;
try
{
    for (int i = 0; i < args.Length; i++)
    {
        int Next() => int.Parse(args[++i]);
        switch (args[i])
        {
            case "--games": games = Next(); break;
            case "--days": days = Next(); break;
            case "--galaxy": galaxy = Next(); break;
            case "--difficulty": difficulty = Next(); break;
            case "--seed": seed = Next(); break;
            case "--csv": csv = args[++i]; break;
            case "--slices": slicesCsv = args[++i]; break;
            case "--parallel": parallel = Next(); break;
            case "--hunt-gate": huntGate = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
            case "--yard-ratio": yardRatio = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
            case "--features":
                string list = args[++i];
                features = OpenSWR.Sim.Ai.OpenSWRController.ParseFeatures(list) ?? throw new FormatException($"unknown features '{list}'");
                break;
            default: throw new FormatException($"unknown argument '{args[i]}'");
        }
    }
    if (games < 1 || days < 1 || galaxy is < 1 or > 3 || difficulty is < 0 or > 2 || parallel < 1) throw new FormatException("argument out of range");
}
catch (Exception e) when (e is FormatException or IndexOutOfRangeException)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine(Usage);
    return 2;
}

string? dir = Environment.GetEnvironmentVariable("OPENSWR_GAME_DIR");
if (string.IsNullOrWhiteSpace(dir) || !File.Exists(Path.Combine(dir, "STRATEGY.DLL")))
{
    Console.Error.WriteLine("Set OPENSWR_GAME_DIR to the original game's folder (the one holding STRATEGY.DLL and GData).");
    return 2;
}
var definitions = GameDefinitions.Load(dir);
using var output = csv is null ? null : new StreamWriter(csv);
output?.WriteLine(Match.CsvHeader);
using var slicesOutput = slicesCsv is null ? null : new StreamWriter(slicesCsv);
slicesOutput?.WriteLine(SliceRecorder.CsvHeader);

Console.WriteLine($"OpenSWR AI version 7 features: {string.Join(",", features.OrderBy(f => f))}; yard ratio {yardRatio.ToString(System.Globalization.CultureInfo.InvariantCulture)}; hunt gate {huntGate.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
var results = new List<GameResult>();
var recorders = new List<(GameResult, SliceRecorder)>();
var total = Stopwatch.StartNew();
// Each game owns its state: its GameState and its two controllers' generators (the openswr controller draws nothing, the
// original AI gets its own UiRandom), so games are independent and may run at once. Process-wide UiRandom.Shared is not drawn by
// a match (checked at the end). The shared definitions are read-only; their Index maps are built once here, before any game.
definitions.Resolve(Refs.Stormtroopers);
var jobs = new List<(int Game, int Seed, Owner Side)>();
for (int g = 0; g < games; g++)
{
    int s = seed is { } first ? first + g : Match.DefaultSeeds[g % Match.DefaultSeeds.Length];
    foreach (var side in new[] { Owner.Alliance, Owner.Empire }) jobs.Add((jobs.Count + 1, s, side));
}
// one game: its text and CSV lines are buffered, then written in game order
(GameResult Result, SliceRecorder? Recorder, string Text, List<string> Csv) Run((int Game, int Seed, Owner Side) job)
{
    var text = new System.Text.StringBuilder();
    var lines = new List<string>();
    text.AppendLine($"game {job.Game}: seed {job.Seed}, galaxy {galaxy}, difficulty {difficulty}, openswr plays {job.Side}, player settings column: {Match.PlayerSide}");
    var recorder = slicesCsv is null ? null : new SliceRecorder();
    var r = Match.Play(definitions, job.Game, job.Seed, job.Side, galaxy, difficulty, days, features: features, yardRatio: yardRatio, huntGate: huntGate, slices: recorder, row: c =>
    {
        text.AppendLine(Match.Row(c));
        if (output is not null) lines.AddRange(Match.Csv(c));
    });
    text.AppendLine($"  result: {r.Result} (day {r.Day}, openswr share {r.OpenSWRShare:P1}, {r.Wall.TotalSeconds:F1} s)");
    text.AppendLine(Match.TickLine(r));
    text.AppendLine("  openswr missions: " + (r.Missions.Count == 0 ? "none"
        : string.Join(", ", r.Missions.Select(m => $"{Match.MissionName(m.Key)} {m.Value.Started} ({m.Value.Succeeded} ok / {m.Value.Failed} failed / {m.Value.Ended} foiled or ended early)"))));
    return (r, recorder, text.ToString(), lines);
}
void Emit((GameResult Result, SliceRecorder? Recorder, string Text, List<string> Csv) done)
{
    Console.Write(done.Text);
    if (output is not null) foreach (var line in done.Csv) output.WriteLine(line);
    results.Add(done.Result);
    if (slicesOutput is not null) foreach (var line in SliceRecorder.Csv(done.Result)) slicesOutput.WriteLine(line);
    if (done.Recorder is not null) recorders.Add((done.Result, done.Recorder));
}
if (parallel == 1)
    foreach (var job in jobs) Emit(Run(job));
else
{
    // up to `parallel` games at once; each finished game is written as soon as every earlier one is
    using var gate = new SemaphoreSlim(parallel);
    var tasks = jobs.Select(job => Task.Run(async () =>
    {
        await gate.WaitAsync();
        try { return Run(job); }
        finally { gate.Release(); }
    })).ToList();
    foreach (var task in tasks) Emit(task.Result);
}
if (UiRandom.Shared.State != -1) Console.Error.WriteLine("warning: the process-wide UiRandom was drawn; parallel games may then differ from a serial run");
Console.WriteLine();
Console.Write(Match.Summary(results));
Console.Write(Match.TickTable(results));
Console.Write(MaintenanceProbe.Table(results));
Console.Write(ShipTypes.Table(results));
Console.Write(Retakes.Table(results));
if (slicesCsv is not null)
{
    Console.WriteLine();
    Console.Write(SliceRecorder.DiffTable(results));
    Console.WriteLine();
    Console.Write(SliceRecorder.ReasonTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.RefusalTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.IntelTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.AgentsTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.RespondTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.NoLaunchTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.TroopRoomTable(recorders));
    Console.WriteLine();
    Console.Write(SliceRecorder.UprisingAgeTable(results));
    Console.WriteLine();
    Console.Write(UprisingRecorder.Table([.. recorders.Select(x => (x.Item1, x.Item2.Uprisings))]));
    Console.WriteLine();
    Console.Write(BombardRecorder.Table([.. recorders.Select(x => (x.Item1, x.Item2.Bombard))]));
    Console.Write(BombardRecorder.AssaultTable([.. recorders.Select(x => (x.Item1, x.Item2.Bombard))]));
    Console.Write(BombardRecorder.DetailTable([.. recorders.Select(x => (x.Item1, x.Item2.Bombard))]));
    Console.WriteLine();
    Console.Write(ObjectivesRecorder.Table([.. recorders.Select(x => (x.Item1, x.Item2.Objectives))]));
    Console.WriteLine();
    Console.Write(FleetShapeRecorder.Table([.. recorders.Select(x => (x.Item1, x.Item2.FleetShape))]));
    Console.WriteLine();
    Console.Write(IdleProbeRecorder.Table([.. recorders.Select(x => (x.Item1, x.Item2.IdleProbes))], definitions));
    Console.WriteLine();
    Console.Write(AgentRecorder.Tables([.. recorders.Select(x => (x.Item1, x.Item2.Agents))], type => TypeName(definitions, type)));
    Console.WriteLine();
    Console.Write(CharacterRecorder.Tables([.. recorders.Select(x => (x.Item1, x.Item2.Characters))]));
}
Console.WriteLine($"total wall time {total.Elapsed.TotalSeconds:F1} s");
return 0;

// Special-force type by its ref, the mission kinds that accept it (from the masks), skills and maintenance
static string TypeName(OpenSWR.Data.Dat.GameDefinitions d, uint type)
{
    var f = d.SpecialForces.FirstOrDefault(x => x.Common.Ref == type);
    if (f is null) return $"{Family.Of(type)}:{SdRef.IndexOf(type)}";
    var kinds = d.Missions.Where(r => !r.Unlisted && f.MissionMask != 0 && r.AllowsSpecialForce(f.MissionMask)).Select(r => OpenSWR.Sim.Family.Of(r.Common.Ref)).Distinct().Order()
        .Select(k => Match.MissionName(k)[..3]);
    return $"{Family.Of(type)}:{SdRef.IndexOf(type)} {string.Join("+", kinds)} e{f.Skills.Espionage.Base}c{f.Skills.Combat.Base}m{f.MaintenanceCost}";
}
