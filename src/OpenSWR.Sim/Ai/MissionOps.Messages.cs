using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

public static partial class MissionOps
{
    // ---- TaskStatus and CompletionStatus messages ----

    /// <summary>The op of a mission, found by its mission key.</summary>
    public static MissionOp? Of(WorldModel m, GameObject mission) => m.Ops.Values.FirstOrDefault(o => o.Mission == mission);

    /// <summary>CompletionStatus message with CompletionStatus 3 to 10: the mission is done, and it moves into the finished set.</summary>
    public static void Completion(WorldModel m, Mission mission)
    {
        if (Of(m, mission) is { } op && mission.CompletionStatus is MissionCompletion.FoiledEnRoute or MissionCompletion.Foiled or MissionCompletion.NoTeam or MissionCompletion.ObjectiveInvalid
            or MissionCompletion.TargetDestroyed or MissionCompletion.ObjectiveOwnerRefused or MissionCompletion.SystemBombarded or MissionCompletion.SystemAssaulted) Done(op);
    }

    static void Done(MissionOp op)
    {
        op.Running = false;
        if (op.Mission is { } mission) op.Finished.Add(mission);
    }

    /// <summary>Counts one failure; above the max failures the gave-up flag is set and the count resets to 0; returns the excess.</summary>
    static bool FailureCount(MissionOp op)
    {
        if (++op.Failures <= op.Class.MaxFailures) return false;
        op.GaveUp = true;
        op.Failures = 0;
        return true;
    }

    /// <summary>
    /// Mission-end cleanup for the op's mission view. The recall, review and mission-running flags are cleared. Team members lose
    /// their on-mission mark (and are dirtied) and stay only while the gave-up flag is clear and they are neither injured nor
    /// captured; with the gave-up flag the staging is dropped, then the flag is cleared. Decoys lose their on-mission mark (and are
    /// dirtied) and leave when injured or captured. The team-complete and decoys-done flags stay (only the picks write them).
    /// </summary>
    public static void MissionEnded(WorldModel m, GameObject mission)
    {
        // openswr bookkeeping: the finished sets drop the mission (its view is destroyed now, which the finished-set reader also tests)
        foreach (var o in m.Ops.Values) o.Finished.Remove(mission);
        if (Of(m, mission) is not { } op) return;
        op.Recall = op.Review = op.Running = false;
        bool fail = op.GaveUp;
        foreach (var (list, all) in new[] { (op.Team, fail), (op.Decoys, false) })
            foreach (var a in list.ToList())
            {
                a.MissionSent = false;
                m.DirtyAgent(a);
                if (!all && !a.Injured && !a.Captured) continue;
                Release(m, op, a);
                list.Remove(a);
            }
        if (fail) op.Staging = null;
        op.GaveUp = false;
    }

    /// <summary>TaskStatus message per class: common, the rescue / abduction / Death Star / recruitment variant, recon / espionage.</summary>
    public static void TaskStatus(GameState game, WorldModel m, Mission mission)
    {
        if (Of(m, mission) is not { } op) return;
        var status = mission.TaskStatus;
        if (op.Class.Object)
        {
            // Espionage and recon with status 3: the target's day is set to today and the failure count resets; abduction, assassination and sabotage use their own handler
            if (status == MissionTaskStatus.Pending) op.Running = true;
            else if (status == MissionTaskStatus.Failed)
            {
                if (op.Class.Family == MissionKind.Espionage) FailureCount(op); // Espionage counts the failure.
                Done(op);
                if (op.Class.Family is not (MissionKind.Espionage or MissionKind.Reconnaissance)) op.Failures++;
            }
            else if (status == MissionTaskStatus.Succeeded)
            {
                if (op.Class.Family is MissionKind.Espionage or MissionKind.Reconnaissance && m.Record(op.Target) is { } tr) tr.BlockadeDay = game.Day;
                op.Failures = 0;
                Done(op);
            }
            return;
        }
        switch (op.Class.Family)
        {
            case MissionKind.Reconnaissance or MissionKind.Espionage:
                if (op.Class.Family == MissionKind.Espionage && status is MissionTaskStatus.Failed or MissionTaskStatus.Succeeded) op.Staging = null; // Espionage also clears the staging system.
                if (status == MissionTaskStatus.Failed) { op.Target = null; Done(op); }
                else if (status == MissionTaskStatus.Succeeded)
                {
                    if (m.Record(op.Target) is { } r) r.BlockadeDay = game.Day;
                    (op.Target, op.Failures) = (null, 0);
                    Done(op);
                }
                break;
            case MissionKind.Rescue or MissionKind.Abduction or MissionKind.DeathStarSabotage or MissionKind.Recruitment:
                if (status == MissionTaskStatus.Pending) op.Running = true;
                else if (status == MissionTaskStatus.Failed) { Done(op); op.Failures++; }
                else if (status == MissionTaskStatus.Succeeded) { Done(op); op.Failures = 0; }
                break;
            default:
                if (status == MissionTaskStatus.Pending) op.Running = true;
                else if (status == MissionTaskStatus.Failed)
                {
                    Done(op);
                    if (FailureCount(op)) op.Recall = true;
                    else op.Review = true;
                }
                else if (status == MissionTaskStatus.Succeeded) { Done(op); op.Failures = 0; op.Review = true; }
                break;
        }
    }
}
