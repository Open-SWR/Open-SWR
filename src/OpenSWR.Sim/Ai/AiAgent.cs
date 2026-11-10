namespace OpenSWR.Sim.Ai;

/// <summary>Agent record: one per character or special force with a view.</summary>
public sealed class AiAgent(Agent agent)
{
    public Agent Agent { get; } = agent;
    /// <summary>Location key (set once from the view's parent).</summary>
    public GameObject? Location { get; set; }
    public bool Dirty { get; set; }
    public int Key { get; set; }
    public int Side { get; set; }
    /// <summary>The view owner.</summary>
    public int Owner { get; set; }
    /// <summary>The mission op holding the agent.</summary>
    public int MissionOpKey { get; set; }
    /// <summary>The character op holding the agent.</summary>
    public int CharacterOpKey { get; set; }
    /// <summary>The holding op's priority.</summary>
    public int Priority { get; set; }
    // set at create, kept by every update
    /// <summary>Own allegiance (owner = own and not Captured, or owner != own and Captured).</summary>
    public bool Own { get; set; }
    /// <summary>Enemy allegiance.</summary>
    public bool Enemy { get; set; }
    /// <summary>One of the six major characters.</summary>
    public bool Major { get; set; }
    public bool Palpatine { get; set; }
    /// <summary>A CreateMission command was sent with the agent; cleared when that mission ends (kept).</summary>
    public bool MissionSent { get; set; }
    /// <summary>Not held by an op: set at create and on release, cleared when an op takes the agent (kept); the list builders treat it like a set priority. Meaning in the original is unknown.</summary>
    public bool Unassigned { get; set; }
    public bool SpecialForce { get; set; }
    public bool Character { get; set; }
    /// <summary>Luke, Mothma, Palpatine or Vader.</summary>
    public bool VictoryCharacter { get; set; }
    // rebuilt by every update
    public bool NotCompleted { get; set; }
    public bool Injured { get; set; }
    public bool Enroute { get; set; }
    public bool Captured { get; set; }
    public bool OnMission { get; set; }
    /// <summary>Force 1-99, not Luke or Vader.</summary>
    public bool ForceTrainee { get; set; }
    /// <summary>Force >= 100, Luke or Vader.</summary>
    public bool ForceMaster { get; set; }
    /// <summary>The special-force pick of planner 5's disband order requires it. No writer: always clear. Meaning in the original is unknown.</summary>
    public bool Disbandable { get; set; }
    /// <summary>On a mandatory mission.</summary>
    public bool MandatoryMission { get; set; }
    public bool CommandKind1 { get; set; }
    public bool CommandKind2 { get; set; }
    public bool CommandKind3 { get; set; }
    /// <summary>A research capability whose line is not exhausted, or Diplomacy capable with Diplomacy &gt; 79: kept out of most teams. Meaning in the original is unknown.</summary>
    public bool Specialist { get; set; }
    public bool HasCommandKind => CommandKind1 || CommandKind2 || CommandKind3;
    /// <summary>Set at create, never recomputed.</summary>
    public AgentCapabilities Can { get; } = new();

    /// <summary>Clears the state an update rebuilds.</summary>
    internal void ClearUnkept()
    {
        (NotCompleted, Injured, Enroute, Captured, OnMission, ForceTrainee, ForceMaster, Disbandable) = (false, false, false, false, false, false, false, false);
        (MandatoryMission, CommandKind1, CommandKind2, CommandKind3, Specialist, Dirty) = (false, false, false, false, false, false);
    }
    /// <summary>The eight enhanced skills by <see cref="SkillIndex"/>.</summary>
    public ref int Skill(int index) => ref _skills[index];
    readonly int[] _skills = new int[8];
    public int Force { get; set; }
    /// <summary>A special force's MaintenanceCost.</summary>
    public int Maintenance { get; set; }

    /// <summary>Zeroes the update's fields.</summary>
    internal void ClearSkills()
    {
        for (int k = 0; k < 8; k++) Skill(k) = 0;
        (Force, Maintenance) = (0, 0);
    }
}
