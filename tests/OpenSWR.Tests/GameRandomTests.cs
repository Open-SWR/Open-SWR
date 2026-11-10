using OpenSWR.Sim;

namespace OpenSWR.Tests;

// Expected values come from an independent implementation of the Lehmer step and the rand / roll formulas.
public class GameRandomTests
{
    [Fact]
    public void StateFollowsLehmerSequenceFromSeed()
    {
        var rng = new GameRandom(1);
        rng.Rand(2); rng.Rand(2); rng.Rand(2);
        Assert.Equal(1945238885, rng.State); // 41358, 1710484164, 1945238885
        Assert.Equal(3, rng.Draws);
    }

    [Fact]
    public void RollsMatchReference()
    {
        var rng = new GameRandom(20261013);
        Assert.Equal([20, 64, 30, 27, 20, 79, 58, 54], Enumerable.Range(0, 8).Select(_ => rng.Percent()));
        Assert.Equal([-5, -5, -1], Enumerable.Range(0, 3).Select(_ => rng.Roll(-5)));
    }

    [Fact]
    public void ZeroRangeDrawsNothing()
    {
        var rng = new GameRandom(7);
        Assert.Equal(0, rng.Roll(0));
        Assert.Equal(0, rng.Rand(1));
        Assert.Equal(0, rng.Draws);
        Assert.Equal(7, rng.State);
    }

    [Fact]
    public void RandCanReturnN()
    {
        // x = 0x7FFFFFFE: floor(0x7FFFFFFE / floor(0x7FFFFFFE / 101)) = 101, so rand(n) can return n itself.
        // Seed s with 41358*s == 0x7FFFFFFE, i.e. s = m - inverse(41358).
        long inverse = BigIntegerModInverse(41358, 0x7FFFFFFF);
        var rng = new GameRandom((int)(0x7FFFFFFF - inverse));
        Assert.Equal(101, rng.Rand(101));
    }

    static long BigIntegerModInverse(long a, long m) => (long)System.Numerics.BigInteger.ModPow(a, m - 2, m);

    [Theory]
    [InlineData(0)]
    [InlineData(0x7FFFFFFF)]
    public void SeedOutsideRangeThrows(int seed) => Assert.Throws<ArgumentOutOfRangeException>(() => new GameRandom(seed));
}
