namespace OpenSWR.Sim;

/// <summary>
/// A hangar for one carrier, either a capital ship or the Death Star scene. The aboard count is the shared
/// <see cref="BattleEngine.Unit.Docked"/> and the capacity is the shared <see cref="BattleEngine.Unit.Capacity"/> (SD FighterCapacity,
/// Inferred: the traced battle fits it), so the automatic finish reads the same counts.
/// </summary>
internal sealed class Hangar(BattleEngine.Unit owner)
{
    /// <summary>The carrier (or the scene).</summary>
    public BattleEngine.Unit Owner { get; } = owner;
    /// <summary>Places used: aboard plus reservations of fighters in flight.</summary>
    public int Places;
    /// <summary>Next launch time (0.0 = not scheduled), compared with the world stamp.</summary>
    public double Next;
    /// <summary>Launchable (docked at setup, not launched yet), landed (never launched again) and in flight (holding a reservation).</summary>
    public readonly List<TacticalUnit> Launchable = [], Landed = [], InFlight = [];
}

public sealed partial class TacticalUnit
{
    /// <summary>The carrier (a fighter's own or assigned hangar owner), none: null.</summary>
    public GameObject? Carrier => U.Carrier?.Object;
    /// <summary>Tractor beam range (SD TractorBeamRange, capital ships).</summary>
    internal double TractorRange;
    /// Held unit (none: null) and the request-pending flag.
    internal TacticalUnit? Held;
    internal bool TractorPending;
    /// <summary>The unit this one holds in its tractor beam, none: null.</summary>
    public TacticalUnit? Holding => Held;
    /// Units holding this one.
    public IReadOnlyList<TacticalUnit> HeldBy => Holders;
    /// <summary>
    /// Crit total: the entry SpeedCH + ShieldCH + hyperdrive crits + TractorCH (without WeaponCH), + 1 per crit call
    /// in the battle (capped counters too), - 1 per damage-control fix.
    /// </summary>
    public int CritTotal { get; internal set; }
    /// Repair timer (ms; 0 when the ship is created) and DamageControl (SD).
    internal double RepairTimer, DamageControl;
    /// In the world list of units taking part (also: in its spatial tree).
    internal bool InWorld;
}

/// <summary>
/// Tactical fighters, hangars, withdraw helpers, tractor beams and damage control. The world lists of units taking part,
/// recovered and withdrawn, the world remove, setup dock and launch, the Recover handlers (messages 0x2C and 0x1A), carrier death and withdraw, the scuttle (0x2F),
/// tractor requests and messages 0x28 and 0x29, damage control with the fix messages 0x23 to 0x27. The Death Star scene's hangar sits at
/// the point (-W/2, 0, 0); it launches only for a local Empire while the scene is alive, and docks while it is alive.
/// </summary>
public sealed partial class TacticalBattle
{
    readonly List<TacticalUnit> _part = []; // units taking part
    readonly List<TacticalUnit> _recovered = [], _withdrawn = []; // recovered and withdrawn units
    readonly Dictionary<BattleEngine.Unit, Hangar> _hangars = [];

    /// The units taking part, in insertion order (setup order, launches appended).
    public IReadOnlyList<TacticalUnit> Taking => _part;

    internal Hangar HangarOf(BattleEngine.Unit owner)
    {
        if (!_hangars.TryGetValue(owner, out var h)) _hangars[owner] = h = new Hangar(owner);
        return h;
    }

    /// <summary>The hangar of a carrier unit (none for a ship that never had one).</summary>
    internal Hangar? FindHangar(BattleEngine.Unit owner) => _hangars.GetValueOrDefault(owner);

    void AddToWorld(TacticalUnit t)
    {
        _part.Add(t);
        t.InWorld = true;
    }

