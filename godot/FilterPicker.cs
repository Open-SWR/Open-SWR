using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>
/// Map display filter picker "Galactic Information Display": a
/// 160 x 160 list at (370, 233) A / (420, 233) E, rows at x 30 (icon, label right of it), height 20; a click on a group row opens
/// its submenu (240 wide, rows from y 7; observed in the original: clicks only, no hover timer); a click on a row without a submenu sends
/// its mode (command 0x140). Titles and legend
/// entries per mode. The zoom animation is left out.
/// </summary>
public static class FilterPicker
{
    record Row(int Mode, int Label, ushort A, ushort E, Row[]? Sub = null);

    static readonly Row[] Rows =
    [
        new(0x10, 5632, 10110, 10119, [new(0x11, 5653, 10110, 10119), new(0x12, 5654, 11608, 11609)]),
        new(0x20, 5633, 10111, 10120, [new(0x21, 5655, 10111, 10120), new(0x22, 5656, 11613, 11614)]),
        new(0x40, 5634, 10112, 10121, [new(0x43, 5661, 10138, 10121), new(0x44, 5662, 10140, 10127)]),
        new(0x50, 5637, 10113, 10122, [new(0x51, 5638, 10134, 10134), new(0x52, 5639, 10135, 10135), new(0x53, 5640, 10136, 10136), new(0x54, 5641, 10137, 10137)]),
        new(0x60, 5642, 10114, 10123, [new(0x62, 5644, 10131, 10131), new(0x65, 5660, 10131, 10131), new(0x63, 5645, 10132, 10132),
            new(0x66, 5659, 10132, 10132), new(0x64, 5646, 10133, 10133), new(0x67, 5658, 10133, 10133)]),
        new(0x70, 5647, 10115, 10124, [new(0x75, 5657, 11610, 11610), new(0x74, 5651, 10143, 10124), new(0x72, 5649, 10141, 10126),
            new(0x71, 5648, 10140, 10127), new(0x73, 5650, 10142, 10142)]),
        new(0x80, 5652, 0, 0),
    ];

    /// <summary>Submenu heights by row count (2: 55, 4: 100, 5: 122, 6: 142).</summary>
    static readonly Dictionary<int, int> SubHeights = new() { [2] = 55, [4] = 100, [5] = 122, [6] = 142 };

    /// <summary>Title string per mode (picker modes only).</summary>
    public static readonly Dictionary<int, int> Titles = new()
    {
        [0x10] = 5653, [0x11] = 5653, [0x12] = 5654, [0x20] = 5655, [0x21] = 5655, [0x22] = 5656, [0x43] = 5661, [0x44] = 5662,
        [0x51] = 5638, [0x52] = 5639, [0x53] = 5640, [0x54] = 5641, [0x61] = 5643, [0x62] = 5644, [0x63] = 5645, [0x64] = 5646,
        [0x65] = 5660, [0x66] = 5659, [0x67] = 5658, [0x71] = 5648, [0x72] = 5649, [0x73] = 5650, [0x74] = 5651, [0x75] = 5657,
    };

    /// <summary>Legend entry per mode; 0 = none (the window stays empty, Inferred).</summary>
    public static int Legend(int mode, bool sideOne) => mode switch
    {
        0x11 => sideOne ? 10183 : 10184,
        0x12 => 11600,
        0x21 => 11601,
        0x22 => 11602,
        0x30 => 10201,
        0x43 => 11603,
        0x44 => 11604,
        >= 0x51 and <= 0x54 => 10188 + mode - 0x51,
        >= 0x61 and <= 0x64 => 10192 + mode - 0x61,
        >= 0x65 and <= 0x67 => 11605 + mode - 0x65,
        >= 0x71 and <= 0x75 => 10196 + mode - 0x71,
        _ => 0,
    };

    /// <summary>Alt+1 ... Alt+9.</summary>
    public static readonly int[] AltModes = [0x11, 0x12, 0x21, 0x22, 0x43, 0x44, 0x65, 0x66, 0x67];

    static GameMenu.Entry Entry(Row r, bool sideOne) =>
        new(r.Mode, Assets.Stra(r.Label), sideOne ? r.A : r.E, 0, 0, 0, true, false, [.. (r.Sub ?? []).Select(s => Entry(s, sideOne))]);

    static Vector2 Img(ushort id) => (Vector2)Assets.BitmapSize(id, "STRATEGY.DLL");

    /// <summary>Fixed layout: picker 160 x 160, rows at y 10 + 22*i; submenus 240 wide, rows at y 7 + 22*i; label at 30 + icon width (Display Off: + the width of 10115).</summary>
    static GameMenu.Level Layout(List<GameMenu.Entry> rows)
    {
        bool main = rows.Any(r => r.Id == 0x80);
        float width = main ? 160 : 240, top = main ? 10 : 7;
        var geometry = rows.Select((r, i) => (new Rect2(0, top + 22 * i, width, 20), 30f, 30 + Img(r.Image != 0 ? r.Image : (ushort)10115).X)).ToList();
        return new GameMenu.Level(rows, new Vector2(width, main ? 160 : SubHeights.GetValueOrDefault(rows.Count, (int)(top + 22 * rows.Count))), geometry);
    }

    /// <summary>The picker as a popup list; the arrow bitmaps 10117 / 10118 (A) and 10128 / 10129 (E) mark the group rows (Inferred: normal / highlighted).</summary>
    public static GameMenu Create(bool sideOne, Action<int> command) =>
        new([.. Rows.Select(r => Entry(r, sideOne))], sideOne ? new Vector2(370, 233) : new Vector2(420, 233), sideOne, command, Layout,
            sideOne ? ((ushort)10117, (ushort)10118) : ((ushort)10128, (ushort)10129), clickOpens: true);
}
