using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

public static partial class MissionOps
{
    // ---- object-kind produce ----

    static (Command?, bool) ProduceObject(GameState game, WorldModel m, MissionOp op)
    {
        switch ((ObjectMissionOpState)op.State)
        {
            case ObjectMissionOpState.Start:
                op.State = (int)ObjectMissionOpState.Check;
                return (null, false);
            case ObjectMissionOpState.Check:
                if (op.Recall || op.Review) return End(op, Recall(m, op));
                if (op.Retired) { CleanUp(m, op); return End(op, null); }
                op.State = (int)ObjectMissionOpState.Target;
                return (null, false);
            case ObjectMissionOpState.Target:
                op.State = (int)(ObjectTarget(game, m, op) ? ObjectMissionOpState.Staging : ObjectMissionOpState.Release);
                return (null, false);
            case ObjectMissionOpState.Staging:
                op.State = (int)(ObjectStaging(m, op) ? ObjectMissionOpState.Recheck : ObjectMissionOpState.Release);
                return (null, false);
            case ObjectMissionOpState.Recheck:
                Recheck(m, op);
                op.State = (int)ObjectMissionOpState.TeamPick;
                return (null, false);
            case ObjectMissionOpState.TeamPick:
                TeamPick(m, op);
                op.State = (int)ObjectMissionOpState.DecoyPick;
                return (null, false);
            case ObjectMissionOpState.DecoyPick:
                DecoyPick(m, op);
                op.State = (int)ObjectMissionOpState.Moves;
                return (null, false);
            case ObjectMissionOpState.Moves:
            {
                var move = ObjectMoves(m, op);
                if (op.AtStaging) { op.State = (int)ObjectMissionOpState.Mission; return (move, false); }
                return End(op, move);
            }
            case ObjectMissionOpState.Mission:
                return End(op, Mission(m, op));
            default: // Release
                if (!(op.Recall || op.Review || op.Running))
                {
                    foreach (var a in op.Team.Concat(op.Decoys)) Release(m, op, a);
                    op.Team.Clear();
                    op.Decoys.Clear(); // The two flags stay untouched.
                }
                return End(op, null);
        }
    }

    /// <summary>
    /// Clears the objective and target, and the target-found flag. With an owning fleet op, the task picker is asked by mission
    /// type (sabotage, espionage, recon, else abduction and assassination), which selects the task's
    /// field. The target-found flag is set when the objective has a family from 1 to 254 and the target is a system.
    /// </summary>
    static bool ObjectTarget(GameState game, WorldModel m, MissionOp op)
    {
        (op.Objective, op.Target) = (null, null);
        op.TargetFound = false;
        if (op.FleetOp is not { Task: { } task, Theater: { } s }) return false;
        var (target, objective) = op.Class.MissionType switch
        {
            Refs.SabotageMission => SabotageTarget(m, task, s),
            Refs.EspionageMission => (Pick(m.SystemDaysList(s, r => r.Claimed && r.Enemy, RankOrder.Ascending)) ?? (task.Type is FleetTaskType.Reserve or FleetTaskType.Site or FleetTaskType.Defence or FleetTaskType.Assault ? Pick(m.SystemRandomList(s, r => r.Claimed, RankOrder.Ascending)) : null)) is { } e
                ? (e, (GameObject?)e.System) : (null, null),
            Refs.ReconnaissanceMission => (Pick(m.SystemDaysList(s, r => r.NotOwn && !r.Explored, RankOrder.Ascending)) ?? Pick(m.SystemDaysList(s, r => r.NotOwn, RankOrder.Ascending))) is { } r
                ? (r, (GameObject?)r.System) : (null, null),
            _ => CharacterTarget(m, task, s),
        };
        (op.Target, op.Objective) = (target?.System, objective);
        if (objective is not null && Family.Of(objective.Type) is >= 1 and <= 254 && target is not null) op.TargetFound = true;
        return op.TargetFound;
    }

