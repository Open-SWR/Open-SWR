using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Tests;

/// <summary>Manager 0xF8 story flags, story check, Dagobah, bounty hunters and palace, final battle.</summary>
public class StoryTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    static Character C(GameState game, uint sdRef) => game.Characters.First(c => c.Ref == sdRef);

    static void NextChanceFails(GameState game, int p)
    {
        while (new GameRandom(game.Random.State).Percent() < p) game.Random.Percent();
    }

    // Timer 0x392: LukeDagobahRequired is set and a Dagobah mission starts for the idle Luke (slot off). After the
    // 100-day phase 8, Luke's Force becomes F + pct(F, G 0x181E), DagobahCompleted is set and LukeDagobahRequired is cleared.
    [GameFact]
    public void DagobahTraining()
    {
        var game = Game();
        var luke = C(game, Refs.Luke);
        int f = luke.Force;
        Story.DagobahTimer(game);
        var m = game.Missions[^1];
        Assert.Equal((Story.DagobahType, luke, game.Abode(Story.Dagobah)), (m.Type, m.Team[0], m.Objective));
        Assert.False(game.Timers[(0x392, 0xF8000006)].Active);
        Assert.True(Story.Has(game, StoryFlag.LukeDagobahRequired));

        game.Clock.RunUntilDay(game, game.Day + 120);
        Assert.Equal((11, 3), (m.Phase, m.TaskStatus));
        Assert.Equal(f + Pct(f, game.Params.General(0x181E)), luke.Force);
        Assert.True(luke.DagobahCompleted);
        Assert.False(Story.Has(game, StoryFlag.LukeDagobahRequired));
    }

    // Timer 0x393: a hunter in Jabba's Palace starts a Bounty mission. A failed evasion captures Han (TaskStatus 3, no escape).
    // Bounty members return to the palace. Han held there sets HanCapturedAtPalace (bounty timer off), and Palace missions
    // start for the idle Luke and Leia.
    [GameFact]
    public void BountyHuntersTakeHanToThePalace()
    {
        var game = Game();
        var palace = game.Abode(Story.JabbasPalace);
        var han = C(game, Refs.Han);
        Story.BountyTimer(game);
        var hunter = palace.Contents.OfType<Agent>().Single(a => a.Type == Story.BountyHunter);
        var m = game.Missions[^1];
        Assert.Equal((Story.BountyType, (GameObject)han, han.System, Owner.Empire), (m.Type, m.Objective, (StarSystem?)m.TargetKey, hunter.Owner));
        Assert.Equal(100, Missions.Probability(game, m, hunter));

        NextChanceFails(game, Missions.Table(game, "RLEVADTB", Missions.Enhanced(game, han, SkillIndex.Combat)) ?? 0);
        Assert.True(Story.Attempt(game, m, hunter, 100));
        Assert.Equal((true, 3, false), (han.Captured, m.TaskStatus, han.CanEscape));
        Assert.True(han.CapturedByBountyHunters);
        Assert.Same(palace, Missions.Fallback(game, hunter, flag: false));

        // Abode legs take 1 day at any speed.
        var luke = C(game, Refs.Luke);
        Fleets.Travel(game, luke, luke.Container, palace);
        Assert.Equal((true, 1, game.Day + 1, (GameObject)palace), (luke.HyperdriveActive, game.Timers[(0x387, luke.Ref)].Base, luke.Eta, luke.Destination));
        Objects.SetHyperdriveActive(game, luke, false);
        Objects.SetEnrouteActive(game, luke, false);

        // the Return mission's arrival, condensed: Han off missions, held in the palace
        if (han.Mission is { } x) Missions.Leave(game, x, han);
        palace.Add(han);
        int missions = game.Missions.Count;
        game.Clock.Flush(game);
        Assert.True(Story.Has(game, StoryFlag.HanCapturedAtPalace));
        Assert.False(Story.Has(game, StoryFlag.BountyHuntersActive));
        Assert.False(game.Timers[(0x393, 0xF8000006)].Active);
        Missions.Leave(game, m, hunter); // its 0x312: the hunter disbands
        game.Clock.Flush(game);
        Assert.Equal((true, 0x14), (hunter.Destroyed, hunter.DestroyedReason));
        var palaceMissions = game.Missions.Skip(missions).Where(p => p.Type == Story.PalaceType).ToList();
        // The HanCapturedAtPalace hook refreshes the Alliance view of Han first. So the Palace missions
        // target the palace, not Han's capture location, and the flush ends.
        Assert.Same(palace, Knowledge.Location(game, han, Owner.Alliance));
        Assert.Contains(palaceMissions, p => p.Team.Any(a => a.Ref == Refs.Luke) && p.Objective == han && p.TargetKey == palace);
        Assert.All(palaceMissions, p => Assert.Same(palace, p.TargetKey));
    }

    // A Palace mission without success captures every character member and blocks escape.
    [GameFact]
    public void PalaceFailureCapturesTheMembers()
    {
        var game = Game();
        var leia = C(game, Refs.Leia);
        var m = Missions.Create(game, Story.PalaceType, Owner.Alliance, C(game, Refs.Han), [leia], [], 0, mandatory: true);
        Story.PalaceFailed(game, m);
        Assert.Equal((2, true, false), (m.TaskStatus, leia.Captured, leia.CanEscape));
    }

    // Luke held with the heritage and Force >= 60, Vader and Palpatine idle: Vader elsewhere gets a Move mission to Luke.
    // All three together give FinalBattle: Luke's Force >= G 0x1807 wins (Vader and Palpatine captured without escape),
    // and FinalBattleReady is cleared.
    [GameFact]
    public void FinalBattle()
    {
        var game = Game();
        var (luke, vader, pal) = (C(game, Refs.Luke), C(game, Refs.Vader), C(game, Refs.Palpatine));
        var coruscant = game.System(Refs.Coruscant);
        var yavin = game.System(Refs.Yavin);
        yavin.Add(vader); // placed directly (a set-parent across systems would start a travel)
        coruscant.Add(pal);
        Jedi.SetForceRank(game, luke, game.Params.General(0x1807));
        Story.SetLukeKnowsHeritage(game, luke);
        coruscant.Add(luke);
        Prisoners.Capture(game, luke, null);
        game.Clock.Flush(game);
        Assert.Equal((true, coruscant), (luke.Captured, luke.Container));
        var move = game.Missions.LastOrDefault(m => m.Type == 0x41000001 && m.Team.Contains(vader));
        Assert.NotNull(move);
        Assert.Same(coruscant, move.Objective);

        // the approach, condensed: Vader arrives and leaves the Move mission
        Objects.SetHyperdriveActive(game, vader, false);
        if (vader.Mission is { } vm) Missions.Leave(game, vm, vader);
        Objects.SetEnrouteActive(game, vader, false);
        coruscant.Add(vader);
        Story.Check(game);
        Assert.True(Story.Has(game, StoryFlag.FinalBattle));
        Assert.False(Story.Has(game, StoryFlag.FinalBattleReady));
        Assert.Equal((false, true, true, false, false), (luke.Captured, vader.Captured, pal.Captured, vader.CanEscape, pal.CanEscape));
    }

    // Deployment check in an abode: Han held at Jabba's Palace (owner 2) and a bounty hunter there stay.
    // Luke (owner 1) at Dagobah (owner 3) autoroutes. Abodes take characters and special forces only.
    [GameFact]
    public void DeploymentCheckInAbodes()
    {
        var game = Game();
        var palace = game.Abode(Story.JabbasPalace);
        var dagobah = game.Abode(Story.Dagobah);
        var (han, luke) = (C(game, Refs.Han), C(game, Refs.Luke));
        dagobah.Add(luke);
        Fleets.DeploymentCheck(game, luke); // before Han is held (that sends Luke to the palace)
        Assert.Equal((dagobah, true), (luke.Container, luke.Autorouting));
        palace.Add(han);
        Prisoners.Capture(game, han, null);
        game.Clock.Flush(game);
        Assert.True(han.Captured);
        var hunter = (Agent)Objects.Create(game, Story.BountyHunter, Owner.Empire, palace, completed: true);
        foreach (var o in new GameObject[] { han, hunter }) Fleets.DeploymentCheck(game, o);
        Assert.Equal((Owner.Empire, palace, false, false), (han.Owner, han.Container, han.Destroyed, han.Autorouting));
        Assert.Equal((palace, false, false), (hunter.Container, hunter.Destroyed, hunter.Autorouting));

        Assert.False(Fleets.CanPlace(game, palace, 0x10000002, Owner.Empire, 0x3F0));
        Assert.False(Fleets.CanPlace(game, dagobah, luke.Type, Owner.Alliance, 0x241));
        Assert.True(Fleets.CanPlace(game, dagobah, luke.Type, Owner.Alliance, 0x240));
    }
}
