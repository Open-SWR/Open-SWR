namespace OpenSWR.Sim;

/// <summary>A TEXTSTRA RCDATA template id with its arguments (slot n = argument n).</summary>
public sealed record MessageText(int Template, MessageArg[] Args);
