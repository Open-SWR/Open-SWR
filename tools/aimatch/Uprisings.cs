using System.Globalization;
using System.Text;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>
/// Measurement only: every own system of the openswr side lost to uprising, with its support and troops history since capture,
/// own troop moves out of it, enemy missions against it, support changes by cause and own subdue missions, all within
/// <see cref="Window"/> days before the loss. An observer: reads the real state once per day (and the controller's troop-move
/// log), draws nothing, writes nothing.
/// </summary>
public sealed class UprisingRecorder
{
    public const int Window = 60;

    /// <summary>One loss to uprising.</summary>
    public sealed record Loss(int Day, uint System, int? TakenDay, int ShareAtCapture, int ShareAtStart, int LowestShare, int TroopsAtStart, int Requirement,
        int MaxTroops, Dictionary<string, int> MovesOut, int Incites, int IncitesOk, int Espionage, int OtherMissions,
        int SupportIncite, int SupportBattle, int SupportOther, bool SubdueSent, string SubdueResult);

    sealed class Track
    {
        public int? Taken;
        public int ShareAtCapture, Lowest = 100, MaxTroops, ShareAtStart = -1, TroopsAtStart, Requirement;
        public List<(int Day, int Delta, string Cause)> Support { get; } = [];
        public int LastShare;
    }

    sealed class Seen(Mission m, int day)
    {
        public Mission Mission { get; } = m;
        public int Day { get; } = day;
        public bool Succeeded { get; set; }
        public string Result { get; set; } = "running";
    }

    public List<Loss> Losses { get; } = [];
    readonly Dictionary<uint, Track> _tracks = [];
    readonly Dictionary<Mission, Seen> _missions = [];
    Owner _side = Owner.None;

    static int Troops(StarSystem s, Owner side) => s.Contents.Count(x => Family.IsTroop(x.Type) && x.Owner == side && x.Existing);

    /// <summary>Called once at the end of every game day.</summary>
    public void Day(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        var ai = game.Controllers.Values.OfType<OpenSWRController>().FirstOrDefault();
        if (ai is null) return;
        _side = ai.Side;
        var enemy = _side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        // missions of either side, first seen and outcome
        foreach (var m in game.Missions.Where(m => !_missions.ContainsKey(m))) _missions[m] = new(m, game.Day);
        foreach (var x in _missions.Values.Where(x => x.Result == "running"))
        {
            if (x.Mission.TaskStatus == 3) x.Succeeded = true;
            if (x.Mission.CompletionStatus != 0 || x.Mission.Deleted) x.Result = x.Succeeded ? "succeeded" : x.Mission.CompletionStatus is 3 or 4 ? "foiled" : "failed";
        }
        var today = changes.Where(c => c.Day == game.Day).ToList();
        var battles = (game.BattleTrace ?? []).Where(b => b.Day == game.Day).Select(b => b.System).ToHashSet();
        foreach (var s in game.Systems)
        {
            int share = s.SupportFor(_side);
            // taken: own today, not own at the last day (the first day's systems count as held since the start)
            if (s.Owner == _side && _seen && !_owned.Contains(s.Ref))
                _tracks[s.Ref] = new() { Taken = game.Day, ShareAtCapture = share, Lowest = share, LastShare = share };
            if (s.Owner != _side)
            {
                // a loss to uprising today: the record ends
                if (today.FirstOrDefault(c => c.System == s.Ref && c.From == _side && c.Cause == ChangeCause.Uprising) is { } lost && _tracks.Remove(s.Ref, out var t))
                    Losses.Add(Close(game, ai, s, lost.Day, t, enemy));
                else _tracks.Remove(s.Ref);
                continue;
            }
            if (!_tracks.TryGetValue(s.Ref, out var tr)) _tracks[s.Ref] = tr = new() { ShareAtCapture = share, Lowest = share, LastShare = share }; // held since the start
            tr.Lowest = Math.Min(tr.Lowest, share);
            tr.MaxTroops = Math.Max(tr.MaxTroops, Troops(s, _side));
            if (share != tr.LastShare)
            {
                // an enemy incite there in its success state today (Inferred: the incident's support change comes with it)
                bool incite = _missions.Values.Any(x => x.Mission.Owner == enemy && x.Mission.Kind == 0x56 && x.Mission.Objective == s && x.Mission.TaskStatus == 3);
                tr.Support.Add((game.Day, share - tr.LastShare, incite ? "incite" : battles.Contains(s) ? "battle" : "other"));
                tr.LastShare = share;
            }
            if (s.Uprising && tr.ShareAtStart < 0) (tr.ShareAtStart, tr.TroopsAtStart, tr.Requirement) = (share, Troops(s, _side), s.GarrisonRequirement);
            if (!s.Uprising) tr.ShareAtStart = -1;
        }
        _owned = [.. game.Systems.Where(s => s.Owner == _side).Select(s => s.Ref)];
        _seen = true;
    }

    HashSet<uint> _owned = [];
    bool _seen;

