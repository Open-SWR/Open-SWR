using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    // ---- unit getters ----

    /// <summary>Speed: (bonus + base)*(1 - 0.25*SpeedCH) - tractor load, 0 unless &gt; 0; tractor load 0 (no holds in automatic battles, Inferred).</summary>
    internal static double Speed(Unit u, Side side)
    {
        if (u.EngineDown != 0) return 0; // set only by tactical ion hits
        double v = side.Admiral + u.BaseSpeed, speed = v - v * 0.25 * u.SpeedCh;
        return speed > 0 ? speed : 0;
    }
    internal static double Agility(Unit u, Side side) => Speed(u, side) == 0 ? 1.0 : Math.Min(9, u.Maneuverability + (u.Type == TacticalUnitType.Fighter ? 1 : side.Admiral));
    /// <summary>Rounds the value to float32.</summary>
    internal static double F(double x) => (float)x;
    /// <summary>Scale: (hull * factor) / hull max, multiplication first.</summary>
    internal static double Scale(Unit u, double x) => u.HullMax > 0 ? u.Hull * x / u.HullMax : 0;
    // Capital ship: f(Scale(B)) - B * 0.25 * counter
    // (at least 0), fighter f(hull * 3.751f / 8) and f(hull * 0.025f / 8)
    internal static double WeaponRate(Unit u) => u.Type == TacticalUnitType.Fighter ? F(u.Hull * (double)3.751f / 8) : Math.Max(0, F(Scale(u, F(3.75 * u.Wrr))) - F(3.75 * u.Wrr) * 0.25 * u.WeaponCh);
    internal static double ShieldRate(Unit u) => u.Type == TacticalUnitType.Fighter ? F(u.Hull * (double)0.025f / 8) : Math.Max(0, F(Scale(u, u.Srr)) - u.Srr * 0.25 * u.ShieldCh);
    internal static bool HasLaser(Unit u) => Enumerable.Range(0, 4).Any(a => u.Arcs[a, 0] > 0);
    internal static bool CanJump(Unit u, Side side) => u.Hyperdrives - u.HyperCh > 0 && Speed(u, side) != 0;

    static double LaserF(bool ship) => ship ? LaserVsShip : 1.0;
    static double HeavyF(bool ship) => ship ? 1.0 : 0.5; // ion / turbolaser

    /// <summary>Arc choice: the first arc with the strictly largest total against the target kind; <see cref="NoArc"/> = none.</summary>
    internal static int Arc(Unit a, bool ship)
    {
        int best = NoArc;
        double top = 0;
        for (int i = 0; i < 4; i++)
        {
            double t = a.Arcs[i, 0] * Base * LaserF(ship) + (ship ? a.Arcs[i, 1] * Base : 0) + a.Arcs[i, 2] * Base * HeavyF(ship);
            if (t > top) (best, top) = (i, t);
        }
        return best;
    }

    /// <summary>
    /// Rate (with float rounding at each step); <paramref name="ratio"/> applies against fighters only; strength
    /// passes none. <paramref name="unshielded"/>: the target's shield fraction is 0 (torpedo term).
    /// </summary>
    internal static double Rate(Unit a, bool ship, double ratio, bool unshielded)
    {
        double w = F(WeaponRate(a));
        int arc = Arc(a, ship);
        if (w == 0 || arc == NoArc) return 0;
        double l = a.Arcs[arc, 0], i = a.Arcs[arc, 1], t = a.Arcs[arc, 2];
        bool fighter = a.Type == TacticalUnitType.Fighter;
        double n = l + i + t;
        if (fighter) n = Math.Truncate(Scale(a, F(n))); // 80 hull of 96 with n = 60 gives exactly 50
        double per = F(n / w);
        if (per <= 0) return 0;
        double acc = 0;
        void Add(double term) => acc = F((fighter ? Scale(a, term) : term) + acc);
        if (i > 0) Add(ship ? F(i * (1.0 * Base)) : 0);
        if (l > 0) Add(ship ? F(l * (LaserF(ship) * Base)) : F(F(l * (LaserF(ship) * Base)) * ratio));
        if (t > 0) Add(ship ? F(t * (Base * HeavyF(ship))) : F(F(t * (Base * HeavyF(ship))) * ratio));
        if (fighter && ship && unshielded && a.Torpedo != 0) return (Scale(a, F(Scale(a, F(a.Torpedo * Torpedo)))) + acc) / per;
        return acc / per;
    }

    internal static double Rate(Unit a, Unit t, Side[] sides)
    {
        double ratio = F(Agility(a, sides[a.Side]) / Agility(t, sides[t.Side]));
        if (ratio < (double)0.1f) ratio = 0.1f;
        return Rate(a, t.Type != TacticalUnitType.Fighter, ratio, t.ShieldMax == 0 || t.Shield / t.ShieldMax == 0);
    }
}
