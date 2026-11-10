using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Rim-first expansion: prefers rim systems (sector Group != 1) more than
/// <see cref="ReachDays"/> (speed 100) from every known enemy system and fleet. Populated ones go first in the diplomacy
/// expansion; explored unpopulated ones with energy get one troop unit dropped from the weakest idle fleet (one drop at a time),
/// keep it as their garrison, and then want a shield and a gun ahead of other facilities. Views only for the enemy; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int RimDropMax = 1;

    bool RimFirsting => On(V7.RimFirst);

    /// <summary>The systems a rim drop took, in drop order (measurement; not saved).</summary>
    public List<uint> RimDropped { get; } = [];

    /// <summary>A rim system more than ReachDays from every known enemy system and fleet (<paramref name="places"/> = <see cref="EnemyPlaces"/>).</summary>
    internal bool RimSafe(GameState game, StarSystem s, List<StarSystem> places) =>
        !s.Sector.IsCore && places.All(e => Orders.TravelDays(game, e, s, 100) > ReachDays);

    /// <summary>
    /// The drop targets: the safe rim system the view shows neutral, explored and unpopulated (a never-refreshed view is unpopulated too),
    /// with energy (known once explored).
    /// </summary>
    internal List<StarSystem> RimDropTargets(GameState game, Known k)
    {
        var places = EnemyPlaces(game, k);
        return [.. game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: Owner.Neutral, Destroyed: false, Explored: true, Populated: false }
            && s.Energy > 0 && RimSafe(game, s, places)).OrderBy(s => s.Key)];
    }

    /// <summary>
    /// The drops of one decision: a fleet at a target lands one troop there; with fewer than <see cref="RimDropMax"/> fleets at or
    /// on the way to a target, the weakest idle fleet (not in a swarm group) at an own system with a troop aboard moves to the
    /// nearest target, else the weakest with troop room and a spare troop at its system loads one.
    /// </summary>
    internal void RimDrops(GameState game, Known k, HashSet<GameObject> used)
    {
        var targets = RimDropTargets(game, k);
        if (targets.Count == 0) return;
        var fleets = Fleets(game).OrderBy(f => f.Key).ToList();
        foreach (var f in fleets.Where(f => !f.Enroute && f.Usable && !used.Contains(f) && f.Container is StarSystem at && targets.Contains(at)))
        {
            var at = (StarSystem)f.Container!;
            if (FleetTroops(f, used).FirstOrDefault() is { } troop && Issue(game, Move(Side, troop, at), used))
            {
                RimDropped.Add(at.Ref);
                used.Add(f); // stays in orbit this decision
            }
        }
        if (fleets.Count(f => f.Container is StarSystem at && targets.Contains(at)) >= RimDropMax) return;
        var idle = fleets.Where(f => Idle(game, f, used) && f.Container is StarSystem at && at.Owner == Side && _groups.All(g => g.Fleet != f.Ref))
            .OrderBy(f => k.Strength(f, Side)).ThenBy(f => f.Key).ToList();
        if (idle.FirstOrDefault(Carrying) is { } carrier)
        {
            var from = (StarSystem)carrier.Container!;
            Issue(game, Move(Side, carrier, targets.OrderBy(t => Orders.Distance(from, t)).ThenBy(t => t.Key).First()), used);
            return;
        }
        foreach (var f in idle)
        {
            var ship = f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).OrderBy(x => x.Key).FirstOrDefault(x => TroopRoom(game, x) > 0);
            if (ship is null || Spare((StarSystem)f.Container!, used).FirstOrDefault() is not { } troop) continue;
            if (Issue(game, Move(Side, troop, f), used)) used.Add(f);
            return;
        }
    }

    /// <summary>
    /// The rim defence sites: each own unpopulated rim system wants a shield (family 36), then a gun (family 35), where its
    /// energy has room and the construction yards can build them. Appended after the threat-driven sites.
    /// </summary>
    internal List<(StarSystem? Site, Data.Dat.SdRecord? Product)> RimDefenceSites(GameState game)
    {
        var buildable = Research.Buildable(game, Side, Families.ConstructionYard);
        var result = new List<(StarSystem?, Data.Dat.SdRecord?)>();
        foreach (var s in Own(game).Where(s => !s.Populated && !s.Sector.IsCore))
        {
            int room = s.Energy - s.UsedEnergy;
            foreach (uint family in new uint[] { Families.PlanetaryShield, Families.Gun })
            {
                if (room <= 0) break;
                if (s.Contents.Any(x => x.Owner == Side && x.Existing && Family.Of(x.Type) == family)) continue;
                if (buildable.FirstOrDefault(r => Family.Of(r.Common.Ref) == family) is not { } product) continue;
                result.Add((s, product));
                room--;
            }
        }
        return result;
    }
}
