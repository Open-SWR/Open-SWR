namespace OpenSWR.Sim.Ai;

/// <summary>
/// Character operation: type 1 general at an own key system (kind 2, counted in the model's count), 3 / 4 / 5 fleet
/// commander / general / commander (fleet-attached, need fleet operations), 6 Palpatine to Coruscant (created once
/// model counter reaches 0). State, flags (character assigned, target valid, kind issued, at the ship, waiting, retired),
/// target, character.
/// </summary>
public sealed class CharacterOp(int key, int type)
{
    public int Key { get; } = key;
    public int Type { get; } = type;
    public int State { get; set; }
    /// <summary>A character is assigned.</summary>
    public bool Assigned { get; set; }
    public bool TargetValid { get; set; }
    /// <summary>The command kind was issued.</summary>
    public bool KindIssued { get; set; }
    /// <summary>The character is aboard the flagship.</summary>
    public bool AtShip { get; set; }
    /// <summary>The character waits at the gather system.</summary>
    public bool Waiting { get; set; }
    public bool Retired { get; set; }
    public StarSystem? Target { get; set; }
    public AiAgent? Character { get; set; }
    /// <summary>The owning fleet op's flagship, for types 3-5.</summary>
    public AiShip? Flagship { get; set; }
    /// <summary>The gather system, for types 3-5 (the fleet op's gather system).</summary>
    public StarSystem? Gather { get; set; }
    /// <summary>The assignment priority, for types 3-5 (the fleet op's priority).</summary>
    public int Priority { get; set; }
    /// <summary>The agent has the CanBe role of the type, for types 3-5.</summary>
    public bool CanBe(AiAgent a) => Type switch { 3 => a.Can.Admiral, 5 => a.Can.Commander, _ => a.Can.General };
    /// <summary>The owning fleet op, for types 3-5.</summary>
    public FleetOp? Owner { get; set; }
    /// <summary>Command of the type: 0x263 / 0x262 / 0x263 / 0x261; type 6 none.</summary>
    public int Command => Type switch { 1 or 4 => 0x263, 3 => 0x262, 5 => 0x261, _ => 0 };
}

