namespace OpenSWR.Sim;

/// <summary>
/// Prisoners: character State with its state and Captured hooks, capture, the 0x30A holding move (manager F8), escape
/// (timer 0x38A, 0x317, ESCAPETB), release and rescue, the character owner check and parent loss (Adrift), healing (timer
/// 0x388) and its triggers, no-destination groups, the autorouting and adrift grouping (Autorouting and Adrift missions 0x43
/// and 0x44), and notifications 0x30B and 0x30E.
/// Story paths: <see cref="Story"/>; victory: <see cref="Victory"/>. Not modelled: the manager FA battle loyalty change.
/// </summary>
public static class Prisoners
{
    /// <summary>State values (names Inferred).</summary>
    public const int Captured = 0, Escaped = 1, Released = 2, Rescued = 3, Free = 4;

    /// <summary>Jail abodes (string ids 0x2D50 / 0x2D60).</summary>
    public const uint AllianceJail = 0x98000440, EmpireJail = 0x98000480;

    /// <summary>Natural side: 1 if the SD record allows only side 1, 2 if only side 2, else 0.</summary>
    public static Owner NaturalSide(GameState game, Character c)
    {
        bool a = Systems.Allowed(game, c, Owner.Alliance), e = Systems.Allowed(game, c, Owner.Empire);
        return a && !e ? Owner.Alliance : e && !a ? Owner.Empire : Owner.None;
    }

    // ---- State and its hooks ----

    /// <summary>
    /// State setter, hooked only on change. The state hook runs in order: Captured is set to (State = 0), with its hook on change;
    /// Captor is cleared; Autorouting is set; MissionRemoveRequest is set if on a mission (0x30B: no effect while alive); then
    /// 0x30A is posted (priority 0x10).
    /// </summary>
    public static void SetState(GameState game, Character c, int state)
    {
        if (c.State == state) return;
        c.State = state;
        SetCaptured(game, c, state == Captured);
        c.Captor = null;
        Objects.SetAutorouting(game, c);
        if (c.Mission is not null) SetRemoveRequest(game, c);
        Objects.Notify(game, g => OnStateChanged(g, c, state));
    }

    /// <summary>
    /// Captured setter and hook, in order: CanEscape follows Captured (timer 0x38A active, base G 0xA18, variance G 0xA19);
    /// EscapeRequest is cleared; the owner becomes the side opposite the natural side while Captured, else the natural side
    /// (0x302); CanHeal is recomputed (which may start healing, timer 0x388); DiscoveringForceUser (timer 0x389) is recomputed;
    /// then the CommandKind check.
    /// </summary>
    static void SetCaptured(GameState game, Character c, bool captured)
    {
        if (c.Captured == captured) return;
        c.Captured = captured;
        SetCanEscape(game, c, captured);
        c.EscapeRequest = false;
        var natural = NaturalSide(game, c);
        Objects.SetOwner(game, c, !captured ? natural : natural == Owner.Alliance ? Owner.Empire : Owner.Alliance);
        RecomputeCanHeal(game, c);
        TimerCatalogue.RecomputeForceActive(game, c);
        CommandKindCheck(c);
    }

    /// <summary>CommandKind check: clears CommandKind when it is set and the character is injured, captured, not Existing, or on a mission [0x50, 0x80).</summary>
    static void CommandKindCheck(Character c)
    {
        if (c.CommandKind != 0 && (c.Injury != 0 || c.Captured || !c.Existing || c.Mission?.Kind is >= 0x50 and < 0x80)) c.CommandKind = 0;
    }

    /// <summary>CanEscape setter. Its hook sets timer 0x38A active to CanEscape.</summary>
    internal static void SetCanEscape(GameState game, Character c, bool value)
    {
        if (c.CanEscape == value) return;
        c.CanEscape = value;
        var slot = TimerCatalogue.Slot(game, 0x38A, c.Ref, c);
        if (value) slot.Start(game, game.Params.General(0xA18), game.Params.General(0xA19));
        else slot.SetActive(game, false);
    }

    // ---- healing ----

    /// <summary>
    /// CanHeal, only with a direct parent: the parent has the character's owner, and the character is not Captured and not
    /// Destroyed. Its hook recomputes Healing. Run by the parent-change, Destroyed and Captured hooks only (not by owner changes).
    /// </summary>
    internal static void RecomputeCanHeal(GameState game, Character c)
    {
        if (c.Container is not { } parent) return;
        bool value = parent.Owner == c.Owner && !c.Captured && !c.Destroyed;
        if (value == c.CanHeal) return;
        c.CanHeal = value;
        RecomputeHealing(game, c);
    }

