using System;
using System.Collections.Generic;
using Godot;
using OpenSWR.Data.Advisor;

/// <summary>
/// One advisor sprite display: plays a queue of sprite commands at a fixed 67 ms frame period.
/// Enqueueing while a command runs interrupts it; an empty queue plays the idle command.
/// </summary>
public partial class AdvisorDisplay : Node2D
{
    public Func<AdvisorCommand>? Idle { get; set; }

    readonly Queue<AdvisorCommand> _queue = new();
    AdvisorCommand? _running;
    double _elapsedMs;
    Sprite2D _sprite = null!;
    AudioStreamPlayer _voice = null!, _sound2 = null!;

    public override void _Ready()
    {
        AddChild(_sprite = new Sprite2D { Centered = false });
        AddChild(_voice = new AudioStreamPlayer { Bus = "Sound" }); // effects volume, no separate voice volume (Inferred)
        AddChild(_sound2 = new AudioStreamPlayer { Bus = "Sound" });
    }

    public void Enqueue(AdvisorCommand command)
    {
        _running?.Interrupt();
        _queue.Enqueue(command);
    }

    /// <summary>Flush (Inferred): the queue cleared, the running command interrupted.</summary>
    public void Flush()
    {
        _queue.Clear();
        _running?.Interrupt();
    }

    public override void _Process(double delta)
    {
        for (_elapsedMs += delta * 1000; _elapsedMs >= SpriteDll.FramePeriodMs; _elapsedMs -= SpriteDll.FramePeriodMs)
            Tick();
    }

    void Tick()
    {
        if (_running is null)
        {
            StartNext();
            return;
        }
        if (!_running.Tick(this)) return;
        _running.Finish(this);
        StartNext();
    }

    void StartNext()
    {
        _running = _queue.Count > 0 ? _queue.Dequeue() : Idle?.Invoke();
        _running?.Start(this);
    }

    public void Show(Texture2D? texture) => _sprite.Texture = texture;

    public void PlaySounds(AudioStream? voice, AudioStream? sound2)
    {
        _voice.Stream = voice;
        _sound2.Stream = sound2;
        if (voice is not null) _voice.Play();
        if (sound2 is not null) _sound2.Play();
    }

    public void StopSounds()
    {
        _voice.Stop();
        _sound2.Stop();
    }

    public bool VoicePlaying => _voice.Playing;
}

/// <summary>Textures and sounds of one sprite DLL (module 9 or the briefing DLL, module 13), cached; frames palette-indexed, index 0 transparent.</summary>
public sealed class AdvisorSprites(SpriteDll dll)
{
    public SpriteDll Dll => dll;
    public string Module { get; set; } = "SPRITE.DLL"; // the sprite DLL's file name, for the draw log
    readonly Dictionary<ushort, Texture2D[]> _clips = [];
    readonly Dictionary<ushort, Texture2D> _bitmaps = [];

    public Texture2D Bitmap(ushort id)
    {
        if (!_bitmaps.TryGetValue(id, out var texture))
        {
            var b = dll.Bitmap(id);
            _bitmaps[id] = texture = Texture(b.Width, b.Height, b.Pixels, b.Palette);
            DrawLog.Register(texture, Module, id, new Vector2I(b.Width, b.Height), SpriteDll.TransparentIndex);
        }
        return texture;
    }

    /// <summary>Clip entry <paramref name="rcId"/>: frames from this DLL, voice from <paramref name="voices"/> (the clip's VoiceModule), second sound from this DLL.</summary>
    public ClipCommand Clip(ushort rcId, AdvisorSprites? voices)
    {
        var clip = dll.Clip(rcId);
        if (!_clips.TryGetValue(rcId, out var frames))
        {
            var indexed = dll.ClipFrames(clip);
            _clips[rcId] = frames = [.. System.Linq.Enumerable.Select(indexed.Frames, f => Texture(indexed.Width, indexed.Height, f, indexed.Palette))];
            if (DrawLog.On) for (int i = 0; i < frames.Length; i++) // frame 0 = the base RT_BITMAP, frame i = 302 delta BaseBitmap + i over it
                DrawLog.Register(frames[i], Module, (ushort)(clip.BaseBitmap + i), new Vector2I(indexed.Width, indexed.Height), SpriteDll.TransparentIndex, i == 0 ? 2 : 302);
        }
        return new ClipCommand(frames, voices?.Wave(clip.VoiceWave), clip.Sound2 != 0 ? Wave(clip.Sound2) : null);
    }

    public AudioStreamWav? Wave(ushort id) => dll.Wave(id) is { } wav ? AudioStreamWav.LoadFromBuffer(wav) : null;

    static ImageTexture Texture(int w, int h, byte[] pixels, byte[] palette) => ImageTexture.CreateFromImage(
        Image.CreateFromData(w, h, false, Image.Format.Rgba8, OpenSWR.Data.IndexedBitmap.ToRgba(pixels, palette, SpriteDll.TransparentIndex)));
}

/// <summary>Sprite command (types 4/19 clip, 17/21 still, 18 hold, 20 relay).</summary>
public abstract class AdvisorCommand
{
    protected bool Interrupted;
    public abstract void Start(AdvisorDisplay display);
    /// <summary>Advance one frame tick; true when done.</summary>
    public abstract bool Tick(AdvisorDisplay display);
    public virtual void Interrupt() => Interrupted = true;
    public virtual void Finish(AdvisorDisplay display) { }
}

/// <summary>Types 4 and 19: base frame, then one frame per tick, voice from frame 0. Done after the last frame once the voice stopped.</summary>
public sealed class ClipCommand(Texture2D[] frames, AudioStream? voice, AudioStream? sound2) : AdvisorCommand
{
    int _frame;

    public override void Start(AdvisorDisplay display)
    {
        display.Show(frames[0]);
        display.PlaySounds(voice, sound2);
    }

    public override bool Tick(AdvisorDisplay display)
    {
        if (Interrupted)
        {
            display.StopSounds();
            return true;
        }
        if (_frame + 1 < frames.Length) display.Show(frames[++_frame]);
        return _frame + 1 >= frames.Length && !display.VoicePlaying;
    }
}

/// <summary>Types 17 / 21 (2 ticks) and 18 (hold for N ticks): show one bitmap, then run <paramref name="finished"/>.</summary>
public sealed class StillCommand(Texture2D? texture, int ticks, Action? finished = null) : AdvisorCommand
{
    int _count = 1;

    public override void Start(AdvisorDisplay display) => display.Show(texture);
    public override bool Tick(AdvisorDisplay display) => Interrupted || ++_count >= ticks;
    public override void Finish(AdvisorDisplay display) => finished?.Invoke();
}

/// <summary>Type 20: show a bitmap here while the secondary display plays its commands; done when those finish.</summary>
public sealed class RelayCommand(Texture2D? texture, AdvisorDisplay secondary, Func<Action, IEnumerable<AdvisorCommand>> secondaryCommands) : AdvisorCommand
{
    bool _secondaryDone;

    public override void Start(AdvisorDisplay display)
    {
        display.Show(texture);
        foreach (var command in secondaryCommands(() => _secondaryDone = true)) secondary.Enqueue(command);
    }

    public override bool Tick(AdvisorDisplay display) => Interrupted || _secondaryDone;
}
