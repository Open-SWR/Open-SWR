namespace OpenSWR.Sim.Ai;

// Production planners 2, 3, 5, 6, 7, 10, 11. State numbers follow the original's order within each phase; the facility
// phase 0x3EC is the shared template (ProductionPlanner.Facility). Unless a step says so, " -> X" with a command = the command
// ends this run and the planner continues at X on its next step.

/// <summary>Planner 2: Mines and Refineries with their own construction role 0x1000000. p = 3.</summary>
public sealed class MinePlanner() : ProductionPlanner(2)
{
    protected override Clone K { get; } = new(SystemRole.Mine, ManufacturingKind.Construction, YardsOf, ProductFilter.ConstructionYard, 1,
        x => x.ConstructionSlot, x => x.ConstructionYards, r => r.AnyConstructionRole || r.Unrest, YardsOf, FreeSlotsOf, 3, 100,
        r => r.ConstructionMaintenance, BothSectorLists: true);
    const int Check = 1, CheckScrap = 2, FacilityCheck = 50, FacilityCheckScrap = 51;
    ManufacturingManager? _manager;
    StarSystem? _mine, _refinery, _scrap;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (Phase == 0)
        {
            if (Recheck(m) is { } r)
                (_manager, Phase, State) = (ManagerOf(r.System, ManufacturingKind.Construction), m.Refineries <= m.Mines ? 0x3EB : 0x3EA, Check);
            else (Phase, State) = (FacilityPhase, FacilityCheck);
            return null;
        }
        if (Phase == FacilityPhase)
            return State switch
            {
                FacilityCheck => MaintenanceCheck(m, FacilityCheckScrap, FN),
                FacilityCheckScrap => ScrapAll(m, side, FN),
                _ => Facility(m, side),
            };
        bool refinery = Phase == 0x3EB;
        switch (State)
        {
            case Check: return MaintenanceCheck(m, CheckScrap, 3);
            case CheckScrap: return ScrapAll(m, side, 3);
            case 3: // Mine / Refinery site
            {
                AiSystem? site;
                // keep the previous site while calm and still a Mine / Refinery site, no owner test
                if (m.Record(refinery ? _refinery : _mine) is { } kept && (refinery ? kept.RefinerySite : kept.MineSite) && Calm(kept)) site = kept;
                else site = Near(m, x => refinery ? x.RefinerySite : x.MineSite, r => refinery ? r.RefinerySite : r.MineSite)
                    ?? Near(m, x => refinery ? x.HasFreeEnergy : x.HasFreeRawMaterial, r => refinery ? r.HasFreeEnergy : r.HasFreeRawMaterial);
                if (site is null) { Finish(); return null; }
                if (refinery) _refinery = site.System;
                else _mine = site.System;
                State = 4;
                return null;
            }
            case 4: // the site's Usable producers without their role
            {
                State = 8;
                var at = refinery ? _refinery! : _mine!;
                var r = m.Systems[at];
                var take = new List<GameObject>();
                foreach (var k in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction })
                    if (!HasRoleFor(r, k)) take.AddRange(Usable(m, at, FamilyOf(k)));
                return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
            }
            case 8:
                if (_manager is null) { Finish(); return null; }
                State = 9;
                return Urgent(new Command(0x214, side) { Targets = [(_manager, 0)], ArgRef = refinery ? _refinery : _mine });
            default: // 9
                Finish();
                return new Command(0x210, side) { Targets = [(_manager!, 0)], Fields = [refinery ? 0x2D000002u : 0x2C000001u, 1] };
        }
    }

    /// <summary>The nearest sector to the construction manager that passes the sector test, in it by distance the calm system that passes the system test.</summary>
    AiSystem? Near(WorldModel m, Func<AiSector, bool> sectorBit, Func<AiSystem, bool> systemBit) => _manager is null ? null
        : SectorThenSystem(() => [m.SectorDistanceList(_manager, sectorBit, 2)], x => [m.SystemDistanceList(x, _manager, r => systemBit(r) && !r.Unrest, 2)]);

    /// <summary>Only when cap &lt; dem, the role-less own system to clear (sectors with a free slot for every producer kind, systems by producers).</summary>
    Command? MaintenanceCheck(WorldModel m, int found, int next)
    {
        _scrap = m.MaintenanceCapacity < m.MaintenanceDemand ? SectorThenSystem(
            () => [m.SectorList(x => x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot, x => x.ConstructionYards, 1), m.SectorList(x => x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot, x => x.Shipyards, 1), m.SectorList(x => x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot, x => x.TrainingFacilities, 1)],
            x => [m.SystemList(x, r => r.Own && !r.HasRole, r => r.ConstructionYards, 1), m.SystemList(x, r => r.Own && !r.HasRole, r => r.Shipyards, 1), m.SystemList(x, r => r.Own && !r.HasRole, r => r.TrainingFacilities, 1)])?.System : null;
        State = _scrap is null ? next : found;
        return null;
    }

    /// <summary>0x200 of every Usable shipyard, training facility and construction yard of that system.</summary>
    Command? ScrapAll(WorldModel m, Owner side, int next)
    {
        State = next;
        var take = Usable(m, _scrap!, 0x28, 0x29, 0x2A);
        return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
    }
}

/// <summary>Planner 3: defences (surplus 0x3ED, shortfall 0x3EE, facility 0x3EC), role 0x2000000. p = 9.</summary>
public sealed class DefencePlanner() : ProductionPlanner(3)
{
    protected override Clone K { get; } = new(SystemRole.Defence, ManufacturingKind.Construction, YardsOf, ProductFilter.ConstructionYard, 7,
        x => x.ConstructionSlot, x => x.ConstructionYards, r => r.AnyConstructionRole || r.Unrest, YardsOf, FreeSlotsOf, 9, 10,
        r => r.ConstructionMaintenance);
    StarSystem? _site; // The construction manager is Builder, and product and count are the template's.
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. The template product is planner 3's product.
    /// </summary>
    internal override void AfterLoad() => _fProduct = 0;

