using System;
using Godot;

/// <summary>
/// Bitmap button: up / down / disabled / checked images, optional toggle. Press inside plays the sound; release inside flips a
/// toggle, then runs <see cref="Command"/>. No hover image. Without images it is a plain hit rectangle.
/// </summary>
public partial class FrontButton : Control
{
    public Texture2D? Up { get; set; }
    public Texture2D? Down { get; set; }
    public Texture2D? DisabledImage { get; set; }
    public Texture2D? CheckedImage { get; set; }
    public AudioStream? Sound { get; set; }
    public bool Toggle { get; set; }
    public Action? Command { get; set; }

    bool _checked, _disabled;
    public bool Checked { get => _checked; set { _checked = value; QueueRedraw(); } }
    public bool Disabled { get => _disabled; set { _disabled = value; QueueRedraw(); } }

    protected bool Held, Inside;

    public void Place(int x, int y, int w, int h)
    {
        Position = new Vector2(x, y);
        Size = new Vector2(w, h);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (Disabled) return; // disabled buttons never hit
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                Held = Inside = true;
                Assets.PlaySound(Sound);
                OnPress();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } b when Held:
                Held = false;
                OnRelease(new Rect2(Vector2.Zero, Size).HasPoint(b.Position));
                break;
            case InputEventMouseMotion m when Held:
                Inside = new Rect2(Vector2.Zero, Size).HasPoint(m.Position);
                break;
            default:
                return;
        }
        QueueRedraw();
    }

    /// <summary>A full click (press + release inside), for keyboard / demo use.</summary>
    public void Click()
    {
        if (Disabled) return;
        Assets.PlaySound(Sound);
        OnPress();
        OnRelease(true);
        QueueRedraw();
    }

    protected virtual void OnPress() { }

    protected virtual void OnRelease(bool inside)
    {
        if (!inside) return;
        if (Toggle) Checked = !Checked;
        Command?.Invoke();
    }

    protected virtual Texture2D? UpImage => Up;

    protected virtual Texture2D? Current() =>
        Disabled ? DisabledImage ?? UpImage : Held && Inside ? Down ?? UpImage : Checked ? CheckedImage ?? UpImage : UpImage;

    public override void _Draw()
    {
        if (Current() is { } t) { DrawTextureRect(t, new Rect2(Vector2.Zero, Size), false); DrawLog.Bitmap(this, t, new Rect2(Vector2.Zero, Size)); }
    }
}

/// <summary>Animated button: the up image is a looping frame list, advanced by the screen's 66 ms timer.</summary>
public partial class AnimatedButton : FrontButton
{
    public Texture2D[] Frames { get; set; } = [];
    int _frame;

    public void Step()
    {
        if (Frames.Length == 0) return;
        _frame = (_frame + 1) % Frames.Length;
        QueueRedraw();
    }

    protected override Texture2D? UpImage => Frames.Length > 0 ? Frames[_frame] : Up;
}

/// <summary>
/// Three-position lever: each press steps one place in the current direction, bouncing at the ends; while held it shows the
/// in-between image of the step. Starts at position 2, decreasing.
/// </summary>
public partial class Lever : FrontButton
{
    public Texture2D[] Rest { get; set; } = [];
    public Texture2D[] Between { get; set; } = [];
    public int Notch { get; private set; } = 2;
    int _direction = -1, _from = 2;

    /// <summary>Sets the position directly (bay clicks; the original animates there with simulated clicks).</summary>
    public void SetPosition(int position)
    {
        Notch = _from = position;
        if (position == 0) _direction = 1;
        if (position == 2) _direction = -1;
        QueueRedraw();
    }

    protected override void OnPress()
    {
        if (Notch + _direction is < 0 or > 2) _direction = -_direction;
        _from = Notch;
        Notch += _direction;
        Command?.Invoke();
    }

    protected override void OnRelease(bool inside) => _from = Notch;

    protected override Texture2D? Current() => _from != Notch ? Between[Math.Min(_from, Notch)] : Rest[Notch];
}

/// <summary>Slider: track bitmap, thumb moved along it, value 0...100; thumb travel = track width - thumb width (Inferred).</summary>
public partial class FrontSlider : Control
{
    public Texture2D? Track { get; set; }
    public Texture2D? Thumb { get; set; }
    public Vector2 ThumbSize { get; set; }
    public Action<int>? Changed { get; set; }

    int _value;
    public int Value { get => _value; set { _value = Math.Clamp(value, 0, 100); QueueRedraw(); } }

    public override void _GuiInput(InputEvent e)
    {
        float x;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } b: x = b.Position.X; break;
            case InputEventMouseMotion m when Input.IsMouseButtonPressed(MouseButton.Left): x = m.Position.X; break;
            default: return;
        }
        int v = (int)Math.Round((x - ThumbSize.X / 2) / (Size.X - ThumbSize.X) * 100);
        AcceptEvent();
        if (Math.Clamp(v, 0, 100) == Value) return;
        Value = v;
        Changed?.Invoke(Value);
    }

    public override void _Draw()
    {
        if (Track is not null) { DrawTextureRect(Track, new Rect2(Vector2.Zero, Size), false); DrawLog.Bitmap(this, Track, new Rect2(Vector2.Zero, Size)); }
        // Inferred: thumb vertically centred on the track
        var at = new Vector2(Value / 100f * (Size.X - ThumbSize.X), (int)((Size.Y - ThumbSize.Y) / 2));
        if (Thumb is not null) { DrawTextureRect(Thumb, new Rect2(at, ThumbSize), false); DrawLog.Bitmap(this, Thumb, new Rect2(at, ThumbSize)); }
    }
}
