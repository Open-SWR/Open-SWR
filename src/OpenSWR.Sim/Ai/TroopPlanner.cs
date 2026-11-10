using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>Planner 6: troops: moves in the troop-move phase, production in the troop-production phase, facility phase (training). p = 22.</summary>
public sealed class TroopPlanner() : ProductionPlanner(PlannerType.Troop)
{
    /// <summary>The four troop-move steps of troop-move phase (the step is the state).</summary>
    enum TroopMoveStep
    {
        /// <summary>Not set (the default value).</summary>
        None,
        /// <summary>Defence base.</summary>
        Defence,
        /// <summary>Assault base.</summary>
        Assault,
        /// <summary>Uprising with a garrison shortfall.</summary>
        Uprising,
        /// <summary>Garrison shortfall (the last step; also any other value).</summary>
        Garrison,
    }

    /// <summary>Troop-production phase states.</summary>
    enum TroopProductionState
    {
        /// <summary>Not set (the default value).</summary>
        None,
        /// <summary>Training site (R).</summary>
        Site,
        /// <summary>Target pick.</summary>
        Target,
        /// <summary>Budget, product and count.</summary>
        Count,
        /// <summary>Over budget: a troop of the excess system.</summary>
        OverBudget,
        /// <summary>SetProductionTarget command: set the production target.</summary>
        SetTarget,
        /// <summary>ProduceTroop command: produce the troops.</summary>
        Produce,
    }

    protected override Clone K { get; } = new(SystemRole.Training, ManufacturingKind.Training, TrainingOf, ProductFilter.TrainingFacility, TurnSlot.Troop,
        x => x.TrainingSlot, x => x.TrainingFacilities, r => r.TrainingRole || r.Unrest, TrainingOf, FreeSlotsOf, 22, 20,
        r => r.TrainingMaintenance);
    ManufacturingManager? _manager;
    StarSystem? _target;
    int _facilities, _want, _count, _rotation = 1;
    /// <summary>The target selectors that found nothing in the current target pick.</summary>
    readonly HashSet<TroopTarget> _failed = [];
    ProductFilter _filter;
    Criterion _criterion;
    uint _product;
    RankOrder _dir;
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. It is the one product
    /// field of both the unit order and the facility-template order, so both of ours are reset.
    /// </summary>
    internal override void AfterLoad() => (_product, _fProduct) = (0, 0);

