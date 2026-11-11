using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

// Scheduler/clock/formula unit tests.
public class DayTickTests
{
    /// <summary>A game with no install data: G CycleCapacityPercent = 20 (shipped value), every other parameter 0.</summary>
    static GameState Bare() => new(new GameSettings(Owner.Alliance, 1, 0, 0, 1), new GameDefinitions
    {
        Sectors = [], Systems = [], CapitalShips = [], Fighters = [], Troops = [], Facilities = [], DefenseFacilities = [],
        MajorCharacters = [], MinorCharacters = [], SpecialForces = [], Missions = [], SdFiles = [], Tables = [], SeedFamilies = [],
        GeneralParams = [new ParamTableEntry(1, 1, 4096, [20, 20, 20, 20, 20, 20, 20, 20])], SideParams = [],
    });

    static ActionEvent Log(List<string> log, string name, int priority = 0) => new(priority, _ => log.Add(name));

    [Fact]
    public void DispatchOrderPriorityThenDueTimedThenFifo()
    {
        var game = Bare();
        var clock = game.Clock;
        var log = new List<string>();
        clock.PostFifo(Log(log, "fifo"));
        clock.Schedule(Log(log, "timed+1"), 1);
        clock.Schedule(Log(log, "timed0-a"), 0);
        clock.Post(Log(log, "prio16-a", 16));
        clock.Post(Log(log, "prio2", 2));
        clock.Post(Log(log, "prio16-b", 16));
        clock.Schedule(Log(log, "timed0-b"), 0);
        while (clock.DispatchOne(game)) { }
        Assert.Equal(["prio2", "prio16-a", "prio16-b", "timed0-a", "timed0-b", "fifo"], log); // timed+1 not due on Day 1
    }

    [Fact]
    public void NotificationsRunBeforeTheNextTimedEvent()
    {
        var game = Bare();
        var clock = game.Clock;
        var log = new List<string>();
        clock.Schedule(new ActionEvent(0, g => { log.Add("A"); g.Clock.Post(Log(log, "A-note", 16)); }), 0);
        clock.Schedule(Log(log, "B"), 0);
        while (clock.DispatchOne(game)) { }
        Assert.Equal(["A", "A-note", "B"], log);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(60)]
    public void DayAdvancesEveryTPlusOneTicks(int ticksPerDay)
    {
        var game = Bare();
        // With speed 1 the first tick sets DayDue, the second advances to Day 2
        game.Clock.Step(game);
        Assert.Equal((2, 1, 1, true), (game.Clock.Tick, game.Day, game.Clock.TicksPerDay, game.Clock.DayDue));
        game.Sides[Owner.Empire].Speed = ticksPerDay; // max over both sides
        game.Clock.Step(game);
        Assert.Equal((3, 2, ticksPerDay), (game.Clock.Tick, game.Day, game.Clock.TicksPerDay));
        for (int i = 0; i < ticksPerDay; i++) { Assert.Equal(2, game.Day); game.Clock.Step(game); }
        Assert.Equal((2, true), (game.Day, game.Clock.DayDue));
        game.Clock.Step(game);
        Assert.Equal(3, game.Day);
        Assert.Equal(3 + ticksPerDay + 1, game.Clock.Tick);
    }

    [Theory]
    [InlineData(1, 600)]
    [InlineData(2, 60)]
    [InlineData(3, 12)]
    [InlineData(4, 4)]
    [InlineData(0, 60)]
    public void UiSpeedTicks(int speed, int ticks) => Assert.Equal(ticks, Clock.TicksForSpeed(speed));

    [Fact]
    public void TimerFiresWhileSerialMatchesAndStaleEventsDrop()
    {
        var game = Bare();
        var units = game.Systems.SelectMany(Descendants).ToList();
        var timers = game.Clock.TimerTrace = [];
        var slot = TimerCatalogue.Slot(game, TimerType.Research, Refs.AllianceSide, game.Sides[Owner.Alliance]);
        slot.Start(game, 3, 0);           // fires day 4, 7, ...
        game.Clock.RunUntilDay(game, 5);
        slot.Start(game, 2, 0);           // restart from today (day 5): new event day 7; old event (day 7) is now stale
        game.Clock.RunUntilDay(game, 10);
        var fires = timers.Where(t => t.Fire).Select(t => (t.Day, t.Base, t.Reinserted)).ToList();
        Assert.Equal([(4, 3, true), (7, 3, false), (7, 2, true), (9, 2, true)], fires);
        Assert.Equal(0, game.Random.Draws); // zero variance draws nothing
        slot.SetActive(game, false);
        game.Clock.RunUntilDay(game, 12);
        Assert.Equal((11, false), timers.Where(t => t.Fire).Select(t => (t.Day, t.Reinserted)).Last());
    }

