using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

public class SaveGameTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    /// <summary>Save gives load gives save gives the same bytes (40 days of a fresh game with no orders: pending timers, views).</summary>
    [GameFact]
    public void SaveLoadSaveIsByteEqual()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        game.Sides[Owner.Alliance].Speed = Clock.TicksForSpeed(4);
        game.Clock.RunUntilDay(game, 40);
        var first = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(first), Definitions);
        Assert.Equal(first, Save(loaded));
        Assert.Equal((game.Day, game.Random.State, game.Clock.Serial), (loaded.Day, loaded.Random.State, loaded.Clock.Serial));
    }

    /// <summary>Enum states keep their names through a save: a captured character, a pending command's group mode and an advisor's helper kind.</summary>
    [GameFact]
    public void EnumStatesSaveByName()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var human = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance);
        game.Controllers[Owner.Alliance] = human;
        human.Advisor = new Advisor(Owner.Alliance, () => game.Clock.Tick, briefing: null, tips: false, rand: _ => 0);
        human.Advisor.HelperStart(AdvisorMenu.BuildShips);
        var coruscant = game.Known.Coruscant!;
        var ship = coruscant.Contents.OfType<Fleet>().Where(f => f.Owner == Owner.Empire && f.Usable).SelectMany(f => f.Contents).First(x => Family.IsCapitalShip(x.Type));
        var captive = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Usable && !c.OnMission && Family.Of(c.Type) < Families.MinorCharacter);
        coruscant.Add(captive);
        game.Clock.Flush(game);
        Prisoners.Capture(game, captive, ship);
        game.Clock.Flush(game); // a save waits for the queued notifications
        Orders.Submit(game, new Command(CommandType.Move, Owner.Alliance) { Targets = [(game.Systems[0], GroupMode.Fleets)] });

        var first = Save(game);
        var text = System.Text.Encoding.UTF8.GetString(first);
        // Captured is the default (0), so the save leaves the state out; the captive flag is written.
        Assert.True(text.Contains("\"captured\":true"), "captive flag");
        Assert.True(text.Contains("\"Fleets\""), "group mode name");
        Assert.True(text.Contains("\"Ships\""), "helper kind name");

        var loaded = SaveGame.Load(new MemoryStream(first), Definitions);
        Assert.Equal((PrisonerState.Captured, true), (loaded.Characters.First(c => c.Key == captive.Key).State, loaded.Characters.First(c => c.Key == captive.Key).Captured));
        Assert.Equal(GroupMode.Fleets, loaded.Clock.PendingCommands.Single().Targets[0].Mode);
        Assert.Equal(BuildHelperKind.Ships, ((OpenSWR.Sim.Ai.HumanController)loaded.Controllers[Owner.Alliance]).Advisor!.HelperKind);
        // the process-wide UI generator moves with other tests running in parallel, so its state is left out
        static string Text(byte[] b) => System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(b), "\"uiRngState\":-?[0-9]+", "");
        Assert.Equal(Text(first), Text(Save(loaded)));
    }

    [GameFact]
    public void LoadRefusesAnotherVersion()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var saved = System.Text.Encoding.UTF8.GetString(Save(game));
        InvalidDataException Refusal(int version) => Assert.Throws<InvalidDataException>(() => SaveGame.Load(
            new MemoryStream(System.Text.Encoding.UTF8.GetBytes(saved.Replace($"\"version\":{SaveGame.Version}", $"\"version\":{version}"))), Definitions));
        Assert.Contains($"version {SaveGame.Version + 1}", Refusal(SaveGame.Version + 1).Message);
        Assert.Equal("This save was made by an older version and can't be loaded.", Refusal(3).Message);
        Assert.Equal("This save was made by an older version and can't be loaded.", Refusal(2).Message);
        Assert.Equal("This save was made by an older version and can't be loaded.", Refusal(1).Message);
        Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream("{}"u8.ToArray()), Definitions));
    }

    /// <summary>The game writes gzip saves; Load reads them like plain ones, a cut one is refused.</summary>
    [GameFact]
    public void LoadReadsGzipSaves()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var plain = Save(game);
        var zipped = new MemoryStream();
        using (var z = new System.IO.Compression.GZipStream(zipped, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(plain);
        Assert.True(zipped.Length * 5 < plain.Length);
        Assert.Equal(plain, Save(SaveGame.Load(new MemoryStream(zipped.ToArray()), Definitions)));
        Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(zipped.ToArray()[..(int)(zipped.Length / 2)]), Definitions));
    }

    /// <summary>A damaged save is refused with <see cref="InvalidDataException"/>, never another exception.</summary>
    [GameFact]
    public void LoadRefusesCorruptSaves()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var text = System.Text.Encoding.UTF8.GetString(Save(game));
        string Mutate(System.Action<System.Text.Json.Nodes.JsonObject> change)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(text)!.AsObject();
            change(root);
            return root.ToJsonString();
        }
        string[] cases =
        [
            "[1]",
            Mutate(r => r["version"] = 1.5),
            Mutate(r => r["tick"] = "x"),
            "{\"format\":\"openswr-save\",\"version\":2}",
            Mutate(r => r["sides"]![0]!["researchPoints"] = new System.Text.Json.Nodes.JsonArray([.. Enumerable.Range(0, 10).Select(i => (System.Text.Json.Nodes.JsonNode?)i)])),
            Mutate(r => r["systems"]![0] = r["fleets"]![0]!.GetValue<uint>()),
            Mutate(r => // a unit type the game data lacks (same family, so references still resolve)
            {
                var o = r["objects"]!.AsArray().First(x => (string?)x!["kind"] == "object")!;
                o["type"] = SdRef.Make(SdRef.FamilyOf(o["type"]!.GetValue<uint>()), 153);
            }),
        ];
        foreach (var c in cases)
            Assert.Throws<InvalidDataException>(() => SaveGame.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(c)), Definitions));
    }

    /// <summary>A save refused while a battle request is open writes nothing.</summary>
    [GameFact]
    public void RefusedSaveWritesNothing()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        game.HostedBattle = game.Systems[0];
        var stream = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => SaveGame.Save(game, stream));
        Assert.Equal(0, stream.Length);
    }
}
