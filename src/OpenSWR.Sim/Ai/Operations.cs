using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Mission-op behaviour: creation and retirement counters, member assignment, the system-kind produce machine, the per-class
/// team, decoy and target lists, staging, the CreateMission and MissionOrder builders, and the TaskStatus and CompletionStatus handlers.
/// </summary>
public static partial class MissionOps
{

    /// <summary>
    /// Creates a new op of a (family, subtype) pair. System-kind classes are counted. The abduction and assassination ops get
    /// their own mission-type masks.
    /// </summary>
    public static MissionOp? Create(WorldModel m, MissionKind family, int subtype)
    {
        if (OpClass.Of(family, subtype) is not { } c) return null;
        var op = new MissionOp(m.NextOpKey++, c, m.Side) { State = (int)MissionOpState.Start };
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
    internal static bool Matches(OpClass c, MissionKind family, int subtype) => OpClass.Of(family, subtype) == c;

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
            MissionKind.Recruitment or MissionKind.SubdueUprising or MissionKind.InciteUprising => Skill(6),
            MissionKind.Research => Skill(op.Class.Subtype + 1),
            MissionKind.Diplomacy => Skill(0),
            MissionKind.Reconnaissance or MissionKind.Espionage => Skill(1),
            MissionKind.DeathStarSabotage or MissionKind.Sabotage => (Skill(5) + Skill(1)) / 2,
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
        switch ((MissionOpState)op.State)
        {
            case MissionOpState.Start:
                op.State = (int)MissionOpState.Check;
                return (null, false);
            case MissionOpState.Check:
                if (op.Recall || op.Review) return End(op, Recall(m, op));
                if (op.Retired) { CleanUp(m, op); return End(op, null); }
                op.State = (int)MissionOpState.Recheck;
                return (null, false);
            case MissionOpState.Recheck:
                Recheck(m, op);
                op.State = (int)MissionOpState.TeamPick;
                return (null, false);
            case MissionOpState.TeamPick:
                TeamPick(m, op);
                op.State = (int)MissionOpState.DecoyPick;
                return (null, false);
            case MissionOpState.DecoyPick:
                DecoyPick(m, op);
                op.State = (int)MissionOpState.TargetSearch;
                return (null, false);
            case MissionOpState.TargetSearch:
                TargetSearch(game, m, op);
                op.State = (int)MissionOpState.StagingSearch;
                return (null, false);
            case MissionOpState.StagingSearch:
                if (!StagingSearch(m, op)) return End(op, null);
                op.State = (int)MissionOpState.Moves;
                return (null, false);
            case MissionOpState.Moves:
            {
                var move = Moves(m, op);
                if (op.AtStaging && op.TargetFound) { op.State = (int)MissionOpState.Mission; return (move, false); }
                return End(op, move);
            }
            default: // Mission
                return End(op, Mission(m, op));
        }
    }

    static (Command?, bool) End(MissionOp op, Command? c)
    {
        op.State = (int)MissionOpState.Start;
        return (c, true);
    }

