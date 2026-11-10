using System;
using System.IO;
using Godot;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Application states of the front end: startup movies MDATA.000 then .001, then the main menu; Start leads to side movie .003 / .004, then
/// MainScreen with the menu's settings; Load / in-game Esc leads to the options screen; Credits leads to .005; Exit to quit confirmation.
/// Menu music MDATA.300 loops while no game exists (Inferred).
/// Env OPENSWR_FRONT_DEMO (headless renders): "menu" skips the startup movies; "options" opens Load; "start" starts as
/// Empire without the side movie; "ingame" also opens the in-game options. OPENSWR_DEMO_PRESS = comma-separated menu control
/// ids (hex) clicked first, e.g. "6F,71,6C"; OPENSWR_DEMO_OPTIONS the same for the options screen; "mods" opens the mods screen.
/// A plain "Mods" button over the menu's bottom-left corner opens the mods screen (openswr extra).
/// </summary>
public partial class FrontEnd : Node
{
    [Export] public string GameDir { get; set; } = "";

    MainMenu? _menu;
    MainScreen? _game;
    Node? _screen;
    AudioStreamPlayer _music = null!;
    string _demo = "";
    bool _skipMovies;
    Label? _modStatus;

    public override void _Ready()
    {
        GetWindow().Title = GameFiles.AppName; // project.godot config/name stays "openswr" (user:// folder)
        _ = DrawLog.On; // draw log (env OPENSWR_DRAWLOG): opened here, so the launcher's nodes are hooked too
        _demo = System.Environment.GetEnvironmentVariable("OPENSWR_FRONT_DEMO") ?? "";
        _skipMovies = _demo != "";
        FrontSettings.Load();
        FrontSettings.ApplyWindow(GetWindow());
        Assets.SoundRoot = this;
        AddChild(_music = new AudioStreamPlayer { Bus = "Music" });
        _music.Finished += () => _music.Play(); // loops (Inferred)
        // the launcher (openswr, no original assets) picks the game folder first; an exported GameDir (debug) skips it
        if (GameFiles.IsGameDir(GameDir)) Begin(GameDir);
        else
        {
            var launcher = new Launcher();
            launcher.Started = dir =>
            {
                launcher.QueueFree();
                Begin(dir);
            };
            AddChild(launcher);
        }
    }

    void Begin(string dir)
    {
        Assets.GameDir = GameDir = dir;
        GameCursors.Install(this);
        // modder tool: `-- --list-ids <file>` writes every base: id with its type and name, then quits
        var args = OS.GetCmdlineUserArgs();
        if (Array.IndexOf(args, "--list-ids") is var at and >= 0)
        {
            string path = at + 1 < args.Length ? args[at + 1] : "base-ids.txt";
            File.WriteAllLines(path, OpenSWR.Data.Mods.ModLoader.BaseIds(dir));
            GD.Print($"base ids written to {Path.GetFullPath(path)}");
            GetTree().Quit();
            return;
        }
        CheckMods();
        if (_skipMovies) Menu();
        else Movie("MDATA.000", () => Movie("MDATA.001", Menu));
    }

    /// <summary>Mods: load errors stay on screen over the menu; the strategy screen refuses to start with them.</summary>
    void CheckMods()
    {
        try
        {
            GameFiles.Definitions(GameDir);
            _modStatus?.QueueFree();
            _modStatus = null;
        }
        catch (OpenSWR.Data.Mods.ModLoadException e)
        {
            if (_modStatus is null)
            {
                _modStatus = new Label { Position = new Vector2(8, 8), Size = new Vector2(624, 440), AutowrapMode = TextServer.AutowrapMode.Word,
                    Modulate = new Color(1, 0.4f, 0.4f), ZIndex = 100, MouseFilter = Control.MouseFilterEnum.Ignore };
                var label = _modStatus;
                Callable.From(() => AddChild(label)).CallDeferred(); // above the menu
            }
            _modStatus.Text = "Mods not loaded (fix or disable them on the Mods screen, or OPENSWR_MODS):\n" + string.Join("\n", e.Errors);
        }
    }

    void Mods()
    {
        if (_modStatus is not null) _modStatus.Visible = false;
        Show(new ModsScreen { GameDir = GameDir, Back = () => Callable.From(() => { CheckMods(); if (_modStatus is not null) _modStatus.Visible = true; Menu(); }).CallDeferred() });
    }

    void Show(Node screen)
    {
        if (_screen is not null)
        {
            RemoveChild(_screen);
            if (_screen != _menu && _screen != _game) _screen.QueueFree();
        }
        AddChild(_screen = screen);
        UpdateMusic();
    }

