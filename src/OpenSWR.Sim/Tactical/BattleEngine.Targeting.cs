using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    /// <summary>Retreat: state 9 and out of the lists and its group; skips retreated / destroyed units.</summary>
    static void Retreat(Unit u, Side[] sides)
    {
        if (u.State is TacticalUnitState.Withdrawn or TacticalUnitState.Destroyed) return;
        u.State = TacticalUnitState.Withdrawn;
        sides[0].Undocked.Remove(u);
        Remove(u, sides);
    }

    /// <summary>Out of the pending list, its group (an empty group leaves the side's group list) and the side's fighter and non-fighter lists.</summary>
    internal static void Remove(Unit u, Side[] sides)
    {
        sides[0].Pending.Remove(u);
        if (u.Group is { } g)
        {
            g.Members.Remove(u);
            if (g.Members.Count == 0) sides[u.Side].Groups.Remove(g);
        }
        foreach (var side in sides) { side.Ships.Remove(u); side.Fighters.Remove(u); } // an activated unit sits in side index 1's list
    }

    /// <summary>Scene alive: finished, hull != 0, not withdrawn.</summary>
    internal static bool Alive(Unit? scene) => scene is { Unfinished: false, Withdrawn: false } && scene.Hull != 0;
    /// <summary>Scene attackable: alive and no Death Star shield facility.</summary>
    static bool Attackable(Unit? scene) => Alive(scene) && !scene!.Shielded;
    /// <summary>Carrier lookup: a Death Star's key finds no unit and gives the current scene's hangar.</summary>
    internal static Unit? Resolve(Unit? carrier, Side[] sides) => carrier is { Type: TacticalUnitType.DeathStarScene } ? sides[1].Scene : carrier;

    // ---- candidates and targeting (no draws) ----

    /// <summary>
    /// Threat: 15 * strength + 1.0 per passenger record aboard - 0.01 * R /
    /// base SRR - 0.01 * hf - 0.01 in state 1 / 3; the fire-record term is empty here. A fighter's base is rewritten by the read itself
    /// 1.0 while it has hull, 0 otherwise (Inferred for the 0 / 0 case).
    /// </summary>
    internal static double Threat(Unit u)
    {
        double shield = u.Type == TacticalUnitType.Fighter ? (u.Hull > 0 ? 1 : 0) : u.BaseSrr > 0 ? ShieldRate(u) / u.BaseSrr : 0;
        return Rate(u, true, 1, false) * 15 + u.Passengers - 0.01 * shield - 0.01 * u.Hf - (u.State is TacticalUnitState.Docked or TacticalUnitState.Launching ? 0.01 : 0);
    }

    /// <summary>Wanted test: state 2 and weapon rate != 0.</summary>
    static bool Wanted(Unit u) => u.State == TacticalUnitState.Active && WeaponRate(u) != 0;

    /// <summary>Group rate: sum over the group's members in state 2 / 4 / 6 of the unit rate on <paramref name="t"/>, rounded to float per member.</summary>
    internal static double GroupRate(Group g, Unit t, Side[] sides)
    {
        double acc = 0;
        foreach (var u in g.Members)
            if (u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered) acc = F(acc + Rate(u, t, sides));
        return acc;
    }

    /// <summary>
    /// Candidates and targeting: the other side's non-fighter units and fighter units in state 2,
    /// wanted gives C, else state 2 gives U; its pending units in state 1 / 3 gives C; each by -threat, ties by key; U appended. L = the
    /// fighters of C, then its non-fighters; C := its non-fighters, then L. Each own group not skipped (scorer skip test) goes to S2 if
    /// its leader has a laser, else S1; evaluator(S1, C), evaluator(S2, L). Groups keep their target until pooled again; an empty
    /// candidate list assigns nothing.
    /// </summary>
    internal static void Target(Side side, Side enemy, Side[] sides)
    {
        TtkFn ttk = (Unit t, Group g, ref double acc) => TimeToKill(t, g, ref acc, sides);
        // scorer skip test (Inferred: a group field stays 0 throughout)
        Targeting(Candidates(side, enemy), side.Groups, new Scorer(g => SkipRule(g, side.Groups, Wanted, ttk), Wanted, ttk, (g, t) => g.Target = t), sides);
    }

    /// <summary>Time to kill (the scorer's TTK) with a cumulative rate: acc += the group's rate on t.</summary>
    internal delegate double TtkFn(Unit t, Group g, ref double acc);

    /// <summary>The side's targeting objects: scorer skip test, wanted test, TTK; the evaluator's assignment.</summary>
    internal sealed record Scorer(Func<Group, bool> Skip, Func<Unit, bool> Wanted, TtkFn Ttk, Action<Group, Unit> Assign);

    /// <summary>
    /// Targeting (shared by both engines): L = the fighters of C, then its non-fighters; C := its non-fighters, then
    /// L; each group not skipped goes to S2 if its leader has a laser, else S1; evaluator(S1, C), evaluator(S2, L).
    /// </summary>
    internal static void Targeting(List<Unit> c, IEnumerable<Group> groups, Scorer s, Side[] sides)
    {
        if (c.Count == 0) return;
        var l = c.Where(u => u.Type == TacticalUnitType.Fighter).Concat(c.Where(u => u.Type != TacticalUnitType.Fighter)).ToList();
        c = [.. c.Where(u => u.Type != TacticalUnitType.Fighter), .. l];
        List<Group> s1 = [], s2 = [];
        foreach (var g in groups.ToList())
            if (!s.Skip(g)) (g.Leader is { } leader && HasLaser(leader) ? s2 : s1).Add(g); // no leader gives S1
        Evaluate(s1, c, s, sides);
        Evaluate(s2, l, s, sides);
    }

    /// <summary>
    /// Skip rule: a fighter formation with a member in state 1 / 3; else the current target wanted and TTK &lt; 300 ( != -1)
    /// alone or cumulative over the side's other groups on it (each adds its rate, the last value counts).
    /// </summary>
    internal static bool SkipRule(Group g, IEnumerable<Group> sideGroups, Func<Unit, bool> wanted, TtkFn ttk)
    {
        if (g.Formation && g.Members.Any(u => u.State is TacticalUnitState.Docked or TacticalUnitState.Launching)) return true;
        if (g.Target is not { } t || !wanted(t)) return false;
        double acc = 0, v = ttk(t, g, ref acc);
        if (v < 300 && v != -1) return true;
        foreach (var o in sideGroups.Where(o => o != g && o.Target == t)) v = ttk(t, o, ref acc);
        return v < 300 && v != -1;
    }

    /// <summary>Candidate list.</summary>
    internal static List<Unit> Candidates(Side side, Side enemy)
    {
        static List<Unit> Sort(IEnumerable<Unit> units) => [.. units.OrderBy(u => -Threat(u)).ThenBy(u => u.Key)];
        var listed = enemy.Ships.Concat(enemy.Fighters.Where(u => u.State == TacticalUnitState.Active)).ToList();
        var c = Sort(listed.Where(Wanted).Concat(side.Pending.Where(u => u.Side == enemy.Index && u.State is TacticalUnitState.Docked or TacticalUnitState.Launching)));
        c.AddRange(Sort(listed.Where(u => !Wanted(u) && u.State == TacticalUnitState.Active)));
        return c;
    }

    /// <summary>Time to kill: acc += the group's rate on t; max(1, 0.65 * hull + shield) / (acc - R(t)) when positive, else -1 (distance term 0 in automatic battles, Inferred).</summary>
    static double TimeToKill(Unit t, Group g, ref double acc, Side[] sides)
    {
        acc += GroupRate(g, t, sides);
        double value = Math.Max(1, 0.65 * t.Hull + t.Shield), net = acc - ShieldRate(t);
        return net > 0 ? value / net : -1;
    }

    /// <summary>
    /// Evaluator over groups: per wanted candidate, the pool ranked by key (TTK in [0, 180], else
    /// 100000 - rate); one element gives assigned and the evaluator ends; else the walk from the last back to (not including) the first
    /// with the cumulative TTK (acc starting at the first's rate) assigns last...e once within [0, 180]. Leftovers gives the first wanted
    /// candidate, else the first candidate.
    /// </summary>
    static void Evaluate(List<Group> groups, List<Unit> targets, Scorer s, Side[] sides)
    {
        var pool = groups.ToList();
        static bool InRange(double x) => x >= 0 && x <= Horizon;
        foreach (var t in targets.Where(s.Wanted))
        {
            if (pool.Count == 0) return;
            var k = pool.Select(g =>
            {
                double acc = 0, ttk = s.Ttk(t, g, ref acc);
                return (Group: g, Key: InRange(ttk) ? ttk : 100000 - acc);
            }).OrderBy(x => x.Key).Select(x => x.Group).ToList(); // stable
            if (k.Count == 1)
            {
                s.Assign(k[0], t);
                return;
            }
            double sum = GroupRate(k[0], t, sides);
            for (int e = k.Count - 1; e > 0; e--)
                if (InRange(s.Ttk(t, k[e], ref sum)))
                {
                    for (int i = k.Count - 1; i >= e; i--) { s.Assign(k[i], t); pool.Remove(k[i]); }
                    break;
                }
        }
        if ((targets.FirstOrDefault(s.Wanted) ?? targets.FirstOrDefault()) is { } first)
            foreach (var g in pool) s.Assign(g, first);
    }
}
