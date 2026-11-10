namespace OpenSWR.Sim;

/// <summary>Advice topic kinds of the side's TEXTSTRA topic list: one kind per tip trigger (2-6), and the kind posted at once (7).</summary>
public static class AdviceKind
{
    public const int Manufacturing = 2, Fleet = 3, Missions = 4, Planet = 5, GoTo = 6, Immediate = 7;
}
