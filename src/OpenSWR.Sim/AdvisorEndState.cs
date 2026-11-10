namespace OpenSWR.Sim;

/// <summary>
/// End-of-game state (the post command <see cref="PostKind.EndGame"/> carries it): the winner and the player's side. Each
/// selects one end movie.
/// </summary>
public enum AdvisorEndState
{
    /// <summary>Not set (the default value).</summary>
    None,
    AllianceWinsPlayerAlliance,
    EmpireWinsPlayerAlliance,
    EmpireWinsPlayerEmpire,
    AllianceWinsPlayerEmpire,
}
