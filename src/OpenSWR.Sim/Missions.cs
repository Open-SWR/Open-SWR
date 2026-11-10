using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>
/// Mission object (families 0x41-0x73 from its MISSNSD record), a content of manager 0xF8. Field names from the debug
/// notifications.
/// </summary>
public sealed class Mission(MissionRecord record, uint id) : GameObject(record.Common.Ref, Owner.Neutral, id)
{
    public MissionRecord Record { get; } = record;
    /// <summary>Family of the type: 0x41 Move ... 0x73 Pickup.</summary>
    public uint Kind => Family.Of(Type);
    /// <summary>UserID: free controller tag, no sim reader.</summary>
    public uint UserId { get; init; }
    /// <summary>TaskStatus: 0 pending, 2 failed, 3 succeeded.</summary>
    public int TaskStatus
    {
        get => _taskStatus;
        set
        {
            if (value == _taskStatus) return;
            if (value != 0) Snapshot();
            FactSink?.Record(0x60, Ref, _taskStatus, value);
            _taskStatus = value;
            FactSink?.ViewMessage(0x1F0, this); // the views' TaskStatus message
        }
    }
    int _taskStatus;
    /// <summary>The game whose <see cref="GameState.FactTrace"/> receives TaskStatus changes (tracing only).</summary>
    internal GameState? FactSink { get; init; }
    /// <summary>CompletionStatus: != 0 = ending (1 normal, 3 / 4 foiled, 5 no valid member, 15 diplomacy at share 100).</summary>
    public int CompletionStatus
    {
        get => _completionStatus;
        set
        {
            if (value == _completionStatus) return;
            if (value != 0) Snapshot();
            _completionStatus = value;
            FactSink?.ViewMessage(0x1F1, this); // the views' CompletionStatus message
        }
    }
    int _completionStatus;
    /// <summary>
    /// UI only: Leader and the first Team member when TaskStatus or CompletionStatus last became nonzero. The original's message
    /// handlers (message types 0x16 / 0x17) run at that notification, before the members leave; openswr builds the
    /// messages at the end of the tick, when an ended mission's Leader and Team are already empty. Not saved.
    /// </summary>
    public Agent? ReportLeader { get; private set; }
    public Agent? ReportMember { get; private set; }
    /// <summary>UI only, same snapshot: the first member (key order) not Destroyed and not captured with a system or fleet above it (the 0x17 location line).</summary>
    public GameObject? ReportPlace { get; private set; }

    void Snapshot()
    {
        (ReportLeader, ReportMember) = (Leader, Team.FirstOrDefault());
        ReportPlace = Members.Where(x => !x.Destroyed && x is not Character { Captured: true }).OrderBy(x => x.Key)
            .Select(x => { var p = x.Container; while (p is not null and not (StarSystem or Fleet)) p = p.Container; return p; })
            .FirstOrDefault(p => p is not null);
    }
    /// <summary>Phase 0..11, plus the ReadyForNextPhase and Mandatory flags.</summary>
    public int Phase { get; set; }
    /// <summary>Timer block LastPhase: the new phase on every change except a change to 11, which keeps it.</summary>
    public int LastPhase { get; set; }
    public bool Ready { get; set; }
    /// <summary>ImpliedTeam flag: the phase check skips its status-5 test.</summary>
    public bool ImpliedTeam { get; init; }
    public bool Mandatory { get; init; }
    /// <summary>OriginLocationKey, ObjectiveKey, TargetKey, TargetLocationKey, LeaderKey.</summary>
    public GameObject? Origin { get; set; }
    public GameObject? Objective { get; set; }
    public GameObject? TargetKey { get; set; }
    public GameObject? TargetLocation { get; set; }
    public Agent? Leader { get; set; }
    /// <summary>Team, Decoy and Captured select lists, each by ascending SdRef.</summary>
    public List<Agent> Team { get; } = [];
    public List<Agent> Decoys { get; } = [];
    public List<Agent> Captured { get; } = [];
    public IEnumerable<Agent> Members => Team.Concat(Decoys).Concat(Captured);
    /// <summary>Class field: recruitment RecruitedCharacterKey; research accumulated points.</summary>
    public Character? Recruited { get; set; }
    /// <summary>Same class field: Jedi training JediTrainingTeacherKey; Dagobah the phase-8 start day.</summary>
    public Character? Teacher { get; set; }
    public int StartDay { get; set; }
    public int Accumulator { get; set; }
    /// <summary>Research levels gained by the last post (display only, Inferred).</summary>
    public int LevelsGained { get; set; }
    /// <summary>Encounter codes of phases 2, 3, 7 and 9, kept in the timer block (<see cref="EncounterCodes"/>).</summary>
    public EncounterCodes Encounters { get; set; } = EncounterCodes.Standard;
}

/// <summary>
/// The encounter code of each encounter phase (2, 3, 7, 9): 0 no encounter, 1 agents, 2 garrison, 3 agents on the first resolver
/// and garrison on the second, 4 the system-or-fleet branch. Class init <see cref="Standard"/>, Adrift <see cref="Adrift"/>.
/// </summary>
public readonly record struct EncounterCodes(int Phase2, int Phase3, int Phase7, int Phase9)
{
    public static readonly EncounterCodes None = default, Standard = new(2, 1, 1, 4), Adrift = new(3, 0, 1, 4);
}

/// <summary>
/// Mission lifecycle (phases 0 -> 11 through notifications 0x30C / 0x30D, timers 0x38B / 0x38C), member travel, the encounter
/// resolver, execution (phase 10) and effects.
/// Deferred, not modelled: story missions and Jedi training, knowledge / fog of war, betrayal consequences.
/// Prisoners and the Autorouting / Adrift grouping: <see cref="Prisoners"/>.
/// </summary>
public static class Missions
{
    // ---- creation and membership ----

    /// <summary>
    /// Mission creation: one command serial, one counter id; object created in manager 0xF8 (owner 3 maps to side,
    /// Created, Completed); Origin = the team's common location (or the given one), ObjectiveKey = target, TargetKey = the given
    /// one, else target (command-created non-movement missions: the target's location),
    /// TargetLocationKey = the target's location; Phase := 1; members join in list order (ascending SdRef):
    /// standard classes: list 1 goes to Team, list 2 to Decoy; movement classes 0x41-0x44: the leader of list 1 goes to Team,
    /// every other list-1 member and all of list 2 go to Decoy.
    /// </summary>
    public static Mission Create(GameState game, uint type, Owner side, GameObject target, IReadOnlyList<Agent> team,
        IReadOnlyList<Agent> decoys, uint userId, bool mandatory, GameObject? origin = null, EncounterCodes? encounters = null, bool impliedTeam = false,
        GameObject? targetKey = null)
    {
        game.Clock.NextSerial();
        var record = game.Definitions.Missions.First(r => r.Common.Ref == type);
        var m = new Mission(record, game.NextId()) { FactSink = game, UserId = userId, Mandatory = mandatory, ImpliedTeam = impliedTeam };
        game.Missions.Add(m);
        Objects.Create(game, m, side, game.MissionManager, created: true, completed: true);
        m.Encounters = encounters ?? (m.Kind == 0x44 ? EncounterCodes.Adrift : EncounterCodes.Standard); // class-init codes (Adrift has its own default); passed codes replace them
        m.Origin = origin ?? Location(team[0]);
        SetObjective(game, m, target);
        m.TargetKey = targetKey ?? target;
        m.TargetLocation = Knowledge.Location(game, target, side) ?? Location(m.TargetKey); // the owner's view; callers refuse an unknown one
        SetPhase(game, m, 1);
        // A movement class without a leader reports a failed join but rolls nothing back: every member joins Team.
        var leader = m.Kind is >= 0x41 and <= 0x44 ? BestLeader(game, team) : null;
        foreach (var a in team.OrderBy(a => a.Ref)) Join(game, m, a, decoy: leader is not null && a != leader);
        foreach (var a in decoys.OrderBy(a => a.Ref)) Join(game, m, a, decoy: true);
        return m;
    }

    /// <summary>
    /// Move mission of a produced character or special force: type 0x41000001, owner = the unit's owner,
    /// ObjectiveKey = TargetKey = the unit's location, origin = the manager's location, team = the unit, Mandatory, UserID 0.
    /// </summary>
    internal static void CreateMove(GameState game, Agent unit, ManufacturingManager manager)
    {
        if (unit.System is { } at) Create(game, 0x41000001, unit.Owner, at, [unit], [], 0, mandatory: true, origin: manager.System);
    }

    /// <summary>
    /// Autorouting (type 0x43000003) or Adrift (type 0x44000004) mission: the Return wrapper; origin = the
    /// group's common location (none -> no mission, nothing consumed), then one serial and the object, created unconditionally
    /// (owner as passed, always 1 or 2 on these paths); ObjectiveKey = TargetKey = D, team = the group, no decoys, UserID 0,
    /// Mandatory, ImpliedTeam as passed (Adrift 0), class-init encounter codes (standard / Adrift).
    /// </summary>
    internal static void CreateAutorouting(GameState game, bool adrift, Owner owner, GameObject d, IReadOnlyList<Agent> group, bool impliedTeam)
    {
        var origin = group[0].System;
        if (origin is null || group.Any(a => a.System != origin)) return;
        Create(game, adrift ? 0x44000004u : 0x43000003u, owner, d, group, [], 0, mandatory: true, origin, impliedTeam: impliedTeam && !adrift);
    }