    /// <summary>Releases all members, clears the diplomacy slot (diplomacy drops its claim), and removes the op (Inferred).</summary>
    static void CleanUp(WorldModel m, MissionOp op)
    {
        foreach (var a in op.Team.Concat(op.Decoys)) Release(m, op, a);
        op.Team.Clear();
        op.Decoys.Clear();
        if (op.Class.Family == MissionKind.Diplomacy && m.Record(op.Target) is { } r) r.DiplomacyMark = false;
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
        if (op.Class.Family != MissionKind.Diplomacy) return true;
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
                MissionKind.Espionage => WithStaging(m, op, m.AgentList(espionage, Espionage, 51, 10000, RankOrder.Ascending), espionage),
                MissionKind.Reconnaissance => WithStaging(m, op, m.AgentList(recon, Espionage, 0, 10000, RankOrder.Ascending), recon),
                MissionKind.Sabotage => [m.AgentList(sabotage, Combat, 51, 10000, RankOrder.Ascending), .. WithStaging(m, op, m.AgentList(sabotage, Espionage, 51, 10000, RankOrder.Ascending), sabotage)],
                _ => WithStaging(m, op, m.AgentList(other, Combat, 51, 10000, RankOrder.Ascending), other),
            };
        }
        Func<AiAgent, bool> systemRecon = a => OwnAgent(a) && a.Can.Recon && !Busy(a), systemEspionage = a => OwnForce(a) && a.Can.Espionage && !Busy(a);
        return c.Family switch
        {
            MissionKind.Recruitment => [m.AgentList(a => a.Own && a.Can.Recruitment && !Busy(a), Leadership, 80, 10000, RankOrder.Ascending, minPri: true)],
            MissionKind.Research => [m.AgentList(a => a.Own && Researcher(a, c.Subtype) && !Busy(a), a => a.Skill(c.Subtype + 1), 10, 10000, RankOrder.Ascending, minPri: true)],
            MissionKind.Diplomacy => [m.AgentList(a => a.Own && a.Can.Diplomacy && !Busy(a) && !a.MissionSent, Diplomacy, 69, 10000, RankOrder.Ascending, minPri: true)],
            MissionKind.SubdueUprising => [m.AgentList(a => a.Own && a.Can.Subdue && !Busy(a), Leadership, 49, 10000, RankOrder.Ascending, minPri: true)],
            MissionKind.InciteUprising => [m.AgentList(a => a.Own && a.Can.Incite && !Busy(a) && !a.VictoryCharacter && !a.Specialist, Leadership, 49, 10000, RankOrder.Ascending, minPri: true)],
            MissionKind.Reconnaissance => WithStaging(m, op, m.AgentList(systemRecon, Espionage, 0, 10000, RankOrder.Ascending), systemRecon),
            MissionKind.Espionage => WithStaging(m, op, m.AgentList(systemEspionage, Espionage, 51, 10000, RankOrder.Ascending), systemEspionage),
            MissionKind.Rescue => [m.AgentList(a => a.Own && a.Can.Rescue && !Busy(a) && !a.VictoryCharacter && !a.Specialist && !a.Can.Commanding, Combat, 51, 10000, RankOrder.Ascending, minPri: true)],
            MissionKind.Abduction => [m.AgentList(a => a.Own && a.Can.Abduction && !Busy(a) && !a.Specialist && !a.Can.Commanding, Combat, 51, 10000, RankOrder.Ascending, minPri: true)],
            _ => [m.AgentList(a => a.Own && a.Can.DeathStarSabotage && !Busy(a) && !a.VictoryCharacter, Combat, 51, 10000, RankOrder.Ascending, minPri: true),
                m.AgentList(a => a.Own && a.Can.DeathStarSabotage && !Busy(a) && !a.VictoryCharacter, Espionage, 51, 10000, RankOrder.Ascending, minPri: true)],
        };
    }

    /// <summary>The research capability of research subtype 1 (ships), 2 (troops) or 3 (facilities).</summary>
    static bool Researcher(AiAgent a, int subtype) => (OpSubtype)subtype switch { OpSubtype.ShipResearch => a.Can.ShipDesign, OpSubtype.TroopResearch => a.Can.TroopTraining, _ => a.Can.FacilityDesign };

    /// <summary>The staging distance list (direction 2), with the companion list's test.</summary>
    static List<RankedList> WithStaging(WorldModel m, MissionOp op, RankedList list, Func<AiAgent, bool> pass) =>
        op.Staging is { } s ? [list, m.AgentDistanceList(s, pass, RankOrder.Descending)] : [list];

    /// <summary>The team slot: its flag is cleared at entry; while the team is below its size, lists are popped in rank-sum order; agents that pass the rejection check join; when full, the flag is set.</summary>
    static void TeamPick(WorldModel m, MissionOp op)
    {
        op.TeamComplete = false;
        if (op.Team.Count >= op.Class.TeamSize) { op.TeamComplete = true; return; }
        var stack = new ListStack();
        foreach (var l in TeamLists(m, op)) stack.Push(l);
        while (op.Team.Count < op.Class.TeamSize && stack.Pick() is AiAgent a)
            if (Family.Of(a.Agent.Type) is >= Families.MajorCharacter and < Families.Mission && Candidate(m, a))
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
            ? op.Class.Family switch { MissionKind.Espionage => a => a.Can.Espionage, MissionKind.Reconnaissance => null, MissionKind.Sabotage => a => a.Can.Sabotage, _ => op.TeamCapability }
            : op.Class.Family switch { MissionKind.InciteUprising => a => a.Can.Incite, MissionKind.Rescue => a => a.Can.Rescue, MissionKind.Abduction => a => a.Can.Abduction, MissionKind.DeathStarSabotage => a => a.Can.DeathStarSabotage, _ => null };
        if (capability is null)
        {
            if (op.Class.Family == MissionKind.Diplomacy && op.Decoys.Count > 0) { Release(m, op, op.Decoys[0]); op.Decoys.RemoveAt(0); }
            op.DecoysDone = true;
            return;
        }
        op.DecoysDone = false;
        int n = op.Class.Family == MissionKind.InciteUprising || op.Class.Object ? ((m.Record(op.Target)?.EnemyUnits ?? 0) + 2) / 2 : 4;
        if (op.Decoys.Count < n)
        {
            var stack = new ListStack();
            Func<AiAgent, bool> decoy = a => OwnForce(a) && capability(a) && !Busy(a);
            foreach (var l in WithStaging(m, op, m.AgentList(decoy, Espionage, 51, 10000, RankOrder.Ascending), decoy)) stack.Push(l);
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
}
