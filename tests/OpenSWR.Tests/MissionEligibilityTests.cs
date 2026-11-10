using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Team checks, type masks check, target checks and the UI lists.</summary>
public class MissionEligibilityTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    static uint TypeOf(uint family) => Definitions.Missions.First(r => Family.Of(r.Common.Ref) == family).Common.Ref;

    static Character Agent(GameState game) =>
        game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Usable && !c.OnMission && c.Container is StarSystem);

    static OrderResult? Validate(GameState game, uint family, Character a, GameObject target) =>
        MissionEligibility.Validate(game, 0x240, Owner.Alliance, TypeOf(family), [a], [], target);

    // Per-agent base order check codes: foreign (1, 0x14), not Completed (1, 0x10), Enroute (1, 0x11).
    [GameFact]
    public void BaseOrderCheckCodes()
    {
        var game = Game();
        var a = Agent(game);
        var own = (StarSystem)a.Container!;
        Assert.Equal(new OrderResult(1, 0x14), MissionEligibility.Validate(game, 0x240, Owner.Empire, TypeOf(0x51), [a], [], own));
        a.Completed = false;
        Assert.Equal(new OrderResult(1, 0x10), Validate(game, 0x51, a, own));
        (a.Completed, a.Enroute) = (true, true);
        Assert.Equal(new OrderResult(1, 0x11), Validate(game, 0x51, a, own));
    }

    // Characters may not do reconnaissance, and assassination is Empire-only. Injured or busy agents are refused.
    // Diplomacy against an enemy system breaks the relation rule. Rescue needs a captured target.
    [GameFact]
    public void RefusalCodes()
    {
        var game = Game();
        var a = Agent(game);
        var coruscant = game.System(Refs.Coruscant);
        var own = (StarSystem)a.Container!;
        Assert.Equal(new OrderResult(0x40, 1), Validate(game, 0x54, a, own));
        Assert.Equal(new OrderResult(0x40, 1), Validate(game, 0x63, a, own));
        Assert.Equal(new OrderResult(0x40, 0x23), Validate(game, 0x51, a, coruscant));
        var vader = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Container is StarSystem); // an Empire character on a planet
        Assert.Equal(new OrderResult(0x40, 0x20), Validate(game, 0x61, a, vader)); // never seen: no location
        Knowledge.Refresh(game, vader, Owner.Alliance);
        Assert.Equal(new OrderResult(0x40, 0x2B), Validate(game, 0x61, a, vader)); // rescue: target not captured
        Assert.Null(Validate(game, 0x62, a, vader));

        Prisoners.SetInjury(game, a, 5);
        Assert.Equal(new OrderResult(0x30, 3), Validate(game, 0x51, a, own));
    }

    // The team-only list: characters get diplomacy but not reconnaissance, and some types are hidden.
    [GameFact]
    public void TeamOnlyList()
    {
        var game = Game();
        var types = MissionEligibility.TypesForTeam(game, Owner.Alliance, [Agent(game)], []).Select(r => Family.Of(r.Common.Ref)).ToList();
        Assert.Contains(0x51u, types);
        Assert.DoesNotContain(0x54u, types);
        Assert.DoesNotContain(0x41u, types);
        Assert.DoesNotContain(0x63u, types);
    }

    // A never-visited rim system (fresh view: owner 3, not Explored, not Populated) allows diplomacy and espionage (0x40, 0x11),
    // and research / recruitment / subdue / incite (0x40, 0x23). A recon special force passes.
    [GameFact]
    public void RimSystemRefusals()
    {
        var game = Game();
        var a = Agent(game);
        var rim = game.Systems.First(s => Family.Of(s.Type) == 0x92 && s.Owner != Owner.Alliance && !Knowledge.IsLive(s, Owner.Alliance));
        Assert.Equal(new OrderResult(0x40, 0x11), Validate(game, 0x51, a, rim));
        Assert.Equal(new OrderResult(0x40, 0x11), Validate(game, 0x52, a, rim));
        foreach (uint family in new uint[] { 0x53, 0x55, 0x57, 0x56 })
            Assert.Equal(new OrderResult(0x40, 0x23), Validate(game, family, a, rim));
        var probeType = Definitions.SpecialForces.First(r => r.Common.Index == 3 && r.Alliance != 0).Common.Ref;
        var probe = Objects.Create(game, probeType, Owner.Alliance, a.Container!, completed: true);
        Assert.Null(MissionEligibility.Validate(game, 0x240, Owner.Alliance, TypeOf(0x54), [probe], [], rim));
    }

    // A movement class tests the ArgRef itself (an Enroute ship gives (0x40, 0x13)). A non-movement class tests the
    // objective's own Destroyed with the standard (0x40, 0x21). The location's view stays clean.
    [GameFact]
    public void CheckBlockSlots()
    {
        var game = Game();
        var a = Agent(game);
        var ship = game.Fleets.Where(f => f.Owner == Owner.Alliance).SelectMany(f => f.Contents).First(o => Family.IsCapitalShip(o.Type));
        Assert.Null(MissionEligibility.Validate(game, 0x241, Owner.Alliance, TypeOf(0x41), [a], [], ship));
        Objects.SetEnrouteActive(game, ship, true);
        Assert.True(ship.Enroute);
        Assert.Equal(new OrderResult(0x40, 0x13), MissionEligibility.Validate(game, 0x241, Owner.Alliance, TypeOf(0x41), [a], [], ship));

        var vader = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Container is StarSystem);
        Knowledge.Refresh(game, vader, Owner.Alliance);
        Assert.Null(Validate(game, 0x62, a, vader));
        Objects.Destroy(game, vader, 1);
        Knowledge.Refresh(game, vader, Owner.Alliance);
        Assert.Equal(new OrderResult(0x40, 0x21), Validate(game, 0x62, a, vader));
    }

    // Yavin 4 to Shili: 49 days at speed 100, 24 with Han (speed 50).
    [GameFact]
    public void TravelDaysWorkedExample()
    {
        var game = Game();
        var (yavin, shili) = (game.System(Refs.Yavin), game.Systems.First(s => s.Type == 0x9000010D));
        Assert.Equal((247, 49, 24), (Orders.Distance(yavin, shili), Orders.TravelDays(game, yavin, shili, 100), Orders.TravelDays(game, yavin, shili, 50)));
    }
    // A command-created non-movement mission keeps the target's location as TargetKey. The objective moving away ends it
    // with status 6; the location destroyed ends it with status 7.
    [GameFact]
    public void PhaseCheckReadsObjectiveAndLocation()
    {
        var game = Game();
        var agent = game.Characters.First(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.Container is StarSystem);
        var enemy = game.Characters.First(c => c.Owner == Owner.Alliance && c.Usable && !c.OnMission && c.Container is StarSystem);
        var at = (StarSystem)enemy.Container!;
        var assassination = Missions.Create(game, TypeOf(0x63), Owner.Empire, enemy, [agent], [], 0, mandatory: false, origin: at, targetKey: at);
        Missions.PhaseCheck(game, assassination);
        Assert.Equal(0, assassination.CompletionStatus);
        Objects.SetParent(game, enemy, game.Systems.First(s => s != at));
        Missions.PhaseCheck(game, assassination);
        Assert.Equal(6, assassination.CompletionStatus);

        var other = game.Characters.First(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.Container is StarSystem && c != agent);
        var neutral = game.Systems.First(s => s.Owner == Owner.Neutral && s.Populated && s != at);
        var diplomacy = Missions.Create(game, TypeOf(0x51), Owner.Empire, neutral, [other], [], 0, mandatory: false, origin: neutral, targetKey: neutral);
        Missions.PhaseCheck(game, diplomacy);
        Assert.Equal(0, diplomacy.CompletionStatus);
        Objects.Destroy(game, neutral, 0xF);
        Missions.PhaseCheck(game, diplomacy);
        Assert.Equal(7, diplomacy.CompletionStatus);
    }
}
