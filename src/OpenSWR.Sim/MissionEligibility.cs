using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Mission validation for commands CreateMission / CreateMoveMission and the UI lists: team checks (resolve, common location, per agent, side), the
/// owner's view of the target, then the class chain generic, standard, target kind, class (all target fields from the
/// owner's views; first error wins, except the character Captured rule). Queries: <see cref="TypesForTeam"/> and
/// <see cref="TypesForTarget"/>.
/// Inferred: a non-agent in the team gives (1, 1) (class refuses the type); a location that is not a system or abode gives (NotTogether).
/// </summary>
public static class MissionEligibility
{
    /// <summary>
    /// Team checks 1-4: resolve (a non-captured Team member, else (NoFreeAgent)); common location (parent chain
    /// to the first system or abode; no member with one (NoLocation); differing (NotTogether)); per agent (with <paramref name="perAgent"/>: base order check, OnMission (OnMission), for CreateMission
    /// Injury (Injured) and Captured (Captured)); sides (both (NotOwner), none (NoLocation)). Null = pass.
    /// </summary>
    static OrderResult? Team(GameState game, Owner side, CommandType commandType, IReadOnlyList<GameObject> team, IReadOnlyList<GameObject> decoys, bool perAgent, bool sides = true)
    {
        if (team.Concat(decoys).Any(o => o is not Agent)) return Refusal.WrongClass; // class refuses the type (Inferred)
        if (team.Concat(decoys).Any(o => o.Destroyed)) return Refusal.Missing; // team resolve check
        var agents = team.Cast<Agent>().ToList();
        if (!agents.Any(a => a is not Character { Captured: true })) return Refusal.NoFreeAgent;
        var all = agents.Concat(decoys.Cast<Agent>()).ToList();
        // locations (parent chain up to the first system or abode); none gives (NoLocation); differing gives (NotTogether)
        if (all.All(a => Missions.Location(a) is null)) return Refusal.NoLocation;
        var at = Missions.Location(all[0]);
        if (at is null || all.Any(a => Missions.Location(a) != at)) return Refusal.NotTogether;
        if (perAgent)
            foreach (var a in all)
            {
                var check = a.Owner is not (Owner.Alliance or Owner.Empire) || a.Owner != side ? Refusal.NotOwner
                    : !a.Completed && commandType != CommandType.CreateMoveMission ? Refusal.NotCompleted
                    : a.Destroyed ? Refusal.Missing : a.Enroute ? Refusal.Enroute : (OrderResult?)null;
                if (check is not null) return check;
                if (a.OnMission) return Refusal.OnMission;
                if (commandType == CommandType.CreateMission && a is Character c)
                {
                    if (c.Injury != 0) return Refusal.Injured;
                    if (c.Captured) return Refusal.Captured;
                }
            }
        if (!sides) return null;
        bool alliance = all.Any(a => a.Owner == Owner.Alliance), empire = all.Any(a => a.Owner == Owner.Empire);
        return alliance && empire ? Refusal.NotOwner : !alliance && !empire ? Refusal.NoLocation : null;
    }

    /// <summary>Masks and side check, part of the generic step: every special force's MissionMask allowed by the record, characters allowed (AllowsCharacters) when any joins, the side's flag set; else (AgentNotAllowed).</summary>
    static OrderResult? Masks(GameState game, MissionRecord r, IReadOnlyList<Agent> all)
    {
        bool forces = all.Where(a => Family.IsSpecialForce(a.Type))
            .All(a => r.AllowsSpecialForce(game.Definitions.SpecialForces.FirstOrDefault(s => s.Common.Ref == a.Type)?.MissionMask ?? 0));
        if (!forces || all.Any(a => a is Character) && !r.AllowsCharacters) return Refusal.AgentNotAllowed;
        return (all[0].Owner == Owner.Alliance ? r.Alliance : r.Empire) == 0 ? Refusal.AgentNotAllowed : null;
    }