    /// <summary>
    /// World remove (destroy, recover, withdraw): out of the taking-part list and its spatial tree, out of its side's lists
    /// and out of its group; sets its state to 0 (the caller then writes 5, 9 or 11). The pending list is left alone.
    /// </summary>
    void WorldRemove(TacticalUnit t)
    {
        _part.Remove(t);
        t.InWorld = false;
        foreach (var side in Sides) { side.Ships.Remove(t.U); side.Fighters.Remove(t.U); }
        Leave(t);
        t.U.State = 0;
    }

    // ---- setup dock and launch ----

    /// <summary>
    /// Setup dock for every fighter docked by the shared setup (the pending list, in setup order), whose capacity test and
    /// docked count the shared setup already made: the places used go up by one, the fighter joins the launchable list, and the first launch time is 3 + rand(0, 8) + 5.0 when none is set (tactical
    /// engine only). A full hangar docked nothing: that fighter stays hidden in state 1 and pending, keeps its carrier key
    /// in the result (Inferred) and never launches.
    /// </summary>
    void SetupDocks()
    {
        foreach (var u in Sides[0].Pending)
        {
            if (u.Overflow || BattleEngine.Resolve(u.Carrier, Sides) is not { } c) continue;
            var h = HangarOf(c);
            h.Places++;
            h.Launchable.Add(_byUnit[u]);
            if (h.Next == 0) h.Next = Stamp + _between(0, 8) + 5.0;
        }
    }

    /// <summary>Launch and docking point: (carrier x, carrier y - half height, carrier z); the scene's is (-W/2, 0, 0).</summary>
    internal V3 LaunchPoint(Hangar h) => h.Owner.Type == 2 ? ScenePoint : _byUnit.TryGetValue(h.Owner, out var c) ? c.Position - new V3(0, c.HalfHeight, 0) : V3.Zero;

    /// <summary>
    /// Launch scheduler (unit update, every active capital ship): empty launchable list means nothing; next time 0.0 means schedule
    /// the stamp plus rand(0, 8) + 5.0 and nothing else; stamp below the next time means nothing; else insert message 0x19 (owner, first
    /// launchable fighter) stamped with the world stamp and flag 1, then, while the docked count is not 0, the next launch time.
    /// </summary>
    void Schedule(Hangar h)
    {
        if (h.Launchable.Count == 0) return;
        if (h.Next == 0)
        {
            h.Next = Stamp + _between(0, 8) + 5.0;
            return;
        }
        if (Stamp < h.Next) return;
        var f = h.Launchable[0];
        Insert(Stamp, 0x19, f, () => LaunchMessage(f, h));
        if (h.Owner.Docked != 0) h.Next = Stamp + _between(0, 8) + 5.0;
    }

    /// <summary>
    /// Message 0x19: if the fighter's group is on simulation mission 2 (Recover), nothing happens (it stays first in the list, so a
    /// Recover group never launches); else a ship's hangar launches while the ship is active, the scene's while it is alive. Inferred: a fighter
    /// no longer in the launchable list (carried along, killed) does nothing.
    /// </summary>
    void LaunchMessage(TacticalUnit f, Hangar h)
    {
        if (f.Group is { Mission: 2 }) return;
        if (!(h.Owner.Type == 2 ? SceneAlive : IsActive(h.Owner)) || !h.Launchable.Contains(f)) return;
        Launch(f, h);
    }

    /// <summary>
    /// Launch: out of the launchable list; position set to the launch point; carrier set to the hangar owner; state 2; the docked count and places used each go down by one. Then
    /// the fighter leaves the pending list, joins the taking-part list, its spatial tree and its side's list; the group's leader (Inferred: the
    /// navigator's leader) resets the group's nav point to its position. Group membership and orders are kept.
    /// </summary>
    void Launch(TacticalUnit f, Hangar h)
    {
        h.Launchable.Remove(f);
        f.Position = f.Previous = LaunchPoint(h);
        f.U.Carrier = h.Owner;
        f.U.State = 2;
        h.Owner.Docked--;
        h.Places--;
        Sides[0].Pending.Remove(f.U);
        AddToWorld(f);
        Sides[f.Side].Fighters.Add(f.U);
        if (f.Group is { Nav: { } n } lg && lg.SimLeader == f) n.Point = f.Position; // the simulation leader
        Sounds.Add(0x1B); // launch sound
    }

