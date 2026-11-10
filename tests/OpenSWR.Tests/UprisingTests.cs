using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Requirement / surplus, start, Suppressing and end, incidents, incite and subdue, mission totals.</summary>
public class UprisingTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    /// <summary>A copy of the game generator: the draws the sim is about to make, predicted.</summary>
    static GameRandom Clone(GameState game) => new(game.Random.State);

    static List<GameObject> Troops(StarSystem s, Owner side) => s.Contents.Where(x => Family.IsTroop(x.Type) && x.Usable && x.Owner == side).ToList();

    static List<Character> Characters(StarSystem s, Owner side) =>
        s.Contents.OfType<Character>().Where(c => c.Usable && c.Owner == side && Family.Of(c.Type) < 0x3C).ToList();

    /// <summary>A Populated Empire system with Empire share below 60, its regiments beyond the requirement removed (surplus 0).</summary>
    static StarSystem Disloyal(GameState game)
    {
        var s = game.SystemsOf(Owner.Empire).First(s => s.Populated && s.SupportFor(Owner.Empire) < 60);
        foreach (var t in Troops(s, Owner.Empire).Skip(s.GarrisonRequirement)) Objects.Destroy(game, t, 0x14);
        game.Clock.Flush(game);
        return s;
    }

    // req0 = ceildiv(60 - share, 10) below 60. Halved (div 2) with DeathStarNearby for owner 2, doubled while Uprising.
    [GameTheory]
    [InlineData(59, false, false, 1, 1)]
    [InlineData(49, false, false, 2, 2)]
    [InlineData(60, false, false, 0, 0)]
    [InlineData(0, false, false, 6, 6)]
    [InlineData(40, false, true, 4, 4)]
    [InlineData(40, true, false, 1, 2)]
    [InlineData(50, true, false, 0, 1)]
    [InlineData(40, true, true, 2, 4)]
    public void RequirementAndSurplus(int share, bool deathStar, bool uprising, int empire, int alliance)
    {
        var game = Game();
        var s = Disloyal(game);
        (s.Support, s.DeathStarNearby, s.Uprising) = (100 - share, deathStar, uprising);
        Assert.Equal(empire, Systems.Requirement(game, s));
        s.GarrisonRequirement = empire;
        Assert.Equal(s.Contents.Count(x => Family.IsTroop(x.Type) && x.Usable) - empire, Systems.Surplus(s));

        (s.Owner, s.Support) = (Owner.Alliance, share);
        Assert.Equal(alliance, Systems.Requirement(game, s));
        s.Populated = false;
        Assert.Equal(0, Systems.Requirement(game, s));
        (s.Populated, s.Owner) = (true, Owner.Neutral);
        Assert.Equal(0, Systems.Requirement(game, s));
        Assert.Equal(0, Systems.Surplus(s));
    }

    // A lost regiment starts the uprising: requirement doubled, facilities suspended, timers 0x384 / 0x38D start.
    // With 2 * req regiments the uprising is Suppressing (timer 0x383). The 0x383 action ends it.
    [GameFact]
    public void UprisingStartSuppressEnd()
    {
        var game = Game();
        var s = Disloyal(game);
        int req = s.GarrisonRequirement;
        var troops = Troops(s, Owner.Empire);
        Assert.True(req > 0);
        Assert.Equal((req, 0, false), (troops.Count, s.GarrisonSurplus, s.Uprising));
        var facilities = s.Contents.OfType<Facility>().Where(f => f.Existing).ToList();
        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;

        Objects.Destroy(game, troops[0], 0x14);
        game.Clock.Flush(game);
        Assert.True(s.Uprising);
        Assert.False(s.Suppressing);
        Assert.Equal((2 * req, req - 1 - 2 * req), (s.GarrisonRequirement, s.GarrisonSurplus));
        Assert.All(facilities, f => Assert.True(f.Suspended));
        Assert.True(game.Timers[(0x384, s.Ref)].Active && game.Timers[(0x38D, s.Ref)].Active);
        Assert.Contains(draws, d => d is { Site: DrawSite.Timer, N: 71 }); // 0x38D start roll(70)

        for (int i = req - 1; i < 2 * req; i++) Objects.Create(game, Refs.EmpireArmy, Owner.Empire, s, completed: true);
        game.Clock.Flush(game);
        Assert.Equal((true, true, 0), (s.Uprising, s.Suppressing, s.GarrisonSurplus));
        Assert.True(game.Timers[(0x383, s.Ref)].Active);
        Assert.Contains(draws, d => d is { Site: DrawSite.Timer, N: 21 }); // 0x383 start roll(20)

        Systems.SuppressTimer(game, s);
        game.Clock.Flush(game);
        Assert.Equal((false, false, req), (s.Uprising, s.Suppressing, s.GarrisonRequirement));
        Assert.False(game.Timers[(0x383, s.Ref)].Active || game.Timers[(0x384, s.Ref)].Active || game.Timers[(0x38D, s.Ref)].Active);
        Assert.All(facilities, f => Assert.False(f.Suspended));
    }

    /// <summary>Sets the incite total so that the coming incident's key is <paramref name="key"/>; returns the predicting generator after the two key rolls.</summary>
    static GameRandom AimKey(GameState game, StarSystem s, int key)
    {
        var clone = Clone(game);
        clone.Roll(7);
        int r = clone.Roll(9) + 1 + clone.Roll(9) + 1;
        s.InciteTotal = key - (r + Systems.BaseRequirement(game, s.SupportFor(s.Owner)) - Troops(s, s.Owner).Count + s.SubdueTotal - Missions.Stormtroopers(s));
        return clone;
    }

    // Key 9: u1 = 1 (a facility, DestroyedReason 8), u2 = 3 (a character's injury check). Then the support change.
    [GameFact]
    public void IncidentDestroysFacilityAndInjures()
    {
        var game = Game();
        var s = game.System(Refs.Coruscant);
        var facilities = s.Contents.Where(x => Family.IsFacility(x.Type) && x.Usable && x.Owner == Owner.Empire).ToList();
        var characters = Characters(s, Owner.Empire);
        Assert.NotEmpty(facilities);
        Assert.NotEmpty(characters);
        s.IncidentSupportDelta = -2;
        int share = s.SupportFor(Owner.Empire);
        var clone = AimKey(game, s, 9);
        int k1 = clone.Roll(facilities.Count - 1);
        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;

        Systems.Incident(game, s);
        Assert.Equal([DrawSite.Informant, DrawSite.UprisingRoll, DrawSite.UprisingRoll, DrawSite.IncidentPick, DrawSite.CharacterPick, DrawSite.Injury],
            draws.Take(6).Select(d => d.Site));
        Assert.Equal([8], draws.Take(1).Select(d => d.N)); // roll(7)
        Assert.Equal([facilities[k1]], facilities.Where(f => f.Destroyed));
        Assert.Equal(8, facilities[k1].DestroyedReason);
        Assert.Equal(share - 2, s.SupportFor(Owner.Empire));
    }

    // Key >= 12: u1 = 2 (a regiment, DestroyedReason 8), u2 = 5 (every captured character freed, State 1, no draw).
    [GameFact]
    public void IncidentDestroysTroopAndReleasesAll()
    {
        var game = Game();
        var s = game.System(Refs.Coruscant);
        var troops = Troops(s, Owner.Empire);
        var characters = Characters(s, Owner.Empire);
        Assert.NotEmpty(troops);
        foreach (var c in characters.Take(2)) (c.State, c.Captured) = (Prisoners.Captured, true);
        var clone = AimKey(game, s, 30);
        int k = clone.Roll(troops.Count - 1);

        Systems.Incident(game, s);
        Assert.Equal([troops[k]], troops.Where(t => t.Destroyed));
        Assert.Equal(8, troops[k].DestroyedReason);
        Assert.All(characters.Take(2), c => Assert.True(c.State == Prisoners.Escaped && !c.Captured && c.Autorouting));
        Assert.Equal(clone.State, game.Random.State);
    }

    // Key 11: u1 = 2, u2 = 4. c = captured count, k = roll(c - 1). The cursor passes k captured characters and picks
    // the one under it, captured or not (k = 0: the first character, here a free one).
    [GameFact]
    public void ReleasePickQuirk()
    {
        var game = Game();
        var s = game.System(Refs.Coruscant);
        foreach (var c in game.Characters.Where(c => c.Owner == Owner.Empire && c.Usable).Take(3)) s.Add(c);
        var characters = Characters(s, Owner.Empire);
        Assert.True(characters.Count >= 3);
        foreach (var c in characters.Skip(1).Take(2)) (c.State, c.Captured) = (Prisoners.Captured, true);
        var clone = AimKey(game, s, 11);
        int troops = Troops(s, Owner.Empire).Count;
        if (troops > 0) clone.Roll(troops - 1);
        int k = clone.Roll(1);
        var picked = characters[k == 0 ? 0 : 2];

        Systems.Incident(game, s);
        Assert.True(picked.Autorouting);
        Assert.False(picked.Captured);
        Assert.Equal(k == 0, characters[2].Captured); // k = 0 frees nobody: the free first character is picked
        Assert.True(characters[1].Captured);
    }

    static Mission Mission(GameState game, uint type, Owner side, StarSystem target, int leadership)
    {
        var agent = game.Characters.First(c => c.Owner == side && c.Placed && c.Existing && !c.OnMission);
        agent.Skills[SkillIndex.Leadership] = leadership;
        return Missions.Create(game, type, side, target, [agent], [], 0, mandatory: false);
    }

    /// <summary>Burns draws until the next chance(99) succeeds (the table's maximum p).</summary>
    static void NextChanceSucceeds(GameState game)
    {
        while (Clone(game).Percent() >= 99) game.Random.Percent();
    }

    // Incite success: the incident runs on the target (calm) system. TaskStatus 2, with a troop of its owner there.
    [GameFact]
    public void InciteRunsIncident()
    {
        var game = Game();
        var s = Disloyal(game);
        var m = Mission(game, 0x56000040, Owner.Alliance, s, 1000);
        Assert.Equal(99, Missions.Probability(game, m, m.Team[0]));
        s.InciteTotal = -100; // u1 = u2 = 0
        NextChanceSucceeds(game);
        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;
        Missions.Execute(game, m);
        Assert.Equal([DrawSite.Incite, DrawSite.Informant, DrawSite.UprisingRoll, DrawSite.UprisingRoll], draws.Select(d => d.Site));
        Assert.False(s.Uprising);
        Assert.Equal(2, m.TaskStatus);
    }

    // Subdue draws nothing without Uprising. On success the owner's support changes by 1 + roll(19), then the subdue end
    // (troops - undoubled requirement >= 0 -> Uprising := 0). TaskStatus 3, Leadership + 1.
    [GameFact]
    public void SubdueEndsUprising()
    {
        var game = Game();
        var s = Disloyal(game);
        var m = Mission(game, 0x57000080, Owner.Empire, s, 1000);
        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;
        Missions.Execute(game, m);
        Assert.Empty(draws);

        var troop = Troops(s, Owner.Empire)[0];
        Objects.Destroy(game, troop, 0x14);
        game.Clock.Flush(game);
        Objects.Create(game, Refs.EmpireArmy, Owner.Empire, s, completed: true); // back to req0 < the doubled requirement
        game.Clock.Flush(game);
        Assert.True(s.Uprising);
        Assert.False(s.Suppressing);
        int share = s.SupportFor(Owner.Empire), lead = m.Team[0].Skills[SkillIndex.Leadership];
        NextChanceSucceeds(game);
        draws.Clear();
        Missions.Execute(game, m);
        Assert.Equal([DrawSite.Subdue, DrawSite.SubdueDelta], draws.Take(2).Select(d => d.Site));
        Assert.Equal(20, draws[1].N); // roll(19), system owner = mission owner
        Assert.Equal(Math.Min(100, share + 1 + draws[1].Result), s.SupportFor(Owner.Empire));
        Assert.False(s.Uprising);
        Assert.Equal(3, m.TaskStatus);
        Assert.Equal(lead + 1, m.Team[0].Skills[SkillIndex.Leadership]);
    }

    // Sum over incite / subdue missions in phase 8 of avgLead div 10 (subdue negated). -2 if an incite counted.
    [GameFact]
    public void MissionTotals()
    {
        var game = Game();
        var s = Disloyal(game);
        var incite = Mission(game, 0x56000040, Owner.Alliance, s, 57);
        var late = Mission(game, 0x56000040, Owner.Alliance, s, 90);
        var subdue = Mission(game, 0x57000080, Owner.Empire, s, 34);
        int Lead(Mission m) => Missions.Enhanced(game, m.Team[0], SkillIndex.Leadership) / 10;
        (incite.Phase, late.Phase, subdue.Phase) = (8, 9, 8);
        Missions.UprisingTotals(game, s);
        Assert.Equal((Lead(incite), -Lead(subdue), -2), (s.InciteTotal, s.SubdueTotal, s.IncidentSupportDelta));

        incite.Phase = 10;
        Missions.UprisingTotals(game, s);
        Assert.Equal((0, -Lead(subdue), 0), (s.InciteTotal, s.SubdueTotal, s.IncidentSupportDelta));
    }

    // A core system's Uprising reaches the other side's frozen view too and posts 0x14B there. The Alliance player's
    // controller counts the 621 alarm for an Empire core system it does not see.
    [GameFact]
    public void CoreUprisingReachesTheFrozenView()
    {
        var game = Game();
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance);
        game.Controllers[Owner.Alliance] = human;
        var s = game.SystemsOf(Owner.Empire).First(s => s.Populated && s.SupportFor(Owner.Empire) < 60 && Family.Of(s.Type) == 0x90 && !Knowledge.IsLive(s, Owner.Alliance));
        foreach (var t in Troops(s, Owner.Empire).Skip(s.GarrisonRequirement)) Objects.Destroy(game, t, 0x14);
        game.Clock.Flush(game);
        Assert.False(Knowledge.See(game, s, Owner.Alliance).Uprising);
        Objects.Destroy(game, Troops(s, Owner.Empire)[0], 0x14);
        game.Clock.Flush(game);
        Assert.True(s.Uprising);
        Assert.True(Knowledge.See(game, s, Owner.Alliance).Uprising);
        Assert.Contains(game.Messages, m => m.Side == Owner.Alliance && m.Type == 0x14B && m.Object == s);
        OpenSWR.Sim.Ai.Controller.Deliver(game);
        Assert.Equal(1, human.UprisingAlarms);
    }
}
