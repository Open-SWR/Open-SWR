namespace OpenSWR.Sim.Ai;

/// <summary>
/// Clone parameters of the producer-facility template: role, producer kind and its count, product mask, turn bit, N sector list
/// (need, value), N system list (exclude mask, value, second value), facility budget (p, g, maintenance), T variant (by distance,
/// else planner 5's role-construction-yard list).
/// </summary>
public sealed record Clone(SystemRole Role, ManufacturingKind Kind, Func<AiSystem, int> Count, ProductFilter Producer, int TurnSlot, Func<AiSector, bool> SectorBit,
    Func<AiSector, int> SectorValue, Func<AiSystem, bool> SystemExclude, Func<AiSystem, int> SystemValue, Func<AiSystem, int> SecondValue, int Percent, int G,
    Func<AiSystem, int> Field, bool TurnByDistance = true, bool BothSectorLists = false);

/// <summary>
/// Shared machinery of the production planners: own role list, list picks, commands, and the
/// producer-facility template steps R, N, T, F, G, H, 0x214 / 0x210, C, D with the facility-phase state machine.
/// </summary>
public abstract class ProductionPlanner(int type) : Planner(type)
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

    protected static bool OwnFree(AiFleet f) => f.Own && f.Free;
    protected static bool OwnFreeShip(AiShip s) => s.Own && s.Free;
    protected static bool HeldWithRoom(AiFleet f) => f.Own && f.Garrison && f.HasFighterRoom;
    protected static bool FreeWithRoom(AiFleet f) => f.Own && f.Free && f.HasFighterRoom;
    protected static bool Own(AiSystem r) => r.Own;
    /// <summary>No Uprising, no Blockade.</summary>
    protected static bool Calm(AiSystem r) => !r.Unrest;


    protected void Finish() => (Phase, State, Done) = (0, 0, true);

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
    protected static uint FamilyOf(ManufacturingKind k) => k switch { ManufacturingKind.Ship => 0x28u, ManufacturingKind.Training => 0x29u, _ => 0x2Au };

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

    protected static Command Scrap(Owner side, IEnumerable<GameObject> objects) => new(0x200, side) { Targets = [.. objects.Select(o => (o, 0))] };

    /// <summary>A command that ends this run with priority +1 (the planner stays urgent for its next step).</summary>
    protected Command Urgent(Command c) { Priority++; Done = true; return c; }

    protected int FacilityBudget(WorldModel m) => m.FacilityBudget(K.Percent, K.G, K.Field, List);

    /// <summary>
    /// R recheck (clones): not own / role lost -> role cleared, dropped; the first role system with producers of the
    /// kind, its manager of that kind idle and no Uprising / Blockade -> chosen. Planners 5, 6, 7, 10, 11 share their site through
    /// model slots: the own slot is cleared, a find is written to it; otherwise the fallback slots in order, each
    /// accepted only when own, with the role, producers, the idle bit and calm.
    /// </summary>
    protected AiSystem? Recheck(WorldModel m)
    {
        var (own, fallbacks) = Type switch
        {
            5 => (0x18, new[] { 0x14 }), 6 => (0x14, new[] { 0x18 }), 7 => (0x1C, new[] { 0x20, 0x24 }),
            10 => (0x20, new[] { 0x1C, 0x24 }), 11 => (0x24, new[] { 0x1C }), _ => (0, Array.Empty<int>()),
        };
        if (own != 0) m.SiteSlots[own] = null;
        bool Fits(AiSystem r) => K.Count(r) > 0 && IsIdle(r, K.Kind) && Calm(r);
        foreach (var s in List.ToList())
        {
            var r = m.Systems[s];
            if (!Own(r) || !r.HasRoleOf(K.Role)) { r.SetRole(K.Role, false); List.Remove(s); continue; }
            if (Fits(r))
            {
                if (own != 0) m.SiteSlots[own] = s;
                return r;
            }
        }
        foreach (int slot in fallbacks)
            if (m.Record(m.SiteSlots.GetValueOrDefault(slot)) is { } r && Own(r) && r.HasRoleOf(K.Role) && Fits(r)) return r;
        return null;
    }

    /// <summary>
    /// N new system: the own list's own calm system with free slots and the fewest producers (first on ties); none: a sector list
    /// (a second by a rank value, dir 2, only when the list is non-empty; planner 2 always pushes both) and a system list
    /// (own, excl the clone's mask and Coruscant; a second by the clone's second value only then) -> role, joins the list. Sets
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
                () => second || K.BothSectorLists ? [m.SectorList(x => need(x), K.SectorValue, 1), m.SectorList(x => need(x), x => x.EnemySystems, 2)] : [m.SectorList(x => need(x), K.SectorValue, 1)],
                x => second ? [m.SystemList(x, r => r.Own && !excl(r), K.SystemValue, 1), m.SystemList(x, r => r.Own && !excl(r), K.SecondValue, 1)] : [m.SystemList(x, r => r.Own && !excl(r), K.SystemValue, 1)]);
            if (best is null) return false;
            best.SetRole(K.Role, true);
            List.Add(best.System);
        }
        Build = best.System;
        return true;
    }

    /// <summary>
    /// T turn site: the nearest sector (distance to the build system, dir 2; planner 5: by a rank value, dir 1) with an idle construction role, in
    /// it the own calm system with the construction role and an idle construction yard by a rank value (dir 1) -> its construction manager; usable yards = yards - 1 when above 1.
    /// </summary>
    protected bool TurnSite(WorldModel m)
    {
        if (Build is null) return false;
        var site = SectorThenSystem(
            () => [K.TurnByDistance ? m.SectorDistanceList(Build, x => x.IdleConstructionRole, 2) : m.SectorList(x => x.IdleConstructionRole, x => x.RoleConstructionYards, 1)],
            x => [m.SystemList(x, r => r.IdleConstruction && r.ConstructionRole && r.Own && !r.Unrest, r => r.ConstructionYards, 1)]);
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
        take.AddRange(Usable(m, s, 0x25));
        if (take.Count == 0 && r.FreeEnergy == 0)
        {
            if (m.Mines < m.Refineries || r.Mines == 0) take.AddRange(Usable(m, s, 0x2D).Take(1));
            else take.AddRange(Usable(m, s, 0x2C).Take(1));
        }
        return take;
    }

    /// <summary>G count: b &gt; 0 -> the producer type with the smallest ProcessingRate, count = b div its cost clamped to the usable yards, free slots and free energy (1 = order); b &lt; 0 -> -1 (H); else 0.</summary>
    protected int OrderCount(WorldModel m)
    {
        _fCount = 0; // G zeroes the count first
        int b = FacilityBudget(m);
        if (b < 0) return -1;
        if (b == 0 || m.Record(Build) is not { } r || m.PickProduct(0x2A, K.Producer, Criterion.ProcessingRate, 2) is not ({ } product, > 0 and var cost)) return 0;
        _fCount = Math.Min(Math.Min(b / cost, _usable), Math.Min(r.FreeSlots, r.FreeEnergy));
        _fProduct = product.Type;
        return _fCount > 0 ? 1 : 0;
    }

    /// <summary>
    /// H scrap one: the own list: a system without producers is dropped (role cleared); one with a Usable facility and >= 2
    /// producers, or 1 when the list holds more than one system (then dropped) -> its first Usable producer -> 0x200.
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
    /// C request: b = facility budget; b >= 1: one sector (bit, value dir 1) picked once; when its value is &gt; 0 its
    /// systems (own, excl the clone's mask and Coruscant, by the count field dir 1) are popped until one with producers and
    /// field <= b -> joins the list with the role; else, with at most one listed system without producers, the cheapest-rate
    /// producer's cost <= b -> request bit. <see cref="Build"/> is set to the joined system, else none.
    /// </summary>
    protected void Request(WorldModel m)
    {
        Build = null; // C resets it at its start and sets it only to its own pick
        // Planner 5's clone returns here when its role list holds 2 or more systems: no budget, no sector list
        // (no draws), no join, no request bit. The clones of 1-3, 6, 7 have no such gate; 10 / 11 not rechecked.
        if (Type == 5 && List.Count >= 2) return;
        int b = FacilityBudget(m);
        AiSystem? found = null;
        // Planner 3's clone pushes the sector list on its local stack but picks from the planner's own stack, which is always
        // empty there: the list is built (its tie draws) and nothing is picked or joined.
        var need = K.SectorBit;
        if (Type == 3) { if (b >= 1) m.SectorList(x => need(x), K.SectorValue, 1); }
        else if (b >= 1 && PickOne(m.SectorList(x => need(x), K.SectorValue, 1)) is AiSector sector && K.SectorValue(sector) > 0)
        {
            var systems = new ListStack();
            systems.Push(m.SystemList(sector, r => r.Own && !K.SystemExclude(r) && !r.Coruscant, K.Count, 1));
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
            && m.PickProduct(0x2A, K.Producer, Criterion.ProcessingRate, 2) is ({ }, var cost) && cost <= b)
            m.TurnRequests[K.TurnSlot] = true;
    }

    /// <summary>
    /// D hub: the list's system with strictly the most producers of the planner's own kind
    /// (planners 1-3, 5-6, 7 / 10 / 11; from 0) and garrison &gt; 0 whose hub mark, if any, is this planner's own;
    /// the old hub loses its hub mark, the chosen one (if any) gets it. Direct writes: nothing is dirtied.
    /// </summary>
    protected void Hub(WorldModel m)
    {
        Func<AiSystem, int> field = Type switch { <= 3 => YardsOf, 5 or 6 => TrainingOf, _ => ShipyardsOf };
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
    /// to the construction manager (dir 2).
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
        var site = SectorThenSystem(() => sectorBy.Select(o => m.SectorList(x => sectorReq(x), o, 1)),
            x => systemBy.Select(o => m.SystemList(x, r => systemReq(r) && !systemExcl(r), o, 1)));
        if (site is not null || manager is null) return site;
        return SectorThenSystem(() => [m.SectorDistanceList(manager, x => fallbackSector(x), 2)],
            x => [m.SystemDistanceList(x, manager, r => fallbackSystem(r), 2)]);
    }

    // ---- facility phase 0x3EC (state machines; internal state numbers) ----

    protected const int FacilityPhase = 0x3EC;
    protected const int FN = 100, FTurn = 101, FScrap = 102, FCount = 103, FScrapOne = 104, F214 = 105, F210 = 106, FRequest = 107;

    /// <summary>Back to the unit phase, run finished.</summary>
    protected virtual void Exit() => Finish();
    /// <summary>N found nothing: default back to the unit phase.</summary>
    protected virtual Command? NewSystemFailed(WorldModel m, Owner side) { Exit(); return null; }
    protected void EnterFacility() => (Phase, State) = (FacilityPhase, FN);

    /// <summary>
    /// N -> turn (own request bit cleared; the turn is ours: advance it, then T) -> F -> G (order / H / back) -> 0x214 -> 0x210;
    /// not our turn or no T site -> C + D (the turn advances only with a T site).
    /// </summary>
    protected Command? Facility(WorldModel m, Owner side)
    {
        switch (State)
        {
            case 0: // entered at state 0 (planners 5, 7): an empty first call
                State = FN;
                return null;
            case FN:
                if (NewSystem(m)) { State = FTurn; return null; }
                return NewSystemFailed(m, side);
            case FTurn:
                m.TurnRequests[K.TurnSlot] = false;
                State = FRequest;
                // only the turn holder with a site advances the turn; without a site it keeps the turn and requests
                if (m.Turn == K.TurnSlot && TurnSite(m))
                {
                    m.AdvanceTurn();
                    State = FScrap;
                }
                return null;
            case FScrap:
                State = FCount;
                return ScrapOthers(m, Build!, K.Kind) is { Count: > 0 } take ? Urgent(Scrap(side, take)) : null;
            case FCount:
                switch (OrderCount(m))
                {
                    case 1: State = F214; return null;
                    case -1: State = FScrapOne; return null;
                    default: Exit(); return null;
                }
            case FScrapOne:
            {
                var c = ScrapOne(m, side);
                Exit();
                return c;
            }
            case F214:
                if (Builder is null || Build is null) { Exit(); return null; }
                State = F210;
                return Urgent(new Command(0x214, side) { Targets = [(Builder, 0)], ArgRef = Build });
            case F210:
                Exit();
                return new Command(0x210, side) { Targets = [(Builder!, 0)], Fields = [_fProduct, (uint)_fCount] };
            default: // FRequest
                Request(m);
                Hub(m);
                Exit();
                return null;
        }
    }
}