    /// <summary>Class team rules: research skill (NoSkill), recruitment majors (MajorsOnly), Jedi training, story types.</summary>
    static OrderResult? TeamRules(GameState game, MissionRecord r, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys)
    {
        var all = team.Concat(decoys).ToList();
        var kind = (MissionKind)Family.Of(r.Common.Ref);
        bool Decoy(Agent a) => decoys.Contains(a);
        switch (kind)
        {
            case MissionKind.Research: // Research: every member has a nonzero skill of the kind
                int skill = r.Common.Ref switch { Refs.ShipResearchMission => SkillIndex.ShipDesign, Refs.FacilityResearchMission => SkillIndex.FacilityDesign, _ => SkillIndex.TroopTraining };
                if (all.Any(a => a.Skills[skill] == 0)) return Refusal.NoSkill;
                break;
            case MissionKind.Recruitment when all.Any(a => Family.Of(a.Type) is not (>= Families.MajorCharacter and < Families.MinorCharacter)): return Refusal.MajorsOnly; // majors only
            case MissionKind.JediTraining: // Jedi training
                var chars = team.OfType<Character>().ToList();
                var teacher = chars.FirstOrDefault(c => c.Definition.IsJediTrainer != 0 && c.Force >= game.Rules.JediTeacherMinForce);
                if (teacher is null) return Refusal.NoTeacher;
                if (chars.Any(c => c.Force == 0)) return Refusal.NoForce;
                if (chars.Any(c => c != teacher && c.Force >= teacher.Force)) return Refusal.TeacherNotStrongest;
                if (team.Count < 2) return Refusal.TeamTooSmall;
                break;
            case MissionKind.Palace: // Palace: Luke / Leia / Chewbacca, not Captured, not Decoy
                if (all.Any(a => a.Ref is not (Refs.Luke or Refs.Leia or Refs.Chewbacca))) return Refusal.NotPalaceTeam;
                if (all.Any(a => a is Character { Captured: true })) return Refusal.CapturedMember;
                if (all.Any(Decoy)) return Refusal.DecoyNotAllowed;
                break;
            case MissionKind.Dagobah or MissionKind.Vacation or MissionKind.Pickup: // Dagobah / Vacation: Luke only; Pickup: Vader only
                if (all.Any(a => Family.Of(a.Type) != (kind == MissionKind.Pickup ? Families.Vader : Families.Luke))) return kind == MissionKind.Pickup ? Refusal.NotVaderTeam : Refusal.NotLukeTeam;
                if (all.Any(a => a is Character { Captured: true })) return Refusal.CapturedMember;
                if (all.Any(Decoy)) return Refusal.DecoyNotAllowed;
                break;
            case MissionKind.Bounty: // Bounty: bounty hunters only, not Decoy
                if (all.Any(a => a.Type != Story.BountyHunter)) return Refusal.BountyHuntersOnly;
                if (all.Any(Decoy)) return Refusal.DecoyNotAllowed;
                break;
        }
        return null;
    }

    /// <summary>Callback 1 without a target (team-only list): the masks check, then the class team rules.</summary>
    static OrderResult? Type(GameState game, MissionRecord r, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys) =>
        Masks(game, r, [.. team, .. decoys]) ?? TeamRules(game, r, team, decoys);

