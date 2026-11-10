using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>
    /// Agent table (zero the agent aggregates, sector maximum included; clear OwnCaptured). Inferred from the planner-5 readers,
    /// the per-record sums are not specced: the first aggregate is the MaintenanceCost of the own special forces,
    /// and the next four are their counts per special-force type.
    /// </summary>
    void AgentAggregates()
    {
        // zero the agent aggregates and the sector maximum inside that range (as coded)
        (SpecialForceMaintenance, FacilityDesigners, ShipDesigners, TroopTrainers, MaxEnemyUnits) = (0, 0, 0, 0, 0);
        for (int k = 0; k < 4; k++) SpecialForces(k) = MarkedSpecialForces(k) = 0;
        OwnCaptured = false;
        foreach (var r in Agents.Values)
        {
            if (!r.Own) continue;
            SpecialForceMaintenance += r.Maintenance;
            if (r.Character)
            {
                if (r.Captured) OwnCaptured = true;
                if (r.Can.FacilityDesign) FacilityDesigners++;
                if (r.Can.ShipDesign) ShipDesigners++;
                if (r.Can.TroopTraining) TroopTrainers++;
                continue;
            }
            for (int k = 0; k < 4; k++)
                if (r.Can.SpecialForceKind == k + 1)
                {
                    SpecialForces(k)++;
                    if (r.Can.Unused) MarkedSpecialForces(k)++; // no writer: stays 0
                }
        }
    }

    // ---- agent record (create and update) ----

    void CreateAgent(Agent a)
    {
        var r = new AiAgent(a) { Key = (int)a.Ref, Side = (int)Side };
        var v = See(a);
        r.Unassigned = true;
        if (a is Character c)
        {
            r.Character = true;
            // a captured character's owner is its captor
            if (v.Owner == Side && !v.Captured || v.Owner != Side && v.Captured) r.Own = true;
            else r.Enemy = true;
            if (v.Captured) r.Captured = true;
            if (c.Ref is Refs.Luke or Refs.Mothma or Refs.Palpatine or Refs.Vader) r.VictoryCharacter = true;
            if (c.Ref == Refs.Palpatine) r.Palpatine = true;
            if (c.Ref is Refs.Luke or Refs.Mothma or Refs.Palpatine or Refs.Vader or Refs.Leia or Refs.Han) r.Major = true;
            else
            {
                if (c.Definition.CanBeAdmiral != 0) r.Can.Admiral = true;
                if (c.Definition.CanBeCommander != 0) r.Can.Commander = true;
                if (c.Definition.CanBeGeneral != 0) r.Can.General = true;
            }
        }
        else if (Family.IsSpecialForce(a.Type))
        {
            r.SpecialForce = true;
            if (v.Owner == Side) r.Own = true;
            else r.Enemy = true;
            r.Can.SpecialForceKind = (int)(((_game.Definitions.Resolve(a.Type)?.Common.Index ?? 0) - 1) % 4) + 1;
        }
        // capability probe: the team-only type list of a CreateMission command for this unit alone (never submitted)
        foreach (var type in MissionEligibility.TypesForTeam(_game, Side, [a], []))
            foreach (var (t, set) in Capabilities)
                if (type.Common.Ref == t) set(r.Can);
        Agents[a.Ref] = r;
        // the agent container count is the model's own minor characters still to come, -1 per own non-major character record created
        // (both callers, again after a Destroyed detach; nothing increments it)
        if (r.Character && !r.Major && r.Own) MinorCharactersToCome--;
        DirtyAgent(r);
    }

    void UpdateAgent(AiAgent r)
    {
        var a = r.Agent;
        r.ClearUnkept();
        r.ClearSkills();
        if (!HasView(a)) return;
        var v = See(a);
        if (v.Destroyed) return;
        r.Owner = (int)v.Owner;
        // shortcut: agent fields beyond the view snapshot (OnMission, OnMandatoryMission, Injury, CommandKind, Force, skills) read
        // from the real object. Upgrade with the view fields when the planners read them (rounds 6, 8).
        if (a.OnMission) r.OnMission = true;
        if (a.Mission is { Mandatory: true }) r.MandatoryMission = true;
        if (Family.IsSpecialForce(a.Type)) r.Maintenance = P(a.Type).MaintenanceCost;
        if (v.Enroute) r.Enroute = true;
        if (!v.Completed) r.NotCompleted = true;
        if (a is Character c)
        {
            if (v.Captured) r.Captured = true;
            if (c.Injury != 0) r.Injured = true;
            // a research capability whose line is not exhausted
            var caps = r.Can;
            if (caps.ShipDesign && !ShipLineDone || caps.TroopTraining && !TroopLineDone
                || caps.FacilityDesign && !FacilityLineDone) r.Specialist = true;
            if (c.CommandKind == CommandKind.Commander) r.CommandKind1 = true;
            else if (c.CommandKind == CommandKind.Admiral) r.CommandKind2 = true;
            else if (c.CommandKind == CommandKind.General) r.CommandKind3 = true;
        }
        for (int k = 0; k < 8; k++) r.Skill(k) = Missions.Enhanced(_game, a, k);
        if (a is Character ch)
        {
            r.Force = ch.Force;
            bool lukeOrVader = ch.Ref is Refs.Luke or Refs.Vader;
            if (ch.Force is >= 1 and <= 99 && !lukeOrVader) r.ForceTrainee = true;
            if (ch.Force >= 100 && lukeOrVader) r.ForceMaster = true;
        }
        if (r.Can.Diplomacy && r.Skill(SkillIndex.Diplomacy) > 79) r.Specialist = true;
        if (r.Location is null && ViewParent(a) is { } p) r.Location = p;
    }

    // ---- system record ----
}