    /// <summary>
    /// Location: <see cref="GameObject.System"/> (the system of the parent chain; a manager's system);
    /// outside systems the first abode (family 0x98) of the parent chain, which is its own location (abodes are direct contents
    /// of the game object; jails and story abodes).
    /// </summary>
    internal static GameObject? Location(GameObject o)
    {
        if (o.System is { } s) return s;
        for (GameObject? x = o; x is not null; x = x.Container)
            if (Family.Of(x.Type) == 0x98) return x;
        return null;
    }

    /// <summary>
    /// ObjectiveKey setter (and its hook): the old objective is unlocked, the new one locked,
    /// at once.
    /// </summary>
    internal static void SetObjective(GameState game, Mission m, GameObject? target)
    {
        if (m.Objective == target) return;
        Objects.Lock(game, m.Objective, -1);
        m.Objective = target;
        Objects.Lock(game, target, +1);
    }

    /// <summary>
    /// RoleMissionKey setter: on a change post 0x321 (old, new), whose handler locks the new mission and
    /// unlocks the old one.
    /// </summary>
    static void SetMission(GameState game, Agent a, Mission? m)
    {
        var old = a.Mission;
        if (old == m) return;
        a.Mission = m;
        PostRole(game, a, old, m);
    }

    /// <summary>Posts 0x321 (old, new); its handler locks the new mission and unlocks the old one.</summary>
    internal static void PostRole(GameState game, Agent a, Mission? old, Mission? m) =>
        Objects.Notify(game, g => { g.Record(0x321, a.Ref, old?.Ref ?? 2, m?.Ref ?? 2); Objects.Lock(g, m, +1); Objects.Lock(g, old, -1); });

    /// <summary>Inserts into a member list kept by ascending SdRef.</summary>
    static void Insert(List<Agent> list, Agent a) => list.Insert(list.FindLastIndex(x => x.Ref < a.Ref) + 1, a);

    /// <summary>
    /// Join: RoleMissionKey := mission, OnMission (feeds 0x389; when it becomes set, ParentAtMissionKey := the
    /// parent); into Captured, Decoy or Team.
    /// </summary>
    internal static void Join(GameState game, Mission m, Agent a, bool decoy)
    {
        SetMission(game, a, m);
        a.ResignOnInjury = m.Record.ResignOnInjury;
        if (!a.OnMission)
        {
            (a.OnMission, a.ParentAtMission, a.LocationAtMission) = (true, a.Container, a.System);
            Objects.Notify(game, g => Story.OnMissionChanged(g, a)); // 0x312
        }
        Insert(a is Character { Captured: true } ? m.Captured : decoy ? m.Decoys : m.Team, a);
        if (a is Character c) TimerCatalogue.RecomputeForceActive(game, c);
    }

    /// <summary>
    /// Leave: EnrouteActive := 0 unless hyperspacing, out of its list, RoleMissionKey cleared (a character's
    /// MissionHyperdriveModifier := G 0x1 and IsDecoying := 0); OnMission and ParentAtMissionKey cleared unless the member moves to
    /// another mission (<paramref name="moving"/>); phase check, a new leader if the leader left, and the 0x38C
    /// start check (no member left and CompletionStatus != 0 starts timer 0x38C with base 1, no variance). Inferred: the resign
    /// request belongs to the mission left and is cleared; the remove request too (the RoleMissionKey hook
    /// clears the mission flags). A cleared OnMission reruns the escape gate.
    /// </summary>
    internal static void Leave(GameState game, Mission m, Agent a, bool moving = false)
    {
        if (!a.HyperdriveActive) Objects.SetEnrouteActive(game, a, false);
        m.Team.Remove(a); m.Decoys.Remove(a); m.Captured.Remove(a);
        SetMission(game, a, null);
        a.IsDecoying = a.ResignOnInjury = false;
        if (a is Character ch) ch.MissionHyperdriveModifier = null;
        if (!moving && a.OnMission)
        {
            (a.OnMission, a.ParentAtMission, a.LocationAtMission) = (false, null, null);
            Objects.Notify(game, g => Story.OnMissionChanged(g, a)); // 0x312
        }
        a.ResignRequest = a.RemoveRequest = false;
        if (a is Character c)
        {
            TimerCatalogue.RecomputeForceActive(game, c);
            Prisoners.EscapeGate(game, c);
        }
        PhaseCheck(game, m);
        if (m.Leader == a) SelectLeader(game, m);
        if (!m.Members.Any() && m.CompletionStatus != 0) TimerCatalogue.Start(game, 0x38C, m.Ref, m, 1, 0);
        Jedi.Left(game, m, a); // Jedi training
    }

    // ---- phases ----

    /// <summary>
    /// Phase setter: leaving 8 runs the phase check (a status set there advances at once: the nested 9 -> 11
    /// posts its 0x30D first), then deactivates timer 0x38B; the entry work; then post 0x30D (priority 0x156).
    /// </summary>
    static void SetPhase(GameState game, Mission m, int phase)
    {
        int old = m.Phase;
        if (old == phase) return;
        m.Phase = phase;
        if (phase != 11) m.LastPhase = phase;
        if (old == 8)
        {
            PhaseCheck(game, m); // leaving phase 8, step 1
            TimerCatalogue.Slot(game, 0x38B, m.Ref, m).SetActive(game, false);
        }
        Enter(game, m, phase);
        Objects.Notify(game, g => { g.Record(0x30D, m.Ref, old, phase); OnPhase(g, m, old, phase); }, Objects.PhasePriority);
    }

    /// <summary>ReadyForNextPhase setter: posts 0x30C (old, new) with priority 0x30C on a change.</summary>
    static void SetReady(GameState game, Mission m, bool value)
    {
        if (m.Ready == value) return;
        m.Ready = value;
        Objects.Notify(game, g => { g.Record(0x30C, m.Ref, value ? 0 : 1, value ? 1 : 0); if (value && !m.Destroyed) Advance(g, m); }, Objects.ReadyPriority);
    }

    /// <summary>
    /// 0x30C with new value 1 -> advance: CompletionStatus != 0 -> Phase 11; else ReadyForNextPhase := 0 and Phase + 1,
    /// except 10 -> 8 for repeating types and 11 stays 11.
    /// </summary>
    static void Advance(GameState game, Mission m)
    {
        if (m.CompletionStatus != 0) { SetPhase(game, m, 11); return; }
        SetReady(game, m, false);
        SetPhase(game, m, m.Phase switch { 10 when m.Record.Repeats => 8, 11 => 11, var p => p + 1 });
    }

    /// <summary>
    /// 0x30D (old, new), in order: encounter resolver; member clean-up (Return missions); new phase 2:
    /// the speed rule; incite / subdue totals when the change crosses the range 8-10; auto-ready
    /// unless the phase is 4 or 8; the Dagobah case.
    /// </summary>
    static void OnPhase(GameState game, Mission m, int old, int now)
    {
        if (m.Destroyed) return;
        Encounters(game, m);
        CleanUp(game, m);
        if (m.Phase == 2) SpeedRule(game, m);
        if (m.Kind is 0x56 or 0x57 && (old is >= 8 and <= 10) != (now is >= 8 and <= 10) && m.TargetLocation is StarSystem s) UprisingTotals(game, s);
        Story.DagobahPhaseChanged(game, m, now); // Inferred: after the other steps
        if (m.Phase is not (4 or 8)) SetReady(game, m, true);
    }

    /// <summary>
    /// Uprising totals of a system over the Usable incite / subdue missions of the container (both sides) whose
    /// TargetLocationKey is it and whose Phase is exactly 8: incite total = sum avgLead div G 0x1800, subdue total = sum -(avgLead div
    /// G 0x1800), and a third total = G 0x1801 if an incite mission was counted, else 0. avgLead = sum of leadership over
    /// the members [0x30, 0x40) of Team, Decoy and Captured div their count (0 if none).
    /// </summary>
    internal static void UprisingTotals(GameState game, StarSystem s)
    {
        var g = game.Params;
        int incite = 0, subdue = 0;
        bool any = false;
        foreach (var m in game.Missions.Where(m => m.Usable && m.Kind is 0x56 or 0x57 && m.TargetLocation == s && m.Phase == 8))
        {
            var members = m.Members.Where(a => Family.Of(a.Type) is >= 0x30 and < 0x40).ToList();
            int lead = members.Count == 0 ? 0 : members.Sum(a => Enhanced(game, a, SkillIndex.Leadership)) / members.Count;
            if (m.Kind == 0x56) { incite += lead / g.General(0x1800); any = true; }
            else subdue += -(lead / g.General(0x1800));
        }
        (s.InciteTotal, s.SubdueTotal, s.IncidentSupportDelta) = (incite, subdue, any ? g.General(0x1801) : 0);
    }

    /// <summary>0x303 part (a): phase check of every Usable mission [0x50, 0x60) whose TargetLocationKey is the system, key order.</summary>
    internal static void UprisingChanged(GameState game, StarSystem s)
    {
        foreach (var m in game.Missions.Where(m => m.Usable && m.Kind is >= 0x50 and < 0x60 && m.TargetLocation == s).OrderBy(m => m.Key).ToList())
            PhaseCheck(game, m);
    }

