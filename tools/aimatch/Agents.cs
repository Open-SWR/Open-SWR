using System.Globalization;
using System.Text;
using OpenSWR.Sim;

namespace OpenSWR.AiMatch;

/// <summary>
/// Measurement only, for the computer opponent's special-force economy: per side and slice the
/// special forces by type (count, on a mission, idle, lost), the missions created by kind and agent kind with decoys, and the
/// retries (a mission at an objective where the side had a foiled mission before) with their success and loss. An observer:
/// reads the real state once per day, draws nothing, writes nothing.
/// </summary>
public sealed class AgentRecorder
{
    static readonly Owner[] Sides = [Owner.Alliance, Owner.Empire];

    sealed class Run(Mission m, bool retry, int born)
    {
        public int Born { get; } = born;
        public Mission Mission { get; } = m;
        public uint Kind { get; } = m.Kind;
        public Owner Side { get; } = m.Owner;
        public List<GameObject> Members { get; } = [.. m.Team, .. m.Decoys];
        public bool ForceAgent { get; } = m.Team.Any(a => Family.IsSpecialForce(a.Type));
        public int Decoys { get; } = m.Decoys.Count;
        public bool Retry { get; } = retry;
        public bool Succeeded { get; set; }
        public bool Ended { get; set; }
        public bool Lost { get; set; }
    }

    /// <summary>Per slice end day and side: type -> (count, on a mission, idle, lost within the slice).</summary>
    public Dictionary<(int Day, Owner Side), Dictionary<uint, (int Count, int OnMission, int Idle, int Lost)>> Forces { get; } = [];
    /// <summary>Per slice end day and side: (kind, special-force agent) -> (missions created, decoys).</summary>
    public Dictionary<(int Day, Owner Side), Dictionary<(uint Kind, bool Force), (int Missions, int Decoys)>> Created { get; } = [];

    readonly Dictionary<Mission, Run> _runs = [];
    readonly List<Run> _all = [];
    readonly HashSet<(Owner, uint)> _foiled = [];
    readonly HashSet<GameObject> _alive = [];
    readonly Dictionary<(Owner, uint), int> _lost = [];

    public void Day(GameState game, int length)
    {
        foreach (var m in game.Missions.Where(m => !_runs.ContainsKey(m) && m.Owner is Owner.Alliance or Owner.Empire && m.Kind >= 0x51 && m.Team.Count + m.Decoys.Count > 0))
        {
            var r = _runs[m] = new Run(m, m.Objective is { } o && _foiled.Contains((m.Owner, o.Ref)), game.Day);
            _all.Add(r);
        }
        foreach (var r in _all.Where(r => !r.Ended))
        {
            var m = r.Mission;
            if (m.TaskStatus == 3) r.Succeeded = true;
            if (r.Members.Any(x => !x.Existing || x is Character { Captured: true })) r.Lost = true;
            if (m.CompletionStatus == 0 && !m.Deleted) continue;
            r.Ended = true;
            if (m.CompletionStatus is 3 or 4 && m.Objective is { } o) _foiled.Add((r.Side, o.Ref));
        }
        foreach (var f in game.Created.Where(o => Family.IsSpecialForce(o.Type) && o.Owner is Owner.Alliance or Owner.Empire))
        {
            if (f.Existing) _alive.Add(f);
            else if (_alive.Remove(f)) _lost[(f.Owner, f.Type)] = _lost.GetValueOrDefault((f.Owner, f.Type)) + 1;
        }
        if (game.Day % length != 0 || game.Day == 0) return;
        foreach (var side in Sides)
        {
            var onMission = game.Missions.Where(m => m.Owner == side && !m.Deleted && m.CompletionStatus == 0).SelectMany(m => m.Team.Concat(m.Decoys)).ToHashSet();
            var mine = game.Created.Where(o => Family.IsSpecialForce(o.Type) && o.Owner == side && o.Existing).ToList();
            var table = Forces[(game.Day, side)] = [];
            foreach (var type in mine.Select(o => o.Type).Concat(_lost.Keys.Where(k => k.Item1 == side).Select(k => k.Item2)).Distinct())
            {
                var of = mine.Where(o => o.Type == type).ToList();
                table[type] = (of.Count, of.Count(onMission.Contains), of.Count(o => !onMission.Contains(o)), _lost.GetValueOrDefault((side, type)));
            }
            var created = Created[(game.Day, side)] = [];
            foreach (var g in _all.Where(r => r.Side == side && r.Born > game.Day - length && r.Born <= game.Day).GroupBy(r => (r.Kind, r.ForceAgent)))
                created[g.Key] = (g.Count(), g.Sum(r => r.Decoys));
        }
        foreach (var k in _lost.Keys.ToList()) _lost[k] = 0;
    }