/// <summary>
/// Planner 1: construction yards, Mines, Refineries. p = 4. Phase 0 (A list walk, B new construction system, C
/// request, D hub) and phase 0x3EC states 1-10 (one state per call).
/// </summary>
public sealed class ConstructionPlanner() : ProductionPlanner(1)
{
    const int Percent = 4;
    protected override Clone K { get; } = new(SystemRole.Construction, ManufacturingKind.Construction, YardsOf, ProductFilter.ConstructionYard, 0,
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
        if (Status != 1) { Finish(); return null; }
        var m = controller.Model;
        if (Phase is not (0 or 0x3EC)) { (Phase, State) = (0, 0); return null; }
        return Phase == 0 ? Phase0(m) : Construction(m, controller.Side);
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
                () => [m.SectorList(x => x.IdleConstruction, x => x.EnemySystems, 2), m.SectorList(x => x.IdleConstruction, x => x.ConstructionYards, 1), m.SectorList(x => x.IdleConstruction, x => x.Energy, 1)],
                x => [m.SystemList(x, r => r.IdleConstruction && r.Own && !r.AnyConstructionRole, r => r.ConstructionYards, 1), m.SystemList(x, r => r.IdleConstruction && r.Own && !r.AnyConstructionRole, r => r.Support, 1),
                    m.SystemList(x, r => r.IdleConstruction && r.Own && !r.AnyConstructionRole, r => r.FreeSlots, 1)]);
            sys ??= SectorThenSystem(() => [m.SectorList(x => x.AllHeld, x => x.RoleConstructionYards, 1)], x => [m.SystemList(x, r => r.Own, r => r.ConstructionYards, 1)]);
            if (sys is null) m.NoConstructionSite = true; // B ended without a system
            else
            {
                TakeRole(sys);
                List.Add(sys.System);
                if (sys.IdleConstruction) (chosen, State) = (sys, 0); // from B the state restarts
            }
        }
        if (chosen is not null)
        {
            // entering 0x3EC from A keeps the state
            Manager = ManagerOf(chosen.System, ManufacturingKind.Construction);
            _yards = chosen.ConstructionYards;
            Phase = 0x3EC;
            return null;
        }
        if (!(m.Turn == K.TurnSlot && !m.NoConstructionSite)) { Chosen = null; Request(m); }
        Hub(m);
        Done = true;
        return null;
    }

    Command? Construction(WorldModel m, Owner side)
    {
        switch (State)
        {
            case 0: // the first call of 0x3EC only sets state 1
                State = 1;
                return null;
            case 1:
                m.TurnRequests[K.TurnSlot] = false;
                if (m.Turn != K.TurnSlot) { State = 10; return null; }
                m.AdvanceTurn();
                State = 2;
                return null;
            case 2: // (E)
            {
                AiSystem? best = null;
                foreach (var s in List)
                    if (m.Systems[s] is var r && Own(r) && Calm(r) && r.FreeSlots > 0 && (best is null || r.ConstructionYards < best.ConstructionYards)) best = r;
                if (best is null)
                {
                    best = SectorThenSystem(
                        () => [m.SectorList(x => x.ConstructionSlot, x => x.EnemySystems, 2), m.SectorList(x => x.ConstructionSlot, x => x.ConstructionYards, 1), m.SectorList(x => x.ConstructionSlot, x => x.Energy, 1)],
                        x => [m.SystemList(x, r => r.Own && !r.AnyConstructionRole && !r.Unrest && !r.Coruscant, r => r.ConstructionYards, 1), m.SystemList(x, r => r.Own && !r.AnyConstructionRole && !r.Unrest && !r.Coruscant, r => r.Support, 1),
                            m.SystemList(x, r => r.Own && !r.AnyConstructionRole && !r.Unrest && !r.Coruscant, r => r.FreeSlots, 1)]);
                    if (best is null) { Finish(); return null; }
                    TakeRole(best);
                    List.Add(best.System);
                }
                (Chosen, _free) = (best.System, best.FreeSlots);
                State = 3;
                return null;
            }
            case 3: // (F)
            {
                var r = m.Systems[Chosen!];
                var take = new List<GameObject>();
                if (!r.ShipyardRole) take.AddRange(Usable(m, Chosen!, 0x28));
                if (!r.TrainingRole) take.AddRange(Usable(m, Chosen!, 0x29));
                take.AddRange(Usable(m, Chosen!, 0x25));
                if (take.Count == 0 && r.FreeEnergy == 0)
                {
                    if (m.Mines < m.Refineries || r.Mines == 0) take.AddRange(Usable(m, Chosen!, 0x2D).Take(1));
                    else take.AddRange(Usable(m, Chosen!, 0x2C).Take(1));
                }
                State = 4;
                if (take.Count == 0) return null;
                Priority++;
                Done = true;
                return Scrap(side, take);
            }
            case 4: // (G)
            {
                if (m.Record(Chosen) is not { } r) { Finish(); return null; }
                int b = m.FacilityBudget(Percent, 100, r => r.ConstructionMaintenance, List);
                if (b < 0) { State = 5; return null; }
                int count = 0;
                if (b > 0 && m.PickProduct(0x2A, ProductFilter.ConstructionYard, Criterion.ProcessingRate, 2) is ({ } product, > 0 and var cost))
                {
                    count = Math.Min(Math.Min(b / cost, Math.Min(_free, r.FreeEnergy)), _yards);
                    Product = product.Type;
                }
                if (count > 0) { Count = count; State = 8; }
                else State = 6;
                return null;
            }
            case 5: // (H)
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
                    if (Usable(m, s, 0x2A).FirstOrDefault() is { } yard) { Finish(); return Scrap(side, [yard]); }
                }
                Finish();
                return null;
            case 6:
            {
                bool refinery = m.Refineries < m.Mines;
                Product = refinery ? 0x2D000002u : 0x2C000001u;
                var site = MineOrRefinerySite(m, Manager, refinery);
                // no site: phase 0 / state 0, then falls through into K on the previous system
                if (site is null) (Phase, State) = (0, 0);
                else (Chosen, Count, State) = (site.System, 1, 7);
                goto case 7;
            }
            case 7: // (K)
            {
                State = 8;
                if (Chosen is null || m.Systems[Chosen].HasRole) return null;
                var take = Usable(m, Chosen, 0x28, 0x29, 0x2A);
                if (take.Count == 0) return null;
                Priority++;
                Done = true;
                return Scrap(side, take);
            }
            case 8:
                if (Manager is null || Chosen is null) { Finish(); return null; }
                Priority++;
                Done = true;
                State = 9;
                return new Command(0x214, side) { Targets = [(Manager, 0)], ArgRef = Chosen };
            case 9:
                Finish();
                return Manager is null ? null : new Command(0x210, side) { Targets = [(Manager, 0)], Fields = [Product, (uint)Count] };
            case 10: // (C) request
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
/// Planner 14: names own completed, not travelling, unnamed capital ships (0x203), up to 10 per run, from the
/// side's TEXTSTRA name lists by size class; active only while the model value is &gt; 0. Renames are not counted.
/// </summary>
public sealed class NamingPlanner() : Planner(14)
{
    uint? _saved;
    int _cursor = -1; // index into the ship records, -1 = 0 (not positioned)
    int _counter;
    /// <summary>Small, medium and large name counters.</summary>
    public int[] Used { get; } = new int[3];

    /// <summary>Load: the walk cursor (a record pointer) is not saved; the next step positions it at the saved key's record, else the first.</summary>
    internal override void AfterLoad() => _cursor = -1;

    /// <summary>
    /// Step: status != 1 -> counter 0, done, cursor 0. A run starts with one call that only
    /// positions the cursor (the record of the saved key if it is a capital ship, else the first). Then one record per
    /// call: an own, completed, not travelling, unnamed ship gets a 0x203. After 10 renames the next call saves the next
    /// record's key and ends; at the list end the counter is 0 and the saved key is cleared.
    /// </summary>
    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { (_counter, Done, _cursor) = (0, true, -1); return null; }
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
        return new Command(0x203, controller.Side) { Targets = [(r.Ship, 0)], Text = Name(m, controller, r.Ship.Type) };
    }

    /// <summary>Failure: priority cleared, the command dropped; the walk continues.</summary>
    public override void Failure(OrderResult result, Command command) => Priority = 0;

    static readonly (int Base, int Count)[][] Lists =
    [
        [(0x5200, 20), (0x5260, 35), (0x52C0, 16)], // Alliance: small, medium, large
        [(0x5100, 39), (0x5160, 34), (0x51C0, 32)], // Empire
    ];

    /// <summary>The next name of the ship's size list, larger ships falling back to smaller lists; none -> null (the current name is kept).</summary>
    string? Name(WorldModel m, ComputerController controller, uint type)
    {
        var p = m.Catalogue[type];
        int size = p is null ? -1 : p.LargeHull ? 2 : p.MediumHull ? 1 : p.SmallHull ? 0 : -1;
        var lists = Lists[controller.Side == Owner.Alliance ? 0 : 1];
        for (int k = size; k >= 0; k--)
            if (Used[k] < lists[k].Count) return controller.Strings?.Invoke(lists[k].Base + Used[k]++);
        return null;
    }
}