    /// <summary>
    /// Han rule: v = G 0xC0B if Han (0x33000243) is on this mission, not Captured, and no special force
    /// (0x3C-0x3F) is a member; else G 0x1. Every character member (0x30-0x3B, all lists) gets MissionHyperdriveModifier := v.
    /// </summary>
    static void SpeedRule(GameState game, Mission m)
    {
        bool han = game.Characters.Any(c => c.Ref == Refs.Han && c.Mission == m && !c.Captured)
            && !m.Members.Any(a => Family.IsSpecialForce(a.Type));
        int v = game.Params.General(han ? 0xC0Bu : 0x1u);
        foreach (var c in m.Members.OfType<Character>()) c.MissionHyperdriveModifier = v;
    }

    // ---- member clean-up and Return missions ----

    /// <summary>
    /// Member clean-up, rule = the record's CleanUpRule. leave = CompletionStatus != 0 or a resign request; x = HyperdriveActive
    /// or rule 2 or (rule 1 and the owner of the member's location != its owner); y = x and rule != 2. 1: leaving members with x = 0 leave
    /// in place. 2: repeatedly, the leaving members with x = 1 get a destination; the first fixes D, every member with the same
    /// destination forms the group, leaves (keeping OnMission when some grouped member was in Team or Decoy), then: only Captured
    /// members -> released; D none -> each member through the no-destination path (<see cref="Prisoners.NoDestination"/>, with
    /// its Autorouting mission 0x43); else a Return mission. Members with a remove request leave but are
    /// excluded from both steps.
    /// </summary>
    static void CleanUp(GameState game, Mission m)
    {
        int rule = m.Record.CleanUpRule;
        bool Leaving(Agent a) => (m.CompletionStatus != 0 || a.Removed) && !a.RemoveRequest;
        bool X(Agent a) => a.HyperdriveActive || rule == 2 || rule == 1 && a.System?.Owner != a.Owner;
        foreach (var a in m.Members.ToList()) if (a.Mission == m && Leaving(a) && !X(a)) Leave(game, m, a);
        while (m.Members.Where(a => Leaving(a) && X(a)).Select(a => (Agent: a, D: Destination(game, m, a, rule != 2))).ToList() is [var first, ..] pending)
        {
            // the group is a key-ordered set: its members leave in ascending SdRef, Team / Decoy / Captured mixed
            var group = pending.Where(p => p.D == first.D).Select(p => p.Agent).OrderBy(a => a.Ref).ToList();
            bool k = group.Any(a => m.Team.Contains(a) || m.Decoys.Contains(a));
            var origin = group.All(a => a.System == group[0].System) ? group[0].System : null; // origin only when the group shares one system
            foreach (var a in group) Leave(game, m, a, moving: k);
            if (!k) { foreach (var a in group) if (a is Character { Captured: true, Destroyed: false } c) Prisoners.Release(game, c); }
            else if (first.D is not { } d) foreach (var a in group) Prisoners.NoDestination(game, a);
            else if (origin is not null) Create(game, 0x42000002, m.Owner, d, group, [], 0, m.Mandatory, origin, ReturnEncounters(m));
        }
    }

    /// <summary>
    /// Destination: only for source missions of families [0x50, 0x80), with y the member's current parent if a 0x241
    /// placement there succeeds, else ParentAtMissionKey if a 0x241 placement there succeeds. Else (movement types at once) the
    /// fallback (once the game is ready): from = the member's location, else LocationAtMission, else Yavin (Alliance
    /// member) / Coruscant; seed = ParentAtMission if LocationAtMission is a system; destination search (flag y, stay 1), Bounty
    /// members -> Jabba's Palace.
    /// </summary>
    static GameObject? Destination(GameState game, Mission m, Agent a, bool y)
    {
        if (Family.Of(m.Type) is >= 0x50 and < 0x80)
        {
            if (y && a.Container is { } here && Fleets.CanPlace(game, here, a.Type, a.Owner, 0x241)) return here;
            if (a.ParentAtMission is { Existing: true } p && Fleets.CanPlace(game, p, a.Type, a.Owner, 0x241)) return p;
        }
        return Fallback(game, a, y);
    }

    /// <summary>
    /// Mission fallback (once the game is ready): from = the member's location, else LocationAtMission, else Yavin
    /// (Alliance member) / Coruscant; seed = ParentAtMission if LocationAtMission is a system; destination search (stay 1). A
    /// member of a Bounty mission (0x65) gets Jabba's Palace (the search draws nothing, so its result is not computed).
    /// </summary>
    internal static GameObject? Fallback(GameState game, Agent a, bool flag)
    {
        if (!game.Ready) return null;
        if (a.Mission?.Kind == 0x65) return game.Abode(Story.JabbasPalace); // Bounty member -> Jabba's Palace, found
        var from = a.System ?? a.LocationAtMission ?? game.System(a.Owner == Owner.Alliance ? Refs.Yavin : Refs.Coruscant);
        return Fleets.Search(game, a, from, a.LocationAtMission is not null ? a.ParentAtMission : null, flag, stay: true);
    }

    /// <summary>
    /// Return-mission encounter codes from the source: none without encounters (MISSNSD encounter field 0); else phases 2, 3, 7, 9 =
    /// 0, 1, 1, 4, then if LastPhase &gt; 6 phases 2 and 3 become 3 and 0 when CompletionStatus = 7; else, when LastPhase &gt; 4,
    /// phases 2 and 3 become 0.
    /// </summary>
    static EncounterCodes ReturnEncounters(Mission m)
    {
        if (!m.Record.HasEncounters) return EncounterCodes.None;
        var v = new EncounterCodes(0, 1, 1, 4);
        if (m.LastPhase > 6) { if (m.CompletionStatus == 7) v = v with { Phase2 = 3, Phase3 = 0 }; }
        else if (m.LastPhase > 4) v = v with { Phase2 = 0, Phase3 = 0 };
        return v;
    }

    /// <summary>Work on entering a phase.</summary>
    static void Enter(GameState game, Mission m, int phase)
    {
        bool travels = m.Origin != m.TargetLocation;
        switch (phase)
        {
            case 2: SelectLeader(game, m); break;
            case 4: TravelPhase(game, m); break;
            case 5 when travels: PhaseCheck(game, m); break;
            case 6 when travels:
                foreach (var a in m.Members.ToList()) Objects.SetEnrouteActive(game, a, false);
                PhaseCheck(game, m);
                break;
            case 8:
                m.TaskStatus = 0;
                TimerCatalogue.Start(game, 0x38B, m.Ref, m, m.Record.DurationBase, m.Record.DurationVariance);
                break;
            case 10 when m.Kind is not (>= 0x41 and <= 0x44): Execute(game, m); break; // movement classes: no phase-10 work
            case 11:
                PhaseCheck(game, m);
                Knowledge.Finalize(game, m);
                break;
        }
        if (m.Kind == 0x58) Jedi.TrainingPhase(game, m, phase); // class phase hook
        else if (m.Kind == 0x71) Story.DagobahPhase(game, m, phase);
    }

    /// <summary>Leader selection over the Team.</summary>
    static void SelectLeader(GameState game, Mission m) => m.Leader = BestLeader(game, m.Team);

    /// <summary>
    /// Over members of families [0x30, 0x40) not removed (resign request), not Destroyed and not Captured: the first,
    /// replaced by a higher rank (major 3, minor 2, special force 1) or an equal rank with strictly higher leadership
    /// (characters EnhancedLeadership, special forces base Leadership).
    /// </summary>
    static Agent? BestLeader(GameState game, IEnumerable<Agent> agents)
    {
        Agent? best = null;
        int bestRank = 0, bestLead = 0;
        foreach (var a in agents.OrderBy(a => a.Ref))
        {
            if (a.Destroyed || a.Removed || a is Character { Captured: true }) continue;
            int rank = Family.Of(a.Type) switch { < 0x30 or >= 0x40 => 0, < 0x38 => 3, < 0x3C => 2, _ => 1 };
            if (rank == 0) continue;
            int lead = Enhanced(game, a, SkillIndex.Leadership);
            if (best is null || rank > bestRank || rank == bestRank && lead > bestLead) (best, bestRank, bestLead) = (a, rank, lead);
        }
        return best;
    }

    /// <summary>
    /// Phase check: nothing if CompletionStatus != 0; unless ImpliedTeam: no valid Team member (all have
    /// a remove / resign request, or none) -> 5, the class check skipped; else the class check; no status and Phase 11 -> 1.
    /// </summary>
    internal static void PhaseCheck(GameState game, Mission m)
    {
        if (m.CompletionStatus != 0) return;
        int code = !m.ImpliedTeam && m.Team.All(a => a.Removed) ? 5 : ClassCheck(game, m);
        if (code == 0 && m.Phase == 11) code = 1;
        if (code != 0) SetCompletion(game, m, code);
    }

