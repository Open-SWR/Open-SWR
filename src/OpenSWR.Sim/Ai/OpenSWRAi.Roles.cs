using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>Unit roles.</summary>
public enum UnitRole
{
    Line, Screen, Durability, Carrier, Transport, Bombard, Speed, // capital ships
    Interceptor, Bomber, Superiority, // fighters
    Garrison, Assault, // troops
    Gun, Shield, BombardDefence, // defence facilities
}

/// <summary>The kind a unit's roles compare within.</summary>
public enum UnitKind { CapitalShip, Fighter, Troop, Defence }

/// <summary>One unit type's roles: the scores per maintenance point, the main role, the flags, and whether another unit of its kind dominates it.</summary>
public sealed record UnitRoles(uint Type, UnitKind Kind, int Maintenance, IReadOnlyDictionary<UnitRole, double> Scores, UnitRole Main,
    bool Interdictor, bool NeedsCarrier, bool Dominated)
{
    public double this[UnitRole r] => Scores.TryGetValue(r, out double v) ? v : 0;
}

/// <summary>
/// Role scores from the definitions' stats only (never names or SdRefs),
/// weighted the way the automatic battle engine and the combat rules use the stats:
/// <list type="bullet">
/// <item>Space fire: a unit fires one arc (the arc with the largest weighted sum against the target kind); its rate is its weapon
/// rate (ships: WeaponRechargeRate; fighters: 3.751 x squadron size) x the arc's weighted share. Against ships: laser x 0.167,
/// ion x 1, turbolaser x 1; against fighters: laser x 1, turbolaser x 0.5, ion x 0, times the attacker / target agility ratio.
/// So anti-ship = rate x (0.167 L + I + T) / (L + I + T), anti-fighter = rate x (L + 0.5 T) / (L + I + T), each on its best arc;
/// the gun count matters only through the shares.</item>
/// <item>Torpedoes (value x squadron size, x 0.5333) hit only ships whose shields are down; fighter bombardment is a separate field.</item>
/// <item>Durability = hull + shield + 20 x shield recharge (shields restore every battle step; 20 steps Inferred). Damage
/// control is strategic repair, not used in battle: left out.</item>
/// <item>Bombardment: ship bombard field (+ fighters' field); defence ion cannons (0x22) and batteries (0x23) fire at the bombarding ships,
/// GenCore shields (0x24) subtract from the shots. Assault: every Usable defence facility fires at the landing troops with
/// chance attack / G 0x200; troop duels compare the attacker's attack with the defender's defence.
/// Planetary defences take no part in the space battle.</item>
/// </list>
/// </summary>
public static class Roles
{
    static readonly UnitRole[] ShipRoles = [UnitRole.Line, UnitRole.Screen, UnitRole.Durability, UnitRole.Carrier, UnitRole.Transport, UnitRole.Bombard, UnitRole.Speed];
    static readonly UnitRole[] FighterRoles = [UnitRole.Interceptor, UnitRole.Bomber, UnitRole.Superiority];
    static readonly UnitRole[] TroopRoles = [UnitRole.Garrison, UnitRole.Assault];
    static readonly UnitRole[] DefenceRoles = [UnitRole.Gun, UnitRole.Shield, UnitRole.BombardDefence];

    /// <summary>
    /// The roles a main role is chosen from: ships line, screen,
    /// carrier, transport (bombardment, interdiction, durability and speed are capabilities); defences gun and shield (bombardment
    /// defence likewise).
    /// </summary>
    public static IReadOnlyList<UnitRole> MainRoles(UnitKind kind) => kind switch
    {
        UnitKind.CapitalShip => [UnitRole.Line, UnitRole.Screen, UnitRole.Carrier, UnitRole.Transport],
        UnitKind.Defence => [UnitRole.Gun, UnitRole.Shield],
        _ => Of(kind),
    };

    /// <summary>The ship roles dominance compares: the space scores and the capacities that fill a doctrine need (bombardment and interdiction only protect).</summary>
    static readonly UnitRole[] ShipDominance = [UnitRole.Line, UnitRole.Screen, UnitRole.Durability, UnitRole.Speed, UnitRole.Carrier, UnitRole.Transport];

    /// <summary>
    /// Carrier or transport is a ship's main role only when its line and screen scores are both below this share of the median of
    /// the side's ships of similar maintenance (0.5 to 2 times this unit's maintenance).
    /// </summary>
    public const double CapacityRoleBelow = 0.5;

    /// <summary>The roles a kind compares in.</summary>
    public static IReadOnlyList<UnitRole> Of(UnitKind kind) => kind switch
    {
        UnitKind.CapitalShip => ShipRoles, UnitKind.Fighter => FighterRoles, UnitKind.Troop => TroopRoles, _ => DefenceRoles,
    };

