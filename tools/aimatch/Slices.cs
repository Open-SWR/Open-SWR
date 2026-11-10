using OpenSWR.Data.Dat;
using System.Globalization;
using System.Text;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>
/// One side's figures in one time slice (version 9 measurement): the state at
/// the slice's last day and the events within it. Names are the CSV columns and the console table rows.
/// </summary>
public sealed class SliceFigures
{
    public static readonly string[] Names =
    [
        "systems", "share_pct",
        "maint_capacity", "maint_used", "raw_stock", "refined_stock", "mines", "refineries", "construction_yards", "shipyards", "training_facilities",
        "capital_maint", "capital_attack", "fighters", "troops", "guns", "shields",
        "shipyard_building_pct", "shipyard_idle_free_pct", "training_building_pct", "training_idle_free_pct", "construction_building_pct", "construction_idle_free_pct",
        .. Enum.GetValues<ChangeCause>().SelectMany(c => new[] { $"gained_{c.ToString().ToLowerInvariant()}", $"lost_{c.ToString().ToLowerInvariant()}" }),
        "battles", "battles_won", "battles_lost", "capital_maint_lost",
        "characters_on_mission", "characters_idle", "characters_captured_or_dead",
        "enemy_in_reach", "fresh_view_pct",
        "fighters_in_systems", "fighters_in_hangars", "wing_battles_won", "wing_battles_lost",
        // weakness audit, see SliceRecorder.Audit
        "flipped_by_support", "flipped_after_sabotage", "garrison_sabotaged", "bombard_troops_lost", "bombard_undefended",
        "rim_owned", "rim_unpopulated_owned", "hq_found_day", "leaders_taken_moving", "ships_lost_maintenance", "rebuilt_100d",
        "retreats_vs_fighters", "chase_days",
    ];

    public readonly double[] Values = new double[Names.Length];
    public double this[string name] { get => Values[Array.IndexOf(Names, name)]; set => Values[Array.IndexOf(Names, name)] = value; }
}

/// <summary>One slice of one game: the slice's last day and both sides' figures.</summary>
public sealed record Slice(int Day, SliceFigures Alliance, SliceFigures Empire)
{
    public SliceFigures Of(Owner side) => side == Owner.Alliance ? Alliance : Empire;
}

/// <summary>
/// Records the time slices of one game (every <see cref="Length"/> days). An observer: it reads the real state once per day and
/// draws no random number, writes nothing (the game result equals a run without it; AiMatchTests).
/// </summary>
public sealed class SliceRecorder(int length = 100)
{
    public int Length { get; } = length;
    public List<Slice> Slices { get; } = [];

    static readonly Owner[] Sides = [Owner.Alliance, Owner.Empire];
    static readonly uint[] Kinds = [40, 41, 42];
    readonly Dictionary<(Owner, uint), (int Days, int Building, int IdleFree)> _use = [];
    readonly Dictionary<Owner, int> _lost = [];
    // per system, its owner and whether it held own fighters directly, at the end of the previous day (the state the
    // day's battles started from; shortcut: fighters arriving on the battle day before the battle are missed)
    Dictionary<StarSystem, (Owner Owner, bool Fighters)> _wings = [];
    readonly Dictionary<(Owner, bool Won), int> _wingBattles = [];

    /// <summary>
    /// Per facility kind (shipyard, training facility, construction yard), the openswr side's facility-days idle with free maintenance, by
    /// the controller's reason at that day's decision (busy at the decision: the order finished later that day).
    /// </summary>
    public Dictionary<(uint Kind, string Reason), int> IdleReasons { get; } = [];

    /// <summary>Refused estimates: per idle facility-day with reason "refused: estimate", that day's refusal of its manager (measurement).</summary>
    public List<OpenSWRController.RefusedEstimate> RefusedDays { get; } = [];

    /// <summary>Every refused estimate of the openswr side (the controller's list, kept at the end of the game).</summary>
    public List<OpenSWRController.RefusedEstimate> RefusedAttempts { get; } = [];

    /// <summary>The openswr side's view age at each covert, attack and assault decision (the controller's list, kept at the end).</summary>
    public List<OpenSWRController.IntelDecision> IntelAges { get; } = [];

    /// <summary>The openswr side's pivots and retries (the controller's lists, kept at the end).</summary>
    public List<OpenSWRController.PivotEvent> PivotEvents { get; } = [];
    public List<OpenSWRController.RetryRecord> RetryRecords { get; } = [];
    /// <summary>Character-led missions sent with fewer decoys than their target.</summary>
    public int FewerDecoys { get; set; }

    /// <summary>The openswr side's losses to troop assault with their warning.</summary>
    public List<OpenSWRController.AssaultSeen> AssaultLossesSeen { get; } = [];

