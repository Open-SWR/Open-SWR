namespace OpenSWR.Sim.Ai;

/// <summary>
/// Fleet-op task (types 1-6 = the theater kind): machine state, the fleet
/// cursors (current fleet, state-5 start, strike cursor), the need targets (one per fleet-op
/// counter, raised only), type 4's site.
/// </summary>
public sealed class FleetTask(FleetTaskType type)
{
    public FleetTaskType Type { get; } = type;
    public int State { get; set; }
    public AiFleet? Fleet { get; set; }
    public AiFleet? Start { get; set; }
    /// <summary>The strike cursor, a member fleet's key (none = start at the lowest key).</summary>
    public uint? Strike { get; set; }
    /// <summary>The producer site (type 4).</summary>
    public StarSystem? Site { get; set; }
    /// <summary>The visited systems (type 2); lives and dies with the task, not saved.</summary>
    public HashSet<StarSystem> Visited { get; } = [];
    // need targets: the fleet-op counter of the same name loses its target
    /// <summary>Target Firepower for the task; the fleet-op counter of the same name is reduced by it. Meaning in the original is unknown.</summary>
    public int Firepower { get; set; }
    /// <summary>Target AntiFighterFirepower for the task; the fleet-op counter of the same name is reduced by it. Meaning in the original is unknown.</summary>
    public int AntiFighterFirepower { get; set; }
    public int TroopCapacity { get; set; }
    public int FighterCapacity { get; set; }
    public int DeathStars { get; set; }
    /// <summary>Target Bombardment for the task; the fleet-op counter of the same name is reduced by it. Meaning in the original is unknown.</summary>
    public int Bombardment { get; set; }
    public int Interdictors { get; set; }
    public int Troops { get; set; }
    public int Fighters { get; set; }
    public int AntiFighters { get; set; }
}
