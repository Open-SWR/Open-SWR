using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>Object windows of the strategy screen: window manager, sector / system windows, drag and drop, go-to.</summary>
public partial class MainScreen
{
    Control _windowLayer = null!;
    internal WindowManager Windows { get; private set; } = null!;

    internal SimOwner Side => _map.Side;
    /// <summary>Production window's last product per class (facilities, ships and fighters, troops and special forces); per session, not saved.</summary>
    internal readonly SdRecord?[] LastProduct = new SdRecord?[3];

    /// <summary>
    /// Production window for a pending Build command; one at a time (an open one closes, no sound); nothing buildable: no
    /// window, the command is dropped.
    /// </summary>
    internal void OpenProduction(Command command)
    {
        if (Windows.Find(ProductionWindow.WindowId) is { } open) Windows.Close(open, sound: false);
        _menu?.Close();
        if (ProductionWindow.Create(this, command) is { } w) Windows.Open(ProductionWindow.WindowId, () => w);
    }
    internal string DisplayName(SdCommon c) => _map.DisplayName(c);
    internal Rect2 MapRect => Frames[_side].MapRect;

    /// <summary>
    /// Display name of an object: definitions for systems, sectors, characters, units, missions; fleets "Fleet" + id (fleet names are
    /// not kept, Inferred). An own character with a command kind gets its title in front ("General Veers": 34818 / 34817 / 34816, the
    /// finder's command strings; observed by the user);<paramref name="title"/> false: the plain name (the finder adds the kind itself).
    /// </summary>
    internal string ObjectName(GameObject o) => ObjectName(o, true);

    internal string ObjectName(GameObject o, bool title) => o switch
    {
        StarSystem s => DisplayName(s.Definition.Common),
        Character { CommandKind: >= 1 and <= 3 } c when title && c.Owner == _map.Side => $"{Assets.Stra(34819 - c.CommandKind)} {DisplayName(c.Definition.Common)}",
        Character c => DisplayName(c.Definition.Common),
        Fleet f => $"{_map.Text(11523) ?? "Fleet"} {f.Id:X}",
        Mission m => DisplayName(m.Record.Common),
        ManufacturingManager m => $"{m.Kind} manager",
        _ => _game!.Definitions.Resolve(o.Type) is { } r ? DisplayName(r.Common) : $"{o.Type:X8}",
    };

