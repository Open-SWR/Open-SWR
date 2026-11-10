using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>The production window's four numbers and whether the order would pass.</summary>
public readonly record struct ProductionEstimate(int Maintenance, int Refined, int BuildDays, int DeliveryDays, bool Valid);

/// <summary>
/// Material points, processing facilities, maintenance and manufacturing managers.
/// </summary>
public static class Economy
{
    /// <summary>Refill events: raw and refined.</summary>
    const int RawRefillPriority = 32, RefinedRefillPriority = 33;

    // ---- facility state machine ----

    // Facility flag setters: each writes and runs the flag's hook only when the value changes.

    /// <summary>PointPresent setter; hook: if cleared, PointProcessed := false, then the state recompute.</summary>
    public static void SetPointPresent(GameState game, Facility f, bool value)
    {
        if (f.PointPresent == value) return;
        f.PointPresent = value;
        if (!value) SetPointProcessed(game, f, false);
        RecomputeState(game, f);
    }

    /// <summary>PointProcessed setter; hook: the state recompute.</summary>
    public static void SetPointProcessed(GameState game, Facility f, bool value)
    {
        if (f.PointProcessed == value) return;
        f.PointProcessed = value;
        RecomputeState(game, f);
    }

    /// <summary>
    /// Suspended setter; hook: set gives IDLE, and for a Mine/Refinery the activity check and the allocation reset (allocation := 0
    /// while suspended, capacity kept), so un-suspending wakes it with allocation 0.
    /// </summary>
    public static void SetSuspended(GameState game, Facility f, bool value)
    {
        if (f.Suspended == value) return;
        f.Suspended = value;
        // base hook (set gives IDLE); Mine/Refinery: the Usable hook's tail (activity check, allocation := 0)
        if (value) SetState(game, f, FacilityState.Idle);
        if (f.IsMineOrRefinery) ActivityTail(game, f);
    }

    /// <summary>OnStartupCycle setter; hook: the Mine/Refinery cycle length only.</summary>
    static void SetOnStartupCycle(GameState game, Facility f, bool value)
    {
        if (f.OnStartupCycle == value) return;
        f.OnStartupCycle = value;
        if (f.IsMineOrRefinery) RecomputeCycle(game, f);
    }

    /// <summary>Processing setter; hook: facility cycle timer and ETC only.</summary>
    static void SetProcessing(GameState game, Facility f, bool value)
    {
        if (f.Processing == value) return;
        f.Processing = value;
        ProcessingChanged(game, f, value);
    }

    // State recompute.
    static void RecomputeState(GameState game, Facility f)
    {
        var s = f.State;
        var next = f.Owner == Owner.Neutral ? FacilityState.Idle
            : s == FacilityState.Waiting && f.PointPresent ? FacilityState.Processing
            : s == FacilityState.Processing && f.PointProcessed ? FacilityState.Done
            : s == FacilityState.Done && !f.PointPresent ? FacilityState.Waiting
            : s;
        SetState(game, f, next);
    }

    /// <summary>
    /// State change from old to new (also balancing's wake and idle), in this order:
    /// 1. wait counter += 1 if old or new is WAITING; 2. OnStartupCycle := (old = IDLE) on new WAITING, 0 on new IDLE;
    /// 3. Processing := (new = PROCESSING); 4. state recompute (may recurse); 5. the OnStateChanged notification carrying new.
    /// Steps 2 and 3 never recompute the state (their hooks do not).
    /// </summary>
    public static void SetState(GameState game, Facility f, FacilityState next)
    {
        var old = f.State;
        if (old == next) return;
        f.State = next;
        if (old == FacilityState.Waiting || next == FacilityState.Waiting) f.WaitCounter++;
        if (next == FacilityState.Waiting) SetOnStartupCycle(game, f, old == FacilityState.Idle);
        else if (next == FacilityState.Idle) SetOnStartupCycle(game, f, false);
        SetProcessing(game, f, next == FacilityState.Processing);
        RecomputeState(game, f);
        Objects.Notify(game, g => OnStateChanged(g, f, next));
    }

