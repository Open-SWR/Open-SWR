using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Data.Advisor;
using OpenSWR.Data.Dat;

/// <summary>
/// Test scene: advisor displays over the strategy frame; pick an SPT action and play it.
/// Requests, priorities and cooldowns are not modelled here: actions play on key press.
/// </summary>
public partial class AdvisorTest : Node2D
{
    static readonly Vector2 LogicalSize = new(640, 480);
    const string Help = "Left/Right: action   Enter: play   S: switch side";

    // Alliance layout; the Empire layout was not read, assumed equal (Inferred).
    static readonly Vector2 MainAt = new(541, 337), SecondaryAt = new(316, 411);
    const ushort IdleHold = 403, IdleTicks = 15, SecondaryEnd = 11575;

    static readonly (string Side, string Script, string Dll, ushort Frame)[] Sides =
        [("Alliance", "C3POACT.SPT", "ALSPRITE.DLL", 900), ("Empire", "IMP22ACT.SPT", "EMSPRITE.DLL", 901)];

    /// <summary>Original game folder. Empty = OPENSWR_GAME_DIR, then user://settings.cfg, else ask.</summary>
    [Export] public string GameDir { get; set; } = "";

    /// <summary>Action played once at start (1-based id, 0 = none); for headless frame checks.</summary>
    [Export] public int AutoPlay { get; set; }

    string _gameDir = "";
    int _side, _action;
    List<SptAction> _actions = [];
    SpriteDll _dll = null!;
    AdvisorSprites _sprites = null!;
    AdvisorDisplay _main = null!, _secondary = null!;
    Sprite2D _frame = null!;
    Label _info = null!;

    public override void _Ready()
    {
        _frame = GetNode<Sprite2D>("Frame");
        _main = GetNode<AdvisorDisplay>("Main");
        _secondary = GetNode<AdvisorDisplay>("Secondary");
        _info = GetNode<Label>("Hud/Info");
        _main.Position = MainAt;
        _secondary.Position = SecondaryAt;
        _main.Idle = () => new StillCommand(Bitmap(_dll.Words(IdleHold)[1]), IdleTicks); // type 18 hold; word order Inferred

        GameFiles.Locate(this, GameDir, dir =>
        {
            _gameDir = dir;
            LoadSide(0);
            if (AutoPlay > 0)
            {
                _action = AutoPlay - 1;
                UpdateInfo();
                Play(_actions[_action]);
            }
        }, status => _info.Text = status);
    }

    void LoadSide(int side)
    {
        _side = side;
        var (_, script, dll, frame) = Sides[side];
        _dll = new SpriteDll(PeResources.Load(Path.Combine(_gameDir, dll)));
        _sprites = new AdvisorSprites(_dll);
        _actions = SptFile.Read(File.ReadAllBytes(Path.Combine(_gameDir, "GData", script)));
        var texture = ImageTexture.CreateFromImage(GameFiles.LoadBitmap(_gameDir, "STRATEGY.DLL", frame));
        _frame.Texture = texture;
        _frame.Scale = LogicalSize / texture.GetSize();
        _action = 0;
        UpdateInfo();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_gameDir == "" || e is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Right: _action = (_action + 1) % _actions.Count; UpdateInfo(); break;
            case Key.Left: _action = (_action + _actions.Count - 1) % _actions.Count; UpdateInfo(); break;
            case Key.Enter or Key.KpEnter: Play(_actions[_action]); break;
            case Key.S: LoadSide((_side + 1) % Sides.Length); break;
        }
    }

    void UpdateInfo()
    {
        var a = _actions[_action];
        string steps = string.Join(" | ", a.Steps.Select(s => s.Briefing ? $"briefing {s.RcId}" : $"{s.RcId}: type {_dll.Command(s.RcId).Type}"));
        _info.Text = $"{Sides[_side].Side} action {a.Id}/{_actions.Count}: {steps}   {Help}";
    }

    // Verbose commentary is on by default.
    void Play(SptAction action)
    {
        foreach (var step in action.Steps)
        {
            if (step.Briefing) continue; // briefing DLL (module 13): not shipped by the 1.02 install
            var command = _dll.Command(step.RcId);
            var (p1, p2) = command.Parameters(step);
            if (Build(command, () => _info.Text = $"Post-action command ({p1}, {p2})   {Help}") is { } c) _main.Enqueue(c);
        }
    }

    AdvisorCommand? Build(SpriteCommand command, Action? finished = null) => command.Type switch
    {
        SpriteCommand.Clip or SpriteCommand.VoiceClip => Clip(command.ResId),
        SpriteCommand.Still or SpriteCommand.SecondaryStill => new StillCommand(Bitmap(_dll.Words(command.ResId)[0]), 2, finished),
        SpriteCommand.Relay => Relay(_dll.Words(command.ResId)),
        _ => null, // type 3 and unregistered types play nothing
    };

    AdvisorCommand Relay(ushort[] words) => new RelayCommand(Bitmap(words[1]), _secondary, done =>
        new[] { Build(_dll.Command(words[2])), Build(_dll.Command(SecondaryEnd), done) }.OfType<AdvisorCommand>());

    ClipCommand Clip(ushort rcId) => _sprites.Clip(rcId, _sprites);

    Texture2D Bitmap(ushort id) => _sprites.Bitmap(id);
}
