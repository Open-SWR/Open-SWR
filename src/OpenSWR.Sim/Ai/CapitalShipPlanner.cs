using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>Planner 11: capital ships (capital-production phase), facility phase (shipyards); moves capital-move phase are round 7. p = 30.</summary>
public sealed class CapitalShipPlanner() : ProductionPlanner(PlannerType.CapitalShip)
{
    /// <summary>Capital-move phase states: capital-ship moves.</summary>
    enum CapitalMoveState
    {
        Start,
        /// <summary>Damaged ships to a yard.</summary>
        Damaged,
        /// <summary>Fleet move.</summary>
        FleetMove,
        /// <summary>Troop delivery to a fleet-op base (default step).</summary>
        TroopDelivery,
    }

    /// <summary>Capital-production phase states: capital-ship production.</summary>
    enum CapitalState
    {
        /// <summary>Starts at the site state (Inferred).</summary>
        Start,
        /// <summary>Site (R).</summary>
        Site,
        /// <summary>Over-budget test.</summary>
        BudgetTest,
        /// <summary>Class: the filter and criterion of the pick.</summary>
        Class,
        /// <summary>Pick a capital ship.</summary>
        Pick,
        /// <summary>Produce the picked ship (no SetProductionTarget).</summary>
        Produce,
        /// <summary>Over budget: scrap a capital ship (default step).</summary>
        OverBudget,
    }

    protected override Clone K { get; } = new(SystemRole.Shipyard, ManufacturingKind.Ship, ShipyardsOf, ProductFilter.Shipyard, TurnSlot.CapitalShip,
        x => x.ShipyardSlot, x => x.Shipyards, r => r.ShipyardRole || r.Unrest, ShipyardsOf, FreeSlotsOf, 30, 10,
        r => r.ConstructionMaintenance);
    ManufacturingManager? _manager;
    ProductFilter _filter;
    Criterion _criterion;
    uint _product;
    /// <summary>The ship class of the current pick.</summary>
    ShipClass _failure;
    /// <summary>The ship classes whose pick found nothing affordable.</summary>
    readonly HashSet<ShipClass> _failed = [];
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. It is the one product
    /// field of both the unit order and the facility-template order, so both of ours are reset.
    /// </summary>
    internal override void AfterLoad() => (_product, _fProduct) = (0, 0);

    /// <summary>Inferred: the production and facility phases end back in the capital-move phase (it runs once per cycle).</summary>
    protected override void Exit() => (Phase, State, Done) = ((int)PlannerPhase.CapitalMove, (int)CapitalMoveState.Start, true);
    int Budget(WorldModel m) => m.UnitBudget(30, 90, m.ShipMaintenance);
    /// <summary>Every end of the class / pick / order path: facility phase at N, with done.</summary>
    void EndProduction() => (Phase, State, Done) = ((int)PlannerPhase.Facility, (int)TemplateStep.NewSystem, true);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { (Phase, State, Done) = ((int)PlannerPhase.Start, (int)TemplateStep.Start, true); return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (CurrentPhase is not (PlannerPhase.Facility or PlannerPhase.CapitalMove or PlannerPhase.CapitalProduction)) { (Phase, State) = ((int)PlannerPhase.CapitalMove, (int)CapitalMoveState.Start); return null; }
        if (CurrentPhase == PlannerPhase.Facility) return Facility(m, side);
        if (CurrentPhase == PlannerPhase.CapitalMove) return Moves(game, m, side);
        switch ((CapitalState)State)
        {
            case CapitalState.Start: // Inferred: the production phase starts at its site state
                State = (int)CapitalState.Site;
                return null;
            case CapitalState.Site: // site (R)
                if (Recheck(m) is { } r) (_manager, State) = (ManagerOf(r.System, ManufacturingKind.Ship), (int)CapitalState.Class);
                else State = (int)CapitalState.BudgetTest;
                return null;
            case CapitalState.BudgetTest: // over-budget test
                if (Budget(m) < 0) State = (int)CapitalState.OverBudget;
                else EnterFacility();
                return null;
            case CapitalState.Class: // class
                Class(m, side); // always gives Pick (no class: mask and criterion zero)
                State = (int)CapitalState.Pick;
                return null;
            case CapitalState.Pick: // pick
            {
                _failed.Clear();
                int b = Budget(m);
                if (b < 0) { State = (int)CapitalState.OverBudget; return null; }
                if (b == 0) { EndProduction(); return null; }
                var stack = new ListStack();
                stack.Push(new RankedList(Research.Buildable(game, side, Families.Shipyard).Select(r => m.Catalogue[r.Common.Ref])
                    .Where(p => p is not null && p.Is(_filter)).Select(p => ((object)p!, p!.Value(_criterion))), RankOrder.Ascending, m.Rand));
                while (stack.Pick() is Product p)
                    if (p.MaintenanceCost <= b) { (_product, State) = (p.Type, (int)CapitalState.Produce); return null; }
                if (_failure != ShipClass.None) _failed.Add(_failure);
                EndProduction();
                return null;
            }
            case CapitalState.Produce: // no SetProductionTarget (the ship goes to the manager's TargetKey)
                EndProduction();
                return _manager is null ? null : new Command(CommandType.ProduceShip, side) { Targets = [(_manager, GroupMode.Object)], Fields = [_product, 1] };
            default: // CapitalState.OverBudget: the own, created fleet without Enroute / unfinished ships with the
                // smallest capital-ship maintenance, then its ship by a rank value among own free ships that are neither Enroute nor not Completed (ship rank value and
                // the free mark Inferred, round 7: read 0 / never set) gives Scrap of a capital ship; each list on its own stack
            {
                EndProduction();
                if (PickOne(m.FleetList(f => OwnFree(f) && !f.NotCompletedShip && !f.Enroute, f => f.Maintenance, RankOrder.Descending)) is not AiFleet f
                    || PickOne(m.ShipList(f, s => OwnFreeShip(s) && !s.NotCompleted && !s.Enroute, s => s.Maintenance, RankOrder.Descending)) is not AiShip ship) return null;
                return Family.IsCapitalShip(ship.Ship.Type) ? Scrap(side, [ship.Ship]) : null;
            }
        }
    }

