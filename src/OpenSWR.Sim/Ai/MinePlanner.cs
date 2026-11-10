using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

// Production planners 2, 3, 5, 6, 7, 10, 11. State numbers follow the original's order within each phase; the facility
// facility phase is the shared template (ProductionPlanner.Facility). Unless a step says so, " gives X" with a command = the command
// ends this run and the planner continues at X on its next step.

/// <summary>Planner 2: Mines and Refineries with their own construction role 16777216. p = 3.</summary>
public sealed class MinePlanner() : ProductionPlanner(PlannerType.Mine)
{
    /// <summary>Planner 2's states in its Mine and Refinery phases and in the facility phase (50 / 51).</summary>
    enum MineState
    {
        /// <summary>Not set (the default value).</summary>
        None,
        /// <summary>Maintenance check (only when capacity is short).</summary>
        Check,
        /// <summary>Scrap the facilities of the maintenance-check system.</summary>
        CheckScrap,
        /// <summary>Mine / Refinery site.</summary>
        Site,
        /// <summary>Scrap the site's producers without their role.</summary>
        SiteScrap,
        /// <summary>SetProductionTarget command: set the production target.</summary>
        SetTarget,
        /// <summary>ProduceFacility command: produce the Mine / Refinery.</summary>
        Produce,
        /// <summary>Facility phase: maintenance check.</summary>
        FacilityCheck,
        /// <summary>Facility phase: scrap of the maintenance-check system.</summary>
        FacilityCheckScrap,
    }

    protected override Clone K { get; } = new(SystemRole.Mine, ManufacturingKind.Construction, YardsOf, ProductFilter.ConstructionYard, TurnSlot.Mine,
        x => x.ConstructionSlot, x => x.ConstructionYards, r => r.AnyConstructionRole || r.Unrest, YardsOf, FreeSlotsOf, 3, 100,
        r => r.ConstructionMaintenance, BothSectorLists: true);
    ManufacturingManager? _manager;
    StarSystem? _mine, _refinery, _scrap;
    /// <summary>The facility phase's own maintenance-check step; None once the template steps run (the state then holds a <see cref="TemplateStep"/>).</summary>
    MineState _facilityCheck;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (CurrentPhase == PlannerPhase.Start)
        {
            _facilityCheck = MineState.None;
            if (Recheck(m) is { } r)
                (_manager, Phase, State) = (ManagerOf(r.System, ManufacturingKind.Construction), m.Refineries <= m.Mines ? (int)PlannerPhase.Refinery : (int)PlannerPhase.Mine, (int)MineState.Check);
            else (Phase, _facilityCheck) = ((int)PlannerPhase.Facility, MineState.FacilityCheck);
            return null;
        }
        if (CurrentPhase == PlannerPhase.Facility)
            return _facilityCheck switch
            {
                MineState.FacilityCheck => MaintenanceCheck(m, MineState.FacilityCheckScrap, TemplateStep.NewSystem),
                MineState.FacilityCheckScrap => ScrapAll(m, side, TemplateStep.NewSystem),
                _ => Facility(m, side),
            };
        bool refinery = CurrentPhase == PlannerPhase.Refinery;
        switch ((MineState)State)
        {
            case MineState.Check: return MaintenanceCheck(m, MineState.CheckScrap, MineState.Site);
            case MineState.CheckScrap: return ScrapAll(m, side, MineState.Site);
            case MineState.Site: // Mine / Refinery site
            {
                AiSystem? site;
                // keep the previous site while calm and still a Mine / Refinery site, no owner test
                if (m.Record(refinery ? _refinery : _mine) is { } kept && (refinery ? kept.RefinerySite : kept.MineSite) && Calm(kept)) site = kept;
                else site = Near(m, x => refinery ? x.RefinerySite : x.MineSite, r => refinery ? r.RefinerySite : r.MineSite)
                    ?? Near(m, x => refinery ? x.HasFreeEnergy : x.HasFreeRawMaterial, r => refinery ? r.HasFreeEnergy : r.HasFreeRawMaterial);
                if (site is null) { Finish(); return null; }
                if (refinery) _refinery = site.System;
                else _mine = site.System;
                State = (int)MineState.SiteScrap;
                return null;
            }
            case MineState.SiteScrap: // the site's Usable producers without their role
            {
                State = (int)MineState.SetTarget;
                var at = refinery ? _refinery! : _mine!;
                var r = m.Systems[at];
                var take = new List<GameObject>();
                foreach (var k in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction })
                    if (!HasRoleFor(r, k)) take.AddRange(Usable(m, at, FamilyOf(k)));
                return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
            }
            case MineState.SetTarget:
                if (_manager is null) { Finish(); return null; }
                State = (int)MineState.Produce;
                return Urgent(new Command(CommandType.SetProductionTarget, side) { Targets = [(_manager, GroupMode.Object)], ArgRef = refinery ? _refinery : _mine });
            default: // MineState.Produce
                Finish();
                return new Command(CommandType.ProduceFacility, side) { Targets = [(_manager!, 0)], Fields = [refinery ? Refs.Refinery : Refs.Mine, 1] };
        }
    }

    /// <summary>The nearest sector to the construction manager that passes the sector test, in it by distance the calm system that passes the system test.</summary>
    AiSystem? Near(WorldModel m, Func<AiSector, bool> sectorBit, Func<AiSystem, bool> systemBit) => _manager is null ? null
        : SectorThenSystem(() => [m.SectorDistanceList(_manager, sectorBit, RankOrder.Descending)], x => [m.SystemDistanceList(x, _manager, r => systemBit(r) && !r.Unrest, RankOrder.Descending)]);

    /// <summary>Only when cap &lt; dem, the role-less own system to clear (sectors with a free slot for every producer kind, systems by producers).</summary>
    Command? MaintenanceCheck(WorldModel m, Enum found, Enum next)
    {
        _scrap = m.MaintenanceCapacity < m.MaintenanceDemand ? SectorThenSystem(
            () => [m.SectorList(x => x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot, x => x.ConstructionYards, RankOrder.Ascending), m.SectorList(x => x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot, x => x.Shipyards, RankOrder.Ascending), m.SectorList(x => x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot, x => x.TrainingFacilities, RankOrder.Ascending)],
            x => [m.SystemList(x, r => r.Own && !r.HasRole, r => r.ConstructionYards, RankOrder.Ascending), m.SystemList(x, r => r.Own && !r.HasRole, r => r.Shipyards, RankOrder.Ascending), m.SystemList(x, r => r.Own && !r.HasRole, r => r.TrainingFacilities, RankOrder.Ascending)])?.System : null;
        Go(_scrap is null ? next : found);
        return null;
    }

    /// <summary>Next step: in the facility phase a <see cref="MineState"/> stays in the own check, a <see cref="TemplateStep"/> starts the template.</summary>
    void Go(Enum next)
    {
        if (CurrentPhase != PlannerPhase.Facility) State = (int)(MineState)next;
        else if (next is MineState check) _facilityCheck = check;
        else (_facilityCheck, State) = (MineState.None, (int)(TemplateStep)next);
    }

    /// <summary>Scrap of every Usable shipyard, training facility and construction yard of that system.</summary>
    Command? ScrapAll(WorldModel m, Owner side, Enum next)
    {
        Go(next);
        var take = Usable(m, _scrap!, Families.Shipyard, Families.TrainingFacility, Families.ConstructionYard);
        return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
    }
}
