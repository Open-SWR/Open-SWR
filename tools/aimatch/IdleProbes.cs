using System.Globalization;
using System.Text;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>
/// Measurement only: the openswr side's idle special forces and the reason each was not used, at the decisions of days 100,
/// 300, 500, 700 and 1000 (the controller's <see cref="OpenSWRController.IdleProbe"/>), plus the training orders placed while
/// more than half the pool was idle.
/// </summary>
public sealed class IdleProbeRecorder
{
    public static readonly int[] Days = [100, 300, 500, 700, 1000];
    public List<OpenSWRController.IdleProbe> Probes { get; } = [];
    /// <summary>Training orders while idle &gt; 50 %, cumulative at each probe day.</summary>
    public Dictionary<int, int> TrainedWhileIdle { get; } = [];
    OpenSWRController.IdleProbe? _pending;

    /// <summary>Called at the end of every game day: arms the probe for the next day's decision, collects the filled one.</summary>
    public void Day(GameState game)
    {
        var ai = game.Controllers.Values.OfType<OpenSWRController>().FirstOrDefault();
        if (ai is null) return;
        if (_pending is not null && _pending.Day == game.Day)
        {
            Probes.Add(_pending);
            TrainedWhileIdle[game.Day] = ai.TrainedWhileIdle;
            _pending = null;
        }
        if (Days.Contains(game.Day + 1)) ai.Probe = _pending = new();
    }

    /// <summary>The type kind of a special-force type from the masks: espionage, reconnaissance, or other.</summary>
    static string KindOf(GameDefinitions d, uint type)
    {
        var f = d.SpecialForces.FirstOrDefault(x => x.Common.Ref == type);
        if (f is null || f.MissionMask == 0) return "other";
        bool Serves(uint kind) => d.Missions.Any(r => !r.Unlisted && Family.Of(r.Common.Ref) == kind && r.AllowsSpecialForce(f.MissionMask));
        return Serves(82) ? "espionage" : Serves(84) ? "recon" : "other";
    }

    public static string Table(IReadOnlyList<(GameResult Result, IdleProbeRecorder Recorder)> runs, GameDefinitions d)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        var reasons = new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i" };
        text.AppendLine($"openswr idle special forces by first reason (mean per game, {runs.Count} games)");
        text.AppendLine($"  {"day",-6} {"pool",6} {"target",7} {"idle",6} " + string.Concat(reasons.Select(r => $" {r,6}")) + $" {"trained@idle>50%",17}");
        foreach (int day in Days)
        {
            var probes = runs.SelectMany(x => x.Recorder.Probes.Where(p => p.Day == day)).ToList();
            if (probes.Count == 0) continue;
            var idle = probes.SelectMany(p => p.Idle).ToList();
            double trained = runs.Average(x => (double)x.Recorder.TrainedWhileIdle.GetValueOrDefault(day));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {day,-6} {probes.Average(p => p.PoolMaintenance),6:F1} {probes.Average(p => p.PoolTarget),7:F1} {(double)idle.Count / probes.Count,6:F1} ")
                + string.Concat(reasons.Select(r => string.Create(CultureInfo.InvariantCulture, $" {(double)idle.Count(x => x.Reason == r) / probes.Count,6:F1}")))
                + string.Create(CultureInfo.InvariantCulture, $" {trained,17:F1}"));
        }
        text.AppendLine("  by type kind (all probe days, mean per game and probe day)");
        var all = runs.SelectMany(x => x.Recorder.Probes).ToList();
        foreach (var g in all.SelectMany(p => p.Idle).GroupBy(x => KindOf(d, x.Type)).OrderBy(g => g.Key))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    {g.Key,-10} {(double)g.Count() / Math.Max(1, all.Count),6:F1} idle: ")
                + string.Join(", ", g.GroupBy(x => x.Reason).OrderByDescending(r => r.Count()).Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.Key} {(double)r.Count() / Math.Max(1, all.Count):F1}"))));
        text.AppendLine("  reasons in detail (share of idle special-force probes)");
        int n = Math.Max(1, all.Sum(p => p.Idle.Count));
        foreach (var g in all.SelectMany(p => p.Idle).GroupBy(x => (x.Reason, x.Detail)).OrderByDescending(g => g.Count()).Take(20))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    {g.Key.Reason} {100.0 * g.Count() / n,5:F1} %  {g.Key.Detail}"));
        text.AppendLine("  covert targets considered per kind and outcome (mean per game and probe day)");
        foreach (var g in all.SelectMany(p => p.Considered).GroupBy(x => (x.Kind, x.Outcome)).OrderBy(g => g.Key.Kind).ThenByDescending(g => g.Count()))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"    {Match.MissionName(g.Key.Kind),-16} {g.Key.Outcome,-40} {(double)g.Count() / Math.Max(1, all.Count),7:F2}"));
        text.AppendLine("  covert candidates per kind at the probe decisions (mean per game and probe day): before caps / after caps / sent");
        foreach (var kind in all.SelectMany(p => p.CandidatesBefore.Keys.Concat(p.CandidatesAfter.Keys).Concat(p.Sent.Keys)).Distinct().Order())
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    {Match.MissionName(kind),-16} {all.Average(p => p.CandidatesBefore.GetValueOrDefault(kind)),6:F2} / {all.Average(p => p.CandidatesAfter.GetValueOrDefault(kind)),6:F2} / {all.Average(p => p.Sent.GetValueOrDefault(kind)),6:F2}"));
        return text.ToString();
    }
}
