using System.Diagnostics;
using System.Globalization;
using System.Text;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>One side's numbers at a checkpoint (real state: the runner is an observer, not a player).</summary>
public sealed record SideStats(int Systems, int Fleets, int CapitalShips, int Troops, int BattlesWon, int BattlesLost, int Commands,
    int Shipyards = 0, int ProductionFacilities = 0, int Capacity = 0, int Allocated = 0, TroopSplit? Split = null, EconomyStats? Economy = null,
    int Fighters = 0, int FighterRoom = 0, int FighterCapacity = 0, RoleMix? Roles = null);

/// <summary>
/// Role mix (version 8), by each unit's main role from the definitions (OpenSWR.Sim.Ai.Roles, the side's own types): capital-ship
/// maintenance in fleets by main role line / screen / carrier / transport, interdictor (any ship with interdiction), and the ships
/// with bombardment (a capability: also counted in their main role); fighters by
/// interceptor / bomber / superiority; troops by garrison / assault; Usable planetary guns and shields; high-value border systems
/// (value above 60, an enemy system within ReachDays) and those of them holding an own gun.
/// </summary>
public sealed record RoleMix(int Line, int Screen, int Carrier, int Transport, int Interdictor, int Bombard, int Interceptor, int Bomber, int Superiority,
    int Garrison, int Assault, int Guns, int Shields, int HighValueBorder, int GunnedHighValueBorder);

/// <summary>
/// Where a side's existing troops are (version 7): in own systems, split into the openswr side's defended garrisons (its covered
/// defence targets), its attack staging systems and the other garrisons (the original side has only these); aboard fleets; Enroute.
/// </summary>
public sealed record TroopSplit(int Defended, int Staging, int Garrisons, int Fleets, int Enroute);

/// <summary>
/// Economy (version 7): Usable mines and refineries, their allocation pairs (capacity / allocated), and the free raw-material and
/// energy slots of the side's own systems (raw - used raw, energy - used energy).
/// </summary>
public sealed record EconomyStats(int Mines, int Refineries, int MineCapacity, int MineAllocated, int RefineryCapacity, int RefineryAllocated, int FreeRaw, int FreeEnergy);

/// <summary>One checkpoint row of one game.</summary>
public sealed record Checkpoint(int Game, int Seed, Owner OpenSWR, int Day, SideStats Alliance, SideStats Empire);

/// <summary>
/// One mission kind of the openswr side: missions started, attempts that succeeded (TaskStatus 3) and failed (2), and missions
/// that ended otherwise: foiled (CompletionStatus 3 / 4) or ended early without a success (any other CompletionStatus than 0 and 1
/// while TaskStatus is not 3, e.g. 5 no valid member, 6 target gone, 15 diplomacy at share 100; a rescue that freed its captive
/// ends with 6 after TaskStatus 3 and is not counted), as seen while the mission is registered.
/// </summary>
public sealed record MissionCount(int Started, int Succeeded, int Failed, int Ended = 0)
{
    public static MissionCount operator +(MissionCount a, MissionCount b) => new(a.Started + b.Started, a.Succeeded + b.Succeeded, a.Failed + b.Failed, a.Ended + b.Ended);
}

/// <summary>Why a system changed owner (observed by the runner around the change; Inferred classification, see <see cref="Match.Cause"/>).</summary>
public enum ChangeCause { Troops, Uprising, Diplomacy, Abandonment, Destruction, Other }

/// <summary>One system owner change: day, system, old and new owner, cause.</summary>
public sealed record OwnerChange(int Day, uint System, Owner From, Owner To, ChangeCause Cause);

/// <summary>
/// One system lost to troop assault (version 6 measurement): day, loser, the system's value for the loser at the start of the day
/// before (real state), the loser's troops there then, the attacker's troops there after the change, an attacker fleet in orbit for 1 day or more before
/// the loss, border (an attacker system within ReachDays at speed 100) or interior.
/// </summary>
public sealed record AssaultLoss(int Day, Owner Loser, int Value, int TroopsBefore, int Landed, bool Orbit, bool Border, bool Defended = false);

/// <summary>Production categories of the mix table.</summary>
public enum ProductCategory { CapitalShips, Fighters, Troops, SpecialForces, ProductionFacilities, Mines, Refineries, DefenceFacilities }

/// <summary>
/// One finished game: the last day, the result ("openswr victory" / "original victory" when the game is over, else "openswr ahead"
/// / "original ahead" by systems owned, else "draw"), the openswr side's share of the owned systems, the wall time, the player
/// settings side, the openswr side's missions per kind.
/// </summary>
public sealed record GameResult(int Game, int Seed, Owner OpenSWR, int Day, string Result, int Score, double OpenSWRShare, TimeSpan Wall, List<Checkpoint> Checkpoints,
    Owner PlayerSide, SortedDictionary<uint, MissionCount> Missions, List<OwnerChange> Changes, List<AssaultLoss> AssaultLosses,
    Dictionary<(Owner Side, int Period, ProductCategory Category), (int Units, int Maintenance)> Production, List<Slice>? Slices = null)
{
    /// <summary>+1 openswr won (victory or ahead), -1 lost, 0 draw.</summary>
    public bool Won => Score > 0;
    public bool Lost => Score < 0;
}

/// <summary>
/// Headless matches of the OpenSWR AI against the original AI, both sides computer controllers, run through the clock (12 ticks
/// per day) with the automatic battle engine, as the soak test does (without its forced battles and save checkpoints).
/// </summary>
public static class Match
{
    public const int TicksPerDay = 12;
    /// <summary>
    /// GameSettings.PlayerSide of every game: the side whose difficulty column of the TB settings applies (GameSettings.Column), as
    /// in the soak test. Both sides are computer controllers here; the other side plays with that column too.
    /// </summary>
    public const Owner PlayerSide = Owner.Alliance;

