using System.Text.Json;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>
/// Replays the recorded command streams of three traced runs (tools/frida/rng_report.py --commands) into the sim
/// and compares the day draws (make_vector.py --days). Run 1 (seed 874334749): no player orders, player Empire, days 1-73.
/// Run 2 (seed 401859171): player Empire orders, days 1-33. Run 3 (seed 532947196): player Alliance missions,
/// days 1-62.
/// </summary>
public class CommandReplayTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    sealed record Record(JsonElement Json)
    {
        public int Day => Json.GetProperty("day").GetInt32();
        public int N => Json.GetProperty("n").GetInt32();
        public int Type => Hex(Json.GetProperty("type"));
        public bool Ok => Json.GetProperty("ok").ValueKind == JsonValueKind.True;
        public static int Hex(JsonElement e) => (int)Convert.ToUInt32(e.GetString(), 16);
    }

    /// <summary>
    /// The sim after replaying a run. Object references are the original ones: the sim's ids follow the original's
    /// counter, fixed and placeholder ids, so every recorded SdRef resolves directly.
    /// </summary>
    sealed class Replay
    {
        public required GameState Game;
        public readonly Dictionary<uint, GameObject> Map = [];
        public readonly List<(string Site, int N, int Result)> Draws = [];
        public readonly List<TimerRecord> Timers = [];
        public int SetupDraws;
        public readonly List<string> Log = [];
        public readonly List<Fact> Facts = [];
        public readonly Dictionary<int, (OrderResult Result, int SideDemandAfter, int RefinedAfter, int RefinedBefore)> Results = [];
        public readonly List<Record> Records = [];
        public readonly List<(int Day, string Site, int N, int Result)> Expected = [];
        /// <summary>Days after which the game is saved and replaced by its loaded copy (<see cref="Reload"/>).</summary>
        public Func<int, bool> SaveAfter = _ => false;
        /// <summary>Called after each recorded command ran (with its queued events).</summary>
        public Action<Record>? AfterRecord;

        public GameObject? Find(uint orig) => Map.GetValueOrDefault(orig) ?? Game.Created.LastOrDefault(o => o.Ref == orig);
    }

    static Replay Run(int seed, Owner player, int? throughDay = null, Func<int, bool>? saveAfter = null, string? key = null, Action<Replay, Record>? afterRecord = null)
    {
        key ??= seed.ToString();
        string dir = Path.Combine(AppContext.BaseDirectory, "Vectors");
        using var days = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, $"days-{key}.json")));
        var settings = new GameSettings(player, 1, 0, 0, seed); // Novice, standard galaxy
        var r = new Replay { Game = null! };
        if (saveAfter is not null) r.SaveAfter = saveAfter;
        if (afterRecord is not null) r.AfterRecord = rec => afterRecord(r, rec);
        r.Game = NewGame.Create(settings, Definitions, r.Draws, r.Timers);
        r.SetupDraws = r.Draws.Count;
        r.Game.FactTrace = r.Facts;
        var game = r.Game;
        foreach (var d in days.RootElement.GetProperty("draws").EnumerateArray())
            if (d[2].GetInt32() > 1) r.Expected.Add((d[0].GetInt32(), d[1].GetString()!, d[2].GetInt32(), d[3].GetInt32()));
        foreach (var l in File.ReadLines(Path.Combine(dir, $"commands-{key}.jsonl")))
            r.Records.Add(new Record(JsonDocument.Parse(l).RootElement.Clone()));

        Index(r);
        // the ids agree: the first step-23 timer 0x394 target is the same object
        uint anchor = days.RootElement.GetProperty("timers").EnumerateArray().First(t => t[1].GetInt32() == 23 && t[3].GetInt32() == 0x394)[4].GetUInt32();
        Assert.Equal(anchor, r.Timers.First(t => t.Type == 0x394).Target);

        game.Sides[player].Speed = Clock.TicksForSpeed(4);
        Advance(r, throughDay ?? r.Expected.Max(e => e.Day));
        return r;
    }

    static void Index(Replay r)
    {
        r.Map.Clear();
        foreach (var s in r.Game.Systems)
        {
            r.Map[s.Ref] = s;
            foreach (var m in s.Managers) r.Map[m.Ref] = m;
        }
        foreach (var c in r.Game.Characters) r.Map[c.Ref] = c;
        foreach (var o in r.Game.Created) r.Map[o.Ref] = o;
    }

    /// <summary>Runs the days after the current one through <paramref name="last"/>, each day's recorded commands after its events.</summary>
    static void Advance(Replay r, int last)
    {
        for (int day = r.Game.Day + 1; day <= last; day++)
        {
            r.Game.Clock.RunUntilDay(r.Game, day);
            foreach (var rec in r.Records.Where(x => x.Day == day))
            {
                Execute(r, rec);
                r.AfterRecord?.Invoke(rec);
            }
            if (r.SaveAfter(day)) Reload(r);
        }
    }

    /// <summary>Saves the game to memory and continues the replay with the loaded copy (the traces keep collecting).</summary>
    static void Reload(Replay r)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(r.Game, stream);
        stream.Position = 0;
        var game = SaveGame.Load(stream, Definitions);
        game.FactTrace = r.Facts;
        game.Random.Trace = r.Draws;
        game.Clock.TimerTrace = r.Timers;
        r.Game = game;
        Index(r);
    }

    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    static List<GameObject>? Objects(Replay r, Record rec, JsonElement list, List<string> log)
    {
        var result = new List<GameObject>();
        foreach (var t in list.EnumerateArray())
        {
            uint orig = Convert.ToUInt32(t[0].GetString(), 16);
            if (r.Find(orig) is not { } o) { log.Add($"#{rec.N} day {rec.Day} {rec.Type:X}: {orig:X8} unknown, skipped"); return null; }
            result.Add(o);
        }
        return result;
    }

    static void Execute(Replay r, Record rec)
    {
        var game = r.Game;
        var j = rec.Json;
        if (rec.Type == 0x280) return; // UI speed message: ticks not recorded, speed 4 kept (day order is unaffected)
        var targetList = j.GetProperty("targets");
        if (Objects(r, rec, targetList, r.Log) is not { } targetObjects) return;
        var targets = targetObjects.Select((o, i) => (o, targetList[i][1].GetInt32())).ToList();
        uint argRef = Convert.ToUInt32(j.GetProperty("arg_ref").GetString(), 16);
        GameObject? arg = null;
        if (argRef != 2 && (arg = r.Find(argRef)) is null) { r.Log.Add($"#{rec.N} day {rec.Day} {rec.Type:X}: arg {argRef:X8} unknown, skipped"); return; }
        List<GameObject> decoys = [];
        if (rec.Type is 0x240 && j.GetProperty("events")[0].GetProperty("lists")[1] is var second
            && Objects(r, rec, second, r.Log) is { } d)
            decoys = d;
        var command = new Command(rec.Type, (Owner)j.GetProperty("side").GetInt32())
        {
            Targets = targets, ArgRef = arg, ArgInt = j.GetProperty("arg_int").GetUInt32(), Decoys = decoys,
            Fields = [.. j.GetProperty("fields").EnumerateArray().Select(f => f.GetUInt32())],
        };
        // the recorded list also holds owner changes of neutral systems' contents (0x302 with old owner 3): compare only the
        // objects that did not exist before the command.
        var origCreated = j.GetProperty("created").EnumerateArray().Select(e => Convert.ToUInt32(e.GetString(), 16)).Where(x => r.Find(x) is null).ToList();
        int created = game.Created.Count, refinedBefore = game.Sides[command.Side].RefinedStock;
        var result = Orders.Execute(game, command);
        while (game.Clock.DispatchOne(game)) { }
        r.Results[rec.N] = (result, game.Sides[command.Side].Demand, game.Sides[command.Side].RefinedStock, refinedBefore);
        if (result.Success != rec.Ok) r.Log.Add($"#{rec.N} day {rec.Day} {rec.Type:X}: sim result {result}, recorded ok={rec.Ok}");
        var simCreated = game.Created.Skip(created).Select(o => o.Ref).ToList();
        if (!origCreated.SequenceEqual(simCreated))
            r.Log.Add($"#{rec.N} day {rec.Day} {rec.Type:X}: created [{string.Join(", ", simCreated.Select(x => x.ToString("X8")))}], recorded [{string.Join(", ", origCreated.Select(x => x.ToString("X8")))}]");
        foreach (var o in game.Created.Skip(created)) r.Map.TryAdd(o.Ref, o);
    }
    /// <summary>First index where the sim's day draws (n &gt; 1) differ from the trace's.</summary>
    int Compare(Replay r, string run)
    {
        var actual = r.Draws.Skip(r.SetupDraws).Where(d => d.N > 1).ToList();
        int i = 0;
        while (i < r.Expected.Count && i < actual.Count && (r.Expected[i].Site, r.Expected[i].N, r.Expected[i].Result) == actual[i]) i++;
        foreach (var l in r.Log) output.WriteLine(l);
        output.WriteLine($"{run}: {r.Expected.Count} expected day draws, {actual.Count} actual, {i} match");
        if (i < Math.Max(r.Expected.Count, actual.Count))
            output.WriteLine($"first divergence at {i}: expected " + (i < r.Expected.Count ? $"day {r.Expected[i].Day} {r.Expected[i].Site} n={r.Expected[i].N} -> {r.Expected[i].Result}" : "(end)")
                + " | actual " + (i < actual.Count ? $"{actual[i].Site} n={actual[i].N} -> {actual[i].Result}" : "(end)"));
        for (int j = Math.Max(0, i - 3); j < Math.Min(i + 6, r.Expected.Count); j++)
            output.WriteLine($"  #{j} day {r.Expected[j].Day} {r.Expected[j].Site} n={r.Expected[j].N} -> {r.Expected[j].Result}"
                + (j < actual.Count ? $" | {actual[j].Site} n={actual[j].N} -> {actual[j].Result}" : ""));
        return i;
    }

    /// <summary>
    /// Day draws match the trace at least up to the recorded depth (regression guard); the first divergence of each run and
    /// its cause are listed in the test output.
    /// </summary>
    [GameTheory]
    [InlineData(874334749, Owner.Empire, 113)]   // all
    [InlineData(401859171, Owner.Empire, 191)]   // all
    [InlineData(532947196, Owner.Alliance, 359)] // all
    public void DayDrawsMatchTheTrace(int seed, Owner player, int depth)
    {
        var r = Run(seed, player);
        Assert.True(Compare(r, $"seed {seed}") >= depth);
        Assert.DoesNotContain(r.Log, l => l.Contains("sim result")); // every recorded command was accepted
    }

    /// <summary>Player orders of run 2.</summary>
    [GameFact]
    public void PlayerOrdersOfRun2()
    {
        // the yard 0x2A00094C: final move after record #32 (the replacing 0x210, tick 89), deleted by the side-2 0x205 of #33 (tick 90)
        var yard = new Dictionary<int, (bool Staged, bool Deleted)>();
        var r = Run(401859171, Owner.Empire, throughDay: 18, afterRecord: (r, rec) =>
        {
            if (rec.N is 32 or 33 && r.Find(0x2A00094C) is { } x) yard[rec.N] = (x.Container == r.Game.Staging || x.Deleted, x.Deleted);
        });
        var game = r.Game;
        foreach (var l in r.Log) output.WriteLine(l);
        // Day 2: construction yard ordered at Coruscant's construction manager; demand 452 -> 462
        var manager = game.System(Refs.Coruscant).Managers[(int)ManufacturingKind.Construction];
        Assert.Same(manager, r.Find(0xA0000625));
        // cancelled day 3, final move 0x372. A queued unit is Created and its owner's view is live, so the deletion
        // waits for the Empire's 0x205; the Alliance view is fresh and takes Destroyed silently
        Assert.Equal((true, false), yard[32]);
        Assert.Equal((true, true), yard[33]);
        var yardObject = r.Find(0x2A00094C)!;
        Assert.Equal(462, r.Results[20].SideDemandAfter);
        // Day 3: GenCore replaces the yard: cancelled with reason 0x16, no refund; demand 464
        Assert.Equal((0x16, true), (yardObject.DestroyedReason, yardObject.Destroyed));
        var genCore = r.Find(0x24000951)!;
        Assert.Equal(0x24000003u, genCore.Type);
        Assert.Equal(464, r.Results[32].SideDemandAfter);
        // Day 5: Fleet 1 from Coruscant [system, mode 0x10] to 0x9000010D: travel 13 days, arrival day 18
        var fleet = r.Find(0x0800077A)!;
        var start = r.Timers.Single(t => t is { Type: 0x387, Fire: false } && t.Target == fleet.Ref);
        Assert.Equal((5, 13, 18), (start.Day, start.Base, start.FireDay));
        Assert.Contains(r.Timers, t => t is { Type: 0x387, Fire: true, Day: 18 } && t.Target == fleet.Ref);
        // Day 8: Refinery scrap refunds RefinedMaterialCost div 2 = 10
        var refinery = r.Find(0x2D000659)!;
        Assert.Equal((0x14, true), (refinery.DestroyedReason, refinery.Destroyed));
        Assert.Equal(10, game.RefinedMaterialCost(refinery.Type) / 2);
        output.WriteLine($"refined stock around the scrap: {r.Results[87].RefinedBefore} → {r.Results[87].RefinedAfter} (trace: 2 → 12)");
        Assert.Equal(r.Results[87].RefinedBefore + 10, r.Results[87].RefinedAfter);
        // Setup placeholders P = 0x7B7: system 0x9000008C (k = 0) reserve 0x080007B7, new-fleet 0x080007B9
        var system = game.System(0x9000008C);
        Assert.Equal((true, system), (((Fleet)r.Find(0x080007B7)!).Reserve, r.Find(0x080007B7)!.Container));
        Assert.Equal(0x08000949u, r.Find(0x08000949)!.Ref); // day-2 replacement placeholder of 0x9200011F (0x270)
        // Fleet 1 arrives at 0x9000010D on day 18: EnrouteActive cleared, Usable, Blockade of the system set
        Assert.Equal((false, true), (fleet.EnrouteActive, fleet.Usable));
        Assert.True(game.System(0x9000010D).Blockade);
    }

    /// <summary>
    /// Run 2 day 4: the 0x211 at 0xA2000500 creates ship 0x14000952 (Existing, not Completed) in the reserve
    /// placeholder 0x080007B7, which becomes a real fleet at order time: Existing and Completed, not Usable; no replacement reserve.
    /// </summary>
    [GameFact]
    public void ReserveFleetTurnsRealAtOrderTime()
    {
        var r = Run(401859171, Owner.Empire, throughDay: 4);
        var ship = r.Find(0x14000952)!;
        var fleet = (Fleet)r.Find(0x080007B7)!;
        Assert.Same(fleet, ship.Container);
        Assert.Equal((true, false), (ship.Existing, ship.Completed));
        Assert.Equal((true, true, true, true, false),
            (fleet.Real, fleet.Reserve, fleet.Existing, fleet.Completed, fleet.Usable));
        Assert.Single(fleet.Container!.Contents.OfType<Fleet>(), f => f.Owner == fleet.Owner && f.Reserve);
    }

    /// <summary>Run 3: Leia's recruitment at 0x92000121 (p = 55, success day 16 with r = 50, Leadership 70 -> 71).</summary>
    [GameFact]
    public void LeiaRecruitsInRun3()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 15);
        var mission = (Mission)r.Find(0x55000965)!;
        var leia = (Character)r.Find(0x31000241)!;
        Assert.Equal((8, 70), (mission.Phase, Missions.Enhanced(r.Game, leia, SkillIndex.Leadership)));
        Assert.Equal(55, Missions.Probability(r.Game, mission, leia));
        Advance(r, 16);
        Assert.Equal((3, 11, 71), (mission.TaskStatus, mission.Phase, leia.Skills[SkillIndex.Leadership]));
        Assert.Equal((Owner.Alliance, r.Game.System(0x92000121)), (mission.Recruited!.Owner, mission.Recruited.Container));
    }

    /// <summary>
    /// Run 3 day 17: the Empire diplomacy at 0x9000010B and its sector wave change the owners of 0x9000010B,
    /// 0x90000108 and 0x9000010D 3 -> 2 (control: owner, support and military 3, 3, 0 -> 2, 2, 0) and 0x9000010A's control 1, 1, 1 -> 1, 3, 1 (owner 1 kept by troops).
    /// </summary>
    [GameFact]
    public void SupportWaveOfRun3()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 17);
        foreach (uint s in new uint[] { 0x9000010B, 0x90000108, 0x9000010D })
            Assert.Equal((Owner.Empire, new SystemControl(Owner.Empire, Owner.Empire, Owner.None)), (r.Game.System(s).Owner, r.Game.System(s).Control));
        Assert.Equal((Owner.Alliance, new SystemControl(Owner.Alliance, Owner.Neutral, Owner.Alliance)), (r.Game.System(0x9000010A).Owner, r.Game.System(0x9000010A).Control));
    }

    /// <summary>
    /// Every CompletionStatus is stored through a virtual setter, which consumes one command serial (statuses 1 and
    /// 5 included); with a status already set the phase check does nothing.
    /// </summary>
    [GameFact]
    public void CompletionStatusConsumesASerial()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        Mission Move()
        {
            var c = game.Characters.First(c => c.Owner == Owner.Alliance && c.Usable && c.Mission is null && c.System is { Owner: Owner.Alliance });
            return Missions.Create(game, 0x41000001, Owner.Alliance, c.System!, [c], [], 0, mandatory: false);
        }
        var done = Move();
        done.Phase = 11;
        uint serial = game.Clock.Serial;
        Missions.OwnerChanged(game, done.Objective!);
        Assert.Equal((1, serial + 1), (done.CompletionStatus, game.Clock.Serial));
        Missions.OwnerChanged(game, done.Objective!);
        Assert.Equal(serial + 1, game.Clock.Serial);

        var empty = Move();
        empty.Team[0].ResignRequest = true;
        serial = game.Clock.Serial;
        Missions.OwnerChanged(game, empty.Objective!);
        Assert.Equal((5, 11, serial + 1), (empty.CompletionStatus, empty.Phase, game.Clock.Serial));
    }

    /// <summary>
    /// Run 3 day 39 (Han + Chewbacca espionage at Coruscant): foil p = 35 for inputs [95, 0, 2, 10 | 12, 0, 1];
    /// decoy p 96 / 96 / 95 for key 90 - D (D = 10, 1, 12); Han's injury check from the traced generator state: chance(1) r = 0,
    /// then roll(1) = 0, roll(29) = 24, + 1 -> Injury 25.
    /// </summary>
    [GameFact]
    public void EncounterFormulasOfRun3()
    {
        var game = new GameState(new GameSettings(Owner.Alliance, 1, 0, 0, 1570241797), Definitions);
        Assert.Equal(35, Missions.FoilChance(game, 95, 0, 10, 0));
        Assert.Equal(35, Missions.FoilChance(game, 95, 0, 12, 0));
        Assert.Equal([96, 96, 95], new[] { 10, 1, 12 }.Select(d => Missions.DecoyChance(game, 90, 0, d, fleet: true)));
        var han = new Character(Definitions.MajorCharacters.First(c => c.Common.Ref == 0x33000243), Owner.Alliance);
        han.Skills[SkillIndex.Combat] = 100;
        var draws = new List<(string, int, int)>();
        game.Random.Trace = draws;
        Missions.InjuryCheck(game, han);
        Assert.Equal(25, han.Injury);
        Assert.Equal([(DrawSite.Injury, 100, 0), (DrawSite.Injury, 2, 0), (DrawSite.Injury, 30, 24)], draws);
    }

    /// <summary>
    /// Run 3: the foiled espionage at Coruscant (day 39) sends Han + Chewbacca home on Return mission 0x42000988
    /// to Yavin (ParentAtMissionKey; Han leader, Chewbacca decoy, encounter codes 0, 1, 1, 4), 32 days at Han's speed 50 (arrival day 71);
    /// the probe droids' reconnaissance (day 50) returns on 0x42000993 to Coruscant, a lone special force leading, 42 days.
    /// </summary>
    [GameFact]
    public void ReturnMissionsOfRun3()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 50);
        var han = (Character)r.Find(Refs.Han)!;
        var chewbacca = (Character)r.Find(0x38000343)!;
        var back = r.Game.Missions.Single(m => m.Ref == 0x42000988);
        Assert.Equal((Refs.Yavin, Refs.Coruscant, new EncounterCodes(0, 1, 1, 4)), (back.TargetKey!.Ref, back.Origin!.Ref, back.Encounters));
        Assert.Equal([han], back.Team);
        Assert.Equal([chewbacca], back.Decoys);
        Assert.Equal((50, 71, 71), (han.MissionHyperdriveModifier, han.Eta, chewbacca.Eta));
        Assert.Equal(25, han.Injury);
        var probes = r.Game.Missions.Single(m => m.Ref == 0x42000993);
        Assert.Equal((Refs.Coruscant, 0x3C0007C1u, 92), (probes.TargetKey!.Ref, probes.Leader!.Ref, probes.Leader.Eta));
    }

    /// <summary>
    /// Run 3 day 58: the Empire assault (0x234) on neutral 0x90000111 lands all 4 troops (limit 6), the owner becomes
    /// the Empire, Garrisoned 0 -> 1 costs 10 support (Empire 50 -> 40, ProductionModifier 250), the recon 0x54000960 ends with
    /// CompletionStatus 8, the Blockade clears.
    /// </summary>
    [GameFact]
    public void AssaultOfRun3()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 58);
        var s = r.Game.System(0x90000111);
        Assert.Equal((Owner.Empire, 60, new SystemControl(Owner.Empire, Owner.Alliance, Owner.Empire), 250, false), (s.Owner, s.Support, s.Control, s.ProductionModifier, s.Blockade));
        Assert.Equal([0x10000778u, 0x100007AFu, 0x100007B3u, 0x100007B9u], s.Contents.Where(x => Family.IsTroop(x.Type)).Select(x => x.Ref));
        var recon = r.Game.Missions.Single(m => m.Ref == 0x54000960);
        Assert.Equal((8, 11), (recon.CompletionStatus, recon.Phase));
        Assert.Equal((true, false), (((Fleet)r.Find(0x0800095A)!).Real, ((Fleet)r.Find(0x0800095A)!).Assault));
    }

    /// <summary>
    /// State events of the replayed runs (rng_report.py --facts, <see cref="Fact"/>) equal the trace's, in order, on every replayed
    /// day (the replayed days end with the last traced draw): notification runs 0x300 Existing, 0x301 Usable, 0x302 owner (old 3 =
    /// creation), 0x306 HyperdriveActive, 0x308 Completed, 0x30C / 0x30D mission ready / phase, 0x320 parent, 0x321 agent mission;
    /// mission TaskStatus / CompletionStatus changes (run 3 only: the run 1 and 2 traces predate those hooks); travel timer 0x387
    /// starts (base, fire day). No known mismatch: every kind is compared in full.
    /// </summary>
    [GameTheory]
    [InlineData(874334749, Owner.Empire)]
    [InlineData(401859171, Owner.Empire)]
    [InlineData(532947196, Owner.Alliance)]
    public void FactsMatchTheTrace(int seed, Owner player) => AssertFacts(Run(seed, player), seed);

    /// <summary>
    /// Save games: the replays saved and loaded after every day (a fresh <see cref="GameState"/> from the save each time) give the
    /// same day draws and state events as the uninterrupted ones.
    /// </summary>
    [GameTheory]
    [InlineData(874334749, Owner.Empire, 113)]
    [InlineData(401859171, Owner.Empire, 191)]
    [InlineData(532947196, Owner.Alliance, 359)]
    public void SavedAndLoadedReplayMatchesTheTrace(int seed, Owner player, int depth)
    {
        var r = Run(seed, player, saveAfter: _ => true);
        Assert.True(Compare(r, $"seed {seed}, reloaded daily") >= depth);
        Assert.DoesNotContain(r.Log, l => l.Contains("sim result"));
        AssertFacts(r, seed);
    }

    /// <summary>
    /// Run 9 (seed 532947196, player Alliance, no orders, unhooked AI): both sides' recorded commands replayed
    /// through day 163 (the day before the first traced automatic battle, Gamor day 164). All 5514 facts and all 887 day draws
    /// match (the last, day 163 n = 8, needs the Usable hook order: the Mine's 0x301 before its 0x315).
    /// </summary>
    [GameFact]
    public void Run9MatchesTheTraceToDay163()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 163, key: "532947196-run9");
        r.Expected.RemoveAll(e => e.Day > 163);
        Assert.Equal(887, Compare(r, "run 9"));
        AssertFacts(r, "532947196-run9");
        Assert.Empty(r.Log);
    }

    /// <summary>
    /// The AI-hooked runs 4-8, 10, 11 (AiReplayTests) replayed like run 9: both sides' recorded commands, no controllers, through
    /// the last traced day. Every day draw (n &gt; 1) and every fact matches, every recorded command is accepted with the recorded ids.
    /// </summary>
    [GameTheory]
    [InlineData("22611212", 22611212, Owner.Empire, 92, 319)]                // run 4, days 1-31
    [InlineData("420082270", 420082270, Owner.Empire, 108, 226)]             // run 5, days 1-11
    [InlineData("99647702", 99647702, Owner.Alliance, 166, 658)]             // run 6, days 1-41
    [InlineData("874334749-forced", 874334749, Owner.Empire, 45, 217)]       // run 7, days 1-11
    [InlineData("807887694", 807887694, Owner.Alliance, 557, 2998)]          // run 8, days 1-165
    [InlineData("532947196-run10", 532947196, Owner.Alliance, 1143, 6454)]   // run 10, days 1-180 (day-62 assault on 0x90000111)
    [InlineData("532947196-run11", 532947196, Owner.Alliance, 139, 386)]     // run 11, days 1-10
    public void AiRunsMatchTheTrace(string key, int seed, Owner player, int draws, int facts)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "Vectors");
        using var days = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, $"days-{key}.json")));
        using var factDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, $"facts-{key}.json")));
        int last = Math.Max(days.RootElement.GetProperty("draws").EnumerateArray().Max(d => d[0].GetInt32()),
            factDoc.RootElement.GetProperty("facts").EnumerateArray().Max(f => f[0].GetInt32()));
        var r = Run(seed, player, throughDay: last, key: key);
        Assert.Equal(draws, Compare(r, key));
        Assert.Equal(draws, r.Draws.Skip(r.SetupDraws).Count(d => d.N > 1));
        Assert.Equal(facts, AssertFacts(r, key));
        Assert.Empty(r.Log);
    }

    /// <summary>
    /// Run 13 (run 3's galaxy, player Alliance, all AI hooks) like <see cref="AiRunsMatchTheTrace"/>, both battles
    /// (Gamor day 231, 0x90000094 day 293) hosted by the engine: all 1927 day draws through day 299 and all facts (day 243: the
    /// abduction's grouped members leave in ascending SdRef, and the capture's 0x318 refreshes the Alliance view
    /// of 0x33000243 with its 0x321 2 -> 0x42000A89; day 293: the loser fleets' 0x307 at priority 0x300, each
    /// fleet's move notifications run before the next fleet's autoroute). With the 32-bit float arithmetic the
    /// engine's day-293 C records equal the recorded ones (0x140007AE ShieldCH 1), so through the last traced day 301
    /// all 1937 day draws (day 300: the repair picks among one critical hit) and all 13040 facts match.
    /// </summary>
    [GameFact]
    public void Run13MatchesTheTraceToDay301()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 301, key: "532947196-run13");
        Assert.Equal(1937, Compare(r, "run 13"));
        Assert.Equal(1937, r.Draws.Skip(r.SetupDraws).Count(d => d.N > 1));
        Assert.Equal(13040, AssertFacts(r, "532947196-run13"));
        Assert.Empty(r.Log);
    }

    const string Run13Trace = "rng-20261020-181346.jsonl";

    /// <summary>
    /// Run 13 with the recorded results of both battles (days 231 and 293, <see cref="RecordedBattle"/>): through the last traced
    /// day 301 every day draw (1937, the day-300 repair picks among one critical hit) and every fact (13040) match.
    /// </summary>
    [GameFact("OPENSWR_TRACE_DIR")]
    public void Run13BattlesRecordedMatchTheTraceToDay301()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 230, key: "532947196-run13");
        var days = new List<int>();
        r.Game.BattleResults = _ => { days.Add(r.Game.Day); return RecordedBattle(r, Run13Trace, r.Game.Day); };
        Advance(r, 301);
        Assert.Equal([231, 293], days);
        Assert.Equal(1937, Compare(r, "run 13"));
        Assert.Equal(1937, r.Draws.Skip(r.SetupDraws).Count(d => d.N > 1));
        Assert.Equal(13040, AssertFacts(r, "532947196-run13"));
        Assert.Empty(r.Log);
    }

    /// <summary>
    /// Run 13's two engine battles (check 1 and the draw count): the carried state of the Alliance ships entering
    /// day 293 (hull damage, critical-hit counters from the day-231 C records) and the engine's battle-generator draws
    /// per battle (trace: day 231 119, day 293 143).
    /// </summary>
    [GameFact("OPENSWR_TRACE_DIR")]
    public void Run13EngineBattles()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 230, key: "532947196-run13");
        var draws = new Dictionary<int, List<(string, int, int)>>();
        var setup = new List<string>();
        r.Game.BattleResults = s =>
        {
            foreach (var ship in s.Contents.OfType<Fleet>().SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type)))
                setup.Add($"day {r.Game.Day} {ship.Ref:X8} owner {ship.Owner} HullDamage {ship.HullDamage} Shield {ship.Critical(0)} Weapon {ship.Critical(1)}"
                    + $" Tractor {ship.Critical(2)} Speed {ship.Critical(3)} hyper {ship.Critical(4)}/{ship.Critical(5)}");
            BattleEngine.Trace = draws[r.Game.Day] = [];
            try
            {
                var result = BattleEngine.Run(r.Game, s);
                foreach (var (o, x) in result.Ships) setup.Add($"  day {r.Game.Day} C {o.Ref:X8} {x}");
                return result;
            }
            finally { BattleEngine.Trace = null; }
        };
        Advance(r, 293);
        foreach (var l in setup) output.WriteLine(l);
        foreach (var (day, d) in draws) output.WriteLine($"day {day}: {d.Count} engine draws; " + string.Join(", ", d.Select(x => x.Item1)));
        Assert.Equal([231, 293], draws.Keys);
        // check 1: the day-231 C records (1400079F hull 892, Speed 1, Shield 4, Tractor 2, Weapon 3; 140007A7 Shield 1) enter day 293
        Assert.Contains("day 293 1400079F owner Alliance HullDamage 308 Shield 4 Weapon 3 Tractor 2 Speed 1 hyper 0/0", setup);
        Assert.Contains("day 293 140007A7 owner Alliance HullDamage 0 Shield 1 Weapon 0 Tractor 0 Speed 0 hyper 0/0", setup);
        Assert.Equal(119, draws[231].Count);
        Assert.Equal(143, draws[293].Count); // = trace with the 32-bit float arithmetic
        Assert.DoesNotContain(draws[293], d => d.Item1.StartsWith("1400079F"));
    }

    /// <summary>
    /// Seed 260732950 (player Alliance) with both sides' recorded commands and the battles of days 216 / 229 at Coruscant hosted by
    /// the engine: day 157 the 0x305 research hook (status 0xC) and the facility outcome (shipyards in production destroyed),
    /// day 182 the encounter-code-4 T branch; through day 230 every day draw and fact.
    /// </summary>
    [GameFact]
    public void Seed260732950MatchesTheTraceToDay230()
    {
        var r = Run(260732950, Owner.Alliance, throughDay: 230, key: "260732950");
        Assert.Equal(989, Compare(r, "seed 260732950"));
        Assert.Equal(989, r.Draws.Skip(r.SetupDraws).Count(d => d.N > 1));
        Assert.Equal(4816, AssertFacts(r, "260732950"));
        Assert.Empty(r.Log);
    }

    /// <summary>
    /// Run 14, seed 421765374 (player Empire): both sides' recorded commands through day 215 match every day draw and
    /// fact (resolver counts, day 187).
    /// </summary>
    [GameFact]
    public void Seed421765374MatchesTheTraceToDay215()
    {
        var r = Run(421765374, Owner.Empire, throughDay: 215, key: "421765374");
        Assert.Equal(1251, Compare(r, "seed 421765374"));
        Assert.Equal(1251, r.Draws.Skip(r.SetupDraws).Count(d => d.N > 1));
        Assert.Equal(8386, AssertFacts(r, "421765374"));
        Assert.Empty(r.Log);
    }

    /// <summary>
    /// Seed 260732950: the two engine battles at Coruscant 0x90000109 against the per-application
    /// vector (rng_report.py --battles). Day 216: 428 applications (unit, state, d, shield / hull before and after as float32, draws,
    /// critical-hit kinds), 396 draws, S, every C (truncated and float hull) and F record. Day 229 (0x14000790 enters with the
    /// original's ShieldCH 4 from the strategic replay): 5 applications, 4 draws, S, C, F.
    /// </summary>
    [GameFact]
    public void Seed260732950EngineBattles()
    {
        var r = Run(260732950, Owner.Alliance, throughDay: 215, key: "260732950");
        var apps = new Dictionary<int, List<BattleEngine.Application>>();
        var draws = new Dictionary<int, List<(string, int, int)>>();
        var results = new Dictionary<int, BattleResult>();
        r.Game.BattleResults = s =>
        {
            int day = r.Game.Day;
            if (s.Ref != 0x90000109 || day is not (216 or 229)) return null;
            (BattleEngine.DamageTrace, BattleEngine.Trace) = (apps[day] = [], draws[day] = []);
            try { return results[day] = BattleEngine.Run(r.Game, s); }
            finally { (BattleEngine.DamageTrace, BattleEngine.Trace) = (null, null); }
        };
        Advance(r, 229);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Vectors", "battles-260732950.json")));
        var battles = doc.RootElement.GetProperty("battles").EnumerateArray().ToList();
        Assert.Equal([216, 229], battles.Select(b => b.GetProperty("day").GetInt32()));
        Assert.Equal([216, 229], results.Keys);
        foreach (var b in battles)
        {
            int day = b.GetProperty("day").GetInt32();
            var result = results[day];
            var rows = b.GetProperty("dmg").EnumerateArray().ToList();
            var actual = apps[day];
            static float F(JsonElement e) => (float)e.GetDouble();
            int match = 0;
            while (match < rows.Count && match < actual.Count)
            {
                var (e, a) = (rows[match], actual[match]);
                var expected = $"{e[0].GetUInt32():X8} st{e[2].GetInt32()} d{F(e[3]):R} [{F(e[4]):R}, {F(e[5]):R}] -> [{F(e[6]):R}, {F(e[7]):R}] draws {e[8].GetInt32()} crits [{string.Join(",", e[9].EnumerateArray().Select(k => k.GetInt32()))}]";
                var got = $"{a.Unit.Ref:X8} st{a.State} d{(float)a.D:R} [{(float)a.Shield0:R}, {(float)a.Hull0:R}] -> [{(float)a.Shield1:R}, {(float)a.Hull1:R}] draws {a.Draws} crits [{string.Join(",", a.Crits)}]";
                if (expected != got) { output.WriteLine($"day {day} row {match + 1}: expected {expected} | actual {got}"); break; }
                match++;
            }
            output.WriteLine($"day {day}: {actual.Count} applications ({rows.Count} recorded, {match} match), {draws[day].Count} draws, S = [{string.Join(", ", result.S)}]");
            Assert.Equal(rows.Count, match);
            Assert.Equal(rows.Count, actual.Count);
            Assert.Equal(b.GetProperty("draws").GetInt32(), draws[day].Count);
            Assert.Equal(b.GetProperty("S").EnumerateArray().Select(x => x.GetInt32()), result.S);
            var keys = new Dictionary<int, GameObject>();
            foreach (var ship in b.GetProperty("ships").EnumerateArray())
            {
                var o = r.Find(ship[0].GetUInt32())!;
                var c = ship[1].EnumerateArray().Select(x => x.GetInt64()).ToList();
                keys[(int)c[0]] = o;
                double hull = ship[2].GetDouble();
                Assert.Equal(new ShipResult((int)Math.Truncate(hull), (int)c[5], (int)c[6], (int)c[7], (int)c[8], (int)c[9]), result.Ships[o]);
                // float hull: the last application's hull after (none: the entry hull); a destroyed ship records 0
                var last = actual.LastOrDefault(a => a.Unit == o);
                float engine = c[10] == 0xB ? 0 : (float)Math.Max(1, last?.Hull1 ?? hull);
                Assert.Equal((float)hull, engine);
            }
            foreach (var f in b.GetProperty("fighters").EnumerateArray())
            {
                var v = f[1].EnumerateArray().Select(x => x.GetInt64()).ToList();
                Assert.Equal(new FighterResult((int)v[6], v[5] != 0 ? keys[(int)v[4]] : null), result.Fighters[r.Find(f[0].GetUInt32())!]);
            }
        }
    }

    const string Run9Trace = "rng-20261018-223709.jsonl";

    /// <summary>
    /// Run 9 through <paramref name="last"/> with the day-164 Gamor battle (0x90000110) hosted by <see cref="BattleEngine"/> or
    /// by the recorded result. Harness order: the recorded commands of the battle's tick (the day's first, tick 820: the 0x212 of
    /// troops 0x10000A13-A15) run before the host's stage 13, as in the trace (cmd_exec seq 75261, battle_flag seq 75363); the
    /// day-granular <see cref="Advance"/> would run them after it.
    /// </summary>
    (Replay R, BattleResult Result, List<(string, int, int)> Draws, List<string> States) Run9Battle(bool recorded, int last)
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 163, key: "532947196-run9");
        var draws = new List<(string, int, int)>();
        var states = new List<string>();
        BattleResult? result = null;
        r.Game.BattleResults = s =>
        {
            var today = r.Records.Where(x => x.Day == r.Game.Day).ToList();
            int first = today.Min(x => x.Json.GetProperty("tick").GetInt32());
            foreach (var rec in today.Where(x => x.Json.GetProperty("tick").GetInt32() == first))
            {
                Execute(r, rec);
                r.Records.Remove(rec);
            }
            (BattleEngine.Trace, BattleEngine.StateTrace) = (draws, states);
            try { return result = recorded ? RecordedBattle(r, Run9Trace, r.Game.Day) : BattleEngine.Run(r.Game, s); }
            finally { (BattleEngine.Trace, BattleEngine.StateTrace) = (null, null); }
        };
        Advance(r, last);
        Assert.NotNull(result);
        r.Expected.RemoveAll(e => e.Day > last);
        output.WriteLine($"battle: {draws.Count} engine draws, S = [{string.Join(", ", result.S)}]");
        foreach (var (o, x) in result.Fighters) output.WriteLine($"  F {o.Ref:X8} {x.Left} {x.Carrier?.Ref:X8}");
        foreach (var (o, x) in result.Ships) output.WriteLine($"  C {o.Ref:X8} {x}");
        return (r, result, draws, states);
    }

    /// <summary>The recorded battle result of a hooked trace: S, F per fighter, C per capital ship of the battle on <paramref name="day"/>.</summary>
    static BattleResult RecordedBattle(Replay r, string file, int day)
    {
        var lines = File.ReadLines(Path.Combine(Environment.GetEnvironmentVariable("OPENSWR_TRACE_DIR")!, file))
            .Where(l => l.Contains($"\"day\":{day}}}") && (l.Contains("\"ev\":\"battle_") || l.Contains("\"ev\":\"apply_")))
            .Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToList();
        int[] S = [];
        var handles = new Dictionary<long, GameObject>();
        var fighters = new List<(GameObject Unit, long[] V)>();
        var ships = new Dictionary<GameObject, ShipResult>();
        GameObject? unit = null;
        string? kind = null;
        static long[] V(JsonElement e) => [.. e.GetProperty("v").EnumerateArray().Select(x => x.GetInt64())];
        foreach (var e in lines)
        {
            string ev = e.GetProperty("ev").GetString()!;
            if (ev == "battle_sides") S = [.. V(e).Select(x => (int)x)];
            else if (ev is "apply_fighter" or "apply_ship" or "apply_fighter_parent")
                (unit, kind) = (r.Find(Convert.ToUInt32(e.GetProperty("unit").GetString(), 16)), ev);
            else if (ev == "battle_fighter" && kind == "apply_fighter") fighters.Add((unit!, V(e)));
            else if (ev == "battle_ship" && kind == "apply_ship")
            {
                var v = V(e);
                handles[v[0]] = unit!;
                ships[unit!] = new ShipResult((int)Math.Truncate(e.GetProperty("f").GetDouble()), (int)v[5], (int)v[6], (int)v[7], (int)v[8], (int)v[9]);
            }
        }
        // F[6] squadrons left; F[5] != 0: aboard the carrier with engine handle F[4]
        var f = fighters.ToDictionary(x => x.Unit, x => new FighterResult((int)x.V[6], x.V[5] != 0 ? handles[x.V[4]] : null));
        return new BattleResult(true, S, f, ships, new Dictionary<GameObject, DeathStarResult>());
    }

    /// <summary>
    /// Run 9 day 164, Gamor (0x90000110), the first traced automatic battle, hosted with the engine live, step by step. 75 battle-generator draws (the trace's rand(100) values) on the units and steps of the table below, the
    /// battle ends in step 17 with S = [1, 0, 0, 1, 0, 0, 0] (the Alliance withdrew); C and F records, 1400079F's hull path. Then
    /// the replay goes on as with the recorded result (<see cref="Run9GamorBattleRecorded"/>).
    /// </summary>
    [GameFact("OPENSWR_TRACE_DIR")]
    public void Run9GamorBattleWithTheEngine()
    {
        var crits = new List<(long Step, GameObject Unit, int K)>();
        BattleEngine.CritTrace = crits;
        (Replay r, BattleResult result, List<(string, int, int)> draws, List<string> states) run;
        try { run = Run9Battle(recorded: false, last: 185); }
        finally { BattleEngine.CritTrace = null; }
        var (r, result, draws, states) = run;
        var expected = File.ReadLines(Path.Combine(Environment.GetEnvironmentVariable("OPENSWR_TRACE_DIR")!, Run9Trace))
            .Where(l => l.Contains("\"gen\":\"0xc5b9b0c\"") && l.Contains("\"ev\":\"rand\""))
            .Select(l => JsonDocument.Parse(l).RootElement).Where(e => e.GetProperty("day").GetInt32() == 164)
            .Select(e => (e.GetProperty("n").GetInt32(), e.GetProperty("result").GetInt32())).ToList();
        Assert.Equal(75, expected.Count);
        Assert.Equal(expected, draws.Select(d => (d.Item2, d.Item3)));
        // Battle table: damaged unit, step, shield / hull draw
        var sites = new List<string>();
        void Add(int step, params string[] x) => sites.AddRange(x.Select(y => $"{y.Split(' ')[0]} {step} {y.Split(' ')[1]}"));
        const string X = "1C0007A3", A = "1400079F", B = "140007A7", T1 = "1C000957", T2 = "1C0007B0", E = "140007AE";
        for (int k = 1; k <= 2; k++) Add(k, $"{X} s", $"{X} h", $"{A} s", $"{T1} h", $"{E} s", $"{T2} h");
        Add(3, $"{X} s", $"{X} h", $"{A} s", $"{E} s", $"{T2} h");
        for (int k = 4; k <= 7; k++) Add(k, $"{A} s", $"{A} s", $"{E} s", $"{T2} h");
        Add(8, $"{A} s", $"{A} s", $"{A} h", $"{E} s", $"{T2} h");
        Add(9, $"{A} s", $"{A} h", $"{A} h", $"{E} s", $"{T2} h");
        for (int k = 10; k <= 16; k++) Add(k, $"{A} h", $"{A} h", $"{E} s", $"{T2} h");
        Add(17, $"{B} s", $"{B} s", $"{E} s", $"{T2} h");
        Assert.Equal(sites, draws.Select(d => d.Item1));
        Assert.Equal(17, states.Count);
        Assert.Equal([1, 0, 0, 1, 0, 0, 0], result.S);
        // 1400079F's hull after steps 8-16
        double Hull(string line) => double.Parse(line[(line.IndexOf($"{A} st") + 1)..].Split(' ')[2][1..], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal([1173.7, 1136.1, 1096.4, 1056.8, 1017.3, 977.9, 938.6, 899.5, 860.4], states.Skip(7).Take(9).Select(l => Math.Round(Hull(l), 1)));
        Assert.Contains(crits, c => c.Step == 1 && c.Unit.Ref == 0x140007AE && c.K == 0);
        Assert.Contains(crits, c => c.Step == 17 && c.Unit.Ref == 0x140007A7 && c.K == 0);
        GameObject U(uint x) => r.Find(x)!;
        Assert.Equal(new ShipResult(860, 1, 4, 0, 3, 3), result.Ships[U(0x1400079F)]);
        Assert.Equal(new ShipResult(500, 0, 1, 0, 0, 0), result.Ships[U(0x140007A7)]);
        Assert.Equal(new ShipResult(1800, 0, 2, 0, 0, 0), result.Ships[U(0x140007AE)]);
        Assert.Equal(new ShipResult(1800, 0, 0, 0, 0, 0), result.Ships[U(0x140007B2)]);
        Assert.Equal(new ShipResult(1200, 0, 0, 0, 0, 0), result.Ships[U(0x140007B8)]);
        Assert.Equal([0, 1, 9, 12, 12, 12], new uint[] { 0x1C0007A3, 0x1C0007B0, 0x1C000957, 0x1C0007B4, 0x1C0007BE, 0x1C0007BA }.Select(x => result.Fighters[U(x)].Left));
        var recorded = RecordedBattle(r, Run9Trace, 164);
        foreach (var (o, x) in recorded.Fighters) Assert.Equal(x, result.Fighters[o]);
        foreach (var (o, x) in recorded.Ships) Assert.Equal(x, result.Ships[o]);
        Assert.Equal(recorded.S, result.S);
        AssertAfterGamor(r);
    }

    /// <summary>
    /// After the Gamor battle: the squadron's 0x320 before the loser's autorouting; fleet 0x0800079E leaves
    /// Gamor for 0x90000094 (flag 0 closes its own system), ETA day 200. Then through day 185 all 1034 day draws and all 6648 facts
    /// match: day 165 the suspended Gamor Mines / Refineries wake with allocation 0, day 168 the
    /// espionage decoy check on the system's own troops, and the strategic repair: 0x140007AE
    /// gets FastRepair at its shipyard system on day 169 and repairs one ShieldCH (2 -> 1) on day 179; 0x1C0007B0 11 -> 3,
    /// 0x1C000957 3 -> 0. Save -> load -> save is byte-equal with the active 0x380 slots.
    /// </summary>
    void AssertAfterGamor(Replay r)
    {
        var gamor = r.Game.System(0x90000110);
        Assert.Equal(Owner.Empire, gamor.BattleWon);
        Assert.Equal([0, 0, 0], gamor.BattleLoss); // battle_loss L after the squadron's 0x300
        var fleet = r.Find(0x0800079E)!;
        Assert.Equal((0x90000094u, 200), (fleet.Destination?.Ref ?? 0, fleet.Eta));
        var squadron = r.Find(0x1C0007A3)!;
        Assert.Equal((true, 0xC), (squadron.Destroyed, squadron.DestroyedReason));
        var ship = r.Find(0x140007AE)!;
        Assert.Equal((1, true, true), (ship.Critical(0), ship.FastRepair, ship.RepairActive));
        Assert.Equal((3, 0), (r.Find(0x1C0007B0)!.SquadSizeDamage, r.Find(0x1C000957)!.SquadSizeDamage));
        Assert.False(r.Find(0x1C000957)!.Damaged);
        Assert.Equal(1034, Compare(r, "run 9"));
        Assert.Equal(6648, AssertFacts(r, "532947196-run9"));
        var first = Save(r.Game);
        var loaded = SaveGame.Load(new MemoryStream(first), Definitions);
        Assert.True(loaded.Timers[(0x380, 0x140007AE)].Active);
        Assert.Equal(first, Save(loaded));
    }

    /// <summary>Run 9 with the recorded Gamor result (from the trace's battle_* / apply_* records) applied, through day 185.</summary>
    [GameFact("OPENSWR_TRACE_DIR")]
    public void Run9GamorBattleRecorded()
    {
        var (r, _, _, _) = Run9Battle(recorded: true, last: 185);
        AssertAfterGamor(r);
    }

    /// <summary>Save -> load -> save gives the same bytes (run 3 day 40: missions, prisoners' Return trip, views, pending timers).</summary>
    [GameFact]
    public void SaveLoadSaveIsByteEqual()
    {
        var r = Run(532947196, Owner.Alliance, throughDay: 40);
        var first = Save(r.Game);
        var loaded = SaveGame.Load(new MemoryStream(first), Definitions);
        Assert.Equal(first, Save(loaded));
        Assert.Equal((r.Game.Day, r.Game.Random.State, r.Game.Clock.Serial), (loaded.Day, loaded.Random.State, loaded.Clock.Serial));
    }

    [GameFact]
    public void LoadRefusesAnotherVersion()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var saved = System.Text.Encoding.UTF8.GetString(Save(game));
        InvalidDataException Refusal(int version) => Assert.Throws<InvalidDataException>(() => SaveGame.Load(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(saved.Replace("\"version\":2", $"\"version\":{version}"))), Definitions));
        Assert.Contains("version 3", Refusal(3).Message);
        Assert.Equal("This save was made by an older version and can't be loaded.", Refusal(1).Message);
        Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream("{}"u8.ToArray()), Definitions));
    }

    /// <summary>The game writes gzip saves; Load reads them like plain ones, a cut one is refused.</summary>
    [GameFact]
    public void LoadReadsGzipSaves()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var plain = Save(game);
        var zipped = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(plain);
        Assert.True(zipped.Length * 5 < plain.Length);
        Assert.Equal(plain, Save(SaveGame.Load(new MemoryStream(zipped.ToArray()), Definitions)));
        Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(zipped.ToArray()[..(int)(zipped.Length / 2)]), Definitions));
    }

    /// <summary>A damaged save is refused with <see cref="InvalidDataException"/>, never another exception.</summary>
    [GameFact]
    public void LoadRefusesCorruptSaves()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var text = System.Text.Encoding.UTF8.GetString(Save(game));
        string Mutate(System.Action<System.Text.Json.Nodes.JsonObject> change)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject();
            change(root);
            return root.ToJsonString();
        }
        string[] cases =
        [
            "[1]",
            Mutate(r => r["version"] = 1.5),
            Mutate(r => r["tick"] = "x"),
            "{\"format\":\"openswr-save\",\"version\":2}",
            Mutate(r => r["sides"]![0]!["researchPoints"] = new System.Text.Json.Nodes.JsonArray([.. Enumerable.Range(0, 10).Select(i => (System.Text.Json.Nodes.JsonNode?)i)])),
            Mutate(r => r["systems"]![0] = r["fleets"]![0]!.GetValue<uint>()),
            Mutate(r => // a unit type the game data lacks (same family, so references still resolve)
            {
                var o = r["objects"]!.AsArray().First(x => (string?)x!["kind"] == "object")!;
                o["type"] = SdRef.Make(SdRef.FamilyOf(o["type"]!.GetValue<uint>()), 0x99);
            }),
        ];
        foreach (var c in cases)
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(c)), Definitions));
    }

    /// <summary>A save refused while a battle request is open writes nothing.</summary>
    [GameFact]
    public void RefusedSaveWritesNothing()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        game.HostedBattle = game.Systems[0];
        var stream = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => SaveGame.Save(game, stream));
        Assert.Equal(0, stream.Length);
    }

    /// <summary>Compares the state events with the trace's; exact = all equal, else returns the matching prefix length.</summary>
    int AssertFacts(Replay r, object seed, bool exact = true)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Vectors", $"facts-{seed}.json")));
        var expected = doc.RootElement.GetProperty("facts").EnumerateArray()
            .Select(f => new Fact(f[0].GetInt32(), f[1].GetInt32(), f[2].GetUInt32(), f[3].GetInt64(), f[4].GetInt64()))
            .Where(f => f.Day <= r.Game.Day).ToList();
        var kinds = doc.RootElement.GetProperty("kinds").EnumerateArray().Select(k => k.GetInt32()).ToHashSet();
        var actual = r.Facts.Where(f => kinds.Contains(f.Kind)).ToList(); // only the kinds the trace's hooks cover
        int i = 0;
        while (i < expected.Count && i < actual.Count && expected[i] == actual[i]) i++;
        output.WriteLine($"seed {seed}: through day {r.Game.Day}, {expected.Count} expected facts, {actual.Count} actual, {i} match; "
            + string.Join(", ", expected.GroupBy(f => f.Kind).Select(g => $"{g.Key:X}: {g.Count()}")));
        for (int j = Math.Max(0, i - 3); j < Math.Min(i + 12, Math.Max(expected.Count, actual.Count)); j++)
            output.WriteLine($"  #{j} " + (j < expected.Count ? Show(expected[j]) : "(end)") + " | " + (j < actual.Count ? Show(actual[j]) : "(end)"));
        if (!exact) return i;
        Assert.Equal(expected.Count, i);
        Assert.Equal(expected.Count, actual.Count);
        return i;
    }

    static string Show(Fact f) => $"day {f.Day} {f.Kind:X} {f.Object:X8} {f.Old} -> {f.New}";

    // isqrt' quirk; verified exhaustively in the original
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(15, 3)]
    [InlineData(16, 3)]
    [InlineData(17, 4)]
    [InlineData(64, 7)]
    [InlineData(100, 10)]
    [InlineData(1048576, 1023)]
    [InlineData(1048577, 1024)]
    [InlineData(7417, 86)] // Coruscant (423, 392) -> (462, 315): 39^2 + 77^2
    public void IsqrtQuirk(int n, int expected) => Assert.Equal(expected, Orders.Isqrt(n));
}
