namespace OpenSWR.Sim.Ai;

/// <summary>
/// Mission operation: the op record of one mission. It holds the key (the mission's UserID), the produce state, flags (team
/// complete, decoys done, staging found, member at staging, target found, score zero, gave up, recheck kept somebody, retired,
/// send 0x250, review, mission running), the mission, type, objective, target, staging, team, decoys, failures and marked
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
    /// <summary>Recall the running mission (command 0x250).</summary>
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
    public bool TeamCapability(AiAgent a) => Class.Family == 0x63 ? a.Can.Assassination : a.Can.Abduction;
    /// <summary>Selectable by planner 4 when no mission is running.</summary>
    public bool Selectable => !Running;
}

/// <summary>
/// Op classes: family, subtype, type index (system: a counter; object: the fleet-op count plus 4 times the index), mission type,
/// team size, and max failures. Object classes: espionage (0x52) and recon (0x54) for subtypes other than 6 and 5, abduction
/// (0x62, subtype 4) and assassination (0x63), and sabotage (0x69). Team sizes are 2, 1, 2 and 2 in that order.
/// </summary>
public sealed record OpClass(uint Family, int Subtype, int Index, uint MissionType, int TeamSize, int MaxFailures, bool Object = false)
{
    public static readonly OpClass Espionage = new(0x52, 0, 1, 0x52000013, 2, 10, true), Recon = new(0x54, 0, 2, 0x54000015, 1, 1000, true),
        Abduction = new(0x62, 4, 0, 0x62000017, 2, 5, true), Assassination = new(0x63, 4, 0, 0x63000081, 2, 5, true),
        Sabotage = new(0x69, 0, 3, 0x69000012, 2, 10, true);

    public static readonly OpClass[] All =
    [
        new(0x55, 0, 0, 0x55000016, 1, 1000), new(0x53, 1, 1, 0x53000020, 1, 1000), new(0x53, 2, 2, 0x53000022, 1, 1000),
        new(0x53, 3, 3, 0x53000021, 1, 1000), new(0x57, 0, 4, 0x57000080, 1, 5), new(0x6A, 0, 5, 0x6A000041, 2, 10),
        new(0x62, 0, 6, 0x62000017, 2, 10), new(0x61, 0, 8, 0x61000011, 2, 10), new(0x54, 5, 9, 0x54000015, 1, 1000),
        new(0x52, 6, 0xA, 0x52000013, 1, 1000), new(0x51, 0, 0xB, 0x51000010, 1, 5), new(0x56, 0, 0xC, 0x56000040, 1, 10),
    ];

    public static OpClass? Of(uint family, int subtype) => All.FirstOrDefault(c => c.Family == family && c.Subtype == subtype) ?? family switch
    {
        0x52 => Espionage, 0x54 => Recon, 0x63 => Assassination, 0x69 => Sabotage, 0x62 when subtype == 4 => Abduction, _ => null,
    };
}

/// <summary>
/// Mission-op behaviour: creation and retirement counters, member assignment, the system-kind produce machine, the per-class
/// team, decoy and target lists, staging, the 0x240 and 0x250 builders, and the 0x1F0 and 0x1F1 handlers.
/// </summary>
public static class MissionOps
{

    /// <summary>
    /// Creates a new op of a (family, subtype) pair. System-kind classes are counted. The abduction and assassination ops get
    /// their own mission-type masks.
    /// </summary>
    public static MissionOp? Create(WorldModel m, uint family, int subtype)
    {
        if (OpClass.Of(family, subtype) is not { } c) return null;
        var op = new MissionOp(m.NextOpKey++, c, m.Side) { State = 0 };
        m.Ops[op.Key] = op;
        if (!c.Object) m.SystemOps(c.Index)++;
        m.Dirty = true;
        return op;
    }

    /// <summary>
    /// Retires the op (its retired flag is set). A system op is uncounted. An object op is detached from its fleet op: taken out
    /// of the attached set, the fleet op's count drops by one, and the op's owning fleet op and slot are cleared. The produce
    /// machine cleans it up.
    /// </summary>
    /// </summary>
    public static void Retire(WorldModel m, MissionOp op)
    {
        if (op.Retired) return;
        op.Retired = true;
        if (!op.Class.Object) m.SystemOps(op.Class.Index)--;
        else if (op.FleetOp is { } fo)
        {
            if (fo.MissionOps.Remove(op)) fo.ObjectOps(op.Class.Index)--;
            SetPriority(m, op, 0);
            op.FleetOp = null;
        }
    }

    /// <summary>(family, subtype) names this op class (object classes match any request subtype of theirs).</summary>
    internal static bool Matches(OpClass c, uint family, int subtype) => OpClass.Of(family, subtype) == c;

    /// <summary>The fleet op's mission request is consumed; the op joins and is counted, and its owning fleet op and theater are set.</summary>
    internal static void Attach(WorldModel m, FleetOp fo, MissionOp op)
    {
        (fo.RequestKind, fo.RequestSubtype) = (0, 0);
        fo.MissionOps.Add(op);
        fo.ObjectOps(op.Class.Index)++;
        (op.FleetOp, op.Theater) = (fo, fo.Theater);
        SetPriority(m, op, fo.Entry);
    }

    /// <summary>The slot setter: on change the op's priority takes the value, and every team and decoy agent of this op gets the same priority.</summary>
    internal static void SetPriority(WorldModel m, MissionOp op, int value)
    {
        if (op.Priority == value) return;
        op.Priority = value;
        foreach (var a in op.Team.Concat(op.Decoys).Where(a => a.MissionOpKey == op.Key)) a.Priority = value;
    }

    /// <summary>Score: the class's skill sum over the team; 0 with the score-zero flag.</summary>
    public static int Score(MissionOp op)
    {
        if (op.ScoreZero) return 0;
        int Skill(int k) => op.Team.Sum(a => a.Skill(k));
        return op.Class.Family switch
        {
            0x55 or 0x57 or 0x56 => Skill(6),
            0x53 => Skill(op.Class.Subtype + 1),
            0x51 => Skill(0),
            0x54 or 0x52 => Skill(1),
            0x6A or 0x69 => (Skill(5) + Skill(1)) / 2,
            _ => Skill(5),
        };
    }

    // ---- members ----

    static void Assign(WorldModel m, MissionOp op, AiAgent a)
    {
        a.MissionOpKey = op.Key;
        a.CharacterOpKey = 0; // The character op's ownership ends.
        a.Priority = op.Class.Object ? op.Priority : 0; // System ops get 0; object ops take the op's priority.
        a.Unassigned = false;
        m.DirtyAgent(a); // Marks the agent dirty.
    }

    /// <summary>
    /// Frees the agent only when its record exists and it still belongs to this op; the caller removes it from the list either way.
    /// A detached record is not written or dirtied.
    /// </summary>
    static void Release(WorldModel m, MissionOp op, AiAgent a)
    {
        if (!m.Agents.ContainsKey(a.Agent.Ref) || a.MissionOpKey != op.Key) return;
        a.Unassigned = true;
        (a.MissionOpKey, a.CharacterOpKey, a.Priority) = (0, 0, 0);
        m.DirtyAgent(a);
    }

    /// <summary>Rejected only when located in a system with a Blockade and not Enroute.</summary>
    static bool Candidate(WorldModel m, AiAgent a) =>
        !(m.Where(a.Agent) is { } s && m.ViewParent(a.Agent) is StarSystem && !a.Enroute && m.Systems[s].Blockade);

    static bool UsableMember(AiAgent a) => !Busy(a);
    static bool MovableMember(AiAgent a) => !Moving(a);

    /// <summary>Agents a team does not take: with a command kind, on a mandatory mission, Captured, Injured or not Completed.</summary>
    internal static bool Busy(AiAgent a) => a.HasCommandKind || a.MandatoryMission || a.Captured || a.Injured || a.NotCompleted;
    /// <summary>As <see cref="Busy"/>, also OnMission or Enroute.</summary>
    internal static bool Moving(AiAgent a) => Busy(a) || a.OnMission || a.Enroute;
    static bool OwnAgent(AiAgent a) => a.Own && a.Unassigned;
    static bool OwnForce(AiAgent a) => OwnAgent(a) && a.SpecialForce;
    static int Diplomacy(AiAgent a) => a.Skill(SkillIndex.Diplomacy);
    static int Espionage(AiAgent a) => a.Skill(SkillIndex.Espionage);
    static int Combat(AiAgent a) => a.Skill(SkillIndex.Combat);
    static int Leadership(AiAgent a) => a.Skill(SkillIndex.Leadership);

    // ---- produce ----