    /// <summary>The assault-loss measurement: the assault losses and their warning; owner changes by cause on days 401-1000.</summary>
    public static string RespondTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        var l = runs.SelectMany(x => x.Recorder.AssaultLossesSeen).ToList();
        var seen = l.Where(x => x.Seen).ToList();
        var inTime = seen.Where(x => x.FleetDays >= 0).ToList();
        string Pct(int n, int of) => of == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * n / of:F1} %");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"openswr systems lost to troop assault: {(double)l.Count / games:F2} per game; seen beforehand {Pct(seen.Count, l.Count)}; mean warning {(seen.Count == 0 ? 0 : seen.Average(x => x.WarningDays)):F1} days (median {(seen.Count == 0 ? 0 : seen.OrderBy(x => x.WarningDays).ElementAt(seen.Count / 2).WarningDays)})"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  an own fleet could have arrived in time: {Pct(inTime.Count, seen.Count)} of the seen ones; that fleet at least as strong as the attacker: {Pct(inTime.Count(x => x.FleetStrength >= x.AttackerStrength), inTime.Count)}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  attacker known strength mean {(seen.Count == 0 ? 0 : seen.Average(x => x.AttackerStrength)):F0}, troops {(seen.Count == 0 ? 0 : seen.Average(x => x.AttackerTroops)):F1}; nearest fleet in time strength mean {(inTime.Count == 0 ? 0 : inTime.Average(x => x.FleetStrength)):F0}, travel {(inTime.Count == 0 ? 0 : inTime.Average(x => x.FleetDays)):F1} days"));
        text.AppendLine("owner changes of the openswr side, days 401-1000, by cause (per game): gained / lost");
        foreach (var cause in Enum.GetValues<ChangeCause>())
        {
            double gained = runs.Sum(x => x.Result.Changes.Count(c => c.Day > 400 && c.Cause == cause && c.To == x.Result.OpenSWR)) / (double)games;
            double lost = runs.Sum(x => x.Result.Changes.Count(c => c.Day > 400 && c.Cause == cause && c.From == x.Result.OpenSWR)) / (double)games;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {cause,-16} {gained,7:F2} / {lost,7:F2}"));
        }
        return text.ToString();
    }

    /// <summary>The controller table: pivots started / ended by reason, retries with and without decoys by outcome.</summary>
    public static string AgentsTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        var pivots = runs.SelectMany(x => x.Recorder.PivotEvents).ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"openswr pivots (controller): started {pivots.Count(p => p.Started)} ({(double)pivots.Count(p => p.Started) / games:F2} per game), ended {pivots.Count(p => !p.Started)}"));
        foreach (var g in pivots.Where(p => !p.Started).GroupBy(p => p.Reason).OrderByDescending(g => g.Count()))
            text.AppendLine($"  ended: {g.Key,-14} {g.Count(),6}");
        foreach (var g in pivots.Where(p => p.Started).GroupBy(p => p.Kind))
            text.AppendLine($"  started: {Match.MissionName(g.Key),-14} {g.Count(),6}");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"openswr character-led missions with fewer decoys than their target (Decoys): {(double)runs.Sum(x => x.Recorder.FewerDecoys) / games:F2} per game"));
        var retries = runs.SelectMany(x => x.Recorder.RetryRecords).ToList();
        text.AppendLine($"openswr retries at foiled targets (controller): {retries.Count} ({(double)retries.Count / games:F2} per game)");
        foreach (var (name, set) in new[] { ("with decoys", retries.Where(r => r.Decoys > 0).ToList()), ("without decoys", retries.Where(r => r.Decoys == 0).ToList()) })
        {
            var ended = set.Where(r => r.Outcome is not null).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {name,-15} {set.Count,6}  ended {ended.Count,6}: success {100.0 * ended.Count(r => r.Outcome == "success") / Math.Max(1, ended.Count),5:F1} %  foiled {100.0 * ended.Count(r => r.Outcome == "foiled") / Math.Max(1, ended.Count),5:F1} %  failed {100.0 * ended.Count(r => r.Outcome == "failed") / Math.Max(1, ended.Count),5:F1} %  decoys {(set.Count == 0 ? 0 : set.Average(r => r.Decoys)):F2}"));
        }
        return text.ToString();
    }

    /// <summary>The openswr side's construction yards per owned system at each slice end.</summary>
    public Dictionary<int, double> YardsPerSystem { get; } = [];
    readonly HashSet<GameObject> _seenLost = [];

    /// <summary>Character losses and missions of both sides.</summary>
    public CharacterRecorder Characters { get; } = new();

    /// <summary>Special forces by type, missions by agent kind, retries.</summary>
    public AgentRecorder Agents { get; } = new();

    /// <summary>The openswr side's no-launch reasons and attack launches (measurement only).</summary>
    public List<OpenSWRController.NoLaunch> NoLaunch { get; } = [];
    public int AttackLaunches { get; set; }

    /// <summary>The no-launch reason table: per day slice the shares of the first no-launch reason (groups and idle fleets), assaults launched and won.</summary>
    public static string NoLaunchTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        var all = runs.SelectMany(x => x.Recorder.NoLaunch).ToList();
        var reasons = all.Select(x => x.Reason).Distinct().OrderBy(x => x).ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"openswr no-launch reasons (per game {(double)all.Count / games:F0} group- or fleet-decisions; share by 200-day slice)"));
        text.AppendLine($"  {"reason",-34}" + string.Concat(new[] { 200, 400, 600, 800, 1000 }.Select(d => $" {"<=" + d,7}")));
        foreach (var r in reasons)
        {
            var line = new StringBuilder($"  {r,-34}");
            foreach (int d in new[] { 200, 400, 600, 800, 1000 })
            {
                var slice = all.Where(x => x.Day > d - 200 && x.Day <= d).ToList();
                line.Append(string.Create(CultureInfo.InvariantCulture, $" {(slice.Count == 0 ? 0 : 100.0 * slice.Count(x => x.Reason == r) / slice.Count),6:F1}%"));
            }
            text.AppendLine(line.ToString());
        }
        double won = runs.Sum(x => x.Result.Changes.Count(c => c.Cause == ChangeCause.Troops && c.To == x.Result.OpenSWR)) / (double)games;
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  attack launches {(double)runs.Sum(x => x.Recorder.AttackLaunches) / games:F1} per game; systems won by troops {won:F2} per game"));
        return text.ToString();
    }

    /// <summary>Measurement: the openswr side's Swarm group decisions (one list per game) and its own troop landings.</summary>
    public List<List<OpenSWRController.SwarmTroopDecision>> TroopRoom { get; } = [];
    public List<OpenSWRController.AssaultLanding> OwnLandings { get; } = [];

    /// <summary>The troop-room table: the "not enough troops loaded" split, groups stuck on room, fleet room, landings against need.</summary>
    public static string TroopRoomTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        var all = runs.SelectMany(x => x.Recorder.TroopRoom).SelectMany(g => g).ToList();
        var shortOf = all.Where(x => x.Short is not null).ToList();
        string Pct(int n, int of) => of == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * n / of:F1} %");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Swarm groups, \"not enough troops loaded\" decisions: {(double)shortOf.Count / games:F1} per game"));
        foreach (var (k, name) in new[] { ('a', "(a) fleet room below the target's need (never launches there)"), ('b', "(b) room enough, no spare troops at the base"), ('c', "(c) other (troops on the way, room enough)") })
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-62} {(double)shortOf.Count(x => x.Short == k) / games,8:F1} per game {Pct(shortOf.Count(x => x.Short == k), shortOf.Count),8}"));
        var stuck = runs.SelectMany(x => x.Recorder.TroopRoom).SelectMany(g => g.Where(x => x.Short == 'a').GroupBy(x => (x.Fleet, x.Target))).ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  groups stuck in (a): {(double)stuck.Count / games:F2} per game, mean {(stuck.Count == 0 ? 0 : stuck.Average(s => s.Count())):F1} decision days stuck"));
        var a = all.Where(x => x.Short == 'a').ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  fleets in (a): capital ships {(a.Count == 0 ? 0 : a.Average(x => x.Ships)):F2}, room {(a.Count == 0 ? 0 : a.Average(x => x.Room)):F2}, need {(a.Count == 0 ? 0 : a.Average(x => x.Need)):F2}; all Swarm group decisions: capital ships {(all.Count == 0 ? 0 : all.Average(x => x.Ships)):F2}, room {(all.Count == 0 ? 0 : all.Average(x => x.Room)):F2}, need {(all.Count == 0 ? 0 : all.Average(x => x.Need)):F2}"));
        var l = runs.SelectMany(x => x.Recorder.OwnLandings).Where(x => x.Won is not null).ToList();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"own troop landings: {(double)l.Count / games:F2} per game, landed {(l.Count == 0 ? 0 : l.Average(x => x.Landed)):F2} against need {(l.Count == 0 ? 0 : l.Average(x => x.Need)):F2}; won {Pct(l.Count(x => x.Won == true), l.Count)}"));
        foreach (var (name, set) in new[] { ("landed >= need", l.Where(x => x.Landed >= x.Need).ToList()), ("landed < need", l.Where(x => x.Landed < x.Need).ToList()) })
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-15} {(double)set.Count / games,6:F2} per game  won {set.Count(x => x.Won == true),5} / lost {set.Count(x => x.Won == false),5}  ({Pct(set.Count(x => x.Won == true), set.Count)} won)"));
        return text.ToString();
    }

    /// <summary>
    /// Own systems lost to uprising, by the days since the openswr side last took them (0-30, 31-100, 101-300,
    /// over 300), or never taken (held since the start or gained otherwise without an owner change to it on record).
    /// </summary>
    public static string UprisingAgeTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, results.Count);
        var buckets = new[] { "0-30", "31-100", "101-300", ">300", "never taken" };
        var counts = buckets.ToDictionary(b => b, _ => 0);
        foreach (var r in results)
            foreach (var c in r.Changes.Where(c => c.Cause == ChangeCause.Uprising && c.From == r.OpenSWR))
            {
                var taken = r.Changes.LastOrDefault(x => x.System == c.System && x.To == r.OpenSWR && x.Day <= c.Day);
                string b = taken is null ? "never taken" : (c.Day - taken.Day) switch { <= 30 => "0-30", <= 100 => "31-100", <= 300 => "101-300", _ => ">300" };
                counts[b]++;
            }
        int total = counts.Values.Sum();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"openswr systems lost to uprising, by days since taken (per game; {(double)total / games:F2} in all)"));
        foreach (var b in buckets)
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {b,-12} {(double)counts[b] / games,7:F2} {(total == 0 ? 0 : 100.0 * counts[b] / total),6:F1} %"));
        return text.ToString();
    }

    /// <summary>The fleet shape of both sides (measurement only).</summary>
    public FleetShapeRecorder FleetShape { get; } = new();

    /// <summary>Measurement: own systems lost to uprising and their history.</summary>
    public UprisingRecorder Uprisings { get; } = new();

    /// <summary>Measurement: bombardments of both sides.</summary>
    public BombardRecorder Bombard { get; } = new();

    /// <summary>Measurement: the victory objectives.</summary>
    public ObjectivesRecorder Objectives { get; } = new();

    /// <summary>Idle special forces and why (measurement only).</summary>
    public IdleProbeRecorder IdleProbes { get; } = new();

    /// <summary>Called once at the end of every game day.</summary>
    public void Day(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        Characters.Day(game, changes);
        Agents.Day(game, Length);
        FleetShape.Day(game);
        Uprisings.Day(game, changes);
        Bombard.Day(game, changes);
        Objectives.Day(game, changes, Bombard);
        IdleProbes.Day(game);
        Audit(game, changes);
        foreach (var side in Sides)
        {
            int free = game.Sides[side].MaintenanceCapacity - game.Sides[side].MaintenanceAllocated;
            foreach (var f in game.Facilities.Where(f => f.Owner == side && f.Usable && Kinds.Contains(Family.Of(f.Type))))
            {
                var key = (side, Family.Of(f.Type));
                var (d, b, i) = _use.GetValueOrDefault(key);
                bool building = f.State is FacilityState.Processing or FacilityState.Waiting;
                _use[key] = (d + 1, b + (building ? 1 : 0), i + (!building && free > 0 ? 1 : 0));
                if (!building && free > 0 && game.Controllers.GetValueOrDefault(side) is OpenSWRController { IdleReasons: { } reasons } && f.Container is StarSystem at)
                {
                    var kind = Family.Of(f.Type) switch { 40 => ManufacturingKind.Ship, 41 => ManufacturingKind.Training, _ => ManufacturingKind.Construction };
                    var m = ScreenViews.Managers(at, side).FirstOrDefault(x => x.Kind == kind);
                    string reason = m is null ? "no manager" : reasons.TryGetValue(m, out var r) ? (r == "busy" ? "busy at decision" : r) : "no decision";
                    if (reason == "refused: estimate" && game.Controllers[side] is OpenSWRController { RefusedEstimates: { } refused }
                        && refused.LastOrDefault(x => x.Manager == m && x.Day == game.Day) is { } today)
                        RefusedDays.Add(today);
                    var rk = (Family.Of(f.Type), reason);
                    IdleReasons[rk] = IdleReasons.GetValueOrDefault(rk) + 1;
                }
            }
        }
        foreach (var o in game.Created.Where(o => o.Destroyed && o.DestroyedReason == DestroyedReason.Battle && Family.IsCapitalShip(o.Type) && !_seenLost.Contains(o)))
        {
            _seenLost.Add(o);
            if (o.Owner is Owner.Alliance or Owner.Empire) _lost[o.Owner] = _lost.GetValueOrDefault(o.Owner) + game.MaintenanceCost(o.Type);
        }
        foreach (var b in (game.BattleTrace ?? []).Where(b => b.Day == game.Day))
            if (_wings.TryGetValue(b.System, out var w) && w.Fighters && b.Sides.Contains(w.Owner) && b.Won is Owner.Alliance or Owner.Empire)
                _wingBattles[(w.Owner, b.Won == w.Owner)] = _wingBattles.GetValueOrDefault((w.Owner, b.Won == w.Owner)) + 1;
        _wings = game.Systems.ToDictionary(s => s, s => (s.Owner, s.Contents.Any(x => x.Owner == s.Owner && x.Usable && Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters)));
        if (game.Day % Length == 0 && game.Day > 0) Close(game, changes);
    }

    // weakness audit: per-slice event counts by (side, column), and the game-long state they need
    readonly Dictionary<(Owner, string), int> _audit = [];
    readonly Dictionary<GameObject, StarSystem?> _garrison = []; // Existing troops: the system they sat in directly at the last day end
    readonly List<(int Day, Owner Victim, uint System)> _sabotaged = [];
    readonly HashSet<GameObject> _auditSeen = [], _autoscrap = [];
    readonly Dictionary<Owner, int> _hqFound = [], _lostBattle = [];
    readonly Dictionary<Character, bool> _exposed = []; // major characters moving or on a mission to a target without own capital ships, last day end
    int _auditCreated, _auditBattles, _auditStrikes, _auditChanges;
    void Add(Owner side, string column, int n = 1) { if (side is Owner.Alliance or Owner.Empire) _audit[(side, column)] = _audit.GetValueOrDefault((side, column)) + n; }

    static bool OwnCapitalAt(StarSystem s, Owner side) =>
        s.Contents.OfType<Fleet>().Any(f => f.Owner == side && f.Existing && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing));

    /// <summary>
    /// Weakness audit: the day's audit events. An observer like the rest (reads, draws nothing).
    /// Inferred classifications: sabotage = DestroyedReason 6, maintenance scrap = the autoscrap flag, a battle withdrawal = a lost battle that own
    /// capital ships survived; units counted at the battle start (BattleReport.Units).
    /// </summary>
    void Audit(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        // destroyed units: sabotaged garrison troops (directly in a system at the last day end), maintenance scrap
        foreach (var o in game.Created.Where(o => o.Destroyed && !_auditSeen.Contains(o)).ToList())
        {
            _auditSeen.Add(o);
            if (o.DestroyedReason == DestroyedReason.Sabotage && Family.IsTroop(o.Type) && _garrison.GetValueOrDefault(o) is { } at)
            {
                Add(o.Owner, "garrison_sabotaged");
                _sabotaged.Add((game.Day, o.Owner, at.Ref));
            }
        }
        // maintenance shortfall: the sim only flags the autoscrap pick (Economy.Autoscrap; the destruction is not modelled), so the
        // column counts capital ships and fighters newly flagged
        foreach (var o in game.Created.Where(o => o.AutoscrapRequested && Family.Of(o.Type) is >= Families.CapitalShip and < Families.Headquarters && _autoscrap.Add(o)))
            Add(o.Owner, "ships_lost_maintenance");
        _garrison.Clear();
        foreach (var o in game.Created.Where(o => Family.IsTroop(o.Type) && o.Existing)) _garrison[o] = o.Container as StarSystem;
        // owner changes without an assault, lost by the side (uprising, diplomacy, abandonment, other)
        for (; _auditChanges < changes.Count; _auditChanges++)
        {
            var c = changes[_auditChanges];
            if (c.Cause is ChangeCause.Troops or ChangeCause.Destruction || c.From is not (Owner.Alliance or Owner.Empire)) continue;
            Add(c.From, "flipped_by_support");
            if (_sabotaged.Any(x => x.Victim == c.From && x.System == c.System && c.Day - x.Day <= 60)) Add(c.From, "flipped_after_sabotage");
        }
        // bombardments of the side's systems: its troops lost; none of its shields, guns or fighters there before the strike
        for (; _auditStrikes < Bombard.Strikes.Count; _auditStrikes++)
        {
            var st = Bombard.Strikes[_auditStrikes];
            Add(st.Defender, "bombard_troops_lost", st.Troops);
            if (st.Undefended) Add(st.Defender, "bombard_undefended");
        }
        // battles: a lost one opens the rebuild window; a withdrawal against more enemy fighters than capital ships
        var battles = game.BattleTrace ?? [];
        for (; _auditBattles < battles.Count; _auditBattles++)
        {
            var b = battles[_auditBattles];
            if (b.Won is not (Owner.Alliance or Owner.Empire)) continue;
            foreach (var side in Sides.Where(x => b.Sides.Contains(x) && x != b.Won))
            {
                _lostBattle[side] = b.Day;
                var units = b.Units ?? [];
                if (units.Any(u => u.Owner == side && Family.IsCapitalShip(u.Type) && !u.Destroyed)
                    && units.Count(u => u.Owner == b.Won && Family.Of(u.Type) is >= Families.Fighter and < Families.Headquarters) > units.Count(u => u.Owner == b.Won && Family.IsCapitalShip(u.Type)))
                    Add(side, "retreats_vs_fighters");
            }
        }
        // capital ships ordered (created) within 100 days after a lost battle: their maintenance
        for (; _auditCreated < game.Created.Count; _auditCreated++)
        {
            var o = game.Created[_auditCreated];
            if (Family.IsCapitalShip(o.Type) && _lostBattle.TryGetValue(o.Owner, out int lost) && game.Day - lost <= 100)
                Add(o.Owner, "rebuilt_100d", game.MaintenanceCost(o.Type));
        }
        // major characters captured or killed while exposed at the last day end
        foreach (var c in game.Characters.Where(c => Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter))
        {
            if (_exposed.GetValueOrDefault(c) && (c.Captured || !c.Existing)) Add(c.Owner, "leaders_taken_moving");
            bool exposed = false;
            if (c.Existing && !c.Captured)
            {
                var m = game.Missions.FirstOrDefault(m => !m.Deleted && m.CompletionStatus == MissionCompletion.None && (m.Team.Contains(c) || m.Decoys.Contains(c)));
                if (m is not null || c.Enroute)
                {
                    var target = m?.TargetLocation?.System ?? c.Destination?.System;
                    exposed = target is null || !OwnCapitalAt(target, c.Owner);
                }
            }
            _exposed[c] = exposed;
        }
        foreach (var side in Sides)
        {
            // fleets with capital ships in a system more than ReachDays (speed 100) from every own system; in transit not counted
            var own = game.Systems.Where(s => s.Owner == side && !s.Destroyed).ToList();
            foreach (var f in game.Fleets.Where(f => f.Owner == side && f.Existing && f.Container is StarSystem && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing)))
                if (own.All(s => Orders.TravelDays(game, s, (StarSystem)f.Container!, 100) > OpenSWRController.ReachDays)) Add(side, "chase_days");
            // the enemy HQ in the side's views: the Alliance HQ object for the Empire; Coruscant (always on the map) for the Alliance
            if (_hqFound.ContainsKey(side)) continue;
            if (side == Owner.Alliance) _hqFound[side] = game.Day;
            else if (game.Created.Where(o => Family.Of(o.Type) is >= Families.Headquarters and < Families.IonCannon && !o.Destroyed).MinBy(o => o.Key) is { System: { } hqAt } hq
                     && Knowledge.Location(game, hq, Owner.Empire) == hqAt)
                _hqFound[side] = game.Day;
        }
    }

    void Close(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        int from = game.Day - Length;
        var figures = Sides.ToDictionary(s => s, _ => new SliceFigures());
        int all = game.Systems.Count(s => !s.Destroyed && s.Owner is Owner.Alliance or Owner.Empire);
        foreach (var side in Sides)
        {
            var x = figures[side];
            var state = game.Sides[side];
            int systems = game.Systems.Count(s => s.Owner == side && !s.Destroyed);
            x["systems"] = systems;
            x["share_pct"] = all == 0 ? 0 : 100.0 * systems / all;
            x["maint_capacity"] = state.MaintenanceCapacity;
            x["maint_used"] = state.MaintenanceAllocated;
            x["raw_stock"] = state.RawStock;
            x["refined_stock"] = state.RefinedStock;
            int Facilities(uint family) => game.Facilities.Count(f => f.Owner == side && f.Usable && Family.Of(f.Type) == family);
            x["mines"] = Facilities(44);
            x["refineries"] = Facilities(45);
            x["construction_yards"] = Facilities(42);
            x["shipyards"] = Facilities(40);
            x["training_facilities"] = Facilities(41);
            var ships = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == side && f.Existing)
                .SelectMany(f => f.Contents).Where(o => Family.IsCapitalShip(o.Type) && o.Existing).ToList();
            x["capital_maint"] = ships.Sum(o => game.MaintenanceCost(o.Type));
            // the version 8 weighted gun mix: anti-ship + anti-fighter rate of each ship
            x["capital_attack"] = ships.Sum(o => Roles.RawScores(game, o.Type) is { } r ? r[UnitRole.Line] + r[UnitRole.Screen] : 0);
            var units = game.Created.Where(o => o.Owner == side && o.Existing).ToList();
            x["fighters"] = units.Count(o => Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters);
            x["fighters_in_systems"] = units.Count(o => Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters && o.Container is StarSystem);
            x["fighters_in_hangars"] = units.Count(o => Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters && o.Container is { } c && Family.IsCapitalShip(c.Type));
            x["wing_battles_won"] = _wingBattles.GetValueOrDefault((side, true));
            x["wing_battles_lost"] = _wingBattles.GetValueOrDefault((side, false));
            x["troops"] = units.Count(o => Family.IsTroop(o.Type));
            var defences = game.Systems.SelectMany(s => s.Contents).Where(o => o.Owner == side && o.Usable && Roles.KindOf(o.Type) == UnitKind.Defence).ToList();
            x["guns"] = defences.Count(o => Roles.RawScores(game, o.Type)![UnitRole.Gun] > 0);
            x["shields"] = defences.Count(o => Roles.RawScores(game, o.Type)![UnitRole.Shield] > 0);
            foreach (var (kind, name) in new[] { (40u, "shipyard"), (41u, "training"), (42u, "construction") })
            {
                var (d, b, i) = _use.GetValueOrDefault((side, kind));
                x[$"{name}_building_pct"] = d == 0 ? 0 : 100.0 * b / d;
                x[$"{name}_idle_free_pct"] = d == 0 ? 0 : 100.0 * i / d;
            }
            foreach (var c in changes.Where(c => c.Day > from && c.Day <= game.Day))
            {
                if (c.To == side) x[$"gained_{c.Cause.ToString().ToLowerInvariant()}"]++;
                if (c.From == side) x[$"lost_{c.Cause.ToString().ToLowerInvariant()}"]++;
            }
            var battles = (game.BattleTrace ?? []).Where(b => b.Day > from && b.Day <= game.Day && b.Sides.Contains(side)).ToList();
            x["battles"] = battles.Count;
            x["battles_won"] = battles.Count(b => b.Won == side);
            x["battles_lost"] = battles.Count(b => b.Won is Owner.Alliance or Owner.Empire && b.Won != side);
            x["capital_maint_lost"] = _lost.GetValueOrDefault(side);
            var characters = game.Characters.Where(c => c.Owner == side).ToList();
            x["characters_on_mission"] = characters.Count(c => c.Existing && !c.Captured && c.OnMission);
            x["characters_idle"] = characters.Count(c => c.Existing && !c.Captured && !c.OnMission);
            x["characters_captured_or_dead"] = characters.Count(c => !c.Existing || c.Captured);
            // The known enemy systems in reach and the share with a fresh view (Intel's measure, both sides)
            var (fresh, reach) = OpenSWRController.FreshShare(game, side);
            x["enemy_in_reach"] = reach;
            x["fresh_view_pct"] = reach == 0 ? 0 : 100.0 * fresh / reach;
            foreach (var name in new[] { "flipped_by_support", "flipped_after_sabotage", "garrison_sabotaged", "bombard_troops_lost", "bombard_undefended",
                         "leaders_taken_moving", "ships_lost_maintenance", "rebuilt_100d", "retreats_vs_fighters", "chase_days" })
                x[name] = _audit.GetValueOrDefault((side, name));
            var rim = game.Systems.Where(s => s.Owner == side && !s.Destroyed && !s.IsCore).ToList();
            x["rim_owned"] = rim.Count;
            x["rim_unpopulated_owned"] = rim.Count(s => !s.Populated);
            x["hq_found_day"] = _hqFound.GetValueOrDefault(side); // 0 = not yet found
        }
        Slices.Add(new(game.Day, figures[Owner.Alliance], figures[Owner.Empire]));
        foreach (var side in Sides.Where(x => game.Controllers.GetValueOrDefault(x) is OpenSWRController))
            YardsPerSystem[game.Day] = figures[side]["systems"] == 0 ? 0 : figures[side]["construction_yards"] / figures[side]["systems"];
        _use.Clear();
        _lost.Clear();
        _wingBattles.Clear();
        _audit.Clear();
    }

    /// <summary>The facility-kind table: per facility kind, the share of each idle reason over the games, and the yards per owned system.</summary>
    public static string ReasonTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        text.AppendLine("openswr idle facility-days with free maintenance, by the reason at that day's decision (all games)");
        foreach (var (kind, name) in new[] { (42u, "construction yards"), (40u, "shipyards"), (41u, "training facilities") })
        {
            var counts = runs.SelectMany(x => x.Recorder.IdleReasons).Where(e => e.Key.Kind == kind)
                .GroupBy(e => e.Key.Reason).Select(g => (Reason: g.Key, Days: g.Sum(e => e.Value))).OrderByDescending(x => x.Days).ToList();
            int total = counts.Sum(x => x.Days);
            text.AppendLine($"  {name}: {total} facility-days");
            foreach (var (reason, d) in counts)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    {reason,-55} {d,9} {100.0 * d / Math.Max(1, total),6:F1} %"));
        }
        text.AppendLine("  openswr construction yards per owned system (average per game)");
        foreach (int day in new[] { 100, 300, 500 })
        {
            var v = runs.Where(x => x.Recorder.YardsPerSystem.ContainsKey(day)).Select(x => x.Recorder.YardsPerSystem[day]).ToList();
            if (v.Count > 0) text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    day {day,4}: {v.Average():F3}"));
        }
        return text.ToString();
    }

    /// <summary>The refused-estimate tables (measurement): codes by attempts and by facility-days, AI free - real free, top (product, code) pairs.</summary>
    public static string RefusalTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        var attempts = runs.SelectMany(x => x.Recorder.RefusedAttempts).ToList();
        var days = runs.SelectMany(x => x.Recorder.RefusedDays).ToList();
        static string Code(OpenSWRController.RefusedEstimate x) =>
            x.Validation.Success ? (x.EstimateNull ? "valid, estimate days null" : x.MaintenanceOver ? "valid, maintenance > AI free" : "valid, other")
            : $"({(int)x.Validation.Category}, {(int)x.Validation.Code}){(x.MaintenanceOver ? " + maintenance > AI free" : "")}";
        text.AppendLine($"refused estimates of the openswr side: {attempts.Count} order attempts, {days.Count} idle facility-days");
        text.AppendLine($"  {"code",-44} {"attempts",9} {"%",6} {"fac-days",9} {"%",6}");
        foreach (var g in attempts.GroupBy(Code).OrderByDescending(g => g.Count()))
        {
            int d = days.Count(x => Code(x) == g.Key);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {g.Key,-44} {g.Count(),9} {100.0 * g.Count() / Math.Max(1, attempts.Count),6:F1} {d,9} {100.0 * d / Math.Max(1, days.Count),6:F1}"));
        }
        var diff = attempts.Select(x => x.AiFree - x.RealFree).Order().ToList();
        if (diff.Count > 0)
            text.AppendLine($"  AI free − real free: min {diff[0]}, median {diff[diff.Count / 2]}, max {diff[^1]}; zero in {diff.Count(x => x == 0)} of {diff.Count}");
        text.AppendLine($"  same system as TargetKey {attempts.Count(x => x.SameSystem)} of {attempts.Count}; TargetKey owned by the side {attempts.Count(x => x.TargetOwner == x.Manager.Owner)}; queue non-empty {attempts.Count(x => x.QueueNonEmpty)}");
        text.AppendLine("  top 10 (product, code) by attempts:");
        foreach (var g in attempts.GroupBy(x => (x.Product, Code: Code(x))).OrderByDescending(g => g.Count()).Take(10))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    {Family.Of(g.Key.Product)}:{SdRef.IndexOf(g.Key.Product)} {g.Key.Code,-44} {g.Count(),8}  count n {g.Min(x => x.Count)}–{g.Max(x => x.Count)}, AI free {g.Min(x => x.AiFree)}–{g.Max(x => x.AiFree)}"));
        return text.ToString();
    }

    /// <summary>The fresh-view table: per side played and slice, the mean fresh-view share of both sides; the view age at the openswr decisions.</summary>
    public static string IntelTable(IReadOnlyList<(GameResult Result, SliceRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        var results = runs.Select(x => x.Result).Where(r => r.Slices is not null).ToList();
        var days = results.SelectMany(r => r.Slices!).Select(s => s.Day).Distinct().Order().ToList();
        text.AppendLine("fresh views (share of known enemy systems in reach with a view updated within FreshDays; mean per game)");
        text.AppendLine($"  {"",-28}" + string.Concat(days.Select(d => $" {d,7}")));
        foreach (var played in Sides)
            foreach (var (who, side) in new[] { ("openswr", played), ("original", played == Owner.Alliance ? Owner.Empire : Owner.Alliance) })
            {
                var games = results.Where(r => r.OpenSWR == played).ToList();
                if (games.Count == 0) continue;
                var line = new StringBuilder($"  {who + " (openswr " + played + ")",-28}");
                foreach (int day in days)
                {
                    var at = games.Select(r => r.Slices!.FirstOrDefault(s => s.Day == day)).OfType<Slice>().ToList();
                    line.Append(string.Create(CultureInfo.InvariantCulture, $" {(at.Count == 0 ? 0 : at.Average(s => s.Of(side)["fresh_view_pct"])),7:0.0}"));
                }
                text.AppendLine(line.ToString());
            }
        var ages = runs.SelectMany(x => x.Recorder.IntelAges).ToList();
        text.AppendLine("openswr view age at its decisions (days)");
        foreach (var g in ages.GroupBy(a => a.Decision).OrderBy(g => g.Key))
        {
            var v = g.Select(a => a.Age).Order().ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {g.Key,-8} {v.Count,7} decisions  mean {v.Average(),6:F1}  median {v[v.Count / 2],4}  fresh (<= {OpenSWRController.FreshDays}) {100.0 * v.Count(a => a <= OpenSWRController.FreshDays) / v.Count,5:F1} %"));
        }
        return text.ToString();
    }

    public static string CsvHeader => "game,seed,openswr,day,side," + string.Join(",", SliceFigures.Names);

    /// <summary>Two CSV lines per slice (Alliance, Empire).</summary>
    public static IEnumerable<string> Csv(GameResult r)
    {
        foreach (var s in r.Slices ?? [])
            foreach (var side in Sides)
                yield return string.Join(",", new[] { $"{r.Game}", $"{r.Seed}", $"{r.OpenSWR}", $"{s.Day}", $"{side}" }
                    .Concat(s.Of(side).Values.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// The console table: per figure and slice, the openswr side minus the original side, averaged over the games, split by the
    /// side the OpenSWR AI played.
    /// </summary>
    public static string DiffTable(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        var days = results.SelectMany(r => r.Slices ?? []).Select(s => s.Day).Distinct().Order().ToList();
        if (days.Count == 0) return "";
        text.AppendLine("time slices: openswr minus original (average per game), by the side the OpenSWR AI played");
        foreach (var played in Sides)
        {
            var games = results.Where(r => r.OpenSWR == played && r.Slices is not null).ToList();
            if (games.Count == 0) continue;
            text.AppendLine($"  openswr as {played} ({games.Count} games)");
            text.AppendLine($"  {"figure",-28}" + string.Concat(days.Select(d => $" {d,7}")));
            for (int i = 0; i < SliceFigures.Names.Length; i++)
            {
                var line = new StringBuilder($"  {SliceFigures.Names[i],-28}");
                foreach (int day in days)
                {
                    var at = games.Select(r => r.Slices!.FirstOrDefault(s => s.Day == day)).OfType<Slice>().ToList();
                    double diff = at.Count == 0 ? 0 : at.Average(s => s.Of(played).Values[i] - s.Of(played == Owner.Alliance ? Owner.Empire : Owner.Alliance).Values[i]);
                    line.Append(string.Create(CultureInfo.InvariantCulture, $" {diff,7:0.#}"));
                }
                text.AppendLine(line.ToString());
            }
        }
        return text.ToString();
    }
}
