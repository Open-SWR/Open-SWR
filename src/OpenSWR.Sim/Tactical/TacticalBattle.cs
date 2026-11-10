namespace OpenSWR.Sim;

/// <summary>One engine unit of a tactical battle as the front end sees it.</summary>
public sealed partial class TacticalUnit
{
    internal TacticalUnit(BattleEngine.Unit u) => U = u;
    internal BattleEngine.Unit U { get; }
    public GameObject Object => U.Object;
    /// <summary>Engine side index: 0 = side 1 (Alliance), 1 = side 2 (Empire).</summary>
    public int Side => U.Side;
    public bool Fighter => U.Type == TacticalUnitType.Fighter;
    /// <summary>The class's place in the engine type table (models, pictures 2001 + i); a class not in the table gets 0.</summary>
    public int TypeIndex => U.TypeIndex;
    /// <summary>State: TacticalUnitState.Docked docked, 2 active, 6-8 jumping, 9 withdrawn, 11 dead, ...</summary>
    public TacticalUnitState State => U.State;
    public double Hull => U.Hull;
    public double HullMax => U.HullMax;
    public double Shield => U.Shield;
    public double ShieldMax => U.ShieldMax;
    /// <summary>Position (x, y, z; y up).</summary>
    public V3 Position { get; set; }
    /// <summary>The position at the start of the last frame step (the display interpolates from it).</summary>
    public V3 Previous { get; internal set; }
    /// <summary>Heading (x and z components of the facing).</summary>
    public (double X, double Z) Heading { get => (Facing.X, Facing.Z); set => Facing = new V3(value.X, 0, value.Z); }
    /// <summary>Its group in the simulation, written by the task-force create, join and leave message handlers.</summary>
    public TacticalGroup? Group { get; internal set; }
    /// <summary>
    /// Its task force as the UI knows it, written at once by the orders; a fighter's: its fighter group. A deleted
    /// group reads as none (the key stays stale in the unit, readers resolve it through the registry; Inferred
    /// that no reader dereferences it).
    /// </summary>
    public TacticalGroup? UiGroup { get => UiGroupKey is { Deleted: false } g ? g : null; internal set => UiGroupKey = value; }
    /// <summary>The raw task force key (a deleted group's key stays).</summary>
    internal TacticalGroup? UiGroupKey;
    /// <summary>A weapon in some arc.</summary>
    public bool Armed => Enumerable.Range(0, 4).Any(a => U.ArcCapacity(a) > 0);
    /// <summary>Active: state 2, 4 or 6.</summary>
    public bool Active => U.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered;
}

/// <summary>
/// A tactical group (task force or fighter group): the simulation side (members in key order,
/// the current target (the shared group's target holder) and the further entries of the target list,
/// the nav list, the mission) and the UI side (members, targets, nav points, UI mission).
/// </summary>
public sealed partial class TacticalGroup
{
    internal TacticalGroup(BattleEngine.Group g, int side, int colour) => (G, Side, Colour) = (g, side, colour);
    internal BattleEngine.Group G { get; }
    internal List<TacticalUnit> Members { get; } = [];
    /// <summary>Simulation members.</summary>
    public IReadOnlyList<TacticalUnit> Units => Members;
    public int Side { get; }
    /// <summary>Fighter group colour (0-3: Red, Blue, Green, Gold; its HUD slot); -1 for a task force.</summary>
    public int Colour { get; }
    public bool Fighters => G.Formation;
    /// <summary>Simulation mission (set-mission message; 3 / 2 set by the withdraw).</summary>
    public TacticalMissionNumber Mission { get; internal set; }
    /// <summary>Target list entries after the current target (Inferred: the list's first entry is the current target).</summary>
    internal List<TacticalUnit> More { get; } = [];
    /// <summary>
    /// Simulation leader: the setup founder (task-force create message) or the first simulation join while none; the
    /// simulation leave hands it to the lowest other member key, else none. Readers: launch nav point, formation change, delete test.
    /// </summary>
    public TacticalUnit? SimLeader { get; internal set; }
    /// <summary>UI leader: the constructor's unit; the UI leave hands it to the lowest remaining UI member key, else none. Reader: the info-unit fallback.</summary>
    public TacticalUnit? UiLeader { get; internal set; }
    /// <summary>Deleted: out of the registry; stale UI keys resolve to none.</summary>
    public bool Deleted { get; internal set; }
}

/// <summary>A shot display message (laser, turbolaser, ion, torpedo) at the turn it ran, with the shot value as its payload.</summary>
public readonly record struct TacticalShot(int Turn, TacticalMessage Kind, TacticalUnit From, TacticalUnit To, double Value);

/// <summary>
/// Tactical space battle ("take command"): the world object of the interactive branch on the units of the shared setup
/// (<see cref="BattleEngine.Build"/>), placement and groups, the 1000 ms lockstep turns with the stamped message
/// queue, the side turn, fire decision and execution, hits incl. ion effects and critical hits,
/// recharge (the shared engine recharge plus the arc energy queue), the end test and S, abort and the records.
/// Missions and the computer side's tactical AI: TacticalAi.cs; hangars, launch, withdraw, tractor beams and damage control:
/// TacticalHangars.cs; the Death Star: TacticalDeathStar.cs. Draws: an own generator
/// (lo + rand() % (hi - lo + 1) = <see cref="GameRandom.Between"/>, seeded from the clock; injectable for tests), never the game generator.
/// </summary>
public sealed partial class TacticalBattle
{
    const double TurnMs = 1000, EndMs = 4000;
    internal readonly BattleEngine.Side[] Sides;
    readonly List<TacticalUnit> _units = [];
    readonly Dictionary<BattleEngine.Unit, TacticalUnit> _byUnit = [];
    readonly Dictionary<BattleEngine.Group, TacticalGroup> _byGroup = [];
    readonly List<TacticalGroup> _groups = [];
    readonly Func<int, int, int> _between;
    readonly List<Message> _queue = [];
    readonly TacticalSideState[] _state = new TacticalSideState[2]; // side state, one per engine side (fighting until a withdraw is ordered)
    readonly double[] _countdown = new double[2]; // side countdown (ms)
    readonly double[] _targeting = new double[2]; // side targeting countdown
    double _clock, _next, _frameAt, _end = -1;

