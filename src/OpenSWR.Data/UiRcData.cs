using System.Buffers.Binary;

namespace OpenSWR.Data;

/// <summary>
/// UI RT_RCDATA tables of STRATEGY / COMMON / TACTICAL: popup-menu items, map legend panels, scroll-bar skins. All little-endian u16
/// words; every word is kept so entries round-trip.
/// </summary>
public static class UiRcData
{
    /// <summary>STRATEGY.DLL entry by id range:<see cref="MenuItem"/>, <see cref="LegendPanel"/> or <see cref="ScrollSkin"/>.</summary>
    public static object Decode(int id, ReadOnlySpan<byte> data) => id switch
    {
        (>= 32 and <= 41) or (>= 256 and <= 624) => MenuItem.Decode(data),
        (>= 10183 and <= 10201) or (>= 11600 and <= 11607) => LegendPanel.Decode(data),
        10650 or 10652 or 10653 or 10699 or 10700 or 10748 => ScrollSkin.Decode(data),
        _ => throw new InvalidDataException($"RCDATA {id} is not a known UI table."),
    };

    public static ushort[] Words(ReadOnlySpan<byte> data)
    {
        if (data.Length % 2 != 0) throw new InvalidDataException("UI table length must be even.");
        var w = new ushort[data.Length / 2];
        for (int i = 0; i < w.Length; i++) w[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(i * 2)..]);
        return w;
    }

    /// <summary>
    /// Menu builder (VA 4466064): per requested (id, enabled, checked) in the caller's order: no
    /// entry gives skipped; Parent != 0 gives the parent group row first (enabled, unchecked), this row into its submenu; a key already in
    /// the target list gives nothing added; else a row is inserted by ascending key. Alternate is 0 in every shipped entry: not built.
    /// </summary>
    public static List<MenuRow> BuildMenu(IEnumerable<(int Id, bool Enabled, bool Checked)> ids, Func<int, MenuItem?> entry)
    {
        var rows = new List<MenuRow>();
        MenuRow? Add(List<MenuRow> list, MenuItem item, bool enabled, bool check)
        {
            if (list.Find(r => r.Item.Key == item.Key) is { } existing) return existing.Item.Id == item.Id ? existing : null;
            var row = new MenuRow(item, enabled, check, []);
            int at = list.FindIndex(r => r.Item.Key > item.Key);
            list.Insert(at < 0 ? list.Count : at, row);
            return row;
        }
        foreach (var (id, enabled, check) in ids)
        {
            if (entry(id) is not { } item) continue;
            var target = rows;
            if (item.Parent != 0)
            {
                if (entry(item.Parent) is not { } parent || Add(rows, parent, true, false) is not { } group) continue;
                target = group.Children;
            }
            Add(target, item, enabled, check);
        }
        return rows;
    }

    internal static byte[] Bytes(IEnumerable<ushort> words)
    {
        var list = words.ToList();
        var b = new byte[list.Count * 2];
        for (int i = 0; i < list.Count; i++) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i * 2), list[i]);
        return b;
    }
}

/// <summary>
/// Popup-menu row: 13 words. <see cref="Extra"/> = words past 13 (id 384 has a 14th; the game reads only 13, so
/// its modules come out as text 0 / images 2).
/// </summary>
public sealed record MenuItem(ushort Id, ushort Parent, ushort Key, ushort Alternate, ushort Group, ushort Label, ushort Unk0C,
    ushort Image, ushort HotImage, ushort DisabledImage, ushort CheckImage, ushort TextModule, ushort ImageModule, ushort[] Extra)
{
    public static MenuItem Decode(ReadOnlySpan<byte> data)
    {
        var w = UiRcData.Words(data);
        if (w.Length < 13) throw new InvalidDataException("Menu item needs 13 words.");
        return new(w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7], w[8], w[9], w[10], w[11], w[12], w[13..]);
    }

    public byte[] ToBytes() => UiRcData.Bytes([Id, Parent, Key, Alternate, Group, Label, Unk0C, Image, HotImage, DisabledImage, CheckImage, TextModule, ImageModule, .. Extra]);
}

