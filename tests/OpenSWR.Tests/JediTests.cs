using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Tests;

/// <summary>Force fields and hooks, rank roll, Jedi training, duels, evasion gains, discovery.</summary>
public class JediTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    static void NextChanceSucceeds(GameState game, int p)
    {
        while (new GameRandom(game.Random.State).Percent() >= p) game.Random.Percent();
    }

    // Setting ForceAware later runs the rank roll when the rank is 0 and ForcePotential is set: JediLevel base + roll(JediLevel variance).
    [GameFact]
    public void ForceAwareRollsTheRank()
    {
        var game = Game();
        var leia = game.Known.Leia!;
        (leia.JediPotential, leia.KnownJedi, leia.ForceRank) = (true, false, 0);
        var draws = game.Random.Trace = [];
        Jedi.SetForceAware(game, leia);
        var level = leia.Definition.JediLevel;
        Assert.True(leia.KnownJedi);
        Assert.InRange(leia.ForceRank, (int)level.Base, (int)(level.Base + level.Variance));
        Assert.Equal((DrawSite.ForceRank, (int)level.Variance + 1), (draws[0].Site, draws[0].N));
    }

    // At phase 2 the teacher is a Jedi trainer with Force at least G JediTeacherMinForce. A student's chance is Ft - Fs, and the
    // teacher's own chance is 0. A success gains roll(pct(Ft - Fs, 50)) into ForceTraining and sets TaskStatus 3.
    // The teacher leaving a running mission sets status 11.
    [GameFact]
    public void TrainingMission()
    {
        var game = Game();
        var g = game.Rules;
        var vader = game.Known.Vader!;
        var student = game.Characters.First(c => c.Owner == Owner.Empire && c.Placed && c.Usable && !c.OnMission && c != vader && c.Ref != Refs.Palpatine);
        Jedi.SetForceRank(game, student, 20);
        var type = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.JediTraining).Common.Ref;
        var m = Missions.Create(game, type, Owner.Empire, game.Known.Coruscant!, [vader, student], [], 0, mandatory: false);
        Jedi.TrainingPhase(game, m, 2);
        Assert.Same(vader, m.Teacher);
        int p = vader.Force - student.Force;
        Assert.Equal((p, 0), (Missions.Probability(game, m, student), Missions.Probability(game, m, vader)));

        NextChanceSucceeds(game, p);
        var copy = new GameRandom(game.Random.State);
        copy.Percent();
        int gain = copy.Roll(Pct(p, g.JediTrainingForcePercent));
        var draws = game.Random.Trace = [];
        Jedi.Attempt(game, m, student, p);
        Assert.Equal((DrawSite.JediTraining, DrawSite.JediGain), (draws[0].Site, draws[1].Site));
        Assert.Equal((gain, 20 + gain, gain != 0 ? MissionTaskStatus.Succeeded : MissionTaskStatus.Pending), (student.ForceBonus, student.Force, m.TaskStatus));

        Missions.Leave(game, m, vader);
        Assert.Equal(MissionCompletion.TeacherLeft, m.CompletionStatus);
    }

    // A detected pair uses one command serial. Luke with Vader: a successful evasion gains G LukeEvasionForceGain rank, Luke learns
    // his heritage, and his rank gains max(G LukeVaderForceMinGain, pct(F(Vader) - F(Luke), G LukeVaderForcePercent)). With the heritage and Force
    // at least G FinalBattleMinForce, Luke is FinalBattleReady.
    [GameFact]
    public void LukeMeetsVader()
    {
        var game = Game();
        var g = game.Rules;
        var (luke, vader) = (game.Known.Luke!, game.Known.Vader!);
        (luke.Skills[SkillIndex.Combat], vader.Skills[SkillIndex.Combat]) = (300, 0);
        uint serial = game.Clock.Serial;
        Jedi.Pair(game, luke, vader);
        Assert.Equal((serial + 1, serial + 1, vader, luke), (luke.EncounterSerial, vader.EncounterSerial, luke.EncounterKey, vader.EncounterKey));

        int p = Missions.Table(game, "RLEVADTB", Missions.Enhanced(game, luke, SkillIndex.Combat) - Missions.Enhanced(game, vader, SkillIndex.Combat)) ?? 0;
        NextChanceSucceeds(game, p);
        int rank = luke.ForceRank;
        Jedi.Duel(game, luke, vader);
        int afterEvasion = rank + g.LukeEvasionForceGain;
        int expected = afterEvasion + Math.Max(g.LukeVaderForceMinGain, Pct(vader.Force - afterEvasion, g.LukeVaderForcePercent));
        Assert.Equal((false, expected), (luke.Captured, luke.ForceRank));
        Assert.Equal((null, null, 0u), (luke.EncounterKey, vader.EncounterKey, luke.EncounterSerial));
        Assert.True(luke.KnowsHeritage);
        Assert.Equal(luke.Force >= g.FinalBattleMinForce, luke.FinalBattleReady);
    }

    // Discovery by a trainer finds characters of its owner in the same parent that have ForcePotential but not
    // ForceAware. Leia is found only after Luke knows his heritage.
    [GameFact]
    public void DiscoveryOfLeia()
    {
        var game = Game();
        var (luke, leia) = (game.Known.Luke!, game.Known.Leia!);
        Objects.SetParent(game, leia, luke.Container!);
        (leia.JediPotential, leia.KnownJedi, leia.ForceRank) = (true, false, 0);
        Jedi.SetForceRank(game, luke, game.Rules.ForceDiscoveryMinForce);
        game.Clock.Flush(game);
        Assert.True(luke.ForceActive);

        Jedi.Discover(game, luke);
        Assert.False(leia.KnownJedi);
        Story.SetLukeKnowsHeritage(game, luke);
        Jedi.Discover(game, luke);
        Assert.Equal((true, leia), (leia.KnownJedi, luke.DiscoveredForceUser)); // the key is stored on the discoverer
        Assert.Null(leia.DiscoveredForceUser);
        Assert.True(leia.KnowsHeritage);
        Assert.True(leia.ForceRank >= (int)leia.Definition.JediLevel.Base);
    }

    // The save keeps the discovered key on the discoverer, as the optional field discoveredForceUser. An old save's
    // discoveredBy on the discovered character is moved to its discoverer when the save loads, without a format version change.
    [GameFact]
    public void DiscoveredKeySavesAndOldSavesMigrate()
    {
        var game = Game();
        var (luke, leia) = (game.Known.Luke!, game.Known.Leia!);
        luke.DiscoveredForceUser = leia;
        var json = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(json), Definitions);
        Assert.Equal((Refs.Leia, (uint?)null), (loaded.Known.Luke!.DiscoveredForceUser!.Ref, loaded.Known.Leia!.DiscoveredForceUser?.Ref));

        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        System.Text.Json.Nodes.JsonObject Character(uint type) =>
            root["objects"]!.AsArray().Single(o => o!["type"]!.GetValue<uint>() == type)!["character"]!.AsObject();
        Character(Refs.Luke).Remove("discoveredForceUser");
        Character(Refs.Leia)["discoveredBy"] = Refs.Luke;
        var old = SaveGame.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString())), Definitions);
        Assert.Equal((Refs.Leia, (uint?)null), (old.Known.Luke!.DiscoveredForceUser!.Ref, old.Known.Leia!.DiscoveredForceUser?.Ref));
        Assert.DoesNotContain("discoveredBy", System.Text.Encoding.UTF8.GetString(Save(old)));
    }

    static byte[] Save(GameState game)
    {
        var ms = new MemoryStream();
        SaveGame.Save(game, ms);
        return ms.ToArray();
    }
}