    /// <summary>One state per call; returns (command, finished).</summary>
    public static (Command? Command, bool Finished) Produce(GameState game, WorldModel m, MissionOp op)
    {
        if (op.Class.Object) return ProduceObject(game, m, op);
        switch (op.State)
        {
            case 0:
                op.State = 1;
                return (null, false);
            case 1:
                if (op.Recall || op.Review) return End(op, Recall(m, op));
                if (op.Retired) { CleanUp(m, op); return End(op, null); }
                op.State = 2;
                return (null, false);
            case 2:
                Recheck(m, op);
                op.State = 3;
                return (null, false);
            case 3:
                TeamPick(m, op);
                op.State = 4;
                return (null, false);
            case 4:
                DecoyPick(m, op);
                op.State = 5;
                return (null, false);
            case 5:
                TargetSearch(game, m, op);
                op.State = 6;
                return (null, false);
            case 6:
                if (!StagingSearch(m, op)) return End(op, null);
                op.State = 7;
                return (null, false);
            case 7:
            {
                var move = Moves(m, op);
                if (op.AtStaging && op.TargetFound) { op.State = 8; return (move, false); }
                return End(op, move);
            }
            default: // 8
                return End(op, Mission(m, op));
        }
    }

    static (Command?, bool) End(MissionOp op, Command? c)
    {
        op.State = 0;
        return (c, true);
    }

    /// <summary>Releases all members, clears the diplomacy slot (diplomacy drops its claim), and removes the op (Inferred).</summary>
    static void CleanUp(WorldModel m, MissionOp op)
    {
        foreach (var a in op.Team.Concat(op.Decoys)) Release(m, op, a);
        op.Team.Clear();
        op.Decoys.Clear();
        if (op.Class.Family == 0x51 && m.Record(op.Target) is { } r) r.DiplomacyMark = false;
        m.Ops.Remove(op.Key);
        m.Dirty = true; // Retire move.
    }

    /// <summary>Releases members that are not usable, not this op's, not candidates, or refused by the class.</summary>
    static void Recheck(WorldModel m, MissionOp op)
    {
        op.KeptMember = false;
        foreach (var list in new[] { op.Team, op.Decoys })
            foreach (var a in list.ToList())
                if (!m.Agents.ContainsKey(a.Agent.Ref) || !UsableMember(a) || a.MissionOpKey != op.Key || !Candidate(m, a) || !Accept(m, op, a))
                {
                    Release(m, op, a);
                    list.Remove(a); // The two flags stay until the next team or decoy pick.
                }
                else op.KeptMember = true;
    }

    /// <summary>The slot's value is 1, except for diplomacy: rebuild its list and refuse a member below the list's best Diplomacy.</summary>
    static bool Accept(WorldModel m, MissionOp op, AiAgent a)
    {
        if (op.Class.Family != 0x51) return true;
        var list = TeamLists(m, op)[0];
        return list.Count == 0 || Diplomacy(a) >= list.Entries.Max(e => e.Value);
    }

    /// <summary>The class's team lists; recon and espionage add the staging distance list when staging is a system.</summary>
    static List<RankedList> TeamLists(WorldModel m, MissionOp op)
    {
        var c = op.Class;
        if (c.Object)
        {
            Func<AiAgent, bool> espionage = a => OwnForce(a) && a.Can.Espionage && !Busy(a), recon = a => OwnAgent(a) && a.Can.Recon && !Busy(a),
                sabotage = a => OwnAgent(a) && a.Can.Sabotage && !Busy(a) && !a.VictoryCharacter && !a.Specialist,
                other = a => OwnForce(a) && op.TeamCapability(a) && !Busy(a) && !a.VictoryCharacter;
            return c.Family switch
            {
                0x52 => WithStaging(m, op, m.AgentList(espionage, Espionage, 51, 10000, 1), espionage),
                0x54 => WithStaging(m, op, m.AgentList(recon, Espionage, 0, 10000, 1), recon),
                0x69 => [m.AgentList(sabotage, Combat, 51, 10000, 1), .. WithStaging(m, op, m.AgentList(sabotage, Espionage, 51, 10000, 1), sabotage)],
                _ => WithStaging(m, op, m.AgentList(other, Combat, 51, 10000, 1), other),
            };
        }
        Func<AiAgent, bool> systemRecon = a => OwnAgent(a) && a.Can.Recon && !Busy(a), systemEspionage = a => OwnForce(a) && a.Can.Espionage && !Busy(a);
        return c.Family switch
        {
            0x55 => [m.AgentList(a => a.Own && a.Can.Recruitment && !Busy(a), Leadership, 80, 10000, 1, minPri: true)],
            0x53 => [m.AgentList(a => a.Own && Researcher(a, c.Subtype) && !Busy(a), a => a.Skill(c.Subtype + 1), 10, 10000, 1, minPri: true)],
            0x51 => [m.AgentList(a => a.Own && a.Can.Diplomacy && !Busy(a) && !a.MissionSent, Diplomacy, 69, 10000, 1, minPri: true)],
            0x57 => [m.AgentList(a => a.Own && a.Can.Subdue && !Busy(a), Leadership, 49, 10000, 1, minPri: true)],
            0x56 => [m.AgentList(a => a.Own && a.Can.Incite && !Busy(a) && !a.VictoryCharacter && !a.Specialist, Leadership, 49, 10000, 1, minPri: true)],
            0x54 => WithStaging(m, op, m.AgentList(systemRecon, Espionage, 0, 10000, 1), systemRecon),
            0x52 => WithStaging(m, op, m.AgentList(systemEspionage, Espionage, 51, 10000, 1), systemEspionage),
            0x61 => [m.AgentList(a => a.Own && a.Can.Rescue && !Busy(a) && !a.VictoryCharacter && !a.Specialist && !a.Can.Commanding, Combat, 51, 10000, 1, minPri: true)],
            0x62 => [m.AgentList(a => a.Own && a.Can.Abduction && !Busy(a) && !a.Specialist && !a.Can.Commanding, Combat, 51, 10000, 1, minPri: true)],
            _ => [m.AgentList(a => a.Own && a.Can.DeathStarSabotage && !Busy(a) && !a.VictoryCharacter, Combat, 51, 10000, 1, minPri: true),
                m.AgentList(a => a.Own && a.Can.DeathStarSabotage && !Busy(a) && !a.VictoryCharacter, Espionage, 51, 10000, 1, minPri: true)],
        };
    }

    /// <summary>The research capability of research subtype 1 (ships), 2 (troops) or 3 (facilities).</summary>
    static bool Researcher(AiAgent a, int subtype) => subtype switch { 1 => a.Can.ShipDesign, 2 => a.Can.TroopTraining, _ => a.Can.FacilityDesign };

    /// <summary>The staging distance list (direction 2), with the companion list's test.</summary>
    static List<RankedList> WithStaging(WorldModel m, MissionOp op, RankedList list, Func<AiAgent, bool> pass) =>
        op.Staging is { } s ? [list, m.AgentDistanceList(s, pass, 2)] : [list];

    /// <summary>The team slot: its flag is cleared at entry; while the team is below its size, lists are popped in rank-sum order; agents that pass the rejection check join; when full, the flag is set.</summary>
    static void TeamPick(WorldModel m, MissionOp op)
    {
        op.TeamComplete = false;
        if (op.Team.Count >= op.Class.TeamSize) { op.TeamComplete = true; return; }
        var stack = new ListStack();
        foreach (var l in TeamLists(m, op)) stack.Push(l);
        while (op.Team.Count < op.Class.TeamSize && stack.Pick() is AiAgent a)
            if (Family.Of(a.Agent.Type) is >= 0x30 and < 0x40 && Candidate(m, a))
            {
                Assign(m, op, a);
                Insert(op.Team, a);
                op.Failures = 0;
            }
        if (op.Team.Count >= op.Class.TeamSize) op.TeamComplete = true;
    }

    /// <summary>Team and decoy containers are ordered trees: every walk visits members in ascending SdRef (Inferred for all walks).</summary>
    static void Insert(List<AiAgent> list, AiAgent a)
    {
        int i = list.FindIndex(x => x.Agent.Ref > a.Agent.Ref);
        list.Insert(i < 0 ? list.Count : i, a);
    }

