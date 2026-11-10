/// <summary>
/// TACTICAL.DLL model ids (resource type 301) of the capital-ship classes: 10 apart by type index, three levels of detail
/// each (id, id + 1, id + 2). Why the type indexes split into two blocks: meaning in the original is unknown.
/// </summary>
public static class TacticalModelIds
{
    // ---- Capital ships ----
    /// <summary>Type indexes 0 to 14.</summary>
    public const int CapitalShipBase = 2010;
    /// <summary>Type indexes 15 to 28.</summary>
    public const int CapitalShipSecondBase = 2510;
}

/// <summary>Type indexes (the SD StringId index of a tactical class) where the model and fighter tables split.</summary>
public static class TacticalTypeIndex
{
    /// <summary>Last type index of the first capital-ship model block.</summary>
    public const int LastFirstBlockShip = 14;
    /// <summary>First and last type index of the second capital-ship model block.</summary>
    public const int FirstSecondBlockShip = 15, LastSecondBlockShip = 28;
    /// <summary>Fighter classes: type indexes FirstFighter to LastFighter.</summary>
    public const int FirstFighter = 29, LastFighter = 36;

    /// <summary>Target type indexes whose turbolaser bolt takes style 2 (the shorter, 0.75 bolt) above the value threshold. Meaning in the original is unknown; the set is still open.</summary>
    static readonly int[] TurbolaserStyleTwoTypes = [11, 13, 14, 22, 25];

    /// <summary>True when a turbolaser bolt at a target of this type index is style 2.</summary>
    public static bool TurbolaserStyleTwo(int typeIndex) => System.Array.IndexOf(TurbolaserStyleTwoTypes, typeIndex) >= 0;
}
