namespace OpenSWR.Sim;

/// <summary>
/// Object flag setters with their hooks (stored derived flags Existing, Enroute, Usable), parent and owner changes, object
/// creation, and the generic notification handlers 0x300 Existing, 0x301 Usable, 0x302 owner, 0x308 Completed, 0x320 parent.
/// Notifications without a sim effect (0x318 apart from systems and agents, 0x312, 0x319) are not posted.
/// </summary>
public static class Objects
{
    /// <summary>Priority keys for the event queue.</summary>
    public const int NotificationPriority = 0x10, PhasePriority = 0x156, ReadyPriority = 0x30C, SystemPulsePriority = 0x400;

    /// <summary>Posts a notification; none is created before the game-ready flag.</summary>
    internal static void Notify(GameState game, Action<GameState> handler, int priority = NotificationPriority)
    {
        if (game.Ready) game.Clock.Post(new ActionEvent(priority, handler));
    }

    // ---- stored flags and their hooks ----

    /// <summary>Created hook: a Mine or Refinery gets capacity 50, then Existing is recomputed.</summary>
    public static void SetCreated(GameState game, GameObject o)
    {
        if (o.Created) return; // setters run their hook only on a change
        o.Created = true;
        game.ViewMessage(0x102, o);
        if (o is Facility { IsMineOrRefinery: true } f) f.Capacity = 50;
        RecomputeExisting(game, o);
    }

    /// <summary>Completed hook: sets Constructed, recomputes Usable (its 0x301 first), then posts 0x308.</summary>
    public static void SetCompleted(GameState game, GameObject o)
    {
        if (o.Completed) return;
        o.Completed = true;
        game.ViewMessage(0x103, o);
        o.Constructed = true;
        RecomputeUsable(game, o);
        Notify(game, g => { g.Record(0x308, o.Ref, 0, 1); OnCompleted(g, o); });
    }

    /// <summary>
    /// Sets DestroyedReason. A nonzero reason sets Destroyed and runs the Destroyed hook. The base part recomputes Existing,
    /// runs the parent-destroyed step for each existing direct content (key order), clears EnrouteActive, clears HyperdriveActive
    /// (timer 0x387 goes inactive; 0x306 is posted but ignored for destroyed objects), clears Autorouting, then recomputes the
    /// travel fields. The class steps follow: fleets lose Battle, Bombard and Assault (not Blockade); capital ships with reason
    /// 1 to 0x10 run the ship-loss injury check on the characters aboard; missions clear their objective, which releases its
    /// lock; agents ask to leave their mission and clear Adrift; characters run their Destroyed step; systems destroy their
    /// managers, then post 0x319 (knowledge mode 11 for both sides). The object stays in its container until its final move
    /// (0x372, <see cref="FinalMoveTest"/>; systems never move).
    /// </summary>
    public static void Destroy(GameState game, GameObject o, int reason)
    {
        o.DestroyedReason = reason;
        if (reason == 0 || o.Destroyed) return;
        o.Destroyed = true;
        game.StrikeDestroys?.Add((o, Combat.LiveSides(o), o.Completed)); // the 0x302 receivers of a strike (UI only)
        if (o.Container is { } at) game.ViewMessage(0x302, o, at); // view Destroyed, only with a parent
        RecomputeExisting(game, o);
        foreach (var x in o.Contents.Where(x => x.Existing).ToList()) Fleets.ParentDestroyed(game, x, cascade: true);
        SetEnrouteActive(game, o, false);
        SetHyperdriveActive(game, o, false);
        ClearAutorouting(game, o);
        RecomputeTravel(game, o);
        if (o is Fleet f) (f.Battle, f.Bombard, f.Assault) = (false, false, false);
        if (o is StarSystem s)
        {
            // managers are direct contents in the original (created Completed): destroyed with the system's reason after the other
            // contents (key order); their Existing hook cancels the queue (0x16, no refund)
            foreach (var mm in s.Managers.Where(x => x is { Existing: true, Destroyed: false }).ToList()) Destroy(game, mm, reason);
            Notify(game, g => { foreach (var side in new[] { Owner.Alliance, Owner.Empire }) Knowledge.Update(g, s, 11, side); }, 0x801); // 0x319
        }
        // capital ships with reason 1 to 0x10: ship-loss injury check of the existing, not EnrouteActive characters aboard,
        // key order, after the content handling
        if (Family.IsCapitalShip(o.Type) && reason is >= 1 and <= 0x10)
            foreach (var x in o.Contents.ToList())
                if (x is Character ch && ch.Existing && !ch.EnrouteActive) Missions.InjuryCheck(game, ch, shipLoss: true);
        if (o is Mission m) Missions.SetObjective(game, m, null);
        if (o is Agent a)
        {
            if (a.Mission is not null) Prisoners.SetRemoveRequest(game, a);
            Prisoners.SetAdrift(game, a, false);
        }
        if (o is Character c) Prisoners.Destroyed(game, c);
        ReadyTest(game, o); // end of the base Destroyed hook
    }

