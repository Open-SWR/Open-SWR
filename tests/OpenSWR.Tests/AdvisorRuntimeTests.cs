using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>The advisor runtime with a scripted clock and random source (never the shared generator).</summary>
public class AdvisorRuntimeTests
{
    sealed class Rig
    {
        public int Now = 100;
        public readonly Queue<int> Draws = new();
        public readonly List<int> Rolls = [], Played = [];
        public readonly Advisor A;
        public Rig(Owner side = Owner.Alliance, bool? briefing = null, bool tips = false)
        {
            A = new Advisor(side, () => Now, briefing, tips, n => { Rolls.Add(n); return Draws.Count > 0 ? Draws.Dequeue() : 0; });
            A.Play = Played.Add;
        }
        /// <summary>One tick with the gate open (as after an idle completion).</summary>
        public void Open() { A.GateOpen(); A.Tick(); }
    }

    // Lowest id wins, one action per tick, and a second pending request plays on the next tick.
    [Fact]
    public void LowestIdWinsOnePerTick()
    {
        var r = new Rig();
        r.A.MessageCode(5); // 59
        r.A.MessageCode(1); // 52
        r.Open();
        Assert.Equal([52], r.Played);
        r.A.Tick(); // gate still open after playing
        Assert.Equal([52, 59], r.Played);
    }

    // Expired requests are cleared, and a request on cooldown is lost.
    [Fact]
    public void ExpiryAndCooldown()
    {
        var r = new Rig();
        r.A.MessageCode(1); // validity 10
        r.Now += 11;
        r.Open();
        Assert.Empty(r.Played);
        Assert.False(r.A.Pending(52));
        r.A.MessageCode(1);
        r.Open();
        r.A.MessageCode(1); // within 60 ticks: dropped
        r.A.MessageCode(2);
        r.A.Tick();
        Assert.Equal([52, 53], r.Played);
        Assert.False(r.A.Pending(52));
        r.Now += 61;
        r.A.MessageCode(1);
        r.Open();
        Assert.Equal([52, 53, 52], r.Played);
    }

    // The bit step closes the gate; a closed gate plays nothing until the next idle completion.
    [Fact]
    public void GateClosesWhenNothingPending()
    {
        var r = new Rig();
        r.Now = 10; // not > last play + 50: no idle bit
        r.Open();
        Assert.False(r.A.Gate);
        r.A.MessageCode(1);
        r.A.Tick();
        Assert.Empty(r.Played);
        r.Open();
        Assert.Equal([52], r.Played);
    }

    // Idle chatter: after more than 50 ticks since the last action, rand(12); r < 6 requests 80 + r (IMP 78 + r).
    [Fact]
    public void IdleChatter()
    {
        var r = new Rig();
        r.Draws.Enqueue(3);
        r.Open(); // last play 0, now 100: idle bit -> rule step draws
        Assert.Equal([12], r.Rolls);
        Assert.True(r.A.Gate);
        r.A.Tick();
        Assert.Equal([83], r.Played);
        r.Draws.Enqueue(7);
        r.Now += 51;
        r.Open();
        Assert.Empty(r.Played.Skip(1));
        var imp = new Rig(Owner.Empire);
        imp.Draws.Enqueue(5);
        imp.Open();
        imp.A.Tick();
        Assert.Equal([83], imp.Played); // 78 + 5
    }

    // Refusal mapping and the repeat memory 15 / 16 (IMP 16 / 17).
    [Fact]
    public void Refusals()
    {
        var r = new Rig();
        void Expect(int category, int code, int id)
        {
            r.A.Refused(category, code, null);
            Assert.True(r.A.Pending(id), $"({category:X}, {code:X}) → {id}");
            r.Now += 100; // let it expire
        }
        Expect(0x90, 1, 11);
        Expect(0x90, 1, 15);
        Expect(0x90, 1, 16);
        Expect(0x90, 1, 11); // forgotten: dispatched again
        Expect(0xA0, 3, 24);
        Expect(1, 0x11, 1);
        Expect(0xF3, 1, 10);
        r.Draws.Enqueue(1);
        Expect(1, 0x17, 18); // generic rand(2) = 1
        int pending = Enumerable.Range(1, 87).Count(r.A.Pending);
        r.A.Refused(1, 0x12, null);
        Assert.Equal(pending, Enumerable.Range(1, 87).Count(r.A.Pending));

        var imp = new Rig(Owner.Empire);
        imp.A.Refused(0x90, 3, null);
        imp.A.Refused(0x90, 3, null);
        Assert.True(imp.A.Pending(11) && imp.A.Pending(16));

        // Exact categories (0x91, 1), (0xA1, 3) and (0x15, 1) take the generic line.
        foreach (var (category, code) in new[] { (0x91, 1), (0xA1, 3), (0x15, 1) })
        {
            r.Draws.Enqueue(0);
            Expect(category, code, 17);
        }
        // (0x40, 0x91): a mission with decoys and no agents -> 20; without decoys -> generic
        var at = new GameObject(0x90000109, Owner.Neutral, 0);
        var decoy = new GameObject(0x30000240, Owner.Alliance, 0);
        Expect(0x40, 0x91, 17); // queue empty: rand(2) = 0
        r.A.Refused(1, 1, null); // reset the stored pair
        r.Now += 100;
        Expect(0x40, 0x91, 17);
        r.A.Refused(1, 1, null);
        r.A.Refused(0x40, 0x91, new Command(0x240, Owner.Alliance) { ArgRef = at, Decoys = [decoy] });
        Assert.True(r.A.Pending(20));
        var e = new Rig(Owner.Empire);
        e.A.Refused(0x40, 0x91, new Command(0x240, Owner.Empire) { ArgRef = at, Decoys = [decoy] });
        Assert.True(e.A.Pending(21));
        // (1, 0x22) without a destroyed system view: nothing, no draw
        int rolls = e.Rolls.Count;
        e.A.Refused(1, 0x22, new Command(0x201, Owner.Empire) { ArgRef = at });
        Assert.Equal(rolls, e.Rolls.Count);
        Assert.Equal(1, Enumerable.Range(1, 85).Count(e.A.Pending));
        // (1, 0x25) / (1, 0x27): a fleet ArgRef -> 7, else C 23 / I 24
        e.A.Refused(1, 0x25, new Command(0x201, Owner.Empire) { ArgRef = new Fleet(0x08000004, Owner.Empire, 0) });
        Assert.True(e.A.Pending(7));
        e.A.Refused(1, 0x27, null);
        Assert.True(e.A.Pending(24));
    }

