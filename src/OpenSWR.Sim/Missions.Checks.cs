using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

public static partial class Missions
{
    /// <summary>
    /// Phase check: nothing if CompletionStatus != 0; unless ImpliedTeam: no valid Team member (all have
    /// a remove / resign request, or none) gives 5, the class check skipped; else the class check; no status and Phase 11 gives 1.
    /// </summary>
    internal static void PhaseCheck(GameState game, Mission m)
    {
        if (m.CompletionStatus != MissionCompletion.None) return;
        var code = !m.ImpliedTeam && m.Team.All(a => a.Removed) ? MissionCompletion.NoTeam : ClassCheck(game, m);
        if (code == 0 && m.Phase == 11) code = MissionCompletion.Done;
        if (code != 0) SetCompletion(game, m, code);
    }

    /// <summary>
    /// Class check of a running mission (callback 1; the validate-only checks are omitted: status -1 never reaches a running
    /// mission). ObjectiveKey O and TargetKey T as objects (the side's views are not modelled); either missing gives none. Steps in
    /// chain order, each only while no earlier one failed unless noted. 1. Generic (every class; the movement classes
    /// 65-68 stop here): RefusesDestroyedTarget and T Destroyed gives 7; the owner of O relative to the mission owner (own / opp /
    /// other) not allowed by the record gives 8. 2. Standard: RefusesDestroyedObjective and O Destroyed gives 6; O Enroute gives 6;
    /// locations of O and T differ or missing gives 6; NeedsPopulatedTarget and T not a Populated system gives 13. 3. Target kind: system
    /// target (81-88): O's Uprising set and not AllowsUprisingSystem, or clear and not AllowsCalmSystem gives 6; character target
    /// (97-101, runs even after an earlier failure and overwrites it): O a character, Captured and not AllowsCapturedCharacter,
    /// or not and not AllowsFreeCharacter gives 6; unit / abode targets: no running check. 4. Diplomacy: owner 1/2 and share(O, owner) = 100
    /// gives 15. 5a. Research: O's manager of kind k missing or without a non-suspended member gives 12; owner's line k
    /// exhausted gives 14. 5b. Recruitment: owner's RecruitmentDone gives 16.
    /// </summary>
    static MissionCompletion ClassCheck(GameState game, Mission m)
    {
        if (m.Objective is not { } o) return 0;
        var t = m.TargetKey; // none after a failed target test at creation: no location, so status 6 (Inferred)
        var r = m.Record;
        // while LastPhase < 5 and the target differs from the origin the owner's views are read
        var side = m.LastPhase < 5 && m.TargetLocation != m.Origin ? m.Owner : Owner.None;
        Seen so = Knowledge.See(game, o, side), st = t is null ? default : Knowledge.See(game, t, side);
        MissionCompletion code = r.RefusesDestroyedTarget && st.Destroyed ? MissionCompletion.TargetDestroyed
            : !(so.Owner == m.Owner ? r.AllowsOwnObjective : so.Owner == Systems.Opp(m.Owner) ? r.AllowsEnemyObjective : r.AllowsNeutralObjective) ? MissionCompletion.ObjectiveOwnerRefused : 0;
        if (m.Kind is >= MissionKind.Move and <= MissionKind.Adrift) return code;
        if (code == 0)
            code = r.RefusesDestroyedObjective && so.Destroyed || so.Enroute || so.Location is null || so.Location != st.Location ? MissionCompletion.ObjectiveInvalid
                : r.NeedsPopulatedTarget && !(t is StarSystem && st.Populated) ? MissionCompletion.TargetNotPopulated : 0;
        if (m.Kind is >= MissionKind.Diplomacy and <= MissionKind.JediTraining && code == 0 && o is StarSystem && !(so.Uprising ? r.AllowsUprisingSystem : r.AllowsCalmSystem)) code = MissionCompletion.ObjectiveInvalid;
        if (m.Kind is >= MissionKind.Rescue and <= MissionKind.Bounty && o is Character && !(so.Captured ? r.AllowsCapturedCharacter : r.AllowsFreeCharacter)) code = MissionCompletion.ObjectiveInvalid;
        if (m.Kind == MissionKind.Diplomacy && code == 0 && o is StarSystem && (m.Owner == Owner.Alliance ? so.Support : 100 - so.Support) == 100) code = MissionCompletion.FullSupport;
        if (code == 0) code = ResearchRecruitCheck(game, m);
        return code;
    }

