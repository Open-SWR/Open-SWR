namespace OpenSWR.Sim;

/// <summary>
/// Mood byte (<see cref="Advisor.Mood"/>), set at each day change; the strategy music pick reads it. Names are Inferred from the
/// conditions of <see cref="Advisor.MoodByte"/>; the meaning in the original is unknown.
/// </summary>
public enum AdvisorMood
{
    /// <summary>Not set (the default value).</summary>
    None,
    /// <summary>First counter 2 or more ahead.</summary>
    Ahead,
    /// <summary>First counter 1 ahead, or the share of 300 or more.</summary>
    SlightlyAhead,
    /// <summary>Share of 200 or more.</summary>
    Strong,
    /// <summary>Neutral: the start value and every other case.</summary>
    Neutral,
    /// <summary>Share of 50 or less.</summary>
    Weak,
    /// <summary>First counter 1 behind.</summary>
    SlightlyBehind,
    /// <summary>First counter 2 or more behind.</summary>
    Behind,
}