    /// <summary>
    /// Target part (exact order; owner's views; each step only while no error, except the character Captured rule, which
    /// runs regardless and overwrites). Check block: ObjectiveKey = the owner's view of the
    /// ArgRef; TargetKey = callback 0's result: non-movement classes the side's view of the ArgRef view's location (its own if
    /// Completed, else its BuilderKey's), movement classes the ArgRef view itself. Generic: masks check
    /// (AgentNotAllowed), TargetKey Enroute (TargetEnroute), RefusesDestroyedTarget and TargetKey Destroyed (TargetDestroyed), the objective owner's
    /// relation allowed (own / enemy / neutral, (ObjectiveOwnerRefused)); movement classes stop here; standard ((64, 16) never occurs for
    /// a command): NeedsExploredTarget and TargetKey not Explored (TargetUnexplored), RefusesDestroyedObjective and objective Destroyed
    /// (TargetLocationDestroyed), objective Enroute (ObjectiveEnroute), locations differ (NoTargetLocation), NeedsPopulatedTarget and not Populated (64,
    /// 22); kind: system (ObjectiveNotSystem) and AllowsUprisingSystem / AllowsCalmSystem (NoTargetLocation), character (ObjectiveNotCharacter) and
    /// AllowsCapturedCharacter / AllowsFreeCharacter (64, 42 / 43), unit (ObjectiveNotUnit) / Completed
    /// (ObjectiveNotCompleted), abode (NotAbode); class: diplomacy share 100 (FullSupport), research (64, 44 / 50), recruitment
    /// (RecruitmentDone), the class team rules, fixed targets (21, 48, 47, 46, 49, 40, 37, 41).
    /// </summary>
    static OrderResult? Target(GameState game, MissionRecord r, Owner side, GameObject target, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys)
    {
        var kind = (MissionKind)Family.Of(r.Common.Ref);
        bool movement = kind is >= MissionKind.Move and <= MissionKind.Adrift;
        if (!movement && Knowledge.TargetCheck(game, target, side) is { } location) return location;
        var o = Knowledge.See(game, target, side);
        var key = movement ? target : o.Completed ? o.Location! : Missions.Location(o.BuilderKey!)!; // non-null after TargetCheck
        var t = Knowledge.See(game, key, side);
        OrderResult? err = Masks(game, r, [.. team, .. decoys]);
        void Step(bool fails, OrderResult refusal) { if (err is null && fails) err = refusal; }
        // generic
        Step(t.Enroute, Refusal.TargetEnroute);
        Step(r.RefusesDestroyedTarget && t.Destroyed, Refusal.TargetDestroyed);
        Step(!(o.Owner == side ? r.AllowsOwnObjective : o.Owner == Systems.Opp(side) ? r.AllowsEnemyObjective : r.AllowsNeutralObjective), Refusal.ObjectiveOwnerRefused);
        if (movement) return err;
        // standard
        Step(r.NeedsExploredTarget && key is StarSystem && !t.Explored, Refusal.TargetUnexplored);
        Step(r.RefusesDestroyedObjective && o.Destroyed, Refusal.TargetLocationDestroyed);
        Step(o.Enroute, Refusal.ObjectiveEnroute);
        Step(o.Location != t.Location, Refusal.NoTargetLocation);
        Step(r.NeedsPopulatedTarget && !(key is StarSystem && t.Populated), Refusal.TargetNotPopulated);
        // target kind
        if (kind is >= MissionKind.Diplomacy and <= MissionKind.JediTraining)
        {
            Step(target is not StarSystem, Refusal.ObjectiveNotSystem);
            Step(target is StarSystem && !(o.Uprising ? r.AllowsUprisingSystem : r.AllowsCalmSystem), Refusal.NoTargetLocation);
        }
        if (kind is >= MissionKind.Rescue and <= MissionKind.Bounty)
        {
            Step(target is not Character, Refusal.ObjectiveNotCharacter);
            if (target is Character && !(o.Captured ? r.AllowsCapturedCharacter : r.AllowsFreeCharacter)) err = o.Captured ? Refusal.CapturedCharacterRefused : Refusal.FreeCharacterRefused; // runs regardless
        }
        if (kind is MissionKind.Sabotage or MissionKind.DeathStarSabotage)
        {
            Step(Family.Of(target.Type) is not (>= Families.Troop and < Families.Mission), Refusal.ObjectiveNotUnit);
            Step(!o.Completed, Refusal.ObjectiveNotCompleted);
        }
        if (kind is MissionKind.Dagobah or MissionKind.Vacation or MissionKind.Pickup) Step(Family.Of(target.Type) != Families.Abode, Refusal.NotAbode);
        // class
        Step(kind == MissionKind.Diplomacy && (side == Owner.Alliance ? o.Support : 100 - o.Support) == 100, Refusal.FullSupport);
        if (err is null)
            err = Missions.ResearchRecruitCheck(game, r.Common.Ref, side, target) switch { MissionCompletion.NoResearchFacility => Refusal.NoResearchFacility, MissionCompletion.ResearchExhausted => Refusal.ResearchExhausted, MissionCompletion.RecruitmentDone => Refusal.RecruitmentDone, _ => null };
        err ??= TeamRules(game, r, team, decoys);
        Step(kind == MissionKind.Palace && key.Ref != Story.JabbasPalace, Refusal.NotJabbasPalace);
        Step(kind == MissionKind.Palace && target.Ref != Refs.Han, Refusal.NotHan);
        Step(kind == MissionKind.Dagobah && target.Ref != Story.Dagobah, Refusal.NotDagobah);
        Step(kind == MissionKind.Vacation && target.Ref != Story.Hermitage, Refusal.NotHermitage);
        Step(kind == MissionKind.Pickup && target.Ref != Story.JabbasPalace, Refusal.PickupNotAtPalace);
        Step(kind == MissionKind.Sabotage && DeathStar.IsDeathStar(target.Type), Refusal.SabotageDeathStar);
        Step(kind == MissionKind.Sabotage && target is Character, Refusal.SabotageCharacter);
        Step(kind == MissionKind.DeathStarSabotage && !DeathStar.IsDeathStar(target.Type), Refusal.NotDeathStar);
        return err;
    }

    /// <summary>Event pre-check slot: team resolve ((Missing), (NoFreeAgent)) and common location ((NoLocation), (NotTogether)).</summary>
    internal static OrderResult? PreCheck(GameState game, Owner side, CommandType commandType, IReadOnlyList<GameObject> team, IReadOnlyList<GameObject> decoys) =>
        Team(game, side, commandType, team, decoys, perAgent: false, sides: false);

