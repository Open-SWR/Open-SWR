namespace OpenSWR.Sim;

/// <summary>Post-action command kinds (<see cref="Advisor.PostCommand"/>): P1 of the command.</summary>
public static class PostKind
{
    /// <summary>Guided-UI step: a UI command to the main window (P2 = the step).</summary>
    public const int GuidedStep = 2;
    /// <summary>End of the game: P2 = the end state.</summary>
    public const int EndGame = 3;
    /// <summary>The last step of the briefing (the openswr adaptation posts it at the first gate open).</summary>
    public const int BriefingLastStep = 13;
}