    // Capital-ship refusals read the advisor side's view of the ArgRef: capacity != 0 -> 8, 0 -> 7, fleet -> 9.
    // (validity 5); (1, 0x22) -> 6 only on a Destroyed system view; (1, 0x24) -> 2 for a system ArgRef
    [GameFact]
    public void RefusalViews()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path));
        var ships = game.Definitions.CapitalShips;
        var carrier = new GameObject(ships.First(x => x.FighterCapacity != 0).Common.Ref, Owner.Alliance, 0);
        var empty = new GameObject(ships.First(x => x.FighterCapacity == 0).Common.Ref, Owner.Alliance, 0);
        var r = new Rig();
        void Expect(int category, int code, GameObject? arg, int id, int validity)
        {
            r.A.Refused(1, 1, null); // another pair first: no repeat
            r.A.Refused(category, code, new Command(0x204, Owner.Alliance) { ArgRef = arg }, game);
            Assert.Equal(r.Now + validity, r.A.Due(id));
            r.Now += 100;
        }
        Expect(0x14, 1, carrier, 8, 5);
        Expect(0x14, 1, empty, 7, 5);
        Expect(0x14, 1, new Fleet(0x08000004, Owner.Alliance, 0), 9, 5);
        Expect(0x14, 2, ships.FirstOrDefault(x => x.TroopCapacity == 0) is { } t ? new GameObject(t.Common.Ref, Owner.Alliance, 0) : empty,
            ships.Any(x => x.TroopCapacity == 0) ? 7 : 8, 5);
        var system = game.Systems.First(s => s.Owner == Owner.Alliance);
        Expect(1, 0x24, system, 2, 10);
        system.Destroyed = true; // own system: live view, real value
        Expect(1, 0x22, system, 6, 10);
    }

    // An accepted order clears the refusal memory and the menu counter.
    [Fact]
    public void AcceptClearsMemory()
    {
        var r = new Rig();
        r.A.Refused(0x90, 2, null);
        r.A.Accepted(new Command(0x201, Owner.Alliance));
        r.A.Refused(0x90, 2, null);
        Assert.False(r.A.Pending(15));
        Assert.True(r.A.Pending(12));
    }

    // Menu counter: 76, 76, 77, 78, then 78 with the counter reset.
    [Fact]
    public void MenuClicks()
    {
        var r = new Rig();
        r.Now = 10;
        for (int i = 0; i < 20; i++) r.Draws.Enqueue(11); // idle chatter draws: no request
        var got = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            r.A.MenuOpen();
            r.A.Tick(); // rule step with the gate closed: raises the request
            got.Add(Enumerable.Range(76, 3).Single(r.A.Pending));
            r.Now += 100;
            r.A.GateOpen();
            r.A.Tick(); // expired: cleared
        }
        Assert.Equal([76, 76, 77, 78, 78, 76], got);
        r.A.MenuOpen();
        r.A.Accepted(new Command(0x201, Owner.Alliance));
        r.A.Tick();
        Assert.True(r.A.Pending(76));
    }

    // The tip reaction draws only when none of 69-72 is pending.
    [Fact]
    public void TipSlotGuard()
    {
        var r = new Rig();
        r.Draws.Enqueue(2);
        for (int i = 0; i < 9; i++) r.A.MessageCode(0x2A);
        Assert.Equal([4], r.Rolls);
        Assert.True(r.A.Pending(71));
    }

    // The 0x115 / 0x116 toggles: record state, counter, reaction bits; 0x11B / 0x11E option bits.
    [Fact]
    public void Toggles()
    {
        var r = new Rig();
        Assert.Equal((true, false), r.A.Row(0x115));
        r.A.Toggle(0x115);
        Assert.Equal((true, true), r.A.Row(0x115));
        r.A.Tick();
        Assert.True(r.A.Pending(36));
        r.A.Toggle(0x115);
        r.A.Toggle(0x116);
        r.A.Tick(); // 0x20 before 0x100
        Assert.True(r.A.Pending(38));
        r.A.Tick();
        Assert.True(r.A.Pending(37));
        Assert.Equal(1, r.A.ToggleCounter);
        Assert.True(r.A.Row(0x11B).Checked);
        r.A.Toggle(0x11B);
        Assert.False(r.A.Plays(commentary: true));
        Assert.True(r.A.Plays(commentary: false));
        Assert.False(r.A.Row(0x11E).Checked);
        r.A.Toggle(0x11E);
        Assert.True(r.A.Row(0x11E).Checked);
    }

    // Comment steps are skipped while the message window is open.
    [Fact]
    public void MessageWindowMutesComments()
    {
        var r = new Rig();
        r.A.MessageWindow(true);
        Assert.False(r.A.Plays(commentary: true));
        r.A.MessageWindow(false);
        Assert.True(r.A.Plays(commentary: true));
    }

    // Post commands run on the next tick and end it; the briefing request / fallback / skip.
    [Fact]
    public void PostCommandsAndBriefing()
    {
        var posts = new List<(int, int)>();
        var r = new Rig(briefing: true);
        r.A.Post = (a, b) => posts.Add((a, b));
        r.Open();
        Assert.Equal([41], r.Played);
        r.A.Posted(2, 0xC);
        r.A.Tick();
        Assert.Equal([(2, 0xC)], posts);
        bool flushed = false;
        r.A.Flush = () => flushed = true;
        r.A.Skip();
        r.A.Tick(); // nothing pending: rule step
        Assert.True(flushed && r.A.Pending(40));

        var none = new Rig(briefing: false);
        none.A.Post = (a, b) => posts.Add((a, b));
        none.Open();
        none.A.Tick();
        Assert.Equal((2, 0xD), posts[^1]);
        Assert.DoesNotContain(41, none.Played);
        var loaded = new Rig();
        Assert.False(loaded.A.Pending(41));
    }

    // Music: first pick rand(3) (mood 8), then three rand(6) div 2 + 1, then rand(3) again.
    [Fact]
    public void Music()
    {
        var r = new Rig();
        foreach (int d in new[] { 2, 5, 0, 3, 1 }) r.Draws.Enqueue(d);
        var tracks = Enumerable.Range(0, 5).Select(_ => r.A.MusicTrack()).ToList();
        Assert.Equal([3, 6, 6, 6, 3], r.Rolls);
        Assert.Equal([303, 303, 301, 302, 302], tracks);
        // The out-of-range results rand(6) = 6 and rand(3) = 3 play track 4 (MDATA.304).
        r.Draws.Enqueue(6);
        Assert.Equal(304, r.A.MusicTrack());
        r.Draws.Enqueue(6);
        r.Draws.Enqueue(6);
        r.A.MusicTrack();
        r.A.MusicTrack();
        r.Draws.Enqueue(3);
        Assert.Equal(304, r.A.MusicTrack());
    }

    // Voice lists: n = 0 none, n = 1 no draw, rand(n) = n silent (draw consumed); Dagobah by SD index.
    [GameFact]
    public void Voices()
    {
        var rolls = new List<int>();
        int draw = 0;
        int Rand(int n) { rolls.Add(n); return draw; }
        Assert.Equal(0, Advisor.Voice([0], Rand));
        Assert.Equal(11, Advisor.Voice([1, 11], Rand));
        Assert.Empty(rolls);
        draw = 1;
        Assert.Equal(22, Advisor.Voice([3, 11, 22, 33, 2], Rand));
        draw = 3;
        Assert.Equal(0, Advisor.Voice([3, 11, 22, 33, 2], Rand));
        Assert.Equal([3, 3], rolls);
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path));
        var luke = game.Characters.First(c => c.Type == Refs.Luke);
        Command To(uint system) => new(0x201, Owner.Alliance) { Targets = [(luke, 0)], ArgRef = new GameObject(system, Owner.Neutral, 0) };
        Assert.True(Messages.DagobahFirst(To(0x920000F9)));
        Assert.True(Messages.DagobahFirst(To(0x900000F9)));
        Assert.False(Messages.DagobahFirst(To(0x920000F8)));
    }

    // Kind-7 burst at build (largest key first, so ids ascend in descending key order), each topic
    // once, triggers unlock their kind and post its first topic, the 300-tick timer posts the first available topic
    [GameFact]
    public void AdviceTopics()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path));
        var r = new Rig(tips: true) { Now = 1 };
        r.A.TopicSource = () => [new(1, 7, 20), new(2, 7, 10), new(3, 1, 140), new(4, 3, 120), new(5, 2, 110), new(6, 1, 150)];
        int Title(GameMessage m) => (m.Title!.Template - 0x6001) / 3;
        var burst = r.A.BuildTopics(game);
        Assert.Equal([1, 2], burst.Select(Title));
        Assert.True(burst[0].Id < burst[1].Id && burst.All(m => (m.Type, m.Kind, m.Category, m.AdvisorCode, m.Picture) == (0x2A, 8, MessageCategory.Advice, 0x2A, 1071)));
        Assert.Empty(r.A.BuildTopics(game));
        Assert.Empty(r.A.TipTimer(game)); // 1 < 0 + 300
        r.Now = 300;
        Assert.Equal([3], r.A.TipTimer(game).Select(Title)); // kinds 2 / 3 locked
        Assert.Equal([4], r.A.TipTrigger(game, AdvisorTip.Fleet).Select(Title));
        Assert.Empty(r.A.TipTrigger(game, AdvisorTip.Fleet));
        r.Now = 599;
        Assert.Empty(r.A.TipTimer(game));
        r.Now = 600;
        Assert.Equal([6], r.A.TipTimer(game).Select(Title));
        r.A.Toggle(0x11E);
        r.Now = 2000;
        Assert.Empty(r.A.TipTimer(game));
        Assert.Equal([5], r.A.Topics.Select(t => t.N));
    }

    // Build helper: kind by row, target rules, the producer search (nearest sector, then system, with an
    // idle producer of the kind), the watched Build command -> HelperDone -> action 35 on its confirmation; the helper resets after
    // every pick; no idle producer -> (0xA0, 3) -> 24
    [GameFact]
    public void BuildHelper()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path));
        var r = new Rig();
        var manager = game.Systems.Where(s => s.Owner == Owner.Alliance && s.Populated && s.Explored).SelectMany(s => s.Managers)
            .First(m => m is not null && Economy.Members(m).Count > 0 && m.Orders.Count == 0);
        var own = manager.System;
        var (row, type) = manager.Kind switch { ManufacturingKind.Ship => (0x110, 0x211), ManufacturingKind.Training => (0x111, 0x212), _ => (0x112, 0x210) };
        r.A.HelperStart(row);
        Assert.NotEqual(0, r.A.HelperKind);
        var (destination, build) = r.A.HelperPick(game, own)!.Value;
        Assert.Equal(0, r.A.HelperKind);
        Assert.Equal((0x214, (GameObject)manager, (GameObject?)own), (destination.Type, destination.Targets[0].Target, destination.ArgRef));
        Assert.Equal(type, build.Type);
        Assert.True(Orders.Validate(game, destination).Success);
        r.A.Watch(build);
        r.A.Accepted(new Command(type, Owner.Alliance) { Targets = build.Targets, Fields = [1, 1] });
        r.A.Tick();
        Assert.True(r.A.Pending(35));

        r.A.HelperStart(0x110);
        Assert.Null(r.A.HelperPick(game, game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed)));
        Assert.True(r.A.Pending(23)); // (1, 0x27) without a fleet ArgRef -> C 23
        var enemy = game.Systems.First(s => s.Owner == Owner.Empire);
        r.A.HelperStart(0x111);
        Assert.Null(r.A.HelperPick(game, enemy)); // troops to a system the player does not own: (1, 0x24) -> 2
        Assert.True(r.A.Pending(2));
        r.A.HelperStart(0x111);
        Assert.Null(r.A.HelperPick(game, null));
        Assert.Equal(0, r.A.HelperKind);
        foreach (var s in game.Systems.Where(s => s.Owner == Owner.Alliance)) s.Uprising = true; // no idle producer anywhere
        r.Now += 100;
        r.A.HelperStart(0x110);
        Assert.Null(r.A.HelperPick(game, own));
        Assert.True(r.A.Pending(24));
    }

    // The advisor rides in the human controller's save entry: due[], flags, counters, topics, records;
    // save -> load -> save byte-equal; a save without one loads with none (the screen then builds one without a briefing)
    [GameFact]
    public void SaveRoundTrip()
    {
        var definitions = OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path);
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), definitions);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Empire);
        game.Controllers[Owner.Empire] = human;
        game.Controllers[Owner.Alliance] = new OpenSWR.Sim.Ai.ComputerController(Owner.Alliance);
        static byte[] Save(GameState g) { using var m = new MemoryStream(); SaveGame.Save(g, m); return m.ToArray(); }
        var plain = SaveGame.Load(new MemoryStream(Save(game)), definitions);
        Assert.Null(((OpenSWR.Sim.Ai.HumanController)plain.Controllers[Owner.Empire]).Advisor);

        human.Advisor = new Advisor(Owner.Empire, () => game.Clock.Tick, briefing: true, tips: true, rand: _ => 0)
        {
            TopicSource = () => [new(1, 7, 10), new(2, 1, 140), new(3, 2, 110)],
        };
        human.Advisor.BuildTopics(game);
        human.Advisor.MessageCode(4);
        human.Advisor.Toggle(0x116);
        human.Advisor.MenuOpen();
        human.Advisor.MusicTrack();
        var bytes = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(bytes), definitions);
        var a = ((OpenSWR.Sim.Ai.HumanController)loaded.Controllers[Owner.Empire]).Advisor!;
        // the shared UI generator's state is written with controllers and moves under parallel tests: compared without it
        static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");
        Assert.Equal(Text(bytes), Text(Save(loaded)));
        Assert.Equal(40000, a.Due(74)); // the briefing had not played
        Assert.Equal(human.Advisor.Due(54), a.Due(54));
        Assert.Equal((human.Advisor.MenuOpened, human.Advisor.ManageProductionOn, human.Advisor.TipsOff, human.Advisor.Translate, 3, 1, 1),
            (a.MenuOpened, a.ManageProductionOn, a.TipsOff, a.Translate, a.MusicCounter, a.MenuClicks, a.ToggleCounter));
        Assert.Equal(0, a.Records[Advisor.Production]);
        a.TopicSource = () => [new(1, 7, 10), new(2, 1, 140), new(3, 2, 110)];
        Assert.Empty(a.BuildTopics(loaded));
        Assert.Equal([3, 2], a.Topics.Select(t => t.N));
    }

    // A shipyard manager with a non-empty queue is busy: with every own shipyard busy the build helper
    // finds no producer and refuses (0xA0, 3)
    [GameFact]
    public void BuildHelperRefusesBusyShipyards()
    {
        var definitions = OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path);
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), definitions);
        var a = new Advisor(Owner.Empire, () => 0, briefing: false, tips: false, rand: _ => 0);
        var target = game.Systems.First(x => x.Owner == Owner.Empire);
        Assert.NotNull(a.Producer(game, 2, target));
        foreach (var m in game.Systems.Where(x => x.Owner == Owner.Empire).Select(x => x.Managers[(int)ManufacturingKind.Ship]).OfType<ManufacturingManager>())
            m.Orders.Add(new GameObject(0x14000046, Owner.Empire, game.NextId()));
        Assert.Null(a.Producer(game, 2, target));
        a.HelperStart(0x110);
        Assert.Null(a.HelperPick(game, target));
    }

    // ---- delegation ----

    static GameState Delegation(Owner side) =>
        NewGame.Create(new GameSettings(side, 1, 0, 0, 532947196), OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path));

    static AdvisorModel Model(GameState game, Owner side)
    {
        var m = new AdvisorModel(side) { Rand = _ => 0 };
        m.Update(game);
        return m;
    }

    /// <summary>The command slot asked frame by frame until the record is done (at most one command).</summary>
    static Command? Run(DelegatedPlanner p, GameState game, AdvisorModel m)
    {
        p.Done = false;
        for (int frame = 0; frame < 10; frame++)
        {
            var c = p.Step(game, m, 1);
            if (p.Done) return c;
            Assert.Null(c);
        }
        throw new InvalidOperationException("the record never finished");
    }

    static int Sq(Sector a, Sector b) => (a.Definition.X - b.Definition.X) * (a.Definition.X - b.Definition.X) + (a.Definition.Y - b.Definition.Y) * (a.Definition.Y - b.Definition.Y);

    // System bits (idle producers, HQ), troop need (support 55 -> 2, Uprising x 2) and surplus
    // (facility need f), sector sums, model totals, cap / dem
    [GameFact]
    public void AdvisorModelRecords()
    {
        var game = Delegation(Owner.Alliance);
        var m = Model(game, Owner.Alliance);
        foreach (var r in m.Systems.Values.Where(r => r.Own && r.Populated))
        {
            var s = r.System;
            foreach (var (kind, bit, family) in new (ManufacturingKind, Func<AdvisorSystem, bool>, uint)[] { (ManufacturingKind.Ship, x => x.IdleShipyard, 0x28u),
                (ManufacturingKind.Training, x => x.IdleTraining, 0x29u), (ManufacturingKind.Construction, x => x.IdleConstruction, 0x2Au) })
            {
                bool idle = !s.Uprising && !s.Blockade && s.Managers[(int)kind] is { Orders.Count: 0 } && s.Contents.Any(x => Family.Of(x.Type) == family && x.Usable);
                Assert.Equal(idle, bit(r));
            }
            int f = (r[AdvisorValue.Mines] + r[AdvisorValue.Refineries]) / 2 + r[AdvisorValue.ConstructionYards] + r[AdvisorValue.Shipyards]
                + r[AdvisorValue.TrainingFacilities];
            Assert.Equal(r[AdvisorValue.Troops] - Math.Max(f, r[AdvisorValue.TroopNeed]), r[AdvisorValue.TroopSurplus]);
            Assert.Equal(r[AdvisorValue.TroopSurplus] < 0, r.TroopShortfall);
        }
        Assert.Contains(m.Systems.Values, r => r.IdleConstruction); // an idle construction yard
        Assert.Contains(m.Systems.Values, r => r.Headquarters); // the HQ
        // support 55 -> need (69 - 55) div 10 + 1 = 2; Uprising doubles it
        var own = m.Systems.Values.First(r => r.Own && r.Populated && r[AdvisorValue.Support] < 100).System;
        own.Support = 55;
        m.Update(game);
        Assert.Equal((55, 2), (m.Systems[own][AdvisorValue.Support], m.Systems[own][AdvisorValue.TroopNeed]));
        own.Uprising = true;
        m.Update(game);
        var u = m.Systems[own];
        Assert.Equal(4, u[AdvisorValue.TroopNeed]);
        Assert.True(u.Uprising && u.TroopShortfall && u[AdvisorValue.TroopSurplus] < 0);
        Assert.False(u.IdleShipyard || u.IdleTraining || u.IdleConstruction); // Uprising: no idle producer
        foreach (var sec in m.Sectors)
        {
            var mine = sec.Systems.Where(x => x.Own).ToList();
            Assert.Equal((mine.Sum(x => x[AdvisorValue.Mines]), mine.Sum(x => x[AdvisorValue.Refineries]), mine.Sum(x => x[AdvisorValue.ConstructionYards])),
                (sec[AdvisorSectorValue.Mines], sec[AdvisorSectorValue.Refineries], sec[AdvisorSectorValue.ConstructionYards]));
            Assert.Equal(-mine.Where(x => x.Uprising).Sum(x => x[AdvisorValue.TroopSurplus]), sec[AdvisorSectorValue.UprisingShortfall]);
            Assert.Equal(mine.Count > 0, sec.Own);
        }
        Assert.True(u.Sector!.Uprising && u.Sector[AdvisorSectorValue.UprisingShortfall] > 0);
        Assert.Equal((m.Sectors.Sum(x => x[AdvisorSectorValue.Mines]), m.Sectors.Sum(x => x[AdvisorSectorValue.Refineries])), (m.Mines, m.Refineries));
        Assert.Equal((game.Sides[Owner.Alliance].MaintenanceCapacity, game.Sides[Owner.Alliance].Demand), (m.MaintenanceCapacity, m.MaintenanceDemand));
    }

    // Producer = an idle construction manager, product Mine only while Mines < Refineries (equal -> Refinery),
    // site = own unblockaded system with free energy (and raw material for a Mine) in the sector nearest the producer; 0x214 then
    // 0x210 count 1; no idle construction manager -> fail without a command
    [GameFact]
    public void ManageProductionPicks()
    {
        var game = Delegation(Owner.Alliance);
        foreach (bool mine in new[] { true, false })
        {
            var m = Model(game, Owner.Alliance);
            m.Mines = mine ? m.Refineries - 1 : m.Refineries;
            var p = new ProductionDelegate();
            var destination = Run(p, game, m)!;
            var manager = (ManufacturingManager)destination.Targets[0].Target;
            var site = (StarSystem)destination.ArgRef!;
            Assert.Equal((0x214, ManufacturingKind.Construction, 4, 1), (destination.Type, manager.Kind, p.Machine, p.Priority));
            Assert.True(m.Systems[manager.System] is { IdleConstruction: true, Own: true });
            var sr = m.Systems[site];
            Assert.True(sr.Own && sr[AdvisorValue.FreeEnergy] > 0 && (!mine || sr[AdvisorValue.FreeRaw] > 0)
                && !sr.Blockade);
            Assert.True(Orders.Validate(game, destination).Success);
            int near = m.Sectors.Where(x => x.FreeEnergy && (!mine || x.FreeRaw))
                .Min(x => Sq(x.Sector, manager.System.Sector));
            Assert.Equal(near, Sq(site.Sector, manager.System.Sector));
            var build = Run(p, game, m)!;
            Assert.Equal((0x210, (GameObject)manager, mine ? 0x2C000001u : 0x2D000002u, 1u, 0), (build.Type, build.Targets[0].Target, build.Fields[0], build.Fields[1], p.Machine));
            Assert.True(Orders.Validate(game, build).Success);
        }
        foreach (var mm in game.Systems.Select(s => s.Managers[(int)ManufacturingKind.Construction]).OfType<ManufacturingManager>())
            mm.Orders.Add(new GameObject(0x2C000001, Owner.Alliance, game.NextId()));
        var busy = new ProductionDelegate();
        Assert.Null(Run(busy, game, Model(game, Owner.Alliance)));
        Assert.Equal(0, busy.Machine);
    }

    // Uprising systems first; producer = an idle training manager; strongest defence troops (cheapest
    // maintenance for an Uprising target); 0x214 then 0x212 count 1; no free maintenance -> fail without a command
    [GameFact]
    public void ManageGarrisonsPicks()
    {
        var game = Delegation(Owner.Alliance);
        var catalogue = new OpenSWR.Sim.Ai.Catalogue(game.Definitions);
        var classes = OpenSWR.Sim.Research.Buildable(game, Owner.Alliance, 0x29).Select(r => catalogue[r.Common.Ref]).OfType<OpenSWR.Sim.Ai.Product>()
            .Where(x => x.DefenseTroop).ToList();
        Assert.NotEmpty(classes);
        foreach (bool uprising in new[] { false, true })
        {
            var m = Model(game, Owner.Alliance);
            var shortOf = m.Systems.Values.Where(r => r.TroopShortfall && r.Own && !r.Blockade).ToList();
            Assert.NotEmpty(shortOf);
            StarSystem? expected = null;
            if (uprising)
            {
                expected = shortOf.Last().System;
                expected.Uprising = true;
                m.Update(game);
            }
            var p = new GarrisonDelegate();
            var destination = Run(p, game, m)!;
            var manager = (ManufacturingManager)destination.Targets[0].Target;
            var target = (StarSystem)destination.ArgRef!;
            Assert.Equal((0x214, ManufacturingKind.Training, 5, 1), (destination.Type, manager.Kind, p.Machine, p.Priority));
            Assert.True(m.Systems[manager.System] is { IdleTraining: true, Own: true });
            Assert.True(m.Systems[target] is { TroopShortfall: true, Own: true });
            if (expected is not null) Assert.Same(expected, target);
            var build = Run(p, game, m)!;
            Assert.Equal((0x212, (GameObject)manager, 1u, 0), (build.Type, build.Targets[0].Target, build.Fields[1], p.Machine));
            var product = catalogue[build.Fields[0]]!;
            if (uprising) Assert.Equal(classes.Min(x => x.MaintenanceCost), product.MaintenanceCost); // cheapest
            else Assert.Equal(classes.Max(x => x.DefenseStrength), product.DefenseStrength);           // strongest
            Assert.True(Orders.Validate(game, destination).Success);
            Assert.True(Orders.Validate(game, build).Success);
            // a loaded game keeps the target as the system itself (Advisor.LoadedKey): state 3 still reads its record (audit 2026-10-26 #14)
            // (the install has one class, so only the no-record branch's rand(1) tells them apart)
            var loaded = new GarrisonDelegate { Machine = 3, Target = target, Producer = manager };
            int draws = 0;
            m.Rand = _ => { draws++; return 0; };
            Run(loaded, game, m);
            Assert.Equal(build.Fields[0], Run(loaded, game, m)!.Fields[0]);
            Assert.Equal(0, draws);
            m.Rand = _ => 0;
            if (expected is not null) expected.Uprising = false;
        }
        game.Sides[Owner.Alliance].Demand = game.Sides[Owner.Alliance].MaintenanceCapacity; // no free maintenance
        var none = new GarrisonDelegate();
        Assert.Null(Run(none, game, Model(game, Owner.Alliance)));
        Assert.Equal((0, 0), (none.Machine, none.Count));
    }

    // 0x216 toggles Reserved over the selection (any reserved -> clear all, else set
    // all); a reserved construction manager is no delegated producer (model ReservedConstruction): with the only idle one reserved planner 0x14
    // fails without a command; 0x216 again clears it. Validation: the production manager checks (foreign (1, 0x14)); the field
    // survives save -> load
    [GameFact]
    public void ReservedManagerIsSkipped()
    {
        var game = Delegation(Owner.Alliance);
        static OrderResult Exec(GameState g, Command c)
        {
            var r = Orders.Execute(g, c);
            while (g.Clock.DispatchOne(g)) { }
            return r;
        }
        var yards = game.Systems.Select(s => s.Managers[(int)ManufacturingKind.Construction]).OfType<ManufacturingManager>()
            .Where(m => m.Owner == Owner.Alliance && m.Existing).ToList();
        var producer = (ManufacturingManager)Run(new ProductionDelegate(), game, Model(game, Owner.Alliance))!.Targets[0].Target;
        foreach (var mm in yards.Where(m => m != producer)) mm.Orders.Add(new GameObject(0x2C000001, Owner.Alliance, game.NextId()));
        Assert.Same(producer, Run(new ProductionDelegate(), game, Model(game, Owner.Alliance))!.Targets[0].Target); // the only idle one
        var other = game.Systems.SelectMany(s => s.Managers).OfType<ManufacturingManager>().First(m => m != producer && m.Owner == Owner.Alliance && Economy.Members(m).Count > 0); // a training or ship manager
        Command Reserve(params ManufacturingManager[] ms) => new(0x216, Owner.Alliance) { Targets = [.. ms.Select(m => ((GameObject)m, 0))] };
        Assert.Equal(new OrderResult(1, 0x14), Orders.Validate(game, new Command(0x216, Owner.Empire) { Targets = [(producer, 0)] }));
        Assert.True(Exec(game, Reserve(producer)).Success);
        Assert.True(producer.Reserved);
        var m = Model(game, Owner.Alliance);
        Assert.True(m.Systems[producer.System].ReservedConstruction);
        var p = new ProductionDelegate();
        Assert.Null(Run(p, game, m));
        Assert.Equal(0, p.Machine);
        // save -> load keeps it
        using (var stream = new MemoryStream())
        {
            SaveGame.Save(game, stream);
            stream.Position = 0;
            var loaded = SaveGame.Load(stream, game.Definitions);
            Assert.True(loaded.Systems[game.Systems.IndexOf(producer.System)].Managers[(int)ManufacturingKind.Construction]!.Reserved);
        }
        // a selection with one reserved manager clears all; none reserved sets all
        Assert.True(Exec(game, Reserve(other, producer)).Success);
        Assert.False(producer.Reserved || other.Reserved);
        Assert.True(Exec(game, Reserve(other, producer)).Success);
        Assert.True(producer.Reserved && other.Reserved);
        Assert.True(Exec(game, Reserve(producer)).Success);
        Assert.False(producer.Reserved);
        Assert.Same(producer, Run(new ProductionDelegate(), game, Model(game, Owner.Alliance))!.Targets[0].Target);
    }

    // The cycle: the 0x214 goes out through the human's outbox, runs in the next tick's stage 3, and
    // that tick's cycle sends the Build (its record has the higher priority); a refused 0x214 resets the machine and the priority;
    // 5 delegated commands per day (the record stays current, DayDue resets); a record turned off issues nothing
    [GameFact]
    public void DelegationCycle()
    {
        var game = Delegation(Owner.Alliance);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance) { Rand = _ => 0 };
        var a = human.Advisor = new Advisor(Owner.Alliance, () => game.Clock.Tick, briefing: null, tips: false, rand: _ => 0);
        game.Controllers[Owner.Alliance] = human;
        game.Sides[Owner.Alliance].Speed = Clock.TicksForSpeed(3);
        game.Clock.CommandTrace = [];
        OpenSWR.Sim.Ai.Controller.StartGame(game);
        game.Clock.Step(game);
        Assert.DoesNotContain(human.Outbox, c => c.Type != 1); // nothing enabled
        a.Toggle(0x116);
        game.Clock.Step(game);
        var destination = Assert.Single(human.Outbox, c => c.Type != 1);
        Assert.Equal((0x214, 1, 1), (destination.Type, a.Issued, a.Planners[Advisor.Production].Priority));
        game.Clock.Step(game);
        Assert.Contains(game.Clock.CommandTrace, c => c == destination && c.Serial != 0); // executed in stage 3
        var build = Assert.Single(human.Outbox, c => c.Type != 1);
        Assert.Equal((0x210, destination.Targets[0].Target, 2), (build.Type, build.Targets[0].Target, a.Issued));
        Assert.True(destination.Sequence < build.Sequence);
        game.Clock.Step(game);
        Assert.Contains(game.Clock.CommandTrace, c => c == build && c.Serial != 0);
        var yard = (ManufacturingManager)build.Targets[0].Target;
        Assert.Single(yard.Orders);
        Assert.Same(destination.ArgRef, yard.TargetKey);

        // a refused 0x214: the site turns foreign before the command is asked for ((1, 0x24)) -> the failure slot
        var p = a.Planners[Advisor.Production];
        yard.Orders.Clear();
        var m = Model(game, Owner.Alliance);
        p.Done = false;
        while (p.Machine != 3) Assert.Null(p.Step(game, m, 1));
        var site = ((AdvisorSystem)p.Target!).System;
        var owner = site.Owner;
        site.Owner = Owner.Empire;
        int issued = a.Issued, queued = human.Outbox.Count;
        a.Delegate(game, human);
        Assert.Equal((0, 0, issued, queued), (p.Machine, p.Priority, a.Issued, human.Outbox.Count));
        site.Owner = owner;

        // the budget used up: no command, the record stays current; DayDue resets the count
        a.Issued = Advisor.DailyBudget;
        a.Delegate(game, human);
        Assert.Equal((Advisor.Production, queued), (a.Current, human.Outbox.Count));
        a.DayDue();
        a.Delegate(game, human);
        Assert.Equal((0x214, 1, queued + 1), (human.Outbox[^1].Type, a.Issued, human.Outbox.Count));
        // turned off between the 0x214 and the Build: nothing is issued; not the current record, so its machine, keys and priority
        // wait; turned on again it is the urgent pick and sends the pending Build
        a.Toggle(0x116);
        a.Delegate(game, human);
        Assert.Equal((queued + 1, 4, 1), (human.Outbox.Count, p.Machine, p.Priority));
        ((ManufacturingManager)p.Producer!).TargetKey = ((AdvisorSystem)p.Target!).System; // its 0x214 executed meanwhile (stage 3)
        a.Toggle(0x116);
        a.Delegate(game, human);
        Assert.Equal((0x210, queued + 2, 0, 0), (human.Outbox[^1].Type, human.Outbox.Count, p.Machine, p.Priority));
    }

    // Both toggles on: the issue phase zeroes the asked record's priority, so after each
    // 0x214 + Build pair the round-robin pick moves to the other planner (0x14 and 0x15 alternate)
    [GameFact]
    public void DelegationAlternatesWithBothToggles()
    {
        var game = Delegation(Owner.Alliance);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance) { Rand = _ => 0 };
        var a = human.Advisor = new Advisor(Owner.Alliance, () => game.Clock.Tick, briefing: null, tips: false, rand: _ => 0);
        game.Controllers[Owner.Alliance] = human;
        OpenSWR.Sim.Ai.Controller.StartGame(game);
        a.Toggle(0x116);
        a.Toggle(0x115);
        var sent = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            a.DayDue(); // keep the budget out of the way
            int before = human.Outbox.Count;
            a.Delegate(game, human);
            Assert.Equal(before + 1, human.Outbox.Count); // one command per cycle
            sent.Add(human.Outbox[^1].Type);
        }
        int first = sent[0] == 0x214 && sent[1] == 0x210 ? 0x210 : 0x212, second = first == 0x210 ? 0x212 : 0x210;
        Assert.Equal([0x214, first, 0x214, second, 0x214, first, 0x214, second], sent);
    }

    // The planners' fields ride in the advisor entry; save -> load -> save byte-equal; the Build follows after the load
    [GameFact]
    public void DelegationSaveRoundTrip()
    {
        var definitions = OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path);
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), definitions);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance) { Rand = _ => 0 };
        human.Advisor = new Advisor(Owner.Alliance, () => game.Clock.Tick, briefing: null, tips: false, rand: _ => 0);
        game.Controllers[Owner.Alliance] = human;
        OpenSWR.Sim.Ai.Controller.StartGame(game);
        human.Advisor.Toggle(0x115);
        game.Clock.Step(game); // the 0x214 decided
        var p = human.Advisor.Planners[Advisor.Garrisons];
        Assert.Equal(5, p.Machine);
        static byte[] Save(GameState g) { using var s = new MemoryStream(); SaveGame.Save(g, s); return s.ToArray(); }
        static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");
        var bytes = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(bytes), definitions);
        Assert.Equal(Text(bytes), Text(Save(loaded)));
        var lh = (OpenSWR.Sim.Ai.HumanController)loaded.Controllers[Owner.Alliance];
        var a = lh.Advisor!;
        a.Rand = _ => 0;
        var q = a.Planners[Advisor.Garrisons];
        Assert.Equal((p.Machine, p.Priority, p.Count, p.Product, human.Advisor.Issued), (q.Machine, q.Priority, q.Count, q.Product, a.Issued));
        OpenSWR.Sim.Ai.Controller.StartGame(loaded);
        loaded.Clock.Step(loaded);
        var build = lh.Outbox.Single(c => c.Type == 0x212);
        Assert.Equal(((GameObject)p.Producer!).Ref, build.Targets[0].Target.Ref);
    }

    // Mood worked examples (d = 0): (12, 30) -> 0x10 -> MDATA.310, (25, 10) -> 4 -> .306, (18, 9) -> 4, (31, 0)
    // -> 2 -> .305, (15, 10) -> 8 -> rand(3) + 1, (5, 0) -> 0x10; the d branches; the mood pick (counter 0) takes no draw except for 8
    [Fact]
    public void MoodWorkedExamples()
    {
        Assert.Equal([0x10, 4, 4, 2, 8, 0x10], new[] { (12, 30), (25, 10), (18, 9), (31, 0), (15, 10), (5, 0) }.Select(x => Advisor.MoodByte(x.Item1, x.Item2)));
        Assert.Equal([1, 1, 2, 0x20, 0x40, 0x40], new[] { 2, 5, 1, -1, -2, -9 }.Select(d => Advisor.MoodByte(31, 0, d)));
        foreach (var (mood, track) in new[] { (0x10, 310), (4, 306), (2, 305) })
        {
            var r = new Rig();
            r.A.Mood = mood;
            Assert.Equal(track, r.A.MusicTrack());
            Assert.Empty(r.Rolls);
        }
        var eight = new Rig();
        eight.Draws.Enqueue(1);
        Assert.Equal(302, eight.A.MusicTrack());
        Assert.Equal([3], eight.Rolls);
    }

    // The cycle counts the systems whose player view is owned by the player / the other side; DayDue resets the
    // budget and recomputes the mood from them; before the first day change the mood is the ctor's 8
    [GameFact]
    public void MoodAtDayDue()
    {
        var game = Delegation(Owner.Alliance);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance) { Rand = _ => 0 };
        var a = human.Advisor = new Advisor(Owner.Alliance, () => game.Clock.Tick, briefing: null, tips: false, rand: _ => 0);
        game.Controllers[Owner.Alliance] = human;
        OpenSWR.Sim.Ai.Controller.StartGame(game);
        Assert.Equal(8, a.Mood);
        int day = game.Day;
        while (game.Day == day) game.Clock.Step(game);
        int own = game.Systems.Count(s => Knowledge.See(game, s, Owner.Alliance).Owner == Owner.Alliance);
        int enemy = game.Systems.Count(s => Knowledge.See(game, s, Owner.Alliance).Owner == Owner.Empire);
        Assert.Equal((own, enemy), (a.StandingOwn, a.StandingEnemy));
        Assert.True(own > 0 && enemy > 0);
        Assert.Equal(Advisor.MoodByte(own, enemy), a.Mood);
    }

    // The victory word with game over reaches the advisors once: Empire wins -> C3PO 87 / IMP 84, validity
    // 40000, game-over UI flag; Alliance wins -> C3PO 86 / IMP 85; no game over -> nothing
    [GameFact]
    public void StatusMaskAtGameOver()
    {
        var definitions = OpenSWR.Data.Dat.GameDefinitions.Load(GameDir.Path);
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 1, 532947196), definitions);
        Advisor Human(Owner side)
        {
            var h = new OpenSWR.Sim.Ai.HumanController(side);
            game.Controllers[side] = h;
            return h.Advisor = new Advisor(side, () => game.Clock.Tick, briefing: null, tips: false, rand: _ => 0);
        }
        var (c3po, imp) = (Human(Owner.Alliance), Human(Owner.Empire));
        var hq = game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key)!;
        Objects.Destroy(game, hq, 0x14);
        game.Clock.Flush(game);
        Assert.True(game.GameOver);
        Assert.Equal((game.Clock.Tick + 40000, game.Clock.Tick + 40000, true, true), (c3po.Due(87), imp.Due(84), c3po.GameOverUi, imp.GameOverUi));
        Assert.Equal((0, 0), (c3po.Due(86), imp.Due(85)));
        Victory.Recompute(game); // unchanged word: no second request
        c3po.Tick(); // gate closed: the request stays pending
        Assert.True(c3po.Pending(87));

        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var a = new Advisor(side, () => 0, briefing: null, tips: false);
            a.StatusMask(new VictoryStatus { AllianceWins = true }); // no game over
            Assert.False(a.GameOverUi);
            a.StatusMask(new VictoryStatus { AllianceWins = true, GameOver = true });
            Assert.Equal((40000, true), (a.Due(side == Owner.Alliance ? 86 : 85), a.GameOverUi));
        }
    }

    // End state from the victory word and the player side, then the end movie
    [Theory]
    [InlineData(true, false, Owner.Alliance, 0x11, "MDATA.106")]
    [InlineData(true, false, Owner.Empire, 0x12, "MDATA.107")]
    [InlineData(false, true, Owner.Alliance, 0x10, "MDATA.105")]
    [InlineData(false, true, Owner.Empire, 0x13, "MDATA.108")]
    [InlineData(false, false, Owner.Alliance, 0, null)]
    public void EndStateAndMovie(bool empireWins, bool allianceWins, Owner player, int state, string? movie)
    {
        Assert.Equal(state, Advisor.EndState(new VictoryStatus { EmpireWins = empireWins, AllianceWins = allianceWins, GameOver = true }, player));
        Assert.Equal(movie, Advisor.EndMovie(state));
    }
}
