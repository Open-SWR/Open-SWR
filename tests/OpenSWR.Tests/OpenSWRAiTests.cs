using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>The OpenSWR AI: save / load with its state, the controller kind switch, the per-day budget.</summary>
public abstract class OpenSWRAiTests(ITestOutputHelper output)
{
    internal static GameDefinitions? _definitions;

    internal static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>The OpenSWR AI on <paramref name="openswr"/>'s side, the original AI on the other; battles run by the engine.</summary>
    internal static GameState NewGame(int seed, Owner openswr)
    {
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, seed, Opponent: ComputerOpponent.OpenSWR), Definitions);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int state = seed ^ (int)side;
            var ui = new UiRandom(() => state);
            game.Controllers[side] = side == openswr ? new OpenSWRController(side) { IssueTrace = [] } : new ComputerController(side) { Rand = n => ui.Rand(n) };
            game.Sides[side].Speed = 12;
        }
        game.BattleResults = _ => null;
        return game;
    }

    internal static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    internal static GameState Load(byte[] save)
    {
        using var stream = new MemoryStream(save);
        return SaveGame.Load(stream, Definitions, _ => _ => 0);
    }

    internal static string Text(byte[] save) => Regex.Replace(System.Text.Encoding.UTF8.GetString(save), "\"uiRngState\":-?[0-9]+", "");

    /// <summary>The OpenSWR AI with the repo's lines against a human side (no advisor).</summary>
    internal static (GameState Game, OpenSWRController Ai) AgainstHuman(int seed, Owner openswr)
    {
        var game = NewGame(seed, openswr);
        var ai = (OpenSWRController)game.Controllers[openswr];
        ai.Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        var human = openswr == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        game.Controllers[human] = new HumanController(human) { Rand = _ => 0 };
        return (game, ai);
    }

    internal static Owner Other(Owner side) => side == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    /// <summary>The openswr Empire's first 60 days: per day its mission commands (issue order) with the sector map of that day.</summary>
    internal static List<(int Day, OpenSWRController.SectorMap Map, List<Command> Missions, int Scouting)> MissionDays(int seed, int days, Dictionary<Mission, (StarSystem Target, int Day)> seen)
    {
        var game = NewGame(seed, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Agents); // the version 1-7 mission rules (Agents' espionage budget and pool change them)
        var list = new List<(int, OpenSWRController.SectorMap, List<Command>, int)>();
        while (game.Day < days && !game.GameOver)
        {
            int day = game.Day, from = ai.IssueTrace!.Count;
            var map = ai.Sectors(game);
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            foreach (var m in game.Missions.Where(m => m.Owner == Owner.Empire && m.Kind is MissionKind.Espionage or MissionKind.Reconnaissance && m.TaskStatus == MissionTaskStatus.Succeeded && m.Objective is StarSystem))
                seen.TryAdd(m, ((StarSystem)m.Objective!, day)); // the day the scouting succeeded
            int scouting = game.Missions.Count(m => m.Owner == Owner.Empire && !m.Deleted && m.CompletionStatus == MissionCompletion.None && m.Kind is MissionKind.Espionage or MissionKind.Reconnaissance && m.Team.Any(a => a is Character)); // character scouts (special-force scouts come on top, version 3)
            list.Add((day, map, [.. ai.IssueTrace!.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.CreateMission).Select(t => t.Command)], scouting));
        }
        return list;
    }

    internal static MissionKind Kind(Command c) => (MissionKind)Family.Of(c.Fields[2]);

    /// <summary>Gives the side's frozen view of a system the values (a constructed knowledge state; the real system is unchanged).</summary>
    internal static void View(GameState game, StarSystem s, Owner side, Owner owner, int share)
    {
        if (!game.Views.TryGetValue((s, side), out var v)) game.Views[(s, side)] = v = new View();
        (v.Owner, v.Populated, v.Explored, v.Completed, v.Created, v.Usable) = (owner, true, true, true, true, true);
        v.Support = side == Owner.Alliance ? share : 100 - share;
    }

    /// <summary>
    /// The aimatch game of seed 532947196 (galaxy 2, Novice) with the OpenSWR AI as Alliance, where its majors get captured: per
    /// decision the rescue commands (day, captive, the captive's view: Enroute, location) and the mission kinds in issue order.
    /// </summary>
    internal static List<(int Day, List<(MissionKind Kind, GameObject? Target, Seen? Captive)> Missions)> RescueGame(int days, int seed = DefaultSeed)
    {
        var (game, ai) = AllianceGame(seed: seed);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.CovertExperience); // version 9: its 10 % prior stops the rescues this test checks
        var list = new List<(int, List<(MissionKind, GameObject?, Seen?)>)>();
        while (game.Day < days && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count;
            game.Clock.Step(game);
            // after the decision tick: the captives' views as at the decision (the tick's dispatch ran before it; nothing after it moves them)
            var issued = ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.CreateMission)
                .Select(t => (Kind(t.Command), t.Command.ArgRef, t.Command.ArgRef is Character c ? Knowledge.See(game, c, Owner.Alliance) : (Seen?)null)).ToList();
            if (issued.Count > 0) list.Add((ai.Decided, issued));
        }
        return list;
    }

    /// <summary>The aimatch game of seed 532947196 (galaxy 2, Novice): the OpenSWR AI on <paramref name="side"/> (Alliance: home sector in the rim) against the original AI.</summary>
    internal const int DefaultSeed = 532947196;

    internal static (GameState Game, OpenSWRController Ai) AllianceGame(Owner side = Owner.Alliance, int seed = DefaultSeed)
    {
        var other = side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, seed, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { IssueTrace = [] };
        int state = seed ^ (int)other;
        var ui = new UiRandom(() => state);
        game.Controllers[side] = ai;
        game.Controllers[other] = new ComputerController(other) { Rand = n => ui.Rand(n) };
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        return (game, ai);
    }

    /// <summary>The repo's taunt file (godot/Data/openswr-taunts.json), found from the test binaries upwards.</summary>
    internal static string TauntFile
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "OpenSWR.sln"))) return Path.Combine(dir.FullName, "godot", "Data", "openswr-taunts.json");
            throw new FileNotFoundException("OpenSWR.sln not found above the test binaries.");
        }
    }
}
