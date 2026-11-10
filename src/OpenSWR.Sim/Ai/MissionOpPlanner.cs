namespace OpenSWR.Sim.Ai;

    /// <summary>
    /// Planner 4, the mission-operations manager. State 0 is empty. State 1 handles retire and create requests. States 2 and 3
    /// handle fleet-operation requests: the fleet op's retire, create and attach. State 5 picks the next selectable op, and state
    /// 6 runs its produce.
    /// </summary>
public sealed class MissionOpPlanner() : Planner(PlannerType.MissionOperation)
{
    int _next; // The next op key to examine (0 means the first).
    int _current;
    int _fleetOp, _nextFleetOp;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { Done = true; return null; }
        var m = controller.Model;
        switch ((MissionOpPlannerState)State)
        {
            case MissionOpPlannerState.Start:
                State = (int)MissionOpPlannerState.Counts;
                return null;
            case MissionOpPlannerState.Counts:
                if (m.MissionRetireKey != OpCodes.NoOp && m.Ops.TryGetValue(m.MissionRetireKey, out var old)) MissionOps.Retire(m, old);
                m.MissionRetireKey = OpCodes.NoOp;
                if (m.MissionRequestKind != OpCodes.NoRequest) MissionOps.Create(m, (MissionKind)m.MissionRequestKind, m.MissionRequestSubtype);
                m.MissionRequestKind = OpCodes.NoRequest;
                State = (int)MissionOpPlannerState.FleetOpRequest;
                return null;
            case MissionOpPlannerState.FleetOpRequest: // From the next fleet-op key (or the first), the first fleet op with a request; no wrap.
            {
                var fo = m.FleetOps.Values.FirstOrDefault(o => (_nextFleetOp == 0 || o.Key >= _nextFleetOp) && (o.RequestKind != OpCodes.NoRequest || o.RetireKey != OpCodes.NoOp));
                if (fo is null) { (_fleetOp, _nextFleetOp, State) = (0, 0, (int)MissionOpPlannerState.NextOp); return null; }
                _fleetOp = fo.Key;
                _nextFleetOp = m.FleetOps.Keys.FirstOrDefault(k => k > fo.Key);
                State = (int)MissionOpPlannerState.FleetAttach;
                return null;
            }
            case MissionOpPlannerState.FleetAttach:
                if (m.FleetOps.TryGetValue(_fleetOp, out var req))
                {
                    if (req.RetireKey != OpCodes.NoOp && m.Ops.TryGetValue(req.RetireKey, out var gone)) MissionOps.Retire(m, gone);
                    req.RetireKey = OpCodes.NoOp; // Inferred, as the model's field.
                    if (req.RequestKind != OpCodes.NoRequest && MissionOps.Create(m, (MissionKind)req.RequestKind, req.RequestSubtype) is { } made && made.Class.Object) MissionOps.Attach(m, req, made);
                    req.RequestKind = OpCodes.NoRequest;
                }
                State = (int)MissionOpPlannerState.NextOp;
                return null;
            case MissionOpPlannerState.NextOp:
            {
                var op = m.Ops.Values.FirstOrDefault(o => (_next == 0 || o.Key >= _next) && o.Selectable);
                if (op is null) { (State, Done, _next) = ((int)MissionOpPlannerState.Start, true, 0); return null; }
                _current = op.Key;
                _next = m.Ops.Keys.FirstOrDefault(k => k > op.Key);
                State = (int)MissionOpPlannerState.Produce;
                return null;
            }
            default: // Produce
            {
                if (!m.Ops.TryGetValue(_current, out var op)) { (State, Done) = ((int)MissionOpPlannerState.Start, true); return null; }
                var (c, finished) = MissionOps.Produce(game, m, op);
                if (c is not null && !finished) { Priority++; Done = true; }
                else if (finished) (State, Done) = ((int)MissionOpPlannerState.Start, true);
                return c;
            }
        }
    }
}
