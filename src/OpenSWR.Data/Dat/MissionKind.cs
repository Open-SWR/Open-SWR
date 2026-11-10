namespace OpenSWR.Data.Dat;

/// <summary>Mission kinds: the family of the mission type.</summary>
public enum MissionKind : uint
{
    Move = 65,
    Return = 66,
    Autorouting = 67,
    Adrift = 68,
    Diplomacy = 81,
    Espionage = 82,
    /// <summary>Ship, facility or troop design research.</summary>
    Research = 83,
    Reconnaissance = 84,
    Recruitment = 85,
    InciteUprising = 86,
    SubdueUprising = 87,
    JediTraining = 88,
    Rescue = 97,
    Abduction = 98,
    Assassination = 99,
    /// <summary>Story: Han's rescue at the palace.</summary>
    Palace = 100,
    /// <summary>Story: bounty hunters after Han.</summary>
    Bounty = 101,
    Sabotage = 105,
    DeathStarSabotage = 106,
    /// <summary>Story: Luke's training on Dagobah.</summary>
    Dagobah = 113,
    /// <summary>Story: vacation or sabbatical.</summary>
    Vacation = 114,
    /// <summary>Story: pickup.</summary>
    Pickup = 115,
}
