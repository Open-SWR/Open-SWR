using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Galaxy bitmap with sectors and systems from SECTORSD/SYSTEMSD, in bitmap pixel coordinates. Reports the hovered system.
/// With a <see cref="Game"/> set, shows only that game's systems as <see cref="Side"/> knows them (own objects real, others
/// through the side's views): owner colour, fleet markers (own left-top, enemy right-top, count), Blockade (orange ring),
/// BattlePending (red ring), Uprising (U); the selected fleet's system is circled.
/// </summary>
public partial class GalaxyMap : Node2D
{
    const ushort GalaxyBitmap = 902;
    const float HoverRadius = 4f; // screen pixels

    Texture2D? _galaxy, _dimmed;
    Color _dimModulate = Colors.White;
    int _filter = MapFilter.Off;
    Dictionary<StarSystem, FilterCell> _cells = [];
    Vector2 _logicalSize; // original bitmap size; the texture may later be an upscaled replacement
    List<SectorRecord> _sectors = [];
    List<SystemRecord> _systems = [];
    Dictionary<uint, SectorRecord> _sectorByIndex = [];
    Dictionary<int, string> _names = [];
    IReadOnlyDictionary<uint, string> _modNames = new Dictionary<uint, string>(); // names set by mods, by SdRef
    SystemRecord? _hovered;
    Dictionary<Sector, Rect2> _sectorBoxes = []; // openswr: the union of each sector's 17 x 17 system cells, the map's click target
    Sector? _hoveredSector;
    static readonly FontVariation BoldFont = new() { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.8f };
    bool _showSectors = true;
    GameState? _game;
    Dictionary<SystemRecord, StarSystem> _state = [];

    public event Action? HoverChanged;

    /// <summary>True while something drawn over the map (a window, the legend) is under the mouse: nothing on the map hovers then.</summary>
    public Func<bool>? Covered { get; set; }

    /// <summary>The player's side: whose knowledge the map shows.</summary>
    public SimOwner Side { get; set; } = SimOwner.Alliance;

    /// <summary>Fleet selected for an order (its system is circled), or null.</summary>
    public Fleet? Selected { get; set; }

    /// <summary>The hovered system of the game, or null.</summary>
    public StarSystem? HoveredSystem => _hovered is null ? null : _state.GetValueOrDefault(_hovered);

    /// <summary>
    /// Map display filter mode: 0x80 = the normal map; any other mode draws background 903 and one 17 x 17
    /// marker cell per system (glyph at offset (1, 1), Inferred) from the player side's views (<see cref="MapFilter"/>);
    /// hidden cells show the white dot 10158 (observation O3); fleet markers and system dots are left out.
    /// </summary>
    public int FilterMode
    {
        get => _filter;
        set { _filter = value; Refresh(); }
    }

    /// <summary>The one-system list (SD indexes) of the advisor-only modes 0x92 / 0x93; set before <see cref="FilterMode"/>.</summary>
    public IReadOnlyCollection<int>? FilterList { get; set; }

    /// <summary>Redraws and re-reports the hover text after the game state changed (a day passed, an order ran); recomputes the filter cells (openswr: all cells per refresh).</summary>
    public void Refresh()
    {
        _cells = _game is not null && _filter != MapFilter.Off ? MapFilter.Cells(_game, Side, _filter, FilterList) : [];
        QueueRedraw();
        HoverChanged?.Invoke();
    }

    public Vector2 Size => _logicalSize;
    public int SectorCount => _sectors.Count;
    public int SystemCount => _systems.Count;

    /// <summary>Details of the hovered system, or null.</summary>
    public string? HoverText => _hovered is null ? null : Describe(_hovered);

    /// <summary>Game whose systems are shown; null = all SD systems.</summary>
    public GameState? Game
    {
        get => _game;
        set
        {
            _game = value;
            _state = value?.Systems.ToDictionary(s => s.Definition) ?? [];
            _sectorBoxes = value?.Systems.GroupBy(s => s.Sector).ToDictionary(g => g.Key,
                g => g.Select(s => new Rect2(Pixel(s.Definition.X, s.Definition.Y), new Vector2(17, 17))).Aggregate((a, b) => a.Merge(b))) ?? [];
            _hovered = null;
            _hoveredSector = null;
            Refresh();
        }
    }

