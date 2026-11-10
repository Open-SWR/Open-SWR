using System;
using System.IO;
using Godot;
using OpenSWR.Data;

/// <summary>
/// The game's cursors from the player's REBEXE.EXE: group 1001 (arrow, hotspot
/// (0, 0)) replaces Godot's arrow everywhere, group 1002 image 4 (crosshair, hotspot (12, 12)) replaces the cross shape that the
/// targeting state sets. Scaled by the window's stretch factor (nearest), hotspot with it (Inferred: 1:1 on the 640 x 480 screen).
/// </summary>
public static class GameCursors
{
    static CursorImage? _arrow, _cross;
    static bool _loaded;
    static int _scale;

    public static void Install(Node node)
    {
        if (!_loaded)
        {
            _loaded = true;
            try
            {
                var exe = PeResources.Load(Path.Combine(Assets.GameDir, "REBEXE.EXE"));
                (_arrow, _cross) = (Cursors.Load(exe, UiIds.DefaultCursor), Cursors.Load(exe, UiIds.TargetingCursor));
            }
            catch (Exception e)
            {
                GD.PushWarning($"Game cursors not loaded: {e.Message}"); // Godot's own cursors stay
                return;
            }
            node.GetTree().Root.SizeChanged += Apply;
        }
        Apply();
    }

    static void Apply()
    {
        var size = DisplayServer.WindowGetSize();
        int k = Math.Max(1, (int)Math.Min(size.X / 640f, size.Y / 480f));
        if (k == _scale) return;
        _scale = k;
        Set(_arrow, Input.CursorShape.Arrow, k);
        Set(_cross, Input.CursorShape.Cross, k);
    }

    static void Set(CursorImage? c, Input.CursorShape shape, int k)
    {
        if (c is null) return;
        var image = Image.CreateFromData(c.Width, c.Height, false, Image.Format.Rgba8, c.Rgba);
        image.Resize(c.Width * k, c.Height * k, Image.Interpolation.Nearest);
        Input.SetCustomMouseCursor(image, shape, new Vector2(c.HotX * k, c.HotY * k));
    }
}
