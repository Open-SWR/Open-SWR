using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Sound / display options in user://settings.cfg [options] (the original keeps them in the registry): music and effects
/// attenuation in hundredths of dB (0 = full), music switch, tactical display switches. Applied to the "Music" / "Sound" buses.
/// </summary>
/// <summary>Tactical display options (the original's registry DisplayOpts), each switched on or off.</summary>
public enum DisplayOption { HighDetailModels, Pyrotechnics, Planet, Holocube, Starfield }

public static class FrontSettings
{
    const string Path = "user://settings.cfg";

    public static int MusicVolume { get; set; }
    public static int Volume { get; set; }
    public static bool MusicOn { get; set; } = true;
    static readonly Dictionary<DisplayOption, bool> Displays = Enum.GetValues<DisplayOption>().ToDictionary(o => o, _ => true); // Inferred: all on by default

    /// <summary>Whether the tactical display option is on.</summary>
    public static bool Display(DisplayOption option) => Displays[option];
    public static void SetDisplay(DisplayOption option, bool on) => Displays[option] = on;
    static string DisplayKey(DisplayOption option) => "display_" + option.ToString().ToLowerInvariant();

    /// <summary>Window sizes offered by the launcher (openswr extra): all 4:3, the logical 640 x 480 scaled; aspect kept by the stretch.</summary>
    public static readonly Vector2I[] Resolutions = [new(640, 480), new(800, 600), new(1024, 768), new(1280, 960), new(1600, 1200), new(1920, 1440), new(2560, 1920)];
    public static Vector2I Resolution { get; set; } = new(1280, 960);
    public static bool Fullscreen { get; set; }
    /// <summary>The computer opponent of new games (launcher option): the original AI by default.</summary>
    public static OpenSWR.Sim.ComputerOpponent Opponent { get; set; }
    /// <summary>The OpenSWR AI's chat lines to the player (launcher option; a preference, not game state): on by default.</summary>
    public static bool ComputerChat { get; set; } = true;

    public static void Load()
    {
        var cfg = new ConfigFile();
        cfg.Load(Path);
        MusicVolume = (int)cfg.GetValue("options", "music_volume", 0);
        Volume = (int)cfg.GetValue("options", "volume", 0);
        MusicOn = (bool)cfg.GetValue("options", "music_on", true);
        foreach (var o in Enum.GetValues<DisplayOption>()) Displays[o] = (bool)cfg.GetValue("options", DisplayKey(o), true);
        Resolution = (Vector2I)cfg.GetValue("display", "resolution", Resolution);
        Fullscreen = (bool)cfg.GetValue("display", "fullscreen", false);
        Opponent = (int)cfg.GetValue("options", "computer_opponent", 0) == 1 ? OpenSWR.Sim.ComputerOpponent.OpenSWR : OpenSWR.Sim.ComputerOpponent.Original;
        ComputerChat = (bool)cfg.GetValue("options", "computer_chat", true);
        Apply();
    }

    public static void Save()
    {
        if (GameFiles.SettingsToWrite() is not { } cfg) return; // keeps [game] dir and [mods]
        cfg.SetValue("options", "music_volume", MusicVolume);
        cfg.SetValue("options", "volume", Volume);
        cfg.SetValue("options", "music_on", MusicOn);
        foreach (var o in Enum.GetValues<DisplayOption>()) cfg.SetValue("options", DisplayKey(o), Displays[o]);
        if (cfg.HasSectionKey("options", "display_opts")) cfg.EraseSectionKey("options", "display_opts"); // the former packed form
        cfg.SetValue("display", "resolution", Resolution);
        cfg.SetValue("display", "fullscreen", Fullscreen);
        cfg.SetValue("options", "computer_opponent", (int)Opponent);
        cfg.SetValue("options", "computer_chat", ComputerChat);
        cfg.Save(Path);
    }

    /// <summary>Creates the buses if missing and sets their volume from the attenuation.</summary>
    public static void Apply()
    {
        SetBus("Music", MusicVolume);
        SetBus("Sound", Volume);
    }

    /// <summary>Sizes the window (centred, clamped to the screen) or goes fullscreen; the 640 x 480 picture keeps its 4:3 aspect with bars.</summary>
    public static void ApplyWindow(Window window)
    {
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
        if (Fullscreen)
        {
            window.Mode = Window.ModeEnum.Fullscreen;
            return;
        }
        window.Mode = Window.ModeEnum.Windowed;
        var screen = DisplayServer.ScreenGetUsableRect(window.CurrentScreen).Size; // 0 x 0 when headless
        window.Size = screen.X > 0 && screen.Y > 0 ? Resolution.Min(screen) : Resolution;
        window.MoveToCenter();
    }

    static void SetBus(string name, int attenuation)
    {
        int bus = AudioServer.GetBusIndex(name);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, name);
        }
        AudioServer.SetBusVolumeDb(bus, attenuation / 100f);
        AudioServer.SetBusMute(bus, attenuation <= -9600);
    }

    /// <summary>Slider 0...100: attenuation: 2000*log10(v/100), -9600 for 0.</summary>
    public static int ToAttenuation(int v) => v <= 0 ? -9600 : (int)(2000 * Math.Log10(v / 100.0));

    /// <summary>Attenuation: slider: 100*10^(A/2000), 0 below -9600. Rounding Inferred (rounded here so values round-trip).</summary>
    public static int ToSlider(int a) => a < -9600 ? 0 : (int)Math.Round(100 * Math.Pow(10, a / 2000.0));
}
