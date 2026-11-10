using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>
/// Death Star: the Death-Star class (capital ships of families [24, 28)), the DeathStarNearby flag of systems, the
/// one-per-system placement record and planet destruction (the planet destruction command).
/// Not modelled: the Death Star shield facility (family 37, no sim reader).
/// </summary>
public static class DeathStar
{
    public static bool IsDeathStar(uint type) => Family.Of(type) is >= Families.DeathStar and < Families.Fighter;

    /// <summary>
    /// DeathStarNearby recompute, galaxy-wide. A sector counts as found when a Usable Death-Star class ship of any owner
    /// sits in one of its systems; every Usable system of a sector then gets that flag. No draw.
    /// </summary>
    internal static void RecomputeNearby(GameState game)
    {
        var sectors = game.Created.Where(o => IsDeathStar(o.Type) && o.Usable && o.System is not null).Select(o => o.System!.Sector).ToHashSet();
        foreach (var s in game.Systems.Where(s => s.Usable)) s.DeathStarNearby = sectors.Contains(s.Sector);
    }

    /// <summary>UsableChanged handler part: a Death-Star class object's Usable changed.</summary>
    internal static void UsableChanged(GameState game, GameObject o)
    {
        if (IsDeathStar(o.Type)) RecomputeNearby(game);
    }

    /// <summary>ParentChanged handler part: a Death-Star class object moved between sectors (or a sector is unknown).</summary>
    internal static void ParentChanged(GameState game, GameObject o, GameObject? old, GameObject now)
    {
        if (IsDeathStar(o.Type) && (old?.System?.Sector is not { } a || now.System?.Sector is not { } b || a != b)) RecomputeNearby(game);
    }

    /// <summary>
    /// Capacity check for a unit placed into the system itself. Death-Star class ships in the system's other fleets (sandbox
    /// mode) count against a limit of 1. Both the unit's own record (a Death-Star class unit) and its contents (a fleet with
    /// Death Stars) must fit, and every alternative must fit. Refused with error Refusal.DeathStarLimit. A Death Star placed into a fleet
    /// checks only the fleet (unlimited).
    /// </summary>
    internal static bool Room(StarSystem s, Fleets.Sandbox sandbox, GameObject? unit, uint type)
    {
        int self = IsDeathStar(type) ? 1 : 0, contents = unit is Fleet f ? f.Contents.Count(x => IsDeathStar(x.Type) && x.Existing) : 0;
        if (self == 0 && contents == 0) return true;
        int remaining = 1 - s.Contents.OfType<Fleet>().Where(x => x != unit).Sum(x => sandbox.Count(x, Families.DeathStar, Families.Fighter));
        return remaining >= self && remaining >= contents;
    }

    /// <summary>
    /// Planet destruction, CommandType.BombardDestroy with a Death-Star class ship among the ships. The system is destroyed with reason
    /// OwnBombardment when the attacking side owns it, else with reason Bombardment, which cascades to its contents. Then every minor character
    /// [56, 60) still directly in the system, Existing and not EnrouteActive, in key order: chance(DestroyedPlanetWoundChance). On success
    /// the character takes one wound (Injury + 1, the minor death test), then DestroyedReason.Killed if still Existing.
    /// True = destroyed (shots skipped, galaxy-wide waves in the aftermath).
    /// </summary>
    internal static bool DestroyPlanet(GameState game, StarSystem s, Owner attacker, IEnumerable<GameObject> ships)
    {
        if (!ships.Any(x => IsDeathStar(x.Type))) return false;
        Objects.Destroy(game, s, s.Owner == attacker ? DestroyedReason.OwnBombardment : DestroyedReason.Bombardment);
        foreach (var c in s.Contents.OfType<Character>().Where(c => Family.Of(c.Type) is >= Families.MinorCharacter and < Families.SpecialForce).ToList())
        {
            if (!c.Existing || c.EnrouteActive || c.Container != s) continue;
            if (!game.Random.Chance(game.Rules.DestroyedPlanetWoundChance, DrawSite.PlanetMinor)) continue;
            Missions.ApplyWound(game, c, 1);
            if (c.Existing) Objects.Destroy(game, c, DestroyedReason.Killed);
        }
        return true;
    }

    /// <summary>
    /// Aftermath of a destroyed planet: two galaxy-wide waves for the attacker a. First the core record
    /// [a, DestroyedPlanetCoreLimit, a, DestroyedPlanetCoreDelta] over every sector (registry order), then the rim record [a, DestroyedPlanetRimLimit, a, DestroyedPlanetRimDelta].
    /// </summary>
    internal static void Waves(GameState game, Owner a)
    {
        var g = game.Rules;
        foreach (var (lo, hi, limit, delta) in new[] { (Families.CoreSystem, Families.RimSystem, g.DestroyedPlanetCoreLimit, g.DestroyedPlanetCoreDelta), (Families.RimSystem, Families.RimSystemEnd, g.DestroyedPlanetRimLimit, g.DestroyedPlanetRimDelta) })
            foreach (var sector in game.Sectors)
                Systems.Wave(game, sector, a, a, limit, delta, lo, hi);
    }
}
