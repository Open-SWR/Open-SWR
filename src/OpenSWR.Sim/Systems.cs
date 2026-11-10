using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>
/// System state driven by support and resources: support setter and its hook (control, owner change, garrison requirement,
/// production modifier), uprisings (start, incidents, end, incite / subdue effects), the sector support wave (0x31B, then 0x373) and
/// the galaxy resource events of manager 0xF9 (timers 0x38F, 0x390).
/// DeathStarNearby is set by <see cref="DeathStar"/>. Not modelled: ObservedBy / knowledge
/// (the informant value), the victory check, autorouting consequences.
/// </summary>
public static class Systems
{
    /// <summary>0x373 sector-wave support change: PriorityQueue key 0x100.</summary>
    const int WavePriority = 0x100;

    /// <summary>opp(s): 2 for 1, 1 for 2, 3 for 3.</summary>
    internal static Owner Opp(Owner s) => s switch { Owner.Alliance => Owner.Empire, Owner.Empire => Owner.Alliance, _ => Owner.Neutral };

    static bool Sided(Owner o) => o is Owner.Alliance or Owner.Empire;

    // ---- support ----

    /// <summary>
    /// Support change for side s by delta d. Only sides 1 and 2 on a Populated system count. When a Death Star is nearby,
    /// a change in the Alliance's favour (side 1 gaining, or side 2 losing) is first divided by G 0x1E01.
    /// The share of side s then becomes the old share plus d, clamped to 0 to 100.
    /// </summary>
    public static void ChangeSupport(GameState game, StarSystem s, Owner side, int d)
    {
        if (!Sided(side) || !s.Populated) return;
        if (s.DeathStarNearby && (side == Owner.Alliance ? d > 0 : d < 0)) d /= game.Params.General(0x1E01);
        int v = Clamp(s.SupportFor(side) + d, 0, 100);
        SetSupport(game, s, side == Owner.Alliance ? v : 100 - v);
    }

    /// <summary>
    /// Support setter: Populated systems only, values 0 to 100, acts only on change. After the game-ready flag it runs the support hook,
    /// then for core systems it copies the value into the non-live side views.
    /// </summary>
    public static void SetSupport(GameState game, StarSystem s, int value)
    {
        if (!s.Populated || value is < 0 or > 100 || value == s.Support) return;
        s.Support = value;
        if (!game.Ready) return;
        OnSupportChanged(game, s);
        // Core systems only, and they also get the frozen copy. Rim systems have no copy.
        // The copy follows the support hook returning nonzero. Inferred: it always does here.
        if (Family.Of(s.Type) is >= 0x90 and < 0x92) Knowledge.CopySupport(game, s);
    }

    /// <summary>
    /// Support hook, in order: control (owner change, 0x31B), garrison requirement, SmugglingPercent,
    /// ProductionModifier, and with Blockade timer 0x386 Base set to G 0x1E0E when the blockading side is the popular side,
    /// otherwise G 0x1E10.
    /// </summary>
    static void OnSupportChanged(GameState game, StarSystem s)
    {
        StoreControl(game, s, Control(game, s));
        SetRequirement(game, s, Requirement(game, s));
        Economy.UpdateSmuggling(game, s);
        UpdateProductionModifier(game, s);
        if (s.Blockade)
            TimerCatalogue.Slot(game, 0x386, s.Ref, s).SetBase(game, game.Params.General(Combat.BlockadingSide(s) == Combat.PopularSide(s) ? 0x1E0Eu : 0x1E10u));
    }

    /// <summary>
    /// Control, built from two parts. Support control is 3 when unpopulated; otherwise 1 when the Alliance share is at least G 0x1E50,
    /// 2 when the Empire share is at least G 0x1E50, else 3. Military control comes from the Usable troops directly in the system:
    /// only side 1 gives 1, only side 2 gives 2, neither gives 0, and both leave it unchanged. The owner is the military control
    /// when that is not 0, otherwise the support control.
    /// </summary>
    public static SystemControl Control(GameState game, StarSystem s)
    {
        int g = game.Params.General(0x1E50);
        var support = !s.Populated ? Owner.Neutral : s.Support >= g ? Owner.Alliance : 100 - s.Support >= g ? Owner.Empire : Owner.Neutral;
        bool Troops(Owner side) => s.Contents.Any(o => Family.IsTroop(o.Type) && o.Usable && o.Owner == side);
        bool a = Troops(Owner.Alliance), e = Troops(Owner.Empire);
        var military = a && e ? s.Control.Military : a ? Owner.Alliance : e ? Owner.Empire : Owner.None;
        return new(military != Owner.None ? military : support, support, military);
    }

