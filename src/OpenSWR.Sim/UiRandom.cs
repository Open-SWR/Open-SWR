namespace OpenSWR.Sim;

/// <summary>
/// The UI generator: the computer player's and the advisor's only random
/// source, separate from the game generator. Same Lehmer step and rand(n) as <see cref="GameRandom"/>. State -1 until the first
/// rand(n) with n &gt; 1 seeds it (lazy): the seed routine's warm-up is discarded because the returned seed becomes the state, so
/// the first draw steps from the seed itself. Never reseeded: not at new game, not by setup, not by loading (a save writes the
/// state, a load discards it).
/// </summary>
public sealed class UiRandom(Func<int>? seed = null)
{
    /// <summary>The process-wide instance (constructed at process start in the original).</summary>
    public static UiRandom Shared { get; } = new();

    readonly Func<int> _seed = seed ?? CrtSeed;
    GameRandom? _generator;

    /// <summary>Current state; -1 = not yet seeded.</summary>
    public int State => _generator?.State ?? -1;

    /// <summary>When set, every rand(n) call is appended as (site, n, result), n <= 1 included.</summary>
    public List<(string Site, int N, int Result)>? Trace { get; set; }

    /// <summary>rand(n): n <= 1 gives 0 without stepping (and without seeding); else seeds on first use, then as <see cref="GameRandom.Rand"/>.</summary>
    public int Rand(int n, string site = "")
    {
        if (n > 1 && _generator is null)
        {
            _generator = new GameRandom(1);
            _generator.Restore(_seed(), 0); // the returned seed is the state (any value the routine yields, 0 included)
        }
        int result = n <= 1 ? 0 : _generator!.Rand(n);
        Trace?.Add((site, n, result));
        return result;
    }

    /// <summary>
    /// Seed routine: C runtime srand(time(0)), then s = r1*r2 + r3*r4 from four rand() calls, repeated while s = 0x7FFFFFFF.
    /// rand() is the MSVC CRT LCG (state * 214013 + 2531011) and returns its middle 15 bits.
    /// </summary>
    static int CrtSeed()
    {
        uint state = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int Rand() { state = state * 214013 + 2531011; return (int)(state >> 16 & 0x7FFF); }
        int s;
        do s = Rand() * Rand() + Rand() * Rand(); while (s == 0x7FFFFFFF);
        return s;
    }
}
