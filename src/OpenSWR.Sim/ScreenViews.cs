using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// One object as side s sees it (the player side's view): the real values while the side owns it or its view
/// is live, else the frozen view (Existing = Created and not Destroyed, as <see cref="MapFilter"/>; a missing view = a fresh one).
/// <see cref="Mission"/> = the agent's mission (live: Mission while OnMission; frozen: the view's RoleMissionKey).
/// </summary>
public readonly record struct ViewObject(GameObject Object, Owner Owner, bool Existing, bool Usable, bool Enroute, bool Destroyed, bool Completed,
    bool Damaged, Mission? Mission)
{
    public uint Family => OpenSWR.Sim.Family.Of(Object.Type);
    public bool In(uint lo, uint hi) => Family >= lo && Family < hi;
}

/// <summary>
/// Fleet-window presence: fighters, troops, characters aboard, immobile (speed 0). The original's repaint-only markers are not kept.
/// </summary>
public readonly record struct Presence(bool Fighters = false, bool Troops = false, bool Characters = false, bool Immobile = false)
{
    public static readonly Presence None = default;
    /// <summary>Present in either.</summary>
    public Presence Or(Presence other) =>
        new(Fighters || other.Fighters, Troops || other.Troops, Characters || other.Characters, Immobile || other.Immobile);
    /// <summary>Everything present in <paramref name="part"/> is present here.</summary>
    public bool Includes(Presence part) => Or(part) == this;
}

/// <summary>Sector-window icon (group mode facilities / ground / fleets / missions): count and colour side (None = hidden).</summary>
public readonly record struct SectorIcon(GroupMode Mode, int Count, Owner Colour)
{
    public bool Shown => Count > 0 && Colour is Owner.Alliance or Owner.Empire or Owner.Neutral;
}

/// <summary>
/// One system entry of a sector window: picture id (14000 + <see cref="Picture"/>), its top-left in the
/// window, the side's view of the system and the four icons (order 4, 8, 16, 64). Energy / raw material: views keep none,
/// so the real values (Inferred, as <see cref="MapFilter"/>).
/// </summary>
public sealed record SectorSystem(StarSystem System, int Picture, int X, int Y, Owner Owner, bool Destroyed, bool Explored, bool Populated, bool Uprising,
    bool Headquarters, int Energy, int UsedEnergy, int Raw, int UsedRaw, int Support, SectorIcon[] Icons);

/// <summary>
/// Read-only queries of the strategy-screen windows: what the player side sees, through its views
/// (<see cref="Knowledge"/>). UI only: no state change, no draw.
/// </summary>
public static class ScreenViews
{
    public static bool Sees(GameObject o, Owner side) => o.Owner == side || Knowledge.IsLive(o, side);

    public static ViewObject View(GameState game, GameObject o, Owner side)
    {
        if (Sees(o, side))
            return new(o, o.Owner, o.Existing, o.Usable, o.Enroute, o.Destroyed, o.Completed, o.Damaged,
                o is Agent { OnMission: true } a ? a.Mission : null);
        game.Views.TryGetValue((o, side), out var v);
        return new(o, v?.Owner ?? Owner.Neutral, v is { Created: true, Destroyed: false }, v?.Usable ?? false, v?.Enroute ?? false, v?.Destroyed ?? false,
            v?.Completed ?? false, v?.Damaged ?? false, v?.RoleMission);
    }

    /// <summary>
    /// Contents of side's view of a container in key order: the real direct contents the side sees, plus the objects whose frozen
    /// view is parented to the container (their last known state). Every entry, Existing or not (filter with <see cref="ViewObject.Existing"/> = iterator mode 3).
    /// </summary>
    public static List<ViewObject> Contents(GameState game, GameObject container, Owner side)
    {
        var list = container.Contents.Where(o => Sees(o, side)).Select(o => View(game, o, side)).ToList();
        foreach (var ((o, s), v) in game.Views)
            if (s == side && v.Parent == container && !Sees(o, side)) list.Add(View(game, o, side));
        return [.. list.OrderBy(x => x.Object.Key)];
    }

    /// <summary>Existing contents (iterator mode 3) of side's view of a container.</summary>
    public static List<ViewObject> Existing(GameState game, GameObject container, Owner side) => [.. Contents(game, container, side).Where(x => x.Existing)];