    /// <summary>
    /// Class check of a running mission (callback 1; the validate-only checks are omitted: status -1 never reaches a running
    /// mission). ObjectiveKey O and TargetKey T as objects (the side's views are not modelled); either missing -> none. Steps in
    /// chain order, each only while no earlier one failed unless noted. 1. Generic (every class; the movement classes
    /// 0x41-0x44 stop here): RefusesDestroyedTarget and T Destroyed -> 7; the owner of O relative to the mission owner (own / opp /
    /// other) not allowed by the record -> 8. 2. Standard: RefusesDestroyedObjective and O Destroyed -> 6; O Enroute -> 6;
    /// locations of O and T differ or missing -> 6; NeedsPopulatedTarget and T not a Populated system -> 0xD. 3. Target kind: system
    /// target (0x51-0x58): O's Uprising set and not AllowsUprisingSystem, or clear and not AllowsCalmSystem -> 6; character target
    /// (0x61-0x65, runs even after an earlier failure and overwrites it): O a character, Captured and not AllowsCapturedCharacter,
    /// or not and not AllowsFreeCharacter -> 6; unit / abode targets: no running check. 4. Diplomacy: owner 1/2 and share(O, owner) = 100
    /// -> 0xF. 5a. Research: O's manager of kind k missing or without a non-suspended member -> 0xC; owner's line k
    /// exhausted -> 0xE. 5b. Recruitment: owner's RecruitmentDone -> 0x10.
    /// </summary>
    static int ClassCheck(GameState game, Mission m)
    {
        if (m.Objective is not { } o) return 0;
        var t = m.TargetKey; // none after a failed target test at creation: no location, so status 6 (Inferred)
        var r = m.Record;
        // while LastPhase < 5 and the target differs from the origin the owner's views are read
        var side = m.LastPhase < 5 && m.TargetLocation != m.Origin ? m.Owner : Owner.None;
        Seen so = Knowledge.See(game, o, side), st = t is null ? default : Knowledge.See(game, t, side);
        int code = r.RefusesDestroyedTarget && st.Destroyed ? 7
            : !(so.Owner == m.Owner ? r.AllowsOwnObjective : so.Owner == Systems.Opp(m.Owner) ? r.AllowsEnemyObjective : r.AllowsNeutralObjective) ? 8 : 0;
        if (m.Kind is >= 0x41 and <= 0x44) return code;
        if (code == 0)
            code = r.RefusesDestroyedObjective && so.Destroyed || so.Enroute || so.Location is null || so.Location != st.Location ? 6
                : r.NeedsPopulatedTarget && !(t is StarSystem && st.Populated) ? 0xD : 0;
        if (m.Kind is >= 0x51 and <= 0x58 && code == 0 && o is StarSystem && !(so.Uprising ? r.AllowsUprisingSystem : r.AllowsCalmSystem)) code = 6;
        if (m.Kind is >= 0x61 and <= 0x65 && o is Character && !(so.Captured ? r.AllowsCapturedCharacter : r.AllowsFreeCharacter)) code = 6;
        if (m.Kind == 0x51 && code == 0 && o is StarSystem && (m.Owner == Owner.Alliance ? so.Support : 100 - so.Support) == 100) code = 0xF;
        if (code == 0) code = ResearchRecruitCheck(game, m);
        return code;
    }

    /// <summary>
    /// Class checks 5a / 5b, also run at creation: research (0x53) status 0xC / 0xE, recruitment (0x55) 0x10;
    /// 0 = pass.
    /// </summary>
    internal static int ResearchRecruitCheck(GameState game, Mission m) => ResearchRecruitCheck(game, m.Type, m.Owner, m.Objective);

    internal static int ResearchRecruitCheck(GameState game, uint type, Owner owner, GameObject? objective)
    {
        bool side = owner is Owner.Alliance or Owner.Empire;
        if (Family.Of(type) == 0x55) return side && game.Sides[owner].RecruitmentDone ? 0x10 : 0;
        if (Family.Of(type) != 0x53) return 0;
        int k = ResearchLine(type);
        var manager = (objective as StarSystem)?.Managers.FirstOrDefault(x => x is not null && Research.Line(x.Kind) == k);
        if (manager is null || !Economy.Members(manager).Any(f => !f.Suspended)) return 0xC;
        return side && game.Sides[owner].ResearchExhausted[k] ? 0xE : 0;
    }

    /// <summary>Research line k of a mission type: 0x53000020 -> 0 ship, 0x53000021 -> 1 facility, 0x53000022 -> 2 troop.</summary>
    static int ResearchLine(uint type) => type switch { 0x53000020 => 0, 0x53000021 => 1, _ => 2 };

    /// <summary>
    /// CompletionStatus setter, called from one place: one command serial, then the setter (on
    /// change); its hook: a nonzero value advances at once, to Phase 11.
    /// </summary>
    internal static void SetCompletion(GameState game, Mission m, int code)
    {
        game.Clock.NextSerial();
        if (m.CompletionStatus == code) return;

        game.Record(0x64, m.Ref, m.CompletionStatus, code);
        m.CompletionStatus = code;
        if (code != 0) Advance(game, m);
    }

    /// <summary>Manager-F8 0x302 hook: every mission whose ObjectiveKey or TargetKey is the object runs its phase check.</summary>
    internal static void OwnerChanged(GameState game, GameObject o)
    {
        foreach (var m in game.Missions.Where(m => !m.Destroyed && (m.Objective == o || m.TargetKey == o)).ToList()) PhaseCheck(game, m);
    }

    // ---- travel ----

    /// <summary>
    /// Phase 4: if CompletionStatus = 0, every member (Team, Decoy, Captured; families 0x30-0x3F) travels from the origin to the
    /// target location and moves into TargetKey; then the arrival check.
    /// </summary>
    static void TravelPhase(GameState game, Mission m)
    {
        if (m.CompletionStatus != 0) return;
        if (m.TargetKey is { } target) // none: no travel (Inferred)
            foreach (var a in m.Members.Where(a => Family.Of(a.Type) is >= 0x30 and < 0x40).ToList())
            {
                Fleets.Travel(game, a, m.Origin, m.TargetLocation);
                Fleets.Move(game, a, target);
            }
        ArrivalCheck(game, m);
    }

    /// <summary>
    /// Arrival check, in phase 4: when AllMembersArrived (no member HyperdriveActive), TargetLocationKey := location
    /// of TargetKey; every member still EnrouteActive runs the test (a new leg if re-parented in hyperspace; arrived =
    /// not HyperdriveActive; EnrouteActive is cleared only on entering phase 6); all arrived -> ReadyForNextPhase := 1.
    /// </summary>
    internal static void ArrivalCheck(GameState game, Mission m)
    {
        if (m.Phase != 4 || m.Members.Any(a => a.HyperdriveActive)) return;
        m.TargetLocation = m.TargetKey is null ? null : Location(m.TargetKey);
        bool arrived = true;
        foreach (var a in m.Members.Where(a => a.EnrouteActive).ToList()) arrived = Fleets.NewLeg(game, a) && arrived;
        if (arrived) SetReady(game, m, true);
    }

    /// <summary>Timer 0x38B action: in phase 8, ReadyForNextPhase := 1.</summary>
    internal static void PhaseTimer(GameState game, Mission m)
    {
        if (m.Phase == 8) SetReady(game, m, true);
    }

    /// <summary>Timer 0x38C action: in phase 11, DestroyedReason 0x14 (the mission object is destroyed).</summary>
    internal static void CompletionTimer(GameState game, Mission m)
    {
        if (m.Phase == 11) Objects.Destroy(game, m, 0x14);
    }

    // ---- encounters ----

    /// <summary>Encounter resolver state: location, pool flags, the foiled flag and the counts.</summary>
    sealed class Resolver(StarSystem location)
    {
        public StarSystem X { get; } = location;
        // AgentPass and TravelPass have no sim reader. Meaning in the original is unknown.
        public bool TravelPass, AgentPass, SystemUnits, FleetUnits, WalkShips, WalkFighters, WalkTroops, Foiled;
        public int Agents, Decoys, Units;
    }

    /// <summary>
    /// Encounter resolver, run on every phase change: if CompletionStatus = 0, the location (target once past
    /// LastPhase 4, else origin) is a system and the owner is 1 or 2: pass 1, then pass 0;
    /// each pass runs the case table and, unless done, the stages. Inferred: one resolver per phase change, its pool flags kept
    /// across both passes.
    /// </summary>
    static void Encounters(GameState game, Mission m)
    {
        if (m.CompletionStatus != 0 || (m.LastPhase > 4 ? m.TargetLocation : m.Origin) is not StarSystem x || m.Owner is not (Owner.Alliance or Owner.Empire)) return;
        var r = new Resolver(x);
        foreach (bool first in new[] { true, false })
            if (!CaseTable(game, m, r, first)) Stages(game, m, r);
        // the resolver ended the mission (foil or the case table's phase check): full refresh of the opposing side's view (no
        // draw); that side acknowledges it at the final move. Not kept: the view's TaskStatus / CompletionStatus
        // and their 0x1F0 / 0x1F1, no-ops for every controller on a foreign mission.
        if (m.CompletionStatus != 0) Knowledge.Refresh(game, m, Combat.Opp(m.Owner));
    }

    /// <summary>The phase's encounter code: 0 while a member is HyperdriveActive (AllMembersArrived in the block).</summary>
    static int EncounterCode(Mission m)
    {
        if (m.Members.Any(a => a.HyperdriveActive)) return 0;
        var e = m.Encounters;
        return m.Phase switch { 2 => e.Phase2, 3 => e.Phase3, 7 => e.Phase7, 9 => e.Phase9, _ => 0 };
    }

    /// <summary>Member filter: no remove / resign request, a parent; with skip also IsDecoying clear.</summary>
    static bool Valid(Agent a, bool skip) => !a.Removed && a.Container is not null && !(skip && a.IsDecoying);