    /// <summary>
    /// Healing = CanHeal and not Enroute and Injury != 0. Its hook activates timer 0x388 (base FastHeal ? G 0xA01 : G 0xA02,
    /// variance 0) or deactivates it. Run by the CanHeal, Enroute and Injury hooks.
    /// </summary>
    internal static void RecomputeHealing(GameState game, Character c)
    {
        bool healing = c.CanHeal && !c.Enroute && c.Injury != 0;
        if (healing == c.Healing) return;
        c.Healing = healing;
        var slot = TimerCatalogue.Slot(game, 0x388, c.Ref, c);
        if (healing) slot.Start(game, game.Params.General(c.FastHeal ? 0xA01u : 0xA02u), 0);
        else slot.SetActive(game, false);
    }

    /// <summary>Timer 0x388 action: lowers Injury by G 0xA03 (fast heal) or G 0xA04, not below 0, when Injury is not 0.</summary>
    internal static void Heal(GameState game, Character c)
    {
        if (c.Injury != 0) SetInjury(game, c, Math.Max(0, c.Injury - game.Params.General(c.FastHeal ? 0xA03u : 0xA04u)));
    }

    /// <summary>
    /// Injury setter, clamped to 0 to 0x7FFF. Its hook runs on change: Healing, then the CommandKind check, then, for a new Injury
    /// that is not 0, a character with the resign-on-injury flag set and on a mission gets MissionResignRequest set.
    /// EnhancedCombat is computed on read; the Damaged flag has no modelled reader.
    /// </summary>
    public static void SetInjury(GameState game, Character c, int value)
    {
        value = Math.Clamp(value, 0, 0x7FFF);
        if (value == c.Injury) return;
        c.Injury = value;
        RecomputeHealing(game, c);
        CommandKindCheck(c);
        if (value != 0 && c.ResignOnInjury && c.Mission is not null) c.ResignRequest = true;
    }

    /// <summary>
    /// Force hook, FastHeal part, only for a new Force that is not 0: FastHeal is Force >= G 0xA00. Its hook re-bases timer 0x388.
    /// The other Force-derived fields have no modelled reader.
    /// </summary>
    internal static void ForceChanged(GameState game, Character c)
    {
        if (c.Force == 0) return;
        bool fast = c.Force >= game.Params.General(0xA00);
        if (fast == c.FastHeal) return;
        c.FastHeal = fast;
        if (game.Timers.TryGetValue((0x388, c.Ref), out var slot)) slot.SetBase(game, game.Params.General(fast ? 0xA01u : 0xA02u));
    }

    /// <summary>Second part of the Destroyed hook: CommandKind check, CanHeal, CanEscape cleared, Captor cleared.</summary>
    internal static void Destroyed(GameState game, Character c)
    {
        CommandKindCheck(c);
        RecomputeCanHeal(game, c);
        SetCanEscape(game, c, false);
        c.Captor = null;
    }

    // ---- 0x30B and 0x30E ----

    /// <summary>
    /// Sets MissionRemoveRequest. Its hook clears MissionResignRequest and posts 0x30B (priority 0x150). The handler: an agent
    /// still on a mission, Destroyed and with the request, leaves it (no move), then the mission's phase check runs; otherwise
    /// nothing.
    /// </summary>
    internal static void SetRemoveRequest(GameState game, Agent a)
    {
        if (a.RemoveRequest) return;
        a.RemoveRequest = true;
        a.ResignRequest = false;
        Objects.Notify(game, g =>
        {
            if (a.Mission is not { } m || !a.Destroyed || !a.RemoveRequest) return;
            Missions.Leave(g, m, a);
            Missions.PhaseCheck(g, m);
        }, 0x150);
    }

    /// <summary>
    /// Sets Adrift. Its hook posts 0x30E (priority 0x200), and when set on a Captured character, releases it. The 0x30E handler:
    /// a resolving agent that is not Destroyed gets the manager F8 step (<see cref="Autoroute"/> for a new value not 0, then the
    /// story check).
    /// </summary>
    internal static void SetAdrift(GameState game, Agent a, bool value)
    {
        if (a.Adrift == value) return;
        a.Adrift = value;
        Objects.Notify(game, g =>
        {
            if (a.Destroyed) return;
            if (value) Autoroute(g, a);
            Story.Notified(g, a);
        }, 0x200);
        if (value && a is Character { Captured: true } c) Release(game, c);
    }