    IEnumerable<SystemRecord> VisibleSystems => _game is null ? _systems : _state.Keys;

    public bool ShowSectors
    {
        get => _showSectors;
        set { _showSectors = value; QueueRedraw(); }
    }

    /// <param name="definitions">The game's definitions (mods included); null = the install's SECTORSD / SYSTEMSD.</param>
    public void Load(string gameDir, GameDefinitions? definitions = null)
    {
        var image = GameFiles.LoadBitmap(gameDir, "STRATEGY.DLL", GalaxyBitmap);
        _logicalSize = image.GetSize();
        _galaxy = ImageTexture.CreateFromImage(image);
        var dimmed = GameFiles.LoadBitmap(gameDir, "STRATEGY.DLL", 903);
        _dimmed = ImageTexture.CreateFromImage(dimmed); // filter background
        DrawLog.Register(_galaxy, "STRATEGY.DLL", GalaxyBitmap, image.GetSize(), -1);
        DrawLog.Register(_dimmed, "STRATEGY.DLL", 903, dimmed.GetSize(), -1);
        // a mod's galaxy picture: drawn into the same logical 607 x 437 rect, so the coordinate mapping holds;
        // the filter background is the same picture darkened (openswr's choice: a mod has no second picture)
        _dimModulate = Colors.White;
        if (definitions?.GalaxyPicture is { } path && Image.LoadFromFile(path) is { } mod && !mod.IsEmpty())
        {
            _galaxy = _dimmed = ImageTexture.CreateFromImage(mod);
            DrawLog.Tag(_galaxy, "mod:galaxy");
            _dimModulate = new Color(0.35f, 0.35f, 0.35f);
        }
        else if (definitions?.GalaxyPicture is { } bad) GD.PushError($"mod galaxy picture '{bad}' cannot be read; the install's galaxy is drawn.");

        _sectors = definitions?.Sectors ?? SectorRecord.Read(File.ReadAllBytes(Path.Combine(gameDir, "GData", "SECTORSD.DAT")));
        _systems = definitions?.Systems ?? SystemRecord.Read(File.ReadAllBytes(Path.Combine(gameDir, "GData", "SYSTEMSD.DAT")));
        _modNames = definitions?.Names ?? new Dictionary<uint, string>();
        _sectorByIndex = _sectors.ToDictionary(s => s.Common.Index);
        _names = StringTable.All(PeResources.Load(Path.Combine(gameDir, "TEXTSTRA.DLL")));
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_galaxy is null) return;
        var mouse = GetLocalMousePosition();
        float radius = HoverRadius / GetGlobalTransformWithCanvas().Scale.X;
        var nearest = VisibleSystems.MinBy(s => WorldPos(s).DistanceTo(mouse));
        bool covered = Covered?.Invoke() == true;
        var hovered = !covered && nearest is not null && WorldPos(nearest).DistanceTo(mouse) <= radius ? nearest : null;
        var sector = covered ? null : _sectorBoxes.FirstOrDefault(b => b.Value.HasPoint(mouse)).Key;
        if (hovered == _hovered && sector == _hoveredSector) return;

