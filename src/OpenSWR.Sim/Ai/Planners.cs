using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Planner type numbers (the planner's type field, an int in the save format). Planners 4, 8, 9, 12 and 13 are declared in
/// their own files and are constructed through the factory in <see cref="ComputerController"/>.
/// </summary>
public enum PlannerType
{
    /// <summary>Not set (the default value).</summary>
    None,
    /// <summary>Planner 1: construction yards, Mines and Refineries.</summary>
    Construction,
    /// <summary>Planner 2: Mines and Refineries with their own construction role.</summary>
    Mine,
    /// <summary>Planner 3: defences (surplus, shortfall and the facility template).</summary>
    Defence,
    /// <summary>Planner 4: mission operations.</summary>
    MissionOperation,
    /// <summary>Planner 5: special forces (training facilities).</summary>
    SpecialForce,
    /// <summary>Planner 6: troops (moves and training facilities).</summary>
    Troop,
    /// <summary>Planner 7: fighters for systems (moves and shipyards).</summary>
    SystemFighter,
    /// <summary>Planner 8: fleet operations.</summary>
    FleetOperation,
    /// <summary>Planner 9: fleet tasks.</summary>
    FleetTask,
    /// <summary>Planner 10: fighters for fleets (shipyards).</summary>
    FleetFighter,
    /// <summary>Planner 11: capital ships (moves and shipyards).</summary>
    CapitalShip,
    /// <summary>Planner 12: character operations.</summary>
    CharacterOperation,
    /// <summary>Planner 13: strategy (mode choice, theaters, headquarters).</summary>
    Strategy,
    /// <summary>Planner 14: names of own capital ships.</summary>
    Naming,
}

/// <summary>
/// Turn slots of the producer-facility turn: the index into the model's turn requests and the turn value.
/// </summary>
public enum TurnSlot
{
    /// <summary>Planner 1.</summary>
    Construction,
    /// <summary>Planner 2.</summary>
    Mine,
    /// <summary>Planner 6.</summary>
    Troop,
    /// <summary>Planner 10.</summary>
    FleetFighter,
    /// <summary>Planner 7.</summary>
    Fighter,
    /// <summary>Planner 5.</summary>
    SpecialForce,
    /// <summary>Planner 11.</summary>
    CapitalShip,
    /// <summary>Planner 3.</summary>
    Defence,
}

/// <summary>
/// Shared site slots of the role-list recheck (the model's site slots): the site each production planner
/// keeps for the others to fall back on. None = the planner has no own slot (planners 1, 2, 3).
/// </summary>
public enum SiteSlot
{
    None,
    /// <summary>Planner 6's training site.</summary>
    Troop,
    /// <summary>Planner 5's training site.</summary>
    SpecialForce,
    /// <summary>Planner 7's shipyard site.</summary>
    Fighter,
    /// <summary>Planner 10's shipyard site.</summary>
    FleetFighter,
    /// <summary>Planner 11's shipyard site.</summary>
    CapitalShip,
}

/// <summary>
/// Clone parameters of the producer-facility template: role, producer kind and its count, product mask, turn bit, N sector list
/// (need, value), N system list (exclude mask, value, second value), facility budget (p, g, maintenance), T variant (by distance,
/// else planner 5's role-construction-yard list).
/// </summary>
public sealed record Clone(SystemRole Role, ManufacturingKind Kind, Func<AiSystem, int> Count, ProductFilter Producer, TurnSlot TurnSlot, Func<AiSector, bool> SectorBit,
    Func<AiSector, int> SectorValue, Func<AiSystem, bool> SystemExclude, Func<AiSystem, int> SystemValue, Func<AiSystem, int> SecondValue, int Percent, int G,
    Func<AiSystem, int> Field, bool TurnByDistance = true, bool BothSectorLists = false);

/// <summary>
/// Phase numbers of the production planners, the values the original stores in the phase field. <see cref="Planner.Phase"/> stays
/// int (save format, traces), so code casts these at each use.
/// </summary>
public enum PlannerPhase
{
    /// <summary>Phase 0: the run's first phase (planner 1 walks its list here); for the others, not started yet.</summary>
    Start,
    /// <summary>Planner 2: Mine site.</summary>
    Mine,
    /// <summary>Planner 2: Refinery site.</summary>
    Refinery,
    /// <summary>The shared facility template (planners 1-3, 5-7, 10, 11).</summary>
    Facility,
    /// <summary>Planner 3: surplus site and the scrap of excess defences.</summary>
    DefenceSurplus,
    /// <summary>Planner 3: shortfall site and the scrap or order that follows it.</summary>
    DefenceShortfall,
    /// <summary>Planner 6: the four troop-move steps.</summary>
    TroopMove,
    /// <summary>Planner 6: troop production.</summary>
    TroopProduction,
    /// <summary>Planner 7: fighter moves.</summary>
    FighterMove,
    /// <summary>Planner 7: fighter production.</summary>
    FighterProduction,
    /// <summary>Planner 10: unit phase (fleet fighters). The original gives this phase only an internal number; its meaning is unknown.</summary>
    FleetFighter,
    /// <summary>Planner 5: special-force production and the character order.</summary>
    SpecialForce,
    /// <summary>Planner 11: capital-ship moves.</summary>
    CapitalMove,
    /// <summary>Planner 11: capital-ship production.</summary>
    CapitalProduction,
}

/// <summary>
/// States of the facility template (facility phase), shared by planners 2, 3, 5, 6, 7, 10 and 11. Planner 1 has its own states
/// (<see cref="ConstructionPlanner"/>). Start is the entry state of every machine (0).
/// </summary>
public enum TemplateStep
{
    /// <summary>Entered at state 0 (planners 5, 7): an empty first call.</summary>
    Start,
    /// <summary>N: new construction system.</summary>
    NewSystem,
    /// <summary>T: the turn site.</summary>
    Turn,
    /// <summary>F: scrap the other producer kinds in the build system.</summary>
    ScrapOthers,
    /// <summary>G: order count.</summary>
    Count,
    /// <summary>H: scrap one producer of the own list.</summary>
    ScrapOne,
    /// <summary>SetProductionTarget command: set the production target.</summary>
    SetTarget,
    /// <summary>ProduceFacility command: produce the facility.</summary>
    Produce,
    /// <summary>C: request, then D (hub).</summary>
    Request,
}

/// <summary>
/// Shared machinery of the production planners: own role list, list picks, commands, and the
/// producer-facility template steps R, N, T, F, G, H, SetProductionTarget / ProduceFacility, C, D with the facility-phase state machine.
/// </summary>
public abstract class ProductionPlanner(PlannerType type) : Planner(type)
{
    /// <summary>The planner's own list of role systems (a key tree: key order).</summary>
    public SortedSet<StarSystem> List { get; } = new(Comparer<StarSystem>.Create((a, b) => a.Ref.CompareTo(b.Ref)));
    /// <summary>Clone parameters.</summary>
    protected abstract Clone K { get; }

    /// <summary>Facility phase: the build system, the borrowed construction manager (T), usable yards, product and count.</summary>
    public StarSystem? Build { get; protected set; }
    public ManufacturingManager? Builder { get; protected set; }
    int _usable;
    /// <summary>Order count and product of the facility template: the planner's own count and product fields; planner 3 shares them.</summary>
    protected int _fCount;
    protected uint _fProduct;
    StarSystem? _hub; // hub set

    /// <summary>The current phase as its enum (<see cref="Planner.Phase"/> is int).</summary>
    protected PlannerPhase CurrentPhase => (PlannerPhase)Phase;

    protected static bool OwnFree(AiFleet f) => f.Own && f.Free;
    protected static bool OwnFreeShip(AiShip s) => s.Own && s.Free;
    protected static bool HeldWithRoom(AiFleet f) => f.Own && f.Garrison && f.HasFighterRoom;
    protected static bool FreeWithRoom(AiFleet f) => f.Own && f.Free && f.HasFighterRoom;
    protected static bool Own(AiSystem r) => r.Own;
    /// <summary>No Uprising, no Blockade.</summary>
    protected static bool Calm(AiSystem r) => !r.Unrest;


    protected void Finish() => (Phase, State, Done) = ((int)PlannerPhase.Start, (int)TemplateStep.Start, true);

    protected static ManufacturingManager? ManagerOf(StarSystem? s, ManufacturingKind kind) => s?.Managers[(int)kind];
    /// <summary>The system's manager of the producer kind is idle over a Usable producer.</summary>
    protected static bool IsIdle(AiSystem r, ManufacturingKind k) => k switch
    {
        ManufacturingKind.Ship => r.IdleShipyard, ManufacturingKind.Training => r.IdleTraining, _ => r.IdleConstruction,
    };
    /// <summary>A role of the producer kind (construction: the roles of planners 1-3; Inferred for the template's F step).</summary>
    protected static bool HasRoleFor(AiSystem r, ManufacturingKind k) => k switch
    {
        ManufacturingKind.Ship => r.ShipyardRole, ManufacturingKind.Training => r.TrainingRole, _ => r.AnyConstructionRole,
    };
    // clone parameters
    protected static int ShipyardsOf(AiSystem r) => r.Shipyards;
    protected static int TrainingOf(AiSystem r) => r.TrainingFacilities;
    protected static int YardsOf(AiSystem r) => r.ConstructionYards;
    protected static int FreeSlotsOf(AiSystem r) => r.FreeSlots;
    protected static uint FamilyOf(ManufacturingKind k) => k switch { ManufacturingKind.Ship => Families.Shipyard, ManufacturingKind.Training => Families.TrainingFacility, _ => Families.ConstructionYard };
    /// <summary>Troop regiment families [Troop, CapitalShip).</summary>
    protected static readonly uint[] TroopFamilies = [Families.Troop, Families.Troop + 1, Families.Troop + 2, Families.Troop + 3];
    /// <summary>Fighter squadron families [Fighter, Headquarters).</summary>
    protected static readonly uint[] FighterFamilies = [Families.Fighter, Families.Fighter + 1, Families.Fighter + 2, Families.Fighter + 3];

    /// <summary>Usable objects of the families, in the container's view contents (key order), owned by the side in its view.</summary>
    protected static List<GameObject> Usable(WorldModel m, GameObject s, params uint[] families) =>
        [.. m.Contents(s).Where(x => families.Contains(Family.Of(x.Type)) && m.Usable(x) && m.See(x).Owner == m.Side)];

    /// <summary>Pick a sector from the sector lists, then a system in it from the system lists (stacks rebuilt per pick).</summary>
    protected static AiSystem? SectorThenSystem(Func<IEnumerable<RankedList>> sectors, Func<AiSector, IEnumerable<RankedList>> systems)
    {
        var stack = new ListStack();
        foreach (var l in sectors()) stack.Push(l);
        if (stack.Pick() is not AiSector sector) return null;
        var inner = new ListStack();
        foreach (var l in systems(sector)) inner.Push(l);
        return inner.Pick() as AiSystem;
    }

    /// <summary>Pick one key from the given lists (one stack).</summary>
    protected static object? PickOne(params RankedList[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) stack.Push(l);
        return stack.Pick();
    }

    protected static Command Scrap(Owner side, IEnumerable<GameObject> objects) => new(CommandType.Scrap, side) { Targets = [.. objects.Select(o => (o, GroupMode.Object))] };

    /// <summary>A command that ends this run with priority +1 (the planner stays urgent for its next step).</summary>
    protected Command Urgent(Command c) { Priority++; Done = true; return c; }

    protected int FacilityBudget(WorldModel m) => m.FacilityBudget(K.Percent, K.G, K.Field, List);

    /// <summary>
    /// R recheck (clones): not own / role lost gives role cleared, dropped; the first role system with producers of the
    /// kind, its manager of that kind idle and no Uprising / Blockade gives chosen. Planners 5, 6, 7, 10, 11 share their site through
    /// model slots: the own slot is cleared, a find is written to it; otherwise the fallback slots in order, each
    /// accepted only when own, with the role, producers, the idle bit and calm.
    /// </summary>
    protected AiSystem? Recheck(WorldModel m)
    {
        var (own, fallbacks) = Type switch
        {
            PlannerType.SpecialForce => (SiteSlot.SpecialForce, new[] { SiteSlot.Troop }),
            PlannerType.Troop => (SiteSlot.Troop, new[] { SiteSlot.SpecialForce }),
            PlannerType.SystemFighter => (SiteSlot.Fighter, new[] { SiteSlot.FleetFighter, SiteSlot.CapitalShip }),
            PlannerType.FleetFighter => (SiteSlot.FleetFighter, new[] { SiteSlot.Fighter, SiteSlot.CapitalShip }),
            PlannerType.CapitalShip => (SiteSlot.CapitalShip, new[] { SiteSlot.Fighter }),
            _ => (SiteSlot.None, Array.Empty<SiteSlot>()),
        };
        if (own != SiteSlot.None) m.SiteSlots[(int)own] = null;
        bool Fits(AiSystem r) => K.Count(r) > 0 && IsIdle(r, K.Kind) && Calm(r);
        foreach (var s in List.ToList())
        {
            var r = m.Systems[s];
            if (!Own(r) || !r.HasRoleOf(K.Role)) { r.SetRole(K.Role, false); List.Remove(s); continue; }
            if (Fits(r))
            {
                if (own != SiteSlot.None) m.SiteSlots[(int)own] = s;
                return r;
            }
        }
        foreach (SiteSlot slot in fallbacks)
            if (m.Record(m.SiteSlots.GetValueOrDefault((int)slot)) is { } r && Own(r) && r.HasRoleOf(K.Role) && Fits(r)) return r;
        return null;
    }

    /// <summary>
    /// N new system: the own list's own calm system with free slots and the fewest producers (first on ties); none: a sector list
    /// (a second by a rank value, dir Descending, only when the list is non-empty; planner 2 always pushes both) and a system list
    /// (own, excl the clone's mask and Coruscant; a second by the clone's second value only then) gives role, joins the list. Sets
    /// <see cref="Build"/>.
    /// </summary>
    protected bool NewSystem(WorldModel m)
    {
        AiSystem? best = null;
        foreach (var s in List)
            if (m.Systems[s] is var r && Own(r) && Calm(r) && r.FreeSlots > 0 && (best is null || K.Count(r) < K.Count(best))) best = r;
        if (best is null)
        {
            bool second = List.Count > 0;
            Func<AiSystem, bool> excl = r => K.SystemExclude(r) || r.Coruscant;
            var need = K.SectorBit;
            best = SectorThenSystem(
                () => second || K.BothSectorLists ? [m.SectorList(x => need(x), K.SectorValue, RankOrder.Ascending), m.SectorList(x => need(x), x => x.EnemySystems, RankOrder.Descending)] : [m.SectorList(x => need(x), K.SectorValue, RankOrder.Ascending)],
                x => second ? [m.SystemList(x, r => r.Own && !excl(r), K.SystemValue, RankOrder.Ascending), m.SystemList(x, r => r.Own && !excl(r), K.SecondValue, RankOrder.Ascending)] : [m.SystemList(x, r => r.Own && !excl(r), K.SystemValue, RankOrder.Ascending)]);
            if (best is null) return false;
            best.SetRole(K.Role, true);
            List.Add(best.System);
        }
        Build = best.System;
        return true;
    }

    /// <summary>
    /// T turn site: the nearest sector (distance to the build system, dir Descending; planner 5: by a rank value, dir Ascending) with an idle construction role, in
    /// it the own calm system with the construction role and an idle construction yard by a rank value (dir Ascending) gives its construction manager; usable yards = yards - 1 when above 1.
    /// </summary>
    protected bool TurnSite(WorldModel m)
    {
        if (Build is null) return false;
        var site = SectorThenSystem(
            () => [K.TurnByDistance ? m.SectorDistanceList(Build, x => x.IdleConstructionRole, RankOrder.Descending) : m.SectorList(x => x.IdleConstructionRole, x => x.RoleConstructionYards, RankOrder.Ascending)],
            x => [m.SystemList(x, r => r.IdleConstruction && r.ConstructionRole && r.Own && !r.Unrest, r => r.ConstructionYards, RankOrder.Ascending)]);
        if (site is null) return false;
        Builder = ManagerOf(site.System, ManufacturingKind.Construction);
        _usable = site.ConstructionYards > 1 ? site.ConstructionYards - 1 : site.ConstructionYards;
        return true;
    }

    /// <summary>
    /// F scrap in a system: Usable producers of the other kinds unless the system holds their role (family order), all Usable
    /// Death Star shields; nothing and no free energy: the first Usable Refinery (own Mines &lt; Refineries or no Mine there), else
    /// the first Usable Mine.
    /// </summary>
    protected static List<GameObject> ScrapOthers(WorldModel m, StarSystem s, ManufacturingKind kind)
    {
        var r = m.Systems[s];
        var take = new List<GameObject>();
        foreach (var k in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction })
            if (k != kind && !HasRoleFor(r, k)) take.AddRange(Usable(m, s, FamilyOf(k)));
        take.AddRange(Usable(m, s, Families.DeathStarShield));
        if (take.Count == 0 && r.FreeEnergy == 0)
        {
            if (m.Mines < m.Refineries || r.Mines == 0) take.AddRange(Usable(m, s, Families.Refinery).Take(1));
            else take.AddRange(Usable(m, s, Families.Mine).Take(1));
        }
        return take;
    }

    /// <summary>Result of the G count: an order (1), H scrap one when the budget is negative (-1), or nothing (0).</summary>
    protected enum OrderCountResult { ScrapOne, None, Order }

    /// <summary>G count: b &gt; 0 gives the producer type with the smallest ProcessingRate, count = b div its cost clamped to the usable yards, free slots and free energy (Order); b &lt; 0 gives ScrapOne (H); else None.</summary>
    protected OrderCountResult OrderCount(WorldModel m)
    {
        _fCount = 0; // G zeroes the count first
        int b = FacilityBudget(m);
        if (b < 0) return OrderCountResult.ScrapOne;
        if (b == 0 || m.Record(Build) is not { } r || m.PickProduct(Families.ConstructionYard, K.Producer, Criterion.ProcessingRate, RankOrder.Descending) is not ({ } product, > 0 and var cost)) return OrderCountResult.None;
        _fCount = Math.Min(Math.Min(b / cost, _usable), Math.Min(r.FreeSlots, r.FreeEnergy));
        _fProduct = product.Type;
        return _fCount > 0 ? OrderCountResult.Order : OrderCountResult.None;
    }

    /// <summary>
    /// H scrap one: the own list: a system without producers is dropped (role cleared); one with a Usable facility and >= 2
    /// producers, or 1 when the list holds more than one system (then dropped) gives its first Usable producer gives Scrap.
    /// </summary>
    protected Command? ScrapOne(WorldModel m, Owner side)
    {
        foreach (var s in List.ToList())
        {
            var r = m.Systems[s];
            if (K.Count(r) == 0) { r.SetRole(K.Role, false); List.Remove(s); continue; }
            if (!r.UsableProducer) continue;
            if (K.Count(r) == 1)
            {
                if (List.Count <= 1) continue;
                r.SetRole(K.Role, false);
                List.Remove(s);
            }
            if (Usable(m, s, FamilyOf(K.Kind)).FirstOrDefault() is { } x) return Scrap(side, [x]);
        }
        return null;
    }

    /// <summary>
    /// C request: b = facility budget; b >= 1: one sector (bit, value dir Ascending) picked once; when its value is &gt; 0 its
    /// systems (own, excl the clone's mask and Coruscant, by the count field dir Ascending) are popped until one with producers and
    /// field <= b gives joins the list with the role; else, with at most one listed system without producers, the cheapest-rate
    /// producer's cost <= b gives request bit. <see cref="Build"/> is set to the joined system, else none.
    /// </summary>
    protected void Request(WorldModel m)
    {
        Build = null; // C resets it at its start and sets it only to its own pick
        // Planner 5's clone returns here when its role list holds 2 or more systems: no budget, no sector list
        // (no draws), no join, no request bit. The clones of 1-3, 6, 7 have no such gate; 10 / 11 not rechecked.
        if (Type == PlannerType.SpecialForce && List.Count >= 2) return;
        int b = FacilityBudget(m);
        AiSystem? found = null;
        // Planner 3's clone pushes the sector list on its local stack but picks from the planner's own stack, which is always
        // empty there: the list is built (its tie draws) and nothing is picked or joined.
        var need = K.SectorBit;
        if (Type == PlannerType.Defence) { if (b >= 1) m.SectorList(x => need(x), K.SectorValue, RankOrder.Ascending); }
        else if (b >= 1 && PickOne(m.SectorList(x => need(x), K.SectorValue, RankOrder.Ascending)) is AiSector sector && K.SectorValue(sector) > 0)
        {
            var systems = new ListStack();
            systems.Push(m.SystemList(sector, r => r.Own && !K.SystemExclude(r) && !r.Coruscant, K.Count, RankOrder.Ascending));
            while (systems.Pick() is AiSystem s)
                if (K.Count(s) > 0 && K.Field(s) <= b) { found = s; break; }
        }
        if (found is not null)
        {
            found.SetRole(K.Role, true);
            List.Add(found.System);
            Build = found.System;
        }
        else if (List.Count <= 1 && (List.Count == 0 || K.Count(m.Systems[List.Min!]) == 0)
            && m.PickProduct(Families.ConstructionYard, K.Producer, Criterion.ProcessingRate, RankOrder.Descending) is ({ }, var cost) && cost <= b)
            m.TurnRequests[(int)K.TurnSlot] = true;
    }

    /// <summary>
    /// D hub: the list's system with strictly the most producers of the planner's own kind
    /// (planners 1-3, 5-6, 7 / 10 / 11; from 0) and garrison &gt; 0 whose hub mark, if any, is this planner's own;
    /// the old hub loses its hub mark, the chosen one (if any) gets it. Direct writes: nothing is dirtied.
    /// </summary>
    protected void Hub(WorldModel m)
    {
        Func<AiSystem, int> field = Type switch { PlannerType.Construction or PlannerType.Mine or PlannerType.Defence => YardsOf, PlannerType.SpecialForce or PlannerType.Troop => TrainingOf, _ => ShipyardsOf };
        AiSystem? best = null;
        int most = 0;
        foreach (var s in List)
        {
            var r = m.Systems[s];
            if (field(r) > most && r.UsableTroops > 0 && (!r.Hub || s == _hub)) (best, most) = (r, field(r));
        }
        if (_hub is not null) m.Systems[_hub].Hub = false;
        _hub = best?.System;
        if (best is not null) best.Hub = true;
    }

    /// <summary>
    /// Mine (J) / Refinery (I) site of planner 1 (planner 3 has its own): sector lists then system lists; fallback by distance
    /// to the construction manager (dir Descending).
    /// </summary>
    protected static AiSystem? MineOrRefinerySite(WorldModel m, GameObject? manager, bool refinery)
    {
        var (sectorReq, sectorBy, systemReq, systemExcl, systemBy, fallbackSector, fallbackSystem) = refinery
            ? ((Func<AiSector, bool>)(x => x.RefinerySite), new Func<AiSector, int>[] { x => x.Support, x => x.FreeEnergy },
                (Func<AiSystem, bool>)(r => r.RefinerySite && r.Own), (Func<AiSystem, bool>)(r => r.HasRole || r.Unrest),
                new Func<AiSystem, int>[] { r => r.FreeEnergy, r => r.Support }, (Func<AiSector, bool>)(x => x.HasFreeEnergy), (Func<AiSystem, bool>)(r => r.HasFreeEnergy))
            : ((Func<AiSector, bool>)(x => x.MineSite), new Func<AiSector, int>[] { x => x.Support, x => x.FreeRawMaterial, x => x.FreeEnergy },
                (Func<AiSystem, bool>)(r => r.MineSite), (Func<AiSystem, bool>)(r => false),
                new Func<AiSystem, int>[] { r => r.FreeRawMaterial, r => r.Support, r => r.FreeEnergy }, (Func<AiSector, bool>)(x => x.HasFreeRawMaterial),
                (Func<AiSystem, bool>)(r => r.HasFreeRawMaterial));
        var site = SectorThenSystem(() => sectorBy.Select(o => m.SectorList(x => sectorReq(x), o, RankOrder.Ascending)),
            x => systemBy.Select(o => m.SystemList(x, r => systemReq(r) && !systemExcl(r), o, RankOrder.Ascending)));
        if (site is not null || manager is null) return site;
        return SectorThenSystem(() => [m.SectorDistanceList(manager, x => fallbackSector(x), RankOrder.Descending)],
            x => [m.SystemDistanceList(x, manager, r => fallbackSystem(r), RankOrder.Descending)]);
    }

    // ---- facility phase (state machine: TemplateStep) ----

    /// <summary>Back to the unit phase, run finished.</summary>
    protected virtual void Exit() => Finish();
    /// <summary>N found nothing: default back to the unit phase.</summary>
    protected virtual Command? NewSystemFailed(WorldModel m, Owner side) { Exit(); return null; }
    protected void EnterFacility() => (Phase, State) = ((int)PlannerPhase.Facility, (int)TemplateStep.NewSystem);

    /// <summary>
    /// N gives turn (own request bit cleared; the turn is ours: advance it, then T) gives F gives G (order / H / back) gives SetProductionTarget gives ProduceFacility;
    /// not our turn or no T site gives C + D (the turn advances only with a T site).
    /// </summary>
    protected Command? Facility(WorldModel m, Owner side)
    {
        switch ((TemplateStep)State)
        {
            case TemplateStep.Start: // entered at state 0 (planners 5, 7): an empty first call
                State = (int)TemplateStep.NewSystem;
                return null;
            case TemplateStep.NewSystem:
                if (NewSystem(m)) { State = (int)TemplateStep.Turn; return null; }
                return NewSystemFailed(m, side);
            case TemplateStep.Turn:
                m.TurnRequests[(int)K.TurnSlot] = false;
                State = (int)TemplateStep.Request;
                // only the turn holder with a site advances the turn; without a site it keeps the turn and requests
                if (m.Turn == (int)K.TurnSlot && TurnSite(m))
                {
                    m.AdvanceTurn();
                    State = (int)TemplateStep.ScrapOthers;
                }
                return null;
            case TemplateStep.ScrapOthers:
                State = (int)TemplateStep.Count;
                return ScrapOthers(m, Build!, K.Kind) is { Count: > 0 } take ? Urgent(Scrap(side, take)) : null;
            case TemplateStep.Count:
                switch (OrderCount(m))
                {
                    case OrderCountResult.Order: State = (int)TemplateStep.SetTarget; return null;
                    case OrderCountResult.ScrapOne: State = (int)TemplateStep.ScrapOne; return null;
                    default: Exit(); return null;
                }
            case TemplateStep.ScrapOne:
            {
                var c = ScrapOne(m, side);
                Exit();
                return c;
            }
            case TemplateStep.SetTarget:
                if (Builder is null || Build is null) { Exit(); return null; }
                State = (int)TemplateStep.Produce;
                return Urgent(new Command(CommandType.SetProductionTarget, side) { Targets = [(Builder, GroupMode.Object)], ArgRef = Build });
            case TemplateStep.Produce:
                Exit();
                return new Command(CommandType.ProduceFacility, side) { Targets = [(Builder!, 0)], Fields = [_fProduct, (uint)_fCount] };
            default: // TemplateStep.Request
                Request(m);
                Hub(m);
                Exit();
                return null;
        }
    }
}

