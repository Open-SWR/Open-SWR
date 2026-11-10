using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

// Rule checks on the shipped data, plus differential vectors from Frida traces of the original (SetupDrawsMatchOriginal).
public class NewGameTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState New(int galaxySize = 1, Owner side = Owner.Alliance, int difficulty = 1, int seed = 12345) =>
        NewGame.Create(new GameSettings(side, galaxySize, difficulty, 0, seed), Definitions);

    [GameTheory]
    [InlineData(1, 10, 100)]
    [InlineData(2, 15, 150)]
    [InlineData(3, 20, 200)]
    public void GalaxySizeSelectsSectors(int size, int sectors, int systems)
    {
        var game = New(size);
        Assert.Equal(sectors, game.Sectors.Count);
        Assert.Equal(systems, game.Systems.Count);
        Assert.Equal(game.Systems.Where(s => s.Definition.Common.Family == 0x90).OrderBy(s => s.Definition.Common.Index)
            .Concat(game.Systems.Where(s => s.Definition.Common.Family == 0x92).OrderBy(s => s.Definition.Common.Index)), game.Systems);
    }

    [GameFact]
    public void SpecialSystemsAndQuotas()
    {
        var game = New();
        var coruscant = game.System(Refs.Coruscant);
        var yavin = game.System(Refs.Yavin);
        var hq = game.HeadquartersSystem!;
        Assert.Equal((Owner.Empire, 0), (coruscant.Owner, coruscant.Support));
        Assert.Equal((Owner.Alliance, 100), (yavin.Owner, yavin.Support));
        Assert.False(hq.IsCore);
        Assert.NotSame(yavin, hq);
        Assert.Equal((Owner.Alliance, 100), (hq.Owner, hq.Support));
        Assert.Contains(hq.Contents, o => o.Type == 0x20000001); // Alliance Headquarters from FACLHQTB

        // Quotas: Alliance A + B, Empire C + D core systems (C includes Coruscant)
        var p = game.Params;
        int n = game.Systems.Count(s => s.IsCore);
        Assert.Equal(GameMath.Pct(n, p.Side(0x1E00, Owner.Alliance)) + GameMath.Pct(n, p.Side(0x1E01, Owner.Alliance)), game.Systems.Count(s => s.IsCore && s.Owner == Owner.Alliance));
        Assert.Equal(GameMath.Pct(n, p.Side(0x1E00, Owner.Empire)) + GameMath.Pct(n, p.Side(0x1E01, Owner.Empire)), game.Systems.Count(s => s.IsCore && s.Owner == Owner.Empire));
        Assert.All(game.Systems.Where(s => !s.IsCore && s != yavin && s != hq), s => Assert.Equal(Owner.Neutral, s.Owner));
    }

    [GameFact]
    public void SystemValuesWithinSpecRanges()
    {
        var game = New(3, seed: 777);
        foreach (var s in game.Systems)
        {
            Assert.InRange(s.Energy, 0, 15);
            Assert.InRange(s.RawMaterials, 0, s.Energy);
            if (!s.Populated) continue;
            if (s.IsCore && s.Owner == Owner.Neutral) Assert.InRange(s.Support, 41, 59);
            if (s.IsCore && s.Owner == Owner.Alliance && !s.Disloyal) Assert.InRange(s.Support, 60, 90);
            if (s.IsCore && s.Owner == Owner.Empire && !s.Disloyal && s.Type != Refs.Coruscant) Assert.InRange(s.Support, 10, 40);
            if (!s.IsCore && s.Owner == Owner.Neutral) Assert.Equal(50, s.Support);
        }
        // Garrisons: disloyal systems with owner share < 60 hold ceil((60 - share) / 10) army regiments
        foreach (var s in game.Systems.Where(s => s.Disloyal))
        {
            uint army = s.Owner == Owner.Alliance ? Refs.AllianceArmy : Refs.EmpireArmy;
            int share = s.SupportFor(s.Owner);
            Assert.True(s.Contents.Count(o => o.Type == army) >= (share < 60 ? GameMath.CeilDiv(60 - share, 10) : 0));
        }
    }

    [GameFact]
    public void CharactersPlacedAndRolled()
    {
        var game = New(2);
        Character C(uint sdRef) => game.Characters.Single(c => c.Type == sdRef);
        Assert.Same(game.System(Refs.Coruscant), C(0x34000280).Container);
        Assert.Same(game.HeadquartersSystem, C(0x30000240).Container);
        Assert.Same(game.System(Refs.Yavin), C(0x32000242).Container);
        var luke = C(0x32000242);
        Assert.True(luke.KnownJedi);
        Assert.Equal(50, luke.ForceRank); // JediLevel 50 + roll(0), potential 100 %
        Assert.Equal(0, C(0x31000241).ForceRank); // Leia: not a known Jedi

        // galaxy size 2: 2 random minors per side on top of the fixed ones
        int fixedAlliance = 3, fixedEmpire = 5; // Chewbacca, Dodonna, Wedge / Jerjerrod, Ozzel, Piett, Veers, Needa
        Assert.Equal(fixedAlliance + 2, game.Characters.Count(c => c.Placed && c.Owner == Owner.Alliance && Family.Of(c.Type) == 0x38));
        Assert.Equal(fixedEmpire + 2, game.Characters.Count(c => c.Placed && c.Owner == Owner.Empire && Family.Of(c.Type) == 0x38));
        foreach (var c in game.Characters.Where(c => c.Placed))
        {
            var d = c.Definition.Skills;
            Assert.InRange(c.Skills[0], (int)d.Diplomacy.Base, (int)(d.Diplomacy.Base + d.Diplomacy.Variance + 1));
        }
    }

    [GameFact]
    public void BudgetBuysStartingUnits()
    {
        var game = New(3, seed: 4242);
        Assert.NotEmpty(game.FleetsOf(Owner.Alliance));
        Assert.NotEmpty(game.FleetsOf(Owner.Empire));
        Assert.All(game.Fleets.Where(f => f.Real), f => Assert.Contains(f.Contents, o => Family.IsCapitalShip(o.Type)));
        Assert.Equal(4 * game.Systems.Count, game.Fleets.Count(f => !f.Real)); // four placeholder fleets per system
    }

    /// <summary>
    /// Differential test: the setup draw sequence of the original exe (a Frida trace turned into a vector by
    /// tools/frida/make_vector.py) must be reproduced draw by draw, including no-step calls.
    /// Excluded: zero-variance timer starts (no generator step; 0x385 is not modelled). The 0x394 starts are checked with
    /// their targets in DayTickTests.
    /// newgame-925860366: player side not recorded; setup draws are side-independent (column 1 vs 4), so column 1 is Inferred.
    /// </summary>
    [GameTheory]
    [InlineData("newgame-445898697.json", 25)]
    [InlineData("newgame-925860366.json", 25)]
    [InlineData("newgame-874334749.json", 25)]
    [InlineData("newgame-401859171.json", 25)]
    [InlineData("newgame-532947196.json", 25)]
    [InlineData("newgame-421765374.json", 25)]
    public void SetupDrawsMatchOriginal(string file, int throughStep)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Vectors", file)));
        var root = doc.RootElement;
        int seed = root.GetProperty("seed").GetInt32();
        int column = root.GetProperty("settings").GetProperty("column").GetInt32();
        int galaxy = root.GetProperty("settings").GetProperty("galaxySize").GetInt32();
        var expected = root.GetProperty("draws").EnumerateArray()
            .Select(d => (Site: d[0].GetString()!, N: d[1].GetInt32(), Result: d[2].GetInt32(), Step: d[3].GetInt32()))
            .Where(d => d.Step <= throughStep && !(d.Site == DrawSite.Timer && d.N <= 1))
            .ToList();

        var settings = new GameSettings((Owner)((column - 1) / 3 + 1), galaxy, (column - 1) % 3, 0, seed);
        Assert.Equal(column, settings.Column);
        var actual = new List<(string Site, int N, int Result)>();
        NewGame.Create(settings, Definitions, actual);
        actual.RemoveAll(d => d.Site == DrawSite.Timer && d.N <= 1);
        if (throughStep < 23 && actual.Count > expected.Count) actual.RemoveRange(expected.Count, actual.Count - expected.Count); // later steps are cut from the vector

        for (int i = 0; i < Math.Max(expected.Count, actual.Count); i++)
        {
            bool same = i < expected.Count && i < actual.Count && (expected[i].Site, expected[i].N, expected[i].Result) == actual[i];
            if (same) continue;
            string Row(int j) =>
                $"  #{j} expected {(j < expected.Count ? $"step {expected[j].Step} {expected[j].Site} n={expected[j].N} -> {expected[j].Result}" : "(end)")}" +
                $" | actual {(j < actual.Count ? $"{actual[j].Site} n={actual[j].N} -> {actual[j].Result}" : "(end)")}";
            Assert.Fail($"{file}: first divergence at draw {i} of {expected.Count} (actual {actual.Count}):\n" +
                string.Join("\n", Enumerable.Range(Math.Max(0, i - 5), 11).Where(j => j < Math.Max(expected.Count, actual.Count)).Select(Row)));
        }
    }

    [GameFact]
    public void SameSeedSameGame()
    {
        static string Fingerprint(GameState g) => string.Join(";", g.Systems.Select(s =>
            $"{s.Type:X}:{s.Owner}:{s.Support}:{s.Energy}:{s.RawMaterials}:{string.Join(",", s.Contents.Select(o => o.Type.ToString("X")))}")) + $"|{g.Random.Draws}";
        Assert.Equal(Fingerprint(New(seed: 99)), Fingerprint(New(seed: 99)));
        Assert.NotEqual(Fingerprint(New(seed: 99)), Fingerprint(New(seed: 100)));
    }
}
