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
        _lever = Add(ControlIds.MainMenuGalaxySizeLever, new Lever
        {
            Rest = [CommonArt.GalaxyLeverHuge, CommonArt.GalaxyLeverLarge, CommonArt.GalaxyLeverStandard],
            Between = [CommonArt.GalaxyLeverHugeToLarge, CommonArt.GalaxyLeverLargeToStandard],
        }, 242, 271, 44, 47, CommonTexts.SelectGalaxySize, CommonSounds.GalaxyLever, () => SetSize(3 - _lever.Notch));
        Add(ControlIds.MainMenuStandardGalaxy, new FrontButton(), 290, 293, 24, 21, CommonTexts.StandardGalaxy, null, () => Bay(1));
        Add(ControlIds.MainMenuLargeGalaxy, new FrontButton(), 326, 293, 24, 21, CommonTexts.LargeGalaxy, null, () => Bay(2));
        Add(ControlIds.MainMenuHugeGalaxy, new FrontButton(), 362, 293, 24, 21, CommonTexts.HugeGalaxy, null, () => Bay(3));
        Animated(ControlIds.MainMenuStartAlliance, CommonBitmaps.StartAllianceFrames, 15, CommonBitmaps.StartAllianceStill, 437, 307, 62, 55, CommonTexts.StartAlliance, CommonSounds.Button, () => StartAs(SimOwner.Alliance));
        Animated(ControlIds.MainMenuStartEmpire, CommonBitmaps.StartEmpireFrames, 15, CommonBitmaps.StartEmpireStill, 153, 308, 62, 55, CommonTexts.StartEmpire, CommonSounds.Button, () => StartAs(SimOwner.Empire));
        _radio =
        [
            Animated(ControlIds.MainMenuNovice, CommonBitmaps.NoviceFrames, 30, CommonBitmaps.NoviceStill, 61, 41, 51, 36, CommonTexts.Novice, CommonSounds.Button, () => SetDifficulty(0), true),
            Animated(ControlIds.MainMenuIntermediate, CommonBitmaps.IntermediateFrames, 30, CommonBitmaps.IntermediateStill, 124, 40, 49, 36, CommonTexts.Intermediate, CommonSounds.Button, () => SetDifficulty(1), true),
            Animated(ControlIds.MainMenuExpert, CommonBitmaps.ExpertFrames, 30, CommonBitmaps.ExpertStill, 187, 41, 45, 36, CommonTexts.Expert, CommonSounds.Button, () => SetDifficulty(2), true),
        ];
        _radio[0].Checked = true;
        Add(ControlIds.MainMenuVictoryConditions, new FrontButton { Up = CommonArt.VictoryToggleUp, Down = CommonArt.VictoryToggleDown, CheckedImage = CommonArt.VictoryToggleDown, Toggle = true },
            305, 333, 42, 30, CommonTexts.VictoryConditions, CommonSounds.Button, () => { _victory = Controls[ControlIds.MainMenuVictoryConditions].Checked ? 2 : 1; QueueRedraw(); });
        Animated(ControlIds.MainMenuLoadGame, CommonBitmaps.LoadGameFrames, 15, CommonBitmaps.LoadGameStill, 411, 232, 40, 37, CommonTexts.LoadSavedGame, CommonSounds.Button, () => Load?.Invoke());
        Animated(ControlIds.MainMenuExit, CommonBitmaps.ExitFrames, 30, CommonBitmaps.ExitStill, 536, 393, 63, 64, CommonTexts.Exit, CommonSounds.Exit, () => Exit?.Invoke());
        Add(ControlIds.MainMenuCredits, new FrontButton { Up = CommonArt.CreditsUp, Down = CommonArt.CreditsDown }, 459, 242, 33, 28, CommonTexts.Credits, CommonSounds.Button, () => Credits?.Invoke());
        Animated(ControlIds.MainMenuHeadToHead, CommonBitmaps.HeadToHeadFrames, 30, CommonBitmaps.HeadToHeadStill, 67, 381, 51, 61, CommonTexts.HeadToHead, CommonSounds.HeadToHead, null); // head to head: skipped, inert

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
        int seed = (int)GD.RandRange(1, 2147483646); // The original takes its seed from the CRT rand()
        Start?.Invoke(new GameSettings(side, _galaxySize, _difficulty, _victory - 1, seed, Opponent: FrontSettings.Opponent));
    }

    public override void _Draw()
    {
        DrawTextureRect(CommonArt.MainMenuBackground, new Rect2(0, 0, 640, 480), false);
        if (DrawLog.On) DrawLog.Bitmap(this, CommonArt.MainMenuBackground, new Rect2(0, 0, 640, 480));
        var (indicator, x) = _galaxySize switch { 1 => (CommonBitmaps.GalaxySizeStandard, 290), 2 => (CommonBitmaps.GalaxySizeLarge, 326), _ => (CommonBitmaps.GalaxySizeHuge, 362) };
        DrawTextureRect(Assets.Bitmap((ushort)indicator), new Rect2(x, 293, 24, 21), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Assets.Bitmap((ushort)indicator), new Rect2(x, 293, 24, 21));
        Assets.DrawText(this, new Rect2(271, 374, 110, 14), Assets.Comm(_victory == 1 ? CommonTexts.StandardGame : CommonTexts.HeadquartersVictory), 10, new Color(0, 1, 0), TextFormat.CentredRow);
    }
}
