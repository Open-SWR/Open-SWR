using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>One system's marker cell of a map display filter: the glyph bitmap (STRATEGY.DLL, 0 = none) and whether it is hidden.</summary>
public readonly record struct FilterCell(int Glyph, bool Hidden);

/// <summary>
/// Map display filters: per system of the player side's sectors a
/// glyph chosen from a value, a colour side and thresholds, read from the player side's views. UI only: no state
/// change, no draw. OpenSWR readings (Inferred): view contents = the real direct contents the side sees (own or live) plus the
/// objects whose frozen view is parented to the system (Usable / Existing / owner from the frozen view); views keep no energy or
/// raw material, so modes 0x51 / 0x52 read the real values; personnel (0x43 / 0x44) reads the agents the side sees.
/// </summary>
public static class MapFilter
{
    public const int Off = 0x80;

    /// <summary>
    /// Filter title text item: at the map rect's top-left (A (55, 40), E (120, 40)), fixed
    /// width = map rect width (A 485, E 480), fixed width, centred, the stock System font (no font set): drawn centred,
    /// top-aligned, unclipped, prefixes processed; one line high.
    /// </summary>
    public static (float Left, float Top, float Width, float Height, Data.TextFormat Format) Title(bool sideOne, string text) =>
        Data.TextLayout.TextItem(sideOne ? 55 : 120, 40, sideOne ? 485 : 480, 0, text, Data.TextLayout.SystemFont, Data.TextFormat.Centred,
            new Data.TextItemSize(FixedWidth: true));

    /// <summary>
    /// Modes the picker and Alt+1...9 offer. 0x13-0x17 and 0x91-0x93 are advisor-only (the briefing's guided steps): computed
    /// by <see cref="Cells"/> but never offered; 0x30, 0x41, 0x42 draw nothing.
    /// </summary>
    public static bool Supported(int mode) => mode is 0x10 or 0x11 or 0x12 or 0x20 or 0x21 or 0x22 or 0x43 or 0x44
        or (>= 0x51 and <= 0x54) or (>= 0x62 and <= 0x67) or (>= 0x71 and <= 0x75);

    /// <summary>
    /// A load: only the mode is saved. It is re-applied (command 0x140, no list, no custom title)
    /// only when it differs from the mode shown before the load. The draw then plays sound 601, unless the mode is 0x80.
    /// </summary>
    public static bool LoadSound(int shown, int saved) => saved != shown && saved != Off;

    static readonly int[][] Sets = [[10149, 10148, 10147, 10146], [10150, 10151, 10152, 10153], [10154, 10155, 10156, 10157]];
    public const int Conflict = 10160;

    /// <summary>Glyph level: 3 if c >= a; 2 if d <= c <= b; 1 if f <= c <= e; else 0. Set by colour side (1 A, 2 E, 3 N, else none).</summary>
    public static int Glyph(int c, Owner colour, int a, int b, int d, int e, int f = 1)
    {
        if (colour is not (Owner.Alliance or Owner.Empire or Owner.Neutral)) return 0;
        int level = c >= a ? 3 : d <= c && c <= b ? 2 : f <= c && c <= e ? 1 : 0;
        return Sets[(int)colour - 1][level];
    }

    readonly record struct Seen(GameObject Object, Owner Owner, bool Usable, bool Existing, bool Enroute);

    /// <summary>
    /// Cells of every system of the game for <paramref name="side"/> in filter <paramref name="mode"/>; <paramref name="list"/> = the
    /// SD indexes of the one-system list of modes 0x92 / 0x93 (the list the guided advisor step sets).
    /// </summary>
    public static Dictionary<StarSystem, FilterCell> Cells(GameState game, Owner side, int mode, IReadOnlyCollection<int>? list = null)
    {
        // frozen views parented to a system: its view contents beyond the real objects the side sees
        var frozen = game.Views.Where(kv => kv.Key.Item2 == side && kv.Value.Parent is StarSystem && !Sees(kv.Key.Item1, side))
            .ToLookup(kv => (StarSystem)kv.Value.Parent!, kv => new Seen(kv.Key.Item1, kv.Value.Owner, kv.Value.Usable, kv.Value.Created && !kv.Value.Destroyed, kv.Value.Enroute));
        return game.Systems.ToDictionary(s => s, s =>
        {
            var contents = s.Contents.Where(o => Sees(o, side)).Select(o => new Seen(o, o.Owner, o.Usable, o.Existing, o.Enroute)).Concat(frozen[s]).ToList();
            return Cell(game, side, s, mode, contents, list);
        });
    }

