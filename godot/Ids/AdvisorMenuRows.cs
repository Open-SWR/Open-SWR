/// <summary>
/// Advisor ids that only the UI handles: the alert menu of the second advisor display (the window menu's rows are
/// <see cref="OpenSWR.Sim.AdvisorMenu"/>) and the guided-UI steps of the briefing that are not map display filters.
/// </summary>
public static class AdvisorAlertMenu
{
    /// <summary>Opens the message window.</summary>
    public const int Messages = 284;

    // ---- Alert toggles, one per message category (checked = alerts on) ----
    public const int SupportAlerts = 369;
    public const int FleetAlerts = 370;
    public const int MissionAlerts = 371;
    public const int ResourceAlerts = 372;
    public const int ManufacturingAlerts = 373;
    public const int DefenseAlerts = 374;
    public const int ConflictAlerts = 375;
    public const int AdviceAlerts = 376;
    public const int ChatAlerts = 377;
}

/// <summary>
/// Guided-UI steps (2, n) the Alliance advisor (C-3PO) sends that set a filter on the Alliance side only.
/// Numbers shared with the Empire side are <see cref="GuidedStep"/>'s; a number in both enums means another filter per side.
/// </summary>
public enum AllianceGuidedStep
{
    OwnControlled = 2,
    HeadquartersList = 3,
    EmpireCoruscantList = 4,
    MothmaList = 7,
    EmpireCoruscantListAt8 = 8,
    EnemyUncontrolled = 16,
    YavinList = 18,
    LukeList = 19,
    EmpireCoruscantListAt14 = 20,
}

/// <summary>Guided-UI steps (2, n) the Empire advisor (IMP-22) sends that set a filter on the Empire side only.</summary>
public enum EmpireGuidedStep
{
    CoruscantList = 2,
    YavinList = 3,
    Unexplored = 4,
    PalpatineList = 7,
    YavinListAt8 = 8,
    OwnUncontrolled = 16,
    CoruscantListAt12 = 18,
    VaderList = 19,
    YavinListAt14 = 20,
}

/// <summary>Guided-UI steps (2, n) of the briefing that do not set a map display filter.</summary>
public static class GuidedStep
{
    /// <summary>Installs the input hooks: keys and clicks are swallowed, Esc skips the briefing.</summary>
    public const int InstallInputHooks = 12;
    /// <summary>Closes the message window, builds the advice topics, opens the Advice tab and removes the hooks.</summary>
    public const int OpenAdvice = 13;
}