    // ---- capital-move phase ----

    Command? Moves(GameState game, WorldModel m, Owner side)
    {
        switch ((CapitalMoveState)State)
        {
            case CapitalMoveState.Start:
                State = (int)CapitalMoveState.Damaged;
                return null;
            case CapitalMoveState.Damaged: // damaged ships
            {
                State = (int)CapitalMoveState.FleetMove;
                var c = DamagedShips(m, side);
                if (c is not null) { Priority++; Done = true; }
                return c;
            }
            case CapitalMoveState.FleetMove: // fleet move
            {
                State = (int)CapitalMoveState.TroopDelivery;
                var c = FleetMove(m, side);
                if (c is not null) { Priority++; Done = true; }
                return c;
            }
            default: // CapitalMoveState.TroopDelivery: troop delivery to a fleet-op base
            {
                (Phase, State, Done) = ((int)PlannerPhase.CapitalProduction, (int)CapitalState.Start, true);
                return TroopDelivery(m, side); // no priority +1
            }
        }
    }

    /// <summary>
    /// (only with an assault or defense base): theater sectors by a helper (dir Descending), popped; the fleet op keyed
    /// by sector with a troop need and a base without Blockade is the destination;
    /// source: fleets own + free (excl Enroute) by usable troops (dir Ascending), per fleet its own Completed ships (excl Enroute) all
    /// popped, their usable troops; the first fleet that gives troops gives Move gives the base. The second-op test has no effect (Inferred).
    /// </summary>
    static Command? TroopDelivery(WorldModel m, Owner side)
    {
        if (!(m.AssaultBase || m.DefenseBase)) return null;
        StarSystem? dest = null;
        var sectors = new ListStack();
        sectors.Push(m.SectorDefaultList(x => x.TheaterMark, RankOrder.Descending));
        while (dest is null && sectors.Pick() is AiSector s)
            if (m.FleetOps.GetValueOrDefault(s.FleetOpKey) is { } op && FleetOps.TroopNeed(m, op) > 0 && m.Record(op.Base) is { } b && !b.Blockade) dest = b.System;
        if (dest is null) return null;
        var fleets = new ListStack();
        fleets.Push(m.FleetList(f => OwnFree(f) && !f.Enroute, f => f.UsableTroops, RankOrder.Ascending));
        while (fleets.Pick() is AiFleet f)
        {
            var troops = m.TroopsAboard(f, s => OwnFreeShip(s) && !s.Enroute);
            if (troops.Count > 0) return new Command(CommandType.Move, side) { Targets = [.. troops.Select(x => (x, GroupMode.Object))], ArgRef = dest };
        }
        return null;
    }

