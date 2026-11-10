namespace OpenSWR.Sim.Ai;

/// <summary>
/// Decoys: assumed detectors on enemy-held systems, special-force decoys for character-led missions and, from
/// the idle pool, for special-force espionage; pivot reservations. Views and definition data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int AssumedDetectors = 1, DecoysCharacter = 2, DecoysMajor = 3;
    /// <summary>Special-force espionage takes a decoy only while the idle special forces exceed this share of the pool.</summary>
    public const double IdleReserve = 0.25;

    /// <summary>Measurement (rule 7): character-led missions sent with fewer decoys than their target.</summary>
    public int FewerDecoys { get; private set; }

    /// <summary>Pivot reservations of the current decision: keyed by special force, giving the pivot's target ref (rule 4).</summary>
    readonly Dictionary<Agent, uint> _reserved = [];
    internal IReadOnlyDictionary<Agent, uint> Reserved => _reserved;

    /// <summary>
    /// Rule 1 (with the decisions): on an enemy-held system with fewer known detectors than <see cref="AssumedDetectors"/>, the
    /// median known enemy system's detectors stand in; when it has none, one detector with the detection value of the cheapest
    /// troop type the enemy side can field (definition data), no commander, on TDECOYTB. Inferred: planning only. The foil mark stays.
    /// </summary>
    internal CovertKnown Assume(GameState game, Known k, StarSystem at, CovertKnown known, StarSystem? median)
    {
        if (!On(V7.Decoys) || known.Count >= AssumedDetectors || Knowledge.See(game, at, Side).Owner != Enemy) return known;
        if (median is not null && median != at && Pools(game, k, median, true) is { Count: > 0 } pools) return pools with { MinFoil = known.MinFoil };
        var troop = game.Definitions.Troops.Where(t => (Enemy == Owner.Alliance ? t.Alliance : t.Empire) != 0)
            .OrderBy(t => t.MaintenanceCost).ThenBy(t => t.Common.Ref).FirstOrDefault();
        return troop is null ? known : new CovertKnown([], [new Detector(Detection(game, troop.Common.Ref))], known.MinFoil);
    }

    /// <summary>
    /// Rule 4 (with the decisions): each running pivot reserves free special forces up to its team size, the types that can run
    /// its kind first, then the lowest worth for it; reserved ones are kept out of every other mission's decoys.
    /// </summary>
    internal void ReserveForPivots(GameState game, Known k, HashSet<GameObject> used)
    {
        _reserved.Clear();
        if (!On(V7.Decoys)) return;
        var free = FreeSpecialForces(game, used);
        foreach (var p in _pivots)
        {
            if (game.Characters.FirstOrDefault(c => c.Ref == p.Target) is not { } target || Knowledge.Location(game, target, Side) is not StarSystem at) continue;
            var known = KnownAt(game, k, at, true, null);
            int value = CharacterValue(target, ScreenViews.Sees(target, Side));
            bool Capable(Agent a) => game.Definitions.SpecialForces.FirstOrDefault(f => f.Common.Ref == a.Type) is { } f && KindsFor(game, Side, f).Contains(p.Kind);
            foreach (var a in free.Where(a => !_reserved.ContainsKey(a)).OrderBy(a => Capable(a) ? 0 : 1)
                         .ThenBy(a => Worth(game, p.Kind, [a], Attempt(game, k, p.Kind, a, target), value, known)).ThenBy(a => a.Key).Take(p.Team).ToList())
                _reserved[a] = p.Target;
        }
    }

    /// <summary>
    /// Rules 2-4 for one team: a character-led team takes <see cref="DecoysCharacter"/>
    /// decoys (a major character's abduction, rescue or assassination <see cref="DecoysMajor"/>), within the team cap; special-force
    /// espionage takes 1 while the idle pool exceeds <see cref="IdleReserve"/>. Decoys come from the free special forces outside other
    /// missions' pivot reservations, the lowest worth as agents for the mission first, and never lower the estimated success. Fewer
    /// free than the target: the mission goes with what is free; a retry still waits below max(1, known detectors).
    /// </summary>
    internal (List<Agent> Team, List<Agent> Decoys)? StepDecoys(GameState game, uint kind, GameObject target, CovertKnown known, List<Agent> team,
        List<Agent> decoys, IEnumerable<Agent> pool, Func<List<Agent>, List<Agent>, bool> valid, out int shortBy, Known? k, int value, double? successCap)
    {
        shortBy = 0;
        int room = TeamCap(kind, target) - team.Count;
        bool character = team.Any(a => a is Character);
        int want = character ? CharacterTarget(kind, target) : kind == 0x52 && IdleShare(game) > IdleReserve ? 1 : 0;
        int retry = _foiledTargets.Contains(target.Ref) ? Math.Max(1, known.Count) : 0;
        want = Math.Min(Math.Max(want, retry), room);
        retry = Math.Min(retry, room);
        var d = new List<Agent>(decoys);
        if (d.Count < want)
        {
            var order = pool.Where(a => Family.IsSpecialForce(a.Type) && !team.Contains(a) && !d.Contains(a)
                && (!_reserved.TryGetValue(a, out uint pivot) || pivot == target.Ref));
            // the lowest worth as agents for this mission first (espionage: the lowest espionage skill)
            order = k is not null && kind != 0x52
                ? order.OrderBy(a => Worth(game, kind, [a], Attempt(game, k, kind, a, target), value, known)).ThenBy(a => a.Key)
                : order.OrderBy(a => Missions.Enhanced(game, a, SkillIndex.Espionage)).ThenBy(a => a.Key);
            foreach (var a in order.ToList())
            {
                if (d.Count >= want) break;
                if (!valid(team, [.. d, a])) continue;
                // a decoy never lowers a character team's estimated success
                if (character && k is not null && kind != 0x52
                    && TeamSuccess(game, k, kind, target, team, [.. d, a], known, successCap) < TeamSuccess(game, k, kind, target, team, d, known, successCap) - 1e-9)
                {
                    Probe?.SuccessSkipped.Add(a); // measurement only
                    continue;
                } // (rounding: equal counts as not lower)
                d.Add(a);
            }
        }
        if (d.Count < retry)
        {
            shortBy = retry - d.Count;
            return null;
        }
        return (team, d);
    }

    /// <summary>Rule 2's decoy target for a character-led team: <see cref="DecoysMajor"/> against a major character's abduction, rescue or assassination, else <see cref="DecoysCharacter"/>.</summary>
    static int CharacterTarget(uint kind, GameObject target) => Major(target) && kind is 0x61 or 0x62 or 0x63 ? DecoysMajor : DecoysCharacter;

    /// <summary>Counts a character-led mission sent with fewer decoys than its target within the cap (rule 7).</summary>
    void CountFewer(uint kind, GameObject target, IReadOnlyList<Agent> team, int decoys)
    {
        if (On(V7.Decoys) && team.Any(a => a is Character) && decoys < Math.Min(CharacterTarget(kind, target), TeamCap(kind, target) - team.Count)) FewerDecoys++;
    }

    /// <summary>Idle own special forces (Completed, no mission) as a share of all own special forces; decoys on missions count as busy.</summary>
    double IdleShare(GameState game)
    {
        var all = SpecialForces(game);
        return all.Count == 0 ? 0 : (double)all.Count(a => !a.OnMission && a.Completed) / all.Count;
    }
}
