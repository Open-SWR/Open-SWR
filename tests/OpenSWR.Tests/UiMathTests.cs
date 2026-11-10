using OpenSWR.Data;

namespace OpenSWR.Tests;

public class UiMathTests
{
    [Theory]
    [InlineData(16, 200, 9, 5, 40)]
    [InlineData(16, 200, 9, 30, 31)]
    [InlineData(10, 60, 30, 3, 200)] // min length beats the proportional one
    public void ThumbRoundTripsAndStaysOnTrack(float top, float bottom, float min, int visible, int total)
    {
        int max = total - visible;
        foreach (int first in new[] { 0, max / 2, max })
        {
            var (y, len) = UiMath.Thumb(top, bottom, min, first, visible, total);
            Assert.Equal(first, UiMath.FirstAt(top, bottom, min, y, visible, total));
            Assert.InRange(y, top, bottom);
            Assert.InRange(y + len, top, bottom + 1e-3f);
        }
    }
}
