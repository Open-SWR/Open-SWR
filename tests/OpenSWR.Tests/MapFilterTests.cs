using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Map display filter glyphs, computed from the player side's views.</summary>
public class MapFilterTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    // Glyph choice: level 3 if c >= a, 2 if d <= c <= b, 1 if f <= c <= e, else 0. Sets A / E / N hold the glyphs by level.
    [Fact]
    public void GlyphLevels()
    {
        Assert.Equal(10146, MapFilter.Glyph(6, Owner.Alliance, 6, 5, 3, 2));
        Assert.Equal(10147, MapFilter.Glyph(4, Owner.Alliance, 6, 5, 3, 2));
        Assert.Equal(10148, MapFilter.Glyph(2, Owner.Alliance, 6, 5, 3, 2));
        Assert.Equal(10149, MapFilter.Glyph(0, Owner.Alliance, 6, 5, 3, 2));
        Assert.Equal(10152, MapFilter.Glyph(70, Owner.Empire, 81, 80, 60, 59, 50)); // Obedient
        Assert.Equal(10151, MapFilter.Glyph(55, Owner.Empire, 81, 80, 60, 59, 50)); // Disloyal
        Assert.Equal(10154, MapFilter.Glyph(40, Owner.Neutral, 81, 80, 60, 59, 50)); // Hostile
        Assert.Equal(0, MapFilter.Glyph(9, Owner.None, 1, 1, 1, 1));
    }

    // Unexplored views are hidden, except in modes FleetsEnroute and ActivePersonnel. Counts are of the owner's Usable objects. Support share is of the player side.
    [GameFact]
    public void CellsFromViews()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var support = MapFilter.Cells(game, Owner.Alliance, 17);
        var active = MapFilter.Cells(game, Owner.Alliance, 68);
        foreach (var s in game.Systems)
        {
            var view = Knowledge.See(game, s, Owner.Alliance);
            Assert.Equal(!view.Explored, support[s].Hidden);
            Assert.False(active[s].Hidden);
            if (view.Explored) Assert.Equal(MapFilter.Glyph(view.Support, view.Owner, 81, 80, 60, 59, 50), support[s].Glyph);
        }
        Assert.Contains(support.Values, c => c.Hidden);
        // Mines of an own system: the owner's Usable Mines directly in it, default thresholds
        var own = game.Systems.Where(s => s.Owner == Owner.Alliance).MaxBy(s => s.Contents.Count(o => Family.Of(o.Type) == Families.Mine))!;
        int mines = own.Contents.Count(o => Family.Of(o.Type) == Families.Mine && o.Usable && o.Owner == Owner.Alliance);
        Assert.True(mines > 0);
        Assert.Equal(MapFilter.Glyph(mines, Owner.Alliance, 6, 5, 3, 2), MapFilter.Cells(game, Owner.Alliance, 83)[own].Glyph);
    }

    // Advisor-only modes. AdvisorListEmpire / AdvisorListAlliance show a one-system list by SD index (c = 7, colour 2 / 1, others hidden).
    // AdvisorUnexplored is inverted (unexplored c = 82 colour 3, explored hidden). AdvisorOwnControlled is the own loyal mode (c = 82 when owner and control are the player).
    [GameFact]
    public void AdvisorModes()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var yavin = game.Known.Yavin!;
        var list = MapFilter.Cells(game, Owner.Alliance, 147, [289]);
        Assert.Equal(new FilterCell(MapFilter.Glyph(7, Owner.Alliance, 6, 5, 3, 2), false), list[yavin]);
        Assert.All(list.Where(kv => kv.Key != yavin), kv => Assert.True(kv.Value.Hidden));
        Assert.Equal(MapFilter.Glyph(7, Owner.Empire, 6, 5, 3, 2), MapFilter.Cells(game, Owner.Alliance, 146, [289])[yavin].Glyph);
        var unexplored = MapFilter.Cells(game, Owner.Alliance, 23);
        foreach (var s in game.Systems)
        {
            bool explored = Knowledge.See(game, s, Owner.Alliance).Explored;
            Assert.Equal(explored ? new FilterCell(0, true) : new FilterCell(MapFilter.Glyph(82, Owner.Neutral, 81, 80, 60, 59, 50), false), unexplored[s]);
        }
        var loyal = MapFilter.Cells(game, Owner.Alliance, 19);
        foreach (var s in game.Systems.Where(s => Knowledge.See(game, s, Owner.Alliance).Explored))
        {
            bool holds = s.Owner == Owner.Alliance && s.Control.Support == Owner.Alliance;
            Assert.Equal(MapFilter.Glyph(holds ? 82 : 0, Knowledge.See(game, s, Owner.Alliance).Owner, 81, 80, 60, 59, 50), loyal[s].Glyph);
        }
        Assert.Contains(game.Systems, s => s.Owner == Owner.Alliance && s.Control.Support == Owner.Alliance);
        // AdvisorListEmpire / AdvisorListAlliance still hide unexplored systems: a listed system the player has not explored is hidden.
        var hidden = game.Systems.First(s => !Knowledge.See(game, s, Owner.Alliance).Explored);
        Assert.True(MapFilter.Cells(game, Owner.Alliance, 147, [(int)SdRef.IndexOf(hidden.Type)])[hidden].Hidden);
    }

    // The map filter mode is saved with the game as an optional field. After a load the screen re-applies it:
    // title and cells of IdleFleetsVariant. Off writes nothing, so a save without the field loads with off.
    [GameFact]
    public void FilterModeSaved()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        static byte[] Save(GameState g) { using var m = new MemoryStream(); SaveGame.Save(g, m); return m.ToArray(); }
        var off = Save(game);
        Assert.DoesNotContain("filterMode", System.Text.Encoding.UTF8.GetString(off));
        Assert.Equal(MapFilter.Off, SaveGame.Load(new MemoryStream(off), Definitions).FilterMode);
        game.FilterMode = 33;
        var loaded = SaveGame.Load(new MemoryStream(Save(game)), Definitions);
        Assert.Equal(33, loaded.FilterMode);
        static List<(uint, FilterCell)> Cells(GameState g) => [.. MapFilter.Cells(g, Owner.Alliance, 33).Select(c => (c.Key.Type, c.Value)).OrderBy(c => c.Item1)];
        Assert.Equal(Cells(game), Cells(loaded));
    }

    // Open windows are saved with the game as an optional field: id, position and minimise slot, in order.
    // No windows writes nothing, so an older save without the field loads with no window.
    [GameFact]
    public void WindowListSaved()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        static byte[] Save(GameState g) { using var m = new MemoryStream(); SaveGame.Save(g, m); return m.ToArray(); }
        var none = Save(game);
        Assert.DoesNotContain("windows", System.Text.Encoding.UTF8.GetString(none));
        Assert.Empty(SaveGame.Load(new MemoryStream(none), Definitions).Windows);
        game.Windows.AddRange([new(WindowKind.Sector, 5, 60, 35, -1), new(WindowKind.Messages, 0, 0, 49, -1), new(WindowKind.SystemFleet, 7, 120, 80, 0), new(WindowKind.SystemManufacturing, 7, 200, 90, 3)]);
        var bytes = Save(game);
        var loaded = SaveGame.Load(new MemoryStream(bytes), Definitions);
        Assert.Equal(game.Windows, loaded.Windows);
        Assert.Equal(bytes, Save(loaded));
    }

    static GameState SaveLoad(GameState game)
    {
        var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return SaveGame.Load(new MemoryStream(stream.ToArray()), Definitions);
    }

    // Only the mode is saved. A load re-applies it when it differs from the mode shown and plays sound 601 once,
    // unless the saved mode is Off. Modes AdvisorListEmpire / AdvisorListAlliance come back with an empty list (every system hidden) and no title.
    [GameFact]
    public void FilterModeAfterALoad()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        game.FilterMode = 33;
        var loaded = SaveLoad(game);
        Assert.Equal(33, loaded.FilterMode);
        Assert.True(MapFilter.LoadSound(MapFilter.Off, loaded.FilterMode)); // fresh screen: one 601
        Assert.False(MapFilter.LoadSound(33, loaded.FilterMode)); // equal mode: nothing
        Assert.False(MapFilter.LoadSound(33, MapFilter.Off)); // a saved Off removes the filter silently
        game.FilterMode = 146;
        loaded = SaveLoad(game);
        Assert.Equal(146, loaded.FilterMode);
        var cells = MapFilter.Cells(loaded, Owner.Alliance, loaded.FilterMode, null);
        Assert.DoesNotContain(cells.Values, c => c.Glyph != 0);
        Assert.All(loaded.Systems.Where(s => Knowledge.See(loaded, s, Owner.Alliance).Explored), s => Assert.True(cells[s].Hidden));
    }
}