    Loss Close(GameState game, OpenSWRController ai, StarSystem s, int day, Track t, Owner enemy)
    {
        int from = day - Window;
        var moves = (ai.TroopMoveLog ?? []).Where(m => m.From == s.Ref && m.Day > from && m.Day <= day).GroupBy(m => m.Step).ToDictionary(g => g.Key, g => g.Count());
        var against = _missions.Values.Where(x => x.Mission.Owner == enemy && x.Day > from && x.Day <= day && (x.Mission.Objective == s || x.Mission.TargetLocation == s)).ToList();
        var support = t.Support.Where(x => x.Day > from).ToList();
        var subdue = _missions.Values.Where(x => x.Mission.Owner == _side && x.Mission.Kind == 0x57 && x.Mission.Objective == s && x.Day > from && x.Day <= day).OrderBy(x => x.Day).LastOrDefault();
        return new(day, s.Ref, t.Taken, t.ShareAtCapture, t.ShareAtStart < 0 ? s.SupportFor(_side) : t.ShareAtStart, t.Lowest, t.TroopsAtStart, t.Requirement, t.MaxTroops, moves,
            against.Count(x => x.Mission.Kind == 0x56), against.Count(x => x.Mission.Kind == 0x56 && x.Succeeded), against.Count(x => x.Mission.Kind == 0x52),
            against.Count(x => x.Mission.Kind is not (0x56 or 0x52)),
            support.Where(x => x.Cause == "incite").Sum(x => x.Delta), support.Where(x => x.Cause == "battle").Sum(x => x.Delta), support.Where(x => x.Cause == "other").Sum(x => x.Delta),
            subdue is not null, subdue?.Result ?? "none");
    }

    public static string Table(IReadOnlyList<(GameResult Result, UprisingRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        var l = runs.SelectMany(x => x.Recorder.Losses).ToList();
        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        double M(Func<Loss, double> f, IEnumerable<Loss>? set = null) { var s = (set ?? l).ToList(); return s.Count == 0 ? 0 : s.Average(f); }
        string Dist(Func<Loss, int> f, int[] edges)
        {
            var parts = new List<string>();
            for (int i = 0; i <= edges.Length; i++)
            {
                int lo = i == 0 ? int.MinValue : edges[i - 1], hi = i == edges.Length ? int.MaxValue : edges[i];
                int n = l.Count(x => f(x) >= lo && f(x) < hi);
                string label = i == 0 ? $"<{hi}" : i == edges.Length ? $">={lo}" : $"{lo}-{hi - 1}";
                parts.Add($"{label} {(l.Count == 0 ? 0 : 100.0 * n / l.Count).ToString("F0", CultureInfo.InvariantCulture)}%");
            }
            return string.Join(", ", parts);
        }
        var taken = l.Where(x => x.TakenDay is not null).ToList();
        text.AppendLine($"openswr own systems lost to uprising (measurement, window {Window} days before the loss): {F((double)l.Count / games)} per game, {F((double)taken.Count / games)} of them taken during the game");
        text.AppendLine($"  (1) own share at capture {F(M(x => x.ShareAtCapture, taken))} (taken only), at the uprising start {F(M(x => x.ShareAtStart))}, lowest since capture {F(M(x => x.LowestShare))}");
        text.AppendLine($"      share at the start: {Dist(x => x.ShareAtStart, [20, 30, 40, 50, 60])}");
        text.AppendLine($"      share at capture (taken): {Dist(x => x.TakenDay is null ? -1 : x.ShareAtCapture, [0, 20, 30, 40, 50, 60])} (<0 = not taken)");
        text.AppendLine($"  (2) own troops at the start {F(M(x => x.TroopsAtStart))} against the requirement {F(M(x => x.Requirement))}; troops below it at the start {(l.Count == 0 ? "-" : (100.0 * l.Count(x => x.TroopsAtStart < x.Requirement) / l.Count).ToString("F0", CultureInfo.InvariantCulture) + "%")}; max troops since capture {F(M(x => x.MaxTroops))}");
        text.AppendLine($"      troops − requirement at the start: {Dist(x => x.TroopsAtStart - x.Requirement, [-3, -1, 0, 1, 3])}");
        var steps = l.SelectMany(x => x.MovesOut.Keys).Distinct().OrderBy(x => x).ToList();
        text.AppendLine($"  (3) losses with own troops moved out in the window: {(l.Count == 0 ? "-" : (100.0 * l.Count(x => x.MovesOut.Count > 0) / l.Count).ToString("F0", CultureInfo.InvariantCulture) + "%")}; moves per loss by step: "
            + (steps.Count == 0 ? "none" : string.Join(", ", steps.Select(st => $"{st} {F(M(x => x.MovesOut.GetValueOrDefault(st)))}"))));
        text.AppendLine($"  (4) enemy missions against it in the window, per loss: incite {F(M(x => x.Incites))} (succeeded {F(M(x => x.IncitesOk))}), espionage {F(M(x => x.Espionage))}, other {F(M(x => x.OtherMissions))}; losses with an incite {(l.Count == 0 ? "-" : (100.0 * l.Count(x => x.Incites > 0) / l.Count).ToString("F0", CultureInfo.InvariantCulture) + "%")}");
        text.AppendLine($"  (5) own support change in the window, per loss: on incite-success days {F(M(x => x.SupportIncite))}, battle days {F(M(x => x.SupportBattle))}, other days (drift and the rest) {F(M(x => x.SupportOther))}");
        var sub = l.Where(x => x.SubdueSent).ToList();
        text.AppendLine($"  (6) own subdue sent in the window: {(l.Count == 0 ? "-" : (100.0 * sub.Count / l.Count).ToString("F0", CultureInfo.InvariantCulture) + "%")} of losses; results: "
            + (sub.Count == 0 ? "none" : string.Join(", ", sub.GroupBy(x => x.SubdueResult).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))));
        return text.ToString();
    }
}