    /// <summary>
    /// Decoy slot: none for most classes. Incite and object classes take n = (the target's figure + 2) div 2; rescue, abduction
    /// and Death Star take 4. Candidates come from the pick lists, plus the staging distance list. The flag is cleared at entry;
    /// picks happen only while below n; the flag is set only with a count of at least n, and then above n the first decoy is
    /// released once.
    /// </summary>
    static void DecoyPick(WorldModel m, MissionOp op)
    {
        // Object classes: espionage uses requirement 8, abduction and assassination use the op's mask, sabotage 4, recon none; n = (the target's figure + 2) div 2
        Func<AiAgent, bool>? capability = op.Class.Object
            ? op.Class.Family switch { 0x52 => a => a.Can.Espionage, 0x54 => null, 0x69 => a => a.Can.Sabotage, _ => op.TeamCapability }
            : op.Class.Family switch { 0x56 => a => a.Can.Incite, 0x61 => a => a.Can.Rescue, 0x62 => a => a.Can.Abduction, 0x6A => a => a.Can.DeathStarSabotage, _ => null };
        if (capability is null)
        {
            if (op.Class.Family == 0x51 && op.Decoys.Count > 0) { Release(m, op, op.Decoys[0]); op.Decoys.RemoveAt(0); }
            op.DecoysDone = true;
            return;
        }
        op.DecoysDone = false;
        int n = op.Class.Family == 0x56 || op.Class.Object ? ((m.Record(op.Target)?.EnemyUnits ?? 0) + 2) / 2 : 4;
        if (op.Decoys.Count < n)
        {
            var stack = new ListStack();
            Func<AiAgent, bool> decoy = a => OwnForce(a) && capability(a) && !Busy(a);
            foreach (var l in WithStaging(m, op, m.AgentList(decoy, Espionage, 51, 10000, 1), decoy)) stack.Push(l);
            while (op.Decoys.Count < n && stack.Pick() is AiAgent a)
                if (Candidate(m, a))
                {
                    Assign(m, op, a);
                    Insert(op.Decoys, a);
                }
        }
        if (op.Decoys.Count < n) return;
        op.DecoysDone = true;
        if (op.Decoys.Count > n) { Release(m, op, op.Decoys[0]); op.Decoys.RemoveAt(0); }
    }

    // ---- target search ----

    static AiSystem? Pick(params RankedList[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) stack.Push(l);
        return stack.Pick() as AiSystem;
    }

    static AiSector? PickSector(params RankedList[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) stack.Push(l);
        return stack.Pick() as AiSector;
    }

    static void TargetSearch(GameState game, WorldModel m, MissionOp op)
    {
        op.TargetFound = false;
        // only recruitment, research 1-3, subdue, incite and diplomacy need the complete team; recon, espionage,
        // rescue, abduction and Death Star sabotage search on every pass.
        if (!op.TeamComplete && op.Class.Family is not (0x54 or 0x52 or 0x61 or 0x62 or 0x6A)) return;
        var target = op.Class.Family switch
        {
            0x55 => Recruitment(m, op),
            0x53 => op.Class.Subtype switch
            {
                1 => Research(m, op, r => r.ShipProduction, x => x.HasShipProduction, x => x.ShipProduction),
                2 => Research(m, op, r => r.TrainingProduction, x => x.HasTrainingProduction, x => x.TrainingProduction),
                _ => Research(m, op, r => r.ConstructionProduction, x => x.HasConstructionProduction, x => x.ConstructionProduction),
            },
            0x57 => Research(m, op, r => r.UprisingNoFleet, x => x.HasUprisingNoFleet, x => x.UprisingNoFleet),
            0x51 => Diplomacy(m, op),
            0x54 => Recon(game, m, op),
            0x52 => Espionage(m, op),
            0x56 => Incite(m, op),
            0x61 or 0x62 => RescueOrAbduction(m, op),
            _ => DeathStar(m, op),
        };
        if (target is null) return;
        op.Target = target.System;
        if (op.Class.Family is not (0x61 or 0x62 or 0x6A)) op.Objective = target.System; // Inferred: system classes send to the target system
        op.TargetFound = true;
    }

    static AiSystem? Keep(WorldModel m, MissionOp op, Func<AiSystem, bool> test) => m.Record(op.Target) is { } r && test(r) ? r : null;

    /// <summary>Recruitment: keep while the target is a character site; else the first member's system when it is one; else by staging distance or by values.</summary>
    static AiSystem? Recruitment(WorldModel m, MissionOp op) =>
        Keep(m, op, r => r.CharacterSite)
        ?? (op.Team.Count > 0 && m.Where(op.Team[0].Agent) is { } at && m.Systems[at].CharacterSite ? m.Systems[at] : null)
        ?? ByStagingOrValue(m, op, x => x.HasCharacterSite, x => x.CharacterSites, r => r.CharacterSite);

    /// <summary>Research and subdue: keep while the flag is set; staging: sector distance to the target plus system distance; else the sector by its count, then the system by support.</summary>
    static AiSystem? Research(WorldModel m, MissionOp op, Func<AiSystem, bool> b, Func<AiSector, bool> s, Func<AiSector, int> count) =>
        Keep(m, op, b) ?? ByStagingOrValue(m, op, s, count, b);

    static AiSystem? ByStagingOrValue(WorldModel m, MissionOp op, Func<AiSector, bool> sectorBit, Func<AiSector, int> count, Func<AiSystem, bool> systemBit)
    {
        if (op.Staging is { } staging)
            return PickSector(m.SectorDistanceList(staging, sectorBit, 2)) is { } sector
                ? Pick(m.SystemDistanceList(sector, staging, systemBit, 2)) : null;
        return PickSector(m.SectorList(sectorBit, count, 1)) is { } x ? Pick(m.SystemList(x, systemBit, r => r.Support, 1)) : null;
    }