    // ---- deletion ----

    /// <summary>
    /// Deletable classes: the classes that make the final move (<see cref="FinalMoveTest"/>) except every character
    /// class [0x30, 0x3C) and the HQ 0x20 (never deleted; special forces 0x3C stay deletable).
    /// </summary>
    internal static bool Deletable(GameObject o) =>
        Family.Of(o.Type) is (< 0x80 and not (>= 0x30 and < 0x3C) and not 0x20) or (>= 0xF8 and <= 0xFA);

    /// <summary>Whether an instance is acknowledged: a side's view, else the real object.</summary>
    internal static bool Acked(GameObject o, Owner instance) => instance is Owner.Alliance or Owner.Empire ? o.AckedViews[instance] : o.AckedReal;

    /// <summary>
    /// Acknowledges one instance. <paramref name="instance"/> is the side of a view, else the real object. When the instance is
    /// Destroyed and not yet acknowledged, it is marked and the ready test runs. A missing view (fresh, deleted) acknowledges
    /// nothing. Callers: the 0x205 run (the command side's view) and the end of the 0x320 handler (the moved real object, when
    /// deletable).
    /// </summary>
    internal static void Acknowledge(GameState game, GameObject o, Owner instance)
    {
        bool real = instance is not (Owner.Alliance or Owner.Empire);
        if (o.Deleted || Acked(o, instance)) return;
        if (real ? !o.Destroyed : Knowledge.ViewDestroyed(game, o, instance) != true) return;
        if (real) o.AckedReal = true;
        else o.AckedViews = o.AckedViews.With(instance, true);
        ReadyTest(game, o);
    }

    /// <summary>
    /// Ready test. It passes when ReadyForDelete is clear, the real object and both side views exist, are Destroyed and deletable,
    /// and have no contents (the real object: none at all, Destroyed ones included; a view: the views whose view parent it is),
    /// the real parent is the staging container (0xF2000003), and every instance with Created set is acknowledged. On a pass it
    /// sets ReadyForDelete and posts 0x31A (priority 0x999; none before game-ready). It runs at the acknowledgements, at the end
    /// of the real Destroyed hook, on each view Destroyed write, and on each real or view parent change (moved object and old
    /// parent). Apart from the pass itself, it only reads state.
    /// </summary>
    internal static void ReadyTest(GameState game, GameObject? o)
    {
        if (o is null || o.Deleted || o.ReadyForDelete) return;
        if (!o.Destroyed || !Deletable(o) || o.Contents.Count != 0 || o.Container != game.Staging) return;
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
            if (Knowledge.ViewDestroyed(game, o, side) != true || Knowledge.HasContentViews(game, o, side)) return; // missing view: null
        if (o.Created && !o.AckedReal) return;
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
            if (Knowledge.ViewCreated(game, o, side) && !o.AckedViews[side]) return;
        o.ReadyForDelete = true;
        Notify(game, g => Delete(g, o), 0x999);
    }

    /// <summary>
    /// Runs the 0x31A notification. With ReadyForDelete set, the object leaves every game list and the staging container, both
    /// side views go, and its timer slots are removed (they cannot fire again, since the target is Destroyed; a slot that a queued
    /// event still names stays until that event drops). Nothing else is cleared: readers still holding the object see it as
    /// missing (<see cref="GameObject.Deleted"/>). The deleted-key tree has no reader and is not kept (shortcut, Inferred harmless).
    /// </summary>
    static void Delete(GameState game, GameObject o)
    {
        if (!o.ReadyForDelete || o.Deleted) return;
        game.Created.Remove(o);
        if (o is Fleet f) game.Fleets.Remove(f);
        if (o is Facility x) game.Facilities.Remove(x);
        if (o is Mission m) game.Missions.Remove(m);
        o.Remove();
        game.Views.Remove((o, Owner.Alliance));
        game.Views.Remove((o, Owner.Empire));
        o.LiveSides = SideSet.None;
        o.Deleted = true;
        var named = game.Clock.TimedEvents.OfType<TimerEvent>().Select(e => e.Slot).ToHashSet();
        foreach (var key in game.Timers.Where(e => e.Key.Target == o.Ref && !named.Contains(e.Value)).Select(e => e.Key).ToList())
            game.Timers.Remove(key);
    }

