using System;
using System.Collections.Generic;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Main menu = new-game setup (COMMON 20001): galaxy lever + bays, Alliance / Empire start, difficulty radio, victory toggle,
/// Load, Exit, Credits; Head to Head drawn but inert. Animated buttons step on one 66 ms timer.
/// </summary>
public partial class MainMenu : Control
{
    public Action<GameSettings>? Start { get; set; }
    public Action? Load { get; set; }
    public Action? Exit { get; set; }
    public Action? Credits { get; set; }

    /// <summary>Controls by command id, for simulated clicks.</summary>
    public Dictionary<int, FrontButton> Controls { get; } = [];

    int _galaxySize = 1, _difficulty, _victory = 1; // defaults: Standard galaxy, Novice, Standard Game
    readonly List<AnimatedButton> _animated = [];
    Timer _timer = null!;
    Lever _lever = null!;
    FrontButton[] _radio = [];

    public override void _Ready()
    {
        Size = new Vector2(640, 480);
        _lever = Add(0x6A, new Lever
        {
            Rest = [Assets.Bitmap(10001), Assets.Bitmap(10002), Assets.Bitmap(10003)],
            Between = [Assets.Bitmap(10014), Assets.Bitmap(10015)],
        }, 242, 271, 44, 47, 4097, 8000, () => SetSize(3 - _lever.Notch));
        Add(0x6B, new FrontButton(), 290, 293, 24, 21, 4111, null, () => Bay(1));
        Add(0x6C, new FrontButton(), 326, 293, 24, 21, 4110, null, () => Bay(2));
        Add(0x6D, new FrontButton(), 362, 293, 24, 21, 4109, null, () => Bay(3));
        Animated(0x65, 11031, 15, 10007, 437, 307, 62, 55, 4098, 8004, () => StartAs(SimOwner.Alliance));
        Animated(0x66, 11001, 15, 10009, 153, 308, 62, 55, 4099, 8004, () => StartAs(SimOwner.Empire));
        _radio =
        [
            Animated(0x6E, 11061, 30, 11273, 61, 41, 51, 36, 4103, 8004, () => SetDifficulty(0), true),
            Animated(0x6F, 11091, 30, 11275, 124, 40, 49, 36, 4104, 8004, () => SetDifficulty(1), true),
            Animated(0x70, 11121, 30, 11274, 187, 41, 45, 36, 4105, 8004, () => SetDifficulty(2), true),
        ];
        _radio[0].Checked = true;
        Add(0x71, new FrontButton { Up = Assets.Bitmap(10158), Down = Assets.Bitmap(10159), CheckedImage = Assets.Bitmap(10159), Toggle = true },
            305, 333, 42, 30, 4106, 8004, () => { _victory = Controls[0x71].Checked ? 2 : 1; QueueRedraw(); });
        Animated(0x68, 11241, 15, 10013, 411, 232, 40, 37, 4101, 8004, () => Load?.Invoke());
        Animated(0x69, 11181, 30, 10011, 536, 393, 63, 64, 4102, 8002, () => Exit?.Invoke());
        Add(0x73, new FrontButton { Up = Assets.Bitmap(11271), Down = Assets.Bitmap(11272) }, 459, 242, 33, 28, 4107, 8004, () => Credits?.Invoke());
        Animated(0x67, 11151, 30, 10005, 67, 381, 51, 61, 4100, 8001, null); // head to head: skipped, inert

        AddChild(_timer = new Timer { WaitTime = 0.066, Autostart = true });
        _timer.Timeout += () => { foreach (var b in _animated) b.Step(); };
    }

    T Add<T>(int id, T control, int x, int y, int w, int h, int tooltip, ushort? sound, Action? command) where T : FrontButton
    {
        control.Place(x, y, w, h);
        control.TooltipText = Assets.Tip(Assets.Comm(tooltip));
        control.Sound = sound is { } s ? Assets.Wave(s) : null;
        control.Command = command;
        Controls[id] = control;
        AddChild(control);
        return control;
    }

    AnimatedButton Animated(int id, ushort first, int count, ushort still, int x, int y, int w, int h, int tooltip, ushort sound, Action? command, bool radio = false)
    {
        var b = Add(id, new AnimatedButton { Frames = Assets.Frames(first, count), Down = Assets.Bitmap(still), CheckedImage = radio ? Assets.Bitmap(still) : null },
            x, y, w, h, tooltip, sound, command);
        _animated.Add(b);
        return b;
    }

    void SetSize(int size)
    {
        _galaxySize = size;
        QueueRedraw();
    }

    // Bay rectangle: lever straight to the bay's position (the original animates it with simulated clicks).
    void Bay(int size)
    {
        if (size == _galaxySize) return;
        _lever.SetPosition(3 - size);
        Assets.PlaySound(_lever.Sound);
        SetSize(size);
    }

    void SetDifficulty(int difficulty)
    {
        _difficulty = difficulty;
        for (int i = 0; i < _radio.Length; i++) _radio[i].Checked = i == difficulty;
    }

    // Settings column = difficulty + 1 (Alliance) / + 4 (Empire), via GameSettings.Column; victory flag = victory - 1.
    void StartAs(SimOwner side)
    {
        int seed = (int)GD.RandRange(1, 0x7FFFFFFE); // The original takes its seed from the CRT rand()
        Start?.Invoke(new GameSettings(side, _galaxySize, _difficulty, _victory - 1, seed, Opponent: FrontSettings.Opponent));
    }

    public override void _Draw()
    {
        DrawTextureRect(Assets.Bitmap(20001), new Rect2(0, 0, 640, 480), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Assets.Bitmap(20001), new Rect2(0, 0, 640, 480));
        var (indicator, x) = _galaxySize switch { 1 => (10019, 290), 2 => (10018, 326), _ => (10017, 362) };
        DrawTextureRect(Assets.Bitmap((ushort)indicator), new Rect2(x, 293, 24, 21), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Assets.Bitmap((ushort)indicator), new Rect2(x, 293, 24, 21));
        Assets.DrawText(this, new Rect2(271, 374, 110, 14), Assets.Comm(_victory == 1 ? 4192 : 4193), 10, new Color(0, 1, 0), TextFormat.CentredRow);
    }
}