    // ---- Recover internals ----

    /// Reserve a place for fighter f in the hangar: if the places used are below capacity, add one, mark f as reserved, add f to the in-flight list, post message 0x2C (f, owner); returns true on success.
    bool Reserve(BattleEngine.Unit owner, TacticalUnit f)
    {
        var h = HangarOf(owner);
        if (h.Places >= owner.Capacity) return false;
        h.Places++;
        f.Recovery = 1;
        h.InFlight.Add(f);
        Post(Stamp, 0x2C, () => Assigned(f, owner));
        return true;
    }

    /// Message 0x2C: marks the fighter as assigned and sets its carrier to the message's carrier (none possible: an orphan).
    void Assigned(TacticalUnit f, BattleEngine.Unit? owner) => (f.Recovery, f.U.Carrier) = (2, owner);

    /// True when the places used equal capacity and the launchable list is empty.
    bool FullEmpty(BattleEngine.Unit owner)
    {
        var h = HangarOf(owner);
        return h.Places == owner.Capacity && h.Launchable.Count == 0;
    }

    /// <summary>
    /// Message 0x1A: a ship's hangar docks while the ship is active, the scene's while it is alive. Docking adds the fighter to the landed list,
    /// takes it out of the in-flight list, adds one to the docked count, marks it as docked, and the fighter leaves its group (UI and simulation; an emptied group is deleted).
    /// It is then added to the recovered list, removed from the world and set to state 5. When the local side's last member docks, the
    /// "recovered" voice (0x87 or 0x105, plus the group's colour) plays.
    /// </summary>
    void DockMessage(TacticalUnit f, BattleEngine.Unit owner)
    {
        if (!(owner.Type == 2 ? SceneAlive : IsActive(owner))) return;
        var h = HangarOf(owner);
        h.Landed.Add(f);
        h.InFlight.Remove(f);
        owner.Docked++;
        f.Recovery = 4;
        var g = LeaveGroup(f);
        _recovered.Add(f);
        WorldRemove(f);
        f.U.State = 5;
        if (f.Side == Local && g is not null && !_groups.Contains(g)) Ui.Voices.Add((f.Side == 0 ? 0x87 : 0x105) + g.Colour);
    }

    /// The fighter leaves its group (UI and simulation); an emptied group is deleted. Returns the old simulation group.
    TacticalGroup? LeaveGroup(TacticalUnit f)
    {
        var g = f.Group;
        if (f.UiGroup is { } ui) UiLeave(ui, f);
        Leave(f);
        if (g is not null && g.SimLeader is null) DeleteGroup(g);
        return g;
    }

    /// <summary>
    /// Called when a capital ship is destroyed: every fighter in flight to its hangar is unassigned (places used down by one,
    /// removed from the list, message 0x2C (f, none): an orphan); its tractor hold is released (0x29).
    /// </summary>
    void ShipLost(TacticalUnit t)
    {
        if (FindHangar(t.U) is { } h)
            foreach (var f in h.InFlight.ToList())
            {
                h.Places--;
                h.InFlight.Remove(f);
                Post(Stamp, 0x2C, () => Assigned(f, null));
            }
        if (t.Held is { } held)
        {
            PostRelease(t, held);
            t.TractorPending = true;
        }
    }

