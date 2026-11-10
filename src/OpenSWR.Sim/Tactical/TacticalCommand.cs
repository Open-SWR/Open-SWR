namespace OpenSWR.Sim;

/// <summary>Tactical screen command ids (buttons and keys).</summary>
public enum TacticalCommand
{
    /// <summary>Task-force buttons 1 to 8, in slot order.</summary>
    TaskForce1, TaskForce2, TaskForce3, TaskForce4, TaskForce5, TaskForce6, TaskForce7, TaskForce8,
    /// <summary>Fighter-group buttons by colour: Red, Blue, Green, Gold.</summary>
    FighterGroup1, FighterGroup2, FighterGroup3, FighterGroup4,
    Superlaser,
    /// <summary>Nav-point list toggles: NavList4 toggles list 3 ... NavList1 toggles list 0.</summary>
    NavList4, NavList3, NavList2, NavList1,
    HighlightAlliance, HighlightEmpire,
    Space,
    Withdraw, ComputerControl, Abort,
    /// <summary>Close on the game options page: resets the panel's page snapshot; no sim effect.</summary>
    CloseOptions,
}
