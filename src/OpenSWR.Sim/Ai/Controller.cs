namespace OpenSWR.Sim.Ai;

/// <summary>
/// Side controller (host side 1, side 2; common base): a sequence counter (side 1 from 1, side 2 from
/// 2000000001), an outbox ordered by sequence, the tick token. Per tick (<see cref="Clock.Step"/>): stage 1 token, stage 3
/// submit (every outbox entry in sequence order, the token last), stage 6 message delivery (<see cref="Deliver"/>), stage 8
/// think, stage 13 budget reset on the DayDue tick. Human and computer controllers both acknowledge destroyed objects (AcknowledgeDestroyed command).
/// </summary>
public abstract class Controller
{
    protected Controller(Owner side)
    {
        Side = side;
        NextSequence = side == Owner.Alliance ? 1u : 2000000001u;
    }

    /// <summary>The battle decision float (the side's withdraw threshold).</summary>
    internal virtual double BattleFloat(GameState game, StarSystem s) => 0.0;

    /// <summary>Host state 6: the controller's decision answer is in; false = the host waits (clock paused).</summary>
    internal virtual bool BattleAnswered(GameState game, StarSystem s) => true;

    /// <summary>The decision answer is 2 = tactical ("take command"); the computer controller always answers 1.</summary>
    internal virtual bool BattleTactical => false;

    /// <summary>The battle has run: a human controller's request and answer are released.</summary>
    internal virtual void BattleDone() { }

    /// <summary>Controller type: 1 human, 3 computer.</summary>
    public abstract int Kind { get; }
    public Owner Side { get; }
    /// <summary>Outbox sequence counter: the next command's sequence.</summary>
    public uint NextSequence { get; internal set; }

    readonly SortedList<uint, Command> _outbox = [];
    /// <summary>Commands waiting for the next stage-3 submit, by sequence.</summary>
    public IList<Command> Outbox => _outbox.Values;

    /// <summary>Outbox append: a sequence from the counter if the command has none.</summary>
    public void Append(Command c)
    {
        if (c.Sequence == 0) c.Sequence = NextSequence++;
        _outbox.Add(c.Sequence, c);
    }

    /// <summary>Stage 1: the tick token, command type 1 (no sim effect).</summary>
    internal void MakeToken() => Append(new Command(CommandType.TickToken, Side));

    /// <summary>Stage 3: every outbox entry to the clock's pending list, lowest sequence first (the token last).</summary>
    internal void SubmitOutbox(GameState game)
    {
        foreach (var c in _outbox.Values) game.Clock.Submit(c);
        _outbox.Clear();
    }

    /// <summary>
    /// One controller message of this side. Base (human and computer): Destroyed view message gives acknowledge with AcknowledgeDestroyed command,
    /// Targets = [(object, GroupMode.Object)], not counted (the count is always 0 in single player).
    /// </summary>
    internal virtual void Receive(GameState game, ViewMessageType type, GameObject o, GameObject? a, GameObject? b)
    {
        if (type == ViewMessageType.Destroyed) Append(new Command(CommandType.AcknowledgeDestroyed, Side) { Targets = [(o, GroupMode.Object)] });
    }

    /// <summary>Stage 6 before the queued messages (computer: the field changes it observes per tick).</summary>
    internal virtual void Observe(GameState game) { }

    /// <summary>Game start has run.</summary>
    public bool Started { get; private set; }

    /// <summary>Game start: computer = model init, human = the advisor's catalogue lists.</summary>
    protected abstract void Start(GameState game);

    /// <summary>
    /// Game start: side 1's controller, then side 2's, each once, before the first tick. A
    /// loaded game does not repeat it: <see cref="AfterLoad"/> runs the load's own effects instead, its only
    /// draws being the catalogue lists. The UI generator is neither restored nor reseeded at a load. An old save
    /// without the computer object (rebuild path) runs this on its first tick: model init and fresh planners, all operations lost;
    /// not the original's behaviour, which continues the saved AI.
    /// </summary>
    public static void StartGame(GameState game)
    {
        foreach (var c in game.Controllers.Values)
            if (!c.Started) { c.Start(game); c.Started = true; }
    }