    static bool Sees(GameObject o, Owner side) => o.Owner == side || Knowledge.IsLive(o, side);

    static FilterCell Cell(GameState game, Owner side, StarSystem s, int mode, List<Seen> contents, IReadOnlyCollection<int>? list)
    {
        var view = Knowledge.See(game, s, side);
        var enemy = side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        // 0x92 / 0x93: listed gives c = 7, colour 2 / 1; not listed gives hidden. Destroyed views are not hidden, unexplored ones are
        if (mode is 0x92 or 0x93)
            return view.Explored && list?.Contains((int)SdRef.IndexOf(s.Type)) == true ? new(Glyph(7, mode == 0x92 ? Owner.Empire : Owner.Alliance, 6, 5, 3, 2), false) : new(0, true);
        // 0x17: c = 82 for a view that is not Explored, colour 3; the hidden rule inverted (Explored views are hidden)
        if (mode == 0x17) return view.Explored ? new(0, true) : new(Glyph(82, Owner.Neutral, 81, 80, 60, 59, 50), false);
        // Other modes hide unexplored views (except 0x22 / 0x44) and destroyed views (except 0x20, 0x21, 0x22, 0x43, 0x44).
        bool hidden = !view.Explored && mode is not (0x22 or 0x44)
            || view.Destroyed && mode is not (0x20 or 0x21 or 0x22 or 0x43 or 0x44);
        if (hidden) return new(0, true);
        var owner = view.Owner;
        // count [lo, hi) of the owner's Usable objects directly in the system (iterator mode 1)
        int Count(uint lo, uint hi) => contents.Count(x => x.Owner == owner && x.Usable && Family.Of(x.Object.Type) >= lo && Family.Of(x.Object.Type) < hi);
        int Default(int c) => Glyph(c, owner, 6, 5, 3, 2);
        switch (mode)
        {
            case 0x10 or 0x11:
                int share = side == Owner.Alliance ? view.Support : 100 - view.Support;
                return new(Glyph(share, owner, 81, 80, 60, 59, 50), false);
            case 0x12:
                return new(Glyph(view.Uprising ? 1 : 0, owner, 1, 1, 1, 1), false);
            case 0x13 or 0x14 or 0x15 or 0x16:
            {
                // c = 82 when the view owner is the side (0x13 / 0x14: the player, 0x15 / 0x16: the enemy) and the control
                // (read from the view's control) is (0x13 / 0x15) or is not (0x14 / 0x16) that side;
                // thresholds as 0x10, colour = the view owner
                var who = mode is 0x13 or 0x14 ? side : enemy;
                bool control = Knowledge.Control(game, s, side).Support == who;
                bool holds = owner == who && (mode is 0x13 or 0x15 ? control : !control);
                return new(Glyph(holds ? 82 : 0, owner, 81, 80, 60, 59, 50), false);
            }
            case 0x20 or 0x21 or 0x22:
            {
                bool enroute = mode == 0x22; // Inferred: iterator mode 2 = fleets en route to the system
                int Fleets(Owner o) => contents.Count(x => x.Object is Fleet && x.Owner == o && (enroute ? x.Enroute && x.Existing : x.Usable));
                int p = Fleets(side), q = Fleets(enemy);
                if (p > 0 && q > 0) return new(Conflict, false);
                if (q > 0) return new(Glyph(q, enemy, 3, 2, 2, 1), false);
                if (p > 0) return new(Glyph(p, side, 3, 2, 2, 1), false);
                if (enroute && !view.Explored) return new(0, false);
                return new(Glyph(Fleets(owner), owner, 3, 2, 2, 1), false);
            }
            case 0x43 or 0x44:
            {
                var (own, other) = Personnel(s, side, mode == 0x43);
                var colour = own > 0 ? side : other > 0 ? enemy : owner;
                return new(Glyph(own > 0 ? own : other, colour, 1, 1, 1, 1), false);
            }
            case 0x51: return new(Default(s.Energy - s.UsedEnergy), false);
            case 0x52: return new(Glyph(s.RawMaterials - s.UsedRaw, owner, 3, 2, 2, 1), false);
            case 0x53: return new(Default(Count(0x2C, 0x2D)), false);
            case 0x54: return new(Default(Count(0x2D, 0x2E)), false);
            case 0x61: return new(Glyph(Count(0x28, 0x2C) > 0 ? 1 : 0, owner, 1, 1, 1, 1), false);
            case 0x62: return new(Glyph(Count(0x28, 0x29), owner, 5, 4, 2, 1), false);
            case 0x63: return new(Glyph(Count(0x29, 0x2A), owner, 5, 4, 2, 1), false);
            case 0x64: return new(Glyph(Count(0x2A, 0x2B), owner, 5, 4, 2, 1), false);
            case 0x65 or 0x66 or 0x67:
            {
                var kind = mode switch { 0x65 => ManufacturingKind.Ship, 0x66 => ManufacturingKind.Training, _ => ManufacturingKind.Construction };
                // Inferred: the manager has producing members and no queued orders.
                int c = owner == side && s.Managers[(int)kind] is { } m && Economy.Members(m).Count > 0 && m.Orders.Count == 0 ? 15 : 0;
                return new(mode == 0x67 ? Glyph(c, owner, 5, 4, 3, 1) : Glyph(c, owner, 5, 4, 2, 1), false);
            }
            case 0x71: return new(Default(Count(0x10, 0x14)), false);
            case 0x72: return new(Default(Count(0x1C, 0x20)), false);
            case 0x73: return new(Glyph(Count(0x25, 0x26), owner, 1, 1, 1, 1), false);
            case 0x74: return new(Default(Count(0x24, 0x25)), false);
            case 0x75: return new(Default(Count(0x22, 0x23) + Count(0x23, 0x24)), false);
            case 0x91: return new(Default(Count(0x10, 0x14) + Count(0x22, 0x28) + Count(0x1C, 0x20)), false); // troops + [0x22, 0x28) + fighters
        }
        return new(0, false);
    }

