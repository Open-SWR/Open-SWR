using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using static OpenSWR.Tests.TacticalTests;

namespace OpenSWR.Tests;

/// <summary>Hangars and world lists, setup dock and launch, Recover internals, withdraw, tractor beams, gravity wells, damage control, finish re-dock.</summary>
public class TacticalHangarTests
{
    static uint Tie => Definitions.Fighters.First(r => r.Empire != 0 && r.Hyperdrive == 0).Common.Ref;
    static uint Carrier(int capacity) =>
        Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && r.FighterCapacity >= capacity).Common.Ref;

    /// <summary>
    /// An Alliance ship against an Empire carrier with <paramref name="ties"/> TIE squadrons aboard (they cannot jump: docked); both
    /// sides human. <paramref name="extra"/> adds objects before the battle is built.
    /// </summary>
    static (GameState Game, TacticalBattle Battle, TacticalUnit Carrier, List<TacticalUnit> Ties) Carried(Script script, uint carrier, int ties, Action<GameState, StarSystem>? extra = null)
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [carrier]);
        var ship = b0.System.Contents.OfType<Fleet>().Single(f => f.Owner == Owner.Empire).Contents.Single();
        for (int i = 0; i < ties; i++) ship.Add(new GameObject(Tie, Owner.Empire, game.NextId()));
        extra?.Invoke(game, b0.System);
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, script.Draw);
        battle.Computer[1] = false;
        var c = battle.Units.Single(u => u.Object == ship);
        return (game, battle, c, [.. battle.Units.Where(u => u.Fighter && u.Side == 1 && u.Object.Container == ship)]);
    }

    /// <summary>
    /// A hangar takes setup docks while its count is below its capacity; the fighter beyond stays hidden in state 1 and
    /// pending (in side index 1's strength and the candidates), in no hangar list but with its carrier key, and is never launched. Docked fighters are not in
    /// the world list but are members of their fighter group.
    /// </summary>
    [GameFact]
    public void AFullHangarLeavesTheExtraFighterPending()
    {
        uint type = Carrier(1);
        int cap = Definitions.Resolve(type)!.Get(SdField.FighterCapacity);
        var (_, battle, carrier, ties) = Carried(new Script([], []), type, cap + 1);
        Assert.Equal(cap + 1, ties.Count);
        Assert.Equal(cap, carrier.U.Docked);
        var extra = ties[^1];
        Assert.Equal(1, extra.State);
        Assert.Same(carrier.U, extra.U.Carrier); // The carrier key is kept (F[4] / F[5])
        Assert.True(extra.U.Overflow);
        Assert.DoesNotContain(extra, battle.FindHangar(carrier.U)!.Launchable);
        Assert.Same(carrier.Object, battle.Result().Fighters[extra.Object].Carrier);
        Assert.All(ties, t => Assert.DoesNotContain(t, battle.Taking));
        Assert.All(ties, t => Assert.NotNull(t.Group));
        Assert.Contains(extra.U, battle.Sides[0].Pending);
        while (battle.Turn < 200) battle.Step();
        Assert.All(ties.SkipLast(1), t => Assert.Equal(2, t.State));
        Assert.Equal(1, extra.State);
        Assert.Contains(extra.U, battle.Sides[0].Pending);
        Assert.Equal(0, carrier.U.Docked);
    }

    /// <summary>
    /// Worked example: setup draw r = 0 -> first launch time 8; turn 5 inserts 0x19 (A) at stamp 8 and reschedules with r' = 2
    /// (15); A launches at turn 9 at the launch point (out of the pending list, into the world list and side list, the group's nav point
    /// reset by its leader); turn 12 inserts 0x19 (B), r'' = 3; B launches at turn 16; then the scheduler stops.
    /// </summary>
    [GameFact]
    public void CarrierLaunchesOneSquadronAtATime()
    {
        var script = new Script([], [0, 2, 3]);
        var (_, battle, carrier, ties) = Carried(script, Carrier(2), 2);
        var (a, b) = (ties[0], ties[1]);
        Assert.Equal([(0, 8, 0)], script.Log);
        for (int turn = 0; turn < 9; turn++)
        {
            battle.Step();
            if (turn == 5) Assert.Equal(1, battle.Queued(0x19, 8));
        }
        Assert.Equal((1, 1), (a.State, b.State));
        Assert.Equal([(0, 8, 0), (0, 8, 2)], script.Log);
        var point = carrier.Position - new V3(0, carrier.HalfHeight, 0);
        battle.Step(); // turn 9
        Assert.Equal((2, 1), (a.State, b.State));
        Assert.Equal(point, a.Position);
        Assert.Equal(point, a.Group!.NavPoint);
        Assert.Contains(a, battle.Taking);
        Assert.Contains(a.U, battle.Sides[1].Fighters);
        Assert.DoesNotContain(a.U, battle.Sides[0].Pending);
        Assert.Same(carrier.U, a.U.Carrier);
        Assert.Equal(1, carrier.U.Docked);
        while (battle.Turn < 16) battle.Step();
        Assert.Equal(1, b.State);
        Assert.Equal([(0, 8, 0), (0, 8, 2), (0, 8, 3)], script.Log);
        battle.Step(); // turn 16
        Assert.Equal(2, b.State);
        while (battle.Turn < 40) battle.Step();
        Assert.Equal(3, script.Log.Count);
        Assert.Equal(2, battle.Sounds.Count(n => n == 0x1B)); // one launch sound each
    }

    /// <summary>
    /// A carrier's death unassigns the fighters in flight to it (0x2C with no carrier: orphans that never dock) and kills its
    /// docked fighters; a withdrawing carrier takes its launchable fighters along (state 5, counts unchanged).
    /// </summary>
    [GameFact]
    public void CarrierDeathOrphansAndWithdrawTakesAlong()
    {
        uint type = Carrier(3);
        var (_, battle, carrier, ties) = Carried(new Script([], []), type, 2, (game, x) => x.Add(new GameObject(Tie, Owner.Empire, game.NextId())));
        var flying = battle.Units.Single(u => u.Fighter && u.Side == 1 && u.State == 2);
        Assert.True(battle.SetMission(flying.Group!, 2));
        battle.RunQueued();
        battle.Step();
        Assert.Equal(1, flying.Recovery);
        battle.RunQueued(); // 0x2C
        Assert.Equal((2, carrier.U), (flying.Recovery, flying.U.Carrier));
        // The fighter steers at the hangar's launch / docking point, not the carrier's centre
        Assert.Equal(carrier.Position - new V3(0, carrier.HalfHeight, 0) - flying.Position, battle.DesiredVector(flying));
        battle.Destroy(carrier);
        Assert.All(ties, t => Assert.Equal(11, t.State));
        battle.RunQueued(); // 0x2C (flying, none)
        Assert.Equal(2, flying.Recovery);
        Assert.Null(flying.U.Carrier);
        battle.Step();
        Assert.Equal(0, battle.Queued(0x1A));

        var (_, b2, c2, t2) = Carried(new Script([], []), type, 2);
        c2.U.State = 7;
        b2.Retreat(c2);
        Assert.Equal(9, c2.State);
        Assert.All(t2, t => Assert.Equal(5, t.State));
        Assert.Equal(2, c2.U.Docked);
        while (b2.Turn < 30) b2.Step();
        Assert.All(t2, t => Assert.Equal(5, t.State));
    }

    /// <summary>
    /// World withdraw and timeline: the old group's 0x0A, then per capital ship 0x37, 0x30, 0x0A(3) (one new task force each);
    /// the countdown 30000 ms reaches 0 in the side turn 30 turns after the withdraw (state 3); the ship that can jump goes 6 -> 7 -> 8
    /// -> 9; the one with SpeedCH 4 (speed 0, cannot jump) is scuttled after state 4 (0x2F); the side counts as withdrawn (S[2]).
    /// </summary>
    [GameFact]
    public void WithdrawTimelineAndScuttle()
    {
        int n = 0;
        var (_, battle, _, e) = Scene(new Script([], []), [0x14000042], [0x14000085, 0x14000085], o => { if (o.Owner == Owner.Empire && n++ == 1) o.SetCritical(3, 4); });
        var (jumper, stuck) = (e[0], e[1]);
        Assert.Same(jumper.Group, stuck.Group);
        var old = jumper.Group!;
        while (battle.Turn < 5) battle.Advance(1000);
        battle.Withdraw(1);
        Assert.Equal([0x0A, 0x37, 0x30, 0x0A, 0x37, 0x30, 0x0A], battle.QueuedKinds());
        Assert.Equal(2, battle.SideState(1));
        int three = -1, four = -1;
        while (!battle.Ending && battle.Turn < 300)
        {
            battle.Advance(1000);
            if (three < 0 && battle.SideState(1) == 3) three = battle.Turn - 1;
            if (four < 0 && battle.SideState(1) == 4) four = battle.Turn - 1;
            if (battle.Turn == 10)
            {
                Assert.NotSame(jumper.Group, stuck.Group);
                Assert.Equal((3, 3), (jumper.Group!.Mission, stuck.Group!.Mission));
                Assert.Empty(old.Units);
            }
        }
        Assert.Equal(4 + 30, three);
        Assert.True(four > three);
        Assert.Equal((9, 11), (jumper.State, stuck.State));
        Assert.True(battle.Ending);
        Assert.Equal(1, battle.S[2]);
    }

    static uint TractorShip => Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && r.TractorBeamPower > 0 && r.TractorBeamRange >= 10).Common.Ref;

    /// <summary>
    /// The fire decision's request (step 4, the group's current target) posts 0x28; four turns later the holder holds and the
    /// held unit's speed loses the holder's power P * hf - P * 0.25 * TractorCH (here to 0: it cannot jump). Out of range -> 0x29
    /// releases; one request at a time (pending); a TractorCH crit taking the power to 0 releases; a 0x28 that finds the target
    /// inactive leaves the holder pending for good (as read).
    /// </summary>
    [GameFact]
    public void TractorBeamsHoldDragAndRelease()
    {
        var (_, battle, a, e) = Scene(new Script([1, 0], []), [0x14000042], [TractorShip]);
        var (t, h) = (a[0], e[0]);
        Place(h, 0, 0);
        Place(t, 0, 4);
        battle.Order(h.Group!, TacticalBattle.ReplaceTargets, t);
        battle.RunQueued();
        battle.Step(); // turn 0: T draws 1 (no fire), H draws 0 -> aimed shot, then the tractor request
        Assert.Equal(1, battle.Queued(0x28, 3));
        Assert.True(h.TractorPending);
        while (battle.Turn < 5) battle.Step();
        Assert.Same(t, h.Holding);
        Assert.Equal([h], t.HeldBy);
        Assert.False(h.TractorPending);
        double v = 1.0 + t.U.BaseSpeed, power = h.TractorPower * h.U.Hf - h.TractorPower * 0.25 * h.U.TractorCh;
        Assert.Equal(Math.Max(0, v - (v * 0.25 * t.U.SpeedCh + power)), battle.Speed(t), 9);
        Assert.Equal(0, battle.Speed(t));
        Assert.False(battle.CanJump(t));

        battle.Tractor(h, t); // kept: active, powered, within range
        Assert.Equal(0, battle.Queued(0x29));
        Place(t, 0, h.TractorRange + 1);
        battle.Tractor(h, t);
        battle.Tractor(h, t); // pending: nothing more
        Assert.Equal(1, battle.Queued(0x29));
        battle.RunQueued();
        Assert.Null(h.Holding);
        Assert.Empty(t.HeldBy);
        Assert.True(battle.Speed(t) > 0);

        Place(t, 0, 4);
        battle.Tractor(h, t);
        battle.RunQueued();
        Assert.Same(t, h.Holding);
        h.U.TractorCh = 3;
        battle.CritMessage(h, 2); // TractorCH 4: power 0
        battle.RunQueued(); // the crit, then its 0x29 is queued
        Assert.Equal(1, battle.Queued(0x29));
        battle.RunQueued();
        Assert.Null(h.Holding);

        h.U.TractorCh = 0;
        battle.Tractor(h, t);
        t.U.State = 11; // gone before the 0x28 runs
        battle.RunQueued();
        Assert.Null(h.Holding);
        Assert.True(h.TractorPending);
        t.U.State = 2;
        battle.Tractor(h, t);
        Assert.Equal(0, battle.Queued(0x28));
        Assert.Equal([6, 7, 6, 7], battle.Sounds.Where(n => n is 6 or 7)); // lock on / off per holder-list change
    }

    /// <summary>
    /// A GravityWell capital ship in the other side's list blocks the retreat (computer withdraw and the Withdraw
    /// button); once it is destroyed the side may retreat again. No per-unit effect: the blocker's own side jumps as usual.
    /// </summary>
    [GameFact]
    public void GravityWellBlocksTheOtherSidesRetreat()
    {
        uint interdictor = Definitions.CapitalShips.First(r => r.Empire != 0 && Family.Of(r.Common.Ref) < 0x18 && r.GravityWellA4 != 0).Common.Ref;
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [interdictor]);
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var well = battle.Units.Single(u => u.Side == 1);
        bool allowed = battle.Sides[0].RetreatAllowed;
        Assert.False(battle.RetreatPossible(0));
        Assert.False(battle.WithdrawLocal());
        Assert.Equal(battle.Sides[1].RetreatAllowed && battle.CanJump(well), battle.RetreatPossible(1));
        battle.Destroy(well);
        Assert.Equal(allowed, battle.RetreatPossible(0));
    }

    /// <summary>
    /// Example: DamageControl in [7, 54], entering with ShieldCH 1 (crit total 1). Turn 0: timer 0 - 1000 <= 0 -> 50000, r = 7
    /// <= DC, j = 1 -> 0x23 posted, ShieldCH 0 at turn 4 (total 0; the timer stands still). A WeaponCH crit applied at turn 20 makes the
    /// total 1; the next check comes in turn 66 (r = 55 &gt; DC: nothing), the one after in turn 116 (the timer also runs in turns 1-3
    /// while the total is still 1: 47000 left at turn 4).
    /// </summary>
    [GameFact]
    public void DamageControlFixesOneCritPerPeriod()
    {
        uint type = Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) < 0x18 && r.DamageControl is >= 7 and <= 54).Common.Ref;
        var script = new Script([], [7, 55]);
        var (_, battle, a, _) = Scene(script, [type], [0x14000085], o => { if (o.Owner == Owner.Alliance) o.SetCritical(0, 1); });
        var t = a[0];
        Assert.Equal(1, t.CritTotal);
        battle.Step(); // turn 0
        Assert.Equal([(1, 100, 7)], script.Log);
        Assert.Equal(1, battle.Queued(0x23, 3));
        while (battle.Turn < 4) battle.Step();
        Assert.Equal(1, t.U.ShieldCh);
        battle.Step(); // turn 4
        Assert.Equal((0, 0), (t.U.ShieldCh, t.CritTotal));
        while (battle.Turn < 16) battle.Step();
        battle.CritMessage(t, 1); // stamp 19: runs at turn 20
        while (battle.Turn < 21) battle.Step();
        Assert.Equal((1, 1), (t.U.WeaponCh, t.CritTotal));
        while (battle.Turn < 66) battle.Step();
        Assert.Single(script.Log);
        battle.Step(); // turn 66
        Assert.Equal([(1, 100, 7), (1, 100, 55)], script.Log);
        Assert.Equal(0, battle.Queued(0x24));
        while (battle.Turn < 116) battle.Step();
        Assert.Equal(2, script.Log.Count);
        battle.Step(); // turn 116
        Assert.Equal(3, script.Log.Count);
    }

    /// <summary>
    /// Finish re-dock at the normal end: a fighter still docked on the second carrier is docked a second time, into the first
    /// ship of its side with room in the world list order (its old hangar still counts it), into that hangar's landed list. The
    /// records take the new carrier.
    /// </summary>
    [GameFact]
    public void FinishDocksTheDockedAgainFirstFit()
    {
        uint type = Carrier(1);
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [type, type]);
        var ships = b0.System.Contents.OfType<Fleet>().Single(f => f.Owner == Owner.Empire).Contents.ToList();
        ships[1].Add(new GameObject(Tie, Owner.Empire, game.NextId()));
        var battle = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        var (first, second) = (battle.Units.Single(u => u.Object == ships[0]), battle.Units.Single(u => u.Object == ships[1]));
        var tie = battle.Units.Single(u => u.Fighter);
        Assert.Same(second.U, tie.U.Carrier);
        battle.Destroy(battle.Units.Single(u => u.Side == 0));
        battle.Advance(1);
        Assert.True(battle.Ending);
        Assert.Equal((1, 1), (tie.State, first.U.Docked));
        Assert.Same(first.U, tie.U.Carrier);
        Assert.Equal(1, second.U.Docked); // counted twice (as read)
        Assert.Contains(tie, battle.FindHangar(first.U)!.Landed);
        Assert.Same(ships[0], battle.Result().Fighters[tie.Object].Carrier);
    }
    /// <summary>
    /// A fighter killed after its 0x1A was posted gives up its reservation and carrier (carrier field 0, carrier none), but 0x1A
    /// tests no fighter and docks it anyway: state 5 in the landed list, Docked + 1 (original quirk, kept). Finish re-docks it
    /// (F[6] = 1, F[5] = the ship with room); Get Immediate Result instead leaves F[5] = 0.
    /// </summary>
    [GameFact]
    public void DestroyedFighterIsRevivedByItsDock()
    {
        foreach (bool abort in new[] { false, true })
        {
            var (_, battle, carrier, _) = Carried(new Script([], []), Carrier(3), 0, (game, x) => x.Add(new GameObject(Tie, Owner.Empire, game.NextId())));
            var tie = battle.Units.Single(u => u.Fighter && u.Side == 1 && u.State == 2);
            Assert.True(battle.SetMission(tie.Group!, 2));
            battle.RunQueued();
            battle.Step();
            battle.RunQueued(); // 0x2C (tie, carrier)
            tie.Position = carrier.Position - new V3(0, carrier.HalfHeight, 0);
            battle.Step(); // within 2.0 of the launch point -> 0x1A
            Assert.Equal((3, 1), (tie.Recovery, battle.Queued(0x1A)));
            var hangar = battle.FindHangar(carrier.U)!;
            int places = hangar.Places, docked = carrier.U.Docked;
            battle.Destroy(tie);
            Assert.Equal((11, 0, places - 1), (tie.State, tie.Recovery, hangar.Places));
            Assert.Null(tie.U.Carrier);
            battle.RunQueued(); // 0x1A, then 0x2C (tie, none)
            Assert.Equal((5, docked + 1), (tie.State, carrier.U.Docked));
            Assert.Contains(tie, hangar.Landed);
            if (abort)
            {
                battle.Abort();
                Assert.Equal(new FighterResult(1, null), battle.Result().Fighters[tie.Object]);
                continue;
            }
            battle.Destroy(battle.Units.Single(u => u.Side == 0));
            battle.Advance(1);
            Assert.True(battle.Ending);
            Assert.Equal(new FighterResult(1, carrier.Object), battle.Result().Fighters[tie.Object]);
        }
    }

    /// <summary>
    /// Destroy takes a fighter out of its fighter group: the UI leave and the simulation leave
    /// the emptied group is deleted. A docked fighter killed with its carrier leaves its group too.
    /// </summary>
    [GameFact]
    public void DestroyedFighterLeavesItsGroup()
    {
        var (_, battle, carrier, docked) = Carried(new Script([], []), Carrier(3), 1, (game, x) => x.Add(new GameObject(Tie, Owner.Empire, game.NextId())));
        var tie = battle.Units.Single(u => u.Fighter && u.Side == 1 && u.State == 2);
        var d = docked.Single();
        var g = tie.Group!;
        Assert.Equal(new[] { d, tie }.OrderBy(u => u.Key), g.Members);
        Assert.Same(g, tie.UiGroup);
        battle.Destroy(tie);
        Assert.Null(tie.Group);
        Assert.Null(tie.UiGroup);
        Assert.DoesNotContain(tie, g.UiMembers);
        Assert.Equal([d], g.Members);
        Assert.Contains(g, battle.Groups);
        battle.Destroy(carrier); // the docked fighter dies with it and empties the group
        Assert.Equal(11, d.State);
        Assert.Null(d.Group);
        Assert.DoesNotContain(g, battle.Groups);
    }

    /// <summary>
    /// A destroyed capital ship leaves its task force like a fighter its group: the leader key goes to the next member, the
    /// HUD button stays; the last member's destroy deletes the group, frees button 1 and turns the task-force mode showing it idle.
    /// The dead ship leaves the UI member list at once.
    /// </summary>
    [GameFact]
    public void DestroyedShipsEmptyAndDeleteTheirTaskForce()
    {
        var (_, b, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000085]);
        foreach (var u in a) b.LeaveTaskForce(u);
        b.RunQueued();
        var g = Tf(b, a[0], a[1]);
        b.Ui.Bind(0, g);
        b.Ui.Command(2);
        b.Ui.Frame();
        Assert.Equal(1, b.Ui.Current.Id);
        Assert.Equal((a[0], a[0]), (g.SimLeader, g.UiLeader));
        b.Destroy(a[0]);
        Assert.Equal((11, 0.0), (a[0].State, (double)a[0].U.Hull));
        Assert.Contains(g, b.Groups);
        Assert.Equal([a[1]], g.Units);
        Assert.Equal((a[1], a[1]), (g.SimLeader, g.UiLeader));
        Assert.Same(g, b.Ui.TaskForces[0]);
        Assert.Equal([a[1]], g.UiMembers);
        Assert.Null(a[0].UiGroup);
        Assert.Equal(1, b.Ui.Current.Id);
        b.Destroy(a[1]);
        Assert.DoesNotContain(g, b.Groups);
        Assert.Null(b.Ui.TaskForces[0]);
        Assert.Equal(0, b.Ui.Current.Id);
    }

    /// <summary>An enemy task force emptied by destroys is deleted without touching the HUD.</summary>
    [GameFact]
    public void DestroyedEnemyTaskForceIsDeletedWithoutTheHud()
    {
        var (_, b, a, e) = Scene(new Script([], []), [0x14000042], [0x14000085, 0x14000085]);
        var g = e[0].Group!;
        Assert.Same(g, e[1].Group);
        var slots = b.Ui.TaskForces.ToArray();
        int mode = b.Ui.Current.Id;
        b.Destroy(e[0]);
        Assert.Contains(g, b.Groups);
        b.Destroy(e[1]);
        Assert.DoesNotContain(g, b.Groups);
        Assert.Equal(slots, b.Ui.TaskForces);
        Assert.Equal(mode, b.Ui.Current.Id);
    }

    /// <summary>A local fighter group's last squadron destroyed in flight deletes the group and frees its fighter button; the fighter mode goes idle; no Recover voice (that belongs to the dock).</summary>
    [GameFact]
    public void DestroyedLastSquadronFreesTheFighterButton()
    {
        var (game, b0, _, _) = Scene(new Script([], []), [0x14000042], [0x14000085]);
        b0.System.Add(new GameObject(0x1C000003, Owner.Alliance, game.NextId())); // X-wing squadron in the system
        var b = new TacticalBattle(game, b0.System, Owner.Alliance, new Script([], []).Draw);
        b.Computer[1] = false;
        var f = b.Units.Single(u => u.Fighter && u.Side == 0);
        Assert.Equal(2, f.State);
        var g = f.Group!;
        int c = Array.IndexOf(b.Ui.Fighters.ToArray(), g);
        Assert.True(c >= 0);
        b.Ui.Command(0xB + c);
        b.Ui.Frame();
        Assert.Equal(9 + c, b.Ui.Current.Id);
        b.Ui.Voices.Clear();
        b.Destroy(f);
        Assert.DoesNotContain(g, b.Groups);
        Assert.Null(b.Ui.Fighters[c]);
        Assert.Equal(0, b.Ui.Current.Id);
        Assert.Empty(b.Ui.Voices);
    }

    /// <summary>A destroy of a unit in state 8 (withdrawn, jumping) changes nothing: state and group stay.</summary>
    [GameFact]
    public void DestroyLeavesAJumpingUnitAlone()
    {
        var (_, b, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042], [0x14000085]);
        var g = a[0].Group!;
        var members = g.Units.ToList();
        a[0].U.State = 8;
        b.Destroy(a[0]);
        Assert.Equal(8, a[0].State);
        Assert.Same(g, a[0].Group);
        Assert.Equal(members, g.Units);
        Assert.Contains(g, b.Groups);
    }

    /// <summary>
    /// Example of keys 3, 5, 7 founded on 5: the local task force on button 1, its middle key dies:
    /// UI members 3, 7 with UI leader 3, simulation leader 3, navigator leader 7 (next key above 5). No 0x37 is posted.
    /// </summary>
    [GameFact]
    public void DestroyedFounderHandsTheThreeLeadersOver()
    {
        var (_, b, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042, 0x14000042], [0x14000085]);
        foreach (var u in a) b.LeaveTaskForce(u);
        b.RunQueued();
        var k = a.OrderBy(u => u.Key).ToList();
        var g = Tf(b, k[1], k[0], k[2]);
        b.Ui.Bind(0, g);
        Assert.Equal((k[1], k[1], k[1]), (g.SimLeader, g.UiLeader, g.Nav!.Leader));
        b.Destroy(k[1]);
        Assert.Empty(b.QueuedKinds());
        Assert.Equal([k[0], k[2]], g.UiMembers.OrderBy(u => u.Key));
        Assert.Equal((k[0], k[0], k[2]), (g.UiLeader, g.SimLeader, g.Nav!.Leader));
        Assert.Same(g, b.Ui.TaskForces[0]);
    }

    /// <summary>
    /// A unit whose 0x33 into another task force is still queued keeps that
    /// UiGroup when it dies; when the last simulation member of a group dies while a pending 0x33 member is in its UI list, the
    /// member keeps the stale key and resolves it to no group.
    /// </summary>
    [GameFact]
    public void PendingJoinKeepsItsUiGroup()
    {
        var (_, b, a, _) = Scene(new Script([], []), [0x14000042, 0x14000042, 0x14000042], [0x14000085]);
        foreach (var u in a) b.LeaveTaskForce(u);
        b.RunQueued();
        var g1 = Tf(b, a[0]);
        var g2 = Tf(b, a[1]);
        var g3 = Tf(b, a[2]);
        b.JoinTaskForce(g1, a[1]); // 0x37 / 0x33 still queued: simulation group g2, UI group g1
        Assert.Same(g2, a[1].Group);
        b.Destroy(a[1]);
        Assert.Same(g1, a[1].UiGroup);
        Assert.Contains(a[1], g1.UiMembers);
        Assert.DoesNotContain(g2, b.Groups);

        b.JoinTaskForce(g3, a[0]); // a[0]: UI group g3, still simulation member of g1
        b.Destroy(a[2]); // g3's only simulation member
        Assert.DoesNotContain(g3, b.Groups);
        Assert.Same(g3, a[0].UiGroupKey); // the stale group reference
        Assert.Null(a[0].UiGroup);
        Assert.Equal(-1, b.Ui.SlotOf(a[0].UiGroup));
    }
}