    /// <summary>Default seeds (the soak test's), so runs without --seed are reproducible.</summary>
    public static readonly int[] DefaultSeeds = [874334749, 401859171, 532947196, 445898697, 12345, 99991, 260732950, 421765374];

    /// <summary>
    /// Plays one game until the clock reaches day <paramref name="days"/> or the game is over: openswr on <paramref name="openswr"/>'s
    /// side, the original AI on the other (its own UI generator, seeded from the seed and side as in the soak test). A row every
    /// <paramref name="every"/> days and at the end.
    /// </summary>
    public static GameResult Play(GameDefinitions definitions, int game, int seed, Owner openswr, int galaxy, int difficulty, int days, int every = 50,
        Action<Checkpoint>? row = null, System.Collections.Immutable.ImmutableHashSet<OpenSWRController.V7>? features = null, SliceRecorder? slices = null,
        double yardRatio = OpenSWRController.DefaultYardRatio, double huntGate = OpenSWRController.MinHuntSuccess)
    {
        var watch = Stopwatch.StartNew();
        var state = NewGame.Create(new GameSettings(PlayerSide, galaxy, difficulty, 0, seed, Opponent: ComputerOpponent.OpenSWR), definitions);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int ui = seed ^ (int)side;
            var rand = new UiRandom(() => ui);
            state.Controllers[side] = side == openswr ? new OpenSWRController(side) { Features = features ?? OpenSWRController.DefaultFeatures, YardRatio = yardRatio, HuntGate = huntGate, IdleReasons = slices is null ? null : [], RefusedEstimates = slices is null ? null : [], MissionEstimates = slices is null ? null : [], IntelAges = slices is null ? null : [], NoLaunchLog = slices is null ? null : [], TroopRoomLog = slices is null ? null : [], TroopMoveLog = slices is null ? null : [], AssaultLossesSeen = slices is null ? null : [], PivotEvents = slices is null ? null : [], RetryRecords = slices is null ? null : [] }
                : new ComputerController(side) { Rand = n => rand.Rand(n) };
            state.Sides[side].Speed = TicksPerDay;
        }
        var commands = new Dictionary<Owner, int> { [Owner.Alliance] = 0, [Owner.Empire] = 0 };
        state.Clock.Executed = (c, r) =>
        {
            if (r.Success && c.Type is not (1 or 0x280) && commands.ContainsKey(c.Side)) commands[c.Side]++;
            if (r.Success && c.Type is >= 0x220 and <= 0x223 && c.ArgRef is StarSystem at) slices?.Bombard.Order(c.Side, c.Type, at);
        };
        state.BattleResults = _ => null; // the engine runs every battle
        state.BattleTrace = []; // battle reports (won side per battle)
        if (slices is not null) state.StrikeTrace = []; // strike records (UI only, no draw): the bombardment measurement
        state.FactTrace = []; // mission TaskStatus changes (0x60); read and cleared every tick
        var owners = new Dictionary<uint, (Owner Owner, uint Kind)>();
        var missions = new SortedDictionary<uint, MissionCount>();
        void Count(uint kind, MissionCount add) => missions[kind] = missions.GetValueOrDefault(kind, new(0, 0, 0)) + add;
        var ended = new HashSet<uint>();
        void Missions()
        {
            foreach (var m in state.Missions.Where(m => !owners.ContainsKey(m.Ref)))
            {
                owners[m.Ref] = (m.Owner, m.Kind);
                if (m.Owner == openswr) Count(m.Kind, new(1, 0, 0));
            }
            // foiled (3 / 4), or ended early without a success (a rescue that succeeded ends with 6 after TaskStatus 3: not counted)
            foreach (var m in state.Missions.Where(m => m.Owner == openswr && (m.CompletionStatus is 3 or 4 || m.CompletionStatus is not (0 or 1) && m.TaskStatus != 3)
                         && ended.Add(m.Ref)))
                Count(m.Kind, new(0, 0, 0, 1));
            foreach (var f in state.FactTrace!.Where(f => f.Kind == 0x60 && f.New is 2 or 3))
                if (owners.TryGetValue(f.Object, out var o) && o.Owner == openswr) Count(o.Kind, f.New == 3 ? new(0, 1, 0) : new(0, 0, 1));
            state.FactTrace!.Clear();
        }
        // owner changes: each tick compared with the state before it (owner, Uprising, the owner's Usable troops there)
        var changes = new List<OwnerChange>();
        var before = state.Systems.ToDictionary(s => s, Snapshot);
        // version 6: assault losses and the production mix
        var assaults = new List<AssaultLoss>();
        // the owner's troops and the system's value for its owner at the start of the previous day (facilities change owner with the
        // system, so the value is taken before the loss)
        var yesterday = state.Systems.ToDictionary(s => s, s => (Troops: 0, Value: 0));
        var today = yesterday;
        // version 7: the openswr side's covered defence targets at the start of the previous day
        HashSet<StarSystem> defendedYesterday = [], defendedToday = [];
        int troopDay = state.Day;
        var orbitSince = new Dictionary<StarSystem, int>(); // first day of a continuous foreign fleet presence
        int OwnTroops(StarSystem s, Owner o) => s.Contents.Count(x => Family.IsTroop(x.Type) && x.Usable && x.Owner == o);
        var production = new Dictionary<(Owner, int, ProductCategory), (int, int)>();
        int created = state.Created.Count;
        void Owners()
        {
            if (state.Day != troopDay)
            {
                (yesterday, troopDay) = (today, state.Day);
                (defendedYesterday, defendedToday) = (defendedToday, (state.Controllers[openswr] as OpenSWRController)?.DefendedSystems.ToHashSet() ?? []);
                var homes = new[] { Owner.Alliance, Owner.Empire }.ToDictionary(o => o, o => OpenSWRController.HomeSector(state, o));
                today = state.Systems.ToDictionary(s => s, s => (OwnTroops(s, s.Owner),
                    s.Owner is Owner.Alliance or Owner.Empire ? OpenSWRController.SystemValue(state, s.Owner, s, homes[s.Owner], x => (x.Owner, x.Populated)) : 0));
            }
            foreach (var s in state.Systems)
            {
                bool foreign = s.Contents.OfType<Fleet>().Any(f => f.Usable && f.Existing && f.Owner is Owner.Alliance or Owner.Empire && f.Owner != s.Owner);
                if (!foreign) orbitSince.Remove(s);
                else orbitSince.TryAdd(s, state.Day);
            }
            foreach (var s in state.Systems)
            {
                var was = before[s];
                if (s.Owner != was.Owner)
                {
                    var cause = Cause(s, was.Uprising, was.OwnerTroops);
                    changes.Add(new(state.Day, s.Ref, was.Owner, s.Owner, cause));
                    if (cause == ChangeCause.Troops && was.Owner is Owner.Alliance or Owner.Empire)
                    {
                        var loser = was.Owner;
                        bool border = state.Systems.Any(x => x.Owner == s.Owner && x != s && Orders.TravelDays(state, x, s, 100) <= OpenSWRController.ReachDays);
                        bool orbit = orbitSince.TryGetValue(s, out int since) && state.Day - since >= 1;
                        var (troopsBefore, value) = yesterday.GetValueOrDefault(s);
                        assaults.Add(new(state.Day, loser, value, troopsBefore, OwnTroops(s, s.Owner), orbit, border, loser == openswr && defendedYesterday.Contains(s)));
                    }
                }
                before[s] = Snapshot(s);
            }
            for (; created < state.Created.Count; created++)
            {
                var o = state.Created[created];
                if (o.Owner is not (Owner.Alliance or Owner.Empire) || Category(o.Type) is not { } category) continue;
                var key = (o.Owner, (state.Day - 1) / 100, category);
                var (n, m) = production.GetValueOrDefault(key);
                production[key] = (n + 1, m + state.MaintenanceCost(o.Type));
            }
        }
        var checkpoints = new List<Checkpoint>();
        void Check()
        {
            var c = new Checkpoint(game, seed, openswr, state.Day, Stats(state, Owner.Alliance, commands), Stats(state, Owner.Empire, commands));
            checkpoints.Add(c);
            row?.Invoke(c);
        }
        while (state.Day < days && !state.GameOver) // ends when the clock reaches day `days` (or at game over)
        {
            int day = state.Day, start = state.Clock.Tick;
            while (state.Day == day && !state.GameOver)
            {
                state.Clock.Step(state);
                Missions();
                Owners();
                slices?.Objectives.Tick(state); // an observer: no draw, no write
                if (state.Clock.Tick - start > 3 * TicksPerDay) throw new InvalidOperationException($"seed {seed}: day {day} did not finish in {3 * TicksPerDay} ticks");
            }
            if (state.Day != day) slices?.Day(state, changes); // version 9: time slices (an observer: no draw, no write)
            if (state.Day % every == 0 && state.Day != day) Check();
        }
        if (checkpoints.Count == 0 || checkpoints[^1].Day != state.Day) Check();

