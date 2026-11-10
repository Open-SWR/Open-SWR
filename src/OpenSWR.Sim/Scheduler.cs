namespace OpenSWR.Sim;

/// <summary>An event in one of the clock's queues.</summary>
public abstract class GameEvent
{
    /// <summary>TimedQueue key: the fire day.</summary>
    public int FireDay { get; set; }
    /// <summary>PriorityQueue key; notification 0x315 = 0x10.</summary>
    public virtual int Priority => 0;
    /// <summary>Runs the event; true = re-insert into the same queue (periodic timers), false = destroy.</summary>
    public abstract bool Run(GameState game);
}

/// <summary>One-shot event running an action (notifications, stock refills).</summary>
public sealed class ActionEvent(int priority, Action<GameState> action) : GameEvent
{
    public override int Priority => priority;
    public override bool Run(GameState game) { action(game); return false; }
}

/// <summary>
/// Ordered event queue: the head is the smallest key; a new event goes to the right of equal keys (FIFO among equals);
/// no key = pure FIFO.
/// </summary>
sealed class EventQueue(Func<GameEvent, int>? key)
{
    readonly List<GameEvent> _events = [];
    public int Count => _events.Count;
    public GameEvent? Head => _events.Count > 0 ? _events[0] : null;

    public void Insert(GameEvent e)
    {
        if (key is null) { _events.Add(e); return; }
        int k = key(e), lo = 0, hi = _events.Count;
        while (lo < hi) { int mid = (lo + hi) / 2; if (key(_events[mid]) <= k) lo = mid + 1; else hi = mid; }
        _events.Insert(lo, e);
    }

    public GameEvent Pop() { var e = _events[0]; _events.RemoveAt(0); return e; }
    /// <summary>The events in queue order (save games).</summary>
    public IReadOnlyList<GameEvent> Events => _events;
}

/// <summary>
/// Strategy clock: tick/day counters and the three event queues.
/// Not modelled: the frozen-game skip (Inferred game over).
/// </summary>
public sealed class Clock
{
    public int Tick { get; private set; } = 1;
    /// <summary>Current day; a new game starts on Day 1.</summary>
    public int Day { get; private set; } = 1;
    public int TicksPerDay { get; private set; } = 1;
    public int SubTick { get; private set; }
    public bool DayDue { get; private set; }

    readonly EventQueue _timed = new(e => e.FireDay), _fifo = new(null), _priority = new(e => e.Priority);

    /// <summary>Optional log of timer starts and fires (differential tests).</summary>
    public List<TimerRecord>? TimerTrace { get; set; }

    /// <summary>Schedule with delay: fireDay = Day + delay, TimedQueue.</summary>
    public void Schedule(GameEvent e, int delay) { e.FireDay = Day + delay; _timed.Insert(e); }
    /// <summary>Post to the PriorityQueue.</summary>
    public void Post(GameEvent e) => _priority.Insert(e);
    /// <summary>Post to the FifoQueue.</summary>
    public void PostFifo(GameEvent e) => _fifo.Insert(e);

    /// <summary>
    /// Serial counter: +1 per successful command or sub-command and per internal consumer such as a mission creation.
    /// Start value Inferred (the setup consumers, e.g. system init, are not modelled); only increments matter.
    /// </summary>
    public uint Serial { get; private set; }
    public uint NextSerial() => ++Serial;

    readonly List<(Command Command, Action<OrderResult>? Done)> _commands = [];
    /// <summary>
    /// Appends a command (player/AI order) to the pending command list; <paramref name="done"/> receives the result when
    /// it runs. The list is ordered by the controller sequence (lowest first); unnumbered commands
    /// (sequence 0) keep FIFO order among themselves and run first (the original destroys a sequence-0 command; the sim keeps
    /// them for tests, replays and the UI's immediate orders).
    /// </summary>
    /// <summary>Optional log of the pending commands in dispatch order (differential tests).</summary>
    public List<Command>? CommandTrace { get; set; }
    /// <summary>When set, called with every executed command and its result (soak tests).</summary>
    public Action<Command, OrderResult>? Executed { get; set; }

