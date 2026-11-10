namespace OpenSWR.Sim;

/// <summary>Advisor event codes: what a message or event tells the advisor.</summary>
public enum AdvisorCode
{
    /// <summary>Not set (the default value).</summary>
    None,
    /// <summary>Enemy uprising begins, own uprising ends, or a system joins.</summary>
    GoodNews,
    BadNews,
    /// <summary>Units deployed, or a facility lost because it could not deploy.</summary>
    Deployed,
    ResearchComplete,
    ShipsArrived,
    UnitsArrived,
    ShipRepaired,
    SquadronFull,
    MaintenanceShortfall,
    /// <summary>Our fleet starts a blockade.</summary>
    BlockadeStarted,
    /// <summary>Our system is under blockade.</summary>
    SystemBlockaded,
    BlockadeLosses,
    AllianceHeadquartersDestroyed,
    /// <summary>Default code of report-style messages.</summary>
    Report,
    MinorCharacterMessage,
    LukeMessage,
    MothmaMessage,
    LeiaMessage,
    HanMessage,
    PalpatineMessage,
    VaderMessage,
    /// <summary>Disaster, new resources, resources depleted, informants.</summary>
    ResourceEvent,
    LukeCaptured,
    MothmaCaptured,
    LeiaCaptured,
    HanCaptured,
    PalpatineCaptured,
    VaderCaptured,
    CharacterEscaped,
    LukeEscaped,
    MothmaEscaped,
    PalpatineEscaped,
    VaderEscaped,
    /// <summary>Message sent to or received from the other side.</summary>
    SideMessage,
    Tip,
    /// <summary>No setter in this build. Meaning in the original is unknown.</summary>
    RandomTip,
    /// <summary>No setter in this build. Meaning in the original is unknown.</summary>
    UnusedTip,
    BountyHuntersFoundHan,
    Bombardment,
    Assault,
}
