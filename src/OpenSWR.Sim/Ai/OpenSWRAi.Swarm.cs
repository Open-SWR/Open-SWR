using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Swarm: the no-launch measurement (aimatch only, no effect
/// on play) and, under the flag, one attack group per fleet idle longer than <see cref="IdleDays"/>, the nearest free target, at
/// least <see cref="SwarmTroops"/> troops, and moving on after a won assault. Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int IdleDays = 10, SwarmTroops = 4, SwarmGarrison = 2, SwarmMemoryDays = 60;

    bool Swarming => On(V7.Swarm);

    // ---- measurement (aimatch; no effect on play) ----

    /// <summary>One decision's first reason a group or an idle fleet launched no assault (measurement).</summary>
    public sealed record NoLaunch(int Day, bool Group, string Reason);

    /// <summary>The no-launch reasons (null = off).</summary>
    public List<NoLaunch>? NoLaunchLog { get; set; }

    /// <summary>Attack launches of the side (measurement).</summary>
    public int AttackLaunches { get; private set; }

    void NoLaunchWhy(GameState game, bool group, string reason) => NoLaunchLog?.Add(new(game.Day, group, reason));

    /// <summary>The first reason a group's gathered fleets did not launch (measurement): gathering, strength, troops, escort, other.</summary>
    string LaunchReason(Known k, List<Fleet> gathered, StarSystem target, int troops)
    {
        if (gathered.Count == 0) return "waiting to gather";
        int mine = gathered.Sum(f => k.Strength(f, Side)), theirs = k.Strength(target, Enemy);
        if (mine < theirs) return "target known strength above own";
        if (!gathered.Any(Carrying) || TroopsAboard(gathered) < troops) return "not enough troops loaded";
        if (mine < 1.5 * theirs) return "escort check";
        return "other";
    }

    // ---- rules 1-4 ----

    /// <summary>Rule 1: per own fleet ref, the first decision day it was seen idle at an own system (Swarm only; saved).</summary>
    readonly SortedDictionary<uint, int> _idleSince = [];

    /// <summary>Rule 2: the known enemy fleet strengths seen per decision day, kept <see cref="SwarmMemoryDays"/> (Swarm only; saved).</summary>
    readonly SortedDictionary<int, List<int>> _enemyStrengths = [];

    /// <summary>Rule 2: the median known enemy fleet strength of the last <see cref="SwarmMemoryDays"/> (0 when none seen).</summary>
    internal int MedianEnemyFleet
    {
        get
        {
            var all = _enemyStrengths.Values.SelectMany(x => x).Order().ToList();
            return all.Count == 0 ? 0 : all[all.Count / 2];
        }
    }

    /// <summary>Test hook: a swarm group of one fleet.</summary>
    internal void AddSwarmGroup(StarSystem target, StarSystem staging, Fleet fleet) => _groups.Add(new(target) { Staging = staging, Fleet = fleet.Ref });

    /// <summary>Test hook: the troops a swarm base gives (rule 4).</summary>
    internal List<GameObject> SwarmSpareForTest(GameState game, StarSystem s) => SwarmSpare(game, s, []);

    /// <summary>Test hook: enemy fleet strengths seen on a day.</summary>
    internal void SawEnemyFleets(int day, params int[] strengths) => _enemyStrengths[day] = [.. strengths];

    /// <summary>Rule 2: a stale target (view older than <see cref="FreshDays"/>) is accepted at the median enemy fleet strength.</summary>
    internal bool SwarmStaleAccept(int strength) => strength >= MedianEnemyFleet;

    /// <summary>Rule 3: the troops a swarm group loads: its target's attack troops, and at least <see cref="SwarmTroops"/> within its room.</summary>
    int SwarmTroopsFor(GameState game, Known k, Fleet f, StarSystem target)
    {
        int room = f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)
            .Sum(x => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type)?.TroopCapacity ?? 0));
        return Math.Max(AttackTroopsFor(k, target), Math.Min(SwarmTroops, room));
    }

    /// <summary>
    /// Rules 1-4: idle-day bookkeeping, one group per fleet idle longer than
    /// <see cref="IdleDays"/> with the nearest free target (ties: the higher value), the won-assault move-on (2 troops stay, Inferred:
    /// the troops beyond <see cref="SwarmGarrison"/> re-embark), the return to the nearest own base without troops; then each group's turn.
    /// </summary>
    void SwarmAttack(GameState game, Known k, HashSet<GameObject> used, List<StarSystem> enemies, List<StarSystem> candidates, Func<StarSystem, StarSystem?> stage)
    {
        // rule 2's memory
        _enemyStrengths[game.Day] = [.. game.Systems.SelectMany(s => k.Fleets(s, Enemy)).Distinct().Select(f => k.Strength(f, Enemy)).Where(x => x > 0)];
        foreach (int d in _enemyStrengths.Keys.Where(d => game.Day - d >= SwarmMemoryDays).ToList()) _enemyStrengths.Remove(d);
        // rule 1: idle days (not held by the defence: the defence steps put their fleets in used)
        var fleets = Fleets(game).Where(f => f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing)).ToList();
        foreach (var r in _idleSince.Keys.Where(r => fleets.All(f => f.Ref != r || !(Idle(game, f, used) && f.Container is StarSystem { } at && at.Owner == Side))).ToList()) _idleSince.Remove(r);
        foreach (var f in fleets.Where(f => Idle(game, f, used) && f.Container is StarSystem { } at && at.Owner == Side)) _idleSince.TryAdd(f.Ref, game.Day);
        // groups whose fleet is gone end; a won assault moves the group on from the taken system (rule 4)
        _groups.RemoveAll(g => g.Fleet is not { } r || fleets.All(f => f.Ref != r));
        foreach (var g in _groups.Where(g => !enemies.Contains(g.Target)).ToList())
        {
            if (g.Target.Owner == Side) (g.Staging, g.HeldSince) = (g.Target, null);
            g.Target = null!;
        }
        var home = HomeSector(game, Side);
        // TroopFit rule 1: only targets whose attack troops fit the fleet's troop room
        _siege = Sieging ? SiegeTarget(game) : null; // Siege
        if (_siege is not null) SiegeDays++; // measurement
        if (Sieging) FollowSiege(_groups); // Siege rule 2
        StarSystem? Nearest(StarSystem from, Fleet f) => SiegeReach(game, from) ? _siege : candidates.Where(c => _groups.All(g => g.Target != c) && (!FittingTroops || FitsRoom(game, k, f, c))
                && (!Holdable || HoldableTarget(game, k, f, c))) // Holdable
            .OrderBy(c => Orders.Distance(from, c)).ThenByDescending(c => Value(game, c, home)).ThenBy(c => c.Key).FirstOrDefault();
        // Siege: a group not yet at or on the way to its target, in reach of the siege target, takes it
        if (_siege is { } siege)
            foreach (var g in _groups.Where(g => g.Target is not null && g.Target != siege))
                if (fleets.First(x => x.Ref == g.Fleet) is { Enroute: false, Container: StarSystem from } f && from != g.Target && SiegeReach(game, from)) g.Target = siege;
        foreach (var g in _groups.Where(g => g.Target is null).ToList())
        {
            var f = fleets.First(x => x.Ref == g.Fleet);
            if ((f.Container as StarSystem ?? g.Staging) is { } from && Nearest(from, f) is { } next) g.Target = next;
            else _groups.Remove(g);
        }
        // rule 1: new groups for the fleets idle past IdleDays
        foreach (var f in fleets.Where(f => _idleSince.TryGetValue(f.Ref, out int since) && game.Day - since > IdleDays && _groups.All(g => g.Fleet != f.Ref)).OrderBy(f => f.Key))
        {
            if (Nearest((StarSystem)f.Container!, f) is not { } target)
            {
                NoLaunchWhy(game, false, candidates.Count == 0 ? "no target in reach"
                    : FittingTroops && candidates.Any(c => _groups.All(g => g.Target != c)) ? "no target fits room" : "target taken by another group");
                continue;
            }
            _groups.Add(new(target) { Staging = (StarSystem)f.Container!, Fleet = f.Ref });
        }
        HoldUprisings(game, fleets, used); // Hold: uprising own systems get the floor from a passing group
        if (Generals) AssignGenerals(game, used, [.. _groups.Select(g => fleets.First(f => f.Ref == g.Fleet))]); // Generals
        // rule 4: a group without troops aboard and none spare at its base returns to the nearest own base
        foreach (var g in _groups)
        {
            var f = fleets.First(x => x.Ref == g.Fleet);
            // TroopFit rule 2: a short group with no spare at its base reloads where the spare troops are
            if (FittingTroops && f.Container == g.Staging && ReloadBase(game, k, g, f, used) is { } reload) g.Staging = reload;
            else if (f.Container == g.Staging && !Carrying(f) && g.Staging is { } s && SwarmSpare(game, s, used).Count == 0 && (HoldReload(game, s, used) ?? stage(g.Target)) is { } back && back != s) g.Staging = back;
        }
        // Siege: the siege groups share one staging system and gather there
        if (_siege is { } sieged && stage(sieged) is { } common)
            foreach (var g in _groups.Where(g => g.Target == sieged))
            {
                g.Staging = common;
                if (g.Fleet is { } joined) _siegeJoined.Add((joined, sieged.Ref)); // measurement
            }
        foreach (var g in _groups.ToList())
        {
            if (!Room) break;
            Run(game, k, used, g, _siege is { } at && g.Target == at ? [.. _groups.Where(x => x.Target == at).Select(x => fleets.First(f => f.Ref == x.Fleet))]
                : [fleets.First(x => x.Ref == g.Fleet)]);
        }
    }

    /// <summary>Rule 4: the own troops at a swarm base beyond <see cref="SwarmGarrison"/> (and the system's own keep).</summary>
    List<GameObject> SwarmSpare(GameState game, StarSystem s, HashSet<GameObject> used)
    {
        var ready = Leaving(game, s.Contents.Where(x => OwnTroop(x, Side) && x.Usable && !x.Enroute && !used.Contains(x)));
        int extra = s.Contents.Count(x => OwnTroop(x, Side) && !used.Contains(x)) - Math.Max(Keep(s), SwarmFloor(game, s)); // Hold: the requirement
        return [.. ready.Take(Math.Clamp(extra, 0, ready.Count))];
    }

    void SaveSwarm(JsonObject j)
    {
        if (_idleSince.Count > 0) j["idleSince"] = new JsonArray([.. _idleSince.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_enemyStrengths.Count > 0) j["enemyStrengths"] = new JsonArray([.. _enemyStrengths.Select(e => (JsonNode)new JsonArray([e.Key, .. e.Value.Select(v => (JsonNode)v)]))]);
    }

    void LoadSwarm(JsonObject j)
    {
        if (j["idleSince"] is JsonArray a) foreach (var e in a) _idleSince[(uint)e![0]!] = (int)e[1]!;
        if (j["enemyStrengths"] is JsonArray b) foreach (var e in b) _enemyStrengths[(int)e![0]!] = [.. e.AsArray().Skip(1).Select(v => (int)v!)];
    }
}
