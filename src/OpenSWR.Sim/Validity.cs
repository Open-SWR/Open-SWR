namespace OpenSWR.Sim;

/// <summary>Request validities in ticks; a cell of 0 means no request on that side.</summary>
public static class Validity
{
    public const int None = 0, Brief = 5, Normal = 10, Medium = 50, Long = 40000;
}
