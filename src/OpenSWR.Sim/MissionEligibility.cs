using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Mission validation for commands 0x240 / 0x241 and the UI lists: team checks (resolve, common location, per agent, side), the
/// owner's view of the target, then the class chain generic, standard, target kind, class (all target fields from the
/// owner's views; first error wins, except the character Captured rule). Queries: <see cref="TypesForTeam"/> and
/// <see cref="TypesForTarget"/>.
/// Inferred: a non-agent in the team gives (1, 1) (class refuses the type); a location that is not a system or abode gives (0x40, 3).
/// </summary>
public static class MissionEligibility
{
    static OrderResult E(int category, int code) => new(category, code);

    /// <summary>
    /// Team checks 1-4: resolve (a non-captured Team member, else (0x40, 0x91)); common location (parent chain
    /// to the first system or abode; no member with one (1, 0x16); differing (0x40, 3)); per agent (with <paramref name="perAgent"/>: base order check, OnMission (0x30, 1), for 0x240
    /// Injury (0x30, 3) and Captured (0x30, 2)); sides (both (1, 0x14), none (1, 0x16)). Null = pass.
    /// </summary>
    static OrderResult? Team(GameState game, Owner side, int commandType, IReadOnlyList<GameObject> team, IReadOnlyList<GameObject> decoys, bool perAgent, bool sides = true)
    {
        if (team.Concat(decoys).Any(o => o is not Agent)) return E(1, 1); // class refuses the type (Inferred)
        if (team.Concat(decoys).Any(o => o.Destroyed)) return E(1, 0x12); // team resolve check
        var agents = team.Cast<Agent>().ToList();
        if (!agents.Any(a => a is not Character { Captured: true })) return E(0x40, 0x91);
        var all = agents.Concat(decoys.Cast<Agent>()).ToList();
        // locations (parent chain up to the first system or abode); none gives (1, 0x16); differing gives (0x40, 3)
        if (all.All(a => Missions.Location(a) is null)) return E(1, 0x16);
        var at = Missions.Location(all[0]);
        if (at is null || all.Any(a => Missions.Location(a) != at)) return E(0x40, 3);
        if (perAgent)
            foreach (var a in all)
            {
                var check = a.Owner is not (Owner.Alliance or Owner.Empire) || a.Owner != side ? E(1, 0x14)
                    : !a.Completed && commandType != 0x241 ? E(1, 0x10)
                    : a.Destroyed ? E(1, 0x12) : a.Enroute ? E(1, 0x11) : (OrderResult?)null;
                if (check is not null) return check;
                if (a.OnMission) return E(0x30, 1);
                if (commandType == 0x240 && a is Character c)
                {
                    if (c.Injury != 0) return E(0x30, 3);
                    if (c.Captured) return E(0x30, 2);
                }
            }
        if (!sides) return null;
        bool alliance = all.Any(a => a.Owner == Owner.Alliance), empire = all.Any(a => a.Owner == Owner.Empire);
        return alliance && empire ? E(1, 0x14) : !alliance && !empire ? E(1, 0x16) : null;
    }

    /// <summary>Masks and side check, part of the generic step: every special force's MissionMask allowed by the record, characters allowed (AllowsCharacters) when any joins, the side's flag set; else (0x40, 1).</summary>
    static OrderResult? Masks(GameState game, MissionRecord r, IReadOnlyList<Agent> all)
    {
        bool forces = all.Where(a => Family.IsSpecialForce(a.Type))
            .All(a => r.AllowsSpecialForce(game.Definitions.SpecialForces.FirstOrDefault(s => s.Common.Ref == a.Type)?.MissionMask ?? 0));
        if (!forces || all.Any(a => a is Character) && !r.AllowsCharacters) return E(0x40, 1);
        return (all[0].Owner == Owner.Alliance ? r.Alliance : r.Empire) == 0 ? E(0x40, 1) : null;
    }