    static Owner Enemy(Owner side) => side == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    /// <summary>
    /// Picture top-left of a system in its sector window: (trunc((sx - x0) * 13 * 235 / 1024), trunc((sy - y0) * 10 * 360 / 1024)).
    /// </summary>
    public static (int X, int Y) PicturePosition(StarSystem s) =>
        ((int)Math.Truncate((s.Definition.X - s.Sector.Definition.X) * 13.0 * 235 / 1024), (int)Math.Truncate((s.Definition.Y - s.Sector.Definition.Y) * 10.0 * 360 / 1024));

    /// <summary>The systems of a sector (key order) as <paramref name="side"/> sees them.</summary>
    public static List<SectorSystem> Sector(GameState game, Sector sector, Owner side) =>
        [.. game.Systems.Where(s => s.Sector == sector).OrderBy(s => s.Key).Select(s => SectorEntry(game, s, side))];

    public static SectorSystem SectorEntry(GameState game, StarSystem s, Owner side)
    {
        var v = Knowledge.See(game, s, side);
        var (x, y) = PicturePosition(s);
        var contents = Existing(game, s, side);
        var owner = v.Owner;
        int support = side == Owner.Alliance ? v.Support : 100 - v.Support;
        return new(s, (int)s.Definition.Picture, x, y, owner, v.Destroyed, v.Explored, v.Populated, v.Uprising,
            contents.Any(c => c.In(Families.Headquarters, Families.IonCannon)), s.Energy, s.UsedEnergy, s.RawMaterials, s.UsedRaw, support,
            [new(GroupMode.Facilities, Facilities(contents, owner).Count, owner), new(GroupMode.Ground, PlanetUnits(contents, owner).Count, owner), FleetIcon(contents, side, owner),
             MissionIcon(game, s, side)]);
    }

    /// <summary>Facilities mode: facilities [40, 48) of the view owner.</summary>
    static List<ViewObject> Facilities(List<ViewObject> contents, Owner owner) => [.. contents.Where(c => c.In(Families.Shipyard, Families.MajorCharacter) && c.Owner == owner)];

    /// <summary>
    /// Ground mode: defences [34, 40), troops, fighters and the characters / special forces not on a mission, of the view owner
    /// (the sub-window side filter), plus those on an unlisted (movement) mission.
    /// </summary>
    static List<ViewObject> PlanetUnits(List<ViewObject> contents, Owner owner) =>
        [.. contents.Where(c => c.Owner == owner && (c.In(Families.IonCannon, Families.Shipyard) || c.In(Families.Troop, Families.CapitalShip) || c.In(Families.Fighter, Families.Headquarters) || c.In(Families.MajorCharacter, Families.Mission) && !Listed(c.Mission)))];

    /// <summary>Fleets mode: the player side's fleets if any, else the enemy's, else the owner's.</summary>
    static SectorIcon FleetIcon(List<ViewObject> contents, Owner side, Owner owner)
    {
        int Count(Owner o) => contents.Count(c => c.In(Families.Fleet, Families.Troop) && c.Owner == o);
        int p = Count(side), q = Count(Enemy(side));
        return p > 0 ? new(GroupMode.Fleets, p, side) : q > 0 ? new(GroupMode.Fleets, q, Enemy(side)) : new(GroupMode.Fleets, Count(owner), owner is Owner.Neutral ? Owner.None : owner);
    }

    /// <summary>
    /// A mission the windows list: the type record's hidden flag is clear. Inferred: the flag is the record's Unlisted field,
    /// which is not 0 for the movement types, so a moving agent shows as plain personnel.
    /// </summary>
    public static bool Listed(Mission? m) => m is not null && !m.Record.Unlisted;

    /// <summary>The listed missions (<see cref="Listed"/>) of the agents [48, 64) at the system (Existing), deduplicated, in key order (sort Inferred).</summary>
    public static List<Mission> Missions(GameState game, StarSystem s, Owner side) =>
        [.. Existing(game, s, side).Where(c => c.In(Families.MajorCharacter, Families.Mission) && Listed(c.Mission)).Select(c => c.Mission!).Distinct().OrderBy(m => m.Key)];

