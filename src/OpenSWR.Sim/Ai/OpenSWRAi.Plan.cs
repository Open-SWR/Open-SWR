using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    /// <summary>The production need of one decision: a weighted shortfall per category, and the phase.</summary>
    internal sealed record ProductionPlan(string Phase, double Troops, double CapitalShips, double Fighters, double SpecialForces, double Defence);

    /// <summary>
    /// The plan: per category the shortfall (need - have) / need (0 when nothing is needed) x the
    /// phase's weight. Needs: troops the troop need (<see cref="TroopNeed"/>); capital ships a fleet value of max(1.2 x the known enemy
    /// fleet value within reach of the power base, 1.5 x each attack group's target defence); fighters the fighter room of own
    /// capital ships; special forces the scouting units (the covert special forces are not built: deviation); defence
    /// facilities the systems that want one (<see cref="DefenceSites"/>). Phases: expansion (day &lt; 150, or under 30 % of the known
    /// populated systems own): special forces 1.5; build-up (own fleet value below the known enemy's): capital ships 1.5; else hold:
    /// troops 1.5, defence facilities 1.2.
    /// </summary>
    internal ProductionPlan Plan(GameState game, Known k, SectorMap map)
    {
        double Short(double need, double have) => need <= 0 ? 0 : Math.Clamp((need - have) / need, 0, 1);
        var fleets = Fleets(game).ToList();
        int own = fleets.Sum(f => k.Strength(f, Side));
        var powerBase = Own(game).Where(s => map.PowerBase.Contains(s.Sector)).ToList();
        var enemyFleets = game.Systems.Where(s => powerBase.Any(b => Orders.TravelDays(game, b, s, 100) <= ReachDays))
            .Sum(s => k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy)));
        int allEnemy = game.Systems.Sum(s => k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy)));
        double fleetNeed = Math.Max(1.2 * enemyFleets, _groups.Sum(g => 1.5 * k.Strength(g.Target, Enemy)));
        var ships = fleets.SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)).ToList();
        int fighterRoom = ships.Sum(x => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type)?.FighterCapacity ?? 0));
        int fighters = ships.Sum(x => x.Contents.Count(y => Family.Of(y.Type) is >= Families.Fighter and < Families.Headquarters));
        int scoutsWanted = ScoutUnitsMissing(game, map) > 0 ? ScoutUnitsWanted : 0;
        var known = game.Systems.Select(s => Knowledge.See(game, s, Side)).Where(v => v.Populated && !v.Destroyed).ToList();
        bool expansion = game.Day < 150 || known.Count > 0 && known.Count(v => v.Owner == Side) < 0.3 * known.Count;
        bool buildUp = !expansion && own < allEnemy;
        string phase = expansion ? "expansion" : buildUp ? "build-up" : "hold";
        return new(phase,
            Short(Need.Total, OwnTroops(game)) * (phase == "hold" ? 1.5 : 1),
            Short(fleetNeed, own) * (buildUp ? 1.5 : 1),
            Short(fighterRoom, fighters),
            (On(V7.Agents) ? Short(AgentPool * game.Sides[Side].MaintenanceCapacity, SpecialForces(game).Sum(a => game.MaintenanceCost(a.Type)))
                : On(V7.Teams) || On(V7.Intel) ? Short(scoutsWanted + ForceNeed, SpecialForces(game).Count) : Short(scoutsWanted, ScoutUnits(game).Count)) * (expansion ? 1.5 : 1),
            (DefenceSites(game, k).Count > 0 ? 1 : 0) * (phase == "hold" ? 1.2 : 1));
    }

    /// <summary>
    /// The production facility kind a construction yard orders: shipyard (family 40), then
    /// training facility (family 41), then construction yard (family 42); a kind with an idle manager (a non-suspended member, empty queue) is
    /// skipped; the facility must be buildable with its maintenance within <paramref name="free"/>. Null: none.
    /// </summary>
    internal SdRecord? ProductionFacility(GameState game, int free)
    {
        var managers = Own(game).SelectMany(s => ScreenViews.Managers(s, Side)).Where(m => Economy.Members(m).Any(f => !f.Suspended)).ToList();
        foreach (var (kind, family) in new[] { (ManufacturingKind.Ship, Families.Shipyard), (ManufacturingKind.Training, Families.TrainingFacility), (ManufacturingKind.Construction, Families.ConstructionYard) })
        {
            if (managers.Any(m => m.Kind == kind && m.Orders.Count == 0)) continue;
            if (Pick(game, Families.ConstructionYard, family) is { } facility && game.MaintenanceCost(facility.Common.Ref) <= free) return facility;
        }
        return null;
    }

    void Why(ManufacturingManager m, string reason)
    {
        if (IdleReasons is not null) IdleReasons[m] = reason;
    }

    /// <summary>
    /// One production order turned down by the estimate (measurement, recorded only while <see cref="IdleReasons"/> is on): the
    /// validation result, whether the build-day estimate failed (no non-suspended member) while validation passed, the estimate's
    /// maintenance over the AI's free, the product and count, the AI's free against the side's real free maintenance, the manager's
    /// system against its TargetKey, the TargetKey's owner, and whether the queue was non-empty.
    /// </summary>
    public sealed record RefusedEstimate(int Day, ManufacturingManager Manager, uint Product, int Count, OrderResult Validation, bool EstimateNull,
        bool MaintenanceOver, int AiFree, int RealFree, bool SameSystem, Owner TargetOwner, bool QueueNonEmpty);
}
