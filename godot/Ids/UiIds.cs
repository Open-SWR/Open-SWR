/// <summary>REBEXE.EXE cursor groups, the TEXTCOMM.DLL accelerator table and the Win32 virtual-key codes it uses.</summary>
public static class UiIds
{
    // ---- Cursor groups (REBEXE.EXE) ----
    /// <summary>Default cursor of the whole game.</summary>
    public const ushort DefaultCursor = 1001;
    /// <summary>Strategy-screen targeting cursor.</summary>
    public const ushort TargetingCursor = 1002;

    // ---- Accelerators (TEXTCOMM.DLL) ----
    /// <summary>Keyboard shortcuts: key to command id.</summary>
    public const ushort AcceleratorTable = 11;

    // ---- Virtual-key codes ----
    public const ushort VirtualKeyTab = 9;
    public const ushort VirtualKeyAdd = 107;
    public const ushort VirtualKeySubtract = 109;
}

/// <summary>
/// Speed box levels of the strategy screen (1 slowest ... 4 fastest; 0 paused). Each running level sends its ticks per
/// day (<see cref="OpenSWR.Sim.Clock.TicksForSpeed"/>): fewer ticks per day run faster.
/// </summary>
public static class SpeedLevel
{
    public const int Paused = 0, Slowest = 1, Normal = 2, Fast = 3, Fastest = 4;
    public const int SlowestTicks = 600, FastTicks = 12, FastestTicks = 4;
}
