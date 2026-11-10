namespace OpenSWR.Sim;

/// <summary>
/// Death Star: the Death-Star class (capital ships of families [0x18, 0x1C)), the DeathStarNearby flag of systems, the
/// one-per-system placement record and planet destruction (command 0x223).
/// Not modelled: the Death Star shield facility (0x25, no sim reader).
/// </summary>
public static class DeathStar
{
    public static bool IsDeathStar(uint type) => Family.Of(type) is >= 0x18 and < 0x1C;

    /// <summary>
    /// DeathStarNearby recompute, galaxy-wide. A sector counts as found when a Usable Death-Star class ship of any owner
    /// sits in one of its systems; every Usable system of a sector then gets that flag. No draw.
    /// </summary>
    internal static void RecomputeNearby(GameState game)
    {
        var sectors = game.Created.Where(o => IsDeathStar(o.Type) && o.Usable && o.System is not null).Select(o => o.System!.Sector).ToHashSet();
        foreach (var s in game.Systems.Where(s => s.Usable)) s.DeathStarNearby = sectors.Contains(s.Sector);
    }

    /// <summary>0x301 handler part: a Death-Star class object's Usable changed.</summary>
    internal static void UsableChanged(GameState game, GameObject o)
    {
        if (IsDeathStar(o.Type)) RecomputeNearby(game);
    }

    /// <summary>0x320 handler part: a Death-Star class object moved between sectors (or a sector is unknown).</summary>
    internal static void ParentChanged(GameState game, GameObject o, GameObject? old, GameObject now)
    {
        if (IsDeathStar(o.Type) && (old?.System?.Sector is not { } a || now.System?.Sector is not { } b || a != b)) RecomputeNearby(game);
    }

    /// <summary>
    /// Capacity check for a unit placed into the system itself. Death-Star class ships in the system's other fleets (sandbox
    /// mode) count against a limit of 1. Both the unit's own record (a Death-Star class unit) and its contents (a fleet with
    /// Death Stars) must fit, and every alternative must fit. Refused with error (0x90, 3). A Death Star placed into a fleet
    /// checks only the fleet (unlimited).
    /// </summary>
    internal static bool Room(StarSystem s, Fleets.Sandbox sandbox, GameObject? unit, uint type)
    {
        int self = IsDeathStar(type) ? 1 : 0, contents = unit is Fleet f ? f.Contents.Count(x => IsDeathStar(x.Type) && x.Existing) : 0;
        if (self == 0 && contents == 0) return true;
        int remaining = 1 - s.Contents.OfType<Fleet>().Where(x => x != unit).Sum(x => sandbox.Count(x, 0x18, 0x1C));
        return remaining >= self && remaining >= contents;
    }

    /// <summary>
    /// Planet destruction, command 0x223 with a Death-Star class ship among the ships. The system is destroyed with reason
    /// 0x10 when the attacking side owns it, else with reason 0xF, which cascades to its contents. Then every minor character
    /// [0x38, 0x3C) still directly in the system, Existing and not EnrouteActive, in key order: chance(G 0x606). On success
    /// the character takes one wound (Injury + 1, the minor death test), then DestroyedReason 10 if still Existing.
    /// True = destroyed (shots skipped, galaxy-wide waves in the aftermath).
    /// </summary>
    internal static bool DestroyPlanet(GameState game, StarSystem s, Owner attacker, IEnumerable<GameObject> ships)
    {
        if (!ships.Any(x => IsDeathStar(x.Type))) return false;
        Objects.Destroy(game, s, s.Owner == attacker ? 0x10 : 0xF);
        foreach (var c in s.Contents.OfType<Character>().Where(c => Family.Of(c.Type) is >= 0x38 and < 0x3C).ToList())
        {
            if (!c.Existing || c.EnrouteActive || c.Container != s) continue;
            if (!game.Random.Chance(game.Params.General(0x606), DrawSite.PlanetMinor)) continue;
            Missions.ApplyWound(game, c, 1);
            if (c.Existing) Objects.Destroy(game, c, 10);
        }
        return true;
    }

    /// <summary>
    /// Aftermath of a destroyed planet: two galaxy-wide waves for the attacker a. First the core record
    /// [a, G 0x1404, a, G 0x1403] over every sector (registry order), then the rim record [a, G 0x1402, a, G 0x1401].
    /// </summary>
    internal static void Waves(GameState game, Owner a)
    {
        var g = game.Params;
        foreach (var (lo, hi, limit, delta) in new[] { (0x90u, 0x92u, 0x1404u, 0x1403u), (0x92u, 0x94u, 0x1402u, 0x1401u) })
            foreach (var sector in game.Sectors)
                Systems.Wave(game, sector, a, a, g.General(limit), g.General(delta), lo, hi);
    }
}
