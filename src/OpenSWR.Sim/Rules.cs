using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>Integer helpers used by the rules.</summary>
public static class GameMath
{
    /// <summary>trunc(n * p / 100)</summary>
    public static int Pct(int n, int p) => n * p / 100;

    public static int Clamp(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

    /// <summary>(a + b - 1) div b</summary>
    public static int CeilDiv(int a, int b) => (a + b - 1) / b;
}

/// <summary>GNPRTB / SDPRTB parameters read with the settings column.</summary>
public sealed class Params(GameDefinitions defs, int column)
{
    readonly Dictionary<uint, ParamTableEntry> _general = defs.GeneralParams.GroupBy(p => p.ParamId).ToDictionary(g => g.Key, g => g.First());
    readonly Dictionary<uint, ParamTableEntry> _side = defs.SideParams.GroupBy(p => p.ParamId).ToDictionary(g => g.Key, g => g.First());

    public int Column => column;

    /// <summary>G id: Values[col], 0 when the id is missing or col is outside 0-7.</summary>
    public int General(uint id) => column is >= 0 and <= 7 && _general.TryGetValue(id, out var p) ? p.Values[column] : 0;

    /// <summary>S id[side]: Values[2*col + side - 1], 0 when missing.</summary>
    public int Side(uint id, Owner side) =>
        side is Owner.Alliance or Owner.Empire && _side.TryGetValue(id, out var p) ? p.Values[2 * column + (int)side - 1] : 0;
}

/// <summary>TB threshold lookup.</summary>
public static class TableLookup
{
    /// <summary>The entry before the first one with Key &gt; x (clamped to the first / last entry); null for an empty table.</summary>
    public static T? Threshold<T>(IReadOnlyList<T> entries, Func<T, int> key, int x) where T : class
    {
        if (entries.Count == 0) return null;
        for (int i = 0; i < entries.Count; i++)
            if (key(entries[i]) > x) return entries[Math.Max(0, i - 1)];
        return entries[^1];
    }

    public static TableEntry? Threshold(IReadOnlyList<TableEntry> entries, int x)
    {
        if (entries.Count == 0) return null;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Key > x) return entries[Math.Max(0, i - 1)];
        return entries[^1];
    }
}
