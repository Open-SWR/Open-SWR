using System.Text;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Message types (trigger -> category, kind, templates, arguments), 300-day expiry, lit buttons / tab exit, save round trip and older saves.</summary>
public class MessageTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static string Name(SdCommon c) => $"#{c.StringId}";

    static (GameState Game, Messages Log) New(Owner side = Owner.Alliance)
    {
        var game = NewGame.Create(new GameSettings(side, 1, 0, 0, 532947196), Definitions);
        return (game, new Messages(game, side, Name));
    }

    static GameObject OwnShip(GameState game, Owner side) =>
        game.Created.First(o => o.Owner == side && Family.IsCapitalShip(o.Type) && o.Existing && o.Container is Fleet { Container: StarSystem });

    static void Fact(GameState game, int kind, GameObject o, long old, long now) => game.FactTrace!.Add(new(game.Day, kind, o.Ref, old, now));

    // Losses by DestroyedReason: 0x15 -> type 0xC (8), 6 -> 0x22 (0x100), 7 -> 0x2B (0x10, kind 8), 0xD -> 0x13 (0x100).
    // Battle (0xC) and scrap (0x14) losses give none. One line per unit.
    [GameFact]
    public void Losses()
    {
        var (game, log) = New();
        var ships = game.Created.Where(o => o.Owner == Owner.Alliance && Family.IsCapitalShip(o.Type) && o.Existing).Take(4).ToList();
        Objects.Destroy(game, ships[0], 0x15);
        Objects.Destroy(game, ships[1], 0xC);
        Objects.Destroy(game, ships[2], 0x14);
        game.Clock.Flush(game);
        var m = Assert.Single(log.Update());
        Assert.Equal((0xC, MessageCategory.Manufacturing, 3, 0x7080), (m.Type, m.Category, m.Kind, m.Title!.Template));
        Assert.Equal([0x7081, 0x7082], m.Body!.Select(b => b.Template));
        Assert.Equal($"#{Definitions.Resolve(ships[0].Type)!.Common.StringId}", m.Body![1].Args[0].Text);
        Assert.True(m.New);

        Objects.Destroy(game, ships[3], 6);
        var minor = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && Family.Of(c.Type) >= 0x38);
        Objects.Destroy(game, minor, 7);
        game.Clock.Flush(game);
        var added = log.Update();
        Assert.Contains(added, x => x.Type == 0x22 && x.Category == MessageCategory.Conflict && x.Title!.Template == 0x7083 && x.Body![^1].Template == 0x7085);
        var killed = Assert.Single(added, x => x.Type == 0x2B);
        Assert.Equal((MessageCategory.Mission, 8, 0x7208, $"#{minor.Definition.Common.StringId}"), (killed.Category, killed.Kind, killed.Title!.Template, killed.Title.Args[0].Text));
        Assert.DoesNotContain(added, x => x.Type == 0x1A); // the assassination has its own message
        Assert.True(added.Select(x => x.Id).SequenceEqual(added.Select(x => x.Id).Order()) && added[0].Id > m.Id);
    }

    // Advisor codes (deployed 3, fleet arrival 5, Mothma arrives 0x17) and the voice pick (list 0xF0), only through the
    // picker. Without one no list is asked for and Voice stays 0.
    [GameFact]
    public void AdvisorCodesAndVoices()
    {
        foreach (bool picker in new[] { false, true })
        {
            var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
            var lists = new List<int>();
            var log = new Messages(game, Owner.Alliance, Name, picker ? list => { lists.Add(list); return 7; } : null);
            var ship = OwnShip(game, Owner.Alliance);
            var mothma = game.Characters.First(c => c.Type == 0x30000240);
            Fact(game, 0x308, ship, 0, 1);
            Fact(game, 0x306, ship.Container!, 1, 0);
            Fact(game, 0x306, mothma, 1, 0);
            var added = log.Update();
            Assert.Equal(3, Assert.Single(added, x => x.Type == 0xE).AdvisorCode);
            Assert.Equal(5, Assert.Single(added, x => x.Type == 0xD).AdvisorCode);
            var arrives = Assert.Single(added, x => x.Type == 0x14);
            Assert.Equal((0x17, picker ? 7 : 0), (arrives.AdvisorCode, arrives.Voice));
            Assert.Equal(picker ? [0xF0] : [], lists);
        }
    }

    // Codes per builder: own uprising begins 2 / ends 1 (picture 1011 for an Alliance-owned end). A minor character arriving
    // gives 0x15 with picture 1042 / sound 1148 (Alliance). Several agents led by Mothma give 0x17.
    [GameFact]
    public void AdvisorCodesPerBuilder()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var own = game.Systems.Where(s => s.Owner == Owner.Alliance && s.Populated).ToList();
        own[1].Uprising = true;
        var log = new Messages(game, Owner.Alliance, Name);
        own[0].Uprising = true;
        own[1].Uprising = false;
        var minor = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && Family.Of(c.Type) is >= 0x38 and < 0x3C);
        Fact(game, 0x306, minor, 1, 0);
        var added = log.Update();
        Assert.Equal((2, 1010), added.Where(x => x.Type == 3 && x.Title!.Template == 0x7030).Select(x => (x.AdvisorCode, x.Picture)).Single());
        Assert.Equal((1, 1011), added.Where(x => x.Type == 3 && x.Title!.Template == 0x7031).Select(x => (x.AdvisorCode, x.Picture)).Single());
        var arrives = Assert.Single(added, x => x.Type == 0x14);
        Assert.Equal((0x15, 1042, 1148), (arrives.AdvisorCode, arrives.Picture, arrives.Sound));
        var mothma = game.Characters.First(c => c.Type == 0x30000240);
        var other = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c != mothma && Missions.Location(c) == Missions.Location(mothma) && c.Ref > mothma.Ref);
        Fact(game, 0x306, mothma, 1, 0);
        Fact(game, 0x306, other, 1, 0);
        Assert.Equal(0x17, Assert.Single(log.Update(), x => x.Type == 0x14).AdvisorCode);
    }

    // A capital ship ordered at a shipyard in system A for a fleet in system B travels alone. Its arrival gives one type 0xD
    // "Ships arrive" message (0x7076 / 0x7077, category 0x80, picture 1019 for the Empire, sound 1114) with the ship's 0x707E line.
    [GameFact]
    public void DeliveredShipArrives()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 401859171), Definitions);
        game.Sides[Owner.Empire].MaintenanceCapacity += 100_000;
        var home = game.SystemsOf(Owner.Empire).First(s => s.Type != Refs.Coruscant && s != game.HeadquartersSystem && !s.Blockade
            && Economy.Members(s.Managers[(int)ManufacturingKind.Ship]).Count > 0);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable && f.Container is StarSystem s && s != home && !s.Blockade);
        var m = home.Managers[(int)ManufacturingKind.Ship];
        var log = new Messages(game, Owner.Empire, Name);
        uint type = Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18).Common.Ref;
        foreach (var c in new[] { new Command(0x214, Owner.Empire) { Targets = [(m, 0)], ArgRef = fleet }, new Command(0x211, Owner.Empire) { Targets = [(m, 0)], Fields = [type, 1] } })
        {
            Assert.True(Orders.Execute(game, c).Success);
            while (game.Clock.DispatchOne(game)) { }
        }
        var ship = Assert.Single(m.Orders);
        var arrivals = new List<GameMessage>();
        for (int day = game.Day + 1; day < game.Day + 400 && arrivals.Count == 0; day++)
        {
            game.Clock.RunUntilDay(game, day);
            arrivals.AddRange(log.Update().Where(x => x.Type == 0xD));
        }
        Assert.Same(fleet, ship.Container);
        var a = Assert.Single(arrivals);
        Assert.Equal((0x7076, MessageCategory.Fleet, 1019, 1114, 5), (a.Title!.Template, a.Category, a.Picture, a.Sound, a.AdvisorCode));
        Assert.Equal([0x7077, 0x707E], a.Body!.Select(b => b.Template));
        Assert.Equal(Name(Definitions.Resolve(ship.Type)!.Common), a.Body![1].Args[0].Text);
    }

    // 0x308 -> type 0xE (one unit by class, several 0x7088 + lines). 0x306 -> 0xD fleet / 0x14 agent.
    [GameFact]
    public void DeployedAndArrived()
    {
        var (game, log) = New();
        var ship = OwnShip(game, Owner.Alliance);
        var fleet = (Fleet)ship.Container!;
        var squadron = game.Created.First(o => o.Owner == Owner.Alliance && Family.Of(o.Type) is >= 0x1C and < 0x20 && o.Existing);
        var troops = game.Created.Where(o => o.Owner == Owner.Alliance && Family.IsTroop(o.Type) && o.Existing && o.Container is StarSystem)
            .GroupBy(o => (o.Container, o.Type)).First(g => g.Count() >= 2).Take(2).ToList();
        var yards = ((StarSystem)troops[0].Container!).Managers;
        (ship.BuilderKey, squadron.BuilderKey) = (yards[0], yards[1]);
        foreach (var t in troops) t.BuilderKey = yards[2];
        Fact(game, 0x308, ship, 0, 1);
        Fact(game, 0x308, squadron, 0, 1);
        foreach (var t in troops) Fact(game, 0x308, t, 0, 1);
        var agent = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c.Container is StarSystem);
        Fact(game, 0x306, fleet, 1, 0);
        Fact(game, 0x306, agent, 1, 0);
        var added = log.Update();
        Assert.Contains(added, x => x.Type == 0xE && x.Title!.Template == (Family.Of(ship.Type) < 0x18 ? 0x7086 : 0x708E) && x.Subject == ship.Ref);
        Assert.Contains(added, x => x.Type == 0xE && x.Title!.Template == 0x708A && x.Body![0].Template == 0x708B);
        var several = Assert.Single(added, x => x.Type == 0xE && x.Title!.Template == 0x7088);
        Assert.Equal([0x7089, 0x707F, 0x707F], several.Body!.Select(b => b.Template));
        Assert.All(added.Where(x => x.Type == 0xE), x => Assert.Equal(MessageCategory.Manufacturing, x.Category));
        var arrival = Assert.Single(added, x => x.Type == 0xD);
        Assert.Equal((MessageCategory.Fleet, 0x7078, fleet.Ref), (arrival.Category, arrival.Title!.Template, arrival.Subject));
        Assert.Contains(added, x => x.Type == 0x14 && x.Category == MessageCategory.Mission && x.Title!.Template == 0x70C1);
        // Squadron deployed 1111 (A), other deployments none, fleet arrival 1114, units arriving 1115 (A).
        Assert.Equal(1111, added.Single(x => x.Type == 0xE && x.Title!.Template == 0x708A).Sound);
        Assert.All(added.Where(x => x.Type == 0xE && x.Title!.Template != 0x708A), x => Assert.Equal(0, x.Sound));
        Assert.Equal(1114, arrival.Sound);
        Fact(game, 0x306, troops[0], 1, 0);
        Assert.Equal(1115, Assert.Single(log.Update(), x => x.Type == 0xD).Sound);
        // Per-manager approximation: one manager's deliveries of one day share one message, whatever their types.
        squadron.BuilderKey = yards[0];
        Fact(game, 0x308, ship, 0, 1);
        Fact(game, 0x308, squadron, 0, 1);
        var one = Assert.Single(log.Update(), x => x.Type == 0xE);
        Assert.Equal((0x7088, 3), (one.Title!.Template, one.Body!.Count));
    }

    // System view changes: owner -> type 4 (Joins Enemy 0x7039). Uprising -> 3 (0x7030 / 0x7031). Own system blockaded -> 7
    // (0x70A2). Category 1 / 0x100. Only the side's own view.
    [GameFact]
    public void SystemChanges()
    {
        var (game, log) = New();
        var own = game.Systems.Where(s => s.Owner == Owner.Alliance && s.Populated).ToList();
        own[0].Uprising = true;
        own[1].Blockade = true;
        var enemy = game.Systems.First(s => s.Owner == Owner.Empire && !Knowledge.IsLive(s, Owner.Alliance));
        enemy.Uprising = true; // not seen: no message
        var added = log.Update();
        Assert.Equal([(3, MessageCategory.Support, 0x7030), (7, MessageCategory.Conflict, 0x70A2)], added.Select(x => (x.Type, x.Category, x.Title!.Template)));
        Assert.Equal(Owner.Alliance, added[0].Body![0].Args[0].Side); // 0x7033: the owner side's adjective
        own[0].Uprising = false;
        own[2].Owner = Owner.Empire; // the live view follows the real owner
        added = log.Update();
        Assert.Contains(added, x => x.Type == 3 && x.Title!.Template == 0x7031);
        var joins = Assert.Single(added, x => x.Type == 4);
        Assert.Equal((0x7039, 0x703C, Owner.Empire), (joins.Title!.Template, joins.Body![0].Template, joins.Body[0].Args[1].Side));
    }

    // Research raised -> type 5 kind 4 with the encyclopedia key. Characters captured 0x19, injured / healed 0x1A. A manager
    // queue running empty -> 0x21 (0x71F8 / 0x71FA / 0x71FC by kind, picture of the yard, code 3, sound 1101).
    [GameFact]
    public void ResearchCharactersQueues()
    {
        var (game, log) = New();
        static uint YardFamily(ManufacturingManager x) => x.Kind switch { ManufacturingKind.Construction => 0x2Au, ManufacturingKind.Training => 0x29u, _ => 0x28u };
        var m = game.Systems.Where(s => s.Owner == Owner.Alliance).SelectMany(s => s.Managers)
            .First(x => x is not null && x.System.Contents.Any(y => y.Usable && Family.Of(y.Type) == YardFamily(x)));
        m.Orders.Add(new GameObject(0x14000001, Owner.Alliance, game.NextId()));
        log = new Messages(game, Owner.Alliance, Name); // snapshot with the queue
        Research.SetPoints(game, game.Sides[Owner.Alliance], 0, 1000);
        var leia = game.Characters.First(c => c.Ref == Refs.Leia);
        (leia.Captured, leia.Owner) = (true, Owner.Empire);
        var luke = game.Characters.First(c => c.Ref == Refs.Luke);
        luke.Injury = 20;
        m.Orders.Clear();
        var added = log.Update();
        var research = added.Where(x => x.Type == 5).ToList();
        Assert.NotEmpty(research);
        Assert.All(research, x => Assert.True(x.Kind == 4 && x.Encyclopedia != 0 && x.Title!.Template == 0x7048 && x.Category == MessageCategory.Manufacturing));
        Assert.Contains(added, x => x.Type == 0x19 && x.Title!.Template == 0x7158 && x.Body![0].Args[2].Side == Owner.Empire && x.Picture == 1048 && x.Overlay == 0);
        Assert.Contains(added, x => x.Type == 0x1A && x.Title!.Template == 0x7160);
        var idle = Assert.Single(added, x => x.Type == 0x21);
        var yard = m.System.Contents.First(x => x.Usable && Family.Of(x.Type) == YardFamily(m));
        int title = m.Kind switch { ManufacturingKind.Construction => 0x71F8, ManufacturingKind.Training => 0x71FA, _ => 0x71FC };
        Assert.Equal((title, m.System.Ref, 3, 1101, Messages.Picture(game, yard)), (idle.Title!.Template, idle.Subject, idle.AdvisorCode, idle.Sound, idle.Picture));
        luke.Injury = 0;
        Assert.Contains(log.Update(), x => x.Type == 0x1A && x.Title!.Template == 0x7162);
    }

    // TaskStatus != 0 -> type 0x16. Diplomacy repeats -> kind 5 with 0x70DB and the mission key. CompletionStatus != 0 with
    // TaskStatus 0 -> type 0x17 (foiled 0x7148 for a character's mission).
    [GameFact]
    public void MissionReports()
    {
        var (game, log) = New();
        var agent = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c.Container is StarSystem && !c.OnMission);
        var target = game.Systems.First(s => s.Owner == Owner.Neutral && s.Populated);
        var m = Missions.Create(game, 0x51000010, Owner.Alliance, target, [agent], [], 0, mandatory: false);
        m.TaskStatus = 3;
        var report = Assert.Single(log.Update(), x => x.Type == 0x16);
        Assert.Equal((MessageCategory.Mission, 5, 0x70D0, m.Ref), (report.Category, report.Kind, report.Title!.Template, report.Mission));
        Assert.Equal(0x70D1, report.Body![0].Template);
        Assert.Equal(0x70DB, report.Body[^1].Template);
        m.TaskStatus = 0;
        game.FactTrace!.Add(new(game.Day, 0x64, m.Ref, 0, 3));
        Assert.DoesNotContain(log.Update(), x => x.Type == 0x17); // no leader yet: dropped
        m.Leader = agent;
        bool major = Family.Of(agent.Type) < 0x38;
        game.FactTrace!.Add(new(game.Day, 0x64, m.Ref, 0, 3));
        var foiled = Assert.Single(log.Update(), x => x.Type == 0x17);
        Assert.Equal((major ? 0x7148 : 0x714A, major ? 0x7149 : 0x714B, 1046, 1152, target.Ref), (foiled.Title!.Template, foiled.Body![0].Template, foiled.Picture, foiled.Sound, foiled.Subject));
        Assert.Equal(0x70E6, foiled.Body[^1].Template); // location line: the leader's system
        game.FactTrace!.Add(new(game.Day, 0x64, m.Ref, 3, 0xC));
        Assert.DoesNotContain(log.Update(), x => x.Type == 0x17); // 0xC: research missions only
    }

    // A failed recruitment by Vader whose members left before the end-of-tick build: the "My ..." report 0x7118 / 0x711A
    // with his name and his portrait as overlay (the original's handler runs at the notification).
    [GameFact]
    public void FailedRecruitmentAfterMembersLeft()
    {
        var (game, log) = New(Owner.Empire);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        var target = game.Systems.First(s => s.Owner == Owner.Empire && s.Populated);
        var m = Missions.Create(game, 0x55000016, Owner.Empire, target, [vader], [], 0, mandatory: false);
        m.Leader = vader;
        m.TaskStatus = 2;
        (m.Leader, m.CompletionStatus) = (null, 1);
        m.Team.Clear();
        var report = Assert.Single(log.Update(), x => x.Type == 0x16);
        Assert.Equal((0x7118, 0x711A, Messages.Picture(game, vader)), (report.Title!.Template, report.Body![0].Template, report.Overlay));
        Assert.Equal(Name(vader.Definition.Common), report.Title.Args[0].Text);
    }

    // Only a movement mission's member reports its arrival. A member arriving at the target of another mission
    // (here espionage) stays silent.
    [GameFact]
    public void MissionMemberArrivingAtItsTargetSendsNoArrival()
    {
        var (game, log) = New();
        var agent = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c.Container is StarSystem && !c.OnMission);
        var target = game.Systems.First(s => s.Owner == Owner.Empire && s.Populated);
        Missions.Create(game, game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref, Owner.Alliance, target, [agent], [], 0, mandatory: false);
        log.Update();
        Fact(game, 0x306, agent, 1, 0);
        Assert.DoesNotContain(log.Update(), x => x.Type == 0x14);
    }

    // openswr deviation (player request): a facility arriving at its destination shows its own picture instead of 1020 / 1021
    [GameFact]
    public void ArrivingFacilityShowsItsPicture()
    {
        var (game, log) = New();
        var facility = game.Created.First(o => o.Owner == Owner.Alliance && Family.IsFacility(o.Type) && o.Existing && o.Container is StarSystem);
        Fact(game, 0x306, facility, 1, 0);
        var arrival = Assert.Single(log.Update(), x => x.Type == 0xD);
        Assert.Equal((0x707A, Messages.Picture(game, facility)), (arrival.Title!.Template, arrival.Picture));
    }

    // An arriving special force shows its own picture, as a character shows its portrait.
    [GameFact]
    public void ArrivingSpecialForceShowsItsPicture()
    {
        var (game, log) = New();
        var force = game.Created.First(o => o.Owner == Owner.Alliance && Family.IsSpecialForce(o.Type) && o.Existing && o.Container is StarSystem);
        Fact(game, 0x306, force, 1, 0);
        var arrival = Assert.Single(log.Update(), x => x.Type == 0x14);
        Assert.NotEqual(0, arrival.Overlay);
        Assert.Equal(Messages.Picture(game, force), arrival.Overlay);
    }

    // Branch (b) location line: a foiled mission whose members have already left at message time still names where they
    // were (0x70E6), not "failed to return" (0x70E7).
    [GameFact]
    public void FoiledMissionAfterMembersLeftNamesTheirPlace()
    {
        var (game, log) = New(Owner.Empire);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        var target = game.Systems.First(s => s.Owner == Owner.Empire && s.Populated);
        var m = Missions.Create(game, 0x55000016, Owner.Empire, target, [vader], [], 0, mandatory: false);
        m.Leader = vader;
        m.CompletionStatus = 3;
        m.Leader = null;
        m.Team.Clear();
        game.FactTrace!.Add(new(game.Day, 0x64, m.Ref, 0, 3));
        var report = Assert.Single(log.Update(), x => x.Type == 0x17);
        Assert.NotNull(m.ReportPlace);
        Assert.Equal(0x70E6, report.Body![1].Template);
    }

    // Branch (a): an enemy mission's view refreshed by the encounter step with CompletionStatus != 0 and TaskStatus 0 gives
    // 0x713E / 0x713F, picture 1073, sound 1148 (Alliance).
    [GameFact]
    public void EnemyMissionFoiled()
    {
        var (game, log) = New();
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Existing && c.Container is StarSystem && !c.OnMission);
        var target = game.Systems.First(s => s.Owner == Owner.Alliance && s.Populated);
        var m = Missions.Create(game, game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref, Owner.Empire, target, [agent], [], 0, mandatory: false);
        Assert.DoesNotContain(log.Update(), x => x.Type == 0x17);
        m.CompletionStatus = 3;
        Knowledge.Refresh(game, m, Owner.Alliance);
        var enemy = Assert.Single(log.Update(), x => x.Type == 0x17);
        Assert.Equal((0x713E, 0x713F, 1073, 1148, target.Ref), (enemy.Title!.Template, enemy.Body![0].Template, enemy.Picture, enemy.Sound, enemy.Subject));
        Assert.DoesNotContain(log.Update(), x => x.Type == 0x17); // unchanged view: no repeat
    }

    // Expiry (day + 300 < today), list (newest first, All without Advice), lit buttons, tab exit, read.
    [GameFact]
    public void StoreRules()
    {
        var (game, log) = New();
        game.Clock.RunUntilDay(game, 5);
        log.Update();
        game.Log.Clear();
        game.Log.AddRange([
            new GameMessage { Id = 1, Day = game.Day - 301, Category = MessageCategory.Fleet, New = true },
            new GameMessage { Id = 2, Day = game.Day - 300, Category = MessageCategory.Mission, New = true },
            new GameMessage { Id = 3, Day = game.Day, Category = MessageCategory.Advice, New = true },
            new GameMessage { Id = 4, Day = game.Day, Category = MessageCategory.Support },
            new GameMessage { Id = 5, Day = game.Day, Category = MessageCategory.Mission, Kind = 6 },
        ]);
        Messages.Expire(game);
        Assert.Equal([2, 3, 4, 5], game.Log.Select(m => m.Id));
        Assert.Equal([4, 2], Messages.List(game, 0).Select(m => m.Id));      // All: newest first, no Advice, no kind 6
        Assert.Equal([3], Messages.List(game, MessageCategory.Advice).Select(m => m.Id));
        Assert.Equal([MessageCategory.Mission, MessageCategory.Advice], Messages.LitCategories(game).Order());
        game.Log[0].Read = true;                                            // displaying only marks read
        Assert.Equal([MessageCategory.Mission, MessageCategory.Advice], Messages.LitCategories(game).Order());
        Messages.ClearNew(game, MessageCategory.Advice);                    // tab exit
        Assert.Equal([MessageCategory.Mission], Messages.LitCategories(game));
        Messages.ClearNew(game, 0);                                         // leaving the All tab: every message
        Assert.Empty(Messages.LitCategories(game));
    }

    // Saved with the game as an optional field. A log entry from an older save loads as a read message, with a category.
    [GameFact]
    public void SaveRoundTrip()
    {
        var (game, log) = New();
        Objects.Destroy(game, OwnShip(game, Owner.Alliance), 0x15);
        game.Clock.Flush(game);
        log.Update();
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        var bytes = stream.ToArray();
        var loaded = SaveGame.Load(new MemoryStream(bytes), Definitions);
        var again = new MemoryStream();
        SaveGame.Save(loaded, again);
        Assert.Equal(bytes, again.ToArray());
        Assert.Equal(game.Log.Single().Body!.SelectMany(b => b.Args), loaded.Log.Single().Body!.SelectMany(b => b.Args));
        Assert.True(loaded.Log.Single().New);

        var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!;
        node["log"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"day\":3,\"text\":\"fleet 7A9 arrived\",\"category\":123}]");
        string old = node.ToJsonString();
        var legacy = SaveGame.Load(new MemoryStream(Encoding.UTF8.GetBytes(old)), Definitions).Log.Single();
        Assert.Equal((1, 3, MessageCategory.Fleet, "fleet 7A9 arrived", false, 3), (legacy.Id, legacy.Day, legacy.Category, legacy.Text, legacy.New, legacy.Kind));

        var fresh = new MemoryStream();
        SaveGame.Save(NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions), fresh);
        Assert.DoesNotContain("\"log\"", Encoding.UTF8.GetString(fresh.ToArray()));
    }

    // Day by day for 40 days: no failure, an attached trace is not replaced or cleared, ids increase
    [GameFact]
    public void DailyUpdates()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 401859171), Definitions);
        var trace = new List<Fact>();
        game.FactTrace = trace;
        var log = new Messages(game, Owner.Empire, Name);
        for (int d = game.Day + 1; d <= 40; d++)
        {
            game.Clock.RunUntilDay(game, d);
            log.Update();
        }
        Assert.Same(trace, game.FactTrace);
        Assert.All(game.Log, e => Assert.InRange(e.Day, 2, 40));
        Assert.Equal(game.Log.Select(m => m.Id).Order(), game.Log.Select(m => m.Id));
    }

    // Kind-6 events (not listed; subject = scene): system destroyed 0x15 (0x400, scene 0x6F, 0x70C9 with the old owner).
    // Death Star sabotaged 0x23 (0x10, 0x72). Coruscant captured 0x1E (1, 0x74; the side's view shows the Alliance).
    // HQ destroyed 0x1F (0x400, 0x73).
    [GameFact]
    public void EventMovies()
    {
        var (game, log) = New(Owner.Empire);
        var s = game.Systems.First(x => x.Owner == Owner.Alliance && x.Populated && x.Type != Refs.Yavin);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        s.Add(fleet);
        var deathStar = Objects.Create(game, Definitions.CapitalShips.First(r => DeathStar.IsDeathStar(r.Common.Ref)).Common.Ref, Owner.Empire, fleet, completed: true);
        game.Clock.Flush(game);
        log.Update();
        fleet.Bombard = true;
        Combat.Bombard(game, s, 0x223);
        game.Clock.Flush(game);
        var m = Assert.Single(log.Update(), x => x.Kind == 6);
        Assert.Equal((0x15, MessageCategory.Event, 0x6Fu, 0x70C8, 0x70C9, Owner.Alliance), (m.Type, m.Category, m.Subject, m.Title!.Template, m.Body![0].Template, m.Body[0].Args[0].Side));
        Assert.Equal("MDATA.101", Messages.EventMovies[m.Subject]);
        Assert.False(m.New); // queued only: no lit category

        Objects.Destroy(game, deathStar, 6);
        game.Clock.Flush(game);
        m = Assert.Single(log.Update(), x => x.Kind == 6);
        Assert.Equal((0x23, MessageCategory.Mission, 0x72u, 0x71BD, 0x71BE), (m.Type, m.Category, m.Subject, m.Title!.Template, m.Body![0].Template));
        Assert.DoesNotContain(game.Log, x => x.Type == 0x22);

        var cor = game.Systems.First(x => x.Type == Refs.Coruscant);
        cor.Owner = Owner.Alliance;
        Victory.Recompute(game);
        game.VictoryState = game.VictoryState with { HqGone = true };
        var added = log.Update().Where(x => x.Kind == 6).ToList();
        Assert.Equal([(0x1E, MessageCategory.Support, 0x74u, 0x7170), (0x1F, MessageCategory.Event, 0x73u, 0x7188)], added.Select(x => (x.Type, x.Category, x.Subject, x.Title!.Template)));
        Assert.DoesNotContain(log.Update(), x => x.Kind == 6); // only on the rise
        Assert.DoesNotContain(Messages.List(game, 0), x => x.Kind == 6);
    }

    // A bombardment gives the attacker kind 7 (no code) and the defender kind 3 with code 0x2E. Title 0x7098, body 0x7099
    // (attacker, defender, system). L78 = the attacking fleet's ships. Category 0x100.
    [GameFact]
    public void StrikeFromABombardment()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        game.StrikeTrace = []; // shared by both logs
        var alliance = new Messages(game, Owner.Alliance, Name);
        var empire = new Messages(game, Owner.Empire, Name);
        var s = game.Systems.First(x => x.Owner == Owner.Alliance && x.Populated && x.Type != Refs.Yavin);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        s.Add(fleet); // arrived (no travel)
        Knowledge.RecomputeSystem(game, s);
        Knowledge.RecomputeObserved(game, fleet);
        game.Clock.Flush(game);
        Assert.Equal((true, false, true), (fleet.Usable, fleet.Enroute, Knowledge.IsLive(s, Owner.Empire)));
        alliance.Update();
        empire.Update();
        fleet.Bombard = true;
        Combat.Bombard(game, s, 0x220);
        game.Clock.Flush(game);
        var rec = Assert.Single(game.StrikeTrace);
        Assert.Equal((SideSet.Both, SideSet.Both, SideSet.Both), (rec.StartLive, rec.EndLive, rec.Fleets.Single().Live));
        var att = Assert.Single(empire.Update(), x => x.Type == 0xF);
        var def = Assert.Single(alliance.Update(), x => x.Type == 0xF);
        Assert.Equal((7, 0, MessageCategory.Conflict, 0x7098, 0x7099), (att.Kind, att.AdvisorCode, att.Category, att.Title!.Template, att.Body![0].Template));
        Assert.Equal((3, 0x2E, Owner.Alliance, Owner.Empire), (def.Kind, def.AdvisorCode, def.Defender, def.Attacker));
        Assert.Equal([Owner.Empire, Owner.Alliance], att.Body[0].Args.Take(2).Select(a => a.Side));
        Assert.Equal((s.Ref, false, 0), (att.Subject, att.Assault, att.Sound));
        Assert.Subset(att.Strike![1].ToHashSet(), fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).ToHashSet());
        Assert.Contains(att.Picture, new[] { 11162, 11163, 11164 });
    }

    // Pictures and bodies by the finish rules (hand-built records): bombardment 11162 / 11163 (L80) / 11164 (L88). Assault
    // taken / repulsed against a side 0x7091 / 0x7092, neutral 0x7093 / 0x7094, picture 11160 / 11161 by the attacker.
    // Defender code 0x2F. A strike whose system is not live at the end gives nothing. The save keeps only those two fields,
    // and the loaded message is kind 3.
    [GameFact]
    public void StrikeFinishRules()
    {
        var (game, log) = New();
        var own = game.Systems.First(x => x.Owner == Owner.Alliance && x.Populated);
        var enemy = game.Systems.First(x => x.Owner == Owner.Empire && Knowledge.See(game, x, Owner.Alliance).Owner == Owner.Empire);
        var neutral = game.Systems.First(x => x.Owner == Owner.Neutral && Knowledge.See(game, x, Owner.Alliance).Owner == Owner.Neutral);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Alliance);
        var ship = OwnShip(game, Owner.Alliance);
        var a = SideSet.Only(Owner.Alliance);
        StrikeReport R(StarSystem s, bool assault, Owner attacker, Owner defender, params (GameObject, SideSet, bool)[] destroyed) =>
            new(game.Day, s, assault, attacker, defender, a, a, [new(fleet, a, [ship], [])], [], [.. destroyed]);
        game.StrikeTrace!.AddRange([
            R(enemy, false, Owner.Alliance, Owner.Empire), R(enemy, false, Owner.Alliance, Owner.Empire, (ship, a, false)),
            R(enemy, false, Owner.Alliance, Owner.Empire, (ship, a, true)), R(neutral, false, Owner.Alliance, Owner.Neutral),
            R(own, true, Owner.Alliance, Owner.Empire), R(enemy, true, Owner.Alliance, Owner.Empire),
            R(own, true, Owner.Alliance, Owner.Neutral), R(neutral, true, Owner.Alliance, Owner.Neutral),
            R(own, true, Owner.Empire, Owner.Alliance) with { Fleets = [new(game.Fleets.First(f => f.Owner == Owner.Empire), a, [], [])] },
            R(enemy, false, Owner.Alliance, Owner.Empire) with { EndLive = SideSet.Only(Owner.Empire) }]);
        var m = log.Update().Where(x => x.Type == 0xF).ToList();
        Assert.Equal(9, m.Count);
        Assert.Equal([11162, 11163, 11164, 11162, 11160, 11160, 11160, 11160, 11161], m.Select(x => x.Picture));
        Assert.Equal([0x7099, 0x7099, 0x7099, 0x709A, 0x7091, 0x7092, 0x7093, 0x7094, 0x7092], m.Select(x => x.Body![0].Template));
        Assert.Equal([0x7098, 0x7098, 0x7098, 0x7098, 0x7090, 0x7090, 0x7090, 0x7090, 0x7090], m.Select(x => x.Title!.Template));
        Assert.All(m.Take(8), x => Assert.Equal((7, 0), (x.Kind, x.AdvisorCode)));
        Assert.Equal((3, 0x2F, Owner.Empire), (m[8].Kind, m[8].AdvisorCode, m[8].Attacker));
        Assert.Equal([ship], m[1].Strike![2]);
        Assert.Equal([ship], m[2].Strike![3]);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        var loaded = SaveGame.Load(new MemoryStream(stream.ToArray()), Definitions).Log.Where(x => x.Type == 0xF).ToList();
        Assert.All(loaded, x => Assert.Equal((3, Owner.None, (List<GameObject>[]?)null), (x.Kind, x.Attacker, x.Strike)));
        Assert.Equal(m.Select(x => (x.Assault, x.Defender, x.Picture)), loaded.Select(x => (x.Assault, x.Defender, x.Picture)));
    }

    // A destroy during an open strike records its views' live sides and Completed at the destroy (an unfinished unit -> 
    // L80, a completed one -> L88). An end pulse without the start leaves that field at 0 (neutral body 0x709A against a side).
    // A Coruscant assault still builds the strike message.
    [GameFact]
    public void StrikeReceiversAndPulses()
    {
        var (game, log) = New();
        var ship = OwnShip(game, Owner.Alliance);
        game.StrikeDestroys = [];
        Objects.Destroy(game, ship, 0xC);
        var (unit, live, completed) = Assert.Single(game.StrikeDestroys);
        Assert.Equal((ship, Combat.LiveSides(ship), true), (unit, live, completed));
        Assert.True(live.Alliance);
        game.StrikeDestroys = null;

        var enemy = game.Systems.First(x => x.Owner == Owner.Empire && Knowledge.See(game, x, Owner.Alliance).Owner == Owner.Empire);
        var cor = game.Systems.First(x => x.Type == Refs.Coruscant);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Alliance);
        var own = OwnShip(game, Owner.Alliance);
        var a = SideSet.Only(Owner.Alliance);
        game.StrikeTrace!.AddRange([
            new(game.Day, enemy, false, Owner.Alliance, Owner.Empire, SideSet.None, a, [new(fleet, a, [own], [])], [], []),
            new(game.Day, cor, true, Owner.Alliance, Owner.Empire, a, a, [new(fleet, a, [own], [])], [], [])]);
        var m = log.Update().Where(x => x.Type == 0xF).ToList();
        Assert.Equal([(0x709A, Owner.None), (0x7092, Owner.Empire)], m.Select(x => (x.Body![0].Template, x.Defender)));
        Assert.Equal(cor.Ref, m[1].Subject);
    }

    // Object picture id = string number + 0x1000. A major character's is a STRATEGY bitmap. A capital ship's is an
    // ENCYBMAP string naming an existing EData file. No object gives 0. An injured character's message carries its overlay.
    [GameFact]
    public void ObjectPictures()
    {
        var (game, log) = New();
        var leia = game.Characters.First(c => c.Ref == Refs.Leia);
        int id = Messages.Picture(game, leia);
        Assert.Equal(leia.Definition.Common.StringNumber + 0x1000, id);
        var strategy = OpenSWR.Data.PeResources.Load(GameDir.File("STRATEGY.DLL"));
        Assert.NotNull(strategy.Find(OpenSWR.Data.ResId.Bitmap, (ushort)id));
        int ship = Messages.Picture(game, OwnShip(game, Owner.Alliance));
        var names = OpenSWR.Data.PeResources.Load(GameDir.File("ENCYBMAP.DLL")).OfType(OpenSWR.Data.ResId.String)
            .SelectMany(b => OpenSWR.Data.StringTable.Decode(b.Name.Id, b.Data.Span)).ToDictionary(x => x.Item1, x => x.Item2);
        Assert.True(File.Exists(Path.Combine(GameDir.Path, "EData", names[ship])));
        Assert.Equal(0, Messages.Picture(game, null));

        var luke = game.Characters.First(c => c.Ref == Refs.Luke);
        luke.Injury = 20;
        var m = Assert.Single(log.Update(), x => x.Type == 0x1A);
        Assert.Equal((1050, Messages.Picture(game, luke)), (m.Picture, m.Overlay));
    }

    // ---- Economy and unrest messages ----

    static StarSystem OwnSystem(GameState game, Owner side) =>
        game.Systems.First(s => s.Owner == side && s.Populated && Knowledge.IsLive(s, side) && !s.Uprising);

    // Type 0xB: own Garrisoned system without Uprising. Surplus old != 0 -> 0 (from above, and from below when the uprising
    // ends at the same time). 0x7070 / 0x7071, picture 1008, sound 1106, category 1, code 2. None with Uprising.
    [GameFact]
    public void NearUprising()
    {
        var (game, log) = New();
        var s = OwnSystem(game, Owner.Alliance);
        (s.Garrisoned, s.GarrisonSurplus) = (true, 2);
        log.Update();
        s.GarrisonSurplus = 0;
        var m = Assert.Single(log.Update(), x => x.Type == 0xB);
        Assert.Equal((MessageCategory.Support, 3, 0x7070, 0x7071, 1008, 1106, 2, s.Ref), (m.Category, m.Kind, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Equal($"#{s.Definition.Common.StringId}", m.Title.Args[0].Text);

        (s.Uprising, s.GarrisonSurplus) = (true, -1);
        log.Update();
        s.GarrisonSurplus = 0; // still in uprising: dropped
        Assert.DoesNotContain(log.Update(), x => x.Type == 0xB);
        s.GarrisonSurplus = -1;
        log.Update();
        (s.Uprising, s.GarrisonSurplus) = (false, 0); // the uprising ends and the halved requirement equals the garrison
        Assert.Single(log.Update(), x => x.Type == 0xB);

        var (empire, elog) = New(Owner.Empire);
        var e = OwnSystem(empire, Owner.Empire);
        (e.Garrisoned, e.GarrisonSurplus) = (true, 1);
        elog.Update();
        e.GarrisonSurplus = 0;
        var em = Assert.Single(elog.Update(), x => x.Type == 0xB);
        Assert.Equal((1009, 1107), (em.Picture, em.Sound));
    }

    // Type 9: begin / end only. Own system 0x7060 / 0x7064 and 0x7061 / 0x7065, an observed enemy system 0x7062 / 0x7066 and
    // 0x7063 / 0x7067. Picture 1004, sound 1103 on a begin only, category 1, no code. A change between two nonzero values gives nothing.
    [GameFact]
    public void Smuggling()
    {
        var (game, log) = New();
        var s = OwnSystem(game, Owner.Alliance);
        s.SmugglingPercent = 0;
        log.Update();
        s.SmugglingPercent = 10;
        var begin = Assert.Single(log.Update(), x => x.Type == 9);
        Assert.Equal((MessageCategory.Support, 3, 0x7060, 0x7064, 1004, 1103, 0), (begin.Category, begin.Kind, begin.Title!.Template, begin.Body![0].Template, begin.Picture, begin.Sound, begin.AdvisorCode));
        s.SmugglingPercent = 20;
        Assert.DoesNotContain(log.Update(), x => x.Type == 9);
        s.SmugglingPercent = 0;
        var end = Assert.Single(log.Update(), x => x.Type == 9);
        Assert.Equal((0x7061, 0x7065, 0), (end.Title!.Template, end.Body![0].Template, end.Sound));

        var (empire, elog) = New(Owner.Empire);
        var x = empire.Systems.First(x => x.Owner == Owner.Alliance && x.Populated);
        var fleet = empire.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        x.Add(fleet);
        Knowledge.RecomputeSystem(empire, x);
        Assert.True(Knowledge.IsLive(x, Owner.Empire));
        x.SmugglingPercent = 0;
        elog.Update();
        x.SmugglingPercent = 15;
        var benefits = Assert.Single(elog.Update(), m => m.Type == 9);
        Assert.Equal((0x7062, 0x7066, 1103), (benefits.Title!.Template, benefits.Body![0].Template, benefits.Sound));
        x.SmugglingPercent = 0;
        var ends = Assert.Single(elog.Update(), m => m.Type == 9);
        Assert.Equal((0x7063, 0x7067, 0), (ends.Title!.Template, ends.Body![0].Template, ends.Sound));
        x.SmugglingPercent = -5; // a begin of the wrong sign for an Alliance-owned view: old 0 passes (old >= 0)
        Assert.Single(elog.Update(), m => m.Type == 9);
        x.SmugglingPercent = 0; // an end from below on an owner-1 view: dropped
        Assert.DoesNotContain(elog.Update(), m => m.Type == 9);
    }

    // Type 0x18: one per incident for the side whose view does not show the system as its own (0x7150 / 0x7151, picture 1000,
    // sound 1100, category 1, code 0x1C). The owner gets none.
    [GameFact]
    public void Informants()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log) = New(side);
            var s = OwnSystem(game, Owner.Alliance);
            Systems.Informant(game, s);
            var added = log.Update().Where(x => x.Type == 0x18).ToList();
            if (side == Owner.Alliance) { Assert.Empty(added); continue; }
            var m = Assert.Single(added);
            Assert.Equal((MessageCategory.Support, 3, 0x7150, 0x7151, 1000, 1100, 0x1C, s.Ref), (m.Category, m.Kind, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        }
    }

    // Type 0x20 for the system owner with a live view: resources up / down with the raw-material body rule (any raw change -> 
    // raw body; any increase -> "New Resources"). Disaster with and without destroyed facilities (lines in destroy order,
    // 0x7193 / 0x7192 / 0x7191, none when nothing changed). Category 4, code 0x1C. Not for the other side.
    [GameFact]
    public void DisasterAndResources()
    {
        var (game, log) = New();
        var s = OwnSystem(game, Owner.Alliance);
        var facilities = s.Contents.Where(x => Family.Of(x.Type) is >= 0x20 and < 0x30).Take(2).ToList();
        var a = SideSet.Only(Owner.Alliance);
        List<(GameObject, SideSet)> none = [];
        void Event(bool disaster, int e0, int e1, int r0, int r1, List<(GameObject, SideSet)>? lost = null, Owner owner = Owner.Alliance, SideSet? live = null) =>
            game.ResourceTrace!.Add(new(game.Day, s, disaster, owner, live ?? a, e0, e1, r0, r1, lost ?? none));
        Event(false, 5, 6, 3, 3);
        Event(false, 5, 4, 3, 3);
        Event(false, 5, 4, 3, 2); // energy loss clamping the raw material: depleted, raw body
        Event(false, 5, 5, 3, 4);
        Event(true, 5, 4, 3, 3, [.. facilities.Select(f => (f, a))]);
        Event(true, 5, 5, 3, 3, [(facilities[0], a)]);
        Event(true, 5, 4, 3, 3);
        Event(true, 5, 5, 3, 3);
        Event(false, 5, 6, 3, 3, owner: Owner.Empire, live: SideSet.Both);
        Event(false, 5, 6, 3, 3, live: SideSet.Only(Owner.Empire));
        var added = log.Update().Where(x => x.Type == 0x20).ToList();
        Assert.Equal([(0x7194, 0x7199), (0x7196, 0x7197), (0x7196, 0x7198), (0x7194, 0x719A), (0x7190, 0x7193), (0x7190, 0x7192), (0x7190, 0x7191)],
            added.Select(x => (x.Title!.Template, x.Body![0].Template)));
        Assert.All(added, x => Assert.Equal((MessageCategory.Resource, 3, 0x1C, s.Ref), (x.Category, x.Kind, x.AdvisorCode, x.Subject)));
        Assert.Equal((1002, 1101), (added[0].Picture, added[0].Sound));
        Assert.Equal((1003, 1102), (added[4].Picture, added[4].Sound));
        Assert.Equal(facilities.Select(f => $"#{Definitions.Resolve(f.Type)!.Common.StringId}"), added[4].Body!.Skip(1).Select(b => b.Args[0].Text));
        Assert.All(added[4].Body!.Skip(1), b => Assert.Equal(0x719B, b.Template));

        // the Sim records its galaxy events: a disaster at a system with resources
        game.ResourceTrace!.Clear();
        for (int i = 0; i < 200 && game.ResourceTrace.Count == 0; i++) Systems.DisasterEvent(game);
        var r = Assert.Single(game.ResourceTrace);
        Assert.True(r.Disaster && (r.EnergyNew < r.EnergyOld || r.RawNew < r.RawOld));
        Assert.Equal((r.System.Energy, r.System.RawMaterials, r.System.Owner), (r.EnergyNew, r.RawNew, r.Owner));
    }

    // Type 6: a capital ship repaired (0x7041 / 0x7043 with the fleet, category 0x80, sound 1118, code 8). A squadron aboard a
    // ship (0x7040 / 0x7042 with the ship's fleet, category 0x80) and in a system (category 0x40 Defense: the Defense button
    // lights). Sound 1111 / 1112, code 9. Picture = the object picture.
    [GameFact]
    public void Repaired()
    {
        var (game, log) = New();
        var ship = OwnShip(game, Owner.Alliance);
        var fighters = game.Created.Where(o => o.Owner == Owner.Alliance && Family.Of(o.Type) is >= 0x1C and < 0x20 && o.Existing).Take(2).ToList();
        var aboard = fighters[0];
        ship.Add(aboard);
        var home = (StarSystem)ship.Container!.Container!;
        home.Add(fighters[1]);
        foreach (var o in new[] { ship, aboard, fighters[1] }) o.Damaged = true;
        log.Update();
        foreach (var o in new[] { ship, aboard, fighters[1] }) o.Damaged = false;
        var added = log.Update().Where(x => x.Type == 6).ToList();
        string N(GameObject o) => $"#{Definitions.Resolve(o.Type)!.Common.StringId}";
        var cap = Assert.Single(added, x => x.Subject == ship.Ref);
        Assert.Equal((MessageCategory.Fleet, 3, 0x7041, 0x7043, 1118, 8, Messages.Picture(game, ship)), (cap.Category, cap.Kind, cap.Title!.Template, cap.Body![0].Template, cap.Sound, cap.AdvisorCode, cap.Picture));
        Assert.Equal(N(ship), cap.Title.Args[0].Text);
        var sq = Assert.Single(added, x => x.Subject == aboard.Ref);
        Assert.Equal((MessageCategory.Fleet, 0x7040, 0x7042, 1111, 9), (sq.Category, sq.Title!.Template, sq.Body![0].Template, sq.Sound, sq.AdvisorCode));
        Assert.Equal(sq.Body[0].Args[1].Text, cap.Body[0].Args[1].Text); // P = the ship's fleet for both
        Assert.EndsWith($" {ship.Container!.Id:X}", cap.Body[0].Args[1].Text);
        var def = Assert.Single(added, x => x.Subject == fighters[1].Ref);
        Assert.Equal(MessageCategory.Defense, def.Category);
        Assert.Equal($"#{home.Definition.Common.StringId}", def.Body![0].Args[1].Text);
        Assert.Contains(MessageCategory.Defense, Messages.LitCategories(game));
    }
}
