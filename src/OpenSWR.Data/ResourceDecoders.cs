using System.Buffers.Binary;
using System.Text;

namespace OpenSWR.Data;

public static class Dib
{
    /// <summary>RT_BITMAP resources are DIBs without the 14-byte BITMAPFILEHEADER; prepend it to get a .bmp file.</summary>
    public static byte[] ToBmp(ReadOnlySpan<byte> dib)
    {
        uint headerSize = U32(dib, 0);
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        uint compression = U32(dib, 16);
        uint colorsUsed = headerSize >= 36 ? U32(dib, 32) : 0;
        uint paletteBytes = (colorsUsed != 0 ? colorsUsed : bpp <= 8 ? 1u << bpp : 0) * 4
            + (compression == 3 && headerSize == 40 ? 12u : 0); // BI_BITFIELDS masks follow a v1 header

        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2), (uint)bmp.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10), 14 + headerSize + paletteBytes);
        dib.CopyTo(bmp.AsSpan(14));
        return bmp;
    }

    static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b[at..]);
}

public static class StringTable
{
    /// <summary>
    /// Decodes one RT_STRING block: 16 entries of (u16 length, UTF-16 chars). Block n holds string ids (n-1)*16 .. (n-1)*16+15.
    /// Empty entries are omitted.
    /// </summary>
    public static List<(int Id, string Text)> Decode(int blockId, ReadOnlySpan<byte> block)
    {
        var result = new List<(int, string)>();
        int p = 0;
        for (int i = 0; i < 16; i++)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(block[p..]) * 2;
            p += 2;
            if (length > 0) result.Add(((blockId - 1) * 16 + i, Encoding.Unicode.GetString(block.Slice(p, length))));
            p += length;
        }
        return result;
    }

    /// <summary>Every RT_STRING text of <paramref name="module"/> by string id (the first block wins; the install's modules repeat no id).</summary>
    public static Dictionary<int, string> All(PeResources module)
    {
        var table = new Dictionary<int, string>();
        foreach (var block in module.OfType(ResId.String))
            foreach (var (id, text) in Decode(block.Name.Id, block.Data.Span))
                table.TryAdd(id, text);
        return table;
    }
}

/// <summary>Part of a message template: literal text or a placeholder.</summary>
public abstract record MessagePart;
public sealed record MessageLiteral(string Text) : MessagePart;
/// <summary>"|" + u8 slot + u32 kind. Inferred: slot = argument number (1-4), kind = how the argument is formatted.</summary>
public sealed record MessagePlaceholder(byte Slot, uint Kind) : MessagePart;

/// <summary>TEXTSTRA.DLL RT_RCDATA message templates: Windows-1252 text with placeholders, ending in 0x01.</summary>
public static class MessageText
{
    static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;

    public static bool IsMessage(ReadOnlySpan<byte> data) => data.Length > 0 && data[^1] == 1;

    public static List<MessagePart> Decode(ReadOnlySpan<byte> data)
    {
        if (!IsMessage(data)) throw new InvalidDataException("Message must end with 0x01.");
        var parts = new List<MessagePart>();
        int start = 0, end = data.Length - 1;
        for (int p = 0; p < end;)
        {
            if (data[p] != '|') { p++; continue; }
            if (p > start) parts.Add(new MessageLiteral(Ansi.GetString(data[start..p])));
            parts.Add(new MessagePlaceholder(data[p + 1], BinaryPrimitives.ReadUInt32LittleEndian(data[(p + 2)..])));
            start = p += 6;
        }
        if (end > start) parts.Add(new MessageLiteral(Ansi.GetString(data[start..end])));
        return parts;
    }
}

/// <summary>Advice topic n of the side's TEXTSTRA topic list: its kind and its sort key.</summary>
public readonly record struct AdviceTopic(int N, int Kind, int Key)
{
    /// <summary>
    /// The topic list of TEXTSTRA RCDATA 0x6000 (Alliance) / 0x6800 (Empire): the base entry holds a u16 count; topic n's header
    /// is RCDATA base + 3n, its kind in the low nibble of byte 0 and a u16 key at byte 2. A missing or short header is skipped.
    /// </summary>
    public static List<AdviceTopic> Read(PeResources textstra, bool alliance)
    {
        ushort b = (ushort)(alliance ? 0x6000 : 0x6800);
        var topics = new List<AdviceTopic>();
        if (textstra.Find(ResId.RcData, b) is not { Data.Length: >= 2 } head) return topics;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(head.Data.Span);
        for (int n = 1; n <= count; n++)
            if (b + 3 * n <= 0xFFFF && textstra.Find(ResId.RcData, (ushort)(b + 3 * n)) is { Data.Length: >= 4 } h)
                topics.Add(new(n, h.Data.Span[0] & 0xF, BinaryPrimitives.ReadUInt16LittleEndian(h.Data.Span[2..])));
        return topics;
    }
}