    /// <summary>
    /// Final-move test. It passes when the object is Destroyed, not Locked, has no contents at all (Destroyed ones included), and
    /// its class moves: families below 0x80 and managers F8 to FA. Sectors, systems, 0x98, manufacturing managers, galaxy, staging
    /// and sides never move. A pass posts 0x372 (priority 0x998). Its handler re-parents the object into the staging container
    /// unless it is there already (parent-change hook, 0x320). No draws.
    /// </summary>
    static void FinalMoveTest(GameState game, GameObject o)
    {
        if (o.Destroyed && !o.Locked && o.Contents.Count == 0 && Family.Of(o.Type) is < 0x80 or (>= 0xF8 and <= 0xFA))
            Notify(game, g => { if (o.Container != g.Staging) SetParent(g, o, g.Staging); }, 0x998);
    }

    /// <summary>
    /// Changes the lock counter by delta, floored at 0. Locked is set while the counter is not 0. Its hook posts 0x316, and that
    /// handler runs the final-move test when the new value is 0. A 0x316 with value 1 has no effect and is not posted.
    /// </summary>
    internal static void Lock(GameState game, GameObject? o, int delta)
    {
        if (o is null || o.Deleted) return; // a deleted key is not found: no counter change
        if (o.LockCount + delta < 0) game.LockFloor?.Invoke(o); // a lost lock (test hook)
        o.LockCount = Math.Max(0, o.LockCount + delta);
        bool locked = o.LockCount != 0;
        if (o.Locked == locked) return;
        o.Locked = locked;
        if (!locked) Notify(game, g => FinalMoveTest(g, o));
    }

    /// <summary>Recomputes Existing, which is Created and not Destroyed. Its hook recomputes Usable, then posts 0x300.</summary>
    static void RecomputeExisting(GameState game, GameObject o)
    {
        bool existing = o.Created && !o.Destroyed;
        if (o.Existing == existing) return;
        o.Existing = existing;
        // manager Existing hook, when it stops existing: cancel the queue (0x16); owner 3 (getter); clear Reserved
        if (o is ManufacturingManager mm && !o.Existing)
        {
            foreach (var unit in mm.Orders.ToList()) Destroy(game, unit, 0x16);
            mm.Reserved = false;
            Knowledge.ManagerOwnerChanged(game, mm, mm.System.Owner); // owner becomes 3: both views frozen
        }
        RecomputeUsable(game, o);
        var (parent, location) = (o.Container, o.System);
        bool value = o.Existing;
        Notify(game, g => { g.Record(0x300, o.Ref, value ? 0 : 1, value ? 1 : 0); OnExistingChanged(g, o, parent, location, value); });
    }

    /// <summary>
    /// Usable = Existing and Completed and not Enroute; fleets also need a ready capital ship. Its hook runs the base part first
    /// (view refresh, then post 0x301 carrying parent and location), then the class follow-ups: facilities and Mines or Refineries
    /// post 0x315, and a character gets the force-active recompute (timer 0x389 input) and then the escape gate. So an object's
    /// 0x301 runs before the 0x315 its own Usable change caused. Ships, Death Stars and fighters update RepairActive.
    /// </summary>
    internal static void RecomputeUsable(GameState game, GameObject o)
    {
        bool usable = o.Existing && o.Completed && !o.Enroute && (o is not Fleet f || Fleets.HasReadyShip(f));
        if (o.Usable == usable) return;
        o.Usable = usable;
        game.ViewMessage(0x101, o);
        Knowledge.RecomputeLive(game, o); // Usable notifier
        var (parent, location) = (o.Container, o.System);
        Notify(game, g => { g.Record(0x301, o.Ref, usable ? 0 : 1, usable ? 1 : 0); OnUsableChanged(g, o, parent, location, usable); });
        if (o is Facility facility) Economy.FacilityUsableChanged(game, facility);
        Repair.UpdateRepairActive(game, o); // ships, Death Stars, fighters
        if (o is Character c)
        {
            TimerCatalogue.RecomputeForceActive(game, c);
            Prisoners.EscapeGate(game, c);
        }
    }

