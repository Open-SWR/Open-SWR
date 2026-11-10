using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>One system's marker cell of a map display filter: the glyph bitmap (STRATEGY.DLL, 0 = none) and whether it is hidden.</summary>
public readonly record struct FilterCell(int Glyph, bool Hidden);

/// <summary>
/// Map display filters: per system of the player side's sectors a
/// glyph chosen from a value, a colour side and thresholds, read from the player side's views. UI only: no state
/// change, no draw. OpenSWR readings (Inferred): view contents = the real direct contents the side sees (own or live) plus the
/// objects whose frozen view is parented to the system (Usable / Existing / owner from the frozen view); views keep no energy or
/// raw material, so the energy and raw material modes read the real values; the personnel modes read the agents the side sees.
/// </summary>
/// <summary>Map display filter modes (the picker's command ids).</summary>
public static class FilterModes
{
    public const int PopularSupport = 16;
    /// <summary>Same display as PopularSupport. Meaning of the second id in the original is unknown.</summary>
    public const int PopularSupportVariant = 17;
    public const int Uprisings = 18;
    /// <summary>Advisor only: the player's systems it controls.</summary>
    public const int AdvisorOwnControlled = 19;
    /// <summary>Advisor only: the player's systems it does not control.</summary>
    public const int AdvisorOwnUncontrolled = 20;
    public const int AdvisorEnemyControlled = 21;
    public const int AdvisorEnemyUncontrolled = 22;
    /// <summary>Advisor only: unexplored systems.</summary>
    public const int AdvisorUnexplored = 23;
    public const int IdleFleets = 32;
    /// <summary>Same display as IdleFleets. Meaning of the second id in the original is unknown.</summary>
    public const int IdleFleetsVariant = 33;
    public const int FleetsEnroute = 34;
    /// <summary>Advisor only: draws nothing and has no title. Meaning in the original is unknown.</summary>
    public const int AdvisorUntitled = 48;
    public const int IdlePersonnel = 67;
    public const int ActivePersonnel = 68;
    public const int AvailableEnergy = 81;
    public const int AvailableRawMaterial = 82;
    public const int Mines = 83;
    public const int Refineries = 84;
    public const int Manufacturing = 97;
    public const int Shipyards = 98;
    public const int TrainingFacilities = 99;
    public const int ConstructionYards = 100;
    public const int IdleShipyards = 101;
    public const int IdleTrainingFacilities = 102;
    public const int IdleConstructionYards = 103;
    public const int Troopers = 113;
    public const int FighterSquadrons = 114;
    public const int DeathStarShields = 115;
    public const int PlanetaryShields = 116;
    public const int DefenseBatteries = 117;
    public const int Off = 128;
    /// <summary>Advisor only: troops, defences and fighters.</summary>
    public const int AdvisorDefenses = 145;
    /// <summary>Advisor only: the listed systems, Empire colour.</summary>
    public const int AdvisorListEmpire = 146;
    /// <summary>Advisor only: the listed systems, Alliance colour.</summary>
    public const int AdvisorListAlliance = 147;
}

public static class MapFilter
{
    public const int Off = FilterModes.Off;

    /// <summary>
    /// Filter title text item: at the map rect's top-left (A (55, 40), E (120, 40)), fixed
    /// width = map rect width (A 485, E 480), fixed width, centred, the stock System font (no font set): drawn centred,
    /// top-aligned, unclipped, prefixes processed; one line high.
    /// </summary>
    public static (float Left, float Top, float Width, float Height, Data.TextFormat Format) Title(bool sideOne, string text) =>
        Data.TextLayout.TextItem(sideOne ? 55 : 120, 40, sideOne ? 485 : 480, 0, text, Data.TextLayout.SystemFont, Data.TextFormat.Centred,
            new Data.TextItemSize(FixedWidth: true));

    /// <summary>
    /// Modes the picker and Alt+1...9 offer. The advisor modes are advisor-only (the briefing's guided steps): computed
    /// by <see cref="Cells"/> but never offered; the untitled advisor mode and two unnamed modes draw nothing.
    /// </summary>
    public static bool Supported(int mode) => mode is FilterModes.PopularSupport or FilterModes.PopularSupportVariant or FilterModes.Uprisings or FilterModes.IdleFleets or FilterModes.IdleFleetsVariant or FilterModes.FleetsEnroute or FilterModes.IdlePersonnel or FilterModes.ActivePersonnel
        or (>= FilterModes.AvailableEnergy and <= FilterModes.Refineries) or (>= FilterModes.Shipyards and <= FilterModes.IdleConstructionYards)
        or (>= FilterModes.Troopers and <= FilterModes.DefenseBatteries);

