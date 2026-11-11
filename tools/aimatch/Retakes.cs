using System.Globalization;
using System.Text;
using OpenSWR.Sim;

namespace OpenSWR.AiMatch;

/// <summary>
/// Retakes: for each system a side lost to troop assault, the days until it owned the system again (any cause), by the side the
/// OpenSWR AI played. Read from the owner changes; no effect on play.
/// </summary>
public static class Retakes
{
    static readonly (int Days, string Label)[] Bands = [(30, "0-30"), (100, "31-100"), (300, "101-300"), (int.MaxValue, ">300")];

    public static string Table(IReadOnlyList<GameResult> results)
    {
        var text = new StringBuilder();
        text.AppendLine("systems lost to troop assault and owned again (per game; days from the loss to the next own gain, any cause)");
        foreach (bool openswr in new[] { true, false })
        {
            var counts = new int[Bands.Length];
            int lost = 0, never = 0;
            foreach (var r in results)
            {
                var side = openswr ? r.OpenSWR : r.OpenSWR == Owner.Alliance ? Owner.Empire : Owner.Alliance;
                foreach (var loss in r.Changes.Where(c => c.From == side && c.Cause == ChangeCause.Troops))
                {
                    lost++;
                    var back = r.Changes.FirstOrDefault(c => c.System == loss.System && c.To == side && c.Day > loss.Day);
                    if (back is null) { never++; continue; }
                    counts[Array.FindIndex(Bands, b => back.Day - loss.Day <= b.Days)]++;
                }
            }
            double n = Math.Max(1, results.Count);
            string bands = string.Join(", ", Bands.Select((b, i) => string.Create(CultureInfo.InvariantCulture, $"{b.Label} days {counts[i] / n:F2}")));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {(openswr ? "openswr" : "original"),-8} lost {lost / n:F2}: {bands}, never {never / n:F2}"));
        }
        return text.ToString();
    }
}