    /// <summary>Class team rules: research skill (0x40, 4), recruitment majors (0x40, 6), Jedi training, story types.</summary>
    static OrderResult? TeamRules(GameState game, MissionRecord r, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys)
    {
        var all = team.Concat(decoys).ToList();
        uint kind = Family.Of(r.Common.Ref);
        bool Decoy(Agent a) => decoys.Contains(a);
        switch (kind)
        {
            case 0x53: // Research: every member has a nonzero skill of the kind
                int skill = r.Common.Ref switch { 0x53000020 => SkillIndex.ShipDesign, 0x53000021 => SkillIndex.FacilityDesign, _ => SkillIndex.TroopTraining };
                if (all.Any(a => a.Skills[skill] == 0)) return E(0x40, 4);
                break;
            case 0x55 when all.Any(a => Family.Of(a.Type) is not (>= 0x30 and < 0x38)): return E(0x40, 6); // majors only
            case 0x58: // Jedi training
                var chars = team.OfType<Character>().ToList();
                var teacher = chars.FirstOrDefault(c => c.Definition.IsJediTrainer != 0 && c.Force >= game.Params.General(0xA15));
                if (teacher is null) return E(0x40, 0x90);
                if (chars.Any(c => c.Force == 0)) return E(0x40, 5);
                if (chars.Any(c => c != teacher && c.Force >= teacher.Force)) return E(0x40, 0xD);
                if (team.Count < 2) return E(0x40, 0x94);
                break;
            case 0x64: // Palace: Luke / Leia / Chewbacca, not Captured, not Decoy
                if (all.Any(a => a.Ref is not (Refs.Luke or Refs.Leia or Refs.Chewbacca))) return E(0x40, 10);
                if (all.Any(a => a is Character { Captured: true })) return E(0x40, 9);
                if (all.Any(Decoy)) return E(0x40, 8);
                break;
            case 0x71 or 0x72 or 0x73: // Dagobah / Vacation: Luke only; Pickup: Vader only
                if (all.Any(a => Family.Of(a.Type) != (kind == 0x73 ? 0x35u : 0x32u))) return E(0x40, kind == 0x73 ? 0xC : 7);
                if (all.Any(a => a is Character { Captured: true })) return E(0x40, 9);
                if (all.Any(Decoy)) return E(0x40, 8);
                break;
            case 0x65: // Bounty: bounty hunters only, not Decoy
                if (all.Any(a => a.Type != Story.BountyHunter)) return E(0x40, 0xB);
                if (all.Any(Decoy)) return E(0x40, 8);
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
    /// (0x40, 1), TargetKey Enroute (0x40, 0x13), RefusesDestroyedTarget and TargetKey Destroyed (0x40, 0x12), the objective owner's
    /// relation allowed (own / enemy / neutral, (0x40, 0x23)); movement classes stop here; standard ((0x40, 0x10) never occurs for
    /// a command): NeedsExploredTarget and TargetKey not Explored (0x40, 0x11), RefusesDestroyedObjective and objective Destroyed
    /// (0x40, 0x21), objective Enroute (0x40, 0x22), locations differ (0x40, 0x20), NeedsPopulatedTarget and not Populated (0x40,
    /// 0x16); kind: system (0x40, 0x24) and AllowsUprisingSystem / AllowsCalmSystem (0x40, 0x20), character (0x40, 0x26) and
    /// AllowsCapturedCharacter / AllowsFreeCharacter (0x40, 0x2A / 0x2B), unit (0x40, 0x27) / Completed
    /// (0x40, 0x34), abode (0x40, 0x2D); class: diplomacy share 100 (0x40, 0x33), research (0x40, 0x2C / 0x32), recruitment
    /// (0x40, 0x35), the class team rules, fixed targets (0x15, 0x30, 0x2F, 0x2E, 0x31, 0x28, 0x25, 0x29).
    /// </summary>
    static OrderResult? Target(GameState game, MissionRecord r, Owner side, GameObject target, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys)
    {
        uint kind = Family.Of(r.Common.Ref);
        bool movement = kind is >= 0x41 and <= 0x44;
        if (!movement && Knowledge.TargetCheck(game, target, side) is { } location) return location;
        var o = Knowledge.See(game, target, side);
        var key = movement ? target : o.Completed ? o.Location! : Missions.Location(o.BuilderKey!)!; // non-null after TargetCheck
        var t = Knowledge.See(game, key, side);
        OrderResult? err = Masks(game, r, [.. team, .. decoys]);
        void Step(bool fails, int code) { if (err is null && fails) err = E(0x40, code); }
        // generic
        Step(t.Enroute, 0x13);
        Step(r.RefusesDestroyedTarget && t.Destroyed, 0x12);
        Step(!(o.Owner == side ? r.AllowsOwnObjective : o.Owner == Systems.Opp(side) ? r.AllowsEnemyObjective : r.AllowsNeutralObjective), 0x23);
        if (movement) return err;
        // standard
        Step(r.NeedsExploredTarget && key is StarSystem && !t.Explored, 0x11);
        Step(r.RefusesDestroyedObjective && o.Destroyed, 0x21);
        Step(o.Enroute, 0x22);
        Step(o.Location != t.Location, 0x20);
        Step(r.NeedsPopulatedTarget && !(key is StarSystem && t.Populated), 0x16);
        // target kind
        if (kind is >= 0x51 and <= 0x58)
        {
            Step(target is not StarSystem, 0x24);
            Step(target is StarSystem && !(o.Uprising ? r.AllowsUprisingSystem : r.AllowsCalmSystem), 0x20);
        }
        if (kind is >= 0x61 and <= 0x65)
        {
            Step(target is not Character, 0x26);
            if (target is Character && !(o.Captured ? r.AllowsCapturedCharacter : r.AllowsFreeCharacter)) err = E(0x40, o.Captured ? 0x2A : 0x2B); // runs regardless
        }
        if (kind is 0x69 or 0x6A)
        {
            Step(Family.Of(target.Type) is not (>= 0x10 and < 0x40), 0x27);
            Step(!o.Completed, 0x34);
        }
        if (kind is 0x71 or 0x72 or 0x73) Step(Family.Of(target.Type) != 0x98, 0x2D);
        // class
        Step(kind == 0x51 && (side == Owner.Alliance ? o.Support : 100 - o.Support) == 100, 0x33);
        if (err is null)
            err = Missions.ResearchRecruitCheck(game, r.Common.Ref, side, target) switch { 0xC => E(0x40, 0x2C), 0xE => E(0x40, 0x32), 0x10 => E(0x40, 0x35), _ => null };
        err ??= TeamRules(game, r, team, decoys);
        Step(kind == 0x64 && key.Ref != Story.JabbasPalace, 0x15);
        Step(kind == 0x64 && target.Ref != Refs.Han, 0x30);
        Step(kind == 0x71 && target.Ref != Story.Dagobah, 0x2F);
        Step(kind == 0x72 && target.Ref != Story.Hermitage, 0x2E);
        Step(kind == 0x73 && target.Ref != Story.JabbasPalace, 0x31);
        Step(kind == 0x69 && DeathStar.IsDeathStar(target.Type), 0x28);
        Step(kind == 0x69 && target is Character, 0x25);
        Step(kind == 0x6A && !DeathStar.IsDeathStar(target.Type), 0x29);
        return err;
    }

    /// <summary>Event pre-check slot: team resolve ((1, 0x12), (0x40, 0x91)) and common location ((1, 0x16), (0x40, 3)).</summary>
    internal static OrderResult? PreCheck(GameState game, Owner side, int commandType, IReadOnlyList<GameObject> team, IReadOnlyList<GameObject> decoys) =>
        Team(game, side, commandType, team, decoys, perAgent: false, sides: false);

    /// <summary>
    /// The mission's TargetKey as the side knows it (class callback 0): movement classes the target itself, others
    /// the location of the side's view of the target (its BuilderKey's if not Completed); the callback's own errors ((0x40, 0x20) /
    /// (0x40, 0x21), <see cref="Knowledge.TargetCheck"/>) refuse.
    /// </summary>
    internal static (GameObject? Key, OrderResult? Error) TargetKey(GameState game, uint type, Owner side, GameObject target)
    {
        if (Family.Of(type) is >= 0x41 and <= 0x44) return (target, null);
        if (Knowledge.TargetCheck(game, target, side) is { } error) return (null, error);
        var o = Knowledge.See(game, target, side);
        return (o.Completed ? o.Location : o.BuilderKey is { } b ? Missions.Location(b) : null, null);
    }

    /// <summary>Command validation (0x240 / 0x241): team steps 1-4 (side), the owner's view of the target (none gives (0x40, 0x21)), then the target chain. Null = pass.</summary>
    public static OrderResult? Validate(GameState game, int commandType, Owner side, uint type, IReadOnlyList<GameObject> team,
        IReadOnlyList<GameObject> decoys, GameObject? target)
    {
        if (Team(game, side, commandType, team, decoys, perAgent: true) is { } fail) return fail;
        if (game.Definitions.Missions.FirstOrDefault(m => m.Common.Ref == type) is not { } r) return E(1, 0x12);
        if (target is null) return E(0x40, 0x21);
        return Target(game, r, side, target, team.Cast<Agent>().ToList(), decoys.Cast<Agent>().ToList());
    }

    /// <summary>Team-only list: the listed types that pass steps 1, 2, 4 and the type's team checks (no per-agent checks, no target).</summary>
    public static List<MissionRecord> TypesForTeam(GameState game, Owner side, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys)
    {
        if (team.Count == 0 || Team(game, side, 0x240, team, decoys, perAgent: false) is not null) return [];
        return [.. game.Definitions.Missions.Where(r => !r.Unlisted && Type(game, r, team, decoys) is null)];
    }

    /// <summary>Team and target list: every listed type with its full validation result (null = passes).</summary>
    public static List<(MissionRecord Type, OrderResult? Error)> TypesForTarget(GameState game, Owner side, IReadOnlyList<Agent> team,
        IReadOnlyList<Agent> decoys, GameObject target) =>
        [.. game.Definitions.Missions.Where(r => !r.Unlisted)
            .Select(r => (r, Validate(game, 0x240, side, r.Common.Ref, team, decoys, target)))];

    /// <summary>
    /// Mission-window order: 1 recruitment 0x55, 2 Jedi training 0x58, 3 subdue 0x57, 4-6 ship /
    /// facility / troop research (0x53000020 / 21 / 22), 7 diplomacy 0x51, 8 incite 0x56, 9 espionage 0x52, 10 reconnaissance 0x54,
    /// 11 assassination 0x63, 12 abduction 0x62, 13 rescue 0x61, 14 Death Star sabotage 0x6A, 15 sabotage 0x69; any other type 65
    /// (the original counts on from 65; those types are hidden and never listed).
    /// </summary>
    public static int MissionRank(MissionRecord r) => r.Common.Ref switch
    {
        0x53000020 => 4,
        0x53000021 => 5,
        0x53000022 => 6,
        var x => Family.Of(x) switch
        {
            0x55 => 1, 0x58 => 2, 0x57 => 3, 0x51 => 7, 0x56 => 8, 0x52 => 9, 0x54 => 10, 0x63 => 11, 0x62 => 12, 0x61 => 13, 0x6A => 14, 0x69 => 15,
            _ => 65,
        },
    };

    /// <summary>The mission window's list: the team-and-target list's passing types in rank order.</summary>
    public static List<MissionRecord> WindowTypes(GameState game, Owner side, IReadOnlyList<Agent> team, IReadOnlyList<Agent> decoys, GameObject target) =>
        [.. TypesForTarget(game, side, team, decoys, target).Where(x => x.Error is null).Select(x => x.Type).OrderBy(MissionRank)];
}