    /// <summary>
    /// Destroy: only a unit in state 1, 2, 4, 5, 6 or 10; in order hull 0, the destroy hook
    /// (fighter: carrier given up; capital ship: <see cref="ShipLost"/>), the group leave of every unit kind (UI leave on the
    /// simulation group, then the simulation leave), world remove, a capital ship's launchable and landed fighters destroyed, state 11.
    /// </summary>
    internal void Destroy(TacticalUnit t)
    {
        var u = t.U;
        // States 0, 3, 7, 8, 9 and 11 only clear the
        // box flags, which the UI recomputes every frame (TacticalUi.Box): nothing to do
        if (u.State is not (1 or 2 or 4 or 5 or 6 or 10)) return;
        u.Hull = 0;
        if (u.Type == 1 && u.Carrier is { } c) // never remote here: single player
        {
            // Unassign: remove from the in-flight list, one place less, message 0x2C (f, none); reset the reservation and the carrier
            if (FindHangar(c) is { } h && h.InFlight.Remove(t))
            {
                h.Places--;
                Post(Stamp, 0x2C, () => Assigned(t, null));
            }
            (t.Recovery, u.Carrier) = (0, null);
        }
        if (u.Type == 0) ShipLost(t);
        GroupLeave(t);
        WorldRemove(t);
        if (u.Type == 0 && FindHangar(u) is { } own)
            foreach (var f in own.Launchable.Concat(own.Landed).ToList()) Destroy(f);
        u.State = 11;
    }

    /// <summary>
    /// Destroy step 3, for the unit's simulation group: its UI leave (no target list touched,
    /// no 0x37), then the leave from the simulation group (simulation leader handed to the lowest other member, navigator); a
    /// group whose simulation leader became none is deleted (its HUD slot is freed, and a mode showing it goes idle).
    /// </summary>
    void GroupLeave(TacticalUnit t)
    {
        if (t.Group is not { } g) return;
        UiLeave(g, t);
        Leave(t);
        if (g.SimLeader is null) DeleteGroup(g);
    }

    /// Scuttle (message 0x2F): only an active unit with hull &gt; 0: state set to 2, the death effect (w = 4) and the explosion sound, destroy.
    internal void Scuttle(TacticalUnit t)
    {
        if (!t.Active || t.U.Hull <= 0) return;
        t.U.State = 2;
        Death(t, 4);
        Destroy(t);
    }

    /// <summary>
    /// Message 0x2E: an active unit, or one in state 7 or 8, is withdrawn: added to the withdrawn list, world remove, state 9; a capital ship
    /// takes every launchable fighter not in state 3 along (into the landed list, state 5, counts unchanged).
    /// </summary>
    internal void Retreat(TacticalUnit t)
    {
        if (!(t.Active || t.State is 7 or 8)) return;
        _withdrawn.Add(t);
        WorldRemove(t);
        t.U.State = 9;
        if (FindHangar(t.U) is { } h) TakeAlong(h);
    }

    /// Every launchable fighter not in state 3 goes into the landed list, state 5 (counts unchanged).
    void TakeAlong(Hangar h)
    {
        foreach (var f in h.Launchable.Where(f => f.State != 3).ToList())
        {
            h.Launchable.Remove(f);
            h.Landed.Add(f);
            f.U.State = 5;
        }
    }

    // ---- tractor beams ----

    /// Tractor power: P * hf - P * 0.25 * TractorCH, at least 0 (P = the tractor beam power).
    internal static double TractorPowerOf(TacticalUnit h) => Math.Max(0, h.TractorPower * h.U.Hf - h.TractorPower * 0.25 * h.U.TractorCh);

    /// <summary>
    /// Tractor request for holder H and target P, from the fire decision (P = the group's current target, none without group or
    /// target): nothing while a request is pending or without beam (P = 0); no target means release a hold; the held unit is kept while
    /// active, powered and within range, else released; holding nothing: powered and within range means hold; holding another means
    /// release first. Every post sets the pending flag.
    /// </summary>
    internal void Tractor(TacticalUnit h, TacticalUnit? p)
    {
        if (h.TractorPending || h.TractorPower == 0) return;
        if (p is null)
        {
            if (h.Held is { } held) Request(() => PostRelease(h, held));
            return;
        }
        bool reach = TractorPowerOf(h) != 0 && Distance(h, p) <= h.TractorRange;
        if (h.Held == p) { if (!(p.Active && reach)) Request(() => PostRelease(h, p)); }
        else if (h.Held is { } other) Request(() => PostRelease(h, other));
        else if (reach) Request(() => Post(Stamp, 0x28, () => Hold(h, p)));

        void Request(Action post)
        {
            post();
            h.TractorPending = true;
        }
    }