    /// <summary>The kind of a unit type: capital ships [0x14, 0x18) (the Death-Star class [0x18, 0x1C) has its own rules: none), fighters, troops, defence facilities [0x22, 0x28) except the Death Star shield 0x25.</summary>
    public static UnitKind? KindOf(uint type) => Family.Of(type) switch
    {
        >= 0x14 and < 0x18 => UnitKind.CapitalShip,
        >= 0x1C and < 0x20 => UnitKind.Fighter,
        >= 0x10 and < 0x14 => UnitKind.Troop,
        0x22 or 0x23 or 0x24 or 0x26 or 0x27 => UnitKind.Defence,
        _ => null,
    };

    static double Share(SdRecord r, (SdField Turbolaser, SdField Ion, SdField Laser) arc, double laser, double ion, double turbo)
    {
        double t = (uint)r.Get(arc.Turbolaser), i = (uint)r.Get(arc.Ion), l = (uint)r.Get(arc.Laser);
        double n = l + i + t;
        return n <= 0 ? 0 : (laser * l + ion * i + turbo * t) / n;
    }

    // engine arc order fore, starboard, aft, port; each arc's turbolaser / ion / laser triple
    static readonly (SdField Turbolaser, SdField Ion, SdField Laser)[] Arcs =
    [
        (SdField.ForeTurbolaser, SdField.ForeIonCannon, SdField.ForeLaserCannon),
        (SdField.StarboardTurbolaser, SdField.StarboardIonCannon, SdField.StarboardLaserCannon),
        (SdField.AftTurbolaser, SdField.AftIonCannon, SdField.AftLaserCannon),
        (SdField.PortTurbolaser, SdField.PortIonCannon, SdField.PortLaserCannon),
    ];

    /// <summary>The weighted share of the best arc against a target kind (the arc the engine picks: largest weighted sum).</summary>
    static double BestShare(SdRecord r, double laser, double ion, double turbo)
    {
        (SdField, SdField, SdField)? best = null;
        double top = 0;
        foreach (var a in Arcs)
        {
            double sum = laser * (uint)r.Get(a.Laser) + ion * (uint)r.Get(a.Ion) + turbo * (uint)r.Get(a.Turbolaser);
            if (sum > top) (best, top) = (a, sum);
        }
        return best is { } b ? Share(r, b, laser, ion, turbo) : 0;
    }

    /// <summary>The raw role scores of a type (not per maintenance), or null for a type without roles.</summary>
    public static Dictionary<UnitRole, double>? RawScores(GameState game, uint type)
    {
        if (KindOf(type) is not { } kind || game.Definitions.Resolve(type) is not { } r) return null;
        int Sd(SdField field) => r.GetOrZero(field);
        var s = new Dictionary<UnitRole, double>();
        switch (kind)
        {
            case UnitKind.CapitalShip:
                double wrr = Sd(SdField.WeaponRechargeRate);
                s[UnitRole.Line] = wrr * BestShare(r, 0.167, 1, 1);
                s[UnitRole.Screen] = wrr * BestShare(r, 1, 0, 0.5);
                s[UnitRole.Durability] = Sd(SdField.Hull) + Sd(SdField.ShieldStrength) + 20.0 * Sd(SdField.ShieldRechargeRate);
                s[UnitRole.Carrier] = Sd(SdField.FighterCapacity);
                s[UnitRole.Transport] = Sd(SdField.TroopCapacity);
                s[UnitRole.Bombard] = Sd(SdField.Bombardment);
                s[UnitRole.Speed] = Sd(SdField.Hyperdrive) != 0 ? Sd(SdField.Hyperdrive) : Sd(SdField.HyperdriveIfDamaged);
                break;
            case UnitKind.Fighter:
                double squad = Math.Max(1, Sd(SdField.SquadronSize)), rate = 3.751 * squad;
                s[UnitRole.Interceptor] = rate * BestShare(r, 1, 0, 0.5) * Math.Max(1, Sd(SdField.Maneuverability));
                s[UnitRole.Bomber] = rate * BestShare(r, 0.167, 1, 1) + Sd(SdField.Torpedoes) * squad * 0.5333 + Sd(SdField.FighterBombardment);
                s[UnitRole.Superiority] = (int)(game.Definitions.Fighters.FirstOrDefault(f => f.Common.Ref == type)?.TotalAttackStrength ?? 0) * Math.Max(1, Sd(SdField.ShieldStrength) * squad);
                break;
            case UnitKind.Troop:
                s[UnitRole.Garrison] = Sd(SdField.TroopDefenseStrength) + Sd(SdField.TroopBombardmentDefense);
                s[UnitRole.Assault] = Sd(SdField.TroopAttackStrength); // per troop, not per maintenance (Compute): a landing is limited by troop places, one duel per troop
                break;
            default:
                s[UnitRole.Gun] = Sd(SdField.DefenseAttackStrength);
                s[UnitRole.Shield] = Sd(SdField.DefenseShieldStrength);
                s[UnitRole.BombardDefence] = Sd(SdField.FacilityBombardmentDefense);
                break;
        }
        return s;
    }

