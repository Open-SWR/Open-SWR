using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>Planner 3: defences (surplus phase, shortfall phase, facility phase), role 33554432. p = 9.</summary>
public sealed class DefencePlanner() : ProductionPlanner(PlannerType.Defence)
{
    /// <summary>Surplus phase states: find the surplus site, then scrap the excess defences.</summary>
    enum SurplusState
    {
        /// <summary>Not set (the default value).</summary>
        None,
        Find,
        Excess,
    }

    /// <summary>Shortfall phase states (the shortfall machine, and its order steps).</summary>
    enum DefenceState
    {
        /// <summary>Not set (the default value).</summary>
        None,
        /// <summary>R recheck of the construction manager.</summary>
        Recheck,
        /// <summary>Shortfall site.</summary>
        Shortfall,
        /// <summary>Scrap at the site (Inferred: priority +1).</summary>
        SiteScrap,
        /// <summary>Budget and pick: count := 0, product and count.</summary>
        Count,
        /// <summary>Over budget: scrap one defence, else the Mine / Refinery site.</summary>
        OverBudget,
        /// <summary>Mine / Refinery site of the facility template.</summary>
        Site,
        /// <summary>K-type scrap at the site (only without a role).</summary>
        KScrap,
        /// <summary>SetProductionTarget command: set the production target.</summary>
        SetTarget,
        /// <summary>ProduceFacility command: produce the facility.</summary>
        Produce,
    }

    protected override Clone K { get; } = new(SystemRole.Defence, ManufacturingKind.Construction, YardsOf, ProductFilter.ConstructionYard, TurnSlot.Defence,
        x => x.ConstructionSlot, x => x.ConstructionYards, r => r.AnyConstructionRole || r.Unrest, YardsOf, FreeSlotsOf, 9, 10,
        r => r.ConstructionMaintenance);
    StarSystem? _site; // The construction manager is Builder, and product and count are the template's.
    /// <summary>
    /// Load: the product is not saved, so it is none until the next pick. The template product is planner 3's product.
    /// </summary>
    internal override void AfterLoad() => _fProduct = 0;