/// <summary>A built menu row (<see cref="UiRcData.BuildMenu"/>): the entry, enabled / checked, submenu rows (group rows only).</summary>
public sealed record MenuRow(MenuItem Item, bool Enabled, bool Checked, List<MenuRow> Children);

/// <summary>Legend-panel item.</summary>
public abstract record LegendItem(ushort Type, ushort X, ushort Y);
/// <summary>Type 1 or 2: bitmap at (x, y).</summary>
public sealed record LegendImage(ushort Type, ushort X, ushort Y, ushort Bitmap) : LegendItem(Type, X, Y);
/// <summary>Type 3: text at (x, y), font index, colour PALETTERGB(r, g, b); y = 1 = the centred title (x ignored).</summary>
public sealed record LegendText(ushort X, ushort Y, ushort String, ushort Font, ushort R, ushort G, ushort B) : LegendItem(3, X, Y);

/// <summary>
/// Map legend panel: Width, Height, ImageModule, TextModule, items until a 0 word. <see cref="Trailing"/> = words
/// after the terminator (none in shipped data).
/// </summary>
public sealed record LegendPanel(ushort Width, ushort Height, ushort ImageModule, ushort TextModule, List<LegendItem> Items, ushort[] Trailing)
{
    public static LegendPanel Decode(ReadOnlySpan<byte> data)
    {
        var w = UiRcData.Words(data);
        var items = new List<LegendItem>();
        int p = 4;
        for (; w[p] != 0; )
        {
            items.Add(w[p] switch
            {
                1 or 2 => new LegendImage(w[p], w[p + 1], w[p + 2], w[p + 3]),
                3 => new LegendText(w[p + 1], w[p + 2], w[p + 3], w[p + 4], w[p + 5], w[p + 6], w[p + 7]),
                var t => throw new InvalidDataException($"Unknown legend item type {t}."),
            });
            p += w[p] == 3 ? 8 : 4;
        }
        return new(w[0], w[1], w[2], w[3], items, w[(p + 1)..]);
    }

    public byte[] ToBytes()
    {
        var w = new List<ushort> { Width, Height, ImageModule, TextModule };
        foreach (var item in Items)
            w.AddRange(item switch
            {
                LegendImage i => [i.Type, i.X, i.Y, i.Bitmap],
                LegendText t => [3, t.X, t.Y, t.String, t.Font, t.R, t.G, t.B],
                _ => throw new InvalidOperationException(),
            });
        w.Add(0);
        w.AddRange(Trailing);
        return UiRcData.Bytes(w);
    }
}

/// <summary>
/// Scroll-bar skin, 10 words: thickness, track, up / pressed, down / pressed, three thumb bitmaps (Inferred: start
/// cap, middle, end cap), word 9 not read by the game.
/// </summary>
public sealed record ScrollSkin(ushort Thickness, ushort Track, ushort Up, ushort UpPressed, ushort Down, ushort DownPressed,
    ushort ThumbStart, ushort ThumbMiddle, ushort ThumbEnd, ushort Unk12)
{
    public static ScrollSkin Decode(ReadOnlySpan<byte> data)
    {
        var w = UiRcData.Words(data);
        if (w.Length != 10) throw new InvalidDataException("Scroll-bar skin has 10 words.");
        return new(w[0], w[1], w[2], w[3], w[4], w[5], w[6], w[7], w[8], w[9]);
    }

    /// <summary>Words 1-8: the part bitmaps.</summary>
    public ushort[] Bitmaps => [Track, Up, UpPressed, Down, DownPressed, ThumbStart, ThumbMiddle, ThumbEnd];

    public byte[] ToBytes() => UiRcData.Bytes([Thickness, Track, Up, UpPressed, Down, DownPressed, ThumbStart, ThumbMiddle, ThumbEnd, Unk12]);
}
