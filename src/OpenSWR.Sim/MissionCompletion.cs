namespace OpenSWR.Sim;

/// <summary>Mission CompletionStatus: nonzero ends the mission.</summary>
public enum MissionCompletion
{
    None,
    Done,
    /// <summary>Ended by a mission order or the story. Meaning in the original is unknown.</summary>
    Aborted,
    /// <summary>Foiled or resigned before the target phase.</summary>
    FoiledEnRoute,
    /// <summary>Foiled or resigned at the target.</summary>
    Foiled,
    NoTeam,
    /// <summary>The objective is gone, moved, or in the wrong state.</summary>
    ObjectiveInvalid,
    TargetDestroyed,
    /// <summary>The objective's owner is not allowed for this mission.</summary>
    ObjectiveOwnerRefused,
    SystemBombarded,
    SystemAssaulted,
    /// <summary>The Jedi teacher left the training.</summary>
    TeacherLeft,
    NoResearchFacility,
    TargetNotPopulated,
    ResearchExhausted,
    /// <summary>Diplomacy at full support.</summary>
    FullSupport,
    RecruitmentDone,
}
