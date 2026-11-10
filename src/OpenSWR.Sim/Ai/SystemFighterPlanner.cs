using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Planner 7: fighters for systems: moves in the fighter-move phase, production in the fighter-production phase, facility phase (shipyards).
/// p = 17. Status != PlannerStatus.Active gives phase 0; another phase gives fighter-move phase (empty call); fighter-move phase / fighter-production phase at state 0
/// gives first state (empty call); each fighter-move phase end gives fighter-production phase, each fighter-production phase end gives facility phase, each facility phase end gives fighter-move phase (Inferred: each end
/// also ends the run).
/// </summary>
public sealed class SystemFighterPlanner() : ProductionPlanner(PlannerType.SystemFighter)
{
    /// <summary>
    /// States of planner 7. The moves phase uses Start, Source, Destination, Move and Scrap; the production phase
    /// (fighter-production phase) uses Start, Site, Destination, Pick, OverBudget, OverBudgetTest, SetTarget and Produce.
    /// </summary>
    enum FighterState
    {
        Start,
        /// <summary>Moves: the source with a usable-fighter surplus.</summary>
        Source,
        /// <summary>Moves and production: the destination (production: its fighter shortfall).</summary>
        Destination,
        /// <summary>Moves: the fighter move.</summary>
        Move,
        /// <summary>Moves: scrap the source's remaining squadrons.</summary>
        Scrap,
        /// <summary>Production: shipyard site (R).</summary>
        Site,
        /// <summary>Production: order pick, count and product.</summary>
        Pick,
        /// <summary>Production: over budget, scrap one squadron.</summary>
        OverBudget,
        /// <summary>Production: over-budget test (f = 90).</summary>
        OverBudgetTest,
        /// <summary>SetProductionTarget command: set the production target.</summary>
        SetTarget,
        /// <summary>ProduceShip command: produce the fighters.</summary>
        Produce,
    }

    protected override Clone K { get; } = new(SystemRole.Shipyard, ManufacturingKind.Ship, ShipyardsOf, ProductFilter.Shipyard, TurnSlot.Fighter,
        x => x.ShipyardSlot, x => x.Shipyards, r => r.ShipyardRole || r.Unrest, ShipyardsOf, FreeSlotsOf, 17, 20,
        r => r.ShipyardMaintenance);
    ManufacturingManager? _manager; // Used by fighter-production phase
    AiSystem? _source; // Used by fighter-move phase
    GameObject? _destination;
    AiFleet? _fleet;
    int _yards, _count;
    uint _product;
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. It is the one product
    /// field of both the unit order and the facility-template order, so both of ours are reset.
    /// </summary>
    internal override void AfterLoad() => (_product, _fProduct) = (0, 0);