    /// <summary>Diplomacy: keep while its flag is set and the system has no mark; five steps; the target gets the diplomacy claim.</summary>
    static AiSystem? Diplomacy(WorldModel m, MissionOp op)
    {
        var found = Keep(m, op, r => r.DiplomacyTarget && !r.EnemyFleet);
        static bool none(AiSystem r) => r.DiplomacyMark;
        if (found is null && PickSector(m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Own, x => x.Support, 2)) is { } s1 && s1.Support < 70)
            found = Pick(m.SystemRandomList(s1, r => r.DiplomacyTarget && r.Own && !none(r), 2));
        if (found is null && op.Staging is { } staging)
        {
            if (PickSector(m.SectorDistanceList(staging, x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, 2)) is { } s2)
                found = Pick(m.SystemRandomList(s2, r => r.DiplomacyTarget && r.Neutral && !none(r), 2));
            if (found is null && m.Record(staging)?.Sector is { } own)
                found = Pick(m.SystemDistanceList(own, staging, r => r.DiplomacyTarget && !none(r), 2));
        }
        if (found is null && PickSector(m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, x => x.OwnSystems, 1), m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, x => x.NeutralSystems, 1),
                m.SectorRandomList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, 1)) is { } s4)
            found = Pick(m.SystemRandomList(s4, r => r.DiplomacyTarget && r.Neutral && !none(r), 1));
        if (found is null && PickSector(m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget, x => x.OwnSystems, 1), m.SectorRandomList(x => x.HasDiplomacyTarget, 1)) is { } s5)
            found = Pick(m.SystemRandomList(s5, r => r.DiplomacyTarget && !none(r), 2));
        if (found is not null) found.DiplomacyMark = true;
        return found;
    }

    /// <summary>
    /// Recon: keep while the flag is set; else the old marked sector loses its recon mark; a sector by random keys (or by staging
    /// distance; Inferred: whenever staging is a system) becomes the op's marked sector, none included, marked only when a system
    /// is found. Its systems come by days since the last visit (model count below 1) or by random keys (direction 2 with staging,
    /// direction 1 without).
    /// </summary>
    static AiSystem? Recon(GameState game, WorldModel m, MissionOp op)
    {
        if (Keep(m, op, r => r.NotOwn) is { } kept) return kept;
        if (op.Marked is { } old) old.ReconMark = false;
        bool few = m.SectorsWithoutKey < 1;
        bool excl(AiSector x) => x.ReconMark || !few && x.AllExplored || x.Theater;
        int dir = few ? 1 : 2;
        var sector = PickSector(op.Staging is { } st ? m.SectorDistanceList(st, x => x.HasNotOwn && !excl(x), 2) : m.SectorRandomList(x => x.HasNotOwn && !excl(x), dir));
        op.Marked = sector; // None included.
        if (sector is null) return null;
        var found = few ? Pick(m.SystemDaysList(sector, r => r.NotOwn, 1))
            : Pick(m.SystemRandomList(sector, r => r.NotOwn && !r.Explored, op.Staging is null ? 1 : 2));
        if (found is not null) sector.ReconMark = true; // mark only with a target
        return found;
    }

    /// <summary>Espionage: keep while the system is enemy and the flag is set; else the old sector loses its espionage mark; a sector by random keys (staging: distance) becomes the op's marked sector; systems by days; the mark is set only with a target.</summary>
    static AiSystem? Espionage(WorldModel m, MissionOp op)
    {
        if (Keep(m, op, r => r.Enemy && r.Claimed) is { } kept) return kept;
        if (op.Marked is { } old) old.EspionageMark = false;
        static bool req(AiSector x) => x.HasClaimed && x.Enemy;
        static bool excl(AiSector x) => x.EspionageMark || x.Theater;
        var sector = PickSector(op.Staging is { } st ? m.SectorDistanceList(st, x => req(x) && !excl(x), 2) : m.SectorRandomList(x => req(x) && !excl(x), 1));
        op.Marked = sector; // None included.
        if (sector is null) return null;
        var found = Pick(m.SystemDaysList(sector, r => r.Claimed && r.Enemy, 1));
        if (found is not null) sector.EspionageMark = true; // mark only with a target
        return found;
    }

    /// <summary>Incite: keep while the flag is set and the system has no mark; staging distance lists, else values and random keys.</summary>
    static AiSystem? Incite(WorldModel m, MissionOp op)
    {
        if (Keep(m, op, r => r.EnemySupported && !r.EnemyFleet) is { } kept) return kept;
        if (op.Staging is { } st)
            return PickSector(m.SectorDistanceList(st, x => x.HasEnemySupported && x.Enemy, 2)) is { } s ? Pick(m.SystemDistanceList(s, st, r => r.EnemySupported && r.Enemy, 2)) : null;
        return PickSector(m.SectorList(x => x.HasEnemySupported && x.Enemy, x => x.EnemySystems, 1), m.SectorRandomList(x => x.HasEnemySupported && x.Enemy, 1)) is { } x
            ? Pick(m.SystemList(x, r => r.EnemySupported && r.Enemy, r => r.Support, 1)) : null;
    }

    /// <summary>The class's decoy count for rescue, abduction and Death Star.</summary>
    const int DecoyCap = 4;

    /// <summary>Decoys with an agent record, none of the agent's status flags set, and an agent location equal to the op's staging.</summary>
    static int DecoysAtStaging(WorldModel m, MissionOp op) =>
        op.Decoys.Count(d => m.Agents.ContainsKey(d.Agent.Ref) && MovableMember(d) && d.Location == op.Staging);

    /// <summary>
    /// Rescue and abduction: sectors by value; per sector in pick order two system lists go into one set; the first set system
    /// (key order) that the decoys at staging can handle is used; one random draw over the candidates (own captured characters or
    /// enemy majors in the system, usable views only; aboard non-own fleets: not collected). Rescue's keep test never passes.
    /// </summary>
    static AiSystem? RescueOrAbduction(WorldModel m, MissionOp op)
    {
        bool rescue = op.Class.Family == 0x61;
        if (!rescue && op.Target is StarSystem && op.Objective is Agent o)
        {
            // Objective cleared first; kept only while the record's location, resolved through the side's view parents, is the target
            op.Objective = null;
            if (m.Agents.TryGetValue(o.Ref, out var ar) && ar.Location is { } l && m.Where(l) == op.Target
                && ar.Major && ar.Enemy && !(ar.Captured || ar.Enroute)
                && m.Record(op.Target) is { } keep)
            {
                op.Objective = o;
                return keep;
            }
        }
        (op.Objective, op.Target) = (null, null);
        var sectors = new ListStack();
        sectors.Push(rescue ? m.SectorList(x => x.HasOwnCaptured, x => x.OwnCaptured, 1)
            : m.SectorList(x => x.HasEnemyMajor, x => x.EnemyMajors, 1));
        var set = new SortedSet<AiSystem>(Comparer<AiSystem>.Create((a, b) => a.System.Ref.CompareTo(b.System.Ref)));
        Func<AiSystem, int> value = rescue ? r => r.EnemyUnits : r => r.EnemyMajors;
        while (sectors.Pick() is AiSector s)
        {
            if (Pick(m.SystemList(s, r => rescue ? r.OwnCaptured : r.EnemyMajor, value, 1)) is { } a) set.Add(a);
            if (Pick(m.SystemList(s, r => rescue ? r.OwnCapturedAboard : r.EnemyMajorAboard, value, 2)) is { } b) set.Add(b);
        }
        int n = DecoysAtStaging(m, op);
        // The first passing system becomes the target and the walk stops there, candidate or not (the target search keeps the target)
        if (set.FirstOrDefault(r => n == DecoyCap || r.EnemyUnits / 2 <= n) is not { } r) return null;
        op.Target = r.System;
        // The iterator walks usable views only
        var candidates = m.Agents.Values.Where(x => x.Character && m.Where(x.Agent) == r.System && m.ViewParent(x.Agent) == r.System
            && m.Usable(x.Agent) && (rescue ? x.Own && x.Captured
                : x.Enemy && x.Major)).ToList();
        if (candidates.Count == 0) return null;
        int i = m.Rand(candidates.Count);
        if (i >= candidates.Count) return null;
        op.Objective = candidates[i].Agent;
        return r;
    }

    /// <summary>
    /// Death Star sabotage: an objective ship with a ship record gives its fleet's location. Else the non-own Death Star fleets
    /// (sorted by their second value, direction 2) are all popped, their locations taken in order, and the first the team can
    /// handle (rescue rule) is used. Ship choice: the first non-own Death Star fleet there, and its first ship record of class 0x80.
    /// </summary>
    static AiSystem? DeathStar(WorldModel m, MissionOp op)
    {
        if (op.Objective is { } o && Family.Of(o.Type) is >= 0x18 and < 0x1C && m.Ships.ContainsKey(o.Ref) && m.Record(m.Where(o)) is { } at) return at;
        op.Objective = null;
        var stack = new ListStack();
        stack.Push(m.FleetList(f => f.DeathStar && !f.Own, f => f.Units, 2));
        int n = DecoysAtStaging(m, op);
        while (stack.Pick() is AiFleet f)
        {
            if (m.Record(f.Location) is not { } r || !(n == DecoyCap || r.EnemyUnits / 2 <= n) || !r.EnemyDeathStarSeen) continue;
            var ship = m.FleetRecords().Where(x => !x.Own && x.DeathStar && x.Location == r.System)
                .SelectMany(m.ShipsOf).FirstOrDefault(s => s.DeathStar);
            if (ship is null) continue;
            op.Objective = ship.Ship;
            return r;
        }
        return null;
    }

    // ---- staging ----

    static bool OwnFree(AiSystem r) => r.Own && !r.Blockade;

    /// <summary>
    /// Staging. The staging-found flag is cleared first; if the team is not full, staging is set to none. Steps: (1) a recorded
    /// staging system that is own and free sets the flag, else staging is set to none (a staging system with no record is left as
    /// it is). (2) With the flag clear and a recorded target system that is own and free, staging becomes the target; else staging
    /// is the target sector's random pick, and when that is none, the nearest search runs (its null results are written). (3) A
    /// staging system with no record test sets the flag. (4) Else the first team member whose agent location, resolved through
    /// the side's view parents to its system, is own and not blockaded sets the flag and becomes the staging system.
    /// </summary>
    static bool StagingSearch(WorldModel m, MissionOp op)
    {
        op.StagingFound = false;
        if (!op.TeamComplete) { op.Staging = null; return false; } // Team not full: staging is none.
        if (m.Record(op.Staging) is { } old)
        {
            if (OwnFree(old)) op.StagingFound = true;
            else op.Staging = null;
        }
        if (!op.StagingFound && m.Record(op.Target) is { } t)
        {
            if (OwnFree(t)) op.Staging = t.System;
            else if (t.Sector is { } sector)
            {
                op.Staging = Pick(m.SystemRandomList(sector, r => r.Own && !r.Blockade, 1))?.System;
                op.Staging ??= Nearest(m, sector)?.System;
            }
            else op.Staging = null;
        }
        if (op.Staging is not null) { op.StagingFound = true; return true; } // Step 3: no record test.
        var s = op.Team.Select(a => a.Location is { } l ? m.Record(m.Where(l)) : null).FirstOrDefault(r => r is not null && OwnFree(r)); // The agent's location gives its system.
        if (s is null) return false;
        op.Staging = s.System;
        op.StagingFound = true;
        return true;
    }

    /// <summary>Nearest search: every other sector in key order closer than the best so far gets a random-key list (own, excluding marked systems); a pick sets the best.</summary>
    static AiSystem? Nearest(WorldModel m, AiSector from) => Nearest(m, from, r => r.Own && !r.EnemyFleet);

    /// <summary>Nearest search with the caller's masks: every other sector closer than the best so far gets a random-key system list.</summary>
    internal static AiSystem? Nearest(WorldModel m, AiSector from, Func<AiSystem, bool> pass)
    {
        AiSystem? best = null;
        long bestDistance = long.MaxValue;
        foreach (var s in m.Sectors.Where(x => x != from))
        {
            long dx = s.Sector.Definition.X - from.Sector.Definition.X, dy = s.Sector.Definition.Y - from.Sector.Definition.Y;
            long d = dx * dx + dy * dy;
            if (d >= bestDistance) continue;
            if (Pick(m.SystemRandomList(s, pass, 1)) is { } pick) (best, bestDistance) = (pick, d);
        }
        return best;
    }

    /// <summary>
    /// Every movable member whose agent location (the first location seen, never refreshed) is not the staging system goes into
    /// one move command (team, then decoys). The flag is cleared at entry, and set only by a team member at staging.
    /// </summary>
    static Command? Moves(WorldModel m, MissionOp op)
    {
        var go = new List<GameObject>();
        op.AtStaging = false;
        foreach (var a in op.Team.Where(MovableMember))
            if (a.Location == op.Staging) op.AtStaging = true;
            else go.Add(a.Agent);
        foreach (var a in op.Decoys.Where(MovableMember))
            if (a.Location != op.Staging) go.Add(a.Agent);
        return go.Count == 0 || op.Staging is null ? null : new Command(0x201, op.Side) { Targets = [.. go.Select(g => (g, 0))], ArgRef = op.Staging };
    }

    /// <summary>
    /// Mission request, command 0x240, with the objective, the mission type and the op key as its argument. Team and decoys whose
    /// agent is neither Enroute, on a mission, nor captured are each marked on mission. No 0x240 with an empty team (Inferred: the
    /// builder's own test).
    /// </summary>
    static Command? Mission(WorldModel m, MissionOp op)
    {
        if (op.Objective is not { } objective || Family.Of(objective.Type) is 0 or 0xFF) return null;
        var team = op.Team.Where(MovableMember).ToList();
        var decoys = op.Decoys.Where(MovableMember).ToList();
        if (team.Count == 0) return null;
        foreach (var a in team.Concat(decoys)) a.MissionSent = true;
        return new Command(0x240, op.Side)
        {
            Targets = [.. team.Select(a => ((GameObject)a.Agent, 0))], Decoys = [.. decoys.Select(a => (GameObject)a.Agent)],
            ArgRef = objective, Fields = [(uint)op.Key, 0, op.Class.MissionType],
        };
    }

    /// <summary>Review: only with the mission view at phase 8; review without a valid target turns into recall; recall is command 0x250 for the mission.</summary>
    static Command? Recall(WorldModel m, MissionOp op)
    {
        Command? c = null;
        if (op.Mission is { Phase: 8 } mission)
        {
            if (op.Review && !(m.Record(op.Target) is { } r && Valid(op, r))) op.Recall = true;
            if (op.Recall) c = new Command(0x250, op.Side) { Targets = [(mission, 0)] };
        }
        op.Recall = op.Review = false;
        return c;
    }

    /// <summary>The class's check that the target is still valid (Inferred: the class's keep test).</summary>
    static bool Valid(MissionOp op, AiSystem r) => op.Class.Family switch
    {
        0x55 => r.CharacterSite, 0x51 => r.DiplomacyTarget && !r.EnemyFleet, 0x56 => r.EnemySupported && !r.EnemyFleet,
        0x57 => r.UprisingNoFleet, _ => true,
    };

    // ---- object-kind produce ----

    static (Command?, bool) ProduceObject(GameState game, WorldModel m, MissionOp op)
    {
        switch (op.State)
        {
            case 0:
                op.State = 1;
                return (null, false);
            case 1:
                if (op.Recall || op.Review) return End(op, Recall(m, op));
                if (op.Retired) { CleanUp(m, op); return End(op, null); }
                op.State = 2;
                return (null, false);
            case 2:
                op.State = ObjectTarget(game, m, op) ? 3 : 9;
                return (null, false);
            case 3:
                op.State = ObjectStaging(m, op) ? 4 : 9;
                return (null, false);
            case 4:
                Recheck(m, op);
                op.State = 5;
                return (null, false);
            case 5:
                TeamPick(m, op);
                op.State = 6;
                return (null, false);
            case 6:
                DecoyPick(m, op);
                op.State = 7;
                return (null, false);
            case 7:
            {
                var move = ObjectMoves(m, op);
                if (op.AtStaging) { op.State = 8; return (move, false); }
                return End(op, move);
            }
            case 8:
                return End(op, Mission(m, op));
            default: // 9
                if (!(op.Recall || op.Review || op.Running))
                {
                    foreach (var a in op.Team.Concat(op.Decoys)) Release(m, op, a);
                    op.Team.Clear();
                    op.Decoys.Clear(); // The two flags stay untouched.
                }
                return End(op, null);
        }
    }

    /// <summary>
    /// Clears the objective and target, and the target-found flag. With an owning fleet op, the task picker is asked by mission
    /// type (sabotage, espionage, recon, else abduction and assassination), which selects the task's
    /// field. The target-found flag is set when the objective has a family from 1 to 0xFE and the target is a system.
    /// </summary>
    static bool ObjectTarget(GameState game, WorldModel m, MissionOp op)
    {
        (op.Objective, op.Target) = (null, null);
        op.TargetFound = false;
        if (op.FleetOp is not { Task: { } task, Theater: { } s }) return false;
        var (target, objective) = op.Class.MissionType switch
        {
            0x69000012 => SabotageTarget(m, task, s),
            0x52000013 => (Pick(m.SystemDaysList(s, r => r.Claimed && r.Enemy, 1)) ?? (task.Type >= 3 ? Pick(m.SystemRandomList(s, r => r.Claimed, 1)) : null)) is { } e
                ? (e, (GameObject?)e.System) : (null, null),
            0x54000015 => (Pick(m.SystemDaysList(s, r => r.NotOwn && !r.Explored, 1)) ?? Pick(m.SystemDaysList(s, r => r.NotOwn, 1))) is { } r
                ? (r, (GameObject?)r.System) : (null, null),
            _ => CharacterTarget(m, task, s),
        };
        (op.Target, op.Objective) = (target?.System, objective);
        if (objective is not null && Family.Of(objective.Type) is >= 1 and <= 0xFE && target is not null) op.TargetFound = true;
        return op.TargetFound;
    }

    /// <summary>
    /// Task picks for the object (task slot). Type 4 first keeps its site when its record has the site flags (no stage, no owner
    /// test). Types 3 to 6 then try stages in order, each only while no system was found: first a stage with two flags, then
    /// another stage with two other flags, and then each single flag in turn. Each pick uses the object's value (direction 2); type 3
    /// picks its first stage by random keys. The first system found goes to the candidate picker with its stage's mask; an empty
    /// candidate set does not fall through.
    /// </summary>
    static (AiSystem?, GameObject?) CharacterTarget(WorldModel m, FleetTask task, AiSector s)
    {
        int type = task.Type;
        if (type == 4 && m.Record(task.Site) is { } site && (site.EnemyCharacter || site.EnemyCharacterAboard)) return (site, Characters(m, site, true, a => a.Enemy));
        var stages = new List<(Func<AiSystem?> System, bool Here, Func<AiAgent, bool> Kind)>();
        if (type >= 3)
        {
            stages.Add((() => type == 3 ? Pick(m.SystemRandomList(s, r => r.EnemyCharacter && r.EnemyCommander, 1)) : Pick(m.SystemList(s, r => r.EnemyCharacter && r.EnemyCommander, r => r.EnemyUnits, 2)),
                true, type == 4 ? a => a.HasCommandKind : a => a.Enemy));
            stages.Add((() => Pick(m.SystemList(s, r => r.EnemyCharacterAboard && r.EnemyCommander, r => r.EnemyUnits, 2)), false, a => a.HasCommandKind));
        }
        stages.Add((() => Pick(m.SystemList(s, r => r.EnemyCharacter, r => r.EnemyUnits, 2)), true, a => a.Enemy));
        stages.Add((() => Pick(m.SystemList(s, r => r.EnemyCharacterAboard, r => r.EnemyUnits, 2)), false, a => a.Enemy));
        foreach (var (system, here, kind) in stages)
            if (system() is { } r) return (r, Characters(m, r, here, kind));
        return (null, null);
    }

    /// <summary>
    /// Candidate picker. With <paramref name="here"/>, the characters directly in the side's view of the system; with an enemy
    /// character aboard there, every usable fleet view there whose fleet record is not own and has an enemy character aboard, and the
    /// characters aboard its ship views. Kept characters: an enemy character of the stage's kind, not Enroute (captured is not
    /// tested); one random draw over the list.
    /// </summary>
    static GameObject? Characters(WorldModel m, AiSystem r, bool here, Func<AiAgent, bool> kind)
    {
        bool Fits(GameObject x) => Family.Of(x.Type) is >= 0x30 and < 0x40 && m.Agents.TryGetValue(x.Ref, out var a) && a.Character
            && a.Enemy && kind(a) && !a.Enroute;
        var list = new List<GameObject>();
        if (here) list.AddRange(m.Contents(r.System).Where(Fits));
        if (r.EnemyCharacterAboard)
            foreach (var fv in m.Contents(r.System).Where(x => x is Fleet && m.Usable(x)))
                if (m.FleetsById.GetValueOrDefault(fv.Ref) is { } f && !f.Own && (f.EnemyMajor || f.EnemyMinor))
                    foreach (var ship in m.Contents(fv)) list.AddRange(m.Contents(ship).Where(Fits));
        if (list.Count == 0) return null;
        int i = m.Rand(list.Count); // rand(n) can return n: no pick then (Inferred, as the guarded picks, e.g. WorldModel.PickProduct)
        return i < list.Count ? list[i] : null;
    }

    /// <summary>
    /// Task slot for the object per task type (types 1 to 6): stages (a flag, plus an optional requirement) by the object's value
    /// (direction 2), each only while no system was found, taking its top system; the stage flag is the mask the candidate step uses.
    /// Type 4 first keeps its site (task field) when it is not own and has the mark (objective = the site system); its first stage
    /// then still runs and a find replaces it (Inferred: an empty pick keeps the site).
    /// </summary>
    static (AiSystem?, GameObject?) SabotageTarget(WorldModel m, FleetTask task, AiSector s)
    {
        (SabotageKind, Func<AiSystem, bool>)[] hq = m.Side == Owner.Empire ? [(SabotageKind.Headquarters, r => r.HeadquartersSeen && r.Headquarters)] : [];
        (SabotageKind, Func<AiSystem, bool>) gencores = (SabotageKind.GenCores, r => r.EnemyGenCores && r.ManyGenCores),
            fleet = (SabotageKind.Fleet, r => r.EnemyFleetSeen && r.EnemyFleet), shields = (SabotageKind.DeathStarShield, r => r.EnemyDeathStarShield),
            batteries = (SabotageKind.Batteries, r => r.EnemyBatteries), anyGenCore = (SabotageKind.GenCores, r => r.EnemyGenCores),
            troops = (SabotageKind.Troops, r => r.EnemyTroops), fighters = (SabotageKind.Fighters, r => r.EnemyFighters),
            producers = (SabotageKind.Producers, r => r.EnemyProducers), mines = (SabotageKind.Mines, r => r.EnemyMines);
        (SabotageKind Kind, Func<AiSystem, bool> Pass)[] stages = task.Type switch
        {
            1 => [.. hq, producers, mines, troops, fleet, fighters],
            2 => [fighters, fleet, producers],
            3 => [gencores, shields, batteries, anyGenCore, troops, producers, fighters, fleet],
            4 or 5 => [gencores, shields, batteries, anyGenCore, troops, fighters, fleet],
            _ => [gencores, .. hq, shields, batteries, anyGenCore, troops, fighters, fleet],
        };
        AiSystem? kept = task.Type == 4 && m.Record(task.Site) is { } site && !site.Own && site.ManyGenCores ? site : null;
        for (int k = 0; k < stages.Length; k++)
        {
            if (kept is not null && k > 0) break;
            var (kind, pass) = stages[k];
            if (Pick(m.SystemList(s, pass, r => r.EnemyUnits, 2)) is { } r)
                return (r, SabotageObject(m, r, kind));
        }
        return kept is null ? (null, null) : (kept, kept.System);
    }

    /// <summary>
    /// Candidates of the class from the side's view of the system: usable iterators only, non-own (Inferred): HQ, generation cores,
    /// Death Star shields, the LNR (0x23) then KDY (0x22) batteries, troops, fighters, the ships of every non-own fleet record,
    /// training facilities, shipyards and construction yards, then mines and the first refinery. One random draw over them.
    /// </summary>
    /// <summary>What a sabotage stage looks for.</summary>
    enum SabotageKind { Headquarters, GenCores, DeathStarShield, Batteries, Troops, Fighters, Fleet, Producers, Mines }

    static GameObject? SabotageObject(WorldModel m, AiSystem r, SabotageKind kind)
    {
        var contents = m.Contents(r.System).Where(x => m.Usable(x) && m.See(x).Owner != m.Side).ToList();
        IEnumerable<GameObject> Of(params uint[] families) => families.SelectMany(f => contents.Where(x => Family.Of(x.Type) == f));
        List<GameObject> list = kind switch
        {
            SabotageKind.Headquarters => [.. Of(0x20, 0x21)],
            SabotageKind.GenCores => [.. Of(0x24)],
            SabotageKind.DeathStarShield => [.. Of(0x25)],
            SabotageKind.Batteries => [.. Of(0x23, 0x22)],
            SabotageKind.Troops => [.. contents.Where(x => Family.IsTroop(x.Type))],
            SabotageKind.Fighters => [.. contents.Where(x => Family.Of(x.Type) is >= 0x1C and < 0x20)],
            SabotageKind.Fleet => [.. contents.Where(x => x is Fleet && m.FleetsById.GetValueOrDefault(x.Ref) is { } f && !f.Own)
                .SelectMany(m.Contents).Where(x => Family.IsCapitalShip(x.Type) && m.Usable(x))],
            SabotageKind.Producers => [.. Of(0x29, 0x28, 0x2A)],
            _ => [.. Of(0x2C), .. Of(0x2D).Take(1)], // All mines, only the first refinery (a quirk of the original).
        };
        if (list.Count == 0) return null;
        int i = m.Rand(list.Count); // rand(n) can return n: no pick then (Inferred, as the guarded picks, e.g. WorldModel.PickProduct)
        return i < list.Count ? list[i] : null;
    }

    static bool OwnCalm(AiSystem r) => r.Own && !r.Unrest;

    /// <summary>
    /// Staging for the op, within its theater S. A staging system in S is kept while own and calm. Outside S, a staging system is
    /// kept only while the outside-theater flag is set and it is own and calm. Else the first team member's system in S that is
    /// own and calm; else (outside-theater flag cleared) the nearest own system in S that is not blockaded; else the nearest search
    /// over S for own systems without the mark, with the outside-theater flag set. Returns the staging-found flag.
    /// </summary>
    static bool ObjectStaging(WorldModel m, MissionOp op)
    {
        op.StagingFound = false;
        if (op.Theater is not { } s) return false;
        if (m.Record(op.Staging) is { } kept)
        {
            if (kept.Sector == s ? OwnCalm(kept) : op.OutsideTheater && OwnCalm(kept))
            {
                op.StagingFound = true;
                if (kept.Sector == s) op.OutsideTheater = false;
                return true;
            }
            op.Staging = null;
        }
        foreach (var a in op.Team)
            if (m.Record(m.Where(a.Agent)) is { } r && r.Sector == s && OwnCalm(r))
            {
                op.Staging = r.System;
                (op.StagingFound, op.OutsideTheater) = (true, false);
                return true;
            }
        op.OutsideTheater = false;
        if (Pick(m.SystemRandomList(s, r => r.Own && !r.Blockade, 1)) is { } pick) { op.Staging = pick.System; op.StagingFound = true; return true; }
        if (Nearest(m, s, r => r.Own && !r.EnemyFleet) is { } near) { op.Staging = near.System; op.StagingFound = op.OutsideTheater = true; return true; }
        return false;
    }

    /// <summary>
    /// Every movable team member, then decoy, not at staging (agent location) goes into one move command to the staging system.
    /// The staging-found flag is set only when every member with a record is movable and at staging, the team is non-empty, and
    /// the team-complete and decoys-done flags are set.
    /// </summary>
    static Command? ObjectMoves(WorldModel m, MissionOp op)
    {
        op.AtStaging = false;
        var members = op.Team.Concat(op.Decoys)
            .Select(a => m.Agents.GetValueOrDefault(a.Agent.Ref)).OfType<AiAgent>().ToList(); // By key; no record: skipped.
        var go = members.Where(a => MovableMember(a) && a.Location != op.Staging).Select(a => (GameObject)a.Agent).ToList();
        if (op.Team.Count > 0 && op.TeamComplete && op.DecoysDone && members.All(a => MovableMember(a) && a.Location == op.Staging)) op.AtStaging = true;
        return go.Count == 0 || op.Staging is null ? null : new Command(0x201, op.Side) { Targets = [.. go.Select(g => (g, 0))], ArgRef = op.Staging };
    }

    // ---- messages 0x1F0 / 0x1F1 ----

    /// <summary>The op of a mission, found by its mission key.</summary>
    public static MissionOp? Of(WorldModel m, GameObject mission) => m.Ops.Values.FirstOrDefault(o => o.Mission == mission);

    /// <summary>Status 0x1F1 with CompletionStatus 3 to 10: the mission is done, and it moves into the finished set.</summary>
    public static void Completion(WorldModel m, Mission mission)
    {
        if (Of(m, mission) is { } op && mission.CompletionStatus is >= 3 and <= 10) Done(op);
    }

    static void Done(MissionOp op)
    {
        op.Running = false;
        if (op.Mission is { } mission) op.Finished.Add(mission);
    }

    /// <summary>Counts one failure; above the max failures the gave-up flag is set and the count resets to 0; returns the excess.</summary>
    static bool FailureCount(MissionOp op)
    {
        if (++op.Failures <= op.Class.MaxFailures) return false;
        op.GaveUp = true;
        op.Failures = 0;
        return true;
    }

    /// <summary>
    /// Mission-end cleanup for the op's mission view. The recall, review and mission-running flags are cleared. Team members lose
    /// their on-mission mark (and are dirtied) and stay only while the gave-up flag is clear and they are neither injured nor
    /// captured; with the gave-up flag the staging is dropped, then the flag is cleared. Decoys lose their on-mission mark (and are
    /// dirtied) and leave when injured or captured. The team-complete and decoys-done flags stay (only the picks write them).
    /// </summary>
    public static void MissionEnded(WorldModel m, GameObject mission)
    {
        // openswr bookkeeping: the finished sets drop the mission (its view is destroyed now, which the finished-set reader also tests)
        foreach (var o in m.Ops.Values) o.Finished.Remove(mission);
        if (Of(m, mission) is not { } op) return;
        op.Recall = op.Review = op.Running = false;
        bool fail = op.GaveUp;
        foreach (var (list, all) in new[] { (op.Team, fail), (op.Decoys, false) })
            foreach (var a in list.ToList())
            {
                a.MissionSent = false;
                m.DirtyAgent(a);
                if (!all && !a.Injured && !a.Captured) continue;
                Release(m, op, a);
                list.Remove(a);
            }
        if (fail) op.Staging = null;
        op.GaveUp = false;
    }

    /// <summary>0x1F0 TaskStatus per class: common, the rescue / abduction / Death Star / recruitment variant, recon / espionage.</summary>
    public static void TaskStatus(GameState game, WorldModel m, Mission mission)
    {
        if (Of(m, mission) is not { } op) return;
        int status = mission.TaskStatus;
        if (op.Class.Object)
        {
            // Espionage and recon with status 3: the target's day is set to today and the failure count resets; abduction, assassination and sabotage use their own handler
            if (status == 0) op.Running = true;
            else if (status == 2)
            {
                if (op.Class.Family == 0x52) FailureCount(op); // Espionage counts the failure.
                Done(op);
                if (op.Class.Family is not (0x52 or 0x54)) op.Failures++;
            }
            else if (status == 3)
            {
                if (op.Class.Family is 0x52 or 0x54 && m.Record(op.Target) is { } tr) tr.BlockadeDay = game.Day;
                op.Failures = 0;
                Done(op);
            }
            return;
        }
        switch (op.Class.Family)
        {
            case 0x54 or 0x52:
                if (op.Class.Family == 0x52 && status is 2 or 3) op.Staging = null; // Espionage also clears the staging system.
                if (status == 2) { op.Target = null; Done(op); }
                else if (status == 3)
                {
                    if (m.Record(op.Target) is { } r) r.BlockadeDay = game.Day;
                    (op.Target, op.Failures) = (null, 0);
                    Done(op);
                }
                break;
            case 0x61 or 0x62 or 0x6A or 0x55:
                if (status == 0) op.Running = true;
                else if (status == 2) { Done(op); op.Failures++; }
                else if (status == 3) { Done(op); op.Failures = 0; }
                break;
            default:
                if (status == 0) op.Running = true;
                else if (status == 2)
                {
                    Done(op);
                    if (FailureCount(op)) op.Recall = true;
                    else op.Review = true;
                }
                else if (status == 3) { Done(op); op.Failures = 0; op.Review = true; }
                break;
        }
    }
}

    /// <summary>
    /// Planner 4, the mission-operations manager. State 0 is empty. State 1 handles retire and create requests. States 2 and 3
    /// handle fleet-operation requests: the fleet op's retire, create and attach. State 5 picks the next selectable op, and state
    /// 6 runs its produce.
    /// </summary>
