namespace OpenSWR.Sim.Ai;

/// <summary>What an agent can do: the missions it may join alone, the command roles of a minor character, the special-force type.</summary>
public sealed class AgentCapabilities
{
    public bool Diplomacy { get; set; }
    public bool Rescue { get; set; }
    public bool Sabotage { get; set; }
    public bool Espionage { get; set; }
    public bool Abduction { get; set; }
    public bool Subdue { get; set; }
    public bool Assassination { get; set; }
    public bool DeathStarSabotage { get; set; }
    public bool Incite { get; set; }
    public bool Recon { get; set; }
    public bool Recruitment { get; set; }
    public bool ShipDesign { get; set; }
    public bool TroopTraining { get; set; }
    public bool FacilityDesign { get; set; }
    /// <summary>No writer: always clear. Meaning in the original is unknown.</summary>
    public bool Unused { get; set; }
    /// <summary>SD CanBeAdmiral (minor characters).</summary>
    public bool Admiral { get; set; }
    /// <summary>SD CanBeCommander.</summary>
    public bool Commander { get; set; }
    /// <summary>SD CanBeGeneral.</summary>
    public bool General { get; set; }
    /// <summary>Special forces: the type 1 to 4 (types 5 to 8 count as 1 to 4); 0 for characters.</summary>
    public int SpecialForceKind { get; set; }
    public bool Commanding => Admiral || Commander || General;
}
