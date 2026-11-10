namespace OpenSWR.Sim.Ai;

/// <summary>
/// Planner 9, fleet tasks. Any other state moves to 2 (an empty call). State 2 picks the task with the saved next key, or else
/// the first in key order, unconditionally; the next key becomes the following key, with 0 at the end and no wrap. State 3 runs
/// the task's produce, one state per call. A command that is not finished raises the priority by one and keeps the state, and
/// the call is done. A finished task moves to state 0, and the call is done. With nothing, the state stays and the call is not done.
/// </summary>
public sealed class FleetTaskPlanner() : Planner(PlannerType.FleetTask)
{
    int _current, _next;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        switch ((FleetTaskPlannerState)State)
        {
            case FleetTaskPlannerState.Pick:
            {
                var task = m.Tasks.FirstOrDefault(t => _next != 0 && t.Key == _next) ?? m.Tasks.FirstOrDefault();
                if (task is null) { (State, Done, _next) = ((int)FleetTaskPlannerState.Start, true, 0); return null; }
                _current = task.Key;
                _next = m.Tasks.Where(t => t.Key > task.Key).Select(t => t.Key).DefaultIfEmpty(0).Min();
                State = (int)FleetTaskPlannerState.Produce;
                return null;
            }
            case FleetTaskPlannerState.Produce:
            {
                var task = m.Tasks.FirstOrDefault(t => t.Key == _current);
                var (c, finished) = task is null ? (null, true) : task.Produce(m, controller.Side);
                if (finished) (State, Done) = ((int)FleetTaskPlannerState.Start, true);
                else if (c is not null) { Priority++; Done = true; }
                return c;
            }
            default: // Start: state 1 is never set, and any other value moves to the pick (Inferred)
                State = (int)FleetTaskPlannerState.Pick;
                return null;
        }
    }
}