    /// <summary>
    /// Class checks 5a / 5b, also run at creation: research status 12 / 14, recruitment 16;
    /// 0 = pass.
    /// </summary>
    internal static MissionCompletion ResearchRecruitCheck(GameState game, Mission m) => ResearchRecruitCheck(game, m.Type, m.Owner, m.Objective);

    internal static MissionCompletion ResearchRecruitCheck(GameState game, uint type, Owner owner, GameObject? objective)
    {
        bool side = owner is Owner.Alliance or Owner.Empire;
        if ((MissionKind)Family.Of(type) == MissionKind.Recruitment) return side && game.Sides[owner].RecruitmentDone ? MissionCompletion.RecruitmentDone : 0;
        if ((MissionKind)Family.Of(type) != MissionKind.Research) return 0;
        int k = ResearchLine(type);
        var manager = (objective as StarSystem)?.Managers.FirstOrDefault(x => x is not null && Research.Line(x.Kind) == k);
        if (manager is null || !Economy.Members(manager).Any(f => !f.Suspended)) return MissionCompletion.NoResearchFacility;
        return side && game.Sides[owner].ResearchExhausted[k] ? MissionCompletion.ResearchExhausted : 0;
    }

    /// <summary>Research line k of a mission type: Refs.ShipResearchMission gives 0 ship, Refs.FacilityResearchMission gives 1 facility, Refs.TroopResearchMission gives 2 troop.</summary>
    static int ResearchLine(uint type) => type switch { Refs.ShipResearchMission => 0, Refs.FacilityResearchMission => 1, _ => 2 };

    /// <summary>
    /// CompletionStatus setter, called from one place: one command serial, then the setter (on
    /// change); its hook: a nonzero value advances at once, to Phase 11.
    /// </summary>
    internal static void SetCompletion(GameState game, Mission m, MissionCompletion code)
    {
        game.Clock.NextSerial();
        if (m.CompletionStatus == code) return;

        game.Record(FactKind.CompletionStatus, m.Ref, (int)m.CompletionStatus, (int)code);
        m.CompletionStatus = code;
        if (code != 0) Advance(game, m);
    }

    /// <summary>Manager-F8 owner notification hook: every mission whose ObjectiveKey or TargetKey is the object runs its phase check.</summary>
    internal static void OwnerChanged(GameState game, GameObject o)
    {
        foreach (var m in game.Missions.Where(m => !m.Destroyed && (m.Objective == o || m.TargetKey == o)).ToList()) PhaseCheck(game, m);
    }

    // ---- travel ----

    /// <summary>
    /// Phase 4: if CompletionStatus = 0, every member (Team, Decoy, Captured; families 48-63) travels from the origin to the
    /// target location and moves into TargetKey; then the arrival check.
    /// </summary>
    static void TravelPhase(GameState game, Mission m)
    {
        if (m.CompletionStatus != MissionCompletion.None) return;
        if (m.TargetKey is { } target) // none: no travel (Inferred)
            foreach (var a in m.Members.Where(a => Family.Of(a.Type) is >= Families.MajorCharacter and < Families.Mission).ToList())
            {
                Fleets.Travel(game, a, m.Origin, m.TargetLocation);
                Fleets.Move(game, a, target);
            }
        ArrivalCheck(game, m);
    }

    /// <summary>
    /// Arrival check, in phase 4: when AllMembersArrived (no member HyperdriveActive), TargetLocationKey := location
    /// of TargetKey; every member still EnrouteActive runs the test (a new leg if re-parented in hyperspace; arrived =
    /// not HyperdriveActive; EnrouteActive is cleared only on entering phase 6); all arrived gives ReadyForNextPhase := 1.
    /// </summary>
    internal static void ArrivalCheck(GameState game, Mission m)
    {
        if (m.Phase != 4 || m.Members.Any(a => a.HyperdriveActive)) return;
        m.TargetLocation = m.TargetKey is null ? null : Location(m.TargetKey);
        bool arrived = true;
        foreach (var a in m.Members.Where(a => a.EnrouteActive).ToList()) arrived = Fleets.NewLeg(game, a) && arrived;
        if (arrived) SetReady(game, m, true);
    }

    /// <summary>Timer MissionPhase action: in phase 8, ReadyForNextPhase := 1.</summary>
    internal static void PhaseTimer(GameState game, Mission m)
    {
        if (m.Phase == 8) SetReady(game, m, true);
    }

    /// <summary>Timer MissionCompletion action: in phase 11, DestroyedReason Scrapped (the mission object is destroyed).</summary>
    internal static void CompletionTimer(GameState game, Mission m)
    {
        if (m.Phase == 11) Objects.Destroy(game, m, DestroyedReason.Scrapped);
    }
}