    [Fact]
    public void SlotSerialCountsOnlyChanges()
    {
        var game = Bare();
        var slot = TimerCatalogue.Slot(game, TimerType.Research, 1, null);
        slot.Start(game, 5, 0);
        Assert.Equal(2u, slot.Serial);   // base, active (FacilityCycle first start serial 2)
        slot.SetActive(game, false);
        slot.Start(game, 9, 0);
        Assert.Equal(5u, slot.Serial);   // deactivate, base, activate (trace: restart serial 5)
        slot.SetActive(game, false);
        slot.Start(game, 9, 0);
        Assert.Equal(7u, slot.Serial);   // unchanged base (trace: second restart serial 7)
    }

    [Theory]
    [InlineData(50, 0, 100, 5)]
    [InlineData(50, 10, 100, 6)]
    [InlineData(50, 11, 100, 7)]
    [InlineData(50, 50, 100, 10)]
    [InlineData(50, 0, 200, 10)]
    [InlineData(50, 9, 166, 9)]   // pct(6, 166) = 9
    [InlineData(0, 3, 100, 8)]    // capacity 0: d = 1, k = allocated
    [InlineData(4, 3, 100, 8)]    // pct(4, 20) = 0 gives d = 1
    public void CycleLength(int capacity, int allocated, int modifier, int expected)
    {
        var game = Bare();
        var f = new Facility(Refs.Mine, Owner.Alliance, 1, 5) { Capacity = capacity, Allocated = allocated, Modifier = modifier };
        Economy.RecomputeCycle(game, f);
        Assert.Equal(expected, f.CycleLength);
        Assert.Equal(0, game.Random.Draws);
    }

    [Fact]
    public void StartupCycleRollsOnce()
    {
        var game = Bare();
        var trace = game.Random.Trace = [];
        var f = new Facility(Refs.Mine, Owner.Alliance, 1, 5) { Capacity = 50, Allocated = 10 };
        Economy.SetState(game, f, FacilityState.Waiting); // IDLE gives WAITING sets OnStartupCycle: p = roll(6) + 3
        Assert.True(f.OnStartupCycle);
        Assert.Equal(new[] { (DrawSite.Cycle, 7, trace[0].Result) }, trace);
        Assert.Equal(trace[0].Result + 3, f.CycleLength);
    }

    [Fact]
    public void CommandsRunOnlyWhenAllQueuesAreEmpty()
    {
        var game = Bare();
        var clock = game.Clock;
        var log = new List<string>();
        // speed messages with ticks 0 (no effect); the result callback runs right after the command
        clock.Submit(new Command(CommandType.GameSpeed, Owner.Alliance), _ => { log.Add("cmd1"); clock.Post(Log(log, "cmd1-note", 16)); clock.PostFifo(Log(log, "cmd1-msg")); });
        clock.Submit(new Command(CommandType.GameSpeed, Owner.Alliance), _ => log.Add("cmd2"));
        clock.PostFifo(Log(log, "fifo"));
        clock.Schedule(Log(log, "timed"), 0);
        while (clock.DispatchOne(game)) { }
        Assert.Equal(["timed", "fifo", "cmd1", "cmd1-note", "cmd1-msg", "cmd2"], log);
    }

    [Fact]
    public void OneShotTimerDrawsItsRescheduleThenDiesStale()
    {
        var game = Bare();
        var units = game.Systems.SelectMany(Descendants).ToList();
        var timers = game.Clock.TimerTrace = [];
        var trace = game.Random.Trace = [];
        TimerCatalogue.Start(game, TimerType.Dagobah, Refs.MissionManager, null, 1, 100);
        trace.Clear();
        game.Clock.RunUntilDay(game, 2);
        Assert.Equal([(DrawSite.Timer, 101)], trace.Select(t => (t.Site, t.N)));
        var refire = timers.Last();
        Assert.True(refire is { Fire: true, Reinserted: true });
        game.Clock.RunUntilDay(game, 103);
        Assert.Single(trace);                                         // the stale event draws nothing
        Assert.False(timers.Last(t => t.Fire).Reinserted);
    }

    [Fact]
    public void TimerOutsideItsFamilyRangeIsDropped()
    {
        var game = Bare();
        var units = game.Systems.SelectMany(Descendants).ToList();
        var timers = game.Clock.TimerTrace = [];
        TimerCatalogue.Start(game, TimerType.FacilityCycle, SdRef.Make(Families.CoreSystem, 1), null, 1, 0); // the FacilityCycle timer targets families 40-47
        game.Clock.RunUntilDay(game, 3);
        Assert.False(timers.Single(t => t.Fire).Reinserted);
    }