    /// <summary>
    /// The original's load effects (host load: side 1's controller first, then side 2's): computer =
    /// catalogue lists at the saved research levels, every record dirty, tree key counters, product / cursor resets, observation
    /// snapshot from the loaded state (<see cref="ComputerController.LoadEffects"/>); human = the advisor model load's seven
    /// catalogue lists at research level 0 (level Inferred). Game start is not repeated.
    /// </summary>
    public static void AfterLoad(GameState game)
    {
        foreach (var c in game.Controllers.Values.OrderBy(c => c.Side))
        {
            if (c is ComputerController computer) computer.LoadEffects(game);
            else if (c is HumanController human) WorldModel.CatalogueLists(game, human.Side, human.Rand);
            c.Started = true;
        }
    }

    /// <summary>Stage 8 think; returns when the cycle is complete. Human: nothing (no modal UI in the sim).</summary>
    internal virtual void Think(GameState game) { }

    /// <summary>Stage 13 on the DayDue tick.</summary>
    internal virtual void DayDue() { }

    /// <summary>
    /// Stage 6: each controller observes, then every queued message goes to the controller of its side (other
    /// sides are dropped), in queue order.
    /// </summary>
    public static void Deliver(GameState game)
    {
        if (game.Controllers.Count == 0) return;
        foreach (var c in game.Controllers.Values) c.Observe(game);
        foreach (var (side, type, o, a, b) in game.Messages)
            if (game.Controllers.TryGetValue(side, out var c)) c.Receive(game, type, o, a, b);
        game.Messages.Clear();
    }
}

/// <summary>Human controller (type 1): tokens and AcknowledgeDestroyed acknowledgements; its orders come from the UI and the advisor's delegation cycle.</summary>
public sealed class HumanController(Owner side) : Controller(side)
{
    public override int Kind => 1;

    /// <summary>
    /// With a battle window: the host waits for <see cref="BattleAnswer"/>. Off (tests, replays, no UI): the
    /// side answers "simulate" at once.
    /// </summary>
    public bool AskBattles { get; set; }
    /// <summary>The system of the open battle request (answer 0, float 0.0), null when none.</summary>
    public StarSystem? BattleRequest { get; private set; }
    /// <summary>The window's answer: 0.0 = simulate (never withdraw), 10000.0 = retreat (withdraw at once).</summary>
    public double? BattleAnswer { get; set; }
    /// <summary>"Take command" (answer 2, float 0.0): set together with <see cref="BattleAnswer"/> 0.0.</summary>
    public bool TakeCommand { get; set; }

    internal override double BattleFloat(GameState game, StarSystem s) => BattleAnswer ?? 0.0;
    internal override bool BattleTactical => TakeCommand && BattleAnswer.HasValue;

    internal override bool BattleAnswered(GameState game, StarSystem s)
    {
        if (!AskBattles || BattleAnswer.HasValue) return true;
        BattleRequest = s;
        return false;
    }

    internal override void BattleDone() => (BattleRequest, BattleAnswer, TakeCommand) = (null, null, false);

    /// <summary>The advisor runtime: built only by the strategy screen; null in replays, soak and Sim tests.</summary>
    public Advisor? Advisor { get; set; }

    /// <summary>The UI generator's rand(n) for the advisor (default the process-wide generator).</summary>
    public Func<int, int> Rand { get; init; } = n => UiRandom.Shared.Rand(n);

    /// <summary>Slot: the advisor's model init draws the seven catalogue lists for the human's side (results unused).</summary>
    protected override void Start(GameState game) => WorldModel.CatalogueLists(game, Side, Rand);

    /// <summary>
    /// Slot: Uprising view message whose new value is set = the uprising alarm 621. Counted
    /// here; the screen plays the sound and clears the count (not saved).
    /// </summary>
    public int UprisingAlarms { get; set; }

    /// <summary>
    /// Slot: NeverBeenControlled view message with the new value set (A = the system) =
    /// sound 620 (a never-controlled system newly seen). Counted here; the screen plays the sound and clears the count (not saved).
    /// </summary>
    public int NeverControlledAlarms { get; set; }

    /// <summary>
    /// openswr deviation: planetary assaults with this side attacking or defending (<see cref="Combat.Assault"/>). The screen plays 620
    /// for these instead of for <see cref="NeverControlledAlarms"/> (player request: the alert only on an invasion). Not saved.
    /// </summary>
    public int AssaultAlarms { get; set; }

    /// <summary>Stage 8: the advisor's delegation cycle after game start; without an advisor nothing.</summary>
    internal override void Think(GameState game)
    {
        if (Started) Advisor?.Delegate(game, this);
    }