    protected override void Exit() => (Phase, State, Done) = ((int)PlannerPhase.FighterMove, (int)FighterState.Start, true);
    void EndMoves() => (Phase, State, Done) = ((int)PlannerPhase.FighterProduction, (int)FighterState.Start, true);
    void EndProduction() => (Phase, State, Done) = ((int)PlannerPhase.Facility, (int)TemplateStep.Start, true);
    int Budget(WorldModel m, int f = 80) => m.UnitBudget(17, f, m.FighterMaintenance);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { (Phase, State, Done) = ((int)PlannerPhase.Start, (int)FighterState.Start, true); return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (CurrentPhase is not (PlannerPhase.FighterMove or PlannerPhase.FighterProduction or PlannerPhase.Facility)) { (Phase, State) = ((int)PlannerPhase.FighterMove, (int)FighterState.Start); return null; }
        if (CurrentPhase == PlannerPhase.Facility) return Facility(m, side);
        if (CurrentPhase == PlannerPhase.FighterMove) return Moves(m, side);
        switch ((FighterState)State)
        {
            case FighterState.Start:
                State = (int)FighterState.Site;
                return null;
            case FighterState.Site: // shipyard site (R)
                if (Recheck(m) is { } r) (_manager, _yards, State) = (ManagerOf(r.System, ManufacturingKind.Ship), r.Shipyards, (int)FighterState.Destination);
                else State = (int)FighterState.OverBudgetTest;
                return null;
            case FighterState.Destination: // destination
            {
                var dest = Destination(m);
                if (dest is { } d && d.FighterBalance < 0) { (_destination, State) = (d.System, (int)FighterState.Pick); return null; }
                if (dest is null && Budget(m) > 0) foreach (var k in new[] { ModelConstant.FighterMargin, ModelConstant.RoleFighterMargin, ModelConstant.KeyFighterMargin }) m.RaiseConstant(k);
                State = (int)FighterState.OverBudgetTest;
                return null;
            }
            case FighterState.Pick:
            {
                int b = Budget(m);
                if (b < 0) { State = (int)FighterState.OverBudget; return null; }
                var rec = m.Systems[(StarSystem)_destination!];
                var (product, cost) = b > 0 ? (rec.HasRole || rec.KeySystem) ? m.PickProduct(Families.Shipyard, ProductFilter.Fighter, Criterion.AntiFighterFirepower, RankOrder.Ascending) : m.PickProduct(Families.Shipyard, ProductFilter.Fighter, Criterion.MaintenanceCost, RankOrder.Descending) : (null, 0);
                // Deviation: the original stores product none when the pick finds nothing and still goes to the order on a
                // positive count; openswr ends here. Only reachable with mods (Inferred: the install data always has a level-0 product for this mask).
                if (product is null) { EndProduction(); return null; }
                int want = -rec.FighterBalance;
                _count = Math.Min(cost * want > b && cost > 0 ? b / cost : want, _yards);
                if (_count <= 0) { EndProduction(); return null; }
                (_product, State) = (product.Type, (int)FighterState.SetTarget);
                return null;
            }
            case FighterState.SetTarget:
                if (_manager is null) { EndProduction(); return null; }
                State = (int)FighterState.Produce;
                return Urgent(new Command(CommandType.SetProductionTarget, side) { Targets = [(_manager, GroupMode.Object)], ArgRef = _destination });
            case FighterState.Produce:
                EndProduction();
                return new Command(CommandType.ProduceShip, side) { Targets = [(_manager!, 0)], Fields = [_product, (uint)_count] };
            case FighterState.OverBudgetTest: // over-budget test (f = 90): over gives 7, else the facility phase in the same think (done not set)
                if (Budget(m, 90) < 0) State = (int)FighterState.OverBudget;
                else (Phase, State) = ((int)PlannerPhase.Facility, (int)TemplateStep.Start);
                return null;
            default: // FighterState.OverBudget: over budget
                EndProduction();
                return OverBudget(m, side);
        }
    }

    /// <summary>
    /// Fighter-move phase: source with surplus; destination (fleets held with room, fleets free with room, the system stage, the
    /// operation fallback); move (the same squadron repeated) or scrap.
    /// </summary>
    Command? Moves(WorldModel m, Owner side)
    {
        switch ((FighterState)State)
        {
            case FighterState.Start:
                State = (int)FighterState.Source;
                return null;
            case FighterState.Source:
                _source = SectorThenSystem(() => [m.SectorList(x => x.HasUsableFighterSurplus, x => x.UsableFighterSurplus, RankOrder.Ascending)],
                    x => [m.SystemList(x, r => r.Own && !r.Blockade && !r.Base, r => r.UsableFighterBalance, RankOrder.Ascending)]);
                if (_source is not null && _source.UsableFighterBalance > 0) State = (int)FighterState.Destination;
                else (Phase, State) = ((int)PlannerPhase.FighterProduction, (int)FighterState.Start); // without done, the production phase runs in the same think
                return null;
            case FighterState.Destination:
            {
                _fleet = FleetDestination(m, _source!.System, HeldWithRoom) ?? FleetDestination(m, _source.System, FreeWithRoom);
                var d = _fleet is not null ? null
                    : SectorThenSystem(() => [m.SectorDistanceList(_source.System, x => x.UsableFighterShort, RankOrder.Descending)], x => [m.SystemList(x, r => r.FighterShort && r.UsableFighterShort && r.Own && !r.Blockade, r => r.FighterBalance, RankOrder.Descending)]);
                _destination = _fleet?.Fleet ?? (GameObject?)d?.System ?? OperationFallback(m, _source.System);
                State = _destination is null ? (int)FighterState.Scrap : (int)FighterState.Move;
                return null;
            }
            case FighterState.Move:
            {
                int want = _fleet is not null ? _fleet.FighterRoom : -m.Systems[(StarSystem)_destination!].FighterBalance; // also for the fallback's system (as specced)
                int n = Math.Min(want, _source!.UsableFighterBalance);
                var squadron = Fighter(m, _source.System);
                if (n <= 0 || squadron is null) { State = (int)FighterState.Scrap; return null; }
                EndMoves(); // no priority (run 2 tick 9: planner 7's move does not keep it urgent)
                return new Command(CommandType.Move, side) { Targets = [.. Enumerable.Repeat((squadron, GroupMode.Object), n)], ArgRef = _destination };
            }
            default: // FighterState.Scrap
            {
                int n = _source!.FighterBalance;
                var squadron = Fighter(m, _source.System);
                EndMoves();
                return n <= 0 || squadron is null ? null : Scrap(side, Enumerable.Repeat(squadron, n));
            }
        }
    }