    // ---- capture and release ----

    /// <summary>
    /// Capture by captor K. The injury check runs first (with the minors' death test) unless Destroyed. If still alive, State is
    /// set to 0 and Captor is set to K. Special forces are destroyed with DestroyedReason 9.
    /// </summary>
    public static void Capture(GameState game, Agent a, GameObject? captor)
    {
        if (a is not Character c) { Objects.Destroy(game, a, 9); return; }
        if (!c.Destroyed) Missions.InjuryCheck(game, c);
        if (c.Destroyed) return;
        SetState(game, c, Captured);
        c.Captor = captor;
    }

    /// <summary>Sets State 1 (escape, uprising effects 4 / 5).</summary>
    public static void Escape(GameState game, Character c) => SetState(game, c, Escaped);

    /// <summary>Sets State 2 (owner check, parent loss, captured-only groups).</summary>
    public static void Release(GameState game, Character c) => SetState(game, c, Released);

    /// <summary>Sets State 3 (rescue success).</summary>
    public static void Rescue(GameState game, Character c) => SetState(game, c, Rescued);

    /// <summary>
    /// Owner check: nothing if OnMission or Destroyed. Otherwise, if the parent's owner differs, the character is released if
    /// Captured, then Autorouting is set if the owners still differ.
    /// </summary>
    internal static void OwnerCheck(GameState game, Character c)
    {
        if (c.OnMission || c.Destroyed || c.Container is not { } parent || parent.Owner == c.Owner) return;
        if (c.Captured) Release(game, c);
        if (parent.Owner != c.Owner) Objects.SetAutorouting(game, c);
    }

    /// <summary>
    /// Parent destroyed: nothing if OnMission or Destroyed. If the direct parent's DestroyedReason is 0x14 (scrap, dissolved
    /// fleet), only Autorouting is set; otherwise Adrift is set (0x30E; a prisoner is released).
    /// </summary>
    internal static void ParentDestroyed(GameState game, Character c)
    {
        if (c.OnMission || c.Destroyed) return;
        if (c.Container?.DestroyedReason == 0x14) Objects.SetAutorouting(game, c);
        else SetAdrift(game, c, true);
    }

    /// <summary>
    /// Handles a group member with no destination. Unless Destroyed, a Captured member is released and any other member is
    /// captured with no captor. Then the mission fallback search runs (flag 0). If nothing is found, a member not Captured now is
    /// captured again and the destination is its owner's jail (<see cref="AllianceJail"/> or <see cref="EmpireJail"/>).
    /// Autorouting is cleared. Unless Destroyed now, an Autorouting mission 0x43 is created for the member alone (owner is its
    /// current owner, implied team is set from its Captured state). Returns the destination.
    /// </summary>
    internal static GameObject NoDestination(GameState game, Agent a)
    {
        if (!a.Destroyed)
        {
            if (a is Character { Captured: true } c) Release(game, c);
            else Capture(game, a, null);
        }
        var destination = Missions.Fallback(game, a, flag: false);
        if (destination is null)
        {
            if (a is not Character { Captured: true }) Capture(game, a, null);
            destination = game.Abode(a.Owner == Owner.Alliance ? AllianceJail : EmpireJail);
        }
        Objects.ClearAutorouting(game, a);
        if (!a.Destroyed) Missions.CreateAutorouting(game, adrift: false, a.Owner, destination, [a], a is Character { Captured: true });
        return destination;
    }

    // ---- autorouting and adrift grouping ----

    /// <summary>
    /// Manager F8 step for a new value not 0 (0x307, 0x30E). Only with Autorouting or Adrift: regroup at the agent's location
    /// (a destroyed ship is still in its fleet then; its final move 0x372 comes later).
    /// </summary>
    internal static void Autoroute(GameState game, Agent a)
    {
        if ((a.Autorouting || a.Adrift) && a.System is { } x) Regroup(game, x);
    }

