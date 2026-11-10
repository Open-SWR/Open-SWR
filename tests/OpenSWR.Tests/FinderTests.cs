using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Finder rows from the player side's views: alphabetical order, troop / special-force counts, personnel text, type-ahead.</summary>
public class FinderTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static string Name(GameObject o) => o switch
    {
        StarSystem s => $"S{s.Definition.Common.StringId}",
        Character c => $"c{c.Definition.Common.StringId}",
        _ => $"o{o.Id:X}",
    };

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);

    // System filters: all, owner 1 / 2 (Alliance / Empire), neutral and explored, unexplored. Rows are sorted by text.
    [GameFact]
    public void SystemRows()
    {
        var game = Game();
        var all = Finders.Systems(game, Owner.Alliance, 1, Name);
        Assert.Equal(game.Systems.Count, all.Count);
        Assert.Equal(all.Select(r => r.Text).Order(StringComparer.OrdinalIgnoreCase), all.Select(r => r.Text));
        var own = Finders.Systems(game, Owner.Alliance, 2, Name);
        Assert.All(own, r => Assert.Equal(Owner.Alliance, Knowledge.See(game, r.Key, Owner.Alliance).Owner));
        Assert.Contains(own, r => r.Key.Owner == Owner.Alliance);
        var unexplored = Finders.Systems(game, Owner.Alliance, 5, Name);
        Assert.All(unexplored, r => Assert.False(Knowledge.See(game, r.Key, Owner.Alliance).Explored));
        Assert.Equal(all.Count, own.Count + Finders.Systems(game, Owner.Alliance, 3, Name).Count + Finders.Systems(game, Owner.Alliance, 4, Name).Count
            + unexplored.Count(r => Knowledge.See(game, r.Key, Owner.Alliance).Owner is Owner.Neutral or Owner.None));
    }

    // Fleet rows are Existing views outside staging. Own fleets are listed, unseen enemy fleets are not.
    [GameFact]
    public void FleetRows()
    {
        var game = Game();
        var own = Finders.Fleets(game, Owner.Alliance, ships: false, 2, Name);
        Assert.Equal(game.Fleets.Where(f => f.Owner == Owner.Alliance && f.Existing).Cast<GameObject>().ToHashSet(), own.Select(r => r.Key).ToHashSet());
        var unseen = game.Fleets.Where(f => f.Owner == Owner.Empire && !Knowledge.IsLive(f, Owner.Alliance) && !game.Views.ContainsKey((f, Owner.Alliance)));
        var enemy = Finders.Fleets(game, Owner.Alliance, ships: false, 3, Name).Select(r => r.Key).ToHashSet();
        Assert.DoesNotContain(unseen, f => enemy.Contains(f));
        var ships = Finders.Fleets(game, Owner.Alliance, ships: true, 2, Name);
        Assert.All(ships, r => Assert.True(Family.IsCapitalShip(r.Key.Type)));
        Assert.NotEmpty(ships);
    }

    // Troop rows: one per system with troops, then one per fleet with troops aboard. Five counts in type order.
    // Usable decides the fill colour of each count box.
    [GameFact]
    public void TroopRows()
    {
        var game = Game();
        var rows = Finders.Locations(game, Owner.Alliance, Owner.Alliance, 0x10, 0x14, Finders.TroopTypes[0], Name);
        Assert.NotEmpty(rows);
        foreach (var r in rows)
        {
            var units = r.Key is StarSystem s ? s.Contents.Where(o => Family.IsTroop(o.Type) && o.Owner == Owner.Alliance && o.Existing).ToList()
                : r.Key.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents).Where(o => Family.IsTroop(o.Type) && o.Existing).ToList();
            Assert.Equal(Finders.TroopTypes[0].Select(t => units.Count(u => u.Type == t)), r.Counts!);
            Assert.Equal(units.Count, r.Counts!.Sum());
        }
        var first = rows[0];
        var unit = Finders.FirstUnit(game, first.Key, Owner.Alliance, 0x10, 0x14);
        Assert.NotNull(unit);
        Assert.True(Family.IsTroop(unit!.Type));
    }

    // Personnel rows are listed by definition side, so a captured Alliance character stays under Alliance. Checks the text pieces.
    [GameFact]
    public void PersonnelRows()
    {
        var game = Game();
        string Text(int id) => $"[{id}]";
        var leia = game.Characters.First(c => c.Ref == Refs.Leia);
        var rows = Finders.Characters(game, Owner.Alliance, Owner.Alliance, Name, Text);
        var row = Assert.Single(rows, r => r.Key == leia);
        Assert.StartsWith($"c{leia.Definition.Common.StringId}[6295]", row.Text);
        Assert.DoesNotContain(rows, r => Finders.DefinitionSide((Character)r.Key) != Owner.Alliance);
        (leia.Captured, leia.Owner) = (true, Owner.Empire);
        row = Assert.Single(Finders.Characters(game, Owner.Alliance, Owner.Alliance, Name, Text), r => r.Key == leia);
        Assert.Contains(" ( [34630] ) ", row.Text);
        leia.CommandKind = 3;
        Assert.EndsWith(" ( [34816] ) ", Finders.CharacterText(game, Owner.Alliance, leia, Name, Text));
    }

    // Type-ahead picks the row with the longest case-insensitive prefix match, first in list order. The walk stops
    // when the match gets shorter. Rows whose first character does not match are skipped. If no row matches, the
    // first row is picked. Empty text picks nothing.
    [Fact]
    public void TypeAhead()
    {
        var o = new GameObject(0x90000001, Owner.Neutral, 1);
        List<FinderRow> rows = [new(o, "Alpha"), new(o, "Bespin"), new(o, "Bestine"), new(o, "Corellia")];
        Assert.Equal(1, Finders.TypeAhead(rows, "be"));
        Assert.Equal(2, Finders.TypeAhead(rows, "BESt"));
        Assert.Equal(0, Finders.TypeAhead(rows, "x"));
        Assert.Equal(3, Finders.TypeAhead(rows, "co"));
        Assert.Equal(-1, Finders.TypeAhead([], "x"));
        Assert.Equal(-1, Finders.TypeAhead(rows, ""));
        Assert.Equal(3, Finders.TypeAhead(rows, "cz"));
        // Matching folds case with the invariant culture, so "ec" finds "Ecole".
        Assert.Equal(1, Finders.TypeAhead([new(o, "Alpha"), new(o, "École")], "éc"));
    }
}
