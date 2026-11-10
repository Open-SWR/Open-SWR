using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Planner 5: special forces (special-force phase), character order (CharacterOrder command), facility phase (training). p = 4.
/// A phase other than facility phase / special-force phase gives special-force phase (empty call); special-force phase at state 0 gives 9 (empty call);
/// every end of special-force phase gives facility phase, state 0, done; every end of facility phase gives special-force phase.
/// </summary>
public sealed class SpecialForcePlanner() : ProductionPlanner(PlannerType.SpecialForce)
{
    /// <summary>Special-force phase states.</summary>
    enum SpecialForceState
    {
        Start,
        /// <summary>Force cycle: the first type below its target.</summary>
        TypeCycle,
        /// <summary>Character order (CharacterOrder command), also the over-budget path.</summary>
        CharacterOrder,
        /// <summary>Budget, product and count of the chosen type.</summary>
        Count,
        /// <summary>Unit budget test.</summary>
        Budget,
        /// <summary>Training site (R).</summary>
        Site,
        /// <summary>ProduceTroop command: produce the troop.</summary>
        Produce,
    }

    /// <summary>The first special-force type id of the type cycle (90 to 93).</summary>
    const int FirstSpecialForceType = 90;

    protected override Clone K { get; } = new(SystemRole.Training, ManufacturingKind.Training, TrainingOf, ProductFilter.TrainingFacility, TurnSlot.SpecialForce,
        x => x.TrainingSlot, x => x.TrainingFacilities, r => r.TrainingRole || r.Unrest, TrainingOf, FreeSlotsOf, 4, 30,
        r => r.TrainingMaintenance, TurnByDistance: false);
    ManufacturingManager? _manager;
    int _cycle, _want, _count;
    ProductFilter _filter;
    uint _product;

    protected override void Exit() => (Phase, State, Done) = ((int)PlannerPhase.SpecialForce, (int)SpecialForceState.Start, true);
    void EndUnit() => (Phase, State, Done) = ((int)PlannerPhase.Facility, (int)TemplateStep.Start, true);
    /// <summary>"3 N gives 9, else 7; 7 facility budget &lt; 0 gives 8 H, else special-force phase state 0 without done" (the special-force phase machine steps
    /// in the next frame of the same run).</summary>
    protected override Command? NewSystemFailed(WorldModel m, Owner side)
    {
        if (FacilityBudget(m) < 0) { State = (int)TemplateStep.ScrapOne; return null; }
        (Phase, State) = ((int)PlannerPhase.SpecialForce, (int)SpecialForceState.Start);
        return null;
    }

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        int Budget() => m.UnitBudget(4, 70, m.SpecialForceMaintenance);
        if (CurrentPhase is not (PlannerPhase.Facility or PlannerPhase.SpecialForce)) { Phase = (int)PlannerPhase.SpecialForce; return null; }
        if (CurrentPhase == PlannerPhase.Facility) return Facility(m, controller.Side);
        switch ((SpecialForceState)State)
        {
            case SpecialForceState.Start:
                State = (int)SpecialForceState.Site;
                return null;
            case SpecialForceState.Site: // training site (R)
                if (Recheck(m) is { } r) (_manager, State) = (ManagerOf(r.System, ManufacturingKind.Training), (int)SpecialForceState.TypeCycle);
                else State = (int)SpecialForceState.Budget;
                return null;
            case SpecialForceState.Budget:
                if (Budget() < 0) State = (int)SpecialForceState.CharacterOrder;
                else EndUnit();
                return null;
            case SpecialForceState.TypeCycle: // force cycle: the current type first; each examined type advances the cycle
                if (_cycle == 0) _cycle = FirstSpecialForceType;
                _want = 0;
                for (int k = 0; k < 4 && _want == 0; k++)
                {
                    int t = _cycle - FirstSpecialForceType;
                    _filter = t switch { 0 => ProductFilter.SpecialForceType1, 1 => ProductFilter.SpecialForceType2, 2 => ProductFilter.SpecialForceType3, _ => ProductFilter.SpecialForceType4 };
                    _want = m.MarkedSpecialForces(t) < Math.Max(m.MaxEnemyUnits, 8) ? 1 : 0;
                    _cycle = FirstSpecialForceType + (t + 1) % 4;
                }
                State = _want == 1 ? (int)SpecialForceState.Count : (int)SpecialForceState.Budget;
                return null;
            case SpecialForceState.Count:
            {
                int b = Budget();
                if (b < 0) { State = (int)SpecialForceState.CharacterOrder; return null; }
                if (b == 0) { EndUnit(); return null; }
                // want reduced to b div cost when cost * want > b; want > 0 gives ProduceTroop even without a product (the old
                // product field stays); else exit. b > 0 never goes to the character order.
                if (m.PickProduct(Families.TrainingFacility, _filter, Criterion.MaintenanceCost, RankOrder.Ascending) is ({ } product, var cost))
                {
                    _product = product.Type;
                    if (cost > 0 && cost * _want > b) _want = b / cost;
                }
                if (_want > 0) (_count, State) = (_want, (int)SpecialForceState.Produce);
                else EndUnit();
                return null;
            }
            case SpecialForceState.Produce:
                EndUnit();
                return _manager is null ? null : new Command(CommandType.ProduceTroop, controller.Side) { Targets = [(_manager, GroupMode.Object)], Fields = [_product, (uint)_count] };
            default: // SpecialForceState.CharacterOrder: CharacterOrder command
            {
                EndUnit();
                var pick = PickOne(m.AgentList(a => a.Unassigned && a.Disbandable && !a.OnMission && !a.Enroute, a => a.Maintenance, 0, 1000, RankOrder.Ascending))
                    ?? PickOne(m.AgentList(a => a.Disbandable && !a.OnMission && !a.Enroute, a => a.Maintenance, 0, 1000, RankOrder.Ascending));
                return pick is AiAgent a && Family.IsSpecialForce(a.Agent.Type) ? new Command(CommandType.CharacterOrder, controller.Side) { Targets = [(a.Agent, GroupMode.Object)] } : null;
            }
        }
    }
}
