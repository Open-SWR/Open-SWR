using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    public void Init(GameState game)
    {
        _game = game;
        SideNumber = (int)Side;
        // 1. settings victory flag (headquarters mode) gives the model's victory flag; catalogue; cap / dem.
        // shortcut: one catalogue field is not specced and stays 0.
        if (game.Settings.VictoryFlag == 1) HeadquartersMode = true;
        Catalogue = new Catalogue(game.Definitions);
        Budget(game);
        Summary(game);
        MinorCharactersToCome = MinorCharacters;
        (Turn, NextTurn) = (0, 1); // producer-facility turn
        // garrison and defence constants, in ModelConstant order
        int[] k = [3, 3, 2, 1, 1, 0, 0, 3, 9, 0, 6, 12];
        for (int i = 0; i < k.Length; i++) Constant((ModelConstant)i) = k[i];
        // 2. system records
        foreach (var s in game.Systems) Systems[s] = new AiSystem(s, this) { Key = (int)s.Ref, Side = (int)Side };
        // 3. sector records: slot i = the i-th system of the sector's contents (key order)
        foreach (var sector in game.Sectors.OrderBy(x => x.Ref))
        {
            var r = new AiSector(sector) { Key = (int)sector.Ref };
            foreach (var s in game.Systems.Where(s => s.Sector == sector).OrderBy(s => s.Ref).Take(10))
            {
                var sr = Systems[s];
                sr.Sector = r;
                r.Systems.Add(sr);
            }
            Sectors.Add(r);
            SystemSlots += 10;
        }
        // 4. ship records (fleet records: round 7)
        foreach (var o in game.Created.Where(o => Family.IsCapitalShip(o.Type)))
            if (HasView(o) && !See(o).Destroyed) Ships[o.Ref] = NewShip(o);
        // 5. agent records
        // every view of the side (fresh, Destroyed and enemy ones included); Inferred: unplaced characters have none
        foreach (var a in game.Characters.Where(c => c.Placed).Cast<Agent>().Concat(game.Created.OfType<Agent>()))
            CreateAgent(a);
        Dirty = true;
        // 6. a first full update
        Update(game);
        Snapshot(game, record: true);
    }

    // ---- views (the AI reads its side's views only) ----

    internal Seen See(GameObject o) => Knowledge.See(_game, o, Side);
    internal bool Live(GameObject o) => Knowledge.IsLive(o, Side);
    View? Frozen(GameObject o) => _game.Views.GetValueOrDefault((o, Side));
    bool HasView(GameObject o) => Live(o) || Frozen(o) is not null;
    internal GameObject? ViewParent(GameObject o) => o is ManufacturingManager m ? m.System : Live(o) ? o.Container : Frozen(o)?.Parent; // a manager view's parent is its system (never changes)
    /// <summary>shortcut: frozen views keep no Usable flag; read as Completed and not Enroute and not Destroyed. Keep Usable in the view when a reader needs the exact bit.</summary>
    internal bool Usable(GameObject o) => Live(o) ? o.Usable : Frozen(o) is { Completed: true, Enroute: false, Destroyed: false };

    /// <summary>The system's direct contents in the side's view tree that are not Destroyed in the view (mode-3 walks), key order.</summary>
    internal List<GameObject> Contents(GameObject s)
    {
        _frozenContents ??= _game.Views.Where(e => e.Key.Item2 == Side && e.Value.Parent is not null && !Live(e.Key.Item1))
            .GroupBy(e => e.Value.Parent!).ToDictionary(g => g.Key, g => g.Select(e => e.Key.Item1).ToList());
        return [.. s.Contents.Where(Live).Concat(_frozenContents.GetValueOrDefault(s) ?? []).Where(x => !See(x).Destroyed).OrderBy(x => x.Key)];
    }

    // ---- notifications ----
}
