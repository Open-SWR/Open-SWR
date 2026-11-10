namespace OpenSWR.Data;

/// <summary>Godot-free scroll bar math shared by the UI's lists (godot/CellList.cs ScrollBar); plain floats so tests can reach it.</summary>
public static class UiMath
{
    /// <summary>Thumb top and length on the track [<paramref name="top"/>, <paramref name="bottom"/>) for rows that overflow (total &gt; visible).</summary>
    public static (float Y, float Length) Thumb(float top, float bottom, float minLength, int first, int visible, int total)
    {
        float track = bottom - top;
        float len = Math.Max(minLength, track * visible / total);
        return (top + (track - len) * first / (total - visible), len);
    }

    /// <summary>First row for a thumb dragged to top <paramref name="y"/> (inverse of <see cref="Thumb"/>, unclamped).</summary>
    public static int FirstAt(float top, float bottom, float minLength, float y, int visible, int total)
    {
        float room = bottom - top - Thumb(top, bottom, minLength, 0, visible, total).Length;
        return room <= 0 ? 0 : (int)Math.Round((y - top) / room * (total - visible));
    }
}
