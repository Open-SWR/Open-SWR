namespace OpenSWR.Sim;

/// <summary>Message-window tab ids: the install's ids of the window's tab buttons (kept as numbers).</summary>
public static class MessageTab
{
    public const int All = 121, Support = 122, Fleet = 123, Mission = 124, Resource = 125, Manufacturing = 126, Defense = 127,
        Conflict = 128, Chat = 129, Advice = 130;

    /// <summary>Message-window tab id (All ... Advice) to its category (None = All).</summary>
    public static MessageCategory CategoryOf(int tab) => tab switch
    {
        Support => MessageCategory.Support, Fleet => MessageCategory.Fleet, Mission => MessageCategory.Mission, Resource => MessageCategory.Resource,
        Manufacturing => MessageCategory.Manufacturing, Defense => MessageCategory.Defense, Conflict => MessageCategory.Conflict,
        Chat => MessageCategory.Chat, Advice => MessageCategory.Advice, _ => MessageCategory.None,
    };

    /// <summary>The tab of a category (All for none or a category without a tab).</summary>
    public static int TabOf(MessageCategory category) => category switch
    {
        MessageCategory.Support => Support, MessageCategory.Fleet => Fleet, MessageCategory.Mission => Mission, MessageCategory.Resource => Resource,
        MessageCategory.Manufacturing => Manufacturing, MessageCategory.Defense => Defense, MessageCategory.Conflict => Conflict,
        MessageCategory.Chat => Chat, MessageCategory.Advice => Advice, _ => All,
    };
}
