using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>
/// Unit movement (departure, travel, arrival), placeholder fleets, the placement solver, the deployment check and the
/// Battle/Blockade evaluation. Battle consequences are in <see cref="Combat"/>.
/// </summary>
public static class Fleets
{
    /// <summary>Fleet type SdRef of placeholders.</summary>
    const uint FleetType = 134217732; // family 8, index 4

    /// <summary>A capital ship (20-27) of the fleet's owner that is Existing, Completed and not EnrouteActive.</summary>
    internal static bool HasReadyShip(Fleet f) =>
        f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Owner == f.Owner && x.Existing && x.Completed && !x.EnrouteActive);

    /// <summary>Can travel. Base: Constructed. Fleets need a ready capital ship.</summary>
    internal static bool CanTravel(GameObject o) => o is Fleet f ? HasReadyShip(f) : o.Constructed;

    // ---- departure, travel, arrival ----

    /// <summary>
    /// Move (manager 249), the run of the Move order event and of mission travel: an object that cannot travel is only
    /// re-parented; else, if HyperdriveActive is clear, EnrouteActive := (locations differ); unless Destroyed,
    /// parent := destination. Its handler starts the travel.
    /// </summary>
    public static void Move(GameState game, GameObject o, GameObject destination)
    {
        if (!CanTravel(o)) { SetParent(game, o, destination); return; }
        if (!o.HyperdriveActive) Objects.SetEnrouteActive(game, o, Missions.Location(o) != Missions.Location(destination));
        if (!o.Destroyed) SetParent(game, o, destination);
    }

    /// <summary>
    /// Set parent with the fleet override: a fleet accepts a system; moved into a fleet it stays and every content
    /// that is Existing and Completed is re-parented into the destination fleet (each with its own handler); other destinations are
    /// refused.
    /// </summary>
    public static void SetParent(GameState game, GameObject o, GameObject destination)
    {
        if (o is not Fleet fleet || destination is StarSystem) { Objects.SetParent(game, o, destination); return; }
        if (destination is not Fleet target) return;
        foreach (var x in fleet.Contents.Where(x => x.Existing && x.Completed).ToList()) Objects.SetParent(game, x, target);
    }

    /// <summary>
    /// Travel start (249) from the location of <paramref name="from"/> to that of <paramref name="to"/>: only if the
    /// object can travel, HyperdriveActive is clear and both locations exist and differ. t = travel days between
    /// two systems, else (an abode end) 1; EnrouteActive := 1, travel timer Base := t (Variance 0), DepDest := the
    /// destination location, HyperdriveActive := 1 (slot Active to roll(0), post the HyperdriveChanged notification), ETA := Day + t.
    /// </summary>
    public static void Travel(GameState game, GameObject o, GameObject? from, GameObject? to)
    {
        if (!CanTravel(o) || o.HyperdriveActive) return;
        GameObject? a = from is null ? null : Missions.Location(from), b = to is null ? null : Missions.Location(to);
        if (a is null || b is null || a == b) return;
        int t = a is StarSystem x && b is StarSystem y ? Orders.TravelDays(game, x, y, Orders.Speed(game, o)) : 1;
        Objects.SetEnrouteActive(game, o, true);
        TimerCatalogue.Slot(game, TimerType.Travel, o.Ref, o).SetBase(game, t);
        o.Destination = b;
        Objects.SetHyperdriveActive(game, o, true);
        o.Eta = game.Day + t;
    }

    /// <summary>
    /// HyperdriveChanged handler: skipped for a Destroyed object. Characters and special forces (48-63): on 1 to 0 their
    /// mission runs its arrival check. Others: if <see cref="NewLeg"/> reports arrival, EnrouteActive := 0 (Usable
    /// cascade, notifications); no container change, no deployment.
    /// </summary>
    internal static void OnHyperdriveChanged(GameState game, GameObject o, bool value)
    {
        if (o.Destroyed) return;
        if (Family.Of(o.Type) is >= Families.MajorCharacter and < Families.Mission)
        {
            if (!value && o is Agent { Mission: { } m }) Missions.ArrivalCheck(game, m);
            return;
        }
        if (NewLeg(game, o)) Objects.SetEnrouteActive(game, o, false);
    }

    /// <summary>
    /// Manager 249 new-leg check: if the location differs from DepDest (re-parented in hyperspace), a new leg from DepDest to the
    /// location; returns arrived = not HyperdriveActive.
    /// </summary>
    internal static bool NewLeg(GameState game, GameObject o)
    {
        if (o.Destination is { } departure && Missions.Location(o) != departure) Travel(game, o, departure, o);
        return !o.HyperdriveActive;
    }

    /// <summary>
    /// Deployment check (249), no RNG: 1. not Destroyed but the parent is: the parent-destroyed hook (Completed and not EnrouteActive:
    /// Inferred "destroyed with the parent", not modelled; else Autorouting; characters by their own rule); 2. unless Autorouting or Destroyed: the parent's
    /// acceptance (same owner; systems also accept troops of any owner when unpopulated) refused to capture; 3. unless Autorouting or
    /// Destroyed: sandbox side = owner, type 1008, mode 1 (Usable), the object placed into its current parent (its own demand
    /// counts only if it is not Usable, Inferred), refused to overflow; 4. unless Autorouting or Destroyed: if (not Completed and located
    /// away from its BuilderKey's system) or Enroute, in a blockaded system whose blockading side differs from its owner, and not a
    /// fleet or capital ship to overflow.
    /// </summary>
    internal static void DeploymentCheck(GameState game, GameObject o)
    {
        if (!o.Destroyed && o.Container is { Destroyed: true }) ParentDestroyed(game, o, cascade: false);
        bool Done() => o.Autorouting || o.Destroyed;
        if (Done() || o.Container is not { } parent) return;
        if (o.Owner != parent.Owner && !(parent is StarSystem { Populated: false } && Family.IsTroop(o.Type))) Systems.Capture(game, o, parent.Owner);
        if (Done()) return;
        var sandbox = new Sandbox(mode: 1);
        if (o.Usable) sandbox.Commit(parent, o.Type, -1); // already counted in the parent: adds nothing (Inferred)
        if (!CanPlace(game, parent, o.Type, o.Owner, CommandType.DestroyedParentPlacement, sandbox, o)) Systems.Overflow(game, o);
        if (Done()) return;
        if ((!o.Completed && o.System != o.BuilderKey?.System || o.Enroute) && o.System is { Blockade: true } s
            && Combat.BlockadingSide(s) != o.Owner && o is not Fleet && !Family.IsCapitalShip(o.Type))
            Systems.Overflow(game, o);
    }

    /// <summary>
    /// Parent destroyed, object hook (<paramref name="cascade"/> = argument 1 from the parent's Destroyed hook,
    /// else 0 from the deployment check): characters (<see cref="Prisoners.ParentDestroyed"/>); fleets:
    /// nothing; special forces: cascade to not EnrouteActive and not Destroyed to destroyed with the parent, deployment to 
    /// not OnMission and not Destroyed to Autorouting; others (argument ignored): Completed and not EnrouteActive to destroyed with
    /// the parent, else Autorouting unless Destroyed.
    /// </summary>
    internal static void ParentDestroyed(GameState game, GameObject o, bool cascade)
    {
        if (o is Character c) Prisoners.ParentDestroyed(game, c);
        else if (o is Fleet) return;
        else if (Family.IsSpecialForce(o.Type))
        {
            if (cascade) { if (!o.EnrouteActive && !o.Destroyed) DestroyedWithParent(game, o); }
            else if (o is Agent { OnMission: false, Destroyed: false }) Objects.SetAutorouting(game, o);
        }
        else if (o.Completed && !o.EnrouteActive) DestroyedWithParent(game, o);
        else if (!o.Destroyed) Objects.SetAutorouting(game, o);
    }

    /// <summary>
    /// If the direct parent is Destroyed, DestroyedReason := the parent's (its own full Destroyed hook runs at once,
    /// depth-first inside the parent's content loop; loss records and refunds follow from its parent change).
    /// </summary>
    static void DestroyedWithParent(GameState game, GameObject o)
    {
        if (o.Container is { Destroyed: true } p) Objects.Destroy(game, o, p.DestroyedReason);
    }

    // ---- autorouting ----

    /// <summary>
    /// Handler for Autorouting := 1: nothing for a Destroyed object; Completed characters / special forces go
    /// to manager 248 (<see cref="Prisoners.Autoroute"/>) and stop there. Seed: in production, the BuilderKey's system if it
    /// differs from the object's; Completed and EnrouteActive, DepDest unless it is the parent. In a system: destination search
    /// (flag 0, stay 1), none to DestroyedReason.NoDestination; Completed, another system and speed 0 to DestroyedReason.CannotTravel; else move there.
    /// </summary>
    internal static void OnAutorouting(GameState game, GameObject o)
    {
        if (o.Destroyed) return;
        bool completed = o.Completed;
        if (Family.Of(o.Type) is >= Families.MajorCharacter and < Families.Mission && completed)
        {
            if (o is Agent a) Prisoners.Autoroute(game, a);
            Story.Notified(game, o); // 248: after the grouping
            return;
        }
        GameObject? seed = null;
        if (!completed) { if (o.System is { } here && o.BuilderKey?.System is { } builder && here != builder) seed = builder; }
        else if (o.EnrouteActive && o.Destination != o.Container) seed = o.Destination;
        if (o.System is not { } from) return;
        if (Search(game, o, from, seed, flag: false, stay: true) is not { } d) { Objects.Destroy(game, o, DestroyedReason.NoDestination); return; }
        if (completed && d.System != o.System && Orders.Speed(game, o) == 0) Objects.Destroy(game, o, DestroyedReason.CannotTravel);
        if (!o.Destroyed) Move(game, o, d);
    }

    /// <summary>
    /// Destination search, no RNG. Candidate test: sandbox side = owner, type CreateMoveMission for characters /
    /// special forces, else MoveObject, mode 3; with flag 0 the object's current parent refuses every category Refusal.DestinationClosed; the solver's
    /// choice is the result. Stages while nothing is found: 1 stay (if <paramref name="stay"/>): the parent chain; 2 the seed's
    /// chain (unless it is the parent); over the distance list of <paramref name="from"/>: 3 own unblockaded systems, 4 own
    /// Existing fleets per system, 5 own blockaded systems, 6 neutral systems, 7 (owner != 3) enemy systems. Stages 2-7 test with
    /// flag 0. Abodes (stage 1 / 2 skip) are not modelled.
    /// </summary>
    internal static GameObject? Search(GameState game, GameObject o, StarSystem from, GameObject? seed, bool flag, bool stay)
    {
        var type = Family.Of(o.Type) is >= Families.MajorCharacter and < Families.Mission ? CommandType.CreateMoveMission : CommandType.MoveObject;
        GameObject? Test(GameObject candidate, bool f)
        {
            var sandbox = new Sandbox();
            if (!f && o.Container is { } parent) sandbox.Close(parent); // flag 0 closes o's current parent
            return Place(game, candidate, o.Type, o.Owner, type, sandbox, o);
        }
        if (stay && o.Container is { } p)
            foreach (var c in Chain(p)) if (Test(c, flag) is { } r) return r;
        if (seed is not null && seed != o.Container)
            foreach (var c in Chain(seed)) if (Test(c, false) is { } r) return r;
        var list = Distances(game, from);
        var own = o.Owner;
        IEnumerable<GameObject> candidates = list.Where(x => x.Owner == own && !x.Blockade).Cast<GameObject>()
            .Concat(list.SelectMany(x => x.Contents.OfType<Fleet>().Where(f => f.Owner == own && f.Existing)))
            .Concat(list.Where(x => x.Owner == own && x.Blockade))
            .Concat(list.Where(x => x.Owner == Owner.Neutral))
            .Concat(own == Owner.Neutral ? [] : list.Where(x => x.Owner == Systems.Opp(own)));
        foreach (var c in candidates) if (Test(c, false) is { } r) return r;
        return null;

        // P if a capital ship, fleet or system; P's parent if P is a ship or fleet; P's grandparent if P is a ship
        static IEnumerable<GameObject> Chain(GameObject p)
        {
            bool ship = Family.IsCapitalShip(p.Type), fleet = p is Fleet;
            if (ship || fleet || p is StarSystem) yield return p;
            if (!(ship || fleet) || p.Container is not { } q) yield break;
            yield return q;
            if (ship && q.Container is { } r) yield return r;
        }
    }

    /// <summary>
    /// Distance list of a system: 200 entries (system, isqrt' distance) by id - 100, null for ids
    /// without a system, sorted by the CRT qsort with comparator (non-null first, ascending distance, ties 0); the
    /// non-null entries in that order.
    /// </summary>
    internal static IReadOnlyList<StarSystem> Distances(GameState game, StarSystem a)
    {
        if (game.DistanceLists.TryGetValue(a, out var list)) return list;
        var entries = new (StarSystem? S, int D)[200];
        foreach (var b in game.Systems) entries[b.Id - 100] = (b, Orders.Distance(a, b));
        Qsort(entries, (x, y) => (x.S, y.S) switch { (null, null) => 0, (null, _) => 1, (_, null) => -1, _ => x.D.CompareTo(y.D) });
        return game.DistanceLists[a] = [.. entries.Where(e => e.S is not null).Select(e => e.S!)];
    }

    /// <summary>
    /// The MSVC CRT qsort (CUTOFF 8): shortsort (max to the end) for ranges <= 8, else median swap to lo, partition,
    /// the smaller part pushed, LIFO stack. Not stable; ties keep exactly this order.
    /// </summary>
    public static void Qsort<T>(T[] a, Comparison<T> cmp)
    {
        if (a.Length < 2) return;
        var stack = new Stack<(int, int)>();
        int lo = 0, hi = a.Length - 1;
        while (true)
        {
            bool more = false;
            if (hi - lo + 1 <= 8)
                for (int h = hi; h > lo; h--)
                {
                    int max = lo;
                    for (int p = lo + 1; p <= h; p++) if (cmp(a[p], a[max]) > 0) max = p;
                    (a[max], a[h]) = (a[h], a[max]);
                }
            else
            {
                int mid = lo + (hi - lo + 1) / 2;
                (a[mid], a[lo]) = (a[lo], a[mid]);
                int i = lo, j = hi + 1;
                while (true)
                {
                    do i++; while (i <= hi && cmp(a[i], a[lo]) <= 0);
                    do j--; while (j > lo && cmp(a[j], a[lo]) >= 0);
                    if (j < i) break;
                    (a[i], a[j]) = (a[j], a[i]);
                }
                (a[lo], a[j]) = (a[j], a[lo]);
                if (j - 1 - lo >= hi - i)
                {
                    if (lo + 1 < j) stack.Push((lo, j - 1));
                    if (i < hi) { lo = i; more = true; }
                }
                else
                {
                    if (i < hi) stack.Push((i, hi));
                    if (lo + 1 < j) { hi = j - 1; more = true; }
                }
            }
            if (more) continue;
            if (stack.Count == 0) return;
            (lo, hi) = stack.Pop();
        }
    }

    // ---- system hooks, Battle / Blockade ----

    /// <summary>
    /// System content hook: for troops control, Garrisoned, surplus and (a Usable troop) its WithdrawPercent copy in
    /// that order (others: Garrisoned); a fighter reported Usable directly here sets BattleReady; for fleets, the
    /// placeholder upkeep, BattleReady, the fleet Blockade flags and the system flag pulse (post the pulse, priority 1024).
    /// ObservedBy; the BattleWithdrew step; facilities: the Mine / Refinery modifier copy and manager balancing. Inferred: a UsableChanged notification of a Usable fleet /
    /// fighter into the system also sets BattleReady.
    /// </summary>
    internal static void SystemContentUsableChanged(GameState game, StarSystem s, GameObject o)
    {
        if (Family.IsFacility(o.Type) && s.Destroyed && s.Populated)
        {
            s.Populated = false; // Destroyed to Populated := 0
            PulseBattle(game, s); // Populated hook pulses the system flag, not gated
        }
        if (Family.IsTroop(o.Type)) Systems.StoreControl(game, s, Systems.Control(game, s));
        Economy.UpdateGarrison(game, s);
        if (Family.IsTroop(o.Type))
        {
            Systems.UpdateSurplus(game, s);
            if (o.Usable) o.WithdrawPercent = s.WithdrawPercent;
        }
        if (o is Facility f)
        {
            // A Mine / Refinery turning Usable takes the system's ProductionModifier
            if (f.IsMineOrRefinery && f.Usable && f.Modifier != s.ProductionModifier)
            {
                f.Modifier = s.ProductionModifier;
                Economy.RecomputeCycle(game, f);
            }
            // either direction: a shipyard first writes CombatUnitFastRepair, then the balancing of
            // the matching manager. Balancing runs on a change to a member's Usable state.
            if (Family.Of(f.Type) == Families.Shipyard) Repair.SetCombatUnitFastRepair(game, s, Repair.ShipyardActive(s));
            if (Family.Of(f.Type) is >= Families.Shipyard and < Families.ManufacturingEnd && s.Managers.FirstOrDefault(m => m?.Kind == Economy.KindOf(f)) is { } manager)
                Economy.Balance(game, manager);
        }
        bool reported = o.Usable && o.Container == s;
        if (Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters && reported) s.BattleReady = o.Owner;
        // A fleet / fighter turning un-Usable while Enroute and Existing in a BattlePending system,
        // owner != the BattleWon record: the first such owner (side 1 / 2) becomes BattleWithdrew. A departure reaches it through the UsableChanged notification
        // on the parent captured at post time, i.e. the departure system. Its place in the hook order is Inferred.
        if ((o is Fleet || Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters) && !o.Usable && s.BattlePending && o.Enroute && o.Existing
            && o.Owner != s.BattleWon && s.BattleWithdrew == Owner.None && o.Owner is Owner.Alliance or Owner.Empire)
            s.BattleWithdrew = o.Owner;
        // fighter branch [28, 32) (this hook = the direct parent captured at post time): system flag pulse, not gated
        if (Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters) PulseBattle(game, s);
        if (o is not Fleet) return;
        Knowledge.RecomputeSystem(game, s);
        Upkeep(game, s);
        if (reported) s.BattleReady = o.Owner;
        Combat.SetFleetBlockade(s, s.Blockade);
        PulseBattle(game, s);
    }

    /// <summary>System flag pulse (a hook): post the pulse (priority 1024) to Battle/Blockade evaluation.</summary>
    internal static void PulseBattle(GameState game, StarSystem s) => Objects.Notify(game, g => EvaluateBattle(g, s), Objects.SystemPulsePriority);

    /// <summary>
    /// Pulse handler: p_s = side s has a Usable fleet or Usable fighter directly in the system; BattlePending := p_1 and p_2;
    /// Blockade := not BattlePending and (Populated or owner != 3) and a Usable fleet with owner != the system's owner; both through their
    /// hooks (<see cref="Combat.SetBattlePending"/>, <see cref="Combat.SetBlockade"/>). One system flag (Inferred) is not kept.
    /// </summary>
    static void EvaluateBattle(GameState game, StarSystem s)
    {
        bool Present(Owner side) => s.Contents.Any(x => x.Owner == side && x.Usable && (x is Fleet || Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters));
        Combat.SetBattlePending(game, s, Present(Owner.Alliance) && Present(Owner.Empire));
        Combat.SetBlockade(game, s, !s.BattlePending && (s.Populated || s.Owner != Owner.Neutral) && s.Contents.OfType<Fleet>().Any(f => f.Usable && f.Owner != s.Owner));
    }

    // ---- placeholder fleets ----

    /// <summary>
    /// Placeholder upkeep of a system (reserve, then new-fleet), each for sides 1 then 2, any system owner.
    /// Over the system's fleets in key order: a reserve keeps its flag unless Destroyed; a new-fleet placeholder keeps it only while
    /// empty and not Destroyed; a side without one gets a new placeholder (one counter id). No RNG.
    /// </summary>
    internal static void Upkeep(GameState game, StarSystem s)
    {
        Pass(reserve: true, f => !f.Destroyed);
        Pass(reserve: false, f => !f.Destroyed && f.Contents.Count == 0);

        // one pass over the reserve placeholders, or over the new-fleet ones
        void Pass(bool reserve, Func<Fleet, bool> keeps)
        {
            var sides = new HashSet<Owner>();
            foreach (var f in s.Contents.OfType<Fleet>().Where(f => reserve ? f.Reserve : f.NewFleet).ToList())
                if (keeps(f)) sides.Add(f.Owner);
                else if (reserve) f.Reserve = false;
                else f.NewFleet = false;
            foreach (var side in new[] { Owner.Alliance, Owner.Empire })
                if (!sides.Contains(side))
                {
                    var created = Objects.Create(game, new Fleet(FleetType, Owner.Neutral, game.NextId()), side, s, created: false, completed: false);
                    if (reserve) created.Reserve = true;
                    else created.NewFleet = true;
                }
        }
    }

    /// <summary>
    /// Fleet state when a capital ship enters or leaves: with a capital ship of its owner (Existing) the fleet
    /// becomes real (the Real flag, Created, Completed); without one a real fleet is destroyed (DestroyedReason.Scrapped).
    /// </summary>
    internal static void ShipsChanged(GameState game, Fleet f)
    {
        if (f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Owner == f.Owner && x.Existing))
        {
            f.Real = true;
            Objects.SetCreated(game, f);
            Objects.SetCompleted(game, f);
        }
        else if (f.Real) Objects.Destroy(game, f, DestroyedReason.Scrapped);
    }

    // ---- placement solver ----

    /// <summary>
    /// The validation's sandbox: unit commits per container (add 1 placed, -1 removed) on top of the real contents, so the units
    /// of one order compete for the same capacity; contents count by the sandbox mode (1 Usable, 3 Existing).
    /// </summary>
    public sealed class Sandbox(int mode = 3)
    {
        /// <summary>
        /// Second side: command sandboxes take the targets' common owner (the first side is the side passed to the
        /// solver = the command's side); null = no separate second side (equal to the first).
        /// </summary>
        public Owner? Second { get; init; }
        readonly List<(GameObject Container, uint Type, int Add)> _commits = [];
        readonly HashSet<GameObject> _closed = [];
        public void Commit(GameObject container, uint type, int add) => _commits.Add((container, type, add));
        /// <summary>All nine category records of the container := error Refusal.DestinationClosed.</summary>
        internal void Close(GameObject container) => _closed.Add(container);
        internal bool Closed(GameObject container) => _closed.Contains(container);
        internal Sandbox Copy()
        {
            var copy = new Sandbox(mode) { Second = Second };
            copy._commits.AddRange(_commits);
            copy._closed.UnionWith(_closed);
            return copy;
        }

        /// <summary>Units of families [lo, hi) in the container: direct contents matching the mode plus the commits.</summary>
        internal int Count(GameObject container, uint lo, uint hi) =>
            container.Contents.Count(x => (mode == 1 ? x.Usable : x.Existing) && Family.Of(x.Type) >= lo && Family.Of(x.Type) < hi)
            + _commits.Where(c => c.Container == container && Family.Of(c.Type) >= lo && Family.Of(c.Type) < hi).Sum(c => c.Add);
    }

    /// <summary>
    /// Solver: candidates = the destination; for a system the side's placeholder fleet there (new-fleet for CreateFleet, else
    /// reserve; first in key order, not Destroyed), then, only while the best so far is invalid, the capital ships of the side's
    /// Usable fleets; for a fleet its capital ships (the side's, Usable, key order). The destination is the initial best; every
    /// later candidate replaces it only if strictly better (<see cref="Better"/>). A valid best is committed to the
    /// sandbox. Null = no container.
    /// </summary>
    public static GameObject? Place(GameState game, GameObject destination, uint type, Owner side, CommandType commandType, Sandbox? sandbox = null, GameObject? unit = null) =>
        Place(game, destination, type, side, commandType, out _, sandbox, unit);

    /// <summary>
    /// Solver with its error: no valid candidate to the kept best's error (<see cref="Better"/>);
    /// the solver itself fails (destination not a system, fleet, capital ship or abode) gives Refusal.WrongClass.
    /// </summary>
    public static GameObject? Place(GameState game, GameObject destination, uint type, Owner side, CommandType commandType, out OrderResult error, Sandbox? sandbox = null, GameObject? unit = null)
    {
        var place = Solve(destination, side, commandType, c =>
        {
            var e = Accept(game, c, type, side, commandType, sandbox, unit);
            return (e, e.Success ? Remaining(game, c, type, sandbox) : 0);
        }, out error);
        if (place is not null) sandbox?.Commit(place, type, 1);
        return place;
    }

    static bool Supported(GameObject destination) =>
        destination is StarSystem or Fleet || Family.IsCapitalShip(destination.Type) || Family.Of(destination.Type) == Families.Abode;

    /// <summary>
    /// Candidate loop over <paramref name="evaluate"/> (error, remaining capacity; int.MaxValue = unlimited).
    /// Returns the valid best, or null with the kept best's error.
    /// </summary>
    static GameObject? Solve(GameObject destination, Owner side, CommandType commandType, Func<GameObject, (OrderResult Error, int Left)> evaluate, out OrderResult error)
    {
        error = Refusal.WrongClass;
        if (!Supported(destination)) return null;
        GameObject best = destination;
        var (bestError, bestLeft) = evaluate(destination);
        void Try(GameObject c)
        {
            var (e, left) = evaluate(c);
            if (Better(e, left, bestError, bestLeft)) (best, bestError, bestLeft) = (c, e, left);
        }
        List<GameObject> Ships(Fleet f) => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Owner == side && x.Usable).ToList();
        if (destination is StarSystem)
        {
            var fleets = destination.Contents.OfType<Fleet>().Where(f => f.Owner == side).ToList();
            bool newFleet = commandType == CommandType.CreateFleet;
            if (fleets.FirstOrDefault(f => (newFleet ? f.NewFleet : f.Reserve) && !f.Destroyed) is { } placeholder) Try(placeholder);
            // the capital-ship stage only while the best so far is invalid; once it runs every ship is compared
            if (!bestError.Success) foreach (var ship in fleets.Where(f => f.Usable).SelectMany(Ships).ToList()) Try(ship);
        }
        else if (destination is Fleet fleet) foreach (var ship in Ships(fleet)) Try(ship);
        error = bestError;
        return bestError.Success ? best : null;
    }

    /// <summary>
    /// The new candidate against the best: a candidate with an acceptance error never wins; one with acceptance OK
    /// beats a best whose acceptance failed (even with its own capacity error); both accepted: a capacity error never wins, capacity
    /// OK beats a capacity error; both valid: unlimited beats limited, two unlimited never replace, two limited to strictly larger
    /// remaining. Refusal.WrongDestinationKind is an acceptance error. So with no valid candidate the destination's own
    /// error is kept unless a later candidate with only a capacity error replaced a destination whose acceptance failed (e.g. a
    /// fighter to an own fleet with full ships gives the first ship's Refusal.NoFighterRoom).
    /// </summary>
    static bool Better(OrderResult e, int left, OrderResult best, int bestLeft)
    {
        if (!e.Success && !IsCapacityError(e)) return false;
        if (!best.Success && !IsCapacityError(best)) return true;
        if (!e.Success) return false;
        return !best.Success || left > bestLeft;
    }

    /// <summary>
    /// Remaining capacity of a valid candidate for the unit; int.MaxValue = unlimited (a system's facility limit is left out: no
    /// other candidate takes facilities).
    /// </summary>
    static int Remaining(GameState game, GameObject container, uint type, Sandbox? sandbox)
    {
        if (!Family.IsCapitalShip(container.Type)) return int.MaxValue;
        sandbox ??= new Sandbox();
        var r = game.Definitions.Resolve(container.Type)!;
        return Family.Of(type) is >= Families.Fighter and < Families.Headquarters ? r.Get(SdField.FighterCapacity) - sandbox.Count(container, Families.Fighter, Families.Headquarters)
            : Family.IsTroop(type) ? r.Get(SdField.TroopCapacity) - sandbox.Count(container, Families.Troop, Families.CapitalShip) : int.MaxValue;
    }

    /// <summary>Capacity category index: 0 capital ships, 1 Death-Star class, 2 fighters, 3 facilities (4 Mines), 5 characters / special forces, 6 troops, 7 fleets, 8 missions; -1 none.</summary>
    internal static int Category(uint type) => Family.Of(type) switch
    {
        >= Families.CapitalShip and < Families.DeathStar => 0, >= Families.DeathStar and < Families.Fighter => 1, >= Families.Fighter and < Families.Headquarters => 2, Families.Mine => 4, >= Families.Headquarters and < Families.MajorCharacter => 3,
        >= Families.MajorCharacter and < Families.Mission => 5, >= Families.Troop and < Families.CapitalShip => 6, >= Families.Fleet and < Families.Troop => 7, >= Families.Mission and < Families.Sector => 8, _ => -1,
    };

    static bool IsCapacityError(OrderResult e) => e == Refusal.NoEnergy || e == Refusal.NoRawMaterials || e == Refusal.DeathStarLimit || e.Category == Families.CapitalShip;

    /// <summary>
    /// One solver run for a group placed together (mission Team then Decoy lists): one unit record built
    /// in list order (per member: in no category Refusal.NotPlaceable, Destroyed Refusal.Missing, not Created Refusal.NotCreated; the build stops at the
    /// first); the demand per category = the Existing members of it (sandbox mode 3); the members leave their places; then the
    /// candidate loop of <see cref="Place"/> once with the summed demand (a candidate's error: acceptance errors before capacity
    /// errors, in category order; characters / special forces are unlimited wherever accepted); unsupported destination gives Refusal.WrongClass.
    /// </summary>
    public static GameObject? PlaceGroup(GameState game, GameObject destination, IReadOnlyList<GameObject> units, Owner side, CommandType commandType, out OrderResult error, Sandbox sandbox)
    {
        error = OrderResult.Ok;
        foreach (var u in units)
        {
            error = Category(u.Type) < 0 ? Refusal.NotPlaceable : u.Destroyed ? Refusal.Missing : !u.Created ? Refusal.NotCreated : OrderResult.Ok;
            if (!error.Success) return null;
        }
        if (!Supported(destination))
        {
            error = Refusal.WrongClass;
            return null;
        }
        foreach (var u in units) if (u.Container is { } from) sandbox.Commit(from, u.Type, -1);
        var groups = units.GroupBy(u => Category(u.Type)).OrderBy(g => g.Key).ToList();
        (OrderResult, int) Fit(GameObject c)
        {
            var results = groups.Select(g =>
            {
                var trial = sandbox.Copy();
                var members = g.Where(u => u.Existing).ToList();
                var first = members.FirstOrDefault() ?? g.First();
                foreach (var u in members.Skip(1)) trial.Commit(c, u.Type, 1); // summed demand: the others first, then the fit of one
                return Accept(game, c, first.Type, side, commandType, trial, first);
            }).Where(e => !e.Success).ToList();
            return (results.FirstOrDefault(e => !IsCapacityError(e), results.FirstOrDefault(OrderResult.Ok)), int.MaxValue);
        }
        var place = Solve(destination, side, commandType, Fit, out error);
        if (place is not null) foreach (var u in units) sandbox.Commit(place, u.Type, 1);
        return place;
    }

    /// <summary>
    /// Capacity model (demand 1 per unit and category). Systems: capital ships not accepted; base acceptance
    /// (Inferred: owner = the side) forgiven for fleets under MoveObject, characters / special forces under CreateMission, troops under
    /// MoveObject in an Explored, unpopulated, owner-3 system, never under CreateMoveMission; Blockade refuses
    /// Refusal.Blockaded except fleets under MoveObject, characters / special forces under CreateMission / CreateMoveMission and type 1008; facilities [32, 48)
    /// need Energy - Existing facilities (any owner) - 1 >= 0, Mines also RawMaterial - Existing Mines - 1 >= 0. Fleets and capital
    /// ships first need the base acceptance (never forgiven). Fleets: capital ships only. Capital ships: fighters up to FighterCapacity, troops up to TroopCapacity, characters and special
    /// forces without limit. Nothing else holds units.
    /// </summary>
    public static bool CanPlace(GameState game, GameObject container, uint type, Owner side, CommandType commandType, Sandbox? sandbox = null, GameObject? unit = null) =>
        Accept(game, container, type, side, commandType, sandbox, unit).Success;

    /// <summary>
    /// <see cref="CanPlace"/> with the candidate's error, acceptance errors before capacity errors: system destroyed Refusal.NoDestination,
    /// other owner Refusal.DestinationOtherOwner, Blockade Refusal.Blockaded; class does not take the category Refusal.WrongDestinationKind; Energy Refusal.NoEnergy, RawMaterial
    /// Refusal.NoRawMaterials (Inferred), then the second-record Death Star limit Refusal.DeathStarLimit; fleets / capital ships: base acceptance
    /// owner Refusal.DestinationOtherOwner, not Completed Refusal.DestinationNotCompleted, Destroyed Refusal.NoDestination, Enroute Refusal.DestinationEnroute; capital ship full: fighters Refusal.NoFighterRoom,
    /// troops Refusal.NoTroopRoom.
    /// </summary>
    internal static OrderResult Accept(GameState game, GameObject container, uint type, Owner side, CommandType commandType, Sandbox? sandbox = null, GameObject? unit = null)
    {
        sandbox ??= new Sandbox();
        if (sandbox.Closed(container)) return Refusal.DestinationClosed; // closed parent of a flag-0 destination search
        uint t = Family.Of(type);
        bool fleet = t is >= Families.Fleet and < Families.Troop, agent = t is >= Families.MajorCharacter and < Families.Mission, troop = Family.IsTroop(type);
        OrderResult ok = OrderResult.Ok, wrongKind = Refusal.WrongDestinationKind;
        // abode: characters / special forces only, unlimited, else Refusal.WrongDestinationKind; base acceptance owner != side
        // Refusal.DestinationOtherOwner, forgiven only for them under CreateMission; no Blockade test
        if (Family.Of(container.Type) == Families.Abode)
            return !agent ? wrongKind : container.Owner == side && container.Owner == (sandbox.Second ?? side) || commandType == CommandType.CreateMission ? ok
                : container.Owner != side ? Refusal.DestinationOtherOwner : Refusal.DestinationRefused;
        if (container is StarSystem s)
        {
            // error priority: acceptance errors (accept slot), then the capacity slot's "does not take the
            // category" Refusal.WrongDestinationKind, then first-record capacity errors, then second-record ones.
            // Accept slot: destroyed system Refusal.NoDestination unless forgiven (fleets under MoveObject, characters / special forces under CreateMission,
            // base acceptance owner Refusal.DestinationOtherOwner unless forgiven; Blockade Refusal.Blockaded only without an
            // acceptance error and unless exempt.
            if (s.Destroyed && !(fleet && commandType == CommandType.MoveObject || agent && commandType == CommandType.CreateMission)) return Refusal.NoDestination;
            // base acceptance (owner != first side Refusal.DestinationOtherOwner, != second side Refusal.DestinationRefused; Completed / Enroute never fire for a
            // system), cleared by the command-type forgiveness
            bool forgiven = fleet && commandType == CommandType.MoveObject || agent && commandType == CommandType.CreateMission
                || troop && commandType == CommandType.MoveObject && s.Explored && s.Owner == Owner.Neutral && !s.Populated;
            if (!forgiven && s.Owner != side) return Refusal.DestinationOtherOwner;
            if (!forgiven && s.Owner != (sandbox.Second ?? side)) return Refusal.DestinationRefused;
            if (s.Blockade && !(fleet && commandType == CommandType.MoveObject || agent && commandType is CommandType.CreateMission or CommandType.CreateMoveMission || commandType == CommandType.DestroyedParentPlacement)) return Refusal.Blockaded;
            if (Family.IsCapitalShip(type)) return wrongKind;
            if (Family.IsFacility(type) && s.Energy - sandbox.Count(s, Families.Headquarters, Families.MajorCharacter) - 1 < 0) return Refusal.NoEnergy;
            if (t == Families.Mine && s.RawMaterials - sandbox.Count(s, Families.Mine, Families.Refinery) - 1 < 0) return Refusal.NoRawMaterials;
            return DeathStar.Room(s, sandbox, unit, type) ? ok : Refusal.DeathStarLimit; // second record
        }
        // base acceptance for fleets and capital ships, every category and command type: owner = side (second side
        // the same), Completed (fleets: only when not a placeholder), not Destroyed, not Enroute.
        bool placeholder = container is Fleet fp && (fp.Reserve || fp.NewFleet);
        if (container is Fleet || Family.IsCapitalShip(container.Type))
        {
            if (container.Owner != side) return Refusal.DestinationOtherOwner;
            if (container.Owner != (sandbox.Second ?? side)) return Refusal.DestinationRefused;
            if (!placeholder && !container.Completed) return Refusal.DestinationNotCompleted;
            if (container.Destroyed) return Refusal.NoDestination;
            if (container.Enroute) return Refusal.DestinationEnroute;
        }
        if (container is Fleet) return Family.IsCapitalShip(type) ? ok : wrongKind; // a Death Star into a fleet: the fleet's records only (unlimited)
        if (!Family.IsCapitalShip(container.Type)) return wrongKind;
        var r = game.Definitions.Resolve(container.Type)!;
        if (t is >= Families.Fighter and < Families.Headquarters) return r.Get(SdField.FighterCapacity) - sandbox.Count(container, Families.Fighter, Families.Headquarters) >= 1 ? ok : Refusal.NoFighterRoom;
        if (troop) return r.Get(SdField.TroopCapacity) - sandbox.Count(container, Families.Troop, Families.CapitalShip) >= 1 ? ok : Refusal.NoTroopRoom;
        return agent ? ok : wrongKind;
    }
}
