namespace OpenSWR.Sim.Ai;

/// <summary>Index of a system op class: its slot in the model's system op counters (an int in the save format).</summary>
public enum SystemOpIndex
{
    Recruitment,
    ShipResearch,
    TroopResearch,
    FacilityResearch,
    SubdueUprising,
    DeathStarSabotage,
    Abduction,
    Rescue,
    Reconnaissance,
    Espionage,
    Diplomacy,
    InciteUprising,
}

/// <summary>Index of an object op class: its slot in the fleet op's attached-op counters. Abduction and assassination share 0.</summary>
public enum ObjectOpIndex
{
    AbductionOrAssassination,
    Espionage,
    Reconnaissance,
    Sabotage,
}

/// <summary>
/// Subtype of a (family, subtype) pair. Research uses 1 to 3 (ship, troop, facility design); recon, espionage and abduction
/// tell their system and object classes apart by subtype.
/// </summary>
public enum OpSubtype
{
    None,
    ShipResearch,
    TroopResearch,
    FacilityResearch,
    Abduction,
    Reconnaissance,
    Espionage,
}

/// <summary>Produce states of a system-kind mission op (the op's state field, an int in the save format).</summary>
enum MissionOpState
{
    Start,
    Check,
    Recheck,
    TeamPick,
    DecoyPick,
    TargetSearch,
    StagingSearch,
    Moves,
    /// <summary>The mission request (CreateMission command); also the default state.</summary>
    Mission,
}

/// <summary>Produce states of an object-kind mission op (the op's state field, an int in the save format).</summary>
enum ObjectMissionOpState
{
    Start,
    Check,
    Target,
    Staging,
    Recheck,
    TeamPick,
    DecoyPick,
    Moves,
    /// <summary>The mission request (CreateMission command).</summary>
    Mission,
    /// <summary>Release the members and end (the default state).</summary>
    Release,
}

/// <summary>Planner 4's states (the planner's state field, an int in the save format).</summary>
enum MissionOpPlannerState
{
    Start,
    /// <summary>Retires and creates the requested system ops.</summary>
    Counts,
    /// <summary>The first fleet op from the cursor with a mission request (no wrap).</summary>
    FleetOpRequest,
    /// <summary>Retires and attaches the fleet op's object-kind mission request.</summary>
    FleetAttach,
    /// <summary>The next selectable op from the cursor.</summary>
    NextOp,
    /// <summary>Produce the chosen op (the default state; state 4 is never set).</summary>
    Produce,
}

/// <summary>States of a garrison task (the task's state field, an int in the save format).</summary>
enum GarrisonTaskState
{
    Start,
    /// <summary>Picks the task's target system.</summary>
    FindTarget,
    /// <summary>Splits the damaged ships of the task fleet.</summary>
    Split,
    /// <summary>The balance: task fleet firepower less the best armed firepower.</summary>
    Balance,
    /// <summary>Reinforces the task fleet, or surplus.</summary>
    Reinforce,
    /// <summary>Unloads the troops aboard the task fleet.</summary>
    UnloadTroops,
    /// <summary>Unloads the characters aboard the task fleet.</summary>
    UnloadCharacters,
    /// <summary>Moves the task fleet to its target.</summary>
    Move,
    /// <summary>Releases the task (the default state).</summary>
    Release,
}

/// <summary>Planner 9's states (the planner's state field, an int in the save format).</summary>
enum FleetTaskPlannerState
{
    /// <summary>Entry: state 1 is never set, and any other value moves to the pick (Inferred).</summary>
    Start,
    /// <summary>Picks the next fleet task by key cursor, no wrap.</summary>
    Pick,
    /// <summary>The task's produce.</summary>
    Produce,
}
