using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data.Mods;

/// <summary>
/// Mods screen (openswr extra; plain Godot controls, not original art): every mod folder of the mods folder
/// with enable box and load-order arrows (enabled mods first, in load order). Save writes user://settings.cfg [mods] enabled
/// and test-loads the set over the install: errors are listed, a clean set takes effect for the next new game. Env
/// OPENSWR_MODS overrides the file (shown as a note).
/// </summary>
public partial class ModsScreen : Control
{
    public Action? Back { get; set; }
    public string GameDir { get; set; } = "";

    readonly List<(ModManifest Mod, bool Enabled)> _rows = [];
    VBoxContainer _list = null!;
    Label _result = null!;

    public override void _Ready()
    {
        Size = new Vector2(640, 480);
        AddChild(new ColorRect { Color = new Color(0.06f, 0.07f, 0.1f), Size = Size });
        AddChild(new Label { Text = "Mods", Position = new Vector2(24, 14), Size = new Vector2(592, 30), HorizontalAlignment = HorizontalAlignment.Center,
            LabelSettings = new LabelSettings { FontSize = 22 } });
        string note = $"Mods folder: {GameFiles.ModsDir()}. Changes apply to the next new game; saves need the mods they were played with.";
        if (GameFiles.ModsFromEnvironment) note += " OPENSWR_MODS is set and overrides this list.";
        AddChild(new Label { AutowrapMode = TextServer.AutowrapMode.Word, Text = note, Position = new Vector2(24, 46), Size = new Vector2(592, 36),
            LabelSettings = new LabelSettings { FontSize = 11, FontColor = new Color(0.7f, 0.7f, 0.7f) } });
        var scroll = new ScrollContainer { Position = new Vector2(24, 102), Size = new Vector2(592, 184) };
        AddChild(scroll);
        scroll.AddChild(_list = new VBoxContainer { CustomMinimumSize = new Vector2(580, 0) });
        var resultScroll = new ScrollContainer { Position = new Vector2(24, 296), Size = new Vector2(592, 136) };
        AddChild(resultScroll);
        resultScroll.AddChild(_result = new Label { CustomMinimumSize = new Vector2(580, 0), AutowrapMode = TextServer.AutowrapMode.Word,
            LabelSettings = new LabelSettings { FontSize = 11 } });
        AddChild(Button("Save", new Vector2(404, 440), Save));
        AddChild(Button("Back", new Vector2(516, 440), () => Back?.Invoke()));

        var available = ModLoader.Available(GameFiles.ModsDir());
        var enabled = GameFiles.EnabledMods();
        foreach (string id in enabled)
            _rows.Add((available.FirstOrDefault(m => m.Id == id) ?? new ModManifest(id, id + " (not found)", "", [], [], ""), true));
        foreach (var m in available.Where(m => !enabled.Contains(m.Id))) _rows.Add((m, false));
        Rebuild();
        Check();
    }

    static Button Button(string text, Vector2 at, Action pressed)
    {
        var b = new Button { Text = text, Position = at, Size = new Vector2(100, 28) };
        b.Pressed += pressed;
        return b;
    }

    void Rebuild()
    {
        foreach (var child in _list.GetChildren()) child.QueueFree();
        if (_rows.Count == 0) _list.AddChild(new Label { Text = "No mods found. Put each mod in its own folder (with a mod.json) in the mods folder." });
        for (int i = 0; i < _rows.Count; i++)
        {
            int at = i;
            var (mod, on) = _rows[i];
            var row = new HBoxContainer();
            var box = new CheckBox { ButtonPressed = on, Text = $"{mod.Name}  ({mod.Id} {mod.Version})", CustomMinimumSize = new Vector2(500, 0) };
            box.Toggled += v => _rows[at] = (_rows[at].Mod, v);
            row.AddChild(box);
            var up = new Button { Text = "Up", Disabled = i == 0 };
            up.Pressed += () => Move(at, -1);
            var down = new Button { Text = "Down", Disabled = i == _rows.Count - 1 };
            down.Pressed += () => Move(at, 1);
            row.AddChild(up);
            row.AddChild(down);
            _list.AddChild(row);
        }
    }

    void Move(int i, int dir)
    {
        (_rows[i], _rows[i + dir]) = (_rows[i + dir], _rows[i]);
        Rebuild();
    }

    void Save()
    {
        GameFiles.SaveEnabledMods(_rows.Where(r => r.Enabled).Select(r => r.Mod.Id));
        Check();
    }

    /// <summary>Test-loads the active set (what the next new game uses) and shows the result.</summary>
    void Check()
    {
        var active = GameFiles.EnabledMods();
        GameFiles.ForgetDefinitions();
        try
        {
            if (GameDir != "") GameFiles.Definitions(GameDir);
            _result.Text = active.Count == 0 ? "No mods enabled: the original game data." : "Active mods load cleanly: " + string.Join(", ", active) + ".";
            _result.LabelSettings.FontColor = new Color(0.6f, 1, 0.6f);
        }
        catch (ModLoadException e)
        {
            _result.Text = "Active mods (" + string.Join(", ", active) + ") have errors; no new game starts until they are fixed or disabled:"
                + "\n" + string.Join("\n", e.Errors);
            _result.LabelSettings.FontColor = new Color(1, 0.5f, 0.5f);
        }
        catch (System.Exception e) when (e is not System.OutOfMemoryException) // a mod that breaks the loader itself: show it, do not crash the screen
        {
            _result.Text = "Active mods (" + string.Join(", ", active) + ") cannot be loaded: " + e.Message;
            _result.LabelSettings.FontColor = new Color(1, 0.5f, 0.5f);
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Back?.Invoke();
        }
    }
}
