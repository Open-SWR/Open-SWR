using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Strategy main screen. Side frame bitmap (STRATEGY.DLL 900 / 901) drawn over the galaxy map;
/// pure blue (0,0,255) in the frame is a colour key and shows the map through. Runs a new game: days advance on demand or
/// automatically (until game over), the player's fleets take move orders (the move command, the replays' path). Battles: the battle
/// request window (answer = the human side's decision float) holds the clock, then the post-battle report; the selected fleet
/// bombards (B) or assaults (V) its system and the result window opens.
/// </summary>
public partial class MainScreen : Node2D
{
    static readonly Vector2 LogicalSize = new(640, 480);
    const string Help = "Space: next day   A: run / pause (speed: right click the speed box, Alt+= / Alt+-)   Click a system: sector window   Double click an icon: system window   Right click in a window: object menu   Drag: move (Ctrl: confirmed)   "
        + "F1-F7: frame buttons (finders, messages, encyclopedia)   R: research   Alt+W: close windows   Ctrl+Tab: next window   N: new game   G: galaxy size   S: side   Shift+F5 / Shift+F9: quick save / load   Alt+1-9: map filters   Tab: sectors";
    const string QuickSave = "user://saves/quick.json";

    // Frame bitmaps per side (STRATEGY.DLL 900 Alliance, 901 Empire; verified). Galaxy bitmap origin and map rect per side.
    static readonly (string Side, ushort Bitmap, Vector2 Origin, Rect2 MapRect)[] Frames =
        [("Alliance", StrategyBitmaps.FrameAlliance, new(21, 25), new(55, 40, 485, 350)), ("Empire", StrategyBitmaps.FrameEmpire, new(84, 27), new(120, 40, 480, 355))];

    /// <summary>Original game folder. Empty = OPENSWR_GAME_DIR, then the folder remembered in user://settings.cfg, else ask.</summary>
    [Export] public string GameDir { get; set; } = "";

    /// <summary>For headless renders (or env OPENSWR_DEMO_DAYS): after the start, order the first own fleet to the nearest other own system, run this many days, show the destination's details.</summary>
    [Export] public int DemoDays { get; set; }

    /// <summary>Set before entering the tree: start a new game with these settings (the main menu's), else the debug default.</summary>
    public GameSettings? Settings { get; set; }
    /// <summary>Set before entering the tree: load this save instead of starting a game.</summary>
    public string? LoadPath { get; set; }
    /// <summary>Esc with nothing selected opens the options screen (set by the front end).</summary>
    public System.Action? OptionsRequested { get; set; }
    /// <summary>The game has ended: play this end movie, then the main menu (set by the front end).</summary>
    public System.Action<string>? GameEnded { get; set; }
    public GameState? Game => _game;

    readonly Dictionary<ushort, (Texture2D Texture, Rect2I Viewport)> _frames = [];
    string _gameDir = "";
    int _side, _galaxySize = 1;
    GameDefinitions? _definitions;
    GalaxyMap _map = null!;
    Control _clip = null!;
    Sprite2D _frame = null!;
    Label _info = null!;
    Label _status = null!;
    ResearchPanel _research = null!;
    Messages? _log;
    GameState? _game;
    bool _auto;
    string? _fault; // an exception from the clock: the run stops and saving is refused (the step was left half done)
    double _autoTimer;
    BattleWindow? _window;
    readonly Queue<GameMessage> _events = new(); // kind-6 messages waiting for their movie
    MoviePlayer? _movie;
    int _targetDay, _reportsSeen;
    List<GameObject> _battleUnits = [];
    string _message = "";
    GameMenu? _menu;
    (int Id, List<(GameObject Object, GroupMode Mode)> Selection)? _pending; // targeting state: the command waiting for its target