public sealed class MissionOpPlanner() : Planner(4)
{
    int _next; // The next op key to examine (0 means the first).
    int _current;
    int _fleetOp, _nextFleetOp;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        switch (State)
        {
            case 0:
                State = 1;
                return null;
            case 1:
                if (m.MissionRetireKey != 0 && m.Ops.TryGetValue(m.MissionRetireKey, out var old)) MissionOps.Retire(m, old);
                m.MissionRetireKey = 0;
                if (m.MissionRequestKind != 0) MissionOps.Create(m, (uint)m.MissionRequestKind, m.MissionRequestSubtype);
                m.MissionRequestKind = 0;
                State = 2;
                return null;
            case 2: // From the next fleet-op key (or the first), the first fleet op with a request; no wrap.
            {
                var fo = m.FleetOps.Values.FirstOrDefault(o => (_nextFleetOp == 0 || o.Key >= _nextFleetOp) && (o.RequestKind != 0 || o.RetireKey != 0));
                if (fo is null) { (_fleetOp, _nextFleetOp, State) = (0, 0, 5); return null; }
                _fleetOp = fo.Key;
                _nextFleetOp = m.FleetOps.Keys.FirstOrDefault(k => k > fo.Key);
                State = 3;
                return null;
            }
            case 3:
                if (m.FleetOps.TryGetValue(_fleetOp, out var req))
                {
                    if (req.RetireKey != 0 && m.Ops.TryGetValue(req.RetireKey, out var gone)) MissionOps.Retire(m, gone);
                    req.RetireKey = 0; // Inferred, as the model's field.
                    if (req.RequestKind != 0 && MissionOps.Create(m, (uint)req.RequestKind, req.RequestSubtype) is { } made && made.Class.Object) MissionOps.Attach(m, req, made);
                    req.RequestKind = 0;
                }
                State = 5;
                return null;
            case 5:
            {
                var op = m.Ops.Values.FirstOrDefault(o => (_next == 0 || o.Key >= _next) && o.Selectable);
                if (op is null) { (State, Done, _next) = (0, true, 0); return null; }
                _current = op.Key;
                _next = m.Ops.Keys.FirstOrDefault(k => k > op.Key);
                State = 6;
                return null;
            }
            default: // 6
            {
                if (!m.Ops.TryGetValue(_current, out var op)) { (State, Done) = (0, true); return null; }
                var (c, finished) = MissionOps.Produce(game, m, op);
                if (c is not null && !finished) { Priority++; Done = true; }
                else if (finished) (State, Done) = (0, true);
                return c;
            }
        }
    }
}

    /// <summary>
    /// Garrison task: key, state, the task's fleet, target system, strength balance, refresh flag, and taken ships.
    /// </summary>
