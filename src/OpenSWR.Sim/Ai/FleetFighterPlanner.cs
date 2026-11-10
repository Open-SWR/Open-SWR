using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>Planner 10: fighters for fleets, facility phase (shipyards). p = 8.</summary>
public sealed class FleetFighterPlanner() : ProductionPlanner(PlannerType.FleetFighter)
{
    /// <summary>Planner 10's states in its unit phase. Its facility phase uses <see cref="TemplateStep"/>.</summary>
    enum FleetFighterState
    {
        /// <summary>Not set (the default value).</summary>
        None,
        /// <summary>Site (R).</summary>
        Site,
        /// <summary>Destination: the operation tree from the cursor, else fleets with free fighter slots, else the system.</summary>
        Destination,
        /// <summary>Budget, criterion, product and count.</summary>
        Pick,
        /// <summary>Over budget: scrap a fighter.</summary>
        OverBudget,
        /// <summary>Over-budget test before the facility phase.</summary>
        BudgetTest,
        /// <summary>SetProductionTarget command: set the production target.</summary>
        SetTarget,
        /// <summary>ProduceShip command: produce the fighters.</summary>
        Produce,
    }

    protected override Clone K { get; } = new(SystemRole.Shipyard, ManufacturingKind.Ship, ShipyardsOf, ProductFilter.Shipyard, TurnSlot.FleetFighter,
        x => x.ShipyardSlot, x => x.Shipyards, r => r.ShipyardRole || r.Unrest, ShipyardsOf, r => r.Energy, 8, 20,
        r => r.ShipyardMaintenance);
    ManufacturingManager? _manager;
    AiFleet? _fleet; // a fleet of the fallback
    StarSystem? _system; // an operation's base
    int _want, _op, _nextOp;
    int _count;
    uint _product;
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. It is the one product
    /// field of both the unit order and the facility-template order, so both of ours are reset.
    /// </summary>
    internal override void AfterLoad() => (_product, _fProduct) = (0, 0);

    protected override void Exit() => (Phase, State, Done) = ((int)PlannerPhase.FleetFighter, (int)FleetFighterState.Site, true);
    /// <summary>Every unit-phase end: facility phase at N, with done.</summary>
    void EndUnit() => (Phase, State, Done) = ((int)PlannerPhase.Facility, (int)TemplateStep.NewSystem, true);
    int Budget(WorldModel m) => m.UnitBudget(8, 80, m.FleetFighterMaintenance);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (CurrentPhase == PlannerPhase.Start) (Phase, State) = ((int)PlannerPhase.FleetFighter, (int)FleetFighterState.Site);
        if (CurrentPhase == PlannerPhase.Facility) return Facility(m, side);
        switch ((FleetFighterState)State)
        {
            case FleetFighterState.Site: // site (R)
                if (Recheck(m) is { } r) (_manager, State) = (ManagerOf(r.System, ManufacturingKind.Ship), (int)FleetFighterState.Destination);
                else State = (int)FleetFighterState.BudgetTest;
                return null;
            case FleetFighterState.Destination: // the operation tree from the cursor, no wrap; else fleets own + created + free fighter slots
            {
                (_fleet, _system, _want) = (null, null, 1);
                FleetOp? found = null;
                foreach (var op in m.FleetOps.Values.Where(o => _nextOp == 0 || o.Key >= _nextOp))
                    if (FleetOps.FighterNeed(m, op) is { } b) { (found, _system, _want) = (op, b, op.FighterNeed); break; }
                (_op, _nextOp) = found is null ? (0, 0) : (found.Key, m.FleetOps.Keys.FirstOrDefault(k => k > found.Key));
                if (found is null)
                    _fleet = PickOne(m.FleetList(f => FreeWithRoom(f) && !f.NotCompletedShip && !f.EnrouteShip && !f.Enroute, f => f.FighterRoom, RankOrder.Descending)) as AiFleet;
                State = _fleet is null && _system is null ? (int)FleetFighterState.BudgetTest : (int)FleetFighterState.Pick;
                return null;
            }
            case FleetFighterState.Pick:
            {
                int b = Budget(m);
                if (b < 0) { State = (int)FleetFighterState.OverBudget; return null; }
                if (b == 0) { EndUnit(); return null; }
                var criterion = m.Rand(10) > 5 ? Criterion.Torpedoes : Criterion.AntiFighterFirepower;
                // Deviation: the original stores product none when the pick finds nothing and still goes to the order on a
                // positive count; openswr ends here. Only reachable with mods (Inferred: the install data always has a level-0 product for this mask).
                if (m.PickProduct(Families.Shipyard, ProductFilter.Fighter, criterion, RankOrder.Ascending) is not ({ } product, var cost)) { EndUnit(); return null; }
                int want = _want;
                _count = cost * want > b && cost > 0 ? b / cost : want;
                // the manager's non-suspended members, fleet and system destinations alike.
                if (_manager is not null) _count = Math.Min(_count, Economy.Members(_manager).Count(f => !f.Suspended));
                if (_count <= 0) { EndUnit(); return null; }
                (_product, State) = (product.Type, (int)FleetFighterState.SetTarget);
                return null;
            }
            case FleetFighterState.BudgetTest:
                if (Budget(m) < 0) State = (int)FleetFighterState.OverBudget;
                else EnterFacility();
                return null;
            case FleetFighterState.OverBudget: // over budget (fleet and ship fields read 0: round 7)
            {
                EndUnit();
                if (PickOne(m.FleetList(f => OwnFree(f) && !f.Enroute, f => f.UsableFighters, RankOrder.Ascending)) is not AiFleet f
                    || PickOne(m.ShipList(f, s => OwnFreeShip(s) && !s.Enroute, s => s.UsableFighters, RankOrder.Ascending)) is not AiShip ship) return null;
                return Usable(m, ship.Ship, FighterFamilies).MinBy(m.FighterKey) is { } squadron ? Scrap(side, [squadron]) : null;
            }
            case FleetFighterState.SetTarget:
                if (_manager is null) { EndUnit(); return null; }
                State = (int)FleetFighterState.Produce;
                return Urgent(new Command(CommandType.SetProductionTarget, side) { Targets = [(_manager, GroupMode.Object)], ArgRef = (GameObject?)_fleet?.Fleet ?? _system });
            default: // FleetFighterState.Produce
                EndUnit();
                return new Command(CommandType.ProduceShip, side) { Targets = [(_manager!, 0)], Fields = [_product, (uint)_count] };
        }
    }
}
