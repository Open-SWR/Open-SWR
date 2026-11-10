using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

// Rule checks on the shipped data.
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
        Assert.Equal(game.Systems.Where(s => s.Definition.Common.Family == Families.CoreSystem).OrderBy(s => s.Definition.Common.Index)
            .Concat(game.Systems.Where(s => s.Definition.Common.Family == Families.RimSystem).OrderBy(s => s.Definition.Common.Index)), game.Systems);
    }

    [GameFact]
    public void SpecialSystemsAndQuotas()
    {
        var game = New();
        var coruscant = game.Known.Coruscant!;
        var yavin = game.Known.Yavin!;
        var hq = game.HeadquartersSystem!;
        Assert.Equal((Owner.Empire, 0), (coruscant.Owner, coruscant.Support));
        Assert.Equal((Owner.Alliance, 100), (yavin.Owner, yavin.Support));
        Assert.False(hq.IsCore);
        Assert.NotSame(yavin, hq);
        Assert.Equal((Owner.Alliance, 100), (hq.Owner, hq.Support));
        Assert.Contains(hq.Contents, o => o.Type == Refs.HeadquartersType); // Alliance Headquarters from FACLHQTB

        // Quotas: Alliance A + B, Empire C + D core systems (C includes Coruscant)
        var p = game.Rules;
        int n = game.Systems.Count(s => s.IsCore);
        Assert.Equal(GameMath.Pct(n, p.For(Owner.Alliance).SetupLoyalShare) + GameMath.Pct(n, p.For(Owner.Alliance).SetupDisloyalShare), game.Systems.Count(s => s.IsCore && s.Owner == Owner.Alliance));
        Assert.Equal(GameMath.Pct(n, p.For(Owner.Empire).SetupLoyalShare) + GameMath.Pct(n, p.For(Owner.Empire).SetupDisloyalShare), game.Systems.Count(s => s.IsCore && s.Owner == Owner.Empire));
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
        Assert.Same(game.Known.Coruscant!, C(Refs.Palpatine).Container);
        Assert.Same(game.HeadquartersSystem, C(Refs.Mothma).Container);
        Assert.Same(game.Known.Yavin!, C(Refs.Luke).Container);
        var luke = C(Refs.Luke);
        Assert.True(luke.KnownJedi);
        Assert.Equal(50, luke.ForceRank); // JediLevel 50 + roll(0), potential 100 %
        Assert.Equal(0, C(Refs.Leia).ForceRank); // Leia: not a known Jedi

        // galaxy size 2: 2 random minors per side on top of the fixed ones
        int fixedAlliance = 3, fixedEmpire = 5; // Chewbacca, Dodonna, Wedge / Jerjerrod, Ozzel, Piett, Veers, Needa
        Assert.Equal(fixedAlliance + 2, game.Characters.Count(c => c.Placed && c.Owner == Owner.Alliance && Family.Of(c.Type) == Families.MinorCharacter));
        Assert.Equal(fixedEmpire + 2, game.Characters.Count(c => c.Placed && c.Owner == Owner.Empire && Family.Of(c.Type) == Families.MinorCharacter));
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

    [GameFact]
    public void SameSeedSameGame()
    {
        static string Fingerprint(GameState g) => string.Join(";", g.Systems.Select(s =>
            $"{s.Type}:{s.Owner}:{s.Support}:{s.Energy}:{s.RawMaterials}:{string.Join(",", s.Contents.Select(o => o.Type.ToString()))}")) + $"|{g.Random.Draws}";
        Assert.Equal(Fingerprint(New(seed: 99)), Fingerprint(New(seed: 99)));
        Assert.NotEqual(Fingerprint(New(seed: 99)), Fingerprint(New(seed: 100)));
    }
}
