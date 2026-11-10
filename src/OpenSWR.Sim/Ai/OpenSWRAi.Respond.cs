namespace OpenSWR.Sim.Ai;

/// <summary>
/// Measurement: for
/// own systems lost to troop assault, how the side saw the attack coming. Recorded only when aimatch asks; no effect on play.
/// Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>A threat: the own system, the known enemy fleets with troops Enroute to or in orbit there, their known strength and troops, the expected landing in days.</summary>
    internal sealed record AssaultThreat(StarSystem System, List<GameObject> Fleets, int Strength, int Troops, int LandingDays);

    /// <summary>One own system lost to troop assault, as the side saw it coming (measurement).</summary>
    public sealed record AssaultSeen(int Day, uint System, bool Seen, int WarningDays, int AttackerStrength, int AttackerTroops, int FleetStrength, int FleetDays);

    /// <summary>The losses to troop assault with their warning (null = off).</summary>
    public List<AssaultSeen>? AssaultLossesSeen { get; set; }

    /// <summary>Measurement: per own system, the first decision day a threat was known, its strength and troops, and the own fleets then (travel days, strength).</summary>
    readonly Dictionary<uint, (int First, int Strength, int Troops, List<(int Days, int Strength)> Fleets)> _threatSeen = [];

    /// <summary>
    /// The threats: per own system, the known enemy fleets carrying troops Enroute there or in orbit (Inferred: troop capacity of
    /// their known capital ships, as the watch reads it, or known troops aboard). Landing: in orbit 1 day (Inferred: a
    /// landing is not instant); Enroute the watch's estimate (travel days, speed 100, from the nearest system seen as the enemy's; at least 1).
    /// </summary>
    internal List<AssaultThreat> Threats(GameState game, Known k)
    {
        var result = new List<AssaultThreat>();
        var enemies = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        int Capacity(GameObject f) => k.Contents(f).Where(v => Family.IsCapitalShip(v.Object.Type))
            .Sum(v => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == v.Object.Type)?.TroopCapacity ?? 0))
            + k.Contents(f).SelectMany(v => k.Contents(v.Object)).Count(v => v.In(0x10, 0x14));
        foreach (var s in Own(game))
        {
            var views = k.Contents(s).Where(v => v.Object is Fleet && v.Owner == Enemy && Capacity(v.Object) > 0).ToList();
            if (views.Count == 0) continue;
            bool orbit = views.Any(v => !v.Enroute);
            int landing = orbit ? 1 : Math.Max(1, enemies.Select(e => Orders.TravelDays(game, e, s, 100)).DefaultIfEmpty(1).Min());
            var fleets = views.Select(v => v.Object).ToList();
            result.Add(new(s, fleets, fleets.Sum(f => k.Strength(f, Enemy)), fleets.Sum(Capacity), landing));
        }
        return result;
    }

    /// <summary>Measurement each decision (aimatch only): the first sighting of a threat per own system, kept while it lasts.</summary>
    void WatchThreats(GameState game, Known k)
    {
        if (AssaultLossesSeen is null) return;
        var threats = Threats(game, k);
        foreach (var r in _threatSeen.Keys.Where(r => threats.All(t => t.System.Ref != r) && game.Systems.First(s => s.Ref == r).Owner == Side).ToList())
            _threatSeen.Remove(r);
        foreach (var t in threats)
        {
            var fleets = Fleets(game).Where(f => f.Container is StarSystem at && at != t.System && f.Usable)
                .Select(f => (Days: Orders.TravelDays(game, (StarSystem)f.Container!, t.System, Math.Max(1, Orders.Speed(game, f))), Strength: k.Strength(f, Side))).ToList();
            if (_threatSeen.TryGetValue(t.System.Ref, out var seen))
                _threatSeen[t.System.Ref] = (seen.First, Math.Max(seen.Strength, t.Strength), Math.Max(seen.Troops, t.Troops), seen.Fleets);
            else _threatSeen[t.System.Ref] = (game.Day, t.Strength, t.Troops, fleets);
        }
    }

    /// <summary>
    /// Measurement at a loss to troop assault (Inferred: a loss where the side's view shows enemy troops):
    /// seen beforehand, warning days, attacker, and the nearest own fleet that could have arrived within the warning.
    /// </summary>
    void RecordAssaultSeen(GameState game, Known? k, StarSystem lost)
    {
        if (AssaultLossesSeen is null || k is null || EnemyTroops(k, lost) == 0) return;
        if (!_threatSeen.Remove(lost.Ref, out var seen))
        {
            AssaultLossesSeen.Add(new(game.Day, lost.Ref, false, 0, k.Strength(lost, Enemy), EnemyTroops(k, lost), 0, 0));
            return;
        }
        int warning = game.Day - seen.First;
        var inTime = seen.Fleets.Where(f => f.Days <= warning).OrderBy(f => f.Days).ToList();
        AssaultLossesSeen.Add(new(game.Day, lost.Ref, true, warning, seen.Strength, seen.Troops, inTime.Count > 0 ? inTime[0].Strength : -1, inTime.Count > 0 ? inTime[0].Days : -1));
    }
}