    /// <summary>
    /// Task picks for the object (task slot). Type 4 first keeps its site when its record has the site flags (no stage, no owner
    /// test). Types 3 to 6 then try stages in order, each only while no system was found: first a stage with two flags, then
    /// another stage with two other flags, and then each single flag in turn. Each pick uses the object's value (direction 2); type 3
    /// picks its first stage by random keys. The first system found goes to the candidate picker with its stage's mask; an empty
    /// candidate set does not fall through.
    /// </summary>
    static (AiSystem?, GameObject?) CharacterTarget(WorldModel m, FleetTask task, AiSector s)
    {
        var type = task.Type;
        if (type == FleetTaskType.Site && m.Record(task.Site) is { } site && (site.EnemyCharacter || site.EnemyCharacterAboard)) return (site, Characters(m, site, true, a => a.Enemy));
        var stages = new List<(Func<AiSystem?> System, bool Here, Func<AiAgent, bool> Kind)>();
        if (type is FleetTaskType.Reserve or FleetTaskType.Site or FleetTaskType.Defence or FleetTaskType.Assault)
        {
            stages.Add((() => type == FleetTaskType.Reserve ? Pick(m.SystemRandomList(s, r => r.EnemyCharacter && r.EnemyCommander, RankOrder.Ascending)) : Pick(m.SystemList(s, r => r.EnemyCharacter && r.EnemyCommander, r => r.EnemyUnits, RankOrder.Descending)),
                true, type == FleetTaskType.Site ? a => a.HasCommandKind : a => a.Enemy));
            stages.Add((() => Pick(m.SystemList(s, r => r.EnemyCharacterAboard && r.EnemyCommander, r => r.EnemyUnits, RankOrder.Descending)), false, a => a.HasCommandKind));
        }
        stages.Add((() => Pick(m.SystemList(s, r => r.EnemyCharacter, r => r.EnemyUnits, RankOrder.Descending)), true, a => a.Enemy));
        stages.Add((() => Pick(m.SystemList(s, r => r.EnemyCharacterAboard, r => r.EnemyUnits, RankOrder.Descending)), false, a => a.Enemy));
        foreach (var (system, here, kind) in stages)
            if (system() is { } r) return (r, Characters(m, r, here, kind));
        return (null, null);
    }

    /// <summary>
    /// Candidate picker. With <paramref name="here"/>, the characters directly in the side's view of the system; with an enemy
    /// character aboard there, every usable fleet view there whose fleet record is not own and has an enemy character aboard, and the
    /// characters aboard its ship views. Kept characters: an enemy character of the stage's kind, not Enroute (captured is not
    /// tested); one random draw over the list.
    /// </summary>
    static GameObject? Characters(WorldModel m, AiSystem r, bool here, Func<AiAgent, bool> kind)
    {
        bool Fits(GameObject x) => Family.Of(x.Type) is >= Families.MajorCharacter and < Families.Mission && m.Agents.TryGetValue(x.Ref, out var a) && a.Character
            && a.Enemy && kind(a) && !a.Enroute;
        var list = new List<GameObject>();
        if (here) list.AddRange(m.Contents(r.System).Where(Fits));
        if (r.EnemyCharacterAboard)
            foreach (var fv in m.Contents(r.System).Where(x => x is Fleet && m.Usable(x)))
                if (m.FleetsById.GetValueOrDefault(fv.Ref) is { } f && !f.Own && (f.EnemyMajor || f.EnemyMinor))
                    foreach (var ship in m.Contents(fv)) list.AddRange(m.Contents(ship).Where(Fits));
        if (list.Count == 0) return null;
        int i = m.Rand(list.Count); // rand(n) can return n: no pick then (Inferred, as the guarded picks, e.g. WorldModel.PickProduct)
        return i < list.Count ? list[i] : null;
    }

