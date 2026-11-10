using OpenSWR.Data.Dat;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

/// <summary>
/// Delegated planner record: done flag, priority, and the machine's
/// state; the record state lives in <see cref="Advisor.Records"/>. Step = the command slot (one machine step per frame of
/// the issue phase, at most one command); a step without a command leaves the record not done.
/// </summary>
public abstract class DelegatedPlanner(int key)
{
    public int Key { get; } = key;
    public bool Done { get; set; }
    public int Priority { get; set; }
    public int Machine { get; set; }
    /// <summary>Manage Garrisons: regiment count (Manage Production has none).</summary>
    public int Count { get; set; }
    /// <summary>Manage Production: site; Manage Garrisons: target (a sector record, then a system).</summary>
    public object? Target { get; set; }
    /// <summary>Manage Production: an unnamed field; Manage Garrisons: a sector record, then a system record, then the manager.</summary>
    public object? Producer { get; set; }
    /// <summary>Manage Production: an unnamed field; Manage Garrisons: an SdRef, 0 = none.</summary>
    public uint Product { get; set; }

    /// <summary>Command slot: record state != 1 gives machine reset, done, no command.</summary>
    public Command? Step(GameState game, AdvisorModel model, int state)
    {
        if (state != 1)
        {
            (Machine, Done) = (0, true);
            return null;
        }
        return Run(game, model);
    }

    protected abstract Command? Run(GameState game, AdvisorModel model);

    /// <summary>"Fail": machine 0, done, no command.</summary>
    protected Command? Fail()
    {
        (Machine, Done) = (0, true);
        return null;
    }

    /// <summary>Failure slot: a refused command gives done, machine 0, priority 0.</summary>
    public void Failure() => (Done, Machine, Priority) = (true, 0, 0);

    protected static StarSystem? System(object? o) => (o as AdvisorSystem)?.System ?? o as StarSystem; // a loaded game's keys are the systems themselves
}

/// <summary>
/// Planner Manage Production: Mines / Refineries. Machine 0 then 1 producer (the idle construction manager in the
/// sector with the fewest own yards, the system with the fewest) then 2 product (a Mine only while own Mines &lt; own Refineries) and site
/// (nearest sector with free energy (and raw material for a Mine), then the rank sum of distance and the three facility counts) then 3 SetProductionTarget
/// (manager to site; priority +1) then 4 ProduceFacility count 1.
/// </summary>
public sealed class ProductionDelegate() : DelegatedPlanner(Advisor.Production)
{
    protected override Command? Run(GameState game, AdvisorModel m)
    {
        var side = m.Side;
        switch (Machine)
        {
            case 1:
                Producer = AdvisorModel.Pick(m.Sectorwise(x => x.IdleConstruction && x.NoReservedConstruction,
                    AdvisorSectorValue.ConstructionYards, RankOrder.Descending));
                Producer = AdvisorModel.Pick(m.Systemwise(Producer, r => r.IdleConstruction && r.Own && !r.ReservedConstruction, AdvisorValue.ConstructionYards, RankOrder.Descending));
                if (System(Producer) is not { } s || m.Manager(game, s, ManufacturingKind.Construction) is not { } manager) return Fail();
                (Producer, Machine) = (manager, 2);
                return null;
            case 2:
            {
                bool mine = m.Mines < m.Refineries;
                Product = mine ? Refs.Mine : Refs.Refinery;
                var from = Producer as GameObject;
                Target = AdvisorModel.Pick(m.SectorsNear(game, from, x => x.FreeEnergy && (!mine || x.FreeRaw), RankOrder.Descending));
                bool fits(AdvisorSystem r) => r.FreeEnergy && (!mine || r.FreeRaw) && r.Own && !r.Blockade;
                Target = AdvisorModel.Pick(m.SystemsNear(game, Target, from, fits, RankOrder.Descending), m.Systemwise(Target, fits, AdvisorValue.Shipyards, RankOrder.Descending),
                    m.Systemwise(Target, fits, AdvisorValue.TrainingFacilities, RankOrder.Descending), m.Systemwise(Target, fits, AdvisorValue.ConstructionYards, RankOrder.Descending));
                if (Target is not AdvisorSystem) return Fail();
                Machine = 3;
                return null;
            }
            case 3:
                if (System(Target) is not { } site || Producer is not ManufacturingManager { Kind: ManufacturingKind.Construction } yard) return Fail();
                (Machine, Done) = (4, true);
                Priority++;
                return new Command(CommandType.SetProductionTarget, side) { Targets = [(yard, GroupMode.Object)], ArgRef = site };
            case 4:
                (Machine, Done) = (0, true);
                return Producer is ManufacturingManager { Kind: ManufacturingKind.Construction } builder
                    ? new Command(CommandType.ProduceFacility, side) { Targets = [(builder, GroupMode.Object)], Fields = [Product, 1] } : null;
            default:
                Machine = 1;
                return null;
        }
    }
}