    static Facility Refinery(GameState game, Owner side) => new(SdRef.Make(Families.Refinery, 1), side, game.NextId(), 5) { Capacity = 50 };

    [Fact]
    public void RefillsServeWaitersInOrderAndSkipStaleEntries()
    {
        var game = Bare();
        game.Ready = true;
        var side = game.Sides[Owner.Alliance];
        var (a, b, c) = (Refinery(game, Owner.Alliance), Refinery(game, Owner.Alliance), Refinery(game, Owner.Alliance));
        foreach (var f in new[] { a, b, c }) Economy.SetState(game, f, FacilityState.Waiting);
        while (game.Clock.DispatchOne(game)) { } // processing notification WAITING with raw stock 0: all three join the raw wait list
        Assert.Equal([a, b, c], side.RefineryWaitList.Select(e => e.Facility));
        Economy.SetState(game, b, FacilityState.Idle);   // b's entry goes stale (wait counter changed)
        Economy.SetState(game, b, FacilityState.Waiting); // and it queues again at the tail
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal([a, b, c, b], side.RefineryWaitList.Select(e => e.Facility));

        // A Mine delivering raw (stock 0 gives 1) posts the raw refill event.
        var mine = new Facility(Refs.Mine, Owner.Alliance, game.NextId(), 5) { Capacity = 50 };
        Economy.SetState(game, mine, FacilityState.Waiting);
        while (game.Clock.DispatchOne(game)) { } // the Mine takes its point gives PROCESSING
        Assert.Equal(FacilityState.Processing, mine.State);
        Economy.SetPointProcessed(game, mine, true); // cycle done gives DONE gives raw + 1 gives refill
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal(FacilityState.Processing, a.State);                 // one point: head served
        Assert.Equal(0, side.RawStock);
        Assert.Equal([b, c, b], side.RefineryWaitList.Select(e => e.Facility));
    }