    /// <summary>
    /// Stage 4: theater sectors ranked by a helper and by distance to the source
    /// (dir Descending), in pick order the fleet op of the sector: its positive fighter need gives its base.
    /// </summary>
    static StarSystem? OperationFallback(WorldModel m, StarSystem source)
    {
        var stack = new ListStack();
        stack.Push(m.SectorDefaultList(x => x.TheaterMark, RankOrder.Descending));
        stack.Push(m.SectorDistanceList(source, x => x.TheaterMark, RankOrder.Descending));
        while (stack.Pick() is AiSector sector)
            if (m.FleetOps.GetValueOrDefault(sector.FleetOpKey) is { } op && FleetOps.FighterNeed(m, op) is { } b) return b;
        return null;
    }

    /// <summary>Fleets with the flags, excl Enroute, ranked by distance to the source (dir Descending) and free fighter slots (dir Ascending).</summary>
    static AiFleet? FleetDestination(WorldModel m, StarSystem source, Func<AiFleet, bool> pass) =>
        PickOne(m.FleetDistanceList(source, f => pass(f) && !f.Enroute, RankOrder.Descending), m.FleetList(f => pass(f) && !f.Enroute, f => f.FighterRoom, RankOrder.Ascending)) as AiFleet;


    /// <summary>The source's Usable fighter squadron (Inferred: key order for the vtable minimum).</summary>
    static GameObject? Fighter(WorldModel m, StarSystem s) => Usable(m, s, FighterFamilies).MinBy(m.FighterKey);

    /// <summary>The three destination stages (sectors by distance to the shipyard manager, systems by their rank value, dir Descending).</summary>
    AiSystem? Destination(WorldModel m)
    {
        if (_manager is null) return null;
        AiSystem? d = null;
        foreach (var (bit, excl) in new (Func<AiSector, bool>, Func<AiSystem, bool>)[] { (x => x.RoleFighterShort, r => r.CalmWithoutRole || r.Unrest),
            (x => x.KeySystem, r => r.Unrest), (x => x.FighterShort, r => r.Unrest) })
            d ??= SectorThenSystem(() => [m.SectorDistanceList(_manager, bit, RankOrder.Descending)], x => [m.SystemList(x, r => r.FighterShort && r.Own && !excl(r), r => r.FighterBalance, RankOrder.Descending)]);
        return d;
    }

    /// <summary>
    /// State 7: source (a) sectors with a usable-fighter surplus / own systems that are no base by a rank value, (b) sectors with own
    /// fighters that hold no key system by a rank value / systems with own fighters that are no key system and no base by a rank value
    /// (dir Descending), (c) sectors with own fighters / systems with own fighters that are no base; one
    /// squadron gives Move to a fleet destination (held with room, free with room, the operation fallback) else Scrap.
    /// </summary>
    static Command? OverBudget(WorldModel m, Owner side)
    {
        var source = SectorThenSystem(() => [m.SectorList(x => x.HasUsableFighterSurplus, x => x.UsableFighterSurplus, RankOrder.Ascending)], x => [m.SystemList(x, r => r.Own && !r.Base, r => r.UsableFighterBalance, RankOrder.Ascending)])
            ?? SectorThenSystem(() => [m.SectorList(x => x.OwnFighters && !x.KeySystem, x => x.Fighters, RankOrder.Descending)], x => [m.SystemList(x, r => r.OwnFighters && !r.KeySystem && !r.Base, r => r.Fighters, RankOrder.Descending)])
            ?? SectorThenSystem(() => [m.SectorList(x => x.OwnFighters, x => x.Fighters, RankOrder.Descending)], x => [m.SystemList(x, r => r.OwnFighters && !r.Base, r => r.Fighters, RankOrder.Descending)]);
        if (source is null || Fighter(m, source.System) is not { } squadron) return null;
        var dest = (GameObject?)(FleetDestination(m, source.System, HeldWithRoom) ?? FleetDestination(m, source.System, FreeWithRoom))?.Fleet
            ?? OperationFallback(m, source.System);
        return dest is not null ? new Command(CommandType.Move, side) { Targets = [(squadron, GroupMode.Object)], ArgRef = dest } : Scrap(side, [squadron]);
    }
}