    public void Submit(Command command, Action<OrderResult>? done = null) =>
        _commands.Insert(_commands.FindLastIndex(c => c.Command.Sequence <= command.Sequence) + 1, (command, done));

    // ---- save games (SaveGame): state access, no behaviour ----
    internal IEnumerable<Command> PendingCommands => _commands.Select(c => c.Command);
    internal IReadOnlyList<GameEvent> TimedEvents => _timed.Events;
    internal bool HasQueuedNotifications => _priority.Count > 0 || _fifo.Count > 0;
    internal void Restore(int tick, int day, int ticksPerDay, int subTick, bool dayDue, uint serial)
        => (Tick, Day, TicksPerDay, SubTick, DayDue, Serial) = (tick, day, ticksPerDay, subTick, dayDue, serial);
    internal void RestoreTimed(GameEvent e) => _timed.Insert(e);

    /// <summary>UI speed 1-4 gives ticks per day 600 / 60 / 12 / 4; anything else 60.</summary>
    public static int TicksForSpeed(int uiSpeed) => uiSpeed switch { 1 => 600, 2 => 60, 3 => 12, 4 => 4, _ => 60 };

    /// <summary>
    /// One host-loop tick: advance the clock (stage 0), the controllers' tick tokens (stage 1) and outbox submission (stage 3),
    /// dispatch until nothing is left (stage 5), the controllers' message delivery (stage 6) and think (stage 8), then the battle
    /// check (stage 13) and on the DayDue tick the controllers' budget reset (stage 13). Without
    /// controllers (<see cref="GameState.Controllers"/> empty: tests, replays) the controller stages do nothing.
    /// </summary>
    public void Step(GameState game)
    {
        Ai.Controller.StartGame(game); // game start before the first tick; no-op once started
        if (game.HostedBattle is not null) // host waiting in state 6 for a controller's answer: only stage 13 runs
        {
            Combat.HostBattles(game);
            return;
        }
        if (!game.GameOver) Advance(game); // no tick / day increment after game over
        var controllers = game.Controllers.Values;
        foreach (var c in controllers) c.MakeToken();
        foreach (var c in controllers) c.SubmitOutbox(game);
        while (DispatchOne(game)) { }
        Ai.Controller.Deliver(game);
        foreach (var c in controllers) c.Think(game);
        if (game.BattleRequired) Combat.HostBattles(game);
        if (DayDue) foreach (var c in controllers) c.DayDue();
    }

    /// <summary>Runs the queued events, not the pending commands (Inferred: notification flush).</summary>
    public void Flush(GameState game)
    {
        while (DispatchOne(game, commands: false)) { }
    }

    /// <summary>Steps until Day has advanced to <paramref name="day"/> and its events have run.</summary>
    public void RunUntilDay(GameState game, int day)
    {
        while (Day < day && !game.GameOver && game.HostedBattle is null) Step(game);
    }

    void Advance(GameState game)
    {
        Tick++;
        if (DayDue) { DayDue = false; SubTick = 0; Day++; }
        else SubTick++;
        TicksPerDay = Math.Max(game.Sides[Owner.Alliance].Speed, game.Sides[Owner.Empire].Speed);
        DayDue = SubTick >= TicksPerDay;
    }

    /// <summary>
    /// Pops and runs one event: PriorityQueue head, else the TimedQueue head if due, else the FifoQueue head; with all three
    /// empty, the next pending command.
    /// </summary>
    public bool DispatchOne(GameState game, bool commands = true)
    {
        EventQueue? queue = _priority.Count > 0 ? _priority
            : _timed.Head is { } t && t.FireDay <= Day ? _timed
            : _fifo.Count > 0 ? _fifo : null;
        if (queue is null)
        {
            if (!commands || _commands.Count == 0) return false;
            var (command, done) = _commands[0];
            _commands.RemoveAt(0);
            CommandTrace?.Add(command);
            if (!game.GameOver) // refused after game over
            {
                var result = Orders.Execute(game, command);
                Executed?.Invoke(command, result);
                done?.Invoke(result);
            }
            return true;
        }
        var e = queue.Pop();
        if (e.Run(game)) queue.Insert(e);
        return true;
    }
}