public sealed class GarrisonTask(int key)
{
    public int Key { get; } = key;
    public int State { get; set; }
    public AiFleet? Fleet { get; set; }
    public StarSystem? Target { get; set; }
    public int Balance { get; set; }
    public bool Refresh { get; set; }
    public List<AiShip> Ships { get; } = [];


    /// <summary>Adopt: the fleet becomes the task's, garrison-held instead of free, with no op key or entry.</summary>
    void Adopt(AiFleet f)
    {
        Fleet = f;
        (f.Free, f.Garrison) = (false, true);
        (f.OpKey, f.OpEntry) = (0, 0);
    }

    /// <summary>Release: free again instead of garrison-held, with no op key or entry, and no task fleet.</summary>
    void ReleaseFleet()
    {
        if (Fleet is not { } f) return;
        (f.Garrison, f.Free) = (false, true);
        (f.OpKey, f.OpEntry) = (0, 0);
        Fleet = null;
    }

    /// <summary>Take: the ship is garrison-taken instead of free, with no op key or entry; the balance gains its strength.</summary>
    void Take(AiShip s)
    {
        (s.Free, s.Garrison) = (false, true);
        (s.OpKey, s.OpEntry) = (0, 0);
        Balance += s.Firepower;
        Ships.Add(s);
    }

