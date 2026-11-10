using System;
using Godot;

/// <summary>
/// Zoom animation: a 1-pixel outline moving from one rect to another in 4 steps of 50 ms, then
/// <see cref="Then"/> (the caller shows or hides its window). Step rule in integers: left / width / height += difference div 4
/// (truncated); top along the line through the destination corner (slope as a float), or += difference div 4 when the lefts are
/// equal. The original XORs a white pen (pixels inverted); openswr draws the pixels under the outline inverted through a screen
/// shader (approximation, Inferred). The original sleeps the UI thread for the 200 ms; here the outline animates on frames and a
/// full-screen stop control (plus <see cref="MainScreen"/>'s key check) ignores the strategy screen's input meanwhile.
/// </summary>
public partial class ZoomOutline : Control
{
    const double Step = 0.05;
    static Shader? _invert;
    /// <summary>Screen-inverting canvas shader (also the edit-field caret).</summary>
    public static Shader Invert => _invert ??= new Shader { Code = "shader_type canvas_item; uniform sampler2D screen : hint_screen_texture, filter_nearest; void fragment() { COLOR = vec4(vec3(1.0) - texture(screen, SCREEN_UV).rgb, 1.0); }" };
    readonly int _dl, _dw, _dh, _dt, _toLeft, _toTop;
    readonly float _slope;
    int _left, _top, _width, _height, _step;
    double _time;
    readonly ColorRect[] _edges = new ColorRect[4];
    public Action? Then { get; set; }
    public bool Done { get; private set; }

    public ZoomOutline(Rect2I from, Rect2I to)
    {
        (_left, _top, _width, _height) = (from.Position.X, from.Position.Y, from.Size.X, from.Size.Y);
        (_dl, _dw, _dh, _dt) = ((to.Position.X - from.Position.X) / 4, (to.Size.X - from.Size.X) / 4, (to.Size.Y - from.Size.Y) / 4, (to.Position.Y - from.Position.Y) / 4);
        (_toLeft, _toTop) = (to.Position.X, to.Position.Y);
        _slope = to.Position.X == from.Position.X ? 0f : (float)(to.Position.Y - from.Position.Y) / (to.Position.X - from.Position.X);
        MouseFilter = MouseFilterEnum.Stop; // input to the strategy screen ignored while it runs
        Size = new Vector2(640, 480);
        for (int i = 0; i < 4; i++) AddChild(_edges[i] = new ColorRect { MouseFilter = MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = Invert } });
        Place(Advance()); // the outline at `from` is erased at once by the first step
    }

    Rect2I Advance()
    {
        _left += _dl;
        _width += _dw;
        _height += _dh;
        _top = _slope == 0f ? _top + _dt : (int)(_left * _slope) + (_toTop - (int)(_toLeft * _slope));
        _step++;
        return new Rect2I(_left, _top, _width, _height);
    }

    void Place(Rect2I r)
    {
        // a hollow 1-pixel rectangle; a negative extent (the picker's legend quirk) is drawn normalised
        var n = new Rect2(r.Position, r.Size).Abs();
        _edges[0].Position = n.Position; _edges[0].Size = new Vector2(n.Size.X, 1);
        _edges[1].Position = new Vector2(n.Position.X, n.End.Y - 1); _edges[1].Size = new Vector2(n.Size.X, 1);
        _edges[2].Position = new Vector2(n.Position.X, n.Position.Y + 1); _edges[2].Size = new Vector2(1, Math.Max(0, n.Size.Y - 2));
        _edges[3].Position = new Vector2(n.End.X - 1, n.Position.Y + 1); _edges[3].Size = new Vector2(1, Math.Max(0, n.Size.Y - 2));
    }

    /// <summary>Env demo hold: frames stay on the given step (1-4) until freed.</summary>
    public int Hold { get; set; }

    public override void _Process(double delta)
    {
        if (Done || Hold != 0 && _step >= Hold) return;
        _time += delta;
        while (_time >= Step && !Done)
        {
            _time -= Step;
            if (_step < 4) Place(Advance());
            else Finish();
        }
    }

    /// <summary>Ends at once (last outline erased) and runs <see cref="Then"/>.</summary>
    public void Finish()
    {
        if (Done) return;
        Done = true;
        QueueFree();
        var then = Then;
        Then = null;
        then?.Invoke();
    }
}
