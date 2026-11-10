namespace OpenSWR.Sim;

/// <summary>
/// A finder row: the object (system, fleet, ship, character, or a location for the troop / special-force rows),
/// the row text, and for location rows the count per unit type with "all counted Usable" per type.
/// </summary>
public sealed record FinderRow(GameObject Key, string Text, int[]? Counts = null, bool[]? Usable = null);

/// <summary>
/// Finder row queries, from the player side's views only. Rows are sorted by text, case-insensitive,
/// stable (ties keep build order, which is ascending key for systems). The original compares with `_stricmp`;
/// this uses an ordinal case-insensitive compare (Inferred equivalent).
/// UI only: no state change.
/// </summary>
public static class Finders
{
    public const int SystemFinder = 0x14, FleetFinder = 0x15, TroopFinder = 0x16, PersonnelFinder = 0x17;

    static List<FinderRow> Sorted(IEnumerable<FinderRow> rows) => [.. rows.OrderBy(r => r.Text, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// System Finder: every system view of the side (ascending key) with filter 1 all, 2 view owner 1, 3 owner 2, 4 owner 3
    /// and Explored, 5 not Explored; row text = the name.
    /// </summary>
    public static List<FinderRow> Systems(GameState game, Owner side, int filter, Func<GameObject, string> name) =>
        Sorted(game.Systems.OrderBy(s => s.Key).Where(s =>
        {
            var v = Knowledge.See(game, s, side);
            return filter switch
            {
                2 => v.Owner == Owner.Alliance, 3 => v.Owner == Owner.Empire, 4 => v.Owner == Owner.Neutral && v.Explored, 5 => !v.Explored, _ => true,
            };
        }).Select(s => new FinderRow(s, name(s))));

    /// <summary>The view parent of an object for side s (real container while own / live).</summary>
    public static GameObject? ViewParent(GameState game, GameObject o, Owner side) =>
        ScreenViews.Sees(o, side) ? o.Container : game.Views.TryGetValue((o, side), out var v) ? v.Parent : null;

    /// <summary>
    /// Fleet Finder: every view of the side in [0x08, 0x10) (fleets) or [0x14, 0x1C) (<paramref name="ships"/>) that is
    /// Existing with a view parent outside staging; filter 2 / 3 = view owner 1 / 2.
    /// </summary>
    public static List<FinderRow> Fleets(GameState game, Owner side, bool ships, int filter, Func<GameObject, string> name)
    {
        IEnumerable<GameObject> candidates = ships ? game.Created.Where(o => Family.IsCapitalShip(o.Type)) : game.Fleets;
        return Sorted(candidates.OrderBy(o => o.Key).Select(o => (o, v: ScreenViews.View(game, o, side)))
            .Where(x => x.v.Existing && ViewParent(game, x.o, side) is { } p && p != game.Staging && Family.Of(p.Type) != 0xF2
                && (filter == 1 || x.v.Owner == (filter == 2 ? Owner.Alliance : Owner.Empire)))
            .Select(x => new FinderRow(x.o, name(x.o))));
    }

    /// <summary>Troop types per side, in the order of the count cells.</summary>
    public static readonly uint[][] TroopTypes =
        [[0x10000002, 0x10000001, 0x10000005, 0x10000003, 0x10000004], [0x10000008, 0x1000000A, 0x10000007, 0x10000006, 0x10000009]];
    /// <summary>Special-force types per side, in the order of the count cells.</summary>
    public static readonly uint[][] SpecialForceTypes =
        [[0x3C000004, 0x3C000002, 0x3C000001, 0x3C000003], [0x3C000007, 0x3C000006, 0x3C000008, 0x3C000005]];

    /// <summary>
    /// Troop Finder / SpecForces rows: over every system view of the side (ascending key): a row for the system when
    /// it directly holds units of [lo, hi) of side <paramref name="s"/> (Existing), then a row per fleet there of owner s whose
    /// capital ships hold such units (counts summed over its ships). Counts per type of <paramref name="types"/>; a cell's Usable =
    /// every counted unit Usable.
    /// </summary>
    public static List<FinderRow> Locations(GameState game, Owner side, Owner s, uint lo, uint hi, uint[] types, Func<GameObject, string> name)
    {
        var rows = new List<FinderRow>();
        FinderRow? Row(GameObject key, List<ViewObject> units)
        {
            units = [.. units.Where(u => u.In(lo, hi) && u.Owner == s)];
            if (units.Count == 0) return null;
            return new FinderRow(key, name(key), [.. types.Select(t => units.Count(u => u.Object.Type == t))],
                [.. types.Select(t => units.Where(u => u.Object.Type == t).All(u => u.Usable))]);
        }
        foreach (var system in game.Systems.OrderBy(x => x.Key))
        {
            var contents = ScreenViews.Existing(game, system, side);
            if (Row(system, contents) is { } here) rows.Add(here);
            foreach (var fleet in contents.Where(c => c.In(0x08, 0x10) && c.Owner == s))
            {
                var aboard = ScreenViews.Ships(game, fleet.Object, side).SelectMany(x => ScreenViews.Existing(game, x.Object, side)).ToList();
                if (Row(fleet.Object, aboard) is { } row) rows.Add(row);
            }
        }
        return Sorted(rows);
    }

    /// <summary>Definition side of a character: 1 when only the Alliance flag is set, 2 when only the Empire flag, else 0.</summary>
    public static Owner DefinitionSide(Character c) =>
        c.Definition.Alliance != 0 && c.Definition.Empire == 0 ? Owner.Alliance : c.Definition.Empire != 0 && c.Definition.Alliance == 0 ? Owner.Empire : Owner.None;

    /// <summary>
    /// Personnel Finder characters: every character [0x30, 0x3C) whose side view is Created and whose definition side is
    /// the filter. Text = name + 6295 + location (Destroyed: 34712 Killed for DestroyedReason 0x14-0x16, else 34713
    /// Retired (branch Inferred); else the view parent's name when a system, capital ship or abode, else 6293), then " ( status ) "
    /// (Captured 34630, on a mission [0x50, 0x80) 34629, Injury 34631, Enroute 34628; at most one), then " ( command ) " (1 gives 34818, 2 gives 34817,
    /// 3 gives 34816). Injury and command kind are not kept in views: the real values (Inferred).
    /// </summary>
    public static List<FinderRow> Characters(GameState game, Owner side, Owner filter, Func<GameObject, string> name, Func<int, string> text) =>
        Sorted(game.Characters.Where(c => Family.Of(c.Type) is >= 0x30 and < 0x3C).OrderBy(c => c.Key)
            .Where(c => DefinitionSide(c) == filter && (ScreenViews.Sees(c, side) ? c.Created : game.Views.TryGetValue((c, side), out var v) && v.Created))
            .Select(c => new FinderRow(c, CharacterText(game, side, c, name, text))));

    public static string CharacterText(GameState game, Owner side, Character c, Func<GameObject, string> name, Func<int, string> text)
    {
        var v = ScreenViews.View(game, c, side);
        string location;
        if (v.Destroyed) location = text(c.DestroyedReason is >= 0x14 and <= 0x16 ? 34712 : 34713);
        else location = ViewParent(game, c, side) is { } p && (p is StarSystem || Family.IsCapitalShip(p.Type) || Family.Of(p.Type) is >= 0x98 and < 0xA0)
            ? name(p) : text(6293);
        string result = name(c) + text(6295) + location;
        bool captured = Knowledge.See(game, c, side).Captured;
        int status = captured ? 34630 : v.Mission is { Kind: >= 0x50 and < 0x80 } ? 34629 : c.Injury != 0 ? 34631 : v.Enroute ? 34628 : 0;
        if (status != 0) result += " ( " + text(status) + " ) ";
        if (c.CommandKind is >= 1 and <= 3) result += " ( " + text(34819 - c.CommandKind) + " ) ";
        return result;
    }

    /// <summary>
    /// Type-ahead: the walk keeps a best row and length, starting with the first row and 0; a row
    /// without a first-character match is skipped, a longer case-insensitive common prefix replaces the best, an equal
    /// one keeps the earlier row, a shorter one ends the walk. So a non-empty list always yields a row (the first when nothing
    /// matches); -1 only for an empty text or list. The original folds case by the user's locale. This uses the
    /// invariant case fold, which gives the same result for the install's ASCII text (Inferred).
    /// </summary>
    public static int TypeAhead(IReadOnlyList<FinderRow> rows, string typed)
    {
        if (typed.Length == 0 || rows.Count == 0) return -1;
        static char Fold(char c) => char.ToUpperInvariant(c);
        int best = 0, length = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            string t = rows[i].Text;
            int n = 0;
            while (n < typed.Length && n < t.Length && Fold(typed[n]) == Fold(t[n])) n++;
            if (n == 0) continue;
            if (n > length) (best, length) = (i, n);
            else if (n < length) break;
        }
        return best;
    }

    /// <summary>Troop Finder go-to: the first Existing regiment directly at a system, or aboard the first capital ship of a fleet that holds one.</summary>
    public static GameObject? FirstUnit(GameState game, GameObject location, Owner side, uint lo, uint hi)
    {
        if (location is StarSystem s) return ScreenViews.Existing(game, s, side).FirstOrDefault(u => u.In(lo, hi)).Object;
        return ScreenViews.Ships(game, location, side).Select(x => ScreenViews.Existing(game, x.Object, side).FirstOrDefault(u => u.In(lo, hi)).Object)
            .FirstOrDefault(o => o is not null);
    }
}
