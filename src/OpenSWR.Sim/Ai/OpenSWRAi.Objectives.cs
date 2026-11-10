using OpenSWR.Data.Dat;
using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Guard (own objective characters stay in own
/// systems), Hunt (find and abduct the enemy objective characters; the Empire's HQ search) and Siege (the known enemy HQ or
/// Coruscant open to every swarm group, bombardment before the landing). Views and own data only; the existing order paths
/// (CreateMission missions, Move commands, BombardVariant2 bombardment, Assault commands); no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>Hunt: a character location older than this many days counts as lost; at most this many hunt espionage missions, objective abductions, HQ searches at once.</summary>
    public const int HuntAgeDays = 30, MaxHuntSpies = 3, MaxHunts = 2, MaxHqSearch = 3;
    /// <summary>Hunt: a hunt espionage target counts as running for this many days after its order (Inferred: a mission's length).</summary>
    const int HuntSpyDays = 60;
    const uint MonMothma = Refs.Mothma;

    bool Guarding => On(V7.Guard);
    bool Hunting => On(V7.Hunt);
    bool Sieging => On(V7.Siege);

    /// <summary>The objective characters of a side: Alliance Luke and Mon Mothma, Empire Vader and Palpatine.</summary>
    static uint[] ObjectiveRefs(Owner side) => side == Owner.Alliance ? [Refs.Luke, MonMothma] : [Refs.Vader, Refs.Palpatine];

    bool OwnObjective(GameObject o) => o is Character c && ObjectiveRefs(Side).Contains(c.Ref);
    bool EnemyObjective(GameObject o) => o is Character c && ObjectiveRefs(Enemy).Contains(c.Ref);

    /// <summary>The Alliance HQ: the first object of families 32 and 33 by key (as Victory.Recompute; the real registry, its view is read through Knowledge).</summary>
    static GameObject? HqObject(GameState game) => game.Created.Where(o => Family.Of(o.Type) is >= Families.Headquarters and < Families.IonCannon).MinBy(o => o.Key);

    // ---- Guard ----

    /// <summary>
    /// Guard: a mission (CreateMission command) or move (Move command) with an own objective character among its team or decoys whose destination is not
    /// an own system is refused (the destination: the ArgRef system, else the system the side's view places the ArgRef in).
    /// </summary>
    internal bool GuardRefuses(GameState game, Command c)
    {
        if (!Guarding || c.Type is not (CommandType.CreateMission or CommandType.Move) || !c.Targets.Select(t => t.Target).Concat(c.Decoys).Any(OwnObjective)) return false;
        var to = c.ArgRef as StarSystem ?? (c.ArgRef is { } a ? Knowledge.Location(game, a, Side) as StarSystem ?? a.System : null);
        return to is null || to.Owner != Side;
    }

    /// <summary>
    /// Guard: where an idle own objective character waits: the HQ system (Alliance) or Coruscant (Empire) while owned, else the own
    /// system with the most own troops, nearest first (Inferred: "the nearest own system with the most troops" as most troops, then nearest).
    /// </summary>
    StarSystem? GuardHome(GameState game, Character c)
    {
        var home = Side == Owner.Alliance ? HqObject(game) is { Destroyed: false } hq ? hq.System : null : game.Known.Coruscant;
        if (home is { Destroyed: false } && home.Owner == Side) return home;
        var at = Missions.Location(c) as StarSystem;
        return Own(game).OrderByDescending(s => s.Contents.Count(x => OwnTroop(x, Side)))
            .ThenBy(s => at is null ? 0 : Orders.Distance(at, s)).ThenBy(s => s.Key).FirstOrDefault();
    }

    /// <summary>Guard: the own objective characters left free after the missions move to their waiting place.</summary>
    void GuardIdle(GameState game, List<Character> free, HashSet<GameObject> used)
    {
        foreach (var c in free.Where(OwnObjective).ToList())
            if (Room && GuardHome(game, c) is { } home && Missions.Location(c) != home && Issue(game, Move(Side, c, home), used)) free.Remove(c);
    }

    // ---- Hunt: the HQ search (Empire side) ----

    /// <summary>HQ candidates: per system ref, the day it joined the set (it leaves on a view written after that day).</summary>
    readonly SortedDictionary<uint, int> _hqCandidates = [];
    bool _hqInit;
    StarSystem? _mothmaAt; // this decision's Mon Mothma hint
    int _hintDay; // the last day her view added a candidate (measurement-free state; not saved: Inferred, a hint is re-read from the view)
    uint? _hqKnownAt;

    /// <summary>Measurement: the HQ search (candidates left, the first day the HQ system was known, relocations seen) and the hunt.</summary>
    public int HqCandidatesLeft => _hqCandidates.Count;
    public int? HqFoundDay { get; private set; }
    public int HqRelocationsSeen { get; private set; }
    public int HuntSpiesSent { get; private set; }
    /// <summary>Measurement: per enemy objective character, the days from losing track (location unknown or older than <see cref="HuntAgeDays"/>) to finding again.</summary>
    public List<(uint Character, int Days)> TrackGaps { get; } = [];
    readonly SortedDictionary<uint, int> _lostSince = [];

    /// <summary>Test hook: the HQ candidates.</summary>
    internal IReadOnlyCollection<uint> HqCandidates => _hqCandidates.Keys;

    static bool Rim(StarSystem s) => Family.Of(s.Type) is >= Families.RimSystem and < Families.RimSystemEnd;

    /// <summary>
    /// The HQ candidate set: at the first decision every rim system the view has not explored
    /// minus Yavin; a candidate leaves once the view shows it explored without an HQ; when the HQ's view leaves its last known
    /// system the set is rebuilt from every Alliance-held or unexplored system.
    /// </summary>
    internal void UpdateHqSearch(GameState game, Known k)
    {
        if (Side != Owner.Empire) return;
        var views = game.Systems.ToDictionary(s => s, s => Knowledge.See(game, s, Side));
        if (!_hqInit)
        {
            _hqInit = true;
            foreach (var s in game.Systems.Where(s => Rim(s) && s.Type != Refs.Yavin && !views[s].Explored)) _hqCandidates[s.Ref] = game.Day;
        }
        var hq = HqObject(game);
        var at = hq is null || Knowledge.See(game, hq, Side).Destroyed ? null : Knowledge.Location(game, hq, Side) as StarSystem;
        if (_hqKnownAt is { } last && at?.Ref != last)
        {
            HqRelocationsSeen++;
            _hqCandidates.Clear();
            foreach (var s in game.Systems.Where(s => !views[s].Destroyed && (views[s].Owner == Enemy || !views[s].Explored))) _hqCandidates[s.Ref] = game.Day;
        }
        // Inferred: an emptied set with the HQ unknown (it moved before it was found) is rebuilt the same way
        if (at is null && _hqCandidates.Count == 0)
            foreach (var s in game.Systems.Where(s => !views[s].Destroyed && (views[s].Owner == Enemy || !views[s].Explored) && s.Type != Refs.Yavin)) _hqCandidates[s.Ref] = game.Day;
        // Inferred: an enemy-held system where the
        // view places her joins the set and is searched first
        var mothma = game.Characters.FirstOrDefault(c => c.Ref == MonMothma);
        _mothmaAt = mothma is not null && Knowledge.See(game, mothma, Side) is { Captured: false, Location: StarSystem m } && views[m].Owner == Enemy ? m : null;
        if (at is null && _mothmaAt is { } hint && !_hqCandidates.ContainsKey(hint.Ref) && Knowledge.UpdatedDay(game, mothma!, Side) > _hintDay)
        {
            _hqCandidates[hint.Ref] = game.Day;
            _hintDay = game.Day;
        }
        _hqKnownAt = at?.Ref;
        if (at is not null) HqFoundDay ??= game.Day;
        // a candidate leaves once a view written since it joined (live, or a mission's refresh) shows it explored without an HQ
        foreach (var s in game.Systems.Where(s => _hqCandidates.TryGetValue(s.Ref, out int added) && (views[s].Destroyed
                     || views[s].Explored && s != at && Knowledge.UpdatedDay(game, s, Side) >= Math.Max(1, added) && !k.Contents(s).Any(v => v.In(Families.Headquarters, Families.IonCannon)))).ToList())
            _hqCandidates.Remove(s.Ref);
    }

    /// <summary>The HQ candidates in search order: nearest a known Alliance-held rim system, then the highest Alliance support (view); unexplored and explored.</summary>
    List<StarSystem> HqSearchOrder(GameState game)
    {
        var views = game.Systems.ToDictionary(s => s, s => Knowledge.See(game, s, Side));
        var held = game.Systems.Where(s => Rim(s) && views[s].Owner == Enemy && !views[s].Destroyed).ToList();
        int Near(StarSystem s) => held.Count == 0 ? 0 : held.Min(h => Orders.Distance(h, s));
        return [.. game.Systems.Where(s => _hqCandidates.ContainsKey(s.Ref)).OrderBy(s => s == _mothmaAt ? 0 : 1).ThenBy(Near).ThenByDescending(s => views[s].Support).ThenBy(s => s.Key)];
    }

    // ---- Hunt: finding the enemy objective characters (rule 1a) ----

    /// <summary>Hunt espionage targets ordered: per system ref, the order day.</summary>
    readonly SortedDictionary<uint, int> _huntSpies = [];

    /// <summary>
    /// Rule 1a: an enemy objective character not captured, not dead (side's view) is lost when the view has no system for it or its
    /// view is older than <see cref="HuntAgeDays"/>; then its search systems: the last known system, its side's HQ (Mon Mothma, and
    /// Luke: Inferred) or Coruscant (Vader, Palpatine) as the view knows them, the enemy systems with the most enemy characters seen,
    /// the enemy systems nearest the last known one. Empty = not lost.
    /// </summary>
    internal List<StarSystem> HuntSearch(GameState game, Known k, Character c)
    {
        var seen = Knowledge.See(game, c, Side);
        if (seen.Captured || seen.Destroyed) return [];
        var last = seen.Location as StarSystem;
        if (last is not null && game.Day - Knowledge.UpdatedDay(game, c, Side) <= HuntAgeDays) return [];
        var list = new List<StarSystem>();
        if (last is not null) list.Add(last);
        var home = c.Ref is Refs.Vader or Refs.Palpatine ? game.Known.Coruscant
            : HqObject(game) is { } hq && !Knowledge.See(game, hq, Side).Destroyed ? Knowledge.Location(game, hq, Side) as StarSystem : null;
        if (home is not null) list.Add(home);
        var enemy = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        list.AddRange(enemy.Select(s => (s, n: k.Contents(s).Count(v => v.Owner == Enemy && v.In(Families.MajorCharacter, Families.SpecialForce)))).Where(x => x.n > 0)
            .OrderByDescending(x => x.n).ThenBy(x => x.s.Key).Select(x => x.s));
        if (last is not null) list.AddRange(enemy.Where(s => s != last).OrderBy(s => Orders.Distance(last, s)).ThenBy(s => s.Key));
        return [.. list.Distinct()];
    }

    /// <summary>Measurement: losing and regaining track of the enemy objective characters.</summary>
    void TrackHunt(GameState game, Known k)
    {
        foreach (var c in game.Characters.Where(EnemyObjective))
        {
            bool lost = HuntSearch(game, k, c).Count > 0;
            if (lost) _lostSince.TryAdd(c.Ref, game.Day);
            else if (_lostSince.Remove(c.Ref, out int since) && !Knowledge.See(game, c, Side).Captured) TrackGaps.Add((c.Ref, game.Day - since));
        }
    }

    /// <summary>One espionage mission at a system: a free special force the list offers first, else the best free character (by espionage).</summary>
    bool SpyAt(GameState game, Known k, StarSystem target, List<Character> free, HashSet<GameObject> used)
    {
        if (TypeOf(game, MissionKind.Espionage) is not { } type) return false;
        foreach (var a in FreeSpecialForces(game, used).OrderByDescending(a => Missions.Enhanced(game, a, SkillIndex.Espionage)).ThenBy(a => a.Key).Cast<Agent>()
                     .Concat(free.OrderByDescending(c => Missions.Enhanced(game, c, SkillIndex.Espionage)).ThenBy(c => c.Key)))
        {
            if (!Room) return false;
            if (MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, [a], [], target) is not null) continue;
            if (!Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(a, GroupMode.Object)], ArgRef = target, Fields = [0, 0, type.Common.Ref] }, used)) continue;
            RecordEstimate(game, k, MissionKind.Espionage, target, [a], -1, -1);
            if (a is Character ch) free.Remove(ch);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Hunt, run first in rule 5: the abductions of the enemy objective characters (highest success first, at most
    /// <see cref="MaxHunts"/> running, one per target), then rule 1a's espionage (at most <see cref="MaxHuntSpies"/> running), then
    /// the Empire's HQ search (reconnaissance on unexplored candidates by scouting units, espionage on explored ones; at most
    /// <see cref="MaxHqSearch"/> running).
    /// </summary>
    void Hunt(GameState game, Known k, SectorMap map, List<Character> free, List<Agent> scoutUnits, List<Mission> running, ISet<GameObject?> targeted,
        HashSet<GameObject> used, Action<IEnumerable<Covert>> sendCovert)
    {
        int hunts = running.Count(m => m.Kind == MissionKind.Abduction && m.Objective is { } o && EnemyObjective(o));
        if (hunts < MaxHunts)
        {
            var abductions = HuntAbductions(game, k, free, targeted, used).Take(MaxHunts - hunts).ToList();
            var assumed = abductions.ToDictionary(x => x.Target, x => (Combat: TargetCombat(game, (Character)x.Target), Seen: ScreenViews.Sees(x.Target, Side))); // measurement, before the orders
            sendCovert(abductions);
            foreach (var x in abductions.Where(x => targeted.Contains(x.Target))) HuntEstimates.Add((game.Day, x.Target.Ref, x.Success, assumed[x.Target].Combat, assumed[x.Target].Seen)); // measurement
        }
        foreach (var r in _huntSpies.Where(e => game.Day - e.Value >= HuntSpyDays).Select(e => e.Key).ToList()) _huntSpies.Remove(r);
        int spies = running.Count(m => m.Kind == MissionKind.Espionage && m.Objective is StarSystem s && _huntSpies.ContainsKey(s.Ref));
        foreach (var c in game.Characters.Where(EnemyObjective).OrderBy(c => c.Key))
            foreach (var s in HuntSearch(game, k, c))
            {
                if (spies >= MaxHuntSpies || !Room) break;
                if (targeted.Contains(s)) continue;
                if (!SpyAt(game, k, s, free, used)) continue;
                targeted.Add(s);
                _huntSpies[s.Ref] = game.Day;
                HuntSpiesSent++;
                spies++;
                break; // one search per character per decision
            }
        if (Side != Owner.Empire) return;
        int searching = running.Count(m => m.Kind is MissionKind.Espionage or MissionKind.Reconnaissance && m.Objective is StarSystem s && _hqCandidates.ContainsKey(s.Ref));
        var recon = TypeOf(game, MissionKind.Reconnaissance);
        foreach (var s in HqSearchOrder(game))
        {
            if (searching >= MaxHqSearch || !Room) break;
            if (targeted.Contains(s)) continue;
            bool sent = false;
            if (!Knowledge.See(game, s, Side).Explored)
            {
                if (recon is not null && scoutUnits.FirstOrDefault(a => MissionEligibility.Validate(game, CommandType.CreateMission, Side, recon.Common.Ref, [a], [], s) is null) is { } unit
                    && Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(unit, GroupMode.Object)], ArgRef = s, Fields = [0, 0, recon.Common.Ref] }, used))
                {
                    scoutUnits.Remove(unit);
                    sent = true;
                }
            }
            else sent = SpyAt(game, k, s, free, used);
            if (!sent) continue;
            targeted.Add(s);
            searching++;
        }
    }

    /// <summary>An objective abduction goes only at this estimated success or more (benchmark variants 0.25 and 0.4: <see cref="HuntGate"/>).</summary>
    public const double MinHuntSuccess = 0.25;

    /// <summary>The gate in use (default <see cref="MinHuntSuccess"/>). Not saved (a run setting).</summary>
    public double HuntGate { get; set; } = MinHuntSuccess;

    /// <summary>HuntStill: a target whose nearest passing abductor needs more travel days than this is skipped (a first guess, not tuned).</summary>
    public const int MaxHuntDays = 20;

    /// <summary>
    /// Travel days of a team to <paramref name="to"/>: the slowest member, each as a travel start (Fleets.Travel) from its location
    ///.
    /// </summary>
    internal static int HuntDays(GameState game, IEnumerable<Agent> team, StarSystem to) =>
        team.Max(a => Missions.Location(a) is not { } from || from == to ? 0 : from is StarSystem s ? Orders.TravelDays(game, s, to, Orders.Speed(game, a)) : 1);

    /// <summary>A target that failed the gate waits while the view shows it in that system: per character ref, the system ref. Saved.</summary>
    readonly SortedDictionary<uint, uint> _huntWait = [];

    /// <summary>Measurement: the objective abductions sent, with their estimate, the target EnhCombat it assumed (<see cref="TargetCombat"/>) and whether the target was seen live at the order.</summary>
    public List<(int Day, uint Target, double Estimate, int AssumedCombat, bool Seen)> HuntEstimates { get; } = [];

    /// <summary>
    /// The gate's estimate: the attempt chance T(ABDCMSTB, best member's EnhCombat - target's EnhCombat) (<see cref="Attempt"/>)
    /// times the no-foil chance at the target's location (<see cref="FoilChance"/>: every known enemy unit of the resolver's walk,
    /// after the decoy pass).
    /// </summary>
    internal double HuntEstimate(GameState game, Known k, List<Agent> team, List<Agent> decoys, Character target, CovertKnown known) =>
        (1 - FoilChance(game, team, known, decoys))
        * Math.Clamp(Missions.Table(game, "ABDCMSTB", team.Max(a => Missions.Enhanced(game, a, SkillIndex.Combat)) - TargetCombat(game, target)) ?? 0, 0, 100) / 100.0;

    /// <summary>
    /// The target's EnhCombat for the estimate: live, the real value; else from the definition (Inferred, as CharacterValue: base +
    /// variance / 2), with the Force bonus (Missions.Enhanced's b + b * Force / 100) at the definition's Jedi level (base + variance / 2)
    /// for a definition-known Jedi (Vader 220, as the definitions give it).
    /// </summary>
    internal int TargetCombat(GameState game, Character c)
    {
        if (ScreenViews.Sees(c, Side)) return Missions.Enhanced(game, c, SkillIndex.Combat);
        var d = c.Definition;
        int b = (int)(d.Skills.Combat.Base + d.Skills.Combat.Variance / 2);
        return d.IsKnownJedi != 0 ? b + GameMath.Pct(b, (int)(d.JediLevel.Base + d.JediLevel.Variance / 2)) : b;
    }

    /// <summary>
    /// Hunt rule 1a "then strike" with the gate: per found enemy objective character (the view places it in a system, not
    /// older than <see cref="HuntAgeDays"/>, not captured) the abductor is the free character or special force the list offers there
    /// with the highest EnhCombat (Jedi with their Force bonus, <see cref="Missions.Enhanced"/>), then the decoys (Agents);
    /// it goes when <see cref="HuntEstimate"/> reaches <see cref="HuntGate"/>, else the target waits until the view shows it
    /// elsewhere. Highest estimate first.
    /// </summary>
    internal List<Covert> HuntAbductions(GameState game, Known k, List<Character> free, ISet<GameObject?> targeted, HashSet<GameObject> used)
    {
        var result = new List<Covert>();
        if (TypeOf(game, MissionKind.Abduction) is not { } type) return result;
        var median = MedianSystem(game, k);
        var forces = FreeSpecialForces(game, used);
        foreach (var c in game.Characters.Where(EnemyObjective).OrderBy(c => c.Key))
        {
            var seen = Knowledge.See(game, c, Side);
            if (targeted.Contains(c) || seen.Captured || seen.Destroyed || seen.Location is not StarSystem at || game.Day - Knowledge.UpdatedDay(game, c, Side) > HuntAgeDays) continue;
            if (_huntWait.TryGetValue(c.Ref, out uint waitAt))
            {
                if (waitAt == at.Ref) continue;
                _huntWait.Remove(c.Ref);
            }
            bool still = On(V7.HuntStill);
            if (still && Knowledge.ViewParent(game, c, Side) != at) continue; // only a target the view places directly in the system
            var known = Assume(game, k, at, KnownAt(game, k, at, Knowledge.ViewParent(game, c, Side) is StarSystem, median), median);
            var pool = free.Cast<Agent>().Concat(forces).Where(a => !used.Contains(a) && result.All(x => !x.Team.Contains(a) && !(x.Decoys ?? []).Contains(a))).ToList();
            bool Valid(List<Agent> team, List<Agent> decoys) => MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, team, [.. decoys], c) is null;
            (List<Agent> Team, List<Agent> Decoys) Plan(Agent a) =>
                On(V7.Agents) && AgentDecoys(game, MissionKind.Abduction, c, known, [a], [], pool.Where(x => x != a), Valid, out _, k) is { } p ? p : ([a], []);
            if (still)
            {
                // HuntStill: among the candidates whose planned estimate reaches the gate, the fewest travel days, then the higher estimate
                var best = pool.Where(a => Valid([a], [])).Select(a => (Plan: Plan(a), Key: a.Key)).Select(x => (x.Plan, x.Key, Estimate: HuntEstimate(game, k, x.Plan.Team, x.Plan.Decoys, c, known)))
                    .Where(x => x.Estimate >= HuntGate).Select(x => (x.Plan, x.Key, x.Estimate, Days: HuntDays(game, x.Plan.Team, at)))
                    .OrderBy(x => x.Days).ThenByDescending(x => x.Estimate).ThenBy(x => x.Key).ToList();
                if (best.Count == 0)
                {
                    _huntWait[c.Ref] = at.Ref;
                    continue;
                }
                if (best[0].Days > MaxHuntDays) continue;
                result.Add(new(MissionKind.Abduction, c, best[0].Plan.Team, 0, best[0].Estimate, best[0].Plan.Decoys));
                continue;
            }
            var agent = pool.Where(a => Valid([a], [])).OrderByDescending(a => Missions.Enhanced(game, a, SkillIndex.Combat)).ThenBy(a => a.Key).FirstOrDefault();
            if (agent is null) continue;
            List<Agent> team = [agent], decoys = [];
            if (On(V7.Agents) && AgentDecoys(game, MissionKind.Abduction, c, known, team, [], pool.Where(a => a != agent), Valid, out _, k) is { } planned) (team, decoys) = planned;
            double estimate = HuntEstimate(game, k, team, decoys, c, known);
            if (estimate < HuntGate)
            {
                _huntWait[c.Ref] = at.Ref;
                continue;
            }
            result.Add(new(MissionKind.Abduction, c, team, 0, estimate, decoys));
        }
        return [.. result.OrderByDescending(x => x.Success).ThenBy(x => x.Target.Key)];
    }

    /// <summary>Hunt rule 1: the objective abductions first in a ranked covert list.</summary>
    internal List<Covert> HuntFirst(IEnumerable<Covert> ranked) => [.. ranked.OrderBy(x => x.Kind == MissionKind.Abduction && EnemyObjective(x.Target) ? 0 : 1)];

    // ---- Siege ----

    /// <summary>
    /// Siege: the known enemy HQ system (Empire side, the HQ's view location while not Destroyed) or Coruscant (Alliance side, only
    /// while the side owns more systems than its views show the enemy owning: "ahead in systems", Inferred), when the view shows it enemy-held.
    /// </summary>
    internal StarSystem? SiegeTarget(GameState game)
    {
        StarSystem? s = Side == Owner.Empire
            ? HqObject(game) is { } hq && !Knowledge.See(game, hq, Side).Destroyed ? Knowledge.Location(game, hq, Side) as StarSystem : null
            : game.Known.Coruscant;
        if (s is null || Knowledge.See(game, s, Side) is not { Owner: var o, Destroyed: false } || o != Enemy) return null;
        if (Side == Owner.Alliance && Own(game).Count <= game.Systems.Count(x => Knowledge.See(game, x, Side) is { Owner: var e, Destroyed: false } && e == Enemy)) return null;
        return s;
    }

    StarSystem? _siege; // this decision's siege target
    uint? _lastSiege; // the previous decision's siege target (the siege follows the HQ). Saved.

    /// <summary>Measurement: siege groups formed (fleet, target pairs) and each siege launch's combined strength against the known defence.</summary>
    public int SiegeGroupsFormed => _siegeJoined.Count;
    readonly HashSet<(uint, uint)> _siegeJoined = [];
    public List<(int Day, int Strength, int Defence)> SiegeLaunches { get; } = [];

    /// <summary>
    /// Siege rule 1 (Empire side, the HQ system): the troops a siege group carries at launch: the target's attack troops plus
    /// the garrison requirement the side would have there (Systems.BaseRequirement at the side's share in its view of the system).
    /// </summary>
    internal int SiegeTroopsFor(GameState game, Known k, StarSystem target)
    {
        int support = Knowledge.See(game, target, Side).Support;
        return AttackTroopsFor(k, target) + Systems.BaseRequirement(game, Side == Owner.Alliance ? support : 100 - support);
    }

    /// <summary>
    /// Siege rule 2: when the siege target changes (the HQ moved and the view knows the new system), the groups on the old
    /// one take the new one; with no new one they choose again.
    /// </summary>
    void FollowSiege(IEnumerable<AttackGroup> groups)
    {
        if (_lastSiege is { } old && old != _siege?.Ref)
            foreach (var g in groups.Where(g => g.Target?.Ref == old)) g.Target = _siege!;
        _lastSiege = _siege?.Ref;
    }

    /// <summary>Measurement: decisions with a siege target, bombardments tried and issued.</summary>
    public int SiegeDays { get; private set; }
    public int SiegeBombardTries { get; private set; }
    public int SiegeBombards { get; private set; }

    /// <summary>Siege: a swarm group's fleet within <see cref="ReachDays"/> (speed 100) of the siege target takes it, whatever other groups hold.</summary>
    bool SiegeReach(GameState game, StarSystem from) => _siege is { } s && Orders.TravelDays(game, from, s, 100) <= ReachDays;

    /// <summary>Test hook: the siege target a fleet at <paramref name="from"/> takes (null: none in reach).</summary>
    internal StarSystem? SiegeTargetFor(GameState game, StarSystem from)
    {
        _siege = Sieging ? SiegeTarget(game) : null;
        return SiegeReach(game, from) ? _siege : null;
    }

    /// <summary>
    /// Siege at the target: while own fleets hold the orbit with no known enemy fleet and the view shows enemy troops or defence
    /// facilities (families 34 to 39) there, they bombard.
    /// True when a bombardment went out (no landing this decision).
    /// </summary>
    bool SiegeBombard(GameState game, Known k, StarSystem target, HashSet<GameObject> used)
    {
        var there = Fleets(game).Where(f => f.Container == target && f.Usable && !f.Enroute && !used.Contains(f)).OrderBy(f => f.Key).ToList();
        if (there.Count == 0 || k.Fleets(target, Enemy).Count > 0 || !EnemyGarrisonSeen(k, target)) return false;
        SiegeBombardTries++;
        bool ok = IssueBombard(game, target, there, used, CommandType.BombardVariant2);
        if (ok) SiegeBombards++;
        return ok;
    }

    void SaveObjectives(JsonObject j)
    {
        if (_hqInit) j["hqInit"] = true;
        if (_hqCandidates.Count > 0) j["hqCandidates"] = new JsonArray([.. _hqCandidates.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_hqKnownAt is { } at) j["hqKnownAt"] = at;
        if (_huntSpies.Count > 0) j["huntSpies"] = new JsonArray([.. _huntSpies.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_huntWait.Count > 0) j["huntWait"] = new JsonArray([.. _huntWait.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_lastSiege is { } siege) j["lastSiege"] = siege;
    }

    void LoadObjectives(JsonObject j)
    {
        _hqInit = j["hqInit"] is not null;
        if (j["hqCandidates"] is JsonArray a) foreach (var e in a) _hqCandidates[(uint)e![0]!] = (int)e[1]!;
        if (j["hqKnownAt"] is { } at) _hqKnownAt = (uint)at;
        if (j["huntSpies"] is JsonArray b) foreach (var e in b) _huntSpies[(uint)e![0]!] = (int)e[1]!;
        if (j["huntWait"] is JsonArray w) foreach (var e in w) _huntWait[(uint)e![0]!] = (uint)e[1]!;
        if (j["lastSiege"] is { } siege) _lastSiege = (uint)siege;
    }
}
