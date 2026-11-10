using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public sealed partial class Messages
{
    bool Mine(Mission m) => Reporter(m) is Character;
    Agent? Reporter(Mission m) => m.Leader ?? m.Team.FirstOrDefault() ?? m.ReportLeader ?? m.ReportMember; // snapshots: an ended mission's members have left

    /// <summary>
    /// Type MessageType.MissionReport (TaskStatus != 0, category Mission): the mission report by type and outcome; "My ..." (title TextId "{1} Mission
    /// Report" with the leader) when a character leads, "The ..." otherwise; kind 5 with TextId.DoYouWishMission for diplomacy, research, incite and
    /// subdue while the mission continues (repeating types return to phase 8). Picture / sound: 1042 / 1043 and
    /// 1148 / 1149 by the message side, overwritten for diplomacy 1044 / 1150, espionage 1045 / 1152, research 1017 / 1151, subdue
    /// with TaskStatus 2 1010 / 1108 and 3 1011 / 1012 (A / E) / 1109.
    /// </summary>
    void MissionReport(int day, Mission m, MissionTaskStatus status)
    {
        bool ok = status == MissionTaskStatus.Succeeded, my = Mine(m);
        string who = Name(Reporter(m)), sys = Name(m.TargetLocation as StarSystem ?? m.Objective?.System), target = Name(m.Objective);
        MessageText title;
        var body = new List<MessageText>();
        switch (m.Kind)
        {
            case MissionKind.Diplomacy:
                title = my ? T(TextId.MyDiplomacyMissionIncreasedTitle, who) : T(TextId.DiplomacyMissionReport);
                body.Add(my ? T(ok ? TextId.MyDiplomacyMissionIncreased : TextId.MyDiplomacyMissionHad, "", sys) : T(ok ? TextId.DiplomacyMissionIncreasedPopular : TextId.DiplomacyMissionHadNo, sys));
                if (m.Objective is StarSystem s && s.Populated)
                    body.Add(T(s.Support > 60 ? TextId.PopulationSupportsAlliance : s.Support < 40 ? TextId.PopulationSupportsEmpire : TextId.PopulationDoesNotStrongly)); // thresholds Inferred
                break;
            case MissionKind.Espionage:
                title = my ? T(TextId.MyEspionageMissionSuccessfulTitle, who) : T(TextId.EspionageMissionReport);
                body.Add(my ? T(ok ? TextId.MyEspionageMissionSuccessful : TextId.MyEspionageMissionNot, "", sys) : T(ok ? TextId.EspionageMissionSuccessful : TextId.EspionageMissionNotSuccessful, sys));
                break;
            case MissionKind.Research:
                title = my ? T(TextId.MyResearchMissionMakingTitle, who) : T(TextId.MissionMakingProgressTitle, "", Name(m));
                int r = !ok ? 2 : m.LevelsGained > 0 ? 1 : 0;
                body.Add(my ? T(TextId.MyResearchMissionMaking + r, "", sys, Name(m)) : T(TextId.MissionMakingProgress + r, sys, Name(m)));
                break;
            case MissionKind.Reconnaissance:
                title = T(TextId.ReconMissionReport);
                body.Add(T(ok ? TextId.ReconnaissanceMissionSuccessful : TextId.ReconnaissanceMissionFailed, sys));
                break;
            case MissionKind.Recruitment:
                title = my ? T(TextId.MyRecruitmentMissionSuccessfulTitle, who) : T(TextId.Recruits, who, "", Name(m.Recruited));
                body.Add(T(ok ? TextId.MyRecruitmentMissionSuccessful : TextId.MyRecruitmentMissionComplete, who, sys, Name(m.Recruited)));
                break;
            case MissionKind.InciteUprising:
                title = my ? T(TextId.MyInciteSuccessTitle, who) : T(TextId.InciteUprisingMissionReport);
                body.Add(my ? T(ok ? TextId.MyInciteSuccess : TextId.MyInciteNoResults, "", sys) : T(ok ? TextId.InciteSuccess : TextId.InciteNoResultsNotMine, sys));
                break;
            case MissionKind.SubdueUprising:
                title = my ? T(TextId.MyMissionSubdueUprisingTitle, who) : T(TextId.SubdueUprisingMissionReport);
                body.Add(my ? T(ok ? TextId.MyMissionSubdueUprising : TextId.MySubdueUprisingMission, "", sys) : T(ok ? TextId.SubdueUprisingMissionSuccessful : TextId.SubdueUprisingMissionFailed, sys));
                break;
            case MissionKind.JediTraining:
                title = my ? T(TextId.MyJediTrainingMissionSuccessTitle, who) : T(TextId.JediTrainingMissionReport);
                body.Add(my ? T(ok ? TextId.MyJediTrainingMissionSuccess : TextId.MyJediTrainingMissionNot, "", sys) : T(ok ? TextId.JediTrainingMissionSuccess : TextId.JediTrainingMissionFailed, sys));
                break;
            case MissionKind.Rescue:
                title = my ? T(TextId.MyMissionRescueSuccessTitle, who) : T(TextId.RescueMissionReport);
                body.Add(my ? T(ok ? TextId.MyMissionRescueSuccess : TextId.MyMissionRescueFailed, "", sys, target) : T(ok ? TextId.MissionRescueSucceeded : TextId.MissionRescueFailed, sys, target));
                break;
            case MissionKind.Abduction:
                title = my ? T(TextId.MyMissionAbductSuccessTitle, who) : T(TextId.AbductionMissionReport);
                bool killed = m.Objective is { Destroyed: true };
                body.Add(my ? T(killed ? TextId.KilledDuringMyAbduction : ok ? TextId.MyMissionAbductSuccess : TextId.MyMissionAbductFailed, "", sys, target) : T(killed ? TextId.KilledDuringAbductionAttempt : ok ? TextId.MissionAbductSucceeded : TextId.MissionAbductFailed, sys, target));
                break;
            case MissionKind.Assassination:
                title = my ? T(TextId.MyAssassinationEliminatedTitle, who) : T(TextId.AssassinationMissionReport);
                bool dead = m.Objective is { Destroyed: true };
                body.Add(my ? T(dead ? TextId.MyAssassinationEliminated : TextId.MyAssassinationNotEliminated, "", sys, target) : T(dead ? TextId.AssassinationEliminated : ok ? TextId.AssassinationInjured : TextId.AssassinationNotEliminated, sys, target));
                break;
            case MissionKind.Sabotage or MissionKind.DeathStarSabotage:
                title = my ? T(TextId.SabotageMissionReportTitle, who) : T(TextId.SabotageMissionReport);
                body.Add(my ? T(TextId.MySabotageMissionTargeting, "", sys, target) : T(TextId.SabotageMissionTargetingComplete, sys, target));
                body.Add(T(m.Objective is { Destroyed: true } ? TextId.TargetDestroyed : TextId.TargetNotDestroyed));
                break;
            default:
                return;
        }
        bool asks = m.Kind is MissionKind.Diplomacy or MissionKind.Research or MissionKind.InciteUprising or MissionKind.SubdueUprising && m.Phase != 11 && m.CompletionStatus == MissionCompletion.None;
        if (asks) body.Add(T(TextId.DoYouWishMission));
        bool a = _side == Owner.Alliance;
        var (picture, sound) = (m.Kind, status) switch
        {
            (MissionKind.Diplomacy, _) => (1044, 1150),
            (MissionKind.Espionage, _) => (1045, 1152),
            (MissionKind.Research, _) => (1017, 1151),
            (MissionKind.SubdueUprising, MissionTaskStatus.Failed) => (1010, 1108),
            (MissionKind.SubdueUprising, MissionTaskStatus.Succeeded) => (a ? 1011 : 1012, 1109),
            _ => (a ? 1042 : 1043, a ? 1148 : 1149),
        };
        var reporter = Reporter(m);
        // the reporter through the player's view: a character to overlay; anything else to picture, replacing
        // the type's picture whenever the view exists (a picture that then fails to load shows none); no view to the type's picture
        bool seen = reporter is not null && (Knowledge.IsLive(reporter, _side) || _game.Views.ContainsKey((reporter, _side)));
        if (seen && reporter is not Character) picture = Pic(reporter);
        var msg = Tag(Add(day, MessageType.MissionReport, MessageCategory.Mission, asks ? 5 : 3, title, body, m.TargetLocation as StarSystem ?? m.Objective, picture, sound),
            Code(reporter), reporter, ok ? SuccessVoices : FailedVoices); // anyone but a major character (or none): AdvisorCode.MinorCharacterMessage
        if (asks) msg.Mission = m.Ref;
        if (seen && reporter is Character) msg.Overlay = Pic(reporter);
    }

    /// <summary>
    /// Type MessageType.MissionEnded branch (b), an own mission: dropped without the leader's view (LeaderKey; the
    /// real Leader, Inferred: an existing leader's view resolves) or when TargetLocationKey T is not a system. By the new
    /// CompletionStatus: 0-2 dropped; 3-5 foiled TextId.MyMissionFoiledOpposingTitle / TextId.MyMissionFoiledOpposing (major leader [48, 56)) or TextId.MissionFoiledOpposingForcesTitle / TextId.MissionFoiledOpposingForces, picture 1046 /
    /// 1047 and sound 1152; 6, 7, 9, 10 failed TextId.MyMissionFailedBecauseTargetTitle / TextId.MyMissionFailedBecauseTarget or TextId.MissionFailedBecauseTargetTitle / TextId.MissionFailedBecauseTarget (research MissionKind.Research dropped); 8 aborted TextId.MajorMissionAborted /
    /// TextId.MinorMissionAborted with body TextId.MyMissionAbortedBecauseSystemDeclared / TextId.MissionAbortedBecauseSystemDeclared (T's view owner 3) or TextId.MyMissionAbortedBecauseSystemJoined / TextId.MissionAbortedBecauseSystemJoined with the side of T's view owner (1 to side 1, else 2);
    /// CompletionStatus MissionCompletion.NoResearchFacility for research only: TextId.MyMissionFailedBecauseTargetTitle / TextId.MyMissionFailedBecauseWe or TextId.MissionFailedBecauseTargetTitle / TextId.MissionFailedBecauseWe; others dropped. Arguments (leader, mission, T[, side]); the body ends
    /// with the location line TextId.PersonnelReturning (W) / TextId.AllPersonnelAssignedMission. Picture 1042 / 1043, sound 1148 / 1149 by the message side; advisor code and
    /// voice by a major leader (other majors none), AdvisorCode.MinorCharacterMessage for a minor character. A character leader's portrait is the overlay, and a
    /// special-force leader's picture is kept by a recon mission's foil. Both come from the object picture id.
    /// </summary>
    void MissionEnded(int day, Mission m, MissionCompletion status)
    {
        if ((m.Leader ?? m.ReportLeader) is not { } leader || m.TargetLocation is not StarSystem t) return;
        bool major = leader is Character && Family.Of(leader.Type) < Families.MinorCharacter, research = m.Kind == MissionKind.Research, a = _side == Owner.Alliance;
        string who = Name(leader), type = Name(m), sys = Name(t);
        int picture = a ? 1042 : 1043, sound = a ? 1148 : 1149;
        if (Family.IsSpecialForce(leader.Type)) picture = Pic(leader); // A special-force leader's picture replaces 1042 / 1043
        MessageText title, body;
        switch (status)
        {
            case MissionCompletion.FoiledEnRoute or MissionCompletion.Foiled or MissionCompletion.NoTeam:
                (title, body) = major ? (T(TextId.MyMissionFoiledOpposingTitle, who, type), T(TextId.MyMissionFoiledOpposing, who, type, sys)) : (T(TextId.MissionFoiledOpposingForcesTitle, who, type), T(TextId.MissionFoiledOpposingForces, who, type, sys));
                sound = 1152;
                if (!(Family.IsSpecialForce(leader.Type) && m.Kind == MissionKind.Reconnaissance)) picture = a ? 1046 : 1047; // r: recon led by a special force keeps the leader's picture
                break;
            case MissionCompletion.ObjectiveInvalid or MissionCompletion.TargetDestroyed or MissionCompletion.SystemBombarded or MissionCompletion.SystemAssaulted when !research:
                (title, body) = major ? (T(TextId.MyMissionFailedBecauseTargetTitle, who, type), T(TextId.MyMissionFailedBecauseTarget, who, type, sys)) : (T(TextId.MissionFailedBecauseTargetTitle, who, type), T(TextId.MissionFailedBecauseTarget, who, type, sys));
                break;
            case MissionCompletion.ObjectiveOwnerRefused:
                var owner = Knowledge.See(_game, t, _side).Owner;
                title = T(major ? TextId.MajorMissionAborted : TextId.MinorMissionAborted, who, type);
                body = owner == Owner.Neutral ? T(major ? TextId.MyMissionAbortedBecauseSystemDeclared : TextId.MissionAbortedBecauseSystemDeclared, who, type, sys)
                    : T(major ? TextId.MyMissionAbortedBecauseSystemJoined : TextId.MissionAbortedBecauseSystemJoined, who, type, sys, MessageArg.Of(owner == Owner.Alliance ? Owner.Alliance : Owner.Empire));
                break;
            case MissionCompletion.NoResearchFacility when research:
                (title, body) = major ? (T(TextId.MyMissionFailedBecauseTargetTitle, who, type), T(TextId.MyMissionFailedBecauseWe, who, type, sys)) : (T(TextId.MissionFailedBecauseTargetTitle, who, type), T(TextId.MissionFailedBecauseWe, who, type, sys));
                break;
            default:
                return;
        }
        var code = leader.Type switch
        {
            Mothma => AdvisorCode.MothmaMessage, Refs.Leia => AdvisorCode.LeiaMessage, Refs.Luke => AdvisorCode.LukeMessage, Refs.Han => AdvisorCode.HanMessage,
            Refs.Palpatine => AdvisorCode.PalpatineMessage, Refs.Vader => AdvisorCode.VaderMessage,
            _ => leader is Character ? major ? AdvisorCode.None : AdvisorCode.MinorCharacterMessage : AdvisorCode.Report, // special forces: the class default Report
        };
        var msg = Tag(Add(day, MessageType.MissionEnded, MessageCategory.Mission, 3, title, [body, Where(m)], t, picture, sound), code, leader, FailedVoices);
        if (leader is Character) msg.Overlay = Pic(leader); // the leader's portrait
    }

    /// <summary>Location line: the first member (key order, Inferred) whose view is present, not Destroyed and not Captured, with a system or fleet above its view to TextId.PersonnelReturning (its name), else <see cref="Mission.ReportPlace"/>, else TextId.AllPersonnelAssignedMission.</summary>
    MessageText Where(Mission m)
    {
        foreach (var x in m.Members.OrderBy(x => x.Key))
        {
            var v = Knowledge.See(_game, x, _side);
            if (!Knowledge.IsLive(x, _side) && !_game.Views.ContainsKey((x, _side)) || v.Destroyed || v.Captured) continue;
            for (var p = Knowledge.ViewParent(_game, x, _side); p is not null; p = Knowledge.ViewParent(_game, p, _side))
                if (p is StarSystem or Fleet) return T(TextId.PersonnelReturning, Name(p));
        }
        return m.ReportPlace is { } at ? T(TextId.PersonnelReturning, Name(at)) : T(TextId.AllPersonnelAssignedMission); // members already left: the place snapshot at the notification
    }

    /// <summary>
    /// Type MessageType.MissionEnded branch (a), an enemy mission: the side's frozen view of another side's mission takes a
    /// non-zero CompletionStatus with TaskStatus 0 (only the encounter refresh writes it): TextId.EnemyMissionFoiled (mission) /
    /// TextId.EnemyMissionFoiledForces (mission, T's system name; none when T is not a system), picture 1073 / 1074, sound 1148 / 1149, subject T.
    /// </summary>
    void EnemyMissions(int day)
    {
        foreach (var ((o, side), v) in _game.Views)
        {
            if (side != _side || o is not Mission m || v.Owner != Enemy || m.Kind is >= MissionKind.Move and <= MissionKind.Adrift) continue;
            if (v.CompletionStatus == MissionCompletion.None || v.TaskStatus != MissionTaskStatus.Pending || v.CompletionStatus == _missionViews.GetValueOrDefault(m)) continue;
            bool a = _side == Owner.Alliance;
            var t = m.TargetLocation;
            var msg = Add(day, MessageType.MissionEnded, MessageCategory.Mission, 3, T(TextId.EnemyMissionFoiled, Name(m)), [T(TextId.EnemyMissionFoiledForces, Name(m), t is StarSystem ? Name(t) : "")], null,
                a ? 1073 : 1074, a ? 1148 : 1149);
            msg.Subject = t?.Ref ?? 0;
        }
    }
}
