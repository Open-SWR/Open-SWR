using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Own system value, threat and priority.
/// Inferred: openswr's starting weights, to be tuned with aimatch.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>The home sector of a side: the Empire's is Coruscant's sector, the Alliance's its HQ system's (else its first own system's).</summary>
    public static Sector? HomeSector(GameState game, Owner side) =>
        side == Owner.Empire ? game.Systems.FirstOrDefault(s => s.Type == Refs.Coruscant)?.Sector
        : game.HeadquartersSystem is { Owner: var o } hq && o == side ? hq.Sector
        : game.Systems.Where(s => s.Owner == side && !s.Destroyed).OrderBy(s => s.Key).FirstOrDefault()?.Sector;

    /// <summary>
    /// The value of an own system: HQ (Alliance: a system holding an own headquarters
    /// facility) or Coruscant (Empire) 100; each own shipyard, training facility, construction yard 20; each Mine and Refinery 5; each
    /// defence facility (families 0x22-0x27) 5; each free energy or raw-material slot 1, at most 10 in all; in the home sector + 10; + 10 when losing it
    /// costs the side its majority of the populated systems in its sector. <paramref name="see"/> gives the side's knowledge of another
    /// system (owner, populated): the side's views for the AI, the real state for an observer.
    /// </summary>
    public static int SystemValue(GameState game, Owner side, StarSystem s, Sector? home, Func<StarSystem, (Owner Owner, bool Populated)> see)
    {
        int v = 0;
        var mine = s.Contents.Where(x => x.Owner == side && x.Existing).ToList();
        bool hq = side == Owner.Empire ? s.Type == Refs.Coruscant : mine.Any(x => Family.Of(x.Type) is 0x20 or 0x21);
        if (hq) v += 100;
        foreach (var x in mine)
            v += Family.Of(x.Type) switch { 0x28 or 0x29 or 0x2A => 20, 0x2C or 0x2D => 5, >= 0x22 and < 0x28 => 5, _ => 0 };
        v += Math.Min(10, Math.Max(0, s.Energy - s.UsedEnergy) + Math.Max(0, s.RawMaterials - s.UsedRaw)); // free slots: 1 each, at most 10
        if (s.Sector == home) v += 10;
        var sector = game.Systems.Where(x => x.Sector == s.Sector).Select(x => x == s ? (Owner: side, s.Populated) : see(x)).Where(x => x.Populated).ToList();
        int owned = sector.Count(x => x.Owner == side);
        if (s.Populated && 2 * owned > sector.Count && 2 * (owned - 1) <= sector.Count) v += 10;
        return v;
    }

    /// <summary>The value of an own system as the side knows it (its views for the other systems of the sector).</summary>
    internal int Value(GameState game, StarSystem s, Sector? home) =>
        SystemValue(game, Side, s, home, x => Knowledge.See(game, x, Side) is var v ? (v.Owner, v.Populated) : default);

    // ---- defence by value ----

    /// <summary>Own system values of the current decision, by system ref, and the high-value systems (the top third by value).</summary>
    readonly Dictionary<uint, int> _values = [];
    readonly HashSet<uint> _high = [];

    /// <summary>The values of the decision before (a system lost since then is no longer own when the values refresh).</summary>
    readonly Dictionary<uint, int> _previousValues = [];

    /// <summary>The values and the top third of the own systems by value (ties: the lower Key), at the start of each decision.</summary>
    internal void RefreshValues(GameState game)
    {
        var home = HomeSector(game, Side);
        _previousValues.Clear();
        foreach (var (r, v) in _values) _previousValues[r] = v;
        _values.Clear();
        _high.Clear();
        var own = Own(game);
        foreach (var s in own) _values[s.Ref] = Value(game, s, home);
        foreach (var s in own.OrderByDescending(s => _values[s.Ref]).ThenBy(s => s.Key).Take((own.Count + 2) / 3)) _high.Add(s.Ref);
    }

    /// <summary>A high-value own system: in the top third by value.</summary>
    internal bool HighValue(StarSystem s) => _high.Contains(s.Ref);

    /// <summary>
    /// Threat of an own system: over the known enemy systems within <see cref="ReachDays"/> (speed 100), the
    /// known enemy troops there and the troop capacity of the known enemy fleets there, each weighted by nearness 1 - travel days /
    /// ReachDays; a fleet with known troops aboard counts in full.
    /// </summary>
    internal double Threat(GameState game, Known k, StarSystem s, List<StarSystem> enemyPlaces)
    {
        double threat = 0;
        foreach (var e in enemyPlaces)
        {
            int days = Orders.TravelDays(game, e, s, 100);
            if (days > ReachDays) continue;
            double near = 1 - (double)days / ReachDays;
            threat += EnemyTroops(k, e) * near;
            foreach (var f in k.Fleets(e, Enemy))
            {
                var ships = k.Contents(f).Where(v => Family.IsCapitalShip(v.Object.Type)).ToList();
                int capacity = ships.Sum(v => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == v.Object.Type)?.TroopCapacity ?? 0));
                bool aboard = ships.Any(v => k.Contents(v.Object).Any(x => x.In(0x10, 0x14)));
                threat += capacity * (aboard ? 1 : near);
            }
        }
        return threat;
    }

    /// <summary>The places of known enemy troops and fleets: systems the views show an enemy system or an enemy fleet at.</summary>
    List<StarSystem> EnemyPlaces(GameState game, Known k) =>
        [.. game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy || k.Fleets(s, Enemy).Count > 0)];

    /// <summary>
    /// Defence by value: every high-value own system with threat &gt; 0 gets a target garrison of ceil(threat x 1.2 + 1), in priority
    /// order (value x (1 + threat)); the side's troops cover them in that order after the uprising garrisons of rule 1 (a system they
    /// cannot cover is skipped, the next one tried). Returns all targets and the covered ones (both in priority order).
    /// </summary>
    internal (Dictionary<StarSystem, int> Targets, Dictionary<StarSystem, int> Covered) Defence(GameState game, Known k)
    {
        var places = EnemyPlaces(game, k);
        var enemies = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        var targets = new Dictionary<StarSystem, int>();
        // Watch: a held system first, with at least the incoming troop capacity + 1; an evacuated one gets no target
        foreach (var x in Incomings.Values.Where(x => x.Hold && x.System.Populated).OrderByDescending(x => _values.GetValueOrDefault(x.System.Ref)).ThenBy(x => x.System.Key))
            targets[x.System] = x.Troops + 1;
        foreach (var (s, threat, border) in Own(game).Where(s => s.Populated && HighValue(s) && !Incomings.ContainsKey(s))
                     .Select(s => (s, threat: Threat(game, k, s, places), border: enemies.Any(e => Orders.TravelDays(game, s, e, 100) <= ReachDays)))
                     .Where(x => x.threat > 0 || x.border && On(V7.Experience))
                     .OrderByDescending(x => _values.GetValueOrDefault(x.s.Ref) * (1 + x.threat)).ThenBy(x => x.s.Key))
        {
            int target = threat > 0 ? (int)Math.Ceiling(threat * 1.2 + 1) : 0;
            // a high-value border system keeps at least the expected landing + 1, threat seen or not
            targets[s] = border && On(V7.Experience) ? Math.Max(target, ExpectedLanding + 1) : target;
        }
        int available = OwnTroops(game) - Own(game).Where(AtRisk).Sum(Garrison);
        var covered = new Dictionary<StarSystem, int>();
        foreach (var (s, target) in targets)
            if (available >= target)
            {
                covered[s] = target;
                available -= target;
            }
        return (targets, covered);
    }

    // ---- garrisons from experience ----

    /// <summary>Landings kept for the expected landing.</summary>
    public const int LandingsKept = 10;

    /// <summary>Landing used before any was seen.</summary>
    public const int FirstLanding = 4;

    /// <summary>The troops the side saw land in its last lost assaults (oldest first, at most <see cref="LandingsKept"/>).</summary>
    readonly List<int> _landings = [];

    internal IReadOnlyList<int> Landings => _landings;

    /// <summary>The mean of the last landings rounded up; <see cref="FirstLanding"/> before any.</summary>
    internal int ExpectedLanding => _landings.Count == 0 ? FirstLanding : (int)Math.Ceiling(_landings.Average());

    /// <summary>Records the troops seen landed in an assault the side lost.</summary>
    internal void RecordLanding(int troops)
    {
        _landings.Add(troops);
        if (_landings.Count > LandingsKept) _landings.RemoveAt(0);
    }

    /// <summary>The landings for the save (optional field "landings"; the expected landing follows from them).</summary>
    void SaveLandings(JsonObject j)
    {
        if (_landings.Count > 0) j["landings"] = new JsonArray([.. _landings.Select(x => (JsonNode)x)]);
    }

    void LoadLandings(JsonObject j)
    {
        if (j["landings"] is JsonArray a) foreach (var x in a) RecordLanding((int)x!);
    }

    /// <summary>
    /// The systems that want a shield or battery: of the top 3 own systems by priority with
    /// threat &gt; 0, each without an own shield (family 0x24) gets one, else each without an own battery (family 0x23) one, when the
    /// construction yards can build it. In priority order.
    /// </summary>
    internal List<(StarSystem? Site, Data.Dat.SdRecord? Product)> DefenceSites(GameState game, Known k)
    {
        var places = EnemyPlaces(game, k);
        var buildable = Research.Buildable(game, Side, 0x2A);
        var result = new List<(StarSystem?, Data.Dat.SdRecord?)>();
        foreach (var (s, _) in Own(game).Select(s => (s, threat: Threat(game, k, s, places))).Where(x => x.threat > 0)
                     .OrderByDescending(x => _values.GetValueOrDefault(x.s.Ref) * (1 + x.threat)).ThenBy(x => x.s.Key).Take(3))
            foreach (uint family in new uint[] { 0x24, 0x23 })
            {
                if (s.Contents.Any(x => x.Owner == Side && x.Existing && Family.Of(x.Type) == family)) continue;
                if (buildable.FirstOrDefault(r => Family.Of(r.Common.Ref) == family) is { } product && s.Energy > s.UsedEnergy) result.Add((s, product));
                break;
            }
        return result;
    }
}
