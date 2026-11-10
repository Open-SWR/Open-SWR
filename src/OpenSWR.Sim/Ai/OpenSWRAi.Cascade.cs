using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Cascade: incite runs at most <see cref="CascadeIncite"/> instead of
/// <see cref="MaxIncite"/> when the target lies in a sector where the side took a system from the enemy without its own troops in
/// control in the last <see cref="CascadeDays"/> days, or in a sector with another enemy system the views show in uprising.
/// Views and own data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int CascadeIncite = 3, CascadeDays = 100;

    /// <summary>Sector ref gives last day the side gained a system there by support. Saved.</summary>
    readonly SortedDictionary<uint, int> _supportGains = [];

    /// <summary>
    /// Systems newly own since the last decision that the side saw as the enemy's then: a gain by support when the military control
    /// (the system control's military part) is not the side (aimatch's cause rule: own troops in control = an assault). Inferred: neutral gains
    /// (diplomacy) do not count, the step reads "gained" as taken from the enemy.
    /// </summary>
    void RecordSupportGains(GameState game, IEnumerable<uint> gained)
    {
        foreach (uint r in gained)
            if (game.Systems.First(s => s.Ref == r) is var s && s.Control.Military != Side) _supportGains[s.Sector.Ref] = game.Day;
        foreach (var key in _supportGains.Where(e => game.Day - e.Value > CascadeDays).Select(e => e.Key).ToList()) _supportGains.Remove(key);
    }

    /// <summary>A cascade target: a system in a sector with a support gain in the last <see cref="CascadeDays"/> days or another enemy system in uprising (views).</summary>
    internal bool CascadeTarget(GameState game, GameObject target) =>
        target is StarSystem s && (_supportGains.TryGetValue(s.Sector.Ref, out int day) && game.Day - day <= CascadeDays
            || game.Systems.Any(x => x != s && x.Sector == s.Sector && Knowledge.See(game, x, Side) is { Owner: var o, Uprising: true, Destroyed: false } && o == Enemy));

    /// <summary>The running incite cap for a target: <see cref="MaxIncite"/>, under Cascade <see cref="CascadeIncite"/> at a cascade target.</summary>
    internal int InciteCap(GameState game, GameObject target) => On(V7.Cascade) && CascadeTarget(game, target) ? CascadeIncite : MaxIncite;

    /// <summary>Test hook: the support gain record.</summary>
    internal IReadOnlyDictionary<uint, int> SupportGains => _supportGains;

    void SaveCascade(JsonObject j)
    {
        if (_supportGains.Count > 0) j["supportGains"] = new JsonArray([.. _supportGains.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
    }

    void LoadCascade(JsonObject j)
    {
        if (j["supportGains"] is JsonArray a) foreach (var e in a) _supportGains[(uint)e![0]!] = (int)e[1]!;
    }
}