    /// <summary>
    /// Store, acting only on change. Before the game-ready flag the control hook does nothing. When the owner part changed, the owner change runs;
    /// any change posts 0x31B (old, new). One system flag (no reader) is not kept.
    /// </summary>
    internal static void StoreControl(GameState game, StarSystem s, SystemControl control)
    {
        var old = s.Control;
        if (old == control) return;
        s.Control = control;
        if (!game.Ready) return;
        if (old.Owner != control.Owner) ChangeOwner(game, s, control.Owner);
        Objects.Notify(game, g => OnControlChanged(g, s, old, control));
    }

    /// <summary>req0: ceildiv(G 0x1E51 - share, -G 0x1E52) while the share is below G 0x1E51, else 0.</summary>
    internal static int BaseRequirement(GameState game, int share)
    {
        var g = game.Params;
        return share < g.General(0x1E51) ? CeilDiv(g.General(0x1E51) - share, -g.General(0x1E52)) : 0;
    }

    /// <summary>req0, divided by G 0x1E00 if DeathStarNearby and owner 2, then times G 0x1E02 with the doubling flag.</summary>
    static int Requirement(GameState game, StarSystem s, bool doubled)
    {
        int r = BaseRequirement(game, s.SupportFor(s.Owner));
        if (s.DeathStarNearby && s.Owner == Owner.Empire) r /= game.Params.General(0x1E00);
        return doubled ? r * game.Params.General(0x1E02) : r;
    }

    /// <summary>Garrison requirement: owner 1 or 2 and Populated, doubled while Uprising; else 0.</summary>
    internal static int Requirement(GameState game, StarSystem s) => Sided(s.Owner) && s.Populated ? Requirement(game, s, s.Uprising) : 0;

    /// <summary>Usable troops of families [0x10, 0x14) directly in the system: of any owner, or of one side.</summary>
    static int Troops(StarSystem s, Owner? side = null) => s.Contents.Count(o => Family.IsTroop(o.Type) && o.Usable && (side is null || o.Owner == side));

    /// <summary>Surplus: Usable troops of any owner directly in the system, minus the requirement; 0 if unowned.</summary>
    internal static int Surplus(StarSystem s) => Sided(s.Owner) ? Troops(s) - s.GarrisonRequirement : 0;

    /// <summary>Requirement setter, acting only on change. After the game-ready flag it recomputes the surplus.</summary>
    static void SetRequirement(GameState game, StarSystem s, int value)
    {
        if (value == s.GarrisonRequirement) return;
        s.GarrisonRequirement = value;
        if (game.Ready) UpdateSurplus(game, s);
    }

    /// <summary>Surplus setter, acting only on change. After the game-ready flag it updates Uprising, then Suppressing.</summary>
    internal static void UpdateSurplus(GameState game, StarSystem s)
    {
        int value = Surplus(s);
        if (value == s.GarrisonSurplus) return;
        s.GarrisonSurplus = value;
        if (!game.Ready) return;
        RecomputeUprising(game, s);
        RecomputeSuppressing(game, s);
    }

    /// <summary>
    /// Surplus hook, during setup final init: Uprising is set when the owner is 1 or 2, the system is Populated, and either Uprising was already set
    /// or the surplus is below 0. The ControlKindUprising write has no sim reader and is not kept.
    /// </summary>
    internal static void RecomputeUprising(GameState game, StarSystem s) =>
        SetUprising(game, s, Sided(s.Owner) && s.Populated && (s.Uprising || s.GarrisonSurplus < 0));

    /// <summary>
    /// Suppressing (set only on change) is true when the owner is 1 or 2, the system is Populated, Uprising is set, and the surplus is 0 or more.
    /// After the game-ready flag, timer 0x383 Active follows Suppressing (Base G 0x1E13, Variance G 0x1E14).
    /// </summary>
    internal static void RecomputeSuppressing(GameState game, StarSystem s)
    {
        bool value = Sided(s.Owner) && s.Populated && s.Uprising && s.GarrisonSurplus >= 0;
        if (value == s.Suppressing) return;
        s.Suppressing = value;
        if (!game.Ready) return;
        var timer = TimerCatalogue.Slot(game, 0x383, s.Ref, s);
        if (value) timer.Start(game, game.Params.General(0x1E13), game.Params.General(0x1E14));
        else timer.SetActive(game, false);
    }