    /// <summary>
    /// Task slot for the object per task type (types 1 to 6): stages (a flag, plus an optional requirement) by the object's value
    /// (direction 2), each only while no system was found, taking its top system; the stage flag is the mask the candidate step uses.
    /// Type 4 first keeps its site (task field) when it is not own and has the mark (objective = the site system); its first stage
    /// then still runs and a find replaces it (Inferred: an empty pick keeps the site).
    /// </summary>
    static (AiSystem?, GameObject?) SabotageTarget(WorldModel m, FleetTask task, AiSector s)
    {
        (SabotageKind, Func<AiSystem, bool>)[] hq = m.Side == Owner.Empire ? [(SabotageKind.Headquarters, r => r.HeadquartersSeen && r.Headquarters)] : [];
        (SabotageKind, Func<AiSystem, bool>) gencores = (SabotageKind.GenCores, r => r.EnemyGenCores && r.ManyGenCores),
            fleet = (SabotageKind.Fleet, r => r.EnemyFleetSeen && r.EnemyFleet), shields = (SabotageKind.DeathStarShield, r => r.EnemyDeathStarShield),
            batteries = (SabotageKind.Batteries, r => r.EnemyBatteries), anyGenCore = (SabotageKind.GenCores, r => r.EnemyGenCores),
            troops = (SabotageKind.Troops, r => r.EnemyTroops), fighters = (SabotageKind.Fighters, r => r.EnemyFighters),
            producers = (SabotageKind.Producers, r => r.EnemyProducers), mines = (SabotageKind.Mines, r => r.EnemyMines);
        (SabotageKind Kind, Func<AiSystem, bool> Pass)[] stages = task.Type switch
        {
            FleetTaskType.Strike => [.. hq, producers, mines, troops, fleet, fighters],
            FleetTaskType.Scout => [fighters, fleet, producers],
            FleetTaskType.Reserve => [gencores, shields, batteries, anyGenCore, troops, producers, fighters, fleet],
            FleetTaskType.Site or FleetTaskType.Defence => [gencores, shields, batteries, anyGenCore, troops, fighters, fleet],
            _ => [gencores, .. hq, shields, batteries, anyGenCore, troops, fighters, fleet],
        };
        AiSystem? kept = task.Type == FleetTaskType.Site && m.Record(task.Site) is { } site && !site.Own && site.ManyGenCores ? site : null;
        for (int k = 0; k < stages.Length; k++)
        {
            if (kept is not null && k > 0) break;
            var (kind, pass) = stages[k];
            if (Pick(m.SystemList(s, pass, r => r.EnemyUnits, RankOrder.Descending)) is { } r)
                return (r, SabotageObject(m, r, kind));
        }
        return kept is null ? (null, null) : (kept, kept.System);
    }

    /// <summary>
    /// Candidates of the class from the side's view of the system: usable iterators only, non-own (Inferred): HQ, generation cores,
    /// Death Star shields, the LNR (family 35) then KDY (family 34) batteries, troops, fighters, the ships of every non-own fleet record,
    /// training facilities, shipyards and construction yards, then mines and the first refinery. One random draw over them.
    /// </summary>
    /// <summary>What a sabotage stage looks for.</summary>
    enum SabotageKind { Headquarters, GenCores, DeathStarShield, Batteries, Troops, Fighters, Fleet, Producers, Mines }

