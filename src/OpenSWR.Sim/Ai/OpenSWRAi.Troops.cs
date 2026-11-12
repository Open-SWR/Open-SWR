using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    /// <summary>One own troop move order (measurement): day, troop, the system it left, the step that ordered it.</summary>
    public sealed record TroopMove(int Day, uint Troop, uint From, string Step);

    List<StarSystem> Own(GameState game) => [.. game.Systems.Where(s => s.Owner == Side && !s.Destroyed).OrderBy(s => s.Key)];

    IEnumerable<Fleet> Fleets(GameState game) => game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == Side && f.Existing);

    /// <summary>An own fleet free to move: Usable, not Enroute, in a system, with a ship and a speed.</summary>
    bool Idle(GameState game, Fleet f, HashSet<GameObject> used) =>
        !used.Contains(f) && f.Usable && !f.Enroute && f.Container is StarSystem && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing) && Orders.Speed(game, f) > 0;

    static bool OwnTroop(GameObject x, Owner side) => Family.IsTroop(x.Type) && x.Owner == side && x.Existing;

    /// <summary>
    /// Troops an own system keeps: the garrison requirement when at risk (at least 1), else 1 in a populated system; a defended system
    /// at least its target garrison.
    /// </summary>
    int Keep(StarSystem s) => Math.Max(Garrison(s), Need.Defend.GetValueOrDefault(s));

    /// <summary>Rule 1's garrison of an own system: the garrison requirement when at risk (at least 1), else 1 in a populated system (rim-first rule: 1 in an unpopulated rim system, the drop's troop).</summary>
    int Garrison(StarSystem s) => !s.Populated ? (RimFirsting && !s.Sector.IsCore ? 1 : 0) : AtRisk(s) ? Math.Max(1, s.GarrisonRequirement) : 1;

    // ---- troops by need ----

    /// <summary>
    /// The troop need of one decision: the total, the defended systems' target garrisons (those the troops cover), the attack troops
    /// per target, and the attack troops of the targets with known enemy troops (only there do landing troops duel).
    /// </summary>
    internal sealed record TroopNeeds(int Total, Dictionary<StarSystem, int> Defend, Dictionary<StarSystem, int> Attack, int Contested)
    {
        public static readonly TroopNeeds None = new(0, [], [], 0);
    }

    /// <summary>Known enemy troops at a system: the enemy's troops the side's view shows there.</summary>
    int EnemyTroops(Known k, StarSystem s) => k.Contents(s).Count(v => v.Owner == Enemy && v.In(Families.Troop, Families.CapitalShip));

    /// <summary>Attack troops for a target: 1.5 x the known enemy troops there + 1, at least 2.</summary>
    internal int AttackTroopsFor(Known k, StarSystem target) => Math.Max(2, (int)Math.Ceiling(1.5 * EnemyTroops(k, target)) + 1);

    /// <summary>
    /// The troop need, the sum of: the garrisons (rule 1's need of each own system at risk,
    /// 1 in every other populated own system); the defence garrisons beyond those (<see cref="Defence"/>: every high-value threatened
    /// system's target garrison, covered or not); the attack troops of each current target. <see cref="TroopNeeds.Defend"/> holds the
    /// systems the side's troops cover.
    /// </summary>
    internal TroopNeeds TroopNeed(GameState game, Known k)
    {
        var own = Own(game).Where(s => s.Populated).ToList();
        var (targets, covered) = Defence(game, k);
        var attack = Targets().ToDictionary(t => t, t => AttackTroopsFor(k, t));
        return new(own.Sum(Garrison) + targets.Sum(e => Math.Max(0, e.Value - Garrison(e.Key))) + attack.Values.Sum(), covered, attack,
            attack.Where(e => EnemyTroops(k, e.Key) > 0).Sum(e => e.Value));
    }

    /// <summary>Own troops: Existing, anywhere (in systems, aboard ships, under construction).</summary>
    internal int OwnTroops(GameState game) => game.Created.Count(o => OwnTroop(o, Side));

    bool AtRisk(StarSystem s) => s.Populated && (s.SupportFor(Side) < SafeShare || s.Uprising);

    /// <summary>
    /// Own troops in the system beyond <see cref="Keep"/> that can leave now; for an assault's loading (<paramref name="assault"/>
    /// given) with AssaultTroops the highest attack strength leaves first.
    /// </summary>
    List<GameObject> Spare(StarSystem s, HashSet<GameObject> used, GameState? assault = null)
    {
        var ready = Leaving(assault, s.Contents.Where(x => OwnTroop(x, Side) && x.Usable && !x.Enroute && !used.Contains(x)));
        int extra = s.Contents.Count(x => OwnTroop(x, Side) && !used.Contains(x)) - Keep(s);
        return [.. ready.Take(Math.Clamp(extra, 0, ready.Count))];
    }

    /// <summary>Own troops aboard a fleet's ships that can leave now.</summary>
    List<GameObject> FleetTroops(Fleet f, HashSet<GameObject> used) =>
        [.. f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).SelectMany(x => x.Contents)
            .Where(x => OwnTroop(x, Side) && x.Usable && !x.Enroute && !used.Contains(x)).OrderBy(x => x.Key)];

    // ---- rule 1: garrisons ----

    /// <summary>
    /// Rule 1: every own system at risk (support share below <see cref="SafeShare"/> or an
    /// Uprising) needs max(1, garrison requirement) troops, counting those on the way and under construction; spare troops of the
    /// nearest own systems move there. Then the defended systems short of their target garrison, in priority order. Returns the
    /// troops still missing at the systems at risk (rule 3 trains them first).
    /// </summary>
    int Garrisons(GameState game, HashSet<GameObject> used)
    {
        var own = Own(game);
        int missing = 0;
        foreach (var s in own.Where(AtRisk))
        {
            int have = s.Contents.Count(x => OwnTroop(x, Side));
            int need = Keep(s);
            foreach (var from in own.Where(x => x != s && !AtRisk(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key))
                foreach (var troop in Spare(from, used))
                {
                    if (have >= need || !Room) break;
                    if (Issue(game, Move(Side, troop, s), used)) have++;
                }
            missing += Math.Max(0, need - have);
        }
        foreach (var s in Need.Defend.Keys.Where(x => !AtRisk(x) && own.Contains(x)))
        {
            int have = s.Contents.Count(x => OwnTroop(x, Side));
            // the spare troops, nearest first: those beyond the keep of the systems not defended, and those aboard idle
            // fleets outside the attack groups (fleets at a group's staging system)
            var staging = StagingSystems.ToHashSet();
            var sources = !On(V7.Experience)
                ? own.Where(x => x != s && !AtRisk(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).Select(x => (At: x, Troops: Spare(x, used))).ToList()
                : own.Where(x => x != s && !AtRisk(x) && !Need.Defend.ContainsKey(x)).Select(x => (At: x, Troops: Spare(x, used)))
                    .Concat(Fleets(game).Where(f => Idle(game, f, used) && f.Container is StarSystem at && !staging.Contains(at) && at != s)
                        .Select(f => (At: (StarSystem)f.Container!, Troops: FleetTroops(f, used))))
                    .OrderBy(x => Orders.Distance(x.At, s)).ThenBy(x => x.At.Key).ToList();
            foreach (var (_, troops) in sources)
                foreach (var troop in troops)
                {
                    if (have >= Keep(s) || !Room) break;
                    if (Issue(game, Move(Side, troop, s), used)) have++;
                }
        }
        return missing;
    }

    // ---- rule 2: defence ----

    /// <summary>
    /// Rule 2: an own system with known enemy fleets (seen there, or seen Enroute there: the FleetsEnroute map filter's reading) where the own
    /// fleets' predicted win chance is below 0.5 gets the idle own fleet (of the 6 nearest) with the highest win chance per value
    /// moved that brings it to 0.5; if none would, the own idle fleets there leave for the nearest own system without
    /// known enemy fleets. Every own fleet at a threatened system is then committed for the day
    /// (rule 4 does not gather it); returns the threatened systems.
    /// </summary>
    internal List<StarSystem> Defence(GameState game, Known k, HashSet<GameObject> used)
    {
        var own = Own(game);
        var threatened = own.Where(s => k.Fleets(s, Enemy).Count > 0).ToList();
        if (!On(V7.Predict)) return DefenceV6(game, k, used, own, threatened);
        // the space predictor's win chance of the own fleets there (plus one more) against the known enemy fleets
        List<Fleet> Here(StarSystem s) => [.. Fleets(game).Where(f => f.Container == s && f.Usable)];
        double Win(StarSystem s, Fleet? extra = null) => PredictSpace(game, k, s, extra is null ? Here(s) : [.. Here(s), extra], k.Fleets(s, Enemy)).Win;
        // fleets holding a threatened system stay; no fleet at a threatened system is sent to help another
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x) && Win(x) >= 0.5).ToList()) used.Add(f);
        foreach (var s in threatened)
        {
            if (!Room) break;
            double win = Win(s);
            if (win >= 0.5) continue;
            // the help with the highest win chance per value moved (the 6 nearest idle fleets tried), when it brings the chance to 0.5
            var help = Fleets(game).Where(f => f.Container is StarSystem x && !threatened.Contains(x) && Idle(game, f, used))
                .OrderBy(f => Orders.Distance((StarSystem)f.Container!, s)).ThenBy(f => f.Key).Take(6)
                .Select(f => (f, w: Win(s, f), v: Math.Max(1, k.Strength(f, Side)))).Where(x => x.w >= 0.5)
                .OrderByDescending(x => x.w / x.v).ThenBy(x => x.f.Key).Select(x => x.f).FirstOrDefault();
            if (help is not null && Issue(game, Move(Side, help, s), used)) continue;
            var safe = own.Where(x => x != s && !threatened.Contains(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault();
            if (safe is null) continue;
            foreach (var f in Fleets(game).Where(f => f.Container == s && Idle(game, f, used)).OrderBy(f => f.Key).ToList())
                Issue(game, Move(Side, f, safe), used);
        }
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x))) used.Add(f);
        return threatened;
    }

    /// <summary>Rule 2 of the value-sum path (value sums), used with <see cref="V7.Predict"/> off.</summary>
    List<StarSystem> DefenceV6(GameState game, Known k, HashSet<GameObject> used, List<StarSystem> own, List<StarSystem> threatened)
    {
        int Enemies(StarSystem s) => k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy));
        int Mine(StarSystem s) => Fleets(game).Where(f => f.Container == s).Sum(f => k.Strength(f, Side));
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x) && Mine(x) >= Enemies(x)).ToList()) used.Add(f);
        foreach (var s in threatened)
        {
            if (!Room) break;
            int enemy = Enemies(s), mine = Mine(s);
            if (mine >= enemy) continue;
            var help = Fleets(game).Where(f => f.Container is StarSystem x && !threatened.Contains(x) && Idle(game, f, used) && mine + k.Strength(f, Side) >= enemy)
                .OrderBy(f => Orders.Distance((StarSystem)f.Container!, s)).ThenBy(f => f.Key).FirstOrDefault();
            if (help is not null && Issue(game, Move(Side, help, s), used)) continue;
            var safe = own.Where(x => x != s && !threatened.Contains(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault();
            if (safe is null) continue;
            foreach (var f in Fleets(game).Where(f => f.Container == s && Idle(game, f, used)).OrderBy(f => f.Key).ToList())
                Issue(game, Move(Side, f, safe), used);
        }
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x))) used.Add(f);
        return threatened;
    }

    // ---- rule 3: production ----
}