    /// <summary>A queued message: stamp, kind, the action its handler runs; the key of an inserted message.</summary>
    sealed record Message(int Stamp, TacticalMessage Kind, Action Run, object? Key = null);

    public StarSystem System { get; }
    /// <summary>The local player's engine side index.</summary>
    public int Local { get; }
    /// <summary>Computer-controlled (not the local side; the local side while observing).</summary>
    public bool[] Computer { get; } = new bool[2];
    /// <summary>Battlefield size W.</summary>
    public double Size { get; }
    /// <summary>Current turn.</summary>
    public int Turn { get; private set; }
    /// <summary>Stamp base: the latency 3 at the start, + 1 after each turn's logic.</summary>
    public int Stamp { get; private set; } = 3;
    /// <summary>Done: the host applies <see cref="Result"/>.</summary>
    public bool Done { get; private set; }
    /// <summary>World state 5: the end countdown runs.</summary>
    public bool Ending => _end >= 0;
    /// <summary>Paused (pause counter not zero): only the display runs.</summary>
    public bool Paused { get; set; }
    /// <summary>The result words S[0..6].</summary>
    public int[] S { get; } = new int[7];
    public IReadOnlyList<TacticalUnit> Units => _units;
    public IReadOnlyList<TacticalGroup> Groups => _groups;
    /// <summary>Shot display messages as they ran (the front end draws the beams).</summary>
    public List<TacticalShot> Shots { get; } = [];
    /// <summary>State of engine side <paramref name="side"/>.</summary>
    public TacticalSideState SideState(int side) => _state[side];
    /// <summary>Fraction of the frame step since the last frame (display interpolation between <see cref="TacticalUnit.Previous"/> and the position).</summary>
    public double FrameFraction => Math.Clamp((_clock - _frameAt) / FrameMs, 0, 1);

    /// <param name="between">The C-rand draw lo + rand() % (hi - lo + 1); default an own <see cref="GameRandom"/> seeded from the clock.</param>
    public TacticalBattle(GameState game, StarSystem system, Owner local, Func<int, int, int>? between = null)
    {
        System = system;
        if (between is null)
        {
            var rand = new GameRandom(Math.Max(1, (int)((uint)Environment.TickCount % 1073741824))); // seeded from the clock (srand(timeGetTime()) in the original)
            between = (lo, hi) => rand.Between(lo, hi);
        }
        _between = between;
        Local = local == Owner.Empire ? 1 : 0; // engine side index i = strategic side i + 1 (Inferred)
        Sides = BattleEngine.Build(game, system);
        Ui = new TacticalUi(this);
        Ui.DeathStarPanel = Local == 1 && Sides[1].Scene is not null; // shown with the main window
        Ui.PanelValue = (int)(Sides[1].Scene?.Charge ?? 0); // Inferred: the panel starts from the charge
        foreach (var u in Sides[0].Battle) _byUnit[u] = new TacticalUnit(u);
        _units.AddRange(Sides[0].Battle.Select(u => _byUnit[u]));
        foreach (var t in _units.Where(t => t.State != TacticalUnitState.Docked)) AddToWorld(t); // every unit not docked
        SetupDocks(); // the setup docks run in the engine add, before the placement
        foreach (var t in _units) Kinematics(game, t);
        // W = 100 + 3 * max(n0, n1) over the undocked units (state != 1)
        Size = 100 + 3 * Math.Max(Sides[0].Battle.Count(u => u.Side == 0 && u.State != TacticalUnitState.Docked), Sides[0].Battle.Count(u => u.Side == 1 && u.State != TacticalUnitState.Docked));
        foreach (var side in Sides)
        {
            double forward = side.Index == 0 ? 1 : -1, back = -forward * Size / 2;
            Line(side.Ships, back, forward);
            Line(side.Fighters, back + forward * 20, forward);
            MakeGroups(side);
        }
        foreach (var u in Sides[0].Pending) // docked fighters: at their carrier (Inferred; hidden while docked)
            if (u.Carrier is { } c && _byUnit.TryGetValue(c, out var carrier)) (_byUnit[u].Position, _byUnit[u].Previous) = (carrier.Position, carrier.Position);
        foreach (var g in _groups) Found(g); // navigators, nav point at the founding unit's position
        // the placement ends by facing every unit of the battle list (docked and pending fighters, the scene hangar's
        // too) along its simulation group's heading, else its side's forward; launch writes no facing
        foreach (var t in _units) t.Facing = t.Group?.Nav is { } nav ? nav.Heading : new V3(0, 0, t.U.Side == 0 ? 1 : -1);
        SetComputer(1 - Local, true); // world init: the non-local side is computer-controlled
        BuildNavGrid(); // after the placement
        // an arc with capacity > 0 (and WeaponCH < 4 for a capital ship) starts ready with full energy, else queued for recharge
        foreach (var u in Sides[0].Battle)
            for (int a = 0; a < 4; a++)
            {
                double cap = u.ArcCapacity(a);
                if (cap > 0 && (u.Type == TacticalUnitType.Fighter || u.WeaponCh < 4)) (u.Energy[a], u.Ready[a]) = (cap, true);
                else if (cap > 0) u.Queue.Add(a);
            }
    }

    // ---- placement and groups ----