    /// <summary>Missions mode: the enemy side if it has missions there, else the player side (Inferred priority).</summary>
    static SectorIcon MissionIcon(GameState game, StarSystem s, Owner side)
    {
        var missions = Missions(game, s, side);
        int p = missions.Count(m => m.Owner == side), q = missions.Count(m => m.Owner == Enemy(side));
        return q > 0 ? new(GroupMode.Missions, q, Enemy(side)) : new(GroupMode.Missions, p, p > 0 ? side : Owner.None);
    }

    /// <summary>
    /// Missions-window skin side: the enemy side when it has missions there, else the player side
    /// when it has, else 3; the sector window's missions rule (which count is the enemy's: Inferred).
    /// </summary>
    public static Owner MissionSide(GameState game, StarSystem s, Owner side) =>
        MissionIcon(game, s, side).Colour is var c && c == Owner.None ? Owner.Neutral : c;

    // ---- system sub-windows: Existing contents of the view, key order, Destroyed views skipped ----

    static List<ViewObject> Of(List<ViewObject> list, uint lo, uint hi) => [.. list.Where(c => c.In(lo, hi) && !c.Destroyed)];

    /// <summary>Fleet window list: every fleet [8, 16) at the system, any owner.</summary>
    public static List<ViewObject> Fleets(GameState game, StarSystem s, Owner side) => Of(Existing(game, s, side), Families.Fleet, Families.Troop);

    /// <summary>A fleet's capital ships [20, 28) (the expanded fleet cell, the Ships tab).</summary>
    public static List<ViewObject> Ships(GameState game, GameObject fleet, Owner side) => Of(Existing(game, fleet, side), Families.CapitalShip, Families.Fighter);

    /// <summary>
    /// Fleet window contents tab (102 ships, 103 fighters, 104 troops, 105 personnel) over the selected fleets: their capital
    /// ships, or what those ships carry (aggregated in fleet, ship, key order; Inferred).
    /// </summary>
    public static List<ViewObject> FleetContents(GameState game, IEnumerable<GameObject> fleets, int tab, Owner side)
    {
        // a selected fleet gives its capital ships, a selected ship row itself; an object already listed is not added twice
        var ships = fleets.SelectMany(f => f is Fleet ? Ships(game, f, side) : [View(game, f, side)]).DistinctBy(x => x.Object).ToList();
        if (tab == 102) return ships;
        var (lo, hi) = tab switch { 103 => (Families.Fighter, Families.Headquarters), 104 => (Families.Troop, Families.CapitalShip), _ => (Families.MajorCharacter, Families.Mission) };
        return [.. ships.SelectMany(x => Of(Existing(game, x.Object, side), lo, hi)).DistinctBy(x => x.Object)];
    }

    /// <summary>
    /// Fleet-window presence of a capital ship as side sees it: fighters, troops,
    /// 4 characters / special forces aboard (Existing contents of its view), speed 0 (<see cref="Orders.Speed"/>; only for a
    /// ship the side sees live, Inferred: a frozen view keeps no speed). The original's repaint-only markers
    /// are left out. A fleet: what any of its capital ships has.
    /// </summary>
    public static Presence PresenceMask(GameState game, GameObject o, Owner side)
    {
        if (o is Fleet) return Ships(game, o, side).Aggregate(Presence.None, (m, s) => m.Or(PresenceMask(game, s.Object, side)));
        var aboard = Existing(game, o, side);
        return new(Fighters: aboard.Any(c => c.In(Families.Fighter, Families.Headquarters)), Troops: aboard.Any(c => c.In(Families.Troop, Families.CapitalShip)),
            Characters: aboard.Any(c => c.In(Families.MajorCharacter, Families.Mission)), Immobile: Sees(o, side) && Orders.Speed(game, o) == 0);
    }

    /// <summary>Presence icons in draw order: (bitmap, x); 15 x 11 each; owner 1 gives A ids, any other owner gives A id + 50.</summary>
    public static List<(int Id, int X)> PresenceIcons(Presence mask, Owner owner)
    {
        int e = owner == Owner.Alliance ? 0 : 50;
        var icons = new List<(int, int)>();
        if (mask.Fighters) icons.Add((10404 + e, 25));
        if (mask.Troops) icons.Add((10405 + e, 41));
        if (mask.Characters) icons.Add((10406 + e, 57));
        if (mask.Immobile) icons.Add((10430 + e, 9));
        return icons;
    }