    protected override void Exit() => (Phase, State, Done) = (0x3ED, 1, true);
    int UnitBudget(WorldModel m) => m.UnitBudget(9, 90, m.DefenceMaintenance);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (Phase == 0) (Phase, State) = (0x3ED, 1); // a new planner starts in 0x3ED
        if (Phase == FacilityPhase) return Facility(m, side);
        if (Phase == 0x3ED)
        {
            if (State == 1)
            {
                if (Surplus(m) is { } s) (_site, State) = (s.System, 2);
                else (Phase, State) = (0x3EE, 4);
                return null;
            }
            // 2: GenCores, then KDY cannons and LNR batteries above their targets; the scrap ends the run without
            // priority (run 1: planner 6 decides on the next tick)
            (Phase, State) = (0x3EE, 4);
            var take = ExcessDefences(m, m.Systems[_site!]);
            if (take.Count == 0) return null;
            Done = true;
            return Scrap(side, take);
        }
        switch (State)
        {
            case 4: // R
                if (Recheck(m) is { } r) (Builder, State) = (ManagerOf(r.System, ManufacturingKind.Construction), 1);
                else EnterFacility();
                return null;
            case 1: // shortfall site
            {
                var site = Shortfall(m);
                if (site is not null && site.DefenceBalance < 0) { (_site, State) = (site.System, 6); return null; }
                if (site is null && UnitBudget(m) > 0 && m.Constant(ModelConstant.GenCores) < 2) m.RaiseConstant(ModelConstant.GenCores);
                State = 9;
                return null;
            }
            case 6: // site scrap; Inferred: priority +1 like the F scraps
            {
                State = 5;
                var take = SiteScrap(m, _site!);
                return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
            }
            case 5: // count := 0
            {
                _fCount = 0;
                int b = UnitBudget(m);
                if (b < 0) { State = 7; return null; }
                var rec = m.Systems[_site!];
                State = 9;
                if (b == 0 || rec.FreeEnergy <= 0) return null;
                var (mask, criterion) = _site!.Id == 0x109 && rec.DeathStarShieldWanted ? (ProductFilter.DeathStarShield, Criterion.Shields)
                    : rec.GenCoreShort ? (ProductFilter.GenCore, Criterion.Shields) : (ProductFilter.Battery, Criterion.DefenceAttack);
                if (m.PickProduct(0x2A, mask, criterion, 1) is ({ } product, var cost) && cost <= b) (_fProduct, _fCount, State) = (product.Type, 1, 0xB);
                return null;
            }
            case 7: // over budget
            {
                var s = Surplus(m) ?? OverBudgetSite(m, x => x.DefendedNonKey && x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot)
                    ?? OverBudgetSite(m, x => x.DefendedNonKey);
                var x = s is null ? null : Usable(m, s.System, 0x22).Concat(Usable(m, s.System, 0x23)).Concat(Usable(m, s.System, 0x24)).FirstOrDefault();
                if (x is null) { State = 9; return null; }
                Exit(); // Inferred: the run ends with the scrap
                return Scrap(side, [x]);
            }
            case 9: // Mine / Refinery site, falls through into 10
            {
                bool refinery = m.Refineries < m.Mines;
                _fProduct = refinery ? 0x2D000002u : 0x2C000001u;
                var site = OwnSite(m, Builder, refinery);
                _site = site?.System;
                if (site is null) (Phase, State) = (FacilityPhase, F214); // phase 0x3EC state 0xB: 0x214 with Build, then 0x210 with the count
                else _fCount = 1;
                goto case 10;
            }
            case 10: // K-type scrap at the site (only without a role)
            {
                if (Phase != FacilityPhase) State = 0xB;
                if (_site is null || m.Systems[_site].HasRole) return null;
                var take = Usable(m, _site, 0x28, 0x29, 0x2A);
                return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
            }
            case 0xB:
                if (Builder is null) { Exit(); return null; }
                State = 0xC;
                return Urgent(new Command(0x214, side) { Targets = [(Builder, 0)], ArgRef = _site });
            default: // 0xC
                Exit();
                return new Command(0x210, side) { Targets = [(Builder!, 0)], Fields = [_fProduct, (uint)_fCount] };
        }
    }

    /// <summary>
    /// Planner 3's own Mine / Refinery site: sectors by distance to the construction manager (Mine / Refinery site),
    /// then systems by distance (Mine site; Refinery site and own; both without a role or unrest), dir 2, no fallback. Inferred: no
    /// manager -> none.
    /// </summary>
    static AiSystem? OwnSite(WorldModel m, GameObject? manager, bool refinery) => manager is null ? null : SectorThenSystem(
        () => [m.SectorDistanceList(manager, x => refinery ? x.RefinerySite : x.MineSite, 2)],
        x => [m.SystemDistanceList(x, manager, r => (refinery ? r.RefinerySite && r.Own : r.MineSite) && !r.HasRole && !r.Unrest, 2)]);

    /// <summary>
    /// Site scrap: producers without their role (the shipyard, training and construction roles); nothing and no free energy: the first Refinery (own Mines &lt; Refineries or no Mine), else the first Mine (own
    /// Refineries <= Mines or no Refinery), else the first shipyard, training facility or construction yard. Inferred: each "else"
    /// follows from the previous condition being false.
    /// </summary>
    static List<GameObject> SiteScrap(WorldModel m, StarSystem s)
    {
        var r = m.Systems[s];
        var take = new List<GameObject>();
        if (!r.ShipyardRole) take.AddRange(Usable(m, s, 0x28));
        if (!r.TrainingRole) take.AddRange(Usable(m, s, 0x29));
        if (!r.AnyConstructionRole) take.AddRange(Usable(m, s, 0x2A));
        if (take.Count > 0 || r.FreeEnergy != 0) return take;
        var one = m.Mines < m.Refineries || r.Mines == 0 ? Usable(m, s, 0x2D).FirstOrDefault()
            : m.Refineries <= m.Mines || r.Refineries == 0 ? Usable(m, s, 0x2C).FirstOrDefault()
            : Usable(m, s, 0x28).Concat(Usable(m, s, 0x29)).Concat(Usable(m, s, 0x2A)).FirstOrDefault();
        return one is null ? take : [one];
    }

    /// <summary>Surplus site: sectors with a defence surplus and own systems, ranked by their counts (dir 1), when the count is above 0.</summary>
    static AiSystem? Surplus(WorldModel m) =>
        SectorThenSystem(() => [m.SectorList(x => x.DefenceSurplus, x => x.DefenceSurplusSystems, 1)], x => [m.SystemList(x, r => r.Own, r => r.DefenceBalance, 1)]) is { } s && s.DefenceBalance > 0 ? s : null;

    /// <summary>Over-budget site: sectors that pass, then own systems excluding roles and Coruscant, ranked by the fields (Inferred: dir 1).</summary>
    static AiSystem? OverBudgetSite(WorldModel m, Func<AiSector, bool> pass) =>
        SectorThenSystem(() => [m.SectorList(pass, x => x.OwnSystems, 1)],
            x => [m.SystemList(x, r => r.Own && !r.HasRole && !r.Coruscant, r => r.GenCores, 1),
                m.SystemList(x, r => r.Own && !r.HasRole && !r.Coruscant, r => r.Batteries, 1)]);

    List<GameObject> ExcessDefences(WorldModel m, AiSystem r)
    {
        int gencores = r.GenCores - r.GenCoreTarget, batteries = r.Batteries - r.BatteryTarget;
        var s = r.System;
        return [.. Usable(m, s, 0x24).Take(Math.Max(0, gencores)), .. Usable(m, s, 0x22).Concat(Usable(m, s, 0x23)).Take(Math.Max(0, batteries))];
    }

    /// <summary>Shortfall site, first match of: Coruscant (own, not blockaded, shield wanted or GenCores below the role-system GenCore target); the sectors with a role-system defence shortfall ranked by a count (dir 1, pushed first) and by distance (dir 2), its own system with a defence shortfall (dir 2); Coruscant with a count below 0; sectors with a defence shortfall by distance, systems with one.</summary>
    AiSystem? Shortfall(WorldModel m)
    {
        var cor = m.Systems.Values.FirstOrDefault(r => r.System.Id == 0x109);
        bool corOk = cor is not null && Own(cor) && !cor.Blockade;
        if (corOk && (cor!.DeathStarShieldWanted || cor.GenCores < m.Constant(ModelConstant.RoleGenCores))) return cor;
        if (Builder is null) return corOk && cor!.DefenceBalance < 0 ? cor : null;
        return SectorThenSystem(() => [m.SectorList(x => x.RoleDefenceShort, x => x.DefenceShortSystems, 1), m.SectorDistanceList(Builder, x => x.RoleDefenceShort, 2)], // list pushed first
                x => [m.SystemList(x, r => r.DefenceShort && r.Own && !r.Unrest && !r.CalmWithoutRole, r => r.DefenceBalance, 2)])
            ?? (corOk && cor!.DefenceBalance < 0 ? cor : null)
            ?? SectorThenSystem(() => [m.SectorDistanceList(Builder, x => x.DefenceShort, 2)], x => [m.SystemList(x, r => r.DefenceShort && !r.Unrest, r => r.DefenceBalance, 2)]);
    }
}