    static GameObject? SabotageObject(WorldModel m, AiSystem r, SabotageKind kind)
    {
        var contents = m.Contents(r.System).Where(x => m.Usable(x) && m.See(x).Owner != m.Side).ToList();
        IEnumerable<GameObject> Of(params uint[] families) => families.SelectMany(f => contents.Where(x => Family.Of(x.Type) == f));
        List<GameObject> list = kind switch
        {
            SabotageKind.Headquarters => [.. Of(Families.Headquarters, Families.SecondHeadquarters)],
            SabotageKind.GenCores => [.. Of(Families.PlanetaryShield)],
            SabotageKind.DeathStarShield => [.. Of(Families.DeathStarShield)],
            SabotageKind.Batteries => [.. Of(Families.Gun, Families.IonCannon)],
            SabotageKind.Troops => [.. contents.Where(x => Family.IsTroop(x.Type))],
            SabotageKind.Fighters => [.. contents.Where(x => Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters)],
            SabotageKind.Fleet => [.. contents.Where(x => x is Fleet && m.FleetsById.GetValueOrDefault(x.Ref) is { } f && !f.Own)
                .SelectMany(m.Contents).Where(x => Family.IsCapitalShip(x.Type) && m.Usable(x))],
            SabotageKind.Producers => [.. Of(Families.TrainingFacility, Families.Shipyard, Families.ConstructionYard)],
            _ => [.. Of(Families.Mine), .. Of(Families.Refinery).Take(1)], // All mines, only the first refinery (a quirk of the original).
        };
        if (list.Count == 0) return null;
        int i = m.Rand(list.Count); // rand(n) can return n: no pick then (Inferred, as the guarded picks, e.g. WorldModel.PickProduct)
        return i < list.Count ? list[i] : null;
    }

    static bool OwnCalm(AiSystem r) => r.Own && !r.Unrest;

    /// <summary>
    /// Staging for the op, within its theater S. A staging system in S is kept while own and calm. Outside S, a staging system is
    /// kept only while the outside-theater flag is set and it is own and calm. Else the first team member's system in S that is
    /// own and calm; else (outside-theater flag cleared) the nearest own system in S that is not blockaded; else the nearest search
    /// over S for own systems without the mark, with the outside-theater flag set. Returns the staging-found flag.
    /// </summary>
    static bool ObjectStaging(WorldModel m, MissionOp op)
    {
        op.StagingFound = false;
        if (op.Theater is not { } s) return false;
        if (m.Record(op.Staging) is { } kept)
        {
            if (kept.Sector == s ? OwnCalm(kept) : op.OutsideTheater && OwnCalm(kept))
            {
                op.StagingFound = true;
                if (kept.Sector == s) op.OutsideTheater = false;
                return true;
            }
            op.Staging = null;
        }
        foreach (var a in op.Team)
            if (m.Record(m.Where(a.Agent)) is { } r && r.Sector == s && OwnCalm(r))
            {
                op.Staging = r.System;
                (op.StagingFound, op.OutsideTheater) = (true, false);
                return true;
            }
        op.OutsideTheater = false;
        if (Pick(m.SystemRandomList(s, r => r.Own && !r.Blockade, RankOrder.Ascending)) is { } pick) { op.Staging = pick.System; op.StagingFound = true; return true; }
        if (Nearest(m, s, r => r.Own && !r.EnemyFleet) is { } near) { op.Staging = near.System; op.StagingFound = op.OutsideTheater = true; return true; }
        return false;
    }

    /// <summary>
    /// Every movable team member, then decoy, not at staging (agent location) goes into one move command to the staging system.
    /// The staging-found flag is set only when every member with a record is movable and at staging, the team is non-empty, and
    /// the team-complete and decoys-done flags are set.
    /// </summary>
    static Command? ObjectMoves(WorldModel m, MissionOp op)
    {
        op.AtStaging = false;
        var members = op.Team.Concat(op.Decoys)
            .Select(a => m.Agents.GetValueOrDefault(a.Agent.Ref)).OfType<AiAgent>().ToList(); // By key; no record: skipped.
        var go = members.Where(a => MovableMember(a) && a.Location != op.Staging).Select(a => (GameObject)a.Agent).ToList();
        if (op.Team.Count > 0 && op.TeamComplete && op.DecoysDone && members.All(a => MovableMember(a) && a.Location == op.Staging)) op.AtStaging = true;
        return go.Count == 0 || op.Staging is null ? null : new Command(CommandType.Move, op.Side) { Targets = [.. go.Select(g => (g, GroupMode.Object))], ArgRef = op.Staging };
    }
}