    void BuildWindowLayer()
    {
        _windowLayer = new Control { Size = LogicalSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        GetNode("Hud").AddChild(_windowLayer);
        Windows = new WindowManager(this, _windowLayer);
    }

    /// <summary>
    /// Left button up on a system of the map: a visible legend collapses into its button, then the system's sector window
    /// opens (or comes to the front). The original's hot items are the systems' cells (Inferred).
    /// </summary>
    void OpenSectorOf(StarSystem s)
    {
        if (_legend is { Visible: true }) HideLegend();
        Windows.OpenSector(s.Sector);
    }

    /// <summary>
    /// A quadrant icon's sub-window: mode 4: kind 9 manufacturing, 8: 10 planet, 0x10: 4 fleet, 0x40: 0xB
    /// missions; id = (kind, system index); created at the point, clamped into the map rect; an open one is raised.
    /// </summary>
    internal GameWindow? OpenSystemWindow(StarSystem s, int mode, Vector2 at)
    {
        int kind = mode switch { 4 => 9, 8 => 10, 0x10 => 4, 0x40 => 0xB, _ => 0 };
        if (kind == 0) return null;
        var id = new WindowAddress(kind, (int)new OpenSWR.Data.Dat.SdRef(s.Ref).Index);
        return Windows.Open(id, () =>
        {
            GameWindow w = kind switch
            {
                9 => new ManufacturingWindow(this, id, s),
                10 => new PlanetWindow(this, id, s),
                4 => new FleetWindow(this, id, s),
                _ => new MissionsWindow(this, id, s),
            };
            var r = MapRect;
            w.Position = new Vector2(Mathf.Clamp(at.X, r.Position.X, r.End.X - w.Size.X), Mathf.Clamp(at.Y, r.Position.Y, r.End.Y - w.Size.Y));
            return w;
        }, 606);
    }

    /// <summary>
    /// The load's window factory by window kind: sector windows (1) and system sub-windows (4, 9, 10, 0xB) of a
    /// system still in the game, the message window (0xD, tab "all"), the finders (0x14-0x17) and the encyclopedia (0x19, index page).
    /// Not re-created: the confirmation dialog (0x10, no factory case), battle windows, and the production (0x22), mission
    /// (0xC) and status (0x1A) windows, whose pending command or subject is not saved (Inferred, as the tab and the encyclopedia entry).
    /// </summary>
    GameWindow? ReopenWindow(SavedWindow saved)
    {
        var (kind, index) = (saved.Kind, saved.Index);
        var game = _game!;
        switch (kind)
        {
            case 1:
                return game.Systems.Select(s => s.Sector).FirstOrDefault(x => x.Definition.Common.Index == index) is { } sector ? Windows.OpenSector(sector) : null;
            case 4 or 9 or 10 or 0xB:
                return game.Systems.FirstOrDefault(s => new OpenSWR.Data.Dat.SdRef(s.Ref).Index == index) is { } system
                    ? OpenSystemWindow(system, kind switch { 9 => 4, 10 => 8, 4 => 0x10, _ => 0x40 }, new Vector2(saved.X, saved.Y)) : null;
            case 0xD when index == 0: OpenMessages(0x79); break;
            case >= 0x14 and <= 0x17 when index == 0: OpenFinder(kind); break;
            case 0x19 when index == 0: OpenEncyclopedia(null, 0); break;
            default: return null;
        }
        return Windows.Find(new WindowAddress(kind, index));
    }

    (GameWindow Source, Vector2 Start, TextureRect? Image)? _drag;

    /// <summary>
    /// Drag start: the source window and the press point; a drag image follows the pointer (the item's picture, Inferred).
    /// The release is taken in <see cref="_Input"/> (the pressed control keeps the mouse).
    /// </summary>
    internal void BeginDrag(GameWindow source, Vector2 start, Texture2D? image, Vector2? size = null)
    {
        EndDragImage();
        TextureRect? rect = null;
        if (image is not null)
        {
            rect = new TextureRect { Texture = image, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0.7f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, Size = size ?? image.GetSize() }; // logical size (mod pictures, ModArt)
            GetNode("Hud").AddChild(rect);
            rect.Position = start - rect.Size / 2;
        }
        _drag = (source, start, rect);
    }

    void EndDragImage()
    {
        _drag?.Image?.QueueFree();
        _drag = null;
    }

    public override void _Input(InputEvent e)
    {
        if (BriefingInput(e)) return;
        // TranslateAccelerator runs before dispatch: an accelerator key never reaches a focused text field
        if (e is InputEventKey { Pressed: true } k && GetViewport().GuiGetFocusOwner() is NameField or LineEdit && IsAncestorOf(GetViewport().GuiGetFocusOwner())
            && Accelerator(k) is not null)
        {
            GetViewport().SetInputAsHandled();
            _UnhandledInput(e);
            return;
        }
        // targeting state before the GUI: a window's cell lists would take the target click; Controls reset the cursor on
        // every motion, so the cross is set again after the GUI has run; with Shift held the arrow shows
        if (_pending is not null && _menu is null && _window is null)
        {
            if (e is InputEventMouseMotion m)
                Callable.From(() => { if (_pending is not null) DisplayServer.CursorSetShape(m.ShiftPressed ? DisplayServer.CursorShape.Arrow : DisplayServer.CursorShape.Cross); }).CallDeferred();
            else if (e is InputEventMouseButton or InputEventKey && TargetingInput(e))
            {
                GetViewport().SetInputAsHandled();
                return;
            }
        }
        if (_drag is not { } drag) return;
        switch (e)
        {
            case InputEventMouseMotion m when drag.Image is { } image:
                image.Position = m.Position - image.Size / 2;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up:
                EndDragImage();
                Drop(drag.Source, drag.Start, up.Position, up.CtrlPressed || Input.IsKeyPressed(Key.Ctrl));
                break;
        }
    }

    /// <summary>
    /// Drop: nothing unless the pointer moved more than 4 px in x or y; the manufacturing and planet windows forward only
    /// outside themselves; the window under the point gives T_dest / T_move (no window: the bare galaxy map, nothing, as observed in the original);
    /// the commands (<see cref="ScreenViews.Drop"/>) go through the dispatcher (confirmation, validation, submit).
    /// </summary>
    void Drop(GameWindow source, Vector2 start, Vector2 at, bool ctrl)
    {
        if (_game is not { GameOver: false } || Mathf.Abs(at.X - start.X) <= 4 && Mathf.Abs(at.Y - start.Y) <= 4) return;
        if (Windows.WindowAt(at) is not { } target) return;
        // the topmost window under the point decides: a window covering the source's rect still takes the drop (Inferred)
        if (source.Kind is 9 or 10 && target == source) return;
        var local = at - target.GetGlobalRect().Position;
        foreach (var (id, selection, to) in ScreenViews.Drop(_game, Side, source.Kind, source.Selection, target.DestAt(local), target.MoveAt(local), ctrl))
            Dispatch(id, selection, to);
    }

    /// <summary>
    /// Go to an object: the sector window of its system (seen through the player's views), then the system
    /// sub-window of its group with the object selected; nothing for an unknown location, staging or an abode.
    /// </summary>
    internal void GoTo(GameObject o, bool planet = false)
    {
        if (_game is null) return;
        if (_advisor is not null) MessagesArrived(_advisor.TipTrigger(_game, AdvisorTip.GoTo));
        if (ScreenViews.GoTo(_game, o, Side) is not { } goTo) return;
        var (system, kind, _) = goTo;
        var sector = Windows.OpenSector(system.Sector);
        if (kind == 0 && planet) kind = 10; // finders: a system opens its planet window too (as observed)
        if (kind == 0)
        {
            sector.SelectObject(system);
            return;
        }
        int mode = kind switch { 9 => 4, 10 => 8, 4 => 0x10, _ => 0x40 };
        var at = sector.GetGlobalRect().Position + new Vector2(ScreenViews.PicturePosition(system).X + 37, ScreenViews.PicturePosition(system).Y);
        OpenSystemWindow(system, mode, at)?.SelectObject(o);
    }

    /// <summary>
    /// Env OPENSWR_DEMO_SECTOR=1: the sector window of the first own fleet's system (else the first own system);
    /// OPENSWR_DEMO_WINDOW = fleet | planet | manu | missions [+ "min"]: also that system's sub-window (selecting the first fleet /
    /// expanded), "min" minimises it into a slot, "+enemy" / "+neutral" the first system with that view owner, "+sel" selects the
    /// first planet cell / manager row, "+fac" the first facility (owner skins); OPENSWR_DEMO_WINDOW=goto: go to the first own capital ship.
    /// OPENSWR_DEMO_REF = a definition SdRef (hex): that system, or the system holding an object of that type (selected).
    /// </summary>
    static uint? DemoRef => uint.TryParse(System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_REF"), System.Globalization.NumberStyles.HexNumber, null, out uint r) ? r : null;

    static bool Contains(GameObject container, GameObject o) => container.Contents.Any(c => c == o || Contains(c, o));

    void DemoWindows()
    {
        string? sector = System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_SECTOR"), window = System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_WINDOW");
        if (sector != "1" && string.IsNullOrEmpty(window) || _game is not { } game) return;
        Only(null);
        var s = game.Systems.SelectMany(OwnFleets).FirstOrDefault()?.System ?? game.Systems.First(x => x.Owner == Side);
        var demo = DemoRef is { } r ? game.Created.FirstOrDefault(o => o.Type == r && o.Existing) : null;
        if (DemoRef is { } dr) s = game.Systems.FirstOrDefault(x => x.Definition.Common.Ref == dr)
            ?? game.Systems.FirstOrDefault(x => demo is not null && Contains(x, demo)) ?? s;
        if (window == "goto")
        {
            GoTo(game.Created.First(o => o.Owner == Side && Family.IsCapitalShip(o.Type) && o.Existing && o.Container is Fleet));
            return;
        }
        if (window?.Contains("+enemy") == true || window?.Contains("+neutral") == true)
        {
            var want = window.Contains("+neutral") ? SimOwner.Neutral : Side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;
            var known = game.Systems.Where(x => Knowledge.See(game, x, Side).Owner == want).ToList();
            s = known.FirstOrDefault(x => ScreenViews.Planet(game, x, 1, Side).Count + ScreenViews.Planet(game, x, 2, Side).Count > 0) ?? known.FirstOrDefault() ?? s;
        }
        var w = Windows.OpenSector(s.Sector);
        if (string.IsNullOrEmpty(window)) return;
        int mode = window.Split('+')[0] switch { "planet" => 8, "manu" => 4, "missions" => 0x40, _ => 0x10 };
        if (mode == 0x40 && ScreenViews.Missions(game, s, Side).Count == 0
            && game.Systems.FirstOrDefault(x => ScreenViews.Missions(game, x, Side).Count > 0) is { } busy) s = busy;
        var sub = OpenSystemWindow(s, mode, w.GetGlobalRect().Position + new Vector2(ScreenViews.PicturePosition(s).X + 40, ScreenViews.PicturePosition(s).Y));
        if (mode == 0x10 && demo?.Container is Fleet own) sub?.SelectObject(demo);
        else if (mode == 0x10 && ScreenViews.Fleets(game, s, Side).FirstOrDefault(f => f.Owner == Side).Object is { } fleet) sub?.SelectObject(fleet.Contents.FirstOrDefault(x => Family.IsCapitalShip(x.Type)) ?? fleet);
        if (window.Contains("+sel") && (mode == 8 ? new[] { 1, 2, 3 }.Select(t => ScreenViews.Planet(game, s, t, Side).FirstOrDefault().Object).FirstOrDefault(o => o is not null) : ScreenViews.ManagerRows(s).FirstOrDefault()) is { } pick) sub?.SelectObject(pick);
        if (window.Contains("+fac") && ScreenViews.Facilities(game, s, 0x2C, 0x2D, Side).FirstOrDefault().Object is { } fac) sub?.SelectObject(fac);
        if (window.EndsWith("+min") && sub is not null) Windows.Minimise(sub);
    }

    /// <summary>
    /// Status window (id 0x1A, one at a time): an object's page when the player side has a view of it (live or frozen;
    /// systems, own managers and missions always), the Galaxy Overview (<paramref name="overview"/>) or the Objectives (null).
    /// </summary>
    internal void OpenStatus(GameObject? o, bool overview = false)
    {
        if (_game is not { } game) return;
        if (o is not null && o is not (StarSystem or ManufacturingManager or Mission) && !ScreenViews.Sees(o, Side) && !game.Views.ContainsKey((o, Side))) return;
        if (Windows.Find(StatusWindow.WindowId) is { } open) Windows.Close(open, sound: false);
        _menu?.Close();
        Windows.Open(StatusWindow.WindowId, () => new StatusWindow(this, o, overview));
    }

    /// <summary>Alt+S (0xBBF): 0x103 Status for the focused window's single selected object, if the row is enabled.</summary>
    void StatusKey()
    {
        if (Windows.Focused?.Selection is [(var o, _)] sel && ObjectCommands.Rows(_game!, Side, sel).Any(r => r.Id == ObjectCommands.Status && r.Enabled)) OpenStatus(o);
    }

    /// <summary>
    /// Env OPENSWR_DEMO_STATUS = system | fleet | ship | character | mission | troop | fighter | facility | overview | objectives: the
    /// status window of the first own such object (mission: a diplomacy mission is sent first).
    /// </summary>
    void DemoStatus(string mode)
    {
        if (_game is not { } game) return;
        Only(null);
        GameObject? Own(System.Func<GameObject, bool> f) => game.Created.Cast<GameObject>().Concat(game.Characters).FirstOrDefault(o => o.Owner == Side && o.Existing && f(o));
        GameObject? o = mode switch
        {
            "system" => game.Systems.FirstOrDefault(s => s.Owner == Side && s.Contents.Any(x => Family.Of(x.Type) == 0x2C)),
            "fleet" => game.Systems.SelectMany(OwnFleets).FirstOrDefault(),
            "ship" => Own(x => Family.IsCapitalShip(x.Type) && x.Container is Fleet),
            "character" => Own(x => x is Character { Placed: true }),
            "troop" => Own(x => Family.IsTroop(x.Type)),
            "fighter" => Own(x => Family.Of(x.Type) is >= 0x1C and < 0x20),
            "facility" => Own(x => Family.Of(x.Type) is >= 0x28 and < 0x2B),
            "manager" => game.Systems.FirstOrDefault(s => s.Owner == Side && s.Managers[1] is { } m && Economy.Members(m).Count > 0)?.Managers[1],
            "mission" => game.Missions.FirstOrDefault(m => m.Owner == Side && !m.Destroyed),
            _ => null,
        };
        if (mode == "mission" && o is null)
        {
            DemoMission("send");
            o = game.Missions.FirstOrDefault(m => m.Owner == Side && !m.Destroyed);
        }
        if (o is not null || mode is "overview" or "objectives") OpenStatus(o, mode == "overview");
    }

    /// <summary>
    /// End of the 0x240 targeting: Team = the selection's agents (a system's own characters for mode 8); a target that is a
    /// Team member becomes the system holding it in the player's view; the list = the passing types in rank order; empty: the
    /// command (no type) is validated and refused at once; else the mission window (one at a time).
    /// </summary>
    internal void OpenMission(List<(GameObject Object, int Mode)> selection, GameObject target)
    {
        var game = _game!;
        List<Agent> team = [.. selection.SelectMany(s => s.Object is StarSystem sys
            ? sys.Contents.OfType<Agent>().Where(a => a.Owner == Side && a.Existing && Family.Of(a.Type) is >= 0x30 and < 0x3C)
            : s.Object is Agent a ? [a] : [])];
        if (team.Contains(target) && Knowledge.Location(game, target, Side) is { } at) target = at as StarSystem ?? at.System ?? at;
        var types = MissionEligibility.WindowTypes(game, Side, team, [], target);
        if (types.Count == 0)
        {
            Submit(new Command(0x240, Side) { Targets = [.. team.Select(a => ((GameObject)a, 0))], ArgRef = target, Fields = [0, 0, 0] });
            return;
        }
        if (Windows.Find(MissionWindow.WindowId) is { } open) Windows.Close(open, sound: false);
        _menu?.Close();
        Windows.Open(MissionWindow.WindowId, () => new MissionWindow(this, types, team, target));
    }

    /// <summary>
    /// Env OPENSWR_DEMO_MISSIONWINDOW = mission | list | personnel | send (Begin pressed): the mission window for the own system with the most free
    /// characters (all of them as the Team, the last one moved to Decoys for personnel) against the nearest neutral populated
    /// system the side knows; list = the type drop-down open.
    /// </summary>
    void DemoMission(string mode)
    {
        if (_game is not { } game) return;
        List<Agent> Free(StarSystem s) => [.. s.Contents.OfType<Character>().Where(c => c.Owner == Side && c.Usable && !c.OnMission)];
        var home = game.Systems.Where(s => s.Owner == Side).MaxBy(s => Free(s).Count);
        if (home is null || Free(home).Count == 0) return;
        var target = game.Systems.Where(s => s != home && Knowledge.See(game, s, Side) is { Populated: true, Owner: SimOwner.Neutral }).MinBy(s => Orders.Distance(home, s));
        if (target is null) return;
        Only(null);
        OpenMission([.. Free(home).Select(a => ((GameObject)a, 0))], target);
        if (Windows.Find(MissionWindow.WindowId) is not MissionWindow w) return;
        w.Demo(mode == "personnel", 0x51, decoy: mode == "personnel", begin: mode == "send");
        if (mode == "list") w.DemoOpenList();
    }

    /// <summary>
    /// Env OPENSWR_DEMO_PRODUCTION = window | list | na: the production window for the first own shipyard (else any manager) with
    /// members; list = the product drop-down open, na = count 255 (maintenance refused: "n/a", Done greyed); order (OPENSWR_DEMO_DAYS):
    /// one unit of the first product ordered with Done. Returns the manager.
    /// </summary>
    ManufacturingManager? DemoProduction(string mode)
    {
        if (_game is not { } game) return null;
        bool Has(StarSystem s, ManufacturingKind k) => s.Owner == Side && s.Managers[(int)k] is { } m && Economy.Members(m).Count > 0;
        var yard = game.Systems.FirstOrDefault(s => Has(s, ManufacturingKind.Ship)) ?? game.Systems.FirstOrDefault(s => System.Enum.GetValues<ManufacturingKind>().Any(k => Has(s, k)));
        if (yard is null) return null;
        var kind = Has(yard, ManufacturingKind.Ship) ? ManufacturingKind.Ship : System.Enum.GetValues<ManufacturingKind>().First(k => Has(yard, k));
        var manager = yard.Managers[(int)kind];
        Only(null);
        OpenProduction(ObjectCommands.Build(0x210 + kind switch { ManufacturingKind.Construction => 0, ManufacturingKind.Ship => 1, _ => 2 }, Side, [(manager, 0)]));
        if (Windows.Find(ProductionWindow.WindowId) is not ProductionWindow w) return manager;
        if (mode == "list") w.DemoOpenList();
        if (mode == "na") w.Demo(0, 255, done: false);
        if (mode == "order") w.Demo(0, 1, done: true);
        return manager;
    }

    /// <summary>A game menu of built rows at a point (finder rows, F4).</summary>
    internal void OpenMenuAt(List<OpenSWR.Data.MenuRow> rows, Vector2 at, System.Action<int> command)
    {
        if (rows.Count > 0) OpenMenu(new GameMenu(rows, at, Side == SimOwner.Alliance, command));
    }

    static bool IsFinder(int id) => id is Finders.SystemFinder or Finders.FleetFinder or Finders.TroopFinder or Finders.PersonnelFinder;

    /// <summary>Finder: the game menu closes first; opens only when no window with its id is open.</summary>
    internal void OpenFinder(int id)
    {
        if (_game is null || Windows.Find(id) is not null) return;
        _menu?.Close();
        Only(null);
        Windows.Open(id, () => new FinderWindow(this, id));
    }

    /// <summary>A finder is open: F1-F7 are ignored.</summary>
    bool FinderOpen => Windows.Windows.Any(w => IsFinder(w.Kind));

    /// <summary>Encyclopedia window (id 0x19): an object's or a definition's entry, else the index; opens only when none is open (no retarget, Inferred).</summary>
    internal void OpenEncyclopedia(GameObject? o, uint definition)
    {
        if (_game is null || Windows.Find(0x19) is not null) return;
        _menu?.Close();
        Only(null);
        var entry = o is null && definition == 0 ? null : EncyclopediaWindow.EntryOf(_game, o, definition, DisplayName);
        Windows.Open(0x19, () => new EncyclopediaWindow(this, entry));
    }

    static List<OpenSWR.Data.Accelerator>? _accelerators;

    /// <summary>
    /// The TEXTCOMM RT_ACCELERATOR 11 entry a key matches (exact Shift / Ctrl / Alt), or null. Virtual keys:
    /// letters and digits are their own codes, Tab 9, Numpad + 0x6B, Numpad - 0x6D; character entries ("=", "-") take the typed
    /// character, else the key's own ASCII code (US layout, Inferred: Godot may report no character with Alt held).
    /// </summary>
    static OpenSWR.Data.Accelerator? Accelerator(InputEventKey k)
    {
        _accelerators ??= Assets.Module("TEXTCOMM.DLL").Find(new OpenSWR.Data.ResId(9), 11) is { } r ? OpenSWR.Data.Accelerator.Decode(r.Data.Span) : [];
        ushort vk = k.Keycode switch
        {
            >= Key.A and <= Key.Z or >= Key.Key0 and <= Key.Key9 => (ushort)k.Keycode,
            Key.Tab => 9,
            Key.KpAdd => 0x6B,
            Key.KpSubtract => 0x6D,
            _ => 0,
        };
        char c = k.Unicode is > 0 and <= 0xFFFF ? (char)k.Unicode : k.Keycode is > Key.None and < (Key)128 ? (char)k.Keycode : '\0';
        return OpenSWR.Data.Accelerator.Find(_accelerators, vk, c, k.ShiftPressed, k.CtrlPressed, k.AltPressed);
    }

    /// <summary>Keys for the windows: the focused window first, Esc closes it; Alt+W, Ctrl+Tab / Ctrl+Shift+Tab. True = consumed.</summary>
    bool WindowKey(InputEventKey k)
    {
        if (k.AltPressed && k.Keycode == Key.W)
        {
            Windows.CloseUnminimised();
            CloseWait(); // the battle wait window is an ordinary window there
            return true;
        }
        if (k.CtrlPressed && k.Keycode == Key.Tab)
        {
            Windows.Cycle(k.ShiftPressed);
            return true;
        }
        if (Windows.Focused is not { } w) return false;
        if (w.HandleKey(k)) return true;
        if (k.Keycode != Key.Escape) return false;
        Windows.Close(w);
        return true;
    }
}