    /// <summary>
    /// Regroups system X (also run when 0x304 clears BattlePending). The existing Adrift agents directly in X, and aboard every
    /// capital ship (Destroyed ones included) of every fleet in X, are re-parented into X in ascending SdRef (a plain set-parent:
    /// same system, no travel). Then, unless X has BattlePending, groups are formed until none is.
    /// </summary>
    internal static void Regroup(GameState game, StarSystem x)
    {
        var containers = new List<GameObject> { x };
        foreach (var f in x.Contents.OfType<Fleet>()) containers.AddRange(f.Contents.Where(s => Family.IsCapitalShip(s.Type)));
        foreach (var a in containers.SelectMany(c => c.Contents.OfType<Agent>())
            .Where(a => Family.Of(a.Type) is >= 0x30 and < 0x40 && a.Existing && a.Adrift).OrderBy(a => a.Ref).ToList())
            Objects.SetParent(game, a, x);
        if (!x.BattlePending) while (Group(game, x)) { }
    }

    /// <summary>
    /// Forms one group in system X. Scans X, then every capital ship of every fleet in X (key order), for Existing agents with
    /// Autorouting or Adrift. A character [0x30, 0x3C) with a Captor is skipped. An agent with a parent, not Destroyed, whose
    /// parent is Destroyed or of another owner gets a destination from the mission fallback search (flag 0). The first such
    /// agent opens the group (owner, Adrift, destination); an agent with the same triple joins; any other is left for a later
    /// call. Every other visited agent gets Autorouting and Adrift cleared. Outcome: no destination, then each member goes
    /// through <see cref="NoDestination"/>; no free agent, then each Captured, not Destroyed member is released if every member
    /// is Captured; otherwise an Autorouting (0x43) or Adrift (0x44) mission is created. Returns whether a group was formed.
    /// The manager's Completed and hyperdrive test is Inferred always true.
    /// </summary>
    static bool Group(GameState game, StarSystem x)
    {
        var group = new List<Agent>();
        (Owner Owner, bool Adrift, GameObject? D) key = default;
        var containers = new List<GameObject> { x };
        foreach (var f in x.Contents.OfType<Fleet>()) containers.AddRange(f.Contents.Where(s => Family.IsCapitalShip(s.Type)));
        foreach (var container in containers)
            foreach (var a in container.Contents.OfType<Agent>().Where(a => a.Existing && (a.Autorouting || a.Adrift)).ToList())
            {
                if (a is Character { Captor: not null } && Family.Of(a.Type) < 0x3C) continue;
                if (a.Container is { } p && !a.Destroyed && (p.Destroyed || p.Owner != a.Owner))
                {
                    var triple = (a.Owner, a.Adrift, Missions.Fallback(game, a, flag: false));
                    if (group.Count == 0) key = triple;
                    else if (triple != key) continue;
                    group.Add(a);
                }
                Objects.ClearAutorouting(game, a);
                SetAdrift(game, a, false);
            }
        if (group.Count == 0) return false;
        if (key.D is not { } d) foreach (var a in group) NoDestination(game, a);
        else if (group.All(a => a is Character { Captured: true })) { foreach (var a in group) if (a is Character { Captured: true, Destroyed: false } c) Release(game, c); }
        else Missions.CreateAutorouting(game, key.Adrift, key.Owner, d, group, impliedTeam: false);
        return true;
    }

    // ---- 0x30A: where prisoners are held ----

    /// <summary>
    /// 0x30A handler: the manager F8 hook, then manager FA's (Emperor at Coruscant, evaluated on read) and, for major characters,
    /// the victory recompute. F8 hook: first the story check; steps 1 to 7 only if the character is not Destroyed, its State
    /// still equals the posted one and Autorouting is set; step 8 always.
    /// </summary>
    static void OnStateChanged(GameState game, Character c, int state)
    {
        Story.Notified(game, c); // F8 hook, first step
        if (!c.Destroyed && c.State == state && c.Autorouting) Hold(game, c);
        OwnerCheck(game, c);
        // manager F8's usable missions [0x60, 0x68), in ascending SdRef
        foreach (var m in game.Missions.Where(m => m.Usable && m.Kind is >= 0x60 and < 0x68 && m.Objective == c).OrderBy(m => m.Key).ToList())
            Missions.PhaseCheck(game, m);
        if (Family.Of(c.Type) is >= 0x30 and < 0x38) Victory.Recompute(game); // majors
    }

