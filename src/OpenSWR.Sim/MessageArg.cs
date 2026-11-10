namespace OpenSWR.Sim;

/// <summary>A template argument: a name (text) or a side (formatted by the placeholder's kind in the UI).</summary>
public readonly record struct MessageArg(string? Text = null, Owner Side = Owner.None)
{
    public static implicit operator MessageArg(string text) => new(text);
    public static MessageArg Of(Owner side) => new(null, side);
}