    /// <summary>
    /// The mission's TargetKey as the side knows it (class callback 0): movement classes the target itself, others
    /// the location of the side's view of the target (its BuilderKey's if not Completed); the callback's own errors ((NoTargetLocation) /
    /// (TargetLocationDestroyed), <see cref="Knowledge.TargetCheck"/>) refuse.
    /// </summary>
    internal static (GameObject? Key, OrderResult? Error) TargetKey(GameState game, uint type, Owner side, GameObject target)
    {
        if (Family.Of(type) is >= (uint)MissionKind.Move and <= (uint)MissionKind.Adrift) return (target, null);
        if (Knowledge.TargetCheck(game, target, side) is { } error) return (null, error);
        var o = Knowledge.See(game, target, side);
        return (o.Completed ? o.Location : o.BuilderKey is { } b ? Missions.Location(b) : null, null);
    }

    /// <summary>Command validation (CreateMission / CreateMoveMission): team steps 1-4 (side), the owner's view of the target (none gives (TargetLocationDestroyed)), then the target chain. Null = pass.</summary>
    public static OrderResult? Validate(GameState game, CommandType commandType, Owner side, uint type, IReadOnlyList<GameObject> team,
        IReadOnlyList<GameObject> decoys, GameObject? target)
    {
        if (Team(game, side, commandType, team, decoys, perAgent: true) is { } fail) return fail;
        if (game.Definitions.Missions.FirstOrDefault(m => m.Common.Ref == type) is not { } r) return Refusal.Missing;
        if (target is null) return Refusal.TargetLocationDestroyed;
        return Target(game, r, side, target, team.Cast<Agent>().ToList(), decoys.Cast<Agent>().ToList());
    }

    /// <summary>Team-only list: the listed types that pass steps 1, 2, 4 and the type's team checks (no per-agent checks, no target).</summary>
    public static List<MissionRecord> TypesForTeam(GameState game, Owner side, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys)
    {
        if (team.Count == 0 || Team(game, side, CommandType.CreateMission, team, decoys, perAgent: false) is not null) return [];
        return [.. game.Definitions.Missions.Where(r => !r.Unlisted && Type(game, r, team, decoys) is null)];
    }

    /// <summary>Team and target list: every listed type with its full validation result (null = passes).</summary>
    public static List<(MissionRecord Type, OrderResult? Error)> TypesForTarget(GameState game, Owner side, IReadOnlyList<Agent> team,
        IReadOnlyList<Agent> decoys, GameObject target) =>
        [.. game.Definitions.Missions.Where(r => !r.Unlisted)
            .Select(r => (r, Validate(game, CommandType.CreateMission, side, r.Common.Ref, team, decoys, target)))];

    /// <summary>
    /// Mission-window order: 1 recruitment 85, 2 Jedi training 88, 3 subdue 87, 4-6 ship /
    /// facility / troop research (family 83 index 32 / 21 / 22), 7 diplomacy 81, 8 incite 86, 9 espionage 82, 10 reconnaissance 84,
    /// 11 assassination 99, 12 abduction 98, 13 rescue 97, 14 Death Star sabotage 106, 15 sabotage 105; any other type 65
    /// (the original counts on from 65; those types are hidden and never listed).
    /// </summary>
    public static int MissionRank(MissionRecord r) => r.Common.Ref switch
    {
        Refs.ShipResearchMission => 4,
        Refs.FacilityResearchMission => 5,
        Refs.TroopResearchMission => 6,
        var x => (MissionKind)Family.Of(x) switch
        {
            MissionKind.Recruitment => 1, MissionKind.JediTraining => 2, MissionKind.SubdueUprising => 3, MissionKind.Diplomacy => 7, MissionKind.InciteUprising => 8, MissionKind.Espionage => 9, MissionKind.Reconnaissance => 10, MissionKind.Assassination => 11, MissionKind.Abduction => 12, MissionKind.Rescue => 13, MissionKind.DeathStarSabotage => 14, MissionKind.Sabotage => 15,
            _ => 65,
        },
    };

    /// <summary>The mission window's list: the team-and-target list's passing types in rank order.</summary>
    public static List<MissionRecord> WindowTypes(GameState game, Owner side, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys, GameObject target) =>
        [.. TypesForTarget(game, side, team, decoys, target).Where(x => x.Error is null).Select(x => x.Type).OrderBy(MissionRank)];
}
