using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Intel: a system's view is
/// fresh when the side's view was updated within <see cref="FreshDays"/> (<see cref="Knowledge.UpdatedDay"/>; live = today).
/// Covert missions and attacks against a system without a fresh view wait, and an espionage there is ordered first; every known
/// enemy system in the power base, the frontier or attack reach keeps a view no older than <see cref="CoverDays"/>, stalest first,
/// special forces before characters. Views only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int FreshDays = 30, CoverDays = 60;

    /// <summary>Days since the side's view of the system was last updated (never: the day itself).</summary>
    internal int ViewAge(GameState game, StarSystem s) => game.Day - Knowledge.UpdatedDay(game, s, Side);

    /// <summary>
    /// The side's view of the system was never refreshed after game setup: not live, and its frozen view never written or written
    /// only on day 1 (the setup refresh). Its contents are unknown, so it is never fresh (GitHub issue 1: early attacks at
    /// unscouted systems counted as fresh for the first <see cref="FreshDays"/>).
    /// </summary>
    internal bool Unscouted(GameState game, StarSystem s) => !Knowledge.IsLive(s, Side) && Knowledge.UpdatedDay(game, s, Side) <= 1;

    internal bool Fresh(GameState game, StarSystem s) => ViewAge(game, s) <= FreshDays && !Unscouted(game, s);

    /// <summary>Systems of this decision's covert candidates that waited for a fresh view (look before acting).</summary>
    readonly List<StarSystem> _lookFirst = [];

    /// <summary>Intel targets of the last decision left without a special force (the special-force need; saved).</summary>
    internal int IntelDemand { get; private set; }

    /// <summary>The special forces wanted beyond the scouting units: the Teams demand and the Intel demand, each when its flag is on.</summary>
    int ForceNeed => (On(V7.Teams) ? SpecialForceDemand : 0) + (On(V7.Intel) ? IntelDemand : 0);

    /// <summary>One openswr decision against a system and the side's view age there (measurement; aimatch only).</summary>
    public sealed record IntelDecision(int Day, string Decision, int Age);

    /// <summary>The view age at each covert, attack and assault decision (measurement; null = off).</summary>
    public List<IntelDecision>? IntelAges { get; set; }

    void RecordAge(GameState game, string decision, StarSystem? s)
    {
        if (IntelAges is not null && s is not null) IntelAges.Add(new(game.Day, decision, ViewAge(game, s)));
    }

    /// <summary>
    /// The known enemy systems in reach: the side's views show them enemy-owned and not destroyed, in a power-base or frontier
    /// sector or within <see cref="ReachDays"/> of an own system. Ascending key.
    /// </summary>
    internal List<StarSystem> IntelReach(GameState game, SectorMap map)
    {
        var own = Own(game);
        return [.. game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy
            && (map.PowerBase.Contains(s.Sector) || map.Frontier.Contains(s.Sector) || own.Any(b => Orders.TravelDays(game, b, s, 100) <= ReachDays))).OrderBy(s => s.Key)];
    }

    /// <summary>Measurement (aimatch): the side's known enemy systems in reach and how many of them have a fresh view.</summary>
    public static (int Fresh, int Total) FreshShare(GameState game, Owner side)
    {
        var c = new OpenSWRController(side);
        var reach = c.IntelReach(game, c.Sectors(game));
        return (reach.Count(s => c.Fresh(game, s)), reach.Count);
    }

    /// <summary>
    /// The Intel targets in order: the look-before-acting systems (this decision's waiting covert targets, then the attack groups'
    /// targets) without a fresh view, then the systems in reach whose view is older than <see cref="CoverDays"/>, stalest first.
    /// </summary>
    internal List<StarSystem> IntelTargets(GameState game, SectorMap map)
    {
        var first = _lookFirst.Concat(Targets()).Where(s => !Fresh(game, s)).Distinct().ToList();
        var cover = IntelReach(game, map).Where(s => ViewAge(game, s) > CoverDays && !first.Contains(s))
            .OrderByDescending(s => ViewAge(game, s)).ThenBy(s => s.Key);
        return [.. first.Concat(cover)];
    }

    void SaveIntel(JsonObject j)
    {
        if (IntelDemand > 0) j["intelDemand"] = IntelDemand;
        if (SpecialForceDemand > 0) j["forceDemand"] = SpecialForceDemand;
    }

    void LoadIntel(JsonObject j)
    {
        IntelDemand = (int?)j["intelDemand"] ?? 0;
        SpecialForceDemand = (int?)j["forceDemand"] ?? 0;
    }
}