/// <summary>
/// Planner 1: construction yards, Mines, Refineries. p = 4. Phase 0 (A list walk, B new construction system, C
/// request, D hub) and facility phase states 1-10 (one state per call).
/// </summary>
public sealed class ConstructionPlanner() : ProductionPlanner(PlannerType.Construction)
{
    /// <summary>Planner 1's own states in the facility phase.</summary>
    enum ConstructionState
    {
        Start,
        /// <summary>Clear the request bit; advance the turn when it is ours.</summary>
        Turn,
        /// <summary>E: choose the build system.</summary>
        ChooseSystem,
        /// <summary>F: scrap the other producer kinds in the chosen system.</summary>
        ScrapOthers,
        /// <summary>G: order count.</summary>
        Count,
        /// <summary>H: scrap one construction yard.</summary>
        ScrapOne,
        /// <summary>Mine / Refinery site (no order count).</summary>
        Site,
        /// <summary>K: scrap in the chosen system (only without role bits).</summary>
        SiteScrap,
        /// <summary>SetProductionTarget command: set the production target.</summary>
        SetTarget,
        /// <summary>ProduceFacility command: produce the facility.</summary>
        Produce,
        /// <summary>C: request.</summary>
        Request,
    }

    const int Percent = 4;
    protected override Clone K { get; } = new(SystemRole.Construction, ManufacturingKind.Construction, YardsOf, ProductFilter.ConstructionYard, TurnSlot.Construction,
        x => x.ConstructionSlot, x => x.ConstructionYards, r => r.AnyConstructionRole || r.Unrest, YardsOf, FreeSlotsOf, 4, 100,
        r => r.ConstructionMaintenance);
    /// <summary>Chosen system, construction manager, product, count, free slots, yards.</summary>
    public StarSystem? Chosen { get; private set; }
    public ManufacturingManager? Manager { get; private set; }
    public uint Product { get; private set; }
    public int Count { get; private set; }
    int _free, _yards;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Finish(); return null; }
        var m = controller.Model;
        if (CurrentPhase is not (PlannerPhase.Start or PlannerPhase.Facility)) { (Phase, State) = ((int)PlannerPhase.Start, (int)ConstructionState.Start); return null; }
        return CurrentPhase == PlannerPhase.Start ? Phase0(m) : Construction(m, controller.Side);
    }

    static void TakeRole(AiSystem r) => r.TakeConstructionRole();

    Command? Phase0(WorldModel m)
    {
        // (A)
        AiSystem? chosen = null;
        foreach (var s in List.ToList())
        {
            var r = m.Systems[s];
            if (!Own(r) || !r.ConstructionRole) { r.SetRole(SystemRole.Construction, false); List.Remove(s); continue; }
            if (r.IdleConstruction && Calm(r)) { chosen = r; break; }
            if (List.Count == 1 && r.ConstructionYards == 0) { r.SetRole(SystemRole.Construction, false); List.Remove(s); }
        }
        // (B)
        if (chosen is null && List.Count == 0)
        {
            var sys = SectorThenSystem(
                () => [m.SectorList(x => x.IdleConstruction, x => x.EnemySystems, RankOrder.Descending), m.SectorList(x => x.IdleConstruction, x => x.ConstructionYards, RankOrder.Ascending), m.SectorList(x => x.IdleConstruction, x => x.Energy, RankOrder.Ascending)],
                x => [m.SystemList(x, r => r.IdleConstruction && r.Own && !r.AnyConstructionRole, r => r.ConstructionYards, RankOrder.Ascending), m.SystemList(x, r => r.IdleConstruction && r.Own && !r.AnyConstructionRole, r => r.Support, RankOrder.Ascending),
                    m.SystemList(x, r => r.IdleConstruction && r.Own && !r.AnyConstructionRole, r => r.FreeSlots, RankOrder.Ascending)]);
            sys ??= SectorThenSystem(() => [m.SectorList(x => x.AllHeld, x => x.RoleConstructionYards, RankOrder.Ascending)], x => [m.SystemList(x, r => r.Own, r => r.ConstructionYards, RankOrder.Ascending)]);
            if (sys is null) m.NoConstructionSite = true; // B ended without a system
            else
            {
                TakeRole(sys);
                List.Add(sys.System);
                if (sys.IdleConstruction) (chosen, State) = (sys, (int)ConstructionState.Start); // from B the state restarts
            }
        }
        if (chosen is not null)
        {
            // entering the facility phase from A keeps the state
            Manager = ManagerOf(chosen.System, ManufacturingKind.Construction);
            _yards = chosen.ConstructionYards;
            Phase = (int)PlannerPhase.Facility;
            return null;
        }
        if (!(m.Turn == (int)K.TurnSlot && !m.NoConstructionSite)) { Chosen = null; Request(m); }
        Hub(m);
        Done = true;
        return null;
    }

    Command? Construction(WorldModel m, Owner side)
    {
        switch ((ConstructionState)State)
        {
            case ConstructionState.Start: // the first call of the facility phase only sets state 1
                State = (int)ConstructionState.Turn;
                return null;
            case ConstructionState.Turn:
                m.TurnRequests[(int)K.TurnSlot] = false;
                if (m.Turn != (int)K.TurnSlot) { State = (int)ConstructionState.Request; return null; }
                m.AdvanceTurn();
                State = (int)ConstructionState.ChooseSystem;
                return null;
            case ConstructionState.ChooseSystem: // (E)
            {
                AiSystem? best = null;
                foreach (var s in List)
                    if (m.Systems[s] is var r && Own(r) && Calm(r) && r.FreeSlots > 0 && (best is null || r.ConstructionYards < best.ConstructionYards)) best = r;
                if (best is null)
                {
                    best = SectorThenSystem(
                        () => [m.SectorList(x => x.ConstructionSlot, x => x.EnemySystems, RankOrder.Descending), m.SectorList(x => x.ConstructionSlot, x => x.ConstructionYards, RankOrder.Ascending), m.SectorList(x => x.ConstructionSlot, x => x.Energy, RankOrder.Ascending)],
                        x => [m.SystemList(x, r => r.Own && !r.AnyConstructionRole && !r.Unrest && !r.Coruscant, r => r.ConstructionYards, RankOrder.Ascending), m.SystemList(x, r => r.Own && !r.AnyConstructionRole && !r.Unrest && !r.Coruscant, r => r.Support, RankOrder.Ascending),
                            m.SystemList(x, r => r.Own && !r.AnyConstructionRole && !r.Unrest && !r.Coruscant, r => r.FreeSlots, RankOrder.Ascending)]);
                    if (best is null) { Finish(); return null; }
                    TakeRole(best);
                    List.Add(best.System);
                }
                (Chosen, _free) = (best.System, best.FreeSlots);
                State = (int)ConstructionState.ScrapOthers;
                return null;
            }
            case ConstructionState.ScrapOthers: // (F)
            {
                var r = m.Systems[Chosen!];
                var take = new List<GameObject>();
                if (!r.ShipyardRole) take.AddRange(Usable(m, Chosen!, Families.Shipyard));
                if (!r.TrainingRole) take.AddRange(Usable(m, Chosen!, Families.TrainingFacility));
                take.AddRange(Usable(m, Chosen!, Families.DeathStarShield));
                if (take.Count == 0 && r.FreeEnergy == 0)
                {
                    if (m.Mines < m.Refineries || r.Mines == 0) take.AddRange(Usable(m, Chosen!, Families.Refinery).Take(1));
                    else take.AddRange(Usable(m, Chosen!, Families.Mine).Take(1));
                }
                State = (int)ConstructionState.Count;
                if (take.Count == 0) return null;
                Priority++;
                Done = true;
                return Scrap(side, take);
            }
            case ConstructionState.Count: // (G)
            {
                if (m.Record(Chosen) is not { } r) { Finish(); return null; }
                int b = m.FacilityBudget(Percent, 100, r => r.ConstructionMaintenance, List);
                if (b < 0) { State = (int)ConstructionState.ScrapOne; return null; }
                int count = 0;
                if (b > 0 && m.PickProduct(Families.ConstructionYard, ProductFilter.ConstructionYard, Criterion.ProcessingRate, RankOrder.Descending) is ({ } product, > 0 and var cost))
                {
                    count = Math.Min(Math.Min(b / cost, Math.Min(_free, r.FreeEnergy)), _yards);
                    Product = product.Type;
                }
                if (count > 0) { Count = count; State = (int)ConstructionState.SetTarget; }
                else State = (int)ConstructionState.Site;
                return null;
            }
            case ConstructionState.ScrapOne: // (H)
                foreach (var s in List.ToList())
                {
                    var r = m.Systems[s];
                    if (r.ConstructionYards == 0) { r.SetRole(SystemRole.Construction, false); List.Remove(s); continue; }
                    if (!r.UsableProducer) continue;
                    if (r.ConstructionYards == 1)
                    {
                        if (List.Count <= 1) break;
                        r.SetRole(SystemRole.Construction, false);
                        List.Remove(s);
                    }
                    if (Usable(m, s, Families.ConstructionYard).FirstOrDefault() is { } yard) { Finish(); return Scrap(side, [yard]); }
                }
                Finish();
                return null;
            case ConstructionState.Site:
            {
                bool refinery = m.Refineries < m.Mines;
                Product = refinery ? Refs.Refinery : Refs.Mine;
                var site = MineOrRefinerySite(m, Manager, refinery);
                // no site: phase 0 / state 0, then falls through into K on the previous system
                if (site is null) (Phase, State) = ((int)PlannerPhase.Start, (int)ConstructionState.Start);
                else (Chosen, Count, State) = (site.System, 1, (int)ConstructionState.SiteScrap);
                goto case ConstructionState.SiteScrap;
            }
            case ConstructionState.SiteScrap: // (K)
            {
                State = (int)ConstructionState.SetTarget;
                if (Chosen is null || m.Systems[Chosen].HasRole) return null;
                var take = Usable(m, Chosen, Families.Shipyard, Families.TrainingFacility, Families.ConstructionYard);
                if (take.Count == 0) return null;
                Priority++;
                Done = true;
                return Scrap(side, take);
            }
            case ConstructionState.SetTarget:
                if (Manager is null || Chosen is null) { Finish(); return null; }
                Priority++;
                Done = true;
                State = (int)ConstructionState.Produce;
                return new Command(CommandType.SetProductionTarget, side) { Targets = [(Manager, GroupMode.Object)], ArgRef = Chosen };
            case ConstructionState.Produce:
                Finish();
                return Manager is null ? null : new Command(CommandType.ProduceFacility, side) { Targets = [(Manager, GroupMode.Object)], Fields = [Product, (uint)Count] };
            case ConstructionState.Request: // (C) request
                Chosen = null;
                Request(m);
                Finish();
                return null;
            default:
                Finish();
                return null;
        }
    }


}

