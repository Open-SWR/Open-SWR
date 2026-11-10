using System;
using System.IO;
using Godot;

/// <summary>
/// First screen (openswr, no original assets): plain Godot controls to pick the original game's folder (STRATEGY.DLL and the
/// GData *.DAT files), a check that the definitions load from it, and Start, which remembers the folder in user://settings.cfg
/// and hands the folder to the front end (<see cref="Started"/>). Demo runs (OPENSWR_FRONT_DEMO) and `--list-ids` go straight on when
/// the folder is valid.
/// </summary>
public partial class Launcher : Control
{
    /// <summary>Start pressed with a valid folder (already remembered).</summary>
    public Action<string>? Started { get; set; }

    LineEdit _path = null!;
    Label _status = null!;
    Button _start = null!;
    FileDialog _dialog = null!;

    public override void _Ready()
    {
        Size = new Vector2(640, 480);
        AddChild(new ColorRect { Color = new Color(0.08f, 0.09f, 0.12f), AnchorRight = 1, AnchorBottom = 1 });
        var box = new VBoxContainer { Position = new Vector2(40, 40), Size = new Vector2(560, 400) };
        box.AddThemeConstantOverride("separation", 12);
        AddChild(box);

        box.AddChild(Text(GameFiles.AppName, 28));
        box.AddChild(Text(GameFiles.AppName + " needs the files of your own copy of the original 1998 game. Pick the game's folder: the one that "
            + "holds STRATEGY.DLL and the GData folder with the .DAT files.", 12));

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        box.AddChild(row);
        row.AddChild(_path = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill, PlaceholderText = "Game folder" });
        _path.AddThemeFontSizeOverride("font_size", 12);
        _path.TextChanged += _ => Check();
        var browse = new Button { Text = "Browse..." };
        browse.AddThemeFontSizeOverride("font_size", 12);
        browse.Pressed += () => _dialog.PopupCentered(new Vector2I(560, 380));
        row.AddChild(browse);

        box.AddChild(_status = Text("", 12));
        box.AddChild(DisplayRow());
        box.AddChild(OpponentRow());
        box.AddChild(ChatRow());
        _start = new Button { Text = "Start", CustomMinimumSize = new Vector2(120, 32), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _start.AddThemeFontSizeOverride("font_size", 14);
        _start.Pressed += Start;
        box.AddChild(_start);

        _dialog = new FileDialog { FileMode = FileDialog.FileModeEnum.OpenDir, Access = FileDialog.AccessEnum.Filesystem, Title = "Original game folder" };
        _dialog.DirSelected += dir =>
        {
            _path.Text = dir;
            Check();
        };
        AddChild(_dialog);

        _path.Text = GameFiles.RememberedGameDir();
        if (Check() && (System.Environment.GetEnvironmentVariable("OPENSWR_FRONT_DEMO") is { Length: > 0 } || Array.IndexOf(OS.GetCmdlineUserArgs(), "--list-ids") >= 0))
            Callable.From(Start).CallDeferred();
    }

    /// <summary>Window size (4:3 sizes) or fullscreen, applied at once and saved with the options.</summary>
    static HBoxContainer DisplayRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = "Display:" };
        label.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(label);
        var pick = new OptionButton();
        pick.AddThemeFontSizeOverride("font_size", 12);
        foreach (var r in FrontSettings.Resolutions) pick.AddItem($"{r.X} × {r.Y} window");
        pick.AddItem("Fullscreen");
        int at = Array.IndexOf(FrontSettings.Resolutions, FrontSettings.Resolution);
        pick.Selected = FrontSettings.Fullscreen ? FrontSettings.Resolutions.Length : at >= 0 ? at : 3;
        pick.ItemSelected += i =>
        {
            FrontSettings.Fullscreen = i == FrontSettings.Resolutions.Length;
            if (!FrontSettings.Fullscreen) FrontSettings.Resolution = FrontSettings.Resolutions[i];
            FrontSettings.ApplyWindow(pick.GetWindow());
            FrontSettings.Save();
        };
        row.AddChild(pick);
        return row;
    }

    /// <summary>The computer opponent of new games: "Original" (default) or "openswr", saved with the options.</summary>
    static HBoxContainer OpponentRow() => Choice("Computer opponent:", ["Original", "OpenSWR"], FrontSettings.Opponent == OpenSWR.Sim.ComputerOpponent.OpenSWR ? 1 : 0,
        i => FrontSettings.Opponent = i == 1 ? OpenSWR.Sim.ComputerOpponent.OpenSWR : OpenSWR.Sim.ComputerOpponent.Original);

    /// <summary>The OpenSWR AI's chat lines to the player: "On" (default) or "Off", saved with the options; applies from the next new game or load.</summary>
    static HBoxContainer ChatRow() => Choice("Computer chat:", ["On", "Off"], FrontSettings.ComputerChat ? 0 : 1, i => FrontSettings.ComputerChat = i == 0);

    /// <summary>A labelled drop-down option row; a pick is applied by <paramref name="pick"/>, then the options are saved.</summary>
    static HBoxContainer Choice(string text, string[] items, int selected, Action<int> pick)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(label);
        var box = new OptionButton();
        box.AddThemeFontSizeOverride("font_size", 12);
        foreach (var item in items) box.AddItem(item);
        box.Selected = selected;
        box.ItemSelected += i =>
        {
            pick((int)i);
            FrontSettings.Save();
        };
        row.AddChild(box);
        return row;
    }

    static Label Text(string text, int size)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.Word, CustomMinimumSize = new Vector2(560, 0) };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    /// <summary>The first file the menus and the strategy screen need that the folder lacks (a partial copy would end on a blank screen), else null.</summary>
    static string? MissingFile(string dir) =>
        Array.Find(new[] { "COMMON.DLL", "GOKRES.DLL", "REBDLOG.DLL", "TEXTCOMM.DLL", "TEXTSTRA.DLL", Path.Combine("GData", "C3POACT.SPT"), Path.Combine("GData", "IMP22ACT.SPT") },
            f => !File.Exists(Path.Combine(dir, f)));

    /// <summary>Valid = STRATEGY.DLL there and the install's definitions (every *SD / *TB .DAT in GData) load; the reason otherwise.</summary>
    bool Check()
    {
        string dir = _path.Text.Trim();
        string? error = dir == "" ? "No folder chosen."
            : !Directory.Exists(dir) ? "This folder does not exist."
            : !GameFiles.IsGameDir(dir) ? "STRATEGY.DLL is not in this folder."
            : !Directory.Exists(Path.Combine(dir, "GData")) ? "The GData folder is missing."
            : MissingFile(dir) is { } missing ? missing + " is missing from this folder."
            : null;
        if (error is null)
            try
            {
                OpenSWR.Data.Dat.GameDefinitions.Load(dir);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            {
                error = "The game data cannot be read: " + ex.Message;
            }
        _status.Text = error ?? "Game files found.";
        _status.AddThemeColorOverride("font_color", error is null ? new Color(0.5f, 1f, 0.5f) : new Color(1f, 0.5f, 0.45f));
        _start.Disabled = error is not null;
        return error is null;
    }

    void Start()
    {
        if (!Check()) return;
        string dir = _path.Text.Trim();
        GameFiles.RememberGameDir(dir);
        Started?.Invoke(dir);
    }
}