    /// <summary>Processing set: start the facility cycle timer with Base = cycle length, ETC = Day + cycle length; cleared: stop it, ETC = 0.</summary>
    static void ProcessingChanged(GameState game, Facility f, bool on)
    {
        var timer = TimerCatalogue.Slot(game, TimerType.FacilityCycle, f.Ref, f);
        if (on)
        {
            timer.Start(game, f.CycleLength, 0);
            f.CompletionDay = game.Day + f.CycleLength;
        }
        else
        {
            timer.SetActive(game, false);
            f.CompletionDay = 0;
        }
    }

    /// <summary>Facility cycle timer action: in PROCESSING, PointProcessed := 1 (state goes to DONE).</summary>
    internal static void FacilityTimer(GameState game, Facility f)
    {
        if (f.State == FacilityState.Processing) SetPointProcessed(game, f, true);
    }

    /// <summary>
    /// Mine/Refinery cycle length: d = max(1, pct(capacity, CycleCapacityPercent)), k = ceildiv(allocated, d), p = pct(rate + k, modifier);
    /// with OnStartupCycle p = roll(p) + p div 2. No failure case.
    /// </summary>
    public static void RecomputeCycle(GameState game, Facility f)
    {
        int d = Pct(f.Capacity, game.Rules.CycleCapacityPercent);
        if (d == 0) d = 1;
        int p = Pct(f.ProcessingRate + CeilDiv(f.Allocated, d), f.Modifier);
        if (f.OnStartupCycle) p = game.Random.Roll(p, DrawSite.Cycle) + p / 2;
        f.CycleLength = p;
    }

    /// <summary>Mine/Refinery pair write: each write recomputes the cycle length.</summary>
    static void SetAllocated(GameState game, Facility f, int allocated)
    {
        if (f.Allocated == allocated) return;
        f.Allocated = allocated;
        RecomputeCycle(game, f);
    }

    /// <summary>
    /// Facility Usable flag hook. Base: cleared gives IDLE. Mine/Refinery, after the base hook: activity
    /// check (active iff Usable and not Suspended and owner is not 3; IDLE and active gives WAITING, which sets OnStartupCycle and
    /// draws the cycle), then not Usable, Suspended or owner 3 gives allocation := 0 (capacity unchanged).
    /// </summary>
    internal static void FacilityUsableChanged(GameState game, Facility f)
    {
        if (!f.Usable) SetState(game, f, FacilityState.Idle);
        if (f.IsMineOrRefinery) ActivityTail(game, f);
    }

    /// <summary>Mine/Refinery tail of the Usable, Suspended and owner hooks: activity check, then the allocation reset (allocation := 0 unless active).</summary>
    static void ActivityTail(GameState game, Facility f)
    {
        ActivityCheck(game, f);
        if (!f.Usable || f.Suspended || f.Owner == Owner.Neutral) SetAllocated(game, f, 0);
    }

    /// <summary>
    /// Mine/Refinery activity check: active iff Usable and not Suspended and owner is not 3 (the facility's own extra check always
    /// holds); IDLE and active gives WAITING; not active and not IDLE gives IDLE.
    /// </summary>
    static void ActivityCheck(GameState game, Facility f)
    {
        bool active = f.Usable && !f.Suspended && f.Owner != Owner.Neutral;
        if (active && f.State == FacilityState.Idle) SetState(game, f, FacilityState.Waiting);
        else if (!active && f.State != FacilityState.Idle) SetState(game, f, FacilityState.Idle);
    }

    /// <summary>
    /// Owner hook part for processing facilities. Mine/Refinery: set IDLE, pair reset to (50, 0) (one write, a
    /// cycle recompute if it changed), activity check (a wake draws the startup cycle with the current modifier); shipyards,
    /// training facilities, construction yards: set IDLE only.
    /// </summary>
    internal static void FacilityOwnerChanged(GameState game, Facility f)
    {
        SetState(game, f, FacilityState.Idle);
        if (!f.IsMineOrRefinery) return;
        if ((f.Capacity, f.Allocated) != (50, 0))
        {
            (f.Capacity, f.Allocated) = (50, 0);
            RecomputeCycle(game, f);
        }
        ActivityCheck(game, f);
    }

    // ---- facility notification handler ----

