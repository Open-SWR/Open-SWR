using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Chat lines and the taunt file.</summary>
public class OpenSWRAiChatTests(ITestOutputHelper output) : OpenSWRAiTests(output)
{
    /// <summary>A chat line names a system (and gives a go-to) only when the human side sees it at that moment.</summary>
    [GameFact]
    public void ChatNamesOnlySeenSystems()
    {
        foreach (var openswr in new[] { Owner.Empire, Owner.Alliance })
        {
            var (game, ai) = AgainstHuman(874334749, openswr);
            ai.Names = c => "NAMED";
            int seen = 0;
            while (game.Day < 300 && !game.GameOver)
            {
                game.Clock.Step(game);
                foreach (var m in game.Log.Where(m => m.Category == MessageCategory.Chat && m.Id > seen).ToList())
                {
                    seen = m.Id;
                    output.WriteLine($"{openswr} day {m.Day}: {m.Text}");
                    if (m.Text!.Contains("NAMED")) Assert.True(ScreenViews.Sees(game.Systems.First(x => x.Ref == m.Subject), Other(openswr)));
                    else Assert.Equal(0u, m.Subject);
                }
            }
        }
    }

    /// <summary>No chat line when an own system goes neutral; a line when the human side takes one.</summary>
    [GameFact]
    public void NoChatLineForNeutralOwnerChanges()
    {
        var (game, ai) = AgainstHuman(874334749, Owner.Empire);
        game.Clock.Step(game);
        ai.OwnerChanges(game); // baseline
        var own = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).ToList();
        Systems.StoreControl(game, own[0], own[0].Control with { Owner = Owner.Neutral });
        Assert.Equal(Owner.Neutral, own[0].Owner);
        ai.OwnerChanges(game);
        Assert.DoesNotContain(game.Log, m => m.Category == MessageCategory.Chat);
        Systems.StoreControl(game, own[1], own[1].Control with { Owner = Owner.Alliance });
        Assert.Equal(Owner.Alliance, own[1].Owner);
        ai.OwnerChanges(game);
        Assert.Single(game.Log, m => m.Category == MessageCategory.Chat);
    }

    /// <summary>Chat lines go to a human opponent only, at most one per cooldown; none against the original AI.</summary>
    [GameFact]
    public void ChatLinesHaveACooldown()
    {
        var game = NewGame(874334749, Owner.Empire);
        ((OpenSWRController)game.Controllers[Owner.Empire]).Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 300 && !game.GameOver) game.Clock.Step(game);
        var chat = game.Log.Where(m => m.Category == MessageCategory.Chat).OrderBy(m => m.Id).ToList();
        foreach (var m in chat) output.WriteLine($"day {m.Day}: {m.Text}");
        Assert.NotEmpty(chat);
        Assert.All(chat.Zip(chat.Skip(1)), p => Assert.True(p.Second.Day - p.First.Day >= OpenSWRController.TauntCooldownDays));
        Assert.All(chat, m => Assert.DoesNotContain("{system}", m.Text));
        var against = NewGame(874334749, Owner.Empire);
        ((OpenSWRController)against.Controllers[Owner.Empire]).Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        while (against.Day < 100 && !against.GameOver) against.Clock.Step(against);
        Assert.DoesNotContain(against.Log, m => m.Category == MessageCategory.Chat);
    }

    [Fact]
    public void TauntFileHasLinesForEveryOccasion()
    {
        var taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        foreach (var occasion in Enum.GetValues<OpenSWRTaunts.Occasion>())
        {
            Assert.NotEmpty(taunts.Lines(occasion));
            Assert.All(taunts.Lines(occasion), line => Assert.Contains("{system}", line));
        }
    }

    [Fact]
    public void TauntParser()
    {
        Assert.Throws<InvalidDataException>(() => OpenSWRTaunts.Parse("{ \"battleWon\": [ "));
        Assert.Throws<InvalidDataException>(() => OpenSWRTaunts.Parse("[\"a\"]"));
        // missing occasion, empty list, unknown key, non-string entries: silent occasions, nothing thrown
        var t = OpenSWRTaunts.Parse("{ \"battleWon\": [\"Won at {system}.\", 3, null], \"battleLost\": [], \"systemLost\": \"no list\", \"extra\": [\"x\"] }");
        Assert.Equal(["Won at {system}."], t.Lines(OpenSWRTaunts.Occasion.BattleWon));
        Assert.Empty(t.Lines(OpenSWRTaunts.Occasion.BattleLost));
        Assert.Empty(t.Lines(OpenSWRTaunts.Occasion.SystemLost));
        Assert.Empty(t.Lines(OpenSWRTaunts.Occasion.AttackLaunched));
    }

    /// <summary>Saved line counters beyond a shorter list after the file changed: they wrap, no crash; no lines: no messages.</summary>
    [GameFact]
    public void TauntCountersSurviveAFileChange()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        ai.Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 100 && !game.GameOver) game.Clock.Step(game);
        int before = game.Log.Count(m => m.Category == MessageCategory.Chat);
        var loaded = Load(Save(game));
        var b = (OpenSWRController)loaded.Controllers[Owner.Empire];
        b.Taunts = OpenSWRTaunts.Parse("{ \"battleWon\": [\"W {system}\"], \"battleLost\": [\"L {system}\"], \"systemTaken\": [\"T {system}\"], \"systemLost\": [\"S {system}\"], \"attackLaunched\": [\"A {system}\"] }");
        int resume = loaded.Day;
        while (loaded.Day < resume + OpenSWRController.TauntCooldownDays && !loaded.GameOver) loaded.Clock.Step(loaded); // past the cooldown
        b.OwnerChanges(loaded);
        var lost = loaded.Systems.First(x => x.Owner == Owner.Empire);
        Systems.StoreControl(loaded, lost, lost.Control with { Owner = Owner.Alliance }); // an own system lost to the human side
        b.OwnerChanges(loaded);
        var after = loaded.Log.Where(m => m.Category == MessageCategory.Chat).Skip(before).ToList();
        Assert.NotEmpty(after);
        Assert.All(after, m => Assert.Matches("^[WLTSA] ", m.Text));
        var silent = Load(Save(game)); // Taunts not set: no chat lines
        while (silent.Day < 300 && !silent.GameOver) silent.Clock.Step(silent);
        Assert.Equal(before, silent.Log.Count(m => m.Category == MessageCategory.Chat));
    }
}