    /// <summary>Stage 13 on the DayDue tick: the delegation budget resets.</summary>
    internal override void DayDue() => Advisor?.DayDue();

    internal override void Receive(GameState game, ViewMessageType type, GameObject o, GameObject? a, GameObject? b)
    {
        if (type == ViewMessageType.Uprising && o is StarSystem s && Knowledge.See(game, s, Side).Uprising) UprisingAlarms++;
        if (type == ViewMessageType.NeverBeenControlled && a is not null) NeverControlledAlarms++;
        base.Receive(game, type, o, a, b);
    }
}

/// <summary>Think state of the computer controller: new (model not built), model built (planners not yet created), issuing commands, idle between thinks.</summary>
public enum ComputerThinkState { New, ModelReady, Issuing, Idle }

/// <summary>Planner status: active, suspended, other = idle.</summary>
public enum PlannerStatus { Idle, Active, Suspended }

/// <summary>
/// Planner (base): status (1 active, 2 suspended, other idle), done flag, priority. The shared think makes an
/// idle planner active with done := 0; step returns at most one command (sets done when finished); the failure slot marks the
/// planner done and clears its priority. The planners' decisions are later rounds: this base is the empty stub, it
/// finishes at once without a command.
/// </summary>
public class Planner(PlannerType type)
{
    public PlannerType Type { get; } = type;
    public PlannerStatus Status { get; set; }
    public bool Done { get; set; }
    public int Priority { get; set; }
    /// <summary>Phase and state (planners without phases use the state only).</summary>
    public int Phase { get; protected set; }
    public int State { get; protected set; }

    /// <summary>Think (shared; planner 14 only while the model value is &gt; 0).</summary>
    public virtual void Think(ComputerController controller)
    {
        if (Type == PlannerType.Naming && controller.Model.UnnamedShips <= 0) return;
        if (Status is not (PlannerStatus.Active or PlannerStatus.Suspended)) (Status, Done) = (PlannerStatus.Active, false);
    }

    /// <summary>Step: at most one command; stub = finished, no command.</summary>
    public virtual Command? Step(GameState game, ComputerController controller)
    {
        Done = true;
        return null;
    }

    /// <summary>Load effect: the fields the original's planner load leaves at their constructor value.</summary>
    internal virtual void AfterLoad() { }

    /// <summary>
    /// Failure: done, phase and state := 0, priority cleared, the command dropped
    /// (planner 14 overrides it).
    /// </summary>
    public virtual void Failure(OrderResult result, Command command) => (Done, Phase, State, Priority) = (true, 0, 0, 0);
}

/// <summary>
/// Computer controller (type 3): think state machine (1 model init, 2 planners, 4 model update,
/// 5 planner think, 3 issue), 14 planners in type order, the world model, the command budget (10 counted commands per budget
/// period; the period is 0 days, so it resets on every DayDue tick; Rename and AcknowledgeDestroyed commands and tokens not counted). Each think call runs
/// one complete cycle (the clock waits for it). Draws only from <see cref="UiRandom"/> (planners; the model draws nothing).
/// </summary>
public sealed class ComputerController(Owner side) : Controller(side)
{
    /// <summary>Planner creation order (state 2); the list itself is kept in type order.</summary>
    public static readonly PlannerType[] CreationOrder = [PlannerType.Strategy, PlannerType.Mine, PlannerType.Construction, PlannerType.Defence, PlannerType.Troop, PlannerType.SystemFighter, PlannerType.MissionOperation, PlannerType.SpecialForce, PlannerType.FleetOperation, PlannerType.FleetTask, PlannerType.FleetFighter, PlannerType.CapitalShip, PlannerType.CharacterOperation, PlannerType.Naming];