/// <summary>
/// Planner 14: names own completed, not travelling, unnamed capital ships (Rename command), up to 10 per run, from the
/// side's TEXTSTRA name lists by size class; active only while the model value is &gt; 0. Renames are not counted.
/// </summary>
public sealed class NamingPlanner() : Planner(PlannerType.Naming)
{
    uint? _saved;
    int _cursor = -1; // index into the ship records, -1 = 0 (not positioned)
    int _counter;
    /// <summary>Name list size classes (indices into <see cref="Used"/>).</summary>
    enum NameSize { Small, Medium, Large }
    /// <summary>Small, medium and large name counters.</summary>
    public int[] Used { get; } = new int[3];

    /// <summary>Load: the walk cursor (a record pointer) is not saved; the next step positions it at the saved key's record, else the first.</summary>
    internal override void AfterLoad() => _cursor = -1;

    /// <summary>
    /// Step: status != 1 gives counter 0, done, cursor 0. A run starts with one call that only
    /// positions the cursor (the record of the saved key if it is a capital ship, else the first). Then one record per
    /// call: an own, completed, not travelling, unnamed ship gets a Rename command. After 10 renames the next call saves the next
    /// record's key and ends; at the list end the counter is 0 and the saved key is cleared.
    /// </summary>
    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { (_counter, Done, _cursor) = (0, true, -1); return null; }
        var m = controller.Model;
        var keys = m.Ships.Keys.ToList();
        if (_cursor < 0)
        {
            int saved = _saved is { } k ? keys.IndexOf(k) : -1;
            _cursor = saved >= 0 ? saved : 0;
            return null;
        }
        if (_counter >= 10)
        {
            (_counter, Done) = (0, true);
            _saved = _cursor < keys.Count ? keys[_cursor] : null;
            _cursor = -1;
            return null;
        }
        if (_cursor >= keys.Count) { (_counter, Done, _saved, _cursor) = (0, true, null, -1); return null; }
        var r = m.Ships[keys[_cursor++]];
        // the advance that reaches the list end finishes the run in the same call (clearing the saved key is Inferred)
        if (_cursor >= keys.Count) (_counter, Done, _saved, _cursor) = (0, true, null, -1);
        if (!r.Own || (r.Enroute || r.NotCompleted || r.Named)) return null;
        if (!Done) _counter++;
        return new Command(CommandType.Rename, controller.Side) { Targets = [(r.Ship, GroupMode.Object)], Text = Name(m, controller, r.Ship.Type) };
    }

    /// <summary>Failure: priority cleared, the command dropped; the walk continues.</summary>
    public override void Failure(OrderResult result, Command command) => Priority = 0;

    /// <summary>TEXTSTRA id bases of the name lists (small, medium, large), per side.</summary>
    const int AllianceSmallNames = 20992, AllianceMediumNames = 21088, AllianceLargeNames = 21184;
    const int EmpireSmallNames = 20736, EmpireMediumNames = 20832, EmpireLargeNames = 20928;
    static readonly (int Base, int Count)[] AllianceLists = [(AllianceSmallNames, 20), (AllianceMediumNames, 35), (AllianceLargeNames, 16)];
    static readonly (int Base, int Count)[] EmpireLists = [(EmpireSmallNames, 39), (EmpireMediumNames, 34), (EmpireLargeNames, 32)];

    /// <summary>The next name of the ship's size list, larger ships falling back to smaller lists; none gives null (the current name is kept).</summary>
    string? Name(WorldModel m, ComputerController controller, uint type)
    {
        var p = m.Catalogue[type];
        int size = p is null ? -1 : p.LargeHull ? (int)NameSize.Large : p.MediumHull ? (int)NameSize.Medium : p.SmallHull ? (int)NameSize.Small : -1;
        var lists = controller.Side == Owner.Alliance ? AllianceLists : EmpireLists;
        for (int k = size; k >= 0; k--)
            if (Used[k] < lists[k].Count) return controller.Strings?.Invoke(lists[k].Base + Used[k]++);
        return null;
    }
}