    // Menu music: MDATA.300 on the menu and the load screen while no game exists, when Play Music is on.
    void UpdateMusic()
    {
        bool want = _game is null && FrontSettings.MusicOn && _screen is MainMenu or OptionsScreen or ModsScreen;
        if (!want) _music.Stop();
        else if (!_music.Playing)
        {
            try
            {
                _music.Stream ??= AudioStreamWav.LoadFromBuffer(File.ReadAllBytes(Path.Combine(GameDir, "MDATA", "MDATA.300")));
                _music.Play();
            }
            catch (IOException ex)
            {
                GD.PushWarning("menu music: " + ex.Message);
            }
        }
    }

    // the next state runs deferred: the movie ends inside its own _Process
    void Movie(string file, Action next) => Show(new MoviePlayer { File = file, Finished = () => Callable.From(next).CallDeferred() });

    void Menu()
    {
        _menu ??= new MainMenu
        {
            Start = StartGame,
            Load = () => Options(),
            Exit = Quit,
            Credits = () => Movie("MDATA.005", Menu),
        };
        if (_menu.GetNodeOrNull("Mods") is null)
        {
            var mods = new Button { Name = "Mods", Text = "Mods", Position = new Vector2(4, 454), Size = new Vector2(56, 22) }; // plain button: the menu art stays untouched
            mods.Pressed += Mods;
            _menu.AddChild(mods);
        }
        Show(_menu);
        if (_demo == "") return;
        string demo = _demo;
        _demo = ""; // once
        foreach (var id in (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_PRESS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            _menu.Controls[Convert.ToInt32(id.Trim(), 16)].Click();
        if (demo == "options") _menu.Controls[0x68].Click();
        if (demo == "mods") Mods();
        if (demo is "start" or "ingame") _menu.Controls[0x66].Click();
        if (demo == "ingame") Callable.From(() => Options()).CallDeferred();
    }

    // Settings: new game: side movie: strategy screen. The demo skips the movie.
    void StartGame(GameSettings settings)
    {
        var screen = NewMainScreen();
        screen.Settings = settings;
        if (_skipMovies) Enter(screen);
        else Movie(settings.PlayerSide == SimOwner.Alliance ? "MDATA.003" : "MDATA.004", () => Enter(screen));
    }

    MainScreen NewMainScreen()
    {
        var screen = GD.Load<PackedScene>("res://MainScreen.tscn").Instantiate<MainScreen>();
        screen.GameDir = GameDir;
        screen.OptionsRequested = () => Options();
        // States 0x10-0x13 play the end movie, then the main menu (state 3); the game is gone
        screen.GameEnded = movie => Callable.From(() => // deferred: the call comes from inside the screen's own _Process
        {
            var old = _game;
            _game = null;
            _menu?.QueueFree(); // State 3 builds a new menu, with the controls at their defaults; credits keep theirs
            _menu = null;
            Movie(movie, Menu);
            old?.QueueFree();
        }).CallDeferred();
        return screen;
    }

    void Enter(MainScreen screen)
    {
        if (_game != screen) _game?.QueueFree();
        _game = screen;
        Show(screen);
    }

    void Options()
    {
        bool inGame = _game?.Game is not null;
        var options = new OptionsScreen
        {
            InGame = inGame,
            FromTactical = _game?.InTactical ?? false,
            Side = _game?.Game?.Settings.PlayerSide ?? SimOwner.Alliance,
            Save = path => _game!.SaveTo(path) ? _game.Game!.Settings.PlayerSide : null,
            Load = LoadGame,
            Restart = () =>
            {
                var old = _game;
                _game = null;
                Menu();
                old?.QueueFree();
            },
            Return = () => Show(_game!),
            Exit = Quit,
            MusicChanged = () => { UpdateMusic(); _game?.MusicSwitched(); },
        };
        Show(options);
        // demo: OPENSWR_DEMO_OPTIONS = options control ids (hex) clicked once, e.g. "6F" save slot 1, "79" load slot 1
        foreach (var id in (System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_OPTIONS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            options.Controls[Convert.ToInt32(id.Trim(), 16)].Click();
        System.Environment.SetEnvironmentVariable("OPENSWR_DEMO_OPTIONS", null);
    }

    // Flow after loading: the strategy screen (Inferred).
    void LoadGame(string path)
    {
        if (_game is not null)
        {
            if (_game.LoadFrom(path)) Show(_game);
            return;
        }
        var screen = NewMainScreen();
        screen.LoadPath = path;
        Enter(screen);
    }

    void Quit() => Assets.Confirm(this, Assets.Dialog(4610), () => GetTree().Quit());
}