    public override void _Ready()
    {
        if (DemoDays == 0 && int.TryParse(System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_DAYS"), out int days)) DemoDays = days;
        if (DemoDays == 0 && System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_BATTLE") is { Length: > 0 }) DemoDays = 1;
        if (DemoDays == 0 && System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_TACTICAL") is { Length: > 0 }) DemoDays = 1;
        _map = GetNode<GalaxyMap>("Clip/Map");
        _clip = GetNode<Control>("Clip");
        _frame = GetNode<Sprite2D>("Frame");
        _info = GetNode<Label>("Hud/Info");
        _status = GetNode<Label>("Hud/Status");
        _title = new Control { Size = LogicalSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        _title.Draw += DrawFilterTitle;
        GetNode("Hud").AddChild(_title);
        BuildWindowLayer();
        _research = new ResearchPanel { Position = new Vector2(372, 40), Size = new Vector2(264, 356) };
        GetNode("Hud").AddChild(_research);
        _map.HoverChanged += () => _info.Text = _map.HoverText ?? Help;
        _map.Covered = () =>
        {
            var at = GetViewport().GetMousePosition();
            return Windows.WindowAt(at) is not null || _legend is { Visible: true } l && l.GetGlobalRect().HasPoint(at);
        };

        GameFiles.Locate(this, GameDir, dir =>
        {
            if (Assets.GameDir == "") Assets.GameDir = dir;
            GameCursors.Install(this);
            try { _definitions = GameFiles.Definitions(dir); }
            catch (OpenSWR.Data.Mods.ModLoadException e)
            {
                _info.Text = "Mods not loaded: " + System.Environment.NewLine + e.Message; // no game with a refused mod set
                return; // _gameDir stays empty: no input reaches a game without definitions
            }
            _gameDir = dir;
            _map.Load(dir, _definitions);
            if (LoadPath is not null)
            {
                ShowSide(0, start: false);
                LoadFrom(LoadPath);
            }
            else
            {
                ShowSide(Settings?.PlayerSide == SimOwner.Empire || System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_SIDE") == "E" ? 1 : 0, start: false); // env: Empire for headless renders
                StartGame(Settings);
            }
            if (DemoDays > 0) Demo();
        }, status => _info.Text = status);
    }

    public override void _Process(double delta)
    {
        if (_auto && _game is not null && _window is null) // event movies do not stop it
        {
            _autoTimer += delta;
            if (_autoTimer >= DayInterval)
            {
                _autoTimer = 0;
                NextDay();
            }
        }
        AdvisorFrame(delta); // after the frame's clock work and messages
    }

    /// <summary>Runs the clock to the next day (events, timers and the submitted commands), then refreshes the screen.</summary>
    void NextDay()
    {
        if (_game is null || _game.GameOver || _window is not null || _tactical is not null || _fault is not null) return;
        _targetDay = _game.Day + 1;
        Run();
    }

    /// <summary>
    /// Steps the clock towards the target day. A battle report or a battle request (host waiting) opens its window
    /// and stops here; closing the window resumes the run. The clock stays stopped from the battle start until its report is closed
    /// (host states 5-11): the report of a battle comes before the next battle's request, and nothing steps the clock
    /// while a battle window is open. Before a battle window the messages so far are built and an event movie handed out first; while
    /// one plays or waits no battle window opens, so the movie comes first; the run resumes after the last movie. Every
    /// hosted battle opens its report. openswr deviation: also when the original would build none and wait for ever).
    /// </summary>
    void Run()
    {
        var game = _game!;
        while (true)
        {
            if (game.BattleTrace!.Count > _reportsSeen)
            {
                if (EventFirst()) return;
                OpenReport(game.BattleTrace[_reportsSeen++]);
                return;
            }
            if (game.HostedBattle is { } battle && game.Controllers.GetValueOrDefault(_map.Side) is OpenSWR.Sim.Ai.HumanController { BattleAnswer: null })
            {
                if (!EventFirst()) OpenBattleRequest(battle);
                return;
            }
            if (game.Tactical is { Done: false } tactical)
            {
                if (!MovieBusy) OpenTactical(tactical);
                return;
            }
            if (game.Day >= _targetDay && game.HostedBattle is null || game.GameOver) break; // a pending battle is hosted first
            try { game.Clock.Step(game); }
            catch (System.Exception ex) when (ex is not System.OutOfMemoryException)
            {
                _fault = ex.Message; _auto = false;
                GD.PushError(ex.ToString());
                Assets.Confirm(GetNode("Hud"), "The game hit an error and stopped: " + ex.Message + "\nSaving is disabled; load a saved game.", () => { });
                UpdateStatus();
                return;
            }
        }
        if (game.GameOver) _auto = false;
        var added = Deliver(); // the day's messages; the newest two titles also in the status line until the next day
        _message = string.Join("; ", added.TakeLast(2).Select(MessageFormat.Title));
        _map.Refresh();
        _research.Refresh();
        Windows.Refresh();
        UpdateStatus();
        PlayEvent();
    }

    /// <summary>
    /// Kind-6 events: one at a time, a full-screen movie of the scene (message subject) with the body as
    /// caption; it ends at the movie end, on any key except Esc or on the caption button. The clock, auto-advance, the advisor and the
    /// message building run on; the movie layer takes every mouse event and key (<see cref="MoviePlayer.Swallow"/>). Battle and strike
    /// windows wait for the last queued movie (<see cref="MovieBusy"/>); an end post during the movie waits for it (<see cref="EndGame"/>).
    /// </summary>
    void PlayEvent()
    {
        if (_movie is not null || !_events.TryDequeue(out var m)) return;
        if (!Messages.EventMovies.TryGetValue(m.Subject, out var file))
        {
            PlayEvent();
            return;
        }
        _movie = new MoviePlayer
        {
            File = file, Caption = MessageFormat.Body(m), Swallow = true,
            Finished = () => Callable.From(() =>
            {
                _movie?.QueueFree();
                _movie = null;
                if (_endPending)
                {
                    _events.Clear(); // later event movies are dropped
                    EndGame();
                    return;
                }
                PickMusic(); // main-screen message after an event movie: the music is picked again
                PlayEvent();
                if (MovieBusy) return;
                foreach (var strike in _waitingStrikes.ToList()) OpenStrike(strike); // strikes held back by the movie
                _waitingStrikes.Clear();
                if (_window is null && _tactical is null && _game is { GameOver: false }) Run(); // a battle window held back by the movie
            }).CallDeferred(),
        };
        _music?.Stop();
        GetNode("Hud").AddChild(_movie);
    }

    /// <summary>
    /// Manager tick before a battle window: the messages built so far delivered, the first queued event movie
    /// handed out; true while a movie plays or waits (the battle window then opens after the last one).
    /// </summary>
    bool EventFirst()
    {
        Deliver();
        PlayEvent();
        return MovieBusy;
    }

    /// <summary>An event movie plays or waits in the queue.</summary>
    bool MovieBusy => _movie is not null || _events.Count > 0;
    readonly List<GameMessage> _waitingStrikes = []; // strike messages held back by an event movie

    void Open(BattleWindow window)
    {
        if (window is BattleReportWindow) CloseWait(); // the report closes the wait window
        _window = window;
        window.Closed += () =>
        {
            _window = null;
            _map.Refresh();
            if (window is not BattleStartWindow)
            {
                Run();
                return;
            }
            // the battle wait window from the decision until the report window opens (simulate, retreat and take
            // command alike, after any event movie) or Alt+W; not saved
            ShowWait();
            Run();
        };
        GetNode("Hud").AddChild(window);
    }

    BattleWaitWindow? _wait;

    void ShowWait()
    {
        _wait?.QueueFree();
        _wait = new BattleWaitWindow(MapRect, _map.Side == SimOwner.Alliance);
        GetNode("Hud").AddChild(_wait);
    }

    void CloseWait()
    {
        _wait?.QueueFree();
        _wait = null;
    }

    /// <summary>Battle request: the units present are kept for the report; the answer goes to the human controller.</summary>
    void OpenBattleRequest(StarSystem s)
    {
        var game = _game!;
        if (game.Controllers.GetValueOrDefault(_map.Side) is not OpenSWR.Sim.Ai.HumanController human) return;
        _battleUnits = BattleUnits(s);
        _auto = false;
        Open(new BattleStartWindow(game, s, _map.Side, _map.DisplayName, BattleEngine.RetreatEnabled(game, s, _map.Side), f => human.BattleAnswer = f,
            () => (human.BattleAnswer, human.TakeCommand) = (0.0, true)));
        _message = "battle at " + _map.DisplayName(s.Definition.Common);
        UpdateStatus();
    }

    TacticalScreen? _tactical;
    /// <summary>A tactical battle is on screen, including the options screen opened from it.</summary>
    internal bool InTactical => _tactical is not null;

    /// <summary>
    /// Host state 20: the tactical screen over everything; the clock waits. When the battle is done the screen
    /// goes and the run resumes (the host applies the result, state 8 if withdrawn, then the report).
    /// </summary>
    void OpenTactical(TacticalBattle battle)
    {
        if (_tactical is not null) return;
        _auto = false;
        _music?.Stop();
        _tactical = new TacticalScreen(battle, _definitions!)
        {
            OptionsRequested = RequestOptions,
            Finished = () => Callable.From(() =>
            {
                _tactical?.QueueFree();
                _tactical = null;
                Run();
            }).CallDeferred(),
        };
        var layer = new CanvasLayer { Layer = 50 };
        layer.AddChild(_tactical);
        AddChild(layer);
        _tactical.TreeExiting += () => layer.QueueFree();
    }

    /// <summary>Post-battle report. Go to system shows its details; go to fleet opens it in the fleet panel.</summary>
    void OpenReport(BattleReport report)
    {
        var game = _game!;
        var fleet = report.System.Contents.OfType<Fleet>().FirstOrDefault(f => f.Owner == _map.Side && f.Usable);
        var units = _battleUnits.Count > 0 ? _battleUnits : BattleUnits(report.System);
        _battleUnits = [];
        Open(new BattleReportWindow(game, report, _map.Side, _map.DisplayName, units, s => GoTo(s, planet: true), fleet, f => GoTo(f)));
    }

    /// <summary>The battle's units (ships, fighters, troops) plus the characters and special forces aboard, for the report pages.</summary>
    static List<GameObject> BattleUnits(StarSystem s) =>
        [.. Combat.Units(s), .. All(s).Where(o => (o is Character || Family.IsSpecialForce(o.Type)) && o.Usable && o.Owner is SimOwner.Alliance or SimOwner.Empire)];

    /// <summary>
    /// Bombardment / assault by the selected fleet on its system, dispatched at once like a move. The result window
    /// is not opened here: the strike's message (<see cref="Messages"/>) is the attacker's kind 7, whose
    /// add opens it.
    /// </summary>
    void Strike(Fleet fleet, int type)
    {
        if (fleet.System is not { } s) return;
        var result = Submit(new Command((CommandType)type, _map.Side) { Targets = [(fleet, GroupMode.Object)], ArgRef = s });
        if (result is not { Success: true })
        {
            _message = $"order refused ({result?.Category}, {result?.Code})";
            UpdateStatus();
            return;
        }
        Deliver();
        PlayEvent(); // a planet destroyed by the strike: its movie first, the strike window after it
    }

    /// <summary>
    /// The messages since the last update (per message): advisor feed and lighting, kind 6 queued, and a kind-7 strike
    /// opens its window at once (the defender's kind 3 opens nothing). Returns the added messages.
    /// </summary>
    List<GameMessage> Deliver()
    {
        var added = _log!.Update();
        UprisingAlarm(_game!);
        MessagesArrived(added);
        foreach (var m in added.Where(m => m.Kind == MessageKinds.Event)) _events.Enqueue(m);
        foreach (var strike in added.Where(m => m.Kind == MessageKinds.Strike))
            if (MovieBusy) _waitingStrikes.Add(strike); // after the last queued event movie
            else OpenStrike(strike); // each kind-7 add opens its own window
        return added;
    }

    /// <summary>The open strike windows, oldest first: each modeless, the clock keeps running.</summary>
    readonly List<StrikeResultWindow> _strikeWindows = [];
    /// <summary>The newest open strike window. It takes the keys, as the last focused window.</summary>
    StrikeResultWindow? NewestStrike => _strikeWindows.LastOrDefault();

    /// <summary>
    /// The strike window on a strike message; <paramref name="reopen"/> = the message window's memory. Not a
    /// battle window: it does not hold the clock. Every call opens a new window on top (no search);
    /// the others stay. The memory is single: a window opened with it takes it from the older ones.
    /// </summary>
    internal void OpenStrike(GameMessage m, System.Action? reopen = null)
    {
        if (_game is null) return;
        if (reopen is not null) foreach (var w in _strikeWindows) w.Reopen = null;
        StrikeResultWindow window = null!;
        window = new StrikeResultWindow(_game, _map.DisplayName, _map.Side, m, (s, fleets) =>
        {
            Windows.OpenSector(s.Sector); // sector window
            if (fleets) // the fleet sub-window (fleets mode)
            {
                var sector = Windows.OpenSector(s.Sector);
                OpenSystemWindow(s, GroupMode.Fleets, sector.GetGlobalRect().Position + new Vector2(ScreenViews.PicturePosition(s).X + 37, ScreenViews.PicturePosition(s).Y));
            }
        }) { Reopen = reopen };
        window.Closed = () => _strikeWindows.Remove(window);
        _strikeWindows.Add(window);
        GetNode("Hud").AddChild(window);
    }

    static IEnumerable<GameObject> All(GameObject o) => o.Contents.SelectMany(c => All(c).Prepend(c));

    /// <summary>Hides the research panel (openswr extra) unless it is <paramref name="keep"/>.</summary>
    void Only(Control? keep)
    {
        if (keep != _research) _research.Visible = false;
    }

    void UpdateStatus()
    {
        if (_game is null) return;
        string state = _game.GameOver ? $"GAME OVER: {_game.Winner} wins" : _auto ? "auto" : "paused";
        string selected = _map.Selected is { } f ? $"   selected: fleet {f.Id} at {f.System?.Definition.Common.Index} (click a destination)" : "";
        _status.Text = $"{Frames[_side].Side}   day {_game.Day}   {state}{selected}   {_message}";
        UpdateTopBar();
    }

    /// <summary>Own fleets with capital ships in the system (key order), the selectable ones.</summary>
    List<Fleet> OwnFleets(StarSystem s) =>
        [.. s.Contents.OfType<Fleet>().Where(f => f.Owner == _map.Side && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type)))];

    /// <summary>Click on a system: its sector window.</summary>
    void Click(StarSystem target)
    {
        if (_game is null || _game.GameOver) return;
        OpenSectorOf(target);
        UpdateStatus();
    }

    /// <summary>
    /// Move order: move command (targets [(fleet, object mode)], ArgRef = the destination system), submitted to the clock's pending
    /// list like a player order; it runs when the queues are empty, i.e. at once here.
    /// </summary>
    void Move(Fleet fleet, StarSystem target)
    {
        var result = Submit(new Command(CommandType.Move, _map.Side) { Targets = [(fleet, GroupMode.Object)], ArgRef = target }); // the command, then its order events (the travel starts there)
        _message = result is { Success: true } ? $"fleet {fleet.Id} en route, ETA day {fleet.Eta}"
            : $"order refused ({result?.Category}, {result?.Code})";
        UpdateStatus();
    }

    /// <summary>Headless demo: first own fleet: nearest other own system, then <see cref="DemoDays"/> days.</summary>
    void Demo()
    {
        var game = _game!;
        DemoDelegate(start: true);
        // OPENSWR_DEMO_GAMEOVER=1: the victory word set to an Empire win (as Victory.Recompute would, whatever the victory mode), an
        // options request is refused, then the advisor's action 84-87 ends the game through its post command (the end movie
        // and the main menu need the front end)
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_GAMEOVER") == "1")
        {
            game.VictoryState = game.VictoryState with { EmpireWins = true, GameOver = true };
            _advisor?.StatusMask(game.VictoryState);
            RequestOptions();
            _message = $"game over: {game.Winner} wins, options locked = {_advisor?.GameOverUi}";
            UpdateStatus();
            return;
        }
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_BATTLE") is { Length: > 0 } battle)
        {
            DemoBattle(battle);
            return;
        }
        // OPENSWR_DEMO_TACTICAL=<mode>: the demo battle's request answered with take command, then the tactical screen's demo mode
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_TACTICAL") is { Length: > 0 } tactical)
        {
            DemoBattle("tactical");
            GetTree().CreateTimer(0).Timeout += () => _tactical?.Demo(tactical); // the tactical screen opens after the wait window's frame
            return;
        }
        var fleet = game.Systems.SelectMany(OwnFleets).FirstOrDefault();
        if (fleet?.System is { } from)
        {
            var to = game.Systems.Where(s => s.Owner == _map.Side && s != from)
                .MinBy(s => (s.Definition.X - from.Definition.X) * (s.Definition.X - from.Definition.X) + (s.Definition.Y - from.Definition.Y) * (s.Definition.Y - from.Definition.Y));
            if (to is not null) Move(fleet, to);
            // production: the first own shipyard (else any manager) with members orders one unit of its first product (production window, Done);
            // not with OPENSWR_DEMO_ADVISOR=build, whose helper refuses a busy shipyard
            var manager = System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_ADVISOR") == "build" ? null : DemoProduction("order");
            // env OPENSWR_DEMO_TARGET=1: move that queue to the fleet's destination (destination order over a non-empty queue)
            if (manager is not null && to is not null && System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_TARGET") == "1")
                Dispatch((int)CommandType.SetProductionTarget, [(manager, GroupMode.Object)], to, confirm: false);
            // env OPENSWR_DEMO_MISSION=1: the own system with the most free characters sends them on a diplomacy mission to the nearest
            // neutral populated system it knows (mission window, Begin)
            if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_MISSION") == "1") DemoMission("send");
            for (int i = 0; i < DemoDays; i++) NextDay();
            if (to is not null) _info.Text = _map.DescribeSystem(to);
            // OPENSWR_DEMO_FLEETS=1: the demo fleet's fleet window (go to its first capital ship)
            if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_FLEETS") == "1" && fleet.Contents.FirstOrDefault(x => Family.IsCapitalShip(x.Type)) is { } ship)
            {
                Only(null);
                GoTo(ship);
            }
            DemoPanels();
            return;
        }
        for (int i = 0; i < DemoDays; i++) NextDay();
        DemoPanels();
    }

    /// <summary>
    /// Env OPENSWR_DEMO_BATTLE = start | startlist | wait | report | units | display | bombard | assault | strikeunits / assaultunits [+1 / +3] | tactical (take command): the first own fleet with capital ships moves to a system with an
    /// enemy fleet; days run until the battle request opens (start; startlist = its Alliance forces tab); "simulate" is pressed (report, units = the Alliance unit tab (units+N: filter N), display = the 4th
    /// tab); bombard / assault: the fleet goes to an unguarded enemy system instead and strikes it on arrival.
    /// </summary>
    void DemoBattle(string mode)
    {
        var game = _game!;
        var enemy = _map.Side == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;
        bool Armed(Fleet f) => f.Usable && !f.Enroute && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Usable);
        bool Troops(Fleet f) => !mode.StartsWith("assault") || f.Contents.Any(x => x.Contents.Any(y => Family.IsTroop(y.Type)));
        var fleet = game.Systems.SelectMany(OwnFleets).FirstOrDefault(f => Armed(f) && Troops(f));
        bool strike = mode.Split('+')[0] is "bombard" or "assault" or "strikeunits" or "assaultunits";
        bool Guarded(StarSystem x) => x.Contents.OfType<Fleet>().Any(f => f.Owner == enemy && Armed(f));
        var target = strike ? game.Systems.FirstOrDefault(x => x.Owner == enemy && !Guarded(x)) : game.Systems.FirstOrDefault(Guarded);
        if (fleet is null || target is null) return;
        Move(fleet, target);
        for (int i = 0; i < 100 && _window is null && !(strike && OwnFleets(target).Count > 0); i++) NextDay(); // the arriving fleet may be a new object
        if (strike)
        {
            if (_window is null && OwnFleets(target).FirstOrDefault() is { } arrived) Strike(arrived, mode.StartsWith("assault") ? (int)CommandType.Assault : (int)CommandType.Bombard);
            // strikeunits / assaultunits: the system tab (+1: the attacker tab, +3: the display page)
            if (mode.Split('+')[0] is "strikeunits" or "assaultunits")
                for (int i = 0, n = mode.EndsWith("+1") ? 1 : mode.EndsWith("+3") ? 3 : 2; i < n; i++) NewestStrike?.HandleKey(Key.Right);
            if (mode.EndsWith("+msg") || mode.EndsWith("+reopen")) // the strike window again from the message window's Conflict tab
            {
                NewestStrike?.HandleKey(Key.Escape);
                OpenMessages(MessageTab.Conflict);
                (Windows.Find(WindowKind.Messages) as MessageWindow)?.DemoDisplay();
                if (mode.EndsWith("+reopen")) NewestStrike?.HandleKey(Key.Escape); // back to the list on the remembered tab
            }
            return;
        }
        if (mode == "startlist" && _window is BattleStartWindow list) list.Demo(1); // the Alliance forces list
        if (mode is "start" or "startlist" || _window is not BattleStartWindow request) return;
        if (mode == "wait") { ShowWait(); return; } // the wait window's art over the start window (it stays)
        if (mode == "tactical")
        {
            if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_DEATHSTAR") == "1" && game.HostedBattle is { } at) DemoDeathStar(at);
            request.TakeCommand(); // the tactical screen opens on the deferred close
            return;
        }
        request.Press(retreat: false);
        GetTree().CreateTimer(0).Timeout += () => // the report is open then
        {
            for (int i = mode.Split('+')[0] switch { "units" => 1, "display" => 3, _ => 0 }; i > 0; i--) _window?.HandleKey(Key.Right);
            if (mode.StartsWith("units+") && int.TryParse(mode[6..], out int filter) && _window is BattleReportWindow report) report.DemoFilter(filter); // units+N: filter N (3 = personnel)
        };
    }

    /// <summary>
    /// Env OPENSWR_DEMO_DEATHSTAR=1 (demo only, not original): a Death Star joins the Empire's fleet of the demo battle and four X-wing
    /// squadrons the first Alliance capital ship at the hosted battle's system, so the tactical demo shows the superlaser and the run.
    /// </summary>
    void DemoDeathStar(StarSystem at)
    {
        var game = _game!;
        Fleet? Of(SimOwner o) => at.Contents.OfType<Fleet>().FirstOrDefault(f => f.Owner == o && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        Of(SimOwner.Empire)?.Add(new OpenSWR.Sim.GameObject(Refs.DeathStarType, SimOwner.Empire, game.NextId()));
        if (Of(SimOwner.Alliance)?.Contents.FirstOrDefault(x => Family.IsCapitalShip(x.Type)) is { } ship)
            for (int i = 0; i < 4; i++) ship.Add(new OpenSWR.Sim.GameObject(Refs.XWingSquadron, SimOwner.Alliance, game.NextId()));
    }

    /// <summary>Env OPENSWR_DEMO_RESEARCH=1 / OPENSWR_DEMO_LOG=1: open the research panel / event log after the demo days.</summary>
    void DemoPanels()
    {
        DemoDelegate(start: false);
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_RESEARCH") == "1") OpenResearch();
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_LOG") == "1") OpenLog();
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_MENU") is { Length: > 0 } menu) DemoMenu(menu);
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_FILTER") is { Length: > 0 } filter) DemoFilter(filter);
        // OPENSWR_DEMO_SPEED=menu: the speed menu at the speed box
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_SPEED") == "menu") OpenSpeedMenu(SpeedRect.GetCenter());
        // OPENSWR_DEMO_MESSAGES=<tab id 121-130>[+display]: the message window on that tab (display: the newest message shown)
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_MESSAGES") is { Length: > 0 } messages
            && int.TryParse(messages.Split('+')[0], out int tab))
        {
            OpenMessages(tab);
            if (messages.EndsWith("+display")) (Windows.Find(WindowKind.Messages) as MessageWindow)?.DemoDisplay();
        }
        DemoWindows();
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_PRODUCTION") is { Length: > 0 } production) DemoProduction(production);
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_MISSIONWINDOW") is { Length: > 0 } mission) DemoMission(mission);
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_STATUS") is { Length: > 0 } status) DemoStatus(status);
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_ADVISOR") is { Length: > 0 } advisor) DemoAdvisor(advisor);
        // OPENSWR_DEMO_MOVIE=<scene 111 / 114 / 115 / 116>: that kind-6 event's movie with its caption
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_MOVIE") is { Length: > 0 } movie && uint.TryParse(movie, out uint scene))
        {
            var cor = _game!.Known.Coruscant!;
            MessageText body = scene switch
            {
                EventScene.PlanetDestroyed => new(TextId.DeathStarDestroyedSystem, [MessageArg.Of(SimOwner.Empire), "", _map.DisplayName(cor.Definition.Common)]),
                EventScene.DeathStarSabotaged => new(TextId.RebelAllianceSabotagedDeath, [_map.DisplayName(cor.Definition.Common)]),
                EventScene.HeadquartersDestroyed => new(TextId.EmpireDestroyedAllianceHeadquarters, []),
                _ => new(TextId.AllianceSeizedImperialCapital, []),
            };
            _events.Enqueue(new GameMessage { Kind = MessageKinds.Event, Subject = scene, Body = [body] });
            PlayEvent();
        }
        // OPENSWR_DEMO_ENCY = index | entry: the encyclopedia index, or the entry of the first own capital ship
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_ENCY") is { Length: > 0 } ency && _game is { } g)
            OpenEncyclopedia(ency == "entry" && DemoRef is null ? g.Created.First(o => o.Owner == Side && Family.IsCapitalShip(o.Type)) : null, ency == "entry" ? DemoRef ?? 0 : 0); // OPENSWR_DEMO_REF: that definition
        // OPENSWR_DEMO_FINDER = system | fleet | ships | troop | personnel | specforces [+empire filter "2"]: that finder open; OPENSWR_DEMO_TYPE=<text> typed into its name box
        if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_FINDER") is { Length: > 0 } finder)
        {
            WindowKind id = finder switch { "fleet" or "ships" => WindowKind.FleetFinder, "troop" => WindowKind.TroopFinder, "personnel" or "specforces" => WindowKind.PersonnelFinder, _ => WindowKind.SystemFinder };
            OpenFinder(id);
            if (Windows.Find(id) is FinderWindow w)
            {
                w.Demo(finder);
                if (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_TYPE") is { Length: > 0 } typed) w.DemoType(typed); // typed into the name box
            }
        }
    }

    void OpenResearch()
    {
        Only(_research);
        _research.Open(_game!, _map.Side, _map.DisplayName);
    }

    /// <summary>The message window on the All tab (F6, Alt+Y, env OPENSWR_DEMO_LOG=1).</summary>
    void OpenLog() => OpenMessages(MessageTab.All);

    ZoomOutline? _zoom;

    /// <summary>
    /// Zoom animation from <paramref name="from"/> to <paramref name="to"/> (logical rects), then
    /// <paramref name="then"/>; a running one ends first. Env OPENSWR_DEMO_ZOOM=&lt;step 1-4&gt; holds the outline on that step (renders).
    /// </summary>
    internal void Zoom(Rect2 from, Rect2 to, System.Action then)
    {
        _zoom?.Finish();
        static Rect2I I(Rect2 r) => new((int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y);
        var z = new ZoomOutline(I(from), I(to));
        z.Then = () => { if (_zoom == z) _zoom = null; then(); };
        if (int.TryParse(System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_ZOOM"), out int hold)) z.Hold = hold;
        _zoom = z;
        GetNode("Hud").AddChild(z);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_zoom is not null) return; // the strategy screen ignores input while the outline runs
        if (_gameDir == "" || _movie is not null || _tactical is not null) return; // the movie takes its own keys; Esc does nothing; the tactical screen takes its own (it has no Esc exit)
        if (_window is null && NewestStrike is not null && Windows.Focused is null && e is InputEventKey { Pressed: true, Keycode: Key.Escape or Key.Enter or Key.KpEnter or Key.Left or Key.Right } strikeKey)
        {
            // the newest modeless strike window takes its keys. shortcut: it counts as focused while no object window
            // is, and the newest counts as the last focused (keys go to the last focused window); give the strike windows a
            // place in the window manager's focus if that ever matters
            NewestStrike.HandleKey(strikeKey.Keycode);
            return;
        }
        if (_window is not null) // the strategy screen waits behind a battle window
        {
            if (e is InputEventKey { Pressed: true } windowKey) _window.HandleKey(windowKey.Keycode);
            return;
        }
        if (_menu is not null)
        {
            if (e is InputEventKey { Pressed: true } menuKey) _menu.HandleKey(menuKey.Keycode);
            return;
        }
        if (_pending is not null && TargetingInput(e)) return;
        if (e is InputEventKey { Pressed: true } mod && (mod.CtrlPressed || mod.AltPressed))
        {
            // Ctrl / Alt keys: only the accelerator table's entries act, matched exactly (AltGr matches no Alt
            // entry; Ctrl+S matches and does nothing); no other Ctrl / Alt key reaches the screen's keys below
            if (Accelerator(mod) is null) return;
            if (mod.AltPressed && mod.Keycode is >= Key.Key1 and <= Key.Key9)
            {
                if (_game is not null) SetFilter(FilterPicker.AltModes[mod.Keycode - Key.Key1]); // Alt+1 ... Alt+9
                return;
            }
            if (!WindowKey(mod) && !mod.Echo) FrameKey(mod);
            return;
        }
        if (e is InputEventKey { Pressed: true } focusKey && WindowKey(focusKey)) return;
        if (e is InputEventKey { Pressed: true, Echo: false } frameKey && FrameKey(frameKey)) return;
        if (e is InputEventKey { Pressed: true } key && _research.HandleKey(key.Keycode))
        {
            _map.Refresh();
            return;
        }
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when (_clip.GetGlobalRect().HasPoint(GetViewport().GetMousePosition()) ? _map.SectorSystemAt() : null) is { } clicked:
                Click(clicked); // anywhere in a sector's box opens its sector window (openswr: single systems are not click targets)
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } adv when _game is not null && (AdvisorRect(0).HasPoint(adv.Position) || AdvisorRect(1).HasPoint(adv.Position)):
                OpenMessages(MessageTab.All); // advisor hotspot click
                break;
            case InputEventKey { Pressed: true, Keycode: Key.R } when _game is not null:
                OpenResearch();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape } when OptionsRequested is not null:
                RequestOptions();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Space }:
                NextDay();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.A }:
                SetSpeed(_auto ? 0 : _speed); // pause / run at the chosen speed
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } up when _game is { GameOver: false }:
                RightClick(up.Position); // right button up: object menu
                break;
            case InputEventKey { Pressed: true, Keycode: Key.S }:
                Assets.Confirm(GetNode("Hud"), "Start a new game as the other side?", () => ShowSide((_side + 1) % Frames.Length)); // a new game: never on a stray key
                break;
            case InputEventKey { Pressed: true, Keycode: Key.N }:
                Assets.Confirm(GetNode("Hud"), "Start a new game?", () => StartGame());
                break;
            case InputEventKey { Pressed: true, Keycode: Key.G }:
                Assets.Confirm(GetNode("Hud"), "Change the galaxy size and start a new game?", () => { _galaxySize = _galaxySize % 3 + 1; StartGame(); });
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Tab }:
                _map.ShowSectors = !_map.ShowSectors;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.F5, ShiftPressed: true } when _game is not null:
                SaveTo(QuickSave);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.F9, ShiftPressed: true }:
                LoadFrom(QuickSave);
                break;
        }
    }

    /// <summary>Reason of the last failed <see cref="SaveTo"/> (the options screen shows it; the status line is hidden behind it).</summary>
    public static string? LastSaveError { get; private set; }

    public bool SaveTo(string file)
    {
        LastSaveError = null;
        string path = ProjectSettings.GlobalizePath(file), tmp = path + ".tmp";
        if (_fault is not null) { _message = LastSaveError = "the game stopped on an error"; UpdateStatus(); return false; }
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            _game!.Windows.Clear();
            _game.Windows.AddRange(Windows.Snapshot()); // the window descriptors
            // the old save stays until the new one is complete (a refused save or a crash mid-write keeps it)
            // gzip: the JSON compresses ~13 x ; SaveGame.Load reads both forms
            using (var file_ = System.IO.File.Create(tmp))
            using (var stream = new System.IO.Compression.GZipStream(file_, System.IO.Compression.CompressionLevel.Optimal))
                SaveGame.Save(_game!, stream);
            System.IO.File.Move(tmp, path, true);
            _message = $"saved day {_game!.Day} to {file}";
            UpdateStatus();
            return true;
        }
        catch (System.Exception ex) when (ex is not System.OutOfMemoryException) // any failure keeps the old save and removes the temp file
        {
            _message = "save failed: " + ex.Message;
            LastSaveError = ex.Message;
            try { System.IO.File.Delete(tmp); } catch (System.Exception d) when (d is System.IO.IOException or System.UnauthorizedAccessException) { }
        }
        UpdateStatus();
        return false;
    }

    public bool LoadFrom(string file)
    {
        string path = ProjectSettings.GlobalizePath(file);
        GameState game;
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            game = SaveGame.Load(stream, _definitions!);
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or System.UnauthorizedAccessException or System.IO.InvalidDataException)
        {
            _message = ex is System.IO.FileNotFoundException or System.IO.DirectoryNotFoundException ? $"no save at {file}" : "load failed: " + ex.Message;
            if (_game is null) _info.Text = _message; // no status line without a game
            UpdateStatus();
            return false;
        }
        int shown = _filter; // the mode shown before the load (a fresh screen: 128)
        Only(null);
        Windows.Clear();
        ShowSide(game.Settings.PlayerSide == SimOwner.Alliance ? 0 : 1, start: false);
        AttachControllers(game);
        _game = game; _fault = null;
        AttachAdvisor(game, newGame: false);
        PickMusic(); // screen entry
        _galaxySize = game.Settings.GalaxySize;
        _auto = false;
        _speed = game.Sides[game.Settings.PlayerSide].Speed switch
        {
            SpeedLevel.SlowestTicks => SpeedLevel.Slowest, SpeedLevel.FastTicks => SpeedLevel.Fast, SpeedLevel.FastestTicks => SpeedLevel.Fastest,
            _ => SpeedLevel.Normal,
        }; // the saved ticks per day
        _log = new Messages(game, game.Settings.PlayerSide, _map.DisplayName, VoicePick); // the saved messages come with the game
        LightMessageButtons();
        _map.Side = game.Settings.PlayerSide;
        _map.Selected = null;
        _map.Game = game;
        // the saved filter mode alone: the one-system modes come back with an empty list and no title;
        // 601 once when it differs from the mode shown before the load and is not 128. ShowSide has reset the map to
        // 128 already, so an equal mode is re-applied silently (Inferred: the original screen keeps what it shows)
        if (game.FilterMode != MapFilter.Off) SetFilter(game.FilterMode, sound: MapFilter.LoadSound(shown, game.FilterMode));
        HideLegend(); // openswr: the legend starts collapsed into its button
        Windows.Reopen(game.Windows, ReopenWindow); // the saved windows, silently
        _message = $"loaded day {game.Day} from {file}";
        UpdateStatus();
        return true;
    }

    void ShowSide(int side, bool start = true)
    {
        _side = side;
        var (texture, viewport) = Frame(Frames[side].Bitmap);
        _frame.Texture = texture;
        _frame.Scale = LogicalSize / texture.GetSize(); // upscaled frames still cover 640 x 480
        _clip.Position = viewport.Position;
        _clip.Size = viewport.Size;
        _map.Position = Frames[side].Origin - _clip.Position; // galaxy bitmap 1:1 at the side's origin under the frame
        SetFilter(MapFilter.Off); // a load re-applies the saved mode afterwards (LoadFrom)
        BuildFrame();
        if (start) StartGame();
    }

    /// <summary>
    /// Single player: the player's side gets a human controller, the other side the computer controller the settings
    /// choose (the original AI or the OpenSWR AI). Saves with controllers bring their own.
    /// </summary>
    void AttachControllers(GameState game)
    {
        if (game.Controllers.Count == 0)
        {
            var player = game.Settings.PlayerSide;
            var computer = player == SimOwner.Alliance ? SimOwner.Empire : SimOwner.Alliance;
            game.Controllers[player] = new OpenSWR.Sim.Ai.HumanController(player);
            game.Controllers[computer] = game.Settings.Opponent switch
            {
                ComputerOpponent.OpenSWR => new OpenSWR.Sim.Ai.OpenSWRController(computer),
                _ => new OpenSWR.Sim.Ai.ComputerController(computer),
            };
        }
        foreach (var c in game.Controllers.Values.OfType<OpenSWR.Sim.Ai.HumanController>()) c.AskBattles = true; // battle window
        game.BattleTrace = []; // read by the battle report windows (not cleared)
        _reportsSeen = 0;
        CloseWindow();
        _events.Clear();
        _movie?.QueueFree();
        _movie = null;
        _waitingStrikes.Clear();
        CloseWait();
        _tactical?.QueueFree(); // a load from the options opened in a tactical battle (Load stays enabled there)
        _tactical = null;
        EndTargeting(); // a targeting state or a strike window of the old game must not act on the new one
        _battleUnits = [];
        foreach (var w in _strikeWindows.ToList()) w.QueueFree();
        _strikeWindows.Clear();
        foreach (var c in game.Controllers.Values.OfType<OpenSWR.Sim.Ai.ComputerController>()) c.Strings = _map.Text; // ship names (planner 14)
        foreach (var c in game.Controllers.Values.OfType<OpenSWR.Sim.Ai.OpenSWRController>())
            (c.Names, c.Taunts) = (_map.DisplayName, FrontSettings.ComputerChat ? LoadTaunts() : null); // system names and its chat lines (new game and load; chat off: none)
    }

    const string TauntFile = "res://Data/openswr-taunts.json";

    /// <summary>The OpenSWR AI's chat lines (<see cref="TauntFile"/>, read on every attach so edits apply to the next game); missing or malformed: a warning and no chat lines.</summary>
    static OpenSWR.Sim.Ai.OpenSWRTaunts? LoadTaunts()
    {
        if (!FileAccess.FileExists(TauntFile))
        {
            GD.PushWarning($"{TauntFile} is missing: the OpenSWR AI sends no chat lines.");
            return null;
        }
        try
        {
            return OpenSWR.Sim.Ai.OpenSWRTaunts.Parse(FileAccess.GetFileAsString(TauntFile));
        }
        catch (System.IO.InvalidDataException e)
        {
            GD.PushWarning($"{TauntFile}: {e.Message} The OpenSWR AI sends no chat lines.");
            return null;
        }
    }

    void CloseWindow()
    {
        if (_window is null) return;
        _window.Closed = null;
        _window.QueueFree();
        _window = null;
    }

    // Random seed in [1, 2147483646]. The original derives it from the C runtime rand(), seeded by time.
    void StartGame(GameSettings? settings = null)
    {
        // env OPENSWR_DEMO_SEED: a fixed seed for headless renders
        if (!int.TryParse(System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_SEED"), out int fixedSeed)) fixedSeed = 0;
        settings ??= new GameSettings(_side == 0 ? SimOwner.Alliance : SimOwner.Empire, _galaxySize, Difficulty: 1, VictoryFlag: 0, fixedSeed > 0 ? fixedSeed : (int)GD.RandRange(1, 2147483646), Opponent: FrontSettings.Opponent);
        var side = settings.PlayerSide;
        int seed = settings.Seed;
        _galaxySize = settings.GalaxySize;
        var game = NewGame.Create(settings, _definitions!);
        AttachControllers(game);
        _game = game; _fault = null;
        AttachAdvisor(game, newGame: true);
        OpenSWR.Sim.Ai.Controller.StartGame(game); // game start before the first tick: both controllers, then the music pick
        PickMusic();
        _log = new Messages(game, side, _map.DisplayName, VoicePick);
        LightMessageButtons();
        Only(null);
        Windows.Clear();
        _auto = false;
        _speed = SpeedLevel.Normal; // game start, sent as the speed command (60 ticks per day)
        _map.Side = side;
        SendSpeed();
        _message = $"new game, galaxy size {_galaxySize} ({game.Systems.Count} systems), seed {seed}";
        _map.Side = side;
        _map.Selected = null;
        _map.Game = game;
        SetFilter(FilterModes.PopularSupportVariant, sound: false); // openswr choice: a new game opens on the Loyalty filter (original: the plain map)
        HideLegend(); // openswr: the legend starts collapsed into its button
        _info.Text = Help;
        UpdateStatus();
    }

    (Texture2D, Rect2I) Frame(ushort bitmap)
    {
        if (_frames.TryGetValue(bitmap, out var cached)) return cached;

        var image = GameFiles.LoadBitmap(_gameDir, "STRATEGY.DLL", bitmap);
        image.Convert(Image.Format.Rgba8);
        var viewport = BlueRegion(image, new Vector2I(image.GetWidth() / 2, image.GetHeight() / 3));
        for (int y = 0; y < image.GetHeight(); y++)
            for (int x = 0; x < image.GetWidth(); x++)
                if (IsKey(image.GetPixel(x, y)))
                    image.SetPixel(x, y, Colors.Transparent);
        var texture = ImageTexture.CreateFromImage(image);
        DrawLog.Register(texture, "STRATEGY.DLL", bitmap, image.GetSize(), Assets.ColourKey);
        return _frames[bitmap] = (texture, viewport);
    }

    static bool IsKey(Color c) => c.R8 == 0 && c.G8 == 0 && c.B8 == 255;

    // Bounding box of the colour-key area connected to `start` (4-neighbour flood fill).
    static Rect2I BlueRegion(Image image, Vector2I start)
    {
        int w = image.GetWidth(), h = image.GetHeight();
        var seen = new bool[w * h];
        var stack = new Stack<Vector2I>([start]);
        Vector2I min = start, max = start;
        while (stack.Count > 0)
        {
            var p = stack.Pop();
            if (p.X < 0 || p.Y < 0 || p.X >= w || p.Y >= h || seen[p.Y * w + p.X] || !IsKey(image.GetPixelv(p))) continue;
            seen[p.Y * w + p.X] = true;
            min = new Vector2I(Mathf.Min(min.X, p.X), Mathf.Min(min.Y, p.Y));
            max = new Vector2I(Mathf.Max(max.X, p.X), Mathf.Max(max.Y, p.Y));
            stack.Push(p + Vector2I.Right);
            stack.Push(p + Vector2I.Left);
            stack.Push(p + Vector2I.Down);
            stack.Push(p + Vector2I.Up);
        }
        return new Rect2I(min, max - min + Vector2I.One);
    }
}
