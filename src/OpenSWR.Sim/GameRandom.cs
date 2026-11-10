namespace OpenSWR.Sim;

/// <summary>
/// The game's random generator: Lehmer / Park-Miller, x' = 41358 * x mod (2^31 - 1).
/// </summary>
public sealed class GameRandom
{
    const long Modulus = 0x7FFFFFFF, Multiplier = 41358;

    /// <summary>Current generator state; a new generator starts with state = seed (no warm-up).</summary>
    public int State { get; private set; }

    /// <summary>Number of generator steps taken (for replay checks).</summary>
    public long Draws { get; private set; }

    /// <summary>When set, every rand(n) call is appended as (site, n, result), including n <= 1 calls that do not step (for differential tests).</summary>
    public List<(string Site, int N, int Result)>? Trace { get; set; }

    public GameRandom(int seed)
    {
        if (seed < 1 || seed > 0x7FFFFFFE) throw new ArgumentOutOfRangeException(nameof(seed), "Seed must be in [1, 0x7FFFFFFE].");
        State = seed;
    }

    internal void Restore(int state, long draws) => (State, Draws) = (state, draws);

    int Step()
    {
        State = (int)(Multiplier * State % Modulus);
        Draws++;
        return State;
    }

    /// <summary>n <= 1 gives 0 without stepping; else floor(x / floor(0x7FFFFFFE / n)), which can rarely return n itself.</summary>
    public int Rand(int n, string site = "")
    {
        int result = n <= 1 ? 0 : Step() / (0x7FFFFFFE / n);
        Trace?.Add((site, n, result));
        return result;
    }

    /// <summary>k >= 0 gives 0..k (rarely k + 1); k &lt; 0 gives -rand(1 - k). roll(0) draws nothing.</summary>
    public int Roll(int k, string site = "") => k >= 0 ? Rand(k + 1, site) : -Rand(1 - k, site);

    public int Between(int lo, int hi, string site = "") => lo + Roll(hi - lo, site);

    /// <summary>roll(99): 0..99 (rarely 100).</summary>
    public int Percent(string site = "") => Roll(99, site);

    public bool Chance(int percent, string site = "") => Percent(site) < percent;
}
