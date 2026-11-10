using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Wings (fighters as system defence and battle weight): every own system whose view
/// shows an enemy fleet in reach gets a fighter garrison target of ceil((known enemy fighters + capital ships in reach) / 4), at least
/// 2 and at most <see cref="WingsMax"/>; the nearest idle shipyard builds the shortfall with the existing fighter pick, aimed at the
/// system (fighters ordered into an own system stay there). Carriers keep their
/// priority; with the maintenance short for both, the fighters beat the capital ship only while own fighters &lt; own capital
/// ships x 2. Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int WingsMin = 2, WingsMax = 8;

    /// <summary>The fighter garrison target for this many known enemy fighters and capital ships in reach.</summary>
    internal static int WingTarget(int enemy) => Math.Clamp((enemy + 3) / 4, WingsMin, WingsMax);

    /// <summary>
    /// The own systems short of their fighter garrison target, by priority (value x (1 + known enemy units in reach)), with the
    /// shortfall: target - own fighters in the system (queued and travelling ones included: they are placed at the order). A
    /// blockaded system is left out.
    /// </summary>
    internal List<(StarSystem System, int Want)> WingNeeds(GameState game, Known k)
    {
        var places = EnemyPlaces(game, k).Where(e => k.Fleets(e, Enemy).Count > 0).ToList();
        var needs = new List<(StarSystem System, int Want, int Enemy)>();
        foreach (var s in Own(game).Where(s => !s.Blockade))
        {
            var units = places.Where(e => Orders.TravelDays(game, e, s, 100) <= ReachDays).SelectMany(e => k.Fleets(e, Enemy)).SelectMany(f => k.Contents(f)).ToList();
            int enemy = units.Count(v => Family.IsCapitalShip(v.Object.Type) || v.In(Families.Fighter, Families.Headquarters)) + units.Sum(v => k.Contents(v.Object).Count(c => c.In(Families.Fighter, Families.Headquarters)));
            if (enemy == 0) continue;
            int have = s.Contents.Count(x => x.Owner == Side && Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters);
            if (WingTarget(enemy) > have) needs.Add((s, WingTarget(enemy) - have, enemy));
        }
        return [.. needs.OrderByDescending(x => _values.GetValueOrDefault(x.System.Ref) * (1 + x.Enemy)).ThenBy(x => x.System.Key).Select(x => (x.System, x.Want))];
    }

    /// <summary>
    /// The wing yards: per system of <see cref="WingNeeds"/> in priority order, the idle ship manager with a working shipyard in
    /// the own system nearest to it (a manager serves one system; fighter-yard managers excluded).
    /// </summary>
    internal Dictionary<ManufacturingManager, (StarSystem System, int Want)> WingYards(GameState game, Known k, HashSet<GameObject> used,
        Dictionary<ManufacturingManager, Fleet>? fighterYards)
    {
        var yards = new Dictionary<ManufacturingManager, (StarSystem, int)>();
        var managers = Own(game).SelectMany(s => ScreenViews.Managers(s, Side).Where(m => m.Kind == ManufacturingKind.Ship && m.Orders.Count == 0 && !used.Contains(m)
                && fighterYards?.ContainsKey(m) != true && Economy.Members(m).Any(f => !f.Suspended))
            .Select(m => (System: s, Manager: m))).ToList();
        foreach (var need in WingNeeds(game, k))
        {
            var nearest = managers.Where(x => !yards.ContainsKey(x.Manager)).OrderBy(x => Orders.Distance(x.System, need.System)).ThenBy(x => x.System.Key)
                .Select(x => x.Manager).FirstOrDefault();
            if (nearest is null) break;
            yards[nearest] = need;
        }
        return yards;
    }

    /// <summary>
    /// A wing yard's turn: the fighter (<see cref="DoctrineFighter"/>, one that can jump when the system is elsewhere; else the
    /// cheapest at the yard's own system), unless the maintenance cannot pay both it and <paramref name="ship"/> and own fighters
    /// >= own capital ships x 2. Retargets the yard first (the fighters follow at the next decision). True: the yard's turn is used.
    /// </summary>
    bool WingOrder(GameState game, Known k, ManufacturingManager m, StarSystem s, (StarSystem System, int Want) wing, SdRecord? ship, ref int free, HashSet<GameObject> used)
    {
        bool here = wing.System == s;
        if ((DoctrineFighter(game, k, free, jumping: !here) ?? (here ? Pick(game, Families.Shipyard, Families.Fighter, cheapest: true) : null)) is not { } fighter) return false;
        if (ship is not null && game.MaintenanceCost(fighter.Common.Ref) * wing.Want + game.MaintenanceCost(ship.Common.Ref) > free)
        {
            var units = game.Created.Where(o => o.Owner == Side && o.Existing).ToList();
            if (units.Count(o => Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters) >= 2 * units.Count(o => Family.IsCapitalShip(o.Type))) return false;
        }
        if (m.TargetKey != wing.System)
        {
            bool retargeted = Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = wing.System }, used);
            Why(m, retargeted ? "retarget" : Room ? "refused" : "budget");
            return retargeted;
        }
        int n = Build(game, CommandType.ProduceShip, m, fighter, wing.Want, ref free, used);
        Why(m, n > 0 ? "ordered" : _buildFail);
        return n > 0;
    }
}