    static IEnumerable<Agent> Of(IEnumerable<Agent> list, uint lo, uint hi, bool skip) =>
        list.Where(a => Family.Of(a.Type) >= lo && Family.Of(a.Type) < hi && Valid(a, skip));

    /// <summary>Agents: Team characters and special forces [0x30, 0x40), skipping IsDecoying.</summary>
    static List<Agent> AgentWalk(Mission m) => Of(m.Team, 0x30, 0x40, true).ToList();

    /// <summary>Decoys: Decoy characters and special forces [0x30, 0x40), no skip.</summary>
    static List<Agent> DecoyList(Mission m) => Of(m.Decoys, 0x30, 0x40, false).ToList();

    /// <summary>Foiled-member walk: Team characters only, majors [0x30, 0x38) then minors [0x38, 0x3C), skipping IsDecoying.</summary>
    static List<Agent> FoiledWalk(Mission m) => [.. Of(m.Team, 0x30, 0x38, true), .. Of(m.Team, 0x38, 0x3C, true)];

    /// <summary>Team special forces: [0x3C, 0x40), skipping IsDecoying.</summary>
    static List<Agent> ForcesWalk(Mission m) => Of(m.Team, 0x3C, 0x40, true).ToList();

    /// <summary>
    /// Case table: phase check; done if ending, no encounters in the MISSNSD record or encounter code 0. F = opp(owner) has a Usable fleet
    /// at X; C = the owner has a Usable ion cannon (family 0x22) at X; O = X is owned by opp(owner); T = ObjectiveKey is itself a
    /// system or sits directly in one (its real container). A = AgentPass, TravelPass if the target location differs from the origin, and with F and not C
    /// FleetUnits, WalkShips, WalkFighters. Then, also when done: every unit of the pool walk gets DetectorIsDecoyed := 0 and the counts are refreshed:
    /// units of the walk with skip, agents, decoys; every member of the decoy walk gets IsDecoying := 0. Returns done.
    /// </summary>
    static bool CaseTable(GameState game, Mission m, Resolver r, bool first)
    {
        PhaseCheck(game, m);
        int code = EncounterCode(m);
        bool done = m.CompletionStatus != 0 || !m.Record.HasEncounters || code == 0;
        if (!done)
        {
            var opp = Systems.Opp(m.Owner);
            bool fleet = r.X.Contents.Any(o => o is Fleet && o.Owner == opp && o.Usable);
            bool cannon = r.X.Contents.Any(o => Family.Of(o.Type) == 0x22 && o.Owner == m.Owner && o.Usable);
            bool hostile = r.X.Owner == opp, direct = m.Objective is StarSystem || m.Objective?.Container is StarSystem;
            void Agents() { r.AgentPass = true; if (m.TargetLocation != m.Origin) r.TravelPass = true; if (fleet && !cannon) r.FleetUnits = r.WalkShips = r.WalkFighters = true; }
            void Garrison() { r.TravelPass = true; if (hostile) r.SystemUnits = r.WalkTroops = true; }
            switch (code, first)
            {
                case (1, true) or (3, true): Agents(); break;
                case (2, true) or (3, false): Garrison(); break;
                case (4, true):
                    r.AgentPass = r.TravelPass = true;
                    if (direct) { if (hostile) r.SystemUnits = r.WalkTroops = true; } // T chooses the branch, O inside it
                    else if (fleet) r.FleetUnits = r.WalkShips = r.WalkFighters = true;
                    break;
                default: done = true; break;
            }
        }
        foreach (var (u, _) in Walk(r, true, true, true, skip: false).ToList()) u.DetectorIsDecoyed = false;
        r.Units = Walk(r, true, true, true, skip: true).Count();
        (r.Agents, r.Decoys) = (AgentWalk(m).Count, DecoyList(m).Count);
        foreach (var d in DecoyList(m)) d.IsDecoying = false;
        return done;
    }