    /// <summary>
    /// Planet window tab 1-5 of the view owner (side filter = view owner): 1 personnel [48, 64) not on a
    /// mission, 2 troops, 3 fighters, 4 shields [36, 38), 5 batteries [34, 36); directly at the system.
    /// </summary>
    public static List<ViewObject> Planet(GameState game, StarSystem s, int tab, Owner side)
    {
        var owner = Knowledge.See(game, s, side).Owner;
        var all = Existing(game, s, side).Where(c => c.Owner == owner).ToList();
        return tab switch
        {
            1 => [.. Of(all, Families.MajorCharacter, Families.Mission).Where(c => !Listed(c.Mission))],
            2 => Of(all, Families.Troop, Families.CapitalShip),
            3 => Of(all, Families.Fighter, Families.Headquarters),
            4 => Of(all, Families.PlanetaryShield, Families.DeathStarShieldEnd),
            _ => Of(all, Families.IonCannon, Families.PlanetaryShield),
        };
    }

    /// <summary>Manufacturing window facility tabs: the view owner's facilities of [lo, hi).</summary>
    public static List<ViewObject> Facilities(GameState game, StarSystem s, uint lo, uint hi, Owner side)
    {
        var owner = Knowledge.See(game, s, side).Owner;
        return [.. Of(Existing(game, s, side), lo, hi).Where(c => c.Owner == owner)];
    }