    public override int Kind => 3;
    /// <summary>Think state.</summary>
    public ComputerThinkState State { get; private set; } = ComputerThinkState.New;
    /// <summary>Command budget (10, set in state 2).</summary>
    public int Budget { get; private set; }
    /// <summary>Counted commands of the current budget period.</summary>
    public int Issued { get; internal set; }
    /// <summary>Days since the last budget reset; the period is always 0.</summary>
    public int DaysSinceReset { get; internal set; }
    /// <summary>World model.</summary>
    public WorldModel Model { get; } = new(side);
    /// <summary>
    /// Planner factory by type: production planners 1, 2, 3, 5, 6, 7, 10, 11 (planner 11's moves round 7),
    /// 14 names; 4, 8, 9, 12, 13 are empty stubs until their rounds.
    /// </summary>
    public Func<PlannerType, Planner> PlannerFactory { get; init; } = type => type switch
    {
        PlannerType.Construction => new ConstructionPlanner(),
        PlannerType.MissionOperation => new MissionOpPlanner(),
        PlannerType.FleetOperation => new FleetOpPlanner(),
        PlannerType.FleetTask => new FleetTaskPlanner(),
        PlannerType.CharacterOperation => new CharacterOpPlanner(),
        PlannerType.Strategy => new StrategyPlanner(),
        PlannerType.Mine => new MinePlanner(),
        PlannerType.Defence => new DefencePlanner(),
        PlannerType.SpecialForce => new SpecialForcePlanner(),
        PlannerType.Troop => new TroopPlanner(),
        PlannerType.SystemFighter => new SystemFighterPlanner(),
        PlannerType.FleetFighter => new FleetFighterPlanner(),
        PlannerType.CapitalShip => new CapitalShipPlanner(),
        PlannerType.Naming => new NamingPlanner(),
        _ => new Planner(type),
    };
    /// <summary>The UI generator's rand(n) for the planners and the model's list ties (default the process-wide generator).</summary>
    public Func<int, int> Rand { get => Model.Rand; init => Model.Rand = value; }
    /// <summary>TEXTSTRA string by id (ship names, planner 14); null = names unavailable.</summary>
    public Func<int, string?>? Strings { get; set; }
    /// <summary>Planner list in type order.</summary>
    public List<Planner> Planners { get; } = [];

    /// <summary>Optional log of every planner command with its validation result (differential tests).</summary>
    public List<(PlannerType Planner, Command Command, OrderResult Result)>? IssueTrace { get; set; }
    /// <summary>Optional per-step log of the issue phase: tick, planner (0 = null pick), phase before the step, command type (0 none), done, model dirty after it.</summary>
    public List<(int Tick, PlannerType Planner, int Phase, int Command, bool Done, bool Dirty)>? StepTrace { get; set; }

    Planner? _current;
    int _urgent = -1, _roundRobin = -1; // list cursors; -1 = none (0 in the original)

    /// <summary>
    /// The AI's message handlers: the Owner message (ControlKind; a system gives the theaters are due), the Usable, Created, Completed and Enroute messages, Damaged, the system fields (Support, Populated, Explored, Uprising, NeverBeenControlled, Battle, Blockade), the manager queue count gives changed;
    /// Parent gives <see cref="WorldModel.ParentChanged"/>; Destroyed gives changed(parent), detach (Inferred: as changed;
    /// records are never removed), then the base acknowledgement; Name gives the ship record's Named bit. Before the model
    /// exists (the first think builds it) messages only acknowledge.
    /// </summary>
    internal override void Receive(GameState game, ViewMessageType type, GameObject o, GameObject? a, GameObject? b)
    {
        // a foreign mission's messages (the opposing view's refresh and staging pass): no record, no dirty; only the
        // base acknowledgement of its Destroyed message
        if (State != ComputerThinkState.New && !(o is Mission && o.Owner != Side))
            switch (type)
            {
                case ViewMessageType.Owner:
                    if (o is StarSystem) Model.TheatersDue = true;
                    Model.Changed(game, o);
                    break;
                case ViewMessageType.Usable or ViewMessageType.Created or ViewMessageType.Completed or ViewMessageType.Enroute or ViewMessageType.Damaged or ViewMessageType.Support
                    or ViewMessageType.Populated or ViewMessageType.Explored or ViewMessageType.Uprising or ViewMessageType.NeverBeenControlled or ViewMessageType.Battle or ViewMessageType.Blockade
                    or ViewMessageType.QueueCount:
                    Model.Changed(game, o);
                    break;
                case ViewMessageType.Parent:
                    Model.ParentChanged(game, o, a, b);
                    break;
                case ViewMessageType.Destroyed:
                    if (o is Mission) MissionOps.MissionEnded(Model, o);
                    if (a is not null) Model.Changed(game, a);
                    Model.Changed(game, o);
                    Model.Detach(o);
                    break;
                case ViewMessageType.EspionageExtraSystem: // espionage ExtraSystem: changed(mission), then the system's day field := day
                    Model.Changed(game, o);
                    if (Model.Record(a) is { } xr) { xr.BlockadeDay = game.Day; Model.DirtySystem(xr); }
                    break;
                case ViewMessageType.Name:
                    Model.Named(game, o);
                    break;
                case ViewMessageType.TaskStatus:
                    if (o is Mission tm) MissionOps.TaskStatus(game, Model, tm);
                    break;
                case ViewMessageType.CompletionStatus:
                    if (o is Mission cm) MissionOps.Completion(Model, cm);
                    break;
            }
        base.Receive(game, type, o, a, b);
    }

