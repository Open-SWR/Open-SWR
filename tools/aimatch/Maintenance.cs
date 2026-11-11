using System.Globalization;
using System.Text;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.AiMatch;

/// <summary>
/// Maintenance probe, per side: days with demand over capacity, units destroyed by maintenance scrap (by production category), and
/// each day-end capacity drop with its likely cause: an owner change the side lost that day (its cause), else a new own uprising,
/// else a new blockade of an own system, else other (a scrapped or destroyed mine or refinery, an arrival still on its way).
/// An observer: reads the state, draws nothing.
/// </summary>
public sealed class MaintenanceProbe
{
    public Dictionary<Owner, int> DaysOver { get; } = new() { [Owner.Alliance] = 0, [Owner.Empire] = 0 };
    public Dictionary<(Owner Side, ProductCategory Category), int> Scrapped { get; } = [];
    public Dictionary<(Owner Side, string Cause), (int Drops, int Lost)> Drops { get; } = [];

    readonly HashSet<GameObject> _units = [];
    readonly HashSet<GameObject> _counted = [];
    readonly Dictionary<Owner, int> _capacity = [];
    HashSet<StarSystem> _uprising = [], _blockade = [];
    int _changes;

    public MaintenanceProbe(GameState game)
    {
        foreach (var o in game.Systems.SelectMany(Descendants)) _units.Add(o);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire }) _capacity[side] = game.Sides[side].MaintenanceCapacity;
        (_uprising, _blockade) = (Flagged(game, s => s.Uprising), Flagged(game, s => s.Blockade));
    }

    /// <summary>Called once per finished day with every owner change so far.</summary>
    public void Day(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        var lost = new Dictionary<Owner, ChangeCause>();
        for (; _changes < changes.Count; _changes++)
            if (changes[_changes].From is Owner.Alliance or Owner.Empire) lost.TryAdd(changes[_changes].From, changes[_changes].Cause);
        var uprising = Flagged(game, s => s.Uprising);
        var blockade = Flagged(game, s => s.Blockade);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var state = game.Sides[side];
            if (state.Demand > state.MaintenanceCapacity) DaysOver[side]++;
            int drop = _capacity[side] - state.MaintenanceCapacity;
            if (drop > 0)
            {
                string cause = lost.TryGetValue(side, out var c) ? c.ToString()
                    : uprising.Any(s => s.Owner == side && !_uprising.Contains(s)) ? "new uprising"
                    : blockade.Any(s => s.Owner == side && !_blockade.Contains(s)) ? "new blockade" : "other";
                var (n, sum) = Drops.GetValueOrDefault((side, cause));
                Drops[(side, cause)] = (n + 1, sum + drop);
            }
            _capacity[side] = state.MaintenanceCapacity;
        }
        (_uprising, _blockade) = (uprising, blockade);
        foreach (var o in game.Created) _units.Add(o);
        foreach (var o in _units.Where(o => o.Destroyed && o.DestroyedReason == DestroyedReason.MaintenanceScrap && _counted.Add(o)))
            if (Match.Category(o.Type) is { } category) Scrapped[(o.Owner, category)] = Scrapped.GetValueOrDefault((o.Owner, category)) + 1;
    }

    static HashSet<StarSystem> Flagged(GameState game, Func<StarSystem, bool> flag) => [.. game.Systems.Where(flag)];

    static IEnumerable<GameObject> Descendants(GameObject o) => o.Contents.SelectMany(c => Descendants(c).Prepend(c));

    /// <summary>Per game averages for the openswr side and the original side.</summary>
    public static string Table(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("maintenance (averages per game): days over capacity, units lost to maintenance scrap, capacity drops by cause");
        foreach (bool openswr in new[] { true, false })
        {
            var probes = results.Where(r => r.Maintenance is not null).Select(r => (Side: openswr ? r.OpenSWR : Other(r.OpenSWR), Probe: r.Maintenance!)).ToList();
            if (probes.Count == 0) continue;
            double Avg(Func<(Owner Side, MaintenanceProbe Probe), double> f) => probes.Average(f);
            string who = openswr ? "openswr" : "original";
            var scraps = Enum.GetValues<ProductCategory>().Select(c => (c, n: Avg(p => p.Probe.Scrapped.GetValueOrDefault((p.Side, c))))).Where(x => x.n > 0);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {who,-8} days over {Avg(p => p.Probe.DaysOver[p.Side]):F1}; scrapped {string.Join(", ", scraps.Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.c} {x.n:F2}")))}"));
            var causes = probes.SelectMany(p => p.Probe.Drops.Where(d => d.Key.Side == p.Side).Select(d => d.Key.Cause)).Distinct().Order();
            foreach (var cause in causes)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {who,-8} drops by {cause}: {Avg(p => p.Probe.Drops.GetValueOrDefault((p.Side, cause)).Drops):F1} drops, {Avg(p => p.Probe.Drops.GetValueOrDefault((p.Side, cause)).Lost):F0} capacity"));
        }
        return text.ToString();
    }

    static Owner Other(Owner side) => side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
}