    /// <summary>Drop: a ship without an op key is free again (garrison and task marks cleared); the balance loses its strength.</summary>
    void Drop(AiShip s)
    {
        if (s.OpKey == 0) (s.Garrison, s.FlagshipMark, s.FlagshipWanted, s.Free) = (false, false, false, true);
        (s.OpKey, s.OpEntry) = (0, 0);
        Balance -= s.Firepower;
        Ships.Remove(s);
    }

    AiFleet? FleetOf(WorldModel m, AiShip s) => m.ViewParent(s.Ship) is Fleet f ? m.FleetsById.GetValueOrDefault(f.Ref) : null;

    /// <summary>
    /// Refresh: the task fleet must be own, held and non-empty, else it is released; each own task ship stays in the task fleet;
    /// without a task fleet the ship's free fleet is adopted; otherwise the ship is dropped.
    /// </summary>
    void RefreshMembers(WorldModel m)
    {
        if (Fleet is { } f && !(f.Own && f.Garrison && m.ShipsOf(f).Any())) ReleaseFleet();
        foreach (var s in Ships.ToList())
        {
            var own = FleetOf(m, s);
            if (s.Own && Fleet is not null && own == Fleet) continue;
            if (s.Own && Fleet is null && own is not null && own.Free) { Adopt(own); continue; }
            Drop(s);
        }
    }

