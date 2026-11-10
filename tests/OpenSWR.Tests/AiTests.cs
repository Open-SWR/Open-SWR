using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Tests;

/// <summary>Controller timing (scripted planners), the UI generator, and the world model of a new game.</summary>
public class AiTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>Planner with a fixed command script: call n of its step returns script(game, n); finish = done after each step.</summary>
    sealed class Script(PlannerType type, Func<GameState, int, Command?> script, bool finish = true) : Planner(type)
    {
        int _n;
        public override Command? Step(GameState game, ComputerController controller)
        {
            var c = script(game, _n++);
            Done = finish || c is null;
            return c;
        }
    }

    /// <summary>Player Alliance (human, side 1), computer Empire (side 2) with planner 1 replaced by <paramref name="planner"/>.</summary>
    static (GameState Game, ComputerController Ai, List<Command> Trace) Game(Func<PlannerType, Planner?> planner)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var ai = new ComputerController(Owner.Empire) { PlannerFactory = t => planner(t) ?? new Planner(t) };
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        game.Controllers[Owner.Empire] = ai;
        game.Clock.CommandTrace = [];
        return (game, ai, game.Clock.CommandTrace);
    }

    static ManufacturingManager Manager(GameState game) =>
        game.Systems.Where(s => s.Owner == Owner.Empire).SelectMany(s => s.Managers).First(m => m.Completed && Economy.Members(m).Any(f => !f.Suspended)); // SetReserved validates like a production order
    static Command Reserve(GameState game) => new(CommandType.SetReserved, Owner.Empire) { Targets = [(Manager(game), 0)] };

    // Tokens numbered per side (side 1 from 1, side 2 from 2001000961); a command decided in tick T runs in T + 1,
    // submitted before that tick's token; side 1's pending commands run before side 2's (lowest sequence first)
    [GameFact]
    public void TokensAndDecisionTiming()
    {
        var (game, _, trace) = Game(t => t == PlannerType.Construction ? new Script(PlannerType.Construction, (g, n) => n == 0 ? Reserve(g) : null) : null);
        var human = game.Controllers[Owner.Alliance];
        game.Clock.Step(game); // tick T: tokens only; the AI decides its SetReserved
        Assert.Equal([(CommandType.TickToken, 1u), (CommandType.TickToken, 2000000001u)], trace.Select(c => (c.Type, c.Sequence)));
        human.Append(new Command(CommandType.SetReserved, Owner.Alliance) { Targets = [(game.Systems.Where(s => s.Owner == Owner.Alliance).SelectMany(s => s.Managers).First(m => m.Completed && Economy.Members(m).Any(f => !f.Suspended)), 0)] });
        trace.Clear();
        game.Clock.Step(game); // tick T + 1
        Assert.Equal([(CommandType.SetReserved, Owner.Alliance, 2u), (CommandType.TickToken, Owner.Alliance, 3u), (CommandType.SetReserved, Owner.Empire, 2000000002u), (CommandType.TickToken, Owner.Empire, 2000000003u)],
            trace.Select(c => (c.Type, c.Side, c.Sequence)));
        Assert.All(trace.Where(c => c.Type == CommandType.SetReserved), c => Assert.NotEqual(0u, c.Serial)); // executed (serial taken)
    }

    // At most 10 counted commands per budget period (one day); Rename not counted; reset after the DayDue tick's think
    [GameFact]
    public void BudgetPerDay()
    {
        var (game, ai, trace) = Game(t => t == PlannerType.Construction ? new Script(PlannerType.Construction, (g, n) => n % 2 == 0 ? Reserve(g)
            : new Command(CommandType.Rename, Owner.Empire) { Targets = [(g.Characters.First(c => c.Owner == Owner.Empire && c.Placed), 0)] }, finish: false) : null);
        game.Sides[Owner.Alliance].Speed = Clock.TicksForSpeed(4);
        var decided = new List<(int Day, int Counted, int Renames)>();
        for (int i = 0; i < 12; i++)
        {
            game.Clock.Step(game);
            var issued = ai.Outbox.Where(c => c.Type is CommandType.SetReserved or CommandType.Rename).ToList(); // the outbox holds this tick's decisions (and its token-free acks)
            decided.Add((game.Day, issued.Count(c => c.Type == CommandType.SetReserved), issued.Count(c => c.Type == CommandType.Rename)));
            Assert.Equal(game.Clock.DayDue ? 0 : ai.Issued, ai.Issued); // reset on the DayDue tick
        }
        foreach (var g in decided.GroupBy(d => d.Day).Where(g => g.Count() == 4))
            Assert.Equal(10, g.Sum(d => d.Counted));
        Assert.Equal((10, 9), (decided[0].Counted, decided[0].Renames)); // the first cycle spends the day's budget; renames do not count
    }

    // The AcknowledgeDestroyed for an object destroyed in tick T + 1 is appended at delivery, before that tick's planner command,
    // so it runs first in T + 2; not counted
    [GameFact]
    public void AcknowledgementBeforePlannerCommand()
    {
        GameObject? scrapped = null;
        var (game, ai, trace) = Game(t => t == PlannerType.Construction ? new Script(PlannerType.Construction, (g, n) => n switch
        {
            0 => new Command(CommandType.Scrap, Owner.Empire) { Targets = [(scrapped = g.Created.First(o => o.Owner == Owner.Empire && Family.Of(o.Type) == Families.Mine && o.Usable), 0)] },
            1 => Reserve(g),
            _ => null,
        }) : null);
        game.Clock.Step(game); // T: decide the scrap
        game.Clock.Step(game); // T + 1: the scrap runs; delivery acknowledges, think decides the SetReserved
        Assert.True(scrapped!.Destroyed);
        trace.Clear();
        game.Clock.Step(game); // T + 2
        var empire = trace.Where(c => c.Side == Owner.Empire).ToList();
        Assert.Equal([CommandType.AcknowledgeDestroyed, CommandType.SetReserved, CommandType.TickToken], empire.Select(c => c.Type));
        Assert.Same(scrapped, empire[0].Targets[0].Target);
        Assert.True(empire[0].Sequence < empire[1].Sequence);
        Assert.DoesNotContain(trace, c => c.Type == CommandType.AcknowledgeDestroyed && c.Side == Owner.Alliance); // the human acknowledges only its own objects
    }

    // Lazy seed; rand(n <= 1) neither seeds nor steps; the first draw steps from the seed itself (run 1: s = 553016217)
    [Fact]
    public void UiGeneratorFirstDraw()
    {
        var ui = new UiRandom(() => 553016217);
        Assert.Equal(0, ui.Rand(1));
        Assert.Equal(-1, ui.State);
        ui.Rand(10);
        Assert.Equal(943862136, ui.State);
        Assert.Equal(41358L * 553016217 % int.MaxValue, ui.State);
    }

    // Records of a new game (Empire model): every sector with its 10 systems, own-system count, Coruscant record
    // with garrison target K384 = 9, enemy major known through its view (allegiance flag), own Vader (victory
    // character, major, own); notifications dirty the system and its sector; the update clears them
    [GameFact]
    public void WorldModelOfNewGame()
    {
        var (game, ai, _) = Game(_ => null);
        game.Clock.Step(game);
        var m = ai.Model;
        Assert.False(m.Dirty);
        Assert.Equal(game.Sectors.Count, m.Sectors.Count);
        Assert.All(m.Sectors, s => Assert.Equal(10, s.Systems.Count));
        Assert.Equal(10 * game.Sectors.Count, m.SystemSlots);
        Assert.Equal(game.Systems.Count(s => Knowledge.See(game, s, Owner.Empire).Owner == Owner.Empire), m.OwnSystems);
        var coruscant = m.Systems[game.Known.Coruscant!];
        Assert.True(coruscant.KeySystem && coruscant.Coruscant && coruscant.Own);
        Assert.True(coruscant.GarrisonTarget >= 9);
        Assert.Equal(coruscant.Troops, game.Known.Coruscant!.Contents.Count(x => Family.IsTroop(x.Type) && x.Existing));
        var luke = m.Agents[Refs.Luke];
        Assert.True(luke.Enemy && luke.VictoryCharacter && luke.Major);
        var vader = m.Agents[Refs.Vader];
        Assert.True(vader.Own && vader.VictoryCharacter && vader.Character && !vader.Enemy);
        var types = MissionEligibility.TypesForTeam(game, Owner.Empire, [game.Known.Vader!], []);
        Assert.NotEmpty(types);
        Assert.Equal(types.Any(t => t.Common.Ref == Refs.DiplomacyMission), vader.Can.Diplomacy); // capability probe
        Assert.Equal(types.Any(t => t.Common.Ref == Refs.AssassinationMission), vader.Can.Assassination);
        Assert.All(game.Characters.Where(c => c.Placed), c => Assert.True(m.Agents.ContainsKey(c.Ref))); // every view gets a record

        var mine = game.Created.First(o => o.Owner == Owner.Empire && Family.Of(o.Type) == Families.Mine);
        var record = m.Systems[mine.System!];
        m.Changed(game, mine);
        Assert.True(record.Dirty && record.Sector!.Dirty && m.Dirty);
        m.Update(game);
        Assert.False(record.Dirty || record.Sector!.Dirty || m.Dirty);
    }

    // validation without execution: no serial, no events; composites validate each sub-command
    [GameFact]
    public void ValidateDoesNotExecute()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        var to = game.Systems.First(s => s.Owner == Owner.Empire && s != fleet.System);
        uint serial = game.Clock.Serial;
        Assert.True(Orders.Validate(game, new Command(CommandType.Move, Owner.Empire) { Targets = [(fleet, GroupMode.Object)], ArgRef = to }).Success);
        Assert.Equal(Refusal.NotOwner, Orders.Validate(game, new Command(CommandType.Move, Owner.Alliance) { Targets = [(fleet, GroupMode.Object)], ArgRef = to }));
        Assert.Equal(serial, game.Clock.Serial);
        Assert.Same(fleet.System, fleet.Container);
        Assert.False(fleet.Enroute);
    }

    // controllers and their outbox survive a save; the UI generator state is written, not restored
    [GameFact]
    public void ControllersSaved()
    {
        var (game, _, _) = Game(t => t == PlannerType.Construction ? new Script(PlannerType.Construction, (g, n) => n == 0 ? Reserve(g) : null) : null);
        game.Clock.Step(game);
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        stream.Position = 0;
        var loaded = SaveGame.Load(stream, Definitions);
        Assert.IsType<HumanController>(loaded.Controllers[Owner.Alliance]);
        var ai = Assert.IsType<ComputerController>(loaded.Controllers[Owner.Empire]);
        Assert.Equal(game.Controllers[Owner.Empire].NextSequence, ai.NextSequence);
        Assert.Equal([(CommandType.SetReserved, 2000000002u)], ai.Outbox.Select(c => (c.Type, c.Sequence)));
        Assert.Equal(((ComputerController)game.Controllers[Owner.Empire]).Issued, ai.Issued);
    }

    // insertion in candidate order, one rand(10) per equal comparison (< 5 gives before), ranks in list order; the
    // pick sums ranks over the pushed lists, a key missing from one is dropped, ties keep the earliest of the first list
    [Fact]
    public void RankedListsAndPick()
    {
        var flips = new Queue<int>([7, 2]);
        int draws = 0;
        int Rand(int n) { Assert.Equal(10, n); draws++; return flips.Dequeue(); }
        var a = new RankedList([("x", 5), ("y", 3), ("z", 5)], RankOrder.Ascending, Rand); // z vs y: no draw; z vs x: equal gives 7 >= 5 gives after x
        Assert.Equal(["y", "x", "z"], a.Entries.Select(e => (string)e.Key));
        Assert.Equal(1, draws);
        var b = new RankedList([("x", 1), ("z", 9), ("w", 4)], RankOrder.Descending, Rand); // descending: z, w, x
        Assert.Equal(["z", "w", "x"], b.Entries.Select(e => (string)e.Key));
        var stack = new ListStack();
        stack.Push(a);
        stack.Push(b);
        Assert.Equal("x", stack.Pick()); // y missing from b gives dropped; x 2 + 3 = 5 beats z 3 + 1 = 4
        Assert.Equal("z", stack.Pick());
        Assert.Null(stack.Pick());
    }

    // no requests: the first field takes the second, which shifts right; with requests the turn moves right (wrapping) to the next
    // requesting bit and consumes it
    [Fact]
    public void ProducerFacilityTurn()
    {
        var m = new WorldModel(Owner.Empire) { Turn = 0, NextTurn = 1 };
        m.AdvanceTurn();
        Assert.Equal((1, 2), (m.Turn, m.NextTurn));
        m.TurnRequests[0] = m.TurnRequests[4] = true;
        m.AdvanceTurn();
        Assert.Equal((4, true, 1), (m.Turn, m.TurnRequests[0], m.TurnRequests.Count(x => x)));
        m.AdvanceTurn();
        Assert.Equal((0, 0), (m.Turn, m.TurnRequests.Count(x => x))); // wraps from the last slot to the first
    }

    // catalogue records: production facilities (class mask by family, ProcessingRate), special-force classes,
    // capital-ship size classes, MaintenanceCost
    [GameFact]
    public void ProductCatalogue()
    {
        var c = new Catalogue(Definitions);
        var yard = c[SdRef.Make(Families.ConstructionYard, 3)]!;
        Assert.True(yard.ConstructionYard && !yard.Shipyard && !yard.TrainingFacility);
        Assert.Equal((int)Definitions.SdFiles.SelectMany(f => f.Records).First(r => r.Common.Ref == SdRef.Make(Families.ConstructionYard, 3)).U32At(36), yard.MaintenanceCost);
        Assert.Equal(yard.ProcessingRate, yard.Value(Criterion.ProcessingRate));
        Assert.Equal(1, c[Refs.ProbeDroids]!.SpecialForceKind);
        Assert.Equal(4, c[Refs.BothanSpies]!.SpecialForceKind);
        foreach (var ship in Definitions.CapitalShips.Where(s => Family.Of(s.Common.Ref) < Families.DeathStar))
        {
            var p = c[ship.Common.Ref]!;
            Assert.Equal((p.Hull < 1100, p.Hull is >= 1100 and < 2000, p.Hull >= 2000), (p.SmallHull, p.MediumHull, p.LargeHull));
        }
        Assert.Null(c[Refs.Mine]); // Mines have no record
    }

    // planner 8 binds a fleet op to a theater (sector key) with the task of its kind; a task
    // base is an own system claimed with C 262144 (types 1-3 drop 196608)
    /// <summary>Game start runs side 1's controller, then side 2's (the human's advisor lists too), all n = 10, before the first tick.</summary>
    [GameFact]
    public void GameStartDrawsInSideOrder()
    {
        foreach (var human in new[] { Owner.Alliance, Owner.Empire })
        {
            var game = NewGame.Create(new GameSettings(human, 1, 0, 0, 874334749), Definitions);
            var ui = new UiRandom(() => 12345);
            var draws = new List<(Owner Side, int N)>();
            var computer = human == Owner.Alliance ? Owner.Empire : Owner.Alliance;
            game.Controllers[human] = new HumanController(human) { Rand = n => { draws.Add((human, n)); return ui.Rand(n); } };
            game.Controllers[computer] = new ComputerController(computer) { Rand = n => { draws.Add((computer, n)); return ui.Rand(n); } };
            int tick = game.Clock.Tick;
            Controller.StartGame(game);
            Assert.Equal(tick, game.Clock.Tick);
            Assert.All(draws, d => Assert.Equal(10, d.N));
            Assert.Contains(draws, d => d.Side == Owner.Empire);
            Assert.Equal(draws.OrderBy(d => d.Side).ToList(), draws); // side 1 first
            int n = draws.Count;
            Controller.StartGame(game); // once only
            Assert.Equal(n, draws.Count);
        }
    }

    [GameFact]
    public void FleetOperationBindsTheaterAndBase()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var ai = new ComputerController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        game.Controllers[Owner.Empire] = ai;
        game.Clock.RunUntilDay(game, 30);
        var m = ai.Model;
        var op = Assert.Single(m.FleetOps.Values); // created only while every op has the 67108864 field
        Assert.NotNull(op.Theater);
        Assert.Equal(op.Key, op.Theater!.FleetOpKey);
        Assert.Equal(op.Theater.TheaterKind, op.TaskKind);
        // types 1-3 clear C 196608; types 4-6 mark 327680 / 393216 (type 5)
        if (m.Record(op.Base) is { } b)
            Assert.True(b.Own && b.Base && (op.Task!.Type switch { FleetTaskType.Strike or FleetTaskType.Scout or FleetTaskType.Reserve => !(b.AssaultBase || b.DefenseBase), FleetTaskType.Defence => b.DefenseBase, _ => b.AssaultBase }));
        // the task targets make shortfalls: the op takes own ships, each record keyed to the op
        Assert.NotEmpty(op.Ships);
        Assert.All(op.Ships, s => Assert.True(s.Own && s.OpKey == op.Key));
        Assert.All(op.Fleets, f => Assert.Equal(op.Key, f.OpKey));
    }

    /// <summary>Fleet-op requests create object classes; attach counts and sets the three count fields; retire detaches.</summary>
    [Fact]
    public void ObjectOpsAttachToTheirFleetOp()
    {
        Assert.Same(OpClass.Espionage, OpClass.Of(MissionKind.Espionage, 0));
        Assert.False(OpClass.Of(MissionKind.Espionage, 6)!.Object);
        Assert.Same(OpClass.Abduction, OpClass.Of(MissionKind.Abduction, 4));
        Assert.False(OpClass.Of(MissionKind.Abduction, 0)!.Object);
        var m = new WorldModel(Owner.Empire);
        var fo = new FleetOp(1) { Entry = 7 };
        fo.RequestKind = 105;
        var op = MissionOps.Create(m, MissionKind.Sabotage, 0)!;
        Assert.Equal(0, m.SystemOps(op.Class.Index)); // object ops are not counted with the system ops
        MissionOps.Attach(m, fo, op);
        Assert.Equal((0, 1, 7), (fo.RequestKind, fo.ObjectOps(op.Class.Index), op.Priority));
        Assert.Same(fo, op.FleetOp);
        MissionOps.Retire(m, op);
        Assert.Equal(0, fo.ObjectOps(op.Class.Index));
        Assert.Empty(fo.MissionOps);
    }

    /// <summary>Setup step 23 stores the used energy / raw counts; the AI system record reads them.</summary>
    [GameFact]
    public void UsedCountsAreStoredAtSetup()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        foreach (var s in game.Systems)
        {
            Assert.Equal(Math.Min(s.Contents.Count(x => Family.IsFacility(x.Type) && x.Existing && x.Owner == s.Owner), s.Energy), s.UsedEnergy);
            Assert.Equal(Math.Min(s.Contents.Count(x => Family.Of(x.Type) == Families.Mine && x.Existing && x.Owner == s.Owner), s.RawMaterials), s.UsedRaw);
        }
        Assert.Contains(game.Systems, s => s.UsedEnergy > 0);
    }
    // The op holds its character as a key: once the record is detached, planner 12 state 4 finds the
    // op selectable whatever the record's last flags, and the team keep sees no record (Assigned cleared, the pick runs)
    [GameFact]
    public void DetachedCharacterOpIsSelectable()
    {
        var (game, ai, _) = Game(_ => null);
        game.Clock.Step(game);
        var m = ai.Model;
        var general = m.Agents.Values.First(a => a.Agent.Owner == Owner.Empire && Family.Of(a.Agent.Type) is >= Families.MajorCharacter and < Families.SpecialForce);
        var op = new CharacterOp(1, CharacterOpType.GeneralAtSystem) { Character = general, Assigned = true };
        m.CharacterOps[op.Key] = op;
        general.CharacterOpKey = op.Key;
        general.OnMission = true; // its Move command mission
        Assert.False(CharacterOpPlanner.Selectable(m, op));
        m.Detach(general.Agent); // the general dies on the mission
        Assert.True(CharacterOpPlanner.Selectable(m, op));
        Assert.False(CharacterOpPlanner.KeepCheck(m, op));
        Assert.False(op.Assigned);
    }
    /// <summary>Three member fleets in key order, each with a bombard claim at its Death Star target (BombardDestroy every visit).</summary>
    static (WorldModel M, FleetOp Op, FleetTask Task, List<AiFleet> Fleets) StrikeSetup()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var m = new WorldModel(Owner.Empire);
        var sys = game.Systems[0];
        m.Systems[sys] = new AiSystem(sys, m) { VictoryCharacters = 1 };
        var op = new FleetOp(1);
        var task = new FleetTask(FleetTaskType.Strike) { State = (int)FleetTaskState.Strike };
        var fleets = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).OrderBy(f => f.Ref).Take(3)
            .Select(f => new AiFleet(f) { Location = sys, Target = sys }).ToList();
        foreach (var f in fleets)
        {
            f.BombardClaim = f.DeathStar = true;
            m.FleetsById[f.Fleet.Ref] = f;
            op.Fleets.Add(f);
        }
        return (m, op, task, fleets);
    }

    static Fleet? StrikeFleet(Command? c) => c?.Targets[0].Item1 as Fleet;

    // The strike cursor is a fleet key: each command moves it to the successor; the last fleet's command comes
    // back done in the same call (cursor none)
    [GameFact]
    public void StrikeLastCommandComesDone()
    {
        var (m, op, task, f) = StrikeSetup();
        var (c, done) = FleetOps.Strike(m, op, task, Owner.Empire);
        Assert.Equal((f[0].Fleet, false, f[1].Fleet.Ref), (StrikeFleet(c), done, task.Strike));
        (c, done) = FleetOps.Strike(m, op, task, Owner.Empire);
        Assert.Equal((f[1].Fleet, false, f[2].Fleet.Ref), (StrikeFleet(c), done, task.Strike));
        (c, done) = FleetOps.Strike(m, op, task, Owner.Empire);
        Assert.Equal((f[2].Fleet, true, (uint?)null), (StrikeFleet(c), done, task.Strike));
    }

    // The cursor's fleet leaves between two calls gives the next call restarts at the lowest key
    [GameFact]
    public void StrikeRestartsWhenTheCursorFleetLeaves()
    {
        var (m, op, task, f) = StrikeSetup();
        FleetOps.Strike(m, op, task, Owner.Empire);
        op.Fleets.Remove(f[1]);
        var (c, done) = FleetOps.Strike(m, op, task, Owner.Empire);
        Assert.Equal((f[0].Fleet, false, f[2].Fleet.Ref), (StrikeFleet(c), done, task.Strike));
    }

    // A fleet below the cursor leaves gives no fleet is skipped
    [GameFact]
    public void StrikeSkipsNoFleetWhenAnEarlierOneLeaves()
    {
        var (m, op, task, f) = StrikeSetup();
        FleetOps.Strike(m, op, task, Owner.Empire);
        FleetOps.Strike(m, op, task, Owner.Empire);
        op.Fleets.Remove(f[0]);
        var (c, done) = FleetOps.Strike(m, op, task, Owner.Empire);
        Assert.Equal((f[2].Fleet, true), (StrikeFleet(c), done));
    }
}
