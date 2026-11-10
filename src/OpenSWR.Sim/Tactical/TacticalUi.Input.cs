namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    // ---- input ----

    /// <summary>Queue 1: next (Tab, page arrow) or previous ship.</summary>
    public void Cycle(bool next) => _cycle.Add(next);

    /// <summary>Queue 2: a command id for the dispatcher.</summary>
    public void Command(TacticalCommand id) => _commands.Add(id);

    /// <summary>Queue 3: a pick (unit, nav point or none = empty space; ctrl).</summary>
    public void Pick(object? key, bool ctrl) => _picks.Add((key, ctrl));

    /// <summary>
    /// Left button in the 3-D view with the first visible hit of the five-point pick. With no hit and Ctrl nothing happens;
    /// with no hit otherwise the empty pick is made. A hit with Ctrl is picked as (key, 1). Otherwise (the task-force and
    /// fighter buttons unchecked) the empty pick is made, then the hit as (key, 0), so a plain left click selects.
    /// </summary>
    public void LeftDown(object? hit, bool ctrl)
    {
        if (hit is null) { if (!ctrl) Pick(null, false); return; }
        if (ctrl) { Pick(hit, true); return; }
        Pick(null, false);
        Pick(hit, false);
    }

    /// <summary>Right button with the first hit of the 13-point pick: no hit does nothing; a hit is picked (key, ctrl) into the current mode as an order.</summary>
    public void RightDown(object? hit, bool ctrl)
    {
        if (hit is not null) Pick(hit, ctrl);
    }

    /// <summary>A task-force button for slot i (also keys 1-8): a used slot, or an empty slot while the mode is own ship or multi-selection, gives the TaskForce command of slot i.</summary>
    public void PressTaskForce(int i)
    {
        if (TaskForceEnabled(i)) Command(TaskForceCommands[i]);
    }

    /// <summary>Whether task-force button i reacts.</summary>
    public bool TaskForceEnabled(int i) => TaskForces[i] is not null || _mode is OwnShipMode or MultiMode;

    /// <summary>Assign mission (right-panel missions and tactics pages).</summary>
    public void AssignMission(TacticalMissionNumber m) => Switch(_mode.Mission(m));

    /// <summary>Assign tactic (tactics page).</summary>
    public void AssignTactic(TacticalTactic t) => Switch(_mode.Tactic(t));
}