/// <summary>
/// Planner 5: special forces (0x3F4), character order 0x242, facility 0x3EC (training). p = 4.
/// A phase other than 0x3EC / 0x3F4 -> 0x3F4 (empty call); 0x3F4 at state 0 -> 9 (empty call);
/// every end of 0x3F4 -> 0x3EC, state 0, done; every end of 0x3EC -> 0x3F4.
/// </summary>
public sealed class SpecialForcePlanner() : ProductionPlanner(5)
{
    protected override Clone K { get; } = new(SystemRole.Training, ManufacturingKind.Training, TrainingOf, ProductFilter.TrainingFacility, 5,
        x => x.TrainingSlot, x => x.TrainingFacilities, r => r.TrainingRole || r.Unrest, TrainingOf, FreeSlotsOf, 4, 30,
        r => r.TrainingMaintenance, TurnByDistance: false);
    ManufacturingManager? _manager;
    int _cycle, _want, _count;
    ProductFilter _filter;
    uint _product;

    protected override void Exit() => (Phase, State, Done) = (0x3F4, 0, true);
    void EndUnit() => (Phase, State, Done) = (FacilityPhase, 0, true);
    /// <summary>"3 N -> 9, else 7; 7 facility budget &lt; 0 -> 8 H, else phase 0x3F4 state 0 without done" (the 0x3F4 machine steps
    /// in the next frame of the same run).</summary>
    protected override Command? NewSystemFailed(WorldModel m, Owner side)
    {
        if (FacilityBudget(m) < 0) { State = FScrapOne; return null; }
        (Phase, State) = (0x3F4, 0);
        return null;
    }

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        int Budget() => m.UnitBudget(4, 70, m.SpecialForceMaintenance);
        if (Phase is not (FacilityPhase or 0x3F4)) { Phase = 0x3F4; return null; }
        if (Phase == FacilityPhase) return Facility(m, controller.Side);
        switch (State)
        {
            case 0:
                State = 9;
                return null;
            case 9: // training site (R)
                if (Recheck(m) is { } r) (_manager, State) = (ManagerOf(r.System, ManufacturingKind.Training), 1);
                else State = 6;
                return null;
            case 6:
                if (Budget() < 0) State = 2;
                else EndUnit();
                return null;
            case 1: // force cycle: the current type first; each examined type advances the cycle
                if (_cycle == 0) _cycle = 0x5A;
                _want = 0;
                for (int k = 0; k < 4 && _want == 0; k++)
                {
                    int t = _cycle - 0x5A;
                    _filter = t switch { 0 => ProductFilter.SpecialForceType1, 1 => ProductFilter.SpecialForceType2, 2 => ProductFilter.SpecialForceType3, _ => ProductFilter.SpecialForceType4 };
                    _want = m.MarkedSpecialForces(t) < Math.Max(m.MaxEnemyUnits, 8) ? 1 : 0;
                    _cycle = 0x5A + (t + 1) % 4;
                }
                State = _want == 1 ? 5 : 6;
                return null;
            case 5:
            {
                int b = Budget();
                if (b < 0) { State = 2; return null; }
                if (b == 0) { EndUnit(); return null; }
                // want reduced to b div cost when cost * want > b; want > 0 -> 0x212 even without a product (the old
                // product field stays); else exit. b > 0 never goes to the character order.
                if (m.PickProduct(0x29, _filter, Criterion.MaintenanceCost, 1) is ({ } product, var cost))
                {
                    _product = product.Type;
                    if (cost > 0 && cost * _want > b) _want = b / cost;
                }
                if (_want > 0) (_count, State) = (_want, 0xB);
                else EndUnit();
                return null;
            }
            case 0xB:
                EndUnit();
                return _manager is null ? null : new Command(0x212, controller.Side) { Targets = [(_manager, 0)], Fields = [_product, (uint)_count] };
            default: // 2: character order 0x242
            {
                EndUnit();
                var pick = PickOne(m.AgentList(a => a.Unassigned && a.Disbandable && !a.OnMission && !a.Enroute, a => a.Maintenance, 0, 1000, 1))
                    ?? PickOne(m.AgentList(a => a.Disbandable && !a.OnMission && !a.Enroute, a => a.Maintenance, 0, 1000, 1));
                return pick is AiAgent a && Family.IsSpecialForce(a.Agent.Type) ? new Command(0x242, controller.Side) { Targets = [(a.Agent, 0)] } : null;
            }
        }
    }
}