    /// <summary>The 7a tables over the games: per side played and figure, per slice the mean per game.</summary>
    public static string Tables(IReadOnlyList<(GameResult Result, AgentRecorder Recorder)> runs, Func<uint, string> typeName)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        string Who(GameResult r, Owner side) => side == r.OpenSWR ? "openswr" : "original";
        var days = runs.SelectMany(x => x.Recorder.Forces.Keys.Select(k => k.Day)).Distinct().Order().ToList();
        text.AppendLine($"special forces by type (mean per game: count / on a mission / idle / lost in the slice), {runs.Count} games");
        foreach (var who in new[] { "openswr", "original" })
            foreach (var played in Sides)
            {
                var mine = runs.Where(x => x.Result.OpenSWR == played).ToList();
                if (mine.Count == 0) continue;
                var side = who == "openswr" ? played : Other(played);
                text.AppendLine($"  {who} as {side} ({mine.Count} games)");
                var types = mine.SelectMany(x => x.Recorder.Forces.Where(e => e.Key.Side == side).SelectMany(e => e.Value.Keys)).Distinct().Order().ToList();
                foreach (var type in types)
                {
                    var line = new StringBuilder($"    {typeName(type),-28}");
                    foreach (int day in days)
                    {
                        var v = mine.Select(x => x.Recorder.Forces.GetValueOrDefault((day, side))?.GetValueOrDefault(type) ?? default).ToList();
                        line.Append(string.Create(CultureInfo.InvariantCulture, $" d{day} {v.Average(x => x.Count):F1}/{v.Average(x => x.OnMission):F1}/{v.Average(x => x.Idle):F1}/{v.Average(x => x.Lost):F1}"));
                    }
                    text.AppendLine(line.ToString());
                }
            }
        text.AppendLine("missions created per kind and agent kind (per game, all slices; decoys per mission)");
        foreach (var who in new[] { "openswr", "original" })
        {
            text.AppendLine($"  {who}");
            var all = runs.SelectMany(x => x.Recorder.Created.Where(e => Who(x.Result, e.Key.Side) == who).SelectMany(e => e.Value)).GroupBy(e => e.Key)
                .Select(g => (g.Key.Kind, g.Key.Force, Missions: g.Sum(e => e.Value.Missions), Decoys: g.Sum(e => e.Value.Decoys))).OrderBy(x => x.Kind).ThenBy(x => x.Force).ToList();
            foreach (var x in all)
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    {Match.MissionName(x.Kind),-16} {(x.Force ? "special force" : "character"),-14} {(double)x.Missions / games,8:F1} per game, decoys {(double)x.Decoys / Math.Max(1, x.Missions),5:F2}"));
            // espionage per slice
            var line = new StringBuilder("    espionage per slice (SF + char):");
            foreach (int day in days)
            {
                int Count(bool force) => runs.Sum(x => x.Recorder.Created.Where(e => e.Key.Day == day && Who(x.Result, e.Key.Side) == who).Sum(e => e.Value.GetValueOrDefault((0x52u, force)).Missions));
                line.Append(string.Create(CultureInfo.InvariantCulture, $" d{day} {(double)Count(true) / games:F1}+{(double)Count(false) / games:F1}"));
            }
            text.AppendLine(line.ToString());
        }
        text.AppendLine("retries (a mission at an objective where the side had a foiled mission before; ended missions): success / any member lost");
        foreach (var who in new[] { "openswr", "original" })
        {
            var ended = runs.SelectMany(x => x.Recorder._all.Where(r => r.Ended && Who(x.Result, r.Side) == who)).ToList();
            foreach (var (name, set) in new[] { ("first tries", ended.Where(r => !r.Retry)), ("retries, no decoys", ended.Where(r => r.Retry && r.Decoys == 0)), ("retries with decoys", ended.Where(r => r.Retry && r.Decoys > 0)) })
            {
                var l = set.ToList();
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {who,-9} {name,-20} {(double)l.Count / games,8:F1} per game  success {100.0 * l.Count(r => r.Succeeded) / Math.Max(1, l.Count),5:F1} %  lost {100.0 * l.Count(r => r.Lost) / Math.Max(1, l.Count),5:F1} %"));
                foreach (var g in l.GroupBy(r => r.Kind).OrderByDescending(g => g.Count()).Take(name == "first tries" ? 0 : 4))
                    text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"      {Match.MissionName(g.Key),-16} {(double)g.Count() / games,6:F1}  success {100.0 * g.Count(r => r.Succeeded) / g.Count(),5:F1} %  lost {100.0 * g.Count(r => r.Lost) / g.Count(),5:F1} %"));
            }
        }
        return text.ToString();
    }

    static Owner Other(Owner side) => side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
}