    /// <summary>
    /// Personnel: (a) the side's characters and (b) special forces directly at the system, (c) characters aboard the capital
    /// ships of every fleet there and the side's special forces aboard; own / other by owner. Idle = Usable and not OnMission and 
    /// not Captured and no command kind (special forces: Usable and not OnMission); active = the rest. Existing agents only (mode 3).
    /// </summary>
    static (int Own, int Other) Personnel(StarSystem s, Owner side, bool idle)
    {
        static bool Idle(Agent a) => a.Usable && !a.OnMission && a is not Character { Captured: true } && a is not Character { CommandKind: not 0 };
        bool Agent(GameObject o) => Family.Of(o.Type) is >= 0x30 and < 0x40 && o.Existing;
        var direct = s.Contents.OfType<Agent>().Where(a => Agent(a) && a.Owner == side);
        var aboard = s.Contents.OfType<Fleet>().Where(f => Sees(f, side)).SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type)))
            .SelectMany(ship => ship.Contents.OfType<Agent>().Where(a => Agent(a) && (a is Character || a.Owner == side))).ToList();
        int own = direct.Concat(aboard.Where(a => a.Owner == side)).Count(a => Idle(a) == idle);
        int other = aboard.Count(a => a.Owner != side && Idle(a) == idle);
        return (own, other);
    }
}
