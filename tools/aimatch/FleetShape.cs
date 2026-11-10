using System.Globalization;
using System.Text;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>
/// Measurement of fleet concentration and assault troop carrying: at days 200, 400,
/// 600, 800 and 1000, per side, the fleets with capital ships, their line-ship strength (capital ships' attack + shield + hull from
/// the definitions; Inferred: the openswr controller's unit value), troops aboard, and what each fleet is doing. An observer: reads
/// the real state, draws nothing, writes nothing.
/// </summary>
public sealed class FleetShapeRecorder
{
    public static readonly int[] Days = [200, 400, 600, 800, 1000];
    static readonly string[] Doings = ["defence", "staging", "attack", "moving", "idle at base"];

    public sealed record Shape(int Day, Owner Side, int Fleets, int Largest, int Second, int Median, double Top2Share, int TroopsAboard, Dictionary<string, int> Doing);
    public List<Shape> Shapes { get; } = [];

    public void Day(GameState game)
    {
        if (!Days.Contains(game.Day)) return;
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var ai = game.Controllers.GetValueOrDefault(side) as OpenSWRController;
            int Line(Fleet f) => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).Sum(x =>
                game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type) is { } c ? (int)(c.TotalAttackStrength + c.ShieldStrength + c.Hull) : 0);
            var fleets = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == side && f.Existing && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing)).ToList();
            var strengths = fleets.Select(Line).OrderDescending().ToList();
            int total = strengths.Sum();
            var doing = Doings.ToDictionary(d => d, _ => 0);
            var staging = ai?.StagingSystems.ToHashSet() ?? [];
            var targets = ai?.AttackTargets.ToHashSet() ?? [];
            foreach (var f in fleets)
            {
                var at = f.Container as StarSystem;
                string d = at is not null && targets.Contains(at) ? "attack"
                    : f.Enroute ? "moving"
                    : at is null ? "moving"
                    : at.Owner == side && at.Contents.OfType<Fleet>().Any(e => e.Owner != side && e.Owner is Owner.Alliance or Owner.Empire && e.Existing) ? "defence"
                    : staging.Contains(at) ? "staging"
                    : at.Owner != side ? "attack"
                    : "idle at base";
                doing[d]++;
            }
            Shapes.Add(new(game.Day, side, fleets.Count, strengths.ElementAtOrDefault(0), strengths.ElementAtOrDefault(1), strengths.Count == 0 ? 0 : strengths[strengths.Count / 2],
                total == 0 ? 0 : (double)strengths.Take(2).Sum() / total,
                fleets.Sum(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type)).Sum(x => x.Contents.Count(y => Family.IsTroop(y.Type) && y.Existing))), doing));
        }
    }

    public static string Table(IReadOnlyList<(GameResult Result, FleetShapeRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        text.AppendLine("fleet shape (mean per game): fleets with capital ships, line-ship strength of the largest / second / median fleet, share in the largest 2, troops aboard; doing: defence / staging / attack / moving / idle at base");
        foreach (var who in new[] { "openswr", "original" })
            foreach (int day in Days)
            {
                var s = runs.SelectMany(x => x.Recorder.Shapes.Where(y => y.Day == day && (y.Side == x.Result.OpenSWR) == (who == "openswr"))).ToList();
                if (s.Count == 0) continue;
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {who,-9} d{day,-5} fleets {s.Average(x => x.Fleets),5:F1}  largest {s.Average(x => x.Largest),7:F0} second {s.Average(x => x.Second),7:F0} median {s.Average(x => x.Median),6:F0}  top2 {100 * s.Average(x => x.Top2Share),5:F1} %  troops aboard {s.Average(x => x.TroopsAboard),5:F1}  doing ")
                    + string.Join(" / ", Doings.Select(d => string.Create(CultureInfo.InvariantCulture, $"{s.Average(x => x.Doing[d]):F1}"))));
            }
        return text.ToString();
    }
}
