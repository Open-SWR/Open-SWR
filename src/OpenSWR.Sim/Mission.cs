using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>
/// Mission object (families 65-115 from its MISSNSD record), a content of manager 248. Field names from the debug
/// notifications.
/// </summary>
public sealed class Mission(MissionRecord record, uint id) : GameObject(record.Common.Ref, Owner.Neutral, id)
{
    public MissionRecord Record { get; } = record;
    /// <summary>Family of the type: the MissionKind values from Move to Pickup.</summary>
    public MissionKind Kind => (MissionKind)Family.Of(Type);
    /// <summary>UserID: free controller tag, no sim reader.</summary>
    public uint UserId { get; init; }
    /// <summary>TaskStatus: 0 pending, 2 failed, 3 succeeded.</summary>
    public MissionTaskStatus TaskStatus
    {
        get => _taskStatus;
        set
        {
            if (value == _taskStatus) return;
            if (value != 0) Snapshot();
            FactSink?.Record(FactKind.TaskStatus, Ref, (int)_taskStatus, (int)value);
            _taskStatus = value;
            FactSink?.ViewMessage(ViewMessageType.TaskStatus, this); // the views' TaskStatus message
        }
    }
    MissionTaskStatus _taskStatus;
    /// <summary>The game whose <see cref="GameState.FactTrace"/> receives TaskStatus changes (tracing only).</summary>
    internal GameState? FactSink { get; init; }
    /// <summary>CompletionStatus: != 0 = ending (1 normal, 3 / 4 foiled, 5 no valid member, 15 diplomacy at share 100).</summary>
    public MissionCompletion CompletionStatus
    {
        get => _completionStatus;
        set
        {
            if (value == _completionStatus) return;
            if (value != 0) Snapshot();
            _completionStatus = value;
            FactSink?.ViewMessage(ViewMessageType.CompletionStatus, this); // the views' CompletionStatus message
        }
    }
    MissionCompletion _completionStatus;
    /// <summary>
    /// UI only: Leader and the first Team member when TaskStatus or CompletionStatus last became nonzero. The original's message
    /// handlers (MissionReport and MissionEnded message types) run at that notification, before the members leave; openswr builds the
    /// messages at the end of the tick, when an ended mission's Leader and Team are already empty. Not saved.
    /// </summary>
    public Agent? ReportLeader { get; private set; }
    public Agent? ReportMember { get; private set; }
    /// <summary>UI only, same snapshot: the first member (key order) not Destroyed and not captured with a system or fleet above it (the location line of the MissionEnded message).</summary>
    public GameObject? ReportPlace { get; private set; }

    void Snapshot()
    {
        (ReportLeader, ReportMember) = (Leader, Team.FirstOrDefault());
        ReportPlace = Members.Where(x => !x.Destroyed && x is not Character { Captured: true }).OrderBy(x => x.Key)
            .Select(x => { var p = x.Container; while (p is not null and not (StarSystem or Fleet)) p = p.Container; return p; })
            .FirstOrDefault(p => p is not null);
    }
    /// <summary>Phase 0..11, plus the ReadyForNextPhase and Mandatory flags.</summary>
    public int Phase { get; set; }
    /// <summary>Timer block LastPhase: the new phase on every change except a change to 11, which keeps it.</summary>
    public int LastPhase { get; set; }
    public bool Ready { get; set; }
    /// <summary>ImpliedTeam flag: the phase check skips its status-5 test.</summary>
    public bool ImpliedTeam { get; init; }
    public bool Mandatory { get; init; }
    /// <summary>OriginLocationKey, ObjectiveKey, TargetKey, TargetLocationKey, LeaderKey.</summary>
    public GameObject? Origin { get; set; }
    public GameObject? Objective { get; set; }
    public GameObject? TargetKey { get; set; }
    public GameObject? TargetLocation { get; set; }
    public Agent? Leader { get; set; }
    /// <summary>Team, Decoy and Captured select lists, each by ascending SdRef.</summary>
    public List<Agent> Team { get; } = [];
    public List<Agent> Decoys { get; } = [];
    public List<Agent> Captured { get; } = [];
    public IEnumerable<Agent> Members => Team.Concat(Decoys).Concat(Captured);
    /// <summary>Class field: recruitment RecruitedCharacterKey; research accumulated points.</summary>
    public Character? Recruited { get; set; }
    /// <summary>Same class field: Jedi training JediTrainingTeacherKey; Dagobah the phase-8 start day.</summary>
    public Character? Teacher { get; set; }
    public int StartDay { get; set; }
    public int Accumulator { get; set; }
    /// <summary>Research levels gained by the last post (display only, Inferred).</summary>
    public int LevelsGained { get; set; }
    /// <summary>Encounter codes of phases 2, 3, 7 and 9, kept in the timer block (<see cref="EncounterCodes"/>).</summary>
    public EncounterCodes Encounters { get; set; } = EncounterCodes.Standard;
}
