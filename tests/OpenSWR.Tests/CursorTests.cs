using OpenSWR.Data;

namespace OpenSWR.Tests;

/// <summary>REBEXE group cursors 1001 (arrow) and 1002 (targeting crosshair).</summary>
public class CursorTests
{
    static PeResources? _exe;
    static PeResources Exe => _exe ??= PeResources.Load(GameDir.File("REBEXE.EXE"));

    static int Opaque(CursorImage c) => Enumerable.Range(0, c.Width * c.Height).Count(i => c.Rgba[i * 4 + 3] == 255);

    [GameFact]
    public void ArrowGroup()
    {
        Assert.Equal([(3, 1)], Cursors.Entries(Exe.Find(Cursors.GroupType, 1001)!.Data.Span).Select(e => ((int)e.Image, e.Bpp)));
        Assert.Equal(308, Exe.Find(Cursors.ImageType, 3)!.Data.Length);
        var c = Cursors.Load(Exe, 1001)!;
        Assert.Equal((3, 32, 32, 1, 0, 0), ((int)c.Id, c.Width, c.Height, c.Bpp, c.HotX, c.HotY));
        // black / white opaque pixels only; the hotspot pixel (the arrow tip) is opaque
        for (int i = 0; i < 32 * 32; i++)
            if (c.Rgba[i * 4 + 3] == 255) Assert.True(c.Rgba[i * 4] is 0 or 255 && c.Rgba[i * 4] == c.Rgba[i * 4 + 1] && c.Rgba[i * 4] == c.Rgba[i * 4 + 2]);
        Assert.Equal(255, c.Rgba[3]);
        Assert.InRange(Opaque(c), 50, 600);
    }

    [GameFact]
    public void CrosshairGroup()
    {
        Assert.Equal([(4, 8), (5, 1)], Cursors.Entries(Exe.Find(Cursors.GroupType, 1002)!.Data.Span).Select(e => ((int)e.Image, e.Bpp)));
        Assert.Equal(2220, Exe.Find(Cursors.ImageType, 4)!.Data.Length);
        var c = Cursors.Load(Exe, 1002)!; // the 8-bpp image
        Assert.Equal((4, 32, 32, 8, 12, 12), ((int)c.Id, c.Width, c.Height, c.Bpp, c.HotX, c.HotY));
        for (int i = 0; i < 32 * 32; i++)
            if (c.Rgba[i * 4 + 3] == 255) Assert.Equal([255, 255, 255], c.Rgba[(i * 4)..(i * 4 + 3)]); // white only
            else Assert.Equal(0, c.Rgba[i * 4 + 3]);
        Assert.InRange(Opaque(c), 30, 400);
        var mono = Cursors.Decode(5, Exe.Find(Cursors.ImageType, 5)!.Data.Span);
        Assert.Equal((32, 32, 1, 0, 0), (mono.Width, mono.Height, mono.Bpp, mono.HotX, mono.HotY));
    }
}
