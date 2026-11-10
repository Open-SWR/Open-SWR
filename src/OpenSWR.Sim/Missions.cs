using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>
/// Mission lifecycle (phases 0 gives 11 through the ready for next phase and phase notifications, timer MissionPhase / timer MissionCompletion), member travel, the encounter
/// resolver, execution (phase 10) and effects.
/// Deferred, not modelled: story missions and Jedi training, knowledge / fog of war, betrayal consequences.
/// Prisoners and the Autorouting / Adrift grouping: <see cref="Prisoners"/>.
/// </summary>
public static partial class Missions
{
    // ---- creation and membership ----

    /// <summary>
    /// Mission creation: one command serial, one counter id; object created in manager 248 (owner 3 maps to side,
    /// Created, Completed); Origin = the team's common location (or the given one), ObjectiveKey = target, TargetKey = the given
    /// one, else target (command-created non-movement missions: the target's location),
    /// TargetLocationKey = the target's location; Phase := 1; members join in list order (ascending SdRef):
    /// standard classes: list 1 goes to Team, list 2 to Decoy; movement classes 65-68: the leader of list 1 goes to Team,
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
        m.Encounters = encounters ?? (m.Kind == MissionKind.Adrift ? EncounterCodes.Adrift : EncounterCodes.Standard); // class-init codes (Adrift has its own default); passed codes replace them
        m.Origin = origin ?? Location(team[0]);
        SetObjective(game, m, target);
        m.TargetKey = targetKey ?? target;
        m.TargetLocation = Knowledge.Location(game, target, side) ?? Location(m.TargetKey); // the owner's view; callers refuse an unknown one
        SetPhase(game, m, 1);
        // A movement class without a leader reports a failed join but rolls nothing back: every member joins Team.
        var leader = m.Kind is >= MissionKind.Move and <= MissionKind.Adrift ? BestLeader(game, team) : null;
        foreach (var a in team.OrderBy(a => a.Ref)) Join(game, m, a, decoy: leader is not null && a != leader);
        foreach (var a in decoys.OrderBy(a => a.Ref)) Join(game, m, a, decoy: true);
        return m;
    }

    /// <summary>
    /// Move mission of a produced character or special force: type Refs.MoveMission, owner = the unit's owner,
    /// ObjectiveKey = TargetKey = the unit's location, origin = the manager's location, team = the unit, Mandatory, UserID 0.
    /// </summary>
    internal static void CreateMove(GameState game, Agent unit, ManufacturingManager manager)
    {
        if (unit.System is { } at) Create(game, Refs.MoveMission, unit.Owner, at, [unit], [], 0, mandatory: true, origin: manager.System);
    }

    /// <summary>
    /// Autorouting (type Refs.AutoroutingMission) or Adrift (type Refs.AdriftMission) mission: the Return wrapper; origin = the
    /// group's common location (none gives no mission, nothing consumed), then one serial and the object, created unconditionally
    /// (owner as passed, always 1 or 2 on these paths); ObjectiveKey = TargetKey = D, team = the group, no decoys, UserID 0,
    /// Mandatory, ImpliedTeam as passed (Adrift 0), class-init encounter codes (standard / Adrift).
    /// </summary>
    internal static void CreateAutorouting(GameState game, bool adrift, Owner owner, GameObject d, IReadOnlyList<Agent> group, bool impliedTeam)
    {
        var origin = group[0].System;
        if (origin is null || group.Any(a => a.System != origin)) return;
        Create(game, adrift ? Refs.AdriftMission : Refs.AutoroutingMission, owner, d, group, [], 0, mandatory: true, origin, impliedTeam: impliedTeam && !adrift);
    }

    /// <summary>
    /// Location: <see cref="GameObject.System"/> (the system of the parent chain; a manager's system);
    /// outside systems the first abode (family 152) of the parent chain, which is its own location (abodes are direct contents
    /// of the game object; jails and story abodes).
    /// </summary>
    internal static GameObject? Location(GameObject o)
    {
        if (o.System is { } s) return s;
        for (GameObject? x = o; x is not null; x = x.Container)
            if (Family.Of(x.Type) == Families.Abode) return x;
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
    /// RoleMissionKey setter: on a change post role mission key notification (old, new), whose handler locks the new mission and
    /// unlocks the old one.
    /// </summary>
    static void SetMission(GameState game, Agent a, Mission? m)
    {
        var old = a.Mission;
        if (old == m) return;
        a.Mission = m;
        PostRole(game, a, old, m);
    }

    /// <summary>Posts role mission key notification (old, new); its handler locks the new mission and unlocks the old one.</summary>
    internal static void PostRole(GameState game, Agent a, Mission? old, Mission? m) =>
        Objects.Notify(game, g => { g.Record(FactKind.AgentMission, a.Ref, old?.Ref ?? 2, m?.Ref ?? 2); Objects.Lock(g, m, +1); Objects.Lock(g, old, -1); });

    /// <summary>Inserts into a member list kept by ascending SdRef.</summary>
    static void Insert(List<Agent> list, Agent a) => list.Insert(list.FindLastIndex(x => x.Ref < a.Ref) + 1, a);

    /// <summary>
    /// Join: RoleMissionKey := mission, OnMission (feeds timer ForceDiscovery; when it becomes set, ParentAtMissionKey := the
    /// parent); into Captured, Decoy or Team.
    /// </summary>
    internal static void Join(GameState game, Mission m, Agent a, bool decoy)
    {
        SetMission(game, a, m);
        a.ResignOnInjury = m.Record.ResignOnInjury;
        if (!a.OnMission)
        {
            (a.OnMission, a.ParentAtMission, a.LocationAtMission) = (true, a.Container, a.System);
            Objects.Notify(game, g => Story.OnMissionChanged(g, a)); // mission membership notification
        }
        Insert(a is Character { Captured: true } ? m.Captured : decoy ? m.Decoys : m.Team, a);
        if (a is Character c) TimerCatalogue.RecomputeForceActive(game, c);
    }

    /// <summary>
    /// Leave: EnrouteActive := 0 unless hyperspacing, out of its list, RoleMissionKey cleared (a character's
    /// MissionHyperdriveModifier := G HyperdriveModifier and IsDecoying := 0); OnMission and ParentAtMissionKey cleared unless the member moves to
    /// another mission (<paramref name="moving"/>); phase check, a new leader if the leader left, and the timer MissionCompletion
    /// start check (no member left and CompletionStatus != 0 starts timer MissionCompletion with base 1, no variance). Inferred: the resign
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
            Objects.Notify(game, g => Story.OnMissionChanged(g, a)); // mission membership notification
        }
        a.ResignRequest = a.RemoveRequest = false;
        if (a is Character c)
        {
            TimerCatalogue.RecomputeForceActive(game, c);
            Prisoners.EscapeGate(game, c);
        }
        PhaseCheck(game, m);
        if (m.Leader == a) SelectLeader(game, m);
        if (!m.Members.Any() && m.CompletionStatus != MissionCompletion.None) TimerCatalogue.Start(game, TimerType.MissionCompletion, m.Ref, m, 1, 0);
        Jedi.Left(game, m, a); // Jedi training
    }

    /// <summary>Leader selection over the Team.</summary>
    static void SelectLeader(GameState game, Mission m) => m.Leader = BestLeader(game, m.Team);

    /// <summary>
    /// Over members of families [48, 64) not removed (resign request), not Destroyed and not Captured: the first,
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
            int rank = Family.Of(a.Type) switch { < Families.MajorCharacter or >= Families.Mission => 0, < Families.MinorCharacter => 3, < Families.SpecialForce => 2, _ => 1 };
            if (rank == 0) continue;
            int lead = Enhanced(game, a, SkillIndex.Leadership);
            if (best is null || rank > bestRank || rank == bestRank && lead > bestLead) (best, bestRank, bestLead) = (a, rank, lead);
        }
        return best;
    }
}