    /// <summary>
    /// Facility state handler: the carried new state selects the action, guards read the facility's current flags. Owner 1/2
    /// only; every stock change goes to the owner's side except a smuggled Mine/Refinery DONE point (opponent). Also run
    /// directly during new-game setup (registration).
    /// </summary>
    public static void OnStateChanged(GameState game, Facility f, FacilityState state)
    {
        if (f.Owner is not (Owner.Alliance or Owner.Empire) || f.Destroyed) return; // target must resolve
        var side = game.Sides[f.Owner];
        uint family = Family.Of(f.Type);
        if (state == FacilityState.Waiting)
        {
            if (f.PointPresent) return;
            if (family == Families.Mine) { SetPointPresent(game, f, true); return; } // from nowhere, state not checked
            if (f.State != FacilityState.Waiting) return;
            if (family == Families.Refinery)
            {
                if (!game.Ready) SetPointPresent(game, f, true); // free before the game-ready flag
                else if (side.RawStock > 0) { SetRaw(game, side, side.RawStock - 1); SetPointPresent(game, f, true); }
                else side.RefineryWaitList.Add(new(f, f.WaitCounter));
            }
            else if (side.RefinedStock > 0) { SetRefined(game, side, side.RefinedStock - 1); SetPointPresent(game, f, true); }
            else side.ManufacturingWaitList.Add(new(f, f.WaitCounter));
        }
        else if (state == FacilityState.Done)
        {
            // Smuggling draw: success = smuggled, the point goes to the opponent of the owner.
            bool smuggled = f.IsMineOrRefinery && f.Container is StarSystem { SmugglingPercent: not 0 } system
                && game.Random.Chance(Math.Abs(system.SmugglingPercent), DrawSite.Smuggling);
            if (!f.PointProcessed) return; // a stale DONE still delivers while PointProcessed is set
            SetPointPresent(game, f, false);
            var to = smuggled ? game.Sides[Systems.Opp(f.Owner)] : side;
            if (family == Families.Mine) SetRaw(game, to, to.RawStock + 1);
            else if (family == Families.Refinery) SetRefined(game, to, to.RefinedStock + 1);
            else DeliverPoint(game, f);
        }
    }

    // Wait lists and refills: a stock change from 0 to above 0 with waiters posts a refill event.
    static void SetRaw(GameState game, SideState side, int value)
    {
        bool refill = side.RawStock == 0 && value > 0 && side.RefineryWaitList.Count > 0;
        side.RawStock = value;
        if (refill) game.Clock.Post(new RefillEvent(side, raw: true));
    }

    /// <summary>Refined stock setter; also used by the scrap refund.</summary>
    internal static void SetRefined(GameState game, SideState side, int value)
    {
        bool refill = side.RefinedStock == 0 && value > 0 && side.ManufacturingWaitList.Count > 0;
        side.RefinedStock = value;
        if (refill) game.Clock.Post(new RefillEvent(side, raw: false));
    }

    /// <summary>
    /// Refill event (raw / refined): one run serves the head entry; a current entry (facility WAITING, counter
    /// unchanged) takes one point, a stale one is discarded. Re-inserted while waiters and stock remain.
    /// </summary>
    sealed class RefillEvent(SideState side, bool raw) : GameEvent
    {
        public override int Priority => raw ? RawRefillPriority : RefinedRefillPriority;

        public override bool Run(GameState game)
        {
            var waiting = raw ? side.RefineryWaitList : side.ManufacturingWaitList;
            int Stock() => raw ? side.RawStock : side.RefinedStock;
            if (waiting.Count > 0 && Stock() > 0)
            {
                var (f, counter) = waiting[0];
                waiting.RemoveAt(0);
                if (!f.Destroyed && f.State == FacilityState.Waiting && f.WaitCounter == counter)
                {
                    if (raw) SetRaw(game, side, side.RawStock - 1);
                    else SetRefined(game, side, side.RefinedStock - 1);
                    SetPointPresent(game, f, true);
                }
            }
            return waiting.Count > 0 && Stock() > 0;
        }
    }

    // ---- systems ----

