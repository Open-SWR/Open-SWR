namespace OpenSWR.Sim.Ai;

/// <summary>
/// Fleet-task types: the theater kind the task runs (the task's type field, an int in the save format). The names follow the
/// use in the slots. Only the types 1 to 6 exist. The original's own meaning of the numbers is unknown.
/// </summary>
public enum FleetTaskType
{
    /// <summary>Not set (the default value).</summary>
    None,
    /// <summary>Base in the theater, troops unloaded at own systems; attacks, bombards and moves to enemy targets. Meaning in the original is unknown.</summary>
    Strike,
    /// <summary>Like <see cref="Strike"/> but keeps the visited systems and moves through the unvisited ones. Meaning in the original is unknown.</summary>
    Scout,
    /// <summary>Like <see cref="Strike"/>, with a goal of an own theater. Meaning in the original is unknown.</summary>
    Reserve,
    /// <summary>Type 4: a producer site (the site pick) with troops taken from it.</summary>
    Site,
    /// <summary>Type 5: a defence base where defence troops gather.</summary>
    Defence,
    /// <summary>Type 6: an assault base where assault troops gather.</summary>
    Assault,
}

/// <summary>Produce states of a fleet op (the op's state field, an int in the save format).</summary>
public enum FleetOpState
{
    /// <summary>Entry: refresh the members and start the status.</summary>
    Start,
    /// <summary>Split the damaged ships of the first damaged member fleet.</summary>
    SplitDamaged,
    /// <summary>Member-fleet sums and the need targets.</summary>
    Needs,
    /// <summary>Balance: the shortfall builders, else the surplus builder.</summary>
    Balance,
    /// <summary>Mission and character requests and the status.</summary>
    Requests,
    /// <summary>The task machine.</summary>
    Task,
    /// <summary>Flagship, gather system and the task status (the default state).</summary>
    Finish,
}

/// <summary>States of planner 8, the fleet-operation manager (the planner's state field, an int in the save format).</summary>
public enum FleetOpPlannerState
{
    Start,
    /// <summary>Creates an operation or removes one, by the theater balance.</summary>
    Balance,
    /// <summary>The next operation by key cursor, no wrap.</summary>
    Next,
    /// <summary>Assigns a theater to the operation.</summary>
    Assign,
    /// <summary>The operation's produce (the default state).</summary>
    Produce,
}

/// <summary>
/// States of the fleet-task machine (the task's state field, an int in the save format). Idle (0) is the finished state; the
/// machine starts again at <see cref="ClaimMarks"/> on the next produce pass. The state names follow the step's action.
/// </summary>
public enum FleetTaskState
{
    Idle,
    /// <summary>Clears the theater's claim marks and marks the member fleets' claimed systems.</summary>
    ClaimMarks,
    /// <summary>The base system of the task's slot.</summary>
    Base,
    /// <summary>Unloads troops (types 1-3) or brings troops into member fleets (types 4-6).</summary>
    Troops,
    /// <summary>Brings the base's fighters into the member fleets.</summary>
    Fighters,
    /// <summary>The next member fleet from the cursor, or none.</summary>
    NextFleet,
    /// <summary>The task fleet's flagship, and the character command.</summary>
    Characters,
    /// <summary>The attack slot.</summary>
    Attack,
    /// <summary>The assault slot.</summary>
    Assault,
    /// <summary>The bombard slot.</summary>
    Bombard,
    /// <summary>The move-to-target slot.</summary>
    MoveTarget,
    /// <summary>Regroups the task fleet.</summary>
    Regroup,
    /// <summary>Moves the task fleet to its destination (no location test).</summary>
    Destination,
    /// <summary>Strike: the strike cursor's command, and the machine's done state.</summary>
    Strike,
}
