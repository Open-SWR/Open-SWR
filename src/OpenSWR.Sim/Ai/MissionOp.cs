using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Mission operation: the op record of one mission. It holds the key (the mission's UserID), the produce state, flags (team
/// complete, decoys done, staging found, member at staging, target found, score zero, gave up, recheck kept somebody, retired,
/// send the MissionOrder command, review, mission running), the mission, type, objective, target, staging, team, decoys, failures and marked
/// sector. Object-kind ops also hold the owning fleet op, theater sector, assignment priority and the mission-type mask; the
/// outside-theater flag marks staging outside the theater.
/// </summary>
public sealed class MissionOp(int key, OpClass @class, Owner side)
{
    public int Key { get; } = key;
    public OpClass Class { get; } = @class;
    public Owner Side { get; } = side;
    public int State { get; set; }
    /// <summary>The team has its full size.</summary>
    public bool TeamComplete { get; set; }
    /// <summary>The decoy pick is done.</summary>
    public bool DecoysDone { get; set; }
    public bool StagingFound { get; set; }
    /// <summary>Every member is at the staging system.</summary>
    public bool AtStaging { get; set; }
    public bool TargetFound { get; set; }
    /// <summary>The score reads 0. No writer: always clear.</summary>
    public bool ScoreZero { get; set; }
    /// <summary>Object kind: the staging system lies outside the theater.</summary>
    public bool OutsideTheater { get; set; }
    /// <summary>Too many failures: at the mission's end the team leaves and the staging is dropped.</summary>
    public bool GaveUp { get; set; }
    /// <summary>The member recheck kept somebody.</summary>
    public bool KeptMember { get; set; }
    public bool Retired { get; set; }
    /// <summary>Recall the running mission (MissionOrder command).</summary>
    public bool Recall { get; set; }
    /// <summary>Review the running mission: recall it when its target is no longer valid.</summary>
    public bool Review { get; set; }
    /// <summary>The mission is running.</summary>
    public bool Running { get; set; }
    public Mission? Mission { get; set; }
    public GameObject? Objective { get; set; }
    public StarSystem? Target { get; set; }
    public StarSystem? Staging { get; set; }
    public List<AiAgent> Team { get; } = [];
    public List<AiAgent> Decoys { get; } = [];
    /// <summary>The missions this op has finished; a mission in this set never attaches again.</summary>
    public HashSet<GameObject> Finished { get; } = [];
    public int Failures { get; set; }
    public AiSector? Marked { get; set; }
    public FleetOp? FleetOp { get; set; }
    public AiSector? Theater { get; set; }
    public int Priority { get; set; }
    /// <summary>The team capability of the abduction and assassination classes.</summary>
    public bool TeamCapability(AiAgent a) => Class.Family == MissionKind.Assassination ? a.Can.Assassination : a.Can.Abduction;
    /// <summary>Selectable by planner 4 when no mission is running.</summary>
    public bool Selectable => !Running;
}
