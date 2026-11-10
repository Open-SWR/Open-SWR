namespace OpenSWR.Sim;

/// <summary>Tactical UI mode ids (the mode the screen is in).</summary>
public enum TacticalModeId
{
    Idle,
    /// <summary>A task force selected (the slot is the mode's own field).</summary>
    TaskForce,
    /// <summary>A fighter group selected (the colour is the mode's own field).</summary>
    FighterGroup,
    NavPoint, OwnShip, Enemy, Multi, Superlaser,
}
