using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>
/// Strategic repair on timer 0x380: capital ships and Death Stars (0x14-0x1B) and fighter squadrons (0x1C-0x1F). The Damaged
/// object flag, RepairActive (Damaged and Usable, drives timer Active) and FastRepair (drives timer Base), then the system
/// FastRepair updates and the repair step.
/// </summary>
public static class Repair
{
    static bool Repairable(GameObject o) => Family.Of(o.Type) is >= 0x14 and < 0x20;
    static bool IsFighter(GameObject o) => Family.Of(o.Type) is >= 0x1C and < 0x20;
    static int Sd(GameState game, GameObject o, SdField field) => game.Definitions.Resolve(o.Type)!.Get(field);

    // ---- damage setters (every write recomputes Damaged) ----

    /// <summary>HullDamage setter. Recomputes Damaged.</summary>
    internal static void SetHullDamage(GameState game, GameObject ship, int value)
    {
        ship.HullDamage = value;
        UpdateDamaged(game, ship);
    }

    /// <summary>SquadSizeDamage setter. The new value is always at most SquadSize here. Recomputes Damaged.</summary>
    internal static void SetSquadSizeDamage(GameState game, GameObject fighter, int value)
    {
        fighter.SquadSizeDamage = value;
        UpdateDamaged(game, fighter);
    }

    /// <summary>Critical-hit counter setter. Recomputes Damaged.</summary>
    internal static void SetCritical(GameState game, GameObject ship, int k, int value)
    {
        ship.SetCritical(k, value);
        UpdateDamaged(game, ship);
    }

    static int CriticalSum(GameObject ship) => Enumerable.Range(0, 6).Sum(ship.Critical);

    /// <summary>
    /// Damaged, recomputed on a change only: ships / Death Stars have HullDamage != 0 or a nonzero critical counter sum;
    /// fighters have SquadSizeDamage != 0. The hook's notification 0x31D is not modelled. Then the RepairActive update runs.
    /// </summary>
    static void UpdateDamaged(GameState game, GameObject unit)
    {
        if (!Repairable(unit)) return;
        bool damaged = IsFighter(unit) ? unit.SquadSizeDamage != 0 : unit.HullDamage != 0 || CriticalSum(unit) != 0;
        if (unit.Damaged == damaged) return;
        unit.Damaged = damaged;
        UpdateRepairActive(game, unit);
    }

    /// <summary>Repair period: ships and Death Stars use G 0x804 / 0x805, fighters G 0x1200 / 0x1201, chosen by FastRepair.</summary>
    static int Period(GameState game, GameObject unit) =>
        game.Params.General(IsFighter(unit) ? unit.FastRepair ? 0x1200u : 0x1201u : unit.FastRepair ? 0x804u : 0x805u);

    /// <summary>
    /// RepairActive is set to Damaged and Usable. It runs from the Damaged hook and from the class Usable hook (after the base
    /// Usable hook). The setter acts on change only, and its hook sets timer 0x380 Active := RepairActive with Variance 0.
    /// Inferred: Base = period written before the activation (the first activations carry the period without a
    /// SetBase event).
    /// </summary>
    internal static void UpdateRepairActive(GameState game, GameObject unit)
    {
        if (!Repairable(unit)) return;
        bool active = unit.Damaged && unit.Usable;
        if (active == unit.RepairActive) return;
        unit.RepairActive = active;
        var slot = TimerCatalogue.Slot(game, 0x380, unit.Ref, unit);
        if (active) slot.Start(game, Period(game, unit), 0);
        else slot.SetActive(game, false);
    }

    /// <summary>FastRepair setter, acting on change only. Its hook sets timer 0x380 Base to the period, which restarts an active slot.</summary>
    static void SetFastRepair(GameState game, GameObject unit, bool value)
    {
        if (unit.FastRepair == value) return;
        unit.FastRepair = value;
        if (game.Timers.TryGetValue((0x380, unit.Ref), out var slot)) slot.SetBase(game, Period(game, unit));
    }

    // ---- FastRepair updates ----

