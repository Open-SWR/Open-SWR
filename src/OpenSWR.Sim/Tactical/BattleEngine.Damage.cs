using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public static partial class BattleEngine
{
    // ---- damage, crits, destruction ----

    /// <summary>Damage application with 32-bit float arithmetic; nothing when the hull is 0.</summary>
    static void Damage(Unit t, double d, GameRandom rand)
    {
        double s0 = t.Shield, h0 = t.Hull;
        int draws = 0;
        List<int> crits = [];
        if (t.Hull > 0)
        {
            string site = rand.Trace is null ? "" : $"{Family.Of(t.Object.Ref)}:{SdRef.IndexOf(t.Object.Ref)} {stepNo} ";
            t.Shield = F(s0 - d);
            if (s0 > 0)
            {
                double p = 100 / s0 * (s0 - (s0 - d)); // the unrounded s0 - d
                p = F(p < 1 ? 1 : p);
                draws++;
                if (rand.Rand(100, site + "s") <= p) Hit(CriticalKind.Shield);
            }
            if (t.Shield < 0)
            {
                double o = t.Shield <= -1 ? t.Shield : -1, old = t.Hull, h = F(o + old);
                t.Shield = 0;
                t.Hull = h < 0 ? 0 : h;
                draws++;
                double x = F((t.Hull - old) * 100 / old + rand.Rand(100, site + "h"));
                CriticalKind? k = x > 95 ? CriticalKind.Hyperdrive : x > 90 ? CriticalKind.Speed : x > 80 ? CriticalKind.Tractor
                    : x > 65 ? CriticalKind.Weapon : x > 50 ? CriticalKind.Shield : null;
                if (k is { } crit) Hit(crit);
            }
        }
        DamageTrace?.Add(new Application(t.Object, t.State, d, s0, h0, t.Shield, t.Hull, draws, crits));

        void Hit(CriticalKind k)
        {
            crits.Add((int)k);
            Crit(t, k);
        }
    }

    /// <summary>Critical hit k: ShieldCH / WeaponCH / TractorCH / SpeedCH + 1 (cap 4), k = 4 hyperdrive + 1 (cap the hyperdrive count).</summary>
    internal static void Crit(Unit t, CriticalKind k)
    {
        CritTrace?.Add((stepNo, t.Object, (int)k));
        switch (k)
        {
            case CriticalKind.Shield: t.ShieldCh = Math.Min(4, t.ShieldCh + 1); break;
            case CriticalKind.Weapon: t.WeaponCh = Math.Min(4, t.WeaponCh + 1); break;
            case CriticalKind.Tractor: t.TractorCh = Math.Min(4, t.TractorCh + 1); break;
            case CriticalKind.Speed: t.SpeedCh = Math.Min(4, t.SpeedCh + 1); break;
            case CriticalKind.Hyperdrive: default: t.HyperCh = Math.Min(t.Hyperdrives, t.HyperCh + 1); break;
        }
    }

    /// <summary>
    /// Destroy: skips only the destroyed state; state := destroyed, then 100000 damage (draws while hull != 
    /// 0). The recursion over a capital ship's own lists adds nothing (Inferred empty); docked fighters die with their
    /// carrier on the strategy side.
    /// </summary>
    static void Kill(Unit u, Side[] sides, GameRandom rand)
    {
        if (u.State == TacticalUnitState.Destroyed) return;
        u.State = TacticalUnitState.Destroyed;
        sides[0].Undocked.Remove(u);
        Damage(u, Destroy, rand);
        u.Hull = 0;
        Remove(u, sides);
    }
}
