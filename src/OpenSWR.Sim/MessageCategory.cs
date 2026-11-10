namespace OpenSWR.Sim;

/// <summary>
/// Message categories, one per message (never combined): None (shown on the All tab only), the tab categories, and Event
/// (full-screen events).
/// </summary>
public enum MessageCategory
{
    None, Support, Resource, Manufacturing, Mission, Chat, Defense, Fleet, Conflict, Advice, Event,
}