/// <summary>
/// Planner Manage Garrisons: troop regiments. Machine 0 then 2 target (own unblockaded system short of troops:
/// Uprising systems first, as read with dir Ascending on the negative surplus) then 1 producer (the idle training manager
/// nearest the target) then 3 product (strongest DefenseStrength "Attack &lt;= Defense" class, cheapest maintenance for an Uprising target)
/// and count (free maintenance cap - dem must be positive), then 4 SetProductionTarget (priority +1), then 5 ProduceTroop.
/// </summary>
public sealed class GarrisonDelegate() : DelegatedPlanner(Advisor.Garrisons)
{
    protected override Command? Run(GameState game, AdvisorModel m)
    {
        var side = m.Side;
        switch (Machine)
        {
            case 2:
                Target = AdvisorModel.Pick(m.Sectorwise(x => x.Uprising && x.TroopShortfall, AdvisorSectorValue.UprisingShortfall, RankOrder.Ascending));
                Target = AdvisorModel.Pick(m.Systemwise(Target, r => r.TroopShortfall && r.Own && r.Uprising && !r.Blockade, AdvisorValue.TroopSurplus, RankOrder.Ascending));
                if (Target is not AdvisorSystem)
                {
                    Target = AdvisorModel.Pick(m.Sectorwise(x => x.TroopShortfall, AdvisorSectorValue.OwnSystems, RankOrder.Descending));
                    Target = AdvisorModel.Pick(m.Systemwise(Target, r => r.TroopShortfall && r.Own && !r.Blockade,
                        AdvisorValue.TroopSurplus, RankOrder.Descending));
                }
                if (Target is not AdvisorSystem) return Fail();
                Machine = 1;
                return null;
            case 1:
            {
                var to = System(Target);
                Producer = AdvisorModel.Pick(m.SectorsNear(game, to, x => x.IdleTraining && x.NoReservedTraining, RankOrder.Descending));
                Producer = AdvisorModel.Pick(m.SystemsNear(game, Producer, to, r => r.IdleTraining && r.Own && !r.ReservedTraining, RankOrder.Descending));
                if (System(Producer) is not { } s || m.Manager(game, s, ManufacturingKind.Training) is not { } manager) return Fail();
                (Producer, Machine) = (manager, 3);
                return null;
            }
            case 3:
            {
                Count = 0;
                int b = m.MaintenanceCapacity - m.MaintenanceDemand;
                if (b <= 0) return Fail();
                // Uprising: MaintenanceCost, cheapest; else DefenseStrength, strongest; no record gives criterion 0
                // a loaded game's target is the system itself: its record is the one the model keeps for it
                var record = Target as AdvisorSystem ?? (Target is StarSystem ts ? m.Systems.GetValueOrDefault(ts) : null);
                var (criterion, dir) = record is { } t ? t.Uprising ? (Ai.Criterion.MaintenanceCost, RankOrder.Descending) : (Ai.Criterion.TroopDefense, RankOrder.Ascending) : (Ai.Criterion.None, default);
                Count = 1;
                var (product, cost) = Ai.WorldModel.PickProduct(game, side, m.Catalogue!, m.Rand, Families.TrainingFacility, Ai.ProductFilter.DefenseTroop, criterion, dir);
                Product = product?.Type ?? 0;
                if (product is not null && cost != 0 && cost > b) Count = b / cost;
                if (Count <= 0) return Fail();
                Machine = 4;
                return null;
            }
            case 4:
                if (System(Target) is not { } target || Producer is not ManufacturingManager { Kind: ManufacturingKind.Training } yard) return Fail();
                (Machine, Done) = (5, true);
                Priority++;
                return new Command(CommandType.SetProductionTarget, side) { Targets = [(yard, GroupMode.Object)], ArgRef = target };
            case 5:
                (Machine, Done) = (0, true);
                return Producer is ManufacturingManager { Kind: ManufacturingKind.Training } trainer
                    ? new Command(CommandType.ProduceTroop, side) { Targets = [(trainer, GroupMode.Object)], Fields = [Product, (uint)Count] } : null;
            default:
                Machine = 2;
                return null;
        }
    }
}