    /// <summary>
    /// Garrisoned := >= 1 Usable troop (families 16 to 19, any owner) directly in the system; its hook: on 0 to 1
    /// after the game-ready flag in a Populated system owned by side 1/2, support change for the owner of 0 if its share >= LowSupportThreshold,
    /// else GeneralParam.LowSupportDelta; then the garrison support timer Active := Garrisoned (GarrisonSupportDays, no variance). Recomputed by the final init and
    /// the contents-change hook.
    /// </summary>
    public static void UpdateGarrison(GameState game, StarSystem system)
    {
        bool garrisoned = system.Contents.Any(o => Family.IsTroop(o.Type) && o.Usable);
        if (garrisoned == system.Garrisoned) return;
        system.Garrisoned = garrisoned;
        if (garrisoned && game.Ready && system.Populated && system.Owner is Owner.Alliance or Owner.Empire)
            Systems.ChangeSupport(game, system, system.Owner,
                system.SupportFor(system.Owner) >= game.Rules.LowSupportThreshold ? 0 : game.Rules.LowSupportDelta);
        var timer = TimerCatalogue.Slot(game, TimerType.GarrisonSupport, system.Ref, system);
        if (garrisoned) timer.Start(game, game.Rules.GarrisonSupportDays, 0);
        else timer.SetActive(game, false);
    }

    /// <summary>SmugglingPercent recompute (on a contents re-evaluation or a support change).</summary>
    public static void UpdateSmuggling(GameState game, StarSystem system) => system.SmugglingPercent = SmugglingPercent(game, system);

    /// <summary>
    /// SmugglingPercent. Every count keeps only objects with the Usable flag and the
    /// system owner's side: a = sum over owner fleets directly in the system of their direct contents in [24, 28),
    /// d = the same over [20, 28), c = owner fighters directly in the system, b = owner troops directly in the system.
    /// </summary>
    public static int SmugglingPercent(GameState game, StarSystem system)
    {
        var o = system.Owner;
        if (o is not (Owner.Alliance or Owner.Empire)) return 0;
        var g = game.Rules;
        int s = system.SupportFor(o);
        IEnumerable<GameObject> Own(GameObject container) => container.Contents.Where(x => x.Usable && x.Owner == o);
        var inFleets = Own(system).Where(x => Family.Of(x.Type) is >= Families.Fleet and < Families.Troop).SelectMany(Own).ToList();
        int a = inFleets.Count(x => Family.Of(x.Type) is >= Families.DeathStar and < Families.Fighter);
        int d = inFleets.Count(x => Family.Of(x.Type) is >= Families.CapitalShip and < Families.Fighter);
        int c = Own(system).Count(x => Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters);
        int b = Own(system).Count(x => Family.IsTroop(x.Type));
        int e = system.DeathStarNearby && o == Owner.Empire ? b * g.DeathStarSupportDivisor : b;
        int v = 0;
        if (s <= g.SmugglingMaxSupport && a == 0)
        {
            int @base = s <= g.SmugglingHighSupport ? g.SmugglingHighRate : s <= g.SmugglingMidSupport ? g.SmugglingMidRate : g.SmugglingLowRate;
            v = Clamp(@base - d * g.SmugglingShipPenalty - c * g.SmugglingFighterPenalty - e * g.SmugglingTroopPenalty, 0, 100);
        }
        return o == Owner.Empire ? -v : v;
    }

    // ---- manufacturing managers ----

    /// <summary>Facility family to manager kind: shipyard (family 40) is ship, training facility (41) is training, construction yard (42) is construction.</summary>
    public static ManufacturingKind KindOf(Facility f) => Family.Of(f.Type) switch
    {
        Families.Shipyard => ManufacturingKind.Ship,
        Families.TrainingFacility => ManufacturingKind.Training,
        Families.ConstructionYard => ManufacturingKind.Construction,
        var x => throw new InvalidOperationException($"Family {x} has no manufacturing manager."),
    };

    /// <summary>RequiredPointCount = RefinedMaterialCost of the head; TotalRequiredPointCount = sum over the queue.</summary>
    public static int Required(GameState game, ManufacturingManager m) => m.Orders.Count > 0 ? game.RefinedMaterialCost(m.Orders[0].Type) : 0;
    public static int TotalRequired(GameState game, ManufacturingManager m) => m.Orders.Sum(o => game.RefinedMaterialCost(o.Type));

    /// <summary>
    /// Members: Usable facilities [40, 44) directly in the
    /// manager's system (key order) of the manager's kind and owner; suspended ones included, so
    /// facilities not Completed or Enroute are never members. Used by balancing, the order split and research accrual.
    /// </summary>
    public static List<Facility> Members(ManufacturingManager m) =>
        m.System.Contents.OfType<Facility>().Where(f => f.Usable && Family.Of(f.Type) is >= Families.Shipyard and < Families.Mine && KindOf(f) == m.Kind && f.Owner == m.Owner).ToList();