        _hovered = hovered;
        _hoveredSector = sector;
        QueueRedraw();
        HoverChanged?.Invoke();
    }

    public override void _Draw()
    {
        if (_galaxy is null) return;
        if (_filter != MapFilter.Off && _game is not null)
        {
            DrawTextureRect(_dimmed!, new Rect2(Vector2.Zero, _logicalSize), tile: false, _dimModulate);
            DrawLog.Bitmap(this, _dimmed, new Rect2(Vector2.Zero, _logicalSize));
            foreach (var (system, cell) in _cells)
            {
                var at = Pixel(system.Definition.X, system.Definition.Y);
                int glyph = cell.Hidden ? 10158 : cell.Glyph; // flag-0x80 cells (unexplored / destroyed): the white dot (observation O3)
                if (glyph == 0) continue;
                var g = Assets.Strategy((ushort)glyph);
                DrawTextureRect(g, new Rect2(at + Vector2.One, new Vector2(15, 15)), false);
                DrawLog.Bitmap(this, g, new Rect2(at + Vector2.One, new Vector2(15, 15)));
            }
            DrawSectorNames(); // openswr extra: sector names stay readable in filter views
            return;
        }
        DrawTextureRect(_galaxy, new Rect2(Vector2.Zero, _logicalSize), tile: false);
        DrawLog.Bitmap(this, _galaxy, new Rect2(Vector2.Zero, _logicalSize));

        foreach (var s in VisibleSystems)
        {
            var (at, colour) = (WorldPos(s), SystemColor(s));
            DrawCircle(at, 1.5f, colour);
            DrawLog.Ellipse(this, at, 1.5f, colour, filled: true);
        }
        if (_game is not null)
        {
            var seen = SeenFleets();
            foreach (var (record, state) in _state) DrawMarkers(WorldPos(record), state, seen.GetValueOrDefault(state) ?? []);
        }

        DrawSectorNames();
    }

    void DrawSectorNames()
    {
        if (!_showSectors) return;
        // openswr: in a game, centred over the top edge of the sector's box, bold while hovered;
        // without a game at the sector's SD position
        var boxes = _sectorBoxes.ToDictionary(b => b.Key.Definition, b => (Box: b.Value, Hovered: b.Key == _hoveredSector));
        foreach (var s in _sectors.Where(s => _game is null || s.GalaxySize <= _game.Settings.GalaxySize))
        {
            string name = DisplayName(s.Common);
            bool known = boxes.TryGetValue(s, out var b);
            var font = known && b.Hovered ? BoldFont : ThemeDB.FallbackFont;
            var size = font.GetStringSize(name, fontSize: 8);
            var at = known ? new Vector2(b.Box.GetCenter().X - size.X / 2, b.Box.Position.Y + 1) : Pixel(s.X, s.Y) + new Vector2(8.5f - size.X / 2, 8.5f);
            var colour = new Color(0.5f, 1f, 1f, known && b.Hovered ? 1f : 0.8f);
            DrawString(font, at, name, fontSize: 8, modulate: colour);
            DrawLog.String(this, font, at, name, 8, colour);
        }
    }

    // Own presentation, not the original's: Alliance red, Empire blue, neutral grey; unpopulated dimmed; as the side knows it.
    Color SystemColor(SystemRecord s)
    {
        if (!_state.TryGetValue(s, out var state) || _game is null) return s.Common.Family == 0x90 ? Colors.Gold : Colors.White;
        var seen = Knowledge.See(_game, state, Side);
        return seen.Populated ? OwnerColor(seen.Owner) : OwnerColor(seen.Owner).Darkened(0.6f);
    }

    static Color OwnerColor(SimOwner o) => o switch
    {
        SimOwner.Alliance => new Color(1f, 0.35f, 0.25f),
        SimOwner.Empire => new Color(0.35f, 0.6f, 1f),
        _ => new Color(0.75f, 0.75f, 0.75f),
    };

    /// <summary>
    /// Real fleets (with capital ships) the side sees in the system: its own and live views where they are, frozen views where
    /// they were last seen (as the Fleet Finder and the map filter).
    /// </summary>
    /// <remarks>One scan of the fleets per call, grouped by system (a redraw reads every system).</remarks>
    Dictionary<StarSystem, List<Fleet>> SeenFleets()
    {
        var seen = new Dictionary<StarSystem, List<Fleet>>();
        if (_game is not { } game) return seen;
        foreach (var f in game.Fleets)
        {
            StarSystem? at = f.Owner == Side || Knowledge.IsLive(f, Side)
                ? f.Container is StarSystem c && f.Existing && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Completed) ? c : null
                : Knowledge.Location(game, f, Side) is StarSystem l && ScreenViews.View(game, f, Side).Existing && ScreenViews.Ships(game, f, Side).Any(x => x.Completed) ? l : null;
            if (at is null) continue;
            if (!seen.TryGetValue(at, out var list)) seen[at] = list = [];
            list.Add(f);
        }
        return seen;
    }

    /// <summary>
    /// Markers in logical (640 x 480) pixels, drawn through a transform at the system.
    /// </summary>
    void DrawMarkers(Vector2 at, StarSystem s, List<Fleet> fleets)
    {
        var font = ThemeDB.FallbackFont;
        int own = fleets.Count(f => f.Owner == Side), other = fleets.Count - own;
        bool known = s.Owner == Side || Knowledge.IsLive(s, Side);
        bool selected = Selected is { } sel && sel.Container == s;
        if (own == 0 && other == 0 && !selected && !(known && (s.BattlePending || s.Blockade || s.Uprising))) return;
        DrawSetTransform(at);
        var xf = new Transform2D(0, at); // the same transform for the log
        var enemy = Side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;
        void Marker(Vector2 offset, int n, SimOwner owner, bool hollow)
        {
            var rect = new Rect2(offset, new Vector2(4f, 4f));
            DrawRect(rect.Grow(0.5f), Colors.Black);
            DrawLog.Fill(this, rect.Grow(0.5f), Colors.Black, xf);
            if (hollow) { DrawRect(rect, OwnerColor(owner), filled: false, width: 1f); DrawLog.Frame(this, rect, OwnerColor(owner), xf); }
            else { DrawRect(rect, OwnerColor(owner)); DrawLog.Fill(this, rect, OwnerColor(owner), xf); }
            if (n <= 1) return;
            var count = offset + (offset.X < 0 ? new Vector2(-6f, 4f) : new Vector2(5f, 4f));
            var text = n.ToString();
            DrawString(font, count, text, fontSize: 7, modulate: OwnerColor(owner));
            DrawLog.String(this, font, count, text, 7, OwnerColor(owner), xf);
        }
        // hollow = every own fleet here is still travelling in (the sim parents a fleet to its destination at departure)
        if (own > 0) Marker(new Vector2(-6f, -7f), own, Side, fleets.Where(f => f.Owner == Side).All(f => f.Enroute));
        if (other > 0) Marker(new Vector2(2f, -7f), other, enemy, hollow: false);
        void Ring(float radius, Color colour)
        {
            DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 20, colour, 1f);
            DrawLog.Ellipse(this, Vector2.Zero, radius, colour, filled: false, xf);
        }
        if (selected) Ring(6f, Colors.White);
        if (known)
        {
            if (s.BattlePending) Ring(4.5f, Colors.Red);
            else if (s.Blockade) Ring(4.5f, Colors.Orange);
            if (s.Uprising)
            {
                DrawString(font, new Vector2(-2.5f, 11f), "U", fontSize: 7, modulate: Colors.Yellow);
                DrawLog.String(this, font, new Vector2(-2.5f, 11f), "U", 7, Colors.Yellow, xf);
            }
        }
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    string Describe(SystemRecord s)
    {
        if (_state.TryGetValue(s, out var state)) return Describe(state);
        var sector = _sectorByIndex[s.SectorIndex];
        string kind = s.Common.Family == 0x90 ? "core" : "rim";
        return $"{DisplayName(s.Common)}   sector: {DisplayName(sector.Common)} (group {sector.Group}, galaxy size {sector.GalaxySize})   "
             + $"{kind}   pos: ({s.X}, {s.Y})   index {s.Common.Index}   string {s.Common.StringModule}:{s.Common.StringId}   unk1C {s.Unk1C}";
    }

    /// <summary>Details of a system as <see cref="Side"/> knows it.</summary>
    public string DescribeSystem(StarSystem s) => Describe(s);

    string Describe(StarSystem s)
    {
        if (_game is null) return "";
        var game = _game;
        var seen = Knowledge.See(game, s, Side);
        bool known = s.Owner == Side || Knowledge.IsLive(s, Side);
        // objects the side sees here: its own, live views, and frozen views last seen here (only those still present)
        bool Sees(GameObject o) => o.Owner == Side || Knowledge.IsLive(o, Side) || Knowledge.Location(game, o, Side) == s;
        static IEnumerable<GameObject> All(GameObject o) => o.Contents.SelectMany(c => All(c).Prepend(c));
        var objects = All(s).Where(o => o.Existing && Sees(o)).ToList();
        string support = seen.Populated ? $"support A {seen.Support} / E {100 - seen.Support}" : "unpopulated / unknown";
        string state = (s.BattlePending && known ? " BATTLE" : "") + (s.Blockade && known ? " blockade" : "") + (seen.Uprising ? " uprising" : "");
        string fleets = string.Join("; ", (SeenFleets().GetValueOrDefault(s) ?? []).Select(f =>
        {
            bool live = f.Owner == Side || Knowledge.IsLive(f, Side);
            if (!live) // a frozen view: what was seen, no ETA
            {
                int Seen(int tab) => ScreenViews.FleetContents(game, [f], tab, Side).Count;
                return $"{ScreenViews.View(game, f, Side).Owner} fleet {f.Id:X} (last seen): {Seen(0x66)} ships {Seen(0x67)} fighters {Seen(0x68)} troops";
            }
            var inside = All(f).ToList();
            string travel = f.Enroute ? $" en route, ETA day {f.Eta}" : "";
            return $"{f.Owner} fleet {f.Id:X}: {inside.Count(x => Family.IsCapitalShip(x.Type))} ships {inside.Count(x => Family.Of(x.Type) is >= 0x1C and < 0x20)} fighters {inside.Count(x => Family.IsTroop(x.Type))} troops{travel}";
        }));
        string people = string.Join(", ", objects.OfType<Character>().Select(c => DisplayName(c.Definition.Common) + (c.Captured ? " (prisoner)" : "")));
        return $"{DisplayName(s.Definition.Common)} ({(s.IsCore ? "core" : "rim")}): {seen.Owner}{state}   {support}"
             + (known ? $"   energy {s.Energy} raw {s.RawMaterials}" : "")
             + $"   facilities {objects.Count(o => Family.IsFacility(o.Type))} troops {objects.Count(o => Family.IsTroop(o.Type) && o.Container == s)}"
             + (fleets == "" ? "" : "\n" + fleets) + (people == "" ? "" : "\n" + people);
    }

    /// <summary>
    /// openswr extra: a system of the hovered sector (its box = the union of its systems' 17 x 17 marker cells holds the mouse), or
    /// null; the caller opens that sector's window as for a click on the system itself.
    /// </summary>
    public StarSystem? SectorSystemAt() => _hoveredSector is { } sector ? _game?.Systems.First(s => s.Sector == sector) : null;

    /// <summary>Display name of an SD record (TEXTSTRA string), or its string id.</summary>
    /// <summary>TEXTSTRA string by id (null when absent).</summary>
    public string? Text(int id) => _names.GetValueOrDefault(id);

    public string DisplayName(SdCommon c) => _modNames.TryGetValue(c.Ref, out var name) ? name : _names.GetValueOrDefault(c.StringId, $"#{c.StringId}");

    /// <summary>A system's position in canvas (logical screen) coordinates.</summary>
    public Vector2 ScreenPosition(StarSystem s) => GetGlobalTransformWithCanvas() * WorldPos(s.Definition);

    /// <summary>
    /// SD (X, Y): galaxy bitmap pixel (trunc(X * 607 / 1024), trunc(Y * 437 / 1024)); the filter
    /// marker cell is the 17 x 17 rect with that pixel as its top-left.
    /// </summary>
    public static Vector2 Pixel(float x, float y) => new(Mathf.Floor(x * 607f / 1024f), Mathf.Floor(y * 437f / 1024f));

    /// <summary>A system's dot / hover point: the centre of its 17 x 17 marker cell (Inferred: the cell is the system's hot item).</summary>
    Vector2 WorldPos(SystemRecord s) => Pixel(s.X, s.Y) + new Vector2(8.5f, 8.5f);
}