    /// <summary>
    /// Uprising setter, acting only on change. After the game-ready flag, in order: every Existing processing facility (key order) is Suspended
    /// when Uprising or Blockade is set; the shipyard, training and construction managers are balanced; the garrison requirement is recomputed
    /// (this updates the surplus and Uprising, and may set Uprising again at once on a clear, nested); Garrisoned is updated; Suppressing is updated;
    /// 0x303 is posted; timers 0x384 and 0x38D follow Uprising (read again afterwards, so a nested change wins).
    /// ControlKindUprising is not kept.
    /// </summary>
    static void SetUprising(GameState game, StarSystem s, bool value)
    {
        if (value == s.Uprising) return;
        s.Uprising = value;
        game.ViewMessage(0x14B, s); // the copy into the live views posts 0x14B on change
        // before the game-ready flag the core copy into both views also runs (silently); setup step 24's full refresh of every core
        // system view copies the same value, so it is left to that refresh
        if (!game.Ready) return;
        Suspend(game, s);
        SetRequirement(game, s, Requirement(game, s));
        Economy.UpdateGarrison(game, s);
        RecomputeSuppressing(game, s);
        Objects.Notify(game, g => OnUprisingChanged(g, s));
        var p = game.Params;
        var (support, incident) = (TimerCatalogue.Slot(game, 0x384, s.Ref, s), TimerCatalogue.Slot(game, 0x38D, s.Ref, s));
        if (s.Uprising) { support.Start(game, p.General(0x1E12), 0); incident.Start(game, p.General(0x1E15), p.General(0x1E16)); }
        else { support.SetActive(game, false); incident.SetActive(game, false); }
        // core systems: both side views again, frozen ones included
        if (Family.Of(s.Type) is >= 0x90 and < 0x92) Knowledge.CopyUprising(game, s);
    }

    /// <summary>
    /// 0x303 handler: phase check of every Usable mission in the mission container (key order, families [0x50, 0x60)) whose
    /// TargetLocationKey is this system. A step with no sim effect runs, then the side recompute for side 1, then for side 2.
    /// </summary>
    static void OnUprisingChanged(GameState game, StarSystem s)
    {
        Missions.UprisingChanged(game, s);
        Economy.RecomputeSideTotals(game);
    }