    /// <summary>
    /// EnrouteActive setter. Its hook recomputes ObservedBy, then the Enroute, ETA and destination fields (own while EnrouteActive,
    /// else the parent's; zero and none unless Constructed and not Destroyed). DestinationCount and ObservedBy are not modelled.
    /// When it becomes set, troop regiments (0x10-0x13) draw against their WithdrawPercent copy; a failed draw sets
    /// DestroyedRunningBlockade, which gives DestroyedReason 0xD.
    /// </summary>
    public static void SetEnrouteActive(GameState game, GameObject o, bool value)
    {
        if (o.EnrouteActive == value) return;
        o.EnrouteActive = value;
        // UI 0x200 RoleEnrouteActive of a Palace-mission agent, kept for type 0x28 only; before the observed
        // recompute, as the live views' copy (Inferred order)
        if (value && o is Agent { Mission: { Kind: 0x64 } pm }) game.StoryPost(0x200, o, 0, 1, pm);
        Knowledge.RecomputeObserved(game, o);
        RecomputeTravel(game, o);
        if (value && Family.IsTroop(o.Type) && !game.Random.Chance(o.WithdrawPercent, DrawSite.Withdraw))
        {
            o.DestroyedRunningBlockade = true;
            Destroy(game, o, 0xD);
        }
    }

    /// <summary>Recomputes Enroute, ETA and destination: own while EnrouteActive, else the parent's; zero and none unless Constructed and not Destroyed.</summary>
    static void RecomputeTravel(GameState game, GameObject o)
    {
        RecomputeEnroute(game, o);
        if (!o.Constructed || o.Destroyed) (o.Eta, o.Destination) = (0, null);
        else if (!o.EnrouteActive) (o.Eta, o.Destination) = (o.Container?.Eta ?? 0, o.Container?.Destination);
    }

    /// <summary>
    /// Enroute = Constructed and not Destroyed and (EnrouteActive or the parent's Enroute). Its hook runs the Usable recompute,
    /// then each content's Enroute recompute in key order (pre-order walk).
    /// </summary>
    static void RecomputeEnroute(GameState game, GameObject o)
    {
        bool enroute = o.Constructed && !o.Destroyed
            && (o.EnrouteActive || o.Container is { Enroute: true });
        if (o.Enroute == enroute) return;
        o.Enroute = enroute;
        game.ViewMessage(0x104, o);
        RecomputeUsable(game, o);
        if (o is Character ch) Prisoners.RecomputeHealing(game, ch); // Inferred: after the base part
        foreach (var c in o.Contents.ToList()) RecomputeEnroute(game, c);
    }

    /// <summary>HyperdriveActive setter. Its hook sets timer 0x387 active to the value, then posts 0x306.</summary>
    public static void SetHyperdriveActive(GameState game, GameObject o, bool value)
    {
        if (o.HyperdriveActive == value) return;
        o.HyperdriveActive = value;
        TimerCatalogue.Slot(game, 0x387, o.Ref, o).SetActive(game, value);
        Notify(game, g => { g.Record(0x306, o.Ref, value ? 0 : 1, value ? 1 : 0); Fleets.OnHyperdriveChanged(g, o, value); });
    }

