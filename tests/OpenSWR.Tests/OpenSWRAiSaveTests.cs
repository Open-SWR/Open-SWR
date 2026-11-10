using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Save and load of the controller, the controller kind switch and the save of marks and choices.</summary>
public class OpenSWRAiSaveTests(ITestOutputHelper output) : OpenSWRAiTests(output)
{
    [GameFact]
    public void SaveAndLoadKeepTheController()
    {
        var game = NewGame(874334749, Owner.Empire);
        while (game.Day < 60 && !game.GameOver) game.Clock.Step(game);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        output.WriteLine($"day {game.Day}: commands {ai.IssueTrace!.Count(t => t.Result.Success)}, refused {ai.IssueTrace!.Count(t => !t.Result.Success)}, " +
            $"staging {ai.Staging?.Ref}, target {ai.Target?.Ref}, refusals remembered {ai.Refusals}");
        Assert.Contains(ai.IssueTrace!, t => t.Result.Success);
        var first = Save(game);
        var loaded = Load(first);
        Assert.Equal(ComputerOpponent.OpenSWR, loaded.Settings.Opponent);
        var b = Assert.IsType<OpenSWRController>(loaded.Controllers[Owner.Empire]);
        Assert.IsType<ComputerController>(loaded.Controllers[Owner.Alliance]);
        Assert.Equal((ai.Issued, ai.Decided, ai.Staging?.Ref, ai.Target?.Ref, ai.Refusals, ai.Save().ToJsonString()),
            (b.Issued, b.Decided, b.Staging?.Ref, b.Target?.Ref, b.Refusals, b.Save().ToJsonString()));
        Assert.Equal(Text(first), Text(Save(loaded)));
        // the loaded game goes on
        while (loaded.Day < 70 && !loaded.GameOver) loaded.Clock.Step(loaded);
        Assert.Equal(Text(Save(loaded)), Text(Save(Load(Save(loaded)))));
    }

    /// <summary>The launcher's pairing: a human side (no advisor) against the OpenSWR AI; the save has no computer object.</summary>
    [GameFact]
    public void SaveAndLoadAgainstHuman()
    {
        var game = NewGame(532947196, Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 40 && !game.GameOver) game.Clock.Step(game);
        var first = Save(game);
        var loaded = Load(first);
        Assert.IsType<HumanController>(loaded.Controllers[Owner.Alliance]);
        Assert.IsType<OpenSWRController>(loaded.Controllers[Owner.Empire]);
        Assert.Equal(Text(first), Text(Save(loaded)));
        while (loaded.Day < 50 && !loaded.GameOver) loaded.Clock.Step(loaded);
        Assert.Equal(Text(Save(loaded)), Text(Save(Load(Save(loaded)))));
    }

    /// <summary>Foil marks appear in a real game (seed 532947196, openswr Alliance) and save gives load gives save stays byte-equal with them.</summary>
    [GameFact]
    public void FoilMarksSaveByteEqual()
    {
        var (game, ai) = AllianceGame();
        ai.Features = [OpenSWRController.V7.Fighters, OpenSWRController.V7.Watch, OpenSWRController.V7.LateEconomy]; // the game with a foil by day 400
        while (game.Day < 400 && !game.GameOver && !ai.Save().ContainsKey("foilMarks")) game.Clock.Step(game);
        Assert.True(ai.Save().ContainsKey("foilMarks"), "no foil by day 400");
        output.WriteLine($"day {game.Day}: {ai.Save()["foilMarks"]!.ToJsonString()}");
        var first = Save(game);
        var loaded = Load(first);
        Assert.Equal(Text(first), Text(Save(loaded)));
        Assert.Equal(ai.Save().ToJsonString(), ((OpenSWRController)loaded.Controllers[Owner.Alliance]).Save().ToJsonString());
    }

    /// <summary>A save from before the `seen` field (removed from the text) loads, and the AI goes on.</summary>
    [GameFact]
    public void OldSaveWithoutSeenLoads()
    {
        var game = NewGame(874334749, Owner.Empire);
        while (game.Day < 20 && !game.GameOver) game.Clock.Step(game);
        string text = System.Text.Encoding.UTF8.GetString(Save(game));
        string old = Regex.Replace(text, ",\"seen\":\\[(\\[[0-9]+,[0-9]+\\],?)*\\]", "");
        Assert.NotEqual(text, old);
        var loaded = Load(System.Text.Encoding.UTF8.GetBytes(old));
        Assert.IsType<OpenSWRController>(loaded.Controllers[Owner.Empire]);
        while (loaded.Day < 30 && !loaded.GameOver) loaded.Clock.Step(loaded);
    }

    /// <summary>Save gives load gives save byte-equal after chat lines were posted (the chat state is saved).</summary>
    [GameFact]
    public void SaveAndLoadWithChatLines()
    {
        var (game, _) = AgainstHuman(874334749, Owner.Empire);
        while (game.Day < 150 && !game.GameOver) game.Clock.Step(game);
        Assert.Contains(game.Log, m => m.Category == MessageCategory.Chat);
        var first = Save(game);
        var loaded = Load(first);
        Assert.Equal(Text(first), Text(Save(loaded)));
        ((OpenSWRController)loaded.Controllers[Owner.Empire]).Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        while (loaded.Day < 200 && !loaded.GameOver) loaded.Clock.Step(loaded);
        Assert.Equal(Text(Save(loaded)), Text(Save(Load(Save(loaded)))));
    }

    [GameFact]
    public void OriginalChoiceIsNotSaved()
    {
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        Assert.DoesNotContain("opponent", System.Text.Encoding.UTF8.GetString(Save(game)));
    }

    [GameFact]
    public void UnknownControllerKindIsAnError()
    {
        var game = NewGame(532947196, Owner.Alliance);
        game.Clock.Step(game);
        string text = System.Text.Encoding.UTF8.GetString(Save(game));
        Assert.Contains("\"kind\":4", text);
        var bad = System.Text.Encoding.UTF8.GetBytes(text.Replace("\"kind\":4", "\"kind\":9"));
        Assert.Throws<InvalidDataException>(() => Load(bad));
    }
}