    /// <summary>
    /// Uprising and Blockade hooks: every Existing facility (key order) is Suspended when Uprising or Blockade is set, then the shipyard,
    /// training and construction managers are balanced.
    /// </summary>
    internal static void Suspend(GameState game, StarSystem s)
    {
        SuspendFacilities(game, s);
        foreach (var kind in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction })
            Economy.Balance(game, s.Managers[(int)kind]);
    }

    /// <summary>Every Existing facility (key order) is Suspended when Uprising or Blockade is set. Setter with hooks, acting only on change.</summary>
    internal static void SuspendFacilities(GameState game, StarSystem s)
    {
        foreach (var f in s.Contents.OfType<Facility>().Where(f => f.Existing).ToList())
            Economy.SetSuspended(game, f, s.Uprising || s.Blockade);
    }

    /// <summary>Timer 0x384: if Uprising, support change G 0x1E11 for the owner.</summary>
    internal static void UprisingSupport(GameState game, StarSystem s)
    {
        if (s.Uprising) ChangeSupport(game, s, s.Owner, game.Params.General(0x1E11));
    }

    /// <summary>Timer 0x383: if Uprising, clears Uprising.</summary>
    internal static void SuppressTimer(GameState game, StarSystem s)
    {
        if (s.Uprising) SetUprising(game, s, false);
    }

    // ---- incidents ----

    /// <summary>
    /// InformantIncident pulse. When the owner is not 3, v = T(INFORMTB, roll(G 0x1E1E) + G 0x1E1D) is used for the knowledge update
    /// of the opposing side (deferred; Inferred: no draw).
    /// </summary>
    internal static void Informant(GameState game, StarSystem s)
    {
        if (s.Owner != Owner.Neutral)
        {
            var g = game.Params;
            int v = Missions.Table(game, "INFORMTB", game.Random.Roll(g.General(0x1E1E), DrawSite.Informant) + g.General(0x1E1D)) ?? 0;
            Knowledge.Update(game, s, v, Opp(s.Owner));
        }
        // the 0x153 posts reach both side views (frozen ones too: the view owners after the knowledge update
        game.InformantTrace?.Add(new(game.Day, s, ViewOwner(game, s, Owner.Alliance), ViewOwner(game, s, Owner.Empire)));
    }

    /// <summary>The owner of the side's view of the system, null without a view (UI records).</summary>
    static Owner? ViewOwner(GameState game, StarSystem s, Owner side) =>
        Knowledge.IsLive(s, side) || game.Views.ContainsKey((s, side)) ? Knowledge.See(game, s, side).Owner : null;

    /// <summary>
    /// Runs a galaxy event of timer 0x38F / 0x390 and records it for the message builder (message type 0x20): the energy
    /// and raw-material values before and after, the facilities [0x20, 0x30) destroyed meanwhile (destroy order, with the live sides
    /// of their views: the 0x302 receivers) and the system's live sides (the flag pulse reaches live views only). UI only.
    /// </summary>
    static void RecordResources(GameState game, StarSystem s, bool disaster, Action run)
    {
        if (game.ResourceTrace is null) { run(); return; }
        int e = s.Energy, r = s.RawMaterials;
        var saved = game.StrikeDestroys;
        var destroyed = game.StrikeDestroys = []; // the destroy hook's capture (Objects.Destroy)
        try { run(); }
        finally { game.StrikeDestroys = saved; }
        game.ResourceTrace.Add(new(game.Day, s, disaster, s.Owner, Combat.LiveSides(s), e, s.Energy, r, s.RawMaterials,
            [.. destroyed.Where(d => Family.Of(d.Unit.Type) is >= 0x20 and < 0x30).Select(d => (d.Unit, d.Live))]));
    }

    /// <summary>
    /// Uprising incident, for owner 1 or 2 only. Uprising is not tested here. The uprising roll gives r1 and r2 = roll(G 0x1E1C) + G 0x1E1B.
    /// f = G 0x1E00 when DeathStarNearby and the owner is 2, else 1. key = r1 + r2 + req0(share(o)) - f * n + (S + I) - nST, where n is the
    /// Usable troops of side o directly here and nST is Missions.Stormtroopers. u1 = T(UPRIS1TB, key) and u2 = T(UPRIS2TB, key).
    /// Effect u1 runs, then effect u2, and the support change is the surplus of side o.
    /// </summary>
    public static void Incident(GameState game, StarSystem s)
    {
        var o = s.Owner;
        if (!Sided(o)) return;
        Informant(game, s);
        var g = game.Params;
        int r1 = game.Random.Roll(g.General(0x1E1C), DrawSite.UprisingRoll) + g.General(0x1E1B);
        int r2 = game.Random.Roll(g.General(0x1E1C), DrawSite.UprisingRoll) + g.General(0x1E1B);
        int f = s.DeathStarNearby && o == Owner.Empire ? g.General(0x1E00) : 1;
        int key = r1 + r2 + BaseRequirement(game, s.SupportFor(o)) - f * Troops(s, o) + (s.SubdueTotal + s.InciteTotal) - Missions.Stormtroopers(s);
        int u1 = Missions.Table(game, "UPRIS1TB", key)!.Value, u2 = Missions.Table(game, "UPRIS2TB", key)!.Value;
        IncidentEffect(game, s, u1, o);
        IncidentEffect(game, s, u2, o);
        ChangeSupport(game, s, o, s.IncidentSupportDelta);
    }

    /// <summary>
    /// Incident effect for side o, over Usable objects of o directly in the system (key order). Case 1: a random facility
    /// [0x20, 0x30). Case 2: a random troop, roll(c - 1), destroyed with DestroyedReason 8. Case 3: a non-captured character [0x30, 0x3C).
    /// c is the non-captured count and k = roll(c - 1); the cursor passes k non-captured ones and picks the character under it,
    /// captured or not (quirk), then the injury check. Case 4: the same over the captured count, setting State to 1
    /// (the hook runs only on change). Case 5: the same State write on every captured character.
    /// Characters come from the owner-filtered direct-contents iterator (mode 1), so prisoners held by o here are included.
    /// </summary>
    static void IncidentEffect(GameState game, StarSystem s, int value, Owner o)
    {
        bool Own(GameObject x, uint lo, uint hi) => x.Usable && x.Owner == o && Family.Of(x.Type) >= lo && Family.Of(x.Type) < hi;
        var characters = s.Contents.OfType<Character>().Where(c => Own(c, 0x30, 0x3C)).ToList();
        switch (value)
        {
            case 1 or 2:
                var units = s.Contents.Where(x => value == 1 ? Own(x, 0x20, 0x30) : Own(x, 0x10, 0x14)).ToList();
                if (units.Count > 0) Objects.Destroy(game, units[game.Random.Roll(units.Count - 1, DrawSite.IncidentPick)], 8);
                break;
            case 3 or 4:
                bool Counted(Character c) => c.Captured == (value == 4);
                int count = characters.Count(Counted);
                if (count == 0) break;
                int k = game.Random.Roll(count - 1, DrawSite.CharacterPick), i = 0;
                for (int passed = 0; passed < k; i++) if (Counted(characters[i])) passed++;
                if (value == 3) Missions.InjuryCheck(game, characters[i]);
                else Prisoners.Escape(game, characters[i]);
                break;
            case 5:
                foreach (var c in characters.Where(c => c.Captured)) Prisoners.Escape(game, c);
                break;
        }
    }

    /// <summary>
    /// Subdue end, for owner 1 or 2. Usable troops of any owner directly here, minus the requirement with no doubling.
    /// If the result is 0 or more, Uprising is cleared.
    /// </summary>
    internal static void SubdueEnd(GameState game, StarSystem s)
    {
        if (Sided(s.Owner) && Troops(s) - Requirement(game, s, doubled: false) >= 0) SetUprising(game, s, false);
    }

    /// <summary>
    /// ProductionModifier = trunc(100 * G 0x1E53 / s) for owner 1 or 2, where s is the owner's share (a share of 0 counts as 1). Owner 3 gets 100.
    /// On change it is pushed to the system's Usable Mines, then Refineries (key order), each changed one recomputing its cycle length
    /// (the others take it when they become Usable, <see cref="Fleets.SystemContentUsableChanged"/>).
    /// </summary>
    public static void UpdateProductionModifier(GameState game, StarSystem s)
    {
        int share = s.SupportFor(s.Owner);
        int modifier = Sided(s.Owner) ? 100 * game.Params.General(0x1E53) / (share == 0 ? 1 : share) : 100;
        if (modifier == s.ProductionModifier) return;
        s.ProductionModifier = modifier;
        foreach (var f in s.Contents.OfType<Facility>().Where(f => f.Usable && Family.Of(f.Type) is >= 0x2C and < 0x30 && f.Modifier != modifier).ToList())
        {
            f.Modifier = modifier;
            Economy.RecomputeCycle(game, f);
        }
    }

    // ---- owner change ----

    /// <summary>
    /// Owner change of a system. Every Existing content (key order) first gets its owner-change request. Then NeverBeenControlled gets the value
    /// (owner 3 or unpopulated) if the update applies, Uprising is cleared, SmugglingPercent is cleared and recomputed, the garrison requirement
    /// is recomputed, timer 0x38E starts for a new owner 1 or 2 (it draws only when it was inactive) and stops for owner 3, and the 0x311 flag
    /// pulse is sent. The system's own 0x302 is posted after the contents' (its manager hook re-checks missions).
    /// </summary>
    static void ChangeOwner(GameState game, StarSystem s, Owner owner)
    {
        var old = s.Owner;
        if (old == owner) return;
        s.Owner = owner;
        // openswr deviation (player request): a captured unpopulated system gives its owner full support (the original keeps
        // its setup value 0, so an Alliance owner smuggles at 75 %); written directly, the support setter refuses unpopulated systems.
        if (!s.Populated && Sided(owner)) s.Support = owner == Owner.Alliance ? 100 : 0;
        game.ViewMessage(0x100, s); // the owner setter's copy into the live views posts 0x100
        Knowledge.RecomputeLive(game, s);   // base owner hook
        Knowledge.RecomputeSystem(game, s); // system owner hook, after the game-ready flag
        if (game.Ready) Repair.RefreshSystem(game, s); // same hook, after the game-ready flag (position Inferred)
        foreach (var o in s.Contents.Where(o => o.Existing).ToList()) Capture(game, o, owner);
        // Managers are direct contents in the original, families 0xA0-0xA5 after every other content. Their owner hook cancels
        // the whole queue (DestroyedReason 0x16, no refund) wherever the units sit, then clears Reserved (0x216). TargetKey is kept.
        // The base part posts the manager's own 0x302 first (its handler only resets TargetKeys).
        foreach (var m in s.Managers.Where(m => m is { Existing: true }).OrderBy(m => m.Ref)) // key order (families 0xA0, 0xA2, 0xA4)
        {
            Knowledge.ManagerOwnerChanged(game, m, old); // view steps 1-2
            Objects.NotifyOwner(game, m, old);
            Objects.Notify(game, g => Knowledge.OwnerFollowUp(g, m, old), 0x800); // 0x318 after the cancel's notifications (step 5)
            foreach (var unit in m.Orders.ToList()) Objects.Destroy(game, unit, 0x16);
            m.Reserved = false;
        }
        Objects.NotifyOwner(game, s, old);
        if (Family.Of(s.Type) is >= 0x90 and < 0x92) Objects.Notify(game, g => Knowledge.CoreOwnerFollowUp(g, s), 0x800); // 0x318
        if (s.NeverBeenControlled && owner != Owner.Neutral && s.Populated)
        {
            s.NeverBeenControlled = false;
            Knowledge.NeverControlledChanged(game, s); // the setter copies into the live views
        }
        SetUprising(game, s, false);
        s.SmugglingPercent = 0;
        Economy.UpdateSmuggling(game, s);
        SetRequirement(game, s, Requirement(game, s));
        var incident = TimerCatalogue.Slot(game, 0x38E, s.Ref, s);
        if (Sided(owner)) incident.Start(game, game.Params.General(0x1E17), game.Params.General(0x1E18));
        else incident.SetActive(game, false);
        Fleets.PulseBattle(game, s);
    }

    /// <summary>
    /// Owner-change request, base part: a Completed object that is not EnrouteActive takes the new owner if the SD record allows it,
    /// else DestroyedReason 3. Otherwise, unless Destroyed, Autorouting is set. Fleets do nothing. Characters run <see cref="Prisoners.OwnerCheck"/>
    /// (a prisoner under another side's parent is released). Fighters set Autorouting unless Destroyed. Special forces do nothing
    /// if OnMission or Destroyed; otherwise the owner becomes the parent's owner if the SD allows it, else DestroyedReason 3.
    /// </summary>
    internal static void Capture(GameState game, GameObject o, Owner owner)
    {
        if (o is Fleet) return;
        if (Family.Of(o.Type) is >= 0x1C and < 0x20) { if (!o.Destroyed) Objects.SetAutorouting(game, o); return; }
        if (o is Agent a)
        {
            if (a is Character c) { Prisoners.OwnerCheck(game, c); return; }
            if (a.OnMission || a.Destroyed || a.Container?.Owner is not { } parent) return;
            if (Allowed(game, a, parent)) Objects.SetOwner(game, a, parent);
            else Objects.Destroy(game, a, 3);
            return;
        }
        if (o.Completed && !o.EnrouteActive)
        {
            if (Allowed(game, o, owner)) Objects.SetOwner(game, o, owner);
            else Objects.Destroy(game, o, 3);
        }
        else if (!o.Destroyed) Objects.SetAutorouting(game, o);
    }

    /// <summary>Whether the SD record allows the side. Side 1 and side 2 each check their own field; side 3 checks both.</summary>
    internal static bool Allowed(GameState game, GameObject o, Owner owner) => game.Definitions.Resolve(o.Type) is { } r && owner switch
    {
        Owner.Alliance => r.Get(SdField.Alliance) != 0,
        Owner.Empire => r.Get(SdField.Empire) != 0,
        _ => r.Get(SdField.Alliance) != 0 && r.Get(SdField.Empire) != 0,
    };

    /// <summary>
    /// Overflow request: characters do nothing. Special forces set Autorouting only if not OnMission and neither Completed nor Destroyed.
    /// The base case, for Completed and not EnrouteActive, sets DestroyedReason 4; otherwise Autorouting is set (also for family 0x20, headquarters).
    /// Facilities of families 0x22-0x25, 0x28-0x2A, 0x2C and 0x2D get DestroyedReason 4 without any test.
    /// </summary>
    internal static void Overflow(GameState game, GameObject o)
    {
        if (o is Character) return;
        if (Family.IsSpecialForce(o.Type))
        {
            if (o is Agent { OnMission: false, Destroyed: false } a && !a.Completed) Objects.SetAutorouting(game, a);
            return;
        }
        // facility overrides for families 0x28-0x2A, 0x2C, 0x2D and 0x22-0x25: DestroyedReason 4 unconditionally
        if (Family.Of(o.Type) is >= 0x22 and <= 0x25 or >= 0x28 and <= 0x2A or 0x2C or 0x2D) { Objects.Destroy(game, o, 4); return; }
        if (o.Completed && !o.EnrouteActive) Objects.Destroy(game, o, 4);
        else Objects.SetAutorouting(game, o);
    }

    // ---- 0x31B and the sector wave ----

    /// <summary>
    /// 0x31B handler. The emperor-at-Coruscant flag of manager 0xFA is evaluated on read (<see cref="GameState.EmperorAtCoruscant"/>).
    /// When the owner part changed, the sector wave runs, and for Coruscant also the victory recompute, then a second wave
    /// (S 0x1423 / S 0x1422, not gated).
    /// </summary>
    static void OnControlChanged(GameState game, StarSystem s, SystemControl old, SystemControl now)
    {
        Owner o = old.Owner, n = now.Owner;
        if (o == n) return;
        Owner o2 = o == Owner.Neutral ? Opp(n) : o, n2 = n == Owner.Neutral ? Opp(o) : n;
        var p = game.Params;
        var col = n2 == Owner.Alliance ? Owner.Alliance : Owner.Empire;
        int Both(uint id) => (o == Owner.Alliance || n == Owner.Alliance ? p.Side(id, Owner.Alliance) : 0)
            + (o == Owner.Empire || n == Owner.Empire ? p.Side(id, Owner.Empire) : 0);
        bool oldMilitary = old.Military != Owner.None, newMilitary = now.Military != Owner.None;
        var (limit, delta) = (oldMilitary, newMilitary) switch
        {
            (false, false) => (p.Side(0x1421, col), Both(0x1420)),
            (false, true) => (p.Side(0x1441, col), p.Side(0x1440, col)),
            (true, false) => (p.Side(0x1443, col), p.Side(0x1442, col)),
            _ => (p.Side(0x1445, col), p.Side(0x1444, col)),
        };
        if (delta != 0 && !(oldMilitary || newMilitary) && Gated(game, o)) delta = 0;
        Wave(game, s, o2, n2, limit, delta);
        if (s.Type != Refs.Coruscant) return;
        Victory.Recompute(game);
        Wave(game, s, o2, n2, p.Side(0x1423, col), Both(0x1422));
    }

    /// <summary>
    /// Wave gate (row 0/0 only): counters reset when the clock Tick differs from the stored one. A wave is allowed only while the
    /// old owner's counter (side 1 or 2) is 0, and that counter is then incremented. Old owner 3 is never gated.
    /// </summary>
    static bool Gated(GameState game, Owner old)
    {
        if (!Sided(old)) return false;
        if (game.WaveTick != game.Clock.Tick) { game.WaveTick = game.Clock.Tick; game.WaveCounts.Clear(); }
        if (game.WaveCounts.GetValueOrDefault(old) != 0) return true;
        game.WaveCounts[old] = 1;
        return false;
    }

    /// <summary>
    /// Wave when D is not 0: every Usable system of the sector (families [lo, hi), key order, the changed one included) that is Populated,
    /// has NeverBeenControlled clear, and has share(X, o') below L gets a 0x373 (priority 0x100) carrying the Alliance-share delta +D
    /// if n' = 1, else -D. When the 0x373 is handled and the system resolves, support changes by that delta for side 1.
    /// </summary>
    internal static void Wave(GameState game, StarSystem s, Owner o2, Owner n2, int limit, int delta, uint lo = 0x90, uint hi = 0x98) =>
        Wave(game, s.Sector, o2, n2, limit, delta, lo, hi);

    /// <summary>The wave over a sector.</summary>
    internal static void Wave(GameState game, Sector sector, Owner o2, Owner n2, int limit, int delta, uint lo = 0x90, uint hi = 0x98)
    {
        if (delta == 0) return;
        int d = n2 == Owner.Alliance ? delta : -delta;
        foreach (var x in game.Systems.Where(x => x.Sector == sector && x.Usable && Family.Of(x.Type) >= lo && Family.Of(x.Type) < hi).OrderBy(x => x.Key).ToList())
            if (x.Populated && !x.NeverBeenControlled && x.SupportFor(o2) < limit)
                Objects.Notify(game, g => { if (!x.Destroyed) ChangeSupport(g, x, Owner.Alliance, d); }, WavePriority);
    }

    // ---- galaxy resource events ----

    /// <summary>System pick: n is the number of Existing systems (registry order). If n is 0 the pick is none, else roll(n - 1) picks one.</summary>
    static StarSystem? Pick(GameState game, string site)
    {
        var systems = game.Systems.Where(x => x.Existing).ToList();
        return systems.Count == 0 ? null : systems[game.Random.Roll(systems.Count - 1, site)];
    }

    /// <summary>
    /// Timer 0x390, galaxy resource event. m is whether the owner has a Usable Mine here, and f whether the owner has a Usable facility
    /// (families [0x20, 0x30)). It always makes one roll(99) and k = T(RESRCTB, r). Case 1: m and R above G 0x1E1F, so R drops by 1.
    /// Case 2: f and E above G 0x1E21, so E drops by 1. Case 3: R below G 0x1E20 and R below E, so R rises by 1.
    /// Case 4: E below G 0x1E22, so E rises by 1. A change sets RawMaterial, then Energy. The Energy flag pulse hook does nothing.
    /// </summary>
    internal static void ResourceEvent(GameState game)
    {
        if (Pick(game, DrawSite.SystemEventB) is not { } s) return;
        var g = game.Params;
        bool Own(uint lo, uint hi) => s.Contents.Any(x => Family.Of(x.Type) >= lo && Family.Of(x.Type) < hi && x.Owner == s.Owner && x.Usable);
        bool mine = Own(0x2C, 0x2D), facility = Own(0x20, 0x30);
        int r = s.RawMaterials, e = s.Energy;
        switch (Missions.Table(game, "RESRCTB", game.Random.Percent(DrawSite.Resources)))
        {
            case 1 when mine && r > g.General(0x1E1F): r--; break;
            case 2 when facility && e > g.General(0x1E21): e--; break;
            case 3 when r < g.General(0x1E20) && r < e: r++; break;
            case 4 when e < g.General(0x1E22): e++; break;
        }
        if (r == s.RawMaterials && e == s.Energy) return;
        RecordResources(game, s, false, () =>
        {
            SetRawMaterials(game, s, r);
            SetEnergy(game, s, e);
        });
    }

    /// <summary>
    /// Timer 0x38F, galaxy disaster event. If E or R is not 0, the flag hook computes the losses. For each i below max(R, E): for i below R,
    /// a chance of (E - lr - le + R) * G 0x1E23 adds 1 to lr; for i below E, a chance of (R - lr - le + E) * G 0x1E23 adds 1 to le.
    /// If none came up, lr is set to 1 when R is not 0, else le is set to 1. Then E drops by le, R becomes min(R - lr, E), Energy is set,
    /// then RawMaterial. Then every Existing facility in families [0x28, 0x30) not Enroute (key order) has a chance of G 0x1E24 to get
    /// DestroyedReason 0xB, then families [0x22, 0x28) follow.
    /// Inferred: the destruction chances are labelled by the flag hook (draw site not given).
    /// </summary>
    internal static void DisasterEvent(GameState game)
    {
        if (Pick(game, DrawSite.SystemEventA) is not { } s) return;
        int r = s.RawMaterials, e = s.Energy;
        if (r == 0 && e == 0) return;
        var g = game.Params;
        int lr = 0, le = 0;
        for (int i = 0; i < Math.Max(r, e); i++)
        {
            if (i < r && game.Random.Chance((e - lr - le + r) * g.General(0x1E23), DrawSite.Losses)) lr++;
            if (i < e && game.Random.Chance((r - lr - le + e) * g.General(0x1E23), DrawSite.Losses)) le++;
        }
        if (lr == 0 && le == 0) { if (r != 0) lr = 1; else le = 1; }
        e -= le;
        RecordResources(game, s, true, () =>
        {
            SetEnergy(game, s, e);
            SetRawMaterials(game, s, Math.Min(r - lr, e));
            foreach (var (lo, hi) in new[] { (0x28u, 0x30u), (0x22u, 0x28u) })
                foreach (var f in s.Contents.Where(f => Family.Of(f.Type) >= lo && Family.Of(f.Type) < hi && f.Existing && !f.Enroute).ToList())
                    if (game.Random.Chance(g.General(0x1E24), DrawSite.Disaster)) Objects.Destroy(game, f, 0xB);
        });
    }

    /// <summary>
    /// RawMaterial setter, with its hook on change. If E is below R, R becomes E (this re-enters the setter). Then the Mines check:
    /// u is the owner's Existing Mines (Inferred: 1 each). If R is below u, the first max(0, number of Mines of any owner - R)
    /// Existing Mines in key order get the overflow request. The system's used Mines count becomes min(u, R).
    /// </summary>
    internal static void SetRawMaterials(GameState game, StarSystem s, int value)
    {
        if (value == s.RawMaterials) return;
        s.RawMaterials = value;
        if (s.Energy < s.RawMaterials) SetRawMaterials(game, s, s.Energy);
        MinesCheck(game, s);
    }

    /// <summary>Mines check, also run when a Mine is added.</summary>
    internal static void MinesCheck(GameState game, StarSystem s)
    {
        var mines = s.Contents.Where(x => Family.Of(x.Type) == 0x2C && x.Existing).ToList();
        UpdateUsedRaw(s);
        if (s.RawMaterials < mines.Count(x => x.Owner == s.Owner))
            foreach (var m in mines.Take(Math.Max(0, mines.Count - s.RawMaterials))) Overflow(game, m);
    }

    /// <summary>
    /// Energy setter, with its hook on change. The same clamp applies: if E is below R, R becomes E. Then the facilities check: u is the
    /// owner's Existing facilities (families [0x20, 0x30)). If E is below u, k = max(0, number of those of any owner - E). Candidates are
    /// taken in key order, minus the Alliance HQ 0x20000005 when the system holds a family-0x20 facility and k is below the candidate count
    /// (Inferred: HQ protected). Then k times, roll(len - 1) picks one candidate for the overflow request.
    /// The system's used facilities count becomes min(u, E).
    /// </summary>
    /// <summary>Facilities check's used count: the smaller of the owner's Existing facilities and E, one per facility.</summary>
    internal static void UpdateUsedEnergy(StarSystem s) =>
        s.UsedEnergy = Math.Min(s.Contents.Count(x => Family.IsFacility(x.Type) && x.Existing && x.Owner == s.Owner), s.Energy);

    /// <summary>Mines check's used count: the smaller of the owner's Existing Mines (one each) and R.</summary>
    internal static void UpdateUsedRaw(StarSystem s) =>
        s.UsedRaw = Math.Min(s.Contents.Count(x => Family.Of(x.Type) == 0x2C && x.Existing && x.Owner == s.Owner), s.RawMaterials);

    internal static void SetEnergy(GameState game, StarSystem s, int value)
    {
        if (value == s.Energy) return;
        s.Energy = value;
        if (s.Energy < s.RawMaterials) SetRawMaterials(game, s, s.Energy);
        FacilitiesCheck(game, s);
    }

    /// <summary>Facilities check, also run in full when a facility is added, overflow roll included.</summary>
    internal static void FacilitiesCheck(GameState game, StarSystem s)
    {
        var facilities = s.Contents.Where(x => Family.IsFacility(x.Type) && x.Existing).ToList();
        UpdateUsedEnergy(s);
        if (s.Energy >= facilities.Count(x => x.Owner == s.Owner)) return;
        int k = Math.Max(0, facilities.Count - s.Energy);
        if (facilities.Any(x => Family.Of(x.Type) == 0x20) && k < facilities.Count) facilities.RemoveAll(x => x.Ref == 0x20000005);
        for (; k > 0 && facilities.Count > 0; k--)
        {
            int i = game.Random.Roll(facilities.Count - 1, DrawSite.EnergyOverflow);
            var f = facilities[i];
            facilities.RemoveAt(i);
            Overflow(game, f);
        }
    }
}

/// <summary>An InformantIncident pulse (message type 0x18): the side views' owners after its knowledge update (null = no view). UI only.</summary>
public sealed record InformantReport(int Day, StarSystem System, Owner? AllianceView, Owner? EmpireView);

/// <summary>A galaxy event 0x38F (disaster) / 0x390 (resources) with its value changes and destroyed facilities (message type 0x20). UI only.</summary>
public sealed record ResourceReport(int Day, StarSystem System, bool Disaster, Owner Owner, SideSet Live, int EnergyOld, int EnergyNew, int RawOld, int RawNew,
    List<(GameObject Unit, SideSet Live)> Destroyed);
