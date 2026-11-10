namespace OpenSWR.Sim;

/// <summary>
/// Tactical mission numbers of a task force or fighter group (the mission page's order): the UI mission and the simulation
/// mission use the same numbers. Recover is for fighter groups (mission 2); Withdraw is set by the world withdraw.
/// </summary>
public enum TacticalMissionNumber
{
    None, Escort, Recover, Withdraw, AttackFighters, AttackCapitalShips, AttackDeathStar,
    LeftHook, RightHook, Hammer, Anvil, Stop,
}
