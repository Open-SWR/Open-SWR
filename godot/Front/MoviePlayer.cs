using System;
using System.IO;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data.Media;

/// <summary>
/// Plays one MDATA Smacker movie full screen (640 x 324 movies centred vertically, Inferred), audio on the "Music" bus
/// (movie volume = music volume). Any key or a left click ends it (openswr: the original ignores Esc and clicks, and the tactical
/// outcome movies do not end at all); cursor hidden.
/// </summary>
public partial class MoviePlayer : Control
{
    public string File { get; set; } = "";
    public Action? Finished { get; set; }
    /// <summary>
    /// Kind-6 event caption: text at (4, 404) 580 x 70, font 4, white; empty = none. REBDLOG.DLL 11061 (47 x 31) is drawn right of
    /// the text as the continue button (Inferred); a click on it ends the movie like a key.
    /// </summary>
    public string Caption { get; set; } = "";
    /// <summary>
    /// Event movies over the running strategy screen. Every mouse event and key stays here. The original ends them on any key
    /// except Esc and ignores clicks; here Esc and a left click end them too. Alt combinations (WM_SYSKEYDOWN) neither end the
    /// movie nor act. Any other key-down ends it and is consumed.
    /// </summary>
    public bool Swallow { get; set; }

    SmackerVideo _video = null!;
    Image _image = null!;
    ImageTexture _texture = null!;
    byte[] _rgb = [];
    double _ms;
    int _track = -1;
    SmackerAudioTrack _format;
    AudioStreamGeneratorPlayback? _playback;
    bool _done;

    public override void _Ready()
    {
        Size = new Vector2(640, 480);
        MouseFilter = MouseFilterEnum.Stop; // nothing under the movie takes clicks (the strategy screen is hidden)
        Input.MouseMode = Input.MouseModeEnum.Hidden;
        try
        {
            _video = new SmackerVideo(System.IO.File.ReadAllBytes(Path.Combine(Assets.GameDir, "MDATA", File)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException)
        {
            GD.PushWarning($"movie {File}: {ex.Message}");
            CallDeferred(MethodName.End);
            return;
        }
        _rgb = new byte[_video.Width * _video.Height * 3];
        _image = Image.CreateEmpty(_video.Width, _video.Height, false, Image.Format.Rgb8);
        _texture = ImageTexture.CreateFromImage(_image);
        if (DrawLog.On) DrawLog.Tag(_texture, "movie:" + File);
        if (_video.AudioTracks.Count > 0)
        {
            _format = _video.AudioTracks[0];
            _track = _format.Track;
            var player = new AudioStreamPlayer { Bus = "Music", Stream = new AudioStreamGenerator { MixRate = _format.SampleRate, BufferLength = 2 } };
            AddChild(player);
            player.Play();
            _playback = player.GetStreamPlayback() as AudioStreamGeneratorPlayback;
        }
        if (!Next()) CallDeferred(MethodName.End);
    }

    public override void _ExitTree() => Input.MouseMode = Input.MouseModeEnum.Visible;

    public override void _Process(double delta)
    {
        if (_done) return;
        for (_ms += delta * 1000; _ms >= _video.FrameDuration; _ms -= _video.FrameDuration)
            if (!Next())
            {
                End();
                return;
            }
    }

    static readonly Rect2 CaptionRect = new(4, 404, 580, 70);
    static Rect2 ButtonRect => new(new Vector2(589, 404 + (70 - Assets.BitmapSize(DialogBitmaps.CaptionContinue, "REBDLOG.DLL").Y) / 2), Assets.BitmapSize(DialogBitmaps.CaptionContinue, "REBDLOG.DLL"));

    public override void _Input(InputEvent e)
    {
        if (Caption != "" && e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } click && ButtonRect.HasPoint(click.Position))
        {
            GetViewport().SetInputAsHandled();
            End();
            return;
        }
        bool leftClick = e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };
        if (leftClick || e is InputEventKey { Pressed: true, Echo: false } key && !(Swallow && (key.AltPressed || key.Keycode == Key.Alt)))
        {
            GetViewport().SetInputAsHandled();
            End();
            return;
        }
        if (Swallow && e is InputEventKey or InputEventMouse) GetViewport().SetInputAsHandled();
    }

    bool Next()
    {
        try { if (!_video.NextFrame()) return false; }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException) // a damaged movie ends instead of crashing
        {
            GD.PushWarning($"movie {File}: {ex.Message}");
            return false;
        }
        var pixels = _video.Pixels;
        var palette = _video.Palette;
        for (int i = 0; i < pixels.Length; i++)
        {
            int c = pixels[i] * 3;
            _rgb[i * 3] = palette[c];
            _rgb[i * 3 + 1] = palette[c + 1];
            _rgb[i * 3 + 2] = palette[c + 2];
        }
        _image.SetData(_video.Width, _video.Height, false, Image.Format.Rgb8, _rgb);
        _texture.Update(_image);
        QueueRedraw();
        if (_track >= 0 && _playback is not null && _video.Audio[_track] is { } pcm) PushAudio(pcm);
        return true;
    }

    // PCM (16-bit signed LE or 8-bit unsigned, interleaved when stereo): generator frames.
    void PushAudio(byte[] pcm)
    {
        int bytes = (_format.Is16Bit ? 2 : 1) * (_format.Stereo ? 2 : 1);
        int count = Math.Min(pcm.Length / bytes, _playback!.GetFramesAvailable());
        var frames = new Vector2[count];
        float Sample(int at) => _format.Is16Bit ? BitConverter.ToInt16(pcm, at) / 32768f : (pcm[at] - 128) / 128f;
        for (int i = 0; i < count; i++)
        {
            int at = i * bytes;
            float l = Sample(at), r = _format.Stereo ? Sample(at + bytes / 2) : l;
            frames[i] = new Vector2(l, r);
        }
        _playback.PushBuffer(frames);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, 640, 480), Colors.Black);
        DrawLog.Fill(this, new Rect2(0, 0, 640, 480), Colors.Black);
        if (_texture is null) return;
        // logical rect: movies are 640 wide; shorter ones letterboxed
        DrawTextureRect(_texture, new Rect2(0, (480 - _video.Height) / 2, _video.Width, _video.Height), false);
        DrawLog.Bitmap(this, _texture, new Rect2(0, (480 - _video.Height) / 2, _video.Width, _video.Height));
        if (Caption == "") return;
        Assets.DrawText(this, CaptionRect, Caption, 4, Colors.White, TextFormat.Wrap);
        var button = DialogArt.CaptionContinue;
        DrawTextureRect(button, ButtonRect, false);
        DrawLog.Bitmap(this, button, ButtonRect);
    }

    void End()
    {
        if (_done) return;
        _done = true;
        Finished?.Invoke();
    }
}
