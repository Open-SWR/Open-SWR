using OpenSWR.Data;

namespace OpenSWR.Tests;

public class RleImageTests
{
    [GameFact]
    public void TacticalImagesDecode()
    {
        var entries = PeResources.Load(GameDir.File("TACTICAL.DLL")).OfType(new ResId(303)).ToList();
        Assert.Equal(227, entries.Count(e => RleImage.IsPalette(e.Data.Span)));

        var failed = new List<ResId>();
        foreach (var e in entries.Where(e => !RleImage.IsPalette(e.Data.Span)))
        {
            try { RleImage.Decode(e.Data.Span); }
            catch (InvalidDataException) { failed.Add(e.Name); }
        }
        Assert.Equal(652, entries.Count - 227 - failed.Count);
        // 2x2 entries that are not RLE images
        Assert.Equal([4200, 4201, 4202, 4203, 4204], failed.Select(n => (int)n.Id).Order());
    }

    [GameTheory]
    [InlineData("ALSPRITE.DLL", 1640)]
    [InlineData("EMSPRITE.DLL", 2348)]
    public void SpriteDeltasDecode(string dll, int count)
    {
        var sprites = PeResources.Load(GameDir.File(dll)).OfType(new ResId(302)).ToList();
        Assert.Equal(count, sprites.Count);
        Assert.All(sprites, s => DeltaSprite.Decode(s.Data.Span));
    }

    [GameFact]
    public void DeltaChainMatchesReference()
    {
        var pe = PeResources.Load(GameDir.File("ALSPRITE.DLL"));
        var bitmap = IndexedBitmap.FromDib(pe.Find(ResId.Bitmap, 2001)!.Data.Span);
        Assert.Equal((67, 116), (bitmap.Width, bitmap.Height));
        var frame = bitmap.Pixels;
        for (ushort id = 2002; id <= 2007; id++)
            DeltaSprite.Decode(pe.Find(new ResId(302), id)!.Data.Span).ApplyTo(frame);
        // C-3PO raising an arm; reference from an independent implementation, checked visually
        Assert.Equal("223831342680543915035766949218346307024", Digest.Md5(frame));
    }
}