    /// <summary>Inferred: runs start (and end) in the troop-move phase step 1.</summary>
    protected override void Exit() => (Phase, State, Done) = ((int)PlannerPhase.TroopMove, (int)TroopMoveStep.Defence, true);
    int Budget(WorldModel m) => m.UnitBudget(22, 80, m.FleetTroopMaintenance + m.TroopMaintenance);
    /// <summary>Production exits after state 7: facility phase at N, with done.</summary>
    void EndProduction() => (Phase, State, Done) = ((int)PlannerPhase.Facility, (int)TemplateStep.NewSystem, true);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (CurrentPhase == PlannerPhase.Start) (Phase, State) = ((int)PlannerPhase.TroopMove, (int)TroopMoveStep.Defence);
        if (CurrentPhase == PlannerPhase.Facility) return Facility(m, side);
        if (CurrentPhase == PlannerPhase.TroopMove)
        {
            var move = Move(m, side, (TroopMoveStep)State);
            // a move, or a state from the Garrison step on (with or without a move), ends the run without priority
            if (move is not null || State >= (int)TroopMoveStep.Garrison) (Phase, State, Done) = ((int)PlannerPhase.TroopProduction, (int)TroopProductionState.Site, true);
            else State++; // the move steps run in declaration order
            return move;
        }
        switch ((TroopProductionState)State)
        {
            case TroopProductionState.Site: // training site
                if (Recheck(m) is { } r) (_manager, _facilities, State) = (ManagerOf(r.System, ManufacturingKind.Training), r.TrainingFacilities, (int)TroopProductionState.Target);
                else EnterFacility();
                return null;
            case TroopProductionState.Target:
                if (Target(m)) State = (int)TroopProductionState.Count;
                else EnterFacility();
                return null;
            case TroopProductionState.Count:
            {
                int b = Budget(m);
                if (m.Dominant && b <= 0)
                    foreach (var (k, floor) in new[] { (ModelConstant.KeyGarrison, 9), (ModelConstant.RoleGarrison, 3), (ModelConstant.Garrison, 0) })
                        if (m.Constant(k) > floor) m.Constant(k)--;
                if (b < 0) { State = (int)TroopProductionState.OverBudget; return null; }
                // Deviation: the original stores product none when the pick finds nothing and still goes to the order on a
                // positive count; openswr ends here. Only reachable with mods (Inferred: the install data always has a level-0 product for this mask).
                if (b == 0 || m.PickProduct(Families.TrainingFacility, _filter, _criterion, _dir) is not ({ } product, var cost)) { EndProduction(); return null; }
                _count = Math.Min(cost * _want > b && cost > 0 ? b / cost : _want, _facilities);
                if (_count <= 0) { EndProduction(); return null; }
                (_product, State) = (product.Type, (int)TroopProductionState.SetTarget);
                return null;
            }
            case TroopProductionState.OverBudget: // over budget: a troop of the excess system
            {
                EndProduction();
                var s = SectorThenSystem(() => [m.SectorList(x => x.GarrisonSurplus, x => x.TroopSurplus, RankOrder.Descending)], x => [m.SystemList(x, r => r.GarrisonSurplus && r.Own, r => r.TroopSurplus, RankOrder.Descending)]);
                return s is not null && Troops(m, s.System, 0).FirstOrDefault() is { } t ? Scrap(side, [t]) : null;
            }
            case TroopProductionState.SetTarget:
                if (_manager is null) { EndProduction(); return null; }
                State = (int)TroopProductionState.Produce;
                return Urgent(new Command(CommandType.SetProductionTarget, side) { Targets = [(_manager, GroupMode.Object)], ArgRef = _target });
            default: // TroopProductionState.Produce
                EndProduction();
                return new Command(CommandType.ProduceTroop, side) { Targets = [(_manager!, 0)], Fields = [_product, (uint)_count] };
        }
    }

    /// <summary>Usable troops of the system with the class mask (0 = any), weakest first by SD DefenseStrength, ties in key order.</summary>
    static IEnumerable<GameObject> Troops(WorldModel m, StarSystem s, ProductFilter filter) =>
        Usable(m, s, TroopFamilies).Where(t => filter == ProductFilter.None || m.Catalogue[t.Type]?.Is(filter) == true).OrderBy(t => m.Catalogue[t.Type]?.DefenseStrength ?? 0);

    /// <summary>
    /// Move steps 1-4: destination; need n (steps 1 / 2: the troop need
    /// of the first fleet op in key order whose base is the destination; none gives 0, no command; they run only with
    /// a defense / assault base); source sectors by distance to the destination, dir Ascending (the farthest), source systems by their rank value (dir Descending) (Inferred:
    /// own, where the table gives no filter); n := min(n, the source's rank value) Usable troops of the step's class (Defence and Garrison: defence troops; Assault: attack troops; Uprising: any)
    /// in key order (Inferred for the vtable minimum) gives Move. Source lists follow the table order (fallbacks
    /// own only, without Blockade or a base in every step; the system's garrison troop marks select the class).
    /// </summary>
    /// <summary>Move sources exclude blockaded systems and fleet-op bases of task types 4-6.</summary>
    static bool MoveExclude(AiSystem r) => r.Blockade || r.AssaultBase || r.DefenseBase;

    Command? Move(WorldModel m, Owner side, TroopMoveStep step)
    {
        if (step == TroopMoveStep.Defence && !m.DefenseBase || step == TroopMoveStep.Assault && !m.AssaultBase) return null;
        var (dest, sector, sectorDir, system, systemExcl, fallbackSector, fallbackSystem) = step switch
        {
            TroopMoveStep.Defence => (SectorThenSystem(() => [m.SectorDefaultList(x => x.DefenseBase, RankOrder.Ascending)], x => [m.SystemList(x, r => r.Own && r.DefenseBase && !r.Blockade, r => r.Troops, RankOrder.Descending)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus && x.DefenseTroopSurplus), RankOrder.Ascending,
                (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.DefenseTroops && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)(x => x.DefenseGarrisonSurplus && x.GarrisonSurplus),
                (Func<AiSystem, bool>?)(r => r.GarrisonSurplus && r.GenCoreShort && r.Own)),
            TroopMoveStep.Assault => (SectorThenSystem(() => [m.SectorDefaultList(x => x.AssaultBase, RankOrder.Ascending)], x => [m.SystemList(x, r => r.Own && r.AssaultBase && !r.Blockade, r => r.Troops, RankOrder.Descending)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus && x.AttackTroopSurplus), RankOrder.Ascending,
                (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.AttackTroops && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)(x => x.AttackGarrisonSurplus && x.GarrisonSurplus),
                (Func<AiSystem, bool>?)(r => r.AttackGarrison && r.GarrisonSurplus && r.Own)),
            TroopMoveStep.Uprising => (SectorThenSystem(() => [m.SectorList(x => x.OwnUprising && x.GarrisonShort, x => x.TroopShortfall, RankOrder.Descending)], x => [m.SystemList(x, r => r.Uprising && r.GarrisonShort && r.Own && !r.Blockade, r => r.TroopShortfall, RankOrder.Descending)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus), RankOrder.Ascending, (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)null, (Func<AiSystem, bool>?)null),
            _ => (SectorThenSystem(() => [m.SectorList(x => x.GarrisonShort, x => x.TroopShortfall, RankOrder.Descending)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Blockade, r => r.TroopShortfall, RankOrder.Descending)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus && x.DefenseTroopSurplus), RankOrder.Ascending,
                (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.DefenseTroops && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)(x => x.DefenseGarrisonSurplus && x.GarrisonSurplus),
                (Func<AiSystem, bool>?)(r => r.DefenseGarrison && r.GarrisonSurplus && r.Own)),
        };
        if (dest is null) return null;
        int need = step is TroopMoveStep.Defence or TroopMoveStep.Assault ? m.FleetOps.Values.FirstOrDefault(o => o.Base == dest.System) is { } op ? FleetOps.TroopNeed(m, op) : 0 : dest.TroopShortfall;
        if (need <= 0) return null;
        var source = SectorThenSystem(() => [m.SectorDistanceList(dest.System, sector, sectorDir)], x => [m.SystemList(x, r => system(r) && !systemExcl(r), r => r.TroopSurplus, RankOrder.Descending)]);
        if (source is null && fallbackSector is not null)
            source = SectorThenSystem(() => [m.SectorDistanceList(dest.System, fallbackSector, sectorDir)], x => [m.SystemList(x, r => fallbackSystem!(r) && !systemExcl(r), r => r.TroopSurplus, RankOrder.Descending)]);
        if (source is null) return null;
        var troops = Troops(m, source.System, step switch { TroopMoveStep.Assault => ProductFilter.AttackTroop, TroopMoveStep.Uprising => ProductFilter.None, _ => ProductFilter.DefenseTroop }).Take(Math.Min(need, source.TroopSurplus)).ToList();
        return troops.Count == 0 ? null : new Command(CommandType.Move, side) { Targets = [.. troops.Select(t => (t, GroupMode.Object))], ArgRef = dest.System };
    }

    /// <summary>Where planner 6 looks for a troop target.</summary>
    enum TroopTarget { Facility, RoleGarrison, KeyGarrison, Garrison, GarrisonTroops }

    static readonly TroopTarget[][] Orders =
    [
        [TroopTarget.RoleGarrison, TroopTarget.GarrisonTroops, TroopTarget.Garrison, TroopTarget.Facility, TroopTarget.KeyGarrison],
        [TroopTarget.GarrisonTroops, TroopTarget.Garrison, TroopTarget.Garrison, TroopTarget.Facility, TroopTarget.KeyGarrison, TroopTarget.RoleGarrison],
        [TroopTarget.Facility, TroopTarget.KeyGarrison, TroopTarget.RoleGarrison, TroopTarget.GarrisonTroops, TroopTarget.Garrison],
        [TroopTarget.KeyGarrison, TroopTarget.RoleGarrison, TroopTarget.GarrisonTroops, TroopTarget.Garrison, TroopTarget.Facility],
    ];

    /// <summary>
    /// Target: the selectors in the rotating order (1 gives 2 gives 3 gives 4 gives 1), each only while no target is set.
    /// Every call clears the four failures and zeroes want / filter / criterion / direction first.
    /// </summary>
    bool Target(WorldModel m)
    {
        _target = null;
        _failed.Clear();
        (_want, _filter, _criterion, _dir) = (0, 0, 0, default);
        foreach (var selector in Orders[_rotation - 1])
            if (_target is null) Select(m, selector);
        _rotation = _rotation % 4 + 1;
        if (_failed.Count == 4 && m.Outgunned && Budget(m) > 0)
            foreach (var (k, limit) in new[] { (ModelConstant.KeyGarrison, 12), (ModelConstant.RoleGarrison, 6), (ModelConstant.Garrison, 4) })
                if (m.Constant(k) < limit) m.RaiseConstant(k);
        return _target is not null;
    }

    void Select(WorldModel m, TroopTarget selector)
    {
        if (_manager is null) return;
        AiSystem? s = selector switch
        {
            TroopTarget.GarrisonTroops => SectorThenSystem(() => [m.SectorDistanceList(_manager, x => x.GarrisonTroopsShort, RankOrder.Descending)], x => [m.SystemList(x, r => r.GarrisonTroopsShort && r.Own && !r.Unrest, r => r.TroopShortfall, RankOrder.Descending)]),
            TroopTarget.RoleGarrison => SectorThenSystem(() => [m.SectorDistanceList(_manager, x => x.RoleGarrisonShort, RankOrder.Descending)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Unrest && !r.CalmWithoutRole, r => r.TroopShortfall, RankOrder.Descending)]),
            TroopTarget.KeyGarrison => SectorThenSystem(() => [m.SectorList(x => x.KeySystem, x => x.TroopShortfall, RankOrder.Descending)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Unrest, r => r.TroopShortfall, RankOrder.Descending)]),
            TroopTarget.Garrison => SectorThenSystem(() => [m.SectorDistanceList(_manager, x => x.GarrisonShort, RankOrder.Descending)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Unrest, r => r.TroopShortfall, RankOrder.Descending)]),
            _ => m.Record(_manager.System),
        };
        if (s is null) { if (selector != TroopTarget.Facility) _failed.Add(selector); return; }
        _target = s.System;
        (_want, _filter, _criterion, _dir) = selector switch
        {
            TroopTarget.GarrisonTroops => (s.HasRole || s.KeySystem) ? (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.TroopDefense, RankOrder.Ascending) : (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.MaintenanceCost, RankOrder.Descending),
            TroopTarget.RoleGarrison or TroopTarget.KeyGarrison => (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.TroopDefense, RankOrder.Ascending),
            TroopTarget.Garrison => (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.MaintenanceCost, RankOrder.Descending),
            _ => (_facilities, ProductFilter.AttackTroop, Criterion.TroopAttack, RankOrder.Ascending),
        };
    }
}