    /// <summary>
    /// UI build-time estimate E(m, type, n, withProgress), a display value only, never sim state: over the
    /// non-suspended members, need = RefinedMaterialCost * n (minus Overflow + Completed with progress, floor 0); R = sum 100 div
    /// ProcessingRate; P(d) = sum t_f div ProcessingRate_f (with progress and a running cycle t_f = d + rate - (ETC - Day)); d0 =
    /// ceildiv(need * 100, R); P(d0) &lt; need: the first d up to d0 + max rate with P(d) >= need (else that bound); otherwise
    /// the smallest d below d0 still producing need. Null = no non-suspended member ((160, 3 / 4)). Queue position k (1-based):
    /// E(m, head type, k, true).
    /// </summary>
    public static int? EstimateDays(GameState game, ManufacturingManager m, uint type, int n, bool withProgress)
    {
        var fs = Members(m).Where(f => !f.Suspended && f.ProcessingRate > 0).ToList();
        if (fs.Count == 0) return null;
        int need = game.RefinedMaterialCost(type) * n;
        if (withProgress) need -= m.Overflow + m.CompletedPoints;
        need = Math.Max(0, need);
        int r = fs.Sum(f => 100 / f.ProcessingRate);
        int P(int d) => fs.Sum(f => (withProgress && f.CompletionDay != 0 ? d + f.ProcessingRate - (f.CompletionDay - game.Day) : d) / f.ProcessingRate);
        int d0 = r == 0 ? 0 : GameMath.CeilDiv(need * 100, r);
        if (P(d0) < need)
        {
            int hi = d0 + fs.Max(f => f.ProcessingRate);
            for (int d = d0; d <= hi; d++) if (P(d) >= need) return d;
            return hi;
        }
        for (int d = d0; d >= 0; d--) if (P(d) < need) return d + 1;
        return 0;
    }

    /// <summary>
    /// Production window estimate: validation of the ProduceFacility, ProduceShip or ProduceTroop command first (valid = (1, -1)); totals
    /// MaintenanceCost * count and RefinedMaterialCost * count; build days = max over the target managers of E(m, product, count, 0);
    /// delivery days = max over the managers of the travel days from the manager's system to its TargetKey's system with speed = the
    /// SD speed field for families [20, 32) if nonzero, else 100. A manager without a non-suspended member also makes it invalid.
    /// UI only.
    /// </summary>
    public static ProductionEstimate ProductionEstimate(GameState game, Command c)
    {
        uint product = c.Fields.ElementAtOrDefault(0);
        int count = (int)c.Fields.ElementAtOrDefault(1);
        bool valid = Orders.Validate(game, c).Success;
        int build = 0, delivery = 0;
        var record = game.Definitions.Resolve(product);
        int speed = record is not null && Family.Of(product) is >= Families.CapitalShip and < Families.Headquarters && record.Get(SdField.Hyperdrive) != 0 ? record.Get(SdField.Hyperdrive) : 100;
        foreach (var m in c.Targets.Select(t => t.Target).OfType<ManufacturingManager>())
        {
            if (EstimateDays(game, m, product, count, false) is { } d) build = Math.Max(build, d);
            else valid = false;
            if ((m.TargetKey as StarSystem ?? m.TargetKey.System) is { } to) delivery = Math.Max(delivery, Orders.TravelDays(game, m.System, to, speed));
        }
        return new(record is null ? 0 : game.MaintenanceCost(product) * count, record is null ? 0 : game.RefinedMaterialCost(product) * count, build, delivery, valid);
    }

    /// <summary>Delivered point: Completed &lt; Required gives Completed += 1, else Overflow += 1; then balancing.</summary>
    static void DeliverPoint(GameState game, Facility f)
    {
        var m = ((StarSystem)f.Container!).Managers[(int)KindOf(f)];
        if (m.CompletedPoints < Required(game, m)) SetCompleted(game, m, m.CompletedPoints + 1);
        else m.Overflow++;
        Balance(game, m);
    }