    /// <summary>
    /// Unit (detector) walk, any owner, every iterator Usable (mode 1): with SystemUnits the fighters (WalkFighters and b), then troops
    /// (WalkTroops and c) directly in X; with FleetUnits for every Usable fleet in X and every Usable capital ship of it (key order): the ship
    /// (WalkShips and a), its fighters (WalkFighters and b), its troops (WalkTroops and c). Skip = leave out DetectorIsDecoyed units. Fleet = the fleet the
    /// unit was reached through (null: directly in X).
    /// </summary>
    static IEnumerable<(GameObject Unit, Fleet? Fleet)> Walk(Resolver r, bool ships, bool fighters, bool troops, bool skip)
    {
        static bool Fighter(GameObject o) => Family.Of(o.Type) is >= 0x1C and < 0x20 && o.Usable;
        static bool Troop(GameObject o) => Family.IsTroop(o.Type) && o.Usable;
        bool Keep(GameObject u) => !(skip && u.DetectorIsDecoyed);
        if (r.SystemUnits)
        {
            if (r.WalkFighters && fighters) foreach (var u in r.X.Contents.Where(Fighter).ToList()) if (Keep(u)) yield return (u, null);
            if (r.WalkTroops && troops) foreach (var u in r.X.Contents.Where(Troop).ToList()) if (Keep(u)) yield return (u, null);
        }
        if (!r.FleetUnits) yield break;
        foreach (var fleet in r.X.Contents.OfType<Fleet>().Where(f => f.Usable).ToList())
            foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).ToList())
            {
                if (r.WalkShips && ships && Keep(ship)) yield return (ship, fleet);
                if (r.WalkFighters && fighters) foreach (var u in ship.Contents.Where(Fighter).ToList()) if (Keep(u)) yield return (u, fleet);
                if (r.WalkTroops && troops) foreach (var u in ship.Contents.Where(Troop).ToList()) if (Keep(u)) yield return (u, fleet);
            }
    }

    /// <summary>A detecting unit's detection value (the name Detection is Inferred).</summary>
    static int Detection(GameState game, GameObject unit) => game.Definitions.Resolve(unit.Type)!.Get(SdField.Detection);

    /// <summary>
    /// Defender of a detecting unit: kind = 2 capital ship, 1 fighter, 3 troop; reached through
    /// a fleet: the first character aboard the fleet's capital ships (key order) of the fleet's owner, Existing, with CommandKind =
    /// kind; else the first character directly in X of X's owner, Existing, with CommandKind = kind.
    /// </summary>
    static Character? Defender(Resolver r, GameObject unit, Fleet? fleet) =>
        Combat.Commander(fleet, r.X, Family.IsCapitalShip(unit.Type) ? 2 : Family.IsTroop(unit.Type) ? 3 : 1);

    /// <summary>The defender's enhanced skill (espionage or combat); 0 without defender.</summary>
    static int DefenderSkill(GameState game, Resolver r, GameObject unit, Fleet? fleet, int skill) =>
        Defender(r, unit, fleet) is { } d ? Enhanced(game, d, skill) : 0;

    /// <summary>
    /// Stages: 1 Force detection; 2 loyalty (standard classes in phase 9, movement classes never; every member draws,
    /// a betrayal foils the mission, then chance(Force) per other Team character); 3 decoy (with SystemUnits walk all, with FleetUnits ships only then
    /// fighters only; at its start the Force duels); 4 foil (agents &gt; 0: the average EnhancedEspionage over the agent walk, computed at the first unit, one
    /// check per unit of walk all until one foils); 5 foiled: ResignOnInjury -> CompletionStatus 4 past phase 4,
    /// else 3; then (also without it) every member of the foiled-member walk is processed, then every Team special force
    /// (the foil's a5 is their count).
    /// </summary>
    static void Stages(GameState game, Mission m, Resolver r)
    {
        ForceDetection(game, m, r);
        if (m.Kind is not (>= 0x41 and <= 0x44) && m.Phase == 9 && !r.Foiled && Betrayal(game, m) is not null) r.Foiled = true;
        // decoy stage start, before its foiled test: Force duels; a visited member with a remove / resign request afterwards lowers
        // the decoy (Decoy member) or agent count, whether or not the duel ran
        foreach (var a in FoiledWalk(m).OfType<Character>().ToList())
        {
            if (a.EncounterKey is not { Container: not null } b) continue;
            Jedi.Duel(game, a, b);
            if (a.Removed) { if (m.Decoys.Contains(a)) r.Decoys--; else r.Agents--; }
        }
        if (!r.Foiled)
        {
            if (r.SystemUnits) DecoyWalk(game, m, r, Walk(r, true, true, true, skip: true));
            if (r.FleetUnits)
            {
                DecoyWalk(game, m, r, Walk(r, true, false, false, skip: true));
                DecoyWalk(game, m, r, Walk(r, false, true, false, skip: true));
            }
        }
        if (!r.Foiled && r.Agents > 0)
        {
            int? average = null;
            int forces = 0;
            foreach (var (u, fleet) in Walk(r, true, true, true, skip: true))
            {
                if (average is null)
                {
                    var agents = AgentWalk(m);
                    average = agents.Count == 0 ? 0 : agents.Sum(a => Enhanced(game, a, SkillIndex.Espionage)) / agents.Count;
                    forces = ForcesWalk(m).Count; // a5: count of the forces walk
                }
                int defender = DefenderSkill(game, r, u, fleet, SkillIndex.Espionage);
                if (!game.Random.Chance(FoilChance(game, average.Value, defender, Detection(game, u), forces), DrawSite.EncounterCheck)) continue;
                r.Foiled = true;
                break;
            }
        }
        if (!r.Foiled) return;
        if (m.Record.ResignOnInjury) SetCompletion(game, m, m.Phase > 4 ? 4 : 3);
        foreach (var a in FoiledWalk(m)) FoiledMember(game, m, r, a);
        foreach (var a in ForcesWalk(m)) FoiledMember(game, m, r, a); // foiled-member step for each Team special force
    }

    /// <summary>
    /// Decoy stage callback per visited unit: no decoy -> stop; else pick roll(decoys - 1) and check
    /// chance(T(FDECOYTB through a fleet / TDECOYTB, EnhancedEspionage(decoy) - Detection(unit) - pct(defender's EnhancedEspionage,
    /// G 0xE04))): success -> the decoy IsDecoying, the unit DetectorIsDecoyed; failure -> the decoy is foiled like an agent with the
    /// visiting unit (no unit pick).
    /// </summary>
    static void DecoyWalk(GameState game, Mission m, Resolver r, IEnumerable<(GameObject Unit, Fleet? Fleet)> walk)
    {
        foreach (var (u, fleet) in walk)
        {
            if (r.Decoys == 0) return;
            int i = game.Random.Roll(r.Decoys - 1, DrawSite.DecoyPick);
            if (DecoyList(m).ElementAtOrDefault(i) is not { } d) return;
            int defender = DefenderSkill(game, r, u, fleet, SkillIndex.Espionage);
            if (game.Random.Chance(DecoyChance(game, Enhanced(game, d, SkillIndex.Espionage), defender, Detection(game, u), fleet is not null), DrawSite.EncounterCheck))
            {
                (d.IsDecoying, u.DetectorIsDecoyed) = (true, true);
                r.Units--; // a decoyed unit leaves the unit count
            }
            else FoiledMember(game, m, r, d, (u, fleet));
        }
    }

    /// <summary>
    /// Foiled member: a failed decoy uses the visiting unit; else without units (unit count 0) no draw, skipped, and
    /// roll(n - 1) picks one of the n units of walk all (with skip); evasion chance(T(RLEVADTB, EnhancedCombat - v)), v = the
    /// EnhancedCombat of the unit's defender (0 without): success -> the evasion hook (gated resign request, then the injury check for
    /// characters; special forces: no draw); failure -> captured by the defender character, else by the unit
    /// (<see cref="Prisoners.Capture"/>: injury check, then State 0; special forces DestroyedReason 9). Then (both outcomes) the second resign check, and a removed member lowers the decoy (Decoy member) or agent
    /// count.
    /// </summary>
    static void FoiledMember(GameState game, Mission m, Resolver r, Agent a, (GameObject Unit, Fleet? Fleet)? visiting = null)
    {
        bool decoy = m.Decoys.Contains(a); // membership read before the processing
        if (visiting is null && r.Units == 0) return;
        var (unit, fleet) = visiting ?? Walk(r, true, true, true, skip: true).ElementAtOrDefault(game.Random.Roll(r.Units - 1, DrawSite.UnitPick));
        int v = unit is null ? 0 : DefenderSkill(game, r, unit, fleet, SkillIndex.Combat);
        if (game.Random.Chance(Table(game, "RLEVADTB", Enhanced(game, a, SkillIndex.Combat) - v) ?? 0, DrawSite.EncounterCheck))
            Evaded(game, a);
        else Prisoners.Capture(game, a, unit is null ? null : Defender(r, unit, fleet) ?? unit);
        // Tail of the foiled-member step. On the capture path Inferred whether the hooks already set the remove flag or cleared the ref
        if (!a.RemoveRequest && a.ResignOnInjury && a.Mission is not null) a.ResignRequest = true;
        if (a.Removed) { if (decoy) r.Decoys--; else r.Agents--; }
    }

    /// <summary>
    /// Evasion success (the evasion hook; special forces reach the same routine):
    /// MissionResignRequest := 1 only with the ResignOnInjury flag and RoleMissionKey set; then characters run the injury check, special
    /// forces draw nothing; Luke / Leia then gain Force rank.
    /// </summary>
    internal static void Evaded(GameState game, Agent a)
    {
        if (a.ResignOnInjury && a.Mission is not null) a.ResignRequest = true;
        if (a is Character c) InjuryCheck(game, c);
        Jedi.EvasionGain(game, a); // Luke and Leia
    }

    /// <summary>
    /// Force detection (stage 1). Members M1 = majors [0x30, 0x38), M2 = minors [0x38, 0x3C) of Team then Decoy
    /// (filter, skipping IsDecoying). Opponents with Force >= G 0xE02, not Captured, owner != the mission's, Usable: with SystemUnits directly
    /// in X, with FleetUnits aboard every Usable capital ship of every Usable fleet in X; O1 = the majors, O2 = the minors. Per picked
    /// opponent a (O1: Luke, Vader, else roll(k - 1) over the unmarked; then O2: random): unmark the members, then per picked
    /// member b (M1: Luke, Vader, random; then M2: random): p = Force(a) + Force(b) +
    /// G 0xE03 if both Forces != 0, else 0; chance(p) if p &gt; 0; success: foiled unless b is a Decoy member, the duel pairing
    /// (the deferred step is not modelled), and the member loop ends.
    /// </summary>
    static void ForceDetection(GameState game, Mission m, Resolver r)
    {
        var m1 = Of(m.Team, 0x30, 0x38, true).Concat(Of(m.Decoys, 0x30, 0x38, true)).ToList();
        var m2 = Of(m.Team, 0x38, 0x3C, true).Concat(Of(m.Decoys, 0x38, 0x3C, true)).ToList();
        bool Keep(Character c) => c.Usable && c.Force >= game.Params.General(0xE02) && !c.Captured && c.Owner != m.Owner;
        var (o1, o2) = (new List<Agent>(), new List<Agent>());
        void Add(GameObject at) { foreach (var c in at.Contents.OfType<Character>().Where(Keep)) (Family.Of(c.Type) < 0x38 ? o1 : o2).Add(c); }
        if (r.SystemUnits) Add(r.X);
        if (r.FleetUnits)
            foreach (var fleet in r.X.Contents.OfType<Fleet>().Where(f => f.Usable))
                foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable))
                    Add(ship);
        var marked = new HashSet<Agent>();
        while ((Pick(o1, true) ?? Pick(o2, false)) is Character a)
        {
            marked.ExceptWith(m1.Concat(m2));
            while ((Pick(m1, true) ?? Pick(m2, false)) is { } b)
            {
                int fb = b is Character cb ? cb.Force : 0;
                int p = a.Force != 0 && fb != 0 ? a.Force + fb + game.Params.General(0xE03) : 0;
                if (p <= 0 || !game.Random.Chance(p, DrawSite.ForceDetect)) continue;
                if (!m.Decoys.Contains(b)) r.Foiled = true;
                if (b is Character cb2) Jedi.Pair(game, cb2, a); // duel pairing
                break;
            }
        }

        Agent? Pick(List<Agent> list, bool named)
        {
            var unmarked = list.Where(x => !marked.Contains(x)).ToList();
            var pick = named ? unmarked.FirstOrDefault(x => x.Ref == Refs.Luke) ?? unmarked.FirstOrDefault(x => x.Ref == Refs.Vader) : null;
            if (pick is null && unmarked.Count > 0) pick = unmarked[game.Random.Roll(unmarked.Count - 1, DrawSite.ForcePick)];
            if (pick is not null) marked.Add(pick);
            return pick;
        }
    }

    /// <summary>
    /// Stage 2: every member (Team, Decoy, Captured) draws its loyalty; the last betrayer is the traitor, whose only
    /// consequence is the foil (the caller sets it); then identify traitor: every other member of the foiled-member
    /// walk draws chance(Force), also for 0 (the TraitorDiscoveredKey it sets is UI / AI only, not kept). Returns the traitor.
    /// </summary>
    internal static Agent? Betrayal(GameState game, Mission m)
    {
        Agent? traitor = null;
        foreach (var a in m.Members.ToList()) if (Loyalty(game, a)) traitor = a;
        if (traitor is null) return null;
        // a success writes TraitorDiscoveredKey := none, then := the traitor (not kept): UI 0x361 from the member's live views; the
        // none post is invalid for type 0x27 and is not recorded
        foreach (var c in FoiledWalk(m).OfType<Character>().Where(c => c != traitor).ToList())
            if (game.Random.Chance(c.Force, DrawSite.Traitor)) game.StoryPost(0x361, c, 0, 1, traitor);
        return traitor;
    }

    /// <summary>
    /// Loyalty "not betray": betrays = chance(100 - EnhancedLoyalty), drawn always. The betrayal
    /// consequences: <see cref="Betrayal"/>. The draw has its own trace label.
    /// </summary>
    public static bool Loyalty(GameState game, Agent a) => game.Random.Chance(100 - Enhanced(game, a, SkillIndex.Loyalty), DrawSite.Loyalty);

    /// <summary>Foil p: T(FOILTB, a1 - a4 - a5 - pct(a2, G 0xE05) - G 0xE00); 100 (always foiled) without the table.</summary>
    public static int FoilChance(GameState game, int a1, int a2, int a4, int a5) =>
        Table(game, "FOILTB", a1 - a4 - a5 - Pct(a2, game.Params.General(0xE05)) - game.Params.General(0xE00)) ?? 100;

    /// <summary>Decoy p: T(TDECOYTB, or FDECOYTB at a fleet, a2 - a4 - pct(a3, G 0xE04)).</summary>
    public static int DecoyChance(GameState game, int a2, int a3, int a4, bool fleet) =>
        Table(game, fleet ? "FDECOYTB" : "TDECOYTB", a2 - a4 - Pct(a3, game.Params.General(0xE04))) ?? 0;

    /// <summary>
    /// Injury check: q = max(G 0xA05, 100 - EnhancedCombat), chance(q); on success Injury +=
    /// roll(q) + roll(G 0xA07) + G 0xA06, then the death test: only one class draws chance(G 0xA11) -> 
    /// DestroyedReason 10 (Inferred: minor characters [0x38, 0x3C)); the others draw nothing. Also on both foiled-member outcomes.
    /// <paramref name="shipLoss"/>: the ship-loss injury check (the ship-loss hook), same shape with G 0xA08,
    /// 0xA0A, 0xA09 (capital-ship class step).
    /// </summary>
    public static void InjuryCheck(GameState game, Character c, bool shipLoss = false)
    {
        var g = game.Params;
        uint k = shipLoss ? 0xA08u : 0xA05u; // base: min q, then (shipLoss) 0xA09 add / 0xA0A roll, (else) 0xA06 add / 0xA07 roll
        string site = shipLoss ? DrawSite.ShipLossInjury : DrawSite.Injury;
        int q = Math.Max(g.General(k), 100 - Enhanced(game, c, SkillIndex.Combat));
        if (!game.Random.Chance(q, site)) return;
        ApplyWound(game, c, game.Random.Roll(q, site) + game.Random.Roll(g.General(k + 2), site) + g.General(k + 1));
    }

    /// <summary>
    /// The wound hook, same routine as the injury check (reason 10): only for w &gt; 0: Injury += w, then the death test (minor characters [0x38, 0x3C)
    /// draw chance(G 0xA11) -> DestroyedReason 10; Inferred class mapping).
    /// </summary>
    internal static void ApplyWound(GameState game, Character c, int w)
    {
        if (w <= 0) return;
        Prisoners.SetInjury(game, c, c.Injury + w);
        if (Family.Of(c.Type) is >= 0x38 and < 0x3C && game.Random.Chance(game.Params.General(0xA11), DrawSite.Death)) Objects.Destroy(game, c, 10);
    }

    // ---- execution ----

    /// <summary>
    /// Phase 10: class pre-step; characters attempt lowest p first (strictly smaller replaces: ties keep list order)
    /// until one stops; then, if not stopped, special forces in list order; post-step (TaskStatus 0 -> 2, 3 -> the success effect).
    /// Finalize (knowledge, moving members to the target; Inferred) is not modelled.
    /// </summary>
    internal static void Execute(GameState game, Mission m)
    {
        m.Recruited = null;
        m.Accumulator = 0;
        m.LevelsGained = 0; // research pre-step
        bool stop = false;
        var collected = new List<(Agent Agent, int P)>();
        foreach (var c in m.Team.OfType<Character>().ToList())
            if (Probability(game, m, c) is { } p) collected.Add((c, p));
        while (!stop && collected.Count > 0)
        {
            int k = 0;
            for (int i = 1; i < collected.Count; i++) if (collected[i].P < collected[k].P) k = i;
            var (a, p) = collected[k];
            collected.RemoveAt(k);
            stop = Attempt(game, m, a, p);
        }
        foreach (var a in m.Team.Where(a => Family.IsSpecialForce(a.Type)).ToList())
        {
            if (stop) break;
            if (Probability(game, m, a) is { } p) stop = Attempt(game, m, a, p);
        }
        if (m.Kind == 0x53) ResearchPost(game, m);
        else if (m.Kind == 0x54 && m.TaskStatus == 3 && m.TargetLocation is StarSystem seen) Knowledge.Update(game, seen, 8, m.Owner); // recon post
        else if (m.Kind == 0x65) { } // Bounty: no post
        else if (m.Kind == 0x64 && m.TaskStatus == 0) Story.PalaceFailed(game, m);
        else if (m.TaskStatus == 0) m.TaskStatus = 2;
        else if (m.TaskStatus == 3) Succeeded(game, m);
        Knowledge.Finalize(game, m);
    }

    /// <summary>
    /// Research post, owner 1 or 2: points of line k += accumulator (through the setter and the level-advance chain),
    /// LevelsGained := order after - before; TaskStatus := 3 if accumulator != 0, else 2.
    /// </summary>
    static void ResearchPost(GameState game, Mission m)
    {
        if (m.Owner is Owner.Alliance or Owner.Empire)
        {
            var side = game.Sides[m.Owner];
            int k = ResearchLine(m.Type), before = side.ResearchOrder[k];
            Research.SetPoints(game, side, k, side.ResearchPoints[k] + m.Accumulator);
            m.LevelsGained = side.ResearchOrder[k] - before;
        }
        m.TaskStatus = m.Accumulator != 0 ? 3 : 2;
    }

    /// <summary>share(sys, s): the system's share for s = 1, else 100 - that.</summary>
    static int Share(StarSystem s, Owner side) => side == Owner.Alliance ? s.Support : 100 - s.Support;

    /// <summary>nST: Stormtrooper Regiments (type 0x10000006) directly in the system, owner 2, Usable, for both sides' missions.</summary>
    internal static int Stormtroopers(StarSystem s) => s.Contents.Count(x => x.Type == 0x10000006 && x.Owner == Owner.Empire && x.Usable);

    internal static int? Table(GameState game, string name, int key) =>
        game.Definitions.Tables.TryGetValue(name, out var t) && TableLookup.Threshold(t, key) is { } e ? e.Value : null;

    /// <summary>
    /// Enhanced skill (character getters): Diplomacy / Espionage + pct(*, Force); Combat max(0, * + pct(*, Force) -
    /// Injury); Leadership + pct(*, LeadershipAdjustment) (G 0xC04 for Empire characters while manager 0xFA's flag is set); others
    /// the base value. Special forces: the base skill.
    /// </summary>
    public static int Enhanced(GameState game, Agent a, int skill)
    {
        int b = a.Skills[skill];
        if (a is not Character c) return b;
        return skill switch
        {
            SkillIndex.Diplomacy or SkillIndex.Espionage => b + Pct(b, c.Force),
            SkillIndex.Combat => Math.Max(0, b + Pct(b, c.Force) - c.Injury),
            SkillIndex.Leadership => b + Pct(b, c.Owner == Owner.Empire && game.EmperorAtCoruscant ? game.Params.General(0xC04) : 0),
            _ => b,
        };
    }

    /// <summary>
    /// Class probability: null = inputs missing (the member is skipped). Jedi training and story types are computed in their own modules.
    /// </summary>
    public static int? Probability(GameState game, Mission m, Agent a)
    {
        var sys = m.Objective as StarSystem;
        var target = m.Objective as Character;
        int E(int skill) => Enhanced(game, a, skill);
        int? T(string table, int key) => Table(game, table, key) ?? 0;
        var opp = Systems.Opp(m.Owner);
        return m.Kind switch
        {
            0x51 when sys is not null => T("DIPLMSTB", E(SkillIndex.Diplomacy) - Share(sys, opp) + Stormtroopers(sys)),
            0x52 => T("ESPIMSTB", E(SkillIndex.Espionage)),
            0x53 => 0,
            0x54 => 100,
            0x55 when sys is not null => T("RCRTMSTB", E(SkillIndex.Leadership) - Share(sys, opp)),
            0x56 when sys is not null => T("INCTMSTB", E(SkillIndex.Leadership) - Share(sys, opp) - Stormtroopers(sys)),
            0x57 when sys is not null => T("SUBDMSTB", E(SkillIndex.Leadership) - Share(sys, opp) + Stormtroopers(sys)),
            0x61 => T("RESCMSTB", E(SkillIndex.Combat)),
            0x62 when target is not null => T("ABDCMSTB", E(SkillIndex.Combat) - Enhanced(game, target, SkillIndex.Combat)),
            0x63 when target is not null => T("ASSNMSTB", E(SkillIndex.Combat) - Enhanced(game, target, SkillIndex.Combat)),
            0x69 => T("SBTGMSTB", (E(SkillIndex.Espionage) + E(SkillIndex.Combat)) / 2),
            0x6A => T("DSSBMSTB", (E(SkillIndex.Espionage) + E(SkillIndex.Combat)) / 2),
            0x58 => Jedi.Probability(m, a),
            _ => Story.Probability(game, m, a),
        };
    }

    /// <summary>
    /// Class attempt: true = stop. Standard: chance(p) at the class attempt site (one step, also for p = 0 or
    /// >= 100), success -> stop, TaskStatus 3 and the per-agent effect; skill growth (base +1, G 0x180C ...) only for characters.
    /// Reconnaissance succeeds without a draw; research draws its own chance and never stops; assassination wounds the target and
    /// succeeds only if it dies; subdue draws only while the system has Uprising (no draw -> no stop, the loop goes on); abduction
    /// captures the target (the member becomes its captor), rescue frees it.
    /// </summary>
    static bool Attempt(GameState game, Mission m, Agent a, int p)
    {
        var g = game.Params;
        void Grow(int skill, uint param) { if (a is Character) a.Skills[skill] += g.General(param); }
        switch (m.Kind)
        {
            case 0x58: Jedi.Attempt(game, m, a, p); return false; // never stops
            case 0x64 or 0x65 or 0x71 or 0x72 or 0x73: return Story.Attempt(game, m, a, p);
            case 0x54:
                m.TaskStatus = 3;
                return true;
            case 0x53: // k 0 -> ShipyardRD, 1 -> ConstructionYardRD, 2 -> TrainingFacilRD (enhanced)
                // mission system: the ObjectiveKey object if it is a system
                if (m.Objective is not StarSystem) return false;
                int skill = ResearchLine(m.Type) switch { 0 => SkillIndex.ShipDesign, 1 => SkillIndex.FacilityDesign, _ => SkillIndex.TroopTraining };
                if (game.Random.Chance(Enhanced(game, a, skill), DrawSite.Research))
                {
                    m.Accumulator += g.General(0x181B) + game.Random.Roll(g.General(0x181C), DrawSite.ResearchPoints);
                    Grow(skill, 0x180E);
                }
                return false;
        }
        string site = m.Kind switch
        {
            0x51 => DrawSite.Diplomacy, 0x52 => DrawSite.Espionage, 0x55 => DrawSite.Recruitment, 0x56 => DrawSite.Incite,
            0x57 => DrawSite.Subdue, 0x61 => DrawSite.Rescue, 0x62 => DrawSite.Abduction, 0x63 => DrawSite.Assassination,
            0x69 => DrawSite.Sabotage, 0x6A => DrawSite.DeathStarSabotage, _ => "",
        };
        if (m.Kind == 0x57 && m.Objective is StarSystem { Uprising: false }) return false;
        if (!game.Random.Chance(p, site)) return false;
        switch (m.Kind)
        {
            case 0x55:
                m.Recruited = RecruitPick(game, (StarSystem)m.Objective!, m.Owner);
                m.TaskStatus = 3;
                Grow(SkillIndex.Leadership, 0x180F);
                break;
            case 0x51: m.TaskStatus = 3; Grow(SkillIndex.Diplomacy, 0x180C); break;
            case 0x52:
                m.TaskStatus = 3;
                if (m.TargetLocation?.Owner != m.Owner) Grow(SkillIndex.Espionage, 0x180D);
                break;
            case 0x69: m.TaskStatus = 3; Grow(SkillIndex.Espionage, 0x1815); Grow(SkillIndex.Combat, 0x1816); break;
            case 0x6A: m.TaskStatus = 3; Grow(SkillIndex.Espionage, 0x1817); Grow(SkillIndex.Combat, 0x1818); break;
            case 0x62:
                m.TaskStatus = 3;
                Grow(SkillIndex.Combat, 0x1813);
                if (m.Objective is Character abducted) Prisoners.Capture(game, abducted, a);
                break;
            case 0x61:
                m.TaskStatus = 3;
                Grow(SkillIndex.Combat, 0x1812);
                if (m.Objective is Character rescued) Prisoners.Rescue(game, rescued);
                break;
            case 0x63 when m.Objective is Character target: // assassination wound on the target
                Wound(game, target);
                if (target.Destroyed) { m.TaskStatus = 3; Grow(SkillIndex.Combat, 0x1814); }
                break;
            case 0x56 when m.Objective is StarSystem x:
                Systems.Incident(game, x);
                m.TaskStatus = x.Control.Support == Systems.Opp(m.Owner)
                    || x.Contents.Any(t => Family.IsTroop(t.Type) && t.Usable && t.Owner == x.Owner) ? 2 : 3;
                if (m.TaskStatus == 3) Grow(SkillIndex.Leadership, 0x1810);
                break;
            case 0x57 when m.Objective is StarSystem x:
                int d = x.Owner == m.Owner ? g.General(0x182B) + game.Random.Roll(g.General(0x182C), DrawSite.SubdueDelta)
                    : x.Owner == Owner.Neutral ? g.General(0x182D) + game.Random.Roll(g.General(0x182E), DrawSite.SubdueDelta)
                    : 0;
                Systems.ChangeSupport(game, x, m.Owner, d);
                if (x.Uprising) Systems.SubdueEnd(game, x);
                m.TaskStatus = x.Uprising ? 2 : 3;
                if (m.TaskStatus == 3) Grow(SkillIndex.Leadership, 0x1811);
                break;
        }
        return true;
    }

    /// <summary>
    /// Assassination wound: w = chance(G 0xA0E) ? roll(G 0xA0E) + roll(G 0xA10) + G 0xA0F : 0; w &gt; 0 -> 
    /// Injury += w, then the death test chance(G 0xA11) -> DestroyedReason 7 "DestroyedAssassination".
    /// </summary>
    static void Wound(GameState game, Character target)
    {
        var g = game.Params;
        int w = game.Random.Chance(g.General(0xA0E), DrawSite.Wound)
            ? game.Random.Roll(g.General(0xA0E), DrawSite.Wound) + game.Random.Roll(g.General(0xA10), DrawSite.Wound) + g.General(0xA0F) : 0;
        if (w <= 0) return;
        Prisoners.SetInjury(game, target, target.Injury + w);
        if (game.Random.Chance(g.General(0xA11), DrawSite.Death)) Objects.Destroy(game, target, 7);
    }

    /// <summary>
    /// Post-step success effects. Diplomacy: support change. Espionage: the
    /// extra-system reveal (knowledge modes deferred). Sabotage / Death Star sabotage: the ObjectiveKey gets DestroyedReason 6
    /// "DestroyedSabotage" (Death Star effects deferred).
    /// </summary>
    static void Succeeded(GameState game, Mission m)
    {
        var g = game.Params;
        if (m.Kind == 0x51 && m.Objective is StarSystem system)
        {
            int d = system.Owner == m.Owner ? g.General(0x1827) + game.Random.Roll(g.General(0x1828), DrawSite.SupportDelta)
                : system.Owner == Owner.Neutral ? g.General(0x1829) + game.Random.Roll(g.General(0x182A), DrawSite.SupportDelta)
                : 0;
            Systems.ChangeSupport(game, system, m.Owner, d);
        }
        else if (m.Kind == 0x52 && m.TargetLocation is StarSystem target)
        {
            Knowledge.Update(game, target, 10, m.Owner);
            Reveal(game, m, target);
        }
        else if (m.Kind is 0x69 or 0x6A && m.Objective is { } objective) Objects.Destroy(game, objective, 6);
    }

    /// <summary>
    /// Espionage extra systems, only for a core target [0x90, 0x92) owned by opp(owner): with a
    /// Usable headquarters [0x20, 0x22) there n = G 0x1824 + roll(G 0x1825) over core and rim; else Coruscant owned by the Empire
    /// n = G 0x1822 + roll(G 0x1823) over core and rim; else n = G 0x1820 + roll(G 0x1821) over core only. Candidates: Usable systems
    /// of the range with the target's owner, registry order, target excluded; min(n, count) times roll(len - 1) picks and removes
    /// one (ExtraSystemKey; the knowledge it raises is deferred).
    /// </summary>
    static void Reveal(GameState game, Mission m, StarSystem target)
    {
        if (target.Owner != Systems.Opp(m.Owner) || Family.Of(target.Type) is not (>= 0x90 and < 0x92)) return;
        var g = game.Params;
        (uint b, uint v, uint hi) = target.Contents.Any(x => Family.Of(x.Type) is >= 0x20 and < 0x22 && x.Usable) ? (0x1824u, 0x1825u, 0x98u)
            : target.Type == Refs.Coruscant && target.Owner == Owner.Empire ? (0x1822u, 0x1823u, 0x98u)
            : (0x1820u, 0x1821u, 0x92u);
        int n = g.General(b) + game.Random.Roll(g.General(v), DrawSite.EspionageReveal);
        var candidates = game.Systems.Where(x => Family.Of(x.Type) >= 0x90 && Family.Of(x.Type) < hi && x.Usable && x.Owner == target.Owner && x != target).ToList();
        for (int k = Math.Min(n, candidates.Count); k > 0; k--)
        {
            int i = game.Random.Roll(candidates.Count - 1, DrawSite.RevealPick);
            // ExtraSystemKey setter: live side views post 0x370 (mission, system) before the hook; key not stored (no sim reader).
            game.ViewMessage(0x370, m, candidates[i]);
            Knowledge.Update(game, candidates[i], 10, m.Owner); // class hook
            candidates.RemoveAt(i);
        }
    }

    /// <summary>
    /// Recruit pick: c = the side's unplaced minors (0x38-0x3B, Created clear) in ascending Index; c = 0 -> none;
    /// i = roll(c - 1); placed in the system (Created -> stat roll; Completed, as setup placement sets both).
    /// Taking the last one sets the side's RecruitmentDone.
    /// </summary>
    static Character? RecruitPick(GameState game, StarSystem location, Owner side)
    {
        var unplaced = game.Characters.Where(c => c.Owner == side && !c.Placed && Family.Of(c.Type) is >= 0x38 and < 0x3C).ToList();
        if (unplaced.Count == 0) return null;
        var c = unplaced[game.Random.Roll(unplaced.Count - 1, DrawSite.Minor)];
        Objects.SetParent(game, c, location);
        Objects.SetCreated(game, c);
        NewGame.StatRoll(game, c);
        Objects.SetCompleted(game, c);
        if (unplaced.Count == 1) game.Sides[side].RecruitmentDone = true;
        return c;
    }
}