    /// <summary>
    /// Manufacturing window tab 103 rows: one per manager of the system, any owner: shipyard (kind 0),
    /// training (2), construction (1). The window reads queue count and TargetKey through the side's view (the manager views in Knowledge; <see cref="Knowledge.QueueCount"/>, <see cref="Knowledge.TargetKey"/>).
    /// </summary>
    public static List<ManufacturingManager> ManagerRows(StarSystem s) =>
        s.Destroyed ? [] : [.. new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction }.Select(k => s.Managers[(int)k]).Where(m => m is not null)];

    /// <summary>The <see cref="ManagerRows"/> of a system the side owns (the side's own managers), else none.</summary>
    public static List<ManufacturingManager> Managers(StarSystem s, Owner side) => s.Owner != side ? [] : ManagerRows(s);

    /// <summary>
    /// Facility cell picture: base by the facility's kind (the SD Index, Inferred): production facilities [40, 44)
    /// 1 is 9006, 2 is 9014, 3 is 9022, 4 is 9010, 5 is 9018, 6 is 9026; Mines / Refineries 1 is 9001, 2 is 9030; not Completed gives base + 1
    /// (+ 2 for a side-1 owner), Completed gives base (+ 3 when the view flag is set).
    /// </summary>
    public static int FacilityPicture(ViewObject f)
    {
        uint kind = SdRef.IndexOf(f.Object.Type);
        int b = f.In(Families.Mine, Families.MajorCharacter) ? (kind == 2 ? 9030 : 9001)
            : kind switch { 1 => 9006, 2 => 9014, 3 => 9022, 4 => 9010, 5 => 9018, _ => 9026 };
        return !f.Completed ? b + (f.Owner == Owner.Alliance ? 2 : 1) : f.Enroute ? b + 3 : b;
    }

    /// <summary>A mission's Agents (Team) or Decoys; mission views are not kept: the real lists (Inferred).</summary>
    public static List<Agent> MissionMembers(Mission m, bool decoys) => [.. decoys ? m.Decoys : m.Team];

    /// <summary>What a picture layer is in the composition: the frame (copied opaque), an overlay or the base (both keyed).</summary>
    public enum PictureRole { Overlay, Frame, Base }

    /// <summary>One layer of an object picture: a GOKRES.DLL (or STRATEGY.DLL) bitmap.</summary>
    public readonly record struct PictureLayer(bool Gokres, int Id, PictureRole Role = PictureRole.Overlay);

    /// <summary>The picture id p: the definition's string number (Inferred: matches the GOKRES ids).</summary>
    public static int PictureId(GameState game, GameObject o) => o switch
    {
        Character c => c.Definition.Common.StringNumber,
        Mission m => m.Record.Common.StringNumber,
        _ => game.Definitions.Resolve(o.Type) is { } r ? r.Common.StringNumber : 0,
    };

    /// <summary>
    /// Object picture in drawing order: the frame (large characters 11500, troops
    /// 11513 / 11514), then with <paramref name="overlays"/> the Enroute, Damaged and not-Completed overlays, the base, and the
    /// Captured overlay on top. The result has the base's size; the composer (<c>IndexedBitmap.Compose</c>) fills it with the base's
    /// key, copies the frame opaque and blits the rest keyed. <paramref name="small"/> = the list's small form (p; the large form is
    /// p + 16384).
    /// </summary>
    public static List<PictureLayer> Picture(GameState game, ViewObject v, Owner side, bool small, bool overlays)
    {
        int p = PictureId(game, v.Object);
        uint f = v.Family;
        bool one = v.Owner == Owner.Alliance;
        int s = small ? 1 : 0;
        var layers = new List<PictureLayer>();
        PictureLayer @base = f is >= Families.Mission and < Families.Sector ? new(true, (one ? 0 : 4096) + (small ? p : p + 16384), PictureRole.Base)
            : f is >= Families.Fleet and < Families.Troop ? new(false, (one ? 10400 : 10450) + 25 * s, PictureRole.Base)
            : new(true, small ? p : p + 16384, PictureRole.Base);
        if (!small && f is >= Families.MajorCharacter and < Families.Mission) layers.Add(new(false, 11500, PictureRole.Frame));
        if (f is >= Families.Troop and < Families.CapitalShip) layers.Add(new(false, 11513 + s, PictureRole.Frame));
        if (!overlays)
        {
            layers.Add(@base);
            return layers;
        }
        if (v.Enroute && !v.Destroyed)
        {
            // Special forces (family 60, index 3 and 5) and troops (family 16, index 2 and 8) have no own Enroute overlay. They take the generic GOKRES overlay (the last branch).
            if (f is >= Families.MajorCharacter and < Families.SpecialForce) { if (!small) layers.Add(new(false, 11501)); }
            else if (f is >= Families.SpecialForce and < Families.Mission && v.Object.Type is not (Refs.LongprobeRecon or Refs.ProbeDroids)) layers.Add(new(false, small ? 11520 : 11501));
            else if (f is >= Families.IonCannon and < Families.Shipyard) layers.Add(new(false, 11509 + s));
            else if (f is >= Families.Headquarters and < Families.MajorCharacter) layers.Add(new(false, 11505 + s));
            else if (f is >= Families.Troop and < Families.CapitalShip && v.Object.Type is not (Refs.AllianceArmy or Refs.EmpireArmy)) layers.Add(new(false, 11515 + s));
            else if (f is >= Families.Fleet and < Families.Troop) layers.Add(new(false, (one ? 10423 : 10473) + 3 * s));
            else layers.Add(new(true, small ? p + 4096 : p + 20480)); // overlay matches its base size (GOKRES p, p + 4096 122 x 50; p + 16384, p + 20480 61 x 25)
        }
        if (v.Damaged)
        {
            if (f is >= Families.MajorCharacter and < Families.Mission) { if (!small) layers.Add(new(false, 11502)); }
            else if (f is >= Families.Fleet and < Families.Troop) layers.Add(new(false, (one ? 10424 : 10474) + 3 * s));
            else layers.Add(new(true, small ? p + 8192 : p + 24576));
        }
        if (!v.Completed) layers.Add(new(false, (one ? 11570 : 11572) + s));
        layers.Add(@base);
        if (!small && f is >= Families.MajorCharacter and < Families.SpecialForce && Knowledge.See(game, v.Object, side).Captured) layers.Add(new(true, p + 28672));
        return layers;
    }

    /// <summary>
    /// Drop (main screen): the commands for a drag from a window of kind <paramref name="sourceKind"/>
    /// with its selection onto a window's targets T_dest / T_move; nothing unless T_move is set. Move id = the move variant with Ctrl, else
    /// Move. Sector window: Facilities entries give the set production target command with (system, Facilities) to T_dest, the other entries (not the object
    /// itself) give the move to T_move; the move first. Fleet and planet windows: the move with the selection; manufacturing: the set production target command with the managers to T_move; missions and others: nothing. Each entry: (command id, selection, target).
    /// </summary>
    public static List<(CommandType Id, List<(GameObject Object, GroupMode Mode)> Selection, GameObject Target)> Drop(WindowKind sourceKind,
        IReadOnlyList<(GameObject Object, GroupMode Mode)> selection, GameObject? dest, GameObject? move, bool ctrl)
    {
        var commands = new List<(CommandType, List<(GameObject, GroupMode)>, GameObject)>();
        if (move is null || selection.Count == 0) return commands;
        var m = ctrl ? CommandType.MoveVariant : CommandType.Move;
        switch (sourceKind)
        {
            case WindowKind.Sector:
                var moves = selection.Where(e => e.Mode is not (GroupMode.Object or GroupMode.Facilities)).Select(e => (e.Object, e.Mode)).ToList();
                var managers = selection.Where(e => e.Mode == GroupMode.Facilities).Select(e => (e.Object, GroupMode.Facilities)).ToList();
                if (moves.Count > 0) commands.Add((m, moves, move));
                if (managers.Count > 0 && dest is not null) commands.Add((CommandType.SetProductionTarget, managers, dest));
                break;
            case WindowKind.SystemFleet or WindowKind.SystemPlanet:
                commands.Add((m, [.. selection], move));
                break;
            case WindowKind.SystemManufacturing:
                commands.Add((CommandType.SetProductionTarget, [.. selection], move));
                break;
        }
        return commands;
    }

    /// <summary>
    /// openswr deviation (product owner): <see cref="Drop(WindowKind, IReadOnlyList{ValueTuple{GameObject, GroupMode}}, GameObject?, GameObject?, bool)"/>,
    /// then a move onto an own fleet takes each unit on its own: the selection resolved (<see cref="Orders.Targets"/>; a fleet
    /// stands for its Existing capital ships; units already directly in the fleet left out), then in key order each unit joins
    /// the accepted set while the move of the set still validates (capital ships into the fleet; troops and fighters aboard a ship
    /// with room, characters and special forces aboard one through the move mission, <see cref="Fleets.Place"/>). The original
    /// refuses the whole move on one unit's error. Every refused unit gives one move of the accepted set plus that unit (it
    /// fails validation: the normal refusal), before the move of the accepted set.
    /// </summary>
    public static List<(CommandType Id, List<(GameObject Object, GroupMode Mode)> Selection, GameObject Target)> Drop(GameState game, Owner side, WindowKind sourceKind,
        IReadOnlyList<(GameObject Object, GroupMode Mode)> selection, GameObject? dest, GameObject? move, bool ctrl)
    {
        var commands = Drop(sourceKind, selection, dest, move, ctrl);
        if (move is not Fleet fleet || fleet.Owner != side) return commands;
        var result = new List<(CommandType, List<(GameObject, GroupMode)>, GameObject)>();
        foreach (var (id, entries, target) in commands)
        {
            if (id is not (CommandType.Move or CommandType.MoveVariant)) { result.Add((id, entries, target)); continue; }
            var units = Orders.Targets(ObjectCommands.Build(id, side, entries, fleet))
                .SelectMany(u => u is Fleet f ? f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing && x.Owner == f.Owner) : [u])
                .Where(u => u != fleet && u.Container != fleet).Distinct().OrderBy(u => u.Ref).ToList();
            // shortcut: validated on the current state, so a troop or fighter cannot board a carrier dropped in the same move; upgrade if players ask
            var kept = new List<(GameObject, GroupMode)>();
            foreach (var u in units)
            {
                List<(GameObject, GroupMode)> trial = [.. kept, (u, GroupMode.Object)];
                if (Orders.Validate(game, ObjectCommands.Build(id, side, trial, fleet)).Success) kept = trial;
                else result.Add((id, trial, fleet));
            }
            if (kept.Count > 0) result.Add((id, kept, fleet));
        }
        return result;
    }

    /// <summary>
    /// openswr deviation (product owner): the fleet a drop on the sector window's fleet icon (fleets mode) moves to: the side's
    /// first Existing, Usable fleet at the system in key order (one icon stands for all of them); null: none (the drop targets the
    /// system).
    /// </summary>
    public static Fleet? IconFleet(StarSystem s, Owner side) =>
        s.Contents.OfType<Fleet>().Where(f => f.Owner == side && f.Existing && f.Usable).OrderBy(f => f.Ref).FirstOrDefault();

    /// <summary>
    /// Go to an object: the system whose sector window opens, the sub-window kind (none = the sector window only) and its container, from side's view of the object (its view parents). Null when the view has no location, is
    /// in the staging container or in an abode.
    /// </summary>
    public static (StarSystem System, WindowKind Kind, GameObject Container)? GoTo(GameState game, GameObject o, Owner side)
    {
        GameObject? Parent(GameObject x) => Sees(x, side) ? x.Container : game.Views.TryGetValue((x, side), out var v) ? v.Parent : null;
        if (o is StarSystem s) return (s, WindowKind.None, s);
        var parent = o is ManufacturingManager mm ? mm.System : Parent(o);
        if (parent is null || parent == game.Staging || Family.Of(parent.Type) is >= Families.Abode and < Families.ConstructionManager) return null;
        uint f = Family.Of(o.Type);
        var system = parent as StarSystem ?? Knowledge.See(game, o, side).Location as StarSystem;
        if (system is null) return null;
        if (o is ManufacturingManager) return (system, WindowKind.SystemManufacturing, system);
        if (f is >= Families.Fleet and < Families.Troop) return (system, WindowKind.SystemFleet, system);
        if (f is >= Families.CapitalShip and < Families.Fighter) return (system, WindowKind.SystemFleet, parent);
        if (f is >= Families.Troop and < Families.CapitalShip or >= Families.Fighter and < Families.Headquarters or >= Families.MajorCharacter and < Families.Mission)
        {
            if (parent is not StarSystem) return (system, WindowKind.SystemFleet, parent);
            if (f >= Families.MajorCharacter && Listed(View(game, o, side).Mission)) return (system, WindowKind.SystemMissions, system); // Inferred: an agent on a mission opens the missions window
            return (system, WindowKind.SystemPlanet, system);
        }
        if (f is >= Families.IonCannon and < Families.Shipyard) return (system, WindowKind.SystemPlanet, system);
        if (f is >= Families.Shipyard and < Families.MajorCharacter) return (system, WindowKind.SystemManufacturing, system);
        if (f is >= Families.Mission and < Families.Sector) return (system, WindowKind.SystemMissions, system);
        return (system, WindowKind.None, system);
    }

    /// <summary>
    /// Transit list of a move confirmation: the Move or move variant command validated (refused: empty,
    /// Inferred), then per sub-command (<see cref="Orders.Expand"/>) every target with its days: the move object command: the travel time to the
    /// destination (<see cref="Orders.TravelDays"/> at the object's speed), the create move mission command: the agent's (Inferred:
    /// a leg to or from an abode takes 1 day); an object's later entry replaces its earlier one;
    /// ascending key (Inferred); days 0 when they cannot be computed.
    /// </summary>
    public static List<(GameObject Object, int Days)> Transit(GameState game, Command c)
    {
        var list = new SortedDictionary<uint, (GameObject, int)>();
        if (!Orders.Validate(game, c).Success) return [];
        var to = c.ArgRef is null ? null : OpenSWR.Sim.Missions.Location(c.ArgRef);
        foreach (var sub in Orders.Expand(c).Subs)
            foreach (var (o, _) in sub.Targets)
            {
                var from = OpenSWR.Sim.Missions.Location(o);
                list[o.Ref] = (o, from is StarSystem a && to is StarSystem b ? Orders.TravelDays(game, a, b, Orders.Speed(game, o))
                    : sub.Type == CommandType.CreateMoveMission && from is not null && to is not null && from != to ? 1 : 0);
            }
        return [.. list.Values];
    }

    /// <summary>
    /// Sector-window slot: left = 0, right = 1. <paramref name="open"/> = the slots in use. None open: right when
    /// the sector's SD X >= 511; one open: the other; both: the sector's half (that window is replaced).
    /// </summary>
    public static int SectorSlot(Sector sector, IReadOnlyCollection<int> open)
    {
        int half = sector.Definition.X >= 511 ? 1 : 0;
        return open.Count == 1 ? 1 - open.First() : half;
    }

    /// <summary>
    /// One row of the battle-start list: the object (its name is the text) or a TEXTSTRA string id
    /// (<paramref name="StringId"/> != 0), font, centred (format 1) or left (the list default), the picture's x in the cell
    /// (-1 = none; the icon form p + 16384 with overlays, top-left at (x, 0)) and the text offset x (0 / 70 / 90).
    /// </summary>
    public readonly record struct BattleStartRow(GameObject? Object, int StringId, int Font, bool Centred, int PictureX, int TextX);

    /// <summary>
    /// Battle-start list rows (rebuild, unit row, contents); every
    /// row one 240 x 40 cell at y = 40 * i. Tabs 1 / 2 (Alliance / Imperial forces, side k): header 6435 / 6436 (font 11, centred);
    /// per Usable fleet of side k directly in the system (key order) a fleet row (font 5, centred, no picture), per Usable capital
    /// ship in it a ship row (font 5, picture at x 0, text at 70), then that ship's Usable fighters, troops and special forces in
    /// that order (font 4, picture at x 20, text at 90); then, if side k has Usable fighters directly in the system, the system's
    /// name (font 5, centred, no picture) and those fighters (font 4, picture at x 0, text at 70). Tab 3 (system assets): header
    /// 6437, the system's Usable facilities, troops and special forces in that order (font 4, picture at x 0, text at 70).
    /// </summary>
    public static List<BattleStartRow> BattleStartRows(StarSystem system, int tab)
    {
        static bool Fighter(GameObject x) => Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters;
        var rows = new List<BattleStartRow>();
        void Units(IEnumerable<GameObject> contents, Func<GameObject, bool>[] classes, int indent)
        {
            var usable = contents.Where(x => x.Usable).ToList();
            foreach (var c in classes)
                rows.AddRange(usable.Where(c).Select(x => new BattleStartRow(x, 0, 4, false, indent, indent == 0 ? 70 : 90)));
        }
        if (tab == 3)
        {
            rows.Add(new(null, 6437, 11, true, -1, 0));
            Units(system.Contents, [x => Family.IsFacility(x.Type), x => Family.IsTroop(x.Type), x => Family.IsSpecialForce(x.Type)], 0);
            return rows;
        }
        var k = tab == 1 ? Owner.Alliance : Owner.Empire;
        rows.Add(new(null, 6434 + tab, 11, true, -1, 0));
        foreach (var f in system.Contents.OfType<Fleet>().Where(f => f.Owner == k && f.Usable))
        {
            rows.Add(new(f, 0, 5, true, -1, 0));
            foreach (var ship in f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable))
            {
                rows.Add(new(ship, 0, 5, false, 0, 70));
                Units(ship.Contents, [Fighter, x => Family.IsTroop(x.Type), x => Family.IsSpecialForce(x.Type)], 20);
            }
        }
        var fighters = system.Contents.Where(x => x.Owner == k && Fighter(x)).ToList();
        if (fighters.Any(x => x.Usable))
        {
            rows.Add(new(system, 0, 5, true, -1, 0));
            Units(fighters, [Fighter], 0);
        }
        return rows;
    }

    /// <summary>
    /// Battle-report unit-tab cells: the columns Operational / Survivors,
    /// Captured (personnel only: <paramref name="captured"/> non-null), Destroyed / Killed filled in parallel rows, one cell per
    /// column per row; a column empty from the start shows one text cell in the first row (Operational / Survivors 34086 "None"
    /// when the other lists are empty too, else 6464 "No Survivors"; Captured 34086; Destroyed / Killed 6465 "No Casualties"), then
    /// blank cells. Rows continue only while the Operational / Survivors or the Destroyed / Killed list still has entries (at least
    /// one row): original behaviour, as coded, Captured entries beyond the longer of those two
    /// columns are not shown.
    /// </summary>
    public static List<(T? Unit, int Text)> ReportCells<T>(IReadOnlyList<T> operational, IReadOnlyList<T>? captured, IReadOnlyList<T> destroyed) where T : class
    {
        var cells = new List<(T?, int)>();
        bool none = destroyed.Count == 0 && (captured is null || captured.Count == 0);
        var columns = captured is null
            ? new[] { (operational, none ? 34086 : 6464), (destroyed, 6465) }
            : [(operational, none ? 34086 : 6464), (captured, 34086), (destroyed, 6465)];
        int rows = Math.Max(1, Math.Max(operational.Count, destroyed.Count));
        for (int r = 0; r < rows; r++)
            foreach (var (list, empty) in columns)
                cells.Add(r < list.Count ? (list[r], 0) : (null, r == 0 && list.Count == 0 ? empty : 0));
        return cells;
    }
}