    /// <summary>Release (state 8): drop all ships, release the fleet, clear the target's mark.</summary>
    public void Release(WorldModel m)
    {
        foreach (var s in Ships.ToList()) Drop(s);
        ReleaseFleet();
        if (m.Record(Target) is { } target) target.GarrisonTaskTarget = false;
    }

    /// <summary>Produce, one state per call; a call that returned a command sets the refresh flag.</summary>
    public (Command? Command, bool Finished) Produce(WorldModel m, Owner side)
    {
        if (Refresh) { RefreshMembers(m); Refresh = false; }
        var (c, finished) = Step(m, side);
        if (c is not null) Refresh = true;
        if (finished) State = 0; // Inferred: a finished task starts again at state 0
        return (c, finished);
    }

    (Command?, bool) Step(WorldModel m, Owner side)
    {
        switch (State)
        {
            case 0:
                (Refresh, State) = (true, 1);
                return (null, false);
            case 1:
                State = FindTarget(m) ? 2 : 8;
                return (null, false);
            case 2: // Split
            {
                State = 3;
                if (Fleet is not { } f || !(f.DamagedMoving || f.DamagedStopped)
                    || f.Claimed || f.EnrouteShip || f.Enroute) return (null, false);
                var split = m.ShipsOf(f).Where(s => (s.DamagedStopped || s.DamagedMoving) && !s.Enroute)
                    .Select(s => s.Ship).ToList();
                return (split.Count == 0 || f.Location is null ? null : new Command(0x270, side) { Targets = [.. split.Select(s => (s, 0))], ArgRef = f.Location }, false);
            }
            case 3:
                Balance = (Fleet?.FirepowerAllFighters ?? 0) - m.BestArmedFirepower * (m.HeadquartersMode ? 2 : 1);
                State = 4;
                return (null, false);
            case 4: // Reinforce
            {
                if (Fleet is { } moving && moving.Enroute || Balance == 0) { State = 5; return (null, false); }
                if (Balance > 0) { State = 5; return (Surplus(m, side), false); }
                var taken = Reinforce(m);
                if (taken.Count == 0) { State = 5; return (null, false); }
                var targets = taken.Select(s => (s.Ship, 0)).ToList();
                return (Fleet is { } f ? new Command(0x201, side) { Targets = targets, ArgRef = f.Fleet } : new Command(0x270, side) { Targets = targets, ArgRef = Target }, false);
            }
            case 5: // Unload troops
            {
                State = 6;
                if (Fleet is not { } f || f.UsableTroops <= 0 || m.Record(f.Location) is not { } at || !at.Own) return (null, false);
                var troops = m.TroopsAboard(f, s => s.Own && !s.Free && !s.Enroute);
                return (troops.Count == 0 ? null : new Command(0x201, side) { Targets = [.. troops.Select(t => (t, 0))], ArgRef = f.Location }, false);
            }
            case 6: // Unload characters: one list, own and prisoners, in ship then contents order; an own one arms it
            {
                State = 7;
                if (Fleet is not { } f || !f.OwnCharacter || f.Enroute || m.Record(f.Location) is not { } at
                    || !at.Own) return (null, false);
                var go = new List<GameObject>();
                bool armed = false;
                foreach (var s in Ships.Where(s => !s.Enroute && (s.OwnCharacter || s.EnemyCaptured)))
                    foreach (var x in m.Contents(s.Ship).Where(x => Family.Of(x.Type) is >= 0x30 and < 0x40 && m.Agents.ContainsKey(x.Ref)))
                    {
                        go.Add(x);
                        if (m.Agents[x.Ref].Own) armed = true;
                    }
                return (armed ? new Command(0x201, side) { Targets = [.. go.Select(x => (x, 0))], ArgRef = f.Location } : null, false);
            }
            case 7: // Move
            {
                if (m.Record(Target) is { } t && !t.Blockade && Fleet is { } f && !f.Enroute && f.Location != Target)
                    return (new Command(0x201, side) { Targets = [(f.Fleet, 0)], ArgRef = Target }, true);
                return (null, true);
            }
            default: // 8
                Release(m);
                return (null, true);
        }
    }

    /// <summary>
    /// Target: keep an own target that is a key system and not blockaded, marking it; else sectors with an own key system by
    /// random keys (rand(20)), and per sector in pick order its own key systems that are not blockaded and not already marked,
    /// by random keys (rand(10)); the first pick.
    /// </summary>
    bool FindTarget(WorldModel m)
    {
        if (m.Record(Target) is { } kept)
        {
            if (kept.Own && kept.KeySystem && !kept.Blockade) { kept.GarrisonTaskTarget = true; return true; }
            kept.GarrisonTaskTarget = false;
            Target = null;
        }
        var sectors = new ListStack();
        sectors.Push(m.SectorRandomList(x => x.KeySystem, 1));
        while (sectors.Pick() is AiSector s)
        {
            var systems = new ListStack();
            systems.Push(m.SystemRandomList(s, r => r.KeySystem && r.Own && !r.Blockade && !r.GarrisonTaskTarget, 1));
            if (systems.Pick() is AiSystem r)
            {
                Target = r.System;
                r.GarrisonTaskTarget = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Surplus: the first task ship in key order with none of the 0x2800 flags, not held back (its fleet is at an enemy system or
    /// has a claim), and with strength at most the balance, is dropped: a 0x270 command for the ship to the target. One per call.
    /// </summary>
    Command? Surplus(WorldModel m, Owner side)
    {
        foreach (var s in Ships.OrderBy(s => s.Ship.Ref).ToList())
        {
            var f = FleetOf(m, s);
            bool held = f is not null && (m.Record(f.Location)?.Enemy == true || f.Claimed);
            if ((s.NotCompleted || s.Enroute) || held || s.Firepower > Balance) continue;
            Drop(s);
            return Target is null ? null : new Command(0x270, side) { Targets = [(s.Ship, 0)], ArgRef = Target };
        }
        return null;
    }

    /// <summary>
    /// Reinforce (balance below 0): fleets nearest the target that are own, free and carry the 0x8000 flag, and are not Enroute
    /// or held; else the fallback search over fleets whose value is above 0. In the first fleet that gives a ship, every free
    /// armed ship (class 1, none of the excluded flags) joins while the balance is negative.
    /// </summary>
    List<AiShip> Reinforce(WorldModel m)
    {
        var taken = new List<AiShip>();
        static bool Own(AiFleet f) => f.Own && f.UndamagedShip;
        foreach (var list in new[] { Target is null ? null : m.FleetDistanceList(Target, f => Own(f) && f.Free && !f.Garrison && !f.Enroute, 2),
            m.FleetMinList(0, f => Own(f) && !f.Claimed && !f.Garrison && !f.Free && !f.Enroute, 1) })
        {
            if (list is null) continue;
            var stack = new ListStack();
            stack.Push(list);
            while (taken.Count == 0 && stack.Pick() is AiFleet f)
                foreach (var s in m.ShipsOf(f).ToList())
                    if (Balance < 0 && s.Free && s.Armed
                        && !(s.DamagedStopped || s.NotCompleted || s.DamagedMoving || s.Enroute))
                    {
                        Take(s);
                        taken.Add(s);
                    }
            if (taken.Count > 0) break;
        }
        return taken;
    }
}

/// <summary>
/// Planner 9, fleet tasks. Any other state moves to 2 (an empty call). State 2 picks the task with the saved next key, or else
/// the first in key order, unconditionally; the next key becomes the following key, with 0 at the end and no wrap. State 3 runs
/// the task's produce, one state per call. A command that is not finished raises the priority by one and keeps the state, and
/// the call is done. A finished task moves to state 0, and the call is done. With nothing, the state stays and the call is not done.
/// </summary>
public sealed class FleetTaskPlanner() : Planner(9)
{
    int _current, _next;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        switch (State)
        {
            case 2:
            {
                var task = m.Tasks.FirstOrDefault(t => _next != 0 && t.Key == _next) ?? m.Tasks.FirstOrDefault();
                if (task is null) { (State, Done, _next) = (0, true, 0); return null; }
                _current = task.Key;
                _next = m.Tasks.Where(t => t.Key > task.Key).Select(t => t.Key).DefaultIfEmpty(0).Min();
                State = 3;
                return null;
            }
            case 3:
            {
                var task = m.Tasks.FirstOrDefault(t => t.Key == _current);
                var (c, finished) = task is null ? (null, true) : task.Produce(m, controller.Side);
                if (finished) (State, Done) = (0, true);
                else if (c is not null) { Priority++; Done = true; }
                return c;
            }
            default:
                State = 2;
                return null;
        }
    }
}