    /// <summary>
    /// Completed setter: stores and runs its hook only when the value changes. Hook: Completed is not 0 and
    /// >= Required gives the head unit completion: Constructed is set, EnrouteActive is set if the unit's location
    /// and the manager's are both set and differ, and Completed is set (Usable recompute, then the Completed notification (OnCompleted)).
    /// </summary>
    static void SetCompleted(GameState game, ManufacturingManager m, int value)
    {
        if (m.CompletedPoints == value) return;
        m.CompletedPoints = value;
        if (value == 0 || m.Orders.Count == 0 || value < Required(game, m)) return;
        var unit = m.Orders[0];
        unit.Constructed = true;
        if (unit.System is { } at && at != m.System) Objects.SetEnrouteActive(game, unit, true);
        Objects.SetCompleted(game, unit);
    }

    /// <summary>
    /// Queue update for one unit: member iff Created and not Completed and not Destroyed and not Enroute and
    /// BuilderKey = m. On a membership change the unit joins (key order) or leaves the queue; the count setter's
    /// change to 0 resets TargetKey to the own system, then Completed and Overflow. Then k = min(Required, Overflow), Completed := k, Overflow -= k, balancing.
    /// </summary>
    public static void UpdateQueue(GameState game, ManufacturingManager m, GameObject unit)
    {
        bool member = unit.Created && !unit.Completed && !unit.Destroyed && !unit.Enroute
            && unit.BuilderKey == m;
        if (member != m.Orders.Contains(unit))
        {
            if (member) m.Orders.Insert(m.Orders.FindLastIndex(o => o.Key <= unit.Key) + 1, unit);
            else m.Orders.Remove(unit);
            if (m.Orders.Count == 0) { m.TargetKey = m.System; SetCompleted(game, m, 0); m.Overflow = 0; } // Count setter hook
        }
        int k = Math.Min(Required(game, m), m.Overflow);
        SetCompleted(game, m, k);
        m.Overflow -= k;
        Balance(game, m);
    }

    /// <summary>
    /// TargetKey reset, from the OnOwnerChanged handler of any object, OnExistingChanged of a Destroyed one, and OnBlockadeChanged of a Blockade set: every manager with an empty queue
    /// whose TargetKey is the object gets its own system back; managers with a queue keep it.
    /// </summary>
    internal static void ResetTargets(GameState game, GameObject o)
    {
        foreach (var s in game.Systems)
            foreach (var m in s.Managers)
                if (m is { Orders.Count: 0 } && m.TargetKey == o) m.TargetKey = s;
    }

    /// <summary>
    /// Facility balancing: need = TotalRequired - Overflow - Completed. Picks re-walk members in key order and keep
    /// only a strictly better one, so ties go to the first in key order. Waking needs the Usable flag and
    /// Suspended clear; otherwise the pick stays IDLE and still counts.
    /// </summary>
    public static void Balance(GameState game, ManufacturingManager m)
    {
        var members = Members(m);
        if (members.Count == 0)
        {
            // No members: every queued unit gets DestroyedReason ProductionCancelled; the setter never fails here.
            foreach (var unit in m.Orders.ToList()) Objects.Destroy(game, unit, DestroyedReason.ProductionCancelled);
            return;
        }
        var usable = members.Where(f => !f.Suspended).ToList();
        int need = TotalRequired(game, m) - m.Overflow - m.CompletedPoints;
        int active = usable.Count(f => f.State != FacilityState.Idle);
        if (active < need)
        {
            for (int i = Math.Min(need - active, usable.Count(f => f.State == FacilityState.Idle)); i > 0; i--)
            {
                var pick = usable.Where(f => f.State == FacilityState.Idle).MinBy(f => f.ProcessingRate); // first of equal minima
                if (pick is null) break;
                if (pick.Usable) SetState(game, pick, FacilityState.Waiting);
            }
        }
        else
        {
            int x = active - need;
            foreach (var state in new[] { FacilityState.Waiting, FacilityState.Processing, FacilityState.Done })
                while (x > 0 && usable.Where(f => f.State == state).MaxBy(f => f.ProcessingRate) is { } pick) // first of equal maxima
                {
                    SetState(game, pick, FacilityState.Idle);
                    x--;
                }
        }
    }

    // ---- side totals and maintenance ----

    /// <summary>Side totals recompute for side 1, then side 2 (during new-game setup, and on the Uprising and Blockade change notifications).</summary>
    public static void RecomputeSideTotals(GameState game)
    {
        RecomputeSide(game, Owner.Alliance);
        RecomputeSide(game, Owner.Empire);
    }