    /// <summary>
    /// F8 hook steps 1-7: Captor K read, Autorouting := 0, Captor := none. A member still listed as Captured (Captured
    /// list, else Team / Decoy) stays. Captor object C = K unless Destroyed or without location; a free character / special force C
    /// on mission CM makes the character join CM. Destination D: C's parent for characters / special forces, fighters, troops; C
    /// itself for a capital ship. Move into D if it differs from the parent (with CM after the mission change); leave the current
    /// mission (moving = CM exists); join CM, then ParentAtMission := the captor's (read in step 3; the join's OnMission hook does
    /// not overwrite it afterwards); with K, Captured and a parent owned by the character's owner, leave CM again unless CM is an
    /// abduction (0x62) of this character.
    /// </summary>
    static void Hold(GameState game, Character c)
    {
        var k = c.Captor;
        Objects.ClearAutorouting(game, c);
        c.Captor = null;
        var m = c.Mission;
        if (m is not null && (c.Captured ? m.Captured.Contains(c) : m.Team.Contains(c) || m.Decoys.Contains(c))) return;
        var captor = k is { Destroyed: false, System: not null } ? k : null;
        var cm = captor is Agent { Mission: { } x } && Family.Of(captor.Type) is >= 0x30 and < 0x40 && captor is not Character { Captured: true } ? x : null;
        var d = captor is null ? null : Family.Of(captor.Type) switch
        {
            >= 0x30 and < 0x40 or >= 0x1C and < 0x20 or >= 0x10 and < 0x14 => captor.Container,
            >= 0x14 and < 0x1C => captor,
            _ => null,
        };
        var copy = (captor as Agent)?.ParentAtMission; // step 3
        bool move = d is not null && d != c.Container;
        if (move && cm is null) Fleets.Move(game, c, d!);
        if (m is not null) Missions.Leave(game, m, c, moving: cm is not null);
        if (cm is null) return;
        Missions.Join(game, cm, c, decoy: false);
        (c.ParentAtMission, c.LocationAtMission) = (copy, copy?.System); // step 6
        if (move) Fleets.Move(game, c, d!);
        if (k is not null && c.Captured && c.Container?.Owner == c.Owner && !(cm.Kind == 0x62 && cm.Objective == c)) Missions.Leave(game, cm, c);
    }

    // ---- escape ----

    /// <summary>Timer 0x38A action: if CanEscape, EscapeAttempt is set to 1, then to 0, posting 0x317 each time (only the 1 runs the check).</summary>
    internal static void EscapeTimer(GameState game, Character c)
    {
        if (c.CanEscape) Objects.Notify(game, g => EscapeCheck(g, c));
    }

    /// <summary>
    /// Escape key: EnhancedCombat + EnhancedEspionage - a4 - a5. With CanEscape and a location system L, a4 is the average
    /// EnhancedCombat (signed division, 0 if none) of the Usable, non-Captured characters [0x30, 0x3C) of the character's owner
    /// (the captor side) directly in L, and a5 is the count of Usable troops of that side directly in L. Otherwise both are 0.
    /// </summary>
    public static int EscapeKey(GameState game, Character c)
    {
        int a4 = 0, a5 = 0;
        if (c.CanEscape && c.System is { } l)
        {
            var guards = l.Contents.OfType<Character>().Where(g => g.Usable && g.Owner == c.Owner && Family.Of(g.Type) < 0x3C && !g.Captured).ToList();
            a4 = guards.Count == 0 ? 0 : guards.Sum(g => Missions.Enhanced(game, g, SkillIndex.Combat)) / guards.Count;
            a5 = l.Contents.Count(x => Family.IsTroop(x.Type) && x.Usable && x.Owner == c.Owner);
        }
        return Missions.Enhanced(game, c, SkillIndex.Combat) + Missions.Enhanced(game, c, SkillIndex.Espionage) - a4 - a5;
    }

    /// <summary>Escape chance: the ESCAPETB table value at the key (table 0x2C); 0 without the table.</summary>
    public static int EscapeChance(GameState game, int key) => Missions.Table(game, "ESCAPETB", key) ?? 0;

    /// <summary>0x317 with new value 1: manager FA draws the escape chance (ESCAPETB table at the key); on success EscapeRequest is set.</summary>
    internal static void EscapeCheck(GameState game, Character c)
    {
        if (!game.Random.Chance(EscapeChance(game, EscapeKey(game, c)), DrawSite.Escape)) return;
        c.EscapeRequest = true;
        EscapeGate(game, c);
    }

    /// <summary>
    /// EscapeRequest hook: a Captured, Usable, not OnMission character with EscapeRequest set escapes (State 1). A refused
    /// request stays set and is rerun by the OnMission hook and by the Usable change hook (Inferred: Usable change).
    /// </summary>
    internal static void EscapeGate(GameState game, Character c)
    {
        if (c.EscapeRequest && c.Captured && c.Usable && !c.OnMission) Escape(game, c);
    }
}