    /// <summary>
    /// Autorouting setter. Its hook posts 0x307 (<see cref="Fleets.OnAutorouting"/>). Fleets also destroy (DestroyedReason 5)
    /// every Completed, not EnrouteActive capital ship aboard with speed 0.
    /// </summary>
    public static void SetAutorouting(GameState game, GameObject o)
    {
        if (o.Autorouting) return;
        o.Autorouting = true;
        Notify(game, g => Fleets.OnAutorouting(g, o), 0x300); // 0x307 priority 0x300
        if (o is not Fleet f) return;
        foreach (var ship in f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Completed && !x.EnrouteActive).ToList())
            if (Orders.Speed(game, ship) == 0) Destroy(game, ship, 5);
    }

    /// <summary>
    /// Clears Autorouting. Its 0x307 (value 0, priority 0x300) only runs the manager F8 story check: the grouping and the other
    /// parts act only on a set value (Inferred for the non-agent parts).
    /// </summary>
    internal static void ClearAutorouting(GameState game, GameObject o)
    {
        if (!o.Autorouting) return;
        o.Autorouting = false;
        Notify(game, g => Story.Notified(g, o), 0x300);
    }

    /// <summary>Posts 0x302 (old owner) for an object whose owner was already written (system owner change).</summary>
    internal static void NotifyOwner(GameState game, GameObject o, Owner old)
    {
        var now = o.Owner;
        Notify(game, g => { g.Record(0x302, o.Ref, (int)old, (int)now); OnOwnerChanged(g, o, old); });
    }

    /// <summary>
    /// Owner setter: a change runs the owner hook: base part (post 0x302 (old, new), then 0x318: characters / special forces refresh the old owner's view), then the class part
    /// (processing facilities, <see cref="Economy.FacilityOwnerChanged"/>).
    /// </summary>
    public static void SetOwner(GameState game, GameObject o, Owner owner)
    {
        var old = o.Owner;
        if (old == owner) return;
        o.Owner = owner;
        // every view live before the write copies the new owner and posts (also the one that freezes now); a view that
        // turns live posts from its refresh. Inferred: the owner write is the ControlKind message of a non-system view
        game.ViewMessage(0x100, o);
        Knowledge.RecomputeLive(game, o); // owner hook
        NotifyOwner(game, o, old);
        if (o is Agent a) Notify(game, g => Knowledge.OwnerFollowUp(g, a, old), 0x800); // 0x318
        if (o is Facility f) Economy.FacilityOwnerChanged(game, f);
    }

    /// <summary>
    /// Sets the parent: moves the object, then runs the parent-change hook. The base part clears Autorouting, recomputes the
    /// Enroute, ETA and destination fields, and posts 0x320 (old, new). DestinationCount and ObservedBy have no sim reader and are
    /// not kept. It does nothing and fails when the old or new parent is none; in the sim, objects without a parent sit in the
    /// staging container (creation, recruitment), where it succeeds. Class parts after it: fleets clear the reserve and new-fleet
    /// flags; characters recompute CanHeal. Inferred: setting the current parent again changes nothing.
    /// The fleet override (moving into a fleet) is <see cref="Fleets.SetParent"/>.
    /// </summary>
    public static void SetParent(GameState game, GameObject o, GameObject parent)
    {
        var old = o.Container;
        if (old == parent) return;
        parent.Add(o);
        game.ViewMessage(0x300, o, parent, old);
        Knowledge.ParentCopy(game, o, parent); // frozen views copy the parent, before the hook
        ClearAutorouting(game, o);
        Knowledge.RecomputeObserved(game, o);
        RecomputeTravel(game, o);
        if (o.Destroyed && parent == game.Staging) Knowledge.AtStaging(game, o); // 0x321 and view 0x302 posts before the 0x320
        Notify(game, g => { g.Record(0x320, o.Ref, (old ?? g.Staging).Ref, parent.Ref); OnParentChanged(g, o, old, parent); });
        if (o is Fleet f) (f.Reserve, f.NewFleet) = (false, false);
        if (o is Character c) Prisoners.RecomputeCanHeal(game, c);
        ReadyTest(game, o); // the moved object, then the old parent
        ReadyTest(game, old);
    }

    /// <summary>
    /// Object creation: registered with its id, owner 3 then the real owner (0x302), put into the parent (0x320 from
    /// the staging container: no system), then Created and Completed as given (0x300, 0x301, 0x308).
    /// </summary>
    public static T Create<T>(GameState game, T o, Owner owner, GameObject parent, bool created, bool completed) where T : GameObject
    {
        o.ClearFlags();
        game.Register(o);
        SetOwner(game, o, owner);
        SetParent(game, o, parent);
        if (created) SetCreated(game, o);
        if (completed) SetCompleted(game, o);
        return o;
    }

    /// <summary>A new object of the type's family with the next id (special forces roll their skills).</summary>
    public static GameObject Create(GameState game, uint type, Owner owner, GameObject parent, bool completed) =>
        Create(game, NewGame.Instantiate(game, type), owner, parent, created: true, completed);

    // ---- container hooks ----

    /// <summary>
    /// Container hook for an Existing object entering (true) or leaving (false) a container. Systems: a facility [0x20, 0x30)
    /// runs the facilities check in full, and a Mine also the Mines check, for either value (so the stored values drop on an
    /// Existing loss or a departure). A production facility [0x28, 0x30) entering sets Suspended to Uprising or Blockade for every
    /// Existing facility of the system, with no balancing. For fleets: the placeholder upkeep and the fleet Blockade flags. Fleets:
    /// a capital ship runs the fleet's ships-changed step. Other branches have no sim effect.
    /// </summary>
    static void ContentExistingChanged(GameState game, GameObject? container, GameObject o, bool added)
    {
        if (container is StarSystem u && Family.IsFacility(o.Type))
        {
            Systems.FacilitiesCheck(game, u);
            if (Family.Of(o.Type) == 0x2C) Systems.MinesCheck(game, u);
            if (added && Family.Of(o.Type) >= 0x28) Systems.SuspendFacilities(game, u);
        }
        if (container is StarSystem s && o is Fleet) { Fleets.Upkeep(game, s); Combat.SetFleetBlockade(s, s.Blockade); }
        else if (container is Fleet f && Family.IsCapitalShip(o.Type)) Fleets.ShipsChanged(game, f);
    }

    /// <summary>
    /// Container hook for an object's Usable change. Systems pass it to <see cref="Fleets.SystemContentUsableChanged"/>.
    /// In a fleet, a capital ship triggers the fleet's Usable recompute.
    /// </summary>
    static void ContentUsableChanged(GameState game, GameObject? container, GameObject o)
    {
        if (container is StarSystem s) Fleets.SystemContentUsableChanged(game, s, o);
        else if (container is Fleet f && Family.IsCapitalShip(o.Type)) RecomputeUsable(game, f);
    }

    /// <summary>
    /// System hook that re-evaluates contents of families 0x08-0x1F: SmugglingPercent first, then, for a new Usable value of 1,
    /// the FastRepair update (<see cref="Repair.ContentUsable"/>).
    /// </summary>
    static void SystemReevaluate(GameState game, StarSystem? system, GameObject o, bool usable)
    {
        if (system is null || Family.Of(o.Type) is not (>= 0x08 and < 0x20)) return;
        Economy.UpdateSmuggling(game, system);
        if (usable) Repair.ContentUsable(game, system, o);
    }

    // ---- notification handlers ----

    static bool IsUnitFamily(GameObject o) => Family.Of(o.Type) is >= 0x10 and < 0x40;

    /// <summary>
    /// 0x300 handler, in order: the story hook (F8); the target reset for a Destroyed object; a unit not Completed with a manager
    /// BuilderKey joins or leaves its queue (with balancing); the parent's Existing hook (object, new value); the location
    /// system's loss records (as captured at post time); the Existing-loss victory check. Then, for families 0x10-0x3F, a side
    /// recompute and, for a Destroyed object with DestroyedReason 0x14, 0x15 or 0x16 that is Completed, a refund
    /// (refined += RefinedMaterialCost div 2). Last, the final-move test.
    /// </summary>
    static void OnExistingChanged(GameState game, GameObject o, GameObject? parent, StarSystem? location, bool value)
    {
        Story.Notified(game, o); // F8 hook
        if (o.Destroyed) Economy.ResetTargets(game, o); // reset targets
        if (!o.Completed && o.BuilderKey is { } m) Economy.UpdateQueue(game, m, o);
        ContentExistingChanged(game, parent, o, value);
        if (location is not null && !value) Combat.RecordLoss(game, location, o);
        if (!value) Victory.ExistingLost(game, o, location); // victory check for a lost Existing
        if (IsUnitFamily(o))
        {
            Economy.RecomputeSide(game, o.Owner);
            if (o.Destroyed && game.Sides.TryGetValue(o.Owner, out var side)
                && o.DestroyedReason is 0x14 or 0x15 or 0x16 && o.Completed)
                Economy.SetRefined(game, side, side.RefinedStock + game.RefinedMaterialCost(o.Type) / 2);
        }
        FinalMoveTest(game, o);
    }

    /// <summary>
    /// 0x301 handler: the story hook, then the parent's Usable hook, the Death Star Usable hook and the location's Usable hook
    /// (all as captured at post time). For families 0x10-0x3F, a side recompute.
    /// </summary>
    static void OnUsableChanged(GameState game, GameObject o, GameObject? parent, StarSystem? location, bool usable)
    {
        Story.Notified(game, o); // F8 hook
        ContentUsableChanged(game, parent, o);
        DeathStar.UsableChanged(game, o); // Death Star usable hook
        SystemReevaluate(game, location, o, usable);
        if (IsUnitFamily(o)) Economy.RecomputeSide(game, o.Owner);
    }

    /// <summary>
    /// 0x302 handler. Every object resets its economy targets. Systems and families 0x10-0x3F run the manager F8 hook (phase
    /// check of the missions whose ObjectiveKey or TargetKey is the object); families 0x10-0x3F recompute the old owner's side,
    /// then the new owner's side.
    /// </summary>
    static void OnOwnerChanged(GameState game, GameObject o, Owner old)
    {
        if (o is StarSystem || IsUnitFamily(o)) Missions.OwnerChanged(game, o);
        Economy.ResetTargets(game, o); // reset targets
        if (!IsUnitFamily(o)) return;
        Economy.RecomputeSide(game, old);
        Economy.RecomputeSide(game, o.Owner);
    }

    /// <summary>
    /// 0x308 handler for families 0x10-0x3F: a manager BuilderKey updates its queue; families 0x30-0x3F with a BuilderKey get a
    /// Move mission; families 0x10-0x2F with a BuilderKey travel from the manager to the unit; then, if not EnrouteActive, the
    /// deployment check.
    /// </summary>
    static void OnCompleted(GameState game, GameObject o)
    {
        if (!IsUnitFamily(o)) return;
        var m = o.BuilderKey;
        if (m is not null) Economy.UpdateQueue(game, m, o);
        if (Family.Of(o.Type) >= 0x30) { if (m is not null && o is Agent a) Missions.CreateMove(game, a, m); }
        else if (m is not null) Fleets.Travel(game, o, m, o);
        if (!o.EnrouteActive) Fleets.DeploymentCheck(game, o);
    }

    /// <summary>
    /// Command kind hook of the 0x320 handler: a capital ship [0x14, 0x1C) clears the CommandKind of every Existing character
    /// [0x30, 0x3C) aboard, whatever the parents. A character keeps a CommandKind only when old and new parent are capital ships
    /// of the same fleet. Palpatine's Emperor-at-Coruscant recompute is <see cref="GameState.EmperorAtCoruscant"/> (evaluated on read).
    /// </summary>
    static void CommandKinds(GameObject o, GameObject? old, GameObject now)
    {
        static bool Capital(GameObject? x) => x is not null && Family.Of(x.Type) is >= 0x14 and < 0x1C;
        if (Capital(o))
        {
            foreach (var c in o.Contents.OfType<Character>())
                if (Family.Of(c.Type) is >= 0x30 and < 0x3C && c.Existing && c.CommandKind != 0) c.CommandKind = 0;
        }
        else if (o is Character ch && Family.Of(ch.Type) is >= 0x30 and < 0x3C && ch.CommandKind != 0
            && !(Capital(old) && Capital(now) && old!.Container == now.Container))
            ch.CommandKind = 0;
    }

    /// <summary>
    /// 0x320 handler, in order: the story hook (F8) and the command kinds hook first; then the travel (object, from the old parent
    /// to the new one); then the parents, grouped by slot, old before new: the Existing hook if Existing, then the Usable hook if
    /// Usable; if the parents' systems differ, their system Usable hook if Usable. Last, the final-move test on the old parent.
    /// The DeploymentKey refresh, the Damaged step and the remaining hooks have no sim effect.
    /// </summary>
    static void OnParentChanged(GameState game, GameObject o, GameObject? old, GameObject now)
    {
        Story.Notified(game, o); // F8 hook; Inferred: before the travel
        CommandKinds(o, old, now); // command kinds hook
        DeathStar.ParentChanged(game, o, old, now); // Death Star parent hook
        Fleets.Travel(game, o, old, now);
        if (o.Existing) { ContentExistingChanged(game, old, o, false); ContentExistingChanged(game, now, o, true); }
        if (o.Usable) { ContentUsableChanged(game, old, o); ContentUsableChanged(game, now, o); }
        StarSystem? a = old?.System, b = now.System;
        if (a != b && o.Usable)
        {
            SystemReevaluate(game, a, o, true);
            SystemReevaluate(game, b, o, true);
        }
        if (old is not null) FinalMoveTest(game, old);
        if (o.Destroyed && Deletable(o)) Acknowledge(game, o, Owner.None); // the moved object acknowledges itself
    }
}
