/// <summary>Placeholder kinds of message template arguments: how an argument formats (<see cref="Assets.Message"/>).</summary>
public static class MessageArgKinds
{
    // ---- Side names ----
    public const int SideName = 1;
    public const int SideFullName = 2;
    public const int SideAdjective = 4;
    /// <summary>First and last of the side name kinds with their own string pair each.</summary>
    public const int SideNameKindFirst = 16, SideNameKindLast = 25;
    /// <summary>The full name with a suffix; the suffix is left out here.</summary>
    public const int SideFullNameSuffixed = 33;

    // ---- Manager lines ----
    /// <summary>The name of the manager's current product.</summary>
    public const int ProductName = 5;
    /// <summary>The manager's queue count.</summary>
    public const int QueueCount = 6;
}