    /// <summary>
    /// (Inferred: damaged ships to a yard): fleets (own, created, damaged-mobile; excl Enroute / Enroute ship) by ship
    /// count, popped until one at an enemy system, else again until one at a system without the shipyard role; its damaged-moving ships
    /// (not damaged-stopped nor Enroute) gives Move to the nearest yard (unblockaded sectors with a shipyard role, shipyard-role systems), else own.
    /// </summary>
    static Command? DamagedShips(WorldModel m, Owner side)
    {
        AiFleet? fleet = null;
        foreach (Func<AiSystem, bool> test in new Func<AiSystem, bool>[] { r => r.Enemy, r => !r.ShipyardRole })
        {
            var stack = new ListStack();
            stack.Push(m.FleetList(f => OwnFree(f) && f.DamagedMoving && !f.EnrouteShip && !f.Enroute, f => f.Ships, RankOrder.Ascending));
            while (fleet is null && stack.Pick() is AiFleet f)
                if (m.Record(f.Location) is { } r && test(r)) fleet = f;
            if (fleet is not null) break;
        }
        if (fleet is null) return null;
        var ships = m.ShipsOf(fleet).Where(s => s.DamagedMoving && !(s.DamagedStopped || s.Enroute))
            .Select(s => s.Ship).ToList();
        if (ships.Count == 0) return null;
        var dest = SectorThenSystem(() => [m.SectorDistanceList(fleet.Fleet, x => x.OwnUnblockaded && x.ShipyardRole, RankOrder.Descending)], x => [m.SystemDistanceList(x, fleet.Fleet, r => r.ShipyardRole && !r.Blockade, RankOrder.Descending)])
            ?? SectorThenSystem(() => [m.SectorDistanceList(fleet.Fleet, x => x.OwnUnblockaded && x.Own, RankOrder.Descending)], x => [m.SystemDistanceList(x, fleet.Fleet, r => r.Own && !r.Blockade, RankOrder.Descending)]);
        return dest is null ? null : new Command(CommandType.Move, side) { Targets = [.. ships.Select(s => (s, GroupMode.Object))], ArgRef = dest.System };
    }

    /// <summary>
    /// Own free fleets (excl Enroute / damaged / Enroute ships) fewest ships first, popped until a command:
    /// a fleet with a not-Completed ship splits its ready ships off with a CreateFleet command to its location, else is skipped
    /// else a Death Star fleet gives own Coruscant;
    /// else, from a location that is not an own claimed base: the nearest claimed own base in its sector, an own key system there
    /// (key sectors), then over claimed bases, then over own systems gives Move [fleet].
    /// </summary>
    static Command? FleetMove(WorldModel m, Owner side)
    {
        var stack = new ListStack();
        stack.Push(m.FleetList(f => OwnFree(f) && !f.DamagedStopped && !f.DamagedMoving && !f.EnrouteShip && !f.Enroute, f => f.Ships, RankOrder.Descending));
        while (stack.Pick() is AiFleet f)
        {
            if (f.NotCompletedShip) // a not-Completed ship: only the split branch, never the move
            {
                if (!f.ReadyShip) continue;
                var ships = new ListStack();
                ships.Push(m.ShipList(f, s => s.Own && !s.DamagedStopped && !s.NotCompleted && !s.DamagedMoving && !s.Enroute, s => s.Hull, RankOrder.Ascending));
                var ready = new List<GameObject>();
                while (ships.Pick() is AiShip s) ready.Add(s.Ship);
                if (ready.Count > 0 && f.Location is not null) return new Command(CommandType.CreateFleet, side) { Targets = [.. ready.Select(s => (s, GroupMode.Object))], ArgRef = f.Location };
                continue;
            }
            if (m.Record(f.Location) is not { } r || r.Sector is not { } sector) continue;
            var cor = m.Systems.Values.FirstOrDefault(x => x.System.Id == Refs.CoruscantIndex);
            AiSystem? dest = null;
            if (f.DeathStar && cor is not null && cor.Own && f.Location != cor.System) dest = cor;
            else if (!r.Own || !r.Base)
            {
                dest = PickOne(m.SystemDistanceList(sector, f.Fleet, r => r.Own && r.Base && !r.Blockade, RankOrder.Descending)) as AiSystem;
                if (dest is null && sector.KeySystem) dest = PickOne(m.SystemDistanceList(sector, f.Fleet, r => r.KeySystem && r.Own && !r.Blockade, RankOrder.Descending)) as AiSystem;
                dest ??= MissionOps.Nearest(m, sector, r => r.Own && r.Base && !r.Blockade);
                dest ??= MissionOps.Nearest(m, sector, r => r.Own && !r.Blockade);
            }
            if (dest is not null) return new Command(CommandType.Move, side) { Targets = [(f.Fleet, GroupMode.Object)], ArgRef = dest.System };
        }
        return null;
    }

