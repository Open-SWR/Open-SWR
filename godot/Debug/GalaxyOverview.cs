using Godot;

/// <summary>Test scene: the galaxy map alone, with camera zoom and pan.</summary>
public partial class GalaxyOverview : Node2D
{
    const string Help = "Wheel: zoom   Right/middle drag: pan   Tab: sector names   Home: reset view";

    /// <summary>Original game folder. Empty = OPENSWR_GAME_DIR, then the folder remembered in user://settings.cfg, else ask.</summary>
    [Export] public string GameDir { get; set; } = "";

    GalaxyMap _map = null!;
    Camera2D _camera = null!;
    Label _info = null!;

    public override void _Ready()
    {
        _map = GetNode<GalaxyMap>("Map");
        _camera = GetNode<Camera2D>("Camera");
        _info = GetNode<Label>("Hud/Info");
        _map.HoverChanged += () => _info.Text = _map.HoverText ?? Help;

        GameFiles.Locate(this, GameDir, dir =>
        {
            _map.Load(dir);
            ResetView();
            _info.Text = $"{_map.SectorCount} sectors, {_map.SystemCount} systems.   {Help}";
        }, status => _info.Text = status);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Zoom(1.15f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Zoom(1 / 1.15f);
                break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Right) || Input.IsMouseButtonPressed(MouseButton.Middle):
                _camera.Position -= m.Relative / _camera.Zoom;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Tab }:
                _map.ShowSectors = !_map.ShowSectors;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Home }:
                ResetView();
                break;
        }
    }

    void Zoom(float factor) => _camera.Zoom = (_camera.Zoom * factor).Clamp(new Vector2(0.5f, 0.5f), new Vector2(12, 12));

    void ResetView()
    {
        _camera.Position = _map.Size / 2;
        _camera.Zoom = Vector2.One;
    }
}
