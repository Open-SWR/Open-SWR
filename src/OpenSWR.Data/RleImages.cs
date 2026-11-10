using System.Buffers.Binary;

namespace OpenSWR.Data;

// Two RLE formats: TACTICAL 303 images and sprite DLL 302 delta frames.

/// <summary>TACTICAL.DLL resource type 303 image: u32 width, u32 height, then (u8 count, u8 palette index) runs, count 0 = 256.</summary>
public sealed record RleImage(int Width, int Height, byte[] Pixels)
{
    const int MaxPixels = 1 << 26; // a header can claim any size; the largest real image is far below this

    public static RleImage Decode(ReadOnlySpan<byte> data)
    {
        int w = BinaryPrimitives.ReadInt32LittleEndian(data), h = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
        if (w <= 0 || h <= 0 || (long)w * h > MaxPixels) throw new InvalidDataException($"RLE image size {w}x{h} is out of range.");
        var pixels = new byte[w * h];
        int p = 8, n = 0;
        while (n < pixels.Length && p + 1 < data.Length)
        {
            int count = data[p] == 0 ? 256 : data[p];
            if (n + count > pixels.Length) break;
            pixels.AsSpan(n, count).Fill(data[p + 1]);
            n += count;
            p += 2;
        }
        if (n != pixels.Length || p != data.Length)
            throw new InvalidDataException($"RLE runs cover {n} of {w}x{h} pixels and end at {p} of {data.Length} bytes.");
        return new RleImage(w, h, pixels);
    }

    /// <summary>Type 303 resources of exactly 768 bytes are 256 x RGB palettes.</summary>
    public static bool IsPalette(ReadOnlySpan<byte> data) => data.Length == 768;
}

/// <summary>One horizontal run of a sprite frame.</summary>
public readonly record struct SpriteRun(int X, int Y, byte[] Values);

/// <summary>
/// Sprite DLL (ALSPRITE / EMSPRITE) resource type 302: a delta frame. Values are added (mod 256) to the previous
/// frame's palette indices; the first frame of a sequence is an 8-bit RT_BITMAP of the same size.
/// </summary>
public sealed record DeltaSprite(int Width, int Height, byte Unk08, ushort[] Unk09, List<SpriteRun> Runs)
{
    public static DeltaSprite Decode(ReadOnlySpan<byte> data)
    {
        int w = BinaryPrimitives.ReadUInt16LittleEndian(data), h = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        int size = BinaryPrimitives.ReadInt32LittleEndian(data[4..]), rows = 17, pixels = rows + h * 4;
        if (pixels + size != data.Length) throw new InvalidDataException($"Sprite data size {size} does not match {data.Length} bytes.");

        var unk09 = new ushort[4];
        for (int i = 0; i < 4; i++) unk09[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(9 + i * 2)..]);

        // Per row: u32 offset into the pixel data; row data = skip, count, values[count], skip, ... up to the width.
        var runs = new List<SpriteRun>();
        for (int y = 0; y < h; y++)
        {
            int p = pixels + BinaryPrimitives.ReadInt32LittleEndian(data[(rows + y * 4)..]), x = 0;
            while (true)
            {
                x += data[p++];
                if (x >= w) break;
                int count = data[p++];
                runs.Add(new SpriteRun(x, y, data.Slice(p, count).ToArray()));
                x += count;
                p += count;
                if (x >= w) break;
            }
            if (x != w) throw new InvalidDataException($"Row {y} covers {x} of {w} pixels.");
        }
        return new DeltaSprite(w, h, data[8], unk09, runs);
    }

    /// <summary>Adds this delta to a frame of palette indices (row-major, Width x Height).</summary>
    public void ApplyTo(Span<byte> frame)
    {
        foreach (var run in Runs)
        {
            var row = frame.Slice(run.Y * Width + run.X, run.Values.Length);
            for (int i = 0; i < row.Length; i++) row[i] += run.Values[i];
        }
    }
}