    /// <summary>
    /// Class: the filter and criterion := none; per fleet op (key order) count its shortfalls (firepower, anti-fighters, troop capacity,
    /// fighter capacity, bombardment, interdictors, the last only with the Death Star shield available), each unless its ship class
    /// failed; the first request class that applies wins. None: the no-request chain, each step unless its class failed: troop carrier when the model's first value is below the second;
    /// the Death Star (Empire) when the shipyard-role list holds at least 2 systems and no own fleet holds a Death Star; armed when that value is below 1;
    /// an anti-fighter request when the free anti-fighter count is below 1; carrier when the first value is at most the second.
    /// </summary>
    /// <summary>The capital-ship classes planner 11 picks from.</summary>
    enum ShipClass { None, Armed, AntiFighter, TroopCarrier, FighterCarrier, Bombarder, DeathStar, Interdictor }

    bool Class(WorldModel m, Owner side)
    {
        bool Open(ShipClass failure) => !_failed.Contains(failure);
        int k1 = 0, k80 = 0, k4 = 0, k8 = 0, k10 = 0, k800 = 0;
        foreach (var op in m.FleetOps.Values)
        {
            var b = op;
            if (b.ShortFirepower && Open(ShipClass.Armed)) k1++;
            if (b.ShortAntiFighters && Open(ShipClass.AntiFighter)) k80++;
            if (b.ShortTroopCapacity && Open(ShipClass.TroopCarrier)) k4++;
            if (b.ShortFighterCapacity && Open(ShipClass.FighterCarrier)) k8++;
            if (b.ShortBombardment && Open(ShipClass.Bombarder)) k10++;
            if (b.ShortInterdictors && Open(ShipClass.Interdictor) && m.DeathStarShieldAvailable) k800++;
        }
        (ProductFilter, Criterion, ShipClass)? byTroopCapacity = (ProductFilter.TroopCarrierShip, Criterion.TroopCapacity, ShipClass.TroopCarrier),
            byStrengthPerMaintenance = (ProductFilter.ArmedShip, Criterion.StrengthPerMaintenance, ShipClass.Armed),
            byAntiFighterFirepower = (ProductFilter.AntiFighterShip, Criterion.AntiFighterFirepower, ShipClass.AntiFighter),
            byFighterCapacity = (ProductFilter.FighterCarrierShip, Criterion.FighterCapacity, ShipClass.FighterCarrier);
        var c = k4 >= 1 ? byTroopCapacity
            : k1 >= 1 && k1 >= k80 && k1 >= k4 && k1 >= k10 && k1 >= k8 && k1 >= k800 ? byStrengthPerMaintenance
            : k80 >= 1 && k80 >= k4 && k80 >= k10 && k80 >= k8 && k80 >= k800 ? byAntiFighterFirepower
            : k10 >= 1 && k10 >= k8 && k10 >= k800 ? (ProductFilter.BombarderShip, Criterion.Bombardment, ShipClass.Bombarder)
            : k8 >= 1 && k8 >= k800 ? byFighterCapacity
            : k800 >= 1 ? (ProductFilter.InterdictorShip, Criterion.Shields, ShipClass.Interdictor)
            : Open(ShipClass.TroopCarrier) && m.CarrierTroopCapacity < m.MaxEnemyTroopsToTake ? byTroopCapacity
            : side == Owner.Empire && List.Count >= 2 && !m.OwnDeathStar && Open(ShipClass.DeathStar) ? (ProductFilter.DeathStar, Criterion.Firepower, ShipClass.DeathStar)
            : Open(ShipClass.Armed) && m.FreeArmed < 1 ? byStrengthPerMaintenance
            : Open(ShipClass.AntiFighter) && m.FreeAntiFighter < 1 ? byAntiFighterFirepower
            : Open(ShipClass.FighterCarrier) && m.FreeFighterCapacity <= m.BestFighterCapacity ? byFighterCapacity
            : null;
        if (c is not { } x) { (_filter, _criterion, _failure) = (ProductFilter.None, Criterion.None, ShipClass.None); return false; }
        (_filter, _criterion, _failure) = x;
        return true;
    }
}
