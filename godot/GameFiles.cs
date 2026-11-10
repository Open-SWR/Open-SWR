using System;
using System.IO;
using System.Linq;
using Godot;
using OpenSWR.Data;

/// <summary>Locates the original game install and reads assets from it at runtime (nothing is copied).</summary>
public static class GameFiles
{
    const string SettingsPath = "user://settings.cfg";

    /// <summary>Display name (window title, launcher, options screen); the code, assemblies and user:// folder keep "openswr".</summary>
    public const string AppName = "Open-SWR";

    public static bool IsGameDir(string dir) => dir != "" && File.Exists(Path.Combine(dir, "STRATEGY.DLL"));

    /// <summary>
    /// Calls <paramref name="found"/> with the game folder: <paramref name="explicitDir"/>, else OPENSWR_GAME_DIR,
    /// else the folder remembered in user://settings.cfg, else asks with a folder picker and remembers the answer.
    /// </summary>
    public static void Locate(Node owner, string explicitDir, Action<string> found, Action<string> status)
    {
        var settings = new ConfigFile();
        settings.Load(SettingsPath);
        string? dir = new[]
        {
            explicitDir,
            System.Environment.GetEnvironmentVariable("OPENSWR_GAME_DIR") ?? "",
            (string)settings.GetValue("game", "dir", ""),
        }.FirstOrDefault(IsGameDir);
        if (dir is not null)
        {
            found(dir);
            return;
        }

        status("Select the folder of the original game (contains STRATEGY.DLL and GData).");
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Original game folder",
        };
        dialog.DirSelected += selected =>
        {
            if (!IsGameDir(selected))
            {
                status($"STRATEGY.DLL not found in '{selected}'. Try again.");
                dialog.PopupCentered(new Vector2I(600, 400));
                return;
            }
            RememberGameDir(selected);
            found(selected);
        };
        dialog.Canceled += () => dialog.PopupCentered(new Vector2I(600, 400)); // nothing works without the folder: ask again
        owner.AddChild(dialog);
        dialog.PopupCentered(new Vector2I(600, 400));
    }

    /// <summary>The folder the launcher starts with: env OPENSWR_GAME_DIR, else user://settings.cfg [game] dir (may be invalid or empty).</summary>
    public static string RememberedGameDir() =>
        System.Environment.GetEnvironmentVariable("OPENSWR_GAME_DIR") is { Length: > 0 } env ? env : (string)Settings().GetValue("game", "dir", "");

    /// <summary>Stores the game folder in user://settings.cfg [game] dir (forward slashes: backslashes break ConfigFile parsing).</summary>
    public static void RememberGameDir(string dir)
    {
        if (SettingsToWrite() is not { } write) return;
        write.SetValue("game", "dir", dir.Replace("\\", "/"));
        write.Save(SettingsPath);
    }

    static (string Key, OpenSWR.Data.Dat.GameDefinitions Definitions)? _definitions;

    static ConfigFile Settings()
    {
        var settings = new ConfigFile();
        settings.Load(SettingsPath);
        return settings;
    }

    /// <summary>
    /// user://settings.cfg for a read-modify-write; null when the file exists but cannot be read (saving then would keep only the
    /// writer's own section and drop the game folder and the mod list).
    /// </summary>
    public static ConfigFile? SettingsToWrite()
    {
        var settings = new ConfigFile();
        return settings.Load(SettingsPath) is Error.Ok or Error.FileNotFound ? settings : null;
    }

    /// <summary>The mods folder: env OPENSWR_MODS_DIR, else user://settings.cfg [mods] dir, else user://mods.</summary>
    public static string ModsDir() => System.Environment.GetEnvironmentVariable("OPENSWR_MODS_DIR") is { Length: > 0 } env ? env
        : (string)Settings().GetValue("mods", "dir", "") is { Length: > 0 } cfg ? cfg : ProjectSettings.GlobalizePath("user://mods");

    /// <summary>True when env OPENSWR_MODS sets the enabled mods (it overrides the settings file).</summary>
    public static bool ModsFromEnvironment => System.Environment.GetEnvironmentVariable("OPENSWR_MODS") is not null;

    /// <summary>Enabled mod ids in load order: env OPENSWR_MODS (comma-separated), else user://settings.cfg [mods] enabled.</summary>
    public static System.Collections.Generic.List<string> EnabledMods() => OpenSWR.Data.Mods.ModLoader.ParseList(
        System.Environment.GetEnvironmentVariable("OPENSWR_MODS") ?? (string)Settings().GetValue("mods", "enabled", ""));

    /// <summary>Writes user://settings.cfg [mods] enabled (the mods screen); the next <see cref="Definitions"/> call loads them.</summary>
    public static void SaveEnabledMods(System.Collections.Generic.IEnumerable<string> ids)
    {
        if (SettingsToWrite() is not { } settings) return;
        settings.SetValue("mods", "enabled", string.Join(",", ids));
        settings.Save(SettingsPath);
    }

    /// <summary>Drops the cached definitions and mod pictures: the mod files may have been edited since (the cache key names no file).</summary>
    public static void ForgetDefinitions() { _definitions = null; ModArt.Forget(); }

    /// <summary>
    /// The game's definitions: the install plus the enabled mods (<see cref="EnabledMods"/>, <see cref="ModsDir"/>).
    /// Cached until the game folder, the mods folder or the enabled list changes. Throws
    /// <see cref="OpenSWR.Data.Mods.ModLoadException"/> listing every error.
    /// </summary>
    public static OpenSWR.Data.Dat.GameDefinitions Definitions(string gameDir)
    {
        var enabled = EnabledMods();
        string modsDir = ModsDir(), key = gameDir + "|" + modsDir + "|" + string.Join(",", enabled);
        if (_definitions is { } known && known.Key == key) return known.Definitions;
        var definitions = OpenSWR.Data.Mods.ModLoader.Load(gameDir, modsDir, enabled);
        _definitions = (key, definitions);
        return definitions;
    }

    public static Image LoadBitmap(string gameDir, string module, ushort id)
    {
        var res = (gameDir == Assets.GameDir ? Assets.Module(module) : PeResources.Load(Path.Combine(gameDir, module))).Find(ResId.Bitmap, id)
            ?? throw new FileNotFoundException($"Bitmap {id} not in {module}.");
        var image = new Image();
        image.LoadBmpFromBuffer(Dib.ToBmp(res.Data.Span));
        return image;
    }
}