    /// <summary>Messages derived from per-tick field changes of the side's live views (systems, managers, units, side object).</summary>
    internal override void Observe(GameState game)
    {
        if (State != ComputerThinkState.New) Model.Observe(game);
    }

    /// <summary>Slot: think state 1, the model init (its only draws are the catalogue lists).</summary>
    protected override void Start(GameState game)
    {
        if (State == ComputerThinkState.New) { Model.Init(game); State = ComputerThinkState.ModelReady; }
    }

    internal override void Think(GameState game)
    {
        Start(game);
        if (State == ComputerThinkState.ModelReady)
        {
            foreach (PlannerType type in CreationOrder)
                if (Planners.All(p => p.Type != type)) Planners.Add(PlannerFactory(type));
            Planners.Sort((a, b) => a.Type.CompareTo(b.Type)); // declaration order = planner number order
            Budget = 10;
            State = ComputerThinkState.Idle;
        }
        Model.Update(game);
        foreach (var p in Planners) p.Think(this); // the disable flag is never set
        State = ComputerThinkState.Issuing;
        Issue(game);
        State = ComputerThinkState.Idle;
    }

    const int MaxIssueSteps = 100_000; // issue-loop steps per cycle

    /// <summary>Issue phase (state 3), one loop pass per original frame, until the cycle ends.</summary>
    void Issue(GameState game)
    {
        int steps = 0;
        while (Issued < Budget)
        {
            // openswr guard, not in the original: a planner that never reaches Done and issues nothing would spin here for ever;
            // give up for this cycle (the planner is picked again in the next one)
            if (++steps > MaxIssueSteps) { _current = null; return; }
            if (_current is null)
            {
                if ((_current = Pick()) is null) { StepTrace?.Add((game.Clock.Tick, PlannerType.None, 0, 0, true, Model.Dirty)); return; }
                continue;
            }
            var p = _current;
            p.Priority = 0;
            int phase = p.Phase;
            var c = p.Step(game, this);
            if (c is not null)
            {
                if (c.Side != Side) throw new InvalidOperationException($"Planner {p.Type} issued a command for side {c.Side}.");
                // targets and the decoys of a CreateMission command go into ordered trees: ascending SdRef
                var ordered = c.Targets.OrderBy(t => t.Target.Ref).ToList();
                c.Targets.Clear();
                c.Targets.AddRange(ordered);
                var decoys = c.Decoys.OrderBy(d => d.Ref).ToList();
                c.Decoys.Clear();
                c.Decoys.AddRange(decoys);
                var result = Orders.Validate(game, c);
                IssueTrace?.Add((p.Type, c, result));
                if (result.Success)
                {
                    Append(c);
                    if (c.Type != CommandType.Rename) Issued++;
                }
                else p.Failure(result, c);
            }
            StepTrace?.Add((game.Clock.Tick, p.Type, phase, (int)(c?.Type ?? 0), p.Done, Model.Dirty));
            if (!p.Done) continue; // not done: stepped again in the next frame, no dirty test
            p.Status = PlannerStatus.Idle;
            _current = null;
            if (c is not null || Model.Dirty) return;
        }
    }

    /// <summary>
    /// Planner pick: the urgent cursor advances by one entry on every call (none / end gives first),
    /// then one cycle from it keeps the active planner with the largest priority compared unsigned (strictly greater, from 0);
    /// else the round-robin cursor advances first, then forward cyclically to the first active planner (back at the start gives
    /// none); it stays on the planner returned. The urgent cursor does not move to the urgent pick.
    /// </summary>
    Planner? Pick()
    {
        int n = Planners.Count;
        if (n == 0) return null;
        _urgent = (_urgent + 1) % n;
        Planner? best = null;
        for (int k = 0; k < n; k++)
            if (Planners[(_urgent + k) % n] is { Status: PlannerStatus.Active } p && (uint)p.Priority > (uint)(best?.Priority ?? 0)) best = p;
        if (best is not null) return best;
        _roundRobin = (_roundRobin + 1) % n;
        for (int k = 0; k < n; k++)
            if (Planners[(_roundRobin + k) % n].Status == PlannerStatus.Active) return Planners[_roundRobin = (_roundRobin + k) % n];
        return null;
    }

