using OpenSWR.Data;

namespace OpenSWR.Tests;

/// <summary>Background darkening: c gives c / 6 + 50 per channel, then the nearest palette entry (squared distance, first strict minimum).</summary>
public class DarkeningTests
{
    [Fact]
    public void ChannelRange()
    {
        Assert.Equal(50, Darkening.Channel(0));
        Assert.Equal(50, Darkening.Channel(5));
        Assert.Equal(51, Darkening.Channel(6));
        Assert.Equal(92, Darkening.Channel(255));
    }

    [Fact]
    public void TableSnapsToNearestEntry()
    {
        byte[] palette =
        [
            0, 0, 0,        // 0 gives (50, 50, 50): entries 1 and 2 both at distance 100, the first wins
            40, 50, 50,     // 1 gives (56, 58, 58): nearest 3 (distance 24; 4 ties, not strict)
            60, 50, 50,     // 2 gives (60, 58, 58): nearest 3
            60, 60, 60,     // 3 c = 60 is its own darkened value: itself
            60, 60, 60,     // 4 the same colour: keeps its own index, not the earlier 3
        ];
        Assert.Equal([1, 3, 3, 3, 4], Darkening.Table(palette, 5)[..5]);
        Assert.Equal(3, Darkening.Nearest(palette, 5, 60, 60, 60)); // the first of the equal entries
    }

    [Fact]
    public void KeyBlueMapsNearDarkBlue()
    {
        byte[] palette = [0, 0, 255, 50, 50, 92, 92, 92, 92];
        Assert.Equal(1, Darkening.Table(palette, 3)[0]);
    }

    [Fact]
    public void LutMatchesTable()
    {
        var palette = new byte[256 * 3];
        var rng = new Random(7);
        rng.NextBytes(palette);
        var lut = Darkening.Lut(palette);
        var table = Darkening.Table(palette);
        for (int i = 0; i < 256; i++)
        {
            int r = palette[i * 3] / 6, g = palette[i * 3 + 1] / 6, b = palette[i * 3 + 2] / 6;
            int at = ((b * Darkening.Levels + g) * Darkening.Levels + r) * 3;
            Assert.Equal(palette.AsSpan(table[i] * 3, 3).ToArray(), lut.AsSpan(at, 3).ToArray());
        }
    }
}