        var last = checkpoints[^1];
        var other = openswr == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        int Systems(Owner side) => (side == Owner.Alliance ? last.Alliance : last.Empire).Systems;
        int mine = Systems(openswr), theirs = Systems(other);
        (string result, int score) = state.GameOver && state.Winner == openswr ? ("openswr victory", 1)
            : state.GameOver && state.Winner == other ? ("original victory", -1)
            : mine > theirs ? ("openswr ahead", 1) : mine < theirs ? ("original ahead", -1) : ("draw", 0);
        if (slices is not null && state.Controllers[openswr] is OpenSWRController { RefusedEstimates: { } refused }) slices.RefusedAttempts.AddRange(refused);
        if (slices is not null && state.Controllers[openswr] is OpenSWRController { IntelAges: { } ages }) slices.IntelAges.AddRange(ages);
        if (slices is not null && state.Controllers[openswr] is OpenSWRController { TroopRoomLog: { } room } roomer)
        {
            slices.TroopRoom.Add([.. room]);
            slices.OwnLandings.AddRange(roomer.OwnLandings);
        }
        if (slices is not null && state.Controllers[openswr] is OpenSWRController { NoLaunchLog: { } why } launcher)
        {
            slices.NoLaunch.AddRange(why);
            slices.AttackLaunches += launcher.AttackLaunches;
        }
        if (slices is not null && state.Controllers[openswr] is OpenSWRController { AssaultLossesSeen: { } seen }) slices.AssaultLossesSeen.AddRange(seen);
        if (slices is not null && state.Controllers[openswr] is OpenSWRController hunter) slices.Objectives.Hunted(hunter); // step 21 (Hunt) measurement
        if (slices is not null && state.Controllers[openswr] is OpenSWRController { PivotEvents: { } pivots, RetryRecords: { } retries })
        {
            slices.PivotEvents.AddRange(pivots);
            slices.RetryRecords.AddRange(retries);
            slices.FewerDecoys += ((OpenSWRController)state.Controllers[openswr]).FewerDecoys;
        }
        if (state.Controllers[openswr] is OpenSWRController { RimDropped.Count: > 0 } rim) // step 37 (RimFirst) measurement
        {
            var dropped = rim.RimDropped.Distinct().Select(r => state.Systems.First(s => s.Ref == r)).ToList();
            Console.WriteLine($"  game {game}: rim drops {rim.RimDropped.Count} at {dropped.Count} systems, held at end {dropped.Count(s => s.Owner == openswr)}, enemy-held at end {dropped.Count(s => s.Owner == (openswr == Owner.Alliance ? Owner.Empire : Owner.Alliance))}, rim systems owned {state.Systems.Count(s => s.Owner == openswr && !s.Sector.IsCore)}");
        }
        return new(game, seed, openswr, state.Day, result, score, mine + theirs == 0 ? 0 : (double)mine / (mine + theirs), watch.Elapsed, checkpoints,
            state.Settings.PlayerSide, missions, changes, assaults, production, slices?.Slices);
    }

    /// <summary>The production category of a unit or facility type, null for anything else.</summary>
    static ProductCategory? Category(uint type) => Family.Of(type) switch
    {
        >= 0x14 and < 0x1C => ProductCategory.CapitalShips,
        >= 0x1C and < 0x20 => ProductCategory.Fighters,
        >= 0x10 and < 0x14 => ProductCategory.Troops,
        >= 0x3C and < 0x40 => ProductCategory.SpecialForces,
        >= 0x28 and < 0x2B => ProductCategory.ProductionFacilities,
        0x2C => ProductCategory.Mines,
        0x2D => ProductCategory.Refineries,
        >= 0x22 and < 0x28 => ProductCategory.DefenceFacilities,
        _ => null,
    };

    /// <summary>
    /// Assault losses (version 6): per side, value band (under 20, 20-60, above 60), border / interior and orbit, the count, the
    /// average troops the loser had there the day before and the average troops landed.
    /// </summary>
    public static string AssaultTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("systems lost to troop assault (value for the loser; troops the day before; troops landed)");
        text.AppendLine($"  {"loser",-8} {"value",-7} {"place",-9} {"orbit",-6} {"count",6} {"troops before",14} {"landed",7}");
        static string Band(int v) => v < 20 ? "<20" : v <= 60 ? "20-60" : ">60";
        var rows = results.SelectMany(r => r.AssaultLosses.Select(a => (Who: a.Loser == r.OpenSWR ? "openswr" : "original", a))).ToList();
        foreach (var g in rows.GroupBy(x => (x.Who, Band(x.a.Value), x.a.Border, x.a.Orbit))
                     .OrderBy(g => g.Key.Who).ThenBy(g => g.Key.Item2).ThenBy(g => g.Key.Border).ThenBy(g => g.Key.Orbit))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {g.Key.Who,-8} {g.Key.Item2,-7} {(g.Key.Border ? "border" : "interior"),-9} {(g.Key.Orbit ? "yes" : "no"),-6} {g.Count(),6} {g.Average(x => x.a.TroopsBefore),14:F1} {g.Average(x => x.a.Landed),7:F1}"));
        foreach (var who in new[] { "openswr", "original" })
        {
            var mine = rows.Where(x => x.Who == who).ToList();
            text.AppendLine($"  {who} lost by value: <20 {mine.Count(x => x.a.Value < 20)}, 20-60 {mine.Count(x => x.a.Value is >= 20 and <= 60)}, >60 {mine.Count(x => x.a.Value > 60)}");
        }
        // version 7: openswr losses by whether the system was a covered defence target the day before
        foreach (bool d in new[] { true, false })
        {
            var g = rows.Where(x => x.Who == "openswr" && x.a.Defended == d).ToList();
            if (g.Count > 0)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  openswr {(d ? "defended" : "not defended")}: {g.Count} (>60 {g.Count(x => x.a.Value > 60)}), troops before {g.Average(x => x.a.TroopsBefore):F1}, landed {g.Average(x => x.a.Landed):F1}"));
        }
        return text.ToString();
    }

    /// <summary>
    /// Facilities and unused maintenance (version 6): per checkpoint day that is a multiple of 100, per side, the average Usable
    /// shipyards and production facilities (shipyards, training facilities, construction yards) and the unused share of the
    /// maintenance capacity (1 - allocated / capacity).
    /// </summary>
    public static string FacilityTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("facilities and unused maintenance (averages per game)");
        text.AppendLine($"  {"day",5} {"openswr yards",14} {"prod fac",9} {"unused",7}   {"original yards",15} {"prod fac",9} {"unused",7}");
        var days = results.SelectMany(r => r.Checkpoints.Select(c => c.Day)).Where(d => d % 100 == 0).Distinct().Order();
        foreach (int day in days)
        {
            var at = results.Select(r => (r, c: r.Checkpoints.FirstOrDefault(c => c.Day == day))).Where(x => x.c is not null).ToList();
            SideStats Mine((GameResult r, Checkpoint? c) x) => x.r.OpenSWR == Owner.Alliance ? x.c!.Alliance : x.c!.Empire;
            SideStats Theirs((GameResult r, Checkpoint? c) x) => x.r.OpenSWR == Owner.Alliance ? x.c!.Empire : x.c!.Alliance;
            static double Unused(SideStats s) => s.Capacity <= 0 ? 0 : 1 - (double)s.Allocated / s.Capacity;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {day,5} {at.Average(x => Mine(x).Shipyards),14:F1} {at.Average(x => Mine(x).ProductionFacilities),9:F1} {at.Average(x => Unused(Mine(x))),7:P0}   "
                + $"{at.Average(x => Theirs(x).Shipyards),15:F1} {at.Average(x => Theirs(x).ProductionFacilities),9:F1} {at.Average(x => Unused(Theirs(x))),7:P0}"));
        }
        return text.ToString();
    }

    /// <summary>Per checkpoint day that is a multiple of 100: the rows of the given side's (openswr or original) average numbers.</summary>
    static IEnumerable<(int Day, List<SideStats> Mine, List<SideStats> Theirs)> Hundreds(IReadOnlyList<GameResult> results)
    {
        foreach (int day in results.SelectMany(r => r.Checkpoints.Select(c => c.Day)).Where(d => d % 100 == 0).Distinct().Order())
        {
            var at = results.Select(r => (r, c: r.Checkpoints.FirstOrDefault(c => c.Day == day))).Where(x => x.c is not null).ToList();
            yield return (day, [.. at.Select(x => x.r.OpenSWR == Owner.Alliance ? x.c!.Alliance : x.c!.Empire)],
                [.. at.Select(x => x.r.OpenSWR == Owner.Alliance ? x.c!.Empire : x.c!.Alliance)]);
        }
    }

    /// <summary>Where the troops are (version 7): averages per game every 100 days, per side.</summary>
    public static string TroopTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("where the troops are (averages per game; defended / staging: openswr only)");
        text.AppendLine($"  {"day",5} | {"openswr defended",16} {"staging",8} {"other gar",9} {"fleets",7} {"enroute",8} | {"original garrisons",18} {"fleets",7} {"enroute",8}");
        foreach (var (day, mine, theirs) in Hundreds(results))
        {
            double A(List<SideStats> l, Func<TroopSplit, int> f) => l.Average(x => x.Split is null ? 0 : f(x.Split));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {day,5} | {A(mine, x => x.Defended),16:F1} {A(mine, x => x.Staging),8:F1} {A(mine, x => x.Garrisons),9:F1} {A(mine, x => x.Fleets),7:F1} {A(mine, x => x.Enroute),8:F1} | "
                + $"{A(theirs, x => x.Garrisons + x.Defended + x.Staging),18:F1} {A(theirs, x => x.Fleets),7:F1} {A(theirs, x => x.Enroute),8:F1}"));
        }
        return text.ToString();
    }

    /// <summary>Economy (version 7): mines, refineries, their allocation, free raw / energy slots, unused maintenance; averages per game.</summary>
    public static string EconomyTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("economy (averages per game; mine / refinery allocated of capacity; free raw / energy slots in own systems)");
        foreach (var who in new[] { "openswr", "original" })
        {
            text.AppendLine($"  {who}");
            text.AppendLine($"  {"day",5} {"mines",6} {"refin",6} {"mine alloc",11} {"ref alloc",11} {"free raw",9} {"free energy",12} {"unused maint",13}");
            foreach (var (day, mine, theirs) in Hundreds(results))
            {
                var l = who == "openswr" ? mine : theirs;
                double A(Func<EconomyStats, int> f) => l.Average(x => x.Economy is null ? 0 : f(x.Economy));
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {day,5} {A(e => e.Mines),6:F1} {A(e => e.Refineries),6:F1} {$"{A(e => e.MineAllocated):F0}/{A(e => e.MineCapacity):F0}",11} {$"{A(e => e.RefineryAllocated):F0}/{A(e => e.RefineryCapacity):F0}",11} "
                    + $"{A(e => e.FreeRaw),9:F1} {A(e => e.FreeEnergy),12:F1} {l.Average(x => x.Capacity <= 0 ? 0 : 1 - (double)x.Allocated / x.Capacity),13:P0}"));
            }
        }
        return text.ToString();
    }

    /// <summary>Fighters and carrier room (version 7): existing fighters, the fighter capacity of the existing capital ships and its free room.</summary>
    public static string FighterTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("fighters and carrier room (averages per game)");
        text.AppendLine($"  {"day",5} | {"openswr fighters",16} {"capacity",9} {"free room",10} | {"original fighters",17} {"capacity",9} {"free room",10}");
        foreach (var (day, mine, theirs) in Hundreds(results))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {day,5} | {mine.Average(x => x.Fighters),16:F1} {mine.Average(x => x.FighterCapacity),9:F1} {mine.Average(x => x.FighterRoom),10:F1} | "
                + $"{theirs.Average(x => x.Fighters),17:F1} {theirs.Average(x => x.FighterCapacity),9:F1} {theirs.Average(x => x.FighterRoom),10:F1}"));
        return text.ToString();
    }

    /// <summary>Role mixes (version 8): averages per game every 100 days, per side (see <see cref="RoleMix"/>).</summary>
    public static string RoleTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("role mix (averages per game; fleet maintenance by main role; fighters, troops by main role; guns, shields; high-value border systems gunned)");
        foreach (var who in new[] { "openswr", "original" })
        {
            text.AppendLine($"  {who}");
            text.AppendLine($"  {"day",5} {"line",6} {"screen",6} {"carr",6} {"trans",6} {"intd",6} {"bomb",6} | {"intc",5} {"bmbr",5} {"supr",5} | {"garr",5} {"asslt",5} | {"guns",5} {"shld",5} {"hv-gunned",10}");
            foreach (var (day, mine, theirs) in Hundreds(results))
            {
                var l = (who == "openswr" ? mine : theirs).Where(x => x.Roles is not null).Select(x => x.Roles!).ToList();
                if (l.Count == 0) continue;
                double A(Func<RoleMix, int> f) => l.Average(f);
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {day,5} {A(x => x.Line),6:F0} {A(x => x.Screen),6:F0} {A(x => x.Carrier),6:F0} {A(x => x.Transport),6:F0} {A(x => x.Interdictor),6:F0} {A(x => x.Bombard),6:F0} | "
                    + $"{A(x => x.Interceptor),5:F1} {A(x => x.Bomber),5:F1} {A(x => x.Superiority),5:F1} | {A(x => x.Garrison),5:F1} {A(x => x.Assault),5:F1} | "
                    + $"{A(x => x.Guns),5:F1} {A(x => x.Shields),5:F1} {$"{A(x => x.GunnedHighValueBorder):F1}/{A(x => x.HighValueBorder):F1}",10}"));
            }
        }
        return text.ToString();
    }

    /// <summary>Production mix (version 6): maintenance of the units and facilities each side ordered, per 100 days, average per game.</summary>
    public static string ProductionTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("production mix: maintenance ordered per 100 days, average per game (units in brackets)");
        var cats = Enum.GetValues<ProductCategory>();
        string Short(ProductCategory c) => c switch
        {
            ProductCategory.CapitalShips => "capital", ProductCategory.Fighters => "fighters", ProductCategory.Troops => "troops",
            ProductCategory.SpecialForces => "special", ProductCategory.ProductionFacilities => "prodfac", ProductCategory.Mines => "mines", ProductCategory.Refineries => "refineries",
            _ => "deffac",
        };
        foreach (var who in new[] { "openswr", "original" })
        {
            text.AppendLine($"  {who}");
            text.AppendLine($"  {"days",-10}" + string.Concat(cats.Select(c => $" {Short(c),14}")));
            int periods = results.SelectMany(r => r.Production.Keys.Select(k => k.Period)).DefaultIfEmpty(-1).Max() + 1;
            for (int p = 0; p < periods; p++)
            {
                var line = new StringBuilder($"  {$"{p * 100 + 1}-{p * 100 + 100}",-10}");
                foreach (var c in cats)
                {
                    double m = 0, n = 0;
                    foreach (var r in results)
                    {
                        var side = who == "openswr" ? r.OpenSWR : r.OpenSWR == Owner.Alliance ? Owner.Empire : Owner.Alliance;
                        var (units, maint) = r.Production.GetValueOrDefault((side, p, c));
                        (m, n) = (m + maint, n + units);
                    }
                    line.Append(string.Create(CultureInfo.InvariantCulture, $" {$"{m / results.Count:F0} ({n / results.Count:F1})",14}"));
                }
                text.AppendLine(line.ToString());
            }
        }
        return text.ToString();
    }

    static (Owner Owner, bool Uprising, int OwnerTroops) Snapshot(StarSystem s) =>
        (s.Owner, s.Uprising, s.Contents.Count(x => Family.IsTroop(x.Type) && x.Usable && x.Owner == s.Owner));

    /// <summary>
    /// The cause of an owner change, from the system's state after it (the runner is an observer: real state) and before it. Inferred
    /// classification (the sim keeps no cause): the system Destroyed -> destruction; the new owner holds the military control (only its
    /// troops are there) -> troops (assault or landing); else, with no troops of either side: the system was in
    /// Uprising before -> uprising, the old owner had troops there before the tick -> abandonment (all units gone), the new owner holds
    /// the support control -> diplomacy (support moved over the threshold); anything else -> other.
    /// </summary>
    public static ChangeCause Cause(StarSystem s, bool uprisingBefore, int ownerTroopsBefore)
    {
        if (s.Destroyed) return ChangeCause.Destruction;
        var military = s.Control.Military;
        if (military != Owner.None && military == s.Owner) return ChangeCause.Troops;
        if (military == Owner.None)
        {
            if (uprisingBefore) return ChangeCause.Uprising; // an uprising that took the garrison counts as the uprising
            if (ownerTroopsBefore > 0) return ChangeCause.Abandonment;
            if (s.Control.Support == s.Owner) return ChangeCause.Diplomacy;
        }
        return ChangeCause.Other;
    }

    /// <summary>
    /// Owner changes by cause: per cause the systems the openswr side and the original side gained and lost, overall and per
    /// <paramref name="period"/> days.
    /// </summary>
    public static string CauseTable(IReadOnlyList<GameResult> results, int period = 200)
    {
        var text = new StringBuilder();
        var all = results.SelectMany(r => r.Changes.Select(c => (r.OpenSWR, c))).ToList();
        void Table(string title, IEnumerable<(Owner OpenSWR, OwnerChange Change)> rows)
        {
            var list = rows.ToList();
            text.AppendLine(title);
            text.AppendLine($"  {"cause",-12} {"openswr +",9} {"openswr -",9} {"original +",10} {"original -",10}");
            foreach (var cause in Enum.GetValues<ChangeCause>())
            {
                var c = list.Where(x => x.Change.Cause == cause).ToList();
                Owner Other(Owner o) => o == Owner.Alliance ? Owner.Empire : Owner.Alliance;
                text.AppendLine($"  {cause,-12} {c.Count(x => x.Change.To == x.OpenSWR),9} {c.Count(x => x.Change.From == x.OpenSWR),9} "
                    + $"{c.Count(x => x.Change.To == Other(x.OpenSWR)),10} {c.Count(x => x.Change.From == Other(x.OpenSWR)),10}");
            }
        }
        Table("owner changes by cause (all games, all days)", all);
        int last = all.Count == 0 ? 0 : all.Max(x => x.c.Day);
        for (int from = 0; from <= last; from += period)
        {
            int f = from;
            Table($"days {f + 1}–{f + period}", all.Where(x => x.c.Day > f && x.c.Day <= f + period));
        }
        return text.ToString();
    }

    static SideStats Stats(GameState game, Owner side, Dictionary<Owner, int> commands)
    {
        var ai = game.Controllers.GetValueOrDefault(side) as OpenSWRController;
        var roles = Roles.Compute(game, Roles.SideTypes(game, side));
        UnitRoles? R(GameObject o) => roles.GetValueOrDefault(o.Type);
        var fleetShips = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == side && f.Existing)
            .SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type) && x.Existing).ToList();
        int Ships(Func<UnitRoles, bool> p) => fleetShips.Where(x => R(x) is { } r && p(r)).Sum(x => game.MaintenanceCost(x.Type));
        var units = game.Created.Where(o => o.Owner == side && o.Existing).ToList();
        int Count(UnitKind kind, UnitRole role) => units.Count(o => R(o) is { } r && r.Kind == kind && r.Main == role);
        var enemySystems = game.Systems.Where(s => s.Owner is Owner.Alliance or Owner.Empire && s.Owner != side).ToList();
        var home = OpenSWRController.HomeSector(game, side);
        var highBorder = game.Systems.Where(s => s.Owner == side && !s.Destroyed
            && OpenSWRController.SystemValue(game, side, s, home, x => (x.Owner, x.Populated)) > 60
            && enemySystems.Any(e => Orders.TravelDays(game, s, e, 100) <= OpenSWRController.ReachDays)).ToList();
        bool Gun(GameObject o) => o.Owner == side && o.Usable && R(o) is { Kind: UnitKind.Defence } r && r[UnitRole.Gun] > 0;
        var mix = new RoleMix(
            Ships(r => !r.Interdictor && r.Main == UnitRole.Line), Ships(r => !r.Interdictor && r.Main == UnitRole.Screen),
            Ships(r => !r.Interdictor && r.Main == UnitRole.Carrier), Ships(r => !r.Interdictor && r.Main == UnitRole.Transport),
            Ships(r => r.Interdictor), Ships(r => r[UnitRole.Bombard] > 0),
            Count(UnitKind.Fighter, UnitRole.Interceptor), Count(UnitKind.Fighter, UnitRole.Bomber), Count(UnitKind.Fighter, UnitRole.Superiority),
            Count(UnitKind.Troop, UnitRole.Garrison), Count(UnitKind.Troop, UnitRole.Assault),
            game.Systems.Sum(s => s.Contents.Count(Gun)), game.Systems.Sum(s => s.Contents.Count(f => f.Owner == side && f.Usable && R(f) is { Kind: UnitKind.Defence, Main: UnitRole.Shield })),
            highBorder.Count, highBorder.Count(s => s.Contents.Any(Gun)));
        var defended = ai?.DefendedSystems.ToHashSet() ?? [];
        var staging = ai?.StagingSystems.ToHashSet() ?? [];
        int d = 0, st = 0, g = 0, fl = 0, en = 0;
        foreach (var t in game.Created.Where(o => Family.IsTroop(o.Type) && o.Owner == side && o.Existing))
            if (t.Enroute) en++;
            else if (t.Container is StarSystem s) { if (staging.Contains(s)) st++; else if (defended.Contains(s)) d++; else g++; }
            else fl++;
        var own = game.Systems.Where(s => s.Owner == side && !s.Destroyed).ToList();
        var eco = game.Facilities.Where(f => f.Owner == side && f.Usable && f.IsMineOrRefinery).ToList();
        var mines = eco.Where(f => Family.Of(f.Type) == 0x2C).ToList();
        var refineries = eco.Where(f => Family.Of(f.Type) == 0x2D).ToList();
        var economy = new EconomyStats(mines.Count, refineries.Count, mines.Sum(f => f.Capacity), mines.Sum(f => f.Allocated),
            refineries.Sum(f => f.Capacity), refineries.Sum(f => f.Allocated),
            own.Sum(s => Math.Max(0, s.RawMaterials - s.UsedRaw)), own.Sum(s => Math.Max(0, s.Energy - s.UsedEnergy)));
        var ships = game.Created.Where(o => Family.IsCapitalShip(o.Type) && o.Owner == side && o.Existing).ToList();
        int fighterCapacity = ships.Sum(x => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type)?.FighterCapacity ?? 0));
        int aboard = ships.Sum(x => x.Contents.Count(y => Family.Of(y.Type) is >= 0x1C and < 0x20 && y.Existing));
        int fighters = game.Created.Count(o => Family.Of(o.Type) is >= 0x1C and < 0x20 && o.Owner == side && o.Existing);
        var fleets = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == side && f.Existing).ToList();
        var battles = game.BattleTrace ?? [];
        return new(
            game.Systems.Count(s => s.Owner == side && !s.Destroyed),
            fleets.Count(f => f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing)),
            fleets.Sum(f => f.Contents.Count(x => Family.IsCapitalShip(x.Type) && x.Existing)),
            game.Created.Count(o => Family.IsTroop(o.Type) && o.Owner == side && o.Existing),
            battles.Count(b => b.Sides.Contains(side) && b.Won == side),
            battles.Count(b => b.Sides.Contains(side) && b.Won is Owner.Alliance or Owner.Empire && b.Won != side),
            commands[side],
            game.Facilities.Count(f => f.Owner == side && f.Usable && Family.Of(f.Type) == 0x28),
            game.Facilities.Count(f => f.Owner == side && f.Usable && Family.Of(f.Type) is >= 0x28 and < 0x2B),
            game.Sides[side].MaintenanceCapacity, game.Sides[side].MaintenanceAllocated,
            new TroopSplit(d, st, g, fl, en), economy, fighters, Math.Max(0, fighterCapacity - aboard), fighterCapacity, mix);
    }

    static string Side(SideStats s) =>
        $"sys {s.Systems,3} fleets {s.Fleets,3} ships {s.CapitalShips,4} troops {s.Troops,4} battles {s.BattlesWon,3}/{s.BattlesLost,-3} cmds {s.Commands,5}";

    /// <summary>A checkpoint as a console row.</summary>
    public static string Row(Checkpoint c) => $"  day {c.Day,4} | Alliance {Side(c.Alliance)} | Empire {Side(c.Empire)}";

    public const string CsvHeader = "game,seed,openswr,day,side,systems,fleets,capital_ships,troops,battles_won,battles_lost,commands,shipyards,production_facilities,capacity,allocated,"
        + "troops_defended,troops_staging,troops_garrisons,troops_fleets,troops_enroute,mines,refineries,mine_capacity,mine_allocated,refinery_capacity,refinery_allocated,free_raw,free_energy,fighters,fighter_room,fighter_capacity";

    /// <summary>Two CSV lines (Alliance, Empire) of a checkpoint.</summary>
    public static IEnumerable<string> Csv(Checkpoint c)
    {
        foreach (var (side, s) in new[] { (Owner.Alliance, c.Alliance), (Owner.Empire, c.Empire) })
            yield return string.Join(",", c.Game, c.Seed, c.OpenSWR, c.Day, side, s.Systems, s.Fleets, s.CapitalShips, s.Troops, s.BattlesWon, s.BattlesLost, s.Commands,
                s.Shipyards, s.ProductionFacilities, s.Capacity, s.Allocated,
                s.Split?.Defended, s.Split?.Staging, s.Split?.Garrisons, s.Split?.Fleets, s.Split?.Enroute,
                s.Economy?.Mines, s.Economy?.Refineries, s.Economy?.MineCapacity, s.Economy?.MineAllocated, s.Economy?.RefineryCapacity, s.Economy?.RefineryAllocated,
                s.Economy?.FreeRaw, s.Economy?.FreeEnergy, s.Fighters, s.FighterRoom, s.FighterCapacity);
    }

    /// <summary>Mission kind names (family of the MISSNSD type).</summary>
    public static string MissionName(uint kind) => kind switch
    {
        0x51 => "diplomacy", 0x52 => "espionage", 0x53 => "research", 0x54 => "reconnaissance", 0x55 => "recruitment", 0x56 => "incite",
        0x57 => "subdue", 0x58 => "jedi training", 0x61 => "rescue", 0x62 => "abduction", 0x63 => "assassination", 0x69 => "sabotage",
        0x41 => "move*", 0x42 => "return*", 0x43 => "autorouting*", 0x44 => "adrift*", 0x64 => "palace*", 0x65 => "bounty*", 0x71 => "dagobah*",
        0x72 => "vacation*", 0x73 => "pickup*", // *: not ordered by rule 5 (movement, story)
        _ => $"kind {kind:X2}",
    };

    /// <summary>One line per mission kind: started, attempts succeeded / failed, success rate, missions foiled or ended early.</summary>
    public static string MissionTable(IEnumerable<KeyValuePair<uint, MissionCount>> counts)
    {
        var text = new StringBuilder();
        text.AppendLine($"{"mission",-15} {"started",7} {"ok",5} {"failed",6} {"rate",6} {"foiled/early",12}");
        foreach (var (kind, c) in counts.OrderBy(x => x.Key))
        {
            int attempts = c.Succeeded + c.Failed;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{MissionName(kind),-15} {c.Started,7} {c.Succeeded,5} {c.Failed,6} {(attempts == 0 ? "-" : $"{100.0 * c.Succeeded / attempts:F0}%"),6} {c.Ended,12}"));
        }
        return text.ToString();
    }

    /// <summary>Wins / losses / draws overall and per side played, the average end share of the owned systems, the wall time per game.</summary>
    public static string Summary(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("OpenSWR AI vs original AI");
        text.AppendLine($"{"played as",-10} {"games",5} {"wins",5} {"losses",6} {"draws",5} {"avg share",9} {"avg wall",9}");
        void Line(string name, IReadOnlyList<GameResult> list)
        {
            if (list.Count == 0) return;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{name,-10} {list.Count,5} {list.Count(r => r.Won),5} {list.Count(r => r.Lost),6} {list.Count(r => r.Score == 0),5} {list.Average(r => r.OpenSWRShare),9:P1} {list.Average(r => r.Wall.TotalSeconds),8:F1}s"));
        }
        Line("Alliance", [.. results.Where(r => r.OpenSWR == Owner.Alliance)]);
        Line("Empire", [.. results.Where(r => r.OpenSWR == Owner.Empire)]);
        Line("all", results);
        var all = new SortedDictionary<uint, MissionCount>();
        foreach (var r in results)
            foreach (var (kind, c) in r.Missions) all[kind] = all.GetValueOrDefault(kind, new(0, 0, 0)) + c;
        text.AppendLine();
        text.AppendLine("openswr missions (all games; ok / failed = attempts by TaskStatus)");
        text.Append(MissionTable(all));
        text.AppendLine();
        text.Append(CauseTable(results));
        text.AppendLine();
        text.Append(AssaultTable(results));
        text.AppendLine();
        text.Append(ProductionTable(results));
        text.AppendLine();
        text.Append(FacilityTable(results));
        text.AppendLine();
        text.Append(TroopTable(results));
        text.AppendLine();
        text.Append(EconomyTable(results));
        text.AppendLine();
        text.Append(FighterTable(results));
        text.AppendLine();
        text.Append(RoleTable(results));
        return text.ToString();
    }
}