    /// <summary>
    /// Strategy slot: S not own or without usable fighters (S fighters <= 0) gives 0.9; own HQ / Coruscant (a key system) gives 0.0;
    /// else own / enemy sum of the fleets' strength at S, capped at 0.9 (no enemy strength gives 0.9). Before the model exists: 0.9 (Inferred).
    /// </summary>
    internal override double BattleFloat(GameState game, StarSystem s)
    {
        if (State == ComputerThinkState.New || Model.Record(s) is not { } r || !r.Own || r.UsableFighters <= 0) return 0.9;
        if (r.KeySystem) return 0.0;
        var here = Model.FleetRecords().Where(f => f.Location == s).ToList();
        double own = here.Where(f => f.Own).Sum(f => f.Firepower), enemy = here.Where(f => !f.Own).Sum(f => f.Firepower);
        return Math.Min(0.9, enemy > 0 ? own / enemy : own);
    }

    internal override void DayDue()
    {
        if (++DaysSinceReset >= 0) (DaysSinceReset, Issued) = (0, 0); // the period is always 0
    }

    /// <summary>Restores the saved counters (<see cref="SaveGame"/>); without <see cref="Load"/> (old save) model and planners are rebuilt by the next think.</summary>
    internal void Restore(int issued, int daysSinceReset) => (Issued, DaysSinceReset) = (issued, daysSinceReset);

    string TypeAt(int index) => (index < 0 ? PlannerType.None : Planners[index].Type).ToString();
    int IndexOf(string type) => Planners.FindIndex(p => p.Type == Enum.Parse<PlannerType>(type));

    /// <summary>
    /// The computer object of the save: think state, budget, current planner and
    /// the two pick cursors (as planner types, None = none), the world model, the planners in type order, then the table of records,
    /// operations and tasks. The received-command list has no counterpart.
    /// </summary>
    internal System.Text.Json.Nodes.JsonObject Save(Func<GameObject, uint> r)
    {
        var w = new AiSave.Writer(r);
        var planners = new System.Text.Json.Nodes.JsonArray();
        foreach (var p in Planners) planners.Add(new System.Text.Json.Nodes.JsonObject { ["type"] = p.Type.ToString(), ["f"] = w.Fields(p) });
        var j = new System.Text.Json.Nodes.JsonObject
        {
            ["state"] = State.ToString(), ["budget"] = Budget, ["current"] = (_current?.Type ?? PlannerType.None).ToString(), ["urgent"] = TypeAt(_urgent), ["roundRobin"] = TypeAt(_roundRobin),
            ["model"] = w.Fields(Model), ["planners"] = planners,
        };
        j["table"] = w.Table();
        return j;
    }

    /// <summary>Restores <see cref="Save"/>; the load effects follow in <see cref="LoadEffects"/>.</summary>
    internal void Load(System.Text.Json.Nodes.JsonObject j, GameState game, Func<uint, GameObject> o)
    {
        var reader = new AiSave.Reader(game, Model, o, j["table"]!.AsArray());
        reader.Fill(Model, j["model"]!.AsObject());
        Planners.Clear();
        foreach (var e in j["planners"]!.AsArray())
        {
            var p = PlannerFactory(Enum.Parse<PlannerType>((string)e!["type"]!));
            reader.Fill(p, e["f"]!.AsObject());
            Planners.Add(p);
        }
        reader.FillTable();
        (State, Budget) = (Enum.Parse<ComputerThinkState>((string)j["state"]!), (int)j["budget"]!);
        int current = IndexOf((string)j["current"]!);
        _current = current < 0 ? null : Planners[current];
        (_urgent, _roundRobin) = (IndexOf((string)j["urgent"]!), IndexOf((string)j["roundRobin"]!));
    }

    /// <summary>The computer side of <see cref="Controller.AfterLoad"/>.</summary>
    internal void LoadEffects(GameState game)
    {
        Model.AfterLoad(game);
        foreach (var p in Planners) p.AfterLoad();
    }
}