    /// <summary>
    /// For a unit located in <paramref name="s"/>, FastRepair := CombatUnitFastRepair and unit owner = system owner;
    /// units elsewhere are left unchanged.
    /// </summary>
    static void UpdateFastRepair(GameState game, StarSystem s, GameObject unit)
    {
        if (unit.System == s) SetFastRepair(game, unit, s.CombatUnitFastRepair && unit.Owner == s.Owner);
    }

    /// <summary>
    /// Whole-system refresh: every Existing fleet (key order), its Existing capital ships, each ship then its Existing fighters;
    /// then the Existing fighters directly in the system. Inferred: "capital ships" includes Death Stars
    /// (0x14-0x1B, the shared class functions). Run by setup step 23, the system owner hook and the CombatUnitFastRepair hook.
    /// </summary>
    internal static void RefreshSystem(GameState game, StarSystem s)
    {
        foreach (var fleet in s.Contents.OfType<Fleet>().Where(f => f.Existing).ToList())
            foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).ToList())
            {
                UpdateFastRepair(game, s, ship);
                foreach (var f in ship.Contents.Where(x => IsFighter(x) && x.Existing).ToList()) UpdateFastRepair(game, s, f);
            }
        foreach (var f in s.Contents.Where(x => IsFighter(x) && x.Existing).ToList()) UpdateFastRepair(game, s, f);
    }

    /// <summary>
    /// Runs after SmugglingPercent, only for a new Usable value of 1. Ships, Death Stars and fighters update their own
    /// FastRepair; fleets trigger the whole-system refresh.
    /// </summary>
    internal static void ContentUsable(GameState game, StarSystem s, GameObject o)
    {
        if (Repairable(o)) UpdateFastRepair(game, s, o);
        else if (o is Fleet) RefreshSystem(game, s);
    }

    /// <summary>
    /// CombatUnitFastRepair setter, acting on change only. Its hook runs the whole-system refresh, after the game-ready flag.
    /// Writers: a shipyard's Usable change and setup step 23.
    /// </summary>
    internal static void SetCombatUnitFastRepair(GameState game, StarSystem s, bool value)
    {
        if (s.CombatUnitFastRepair == value) return;
        s.CombatUnitFastRepair = value;
        if (game.Ready) RefreshSystem(game, s);
    }

    /// <summary>The system has a ship manager with at least one non-suspended member.</summary>
    internal static bool ShipyardActive(StarSystem s) =>
        s.Managers.FirstOrDefault(m => m?.Kind == ManufacturingKind.Ship) is { } m && Economy.Members(m).Any(f => !f.Suspended);

    // ---- repair step ----

    /// <summary>
    /// Timer 0x380 action. Ships / Death Stars: HullDamage -= Hull div 10 (floor 0) if &gt; 0;
    /// n = sum of the critical counters; n &gt; 0: chance(DamageControl), success gives k = roll(n - 1), the
    /// first counter (order Shield, Weapon, Tractor, Speed, primary, backup) with k &lt; c is decremented, else k -= c. Fighters:
    /// SquadSizeDamage -= 1 if &gt; 0, no draw.
    /// </summary>
    internal static void Step(GameState game, GameObject unit)
    {
        if (IsFighter(unit))
        {
            if (unit.SquadSizeDamage > 0) SetSquadSizeDamage(game, unit, unit.SquadSizeDamage - 1);
            return;
        }
        if (unit.HullDamage > 0) SetHullDamage(game, unit, Math.Max(0, unit.HullDamage - Sd(game, unit, SdField.Hull) / 10));
        int n = CriticalSum(unit);
        if (n == 0 || !game.Random.Chance(Sd(game, unit, SdField.DamageControl), DrawSite.RepairCheck)) return;
        int k = game.Random.Roll(n - 1, DrawSite.RepairPick);
        for (int i = 0; i < 6; i++)
        {
            int c = unit.Critical(i);
            if (c != 0 && k < c) { SetCritical(game, unit, i, c - 1); return; }
            k -= c;
        }
    }
}
