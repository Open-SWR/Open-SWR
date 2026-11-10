using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Tests;

/// <summary>Acknowledged (0x205, the 0x320 self-acknowledge), ReadyForDelete, 0x31A deletion.</summary>
public class DeletionTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    static void Ack(GameState game, GameObject o, Owner side)
    {
        var invariants = new ViewInvariants(game);
        Assert.True(Orders.Execute(game, new Command(0x205, side) { Targets = [(o, 0)] }).Success);
        game.Clock.Flush(game);
        Assert.Empty(invariants.Check("ack"));
    }

    static bool Gone(GameState game, GameObject o) =>
        o.Deleted && !game.Created.Contains(o) && !game.Staging.Contents.Contains(o) && o.Container is null
        && !game.Views.ContainsKey((o, Owner.Alliance)) && !game.Views.ContainsKey((o, Owner.Empire));

    static Character Major(GameState game, Owner side) =>
        game.Characters.First(c => c.Owner == side && c.Placed && c.Usable && !c.OnMission && Family.Of(c.Type) < 0x38);

    /// <summary>
    /// A fleet with a fighter aboard one of its ships, seen live by the other side: an enemy fleet created in the same system makes
    /// the other side observe the system and everything in it.
    /// </summary>
    static (Fleet Fleet, Owner Own, Owner Other, List<GameObject> All) SeenFleet(GameState game)
    {
        bool Hangar(GameObject s) => Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == s.Type) is { FighterCapacity: > 0 };
        var fleet = game.Fleets.First(x => x.Usable && x.Contents.Any(Hangar) && x.Contents.All(s => !s.Contents.Any(y => y is Agent)));
        var (own, other) = (fleet.Owner, fleet.Owner == Owner.Alliance ? Owner.Empire : Owner.Alliance);
        var fighterType = game.Created.First(x => x.Owner == own && Family.Of(x.Type) is >= 0x1C and < 0x20).Type;
        Objects.Create(game, fighterType, own, fleet.Contents.First(Hangar), completed: true);
        var all = new List<GameObject> { fleet };
        for (int i = 0; i < all.Count; i++) all.AddRange(all[i].Contents);
        var model = game.Fleets.First(x => x.Owner == other && x.Usable);
        var enemy = Objects.Create(game, model.Type, other, fleet.Container!, completed: true);
        Objects.Create(game, model.Contents.First(x => Family.IsCapitalShip(x.Type)).Type, other, enemy, completed: true);
        game.Clock.Flush(game);
        Assert.True(enemy.Usable);
        Assert.All(all, x => Assert.True(Knowledge.IsLive(x, other)));
        return (fleet, own, other, all);
    }

    static void RunUntil(GameState game, Func<bool> done)
    {
        for (int day = game.Day + 1; !done(); day++)
        {
            Assert.True(day < 200, "condition not reached");
            game.Clock.RunUntilDay(game, day);
        }
    }

    /// <summary>A controller that records its view messages; with <c>autoAck</c> it acknowledges each 0x302, as the game controllers do.</summary>
    sealed class Recorder(Owner side, bool autoAck) : Controller(side)
    {
        public List<(int Type, GameObject Object, GameObject? A, GameObject? B)> Got { get; } = [];
        public override int Kind => 1;
        protected override void Start(GameState game) { }
        internal override void Receive(GameState game, int type, GameObject o, GameObject? a, GameObject? b)
        {
            Got.Add((type, o, a, b));
            if (autoAck) base.Receive(game, type, o, a, b);
        }
        public bool Heard(GameObject o, params int[] types) => Got.Any(m => m.Object == o && types.Contains(m.Type));
    }

    static (Recorder Alliance, Recorder Empire) Recorders(GameState game, bool autoAck)
    {
        var (a, e) = (new Recorder(Owner.Alliance, autoAck), new Recorder(Owner.Empire, autoAck));
        (game.Controllers[Owner.Alliance], game.Controllers[Owner.Empire]) = (a, e);
        return (a, e);
    }

    /// <summary>The acknowledged instances: the real object, the Alliance view, the Empire view.</summary>
    static (bool Real, bool Alliance, bool Empire) Acks(GameObject o) => (o.AckedReal, o.AckedViews.Alliance, o.AckedViews.Empire);
    static readonly (bool, bool, bool) RealOnly = (true, false, false), RealAndAlliance = (true, true, false);

    /// <summary>Steps the clock until the object is deleted; returns its acknowledged instances before the deleting tick.</summary>
    static (bool Real, bool Alliance, bool Empire) StepUntilDeleted(GameState game, GameObject o)
    {
        var invariants = new ViewInvariants(game); // invariant checks between ticks
        for (int i = 0; i < 12 * 200; i++)
        {
            var before = Acks(o);
            game.Clock.Step(game);
            Assert.Empty(invariants.Check($"tick {game.Clock.Tick}"));
            if (o.Deleted) return before;
        }
        throw new Xunit.Sdk.XunitException("not deleted");
    }

    static void StepUntilStaged(GameState game, GameObject o)
    {
        var invariants = new ViewInvariants(game);
        for (int i = 0; o.Container != game.Staging; i++)
        {
            Assert.True(i < 12 * 200, "not staged");
            game.Clock.Step(game);
            Assert.Empty(invariants.Check($"tick {game.Clock.Tick}"));
        }
    }

    /// <summary>The view was fresh until the final move, whose staging pass made it Destroyed silently.</summary>
    static void AssertFreshViewDestroyedSilently(GameState game, GameObject o, Owner side, Recorder r)
    {
        Assert.True(game.Views.TryGetValue((o, side), out var v));
        Assert.Equal((true, false, game.Staging), (v!.Destroyed, v.Created, v.Parent));
        Assert.False(r.Heard(o, 0x302, 0x300));
    }

    // A Move mission ends and the enemy never saw it, so its view is still fresh. The final move's staging pass makes
    // that view Destroyed without a message. The owner's single 0x205 deletes the mission (0x31A in the tick of that 0x205).
    [GameFact]
    public void MoveMissionTheEnemyNeverSawIsDeletedAfterItsOwnersAcknowledgement()
    {
        var game = Game();
        var (al, em) = Recorders(game, autoAck: true);
        var a = Major(game, Owner.Alliance);
        var m = Missions.Create(game, 0x41000001, Owner.Alliance, a.System!, [a], [], 0, mandatory: true, origin: a.System);
        Assert.False(game.Views.ContainsKey((m, Owner.Empire)));
        var before = StepUntilDeleted(game, m);
        Assert.Equal((RealOnly, RealAndAlliance), (before, Acks(m))); // the 0x320 self-acknowledge, then the owner's 0x205 in the deleting tick
        Assert.Single(al.Got, x => x.Object == m && x.Type == 0x302);
        Assert.False(em.Heard(m, 0x302, 0x300));
        Assert.True(Gone(game, m));
        Assert.DoesNotContain(m, game.Missions);
        // a timer slot kept for its queued event goes when that event drops
        for (int i = 0; i < 12 * 60 && game.Clock.TimedEvents.OfType<TimerEvent>().Any(e => e.Slot.Target == m.Ref); i++) game.Clock.Step(game);
        Assert.DoesNotContain(game.Timers.Keys, k => k.Target == m.Ref);
    }

    // The enemy view takes Destroyed at the final move itself, before any acknowledgement. A later 0x205 finds no key:
    // (1, 0x12), no serial.
    [GameFact]
    public void FreshViewTakesDestroyedAtTheFinalMove()
    {
        var game = Game();
        var (_, em) = Recorders(game, autoAck: false);
        var a = Major(game, Owner.Alliance);
        var m = Missions.Create(game, 0x41000001, Owner.Alliance, a.System!, [a], [], 0, mandatory: true, origin: a.System);
        StepUntilStaged(game, m);
        game.Clock.Flush(game);
        Assert.Equal(RealOnly, Acks(m));
        AssertFreshViewDestroyedSilently(game, m, Owner.Empire, em);
        Assert.False(m.Deleted);
        Ack(game, m, Owner.Alliance);
        Assert.True(Gone(game, m));
        uint serial = game.Clock.Serial;
        Assert.Equal(new OrderResult(1, 0x12), Orders.Execute(game, new Command(0x205, Owner.Alliance) { Targets = [(m, 0)] }));
        Assert.Equal(serial, game.Clock.Serial);
    }

    // A Return mission makes its final move only after its last member's 0x321 unlocks it. The enemy view is fresh,
    // so the owner's 0x205 alone deletes the mission.
    [GameFact]
    public void ReturnMissionTheEnemyNeverSawIsDeletedAfterItsOwnersAcknowledgement()
    {
        var game = Game();
        var (_, em) = Recorders(game, autoAck: true);
        var a = Major(game, Owner.Alliance);
        var home = game.Systems.First(s => s.Owner == Owner.Alliance && s != a.System);
        var m = Missions.Create(game, 0x42000002, Owner.Alliance, home, [a], [], 0, mandatory: true, origin: a.System);
        game.Clock.Flush(game);
        Assert.NotEqual(0, m.LockCount); // the member's RoleMissionKey lock
        var before = StepUntilDeleted(game, m);
        Assert.Equal((0, RealOnly), (m.LockCount, before));
        Assert.Equal(RealAndAlliance, Acks(m));
        Assert.False(em.Heard(m, 0x302, 0x300));
        Assert.True(Gone(game, m));
    }

    // An encounter refreshed the enemy view. At the final move that frozen view is dropped from the F8
    // view into staging (0x300 F8 -> 0xF2000003, then 0x302 to the enemy). The mission is deleted only after both
    // 0x205s, in either order.
    [GameTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncounteredMissionIsDeletedAfterBothAcknowledgements(bool enemyFirst)
    {
        var game = Game();
        var (_, em) = Recorders(game, autoAck: false);
        var a = Major(game, Owner.Alliance);
        var m = Missions.Create(game, 0x41000001, Owner.Alliance, a.System!, [a], [], 0, mandatory: true, origin: a.System);
        Knowledge.Refresh(game, m, Owner.Empire);
        StepUntilStaged(game, m);
        game.Clock.Step(game); // deliver the messages
        int drop = em.Got.FindIndex(x => x.Object == m && x.Type == 0x300 && x.A == game.Staging && x.B == game.MissionManager);
        Assert.True(drop >= 0);
        Assert.True(em.Got.FindIndex(x => x.Object == m && x.Type == 0x302) > drop);
        Assert.Equal((true, true), (Knowledge.ViewDestroyed(game, m, Owner.Empire), Knowledge.ViewCreated(game, m, Owner.Empire)));
        var (first, second) = enemyFirst ? (Owner.Empire, Owner.Alliance) : (Owner.Alliance, Owner.Empire);
        Ack(game, m, first);
        Assert.False(m.Deleted);
        Ack(game, m, second);
        Assert.True(Gone(game, m));
    }

    // A facility built after setup in an own system the enemy does not observe, then scrapped. Its enemy view is fresh,
    // takes Destroyed silently at the final move, and the owner's 0x205 deletes it.
    [GameFact]
    public void ScrappedFacilityTheEnemyNeverSawIsDeleted()
    {
        var game = Game();
        var (_, em) = Recorders(game, autoAck: false);
        var model = game.Facilities.First(x => x.Owner == Owner.Alliance && x.Usable && Family.Of(x.Type) is >= 0x22 and < 0x2C);
        var site = game.Systems.First(s => s.Owner == Owner.Alliance && !Knowledge.IsLive(s, Owner.Empire));
        var f = Objects.Create(game, model.Type, Owner.Alliance, site, completed: true);
        game.Clock.Flush(game);
        Assert.False(Knowledge.IsLive(f, Owner.Empire));
        Assert.False(game.Views.ContainsKey((f, Owner.Empire)));
        Assert.True(Orders.Execute(game, new Command(0x200, Owner.Alliance) { Targets = [(f, 0)] }).Success);
        StepUntilStaged(game, f);
        game.Clock.Flush(game);
        AssertFreshViewDestroyedSilently(game, f, Owner.Empire, em);
        Assert.False(f.Deleted);
        Ack(game, f, Owner.Alliance);
        Assert.True(Gone(game, f));
    }

    // A unit cancelled in its queue (0x213) in a system the enemy never observed. The queued unit is Created and its
    // owner's view is live, so the deletion waits for the owner's single 0x205. The enemy view is fresh and takes
    // Destroyed silently at the final move.
    [GameFact]
    public void CancelledQueuedUnitIsDeletedAfterItsOwnersAcknowledgement()
    {
        var game = Game();
        var (al, em) = Recorders(game, autoAck: false);
        var yard = game.Facilities.First(x => x.Owner == Owner.Alliance && x.Usable && Family.Of(x.Type) is >= 0x22 and < 0x2C).Type;
        var (site, m) = game.Systems.Where(s => s.Owner == Owner.Alliance && !Knowledge.IsLive(s, Owner.Empire))
            .Select(s => (s, s.Managers[(int)ManufacturingKind.Construction]))
            .First(x => x.Item2 is { } mm && Orders.Validate(game, new Command(0x210, Owner.Alliance) { Targets = [(mm, 0)], Fields = [yard, 1] }).Success);
        Assert.True(Orders.Execute(game, new Command(0x210, Owner.Alliance) { Targets = [(m!, 0)], Fields = [yard, 1] }).Success);
        game.Clock.Flush(game);
        var unit = m!.Orders.Single();
        Assert.True(unit.Created);
        Assert.True(Knowledge.IsLive(unit, Owner.Alliance));
        Assert.False(game.Views.ContainsKey((unit, Owner.Empire)));
        Assert.True(Orders.Execute(game, new Command(0x213, Owner.Alliance) { Targets = [(m, 0)] }).Success);
        StepUntilStaged(game, unit);
        game.Clock.Flush(game);
        AssertFreshViewDestroyedSilently(game, unit, Owner.Empire, em);
        Assert.True(al.Heard(unit, 0x302));
        Assert.Equal(RealOnly, Acks(unit));
        Assert.False(unit.Deleted);
        Ack(game, unit, Owner.Alliance);
        Assert.True(Gone(game, unit));
    }

    // A setup facility of a core system, scrapped while the enemy has no live view of the system. Setup step 24 refreshed
    // the enemy view into the frozen system view. No pass reaches that view, so the object stays in staging.
    [GameFact]
    public void ScrappedSetupFacilityInAFrozenSystemViewStays()
    {
        var game = Game();
        var (_, em) = Recorders(game, autoAck: false);
        var f = game.Facilities.First(x => x.Owner == Owner.Alliance && x.Usable && Family.Of(x.Type) != 0x20 && x.Container is StarSystem s
            && !Knowledge.IsLive(s, Owner.Empire) && !Knowledge.IsLive(x, Owner.Empire)
            && game.Views.TryGetValue((x, Owner.Empire), out var v) && v.Parent == s);
        Assert.True(Orders.Execute(game, new Command(0x200, Owner.Alliance) { Targets = [(f, 0)] }).Success);
        StepUntilStaged(game, f);
        game.Clock.Flush(game);
        Assert.Equal(false, Knowledge.ViewDestroyed(game, f, Owner.Empire));
        Assert.False(em.Heard(f, 0x302, 0x300));
        Ack(game, f, Owner.Alliance);
        Ack(game, f, Owner.Empire); // the enemy view is not Destroyed: acknowledges nothing
        Assert.Equal(RealAndAlliance, Acks(f));
        Assert.False(f.ReadyForDelete);
        Assert.Contains(f, game.Staging.Contents);
    }

    // A fleet both sides saw, destroyed with its ships and the fighters aboard. Each object is deleted after the later
    // of its two 0x205s. The fleet only needs its contents moved out of it, not deleted, before it can go.
    [GameFact]
    public void DestroyedFleetSeenByBothSidesIsDeletedAfterBothAcknowledgements()
    {
        var game = Game();
        var (fleet, own, other, all) = SeenFleet(game);
        var fighter = all.First(y => Family.Of(y.Type) is >= 0x1C and < 0x20);
        Objects.Destroy(game, fleet, 0xC);
        game.Clock.Flush(game);
        Assert.All(all, x => Assert.Same(game.Staging, x.Container));
        Assert.All(all, x => Assert.All(new[] { Owner.Alliance, Owner.Empire }, s => Assert.True(Knowledge.ViewCreated(game, x, s))));

        Ack(game, fleet, own);
        Ack(game, fighter, other);
        Assert.False(fleet.Deleted);
        Assert.False(fighter.Deleted);
        Ack(game, fleet, other);
        Assert.True(Gone(game, fleet));
        Assert.DoesNotContain(fleet, game.Fleets);
        Assert.False(fighter.Deleted);
        Ack(game, fighter, own);
        Assert.True(Gone(game, fighter));
    }

    // Characters are never deleted, whatever is acknowledged.
    [GameFact]
    public void KilledCharacterIsNeverDeleted()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        Objects.Destroy(game, c, 7);
        game.Clock.Flush(game);
        Assert.Same(game.Staging, c.Container);
        Ack(game, c, Owner.Alliance);
        Ack(game, c, Owner.Empire);
        Assert.False(c.ReadyForDelete);
        Assert.False(c.Deleted);
        Assert.Contains(c, game.Staging.Contents);
    }

    // A frozen character view still holds the mission's lock. The mission gets no final move, so it is never deleted,
    // whatever is acknowledged.
    [GameFact]
    public void MissionLockedByAFrozenViewIsNotDeleted()
    {
        var game = Game();
        var a = Major(game, Owner.Alliance);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        var m = Missions.Create(game, type, Owner.Alliance, game.System(Refs.Coruscant), [a], [], 0, mandatory: false);
        game.Clock.Flush(game);
        Knowledge.Refresh(game, a, Owner.Empire);
        game.Clock.Flush(game);
        Missions.Leave(game, m, a);
        Objects.Destroy(game, m, 0x14);
        game.Clock.Flush(game);
        Ack(game, m, Owner.Alliance);
        Ack(game, m, Owner.Empire);
        Assert.Equal((1, game.MissionManager, false), (m.LockCount, m.Container, m.Deleted));
    }

    // Saved between the 0x302s and their 0x205s: the acknowledged instances survive the save, the save round-trips
    // byte-equal, and the loaded game deletes the fleet when both controllers' 0x205s have run.
    [GameFact]
    public void SaveBetweenTheDestroyAndItsAcknowledgement()
    {
        var game = Game();
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int state = 77 ^ (int)side;
            var ui = new UiRandom(() => state);
            game.Controllers[side] = new ComputerController(side) { Rand = n => ui.Rand(n) };
        }
        game.Clock.RunUntilDay(game, 3);
        var (fleet, _, _, all) = SeenFleet(game);
        Objects.Destroy(game, fleet, 0xC);
        game.Clock.Step(game); // final moves, the 0x302s delivered; the 0x205s wait in the outboxes
        Assert.Same(game.Staging, fleet.Container);
        Assert.Equal(RealOnly, Acks(fleet));
        var first = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(first), Definitions);
        Assert.Equal(first, Save(loaded));
        foreach (var c in loaded.Controllers.Values.OfType<ComputerController>())
        {
            int state = 78 ^ (int)c.Side;
            var ui = new UiRandom(() => state);
            c.Model.Rand = n => ui.Rand(n); // keep off the process-wide generator other tests draw from
        }
        var lf = loaded.Fleets.Single(x => x.Ref == fleet.Ref);
        Assert.Equal(RealOnly, Acks(lf));
        for (int i = 0; i < 12 && !lf.Deleted; i++) loaded.Clock.Step(loaded);
        Assert.True(Gone(loaded, lf));
        Assert.All(all.Skip(1), x => Assert.DoesNotContain(loaded.Created, y => y.Ref == x.Ref)); // ships and fighter too
        var again = Save(loaded);
        Assert.Equal(again, Save(SaveGame.Load(new MemoryStream(again), Definitions)));
    }

    // openreb deviation (Inferred): a staged object nothing can reach again (the enemy never saw it, its owner
    // acknowledged it) stays in the running game but is left out of the save. Save, load, save stays byte-equal.
    // Since the fresh-view fix, the exact rule deletes such an object itself. This test builds the state of a game
    // staged before that fix (the fresh view never passed, so there is no entry), which only an older save can hold.
    [GameFact]
    public void SaveLeavesOutUnreachableStagedObjects()
    {
        var game = Game();
        var f = game.Facilities.First(x => x.Owner == Owner.Alliance && x.Usable && !Knowledge.IsLive(x, Owner.Empire)
            && !game.Views.ContainsKey((x, Owner.Empire)) && Family.Of(x.Type) is >= 0x22 and < 0x2C);
        Assert.True(Orders.Execute(game, new Command(0x200, Owner.Alliance) { Targets = [(f, 0)] }).Success);
        RunUntil(game, () => f.Container == game.Staging);
        game.Views.Remove((f, Owner.Empire)); // the fresh view as it was before the fix: never Destroyed
        var kept = Save(game);
        Assert.Contains(SaveGame.Load(new MemoryStream(kept), Definitions).Created, y => y.Ref == f.Ref); // the owner's view not acknowledged yet
        Ack(game, f, Owner.Alliance);
        var first = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(first), Definitions);
        Assert.Contains(f, game.Staging.Contents);
        Assert.DoesNotContain(loaded.Created, y => y.Ref == f.Ref);
        Assert.DoesNotContain(loaded.Staging.Contents, y => y.Ref == f.Ref);
        Assert.Equal(first, Save(loaded));
        using var plain = new MemoryStream();
        SaveGame.Save(game, plain, compact: false);
        plain.Position = 0;
        Assert.Contains(SaveGame.Load(plain, Definitions).Staging.Contents, y => y.Ref == f.Ref);
    }

    /// <summary>The save with uiRngState 0: the process-wide UI generator (written, ignored on load) moves with other tests.</summary>
    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return System.Text.Encoding.UTF8.GetBytes(System.Text.RegularExpressions.Regex.Replace(
            System.Text.Encoding.UTF8.GetString(stream.ToArray()), "\"uiRngState\":-?\\d+", "\"uiRngState\":0"));
    }
}