/// <summary>Planner 6: troops: moves 0x3EF, production 0x3F0, facility 0x3EC (training). p = 22.</summary>
public sealed class TroopPlanner() : ProductionPlanner(6)
{
    protected override Clone K { get; } = new(SystemRole.Training, ManufacturingKind.Training, TrainingOf, ProductFilter.TrainingFacility, 2,
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
    int _dir;
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. It is the one product
    /// field of both the unit order and the facility-template order, so both of ours are reset.
    /// </summary>
    internal override void AfterLoad() => (_product, _fProduct) = (0, 0);

    /// <summary>Inferred: runs start (and end) in the move phase 0x3EF step 1.</summary>
    protected override void Exit() => (Phase, State, Done) = (0x3EF, 1, true);
    int Budget(WorldModel m) => m.UnitBudget(22, 80, m.FleetTroopMaintenance + m.TroopMaintenance);
    /// <summary>Production exits after state 7: phase 0x3EC at N, with done.</summary>
    void EndProduction() => (Phase, State, Done) = (FacilityPhase, FN, true);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (Phase == 0) (Phase, State) = (0x3EF, 1);
        if (Phase == FacilityPhase) return Facility(m, side);
        if (Phase == 0x3EF)
        {
            var move = Move(m, side, State);
            // a move or step 4 (with or without a move) ends the run without priority
            if (move is not null || State >= 4) (Phase, State, Done) = (0x3F0, 5, true);
            else State++;
            return move;
        }
        switch (State)
        {
            case 5: // training site
                if (Recheck(m) is { } r) (_manager, _facilities, State) = (ManagerOf(r.System, ManufacturingKind.Training), r.TrainingFacilities, 6);
                else EnterFacility();
                return null;
            case 6:
                if (Target(m)) State = 7;
                else EnterFacility();
                return null;
            case 7:
            {
                int b = Budget(m);
                if (m.Dominant && b <= 0)
                    foreach (var (k, floor) in new[] { (ModelConstant.KeyGarrison, 9), (ModelConstant.RoleGarrison, 3), (ModelConstant.Garrison, 0) })
                        if (m.Constant(k) > floor) m.Constant(k)--;
                if (b < 0) { State = 8; return null; }
                // Deviation: the original stores product none when the pick finds nothing and still goes to the order on a
                // positive count; openswr ends here. Only reachable with mods (Inferred: the install data always has a level-0 product for this mask).
                if (b == 0 || m.PickProduct(0x29, _filter, _criterion, _dir) is not ({ } product, var cost)) { EndProduction(); return null; }
                _count = Math.Min(cost * _want > b && cost > 0 ? b / cost : _want, _facilities);
                if (_count <= 0) { EndProduction(); return null; }
                (_product, State) = (product.Type, 0xC);
                return null;
            }
            case 8: // over budget: a troop of the excess system
            {
                EndProduction();
                var s = SectorThenSystem(() => [m.SectorList(x => x.GarrisonSurplus, x => x.TroopSurplus, 2)], x => [m.SystemList(x, r => r.GarrisonSurplus && r.Own, r => r.TroopSurplus, 2)]);
                return s is not null && Troops(m, s.System, 0).FirstOrDefault() is { } t ? Scrap(side, [t]) : null;
            }
            case 0xC:
                if (_manager is null) { EndProduction(); return null; }
                State = 0xD;
                return Urgent(new Command(0x214, side) { Targets = [(_manager, 0)], ArgRef = _target });
            default: // 0xD
                EndProduction();
                return new Command(0x212, side) { Targets = [(_manager!, 0)], Fields = [_product, (uint)_count] };
        }
    }

    /// <summary>Usable troops of the system with the class mask (0 = any), weakest first by SD DefenseStrength, ties in key order.</summary>
    static IEnumerable<GameObject> Troops(WorldModel m, StarSystem s, ProductFilter filter) =>
        Usable(m, s, 0x10, 0x11, 0x12, 0x13).Where(t => filter == ProductFilter.None || m.Catalogue[t.Type]?.Is(filter) == true).OrderBy(t => m.Catalogue[t.Type]?.DefenseStrength ?? 0);

    /// <summary>
    /// Move steps 1-4: destination; need n (steps 1 / 2: the troop need
    /// of the first fleet op in key order whose base is the destination; none -> 0, no command; they run only with
    /// a defense / assault base); source sectors by distance to the destination, dir 1 (the farthest), source systems by their rank value (dir 2) (Inferred:
    /// own, where the table gives no filter); n := min(n, the source's rank value) Usable troops of the step's class (1 / 4: 0x2000, 2:
    /// 0x1000, 3: any) in key order (Inferred for the vtable minimum) -> 0x201. Source lists follow the table order (fallbacks
    /// own only, without Blockade or a base in every step; the system's garrison troop marks select the class).
    /// </summary>
    /// <summary>Move sources exclude blockaded systems and fleet-op bases of task types 4-6.</summary>
    static bool MoveExclude(AiSystem r) => r.Blockade || r.AssaultBase || r.DefenseBase;

    Command? Move(WorldModel m, Owner side, int step)
    {
        if (step == 1 && !m.DefenseBase || step == 2 && !m.AssaultBase) return null;
        var (dest, sector, sectorDir, system, systemExcl, fallbackSector, fallbackSystem) = step switch
        {
            1 => (SectorThenSystem(() => [m.SectorDefaultList(x => x.DefenseBase, 1)], x => [m.SystemList(x, r => r.Own && r.DefenseBase && !r.Blockade, r => r.Troops, 2)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus && x.DefenseTroopSurplus), 1,
                (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.DefenseTroops && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)(x => x.DefenseGarrisonSurplus && x.GarrisonSurplus),
                (Func<AiSystem, bool>?)(r => r.GarrisonSurplus && r.GenCoreShort && r.Own)),
            2 => (SectorThenSystem(() => [m.SectorDefaultList(x => x.AssaultBase, 1)], x => [m.SystemList(x, r => r.Own && r.AssaultBase && !r.Blockade, r => r.Troops, 2)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus && x.AttackTroopSurplus), 1,
                (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.AttackTroops && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)(x => x.AttackGarrisonSurplus && x.GarrisonSurplus),
                (Func<AiSystem, bool>?)(r => r.AttackGarrison && r.GarrisonSurplus && r.Own)),
            3 => (SectorThenSystem(() => [m.SectorList(x => x.OwnUprising && x.GarrisonShort, x => x.TroopShortfall, 2)], x => [m.SystemList(x, r => r.Uprising && r.GarrisonShort && r.Own && !r.Blockade, r => r.TroopShortfall, 2)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus), 1, (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)null, (Func<AiSystem, bool>?)null),
            _ => (SectorThenSystem(() => [m.SectorList(x => x.GarrisonShort, x => x.TroopShortfall, 2)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Blockade, r => r.TroopShortfall, 2)]),
                (Func<AiSector, bool>)(x => x.GarrisonSurplus && x.DefenseTroopSurplus), 1,
                (Func<AiSystem, bool>)(r => r.GarrisonSurplus && r.DefenseTroops && r.Own), (Func<AiSystem, bool>)MoveExclude,
                (Func<AiSector, bool>?)(x => x.DefenseGarrisonSurplus && x.GarrisonSurplus),
                (Func<AiSystem, bool>?)(r => r.DefenseGarrison && r.GarrisonSurplus && r.Own)),
        };
        if (dest is null) return null;
        int need = step <= 2 ? m.FleetOps.Values.FirstOrDefault(o => o.Base == dest.System) is { } op ? FleetOps.TroopNeed(m, op) : 0 : dest.TroopShortfall;
        if (need <= 0) return null;
        var source = SectorThenSystem(() => [m.SectorDistanceList(dest.System, sector, sectorDir)], x => [m.SystemList(x, r => system(r) && !systemExcl(r), r => r.TroopSurplus, 2)]);
        if (source is null && fallbackSector is not null)
            source = SectorThenSystem(() => [m.SectorDistanceList(dest.System, fallbackSector, sectorDir)], x => [m.SystemList(x, r => fallbackSystem!(r) && !systemExcl(r), r => r.TroopSurplus, 2)]);
        if (source is null) return null;
        var troops = Troops(m, source.System, step switch { 2 => ProductFilter.AttackTroop, 3 => ProductFilter.None, _ => ProductFilter.DefenseTroop }).Take(Math.Min(need, source.TroopSurplus)).ToList();
        return troops.Count == 0 ? null : new Command(0x201, side) { Targets = [.. troops.Select(t => (t, 0))], ArgRef = dest.System };
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
    /// Target: the selectors in the rotating order (1 -> 2 -> 3 -> 4 -> 1), each only while no target is set.
    /// Every call clears the four failures and zeroes want / filter / criterion / direction first.
    /// </summary>
    bool Target(WorldModel m)
    {
        _target = null;
        _failed.Clear();
        (_want, _filter, _criterion, _dir) = (0, 0, 0, 0);
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
            TroopTarget.GarrisonTroops => SectorThenSystem(() => [m.SectorDistanceList(_manager, x => x.GarrisonTroopsShort, 2)], x => [m.SystemList(x, r => r.GarrisonTroopsShort && r.Own && !r.Unrest, r => r.TroopShortfall, 2)]),
            TroopTarget.RoleGarrison => SectorThenSystem(() => [m.SectorDistanceList(_manager, x => x.RoleGarrisonShort, 2)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Unrest && !r.CalmWithoutRole, r => r.TroopShortfall, 2)]),
            TroopTarget.KeyGarrison => SectorThenSystem(() => [m.SectorList(x => x.KeySystem, x => x.TroopShortfall, 2)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Unrest, r => r.TroopShortfall, 2)]),
            TroopTarget.Garrison => SectorThenSystem(() => [m.SectorDistanceList(_manager, x => x.GarrisonShort, 2)], x => [m.SystemList(x, r => r.GarrisonShort && r.Own && !r.Unrest, r => r.TroopShortfall, 2)]),
            _ => m.Record(_manager.System),
        };
        if (s is null) { if (selector != TroopTarget.Facility) _failed.Add(selector); return; }
        _target = s.System;
        (_want, _filter, _criterion, _dir) = selector switch
        {
            TroopTarget.GarrisonTroops => (s.HasRole || s.KeySystem) ? (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.TroopDefense, 1) : (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.MaintenanceCost, 2),
            TroopTarget.RoleGarrison or TroopTarget.KeyGarrison => (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.TroopDefense, 1),
            TroopTarget.Garrison => (s.TroopShortfall, ProductFilter.DefenseTroop, Criterion.MaintenanceCost, 2),
            _ => (_facilities, ProductFilter.AttackTroop, Criterion.TroopAttack, 1),
        };
    }
}

