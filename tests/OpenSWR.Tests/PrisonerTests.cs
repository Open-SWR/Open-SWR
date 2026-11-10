using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>State / Captured polarity and hooks, capture paths, holding (0x30A), escape check, release paths, jails.</summary>
public class PrisonerTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    /// <summary>A placed, Usable major character of the side, off mission, with Combat 100 (injury check q = 1).</summary>
    static Character Major(GameState game, Owner side, int skip = 0)
    {
        var c = game.Characters.Where(c => c.Owner == side && c.Placed && c.Usable && !c.OnMission && Family.Of(c.Type) < 0x38).Skip(skip).First();
        c.Skills[SkillIndex.Combat] = 100;
        return c;
    }

    static List<(string Site, int N, int Result)> Trace(GameState game) => game.Random.Trace = [];

    /// <summary>Burns draws until the next chance(p) succeeds.</summary>
    static void NextChanceSucceeds(GameState game, int p)
    {
        while (new GameRandom(game.Random.State).Percent() >= p) game.Random.Percent();
    }

    // Captured = (State = 0). The state hook runs only on change.
    [GameFact]
    public void StatePolarityAndHooks()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        Assert.Equal((Prisoners.Free, false), (c.State, c.Captured));
        Assert.Equal(Owner.Alliance, Prisoners.NaturalSide(game, c));

        Prisoners.Release(game, c); // 4 -> 2: a change, still not captured
        Assert.Equal((Prisoners.Released, false, Owner.Alliance), (c.State, c.Captured, c.Owner));
        Assert.True(c.Autorouting);

        var draws = Trace(game);
        Prisoners.Capture(game, c, null);
        Assert.Equal((Prisoners.Captured, true, Owner.Empire, true), (c.State, c.Captured, c.Owner, c.CanEscape));
        Assert.Equal(DrawSite.Injury, draws[0].Site);
        Assert.Equal((DrawSite.Timer, 1001), (draws[^1].Site, draws[^1].N)); // 0x38A start roll(1000)
        var slot = game.Timers[(0x38A, c.Ref)];
        Assert.Equal((true, 100, 1000), (slot.Active, slot.Base, slot.Variance));

        Prisoners.Escape(game, c);
        Assert.Equal((Prisoners.Escaped, false, Owner.Alliance, false, false), (c.State, c.Captured, c.Owner, c.CanEscape, slot.Active));
        Objects.ClearAutorouting(game, c);
        Prisoners.Escape(game, c); // 1 -> 1: no hook
        Assert.False(c.Autorouting);
    }

    // A capture by a capital ship: injury check, State 0, Captor. The 0x30A hook moves the prisoner aboard and clears
    // Autorouting and the Captor. Freed aboard the enemy ship, it autoroutes: the 0x307 grouping sends it home with an
    // Autorouting mission 0x43.
    [GameFact]
    public void HeldAboardTheCapturingShip()
    {
        var game = Game();
        var coruscant = game.System(Refs.Coruscant);
        var ship = coruscant.Contents.OfType<Fleet>().Where(f => f.Owner == Owner.Empire && f.Usable)
            .SelectMany(f => f.Contents).First(x => Family.IsCapitalShip(x.Type));
        var c = Major(game, Owner.Alliance);
        coruscant.Add(c);
        game.Clock.Flush(game);

        Prisoners.Capture(game, c, ship);
        Assert.Equal((ship, true), (c.Captor, c.Autorouting));
        game.Clock.Flush(game);
        Assert.Equal((ship, null, false, true), (c.Container, c.Captor, c.Autorouting, c.Captured));

        int serial = (int)game.Clock.Serial;
        Prisoners.Escape(game, c);
        Assert.True(c.Autorouting);
        game.Clock.Flush(game);
        var m = c.Mission!; // the enemy ship, its fleet and Coruscant refuse the freed agent, so its destination is a system of its side
        Assert.Equal((Owner.Alliance, false), (c.Owner, c.Autorouting));
        Assert.Equal((coruscant, Owner.Alliance, 0, c), (m.Origin, m.Objective!.Owner, m.CompletionStatus, m.Leader));
        Assert.Equal((0x43000003u, Owner.Alliance, true, false, EncounterCodes.Standard), (m.Type, m.Owner, m.Mandatory, m.ImpliedTeam, m.Encounters));
        Assert.True(game.Clock.Serial > serial);
    }

    // A prisoner aboard a ship destroyed in battle is released and goes adrift. The 0x30E / 0x307 grouping re-parents it
    // into the ship's system (enemy-owned) and creates an Adrift mission 0x44 (Adrift encounter codes). Aboard a scrapped ship
    // (DestroyedReason 0x14) it only autoroutes and stays Captured.
    [GameFact]
    public void PrisonerAboardDestroyedShipGoesAdrift()
    {
        var game = Game();
        var coruscant = game.System(Refs.Coruscant);
        var ships = coruscant.Contents.OfType<Fleet>().Where(f => f.Owner == Owner.Empire && f.Usable)
            .SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type)).ToList();
        var c = Major(game, Owner.Alliance);
        ships[0].Add(c);
        Prisoners.Capture(game, c, ships[0]);
        game.Clock.Flush(game);
        Assert.Equal((ships[0], true), (c.Container, c.Captured));

        var fleet = ships[0].Container;
        Objects.Destroy(game, ships[0], 0xC);
        Assert.Equal((true, Prisoners.Released, Owner.Alliance), (c.Adrift, c.State, c.Owner));
        Assert.Equal(fleet, ships[0].Container); // stays in its fleet until its final move 0x372 (priority 0x998)
        game.Clock.Flush(game);
        Assert.Same(game.Staging, ships[0].Container); // 0x372 once the prisoner left
        var m = game.Missions[^1]; // origin = Coruscant: re-parented there; its encounters there are not checked here
        Assert.Equal((false, false), (c.Adrift, c.Autorouting));
        Assert.Equal((0x44000004u, Owner.Alliance, EncounterCodes.Adrift, coruscant, true), (m.Type, m.Owner, m.Encounters, m.Origin, m.Mandatory));

        var d = Major(game, Owner.Alliance);
        ships[1].Add(d);
        Prisoners.Capture(game, d, ships[1]);
        game.Clock.Flush(game);
        Objects.Destroy(game, ships[1], 0x14);
        Assert.Equal((false, true, true), (d.Adrift, d.Captured, d.Autorouting));
    }

    // An autorouting agent in its own side's territory just stops: flags cleared, no mission.
    [GameFact]
    public void AutoroutingInOwnTerritoryStops()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        Objects.SetParent(game, c, game.System(Refs.Yavin));
        game.Clock.Flush(game);
        int missions = game.Missions.Count;
        Objects.SetAutorouting(game, c);
        game.Clock.Flush(game);
        Assert.Equal((false, null, missions), (c.Autorouting, c.Mission, game.Missions.Count));
    }

    // A killed member leaves its mission through 0x30B (remove request), then the phase check (status 5).
    [GameFact]
    public void KilledMemberLeavesThroughRemoveRequest()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        var m = Missions.Create(game, type, Owner.Alliance, game.System(Refs.Coruscant), [c], [], 0, mandatory: false);
        c.ResignRequest = true;
        Objects.Destroy(game, c, 10);
        Assert.Equal((true, false, m), (c.RemoveRequest, c.ResignRequest, c.Mission));
        game.Clock.Flush(game);
        Assert.Equal((null, false, 5), (c.Mission, m.Team.Contains(c), m.CompletionStatus));
    }

    // Injury hook -> Healing in own territory. A parent change into enemy territory clears CanHeal ( -> Healing).
    // FastHeal = Force >= G 0xA00 from the Force hook, and a drop to 0 does not change it.
    [GameFact]
    public void HealTriggers()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        Objects.SetParent(game, c, game.System(Refs.Yavin));
        Prisoners.SetInjury(game, c, 3);
        Assert.Equal((true, true), (c.CanHeal, c.Healing));
        Assert.True(game.Timers[(0x388, c.Ref)].Active);
        Objects.SetParent(game, c, game.System(Refs.Coruscant));
        Assert.Equal((false, false, false), (c.CanHeal, c.Healing, game.Timers[(0x388, c.Ref)].Active));

        (c.ForceRank, c.ForceBonus) = (game.Params.General(0xA00), 0);
        Prisoners.ForceChanged(game, c);
        Assert.True(c.FastHeal);
        (c.ForceRank, c.ForceBonus) = (0, 0);
        Prisoners.ForceChanged(game, c);
        Assert.True(c.FastHeal);
    }

    // A minor killed by the capture's injury check (chance(100), roll(100), roll(29), death chance(25)) is not captured.
    [GameFact]
    public void KilledMinorIsNotCaptured()
    {
        var game = Game();
        var c = game.Characters.First(c => Family.Of(c.Type) is >= 0x38 and < 0x3C);
        c.Skills[SkillIndex.Combat] = 0; // q = 100
        while (true)
        {
            var clone = new GameRandom(game.Random.State);
            if (clone.Chance(100) && clone.Roll(100) + clone.Roll(29) >= 0 && clone.Chance(25)) break;
            game.Random.Percent();
        }
        Prisoners.Capture(game, c, null);
        Assert.Equal((true, 10, Prisoners.Free, false), (c.Destroyed, c.DestroyedReason, c.State, c.Captured));
    }

    // A special force's DestroyedReason is 9.
    [GameFact]
    public void CapturedSpecialForceIsDestroyed()
    {
        var game = Game();
        var force = (Agent)Objects.Create(game, Definitions.SpecialForces[0].Common.Ref, Owner.Alliance, game.System(Refs.Yavin), completed: true);
        Prisoners.Capture(game, force, null);
        Assert.Equal(9, force.DestroyedReason);
    }

    // Abduction success captures the target for the abductor. The 0x30A hook puts it with the abductor (joining the
    // abduction's Captured list). The abduction then ends with status 6 (its target is Captured).
    [GameFact]
    public void AbductionCapturesTheTarget()
    {
        var game = Game();
        var vader = (Character)game.Characters.First(c => c.Ref == Refs.Vader);
        vader.Skills[SkillIndex.Combat] = 1000;
        var target = Major(game, Owner.Alliance);
        target.Skills[SkillIndex.Combat] = 0;
        vader.System!.Add(target);
        game.Clock.Flush(game);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x62).Common.Ref;
        var m = Missions.Create(game, type, Owner.Empire, target, [vader], [], 0, mandatory: false);
        NextChanceSucceeds(game, Missions.Probability(game, m, vader)!.Value);

        Missions.Execute(game, m);
        Assert.Equal((3, Prisoners.Captured, vader, Owner.Empire), (m.TaskStatus, target.State, target.Captor, target.Owner));
        game.Clock.Flush(game);
        Assert.Equal((vader.Container, null, true), (target.Container, target.Captor, target.Captured));
        Assert.Equal(6, m.CompletionStatus);
    }

    // Rescue success: State 3, owner back to the natural side.
    [GameFact]
    public void RescueFreesTheTarget()
    {
        var game = Game();
        var coruscant = game.System(Refs.Coruscant);
        var prisoner = Major(game, Owner.Alliance);
        var rescuer = Major(game, Owner.Alliance, skip: 1);
        rescuer.Skills[SkillIndex.Combat] = 1000;
        coruscant.Add(prisoner);
        coruscant.Add(rescuer);
        Prisoners.Capture(game, prisoner, null);
        game.Clock.Flush(game);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x61).Common.Ref;
        var m = Missions.Create(game, type, Owner.Alliance, prisoner, [rescuer], [], 0, mandatory: false);
        NextChanceSucceeds(game, Missions.Probability(game, m, rescuer)!.Value);

        Missions.Execute(game, m);
        Assert.Equal((3, Prisoners.Rescued, false, Owner.Alliance), (m.TaskStatus, prisoner.State, prisoner.Captured, prisoner.Owner));
    }

    // Key = EnhCombat + EnhEsp - guard average EnhancedCombat - guard troops. ESCAPETB threshold lookup.
    [GameFact]
    public void EscapeKeyAndChance()
    {
        var game = Game();
        var coruscant = game.System(Refs.Coruscant);
        var c = Major(game, Owner.Alliance);
        coruscant.Add(c);
        Prisoners.Capture(game, c, null);
        game.Clock.Flush(game);
        var guards = coruscant.Contents.OfType<Character>().Where(g => g.Usable && g.Owner == Owner.Empire && !g.Captured && Family.Of(g.Type) < 0x3C).ToList();
        int a4 = guards.Count == 0 ? 0 : guards.Sum(g => Missions.Enhanced(game, g, SkillIndex.Combat)) / guards.Count;
        int a5 = coruscant.Contents.Count(x => Family.IsTroop(x.Type) && x.Usable && x.Owner == Owner.Empire);
        Assert.Equal(coruscant, c.Container);
        Assert.Equal(Missions.Enhanced(game, c, SkillIndex.Combat) + Missions.Enhanced(game, c, SkillIndex.Espionage) - a4 - a5, Prisoners.EscapeKey(game, c));
        Assert.Equal([1, 2, 5, 10, 10, 30, 30], new[] { -50, -49, -11, 10, 16, 50, 200 }.Select(k => Prisoners.EscapeChance(game, k)));
    }

    // A successful check sets EscapeRequest. The gate needs not OnMission and is rerun when OnMission clears.
    [GameFact]
    public void EscapeRequestWaitsForTheGate()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        game.System(Refs.Coruscant).Add(c);
        Prisoners.Capture(game, c, null);
        game.Clock.Flush(game);
        c.Skills[SkillIndex.Espionage] = 1000;
        c.OnMission = true;
        NextChanceSucceeds(game, Prisoners.EscapeChance(game, Prisoners.EscapeKey(game, c)));
        var draws = Trace(game);
        Prisoners.EscapeCheck(game, c);
        Assert.Equal([DrawSite.Escape], draws.Select(d => d.Site));
        Assert.Equal((true, Prisoners.Captured), (c.EscapeRequest, c.State));
        c.OnMission = false;
        Prisoners.EscapeGate(game, c);
        Assert.Equal((false, Prisoners.Escaped, Owner.Alliance), (c.EscapeRequest, c.State, c.Owner));
    }

    // Owner check: a prisoner under a parent of another side is released (State 2). A lost holding ship sets Adrift and releases.
    [GameFact]
    public void ReleasedByOwnerCheckAndParentLoss()
    {
        var game = Game();
        var yavin = game.System(Refs.Yavin);
        var c = Major(game, Owner.Alliance);
        Prisoners.Capture(game, c, null);
        yavin.Add(c);
        Prisoners.OwnerCheck(game, c);
        Assert.Equal((Prisoners.Released, Owner.Alliance), (c.State, c.Owner));

        var ship = game.Created.First(x => Family.IsCapitalShip(x.Type) && x.Owner == Owner.Empire && x.Usable);
        Prisoners.Capture(game, c, null);
        ship.Add(c);
        Objects.Destroy(game, ship, 0xC);
        Fleets.DeploymentCheck(game, c);
        Assert.Equal((true, Prisoners.Released, false), (c.Adrift, c.State, c.Captured));
    }

    // No destination: a free member is captured, a prisoner is released. If nothing is found, the member is captured again
    // and sent to the jail of its (captor-side) owner. Autorouting is cleared.
    [GameFact]
    public void NoDestinationGoesToJail()
    {
        var game = Game();
        game.Ready = false; // the mission fallback finds nothing before the game-ready flag
        var luke = Major(game, Owner.Alliance);
        var jail = Prisoners.NoDestination(game, luke);
        Assert.Equal(Prisoners.EmpireJail, jail.Ref);
        Assert.Equal((Prisoners.Captured, Owner.Empire, false), (luke.State, luke.Owner, luke.Autorouting));
        // Autorouting mission for the member alone, owner = its current owner, ImpliedTeam = its Captured flag.
        var m = luke.Mission!;
        Assert.Equal((0x43000003u, Owner.Empire, jail, true, true, true), (m.Type, m.Owner, m.Objective, m.ImpliedTeam, m.Mandatory, m.Captured.Contains(luke)));
        Assert.Equal(0, m.CompletionStatus); // ImpliedTeam: no status 5 without a Team member

        var other = Major(game, Owner.Alliance);
        Prisoners.Capture(game, other, null);
        Assert.Equal(Prisoners.EmpireJail, Prisoners.NoDestination(game, other).Ref); // released, then captured again
        Assert.Equal(Prisoners.Captured, other.State);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        vader.Skills[SkillIndex.Combat] = 100;
        Assert.Equal(Prisoners.AllianceJail, Prisoners.NoDestination(game, vader).Ref);
    }

    // Freed in own territory with an injury: Healing -> timer 0x388 (Base G 0xA02 = 1). The action heals 1.
    [GameFact]
    public void HealingAfterRelease()
    {
        var game = Game();
        var c = Major(game, Owner.Alliance);
        game.System(Refs.Yavin).Add(c);
        Prisoners.Capture(game, c, null);
        c.Injury = 5;
        Prisoners.Release(game, c);
        Assert.True(c.Healing);
        Assert.Equal((true, 1), (game.Timers[(0x388, c.Ref)].Active, game.Timers[(0x388, c.Ref)].Base));
        Prisoners.Heal(game, c);
        Assert.Equal(4, c.Injury);
    }

    // Capital ships and fleets refuse every category of another owner, and Destroyed or Enroute containers, under every
    // command type. (Base acceptance.)
    [GameFact]
    public void ShipAcceptanceChecksOwnerAndState()
    {
        var game = Game();
        var ship = game.Created.First(x => Family.IsCapitalShip(x.Type) && x.Owner == Owner.Empire && x.Usable);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        Assert.True(Fleets.CanPlace(game, ship, vader.Type, Owner.Empire, 0x241));
        Assert.False(Fleets.CanPlace(game, ship, vader.Type, Owner.Alliance, 0x241));
        Assert.False(Fleets.CanPlace(game, ship, vader.Type, Owner.Alliance, 0x240));
        Assert.False(Fleets.CanPlace(game, ship.Container!, ship.Type, Owner.Alliance, 0x204));
        Objects.Destroy(game, ship, 0xC);
        Assert.False(Fleets.CanPlace(game, ship, vader.Type, Owner.Empire, 0x241));
    }

    // No grouping while BattlePending. Its clear posts 0x304, which groups the system's agents.
    [GameFact]
    public void BattlePendingClearRegroups()
    {
        var game = Game();
        var coruscant = game.System(Refs.Coruscant);
        var c = Major(game, Owner.Alliance);
        Objects.SetParent(game, c, coruscant);
        game.Clock.Flush(game);
        Combat.SetBattlePending(game, coruscant, true);
        Objects.SetAutorouting(game, c);
        game.Clock.Flush(game);
        Assert.Equal((true, null), (c.Autorouting, c.Mission));
        Combat.SetBattlePending(game, coruscant, false);
        game.Clock.Flush(game);
        Assert.Equal((false, 0x43000003u), (c.Autorouting, c.Mission?.Type));
    }

    // The resign flag is set from the mission record's ResignOnInjury at the join. An injury makes a standard-type member resign. A movement-type
    // member (ResignOnInjury clear) does not. Leaving clears the flag.
    [GameFact]
    public void InjuryResignsByMissionType()
    {
        var game = Game();
        var a = Major(game, Owner.Alliance);
        var b = Major(game, Owner.Alliance, skip: 1);
        var espionage = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        var m1 = Missions.Create(game, espionage, Owner.Alliance, game.System(Refs.Coruscant), [a], [], 0, mandatory: false);
        var m2 = Missions.Create(game, 0x41000001, Owner.Alliance, game.System(Refs.Yavin), [b], [], 0, mandatory: false);
        Assert.Equal((m1.Record.ResignOnInjury, m2.Record.ResignOnInjury), (a.ResignOnInjury, b.ResignOnInjury));
        Prisoners.SetInjury(game, a, 5);
        Prisoners.SetInjury(game, b, 5);
        Assert.Equal((true, false), (a.ResignRequest, b.ResignRequest));
        Missions.Leave(game, m1, a);
        Assert.False(a.ResignOnInjury);
    }

    // Special force from the Destroyed cascade: destroyed with the parent (its reason, no Completed test).
    // From the deployment check, Autorouting. Never Adrift.
    [GameFact]
    public void SpecialForceParentDestroyed()
    {
        var game = Game();
        var ship = game.Created.First(x => Family.IsCapitalShip(x.Type) && x.Owner == Owner.Alliance && x.Usable);
        var force = (Agent)Objects.Create(game, Definitions.SpecialForces[0].Common.Ref, Owner.Alliance, ship, completed: false);
        Objects.Destroy(game, ship, 0xC);
        Assert.Equal((false, false, true, 0xC), (force.Autorouting, force.Adrift, force.Destroyed, force.DestroyedReason));
        var other = (Agent)Objects.Create(game, Definitions.SpecialForces[0].Common.Ref, Owner.Alliance, game.System(Refs.Yavin), completed: true);
        ship.Add(other);
        Fleets.DeploymentCheck(game, other);
        Assert.Equal((true, false, false), (other.Autorouting, other.Adrift, other.Destroyed));
    }

    // A destroyed ship's Completed, not EnrouteActive, fighters and troops inherit its reason (loss records 0xC; scrap 0x14
    // refunds them). Characters aboard draw the ship-loss injury check (G 0xA08-0xA0A) after the content handling, only for
    // reasons 1...0x10. The destroyed contents move out by their own 0x372, then the ship.
    [GameFact]
    public void ContentsDestroyedWithTheShip()
    {
        var game = Game();
        var coruscant = game.System(Refs.Coruscant);
        var ships = coruscant.Contents.OfType<Fleet>().Where(f => f.Owner == Owner.Empire && f.Usable).SelectMany(f => f.Contents)
            .Where(x => Family.IsCapitalShip(x.Type) && x.Contents.Any(y => y.Completed)).ToList();
        var ship = ships[0];
        var units = ship.Contents.Where(x => x is not Agent && x.Completed && !x.EnrouteActive).ToList();
        var c = Major(game, Owner.Empire);
        ship.Add(c);
        game.Clock.Flush(game);
        int loss = coruscant.BattleLoss[(int)Owner.Empire - 1];
        var draws = Trace(game);
        Objects.Destroy(game, ship, 0xC);
        Assert.All(units, u => Assert.Equal((true, 0xC), (u.Destroyed, u.DestroyedReason)));
        Assert.Equal((DrawSite.ShipLossInjury, 100), (draws[0].Site, draws[0].N)); // chance(q = max(G 0xA08, 100 - 100) = 1)
        Assert.False(c.Destroyed);
        game.Clock.Flush(game);
        Assert.Equal(loss + new[] { ship }.Concat(units).Where(x => Family.Of(x.Type) is >= 0x10 and < 0x20)
            .Sum(x => Definitions.Resolve(x.Type)!.Get(SdField.CombatValue)), coruscant.BattleLoss[(int)Owner.Empire - 1]);
        Assert.All(units, u => Assert.Same(game.Staging, u.Container));
        Assert.Same(game.Staging, ship.Container); // the adrift character was regrouped out of it first
        Assert.NotSame(ship, c.Container);

        var scrapped = ships[1];
        var refits = scrapped.Contents.Where(x => x.Completed && !x.EnrouteActive).ToList();
        var d2 = Major(game, Owner.Empire, skip: 1);
        scrapped.Add(d2);
        game.Clock.Flush(game);
        var side = game.Sides[Owner.Empire];
        int before = side.RefinedStock;
        var draws2 = Trace(game);
        Objects.Destroy(game, scrapped, 0x14);
        game.Clock.Flush(game);
        Assert.DoesNotContain(draws2, x => x.Site == DrawSite.ShipLossInjury);
        int expected = new[] { scrapped }.Concat(refits).Sum(x => game.RefinedMaterialCost(x.Type) / 2);
        Assert.Equal(before + expected, side.RefinedStock);
    }

    // Special-force evasion success: resign request only with the resign flag and a mission.
    // No injury check, no draw.
    [GameFact]
    public void SpecialForceEvasionResignIsGated()
    {
        var game = Game();
        var yavin = game.System(Refs.Yavin);
        Agent Force() => (Agent)Objects.Create(game, Definitions.SpecialForces[0].Common.Ref, Owner.Alliance, yavin, completed: true);
        var (a, b, c) = (Force(), Force(), Force());
        var espionage = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52);
        Assert.True(espionage.ResignOnInjury);
        Missions.Create(game, espionage.Common.Ref, Owner.Alliance, game.System(Refs.Coruscant), [a], [], 0, mandatory: false);
        Missions.Create(game, 0x41000001, Owner.Alliance, yavin, [b], [], 0, mandatory: false);
        var draws = Trace(game);
        foreach (var x in new[] { a, b, c }) Missions.Evaded(game, x);
        Assert.Equal((true, false, false), (a.ResignRequest, b.ResignRequest, c.ResignRequest));
        Assert.Empty(draws);
    }

    // Final move 0x372: a destroyed objective stays while a mission's ObjectiveKey locks it. A destroyed mission stays while
    // a member's 0x321 lock holds it. Both then move to the staging container. Destroyed fleets clear Battle / Bombard /
    // Assault, not Blockade. Their ships are destroyed with them and leave first.
    [GameFact]
    public void FinalMoveWaitsForLocksAndContents()
    {
        var game = Game();
        var a = Major(game, Owner.Alliance);
        var t = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Contents.Count == 0);
        var at = t.Container;
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        var m1 = Missions.Create(game, type, Owner.Alliance, t, [], [], 0, mandatory: false, origin: game.System(Refs.Yavin));
        var m2 = Missions.Create(game, type, Owner.Alliance, game.System(Refs.Coruscant), [a], [], 0, mandatory: false);
        Assert.Equal((1, 0), (t.LockCount, m2.LockCount)); // ObjectiveKey at once, RoleMissionKey at 0x321 dispatch
        game.Clock.Flush(game);
        Assert.Equal(1, m2.LockCount);

        Objects.Destroy(game, t, 7);
        game.Clock.Flush(game);
        Assert.Same(at, t.Container); // locked by m1's ObjectiveKey
        Objects.Destroy(game, m1, 0x14); // ObjectiveKey := none releases t
        Assert.Equal((null, false), (m1.Objective, t.Locked));
        Objects.Destroy(game, m2, 0x14);
        game.Clock.Flush(game);
        Assert.Equal((game.Staging, game.Staging, game.MissionManager), (t.Container, m1.Container, m2.Container)); // a's lock holds m2
        Missions.Leave(game, m2, a);
        Assert.True(m2.Locked);
        game.Clock.Flush(game);
        Assert.Equal((0, game.Staging), (m2.LockCount, m2.Container));

        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable && f.Contents.Count > 0);
        var system = fleet.Container;
        (fleet.Battle, fleet.Blockade, fleet.Bombard, fleet.Assault) = (true, true, true, true);
        Objects.Destroy(game, fleet, 0xC);
        Assert.Equal((false, true, false, false), (fleet.Battle, fleet.Blockade, fleet.Bombard, fleet.Assault));
        Assert.All(fleet.Contents.Where(x => Family.IsCapitalShip(x.Type)), x => Assert.Equal((true, 0xC), (x.Destroyed, x.DestroyedReason)));
        Assert.Same(system, fleet.Container);
        game.Clock.Flush(game);
        Assert.Same(game.Staging, fleet.Container);
    }

    // Every view copy of RoleMissionKey posts 0x321. A full refresh of the frozen Empire view locks the mission too, so the
    // destroyed mission stays after the member's leave. The character's own final move drops that view into staging (its
    // view parent is live for the Empire), so it runs the late pass: the real key is already none, and the processing
    // view's second copy clears it (0x321 -> lock 0). Then the mission makes its final move.
    [GameFact]
    public void FrozenViewHoldsTheMissionLock()
    {
        var game = Game();
        var a = Major(game, Owner.Alliance);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        var m = Missions.Create(game, type, Owner.Alliance, game.System(Refs.Coruscant), [a], [], 0, mandatory: false);
        game.Clock.Flush(game);
        Knowledge.Refresh(game, a, Owner.Empire);
        game.Clock.Flush(game);
        Assert.Equal(2, m.LockCount);
        Missions.Leave(game, m, a);
        Objects.Destroy(game, m, 0x14);
        game.Clock.Flush(game);
        Assert.Equal((1, game.MissionManager), (m.LockCount, m.Container));
        Objects.Destroy(game, a, 7);
        game.Clock.Flush(game);
        Assert.Equal((game.Staging, 0, game.Staging), (a.Container, m.LockCount, m.Container));
    }

    // Moved into a travelling carrier: Enroute (not Usable) with the carrier's ETA and DepDest. A destroyed traveller leaves
    // hyperspace (timer 0x387 inactive) and its 0x306 does nothing.
    [GameFact]
    public void ParentChangeAndDestroyRecomputeTravel()
    {
        var game = Game();
        var ship = game.Created.First(x => Family.IsCapitalShip(x.Type) && x.Owner == Owner.Alliance && x.Usable);
        var c = Major(game, Owner.Alliance);
        Objects.SetEnrouteActive(game, ship, true);
        (ship.Eta, ship.Destination) = (9, game.System(Refs.Coruscant));
        Objects.SetParent(game, c, ship);
        Assert.Equal((true, false, 9, game.System(Refs.Coruscant)), (c.Enroute, c.Usable, c.Eta, c.Destination));

        var d = Major(game, Owner.Alliance, skip: 1);
        Fleets.Travel(game, d, d.Container, game.System(Refs.Coruscant));
        Assert.True(d.HyperdriveActive);
        var at = d.Container;
        Objects.Destroy(game, d, 10);
        Assert.Equal((false, false, false, 0, null), (d.HyperdriveActive, d.EnrouteActive,
            game.Timers[(0x387, d.Ref)].Active, d.Eta, d.Destination));
        game.Clock.Flush(game);
        Assert.Same(game.Staging, d.Container);
        Assert.NotSame(at, d.Container);
    }

    // Every member draws its loyalty. The betrayer is the traitor. Every other Team character then draws chance(Force),
    // also for Force 0.
    [GameFact]
    public void BetrayalIdentifiesTheTraitor()
    {
        var game = Game();
        var a = Major(game, Owner.Alliance);
        var b = Major(game, Owner.Alliance, skip: 1);
        (a.Skills[SkillIndex.Loyalty], b.Skills[SkillIndex.Loyalty]) = (0, 100);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        var m = Missions.Create(game, type, Owner.Alliance, game.System(Refs.Coruscant), [a, b], [], 0, mandatory: false);
        var draws = Trace(game);
        Assert.Same(a, Missions.Betrayal(game, m));
        Assert.Equal([DrawSite.Loyalty, DrawSite.Loyalty, DrawSite.Traitor], draws.Select(d => d.Site));
        Assert.Equal(100, draws[2].N);
    }
}
