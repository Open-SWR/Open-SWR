using OpenSWR.AiMatch;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>tools/aimatch: one short game per side and the shape of its rows, CSV and summary.</summary>
public class AiMatchTests(ITestOutputHelper output)
{
    /// <summary>
    /// Version 7 feature flags: with Features = None the OpenSWR AI plays version 6 exactly. Pinned to the version 6 aimatch run
    /// (commit 8bac510 code, v7base.csv): game 1, seed 200001, galaxy 2, Novice, openswr Alliance, days 50-200 per side (systems,
    /// fleets, capital ships, troops, battles won / lost, commands).
    /// </summary>
    [GameFact]
    public void FeaturesNoneIsVersion6()
    {
        var definitions = GameDefinitions.Load(GameDir.Path);
        var r = Match.Play(definitions, 1, 200001, Owner.Alliance, 2, 0, 200, every: 50, features: []);
        int[][] expected =
        [
            [7, 6, 8, 16, 0, 0, 29, 5, 3, 3, 20, 0, 0, 342],
            [8, 6, 8, 22, 0, 0, 65, 5, 3, 4, 22, 0, 0, 421],
            [10, 6, 8, 47, 0, 0, 92, 5, 4, 4, 23, 0, 0, 563],
            [10, 6, 9, 47, 0, 1, 134, 7, 4, 4, 28, 1, 0, 773],
        ];
        static int[] Row(SideStats x) => [x.Systems, x.Fleets, x.CapitalShips, x.Troops, x.BattlesWon, x.BattlesLost, x.Commands];
        var actual = r.Checkpoints.Select(c => (int[])[.. Row(c.Alliance), .. Row(c.Empire)]).ToList();
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i]);
    }

    /// <summary>
    /// Version 9 time slices: an observer. A fixed-seed game played with the slice recorder gives the same checkpoints, owner
    /// changes, missions and result as without it (no draw, no state change), and records one slice per 100 days.
    /// </summary>
    [GameFact]
    public void SlicesChangeNothing()
    {
        var definitions = GameDefinitions.Load(GameDir.Path);
        var plain = Match.Play(definitions, 1, 200001, Owner.Empire, 2, 0, 300, every: 50);
        var recorder = new SliceRecorder();
        var sliced = Match.Play(definitions, 1, 200001, Owner.Empire, 2, 0, 300, every: 50, slices: recorder);
        Assert.Equal(plain.Checkpoints.Select(c => (c.Day, c.Alliance with { Roles = null }, c.Empire with { Roles = null })),
            sliced.Checkpoints.Select(c => (c.Day, c.Alliance with { Roles = null }, c.Empire with { Roles = null })));
        Assert.Equal(plain.Checkpoints.Select(c => (c.Alliance.Roles, c.Empire.Roles)), sliced.Checkpoints.Select(c => (c.Alliance.Roles, c.Empire.Roles)));
        Assert.Equal(plain.Changes, sliced.Changes);
        Assert.Equal(plain.Missions, sliced.Missions);
        Assert.Equal((plain.Result, plain.Day, plain.OpenSWRShare), (sliced.Result, sliced.Day, sliced.OpenSWRShare));
        Assert.Equal([100, 200, 300], recorder.Slices.Select(s => s.Day));
        // The character and mission recording also runs in the sliced game, and changes nothing.
        Assert.NotEmpty(recorder.Characters.Missions);
        Assert.Contains(recorder.Characters.Missions, m => m.Estimate is not null);
        var csv = SliceRecorder.Csv(sliced).ToList();
        Assert.Equal(6, csv.Count);
        output.WriteLine(SliceRecorder.DiffTable([sliced]));
        Assert.Equal(sliced.Checkpoints.First(c => c.Day == 300).Empire.Systems, recorder.Slices[^1].Empire["systems"]);
    }

    [GameFact]
    public void OneShortGamePerSide()
    {
        var definitions = GameDefinitions.Load(GameDir.Path);
        var results = new List<GameResult>();
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var rows = new List<Checkpoint>();
            var r = Match.Play(definitions, results.Count + 1, 874334749, side, 1, 0, 30, every: 10, row: rows.Add);
            results.Add(r);
            foreach (var c in rows) output.WriteLine(Match.Row(c));
            Assert.Equal(rows, r.Checkpoints);
            Assert.Equal([10, 20, 30], rows.Select(c => c.Day)); // every 10 days; day 30 is also the end
            Assert.All(rows, c => Assert.Equal(side, c.OpenSWR));
            Assert.True(rows[^1].Alliance.Systems > 0 && rows[^1].Empire.Systems > 0);
            Assert.True(rows[^1].Alliance.Commands > 0 && rows[^1].Empire.Commands > 0);
            Assert.Contains(r.Result, new[] { "openswr victory", "original victory", "openswr ahead", "original ahead", "draw" });
            Assert.InRange(r.OpenSWRShare, 0, 1);
            var csv = rows.SelectMany(Match.Csv).ToList();
            Assert.Equal(2 * rows.Count, csv.Count);
            Assert.All(csv, line => Assert.Equal(Match.CsvHeader.Split(',').Length, line.Split(',').Length));
        }
        string summary = Match.Summary(results);
        output.WriteLine(summary);
        var lines = summary.TrimEnd().Split('\n');
        Assert.StartsWith("all", lines[4]); // title, header, Alliance, Empire, all; then the mission table
        Assert.Equal("2", lines[4].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
        Assert.Contains(lines, l => l.StartsWith("openswr missions"));
        Assert.All(results, r => Assert.Equal(Match.PlayerSide, r.PlayerSide));
        // the openswr side sends rule-5 missions from day 1 (not only the game's own movement / story missions, marked *)
        Assert.All(results, r => Assert.Contains(r.Missions.Keys, k => !Match.MissionName(k).EndsWith('*')));
    }

    /// <summary>
    /// Version 5 measurement: every system owner change of a 150-day game is recorded with a cause, the gains and losses add up to
    /// the systems' owner counts, and the cause table lists every cause overall and per period.
    /// </summary>
    [GameFact]
    public void OwnerChangeCauses()
    {
        var definitions = GameDefinitions.Load(GameDir.Path);
        var r = Match.Play(definitions, 1, 532947196, Owner.Alliance, 2, 0, 150, every: 50);
        foreach (var c in r.Changes) output.WriteLine($"day {c.Day}: {c.System:X8} {c.From} → {c.To} {c.Cause}");
        Assert.NotEmpty(r.Changes);
        Assert.All(r.Changes, c => Assert.NotEqual(c.From, c.To));
        Assert.Contains(r.Changes, c => c.Cause != ChangeCause.Other);
        // the end counts follow from the start counts and the recorded changes
        var start = Sim.NewGame.Create(new GameSettings(Match.PlayerSide, 2, 0, 0, 532947196), definitions);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int expected = start.Systems.Count(s => s.Owner == side && !s.Destroyed) + r.Changes.Count(c => c.To == side) - r.Changes.Count(c => c.From == side);
            Assert.Equal(expected, side == Owner.Alliance ? r.Checkpoints[^1].Alliance.Systems : r.Checkpoints[^1].Empire.Systems);
        }
        // version 6: an assault loss per troop loss, with the loser's value and troops; the production mix has every category
        Assert.Equal(r.Changes.Count(c => c.Cause == ChangeCause.Troops && c.From is Owner.Alliance or Owner.Empire), r.AssaultLosses.Count);
        Assert.All(r.AssaultLosses, a => Assert.True(a.Value >= 0 && a.TroopsBefore >= 0 && a.Landed >= 1));
        Assert.Contains(r.Production.Keys, key => key.Category == ProductCategory.Troops);
        string mix = Match.ProductionTable([r]), assaults = Match.AssaultTable([r]);
        output.WriteLine(assaults);
        output.WriteLine(mix);
        Assert.Contains("lost by value", assaults);
        Assert.All(new[] { "capital", "fighters", "troops", "special", "prodfac", "mines", "refineries", "deffac" }, c => Assert.Contains(c, mix));
        // the value of an own system: Coruscant for the Empire counts 100 and more
        var coruscant = start.Systems.First(s => s.Type == Refs.Coruscant);
        Assert.True(OpenSWRController.SystemValue(start, Owner.Empire, coruscant, OpenSWRController.HomeSector(start, Owner.Empire), x => (x.Owner, x.Populated)) >= 110);
        string table = Match.CauseTable([r], 100);
        output.WriteLine(table);
        Assert.All(Enum.GetNames<ChangeCause>(), name => Assert.Contains(name, table));
        Assert.Contains("days 101–200", table);
    }
}
