using System.Buffers.Binary;

namespace OpenSWR.Data;

/// <summary>A decoded cursor image: RGBA8 rows top-down, hotspot in image pixels.</summary>
public sealed record CursorImage(ushort Id, int Width, int Height, int Bpp, int HotX, int HotY, byte[] Rgba);

/// <summary>RT_GROUP_CURSOR (12) / RT_CURSOR (1) resources.</summary>
public static class Cursors
{
    public static readonly ResId GroupType = new(12), ImageType = new(1);

    /// <summary>The image ids and bit depths a group lists, in resource order.</summary>
    public static List<(ushort Image, int Bpp)> Entries(ReadOnlySpan<byte> group)
    {
        if (BinaryPrimitives.ReadUInt16LittleEndian(group[2..]) != 2) throw new InvalidDataException("Not a cursor group.");
        int n = BinaryPrimitives.ReadUInt16LittleEndian(group[4..]);
        var list = new List<(ushort, int)>();
        for (int k = 0; k < n; k++)
        {
            var e = group.Slice(6 + 14 * k, 14);
            list.Add((BinaryPrimitives.ReadUInt16LittleEndian(e[12..]), BinaryPrimitives.ReadUInt16LittleEndian(e[6..])));
        }
        return list;
    }

    /// <summary>A group's image with the most bits per pixel (Inferred: Windows' choice on an 8-bit or deeper display), or null when missing.</summary>
    public static CursorImage? Load(PeResources module, ushort group)
    {
        if (module.Find(GroupType, group) is not { } g) return null;
        var entries = Entries(g.Data.Span);
        if (entries.Count == 0) return null;
        var best = entries.MaxBy(e => e.Bpp);
        return module.Find(ImageType, best.Image) is { } image ? Decode(best.Image, image.Data.Span) : null;
    }

    /// <summary>
    /// Cursor image: hotspot, BITMAPINFOHEADER (height doubled), colour table, bottom-up XOR plane (1 / 8 bpp), bottom-up 1-bpp AND
    /// plane. AND 0 gives opaque colour; AND 1 with black gives transparent; AND 1 with another colour (screen inversion) gives opaque black (Inferred).
    /// </summary>
    public static CursorImage Decode(ushort id, ReadOnlySpan<byte> data)
    {
        int hotX = BinaryPrimitives.ReadUInt16LittleEndian(data), hotY = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        var h = data[4..];
        int w = BinaryPrimitives.ReadInt32LittleEndian(h[4..]), height = BinaryPrimitives.ReadInt32LittleEndian(h[8..]) / 2;
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(h[14..]);
        if (bpp is not (1 or 8)) throw new InvalidDataException($"Cursor {id}: {bpp} bpp not supported.");
        int colours = BinaryPrimitives.ReadInt32LittleEndian(h[32..]) is var c && c != 0 ? c : 1 << bpp;
        var table = h.Slice(40, 4 * colours);
        int xs = (w * bpp + 31) / 32 * 4, ands = (w + 31) / 32 * 4;
        var xor = h.Slice(40 + 4 * colours, xs * height);
        var and = h.Slice(40 + 4 * colours + xs * height, ands * height);
        var rgba = new byte[w * height * 4];
        for (int y = 0; y < height; y++)
        {
            int r = height - 1 - y;
            for (int x = 0; x < w; x++)
            {
                int index = bpp == 8 ? xor[r * xs + x] : (xor[r * xs + x / 8] >> (7 - x % 8)) & 1;
                bool mask = ((and[r * ands + x / 8] >> (7 - x % 8)) & 1) != 0;
                byte b = table[index * 4], g = table[index * 4 + 1], red = table[index * 4 + 2];
                int o = (y * w + x) * 4;
                if (!mask) (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]) = (red, g, b, 255);
                else if (red != 0 || g != 0 || b != 0) rgba[o + 3] = 255; // inverted pixel: opaque black (Inferred; none in the game's cursors)
            }
        }
        return new CursorImage(id, w, height, bpp, hotX, hotY, rgba);
    }
}
