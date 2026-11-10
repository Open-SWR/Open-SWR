namespace OpenSWR.Data;

/// <summary>
/// Window background darkening as the original does it. Each palette
/// entry is darkened per channel to c / 6 + 50, then mapped to the nearest palette entry by squared RGB distance. Used under the
/// sector window, the legend window and the popup menus.
/// </summary>
public static class Darkening
{
    /// <summary>Distinct darkened channel values: 50 ... 92.</summary>
    public const int Levels = 43;

    public static int Channel(int c) => c / 6 + 50;

    /// <summary>Nearest of the first <paramref name="used"/> entries (RGB triples) to (r, g, b): first strict minimum, stops at distance 0.</summary>
    public static int Nearest(ReadOnlySpan<byte> palette, int used, int r, int g, int b)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < used; i++)
        {
            int dr = palette[i * 3] - r, dg = palette[i * 3 + 1] - g, db = palette[i * 3 + 2] - b;
            int d = dr * dr + dg * dg + db * db;
            if (d >= bestD) continue;
            (best, bestD) = (i, d);
            if (d == 0) break;
        }
        return best;
    }

    /// <summary>Remap table: palette index -> index of its darkened colour; an entry that already is its darkened colour keeps its index.</summary>
    public static byte[] Table(ReadOnlySpan<byte> palette, int used = 256)
    {
        var table = new byte[256];
        for (int i = 0; i < used; i++)
        {
            int r = Channel(palette[i * 3]), g = Channel(palette[i * 3 + 1]), b = Channel(palette[i * 3 + 2]);
            table[i] = r == palette[i * 3] && g == palette[i * 3 + 1] && b == palette[i * 3 + 2] ? (byte)i : (byte)Nearest(palette, used, r, g, b);
        }
        return table;
    }

    /// <summary>
    /// The remap for any RGB input (true-colour screens): RGB8 image <see cref="Levels"/>^2 x <see cref="Levels"/>, texel
    /// (r / 6 + 43 * (g / 6), b / 6) = the palette colour nearest to the darkened (r, g, b).
    /// </summary>
    public static byte[] Lut(ReadOnlySpan<byte> palette, int used = 256)
    {
        var lut = new byte[Levels * Levels * Levels * 3];
        for (int b = 0; b < Levels; b++)
            for (int g = 0; g < Levels; g++)
                for (int r = 0; r < Levels; r++)
                {
                    int n = Nearest(palette, used, r + 50, g + 50, b + 50), at = ((b * Levels + g) * Levels + r) * 3;
                    lut[at] = palette[n * 3];
                    lut[at + 1] = palette[n * 3 + 1];
                    lut[at + 2] = palette[n * 3 + 2];
                }
        return lut;
    }
}
