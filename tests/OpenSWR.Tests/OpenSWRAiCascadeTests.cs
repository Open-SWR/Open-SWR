using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Tests;

/// <summary>Cascade: incite cap 1 to 3 in sectors with a support gain or an enemy uprising.</summary>
public class OpenSWRAiCascadeTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 Cascade = OpenSWRController.V7.Cascade;

    static (GameState Game, OpenSWRController Ai) AllianceAiGame(ImmutableHashSet<OpenSWRController.V7> features)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Alliance) { Features = features };
        game.Controllers[Owner.Alliance] = ai;
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    /// <summary>Two populated Empire systems in one sector.</summary>
    static (StarSystem Target, StarSystem Other) EnemyPair(GameState game)
    {
        var pairs = game.Systems.Where(x => x.Owner == Owner.Empire && x.Populated).GroupBy(x => x.Sector).First(g => g.Count() >= 2).OrderBy(x => x.Key).ToList();
        return (pairs[0], pairs[1]);
    }

    /// <summary>An enemy system in uprising in the target's sector (seen) raises the cap to 3 with Cascade; without it, or flag off, it stays 1.</summary>
    [GameFact]
    public void UprisingNextDoorRaisesTheCap()
    {
        foreach (var features in new[] { OpenSWRController.DefaultFeatures, OpenSWRController.DefaultFeatures.Add(Cascade) })
        {
            var (game, ai) = AllianceAiGame(features);
            var (target, other) = EnemyPair(game);
            Assert.Equal(OpenSWRController.MaxIncite, ai.InciteCap(game, target));
            other.Uprising = true;
            Knowledge.Update(game, other, 8, Owner.Alliance);
            Assert.True(Knowledge.See(game, other, Owner.Alliance).Uprising);
            Assert.Equal(features.Contains(Cascade) ? OpenSWRController.CascadeIncite : OpenSWRController.MaxIncite, ai.InciteCap(game, target));
            Assert.Equal(OpenSWRController.MaxIncite, ai.InciteCap(game, other)); // its own uprising does not count
        }
    }

    /// <summary>
    /// An enemy system taken without own troops in control is a support gain: its sector's targets get cap 3 for CascadeDays,
    /// and the record survives a save. Flag off: no record.
    /// </summary>
    [GameFact]
    public void SupportGainRaisesTheCapForCascadeDays()
    {
        foreach (var features in new[] { OpenSWRController.DefaultFeatures, OpenSWRController.DefaultFeatures.Add(Cascade) })
        {
            bool on = features.Contains(Cascade);
            var (game, ai) = AllianceAiGame(features);
            var (target, gained) = EnemyPair(game);
            Knowledge.Update(game, gained, 8, Owner.Alliance);
            ai.OwnerChanges(game);
            Objects.SetOwner(game, gained, Owner.Alliance);
            gained.Control = gained.Control with { Military = Owner.None }; // no military control: a gain by support
            ai.OwnerChanges(game);
            Assert.Equal(on, ai.SupportGains.ContainsKey(gained.Sector.Ref));
            Assert.Equal(on ? OpenSWRController.CascadeIncite : OpenSWRController.MaxIncite, ai.InciteCap(game, target));
            if (!on) continue;
            var j = ai.Save();
            var loaded = new OpenSWRController(Owner.Alliance) { Features = features };
            loaded.Load(game, 0, j);
            Assert.Equal(ai.SupportGains, loaded.SupportGains);
            Assert.Equal(OpenSWRController.CascadeIncite, loaded.InciteCap(game, target));
            j["supportGains"] = new JsonArray(new JsonArray(gained.Sector.Ref, game.Day - OpenSWRController.CascadeDays - 1));
            var old = new OpenSWRController(Owner.Alliance) { Features = features };
            old.Load(game, 0, j);
            Assert.Equal(OpenSWRController.MaxIncite, old.InciteCap(game, target)); // older than CascadeDays
        }
    }

    /// <summary>Cascade is off in the default.</summary>
    [Fact]
    public void FlagOffInTheDefault()
    {
        Assert.True(OpenSWRController.ParseFeatures("Cascade") is { } parsed && parsed.SetEquals([Cascade])); // aimatch --features
        Assert.DoesNotContain(Cascade, OpenSWRController.DefaultFeatures);
    }
}