/// <summary>Standard RT_ACCELERATOR table: 8-byte entries (u16 flags, u16 key, u16 command, u16 padding); flag 0x80 marks the last.</summary>
public readonly record struct Accelerator(ushort Flags, ushort Key, ushort Command)
{
    public static List<Accelerator> Decode(ReadOnlySpan<byte> data)
    {
        var list = new List<Accelerator>();
        for (int p = 0; p + 8 <= data.Length; p += 8)
        {
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(data[p..]);
            list.Add(new Accelerator((ushort)(flags & 0x7F), BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 2)..]), BinaryPrimitives.ReadUInt16LittleEndian(data[(p + 4)..])));
            if ((flags & 0x80) != 0) break;
        }
        return list;
    }

    /// <summary>
    /// TranslateAccelerator match (Inferred: Windows behaviour): Ctrl (FCONTROL 8) and Alt (FALT 0x10) must
    /// equal the entry's flags, so Ctrl+Alt (AltGr) matches no Alt entry; a virtual-key entry (FVIRTKEY 1) also needs the same Shift
    /// (FSHIFT 4) and <paramref name="vk"/> = Key; a character entry matches the typed character <paramref name="c"/> (Shift is in it).
    /// </summary>
    public bool Matches(ushort vk, char c, bool shift, bool ctrl, bool alt) =>
        ((Flags & 8) != 0) == ctrl && ((Flags & 0x10) != 0) == alt
        && ((Flags & 1) != 0 ? Key == vk && ((Flags & 4) != 0) == shift : c != '\0' && Key == c);

    /// <summary>The first entry of <paramref name="table"/> that matches (<see cref="Matches"/>), or null.</summary>
    public static Accelerator? Find(IEnumerable<Accelerator> table, ushort vk, char c, bool shift, bool ctrl, bool alt)
    {
        foreach (var a in table) if (a.Matches(vk, c, shift, ctrl, alt)) return a;
        return null;
    }
}

/// <summary>8-bit RT_BITMAP as palette indices.</summary>
public sealed record IndexedBitmap(int Width, int Height, byte[] Pixels, byte[] Palette)
{
    /// <summary>Bottom-up 8-bit DIB -> top-down row-major indices; Palette = 256 x RGB.</summary>
    public static IndexedBitmap FromDib(ReadOnlySpan<byte> dib)
    {
        int header = BinaryPrimitives.ReadInt32LittleEndian(dib), bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        if (bpp != 8) throw new InvalidDataException($"Expected an 8-bit DIB, got {bpp} bpp.");
        int w = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]), h = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        int colors = BinaryPrimitives.ReadInt32LittleEndian(dib[32..]) is var n && n != 0 ? n : 256;
        var palette = new byte[768];
        for (int i = 0; i < colors; i++)
        {
            int p = header + i * 4; // RGBQUAD = B, G, R, reserved
            palette[i * 3] = dib[p + 2];
            palette[i * 3 + 1] = dib[p + 1];
            palette[i * 3 + 2] = dib[p];
        }
        int stride = (w + 3) & ~3, bits = header + colors * 4;
        var pixels = new byte[w * h];
        for (int y = 0; y < h; y++) dib.Slice(bits + (h - 1 - y) * stride, w).CopyTo(pixels.AsSpan(y * w));
        return new IndexedBitmap(w, h, pixels, palette);
    }

    /// <summary>Colour key (VA 0x5fd170): the bottom-left pixel's index (the first byte of the bottom-up DIB's pixel data).</summary>
    public int Key => Pixels[(Height - 1) * Width];

    /// <summary>
    /// Object picture composition (VA 0x42c3b0): a <paramref name="width"/> x <paramref name="height"/> bitmap
    /// filled with index <paramref name="fill"/> (the base's key; key := fill), then each layer at (0, 0), clipped: opaque layers (the
    /// frame, VA 0x5fcc30) copied whole, after which the key := the result's bottom-left index; the others blitted keyed on their own
    /// key (VA 0x5fd0f0), never changing the result's key. A finished pixel is transparent iff its index equals the returned key.
    /// Palette = <paramref name="palette"/> (every object-picture bitmap of the install carries the same palette, checked).
    /// </summary>
    public static (IndexedBitmap Picture, int Key) Compose(int width, int height, int fill, byte[] palette, IEnumerable<(IndexedBitmap Bitmap, bool Opaque)> layers)
    {
        var d = new byte[width * height];
        Array.Fill(d, (byte)fill);
        int key = fill;
        foreach (var (b, opaque) in layers)
        {
            int k = b.Key;
            for (int y = 0; y < Math.Min(height, b.Height); y++)
                for (int x = 0; x < Math.Min(width, b.Width); x++)
                {
                    byte c = b.Pixels[y * b.Width + x];
                    if (opaque || c != k) d[y * width + x] = c;
                }
            if (opaque) key = d[(height - 1) * width];
        }
        return (new IndexedBitmap(width, height, d, palette), key);
    }

    /// <summary>RGBA8 with index <paramref name="transparent"/> fully transparent.</summary>
    public static byte[] ToRgba(ReadOnlySpan<byte> pixels, ReadOnlySpan<byte> palette, int transparent)
    {
        var rgba = new byte[pixels.Length * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            int c = pixels[i];
            palette.Slice(c * 3, 3).CopyTo(rgba.AsSpan(i * 4));
            rgba[i * 4 + 3] = c == transparent ? (byte)0 : (byte)255;
        }
        return rgba;
    }
}