/// <summary>
/// Planner 7: fighters for systems: moves 0x3F1, production 0x3F2, facility 0x3EC (shipyards).
/// p = 17. Status != 1 -> phase 0; another phase -> 0x3F1 (empty call); 0x3F1 / 0x3F2 at state 0
/// -> first state (empty call); each 0x3F1 end -> 0x3F2, each 0x3F2 end -> 0x3EC, each 0x3EC end -> 0x3F1 (Inferred: each end
/// also ends the run).
/// </summary>
public sealed class SystemFighterPlanner() : ProductionPlanner(7)
{
    protected override Clone K { get; } = new(SystemRole.Shipyard, ManufacturingKind.Ship, ShipyardsOf, ProductFilter.Shipyard, 4,
        x => x.ShipyardSlot, x => x.Shipyards, r => r.ShipyardRole || r.Unrest, ShipyardsOf, FreeSlotsOf, 17, 20,
        r => r.ShipyardMaintenance);
    ManufacturingManager? _manager; // Used by phase 0x3F2
    AiSystem? _source; // Used by phase 0x3F1
    GameObject? _destination;
    AiFleet? _fleet;
    int _yards, _count;
    uint _product;
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. It is the one product
    /// field of both the unit order and the facility-template order, so both of ours are reset.
    /// </summary>
    internal override void AfterLoad() => (_product, _fProduct) = (0, 0);

    protected override void Exit() => (Phase, State, Done) = (0x3F1, 0, true);
    void EndMoves() => (Phase, State, Done) = (0x3F2, 0, true);
    void EndProduction() => (Phase, State, Done) = (FacilityPhase, 0, true);
    int Budget(WorldModel m, int f = 80) => m.UnitBudget(17, f, m.FighterMaintenance);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { (Phase, State, Done) = (0, 0, true); return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (Phase is not (0x3F1 or 0x3F2 or FacilityPhase)) { (Phase, State) = (0x3F1, 0); return null; }
        if (Phase == FacilityPhase) return Facility(m, side);
        if (Phase == 0x3F1) return Moves(m, side);
        switch (State)
        {
            case 0:
                State = 5;
                return null;
            case 5: // shipyard site (R)
                if (Recheck(m) is { } r) (_manager, _yards, State) = (ManagerOf(r.System, ManufacturingKind.Ship), r.Shipyards, 2);
                else State = 10;
                return null;
            case 2: // destination
            {
                var dest = Destination(m);
                if (dest is { } d && d.FighterBalance < 0) { (_destination, State) = (d.System, 6); return null; }
                if (dest is null && Budget(m) > 0) foreach (var k in new[] { ModelConstant.FighterMargin, ModelConstant.RoleFighterMargin, ModelConstant.KeyFighterMargin }) m.RaiseConstant(k);
                State = 10;
                return null;
            }
            case 6:
            {
                int b = Budget(m);
                if (b < 0) { State = 7; return null; }
                var rec = m.Systems[(StarSystem)_destination!];
                var (product, cost) = b > 0 ? (rec.HasRole || rec.KeySystem) ? m.PickProduct(0x28, ProductFilter.Fighter, Criterion.AntiFighterFirepower, 1) : m.PickProduct(0x28, ProductFilter.Fighter, Criterion.MaintenanceCost, 2) : (null, 0);
                // Deviation: the original stores product none when the pick finds nothing and still goes to the order on a
                // positive count; openswr ends here. Only reachable with mods (Inferred: the install data always has a level-0 product for this mask).
                if (product is null) { EndProduction(); return null; }
                int want = -rec.FighterBalance;
                _count = Math.Min(cost * want > b && cost > 0 ? b / cost : want, _yards);
                if (_count <= 0) { EndProduction(); return null; }
                (_product, State) = (product.Type, 0xB);
                return null;
            }
            case 0xB:
                if (_manager is null) { EndProduction(); return null; }
                State = 0xC;
                return Urgent(new Command(0x214, side) { Targets = [(_manager, 0)], ArgRef = _destination });
            case 0xC:
                EndProduction();
                return new Command(0x211, side) { Targets = [(_manager!, 0)], Fields = [_product, (uint)_count] };
            case 10: // over-budget test (f = 90): over -> 7, else the facility phase in the same think (done not set)
                if (Budget(m, 90) < 0) State = 7;
                else (Phase, State) = (FacilityPhase, 0);
                return null;
            default: // 7: over budget
                EndProduction();
                return OverBudget(m, side);
        }
    }

