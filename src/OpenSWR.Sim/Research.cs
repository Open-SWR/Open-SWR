using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Side research lines: accrual (timer 0x381), level advance, buildable list. Lines are indexed by the mission kind k
/// (0 ship / shipyard 0x28, 1 facility / construction yard 0x2A, 2 troop / training facility 0x29). Research posts no
/// notification, no event and draws nothing.
/// </summary>
public static class Research
{
    /// <summary>Facility family of line k.</summary>
    public static uint FacilityFamily(int k) => k switch { 0 => 0x28u, 1 => 0x2Au, _ => 0x29u };

    /// <summary>Line k of a manufacturing kind (0 ship, 1 construction, 2 training; the same numbering as the kind).</summary>
    public static int Line(ManufacturingKind kind) => (int)kind;

    /// <summary>
    /// Candidates of side for facility family f: any SD record whose producer range contains f, with
    /// RefinedMaterialCost != 0 and the side's flag (Alliance or Empire) set.
    /// </summary>
    public static IEnumerable<SdRecord> Candidates(GameState game, Owner side, uint f) =>
        game.Definitions.SdFiles.SelectMany(x => x.Records).Where(r =>
            r.Common.ProducerFamilyMin <= f && f < r.Common.ProducerFamilyMax && r.Has(SdField.ResearchDifficulty)
            && r.Get(SdField.RefinedMaterialCost) != 0 && r.Get(side == Owner.Alliance ? SdField.Alliance : SdField.Empire) != 0);

    /// <summary>
    /// Buildable list: the side's candidates for f with ResearchOrder <= the line's current order. For the
    /// UI and computer player; the sim's command validation never checks research.
    /// </summary>
    public static List<SdRecord> Buildable(GameState game, Owner side, uint f)
    {
        int order = game.Sides[side].ResearchOrder[LineOf(f)];
        return Candidates(game, side, f).Where(r => r.Get(SdField.ResearchOrder) <= order).ToList();
    }

    static int LineOf(uint f) => f switch { 0x28 => 0, 0x2A => 1, 0x29 => 2, _ => throw new ArgumentOutOfRangeException(nameof(f)) };

    /// <summary>
    /// Timer 0x381 action: over the systems owned by the side in registry order, sum the non-suspended member
    /// facilities of each manager; a system missing one of its three managers aborts with no update. Then
    /// ship, troop, facility points += their sums, each setter running its hook.
    /// </summary>
    public static void Accrue(GameState game, SideState side)
    {
        if (AccrualSums(game, side.Owner) is not { } sums) return;
        // The setters never fail for the real sides (the side test is the only check), so all three lines are updated.
        foreach (int k in new[] { 0, 2, 1 }) SetPoints(game, side, k, side.ResearchPoints[k] + sums[k]);
    }

    /// <summary>
    /// Points one accrual adds per line k: the non-suspended member facilities of each manager over the side's systems; null = a
    /// system misses a manager (the accrual aborts).
    /// </summary>
    public static int[]? AccrualSums(GameState game, Owner owner)
    {
        var sums = new int[3];
        foreach (var s in game.Systems.Where(s => s.Owner == owner))
        {
            if (s.Managers.Any(m => m is null)) return null;
            foreach (var m in s.Managers) sums[Line(m.Kind)] += Economy.Members(m).Count(f => !f.Suspended);
        }
        return sums;
    }

    /// <summary>Points setter: on change, writes the value and runs the level-advance hook.</summary>
    public static void SetPoints(GameState game, SideState side, int k, int value)
    {
        if (side.ResearchPoints[k] == value) return;
        side.ResearchPoints[k] = value;
        Advance(game, side, k);
    }

    /// <summary>
    /// Next item of line k: the first candidate of the first ResearchOrder above the current one (up to the largest);
    /// null = none left.
    /// </summary>
    public static SdRecord? Next(GameState game, SideState side, int k)
    {
        var candidates = Candidates(game, side.Owner, FacilityFamily(k)).ToList();
        int max = candidates.Count == 0 ? 0 : candidates.Max(r => r.Get(SdField.ResearchOrder));
        SdRecord? next = null;
        for (int o = side.ResearchOrder[k] + 1; o <= max && next is null; o++)
            next = candidates.FirstOrDefault(r => r.Get(SdField.ResearchOrder) == o);
        return next;
    }

    /// <summary>Cost of an item: ResearchDifficulty * CostScale div 100.</summary>
    public static int Cost(SideState side, SdRecord item) => item.Get(SdField.ResearchDifficulty) * side.CostScale / 100;

    /// <summary>
    /// Level-advance hook: next item = the first entry of the first order above the current one (up to maxOrder) with any
    /// candidate; cost = difficulty * CostScale div 100. If that entry lies outside families [0x10, 0x40) or does not resolve,
    /// the search stops and the hook does nothing (line stuck). No next item sets exhausted (points kept). Points >= cost
    /// set the order to the next item, then points -= cost (the setter re-runs this hook).
    /// </summary>
    static void Advance(GameState game, SideState side, int k)
    {
        var next = Next(game, side, k);
        if (next is not null && (Family.Of(next.Common.Ref) is not (>= 0x10 and < 0x40) || game.Definitions.Resolve(next.Common.Ref) is null)) return;
        if (next is null) { side.ResearchExhausted[k] = true; return; }
        int cost = Cost(side, next);
        if (side.ResearchPoints[k] < cost) return;
        side.ResearchOrder[k] = next.Get(SdField.ResearchOrder);
        SetPoints(game, side, k, side.ResearchPoints[k] - cost);
    }
}
