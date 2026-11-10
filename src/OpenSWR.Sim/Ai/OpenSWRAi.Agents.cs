using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Agents: special-force capabilities from the masks, a broad pool of every buildable type, the pivot to a
/// vulnerable major character, retries only with decoys, ranking by chance then worth, a mission budget, the agent by worth with
/// special-force decoys for character agents. Views only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>The pool target and the cap, as shares of the maintenance capacity.</summary>
    public const double AgentPool = 0.03, AgentShare = 0.05;
    /// <summary>A pivot lasts at most this many days.</summary>
    public const int PivotDays = 60;
    /// <summary>Running sabotage / incite missions at most (Agents).</summary>
    public const int MaxSabotage = 2, MaxIncite = 1;

    /// <summary>Teams rules in use: the Teams flag, or Agents (which runs covert missions with special forces too).</summary>
    bool TeamsOn => On(V7.Teams) || On(V7.Agents);

    /// <summary>The mission kinds a special force may serve in (espionage, reconnaissance and the covert kinds).</summary>
    static readonly uint[] AgentKinds = [0x52, 0x54, 0x56, 0x61, 0x62, 0x63, 0x69];

    /// <summary>
    /// Rule 1: the mission kinds whose playable type accepts the special-force type for the side (MissionMask within the mission's
    /// SpecialForceMask, the side's flag; as <see cref="MissionEligibility"/>). Never by name.
    /// </summary>
    internal static List<uint> KindsFor(GameState game, Owner side, SpecialForceRecord f) =>
        (side == Owner.Alliance ? f.Alliance : f.Empire) == 0 || f.MissionMask == 0 ? []
        : [.. AgentKinds.Where(kind => TypeOf(game, kind) is { } m && m.AllowsSpecialForce(f.MissionMask))];

    /// <summary>A special-force type's skill for a kind.</summary>
    static int SkillFor(SpecialForceRecord f, uint kind) => kind switch
    {
        0x52 or 0x54 => (int)f.Skills.Espionage.Base,
        0x56 => (int)f.Skills.Leadership.Base,
        0x69 => (int)(f.Skills.Espionage.Base + f.Skills.Combat.Base) / 2,
        _ => (int)f.Skills.Combat.Base,
    };

    /// <summary>One buildable own special-force type: its record, the kinds it serves, its pool weight (espionage or reconnaissance 2, else 1).</summary>
    internal sealed record AgentType(SpecialForceRecord Record, List<uint> Kinds)
    {
        public int Weight => Kinds.Contains(0x52u) || Kinds.Contains(0x54u) ? 2 : 1;
        public bool Espionage => Kinds.Contains(0x52u);
    }

    /// <summary>Rule 1: the buildable own special-force types with their kinds, in ascending ref order.</summary>
    internal List<AgentType> AgentTypes(GameState game)
    {
        var buildable = Research.Buildable(game, Side, 0x29).Where(r => Family.IsSpecialForce(r.Common.Ref)).Select(r => r.Common.Ref).ToHashSet();
        return [.. game.Definitions.SpecialForces.Where(f => buildable.Contains(f.Common.Ref)).OrderBy(f => f.Common.Ref)
            .Select(f => new AgentType(f, KindsFor(game, Side, f))).Where(t => t.Kinds.Count > 0)];
    }

    /// <summary>
    /// Rule 2: the per-type shortfall of the pool: target maintenance = <see cref="AgentPool"/> x capacity, shared by
    /// weight; a pivot adds its team to the abduction / assassination types; the decoy shortfall of the last decision is shared
    /// by weight too. Order: espionage types first, then the largest shortfall, then ref. The total stays within <see cref="AgentShare"/>.
    /// </summary>
    internal List<(AgentType Type, int Missing)> PoolShortfall(GameState game)
    {
        var types = AgentTypes(game);
        if (types.Count == 0) return [];
        int capacity = game.Sides[Side].MaintenanceCapacity;
        var forces = SpecialForces(game);
        int used = forces.Sum(a => game.MaintenanceCost(a.Type));
        int room = (int)(AgentShare * capacity) - used;
        if (room <= 0) return [];
        double target = AgentPool * capacity;
        int weights = types.Sum(t => t.Weight);
        // PoolUseful: only useful types are counted and trained; each keeps its share of the full target (Inferred)
        if (On(V7.PoolUseful))
        {
            bool unexplored = UnexploredInReach(game, Sectors(game));
            types = [.. types.Where(t => Useful(t, game.Day, unexplored))];
        }
        var result = new List<(AgentType Type, int Missing)>();
        foreach (var t in types)
        {
            int cost = Math.Max(1, (int)t.Record.MaintenanceCost);
            double share = target * t.Weight / weights;
            int want = (int)Math.Ceiling(share / cost);
            want += (int)Math.Ceiling((double)_decoyShort * t.Weight / weights); // any type can be a decoy: the shortfall by weight
            foreach (var p in _pivots.Where(p => t.Kinds.Contains(p.Kind))) want += p.Team;
            int have = forces.Count(a => a.Type == t.Record.Common.Ref);
            if (want > have) result.Add((t, want - have));
        }
        var ordered = result.OrderBy(x => x.Type.Espionage ? 0 : 1).ThenByDescending(x => x.Missing).ThenBy(x => x.Type.Record.Common.Ref).ToList();
        // the cap: the shortfalls in order until the room is used
        var capped = new List<(AgentType Type, int Missing)>();
        foreach (var (t, missing) in ordered)
        {
            int cost = Math.Max(1, (int)t.Record.MaintenanceCost);
            int n = Math.Min(missing, room / cost);
            if (n <= 0) break;
            capped.Add((t, n));
            room -= n * cost;
        }
        return capped;
    }

    /// <summary>
    /// Rule 2 at one training facility: the first short type not yet ordered this decision (so the facilities share the types,
    /// espionage first); true when an order was placed.
    /// </summary>
    bool TrainAgents(GameState game, ManufacturingManager m, ref int free, HashSet<GameObject> used, HashSet<uint> ordered)
    {
        var shortfall = PoolShortfall(game);
        if (shortfall.Count == 0) return false;
        var (type, missing) = shortfall.FirstOrDefault(x => !ordered.Contains(x.Type.Record.Common.Ref)) is { Type: not null } x ? x : shortfall[0];
        if (game.Definitions.Resolve(type.Record.Common.Ref) is not { } product) return false;
        int built = Build(game, 0x212, m, product, missing, ref free, used);
        if (built <= 0) return false;
        ordered.Add(type.Record.Common.Ref);
        NoteTrained(game); // measurement only
        Why(m, "ordered");
        return true;
    }

    /// <summary>Rule 6: espionage on enemy systems running at most one per enemy sector in reach, plus one per attack group and planned covert target system.</summary>
    internal int SpyBudget(GameState game, SectorMap map) =>
        IntelReach(game, map).Select(s => s.Sector).Distinct().Count() * (EspionageIdleOn(game) ? 2 : 1) + _groups.Count + _plannedAt.Count; // EspionageIdle: 2 per sector

    /// <summary>Rule 6 for one espionage at a system the side does not own: within the budget, and not seen within <see cref="CoverDays"/> unless a target is planned there.</summary>
    string? EspionageBlock(GameState game, StarSystem target, List<Mission> running, int sent, int budget)
    {
        int spying = running.Count(m => m.Kind == 0x52 && (m.TargetLocation ?? m.Objective) is StarSystem s && Knowledge.See(game, s, Side).Owner != Side) + sent;
        if (spying >= budget) return "budget";
        int window = EspionageIdleOn(game) ? CoverDays / 2 : CoverDays; // EspionageIdle
        return ViewAge(game, target) > window || _plannedAt.Contains(target.Ref) || Targets().Contains(target) ? null : "coverdays";
    }

    /// <summary>The decoys missing at the last decision (rule 4); saved.</summary>
    int _decoyShort;

    /// <summary>Targets of foiled own missions (rule 4, from the side's own 0x1F1 messages; Agents only; saved).</summary>
    readonly SortedSet<uint> _foiledTargets = [];

    /// <summary>Test hook / measurement: the decoys missing at the last decision.</summary>
    internal int DecoyShort { get => _decoyShort; set => _decoyShort = value; }

    /// <summary>Test hook: a foiled own mission at the target.</summary>
    internal void MarkFoiledTarget(GameObject target) => _foiledTargets.Add(target.Ref);

    /// <summary>The systems of the last decision's covert candidates (rule 6: espionage planned there); saved.</summary>
    readonly SortedSet<uint> _plannedAt = [];
    internal IReadOnlyCollection<uint> PlannedAt => _plannedAt;

    /// <summary>A running pivot (rule 3): kind, target, the target's system at the start, start day, team (with decoys).</summary>
    internal sealed record Pivot(uint Kind, uint Target, uint System, int Start, int Team);
    readonly List<Pivot> _pivots = [];
    internal IReadOnlyList<Pivot> Pivots => _pivots;

    /// <summary>Pivot measurement (aimatch): started / ended with the reason; null = off.</summary>
    public sealed record PivotEvent(int Day, uint Kind, uint Target, bool Started, string Reason);
    public List<PivotEvent>? PivotEvents { get; set; }

    /// <summary>Retry measurement (aimatch): a covert mission at a target with an earlier foil, its decoys, and its outcome (null while running); null = off.</summary>
    public sealed class RetryRecord(int day, uint kind, uint target, int decoys)
    {
        public int Day { get; } = day;
        public uint Kind { get; } = kind;
        public uint Target { get; } = target;
        public int Decoys { get; } = decoys;
        public bool Succeeded { get; set; }
        public string? Outcome { get; set; }
    }
    public List<RetryRecord>? RetryRecords { get; set; }

    /// <summary>A major character (family 0x30-0x37).</summary>
    static bool Major(GameObject o) => o is Character && Family.Of(o.Type) is >= 0x30 and < 0x38;

    /// <summary>Rule 5's success band: 0 for at least 60 %, 1 for 40-60 %, 2 below.</summary>
    internal static int Band(double success) => success >= 0.6 ? 0 : success >= 0.4 ? 1 : 2;

    /// <summary>Rule 5: by success band, then worth (then kind and target key).</summary>
    internal static List<Covert> Rank(IEnumerable<Covert> candidates) =>
        [.. candidates.OrderBy(x => Band(x.Success)).ThenByDescending(x => x.Worth).ThenBy(x => x.Kind).ThenBy(x => x.Target.Key)];

    /// <summary>
    /// The top covert band (rules 3 and 6): worth at least the upper quartile of the decision's candidates (Inferred: the doc names
    /// the band, not its edge).
    /// </summary>
    static double TopWorth(IReadOnlyList<Covert> candidates)
    {
        var w = candidates.Select(x => x.Worth).Order().ToList();
        return w.Count == 0 ? double.MaxValue : w[Math.Min(w.Count - 1, 3 * w.Count / 4)];
    }

    /// <summary>
    /// Rule 3: an abduction or assassination candidate against a major character with success (decoys planned) at least the
    /// minimum chance and worth in the top band starts a pivot (one per target).
    /// </summary>
    internal void StartPivots(GameState game, IReadOnlyList<Covert> candidates)
    {
        double top = TopWorth(candidates);
        foreach (var x in candidates.Where(x => x.Kind is 0x62 or 0x63 && Major(x.Target) && x.Success >= MinChance / 100.0 && x.Worth >= top))
        {
            if (_pivots.Any(p => p.Target == x.Target.Ref) || Knowledge.Location(game, x.Target, Side) is not StarSystem at) continue;
            _pivots.Add(new(x.Kind, x.Target.Ref, at.Ref, game.Day, x.Team.Count + (x.Decoys?.Count ?? 0)));
            PivotEvents?.Add(new(game.Day, x.Kind, x.Target.Ref, true, "started"));
        }
    }

    /// <summary>Rule 3: a pivot ends after <see cref="PivotDays"/>, when its own mission against the target ended, or when the target left its system (side's view).</summary>
    internal void EndPivots(GameState game)
    {
        foreach (var p in _pivots.ToList())
        {
            var target = game.Characters.FirstOrDefault(c => c.Ref == p.Target); // pivot targets are characters
            var seen = target is null ? default : Knowledge.See(game, target, Side);
            string? reason = game.Day - p.Start >= PivotDays ? "days"
                : target is null || seen.Destroyed || seen.Captured || (Knowledge.Location(game, target, Side) as StarSystem)?.Ref != p.System ? "target left"
                : game.Missions.Any(m => m.Owner == Side && m.Kind == p.Kind && m.Objective == target && m.CompletionStatus != 0) ? "mission ended"
                : null;
            if (reason is null) continue;
            _pivots.Remove(p);
            PivotEvents?.Add(new(game.Day, p.Kind, p.Target, false, reason));
        }
    }

    /// <summary>
    /// Rule 7 and rule 4 for a grown team: a character agent takes at least one special-force decoy when any
    /// detector is known; a retry at a foiled target needs max(1, known detectors) decoys (within the team cap). Null: not enough
    /// free special forces (the mission waits; <paramref name="shortBy"/> the decoys missing).
    /// </summary>
    internal (List<Agent> Team, List<Agent> Decoys)? AgentDecoys(GameState game, uint kind, GameObject target, CovertKnown known, List<Agent> team,
        List<Agent> decoys, IEnumerable<Agent> pool, Func<List<Agent>, List<Agent>, bool> valid, out int shortBy,
        Known? k = null, int value = 0, double? successCap = null)
    {
        if (On(V7.Decoys)) return StepDecoys(game, kind, target, known, team, decoys, pool, valid, out shortBy, k, value, successCap);
        shortBy = 0;
        int cap = TeamCap(kind, target);
        int need = 0;
        if (team.Any(a => a is Character) && known.Count > 0) need = 1;
        if (_foiledTargets.Contains(target.Ref)) need = Math.Max(need, Math.Max(1, known.Count));
        need = Math.Min(need, cap - team.Count);
        if (decoys.Count >= need) return (team, decoys);
        var d = new List<Agent>(decoys);
        foreach (var a in pool.Where(a => Family.IsSpecialForce(a.Type) && !team.Contains(a) && !d.Contains(a))
                     .OrderByDescending(a => Missions.Enhanced(game, a, SkillIndex.Espionage)).ThenBy(a => a.Key))
        {
            if (d.Count >= need) break;
            if (valid(team, [.. d, a])) d.Add(a);
        }
        if (d.Count >= need) return (team, d);
        shortBy = need - d.Count;
        return null;
    }

    /// <summary>p_success of a team with its decoys (the CovertExperience cap included): (1 - foil) * (1 - product (1 - p_i)).</summary>
    internal double TeamSuccess(GameState game, Known k, uint kind, GameObject target, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys, CovertKnown known, double? cap)
    {
        double none = team.Aggregate(1.0, (x, a) => x * (1 - Math.Clamp(Attempt(game, k, kind, a, target), 0, 100) / 100.0));
        return Math.Min((1 - FoilChance(game, team, known, decoys)) * (1 - none), cap ?? 1);
    }

    /// <summary>Retry measurement: an own mission's TaskStatus 3 or end for an open retry record of its kind and target.</summary>
    void ObserveRetry(Mission m)
    {
        if (RetryRecords is null || m.Objective is not { } o) return;
        var r = RetryRecords.FirstOrDefault(x => x.Outcome is null && x.Kind == m.Kind && x.Target == o.Ref);
        if (r is null) return;
        if (m.TaskStatus == 3) r.Succeeded = true;
        if (m.CompletionStatus != 0) r.Outcome = r.Succeeded ? "success" : m.CompletionStatus is 3 or 4 ? "foiled" : "failed";
    }

    void SaveAgents(JsonObject j)
    {
        if (_decoyShort > 0) j["decoyShort"] = _decoyShort;
        if (_foiledTargets.Count > 0) j["foiledTargets"] = new JsonArray([.. _foiledTargets.Select(r => (JsonNode)r)]);
        if (_plannedAt.Count > 0) j["plannedAt"] = new JsonArray([.. _plannedAt.Select(r => (JsonNode)r)]);
        if (_pivots.Count > 0) j["pivots"] = new JsonArray([.. _pivots.Select(p => (JsonNode)new JsonArray(p.Kind, p.Target, p.System, p.Start, p.Team))]);
    }

    void LoadAgents(JsonObject j)
    {
        _decoyShort = (int?)j["decoyShort"] ?? 0;
        if (j["foiledTargets"] is JsonArray f) foreach (var r in f) _foiledTargets.Add((uint)r!);
        if (j["plannedAt"] is JsonArray p) foreach (var r in p) _plannedAt.Add((uint)r!);
        if (j["pivots"] is JsonArray pv) foreach (var e in pv) _pivots.Add(new((uint)e![0]!, (uint)e[1]!, (uint)e[2]!, (int)e[3]!, (int)e[4]!));
    }
}
