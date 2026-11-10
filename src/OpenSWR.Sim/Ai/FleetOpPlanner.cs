namespace OpenSWR.Sim.Ai;

/// <summary>
/// Planner 8, the fleet-operation manager. State 0 is empty. State 1 creates an operation for a theater sector that has none
/// (when the model's count is below 0 and every operation has the create mark), or removes one (when the count is above 0).
/// State 2 takes the next operation by key cursor, with no wrap. State 3 assigns theaters: keep the current one; else the
/// nearest sector needing an operation; else random keys over theaters without one, then over all theaters. The sector is
/// bound to the operation, and the task is of the theater kind. State 4 runs the operation's produce (exits as planner 9
/// state 3).
/// </summary>
public sealed class FleetOpPlanner() : Planner(PlannerType.FleetOperation)
{
    int _current, _next;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        switch ((FleetOpPlannerState)State)
        {
            case FleetOpPlannerState.Start:
                State = (int)FleetOpPlannerState.Balance;
                return null;
            case FleetOpPlannerState.Balance:
            {
                int total = m.TheaterBalance;
                if (total < 0 && m.FleetOps.Values.All(o => o.CharacterWaiting))
                {
                    var op = new FleetOp(m.NextFleetOpKey++);
                    m.FleetOps[op.Key] = op;
                }
                else if (total > 0 && (m.FleetOps.Values.FirstOrDefault(o => o.Theater is null) ?? m.FleetOps.Values.FirstOrDefault(o => o.Theater!.TheaterBalance > 0)) is { } gone)
                {
                    FleetOps.Reset(m, gone);
                    m.FleetOps.Remove(gone.Key);
                }
                State = (int)FleetOpPlannerState.Next;
                return null;
            }
            case FleetOpPlannerState.Next:
            {
                var op = m.FleetOps.Values.FirstOrDefault(o => _next != 0 && o.Key == _next) ?? m.FleetOps.Values.FirstOrDefault();
                if (op is null) { (State, Done, _next) = ((int)FleetOpPlannerState.Start, true, 0); return null; }
                _current = op.Key;
                _next = m.FleetOps.Keys.Where(k => k > op.Key).DefaultIfEmpty(0).Min();
                State = (int)FleetOpPlannerState.Assign;
                return null;
            }
            case FleetOpPlannerState.Assign:
            {
                if (!m.FleetOps.TryGetValue(_current, out var op) || !Assign(m, op)) { (State, Done) = ((int)FleetOpPlannerState.Start, true); return null; }
                State = (int)FleetOpPlannerState.Produce;
                return null;
            }
            default: // Produce: the operation's produce
            {
                var (c, finished) = m.FleetOps.TryGetValue(_current, out var op) ? FleetOps.Produce(game, m, op, controller.Side) : (null, true);
                if (finished) (State, Done) = ((int)FleetOpPlannerState.Start, true);
                else if (c is not null) { Priority++; Done = true; }
                return c;
            }
        }
    }

    /// <summary>
    /// Theater pick (planner 8): keep the current one; else, for a sector theater, the nearest pick by distance after a reset,
    /// falling through to the random lists when none; else the random lists, first with one set of requirement flags, then with
    /// another. Every success ends in <see cref="Rebind"/>.
    /// </summary>
    static bool Assign(WorldModel m, FleetOp op)
    {
        if (op.Theater is { } t && t.TheaterBalance == 0 && t.FleetOpKey == op.Key) return Rebind(m, op);
        AiSector? pick = null;
        if (op.Theater is { } old)
        {
            var stack = new ListStack();
            stack.Push(m.SectorDistanceFrom(old, x => x.OpenTheater, RankOrder.Descending));
            pick = stack.Pick() as AiSector;
            FleetOps.Reset(m, op);
        }
        if (pick is null)
        {
            var stack = new ListStack();
            stack.Push(m.SectorRandomList(x => x.OpenTheater && x.TheaterMark, RankOrder.Descending));
            pick = stack.Pick() as AiSector;
            if (pick is null)
            {
                var all = new ListStack();
                all.Push(m.SectorRandomList(x => x.OpenTheater, RankOrder.Descending));
                pick = all.Pick() as AiSector;
            }
        }
        if (pick is null) return false;
        // Bind: a pick that is already taken leaves no theater: push 0, reset, then return false.
        if (pick.FleetOpKey != 0)
        {
            FleetOps.PushEntry(m, op, 0);
            return Rebind(m, op);
        }
        // Binds the sector to the op key (dirtying the sector) and sets the op's entry; the theater's entry is pushed at once.
        pick.FleetOpKey = op.Key;
        m.DirtySector(pick);
        op.Theater = pick;
        FleetOps.PushEntry(m, op, pick.Entry);
        return Rebind(m, op);
    }

    /// <summary>
    /// Reset. The theater is the op's theater. A task of another kind than the theater's kind is deleted. With no task, the op's
    /// two counters are zeroed, the produce state is 0, refresh is off, and a new task of the theater's kind (1-6) is made; the
    /// theater's entry is pushed when it differs from the op's entry. Still no task: reset, fail.
    /// </summary>
    static bool Rebind(WorldModel m, FleetOp op)
    {
        if (op.Theater is not { } s) { FleetOps.Reset(m, op); return false; }
        var kind = s.TheaterKind;
        if (op.Task is { } task && task.Type != kind) op.Task = null;
        if (op.Task is null)
        {
            op.ClearStatus();
            (op.State, op.Refresh) = ((int)FleetOpState.Start, false);
            if (kind != FleetTaskType.None) op.Task = new FleetTask(kind);
        }
        FleetOps.PushEntry(m, op, s.Entry);
        if (op.Task is not null) return true;
        FleetOps.Reset(m, op);
        return false;
    }
}
