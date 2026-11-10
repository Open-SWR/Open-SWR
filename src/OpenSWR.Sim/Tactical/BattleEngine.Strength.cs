using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    // ---- strength, withdraw, recharge ----

    /// <summary>
    /// Strength: no state test; sum of the rate against a ship over the non-fighter list (a unit
    /// left out while some group of the other side targets it with a rate >= its shield recharge), every unit of the fighter list and, for
    /// side index 1, every pending unit of the shared pending list; while the scene is alive an f32 add of 20000000 for side index 1, and for
    /// side index 0 under the exposure test (the group fields never equal 6, Inferred).
    /// </summary>
    internal static double Strength(Side side, Side[] sides)
    {
        var other = sides[1 - side.Index];
        double sum = 0;
        foreach (var u in side.Ships)
        {
            if (other.Groups.Any(g => g.Target == u && GroupRate(g, u, sides) >= ShieldRate(u))) continue;
            sum += Rate(u, true, 1, false);
        }
        foreach (var u in side.Fighters) sum += Rate(u, true, 1, false);
        if (side.Index == 1) foreach (var u in side.Pending) sum += Rate(u, true, 1, false);
        return Alive(sides[1].Scene) && (side.Index == 1 || Exposed(sides)) ? F(sum + 20000000.0) : sum;
    }

    /// <summary>Exposure test: the scene is attackable and side index 0's fighters in the shared undocked list outnumber side index 1's.</summary>
    static bool Exposed(Side[] sides) =>
        Attackable(sides[1].Scene) && sides[0].Undocked.Count(u => u.Type == TacticalUnitType.Fighter && u.Side == 0) > sides[0].Undocked.Count(u => u.Type == TacticalUnitType.Fighter && u.Side == 1);

    /// <summary>
    /// Retreat possible: retreat allowed (v3), not withdrawn, no GravityWell blocker in the other side's non-fighter list (any
    /// state), and a unit of the fighter list, then the non-fighter list, in any state can jump, or side index 1 has a scene alive or unfinished.
    /// The original also zeroes one of the side's own fields here; that is not modelled, since every reader recomputes the strength first.
    /// </summary>
    internal static bool RetreatPossible(Side side, Side[] sides) =>
        side.RetreatAllowed && side.State == TacticalSideState.Fighting && !sides[1 - side.Index].Ships.Any(u => u.Blocker)
        && (side.Fighters.Concat(side.Ships).Any(u => CanJump(u, side)) || side.Index == 1 && sides[1].Scene is { } sc && (Alive(sc) || sc.Unfinished));

    /// <summary>Withdraw decision: both strengths recomputed at decision time (the other side's stored).</summary>
    static bool Withdraw(Side side, Side[] sides)
    {
        if (!RetreatPossible(side, sides)) return false;
        if (side.Withdrawing) return true;
        var other = sides[1 - side.Index];
        double own = side.Strength = Strength(side, sides);
        if (own == 0) return true;
        double e = other.Strength = Strength(other, sides);
        return Outmatched(own, e, side.Threshold);
    }

    /// <summary>Recharge (with float rounding): a fighter's shield max compounds by hull / hull max; then current += R * dt up to max.</summary>
    internal static void Recharge(Unit u)
    {
        if (u.Type == TacticalUnitType.Fighter)
        {
            u.ShieldMax = F(Scale(u, u.ShieldMax));
            u.Shield = F(Math.Min(u.Shield, u.ShieldMax));
        }
        if (u.ShieldMax - u.Shield > 0) u.Shield = F(Math.Min(u.ShieldMax, ShieldRate(u) * Dt + u.Shield));
    }

    // ---- finish and result ----

    /// <summary>
    /// Hangar dock: accepts while its count is below its capacity (the scene hangar too, Inferred).
    /// Docked: the carrier is set, state 1, count + 1.
    /// </summary>
    internal static bool Dock(Unit f, Unit carrier)
    {
        if (carrier.Docked >= carrier.Capacity) return false;
        (f.Carrier, f.State) = (carrier, TacticalUnitState.Docked);
        carrier.Docked++;
        return true;
    }

    /// <summary>
    /// Cannot jump, state 2 only: its own carrier (the Death Star scene while alive, else a capital ship in state
    /// 2), else the first ship of the side's non-fighter list in state 2 with room, else (side index 1) the live Death Star scene. Docked:
    /// state 5, out of its lists. Inferred: a full or gone own carrier falls through to the list.
    /// </summary>
    static bool Land(Unit u, Side side, Side[] sides)
    {
        if (u.State != TacticalUnitState.Active) return false;
        var scene = sides[1].Scene;
        bool docked = u.Carrier is { } c && (c.Type == TacticalUnitType.DeathStarScene ? Alive(scene) && Dock(u, scene!) : c.State == TacticalUnitState.Active && Dock(u, c));
        docked = docked || side.Ships.Any(x => x.State == TacticalUnitState.Active && Dock(u, x));
        docked = docked || side.Index == 1 && Alive(scene) && Dock(u, scene!);
        if (!docked) return false;
        u.State = TacticalUnitState.Recovered;
        Remove(u, sides);
        return true;
    }

    /// <summary>
    /// Finish: per fighter of the battle list in state 1 / 2 / 5 / 9, the first capital ship of the list from
    /// its start on the fighter's side in state 2 / 9 with room takes it (no old-carrier preference); else, side index 1, the Death
    /// Star scene while alive or withdrawn; else it keeps its state and carrier.
    /// </summary>
    static void Finish(Side[] sides)
    {
        var battle = sides[0].Battle;
        foreach (var f in battle.Where(u => u.Type == TacticalUnitType.Fighter && u.State is TacticalUnitState.Docked or TacticalUnitState.Active or TacticalUnitState.Recovered or TacticalUnitState.Withdrawn).ToList())
        {
            if (battle.Any(x => x.Type == TacticalUnitType.CapitalShip && x.Side == f.Side && x.State is TacticalUnitState.Active or TacticalUnitState.Withdrawn && Dock(f, x))) continue;
            if (f.Side == 1 && sides[1].Scene is { } sc && (Alive(sc) || sc.Withdrawn)) Dock(f, sc);
        }
    }
}
