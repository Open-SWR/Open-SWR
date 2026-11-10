namespace OpenSWR.Sim;

/// <summary>Message types (the message handler that builds a message).</summary>
public enum MessageType
{
    /// <summary>Not set (the default value).</summary>
    None,
    /// <summary>The battle windows; not listed.</summary>
    Battle,
    Uprising,
    /// <summary>A system changes sides or declares neutrality.</summary>
    SystemJoins,
    ResearchComplete,
    /// <summary>Capital ship repaired or squadron at full strength.</summary>
    Repaired,
    Blockade,
    Smuggling,
    NearUprising,
    MaintenanceScrap,
    /// <summary>Ships, fleets or units arrived.</summary>
    Arrived,
    Deployed,
    /// <summary>Bombardment or assault result.</summary>
    Strike,
    FacilityLost,
    EvacuationLosses,
    PersonnelArrived,
    /// <summary>Full-screen event: a planet destroyed.</summary>
    PlanetDestroyed,
    MissionReport,
    /// <summary>Mission foiled, failed or aborted.</summary>
    MissionEnded,
    Informants,
    CapturedOrEscaped,
    /// <summary>Character injured, healed or killed.</summary>
    InjuredOrHealed,
    BountyHunters,
    /// <summary>A Force user discovered.</summary>
    ForceDiscovered,
    ForceGrowth,
    CoruscantCaptured,
    HeadquartersDestroyed,
    /// <summary>Natural disaster, new resources or resources depleted.</summary>
    ResourceEvent,
    /// <summary>A manager queue ran empty.</summary>
    QueueEmpty,
    Sabotage,
    DeathStarSabotaged,
    /// <summary>Force encounter, heritage, final battle.</summary>
    ForceEncounter,
    Dagobah,
    /// <summary>The Emperor arrives at Coruscant.</summary>
    EmperorArrives,
    Traitor,
    PalaceRescue,
    JabbaPrisoners,
    Tip,
    Assassination,
    ResearchExhausted,
    RecruitmentDone,
}
