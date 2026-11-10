using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

public static partial class Missions
{
    // ---- phases ----

    /// <summary>
    /// Phase setter: leaving 8 runs the phase check (a status set there advances at once: the nested 9 gives 11
    /// posts its phase notification first), then deactivates timer MissionPhase; the entry work; then post phase notification (priority PhasePriority).
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
            TimerCatalogue.Slot(game, TimerType.MissionPhase, m.Ref, m).SetActive(game, false);
        }
        Enter(game, m, phase);
        Objects.Notify(game, g => { g.Record(FactKind.MissionPhase, m.Ref, old, phase); OnPhase(g, m, old, phase); }, Objects.PhasePriority);
    }

    /// <summary>ReadyForNextPhase setter: posts ready for next phase notification (old, new) with priority ReadyPriority on a change.</summary>
    static void SetReady(GameState game, Mission m, bool value)
    {
        if (m.Ready == value) return;
        m.Ready = value;
        Objects.Notify(game, g => { g.Record(FactKind.MissionReady, m.Ref, value ? 0 : 1, value ? 1 : 0); if (value && !m.Destroyed) Advance(g, m); }, Objects.ReadyPriority);
    }

    /// <summary>
    /// ready for next phase notification with new value 1 gives advance: CompletionStatus != 0 gives Phase 11; else ReadyForNextPhase := 0 and Phase + 1,
    /// except 10 gives 8 for repeating types and 11 stays 11.
    /// </summary>
    static void Advance(GameState game, Mission m)
    {
        if (m.CompletionStatus != MissionCompletion.None) { SetPhase(game, m, 11); return; }
        SetReady(game, m, false);
        SetPhase(game, m, m.Phase switch { 10 when m.Record.Repeats => 8, 11 => 11, var p => p + 1 });
    }

    /// <summary>
    /// phase notification (old, new), in order: encounter resolver; member clean-up (Return missions); new phase 2:
    /// the speed rule; incite / subdue totals when the change crosses the range 8-10; auto-ready
    /// unless the phase is 4 or 8; the Dagobah case.
    /// </summary>
    static void OnPhase(GameState game, Mission m, int old, int now)
    {
        if (m.Destroyed) return;
        Encounters(game, m);
        CleanUp(game, m);
        if (m.Phase == 2) SpeedRule(game, m);
        if (m.Kind is MissionKind.InciteUprising or MissionKind.SubdueUprising && (old is >= 8 and <= 10) != (now is >= 8 and <= 10) && m.TargetLocation is StarSystem s) UprisingTotals(game, s);
        Story.DagobahPhaseChanged(game, m, now); // Inferred: after the other steps
        if (m.Phase is not (4 or 8)) SetReady(game, m, true);
    }

    /// <summary>
    /// Uprising totals of a system over the Usable incite / subdue missions of the container (both sides) whose
    /// TargetLocationKey is it and whose Phase is exactly 8: incite total = sum avgLead div G UprisingLeadershipDivisor, subdue total = sum -(avgLead div
    /// G UprisingLeadershipDivisor), and a third total = G InciteSupportDelta if an incite mission was counted, else 0. avgLead = sum of leadership over
    /// the members [48, 64) of Team, Decoy and Captured div their count (0 if none).
    /// </summary>
    internal static void UprisingTotals(GameState game, StarSystem s)
    {
        var g = game.Rules;
        int incite = 0, subdue = 0;
        bool any = false;
        foreach (var m in game.Missions.Where(m => m.Usable && m.Kind is MissionKind.InciteUprising or MissionKind.SubdueUprising && m.TargetLocation == s && m.Phase == 8))
        {
            var members = m.Members.Where(a => Family.Of(a.Type) is >= Families.MajorCharacter and < Families.Mission).ToList();
            int lead = members.Count == 0 ? 0 : members.Sum(a => Enhanced(game, a, SkillIndex.Leadership)) / members.Count;
            if (m.Kind == MissionKind.InciteUprising) { incite += lead / g.UprisingLeadershipDivisor; any = true; }
            else subdue += -(lead / g.UprisingLeadershipDivisor);
        }
        (s.InciteTotal, s.SubdueTotal, s.IncidentSupportDelta) = (incite, subdue, any ? g.InciteSupportDelta : 0);
    }

    /// <summary>Uprising notification part (a): phase check of every Usable mission [80, 96) whose TargetLocationKey is the system, key order.</summary>
    internal static void UprisingChanged(GameState game, StarSystem s)
    {
        foreach (var m in game.Missions.Where(m => m.Usable && m.Kind is >= MissionKind.Diplomacy and <= MissionKind.JediTraining && m.TargetLocation == s).OrderBy(m => m.Key).ToList())
            PhaseCheck(game, m);
    }

    /// <summary>
    /// Han rule: v = G HanHyperdriveModifier if Han (Refs.Han) is on this mission, not Captured, and no special force
    /// (60-63) is a member; else G HyperdriveModifier. Every character member (48-59, all lists) gets MissionHyperdriveModifier := v.
    /// </summary>
    static void SpeedRule(GameState game, Mission m)
    {
        bool han = game.Characters.Any(c => c.Ref == Refs.Han && c.Mission == m && !c.Captured)
            && !m.Members.Any(a => Family.IsSpecialForce(a.Type));
        int v = (han ? game.Rules.HanHyperdriveModifier : game.Rules.HyperdriveModifier);
        foreach (var c in m.Members.OfType<Character>()) c.MissionHyperdriveModifier = v;
    }

    // ---- member clean-up and Return missions ----

    /// <summary>
    /// Member clean-up, rule = the record's CleanUpRule. leave = CompletionStatus != 0 or a resign request; x = HyperdriveActive
    /// or rule 2 or (rule 1 and the owner of the member's location != its owner); y = x and rule != 2. 1: leaving members with x = 0 leave
    /// in place. 2: repeatedly, the leaving members with x = 1 get a destination; the first fixes D, every member with the same
    /// destination forms the group, leaves (keeping OnMission when some grouped member was in Team or Decoy), then: only Captured
    /// members gives released; D none gives each member through the no-destination path (<see cref="Prisoners.NoDestination"/>, with
    /// its Autorouting mission); else a Return mission. Members with a remove request leave but are
    /// excluded from both steps.
    /// </summary>
    static void CleanUp(GameState game, Mission m)
    {
        int rule = m.Record.CleanUpRule;
        bool Leaving(Agent a) => (m.CompletionStatus != MissionCompletion.None || a.Removed) && !a.RemoveRequest;
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
            else if (origin is not null) Create(game, Refs.ReturnMission, m.Owner, d, group, [], 0, m.Mandatory, origin, ReturnEncounters(m));
        }
    }

    /// <summary>
    /// Destination: only for source missions of families [80, 128), with y the member's current parent if a CreateMoveMission
    /// placement there succeeds, else ParentAtMissionKey if a CreateMoveMission placement there succeeds. Else (movement types at once) the
    /// fallback (once the game is ready): from = the member's location, else LocationAtMission, else Yavin (Alliance
    /// member) / Coruscant; seed = ParentAtMission if LocationAtMission is a system; destination search (flag y, stay 1), Bounty
    /// members gives Jabba's Palace.
    /// </summary>
    static GameObject? Destination(GameState game, Mission m, Agent a, bool y)
    {
        if (Family.Of(m.Type) is >= Families.AgentMission and < Families.Sector)
        {
            if (y && a.Container is { } here && Fleets.CanPlace(game, here, a.Type, a.Owner, CommandType.CreateMoveMission)) return here;
            if (a.ParentAtMission is { Existing: true } p && Fleets.CanPlace(game, p, a.Type, a.Owner, CommandType.CreateMoveMission)) return p;
        }
        return Fallback(game, a, y);
    }

    /// <summary>
    /// Mission fallback (once the game is ready): from = the member's location, else LocationAtMission, else Yavin
    /// (Alliance member) / Coruscant; seed = ParentAtMission if LocationAtMission is a system; destination search (stay 1). A
    /// member of a Bounty mission gets Jabba's Palace (the search draws nothing, so its result is not computed).
    /// </summary>
    internal static GameObject? Fallback(GameState game, Agent a, bool flag)
    {
        if (!game.Ready) return null;
        if (a.Mission?.Kind == MissionKind.Bounty) return game.Abode(Story.JabbasPalace); // Bounty member gives Jabba's Palace, found
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
        if (m.LastPhase > 6) { if (m.CompletionStatus == MissionCompletion.TargetDestroyed) v = v with { Phase2 = 3, Phase3 = 0 }; }
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
                m.TaskStatus = MissionTaskStatus.Pending;
                TimerCatalogue.Start(game, TimerType.MissionPhase, m.Ref, m, m.Record.DurationBase, m.Record.DurationVariance);
                break;
            case 10 when m.Kind is not (>= MissionKind.Move and <= MissionKind.Adrift): Execute(game, m); break; // movement classes: no phase-10 work
            case 11:
                PhaseCheck(game, m);
                Knowledge.Finalize(game, m);
                break;
        }
        if (m.Kind == MissionKind.JediTraining) Jedi.TrainingPhase(game, m, phase); // class phase hook
        else if (m.Kind == MissionKind.Dagobah) Story.DagobahPhase(game, m, phase);
    }
}