    /// <summary>
    /// One side: demand = sum MaintenanceCost over the side's Existing objects of families [16, 64); Mine then
    /// Refinery re-allocation over the Usable, non-suspended ones; maintenance pair; exceeded flag.
    /// </summary>
    public static void RecomputeSide(GameState game, Owner owner)
    {
        if (owner is not (Owner.Alliance or Owner.Empire)) return;
        int demand = Placed(game).Where(o => o.Existing && o.Owner == owner && Family.Of(o.Type) is >= Families.Troop and < Families.Mission).Sum(o => game.MaintenanceCost(o.Type));
        var (mineCapacity, mines) = Reallocate(game, owner, Families.Mine, demand);
        var (refineryCapacity, refineries) = Reallocate(game, owner, Families.Refinery, demand);
        var side = game.Sides[owner];
        side.Demand = demand;
        side.MaintenanceCapacity = Math.Min(mineCapacity, refineryCapacity);
        side.MaintenanceAllocated = Math.Min(Math.Max(mines, refineries), side.MaintenanceCapacity);
        UpdateExceeded(game, side);
    }

    /// <summary>
    /// Spreads min(demand, C) - T over the side's Usable, non-suspended facilities of one family in registry order, in passes;
    /// every changed pair recomputes that facility's cycle length. Returns the final (C, T).
    /// </summary>
    static (int Capacity, int Allocated) Reallocate(GameState game, Owner side, uint family, int demand)
    {
        var facilities = game.Facilities.Where(f => f.Owner == side && Family.Of(f.Type) == family && f.Usable && !f.Suspended)
            .OrderBy(f => f.Key).ToList();
        int c = facilities.Sum(f => f.Capacity), t = facilities.Sum(f => f.Allocated);
        int d = Math.Min(demand, c) - t;
        bool increase = d > 0;
        d = Math.Abs(d);
        for (bool again = d > 0; again && d > 0;)
        {
            again = false;
            foreach (var f in facilities)
            {
                if (d == 0) break;
                int delta = c == 0 ? 0 : increase ? t * f.Capacity / c - f.Allocated + 1 : f.Allocated - (t - 1) * f.Capacity / c;
                delta = Clamp(delta, 0, increase ? Math.Min(d, f.Capacity - f.Allocated) : d);
                if (delta > 0)
                {
                    SetAllocated(game, f, f.Allocated + (increase ? delta : -delta));
                    t += increase ? delta : -delta;
                    d -= delta;
                }
                if (increase ? f.Allocated != f.Capacity : f.Allocated != 0) again = true;
            }
        }
        return (c, t);
    }

    /// <summary>exceeded := demand &gt; capacity; a change starts (MaintenanceEnforcementDays, no variance) or stops the maintenance timer.</summary>
    static void UpdateExceeded(GameState game, SideState side)
    {
        bool exceeded = side.Demand > side.MaintenanceCapacity;
        if (exceeded == side.Exceeded) return;
        side.Exceeded = exceeded;
        var timer = TimerCatalogue.Slot(game, TimerType.Maintenance, side.Ref, side);
        if (exceeded) timer.Start(game, game.Rules.MaintenanceEnforcementDays, 0);
        else timer.SetActive(game, false);
    }

    /// <summary>
    /// Maintenance timer (fires only while exceeded: clearing the flag stops the timer): candidates = the side's Existing, Completed
    /// objects of families [16, 64) with MaintenanceCost != 0 in registry order; roll(n - 1) picks one for autoscrap.
    /// Inferred, not modelled: the destruction that follows the request.
    /// </summary>
    internal static void Autoscrap(GameState game, SideState side)
    {
        var owner = game.Sides.First(s => s.Value == side).Key;
        var candidates = Placed(game).Where(o => o.Existing && o.Completed && o.Owner == owner && Family.Of(o.Type) is >= Families.Troop and < Families.Mission
            && game.MaintenanceCost(o.Type) != 0).OrderBy(o => o.Key).ToList();
        if (candidates.Count > 0) candidates[game.Random.Roll(candidates.Count - 1, DrawSite.Autoscrap)].AutoscrapRequested = true;
    }

    /// <summary>All objects inside systems (destroyed objects have left their container).</summary>
    static IEnumerable<GameObject> Placed(GameState game) => game.Systems.SelectMany(Descendants);

    static IEnumerable<GameObject> Descendants(GameObject o) => o.Contents.SelectMany(c => Descendants(c).Prepend(c));
}
