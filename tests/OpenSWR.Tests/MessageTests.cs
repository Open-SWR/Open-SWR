using System.Text;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Message types (trigger gives category, kind, templates, arguments), 300-day expiry, lit buttons / tab exit, save round trip and older saves.</summary>
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

    static void Fact(GameState game, FactKind kind, GameObject o, long old, long now) => game.FactTrace!.Add(new(game.Day, kind, o.Ref, old, now));

    // Losses by DestroyedReason: MaintenanceScrap, Sabotage, Assassination and RanBlockade each give a message, as the asserts below show.
    // Battle and Scrapped losses give none. One line per unit.
    [GameFact]
    public void Losses()
    {
        var (game, log) = New();
        var ships = game.Created.Where(o => o.Owner == Owner.Alliance && Family.IsCapitalShip(o.Type) && o.Existing).Take(4).ToList();
        Objects.Destroy(game, ships[0], DestroyedReason.MaintenanceScrap);
        Objects.Destroy(game, ships[1], DestroyedReason.Battle);
        Objects.Destroy(game, ships[2], DestroyedReason.Scrapped);
        game.Clock.Flush(game);
        var m = Assert.Single(log.Update());
        Assert.Equal((MessageType.MaintenanceScrap, MessageCategory.Manufacturing, 3, TextId.MaintenanceShortfall), (m.Type, m.Category, m.Kind, m.Title!.Template));
        Assert.Equal([TextId.FollowingUnitsDestroyedDue, TextId.FollowingUnitsDestroyedDueLine], m.Body!.Select(b => b.Template));
        Assert.Equal($"#{Definitions.Resolve(ships[0].Type)!.Common.StringId}", m.Body![1].Args[0].Text);
        Assert.True(m.New);

        Objects.Destroy(game, ships[3], DestroyedReason.Sabotage);
        var minor = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && Family.Of(c.Type) >= Families.MinorCharacter);
        Objects.Destroy(game, minor, DestroyedReason.Assassination);
        game.Clock.Flush(game);
        var added = log.Update();
        Assert.Contains(added, x => x.Type == MessageType.Sabotage && x.Category == MessageCategory.Conflict && x.Title!.Template == TextId.SaboteursStrike && x.Body![^1].Template == TextId.FollowingUnitsWereDestroyedLine);
        var killed = Assert.Single(added, x => x.Type == MessageType.Assassination);
        Assert.Equal((MessageCategory.Mission, 8, TextId.KilledImperialAssassinsTitle, $"#{minor.Definition.Common.StringId}"), (killed.Category, killed.Kind, killed.Title!.Template, killed.Title.Args[0].Text));
        Assert.DoesNotContain(added, x => x.Type == MessageType.InjuredOrHealed); // the assassination has its own message
        Assert.True(added.Select(x => x.Id).SequenceEqual(added.Select(x => x.Id).Order()) && added[0].Id > m.Id);
    }

    // Advisor codes (AdvisorCode.Deployed, ShipsArrived, MothmaMessage) and the voice pick (list MajorVoice.MothmaArrive), only through the
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
            var mothma = game.Known.Mothma!;
            Fact(game, FactKind.Completed, ship, 0, 1);
            Fact(game, FactKind.HyperdriveChanged, ship.Container!, 1, 0);
            Fact(game, FactKind.HyperdriveChanged, mothma, 1, 0);
            var added = log.Update();
            Assert.Equal(AdvisorCode.Deployed, Assert.Single(added, x => x.Type == MessageType.Deployed).AdvisorCode);
            Assert.Equal(AdvisorCode.ShipsArrived, Assert.Single(added, x => x.Type == MessageType.Arrived).AdvisorCode);
            var arrives = Assert.Single(added, x => x.Type == MessageType.PersonnelArrived);
            Assert.Equal((AdvisorCode.MothmaMessage, picker ? 7 : 0), (arrives.AdvisorCode, arrives.Voice));
            Assert.Equal(picker ? [240] : [], lists);
        }
    }

    // Codes per builder: own uprising begins 2 / ends 1 (picture 1011 for an Alliance-owned end). A minor character arriving
    // gives MinorCharacterMessage with picture 1042 / sound 1148 (Alliance). Several agents led by Mothma give MothmaMessage.
    [GameFact]
    public void AdvisorCodesPerBuilder()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var own = game.Systems.Where(s => s.Owner == Owner.Alliance && s.Populated).ToList();
        own[1].Uprising = true;
        var log = new Messages(game, Owner.Alliance, Name);
        own[0].Uprising = true;
        own[1].Uprising = false;
        var minor = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && Family.Of(c.Type) is >= Families.MinorCharacter and < Families.SpecialForce);
        Fact(game, FactKind.HyperdriveChanged, minor, 1, 0);
        var added = log.Update();
        Assert.Equal((AdvisorCode.BadNews, 1010), added.Where(x => x.Type == MessageType.Uprising && x.Title!.Template == TextId.UprisingBegins).Select(x => (x.AdvisorCode, x.Picture)).Single());
        Assert.Equal((AdvisorCode.GoodNews, 1011), added.Where(x => x.Type == MessageType.Uprising && x.Title!.Template == TextId.UprisingEnds).Select(x => (x.AdvisorCode, x.Picture)).Single());
        var arrives = Assert.Single(added, x => x.Type == MessageType.PersonnelArrived);
        Assert.Equal((AdvisorCode.MinorCharacterMessage, 1042, 1148), (arrives.AdvisorCode, arrives.Picture, arrives.Sound));
        var mothma = game.Known.Mothma!;
        var other = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c != mothma && Missions.Location(c) == Missions.Location(mothma) && c.Ref > mothma.Ref);
        Fact(game, FactKind.HyperdriveChanged, mothma, 1, 0);
        Fact(game, FactKind.HyperdriveChanged, other, 1, 0);
        Assert.Equal(AdvisorCode.MothmaMessage, Assert.Single(log.Update(), x => x.Type == MessageType.PersonnelArrived).AdvisorCode);
    }

    // A capital ship ordered at a shipyard in system A for a fleet in system B travels alone. Its arrival gives one Arrived message
    // (ShipsArrive title and FollowingShipsArrived body, category Fleet, picture 1019 for the Empire, sound 1114) with the ship's FollowingUnitsArrivedLine.
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
        uint type = Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < Families.DeathStar).Common.Ref;
        foreach (var c in new[] { new Command(CommandType.SetProductionTarget, Owner.Empire) { Targets = [(m, GroupMode.Object)], ArgRef = fleet }, new Command(CommandType.ProduceShip, Owner.Empire) { Targets = [(m, GroupMode.Object)], Fields = [type, 1] } })
        {
            Assert.True(Orders.Execute(game, c).Success);
            while (game.Clock.DispatchOne(game)) { }
        }
        var ship = Assert.Single(m.Orders);
        var arrivals = new List<GameMessage>();
        for (int day = game.Day + 1; day < game.Day + 400 && arrivals.Count == 0; day++)
        {
            game.Clock.RunUntilDay(game, day);
            arrivals.AddRange(log.Update().Where(x => x.Type == MessageType.Arrived));
        }
        Assert.Same(fleet, ship.Container);
        var a = Assert.Single(arrivals);
        Assert.Equal((TextId.ShipsArrive, MessageCategory.Fleet, 1019, 1114, AdvisorCode.ShipsArrived), (a.Title!.Template, a.Category, a.Picture, a.Sound, a.AdvisorCode));
        Assert.Equal([TextId.FollowingShipsArrived, TextId.FollowingUnitsArrivedLine], a.Body!.Select(b => b.Template));
        Assert.Equal(Name(Definitions.Resolve(ship.Type)!.Common), a.Body![1].Args[0].Text);
    }

    // The Completed notification gives a Deployed message (one unit by class, several body lines). The HyperdriveChanged notification gives an Arrived message for a fleet, or a PersonnelArrived message for an agent.
    [GameFact]
    public void DeployedAndArrived()
    {
        var (game, log) = New();
        var ship = OwnShip(game, Owner.Alliance);
        var fleet = (Fleet)ship.Container!;
        var squadron = game.Created.First(o => o.Owner == Owner.Alliance && Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters && o.Existing);
        var troops = game.Created.Where(o => o.Owner == Owner.Alliance && Family.IsTroop(o.Type) && o.Existing && o.Container is StarSystem)
            .GroupBy(o => (o.Container, o.Type)).First(g => g.Count() >= 2).Take(2).ToList();
        var yards = ((StarSystem)troops[0].Container!).Managers;
        (ship.BuilderKey, squadron.BuilderKey) = (yards[0], yards[1]);
        foreach (var t in troops) t.BuilderKey = yards[2];
        Fact(game, FactKind.Completed, ship, 0, 1);
        Fact(game, FactKind.Completed, squadron, 0, 1);
        foreach (var t in troops) Fact(game, FactKind.Completed, t, 0, 1);
        var agent = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c.Container is StarSystem);
        Fact(game, FactKind.HyperdriveChanged, fleet, 1, 0);
        Fact(game, FactKind.HyperdriveChanged, agent, 1, 0);
        var added = log.Update();
        Assert.Contains(added, x => x.Type == MessageType.Deployed && x.Title!.Template == (Family.Of(ship.Type) < Families.DeathStar ? TextId.NewNamedUnitDeployedTitle : TextId.NewDeathStarDeployedTitle) && x.Subject == ship.Ref);
        Assert.Contains(added, x => x.Type == MessageType.Deployed && x.Title!.Template == TextId.SquadronDeployed && x.Body![0].Template == TextId.NewSquadronDeployed);
        var several = Assert.Single(added, x => x.Type == MessageType.Deployed && x.Title!.Template == TextId.DeployedTo);
        Assert.Equal([TextId.FollowingUnitsDeployed, TextId.FollowingUnitsDeployedLine, TextId.FollowingUnitsDeployedLine], several.Body!.Select(b => b.Template));
        Assert.All(added.Where(x => x.Type == MessageType.Deployed), x => Assert.Equal(MessageCategory.Manufacturing, x.Category));
        var arrival = Assert.Single(added, x => x.Type == MessageType.Arrived);
        Assert.Equal((MessageCategory.Fleet, TextId.FleetArrives, fleet.Ref), (arrival.Category, arrival.Title!.Template, arrival.Subject));
        Assert.Contains(added, x => x.Type == MessageType.PersonnelArrived && x.Category == MessageCategory.Mission && x.Title!.Template == TextId.Arrives);
        // Squadron deployed 1111 (A), other deployments none, fleet arrival 1114, units arriving 1115 (A).
        Assert.Equal(1111, added.Single(x => x.Type == MessageType.Deployed && x.Title!.Template == TextId.SquadronDeployed).Sound);
        Assert.All(added.Where(x => x.Type == MessageType.Deployed && x.Title!.Template != TextId.SquadronDeployed), x => Assert.Equal(0, x.Sound));
        Assert.Equal(1114, arrival.Sound);
        Fact(game, FactKind.HyperdriveChanged, troops[0], 1, 0);
        Assert.Equal(1115, Assert.Single(log.Update(), x => x.Type == MessageType.Arrived).Sound);
        // Per-manager approximation: one manager's deliveries of one day share one message, whatever their types.
        squadron.BuilderKey = yards[0];
        Fact(game, FactKind.Completed, ship, 0, 1);
        Fact(game, FactKind.Completed, squadron, 0, 1);
        var one = Assert.Single(log.Update(), x => x.Type == MessageType.Deployed);
        Assert.Equal((TextId.DeployedTo, 3), (one.Title!.Template, one.Body!.Count));
    }

    // System view changes: owner gives SystemJoins (JoinsEnemy text). Uprising gives Uprising (uprising texts). Own system blockaded gives Blockade
    // (UnderBlockade text). Category Support / Conflict. Only the side's own view.
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
        Assert.Equal([(MessageType.Uprising, MessageCategory.Support, TextId.UprisingBegins), (MessageType.Blockade, MessageCategory.Conflict, TextId.UnderBlockade)], added.Select(x => (x.Type, x.Category, x.Title!.Template)));
        Assert.Equal(Owner.Alliance, added[0].Body![0].Args[0].Side); // UprisingBegunControlledSystem text: the owner side's adjective
        own[0].Uprising = false;
        own[2].Owner = Owner.Empire; // the live view follows the real owner
        added = log.Update();
        Assert.Contains(added, x => x.Type == MessageType.Uprising && x.Title!.Template == TextId.UprisingEnds);
        var joins = Assert.Single(added, x => x.Type == MessageType.SystemJoins);
        Assert.Equal((TextId.JoinsEnemy, TextId.PopularDissentCausedWorld, Owner.Empire), (joins.Title!.Template, joins.Body![0].Template, joins.Body[0].Args[1].Side));
    }

    // Research raised gives ResearchComplete kind 4 with the encyclopedia key. Characters captured (CapturedOrEscaped), injured / healed (InjuredOrHealed). A manager
    // queue running empty gives QueueEmpty (texts by kind, picture of the yard, code 3, sound 1101).
    [GameFact]
    public void ResearchCharactersQueues()
    {
        var (game, log) = New();
        static uint YardFamily(ManufacturingManager x) => x.Kind switch { ManufacturingKind.Construction => Families.ConstructionYard, ManufacturingKind.Training => Families.TrainingFacility, _ => Families.Shipyard };
        var m = game.Systems.Where(s => s.Owner == Owner.Alliance).SelectMany(s => s.Managers)
            .First(x => x is not null && x.System.Contents.Any(y => y.Usable && Family.Of(y.Type) == YardFamily(x)));
        m.Orders.Add(new GameObject(SdRef.Make(Families.CapitalShip, 1), Owner.Alliance, game.NextId()));
        log = new Messages(game, Owner.Alliance, Name); // snapshot with the queue
        Research.SetPoints(game, game.Sides[Owner.Alliance], 0, 1000);
        var leia = game.Known.Leia!;
        (leia.Captured, leia.Owner) = (true, Owner.Empire);
        var luke = game.Known.Luke!;
        luke.Injury = 20;
        m.Orders.Clear();
        var added = log.Update();
        var research = added.Where(x => x.Type == MessageType.ResearchComplete).ToList();
        Assert.NotEmpty(research);
        Assert.All(research, x => Assert.True(x.Kind == 4 && x.Encyclopedia != 0 && x.Title!.Template == TextId.ResearchComplete && x.Category == MessageCategory.Manufacturing));
        Assert.Contains(added, x => x.Type == MessageType.CapturedOrEscaped && x.Title!.Template == TextId.WasCapturedByTitle && x.Body![0].Args[2].Side == Owner.Empire && x.Picture == 1048 && x.Overlay == 0);
        Assert.Contains(added, x => x.Type == MessageType.InjuredOrHealed && x.Title!.Template == TextId.Injured);
        var idle = Assert.Single(added, x => x.Type == MessageType.QueueEmpty);
        var yard = m.System.Contents.First(x => x.Usable && Family.Of(x.Type) == YardFamily(m));
        int title = m.Kind switch { ManufacturingKind.Construction => TextId.ConstructionYardIdle, ManufacturingKind.Training => TextId.TrainingFacilityIdle, _ => TextId.ShipyardIdle };
        Assert.Equal((title, m.System.Ref, AdvisorCode.Deployed, 1101, Messages.Picture(game, yard)), (idle.Title!.Template, idle.Subject, idle.AdvisorCode, idle.Sound, idle.Picture));
        luke.Injury = 0;
        Assert.Contains(log.Update(), x => x.Type == MessageType.InjuredOrHealed && x.Title!.Template == TextId.Healed);
    }

    // TaskStatus != 0 gives MissionReport. Diplomacy repeats gives kind 5 with DoYouWishMission and the mission key. CompletionStatus != 0 with
    // TaskStatus 0 gives MissionEnded (foiled text for a character's mission).
    [GameFact]
    public void MissionReports()
    {
        var (game, log) = New();
        var agent = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Existing && c.Container is StarSystem && !c.OnMission);
        var target = game.Systems.First(s => s.Owner == Owner.Neutral && s.Populated);
        var m = Missions.Create(game, Refs.DiplomacyMission, Owner.Alliance, target, [agent], [], 0, mandatory: false);
        m.TaskStatus = MissionTaskStatus.Succeeded;
        var report = Assert.Single(log.Update(), x => x.Type == MessageType.MissionReport);
        Assert.Equal((MessageCategory.Mission, 5, TextId.MyDiplomacyMissionIncreasedTitle, m.Ref), (report.Category, report.Kind, report.Title!.Template, report.Mission));
        Assert.Equal(TextId.MyDiplomacyMissionIncreased, report.Body![0].Template);
        Assert.Equal(TextId.DoYouWishMission, report.Body[^1].Template);
        m.TaskStatus = MissionTaskStatus.Pending;
        game.FactTrace!.Add(new(game.Day, FactKind.CompletionStatus, m.Ref, 0, 3));
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.MissionEnded); // no leader yet: dropped
        m.Leader = agent;
        bool major = Family.Of(agent.Type) < Families.MinorCharacter;
        game.FactTrace!.Add(new(game.Day, FactKind.CompletionStatus, m.Ref, 0, 3));
        var foiled = Assert.Single(log.Update(), x => x.Type == MessageType.MissionEnded);
        Assert.Equal((major ? TextId.MyMissionFoiledOpposingTitle : TextId.MissionFoiledOpposingForcesTitle, major ? TextId.MyMissionFoiledOpposing : TextId.MissionFoiledOpposingForces, 1046, 1152, target.Ref), (foiled.Title!.Template, foiled.Body![0].Template, foiled.Picture, foiled.Sound, foiled.Subject));
        Assert.Equal(TextId.PersonnelReturning, foiled.Body[^1].Template); // location line: the leader's system
        game.FactTrace!.Add(new(game.Day, FactKind.CompletionStatus, m.Ref, 3, 12));
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.MissionEnded); // MissionCompletion.NoResearchFacility (value 12): research missions only
    }

    // A failed recruitment by Vader whose members left before the end-of-tick build: the "My ..." report texts
    // with his name and his portrait as overlay (the original's handler runs at the notification).
    [GameFact]
    public void FailedRecruitmentAfterMembersLeft()
    {
        var (game, log) = New(Owner.Empire);
        var vader = game.Known.Vader!;
        var target = game.Systems.First(s => s.Owner == Owner.Empire && s.Populated);
        var m = Missions.Create(game, Refs.RecruitmentMission, Owner.Empire, target, [vader], [], 0, mandatory: false);
        m.Leader = vader;
        m.TaskStatus = MissionTaskStatus.Failed;
        (m.Leader, m.CompletionStatus) = (null, MissionCompletion.Done);
        m.Team.Clear();
        var report = Assert.Single(log.Update(), x => x.Type == MessageType.MissionReport);
        Assert.Equal((TextId.MyRecruitmentMissionSuccessfulTitle, TextId.MyRecruitmentMissionComplete, Messages.Picture(game, vader)), (report.Title!.Template, report.Body![0].Template, report.Overlay));
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
        Missions.Create(game, game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.Espionage).Common.Ref, Owner.Alliance, target, [agent], [], 0, mandatory: false);
        log.Update();
        Fact(game, FactKind.HyperdriveChanged, agent, 1, 0);
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.PersonnelArrived);
    }

    // openswr deviation (player request): a facility arriving at its destination shows its own picture instead of 1020 / 1021
    [GameFact]
    public void ArrivingFacilityShowsItsPicture()
    {
        var (game, log) = New();
        var facility = game.Created.First(o => o.Owner == Owner.Alliance && Family.IsFacility(o.Type) && o.Existing && o.Container is StarSystem);
        Fact(game, FactKind.HyperdriveChanged, facility, 1, 0);
        var arrival = Assert.Single(log.Update(), x => x.Type == MessageType.Arrived);
        Assert.Equal((TextId.UnitsArrive, Messages.Picture(game, facility)), (arrival.Title!.Template, arrival.Picture));
    }

    // An arriving special force shows its own picture, as a character shows its portrait.
    [GameFact]
    public void ArrivingSpecialForceShowsItsPicture()
    {
        var (game, log) = New();
        var force = game.Created.First(o => o.Owner == Owner.Alliance && Family.IsSpecialForce(o.Type) && o.Existing && o.Container is StarSystem);
        Fact(game, FactKind.HyperdriveChanged, force, 1, 0);
        var arrival = Assert.Single(log.Update(), x => x.Type == MessageType.PersonnelArrived);
        Assert.NotEqual(0, arrival.Overlay);
        Assert.Equal(Messages.Picture(game, force), arrival.Overlay);
    }

    // Branch (b) location line: a foiled mission whose members have already left at message time still names where they
    // were (PersonnelReturning), not "failed to return" (AllPersonnelAssignedMission).
    [GameFact]
    public void FoiledMissionAfterMembersLeftNamesTheirPlace()
    {
        var (game, log) = New(Owner.Empire);
        var vader = game.Known.Vader!;
        var target = game.Systems.First(s => s.Owner == Owner.Empire && s.Populated);
        var m = Missions.Create(game, Refs.RecruitmentMission, Owner.Empire, target, [vader], [], 0, mandatory: false);
        m.Leader = vader;
        m.CompletionStatus = MissionCompletion.FoiledEnRoute;
        m.Leader = null;
        m.Team.Clear();
        game.FactTrace!.Add(new(game.Day, FactKind.CompletionStatus, m.Ref, 0, 3));
        var report = Assert.Single(log.Update(), x => x.Type == MessageType.MissionEnded);
        Assert.NotNull(m.ReportPlace);
        Assert.Equal(TextId.PersonnelReturning, report.Body![1].Template);
    }

    // Branch (a): an enemy mission's view refreshed by the encounter step with CompletionStatus != 0 and TaskStatus 0 gives
    // EnemyMissionFoiled and the next text, picture 1073, sound 1148 (Alliance).
    [GameFact]
    public void EnemyMissionFoiled()
    {
        var (game, log) = New();
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Existing && c.Container is StarSystem && !c.OnMission);
        var target = game.Systems.First(s => s.Owner == Owner.Alliance && s.Populated);
        var m = Missions.Create(game, game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.Espionage).Common.Ref, Owner.Empire, target, [agent], [], 0, mandatory: false);
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.MissionEnded);
        m.CompletionStatus = MissionCompletion.FoiledEnRoute;
        Knowledge.Refresh(game, m, Owner.Alliance);
        var enemy = Assert.Single(log.Update(), x => x.Type == MessageType.MissionEnded);
        Assert.Equal((TextId.EnemyMissionFoiled, TextId.EnemyMissionFoiledForces, 1073, 1148, target.Ref), (enemy.Title!.Template, enemy.Body![0].Template, enemy.Picture, enemy.Sound, enemy.Subject));
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.MissionEnded); // unchanged view: no repeat
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
        Assert.Equal([4, 2], Messages.List(game, MessageCategory.None).Select(m => m.Id));      // All: newest first, no Advice, no kind 6
        Assert.Equal([3], Messages.List(game, MessageCategory.Advice).Select(m => m.Id));
        Assert.Equal([MessageCategory.Mission, MessageCategory.Advice], Messages.LitCategories(game).Order());
        game.Log[0].Read = true;                                            // displaying only marks read
        Assert.Equal([MessageCategory.Mission, MessageCategory.Advice], Messages.LitCategories(game).Order());
        Messages.ClearNew(game, MessageCategory.Advice);                    // tab exit
        Assert.Equal([MessageCategory.Mission], Messages.LitCategories(game));
        Messages.ClearNew(game, MessageCategory.None);                      // leaving the All tab: every message
        Assert.Empty(Messages.LitCategories(game));
    }

    // Saved with the game as an optional field. A log entry from an older save loads as a read message, with a category.
    [GameFact]
    public void SaveRoundTrip()
    {
        var (game, log) = New();
        Objects.Destroy(game, OwnShip(game, Owner.Alliance), DestroyedReason.MaintenanceScrap);
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
        node["log"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"day\":3,\"text\":\"fleet 7A9 arrived\",\"category\":\"Fleet\"}]");
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

    // Kind-6 events (not listed; subject = scene): system destroyed (Event category, PlanetDestroyed scene, text DeathStarDestroyedSystem with the old owner).
    // Death Star sabotaged (Mission category, DeathStarSabotaged scene, texts). Coruscant captured (Support category, CoruscantCaptured scene; the side's view shows the Alliance).
    // HQ destroyed (Event category, HeadquartersDestroyed scene).
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
        Combat.Bombard(game, s, CommandType.BombardDestroy);
        game.Clock.Flush(game);
        var m = Assert.Single(log.Update(), x => x.Kind == 6);
        Assert.Equal((MessageType.PlanetDestroyed, MessageCategory.Event, 111u, TextId.Destroyed, TextId.DeathStarDestroyedSystem, Owner.Alliance), (m.Type, m.Category, m.Subject, m.Title!.Template, m.Body![0].Template, m.Body[0].Args[0].Side));
        Assert.Equal("MDATA.101", Messages.EventMovies[m.Subject]);
        Assert.False(m.New); // queued only: no lit category

        Objects.Destroy(game, deathStar, DestroyedReason.Sabotage);
        game.Clock.Flush(game);
        m = Assert.Single(log.Update(), x => x.Kind == 6);
        Assert.Equal((MessageType.DeathStarSabotaged, MessageCategory.Mission, 114u, TextId.DeathStarSabotaged, TextId.RebelAllianceSabotagedDeath), (m.Type, m.Category, m.Subject, m.Title!.Template, m.Body![0].Template));
        Assert.DoesNotContain(game.Log, x => x.Type == MessageType.Sabotage);

        var cor = game.Known.Coruscant!;
        cor.Owner = Owner.Alliance;
        Victory.Recompute(game);
        game.VictoryState = game.VictoryState with { HqGone = true };
        var added = log.Update().Where(x => x.Kind == 6).ToList();
        Assert.Equal([(MessageType.CoruscantCaptured, MessageCategory.Support, 116u, TextId.CoruscantCaptured), (MessageType.HeadquartersDestroyed, MessageCategory.Event, 115u, TextId.AllianceHeadquartersDestroyed)], added.Select(x => (x.Type, x.Category, x.Subject, x.Title!.Template)));
        Assert.DoesNotContain(log.Update(), x => x.Kind == 6); // only on the rise
        Assert.DoesNotContain(Messages.List(game, MessageCategory.None), x => x.Kind == 6);
    }

    // A bombardment gives the attacker kind 7 (no code) and the defender kind 3 with code AdvisorCode.Bombardment. Title OrbitalBombardment, body OrbitalStrikeSystem
    // (attacker, defender, system). L78 = the attacking fleet's ships. Category Conflict.
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
        Combat.Bombard(game, s, CommandType.Bombard);
        game.Clock.Flush(game);
        var rec = Assert.Single(game.StrikeTrace);
        Assert.Equal((SideSet.Both, SideSet.Both, SideSet.Both), (rec.StartLive, rec.EndLive, rec.Fleets.Single().Live));
        var att = Assert.Single(empire.Update(), x => x.Type == MessageType.Strike);
        var def = Assert.Single(alliance.Update(), x => x.Type == MessageType.Strike);
        Assert.Equal((7, AdvisorCode.None, MessageCategory.Conflict, TextId.OrbitalBombardment, TextId.OrbitalStrikeSystem), (att.Kind, att.AdvisorCode, att.Category, att.Title!.Template, att.Body![0].Template));
        Assert.Equal((3, AdvisorCode.Bombardment, Owner.Alliance, Owner.Empire), (def.Kind, def.AdvisorCode, def.Defender, def.Attacker));
        Assert.Equal([Owner.Empire, Owner.Alliance], att.Body[0].Args.Take(2).Select(a => a.Side));
        Assert.Equal((s.Ref, false, 0), (att.Subject, att.Assault, att.Sound));
        Assert.Subset(att.Strike![1].ToHashSet(), fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).ToHashSet());
        Assert.Contains(att.Picture, new[] { 11162, 11163, 11164 });
    }

    // Pictures and bodies by the finish rules (hand-built records): bombardment 11162 / 11163 (L80) / 11164 (L88). Assault
    // taken / repulsed against a side (TroopsTakenControlSystem / TroopsDefendedAssault), neutral (TroopsSeizedControlNeutral / NeutralSystemRepulsedAttack), picture 11160 / 11161 by the attacker.
    // Defender code AdvisorCode.Assault. A strike whose system is not live at the end gives nothing. The save keeps only those two fields,
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
        var m = log.Update().Where(x => x.Type == MessageType.Strike).ToList();
        Assert.Equal(9, m.Count);
        Assert.Equal([11162, 11163, 11164, 11162, 11160, 11160, 11160, 11160, 11161], m.Select(x => x.Picture));
        Assert.Equal([TextId.OrbitalStrikeSystem, TextId.OrbitalStrikeSystem, TextId.OrbitalStrikeSystem, TextId.OrbitalStrikeNonAligned, TextId.TroopsTakenControlSystem, TextId.TroopsDefendedAssault, TextId.TroopsSeizedControlNeutral, TextId.NeutralSystemRepulsedAttack, TextId.TroopsDefendedAssault], m.Select(x => x.Body![0].Template));
        Assert.Equal([TextId.OrbitalBombardment, TextId.OrbitalBombardment, TextId.OrbitalBombardment, TextId.OrbitalBombardment, TextId.Assault, TextId.Assault, TextId.Assault, TextId.Assault, TextId.Assault], m.Select(x => x.Title!.Template));
        Assert.All(m.Take(8), x => Assert.Equal((7, AdvisorCode.None), (x.Kind, x.AdvisorCode)));
        Assert.Equal((3, AdvisorCode.Assault, Owner.Empire), (m[8].Kind, m[8].AdvisorCode, m[8].Attacker));
        Assert.Equal([ship], m[1].Strike![2]);
        Assert.Equal([ship], m[2].Strike![3]);

        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        var loaded = SaveGame.Load(new MemoryStream(stream.ToArray()), Definitions).Log.Where(x => x.Type == MessageType.Strike).ToList();
        Assert.All(loaded, x => Assert.Equal((3, Owner.None, (List<GameObject>[]?)null), (x.Kind, x.Attacker, x.Strike)));
        Assert.Equal(m.Select(x => (x.Assault, x.Defender, x.Picture)), loaded.Select(x => (x.Assault, x.Defender, x.Picture)));
    }

    // A destroy during an open strike records its views' live sides and Completed at the destroy (an unfinished unit gives
    // L80, a completed one gives L88). An end pulse without the start leaves that field at 0 (neutral body OrbitalStrikeNonAligned against a side).
    // A Coruscant assault still builds the strike message.
    [GameFact]
    public void StrikeReceiversAndPulses()
    {
        var (game, log) = New();
        var ship = OwnShip(game, Owner.Alliance);
        game.StrikeDestroys = [];
        Objects.Destroy(game, ship, DestroyedReason.Battle);
        var (unit, live, completed) = Assert.Single(game.StrikeDestroys);
        Assert.Equal((ship, Combat.LiveSides(ship), true), (unit, live, completed));
        Assert.True(live.Alliance);
        game.StrikeDestroys = null;

        var enemy = game.Systems.First(x => x.Owner == Owner.Empire && Knowledge.See(game, x, Owner.Alliance).Owner == Owner.Empire);
        var cor = game.Known.Coruscant!;
        var fleet = game.Fleets.First(f => f.Owner == Owner.Alliance);
        var own = OwnShip(game, Owner.Alliance);
        var a = SideSet.Only(Owner.Alliance);
        game.StrikeTrace!.AddRange([
            new(game.Day, enemy, false, Owner.Alliance, Owner.Empire, SideSet.None, a, [new(fleet, a, [own], [])], [], []),
            new(game.Day, cor, true, Owner.Alliance, Owner.Empire, a, a, [new(fleet, a, [own], [])], [], [])]);
        var m = log.Update().Where(x => x.Type == MessageType.Strike).ToList();
        Assert.Equal([(TextId.OrbitalStrikeNonAligned, Owner.None), (TextId.TroopsDefendedAssault, Owner.Empire)], m.Select(x => (x.Body![0].Template, x.Defender)));
        Assert.Equal(cor.Ref, m[1].Subject);
    }

    // Object picture id = string number + 4096. A major character's is a STRATEGY bitmap. A capital ship's is an
    // ENCYBMAP string naming an existing EData file. No object gives 0. An injured character's message carries its overlay.
    [GameFact]
    public void ObjectPictures()
    {
        var (game, log) = New();
        var leia = game.Known.Leia!;
        int id = Messages.Picture(game, leia);
        Assert.Equal(leia.Definition.Common.StringNumber + 4096, id);
        var strategy = OpenSWR.Data.PeResources.Load(GameDir.File("STRATEGY.DLL"));
        Assert.NotNull(strategy.Find(OpenSWR.Data.ResId.Bitmap, (ushort)id));
        int ship = Messages.Picture(game, OwnShip(game, Owner.Alliance));
        var names = OpenSWR.Data.PeResources.Load(GameDir.File("ENCYBMAP.DLL")).OfType(OpenSWR.Data.ResId.String)
            .SelectMany(b => OpenSWR.Data.StringTable.Decode(b.Name.Id, b.Data.Span)).ToDictionary(x => x.Item1, x => x.Item2);
        Assert.True(File.Exists(Path.Combine(GameDir.Path, "EData", names[ship])));
        Assert.Equal(0, Messages.Picture(game, null));

        var luke = game.Known.Luke!;
        luke.Injury = 20;
        var m = Assert.Single(log.Update(), x => x.Type == MessageType.InjuredOrHealed);
        Assert.Equal((1050, Messages.Picture(game, luke)), (m.Picture, m.Overlay));
    }

    // ---- Economy and unrest messages ----

    static StarSystem OwnSystem(GameState game, Owner side) =>
        game.Systems.First(s => s.Owner == side && s.Populated && Knowledge.IsLive(s, side) && !s.Uprising);

    // NearUprising: own Garrisoned system without Uprising. Surplus old != 0 gives 0 (from above, and from below when the uprising
    // ends at the same time). NearUprising and UnrestPushedCloseUprising texts, picture 1008, sound 1106, category Support, code 2. None with Uprising.
    [GameFact]
    public void NearUprising()
    {
        var (game, log) = New();
        var s = OwnSystem(game, Owner.Alliance);
        (s.Garrisoned, s.GarrisonSurplus) = (true, 2);
        log.Update();
        s.GarrisonSurplus = 0;
        var m = Assert.Single(log.Update(), x => x.Type == MessageType.NearUprising);
        Assert.Equal((MessageCategory.Support, 3, TextId.NearUprising, TextId.UnrestPushedCloseUprising, 1008, 1106, AdvisorCode.BadNews, s.Ref), (m.Category, m.Kind, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        Assert.Equal($"#{s.Definition.Common.StringId}", m.Title.Args[0].Text);

        (s.Uprising, s.GarrisonSurplus) = (true, -1);
        log.Update();
        s.GarrisonSurplus = 0; // still in uprising: dropped
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.NearUprising);
        s.GarrisonSurplus = -1;
        log.Update();
        (s.Uprising, s.GarrisonSurplus) = (false, 0); // the uprising ends and the halved requirement equals the garrison
        Assert.Single(log.Update(), x => x.Type == MessageType.NearUprising);

        var (empire, elog) = New(Owner.Empire);
        var e = OwnSystem(empire, Owner.Empire);
        (e.Garrisoned, e.GarrisonSurplus) = (true, 1);
        elog.Update();
        e.GarrisonSurplus = 0;
        var em = Assert.Single(elog.Update(), x => x.Type == MessageType.NearUprising);
        Assert.Equal((1009, 1107), (em.Picture, em.Sound));
    }

    // Smuggling: begin / end only. Own system: SmugglingLosses and its end texts. Observed enemy system: SmugglingBenefits and its end texts.
    // Picture 1004, sound 1103 on a begin only, category Support, no code. A change between two nonzero values gives nothing.
    [GameFact]
    public void Smuggling()
    {
        var (game, log) = New();
        var s = OwnSystem(game, Owner.Alliance);
        s.SmugglingPercent = 0;
        log.Update();
        s.SmugglingPercent = 10;
        var begin = Assert.Single(log.Update(), x => x.Type == MessageType.Smuggling);
        Assert.Equal((MessageCategory.Support, 3, TextId.SmugglingLosses, 28772, 1004, 1103, AdvisorCode.None), (begin.Category, begin.Kind, begin.Title!.Template, begin.Body![0].Template, begin.Picture, begin.Sound, begin.AdvisorCode));
        s.SmugglingPercent = 20;
        Assert.DoesNotContain(log.Update(), x => x.Type == MessageType.Smuggling);
        s.SmugglingPercent = 0;
        var end = Assert.Single(log.Update(), x => x.Type == MessageType.Smuggling);
        Assert.Equal((28769, 28773, 0), (end.Title!.Template, end.Body![0].Template, end.Sound));

        var (empire, elog) = New(Owner.Empire);
        var x = empire.Systems.First(x => x.Owner == Owner.Alliance && x.Populated);
        var fleet = empire.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        x.Add(fleet);
        Knowledge.RecomputeSystem(empire, x);
        Assert.True(Knowledge.IsLive(x, Owner.Empire));
        x.SmugglingPercent = 0;
        elog.Update();
        x.SmugglingPercent = 15;
        var benefits = Assert.Single(elog.Update(), m => m.Type == MessageType.Smuggling);
        Assert.Equal((TextId.SmugglingBenefits, 28774, 1103), (benefits.Title!.Template, benefits.Body![0].Template, benefits.Sound));
        x.SmugglingPercent = 0;
        var ends = Assert.Single(elog.Update(), m => m.Type == MessageType.Smuggling);
        Assert.Equal((28771, 28775, 0), (ends.Title!.Template, ends.Body![0].Template, ends.Sound));
        x.SmugglingPercent = -5; // a begin of the wrong sign for an Alliance-owned view: old 0 passes (old >= 0)
        Assert.Single(elog.Update(), m => m.Type == MessageType.Smuggling);
        x.SmugglingPercent = 0; // an end from below on an owner-1 view: dropped
        Assert.DoesNotContain(elog.Update(), m => m.Type == MessageType.Smuggling);
    }

    // Informants: one per incident for the side whose view does not show the system as its own (InformantsProvideInformation and InformantsProvidedUsInformation, picture 1000,
    // sound 1100, category Support, code AdvisorCode.ResourceEvent). The owner gets none.
    [GameFact]
    public void Informants()
    {
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, log) = New(side);
            var s = OwnSystem(game, Owner.Alliance);
            Systems.Informant(game, s);
            var added = log.Update().Where(x => x.Type == MessageType.Informants).ToList();
            if (side == Owner.Alliance) { Assert.Empty(added); continue; }
            var m = Assert.Single(added);
            Assert.Equal((MessageCategory.Support, 3, TextId.InformantsProvideInformation, TextId.InformantsProvidedUsInformation, 1000, 1100, AdvisorCode.ResourceEvent, s.Ref), (m.Category, m.Kind, m.Title!.Template, m.Body![0].Template, m.Picture, m.Sound, m.AdvisorCode, m.Subject));
        }
    }

    // ResourceEvent for the system owner with a live view: resources up / down with the raw-material body rule (any raw change gives
    // raw body; any increase gives "New Resources"). Disaster with and without destroyed facilities (lines in destroy order,
    // NaturalDisasterDestroyedFacilities, NaturalDisasterDestroyedUnits and NaturalDisasterDestroyedResources, none when nothing changed). Category Resource, code AdvisorCode.ResourceEvent. Not for the other side.
    [GameFact]
    public void DisasterAndResources()
    {
        var (game, log) = New();
        var s = OwnSystem(game, Owner.Alliance);
        var facilities = s.Contents.Where(x => Family.Of(x.Type) is >= Families.Headquarters and < Families.MajorCharacter).Take(2).ToList();
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
        var added = log.Update().Where(x => x.Type == MessageType.ResourceEvent).ToList();
        Assert.Equal([(TextId.NewResources, TextId.NewSourcesSystemEnergy), (TextId.ResourcesDepleted, TextId.OperationsReducedSystemEnergy), (TextId.ResourcesDepleted, TextId.OperationsReducedNumberRaw), (TextId.NewResources, TextId.NewSourcesRawMaterials), (TextId.NaturalDisaster, TextId.NaturalDisasterDestroyedFacilities), (TextId.NaturalDisaster, TextId.NaturalDisasterDestroyedUnits), (TextId.NaturalDisaster, TextId.NaturalDisasterDestroyedResources)],
            added.Select(x => (x.Title!.Template, x.Body![0].Template)));
        Assert.All(added, x => Assert.Equal((MessageCategory.Resource, 3, AdvisorCode.ResourceEvent, s.Ref), (x.Category, x.Kind, x.AdvisorCode, x.Subject)));
        Assert.Equal((1002, 1101), (added[0].Picture, added[0].Sound));
        Assert.Equal((1003, 1102), (added[4].Picture, added[4].Sound));
        Assert.Equal(facilities.Select(f => $"#{Definitions.Resolve(f.Type)!.Common.StringId}"), added[4].Body!.Skip(1).Select(b => b.Args[0].Text));
        Assert.All(added[4].Body!.Skip(1), b => Assert.Equal(TextId.NaturalDisasterLine, b.Template));

        // the Sim records its galaxy events: a disaster at a system with resources
        game.ResourceTrace!.Clear();
        for (int i = 0; i < 200 && game.ResourceTrace.Count == 0; i++) Systems.DisasterEvent(game);
        var r = Assert.Single(game.ResourceTrace);
        Assert.True(r.Disaster && (r.EnergyNew < r.EnergyOld || r.RawNew < r.RawOld));
        Assert.Equal((r.System.Energy, r.System.RawMaterials, r.System.Owner), (r.EnergyNew, r.RawNew, r.Owner));
    }

    // Repaired: a capital ship repaired (CapitalShipRepaired and AttachedRepairedAllBattle texts with the fleet, category Fleet, sound 1118, code AdvisorCode.ShipRepaired). A squadron aboard a
    // ship (SquadronFullStrength and SquadronAttachedReplacedAll texts with the ship's fleet, category Fleet) and in a system (category Defense: the Defense button
    // lights). Sound 1111 / 1112, code 9. Picture = the object picture.
    [GameFact]
    public void Repaired()
    {
        var (game, log) = New();
        var ship = OwnShip(game, Owner.Alliance);
        var fighters = game.Created.Where(o => o.Owner == Owner.Alliance && Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters && o.Existing).Take(2).ToList();
        var aboard = fighters[0];
        ship.Add(aboard);
        var home = (StarSystem)ship.Container!.Container!;
        home.Add(fighters[1]);
        foreach (var o in new[] { ship, aboard, fighters[1] }) o.Damaged = true;
        log.Update();
        foreach (var o in new[] { ship, aboard, fighters[1] }) o.Damaged = false;
        var added = log.Update().Where(x => x.Type == MessageType.Repaired).ToList();
        string N(GameObject o) => $"#{Definitions.Resolve(o.Type)!.Common.StringId}";
        var cap = Assert.Single(added, x => x.Subject == ship.Ref);
        Assert.Equal((MessageCategory.Fleet, 3, TextId.CapitalShipRepaired, TextId.AttachedRepairedAllBattle, 1118, AdvisorCode.ShipRepaired, Messages.Picture(game, ship)), (cap.Category, cap.Kind, cap.Title!.Template, cap.Body![0].Template, cap.Sound, cap.AdvisorCode, cap.Picture));
        Assert.Equal(N(ship), cap.Title.Args[0].Text);
        var sq = Assert.Single(added, x => x.Subject == aboard.Ref);
        Assert.Equal((MessageCategory.Fleet, TextId.SquadronFullStrength, TextId.SquadronAttachedReplacedAll, 1111, AdvisorCode.SquadronFull), (sq.Category, sq.Title!.Template, sq.Body![0].Template, sq.Sound, sq.AdvisorCode));
        Assert.Equal(sq.Body[0].Args[1].Text, cap.Body[0].Args[1].Text); // P = the ship's fleet for both
        Assert.EndsWith($" {ship.Container!.Id}", cap.Body[0].Args[1].Text);
        var def = Assert.Single(added, x => x.Subject == fighters[1].Ref);
        Assert.Equal(MessageCategory.Defense, def.Category);
        Assert.Equal($"#{home.Definition.Common.StringId}", def.Body![0].Args[1].Text);
        Assert.Contains(MessageCategory.Defense, Messages.LitCategories(game));
    }
}