    /// <summary>Line on y = 0: x in list order 0, +5, -5, -10, +10, +15, -15, -20, ...; facing the side's forward vector.</summary>
    void Line(List<BattleEngine.Unit> list, double z, double forward)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var t = _byUnit[list[i]];
            t.Previous = t.Position = ((i % 4 < 2 ? 5.0 : -5.0) * ((i + 1) / 2), 0, z);
            t.Heading = (0, forward);
        }
    }

    /// <summary>
    /// Fighter groups: one fighter group per fighter type of the side (first unit leads; types in type-index order
    /// Inferred for this function; docked fighters included, Inferred), then for each of the four slots left
    /// empty the largest group (the first on ties, Inferred) with more than 7 units gives half its members, from the front after the
    /// leader, to a new group in that slot; the local side's groups take their HUD slots. Then the setup task forces.
    /// </summary>
    void MakeGroups(BattleEngine.Side side)
    {
        var fighters = side.Battle.Where(u => u.Side == side.Index && u.Type == TacticalUnitType.Fighter).GroupBy(u => u.Object.Type).OrderBy(g => g.First().TypeIndex).ToList();
        var slots = new TacticalGroup?[4];
        // openswr deviation: the HUD and the groups have four fighter slots; a fifth and later type of a modded side joins slot i % 4
        // (left out it would have no group and sit idle, while the automatic battle fights with it)
        var slotUnits = new List<BattleEngine.Unit>?[4];
        for (int i = 0; i < fighters.Count; i++) (slotUnits[i % 4] ??= []).AddRange(fighters[i]);
        for (int i = 0; i < 4; i++)
            if (slotUnits[i] is { } units) slots[i] = NewGroup(side, true, i, units);
        for (int slot = 0; slot < 4; slot++)
        {
            if (slots[slot] is not null) continue;
            var big = slots.Where(g => g is not null).MaxBy(g => g!.Members.Count);
            if (big is null || big.Members.Count <= 7) continue;
            var moved = big.Members.Skip(1).Take(big.Members.Count / 2).ToList();
            foreach (var u in moved) { big.Members.Remove(u); big.G.Members.Remove(u.U); }
            foreach (var u in moved) { big.UiMemberList.Remove(u); u.UiGroup = null; }
            slots[slot] = NewGroup(side, true, slot, moved.Select(u => u.U));
        }
        if (side.Index == Local)
            for (int c = 0; c < 4; c++) Ui.Fighters[c] = slots[c];
        SetupTaskForces(side);
    }

    /// <summary>
    /// Setup task forces: each sorted list is split over an index range (heavy 0 to 3, laser n1 to n1 + 3, unarmed
    /// n1 + n2 to 7) with the group count and stride of the automatic battle's split. A leader moves to (0, h(k), its z)
    /// (h = 0, +8, -8, +16, -16, +24, -24, then +32) and founds a task
    /// force (line navigator, nav point at it; local side: HUD slot k); the others join the current one (UI and simulation at once);
    /// after each unit every member of that task force snaps into its slot (position += the desired vector). No messages.
    /// </summary>
    void SetupTaskForces(BattleEngine.Side side)
    {
        var (heavy, laser, unarmed) = _setupLists[side.Index] = BattleEngine.SortCapital(side);
        int n1 = Make(heavy, 0, 3), n2 = Make(laser, n1, n1 + 3);
        Make(unarmed, n1 + n2, 7);

        int Make(List<BattleEngine.Unit> list, int lo, int hi)
        {
            int n = list.Count;
            if (n == 0) return 0;
            int g = BattleEngine.GroupCount(n, lo, hi), stride = n / g, made = 0, k = lo;
            TacticalGroup? current = null;
            for (int j = 0; j < n; j++)
            {
                var t = _byUnit[list[j]];
                if (BattleEngine.GroupStart(j, stride, made, g))
                {
                    t.Position = t.Previous = new V3(0, k == 0 ? 0 : k >= 7 ? 32 : (k + 1) / 2 * 8 * (k % 2 == 1 ? 1 : -1), t.Position.Z);
                    current = Wrap(BattleEngine.AddGroup(side, false, [list[j]]), side.Index, -1);
                    Found(current);
                    if (side.Index == Local && k < 8) Ui.TaskForces[k] = current;
                    made++;
                    k++;
                }
                else
                {
                    Join(current!, t);
                    current!.UiMemberList.Add(t);
                    t.UiGroup = current;
                }
                foreach (var m in current!.Members)
                {
                    m.Position += DesiredVector(m);
                    m.Previous = m.Position;
                }
            }
            return made;
        }
    }

    TacticalGroup NewGroup(BattleEngine.Side side, bool fighters, int slot, IEnumerable<BattleEngine.Unit> members) =>
        Wrap(BattleEngine.AddGroup(side, fighters, members), side.Index, slot);

    TacticalGroup Wrap(BattleEngine.Group g, int side, int slot)
    {
        var t = new TacticalGroup(g, side, slot);
        foreach (var u in g.Members.OrderBy(m => _byUnit[m].Key)) // member list in key order
        {
            t.Members.Add(_byUnit[u]);
            t.UiMemberList.Add(_byUnit[u]);
            _byUnit[u].Group = _byUnit[u].UiGroup = t;
        }
        if (g.Members.Count > 0) t.SimLeader = t.UiLeader = _byUnit[g.Members[0]]; // the founder (constructor leader)
        _byGroup[g] = t;
        _groups.Add(t);
        return t;
    }

    /// <summary>A group's current target: the shared group's target holder (the computer targeting writes it).</summary>
    TacticalUnit? Current(TacticalGroup g) => g.G.Target is { } t ? _byUnit[t] : null;

    // ---- time base ----

    /// <summary>
    /// <paramref name="ms"/> milliseconds of world time (in world state 4): a turn whenever 1000 ms have elapsed (the
    /// first at once), between two turns the 20 fixed frame steps of <see cref="FrameMs"/> (openswr design, not original: turn logic, then the
    /// frames up to the next turn); the frame update runs from turn counter 3 on (latency) and not in the end state; the end
    /// test after each; in state 5 the 4000 ms end countdown, then done. Nothing while paused.
    /// </summary>
    public void Advance(double ms)
    {
        if (Done || Paused) return;
        if (InIntro) { IntroStep(ms); return; } // world state 2 (the intro): no turns
        _clock += ms;
        if (Ending)
        {
            _end -= ms;
            if (_end <= 0) Done = true;
            return;
        }
        while (!Ending && !Done && !Paused) // a pause set inside a step (Death Star outcome movie) stops this advance too
        {
            if (_frameAt + FrameMs <= _clock && _frameAt + FrameMs <= _next)
            {
                _frameAt += FrameMs;
                if (Turn >= Latency) Frame(FrameMs);
                Centroids();
            }
            else if (_clock >= _next)
            {
                Step();
                _next += TurnMs;
            }
            else break;
            EndTest();
        }
        EndTest();
    }

    /// <summary>
    /// One turn (single-player world): phase 0 dispatch (turn n >= 3: every queued message stamped n - 1 in queue order,
    /// earlier stamps deleted, later kept); phase 1: side turns (local side first), group update (per group
    /// of side 0, then side 1, the range band, the navigator's per-turn update, then the group step: mission step and
    /// target refresh), unit update
    /// (recharge, ion timers, fire); then the stamp base + 1.
    /// </summary>
    public void Step()
    {
        if (Turn >= 3)
        {
            int due = Turn - 1;
            var run = _queue.Where(m => m.Stamp <= due).ToList(); // Inferred: one queue for both sides' messages, in post order
            _queue.RemoveAll(m => m.Stamp <= due);
            foreach (var m in run.Where(m => m.Stamp == due)) m.Run();
        }
        SideTurn(Local);
        SideTurn(1 - Local);
        foreach (var g in GroupsInOrder())
        {
            UpdateBand(g);
            if (g.Nav is not null) NavTurn(g);
            GroupStep(g);
        }
        // unit update over the battle units; before the walk the Death Star scene's scheduler, only for a local
        // Empire with a live scene
        if (Local == 1 && SceneAlive && FindHangar(Scene!) is { } sh) Schedule(sh);
        foreach (var t in _part.ToList())
        {
            var u = t.U;
            if (!t.Active) continue;
            Recharge(u);
            if (u.Type == TacticalUnitType.CapitalShip)
            {
                ShipTimers(t); // ion timers and damage control
                if (FindHangar(u) is { } h) Schedule(h); // the hangar's schedule
            }
            Fire(t);
        }
        Stamp++;
        Turn++;
    }

    void Post(int stamp, TacticalMessage kind, Action run) => _queue.Add(new Message(stamp, kind, run));

    /// <summary>Insert with an explicit stamp: dropped when a message with that key is queued (Inferred: key = kind and its object).</summary>
    void Insert(int stamp, TacticalMessage kind, object key, Action run)
    {
        if (_queue.Any(m => m.Kind == kind && Equals(m.Key, key))) return;
        _queue.Add(new Message(stamp, kind, run, key));
    }

    /// <summary>Queued messages of a kind and stamp (tests).</summary>
    internal int Queued(TacticalMessage kind, int? stamp = null) => _queue.Count(m => m.Kind == kind && (stamp is null || m.Stamp == stamp));

    /// <summary>The kinds of the queued messages in queue order (tests).</summary>
    internal List<TacticalMessage> QueuedKinds() => [.. _queue.Select(m => m.Kind)];

    /// <summary>Recharge: the shared shield part, then the weapon energy W * dt fills the queued arcs in order to capacity (ready), the rest carrying on.</summary>
    static void Recharge(BattleEngine.Unit u)
    {
        BattleEngine.Recharge(u);
        double e = BattleEngine.WeaponRate(u) * 1.0;
        while (e > 0 && u.Queue.Count > 0)
        {
            int a = u.Queue[0];
            double need = u.ArcCapacity(a) - u.Energy[a];
            if (e < need) { u.Energy[a] += e; break; }
            (u.Energy[a], u.Ready[a]) = (u.ArcCapacity(a), true);
            u.Queue.RemoveAt(0); // Inferred: a full arc leaves the queue
            e -= need;
        }
    }

    static void Enqueue(BattleEngine.Unit u, int a)
    {
        if (!u.Queue.Contains(a)) u.Queue.Add(a); // Inferred: an arc is queued once
    }

    // ---- side turn ----

    /// <summary>
    /// Side turn: strength; state 2 countdown (to 3; side index 1 with a live scene: scene-withdraw message); the candidate build
    /// (every turn of both sides); side index 1's superlaser charge; state 3 jumps; state 0 and computer-controlled:
    /// the targeting countdown -1.0, at <= 0 the rebuild when due, 6.0, the targeting, the withdraw decision (not the
    /// local side, no Death Star run in progress), then the Death Star cases. Fire-event records are not kept (their
    /// only reader adds 0).
    /// </summary>
    void SideTurn(int i)
    {
        var side = Sides[i];
        var other = Sides[1 - i];
        side.Strength = Strength(side); // 1. strength of the side
        if (_state[i] == TacticalSideState.WithdrawOrdered)
        {
            _countdown[i] -= TurnMs;
            if (_countdown[i] <= 0)
            {
                _state[i] = TacticalSideState.Jumping;
                if (i == 1 && SceneAlive) SceneWithdraw(); // the scene is withdrawn
            }
        }
        BuildCandidates(i); // resets the side's candidate field (Inferred throughout)
        if (i == 1) Charge(); // superlaser charge
        if (_state[i] == TacticalSideState.Jumping) Jump(side);
        if (_state[i] == TacticalSideState.Fighting && Computer[i])
        {
            _targeting[i] -= 1.0;
            if (_targeting[i] <= 0)
            {
                if (_rebuildDue[i]) Rebuild(i);
                _rebuildDue[i] = false;
                _targeting[i] = 6.0;
                Targeting(i);
                double own = Strength(side), e = Strength(other);
                if (BattleEngine.Outmatched(own, e, side.Threshold) && RetreatPossible(i) && i != Local && !DeathStarRun) Withdraw(i);
                if (i == 1) ComputerSuperlaser(); // Death Star cases
                else ComputerRun();
            }
        }
    }

    /// <summary>
    /// Tactical strength: the left-out rule against the other side's groups over the side's first unit list; then both sides
    /// add their second unit list, side index 1 the pending list before it; the Death Star
    /// bonus (20000000) where it applies.
    /// </summary>
    double Strength(BattleEngine.Side side)
    {
        var other = Sides[1 - side.Index];
        double sum = 0;
        foreach (var u in side.Ships)
        {
            if (other.Groups.Any(g => g.Target == u && BattleEngine.GroupRate(g, u, Sides) >= BattleEngine.ShieldRate(u))) continue;
            sum += BattleEngine.Rate(u, true, 1, false);
        }
        foreach (var u in side.Index == 1 ? side.Pending.Concat(side.Fighters) : side.Fighters) sum += BattleEngine.Rate(u, true, 1, false);
        return SceneBonus(side.Index) ? sum + 20000000.0 : sum;
    }

    /// <summary>Retreat possible: the automatic battle's rule with the tactical side state 0.</summary>
    public bool RetreatPossible(int side) => _state[side] == TacticalSideState.Fighting && BattleEngine.RetreatPossible(Sides[side], Sides);

    /// <summary>
    /// World withdraw:
    /// side state 2 (local side: the withdraw-ordered voice); every group of the side in order: a fighter group leaves the local side's
    /// fighter button and, with a first member, a Death Star run (simulation mission 6) posts its abort run-state message (group G, state 8) first, then
    /// gets mission 2 (that member without a working hyperdrive) or 3, a task force mission 3, both through the UI setter (its rule 1 keeps a computer group's Recover); then every
    /// capital ship of the first list leaves its group (a numbered task force of the local side frees the button holding it; UI remove,
    /// the task-force leave message) and gets its own new task force (the task-force create message) with mission 3 (the set-mission message). Countdown max(the side's countdown field, 30000 ms), 150000 ms for side
    /// index 1 with a live Death Star scene (Inferred: that field = 0.4 * v4 * 1000 with v4 not computed, 0).
    /// </summary>
    public void Withdraw(int side)
    {
        if (_state[side] != TacticalSideState.Fighting) return; // Inferred: once
        _state[side] = TacticalSideState.WithdrawOrdered;
        if (side == Local) Ui.Voices.Add(side == 0 ? TacticalVoice.WithdrawOrdered : TacticalVoice.WithdrawOrderedEmpire);
        foreach (var bg in Sides[side].Groups.ToList())
        {
            if (!_byGroup.TryGetValue(bg, out var g)) continue;
            if (g.Fighters)
            {
                if (side == Local) for (int c = 0; c < 4; c++) if (Ui.Fighters[c] == g) Ui.Fighters[c] = null;
                if (g.Members.FirstOrDefault() is not { } fm) continue;
                if (g.Mission == TacticalMissionNumber.AttackDeathStar) PostRun(g, TacticalRunState.Abort); // a plain post, no pending flag (Inferred)
                SetMission(g, fm.U.Hyperdrives - fm.U.HyperCh > 0 ? TacticalMissionNumber.Withdraw : TacticalMissionNumber.Recover); // working hyperdrives
            }
            else SetMission(g, TacticalMissionNumber.Withdraw);
        }
        foreach (var u in Sides[side].Ships.ToList())
        {
            var t = _byUnit[u];
            if (t.Group is { } g)
            {
                if (Ui.Unassigned(t) is null && g.Side == Local) for (int i = 0; i < 8; i++) if (Ui.TaskForces[i] == g) Ui.TaskForces[i] = null;
                UiLeave(g, t); // UI remove, then the task-force leave message
                Post(Stamp, TacticalMessage.TaskForceLeave, () => { if (t.Group == g) Leave(t); });
            }
            SetMission(CreateTaskForce(t), TacticalMissionNumber.Withdraw); // no button
        }
        _countdown[side] = side == 1 && BattleEngine.Alive(Sides[1].Scene) ? 150000 : 30000;
    }

    /// <summary>
    /// Side state 3: while a unit of the two lists that can jump is active or in state 7 / 8, nothing more; else
    /// state 4 and every unit of both lists that cannot jump gets the scuttle message (scuttle: destroyed). The jumps themselves start in
    /// the groups' withdraw movement (state 6, jump-start message to state 7, 4000 ms to state 8, retreat message to state 9).
    /// </summary>
    void Jump(BattleEngine.Side side)
    {
        bool CanJump(BattleEngine.Unit u) => this.CanJump(_byUnit[u]);
        if (side.Ships.Concat(side.Fighters).Any(u => CanJump(u) && u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered or TacticalUnitState.Accelerating or TacticalUnitState.InHyperspace)) return;
        _state[side.Index] = TacticalSideState.JumpFinished;
        foreach (var u in side.Ships.Concat(side.Fighters).Where(u => !CanJump(u)).ToList())
        {
            var t = _byUnit[u];
            Post(Stamp, TacticalMessage.Scuttle, () => Scuttle(t)); // Inferred stamp: the current Stamp
        }
    }

    // ---- fire ----

    /// <summary>One arc's three shot slots: (value, target) for turbolaser, laser and ion.</summary>
    sealed class Slots
    {
        public readonly double[] Tl = new double[4], Laser = new double[4], Ion = new double[4];
        public readonly TacticalUnit?[] TlT = new TacticalUnit?[4], LaserT = new TacticalUnit?[4], IonT = new TacticalUnit?[4];
    }

    static double Distance(TacticalUnit a, TacticalUnit b)
    {
        double dx = b.Position.X - a.Position.X, dy = b.Position.Y - a.Position.Y, dz = b.Position.Z - a.Position.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Arc of a target: horizontal plane, the heading turned by +/- 45 deg; a fighter shooter (Inferred) gives arc 0.</summary>
    internal static int ArcOf(TacticalUnit a, TacticalUnit t)
    {
        if (a.Fighter) return 0;
        double dx = t.Position.X - a.Position.X, dz = t.Position.Z - a.Position.Z;
        var (fx, fz) = a.Heading;
        double c = Math.Cos(Math.PI / 4), s = Math.Sin(Math.PI / 4);
        double A = dx * (fx * c - fz * s) + dz * (fz * c + fx * s), B = dx * (fx * c + fz * s) + dz * (fz * c - fx * s);
        return A >= 0 ? (B >= 0 ? 0 : 3) : (B >= 0 ? 1 : 2);
    }

    const double Base = 0.26666668f, LaserVsShip = 0.167f, Torpedo = 0.53333336f; // weapon factor constants, read from the original bytes (single precision)

    /// <summary>
    /// Fire decision: active, not jump-ordered, an arc ready; draw rand() % 3, only 0 goes on; the group's
    /// current target P (mission 1: no fire; no arc against P's type: stop) by an aimed shot, then, while r = 2 (also without P), the
    /// nearest-enemy list; opportunistic fire when the group is on mission 4 or (the unit
    /// has a laser and r = 2).
    /// </summary>
    void Fire(TacticalUnit t)
    {
        var u = t.U;
        if (!t.Active || u.State == TacticalUnitState.JumpOrdered || !u.Ready.Any(r => r)) return;
        if (_between(0, 2) != 0) return;
        int r = 2;
        var g = t.Group;
        if (g is not null)
        {
            if (g.Mission == TacticalMissionNumber.Escort) return;
            if (Current(g) is { } p)
            {
                if (BattleEngine.Arc(u, p.U.Type != TacticalUnitType.Fighter) == BattleEngine.NoArc) return;
                r = Aimed(t, p);
            }
            foreach (var next in g.NearestList)
            {
                if (r != 2) break;
                r = Aimed(t, next);
            }
        }
        Tractor(t, g is null ? null : Current(g)); // tractor beam handling
        if (g is { Mission: TacticalMissionNumber.AttackFighters } || BattleEngine.HasLaser(u) && r == 2) Opportunistic(t);
    }

    /// <summary>Aimed shot at P, giving r: 1 arc not ready, 2 out of range, 3 nothing fired, 4 fired.</summary>
    int Aimed(TacticalUnit t, TacticalUnit p)
    {
        var u = t.U;
        if (p.Fighter && !u.Ready[0]) return 1;
        double d = Distance(t, p);
        if (d > BattleEngine.MaxRange(u)) return 2;
        var slots = new Slots();
        int a = ArcOf(t, p);
        if (!u.Ready[a]) return 1;
        bool ship = !p.Fighter;
        double laser = u.Arcs[a, 0], ion = u.Arcs[a, 1], tl = u.Arcs[a, 2];
        if (ion > 0 && d <= u.IonRange && p.U.Type == TacticalUnitType.CapitalShip) (slots.Ion[a], slots.IonT[a]) = (ion * Base * 1.0, p);
        if (tl > 0 && d <= u.TlRange) (slots.Tl[a], slots.TlT[a]) = (tl * Base * (ship ? 1.0 : 0.5), p);
        if (laser > 0 && d <= u.LaserRange) (slots.Laser[a], slots.LaserT[a]) = (laser * (ship ? LaserVsShip : 1.0) * Base, p);
        return Execute(t, slots) ? 4 : 3;
    }

    /// <summary>
    /// Opportunistic fire: the fire query over the fighter tree, then per candidate in its arc, when
    /// ready: turbolaser and laser slots with the anti-fighter factor, kept while the slot value is below the raw value (value = hull
    /// fraction * raw); no ion; then the execution.
    /// </summary>
    void Opportunistic(TacticalUnit t)
    {
        var u = t.U;
        var candidates = FireQuery(t, BattleEngine.MaxRange(u));
        var slots = new Slots();
        double hf = u.HullMax > 0 ? u.Hull / u.HullMax : 0;
        foreach (var c in candidates)
        {
            int a = ArcOf(t, c);
            if (!u.Ready[a]) continue;
            double d = Distance(t, c), tl = u.Arcs[a, 2] * Base * 0.5, laser = u.Arcs[a, 0] * 1.0 * Base;
            if (u.Arcs[a, 2] > 0 && d <= u.TlRange && slots.Tl[a] < tl) (slots.Tl[a], slots.TlT[a]) = (hf * tl, c);
            if (u.Arcs[a, 0] > 0 && d <= u.LaserRange && slots.Laser[a] < laser) (slots.Laser[a], slots.LaserT[a]) = (hf * laser, c);
        }
        Execute(t, slots);
    }

    /// <summary>
    /// Fire query: the fighter tree walked from the shooter's place first toward lower
    /// x, then toward higher x, each walk ending at the first entry whose |dx| exceeds the range; kept: active, not jump-ordered, the
    /// other side, |dy| and |dz| within the range.
    /// </summary>
    internal List<TacticalUnit> FireQuery(TacticalUnit t, double range) =>
        [.. Walk(t, true, x => Math.Abs(x.Position.X - t.Position.X) > range).Where(x => x.Active && x.State != TacticalUnitState.JumpOrdered && x.Side != t.Side
            && Math.Abs(x.Position.Y - t.Position.Y) <= range && Math.Abs(x.Position.Z - t.Position.Z) <= range)];

    /// <summary>
    /// Fire execution: repeatedly the arc with the largest slot total (first on ties) fires ion, laser (+ a fighter's
    /// torpedo at an unshielded ship within torpedo range), turbolaser; each only with a value != 0, a target still active and the shooter not
    /// jump-ordered; display messages and hit messages with their stamps (T = the stamp base); energy -= the weapon count, ready := 0,
    /// the arc queued for recharge. Returns whether something fired.
    /// </summary>
    bool Execute(TacticalUnit t, Slots s)
    {
        var u = t.U;
        bool fired = false;
        while (true)
        {
            int best = -1;
            double top = 0;
            for (int a = 0; a < 4; a++)
            {
                double total = s.Tl[a] + s.Laser[a] + s.Ion[a];
                if (total > top) (best, top) = (a, total);
            }
            if (best < 0) return fired;
            int arc = best, T = Stamp;
            bool Ok(TacticalUnit? x) => x is { Active: true } && u.State != TacticalUnitState.JumpOrdered;
            if (s.Ion[arc] != 0 && Ok(s.IonT[arc]))
            {
                var (p, v) = (s.IonT[arc]!, s.Ion[arc]);
                Post(T + 1, TacticalMessage.IonShot, () => Show(TacticalMessage.IonShot, t, p, v));
                Post(T + 2, TacticalMessage.IonHit, () => { if (p.Active) IonHit(p, v, t); });
                Spend(arc, u.Arcs[arc, 1]);
            }
            if (s.Laser[arc] != 0 && Ok(s.LaserT[arc]))
            {
                var (p, v) = (s.LaserT[arc]!, s.Laser[arc]);
                Post(T, TacticalMessage.LaserShot, () => Show(TacticalMessage.LaserShot, t, p, v));
                Post(T + 1, TacticalMessage.LaserHit, () => { if (p.Active) Hit(p, v, t, ratio: true, TacticalWeapon.Laser); });
                double sf = p.ShieldMax > 0 ? p.Shield / p.ShieldMax : 0;
                if (t.Fighter && !p.Fighter && sf == 0 && Distance(t, p) <= u.TorpedoRange)
                {
                    double hf = u.HullMax > 0 ? u.Hull / u.HullMax : 0, torpedo = hf * u.Torpedo * Torpedo;
                    Post(T, TacticalMessage.TorpedoShot, () => Show(TacticalMessage.TorpedoShot, t, p, torpedo));
                    Post(T + 2, TacticalMessage.TorpedoHit, () => { if (p.Active) Hit(p, torpedo, t, ratio: false, TacticalWeapon.Torpedo); });
                }
                Spend(arc, u.Arcs[arc, 0]);
            }
            if (s.Tl[arc] != 0 && Ok(s.TlT[arc]))
            {
                var (p, v) = (s.TlT[arc]!, s.Tl[arc]);
                Post(T, TacticalMessage.TurbolaserShot, () => Show(TacticalMessage.TurbolaserShot, t, p, v));
                Post(T + 1, TacticalMessage.TurbolaserHit, () => { if (p.Active) Hit(p, v, t, ratio: true, TacticalWeapon.Turbolaser); });
                Spend(arc, u.Arcs[arc, 2]);
            }
            (s.Tl[arc], s.Laser[arc], s.Ion[arc]) = (0, 0, 0);
        }

        void Spend(int arc, double count)
        {
            u.Energy[arc] -= count;
            u.Ready[arc] = false;
            Enqueue(u, arc);
            fired = true;
        }
    }

    /// <summary>
    /// Display handlers: nothing unless both units are active; then the shot
    /// sound (laser: capital ship 0, fighter 3 / 4 by side index; turbolaser and torpedo 1; ion: capital ship 2, fighter 5) and the
    /// bolt (<see cref="Shots"/>, drawn by the front end).
    /// </summary>
    void Show(TacticalMessage kind, TacticalUnit from, TacticalUnit to, double value)
    {
        if (!from.Active || !to.Active) return;
        Sounds.Add(kind switch
        {
            TacticalMessage.LaserShot => from.Fighter ? TacticalEffectSound.LaserFighter + from.Side : TacticalEffectSound.LaserCapital,
            TacticalMessage.IonShot => from.Fighter ? TacticalEffectSound.IonFighter : TacticalEffectSound.IonCapital,
            _ => TacticalEffectSound.TurbolaserShot,
        });
        Shots.Add(new TacticalShot(Turn, kind, from, to, value));
    }

    // ---- hits ----

    /// <summary>Ratio of a fighter target: target / shooter maneuverability, at least 0.1; if the shooter is gone (Inferred: dead or withdrawn) it is the target's maneuverability alone.</summary>
    double Ratio(TacticalUnit p, TacticalUnit shooter)
    {
        double ta = Maneuverability(p);
        if (shooter.State is TacticalUnitState.Withdrawn or TacticalUnitState.Destroyed) return ta;
        return Math.Max(0.1, ta / Maneuverability(shooter));
    }

    /// <summary>
    /// Turbolaser / laser / torpedo hit: fighter target d / ratio (not torpedoes); d >= 1;
    /// shield held (ship target): crit 0 when rand 0...100 <= max(1, 100 * d / s0); shield broken: the whole overflow on the hull; dead:
    /// destroy; else ship target: rand 0...100 + 100 * delta hull / hull, then crit bands. Crit messages stamped with the stamp base. Presentation (no draws):
    /// weapon <paramref name="w"/> (0 laser, 1 turbolaser, 3 torpedo); shield held: sound laser shield hit / other shield hit and a
    /// spark; hull: sound laser hull hit / other hull hit, then either the kill voices, the death effect and the explosion sound, or the hull explosion.
    /// </summary>
    internal void Hit(TacticalUnit p, double d, TacticalUnit shooter, bool ratio, TacticalWeapon w = TacticalWeapon.Laser)
    {
        var u = p.U;
        double s0 = u.Shield, h0 = u.Hull;
        if (h0 <= 0) { u.Hull = 0; return; }
        if (ratio && p.Fighter) d /= Ratio(p, shooter);
        d = Math.Max(d, 1.0);
        u.Shield = s0 - d;
        if (u.Shield >= 0)
        {
            Sounds.Add(w == TacticalWeapon.Laser ? TacticalEffectSound.LaserShieldHit : TacticalEffectSound.OtherShieldHit);
            Effect(p, w, TacticalEffectKind.Spark);
            if (u.Type == TacticalUnitType.CapitalShip && _between(0, 100) <= Math.Max(1, 100 * d / s0)) CritMessage(p, 0);
            return;
        }
        u.Shield = 0;
        u.Hull = h0 + (s0 - d);
        Sounds.Add(w == TacticalWeapon.Laser ? TacticalEffectSound.LaserHullHit : TacticalEffectSound.OtherHullHit);
        if (u.Hull <= 0)
        {
            KillVoices(p, shooter); // the kill statistics are not modelled
            u.Hull = 0; // hull set to 0 also for a unit the destroy then leaves alive (state 7 / 8)
            Death(p, w);
            Destroy(p);
            return;
        }
        Effect(p, w, TacticalEffectKind.HullExplosion);
        if (u.Type != TacticalUnitType.CapitalShip) return;
        double x = 100 * (u.Hull - h0) / h0 + _between(0, 100);
        CriticalKind? k = x > 95 ? CriticalKind.Hyperdrive : x > 90 ? CriticalKind.Speed : x > 80 ? CriticalKind.Tractor
            : x > 65 ? CriticalKind.Weapon : x > 50 ? CriticalKind.Shield : null;
        if (k is { } crit) CritMessage(p, crit);
    }

    /// <summary>
    /// Ion hit: as the shield part of a hit; the excess never reaches the hull: per whole point (not on a
    /// fighter) k = rand 1...10: 1 tractor-down += rand 30...50, 2 engine-down += rand 30...50, 3...10 arc (k - 3) div 2 down; then
    /// shield-overflow message stamped with the stamp base applies them. Presentation: shield held gives the ion shield hit sound and a spark; overflow gives the ion overflow hit sound and the
    /// hull explosion (w = 2).
    /// </summary>
    internal void IonHit(TacticalUnit p, double d, TacticalUnit shooter)
    {
        var u = p.U;
        if (u.Hull <= 0) { u.Hull = 0; return; }
        if (p.Fighter) d /= Ratio(p, shooter);
        d = Math.Max(d, 1.0);
        double s0 = u.Shield;
        u.Shield = s0 - d;
        if (u.Shield >= 0)
        {
            Sounds.Add(TacticalEffectSound.IonShieldHit);
            Effect(p, TacticalWeapon.Ion, TacticalEffectKind.Spark);
            if (u.Type == TacticalUnitType.CapitalShip && _between(0, 100) <= Math.Max(1, 100 * d / s0)) CritMessage(p, 0);
            return;
        }
        Sounds.Add(TacticalEffectSound.IonOverflowHit);
        Effect(p, TacticalWeapon.Ion, TacticalEffectKind.HullExplosion);
        double x = -u.Shield;
        u.Shield = 0;
        if (p.Fighter) return;
        double a = 0, b = 0;
        var arcs = new bool[4];
        while (x > 0)
        {
            int k = _between(1, 10);
            if (k == 1) a += _between(30, 50);
            else if (k == 2) b += _between(30, 50);
            else arcs[(k - 3) / 2] = true;
            x -= 1.0;
        }
        Post(Stamp, TacticalMessage.ShieldOverflow, () =>
        {
            u.EngineDown += b;
            u.TractorDown += a;
            for (int i = 0; i < 4; i++)
                if (arcs[i]) (u.Ready[i], u.Energy[i]) = (false, 0);
            for (int i = 0; i < 4; i++)
                if (arcs[i]) Enqueue(u, i);
        });
    }

    /// <summary>The critical-hit message kind of a critical kind.</summary>
    static TacticalMessage CriticalHitMessage(CriticalKind k) => k switch
    {
        CriticalKind.Shield => TacticalMessage.CriticalHitShield,
        CriticalKind.Weapon => TacticalMessage.CriticalHitWeapon,
        CriticalKind.Tractor => TacticalMessage.CriticalHitTractor,
        CriticalKind.Speed => TacticalMessage.CriticalHitSpeed,
        CriticalKind.Hyperdrive => TacticalMessage.CriticalHitHyperdrive,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };

    /// <summary>
    /// Critical-hit messages, one per kind k (stamped with the stamp base): the shared crit function for kind k (the automatic battle's critical hit), the crit total
    /// + 1; a TractorCH crit (k = 2) leaving a holder without power releases its hold (tractor-release message, pending).
    /// </summary>
    internal void CritMessage(TacticalUnit p, CriticalKind k) => Post(Stamp, CriticalHitMessage(k), () =>
    {
        BattleEngine.Crit(p.U, k);
        p.CritTotal++;
        if (k == CriticalKind.Tractor && p.Held is { } held && TractorPowerOf(p) == 0)
        {
            PostRelease(p, held);
            p.TractorPending = true;
        }
    });

    // ---- end and exits ----

    /// <summary>
    /// End test: counts per side of units in states 2, 4, 6, 7, 8, 10; side 0 &gt; 0 and (side 1 &gt; 0 or a live Death
    /// Star scene) the battle goes on; otherwise, once: S built, the finish re-dock, world state 5 with the 4000 ms
    /// countdown; the battle-end voice right after S.
    /// </summary>
    void EndTest()
    {
        if (Ending || Done) return;
        int Count(int side) => _units.Count(u => u.Side == side && u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered or TacticalUnitState.Accelerating or TacticalUnitState.InHyperspace or TacticalUnitState.DeathStarRun);
        bool scene = BattleEngine.Alive(Sides[1].Scene);
        int n0 = Count(0), n1 = Count(1);
        if (n0 > 0 && (n1 > 0 || scene)) return;
        bool Gone(int side) => _state[side] is TacticalSideState.Jumping or TacticalSideState.JumpFinished or TacticalSideState.Withdrawn &&!_units.Any(u => u.Side == side && u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered or TacticalUnitState.Accelerating or TacticalUnitState.InHyperspace);
        S[0] = Gone(0) && _units.Any(u => u.Side == 0 && u.State == TacticalUnitState.Withdrawn) ? 1 : 0;
        S[2] = Gone(1) && (_units.Any(u => u.Side == 1 && u.State == TacticalUnitState.Withdrawn) || Sides[1].Scene?.Withdrawn == true) ? 1 : 0;
        S[1] = n1 == 0 && !scene ? 1 : 0;
        S[3] = n0 == 0 ? 1 : 0;
        if (S[1] != 0 && S[3] != 0) S[1] = S[3] = 0;
        (S[4], S[5]) = (0, 0);
        EndVoice();
        Finish();
        _end = EndMs;
    }

    /// <summary>
    /// Finish re-dock: the first word of H is set to 0 when observing; per unit of the world battle list (Inferred: the battle list in
    /// setup order) that is a fighter, active (2 / 4 / 6) or in state 1, 5 or 9: the first capital ship of the same list and side,
    /// active or in state 7, 8 or 9, whose hangar accepts it (state 1, count + 1); none, then for side index 1 a
    /// Death Star scene that is alive or withdrawn. The two follow-up messages of an unnamed kind are not modelled (no reader). Not on abort.
    /// </summary>
    void Finish()
    {
        Ui.InCommand = false;
        var battle = Sides[0].Battle;
        foreach (var f in battle.Where(u => u.Type == TacticalUnitType.Fighter && u.State is TacticalUnitState.Docked or TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.Recovered or TacticalUnitState.JumpOrdered or TacticalUnitState.Withdrawn).ToList())
        {
            var t = _byUnit[f];
            if (battle.FirstOrDefault(x => x.Type == TacticalUnitType.CapitalShip && x.Side == f.Side && x.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered or TacticalUnitState.Accelerating or TacticalUnitState.InHyperspace or TacticalUnitState.Withdrawn && BattleEngine.Dock(f, x)) is { } c) HangarOf(c).Landed.Add(t);
            else if (f.Side == 1 && Sides[1].Scene is { } sc && (BattleEngine.Alive(sc) || sc.Withdrawn) && BattleEngine.Dock(f, sc)) HangarOf(sc).Landed.Add(t);
        }
    }

    /// <summary>
    /// Abort = "Get Immediate Result": S[6] := 1, done; S[0..5] stay 0, so the application
    /// takes the withdraw branch and the automatic battle resolves the remaining forces (host state 8).
    /// </summary>
    public void Abort()
    {
        if (Done) return;
        Array.Clear(S);
        S[6] = 1;
        Done = true;
    }

    /// <summary>The result for the application (automatic flag 0): S and the shared F / C / D records of every unit.</summary>
    public BattleResult Result() => BattleEngine.Records(Sides, (int[])S.Clone(), false);
}
