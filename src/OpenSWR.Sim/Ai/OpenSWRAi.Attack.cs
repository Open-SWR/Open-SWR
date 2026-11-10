using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Rule 4 of the OpenSWR AI: up to <see cref="MaxGroups"/>
/// attack groups, each with its own target, staging system and patience clock; recently lost systems are the first-choice targets.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>At most this many attack groups at once.</summary>
    public const int MaxGroups = 3;
    /// <summary>A system lost to the enemy is a counter-attack target for this many days.</summary>
    public const int CounterDays = 60;

    /// <summary>One attack group: its target, its staging system (the own system nearest the target), the first day it was held back.</summary>
    internal sealed class AttackGroup(StarSystem target)
    {
        public StarSystem Target { get; set; } = target;
        public StarSystem? Staging { get; set; }
        public int? HeldSince { get; set; }
        /// <summary>The group's one fleet (Swarm).</summary>
        public uint? Fleet { get; set; }
    }

    readonly List<AttackGroup> _groups = [];
    /// <summary>Systems lost to the enemy, by system ref: the decision day of the loss (kept <see cref="CounterDays"/>) and its value then.</summary>
    readonly SortedDictionary<uint, (int Day, int Value)> _lost = [];

    /// <summary>The attack groups (first = the oldest).</summary>
    internal IReadOnlyList<AttackGroup> Groups => _groups;

    /// <summary>Adds an attack group (tests).</summary>
    internal void AddGroup(StarSystem target, StarSystem? staging) => _groups.Add(new(target) { Staging = staging });
    /// <summary>Rule 4: the first group's staging system and target (null = none), and its held-back day.</summary>
    public StarSystem? Staging => _groups.FirstOrDefault()?.Staging;

    /// <summary>The staging systems of the attack groups (for the aimatch measurement).</summary>
    public IEnumerable<StarSystem> StagingSystems => _groups.Select(g => g.Staging).OfType<StarSystem>();

    /// <summary>The defence targets the troops cover at the last decision (for the aimatch measurement).</summary>
    public IEnumerable<StarSystem> DefendedSystems => Need.Defend.Keys;
    public StarSystem? Target => _groups.FirstOrDefault()?.Target;
    public int? HeldSince => _groups.FirstOrDefault()?.HeldSince;

    /// <summary>The attack targets (for the aimatch fleet-shape measurement).</summary>
    public IEnumerable<StarSystem> AttackTargets => _groups.Select(g => g.Target);

    /// <summary>The current attack targets.</summary>
    IEnumerable<StarSystem> Targets() => _groups.Select(g => g.Target).Distinct(); // (Siege: several groups may share one)

    /// <summary>A loss to the enemy (from the owner changes of the decision): the system becomes a counter-attack target.</summary>
    internal void MarkLost(StarSystem s, int day, int? value = null) =>
        _lost[s.Ref] = (day, value ?? (_values.TryGetValue(s.Ref, out int v) ? v : _previousValues.GetValueOrDefault(s.Ref)));

    /// <summary>
    /// Rule 4. Candidates: known enemy systems within <see cref="ReachDays"/> (speed 100) of an own system that is not
    /// threatened, not passed over; ordered: lost within <see cref="CounterDays"/> first (counter-attack; highest value at
    /// the loss first), then power base, frontier,
    /// the rest; within that the weakest known defence, then the nearest. Each group keeps its target while it is a known enemy system;
    /// it changes to a better candidate no other group has (counter-attack or a better sector group, or clearly weaker in the same
    /// order) only while none of its fleets is on the way or there. Fleets: each group keeps the idle fleets at its staging system
    /// and takes the nearest other idle fleets until it has 1.5 x its target's defence; a new group forms when the idle fleets left
    /// reach 1.5 x the next free candidate's defence; idle fleets still left join the first group. Per group then as before: landing
    /// at the target, gathering, troops aboard, the launch (value and attack troops), the patience clock.
    /// </summary>
    internal void Attack(GameState game, Known k, HashSet<GameObject> used, List<StarSystem> threatened, SectorMap map)
    {
        var bases = Own(game).Where(s => !threatened.Contains(s)).ToList();
        ResolveLandings(game); // measurement only
        foreach (var r in _passed.Where(e => e.Value <= game.Day).Select(e => e.Key).ToList()) _passed.Remove(r);
        foreach (var r in _lost.Where(e => game.Day - e.Value.Day >= CounterDays).Select(e => e.Key).ToList()) _lost.Remove(r);
        var enemies = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).OrderBy(s => s.Key).ToList();
        if (!Swarming) _groups.RemoveAll(g => !enemies.Contains(g.Target)); // (Swarm: the won-assault move-on first)
        int Counter(StarSystem s) => _lost.ContainsKey(s.Ref) ? 0 : 1;
        int Group(StarSystem s) => map.PowerBase.Contains(s.Sector) ? 0 : map.Frontier.Contains(s.Sector) ? 1 : 2;
        int From(StarSystem s) => bases.Count == 0 ? int.MaxValue : bases.Min(b => Orders.Distance(b, s));
        var candidates = enemies.Where(s => !_passed.ContainsKey(s.Ref) && bases.Any(b => Orders.TravelDays(game, b, s, 100) <= ReachDays))
            .OrderBy(Counter).ThenByDescending(s => _lost.TryGetValue(s.Ref, out var l) ? l.Value : 0).ThenBy(Group).ThenBy(s => k.Strength(s, Enemy)).ThenBy(From).ThenBy(s => s.Key).ToList();
        bool Committed(AttackGroup g) => Fleets(game).Any(f => f.Container == g.Target);
        StarSystem? Free(AttackGroup? self) => candidates.FirstOrDefault(c => _groups.All(g => g == self || g.Target != c));

        StarSystem? Stage(StarSystem target) =>
            // the power base is preferred only among the systems within reach of the target
            bases.OrderBy(s => Orders.TravelDays(game, s, target, 100) <= ReachDays ? 0 : 1).ThenBy(s => map.PowerBase.Contains(s.Sector) ? 0 : 1)
                .ThenBy(s => Orders.Distance(s, target)).ThenBy(s => s.Key).FirstOrDefault();
        // Swarm: one group per idle fleet in place of the normal groups
        if (Swarming)
        {
            SwarmAttack(game, k, used, enemies, candidates, Stage);
            return;
        }

        // retarget: a better free candidate while not committed
        foreach (var g in _groups)
            if (!Committed(g) && Free(g) is { } best && best != g.Target
                && (Counter(best) < Counter(g.Target) || Counter(best) == Counter(g.Target)
                    && (Group(best) < Group(g.Target) || Group(best) == Group(g.Target) && 4 * k.Strength(best, Enemy) < 3 * k.Strength(g.Target, Enemy))))
                g.Target = best; // the patience clock runs on, so two targets cannot trade places for ever
        if (_groups.Count == 0 && Free(null) is { } first) _groups.Add(new(first));
        foreach (var g in _groups)
            if (g.Staging is null || g.Staging.Owner != Side || g.Staging.Destroyed || threatened.Contains(g.Staging)
                || Orders.TravelDays(game, g.Staging, g.Target, 100) > ReachDays && Stage(g.Target) != g.Staging)
                g.Staging = Stage(g.Target);

        // fleets: each group its staging system's idle fleets, then the nearest idle fleets until 1.5 x its target's defence
        // (fleets with troops at a target land them there; others there gather again)
        var idle = Fleets(game).Where(f => Idle(game, f, used) && !(_groups.Any(g => f.Container == g.Target) && Carrying(f))).OrderBy(f => f.Key).ToList();
        var share = new Dictionary<AttackGroup, List<Fleet>>();
        int Value(IEnumerable<Fleet> list) => list.Sum(f => k.Strength(f, Side));
        void Take(AttackGroup g)
        {
            var mine = share[g] = [.. idle.Where(f => f.Container == g.Staging)];
            idle.RemoveAll(mine.Contains);
            double wanted = 1.5 * k.Strength(g.Target, Enemy);
            int enroute = Value(Fleets(game).Where(f => f.Container == g.Staging && f.Enroute));
            // doctrine: fleets that bring a role the group lacks first
            foreach (var f in idle.Where(f => g.Staging is not null).OrderBy(f => On(V7.DoctrineGather) ? FillsGroup(game, k, g, f, mine) : 0)
                         .ThenBy(f => Orders.Distance((StarSystem)f.Container!, g.Staging!)).ThenBy(f => f.Key).ToList())
            {
                if ((mine.Count > 0 || enroute > 0) && Value(mine) + enroute >= wanted) break;
                mine.Add(f);
                idle.Remove(f);
            }
        }
        foreach (var g in _groups.ToList()) Take(g);
        while (_groups.Count < MaxGroups && idle.Count > 0 && Free(null) is { } next && Value(idle) >= 1.5 * k.Strength(next, Enemy))
        {
            var g = new AttackGroup(next) { Staging = Stage(next) };
            _groups.Add(g);
            Take(g);
        }
        if (_groups.Count > 0) share[_groups[0]].AddRange(idle); // the rest join the first group (v1 behaviour for a single group)
        else if (NoLaunchLog is not null) // measurement: idle fleets without a group
            foreach (var f in idle)
                NoLaunchWhy(game, false, candidates.Count == 0 ? "no target in reach" : Free(null) is null ? "target taken by another group" : "target known strength above own");

        foreach (var g in _groups.ToList())
        {
            if (!Room) return;
            Run(game, k, used, g, share[g]);
        }
    }

    /// <summary>One group's turn: landing at the target, gathering its fleets, troops aboard, the launch, the patience clock.</summary>
    void Run(GameState game, Known k, HashSet<GameObject> used, AttackGroup g, List<Fleet> fleets)
    {
        var target = g.Target;
        if (g.Staging is not { } staging) return;
        bool committed = Fleets(game).Any(f => f.Container == target);

        // at the target: the fleets with troops aboard land them once no known enemy fleet is left; the others gather again
        var there = Fleets(game).Where(f => f.Container == target && f.Usable && !f.Enroute && !used.Contains(f) && Carrying(f)).OrderBy(f => f.Key).ToList();
        int landing = TroopRoomLog is null ? 0 : TroopsAboard(there); // measurement only
        // LandGate rule 3: no landing below the target's attack troops; the fleets wait in orbit, then return
        bool clear = there.Count > 0 && k.Fleets(target, Enemy).Count == 0;
        bool siege = g.Fleet is not null && _siege == target; // Siege: bombard until the defence and troops are gone, then land
        if (siege && SiegeBombard(game, k, target, used)) { }
        else if (clear && !LandAllowed(k, there, target)) HoldLanding(game, target, staging, there, used);
        else if (clear && Issue(game, new Command(0x234, Side) { Targets = [.. there.Select(f => ((GameObject)f, 0))], ArgRef = target }, used))
        {
            RecordAge(game, "assault", target);
            RecordOwnLanding(game, target, landing, AttackTroopsFor(k, target)); // measurement only
            _orbitWait.Remove(target.Ref);
        }

        // gather
        foreach (var f in fleets.Where(f => f.Container != staging && !there.Contains(f) && Idle(game, f, used)).OrderBy(f => f.Key).ToList())
            if (!Issue(game, Move(Side, f, staging), used) && !Room) return;

        // troops aboard
        var gathered = fleets.Where(f => f.Container == staging && Idle(game, f, used)).OrderBy(f => f.Key).ToList();
        var ships = gathered.SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).OrderBy(x => x.Key)).ToList();
        var room = ships.ToDictionary(x => x, x => TroopRoom(game, x));
        if (g.Fleet is not null) _step = "swarm re-embark"; // measurement only
        foreach (var troop in g.Fleet is null ? Spare(staging, used) : SwarmSpare(game, staging, used)) // rule 4: 2 troops stay
            if (ships.FirstOrDefault(x => room[x] > 0) is { } ship && Issue(game, Move(Side, troop, ship.Container!), used)) room[ship]--;
        _step = "attack loading";
        int wanted = room.Values.Sum() - staging.Contents.Count(x => OwnTroop(x, Side) && x.Enroute);
        foreach (var from in Own(game).Where(x => x != staging && !AtRisk(x)).OrderBy(x => Orders.Distance(x, staging)).ThenBy(x => x.Key))
            foreach (var troop in Spare(from, used))
                if (wanted > 0 && Issue(game, Move(Side, troop, staging), used)) wanted--;

        // launch; a target the gathered fleets stay too weak for during TargetPatienceDays is passed over for PassDays
        // Intel: no launch against a system without a fresh view (its espionage goes first; the patience clock runs on)
        // Swarm groups: rule 3's troops, rule 2's stale-view acceptance at the median enemy fleet
        int troops = g.Fleet is not null && fleets.Count > 0 ? SwarmTroopsFor(game, k, fleets[0], target) : AttackTroopsFor(k, target);
        // an unscouted target with no enemy fleet seen yet (median 0) waits for a look (GitHub issue 1)
        Func<List<Fleet>, bool>? accept = g.Fleet is not null && !Fresh(game, target)
            ? group => SwarmStaleAccept(group.Sum(f => k.Strength(f, Side))) && (MedianEnemyFleet > 0 || !Unscouted(game, target)) : null;
        if (siege) accept = group => group.Sum(f => k.Strength(f, Side)) >= 1.5 * k.Strength(target, Enemy); // Siege: the groups together
        if (siege && Side == Owner.Empire) troops = Math.Max(troops, SiegeTroopsFor(game, k, target)); // rule 1: attack troops + requirement
        if ((!On(V7.Intel) || Fresh(game, target))
            && Launch(game, gathered, target, k.Strength(target, Enemy), f => k.Strength(f, Side), used, troops, k, accept))
        {
            RecordAge(game, "attack", target);
            if (siege) SiegeLaunches.Add((game.Day, gathered.Sum(f => k.Strength(f, Side)), k.Strength(target, Enemy))); // measurement
            (g.HeldSince, _launchedAt) = (null, target.Ref);
            AttackLaunches++; // measurement
            if (g.Fleet is not null && fleets.Count > 0) RecordSwarmTroops(game, g, fleets[0], staging, troops, false, used); // measurement
            return;
        }
        string reason = NoLaunchLog is null && TroopRoomLog is null ? "" : On(V7.Intel) && !Fresh(game, target) ? "other" : LaunchReason(k, gathered, target, troops); // measurement
        if (NoLaunchLog is not null) NoLaunchWhy(game, true, reason);
        if (g.Fleet is not null && fleets.Count > 0) RecordSwarmTroops(game, g, fleets[0], staging, troops, reason == "not enough troops loaded", used); // measurement
        if (gathered.Count == 0) return; // nothing gathered: no patience clock
        g.HeldSince ??= game.Day;
        if (game.Day - g.HeldSince >= TargetPatienceDays && !committed)
        {
            _passed[target.Ref] = game.Day + PassDays;
            _groups.Remove(g);
        }
    }

    /// <summary>The groups and losses for the save: groups [target, staging (0 none), held since (-1 none)], losses [system, day].</summary>
    void SaveAttack(JsonObject j)
    {
        if (_groups.Count > 0)
            j["groups"] = new JsonArray([.. _groups.Select(g => (JsonNode)(g.Fleet is { } f ? new JsonArray(g.Target.Ref, g.Staging?.Ref ?? 0, g.HeldSince ?? -1, f)
                : new JsonArray(g.Target.Ref, g.Staging?.Ref ?? 0, g.HeldSince ?? -1)))]);
        if (_lost.Count > 0) j["lost"] = new JsonArray([.. _lost.Select(e => (JsonNode)new JsonArray(e.Key, e.Value.Day, e.Value.Value))]);
    }

    /// <summary>Restores <see cref="SaveAttack"/>; a save from before the multi-group format holds one group as "target" / "staging" / "heldSince".</summary>
    void LoadAttack(GameState game, JsonObject j)
    {
        StarSystem? System(uint r) => r == 0 ? null : game.Systems.First(s => s.Ref == r);
        if (j["groups"] is JsonArray groups)
            foreach (var e in groups)
                _groups.Add(new(System((uint)e![0]!)!) { Staging = System((uint)e[1]!), HeldSince = (int)e[2]! is var h && h >= 0 ? h : null,
                    Fleet = e.AsArray().Count > 3 ? (uint)e[3]! : null });
        else if (j["target"] is { } t)
            _groups.Add(new(System((uint)t)!) { Staging = j["staging"] is { } st ? System((uint)st) : null, HeldSince = (int?)j["heldSince"] });
        if (j["lost"] is JsonArray lost) foreach (var e in lost) _lost[(uint)e![0]!] = ((int)e[1]!, e.AsArray().Count > 2 ? (int)e[2]! : 0);
    }
}