    /// <summary>
    /// A load: only the mode is saved. It is re-applied (the map filter command, no list, no custom title)
    /// only when it differs from the mode shown before the load. The draw then plays sound 601, unless the mode is Off.
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
    /// SD indexes of the one-system list of the advisor list modes (the list the guided advisor step sets).
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
        // Advisor list modes: listed gives c = 7, colour 2 / 1; not listed gives hidden. Destroyed views are not hidden, unexplored ones are
        if (mode is FilterModes.AdvisorListEmpire or FilterModes.AdvisorListAlliance)
            return view.Explored && list?.Contains((int)SdRef.IndexOf(s.Type)) == true ? new(Glyph(7, mode == FilterModes.AdvisorListEmpire ? Owner.Empire : Owner.Alliance, 6, 5, 3, 2), false) : new(0, true);
        // Advisor unexplored mode: c = 82 for a view that is not Explored, colour 3; the hidden rule inverted (Explored views are hidden)
        if (mode == FilterModes.AdvisorUnexplored) return view.Explored ? new(0, true) : new(Glyph(82, Owner.Neutral, 81, 80, 60, 59, 50), false);
        // Other modes hide unexplored views (except fleets enroute and active personnel) and destroyed views (except idle fleets, its variant, fleets enroute, idle and active personnel).
        bool hidden = !view.Explored && mode is not (FilterModes.FleetsEnroute or FilterModes.ActivePersonnel)
            || view.Destroyed && mode is not (FilterModes.IdleFleets or FilterModes.IdleFleetsVariant or FilterModes.FleetsEnroute or FilterModes.IdlePersonnel or FilterModes.ActivePersonnel);
        if (hidden) return new(0, true);
        var owner = view.Owner;
        // count [lo, hi) of the owner's Usable objects directly in the system (iterator mode 1)
        int Count(uint lo, uint hi) => contents.Count(x => x.Owner == owner && x.Usable && Family.Of(x.Object.Type) >= lo && Family.Of(x.Object.Type) < hi);
        int Default(int c) => Glyph(c, owner, 6, 5, 3, 2);
        switch (mode)
        {
            case FilterModes.PopularSupport or FilterModes.PopularSupportVariant:
                int share = side == Owner.Alliance ? view.Support : 100 - view.Support;
                return new(Glyph(share, owner, 81, 80, 60, 59, 50), false);
            case FilterModes.Uprisings:
                return new(Glyph(view.Uprising ? 1 : 0, owner, 1, 1, 1, 1), false);
            case FilterModes.AdvisorOwnControlled or FilterModes.AdvisorOwnUncontrolled or FilterModes.AdvisorEnemyControlled or FilterModes.AdvisorEnemyUncontrolled:
            {
                // c = 82 when the view owner is the side (own modes: the player, enemy modes: the enemy) and the control
                // (read from the view's control) is (controlled modes) or is not (uncontrolled modes) that side;
                // thresholds as popular support, colour = the view owner
                var who = mode is FilterModes.AdvisorOwnControlled or FilterModes.AdvisorOwnUncontrolled ? side : enemy;
                bool control = Knowledge.Control(game, s, side).Support == who;
                bool holds = owner == who && (mode is FilterModes.AdvisorOwnControlled or FilterModes.AdvisorEnemyControlled ? control : !control);
                return new(Glyph(holds ? 82 : 0, owner, 81, 80, 60, 59, 50), false);
            }
            case FilterModes.IdleFleets or FilterModes.IdleFleetsVariant or FilterModes.FleetsEnroute:
            {
                bool enroute = mode == FilterModes.FleetsEnroute; // Inferred: iterator mode 2 = fleets en route to the system
                int Fleets(Owner o) => contents.Count(x => x.Object is Fleet && x.Owner == o && (enroute ? x.Enroute && x.Existing : x.Usable));
                int p = Fleets(side), q = Fleets(enemy);
                if (p > 0 && q > 0) return new(Conflict, false);
                if (q > 0) return new(Glyph(q, enemy, 3, 2, 2, 1), false);
                if (p > 0) return new(Glyph(p, side, 3, 2, 2, 1), false);
                if (enroute && !view.Explored) return new(0, false);
                return new(Glyph(Fleets(owner), owner, 3, 2, 2, 1), false);
            }
            case FilterModes.IdlePersonnel or FilterModes.ActivePersonnel:
            {
                var (own, other) = Personnel(s, side, mode == FilterModes.IdlePersonnel);
                var colour = own > 0 ? side : other > 0 ? enemy : owner;
                return new(Glyph(own > 0 ? own : other, colour, 1, 1, 1, 1), false);
            }
            case FilterModes.AvailableEnergy: return new(Default(s.Energy - s.UsedEnergy), false);
            case FilterModes.AvailableRawMaterial: return new(Glyph(s.RawMaterials - s.UsedRaw, owner, 3, 2, 2, 1), false);
            case FilterModes.Mines: return new(Default(Count(Families.Mine, Families.Refinery)), false);
            case FilterModes.Refineries: return new(Default(Count(Families.Refinery, Families.RefineryEnd)), false);
            case FilterModes.Manufacturing: return new(Glyph(Count(Families.Shipyard, Families.Mine) > 0 ? 1 : 0, owner, 1, 1, 1, 1), false);
            case FilterModes.Shipyards: return new(Glyph(Count(Families.Shipyard, Families.TrainingFacility), owner, 5, 4, 2, 1), false);
            case FilterModes.TrainingFacilities: return new(Glyph(Count(Families.TrainingFacility, Families.ConstructionYard), owner, 5, 4, 2, 1), false);
            case FilterModes.ConstructionYards: return new(Glyph(Count(Families.ConstructionYard, Families.ManufacturingEnd), owner, 5, 4, 2, 1), false);
            case FilterModes.IdleShipyards or FilterModes.IdleTrainingFacilities or FilterModes.IdleConstructionYards:
            {
                var kind = mode switch { FilterModes.IdleShipyards => ManufacturingKind.Ship, FilterModes.IdleTrainingFacilities => ManufacturingKind.Training, _ => ManufacturingKind.Construction };
                // Inferred: the manager has producing members and no queued orders.
                int c = owner == side && s.Managers[(int)kind] is { } m && Economy.Members(m).Count > 0 && m.Orders.Count == 0 ? 15 : 0;
                return new(mode == FilterModes.IdleConstructionYards ? Glyph(c, owner, 5, 4, 3, 1) : Glyph(c, owner, 5, 4, 2, 1), false);
            }
            case FilterModes.Troopers: return new(Default(Count(Families.Troop, Families.CapitalShip)), false);
            case FilterModes.FighterSquadrons: return new(Default(Count(Families.Fighter, Families.Headquarters)), false);
            case FilterModes.DeathStarShields: return new(Glyph(Count(Families.DeathStarShield, Families.DeathStarShieldEnd), owner, 1, 1, 1, 1), false);
            case FilterModes.PlanetaryShields: return new(Default(Count(Families.PlanetaryShield, Families.DeathStarShield)), false);
            case FilterModes.DefenseBatteries: return new(Default(Count(Families.IonCannon, Families.Gun) + Count(Families.Gun, Families.PlanetaryShield)), false);
            case FilterModes.AdvisorDefenses: return new(Default(Count(Families.Troop, Families.CapitalShip) + Count(Families.IonCannon, Families.Shipyard) + Count(Families.Fighter, Families.Headquarters)), false); // troops + [34, 40) + fighters
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
        bool Agent(GameObject o) => Family.Of(o.Type) is >= Families.MajorCharacter and < Families.Mission && o.Existing;
        var direct = s.Contents.OfType<Agent>().Where(a => Agent(a) && a.Owner == side);
        var aboard = s.Contents.OfType<Fleet>().Where(f => Sees(f, side)).SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type)))
            .SelectMany(ship => ship.Contents.OfType<Agent>().Where(a => Agent(a) && (a is Character || a.Owner == side))).ToList();
        int own = direct.Concat(aboard.Where(a => a.Owner == side)).Count(a => Idle(a) == idle);
        int other = aboard.Count(a => a.Owner != side && Idle(a) == idle);
        return (own, other);
    }
}
