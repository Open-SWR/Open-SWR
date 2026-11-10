using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    /// <summary>
    /// Groups at deploy, in the order of the side's group list. 1. Fighter formations: one per fighter class of
    /// the side's units (docked included, setup order); class order = the engine type index (Alliance X-, Y-, A-,
    /// B-wing; Empire = SdRef order), the used classes in slots 0, 1, ... of four (Inferred). Balancing: per slot with bookkeeping count 0 (slots 0 ... 3 in order), b = the
    /// first slot with the strictly largest count; above 7, m = count / 2 moves in the counts only; units move from the front of
    /// b's formation (Alliance m, Empire one) into a new formation appended to the side's group list for the slot; a drained formation stays.
    /// 2. Capital-ship groups from the side's non-fighter list sorted into turbolaser /
    /// ion only, any laser, unarmed, each by -(speed + 10000 * max range) ascending, ties by key.
    /// </summary>
    static void Deploy(Side side)
    {
        // The used classes take slots 0, 1, ... (Inferred)
        var classes = side.Units.Where(u => u.Type == TacticalUnitType.Fighter).GroupBy(u => u.Object.Type).OrderBy(g => g.First().TypeIndex).ToList();
        var count = new int[Math.Max(4, classes.Count)];
        var formation = new Group?[count.Length];
        for (int c = 0; c < classes.Count; c++) (count[c], formation[c]) = (classes[c].Count(), AddGroup(side, true, classes[c]));
        for (int slot = 0; slot < 4; slot++)
        {
            if (count[slot] != 0) continue;
            int b = 0;
            for (int j = 1; j < 4; j++) if (count[j] > count[b]) b = j; // first strictly largest bookkeeping count
            if (count[b] <= 7) continue;
            int m = count[b] / 2;
            (count[slot], count[b]) = (m, count[b] - m); // bookkeeping only, whatever number of units moves
            if (formation[b] is not { } src) continue;
            var moved = src.Members.Take(side.Index == 0 ? m : 1).ToList(); // from the front; Empire exactly one
            src.Members.RemoveRange(0, moved.Count); // a drained formation stays in the side's group list
            if (moved.Count > 0) formation[slot] = AddGroup(side, true, moved);
        }
        CapitalGroups(side);
    }

    /// <summary>Sort the non-fighter list into turbolaser / ion only, any laser and unarmed lists, each by -(speed + 10000 * max range) ascending, ties by key.</summary>
    internal static (List<Unit> Heavy, List<Unit> Laser, List<Unit> Unarmed) SortCapital(Side side)
    {
        List<Unit> Sorted(Func<Unit, bool> pick) => [.. side.Ships.Where(pick).OrderBy(u => -(Speed(u, side) + 10000 * MaxRange(u))).ThenBy(u => u.Key)];
        bool Unarmed(Unit u) => !Has(u, 0) && !Has(u, 1) && !Has(u, 2);
        return (Sorted(u => !Unarmed(u) && !Has(u, 0)), Sorted(u => Has(u, 0)), Sorted(Unarmed));
    }

    /// <summary>Group count g of a list of n >= 1 units in slots lo ... hi: 1 for n &lt; 2, else the largest i <= hi with i^2 <= n; hi - lo + 1 when that is <= g.</summary>
    internal static int GroupCount(int n, int lo, int hi)
    {
        int g = 1;
        if (n >= 2) for (int i = 1; i <= hi && i * i <= n; i++) g = i;
        if (hi - lo + 1 <= g) g = hi - lo + 1;
        return Math.Max(1, g); // a slot range exhausted (n1 + n2 > 7): one group (Inferred, unreachable with shipped fleets)
    }

    /// <summary>Whether unit j of a list split into <paramref name="groups"/> groups of <paramref name="stride"/> starts the next group (the automatic and the tactical battle share it).</summary>
    internal static bool GroupStart(int j, int stride, int made, int groups) => j % stride == 0 && made < groups;

    /// <summary>The withdraw test: the enemy has strength and own / enemy is below the side's threshold or zero.</summary>
    internal static bool Outmatched(double own, double enemy, double threshold) => enemy > 0 && (own / enemy < threshold || own / enemy == 0);

    static bool Has(Unit u, int w) => Enumerable.Range(0, 4).Any(a => u.Arcs[a, w] > 0);
    /// <summary>Max range: the largest range of a weapon type the unit has in some arc.</summary>
    internal static double MaxRange(Unit u) => Math.Max(Has(u, 1) ? u.IonRange : 0, Math.Max(Has(u, 0) ? u.LaserRange : 0, Has(u, 2) ? u.TlRange : 0));

    /// <summary>Capital-ship groups: the non-fighter list split into the turbolaser / ion, laser and unarmed lists, groups appended to the side's group list.</summary>
    internal static void CapitalGroups(Side side)
    {
        var (heavy, laser, unarmed) = SortCapital(side);
        int n1 = Groups(heavy, 0, 3), n2 = Groups(laser, n1, n1 + 3);
        Groups(unarmed, n1 + n2, 7);

        int Groups(List<Unit> list, int lo, int hi)
        {
            int n = list.Count;
            if (n == 0) return 0;
            int g = GroupCount(n, lo, hi), stride = n / g, made = 0;
            Group? current = null;
            for (int j = 0; j < n; j++)
            {
                if (GroupStart(j, stride, made, g)) { current = AddGroup(side, false, []); made++; }
                current!.Members.Add(list[j]);
                list[j].Group = current;
            }
            return made;
        }
    }

    internal static Group AddGroup(Side side, bool formation, IEnumerable<Unit> members)
    {
        var g = new Group(formation);
        g.Members.AddRange(members);
        foreach (var u in g.Members) u.Group = g;
        side.Groups.Add(g);
        return g;
    }
}