    void PostRelease(TacticalUnit h, TacticalUnit t) => Post(Stamp, 0x29, () => Release(h, t));

    /// Message 0x28: if both are active, H holds T (T's holder list gains H; the list was empty: sound 6), pending cleared; otherwise nothing and H stays pending for good (as read).
    void Hold(TacticalUnit h, TacticalUnit t)
    {
        if (!h.Active || !t.Active) return;
        h.Held = t;
        if (t.Holders.Count == 0) Sounds.Add(6);
        if (!t.Holders.Contains(h)) t.Holders.Add(h);
        h.TractorPending = false;
    }

    /// Message 0x29: H holds nothing, pending cleared; H leaves T's holder list (now empty: sound 7).
    void Release(TacticalUnit h, TacticalUnit t)
    {
        (h.Held, h.TractorPending) = (null, false);
        if (t.Holders.Remove(h) && t.Holders.Count == 0) Sounds.Add(7);
    }

    // ---- damage control ----

    /// <summary>
    /// Per-tick timers (step 1000 ms) of an active capital ship: engine-down and tractor-down -1.0 (floor 0); while the crit total
    /// is not 0 the repair timer drops by 1000, and at 0 or below it is set to 50000 and damage control runs.
    /// </summary>
    void ShipTimers(TacticalUnit t)
    {
        var u = t.U;
        u.EngineDown = Math.Max(0, u.EngineDown - 1.0);
        u.TractorDown = Math.Max(0, u.TractorDown - 1.0);
        if (t.CritTotal == 0) return;
        t.RepairTimer -= TurnMs;
        if (t.RepairTimer > 0) return;
        t.RepairTimer = 50000;
        Repair(t);
    }

    /// <summary>
    /// Damage control (DC = SD DamageControl): nothing without crits or DC; a roll of rand(1, 100) above DC does nothing; j = 1
    /// (total &lt; 2) else rand(1, total); the first of SpeedCH, ShieldCH, hyperdrive crits, TractorCH, WeaponCH with j &lt;= its count gets
    /// its fix message (0x26, 0x23, 0x27, 0x25, 0x24), else j is reduced by the count; beyond the five: nothing.
    /// </summary>
    void Repair(TacticalUnit t)
    {
        var u = t.U;
        if (t.CritTotal == 0 || t.DamageControl == 0) return;
        if (_between(1, 100) > t.DamageControl) return;
        int j = t.CritTotal < 2 ? 1 : _between(1, t.CritTotal);
        foreach (var (count, kind, k) in new[] { (u.SpeedCh, 0x26, 3), (u.ShieldCh, 0x23, 0), (u.HyperCh, 0x27, 4), (u.TractorCh, 0x25, 2), (u.WeaponCh, 0x24, 1) })
        {
            if (j <= count)
            {
                Post(Stamp, kind, () => Fix(t, k));
                return;
            }
            j -= count;
        }
    }

    /// Fixes 0x23 to 0x27 (k = 0 shield, 1 weapon, 2 tractor, 3 speed, 4 hyperdrive): while the total is not 0, the total and counter k go down by one. Hull and shields are not restored.
    void Fix(TacticalUnit t, int k)
    {
        if (t.CritTotal == 0) return;
        t.CritTotal--;
        var u = t.U;
        switch (k)
        {
            case 0: u.ShieldCh--; break;
            case 1: u.WeaponCh--; break;
            case 2: u.TractorCh--; break;
            case 3: u.SpeedCh--; break;
            default: u.HyperCh--; break;
        }
    }
}
