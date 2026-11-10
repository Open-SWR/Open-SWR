namespace OpenSWR.Sim.Ai;

/// <summary>
/// Measurement only (aimatch): why idle special forces were not used in one decision's Assign. The controller records the
/// decision's espionage, reconnaissance and covert outcomes while it decides, then classifies each idle own special force by
/// the first mission kind (in Assign order: espionage, reconnaissance, covert kinds) that had targets. Pure reads; no draws, no
/// state change: play is the same with or without a probe.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>One decision's probe; set by aimatch before the decision, filled by it.</summary>
    public sealed class IdleProbe
    {
        public int Day { get; internal set; }
        /// <summary>Per idle special force: type, system ref (0: none), the reason letter (a-i) and its detail.</summary>
        public List<(uint Type, uint System, string Reason, string Detail)> Idle { get; } = [];
        public Dictionary<uint, int> CandidatesBefore { get; } = [];
        public Dictionary<uint, int> CandidatesAfter { get; } = [];
        public Dictionary<uint, int> Sent { get; } = [];
        public int PoolMaintenance { get; internal set; }
        public double PoolTarget { get; internal set; }
        internal List<(StarSystem Target, string Outcome)> Espionage { get; } = [];
        internal List<(StarSystem Target, string Outcome)> Recon { get; } = [];
        public List<(uint Kind, GameObject Target, string Outcome)> Considered { get; } = [];
        internal List<Covert> Final { get; } = [];
        internal Dictionary<(uint Kind, uint Target), string> Dropped { get; } = [];
        internal HashSet<Agent> SuccessSkipped { get; } = [];
        /// <summary>Why the covert step did not run at this decision (null: it ran).</summary>
        internal string? CovertSkipped { get; set; }
    }

    /// <summary>The probe of the next decision (null = off).</summary>
    public IdleProbe? Probe { get; set; }

    /// <summary>Measurement: special-force training orders placed while more than half of the pool was idle.</summary>
    public int TrainedWhileIdle { get; private set; }

    void NoteTrained(GameState game)
    {
        if (IdleShare(game) > 0.5) TrainedWhileIdle++;
    }

    /// <summary>The first reason for each idle own special force after Assign.</summary>
    void ClassifyIdle(GameState game, HashSet<GameObject> used)
    {
        var p = Probe!;
        p.Day = game.Day;
        int capacity = game.Sides[Side].MaintenanceCapacity;
        p.PoolMaintenance = SpecialForces(game).Sum(a => game.MaintenanceCost(a.Type));
        p.PoolTarget = AgentPool * capacity;
        foreach (var a in SpecialForces(game).Where(a => a.Completed && !a.OnMission && !used.Contains(a)))
        {
            var (reason, detail) = Why(game, a);
            p.Idle.Add((a.Type, (Missions.Location(a) as StarSystem)?.Ref ?? 0, reason, detail));
        }
        Probe = null;
    }

    (string, string) Why(GameState game, Agent a)
    {
        var p = Probe!;
        if (a.Enroute || Missions.Location(a) is not StarSystem) return ("i", "travelling or not at a system");
        if (_reserved.ContainsKey(a)) return ("h", "reserved for a pivot");
        var record = game.Definitions.SpecialForces.FirstOrDefault(f => f.Common.Ref == a.Type);
        var kinds = record is null ? [] : KindsFor(game, Side, record);
        if (kinds.Count == 0) return ("a", "type serves no mission kind");
        bool Valid(uint kind, GameObject target, List<Agent> team, List<Agent> decoys) =>
            TypeOf(game, kind) is { } t && MissionEligibility.Validate(game, 0x240, Side, t.Common.Ref, team, decoys, target) is null;

        // espionage (enemy systems: the scouting step), then reconnaissance (unexplored systems)
        foreach (var (kind, list) in new[] { (0x52u, p.Espionage), (0x54u, p.Recon) })
        {
            if (!kinds.Contains(kind) || list.Count == 0) continue;
            if (!list.Any(x => Valid(kind, x.Target, [a], []))) return ("b", $"{Name(kind)}: mask rejected at every target");
            var open = list.Where(x => x.Outcome is not ("sent" or "running")).ToList();
            if (open.Count == 0)
                return kind == 0x52 && IdleShare(game) <= IdleReserve && On(V7.Decoys) ? ("g", "espionage: every target covered; decoy gate (idle <= 25 %)")
                    : ("e", $"{Name(kind)}: every target covered; decoy target met");
            string o = open[0].Outcome;
            return o switch
            {
                "budget" => ("d", "espionage budget (step 7 rule 6)"),
                "coverdays" => ("d", "CoverDays repeat"),
                "maxscouts" => ("i", "scouting loop gate: MaxScouts (2) new or running scouts per decision"),
                "no free character" => ("i", "scouting loop gate: stops when no character is free"),
                "room" => ("i", "command budget (10 per decision)"),
                "retry waits" => ("i", "retry waits for decoys (step 7 rule 4)"),
                _ => ("i", $"{Name(kind)}: {o}"),
            };
        }
        // covert kinds
        var covertKinds = kinds.Where(k => k is not (0x52 or 0x54)).ToList();
        var considered = p.Considered.Where(x => covertKinds.Contains(x.Kind)).ToList();
        if (considered.Count == 0)
            return p.CovertSkipped is { } skipped ? (skipped.StartsWith("MaxCovert") ? "d" : "i", $"covert step not run: {skipped}") : ("a", "no candidate for any kind its mask allows");
        var candidates = p.Final.Where(x => covertKinds.Contains(x.Kind)).ToList();
        var dropped = p.Dropped.Where(e => covertKinds.Contains(e.Key.Kind)).ToList();
        if (candidates.Count == 0 && dropped.Count == 0)
        {
            var top = considered.GroupBy(x => x.Outcome).OrderByDescending(g => g.Count()).First();
            return ("i", $"covert: targets considered, none a candidate (mostly: {top.Key})");
        }
        var all = candidates.Select(x => (x.Kind, x.Target, x.Team, Decoys: x.Decoys ?? [])).ToList();
        bool AsAgent(uint kind, GameObject t) => Valid(kind, t, [a], []);
        bool AsDecoy(uint kind, GameObject t, List<Agent> team) => team.Count > 0 && Valid(kind, t, team, [a]);
        var usable = all.Where(x => AsAgent(x.Kind, x.Target) || AsDecoy(x.Kind, x.Target, x.Team)).ToList();
        if (usable.Count == 0 && dropped.Count == 0) return ("b", "covert: mask rejected as agent and decoy on every candidate");
        if (dropped.Count > 0 && usable.All(x => p.Dropped.ContainsKey((x.Kind, x.Target.Ref))))
            return ("d", $"covert cap: {dropped.GroupBy(e => e.Value).OrderByDescending(g => g.Count()).First().Key}");
        if (p.SuccessSkipped.Contains(a)) return ("f", "decoy would lower estimated success");
        foreach (var x in usable.Where(x => !p.Dropped.ContainsKey((x.Kind, x.Target.Ref))))
        {
            int room = TeamCap(x.Kind, x.Target) - x.Team.Count - x.Decoys.Count;
            int target = x.Team.Any(m => m is Character) && On(V7.Decoys) ? CharacterTarget(x.Kind, x.Target) : 0;
            if (room > 0 && x.Decoys.Count < target) return ("i", "decoy target not met though free (not chosen: lower-worth order or mask)");
            if (room > 0 && target == 0) return ("f", "not added: MinJoinGain (worth gain below 1 % of the value)");
        }
        return ("e", "team cap or decoy target met on every candidate");
    }

    static string Name(uint kind) => kind switch { 0x52 => "espionage", 0x54 => "reconnaissance", _ => $"kind {kind:X2}" };
}
