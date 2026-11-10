using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Target (0x214), owner change cancels the queue, destinations and Blockade, lost destinations.</summary>
public class ProductionTargetTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static uint ShipType => Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18).Common.Ref;
    static uint FighterType => Definitions.Fighters.First(r => r.Empire != 0).Common.Ref;

    /// <summary>Empire game; a shipyard manager with members in a system that is neither Coruscant nor an HQ, and a second own system.</summary>
    static (GameState Game, StarSystem Home, StarSystem Other, ManufacturingManager Yard) Setup()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 401859171), Definitions);
        game.Sides[Owner.Empire].MaintenanceCapacity += 100_000;
        bool Plain(StarSystem s) => s.Type != Refs.Coruscant && s != game.HeadquartersSystem && !s.Blockade;
        var home = game.SystemsOf(Owner.Empire).First(s => Plain(s) && Economy.Members(s.Managers[(int)ManufacturingKind.Ship]).Count > 0);
        var other = game.SystemsOf(Owner.Empire).First(s => s != home && Plain(s));
        return (game, home, other, home.Managers[(int)ManufacturingKind.Ship]);
    }

    /// <summary>Owner change through the control: no troops left, Alliance support 100.</summary>
    static void ToAlliance(GameState game, StarSystem s)
    {
        foreach (var troop in s.Contents.Where(o => Family.IsTroop(o.Type)).ToList()) troop.Remove();
        s.Populated = true;
        Systems.SetSupport(game, s, 100);
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal(Owner.Alliance, s.Owner);
    }

    static OrderResult Run(GameState game, Command c)
    {
        var r = Orders.Execute(game, c);
        while (game.Clock.DispatchOne(game)) { }
        return r;
    }

    static OrderResult Target(GameState game, ManufacturingManager m, GameObject d) =>
        Run(game, new Command(0x214, Owner.Empire) { Targets = [(m, 0)], ArgRef = d });

    static OrderResult Order(GameState game, ManufacturingManager m, uint product, int type = 0x211) =>
        Run(game, new Command(type, Owner.Empire) { Targets = [(m, 0)], Fields = [product, 1] });

    // Over a non-empty queue each queued unit is placed by the solver and moved at once (no travel, points kept).
    // The ship made the reserve placeholder real at order time. Emptied, it is destroyed (0x14).
    [GameFact]
    public void TargetMovesTheQueueWithoutTravel()
    {
        var (game, home, other, m) = Setup();
        Assert.True(Order(game, m, ShipType).Success);
        var unit = Assert.Single(m.Orders);
        var reserve = Assert.IsType<Fleet>(unit.Container);
        Assert.Same(home, reserve.Container);
        Assert.Equal((true, true, true, true, false),
            (reserve.Reserve, reserve.Real, reserve.Existing, reserve.Completed, reserve.Usable));
        m.Overflow = 7;
        var points = (m.CompletedPoints, m.Overflow);

        Assert.True(Target(game, m, other).Success);
        Assert.Same(other, m.TargetKey);
        Assert.Same(unit, Assert.Single(m.Orders));
        var into = Assert.IsType<Fleet>(unit.Container);
        Assert.Equal((other, true), ((GameObject?)into.Container, into.Reserve));
        Assert.False(unit.EnrouteActive || unit.HyperdriveActive);
        Assert.Equal(points, (m.CompletedPoints, m.Overflow));
        Assert.Equal((true, 0x14), (reserve.Destroyed, reserve.DestroyedReason));
    }

    // With a TargetKey of another owner only capital ships [0x14, 0x1C) may be ordered. Fighters give (1, 0x28).
    [GameFact]
    public void FighterRefusedWithForeignTargetKey()
    {
        var (game, _, _, m) = Setup();
        var neutral = game.SystemsOf(Owner.Neutral).First(s => !s.Blockade);
        Assert.True(Target(game, m, neutral).Success); // shipyard, empty queue: no owner test
        Assert.Equal(new OrderResult(1, 0x28), Order(game, m, FighterType));
        Assert.True(Order(game, m, ShipType).Success);
        Assert.Same(neutral, m.Orders.Single().Container!.Container); // the side's reserve placeholder there
    }

    // Validation: owner rule over queued non-capital-ship units. Construction: (1, 0x24), (1, 0x25). Null gives (1, 0x22).
    [GameFact]
    public void TargetValidation()
    {
        var (game, home, _, m) = Setup();
        var neutral = game.SystemsOf(Owner.Neutral).First(s => !s.Blockade);
        Assert.True(Order(game, m, FighterType).Success);
        Assert.Equal(new OrderResult(1, 0x28), Target(game, m, neutral));
        Assert.Equal(new OrderResult(1, 0x22), Target(game, m, null!));
        var construction = home.Managers[(int)ManufacturingKind.Construction];
        Assert.Equal(new OrderResult(1, 0x24), Target(game, construction, neutral));
        var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Empire && f.Usable);
        Assert.Equal(new OrderResult(1, 0x25), Target(game, construction, fleet));
    }

    // The producing system's owner change cancels the whole queue (0x16, no refund), wherever the units sit. The emptied
    // queue resets TargetKey to the own system. The emptied reserve fleet is destroyed (0x14) and the reserve is re-created.
    [GameFact]
    public void OwnerChangeCancelsTheQueue()
    {
        var (game, home, other, m) = Setup();
        Assert.True(Target(game, m, other).Success);
        Assert.True(Order(game, m, ShipType).Success);
        var unit = m.Orders.Single();
        var reserve = (Fleet)unit.Container!;
        int stock = game.Sides[Owner.Empire].RefinedStock;
        var log = new Messages(game, Owner.Empire, c => $"#{c.StringId}");
        var newOwner = new Messages(game, Owner.Alliance, c => $"#{c.StringId}");
        Assert.Equal((true, false, 1), (Knowledge.IsLive(m, Owner.Empire), Knowledge.IsLive(m, Owner.Alliance), Knowledge.QueueCount(game, m, Owner.Empire)));

        ToAlliance(game, home);
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal(Owner.Alliance, home.Owner);
        Assert.Equal((true, 0x16), (unit.Destroyed, unit.DestroyedReason));
        Assert.Empty(m.Orders);
        Assert.Same(home, m.TargetKey);
        Assert.Equal(stock, game.Sides[Owner.Empire].RefinedStock);
        Assert.Equal((true, 0x14), (reserve.Destroyed, reserve.DestroyedReason));
        Assert.Single(other.Contents.OfType<Fleet>(), f => f.Owner == Owner.Empire && f.Reserve && !f.Destroyed);
        // Manager views: live <=> owner = side. The old owner's frozen view takes the values at the 0x318 (after the cancel:
        // count 0, TargetKey reset). "Idle on {1}" only for the new owner (old owner: none).
        Assert.Equal((false, true), (Knowledge.IsLive(m, Owner.Empire), Knowledge.IsLive(m, Owner.Alliance)));
        Assert.Equal((home, 0), (Knowledge.TargetKey(m, Owner.Empire), Knowledge.QueueCount(game, m, Owner.Empire)));
        Assert.DoesNotContain(log.Update(), x => x.Type == 0x21);
        Assert.Equal(home.Ref, Assert.Single(newOwner.Update(), x => x.Type == 0x21).Subject);
    }

    // Issue #6: the manufacturing window lists another side's managers. Their rows read the frozen view, which a mode-10
    // update (espionage) fully refreshes (queue count and TargetKey). Commands stay own-only.
    [GameFact]
    public void EnemyManagersShowTheirLastKnownState()
    {
        var (game, home, other, m) = Setup();
        Assert.True(Target(game, m, other).Success);
        Assert.True(Order(game, m, ShipType).Success);
        Assert.Contains(m, ScreenViews.ManagerRows(home));
        Assert.Empty(ScreenViews.Managers(home, Owner.Alliance));
        Assert.Equal(0, Knowledge.QueueCount(game, m, Owner.Alliance));
        Knowledge.Update(game, home, 3, Owner.Alliance); // no manager range: nothing
        Assert.Equal(0, Knowledge.QueueCount(game, m, Owner.Alliance));
        Knowledge.Update(game, home, 10, Owner.Alliance);
        Assert.Equal((1, other), (Knowledge.QueueCount(game, m, Owner.Alliance), Knowledge.TargetKey(m, Owner.Alliance)));
        using (var first = new MemoryStream()) // the frozen manager view survives save -> load -> save
        {
            SaveGame.Save(game, first);
            var loaded = SaveGame.Load(new MemoryStream(first.ToArray()), Definitions);
            var lm = loaded.Systems.Single(s => s.Key == home.Key).Managers[(int)ManufacturingKind.Ship]!;
            Assert.Equal((1, other.Key), (Knowledge.QueueCount(loaded, lm, Owner.Alliance), Knowledge.TargetKey(lm, Owner.Alliance)!.Key));
            using var second = new MemoryStream();
            SaveGame.Save(loaded, second);
            Assert.Equal(first.ToArray(), second.ToArray());
        }
        Assert.False(Orders.Validate(game, new Command(0x214, Owner.Alliance) { Targets = [(m, 0)], ArgRef = home }).Success);
        Assert.False(Orders.Validate(game, new Command(0x211, Owner.Alliance) { Targets = [(m, 0)], Fields = [ShipType, 1] }).Success);
    }

    // After an owner change the new owner gets "Idle on {1}" when its frozen view count or the real count was not 0
    // before (here only a stale frozen count). An owner change to 3 gives nobody the message.
    [GameFact]
    public void IdleAfterAnyOwnerChange()
    {
        var (game, home, _, m) = Setup();
        m.Orders.Add(new GameObject(0x14000001, Owner.Empire, game.NextId()));
        Knowledge.Refresh(game, m, Owner.Alliance); // a frozen Alliance view seen with a queue (e.g. by espionage)
        m.Orders.Clear();
        Assert.Equal((1, 0), (Knowledge.QueueCount(game, m, Owner.Alliance), Knowledge.QueueCount(game, m, Owner.Empire)));
        var empire = new Messages(game, Owner.Empire, c => $"#{c.StringId}");
        var alliance = new Messages(game, Owner.Alliance, c => $"#{c.StringId}");
        ToAlliance(game, home);
        Assert.DoesNotContain(empire.Update(), x => x.Type == 0x21);
        Assert.Equal(home.Ref, Assert.Single(alliance.Update(), x => x.Type == 0x21).Subject);

        (game, home, _, m) = Setup();
        Assert.True(Order(game, m, ShipType).Success);
        empire = new Messages(game, Owner.Empire, c => $"#{c.StringId}");
        alliance = new Messages(game, Owner.Alliance, c => $"#{c.StringId}");
        foreach (var troop in home.Contents.Where(o => Family.IsTroop(o.Type)).ToList()) troop.Remove();
        home.Populated = false; // the Populated path: support control 3, no troops -> owner 3
        Systems.StoreControl(game, home, Systems.Control(game, home));
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal((Owner.Neutral, 0), (home.Owner, m.Orders.Count));
        Assert.DoesNotContain(empire.Update(), x => x.Type == 0x21);
        Assert.DoesNotContain(alliance.Update(), x => x.Type == 0x21);
    }

    // Destination owner change: the queued unit autoroutes, the builder's system first. It is moved without travel and
    // stays in the queue.
    [GameFact]
    public void UnfinishedUnitAutoroutesToTheBuilder()
    {
        var (game, home, other, m) = Setup();
        Assert.True(Target(game, m, other).Success);
        Assert.True(Order(game, m, FighterType).Success);
        var unit = m.Orders.Single();
        Assert.Same(other, unit.Container);

        ToAlliance(game, other);
        while (game.Clock.DispatchOne(game)) { }
        Assert.False(unit.Destroyed);
        Assert.Same(home, unit.Container);
        Assert.False(unit.EnrouteActive);
        Assert.Same(unit, Assert.Single(m.Orders));
    }

    // Placements under 0x210-0x214 into a blockaded system are refused. Facilities fail. Troops / fighters fall through to
    // own capital ships with room. Capital ships still go to the reserve placeholder.
    [GameFact]
    public void BlockadeRefusesPlacement()
    {
        var (game, home, _, _) = Setup();
        home.Blockade = true;
        Assert.Null(Fleets.Place(game, home, Refs.Mine, Owner.Empire, 0x210));
        Assert.True(((Fleet)Fleets.Place(game, home, ShipType, Owner.Empire, 0x211)!).Reserve);
        var coruscant = game.System(Refs.Coruscant); // has a Construction Yard
        var construction = coruscant.Managers[(int)ManufacturingKind.Construction];
        coruscant.Blockade = true;
        Assert.Equal(new OrderResult(0x90, 4), Order(game, construction, Refs.Mine, 0x210));
        coruscant.Blockade = false;
        Assert.True(Order(game, construction, Refs.Mine, 0x210).Success);

        var carrier = Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && Definitions.Resolve(r.Common.Ref)!.Get(SdField.FighterCapacity) > 0).Common.Ref;
        var fleet = home.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        fleet.Add(new GameObject(carrier, Owner.Empire, game.NextId()));
        var aboard = Fleets.Place(game, home, FighterType, Owner.Empire, 0x211)!;
        Assert.True(Family.IsCapitalShip(aboard.Type) && aboard.Container is Fleet { Usable: true } f && f.Container == home);
        home.Blockade = false;
        Assert.Same(home, Fleets.Place(game, home, FighterType, Owner.Empire, 0x211));
    }
    static uint Hull(int fighters, int troops) => Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18
        && Definitions.Resolve(r.Common.Ref)!.Get(SdField.FighterCapacity) == fighters && (troops < 0 || Definitions.Resolve(r.Common.Ref)!.Get(SdField.TroopCapacity) == troops)).Common.Ref;

    // Placement errors: no valid container gives the TargetKey's own error. The solver failing gives (1, 1).
    [GameFact]
    public void PlacementErrorsAreTheTargetKeys()
    {
        var (game, home, _, m) = Setup();
        var coruscant = game.System(Refs.Coruscant);
        var construction = coruscant.Managers[(int)ManufacturingKind.Construction];
        int facilities = coruscant.Contents.Count(o => o.Existing && Family.IsFacility(o.Type));
        coruscant.Energy = facilities;
        Assert.Equal(new OrderResult(0x90, 1), Order(game, construction, Refs.Mine, 0x210));
        coruscant.Energy = facilities + 5;
        coruscant.RawMaterials = coruscant.Contents.Count(o => o.Existing && Family.Of(o.Type) == 0x2C);
        Assert.Equal(new OrderResult(0x90, 2), Order(game, construction, Refs.Mine, 0x210));

        // A capital ship without hangar / barracks as TargetKey: (0x14, 1) for fighters, (0x14, 2) for troops. For its fleet,
        // the full Usable own ship's (0x14, 1) replaces the fleet's acceptance error (1, 0x27).
        var fleet = home.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        var ship = fleet.Add(new GameObject(Hull(0, 0), Owner.Empire, game.NextId()));
        Assert.True(Target(game, m, ship).Success);
        Assert.Equal(new OrderResult(0x14, 1), Order(game, m, FighterType));
        Fleets.Place(game, ship, Refs.EmpireArmy, Owner.Empire, 0x212, out var troops);
        Assert.Equal(new OrderResult(0x14, 2), troops);
        Assert.True(Target(game, m, fleet).Success);
        Assert.True(ship.Usable && fleet.Usable);
        Assert.Equal(new OrderResult(0x14, 1), Order(game, m, FighterType));
        Fleets.Place(game, ship, ShipType, Owner.Empire, 0x211, out var shipInShip);
        Assert.Equal(new OrderResult(1, 0x27), shipInShip);
        var troop = home.Contents.First(o => Family.IsTroop(o.Type));
        Assert.Null(Fleets.Place(game, troop, FighterType, Owner.Empire, 0x211, out var solver));
        Assert.Equal(new OrderResult(1, 1), solver);
    }

    // A placeholder is refused (1, 0x20) over an empty queue, but takes a queued ship (solver exemption).
    [GameFact]
    public void PlaceholderDestination()
    {
        var (game, _, other, m) = Setup();
        var placeholder = other.Contents.OfType<Fleet>().First(f => f.Owner == Owner.Empire && f.Reserve);
        Assert.False(placeholder.Completed);
        Assert.Equal(new OrderResult(1, 0x20), Target(game, m, placeholder));
        Assert.True(Order(game, m, ShipType).Success);
        Assert.True(Target(game, m, placeholder).Success);
        Assert.Same(placeholder, m.Orders.Single().Container);
    }

    // Owner change (0x302) or destruction (0x300) of the target resets the TargetKey of managers with an
    // empty queue to their own system. A manager with a queue keeps it.
    [GameFact]
    public void TargetKeyResetsWhenTheTargetIsLost()
    {
        var (game, home, other, m) = Setup();
        Assert.True(Target(game, m, other).Success);
        ToAlliance(game, other);
        Assert.Same(home, m.TargetKey);

        var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Empire && f.Usable);
        Assert.True(Target(game, m, fleet).Success);
        Objects.Destroy(game, fleet, 0x14);
        while (game.Clock.DispatchOne(game)) { }
        Assert.Same(home, m.TargetKey);

        var neutral = game.SystemsOf(Owner.Neutral).First(s => !s.Blockade);
        Assert.True(Target(game, m, neutral).Success);
        game.Sides[Owner.Empire].MaintenanceCapacity += 100_000; // recomputed by the side totals above
        Assert.True(Order(game, m, ShipType).Success);
        ToAlliance(game, neutral);
        Assert.Same(neutral, m.TargetKey);
    }

    // Manager Existing hook: a destroyed manager cancels its queue (0x16) and has owner 3.
    [GameFact]
    public void DestroyedManagerCancelsItsQueue()
    {
        var (game, _, other, m) = Setup();
        Assert.True(Target(game, m, other).Success);
        Assert.True(Order(game, m, ShipType).Success);
        var unit = m.Orders.Single();
        Objects.Destroy(game, m, 1);
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal((true, 0x16), (unit.Destroyed, unit.DestroyedReason));
        Assert.Empty(m.Orders);
        Assert.Equal(Owner.Neutral, m.Owner);
    }
    // Placement error priority: acceptance (destroyed, owner, then Blockade) before capacity. The Death Star limit (0x90, 3)
    // is a second-record capacity error, reported only without acceptance or first-record capacity errors.
    [GameFact]
    public void PlacementErrorPriority()
    {
        var (game, home, _, m) = Setup();
        OrderResult Error(GameObject at, uint type, int command, GameObject? unit = null)
        {
            Fleets.Place(game, at, type, Owner.Empire, command, out var e, unit: unit);
            return e;
        }
        var neutral = game.SystemsOf(Owner.Neutral).First(x => !x.Blockade);
        neutral.Blockade = true;
        Assert.Equal(new OrderResult(1, 0x24), Error(neutral, Refs.Mine, 0x210));
        neutral.Destroyed = true;
        Assert.Equal(new OrderResult(1, 0x22), Error(neutral, Refs.Mine, 0x210));
        home.Blockade = true;
        home.Energy = 0;
        Assert.Equal(new OrderResult(0x90, 4), Error(home, Refs.Mine, 0x210));
        home.Blockade = false;
        Assert.Equal(new OrderResult(0x90, 1), Error(home, Refs.Mine, 0x210));

        var deathStar = Definitions.CapitalShips.First(r => Family.Of(r.Common.Ref) is >= 0x18 and < 0x1C).Common.Ref;
        var here = home.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        Objects.Create(game, deathStar, Owner.Empire, here, completed: true);
        var other = game.Systems.First(x => x != home).Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        Objects.Create(game, deathStar, Owner.Empire, other, completed: true);
        while (game.Clock.DispatchOne(game)) { }
        Assert.Equal(new OrderResult(0x90, 3), Error(home, other.Type, 0x204, other));
        home.Blockade = true;
        Assert.Equal(new OrderResult(0x90, 3), Error(home, other.Type, 0x204, other)); // a fleet under 0x204 ignores Blockade
        Assert.Equal(new OrderResult(0x90, 4), Error(home, other.Type, 0x270, other));

        // 0x210-0x212: a TargetKey that no longer resolves (Destroyed; kept by a manager with a queue) -> (1, 0x22)
        home.Blockade = false;
        Assert.True(Target(game, m, here).Success);
        game.Sides[Owner.Empire].MaintenanceCapacity += 100_000; // recomputed since Setup
        Assert.Equal(OrderResult.Ok, Order(game, m, ShipType));
        Objects.Destroy(game, here, 0x14);
        while (game.Clock.DispatchOne(game)) { }
        Assert.NotEmpty(m.Orders);
        Assert.Same(here, m.TargetKey);
        game.Sides[Owner.Empire].MaintenanceCapacity += 100_000;
        Assert.Equal(new OrderResult(1, 0x22), Order(game, m, ShipType)); // missing-only resolve passes; the fleet's acceptance refuses
    }
    // Two sides: (1, 0x24) when the container's owner is not the first side (the command's). (1, 0x28) when it is not the
    // second side (the targets' owner). Reachable only when a command's side differs from its targets' owner, since the
    // generic order check refuses that first.
    [GameFact]
    public void SecondSideMismatch()
    {
        var (game, home, _, m) = Setup();
        var other = new Fleets.Sandbox { Second = Owner.Alliance };
        Fleets.Place(game, home, Refs.Mine, Owner.Empire, 0x210, out var system, other);
        Assert.Equal(new OrderResult(1, 0x28), system);
        var fleet = home.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        Fleets.Place(game, fleet, ShipType, Owner.Empire, 0x211, out var ships, new Fleets.Sandbox { Second = Owner.Alliance });
        Assert.Equal(new OrderResult(1, 0x28), ships);
        Fleets.Place(game, fleet, ShipType, Owner.Alliance, 0x211, out var first, new Fleets.Sandbox { Second = Owner.Empire });
        Assert.Equal(new OrderResult(1, 0x24), first);
        // The generic object check runs in the pre-check slot: a foreign manager gives (1, 0x14).
        Assert.Equal(new OrderResult(1, 0x14), Run(game, new Command(0x211, Owner.Alliance) { Targets = [(m, 0)], Fields = [ShipType, 1] }));
    }
    static OrderResult Move(GameState game, GameObject unit, GameObject destination, int type = 0x204) =>
        Run(game, new Command(type, Owner.Empire) { Targets = [(unit, 0)], ArgRef = destination });

    // Move errors: the destination's kept solver error. Check order: owner rule before the generic check.
    [GameFact]
    public void MoveErrors()
    {
        var (game, home, _, _) = Setup();
        var enemy = game.SystemsOf(Owner.Alliance).First(x => x.Populated && !x.Blockade);
        var fleet = home.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        var ship = fleet.Add(new GameObject(Hull(0, 0), Owner.Empire, game.NextId()));
        var fighter = home.Add(new GameObject(FighterType, Owner.Empire, game.NextId()));
        var troop = home.Add(new GameObject(Refs.EmpireArmy, Owner.Empire, game.NextId()));

        Assert.Equal(new OrderResult(0x14, 1), Move(game, fighter, ship));                 // own ship, no hangar
        Assert.Equal(new OrderResult(0x14, 2), Move(game, troop, ship));                   // own ship, no barracks
        Assert.True(ship.Usable && fleet.Usable);
        Assert.Equal(new OrderResult(0x14, 1), Move(game, fighter, fleet));                // own fleet, all ships full: the first ship's
        Assert.Equal(new OrderResult(1, 0x28), Move(game, troop, enemy));                  // owner rule
        troop.Completed = false;
        Assert.Equal(new OrderResult(1, 0x28), Move(game, troop, enemy));                  // before the generic check (1, 2)
        troop.Completed = true;
        var enemyFleet = enemy.Contents.OfType<Fleet>().FirstOrDefault(f => f.Owner == Owner.Alliance && f.Usable)
            ?? enemy.Add(new Fleet(Refs.Fleet, Owner.Alliance, game.NextId()) { Real = true });
        Assert.Equal(new OrderResult(1, 0x24), Move(game, ship, enemyFleet));              // ships exempt from the owner rule
        var other = game.Systems.First(x => x != home).Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        other.Add(new GameObject(ShipType, Owner.Empire, game.NextId()));
        Assert.Null(Fleets.Place(game, fleet, other.Type, Owner.Empire, 0x204, out var intoFleet, unit: other));
        Assert.Equal(new OrderResult(1, 0x27), intoFleet);                                 // a fleet into a fleet

        var blockaded = game.SystemsOf(Owner.Empire).First(x => x != home && !x.Contents.OfType<Fleet>().Any(f => f.Owner == Owner.Empire && f.Usable));
        blockaded.Blockade = true;
        Assert.Equal(new OrderResult(0x90, 4), Move(game, troop, blockaded));
        // An own Usable fleet there whose ships are all full: the first ship's capacity error replaces the system's (0x90, 4).
        // The placeholder's (1, 0x27) does not.
        var guard = blockaded.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true });
        guard.Add(new GameObject(Hull(0, 0), Owner.Empire, game.NextId()));
        guard.Add(new GameObject(Hull(0, 0), Owner.Empire, game.NextId()));
        Assert.True(guard.Usable);
        Assert.Equal(new OrderResult(0x14, 2), Move(game, troop, blockaded));
        Assert.Equal(new OrderResult(0x14, 1), Move(game, fighter, blockaded));
        guard.Usable = false;
        Assert.Equal(new OrderResult(0x90, 4), Move(game, troop, blockaded));               // the fleet is not Usable: ships not tried
        Assert.True(Move(game, fleet, enemy).Success);                                     // fleets under 0x204: owner / Blockade forgiven
    }
    // Event checks after class validation: production (manager generic check, a non-Suspended member (0xA0, 3)), moves
    // (Destroyed (1, 0x12), facilities (1, 0x17), Enroute (1, 0x11)). 0x205 accepts a Destroyed target.
    [GameFact]
    public void EventChecks()
    {
        var (game, home, _, m) = Setup();
        m.Completed = false;
        Assert.Equal(new OrderResult(1, 0x10), Order(game, m, ShipType));
        m.Completed = true;
        var members = Economy.Members(m);
        foreach (var f in members) f.Suspended = true;
        Assert.Equal(new OrderResult(0xA0, 3), Order(game, m, ShipType));
        foreach (var f in members) f.Suspended = false;

        var troop = home.Add(new GameObject(Refs.EmpireArmy, Owner.Empire, game.NextId()));
        var facility = home.Contents.First(o => Family.IsFacility(o.Type));
        var other = game.SystemsOf(Owner.Empire).First(x => x != home && !x.Blockade);
        Assert.Equal(new OrderResult(1, 0x17), Move(game, facility, home));
        troop.Enroute = true;
        Assert.Equal(new OrderResult(1, 0x11), Move(game, troop, other));
        (troop.Enroute, troop.Destroyed) = (false, true);
        Assert.Equal(new OrderResult(1, 0x12), Move(game, troop, other));
        Assert.True(Run(game, new Command(0x205, Owner.Empire) { Targets = [(troop, 0)] }).Success);
    }
    // Speed 0 to another system gives (1, 0x18). Headquarters leaving a blockade gives (0x90, 4) when withdraw < 31.
    // 0x201 sub-commands execute in order and stop at the first failure (earlier ones stay executed).
    [GameFact]
    public void MoveRulesAndPartialComposite()
    {
        var (game, home, other, _) = Setup();
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.System is { Owner: Owner.Empire } s && s != other);
        agent.MissionHyperdriveModifier = 0;
        Assert.Equal(new OrderResult(1, 0x18), Move(game, agent, other));
        agent.MissionHyperdriveModifier = null;

        var hqType = Definitions.Facilities.First(r => Family.Of(r.Common.Ref) == 0x20).Common.Ref;
        var hq = home.Add(new GameObject(hqType, Owner.Empire, game.NextId()));
        home.Blockade = true;
        for (int i = 0; i < 35; i++) home.Add(new GameObject(FighterType, Owner.Alliance, game.NextId())); // withdraw 100 - 2*35 = 30
        Assert.Equal(new OrderResult(0x90, 4), Move(game, hq, other)); // the headquarters is movable (generic check)
        home.Blockade = false;

        var facility = home.Contents.First(o => Family.IsFacility(o.Type) && Family.Of(o.Type) != 0x20);
        var composite = new Command(0x201, Owner.Empire) { Targets = [(agent, 0), (facility, 0)], ArgRef = other };
        int missions = game.Missions.Count;
        Assert.Equal(new OrderResult(1, 0x17), Run(game, composite)); // the 0x241 ran, the 0x204 of the facility is refused
        Assert.Equal(missions + 1, game.Missions.Count);
        Assert.Same(agent, game.Missions.Last().Team.Single());
    }
    // 0x203, 0x213 with a non-empty queue, and mission class validation (type, placement).
    [GameFact]
    public void FamilyChecks()
    {
        var (game, home, other, m) = Setup();
        var troop = home.Add(new GameObject(Refs.EmpireArmy, Owner.Empire, game.NextId()));
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.System is { Owner: Owner.Empire });
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        OrderResult Name(GameObject t) => Run(game, new Command(0x203, Owner.Empire) { Targets = [(t, 0)] });
        Assert.Equal(new OrderResult(1, 0x17), Name(troop));
        Assert.True(Name(agent).Success);
        Assert.True(Name(fleet).Success);

        Assert.Equal(new OrderResult(0xA0, 1), Run(game, new Command(0x213, Owner.Empire) { Targets = [(m, 0)] }));
        Assert.True(Order(game, m, ShipType).Success);
        Assert.True(Run(game, new Command(0x213, Owner.Empire) { Targets = [(m, 0)] }).Success);
        Assert.Empty(m.Orders);

        OrderResult Mission(uint type, GameObject target) =>
            Run(game, new Command(0x241, Owner.Empire) { Targets = [(agent, 0)], ArgRef = target, Fields = [0, 0, type] });
        Assert.Equal(new OrderResult(0x40, 0x92), Mission(0, other));
        Assert.Equal(new OrderResult(1, 1), Mission(0x41FFFFFF, other));                 // (0x40, 0x93) returns 0
        var buddy = game.Characters.First(c => c.Owner == Owner.Empire && c != agent && c.Usable);
        Assert.Equal(new OrderResult(1, 1), Mission(0x41000001, buddy));                  // a character is no container

        // Team and Decoys are placed together: every member must fit the one container
        var noBarracks = home.Add(new Fleet(Refs.Fleet, Owner.Empire, game.NextId()) { Real = true })
            .Add(new GameObject(Hull(0, 0), Owner.Empire, game.NextId()));
        Assert.Null(Fleets.PlaceGroup(game, noBarracks, [agent, troop], Owner.Empire, 0x240, out var group, new Fleets.Sandbox()));
        Assert.Equal(new OrderResult(0x14, 2), group);
        Assert.Same(noBarracks, Fleets.PlaceGroup(game, noBarracks, [agent, buddy], Owner.Empire, 0x240, out _, new Fleets.Sandbox()));
    }
    // A 0x241 to a fleet places the team on one of its capital ships. ObjectiveKey and TargetKey are that ship.
    // Group record errors: (1, 0x19) no category, (1, 0x12) Destroyed, (1, 0x1A) not Created.
    [GameFact]
    public void MissionPlacementSetsTheKeys()
    {
        var (game, _, _, _) = Setup();
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable && f.System is { Owner: Owner.Empire }
            && game.Characters.Any(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.System == f.System && c.Container is StarSystem));
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.System == fleet.System && c.Container is StarSystem);
        var ship = fleet.Contents.First(x => Family.IsCapitalShip(x.Type) && x.Usable);
        Assert.True(Run(game, new Command(0x241, Owner.Empire) { Targets = [(agent, 0)], ArgRef = fleet, Fields = [0, 0, 0x41000001] }).Success);
        var mission = game.Missions.Last();
        Assert.Equal((ship, ship), (mission.Objective, mission.TargetKey));

        var sandbox = new Fleets.Sandbox();
        var other = game.Characters.First(c => c.Owner == Owner.Empire && c != agent && c.Placed);
        Assert.Null(Fleets.PlaceGroup(game, fleet.System!, [other, fleet.System!], Owner.Empire, 0x240, out var none, sandbox));
        Assert.Equal(new OrderResult(1, 0x19), none);
        other.Created = false;
        Assert.Null(Fleets.PlaceGroup(game, fleet.System!, [other], Owner.Empire, 0x240, out var created, sandbox));
        Assert.Equal(new OrderResult(1, 0x1A), created);
        (other.Created, other.Destroyed) = (true, true);
        Assert.Null(Fleets.PlaceGroup(game, fleet.System!, [other], Owner.Empire, 0x240, out var destroyed, sandbox));
        Assert.Equal(new OrderResult(1, 0x12), destroyed);
    }
}
