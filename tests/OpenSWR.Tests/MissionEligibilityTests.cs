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
        MissionEligibility.Validate(game, CommandType.CreateMission, Owner.Alliance, TypeOf(family), [a], [], target);

    // Per-agent base order check codes: foreign Refusal.NotOwner, not Completed Refusal.NotCompleted, Enroute Refusal.Enroute.
    [GameFact]
    public void BaseOrderCheckCodes()
    {
        var game = Game();
        var a = Agent(game);
        var own = (StarSystem)a.Container!;
        Assert.Equal(Refusal.NotOwner, MissionEligibility.Validate(game, CommandType.CreateMission, Owner.Empire, TypeOf((uint)MissionKind.Diplomacy), [a], [], own));
        a.Completed = false;
        Assert.Equal(Refusal.NotCompleted, Validate(game, 81, a, own));
        (a.Completed, a.Enroute) = (true, true);
        Assert.Equal(Refusal.Enroute, Validate(game, 81, a, own));
    }

    // Characters may not do reconnaissance, and assassination is Empire-only. Injured or busy agents are refused.
    // Diplomacy against an enemy system breaks the relation rule. Rescue needs a captured target.
    [GameFact]
    public void RefusalCodes()
    {
        var game = Game();
        var a = Agent(game);
        var coruscant = game.Known.Coruscant!;
        var own = (StarSystem)a.Container!;
        Assert.Equal(Refusal.AgentNotAllowed, Validate(game, 84, a, own));
        Assert.Equal(Refusal.AgentNotAllowed, Validate(game, 99, a, own));
        Assert.Equal(Refusal.ObjectiveOwnerRefused, Validate(game, 81, a, coruscant));
        var vader = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Container is StarSystem); // an Empire character on a planet
        Assert.Equal(Refusal.NoTargetLocation, Validate(game, 97, a, vader)); // never seen: no location
        Knowledge.Refresh(game, vader, Owner.Alliance);
        Assert.Equal(Refusal.FreeCharacterRefused, Validate(game, 97, a, vader)); // rescue: target not captured
        Assert.Null(Validate(game, 98, a, vader));

        Prisoners.SetInjury(game, a, 5);
        Assert.Equal(Refusal.Injured, Validate(game, 81, a, own));
    }

    // The team-only list: characters get diplomacy but not reconnaissance, and some types are hidden.
    [GameFact]
    public void TeamOnlyList()
    {
        var game = Game();
        var types = MissionEligibility.TypesForTeam(game, Owner.Alliance, [Agent(game)], []).Select(r => Family.Of(r.Common.Ref)).ToList();
        Assert.Contains(81u, types);
        Assert.DoesNotContain(84u, types);
        Assert.DoesNotContain(65u, types);
        Assert.DoesNotContain(99u, types);
    }

    // A never-visited rim system (fresh view: owner 3, not Explored, not Populated) allows diplomacy and espionage (TargetUnexplored),
    // and research / recruitment / subdue / incite (ObjectiveOwnerRefused). A recon special force passes.
    [GameFact]
    public void RimSystemRefusals()
    {
        var game = Game();
        var a = Agent(game);
        var rim = game.Systems.First(s => Family.Of(s.Type) == Families.RimSystem && s.Owner != Owner.Alliance && !Knowledge.IsLive(s, Owner.Alliance));
        Assert.Equal(Refusal.TargetUnexplored, Validate(game, 81, a, rim));
        Assert.Equal(Refusal.TargetUnexplored, Validate(game, 82, a, rim));
        foreach (uint family in new uint[] { (uint)MissionKind.Research, (uint)MissionKind.Recruitment, (uint)MissionKind.SubdueUprising, (uint)MissionKind.InciteUprising })
            Assert.Equal(Refusal.ObjectiveOwnerRefused, Validate(game, family, a, rim));
        var probeType = Definitions.SpecialForces.First(r => r.Common.Index == 3 && r.Alliance != 0).Common.Ref;
        var probe = Objects.Create(game, probeType, Owner.Alliance, a.Container!, completed: true);
        Assert.Null(MissionEligibility.Validate(game, CommandType.CreateMission, Owner.Alliance, TypeOf((uint)MissionKind.Reconnaissance), [probe], [], rim));
    }

    // A movement class tests the ArgRef itself (an Enroute ship gives Refusal.TargetEnroute). A non-movement class tests the
    // objective's own Destroyed with the standard Refusal.TargetLocationDestroyed. The location's view stays clean.
    [GameFact]
    public void CheckBlockSlots()
    {
        var game = Game();
        var a = Agent(game);
        var ship = game.Fleets.Where(f => f.Owner == Owner.Alliance).SelectMany(f => f.Contents).First(o => Family.IsCapitalShip(o.Type));
        Assert.Null(MissionEligibility.Validate(game, CommandType.CreateMoveMission, Owner.Alliance, TypeOf((uint)MissionKind.Move), [a], [], ship));
        Objects.SetEnrouteActive(game, ship, true);
        Assert.True(ship.Enroute);
        Assert.Equal(Refusal.TargetEnroute, MissionEligibility.Validate(game, CommandType.CreateMoveMission, Owner.Alliance, TypeOf((uint)MissionKind.Move), [a], [], ship));

        var vader = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Container is StarSystem);
        Knowledge.Refresh(game, vader, Owner.Alliance);
        Assert.Null(Validate(game, 98, a, vader));
        Objects.Destroy(game, vader, DestroyedReason.Other);
        Knowledge.Refresh(game, vader, Owner.Alliance);
        Assert.Equal(Refusal.TargetLocationDestroyed, Validate(game, 98, a, vader));
    }

    // Yavin 4 to Shili: 49 days at speed 100, 24 with Han (speed 50).
    [GameFact]
    public void TravelDaysWorkedExample()
    {
        var game = Game();
        var (yavin, shili) = (game.Known.Yavin!, game.Systems.First(s => s.Type == SdRef.Make(Families.CoreSystem, 269)));
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
        var assassination = Missions.Create(game, TypeOf((uint)MissionKind.Assassination), Owner.Empire, enemy, [agent], [], 0, mandatory: false, origin: at, targetKey: at);
        Missions.PhaseCheck(game, assassination);
        Assert.Equal(MissionCompletion.None, assassination.CompletionStatus);
        Objects.SetParent(game, enemy, game.Systems.First(s => s != at));
        Missions.PhaseCheck(game, assassination);
        Assert.Equal(MissionCompletion.ObjectiveInvalid, assassination.CompletionStatus);

        var other = game.Characters.First(c => c.Owner == Owner.Empire && c.Usable && !c.OnMission && c.Container is StarSystem && c != agent);
        var neutral = game.Systems.First(s => s.Owner == Owner.Neutral && s.Populated && s != at);
        var diplomacy = Missions.Create(game, TypeOf((uint)MissionKind.Diplomacy), Owner.Empire, neutral, [other], [], 0, mandatory: false, origin: neutral, targetKey: neutral);
        Missions.PhaseCheck(game, diplomacy);
        Assert.Equal(MissionCompletion.None, diplomacy.CompletionStatus);
        Objects.Destroy(game, neutral, DestroyedReason.Bombardment);
        Missions.PhaseCheck(game, diplomacy);
        Assert.Equal(MissionCompletion.TargetDestroyed, diplomacy.CompletionStatus);
    }
}