    /// <summary>
    /// Phase 0x3F1: source with surplus; destination (fleets 0x100009, fleets 0x100005, the system stage, the
    /// operation fallback); move (the same squadron repeated) or scrap.
    /// </summary>
    Command? Moves(WorldModel m, Owner side)
    {
        switch (State)
        {
            case 0:
                State = 1;
                return null;
            case 1:
                _source = SectorThenSystem(() => [m.SectorList(x => x.HasUsableFighterSurplus, x => x.UsableFighterSurplus, 1)],
                    x => [m.SystemList(x, r => r.Own && !r.Blockade && !r.Base, r => r.UsableFighterBalance, 1)]);
                if (_source is not null && _source.UsableFighterBalance > 0) State = 2;
                else (Phase, State) = (0x3F2, 0); // without done, the production phase runs in the same think
                return null;
            case 2:
            {
                _fleet = FleetDestination(m, _source!.System, HeldWithRoom) ?? FleetDestination(m, _source.System, FreeWithRoom);
                var d = _fleet is not null ? null
                    : SectorThenSystem(() => [m.SectorDistanceList(_source.System, x => x.UsableFighterShort, 2)], x => [m.SystemList(x, r => r.FighterShort && r.UsableFighterShort && r.Own && !r.Blockade, r => r.FighterBalance, 2)]);
                _destination = _fleet?.Fleet ?? (GameObject?)d?.System ?? OperationFallback(m, _source.System);
                State = _destination is null ? 4 : 3;
                return null;
            }
            case 3:
            {
                int want = _fleet is not null ? _fleet.FighterRoom : -m.Systems[(StarSystem)_destination!].FighterBalance; // also for the fallback's system (as specced)
                int n = Math.Min(want, _source!.UsableFighterBalance);
                var squadron = Fighter(m, _source.System);
                if (n <= 0 || squadron is null) { State = 4; return null; }
                EndMoves(); // no priority (run 2 tick 9: planner 7's move does not keep it urgent)
                return new Command(0x201, side) { Targets = [.. Enumerable.Repeat((squadron, 0), n)], ArgRef = _destination };
            }
            default: // 4
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
    /// (dir 2), in pick order the fleet op of the sector: its positive fighter need -> its base.
    /// </summary>
    static StarSystem? OperationFallback(WorldModel m, StarSystem source)
    {
        var stack = new ListStack();
        stack.Push(m.SectorDefaultList(x => x.TheaterMark, 2));
        stack.Push(m.SectorDistanceList(source, x => x.TheaterMark, 2));
        while (stack.Pick() is AiSector sector)
            if (m.FleetOps.GetValueOrDefault(sector.FleetOpKey) is { } op && FleetOps.FighterNeed(m, op) is { } b) return b;
        return null;
    }

    /// <summary>Fleets with the flags, excl Enroute, ranked by distance to the source (dir 2) and free fighter slots (dir 1).</summary>
    static AiFleet? FleetDestination(WorldModel m, StarSystem source, Func<AiFleet, bool> pass) =>
        PickOne(m.FleetDistanceList(source, f => pass(f) && !f.Enroute, 2), m.FleetList(f => pass(f) && !f.Enroute, f => f.FighterRoom, 1)) as AiFleet;


    /// <summary>The source's Usable fighter squadron (Inferred: key order for the vtable minimum).</summary>
    static GameObject? Fighter(WorldModel m, StarSystem s) => Usable(m, s, 0x1C, 0x1D, 0x1E, 0x1F).MinBy(m.FighterKey);

    /// <summary>The three destination stages (sectors by distance to the shipyard manager, systems by their rank value, dir 2).</summary>
    AiSystem? Destination(WorldModel m)
    {
        if (_manager is null) return null;
        AiSystem? d = null;
        foreach (var (bit, excl) in new (Func<AiSector, bool>, Func<AiSystem, bool>)[] { (x => x.RoleFighterShort, r => r.CalmWithoutRole || r.Unrest),
            (x => x.KeySystem, r => r.Unrest), (x => x.FighterShort, r => r.Unrest) })
            d ??= SectorThenSystem(() => [m.SectorDistanceList(_manager, bit, 2)], x => [m.SystemList(x, r => r.FighterShort && r.Own && !excl(r), r => r.FighterBalance, 2)]);
        return d;
    }

    /// <summary>
    /// State 7: source (a) sectors with a usable-fighter surplus / own systems that are no base by a rank value, (b) sectors with own
    /// fighters that hold no key system by a rank value / systems with own fighters that are no key system and no base by a rank value
    /// (dir 2), (c) sectors with own fighters / systems with own fighters that are no base; one
    /// squadron -> 0x201 to a fleet destination (0x100009, 0x100005, the operation fallback) else 0x200.
    /// </summary>
    static Command? OverBudget(WorldModel m, Owner side)
    {
        var source = SectorThenSystem(() => [m.SectorList(x => x.HasUsableFighterSurplus, x => x.UsableFighterSurplus, 1)], x => [m.SystemList(x, r => r.Own && !r.Base, r => r.UsableFighterBalance, 1)])
            ?? SectorThenSystem(() => [m.SectorList(x => x.OwnFighters && !x.KeySystem, x => x.Fighters, 2)], x => [m.SystemList(x, r => r.OwnFighters && !r.KeySystem && !r.Base, r => r.Fighters, 2)])
            ?? SectorThenSystem(() => [m.SectorList(x => x.OwnFighters, x => x.Fighters, 2)], x => [m.SystemList(x, r => r.OwnFighters && !r.Base, r => r.Fighters, 2)]);
        if (source is null || Fighter(m, source.System) is not { } squadron) return null;
        var dest = (GameObject?)(FleetDestination(m, source.System, HeldWithRoom) ?? FleetDestination(m, source.System, FreeWithRoom))?.Fleet
            ?? OperationFallback(m, source.System);
        return dest is not null ? new Command(0x201, side) { Targets = [(squadron, 0)], ArgRef = dest } : Scrap(side, [squadron]);
    }
}

/// <summary>Planner 10: fighters for fleets, facility 0x3EC (shipyards). p = 8.</summary>
public sealed class FleetFighterPlanner() : ProductionPlanner(10)
{
    protected override Clone K { get; } = new(SystemRole.Shipyard, ManufacturingKind.Ship, ShipyardsOf, ProductFilter.Shipyard, 3,
        x => x.ShipyardSlot, x => x.Shipyards, r => r.ShipyardRole || r.Unrest, ShipyardsOf, r => r.Energy, 8, 20,
        r => r.ShipyardMaintenance);
    const int Unit = 0x3F3; // internal id of the unit phase
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

    protected override void Exit() => (Phase, State, Done) = (Unit, 3, true);
    /// <summary>Every unit-phase end: phase 0x3EC at N, with done.</summary>
    void EndUnit() => (Phase, State, Done) = (FacilityPhase, FN, true);
    int Budget(WorldModel m) => m.UnitBudget(8, 80, m.FleetFighterMaintenance);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (Phase == 0) (Phase, State) = (Unit, 3);
        if (Phase == FacilityPhase) return Facility(m, side);
        switch (State)
        {
            case 3: // site (R)
                if (Recheck(m) is { } r) (_manager, State) = (ManagerOf(r.System, ManufacturingKind.Ship), 2);
                else State = 8;
                return null;
            case 2: // the operation tree from the cursor, no wrap; else fleets own + created + free fighter slots
            {
                (_fleet, _system, _want) = (null, null, 1);
                FleetOp? found = null;
                foreach (var op in m.FleetOps.Values.Where(o => _nextOp == 0 || o.Key >= _nextOp))
                    if (FleetOps.FighterNeed(m, op) is { } b) { (found, _system, _want) = (op, b, op.FighterNeed); break; }
                (_op, _nextOp) = found is null ? (0, 0) : (found.Key, m.FleetOps.Keys.FirstOrDefault(k => k > found.Key));
                if (found is null)
                    _fleet = PickOne(m.FleetList(f => FreeWithRoom(f) && !f.NotCompletedShip && !f.EnrouteShip && !f.Enroute, f => f.FighterRoom, 2)) as AiFleet;
                State = _fleet is null && _system is null ? 8 : 4;
                return null;
            }
            case 4:
            {
                int b = Budget(m);
                if (b < 0) { State = 5; return null; }
                if (b == 0) { EndUnit(); return null; }
                var criterion = m.Rand(10) > 5 ? Criterion.Torpedoes : Criterion.AntiFighterFirepower;
                // Deviation: the original stores product none when the pick finds nothing and still goes to the order on a
                // positive count; openswr ends here. Only reachable with mods (Inferred: the install data always has a level-0 product for this mask).
                if (m.PickProduct(0x28, ProductFilter.Fighter, criterion, 1) is not ({ } product, var cost)) { EndUnit(); return null; }
                int want = _want;
                _count = cost * want > b && cost > 0 ? b / cost : want;
                // the manager's non-suspended members, fleet and system destinations alike.
                if (_manager is not null) _count = Math.Min(_count, Economy.Members(_manager).Count(f => !f.Suspended));
                if (_count <= 0) { EndUnit(); return null; }
                (_product, State) = (product.Type, 9);
                return null;
            }
            case 8:
                if (Budget(m) < 0) State = 5;
                else EnterFacility();
                return null;
            case 5: // over budget (fleet and ship fields read 0: round 7)
            {
                EndUnit();
                if (PickOne(m.FleetList(f => OwnFree(f) && !f.Enroute, f => f.UsableFighters, 1)) is not AiFleet f
                    || PickOne(m.ShipList(f, s => OwnFreeShip(s) && !s.Enroute, s => s.UsableFighters, 1)) is not AiShip ship) return null;
                return Usable(m, ship.Ship, 0x1C, 0x1D, 0x1E, 0x1F).MinBy(m.FighterKey) is { } squadron ? Scrap(side, [squadron]) : null;
            }
            case 9:
                if (_manager is null) { EndUnit(); return null; }
                State = 10;
                return Urgent(new Command(0x214, side) { Targets = [(_manager, 0)], ArgRef = (GameObject?)_fleet?.Fleet ?? _system });
            default: // 10
                EndUnit();
                return new Command(0x211, side) { Targets = [(_manager!, 0)], Fields = [_product, (uint)_count] };
        }
    }
}

/// <summary>Planner 11: capital ships (phase 0x3F9), facility 0x3EC (shipyards); moves 0x3F8 are round 7. p = 30.</summary>
public sealed class CapitalShipPlanner() : ProductionPlanner(11)
{
    protected override Clone K { get; } = new(SystemRole.Shipyard, ManufacturingKind.Ship, ShipyardsOf, ProductFilter.Shipyard, 6,
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

    /// <summary>Inferred: the production and facility phases end back in the move phase 0x3F8 (it runs once per cycle).</summary>
    protected override void Exit() => (Phase, State, Done) = (0x3F8, 0, true);
    int Budget(WorldModel m) => m.UnitBudget(30, 90, m.ShipMaintenance);
    /// <summary>Every end of the class / pick / order path: phase 0x3EC at N, with done.</summary>
    void EndProduction() => (Phase, State, Done) = (FacilityPhase, FN, true);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { (Phase, State, Done) = (0, 0, true); return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (Phase is not (FacilityPhase or 0x3F8 or 0x3F9)) { (Phase, State) = (0x3F8, 0); return null; }
        if (Phase == FacilityPhase) return Facility(m, side);
        if (Phase == 0x3F8) return Moves(game, m, side);
        switch (State)
        {
            case 0: // Inferred: the production phase starts at its site state
                State = 8;
                return null;
            case 8: // site (R)
                if (Recheck(m) is { } r) (_manager, State) = (ManagerOf(r.System, ManufacturingKind.Ship), 5);
                else State = 0xD;
                return null;
            case 0xD: // over-budget test
                if (Budget(m) < 0) State = 10;
                else EnterFacility();
                return null;
            case 5: // class
                Class(m, side); // always -> 9 (no class: mask and criterion zero)
                State = 9;
                return null;
            case 9: // pick
            {
                _failed.Clear();
                int b = Budget(m);
                if (b < 0) { State = 10; return null; }
                if (b == 0) { EndProduction(); return null; }
                var stack = new ListStack();
                stack.Push(new RankedList(Research.Buildable(game, side, 0x28).Select(r => m.Catalogue[r.Common.Ref])
                    .Where(p => p is not null && p.Is(_filter)).Select(p => ((object)p!, p!.Value(_criterion))), 1, m.Rand));
                while (stack.Pick() is Product p)
                    if (p.MaintenanceCost <= b) { (_product, State) = (p.Type, 0xF); return null; }
                if (_failure != ShipClass.None) _failed.Add(_failure);
                EndProduction();
                return null;
            }
            case 0xF: // no 0x214 (the ship goes to the manager's TargetKey)
                EndProduction();
                return _manager is null ? null : new Command(0x211, side) { Targets = [(_manager, 0)], Fields = [_product, 1] };
            default: // 10: over budget: the own, created fleet without Enroute / unfinished ships with the
                // smallest capital-ship maintenance, then its ship by a rank value among own free ships that are neither Enroute nor not Completed (ship rank value and
                // the free mark Inferred, round 7: read 0 / never set) -> 0x200 of a capital ship; each list on its own stack
            {
                EndProduction();
                if (PickOne(m.FleetList(f => OwnFree(f) && !f.NotCompletedShip && !f.Enroute, f => f.Maintenance, 2)) is not AiFleet f
                    || PickOne(m.ShipList(f, s => OwnFreeShip(s) && !s.NotCompleted && !s.Enroute, s => s.Maintenance, 2)) is not AiShip ship) return null;
                return Family.IsCapitalShip(ship.Ship.Type) ? Scrap(side, [ship.Ship]) : null;
            }
        }
    }

    // ---- phase 0x3F8 ----

    Command? Moves(GameState game, WorldModel m, Owner side)
    {
        switch (State)
        {
            case 0:
                State = 2;
                return null;
            case 2: // damaged ships
            {
                State = 3;
                var c = DamagedShips(m, side);
                if (c is not null) { Priority++; Done = true; }
                return c;
            }
            case 3: // fleet move
            {
                State = 4;
                var c = FleetMove(m, side);
                if (c is not null) { Priority++; Done = true; }
                return c;
            }
            default: // 4: troop delivery to a fleet-op base
            {
                (Phase, State, Done) = (0x3F9, 0, true);
                return TroopDelivery(m, side); // no priority +1
            }
        }
    }

    /// <summary>
    /// (only with an assault or defense base): theater sectors by a helper (dir 2), popped; the fleet op keyed
    /// by sector with a troop need and a base without Blockade is the destination;
    /// source: fleets own + free (excl Enroute) by usable troops (dir 1), per fleet its own Completed ships (excl Enroute) all
    /// popped, their usable troops; the first fleet that gives troops -> 0x201 -> the base. The second-op test has no effect (Inferred).
    /// </summary>
    static Command? TroopDelivery(WorldModel m, Owner side)
    {
        if (!(m.AssaultBase || m.DefenseBase)) return null;
        StarSystem? dest = null;
        var sectors = new ListStack();
        sectors.Push(m.SectorDefaultList(x => x.TheaterMark, 2));
        while (dest is null && sectors.Pick() is AiSector s)
            if (m.FleetOps.GetValueOrDefault(s.FleetOpKey) is { } op && FleetOps.TroopNeed(m, op) > 0 && m.Record(op.Base) is { } b && !b.Blockade) dest = b.System;
        if (dest is null) return null;
        var fleets = new ListStack();
        fleets.Push(m.FleetList(f => OwnFree(f) && !f.Enroute, f => f.UsableTroops, 1));
        while (fleets.Pick() is AiFleet f)
        {
            var troops = m.TroopsAboard(f, s => OwnFreeShip(s) && !s.Enroute);
            if (troops.Count > 0) return new Command(0x201, side) { Targets = [.. troops.Select(x => (x, 0))], ArgRef = dest };
        }
        return null;
    }

    /// <summary>
    /// (Inferred: damaged ships to a yard): fleets (own, created, damaged-mobile; excl Enroute / Enroute ship) by ship
    /// count, popped until one at an enemy system, else again until one at a system without the shipyard role; its damaged-moving ships
    /// (not damaged-stopped nor Enroute) -> 0x201 to the nearest yard (unblockaded sectors with a shipyard role, shipyard-role systems), else own.
    /// </summary>
    static Command? DamagedShips(WorldModel m, Owner side)
    {
        AiFleet? fleet = null;
        foreach (Func<AiSystem, bool> test in new Func<AiSystem, bool>[] { r => r.Enemy, r => !r.ShipyardRole })
        {
            var stack = new ListStack();
            stack.Push(m.FleetList(f => OwnFree(f) && f.DamagedMoving && !f.EnrouteShip && !f.Enroute, f => f.Ships, 1));
            while (fleet is null && stack.Pick() is AiFleet f)
                if (m.Record(f.Location) is { } r && test(r)) fleet = f;
            if (fleet is not null) break;
        }
        if (fleet is null) return null;
        var ships = m.ShipsOf(fleet).Where(s => s.DamagedMoving && !(s.DamagedStopped || s.Enroute))
            .Select(s => s.Ship).ToList();
        if (ships.Count == 0) return null;
        var dest = SectorThenSystem(() => [m.SectorDistanceList(fleet.Fleet, x => x.OwnUnblockaded && x.ShipyardRole, 2)], x => [m.SystemDistanceList(x, fleet.Fleet, r => r.ShipyardRole && !r.Blockade, 2)])
            ?? SectorThenSystem(() => [m.SectorDistanceList(fleet.Fleet, x => x.OwnUnblockaded && x.Own, 2)], x => [m.SystemDistanceList(x, fleet.Fleet, r => r.Own && !r.Blockade, 2)]);
        return dest is null ? null : new Command(0x201, side) { Targets = [.. ships.Select(s => (s, 0))], ArgRef = dest.System };
    }

    /// <summary>
    /// Own free fleets (excl Enroute / damaged / Enroute ships) fewest ships first, popped until a command:
    /// a fleet with a not-Completed ship (0x400) splits its ready ships off with 0x4000 (0x270 -> its location), else is skipped
    /// else a Death Star fleet -> own Coruscant;
    /// else, from a location that is not an own claimed base: the nearest claimed own base in its sector, an own key system there
    /// (key sectors), then over claimed bases, then over own systems -> 0x201 [fleet].
    /// </summary>
    static Command? FleetMove(WorldModel m, Owner side)
    {
        var stack = new ListStack();
        stack.Push(m.FleetList(f => OwnFree(f) && !f.DamagedStopped && !f.DamagedMoving && !f.EnrouteShip && !f.Enroute, f => f.Ships, 2));
        while (stack.Pick() is AiFleet f)
        {
            if (f.NotCompletedShip) // a not-Completed ship: only the split branch, never the move
            {
                if (!f.ReadyShip) continue;
                var ships = new ListStack();
                ships.Push(m.ShipList(f, s => s.Own && !s.DamagedStopped && !s.NotCompleted && !s.DamagedMoving && !s.Enroute, s => s.Hull, 1));
                var ready = new List<GameObject>();
                while (ships.Pick() is AiShip s) ready.Add(s.Ship);
                if (ready.Count > 0 && f.Location is not null) return new Command(0x270, side) { Targets = [.. ready.Select(s => (s, 0))], ArgRef = f.Location };
                continue;
            }
            if (m.Record(f.Location) is not { } r || r.Sector is not { } sector) continue;
            var cor = m.Systems.Values.FirstOrDefault(x => x.System.Id == 0x109);
            AiSystem? dest = null;
            if (f.DeathStar && cor is not null && cor.Own && f.Location != cor.System) dest = cor;
            else if (!r.Own || !r.Base)
            {
                dest = PickOne(m.SystemDistanceList(sector, f.Fleet, r => r.Own && r.Base && !r.Blockade, 2)) as AiSystem;
                if (dest is null && sector.KeySystem) dest = PickOne(m.SystemDistanceList(sector, f.Fleet, r => r.KeySystem && r.Own && !r.Blockade, 2)) as AiSystem;
                dest ??= MissionOps.Nearest(m, sector, r => r.Own && r.Base && !r.Blockade);
                dest ??= MissionOps.Nearest(m, sector, r => r.Own && !r.Blockade);
            }
            if (dest is not null) return new Command(0x201, side) { Targets = [(f.Fleet, 0)], ArgRef = dest.System };
        }
        return null;
    }

    /// <summary>
    /// Class: the filter and criterion := none; per fleet op (key order) count its shortfalls (firepower, anti-fighters, troop capacity,
    /// fighter capacity, bombardment, interdictors, the last only with the Death Star shield available), each unless its ship class
    /// failed; the first request class that applies wins. None: the no-request chain, each step unless its class failed: troop carrier when the model's first value is below the second;
    /// the Death Star (Empire) when the shipyard-role list holds at least 2 systems and no own fleet holds a Death Star; armed when that value is below 1;
    /// 0x100002 when that value is below 1; carrier when the first value is at most the second.
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
