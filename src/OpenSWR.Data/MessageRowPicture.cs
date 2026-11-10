namespace OpenSWR.Data;

/// <summary>
/// The two pictures of a message-window row, built from the 8-bit bitmaps as the
/// original does, so the draw can key index 0 like the list's cell blit (list key = 0).
/// </summary>
public static class MessageRowPicture
{
    /// <summary>Colour key of a bitmap object: the first byte of the DIB's pixel data = the bottom-left pixel.</summary>
    public static int Key(IndexedBitmap b) => b.Pixels[(b.Height - 1) * b.Width];

    /// <summary>
    /// N (not selected): a new bitmap of the icon's size (all index 0) with the icon raw-copied at (1, 1), cut to (w - 1) x (h - 1).
    /// S (selected): a new 356 x 21 bitmap (all 0) with the bar keyed by its own key, then the icon raw-copied whole at (1, 1).
    /// Inferred: the result keeps the icon's palette (the bitmaps share the game palette).
    /// </summary>
    public static IndexedBitmap Build(IndexedBitmap icon, IndexedBitmap? bar)
    {
        int w = bar?.Width ?? icon.Width, h = bar?.Height ?? icon.Height;
        var pixels = new byte[w * h];
        if (bar is not null)
        {
            int key = Key(bar);
            for (int i = 0; i < pixels.Length; i++)
                if (bar.Pixels[i] != key) pixels[i] = bar.Pixels[i];
        }
        for (int y = 0; y < icon.Height && 1 + y < h; y++)
            for (int x = 0; x < icon.Width && 1 + x < w; x++)
                pixels[(1 + y) * w + 1 + x] = icon.Pixels[y * icon.Width + x];
        return new IndexedBitmap(w, h, pixels, icon.Palette);
    }
}