    protected override void Exit() => (Phase, State, Done) = ((int)PlannerPhase.DefenceSurplus, (int)SurplusState.Find, true);
    int UnitBudget(WorldModel m) => m.UnitBudget(9, 90, m.DefenceMaintenance);

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        var side = controller.Side;
        if (CurrentPhase == PlannerPhase.Start) (Phase, State) = ((int)PlannerPhase.DefenceSurplus, (int)SurplusState.Find); // a new planner starts in the surplus phase
        if (CurrentPhase == PlannerPhase.Facility) return Facility(m, side);
        if (CurrentPhase == PlannerPhase.DefenceSurplus)
        {
            if ((SurplusState)State == SurplusState.Find)
            {
                if (Surplus(m) is { } s) (_site, State) = (s.System, (int)SurplusState.Excess);
                else (Phase, State) = ((int)PlannerPhase.DefenceShortfall, (int)DefenceState.Recheck);
                return null;
            }
            // Excess: GenCores, then KDY cannons and LNR batteries above their targets; the scrap ends the run without
            // priority (run 1: planner 6 decides on the next tick)
            (Phase, State) = ((int)PlannerPhase.DefenceShortfall, (int)DefenceState.Recheck);
            var take = ExcessDefences(m, m.Systems[_site!]);
            if (take.Count == 0) return null;
            Done = true;
            return Scrap(side, take);
        }
        switch ((DefenceState)State)
        {
            case DefenceState.Recheck: // R
                if (Recheck(m) is { } r) (Builder, State) = (ManagerOf(r.System, ManufacturingKind.Construction), (int)DefenceState.Shortfall);
                else EnterFacility();
                return null;
            case DefenceState.Shortfall: // shortfall site
            {
                var site = Shortfall(m);
                if (site is not null && site.DefenceBalance < 0) { (_site, State) = (site.System, (int)DefenceState.SiteScrap); return null; }
                if (site is null && UnitBudget(m) > 0 && m.Constant(ModelConstant.GenCores) < 2) m.RaiseConstant(ModelConstant.GenCores);
                State = (int)DefenceState.Site;
                return null;
            }
            case DefenceState.SiteScrap: // site scrap; Inferred: priority +1 like the F scraps
            {
                State = (int)DefenceState.Count;
                var take = SiteScrap(m, _site!);
                return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
            }
            case DefenceState.Count: // count := 0
            {
                _fCount = 0;
                int b = UnitBudget(m);
                if (b < 0) { State = (int)DefenceState.OverBudget; return null; }
                var rec = m.Systems[_site!];
                State = (int)DefenceState.Site;
                if (b == 0 || rec.FreeEnergy <= 0) return null;
                var (mask, criterion) = _site!.Id == Refs.CoruscantIndex && rec.DeathStarShieldWanted ? (ProductFilter.DeathStarShield, Criterion.Shields)
                    : rec.GenCoreShort ? (ProductFilter.GenCore, Criterion.Shields) : (ProductFilter.Battery, Criterion.DefenceAttack);
                if (m.PickProduct(Families.ConstructionYard, mask, criterion, RankOrder.Ascending) is ({ } product, var cost) && cost <= b) (_fProduct, _fCount, State) = (product.Type, 1, (int)DefenceState.SetTarget);
                return null;
            }
            case DefenceState.OverBudget: // over budget
            {
                var s = Surplus(m) ?? OverBudgetSite(m, x => x.DefendedNonKey && x.ShipyardSlot && x.TrainingSlot && x.ConstructionSlot)
                    ?? OverBudgetSite(m, x => x.DefendedNonKey);
                var x = s is null ? null : Usable(m, s.System, Families.IonCannon).Concat(Usable(m, s.System, Families.Gun)).Concat(Usable(m, s.System, Families.PlanetaryShield)).FirstOrDefault();
                if (x is null) { State = (int)DefenceState.Site; return null; }
                Exit(); // Inferred: the run ends with the scrap
                return Scrap(side, [x]);
            }
            case DefenceState.Site: // Mine / Refinery site, falls through into KScrap
            {
                bool refinery = m.Refineries < m.Mines;
                _fProduct = refinery ? Refs.Refinery : Refs.Mine;
                var site = OwnSite(m, Builder, refinery);
                _site = site?.System;
                if (site is null) (Phase, State) = ((int)PlannerPhase.Facility, (int)TemplateStep.SetTarget); // SetProductionTarget with Build, then ProduceFacility with the count
                else _fCount = 1;
                goto case DefenceState.KScrap;
            }
            case DefenceState.KScrap: // K-type scrap at the site (only without a role)
            {
                if (CurrentPhase != PlannerPhase.Facility) State = (int)DefenceState.SetTarget;
                if (_site is null || m.Systems[_site].HasRole) return null;
                var take = Usable(m, _site, Families.Shipyard, Families.TrainingFacility, Families.ConstructionYard);
                return take.Count > 0 ? Urgent(Scrap(side, take)) : null;
            }
            case DefenceState.SetTarget:
                if (Builder is null) { Exit(); return null; }
                State = (int)DefenceState.Produce;
                return Urgent(new Command(CommandType.SetProductionTarget, side) { Targets = [(Builder, GroupMode.Object)], ArgRef = _site });
            default: // DefenceState.Produce
                Exit();
                return new Command(CommandType.ProduceFacility, side) { Targets = [(Builder!, 0)], Fields = [_fProduct, (uint)_fCount] };
        }
    }

    /// <summary>
    /// Planner 3's own Mine / Refinery site: sectors by distance to the construction manager (Mine / Refinery site),
    /// then systems by distance (Mine site; Refinery site and own; both without a role or unrest), dir Descending, no fallback. Inferred: no
    /// manager gives none.
    /// </summary>
    static AiSystem? OwnSite(WorldModel m, GameObject? manager, bool refinery) => manager is null ? null : SectorThenSystem(
        () => [m.SectorDistanceList(manager, x => refinery ? x.RefinerySite : x.MineSite, RankOrder.Descending)],
        x => [m.SystemDistanceList(x, manager, r => (refinery ? r.RefinerySite && r.Own : r.MineSite) && !r.HasRole && !r.Unrest, RankOrder.Descending)]);

    /// <summary>
    /// Site scrap: producers without their role (the shipyard, training and construction roles); nothing and no free energy: the first Refinery (own Mines &lt; Refineries or no Mine), else the first Mine (own
    /// Refineries <= Mines or no Refinery), else the first shipyard, training facility or construction yard. Inferred: each "else"
    /// follows from the previous condition being false.
    /// </summary>
    static List<GameObject> SiteScrap(WorldModel m, StarSystem s)
    {
        var r = m.Systems[s];
        var take = new List<GameObject>();
        if (!r.ShipyardRole) take.AddRange(Usable(m, s, Families.Shipyard));
        if (!r.TrainingRole) take.AddRange(Usable(m, s, Families.TrainingFacility));
        if (!r.AnyConstructionRole) take.AddRange(Usable(m, s, Families.ConstructionYard));
        if (take.Count > 0 || r.FreeEnergy != 0) return take;
        var one = m.Mines < m.Refineries || r.Mines == 0 ? Usable(m, s, Families.Refinery).FirstOrDefault()
            : m.Refineries <= m.Mines || r.Refineries == 0 ? Usable(m, s, Families.Mine).FirstOrDefault()
            : Usable(m, s, Families.Shipyard).Concat(Usable(m, s, Families.TrainingFacility)).Concat(Usable(m, s, Families.ConstructionYard)).FirstOrDefault();
        return one is null ? take : [one];
    }

    /// <summary>Surplus site: sectors with a defence surplus and own systems, ranked by their counts (dir Ascending), when the count is above 0.</summary>
    static AiSystem? Surplus(WorldModel m) =>
        SectorThenSystem(() => [m.SectorList(x => x.DefenceSurplus, x => x.DefenceSurplusSystems, RankOrder.Ascending)], x => [m.SystemList(x, r => r.Own, r => r.DefenceBalance, RankOrder.Ascending)]) is { } s && s.DefenceBalance > 0 ? s : null;

    /// <summary>Over-budget site: sectors that pass, then own systems excluding roles and Coruscant, ranked by the fields (Inferred: dir Ascending).</summary>
    static AiSystem? OverBudgetSite(WorldModel m, Func<AiSector, bool> pass) =>
        SectorThenSystem(() => [m.SectorList(pass, x => x.OwnSystems, RankOrder.Ascending)],
            x => [m.SystemList(x, r => r.Own && !r.HasRole && !r.Coruscant, r => r.GenCores, RankOrder.Ascending),
                m.SystemList(x, r => r.Own && !r.HasRole && !r.Coruscant, r => r.Batteries, RankOrder.Ascending)]);

    List<GameObject> ExcessDefences(WorldModel m, AiSystem r)
    {
        int gencores = r.GenCores - r.GenCoreTarget, batteries = r.Batteries - r.BatteryTarget;
        var s = r.System;
        return [.. Usable(m, s, Families.PlanetaryShield).Take(Math.Max(0, gencores)), .. Usable(m, s, Families.IonCannon).Concat(Usable(m, s, Families.Gun)).Take(Math.Max(0, batteries))];
    }

    /// <summary>Shortfall site, first match of: Coruscant (own, not blockaded, shield wanted or GenCores below the role-system GenCore target); the sectors with a role-system defence shortfall ranked by a count (dir Ascending, pushed first) and by distance (dir Descending), its own system with a defence shortfall (dir Descending); Coruscant with a count below 0; sectors with a defence shortfall by distance, systems with one.</summary>
    AiSystem? Shortfall(WorldModel m)
    {
        var cor = m.Systems.Values.FirstOrDefault(r => r.System.Id == Refs.CoruscantIndex);
        bool corOk = cor is not null && Own(cor) && !cor.Blockade;
        if (corOk && (cor!.DeathStarShieldWanted || cor.GenCores < m.Constant(ModelConstant.RoleGenCores))) return cor;
        if (Builder is null) return corOk && cor!.DefenceBalance < 0 ? cor : null;
        return SectorThenSystem(() => [m.SectorList(x => x.RoleDefenceShort, x => x.DefenceShortSystems, RankOrder.Ascending), m.SectorDistanceList(Builder, x => x.RoleDefenceShort, RankOrder.Descending)], // list pushed first
                x => [m.SystemList(x, r => r.DefenceShort && r.Own && !r.Unrest && !r.CalmWithoutRole, r => r.DefenceBalance, RankOrder.Descending)])
            ?? (corOk && cor!.DefenceBalance < 0 ? cor : null)
            ?? SectorThenSystem(() => [m.SectorDistanceList(Builder, x => x.DefenceShort, RankOrder.Descending)], x => [m.SystemList(x, r => r.DefenceShort && !r.Unrest, r => r.DefenceBalance, RankOrder.Descending)]);
    }
}