/// <summary>A timer start (Fire = false) or firing, for traces.</summary>
public readonly record struct TimerRecord(int Day, bool Fire, int Type, uint Target, int Base, int Variance, int FireDay, uint Serial, bool Reinserted);

/// <summary>
/// Periodic timer slot: Serial plus Base / Variance / Active. Every change of a part increments Serial; setting Active or
/// Base while Active schedules an event with delay Base + roll(Variance).
/// </summary>
public sealed class TimerSlot(int type, uint target, object? subject, Action<GameState, TimerSlot> action)
{
    public int Type { get; } = type;
    /// <summary>Target object reference (family and id), as held by the event.</summary>
    public uint Target { get; } = target;
    /// <summary>The target object, for the action.</summary>
    public object? Subject { get; } = subject;
    public uint Serial { get; private set; }
    public int Base { get; private set; }
    public int Variance { get; private set; }
    public bool Active { get; private set; }

    /// <summary>Start/restart: Variance, then Base, then Active (order Inferred; trace serial counts agree).</summary>
    public void Start(GameState game, int @base, int variance)
    {
        if (variance != Variance) { Variance = variance; Serial++; }
        SetBase(game, @base);
        SetActive(game, true);
    }

    public void SetBase(GameState game, int @base)
    {
        if (@base == Base) return;
        Base = @base;
        Serial++;
        if (@base < 0) Active = false; // a negative Base clears Active
        ScheduleIfActive(game);
    }

    public void SetActive(GameState game, bool active)
    {
        if (active == Active) return;
        Active = active;
        Serial++;
        ScheduleIfActive(game);
    }

    void ScheduleIfActive(GameState game)
    {
        if (!Active) return;
        var e = new TimerEvent(this);
        game.Clock.Schedule(e, Base + game.Random.Roll(Variance, DrawSite.Timer));
        game.Clock.TimerTrace?.Add(new(game.Day, false, Type, Target, Base, Variance, e.FireDay, Serial, false));
        if (Type == 0x387) game.Record(0x387, Target, Base, e.FireDay);
    }

    internal void Act(GameState game) => action(game, this);
    internal void Restore(uint serial, int @base, int variance, bool active) => (Serial, Base, Variance, Active) = (serial, @base, variance, active);
}

/// <summary>
/// Timer event: copy of the slot's Serial/Base/Variance. Fires only if the target resolves (family in the type's range) and the
/// slot Serial still matches: action first, then fireDay += Base + roll(Variance) from the event's copy, re-insert; otherwise
/// dropped without a draw.
/// A Destroyed target does not resolve (verified for all timer classes 0x380-0x394).
/// </summary>
sealed class TimerEvent(TimerSlot slot, uint serial, int @base, int variance) : GameEvent
{
    public TimerEvent(TimerSlot slot) : this(slot, slot.Serial, slot.Base, slot.Variance) { }
    readonly uint _serial = serial;
    readonly int _base = @base, _variance = variance;
    internal TimerSlot Slot => slot;
    internal (uint Serial, int Base, int Variance) Copy => (_serial, _base, _variance);

    public override bool Run(GameState game)
    {
        int fireDay = FireDay;
        bool current = TimerCatalogue.InRange(slot.Type, slot.Target) && slot.Serial == _serial
            && slot.Subject is not GameObject { Destroyed: true };
        if (current)
        {
            slot.Act(game);
            FireDay += _base + game.Random.Roll(_variance, DrawSite.Timer);
        }
        game.Clock.TimerTrace?.Add(new(game.Day, true, slot.Type, slot.Target, _base, _variance, fireDay, _serial, current));
        // the 0x31A kept this slot for its queued event (Objects.Delete); dropped now, it can never fire again
        if (!current && slot.Subject is GameObject { Deleted: true } && game.Timers.GetValueOrDefault((slot.Type, slot.Target)) == slot
            && !game.Clock.TimedEvents.OfType<TimerEvent>().Any(e => e != this && e.Slot == slot))
            game.Timers.Remove((slot.Type, slot.Target));
        return current;
    }
}