    [Fact]
    public void IdledFacilityKeepsItsPointAndRestartsAFullCycle()
    {
        var game = Bare();
        game.Ready = true;
        var f = new Facility(SdRef.Make(Families.ConstructionYard, 1), Owner.Alliance, game.NextId(), 4) { CycleLength = 4 };
        game.Sides[Owner.Alliance].RefinedStock = 1;
        Economy.SetState(game, f, FacilityState.Waiting);
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal((FacilityState.Processing, 1 + 4), (f.State, f.CompletionDay));
        game.Clock.RunUntilDay(game, 3);
        Economy.SetState(game, f, FacilityState.Idle); // Going IDLE keeps PointPresent
        Assert.True(f.PointPresent);
        Assert.Equal(0, f.CompletionDay);
        Economy.SetState(game, f, FacilityState.Waiting); // woken: WAITING gives PROCESSING at once, full cycle from today
        Assert.Equal((FacilityState.Processing, 3 + 4), (f.State, f.CompletionDay));
    }

    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    [GameFact]
    public void MaintenanceCapAndAutoscrap()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 12345), Definitions);
        var side = game.Sides[Owner.Alliance];
        int mines = game.Facilities.Count(f => f.Owner == Owner.Alliance && Family.Of(f.Type) == Families.Mine);
        int refineries = game.Facilities.Count(f => f.Owner == Owner.Alliance && Family.Of(f.Type) == Families.Refinery);
        Assert.Equal(50 * Math.Min(mines, refineries), side.MaintenanceCapacity);
        Assert.Equal(side.Demand > side.MaintenanceCapacity, side.Exceeded);

        // Push demand over capacity: exceeded gives Maintenance timer every G MaintenanceEnforcementDays = 10 days, one autoscrap per
        // firing (the unit destroyed as a maintenance scrap, demand recomputed; the timer stops once demand fits).
        var yavin = game.Known.Yavin!;
        int cost = game.MaintenanceCost(Refs.AllianceArmy);
        for (int i = (side.MaintenanceCapacity - side.Demand) / cost + 1; i > 0; i--) yavin.Add(new GameObject(Refs.AllianceArmy, Owner.Alliance, game.NextId()));
        Economy.RecomputeSideTotals(game);
        Assert.True(side.Exceeded);
        Assert.Equal(side.MaintenanceCapacity, side.MaintenanceAllocated);
        var units = game.Systems.SelectMany(Descendants).ToList();
        var timers = game.Clock.TimerTrace = [];
        game.Sides[Owner.Alliance].Speed = 4;
        game.Clock.RunUntilDay(game, 22); // past the day-21 firing
        var fires = timers.Where(t => t.Fire && t.Reinserted && t.Type == TimerType.Maintenance).Select(t => t.Day).ToList(); // a stale firing (timer stopped) acts on nothing
        Assert.Equal(11, fires[0]);
        Assert.True(fires.SequenceEqual([11]) || fires.SequenceEqual([11, 21]));
        var scrapped = units.Concat(game.Created).Distinct().Where(o => o.AutoscrapRequested).ToList();
        Assert.Equal(fires.Count, scrapped.Count);
        Assert.All(scrapped, o => Assert.True(o.Destroyed && o.DestroyedReason == DestroyedReason.MaintenanceScrap && !o.Existing));
        Assert.DoesNotContain(game.Systems.SelectMany(Descendants), o => o.AutoscrapRequested);
        Assert.Equal(side.Demand > side.MaintenanceCapacity, side.Exceeded);
    }

    static IEnumerable<GameObject> Descendants(GameObject o) => o.Contents.SelectMany(c => Descendants(c).Prepend(c));

    [GameFact]
    public void ConstructionYardsBuildAnOrder()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 12345), Definitions);
        var coruscant = game.Known.Coruscant!; // FACLCRTB: one Construction Yard
        var yards = coruscant.Contents.OfType<Facility>().Where(f => Family.Of(f.Type) == Families.ConstructionYard).ToList();
        var yard = yards.MinBy(f => f.ProcessingRate)!; // balancing wakes the smallest rate first
        var manager = coruscant.Managers[(int)ManufacturingKind.Construction];
        int cost = game.RefinedMaterialCost(Refs.Mine);
        Assert.True(cost > 0);
        game.Sides[Owner.Empire].RefinedStock = cost;
        // A production order creates the unit (Created, not Completed) in the manager's TargetKey (default
        // own system); its existing notification joins the queue and balancing wakes the yard, whose processing notification takes a refined point gives PROCESSING.
        var result = Orders.Execute(game, new Command(CommandType.ProduceFacility, Owner.Empire) { Targets = [(manager, GroupMode.Object)], Fields = [Refs.Mine, 1] });
        Assert.True(result.Success);
        while (game.Clock.DispatchOne(game)) { }
        var unit = Assert.Single(manager.Orders);
        Assert.Same(coruscant, unit.Container);
        Assert.Equal(FacilityState.Processing, yard.State);
        Assert.Equal(game.Day + yard.ProcessingRate, yard.CompletionDay);

        game.Sides[Owner.Empire].Speed = 4;
        int start = game.Day;
        game.Clock.RunUntilDay(game, start + cost * yards.Max(f => f.ProcessingRate));
        Assert.Empty(manager.Orders);                                // one point per facility cycle, `cost` points; Completed notification dequeues
        Assert.Equal((true, true, true, true, true, false, false, false, false, false, false, false, false), (unit.Created, unit.Completed, unit.Constructed, unit.Existing,
            unit.Usable, unit.Destroyed, unit.Enroute, unit.EnrouteActive, unit.Damaged, unit.HyperdriveActive, unit.Autorouting, unit.Locked, unit.ReadyForDelete)); // same system: no travel
        Assert.Equal((0, 0), (manager.CompletedPoints, manager.Overflow)); // reset when empty
        Assert.All(yards, f => Assert.Equal(FacilityState.Idle, f.State)); // need 0 gives back to IDLE
    }

    // UI build estimate E(m, type, n, withProgress): no non-suspended member gives none; with members d0 =
    // ceildiv(need * 100, R) refined to the first day producing the need; more units take at least as long
    [GameFact]
    public void BuildEstimate()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var m = game.Systems.Select(s => s.Managers[(int)ManufacturingKind.Ship]).First(x => x is not null && Economy.Members(x).Count == 1);
        var f = Economy.Members(m)[0];
        uint type = SdRef.Make(Families.Fighter, 1);
        int need = game.RefinedMaterialCost(type);
        int expected = Enumerable.Range(0, 10000).First(d => d / f.ProcessingRate >= need);
        Assert.Equal(expected, Economy.EstimateDays(game, m, type, 1, false));
        Assert.True(Economy.EstimateDays(game, m, type, 2, false) >= expected);
        var empty = game.Systems.Select(s => s.Managers[(int)ManufacturingKind.Ship]).First(x => x is not null && Economy.Members(x).Count == 0);
        Assert.Null(Economy.EstimateDays(game, empty, type, 1, false));
    }
}