/// <summary>
/// Planner 12: state 0 empty; 1 retire / create counts; 2-3 fleet-op character requests
/// (none without fleet operations); 4 the next selectable op (its character neither OnMission nor Enroute); 5 its produce
/// (exits as planner 4 state 6). Produce for types 1 / 6; types 3-5 are attached to fleet operations (not built).
/// </summary>
public sealed class CharacterOpPlanner() : Planner(12)
{
    static bool OwnAgent(AiAgent a) => a.Own && a.Unassigned;
    /// <summary>Agents a character op does not take: moving or busy, victory characters, specialists.</summary>
    static bool NotFree(AiAgent a) => MissionOps.Moving(a) || a.VictoryCharacter || a.Specialist;
    static int Leadership(AiAgent a) => a.Skill(SkillIndex.Leadership);
    int _next, _current, _fleet, _fleetNext;

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
                if (m.CharacterRetireKey != 0 && m.CharacterOps.TryGetValue(m.CharacterRetireKey, out var old)) Retire(m, old);
                m.CharacterRetireKey = 0;
                if (m.CharacterRequestType is 1 or 6) Create(m, m.CharacterRequestType);
                m.CharacterRequestType = 0;
                State = 2;
                return null;
            case 2: // the first fleet op from the cursor with a request (no wrap)
            {
                var fo = m.FleetOps.Values.FirstOrDefault(o => (_fleetNext == 0 || o.Key >= _fleetNext) && (o.CharacterRequest != 0 || o.RetireRequest is not null));
                if (fo is null) { (State, _fleetNext) = (4, 0); return null; }
                _fleet = fo.Key;
                _fleetNext = m.FleetOps.Keys.FirstOrDefault(k => k > fo.Key);
                State = 3;
                return null;
            }
            case 3: // retire, create and attach it
                if (m.FleetOps.TryGetValue(_fleet, out var owner))
                {
                    if (owner.RetireRequest is { } gone) Retire(m, gone);
                    owner.RetireRequest = null;
                    if (owner.CharacterRequest is 3 or 4 or 5)
                    {
                        var op = new CharacterOp(m.NextCharacterOpKey++, owner.CharacterRequest) { Owner = owner, Priority = owner.Entry };
                        m.CharacterOps[op.Key] = op;
                        owner.CharacterOps.Add(op);
                        owner.CharacterOpCount(op.Type)++;
                    }
                    owner.CharacterRequest = 0;
                }
                State = 4;
                return null;
            case 4:
            {
                var op = m.CharacterOps.Values.FirstOrDefault(o => (_next == 0 || o.Key >= _next) && Selectable(m, o));
                if (op is null) { (State, Done, _next) = (0, true, 0); return null; }
                _current = op.Key;
                _next = m.CharacterOps.Keys.FirstOrDefault(k => k > op.Key);
                State = 5;
                return null;
            }
            default:
            {
                if (!m.CharacterOps.TryGetValue(_current, out var op)) { (State, Done) = (0, true); return null; }
                var (c, finished) = Produce(m, op, controller.Side);
                if (c is not null && !finished) { Priority++; Done = true; }
                else if (finished) (State, Done) = (0, true);
                return c;
            }
        }
    }

    /// <summary>
    /// Selectable when its character is neither OnMission (0x80) nor Enroute (0x20); without one it is. The character
    /// is a key resolved every call, so a detached (0x302) record counts as none.
    /// </summary>
    internal static bool Selectable(WorldModel m, CharacterOp op) => Record(m, op) is not { } a || !(a.OnMission || a.Enroute);

    /// <summary>The op's character record: the op holds a key, looked up in the agent container on every read.</summary>
    static AiAgent? Record(WorldModel m, CharacterOp op) =>
        op.Character is { } a && m.Agents.TryGetValue(a.Agent.Ref, out var r) ? r : null;

    static void Create(WorldModel m, int type)
    {
        var op = new CharacterOp(m.NextCharacterOpKey++, type);
        m.CharacterOps[op.Key] = op;
        if (type == 1) m.Type1CharacterOps++; else m.Type6CharacterOps++;
    }

    /// <summary>
    /// Kind-1 ops (types 3-5) are first detached: the fleet op's counter -1, its owning op := 0, slot (0)
    /// (the character's agent priority := 0 and, when its agent's op is the op key, that op := 0; no dirty); the op is marked retired.
    /// Callers: planner 12 state 3 and the fleet-op reset.
    /// </summary>
    internal static void Retire(WorldModel m, CharacterOp op)
    {
        if (op.Owner is { } owner)
        {
            owner.CharacterOpCount(op.Type)--;
            owner.CharacterOps.Remove(op);
            op.Owner = null;
            op.Priority = 0;
            if (Record(m, op) is { } a && a.CharacterOpKey == op.Key) a.Priority = 0;
        }
        op.Retired = true;
    }

    static void Assign(WorldModel m, CharacterOp op, AiAgent a)
    {
        (a.CharacterOpKey, a.Priority, a.MissionOpKey) = (op.Key, op.Type is 1 or 6 ? 0 : op.Priority, 0); // type 1 / 6 priority 0 (Inferred)
        a.Unassigned = false;
        m.DirtyAgent(a);
        op.Character = a;
        op.Assigned = true;
    }

    /// <summary>
    /// Team keep: no agent record -> nothing (op keeps its character, not assigned,
    /// the pick runs); agent's op = this op and healthy -> keep (true); = this op but Injured / Captured (0x50) -> Unassigned set,
    /// its fields := 0, op drops it, no dirty; type 1 then releases its target claim; another
    /// owner -> only the op's reference is dropped. Type 6 keeps only Palpatine.
    /// </summary>
    internal static bool KeepCheck(WorldModel m, CharacterOp op)
    {
        if (op.Character is null) return false;
        if (Record(m, op) is not { } a) { op.Assigned = false; return false; }
        op.Character = a; // the key's current record
        if (a.CharacterOpKey == op.Key)
        {
            if (!(a.Captured || a.Injured) && (op.Type != 6 || a.Palpatine)) return true;
            a.Unassigned = true;
            (a.MissionOpKey, a.CharacterOpKey, a.Priority) = (0, 0, 0);
            if (op.Type == 1 && m.Record(op.Target) is { } claimed) claimed.CharacterClaim = false;
        }
        op.Character = null;
        op.Assigned = false;
        return false;
    }

    /// <summary>Produce (types 1 / 6): 0 empty, 1 retired -> cleanup, 3 target, 2 team, 4 command; types 3-5 as below.</summary>
    static (Command?, bool) Produce(WorldModel m, CharacterOp op, Owner side)
    {
        if (op.Type is 3 or 4 or 5) return FleetProduce(m, op, side);
        switch (op.State)
        {
            case 0:
                op.State = 1;
                return (null, false);
            case 1:
                if (op.Retired) { op.State = 0; return (Cleanup(m, op, side), true); }
                op.State = 3;
                return (null, false);
            case 3:
                Target(m, op);
                op.State = 2;
                return (null, false);
            case 2:
                if (Team(m, op)) { op.State = 4; return (null, false); }
                op.State = 0;
                return (null, true);
            default:
                op.State = 0;
                return (CommandFor(op, side), true);
        }
    }

    /// <summary>
    /// Retire (types 1 / 6, types 3-5): uncount; only when the agent's op is the op key:
    /// Unassigned set, character := 0, then 0x260 when it has a command kind and is free to act. Its other fields are not written and
    /// the agent record is not dirtied (as the team keep). The op drops its character; types 1 / 6 drop the claim.
    /// </summary>
    static Command? Cleanup(WorldModel m, CharacterOp op, Owner side)
    {
        m.CharacterOps.Remove(op.Key);
        if (op.Type == 1) m.Type1CharacterOps--;
        else if (op.Type == 6) m.Type6CharacterOps--;
        m.Dirty = true; // retire move
        Command? c = null;
        if (Record(m, op) is { } a && a.CharacterOpKey == op.Key) // detached record: nothing (Inferred from op holds a key)
        {
            a.Unassigned = true;
            a.Priority = 0;
            if (a.HasCommandKind && !(a.OnMission || a.Enroute)) c = new Command(0x260, side) { Targets = [(a.Agent, 0)] };
        }
        op.Character = null;
        op.Assigned = false;
        if (op.Type is 1 or 6)
        {
            if (m.Record(op.Target) is { } claimed) claimed.CharacterClaim = false;
            op.Target = null;
        }
        return c;
    }

    /// <summary>
    /// Type 1 target: target valid cleared on entry; keep while the system is a hub; else drop the claim and search: sectors with an own
    /// key system and the mark by a rank value, systems key + marked, not claimed, by a rank value; fallback without the key requirements
    /// (Inferred dir 1). Type 6: <see cref="Target6"/>.
    /// </summary>
    static void Target(WorldModel m, CharacterOp op)
    {
        if (op.Type == 6) { Target6(m, op); return; }
        if (op.Type != 1) return;
        op.TargetValid = false; // set again only by the keep or a found system
        if (m.Record(op.Target) is { } kept && kept.Hub) { op.TargetValid = true; return; }
        if (m.Record(op.Target) is { } claimed) claimed.CharacterClaim = false;
        op.Target = null;
        AiSystem? Search(Func<AiSector, bool> sectorReq, Func<AiSystem, bool> systemReq)
        {
            var sectors = new ListStack();
            sectors.Push(m.SectorList(x => sectorReq(x), x => x.ProducerMaintenance, 1));
            if (sectors.Pick() is not AiSector s) return null;
            var systems = new ListStack();
            systems.Push(m.SystemList(s, r => systemReq(r) && !r.CharacterClaim, r => r.ProducerMaintenance, 1));
            return systems.Pick() as AiSystem;
        }
        var found = Search(x => x.KeySystem && x.Hub, r => r.KeySystem && r.Hub)
            ?? Search(x => x.Hub, r => r.Hub);
        op.Target = found?.System;
        if (found is not null) op.TargetValid = true;
    }

    /// <summary>
    /// Type 6 target: target valid cleared; keep a target that is Coruscant and own; else one stage,
    /// no fallback: sectors (rand(20) each), then that sector's systems (rand(10) each), one pick each.
    /// </summary>
    static void Target6(WorldModel m, CharacterOp op)
    {
        op.TargetValid = false;
        if (m.Record(op.Target) is { } kept && (kept.Own && kept.Coruscant)) { op.TargetValid = true; return; }
        op.Target = null;
        var sectors = new ListStack();
        sectors.Push(m.SectorRandomList(x => x.KeySystem && x.Coruscant, 1));
        if (sectors.Pick() is not AiSector s) return;
        var systems = new ListStack();
        systems.Push(m.SystemRandomList(s, r => r.KeySystem && r.Coruscant, 1));
        if (systems.Pick() is AiSystem found) { op.Target = found.System; op.TargetValid = true; }
    }

    /// <summary>Blockade test: at a system (not Enroute) whose record has the Blockade flag.</summary>
    static bool Blocked(WorldModel m, AiAgent a) =>
        m.Where(a.Agent) is { } s && m.ViewParent(a.Agent) is StarSystem && !a.Enroute && m.Systems[s].Blockade;

    /// <summary>
    /// Type 1 team: keep the own healthy character; else a missing or claimed target is dropped; with an unclaimed target, the free own
    /// general-capable characters by Leadership (lo 50) and by distance to the target; the first passing the Blockade test; claim.
    /// </summary>
    static bool Team(WorldModel m, CharacterOp op)
    {
        if (op.Type == 6) return Team6(m, op);
        if (KeepCheck(m, op)) return true;
        if (op.Target is not { } target) return false;
        // no record, or claimed by another op -> target := none; no claim release, no dirty, target valid kept.
        if (m.Record(target) is not { } tr || tr.CharacterClaim) { op.Target = null; return false; }
        var stack = new ListStack();
        stack.Push(m.AgentList(a => OwnAgent(a) && a.Can.General && !NotFree(a), Leadership, 50, 10000, 1));
        stack.Push(m.AgentDistanceList(target, a => OwnAgent(a) && a.Can.General && !NotFree(a), 2));
        while (stack.Pick() is AiAgent pick)
        {
            if (Blocked(m, pick)) continue;
            Assign(m, op, pick);
            tr.CharacterClaim = true;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Type 6 team: keep (0x400, healthy); else with an own target record (else target := none) one list (0x402, by
    /// Leadership, lo 0), one pick, Blockade test, assign with priority 0; no target claim.
    /// </summary>
    static bool Team6(WorldModel m, CharacterOp op)
    {
        if (KeepCheck(m, op)) return true;
        if (m.Record(op.Target) is not { } tr || !tr.Own) { op.Target = null; return false; }
        var stack = new ListStack();
        stack.Push(m.AgentList(a => a.Own && a.Palpatine && !MissionOps.Moving(a), Leadership, 0, 10000, 1));
        if (stack.Pick() is not AiAgent pick || Blocked(m, pick)) return false;
        Assign(m, op, pick);
        return true;
    }

    /// <summary>A usable character at the target (the agent's location) without a command kind -> 0x26x; not there -> 0x201 -> the target.</summary>
    static Command? CommandFor(CharacterOp op, Owner side)
    {
        if (op.Character is not { } a || MissionOps.Moving(a) || op.Target is null) return null;
        if (a.Location == op.Target) return !a.HasCommandKind && op.Command != 0 ? new Command(op.Command, side) { Targets = [(a.Agent, 0)] } : null;
        return new Command(0x201, side) { Targets = [(a.Agent, 0)], ArgRef = op.Target };
    }

    // ---- types 3-5 ----

    static (Command?, bool) FleetProduce(WorldModel m, CharacterOp op, Owner side)
    {
        switch (op.State)
        {
            case 0:
                op.State = 1;
                return (null, false);
            case 1: // retired -> release, 0x260 as for types 1 / 6
                if (op.Retired) { op.State = 0; return (Cleanup(m, op, side), true); }
                op.State = 3;
                return (null, false);
            case 3:
                (op.Flagship, op.Gather) = (op.Owner?.Flagship, op.Owner?.Gather);
                op.TargetValid = op.Flagship is not null && op.Gather is not null;
                op.State = 2;
                return (null, false);
            case 2:
                if (FleetTeam(m, op)) { op.State = 4; return (null, false); }
                op.State = 0;
                return (null, true);
            default:
                op.State = 0;
                return (FleetCommand(m, op, side), true);
        }
    }

    /// <summary>
    /// Team (types 3 / 5, type 4): assigned, kind issued, at the ship and waiting are cleared first; keep the own healthy character
    /// (assigned; at the ship when the agent is aboard the flagship, then also kind issued with a command kind); else free own characters with the CanBe mask by Leadership (lo 50) and by distance to the
    /// gather system (else the first system of the fleet op's sector), by rank sum.
    /// </summary>
    static bool FleetTeam(WorldModel m, CharacterOp op)
    {
        op.Assigned = op.KindIssued = op.AtShip = op.Waiting = false; // every call; target valid and retired are kept
        if (KeepCheck(m, op) && op.Character is { } kept)
        {
            op.Assigned = true;
            if (op.Flagship is { } fs && kept.Location == fs.Ship)
            {
                op.AtShip = true;
                if (kept.HasCommandKind) op.KindIssued = true;
            }
            return true;
        }
        var reference = op.Gather ?? op.Owner?.Theater?.Systems.FirstOrDefault()?.System;
        Func<AiAgent, bool> pass = op.Type == 4 ? a => OwnAgent(a) && op.CanBe(a) && !MissionOps.Moving(a) && !a.VictoryCharacter
            : a => OwnAgent(a) && op.CanBe(a) && !NotFree(a);
        var stack = new ListStack();
        stack.Push(m.AgentList(pass, Leadership, 50, 10000, 1));
        if (reference is not null) stack.Push(m.AgentDistanceList(reference, pass, 2));
        if (stack.Pick() is not AiAgent pick) return false;
        Assign(m, op, pick);
        return true;
    }

    /// <summary>
    /// A usable character: aboard the flagship without a kind -> 0x26x; aboard another ship of the flagship's fleet, or
    /// on the flagship's system -> 0x201 -> the flagship (not Enroute); elsewhere -> the gather system (none: nothing; at it: wait).
    /// No flagship: the gather rule alone.
    /// </summary>
    static Command? FleetCommand(WorldModel m, CharacterOp op, Owner side)
    {
        if (op.Character is not { } a || MissionOps.Moving(a)) return null;
        if (op.Flagship is not { } fs) // no flagship: to the gather system, at it waiting, no gather nothing
        {
            if (op.Gather is not { } gather) return null;
            if (a.Location == gather) { op.Waiting = true; return null; }
            return new Command(0x201, side) { Targets = [(a.Agent, 0)], ArgRef = gather };
        }
        bool ready = !fs.Enroute;
        if (a.Location == fs.Ship)
        {
            if (a.HasCommandKind) return null;
            op.KindIssued = true;
            return new Command(op.Command, side) { Targets = [(a.Agent, 0)] };
        }
        if (a.Location is { } at && Family.IsCapitalShip(at.Type))
        {
            if (fs.Fleet is not null && m.ViewParent(at) == fs.Fleet)
            {
                op.AtShip = true;
                return ready ? new Command(0x201, side) { Targets = [(a.Agent, 0)], ArgRef = fs.Ship } : null;
            }
            return op.Gather is { } g ? new Command(0x201, side) { Targets = [(a.Agent, 0)], ArgRef = g } : null;
        }
        if (a.Location is StarSystem here)
        {
            if (here != m.Where(fs.Ship))
            {
                if (here == op.Gather) { op.Waiting = true; return null; }
                return op.Gather is { } g ? new Command(0x201, side) { Targets = [(a.Agent, 0)], ArgRef = g } : null;
            }
            if (ready)
            {
                op.AtShip = true;
                return new Command(0x201, side) { Targets = [(a.Agent, 0)], ArgRef = fs.Ship };
            }
        }
        return null;
    }
}