    /// <summary>
    /// The roles of the given types: scores per maintenance point (maintenance 0 counts as 1; Speed is a rating, not divided),
    /// each type's main role = its highest score relative to the best other given type of its kind in that role, and dominated =
    /// another type of the kind, of no more maintenance, at least as good in every compared role and better in one (ships: line, screen, durability, speed,
    /// carrier, transport; a ship with interdiction or bombardment is never dominated by one without).
    /// </summary>
    public static Dictionary<uint, UnitRoles> Compute(GameState game, IEnumerable<uint> types)
    {
        var raw = new Dictionary<uint, (UnitKind Kind, int Maintenance, Dictionary<UnitRole, double> Scores, bool Interdictor, bool NeedsCarrier)>();
        foreach (uint t in types.Distinct().Order())
        {
            if (RawScores(game, t) is not { } s || game.Definitions.Resolve(t) is not { } r) continue;
            var kind = KindOf(t)!.Value;
            int m = game.MaintenanceCost(t);
            foreach (var role in s.Keys.ToList()) if (role is not (UnitRole.Speed or UnitRole.Assault)) s[role] /= Math.Max(1, m);
            bool interdictor = kind == UnitKind.CapitalShip && r.Get(SdField.InterdictionStrength) > 0;
            bool needsCarrier = kind == UnitKind.Fighter && r.Get(SdField.Hyperdrive) == 0;
            raw[t] = (kind, m, s, interdictor, needsCarrier);
        }
        var result = new Dictionary<uint, UnitRoles>();
        foreach (var (t, x) in raw)
        {
            var mates = raw.Where(e => e.Value.Kind == x.Kind).ToList();
            // relative to the best of the other units of the kind (a unit that leads two roles is placed by its larger lead)
            double Rel(UnitRole role)
            {
                double own = x.Scores[role], best = mates.Where(e => e.Key != t).Select(e => e.Value.Scores[role]).DefaultIfEmpty(0).Max();
                return own <= 0 ? 0 : best <= 0 ? double.MaxValue : own / best;
            }
            // the median of the kind's units of similar maintenance ( x 0.5 to x 2, this unit included)
            double Median(UnitRole role)
            {
                var band = mates.Where(e => e.Value.Maintenance * 2 >= x.Maintenance && e.Value.Maintenance <= 2 * x.Maintenance)
                    .Select(e => e.Value.Scores[role]).Order().ToList();
                return band.Count == 0 ? 0 : band.Count % 2 == 1 ? band[band.Count / 2] : (band[band.Count / 2 - 1] + band[band.Count / 2]) / 2;
            }
            var candidates = MainRoles(x.Kind);
            if (x.Kind == UnitKind.CapitalShip)
                candidates = x.Scores[UnitRole.Line] < CapacityRoleBelow * Median(UnitRole.Line) && x.Scores[UnitRole.Screen] < CapacityRoleBelow * Median(UnitRole.Screen)
                    && (x.Scores[UnitRole.Carrier] > 0 || x.Scores[UnitRole.Transport] > 0)
                    ? [UnitRole.Carrier, UnitRole.Transport] : [UnitRole.Line, UnitRole.Screen];
            var main = candidates.OrderByDescending(Rel).First();
            var compared = x.Kind == UnitKind.CapitalShip ? ShipDominance : Of(x.Kind);
            bool dominated = mates.Any(e => e.Key != t && e.Value.Maintenance <= x.Maintenance // never by a dearer unit (it may not fit)
                && compared.All(role => e.Value.Scores[role] >= x.Scores[role]) && compared.Any(role => e.Value.Scores[role] > x.Scores[role])
                && (!x.Interdictor || e.Value.Interdictor)
                && (x.Kind != UnitKind.CapitalShip || x.Scores[UnitRole.Bombard] <= 0 || e.Value.Scores[UnitRole.Bombard] > 0));
            result[t] = new(t, x.Kind, x.Maintenance, x.Scores, main, x.Interdictor, x.NeedsCarrier, dominated);
        }
        return result;
    }

    /// <summary>Every type of the side's definitions (Alliance / Empire field set) of the kinds with roles.</summary>
    public static IEnumerable<uint> SideTypes(GameState game, Owner side)
    {
        var d = game.Definitions;
        bool Mine(uint alliance, uint empire) => side == Owner.Alliance ? alliance != 0 : empire != 0;
        return d.CapitalShips.Where(r => Mine(r.Alliance, r.Empire)).Select(r => r.Common.Ref)
            .Concat(d.Fighters.Where(r => Mine(r.Alliance, r.Empire)).Select(r => r.Common.Ref))
            .Concat(d.Troops.Where(r => Mine(r.Alliance, r.Empire)).Select(r => r.Common.Ref))
            .Concat(d.DefenseFacilities.Where(r => Mine(r.Alliance, r.Empire)).Select(r => r.Common.Ref))
            .Where(t => KindOf(t) is not null);
    }
}
