using OpenSWR.Data.Dat;
using System.Globalization;
using System.Text;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>One character capture or death (measurement): when, where, what the character was doing, why.</summary>
public sealed record CharacterLoss(int Day, Owner Side, uint Character, bool Death, uint? System, Owner SystemOwner, uint? MissionKind,
    string Activity, string Cause, OpenSWRController.MissionEstimate? Estimate);

/// <summary>One mission of either side: kind, created day, outcome, and the openswr estimate it was ordered with.</summary>
public sealed class MissionRecord(Mission m, int day)
{
    public Mission Mission { get; } = m;
    public uint Kind { get; } = (uint)m.Kind;
    public Owner Side { get; } = m.Owner;
    public int Created { get; } = day;
    public bool Succeeded { get; set; }
    public bool Ended { get; set; }
    public bool Lost { get; set; }
    public OpenSWRController.MissionEstimate? Estimate { get; set; }
    /// <summary>Team at the first sight: agents, decoys, special forces among each; the objective a major character.</summary>
    public int Agents { get; } = m.Team.Count;
    public int Decoys { get; } = m.Decoys.Count;
    public int ForceAgents { get; } = m.Team.Count(a => Family.IsSpecialForce(a.Type));
    public int ForceDecoys { get; } = m.Decoys.Count(a => Family.IsSpecialForce(a.Type));
    public bool Major { get; } = m.Objective is Character c && Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter;
    /// <summary>The objective system's owner at the first sight (none for other objectives).</summary>
    public Owner? ObjectiveOwner { get; } = (m.Objective as StarSystem)?.Owner;
}

/// <summary>
/// Records every character capture and death of both sides, the missions of both sides and their outcomes, and the openswr
/// side's mission estimates. An observer: reads the real state once per game day, draws
/// nothing, writes nothing.
/// </summary>
public sealed class CharacterRecorder
{
    public List<CharacterLoss> Losses { get; } = [];
    public List<MissionRecord> Missions { get; } = [];
    /// <summary>Per day and side: alive (in play, free), captured, dead (in play once, now not Existing), not yet in play.</summary>
    public Dictionary<(int Day, Owner Side), (int Alive, int Captured, int Dead, int NotInPlay)> Counts { get; } = [];
    readonly HashSet<Character> _inPlay = [];
    public Dictionary<Owner, int> Freed { get; } = [];
    public Dictionary<Owner, int> Recruited { get; } = [];
    /// <summary>Special forces lost (Existing gives not) per side.</summary>
    public Dictionary<Owner, int> ForcesLost { get; } = [];
    readonly HashSet<GameObject> _forces = [];

    readonly Dictionary<Character, (bool Captured, bool Existing, Owner Owner, Mission? Mission, GameObject? Container, StarSystem? Location)> _prev = [];
    readonly Dictionary<Mission, MissionRecord> _missions = [];
    readonly HashSet<OpenSWRController.MissionEstimate> _usedEstimates = [];
    bool _started;

    static readonly Owner[] Sides = [Owner.Alliance, Owner.Empire];

    /// <summary>The system a character is in (through its containers), or null.</summary>
    static StarSystem? SystemOf(GameObject o)
    {
        for (var x = o.Container; x is not null; x = x.Container)
            if (x is StarSystem s) return s;
        return null;
    }

    static Mission? MissionOf(GameState game, Character c) =>
        game.Missions.FirstOrDefault(m => !m.Deleted && m.CompletionStatus == MissionCompletion.None && m.Team.Contains(c));

    void Count(GameState game, int day)
    {
        foreach (var side in Sides)
        {
            var mine = game.Characters.Where(c => c.Owner == side || _prev.TryGetValue(c, out var p) && p.Owner == side).ToList();
            Counts[(day, side)] = (mine.Count(c => c.Existing && !c.Captured), mine.Count(c => c.Existing && c.Captured),
                mine.Count(c => !c.Existing && _inPlay.Contains(c)), mine.Count(c => !c.Existing && !_inPlay.Contains(c)));
        }
    }

    public void Day(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        if (!_started)
        {
            _started = true;
            foreach (var c in game.Characters.Where(c => c.Existing)) _inPlay.Add(c);
            Count(game, 0); // the state after the first day stands for day 0
        }
        // missions: new ones (with the openswr estimate they were ordered with), outcomes
        foreach (var m in game.Missions.Where(m => !_missions.ContainsKey(m) && m.Owner is Owner.Alliance or Owner.Empire && m.Kind >= MissionKind.Diplomacy))
        {
            var r = _missions[m] = new MissionRecord(m, game.Day);
            Missions.Add(r);
            if (game.Controllers.GetValueOrDefault(m.Owner) is OpenSWRController { MissionEstimates: { } estimates })
                r.Estimate = estimates.LastOrDefault(e => !_usedEstimates.Contains(e) && e.Kind == m.Kind && game.Day - e.Day <= 2 && e.Team.Any(m.Team.Contains));
            if (r.Estimate is not null) _usedEstimates.Add(r.Estimate);
        }
        foreach (var (m, r) in _missions)
        {
            if (m.TaskStatus == MissionTaskStatus.Succeeded) r.Succeeded = true;
            if (m.CompletionStatus != MissionCompletion.None || m.Deleted) r.Ended = true;
        }
        // special forces lost
        foreach (var f in game.Created.Where(o => Family.IsSpecialForce(o.Type) && o.Owner is Owner.Alliance or Owner.Empire))
        {
            if (f.Existing) _forces.Add(f);
            else if (_forces.Remove(f)) ForcesLost[f.Owner] = ForcesLost.GetValueOrDefault(f.Owner) + 1;
        }
        // characters
        var battles = (game.BattleTrace ?? []).Where(b => b.Day == game.Day).ToList();
        var changedToday = changes.Where(c => c.Day == game.Day).ToList();
        foreach (var c in game.Characters)
        {
            var location = SystemOf(c);
            if (_prev.TryGetValue(c, out var p))
            {
                bool captured = !p.Captured && c.Captured, death = p.Existing && !c.Existing;
                if (p.Captured && !c.Captured && c.Existing) Freed[p.Owner] = Freed.GetValueOrDefault(p.Owner) + 1;
                // recruited: a character that was never in play comes into play (Existing), or changes from neutral to a side
                if (!p.Existing && c.Existing && !_inPlay.Contains(c) || p.Owner == Owner.Neutral && c.Owner is Owner.Alliance or Owner.Empire)
                    if (c.Owner is Owner.Alliance or Owner.Empire) Recruited[c.Owner] = Recruited.GetValueOrDefault(c.Owner) + 1;
                if ((captured || death) && p.Owner is Owner.Alliance or Owner.Empire)
                {
                    var at = location ?? p.Location;
                    string activity;
                    if (p.Mission is { } pm)
                        activity = pm.Phase <= 4 ? "mission: travel" : pm.Phase <= 8 ? "mission: at the target" : "mission: execution";
                    else if (at is not null && changedToday.Any(x => x.System == at.Ref)) activity = "system changed owner";
                    else if (p.Container is not StarSystem && at is not null && battles.Any(b => b.System == at && b.Sides.Contains(p.Owner) && b.Won != p.Owner)) activity = "fleet lost a battle";
                    else if (at is not null && at.Owner == p.Owner) activity = "at home";
                    else activity = "other";
                    var enemy = p.Owner == Owner.Alliance ? Owner.Empire : Owner.Alliance;
                    string cause;
                    if (p.Mission is { CompletionStatus: MissionCompletion.FoiledEnRoute or MissionCompletion.Foiled }) cause = "foiled mission";
                    else if (game.Missions.Any(m => m.Owner == enemy && m.Kind is MissionKind.Abduction or MissionKind.Assassination && m.Objective == c)) cause = "enemy abduction / assassination";
                    else if (c.DestroyedReason is DestroyedReason.Battle or DestroyedReason.Assault || at is not null && (battles.Any(b => b.System == at) || changedToday.Any(x => x.System == at.Ref && x.Cause == ChangeCause.Troops)))
                        cause = "battle or assault";
                    else if (game.Missions.Any(m => m.Kind is MissionKind.Palace or MissionKind.Bounty or MissionKind.Dagobah or MissionKind.Vacation or MissionKind.Pickup && (m.Objective == c || m.Team.Contains(c)))) cause = "bounty hunter or story";
                    else if (p.Mission is not null) cause = "mission (other end)";
                    else cause = "other";
                    var rec = p.Mission is { } mm && _missions.TryGetValue(mm, out var mr) ? mr : null;
                    if (rec is not null) rec.Lost = true;
                    Losses.Add(new(game.Day, p.Owner, c.Ref, death, at?.Ref, at?.Owner ?? Owner.Neutral, (uint?)p.Mission?.Kind, activity, cause, rec?.Estimate));
                }
            }
            if (c.Existing) _inPlay.Add(c);
            _prev[c] = (c.Captured, c.Existing, c.Owner, c.Existing ? MissionOf(game, c) : null, c.Container, location ?? (_prev.TryGetValue(c, out var q) ? q.Location : null));
        }
        if (game.Day is 100 or 500 or 1000) Count(game, game.Day);
    }

    static string Name(uint kind) => Match.MissionName(kind);

    /// <summary>The character tables over the games of a run.</summary>
    public static string Tables(IReadOnlyList<(GameResult Result, CharacterRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        string Who(GameResult r, Owner side) => side == r.OpenSWR ? "openswr" : "original";
        var losses = runs.SelectMany(x => x.Recorder.Losses.Select(l => (Who: Who(x.Result, l.Side), l))).ToList();
        var missions = runs.SelectMany(x => x.Recorder.Missions.Select(m => (Who: Who(x.Result, m.Side), m))).ToList();
        int games = runs.Count;
        text.AppendLine($"character captures and deaths ({games} games; per game in brackets)");
        foreach (var who in new[] { "openswr", "original" })
        {
            var l = losses.Where(x => x.Who == who).Select(x => x.l).ToList();
            text.AppendLine($"  {who}: {l.Count} ({(double)l.Count / games:F1}), captures {l.Count(x => !x.Death)}, deaths {l.Count(x => x.Death)}");
            text.AppendLine("    by cause:");
            foreach (var g in l.GroupBy(x => x.Cause).OrderByDescending(g => g.Count()))
                text.AppendLine($"      {g.Key,-34} {g.Count(),6} {100.0 * g.Count() / Math.Max(1, l.Count),6:F1} %");
            text.AppendLine("    by activity:");
            foreach (var g in l.GroupBy(x => x.Activity).OrderByDescending(g => g.Count()))
                text.AppendLine($"      {g.Key,-34} {g.Count(),6} {100.0 * g.Count() / Math.Max(1, l.Count),6:F1} %");
            text.AppendLine("    by mission type (on a mission):");
            foreach (var g in l.Where(x => x.MissionKind is not null).GroupBy(x => x.MissionKind!.Value).OrderByDescending(g => g.Count()))
                text.AppendLine($"      {Name(g.Key),-34} {g.Count(),6}");
        }
        text.AppendLine("missions per type (created; share succeeded / failed / ended in a capture or death)");
        foreach (var who in new[] { "openswr", "original" })
        {
            text.AppendLine($"  {who}");
            foreach (var g in missions.Where(x => x.Who == who).Select(x => x.m).GroupBy(m => m.Kind).OrderByDescending(g => g.Count()))
            {
                var ended = g.Where(m => m.Ended).ToList();
                int n = Math.Max(1, ended.Count);
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    {Name(g.Key),-18} {g.Count(),7}   ended {ended.Count,7}: ok {100.0 * ended.Count(m => m.Succeeded) / n,5:F1} %  failed {100.0 * ended.Count(m => !m.Succeeded) / n,5:F1} %  lost {100.0 * ended.Count(m => m.Lost) / n,5:F1} %"));
            }
        }
        text.AppendLine("openswr estimated chance against the actual success (ended missions with an estimate)");
        foreach (var (lo, hi) in new[] { (0.0, 40.0), (40.0, 60.0), (60.0, 80.0), (80.0, 101.0) })
        {
            var band = missions.Where(x => x.Who == "openswr" && x.m.Ended && x.m.Estimate is { Chance: >= 0 } e && e.Chance >= lo && e.Chance < hi).Select(x => x.m).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {(hi > 100 ? $">= {lo:F0} %" : $"{lo:F0}–{hi:F0} %"),-10} missions {band.Count,6}, mean estimate {(band.Count == 0 ? 0 : band.Average(m => m.Estimate!.Chance)),5:F1} %, actual success {(band.Count == 0 ? 0 : 100.0 * band.Count(m => m.Succeeded) / band.Count),5:F1} %, lost {(band.Count == 0 ? 0 : 100.0 * band.Count(m => m.Lost) / band.Count),5:F1} %"));
            foreach (var g in band.GroupBy(m => m.Kind).OrderByDescending(g => g.Count()).Take(4))
                text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"      {Name(g.Key),-16} {g.Count(),6}  success {100.0 * g.Count(m => m.Succeeded) / g.Count(),5:F1} %"));
        }
        // Measurement: openswr diplomacy by the objective's owner at the order
        text.AppendLine("openswr diplomacy missions by objective (per game; ended: succeeded)");
        foreach (var (name, pick) in new (string, Func<GameResult, MissionRecord, bool>)[] {
            ("own systems", (r, m) => m.ObjectiveOwner == r.OpenSWR), ("neutral systems", (r, m) => m.ObjectiveOwner == Owner.Neutral),
            ("other", (r, m) => m.ObjectiveOwner != r.OpenSWR && m.ObjectiveOwner != Owner.Neutral) })
        {
            var d = runs.SelectMany(x => x.Recorder.Missions.Where(m => m.Side == x.Result.OpenSWR && m.Kind == 81 && pick(x.Result, m))).ToList();
            var ended = d.Where(m => m.Ended).ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {name,-16} {(double)d.Count / games,7:F2}  ended {(double)ended.Count / games,7:F2}: succeeded {(ended.Count == 0 ? 0 : 100.0 * ended.Count(m => m.Succeeded) / ended.Count),5:F1} %"));
        }
        // Rule 5 (always): per kind, the estimated success at the send against the realized success
        text.AppendLine("openswr estimate against reality per kind (ended missions with an estimate): missions, mean estimated success, realized success");
        foreach (var g in missions.Where(x => x.Who == "openswr" && x.m.Ended && x.m.Estimate is { Chance: >= 0 }).Select(x => x.m).GroupBy(m => m.Kind).OrderBy(g => g.Key))
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {Name(g.Key),-16} {g.Count(),7}  estimate {g.Average(m => m.Estimate!.Chance),5:F1} %  realized {100.0 * g.Count(m => m.Succeeded) / g.Count(),5:F1} %  lost {100.0 * g.Count(m => m.Lost) / g.Count(),5:F1} %"));
        text.AppendLine("top 5 openswr mission types by characters lost, with the known target defences at the order (mean troops / special forces / characters / fleets)");
        foreach (var g in losses.Where(x => x.Who == "openswr" && x.l.MissionKind is not null).GroupBy(x => x.l.MissionKind!.Value).OrderByDescending(g => g.Count()).Take(5))
        {
            var e = g.Select(x => x.l.Estimate).OfType<OpenSWRController.MissionEstimate>().ToList();
            string d = e.Count == 0 ? "no estimate recorded" : string.Create(CultureInfo.InvariantCulture,
                $"{e.Average(x => x.Troops):F1} / {e.Average(x => x.SpecialForces):F1} / {e.Average(x => x.Characters):F1} / {e.Average(x => x.Fleets):F1} ({e.Count} with estimates; chance {(e.Any(x => x.Chance >= 0) ? e.Where(x => x.Chance >= 0).Average(x => x.Chance) : -1):F0} %, worth {(e.Any(x => x.Worth >= 0) ? e.Where(x => x.Worth >= 0).Average(x => x.Worth) : -1):F1})");
            text.AppendLine($"  {Name(g.Key),-18} {g.Count(),6} lost; defences {d}; causes " + string.Join(", ", g.GroupBy(x => x.l.Cause).OrderByDescending(c => c.Count()).Select(c => $"{c.Key} {c.Count()}")));
        }
        text.AppendLine("team sizes per mission type (average agents / decoys; special forces as agents / decoys, share of missions); split minor / major character targets");
        foreach (var who in new[] { "openswr", "original" })
        {
            text.AppendLine($"  {who}: special forces lost {(double)runs.Sum(x => x.Recorder.ForcesLost.GetValueOrDefault(who == "openswr" ? x.Result.OpenSWR : Other(x.Result.OpenSWR))) / games:F1} per game");
            foreach (var g in missions.Where(x => x.Who == who).Select(x => x.m).GroupBy(m => (m.Kind, m.Major)).OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Major))
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"    {Name(g.Key.Kind) + (g.Key.Kind is 97 or 98 or 99 ? g.Key.Major ? " (major)" : " (minor)" : ""),-26} {g.Count(),7}  agents {g.Average(m => m.Agents),4:F2} decoys {g.Average(m => m.Decoys),4:F2}  special-force agents {100.0 * g.Count(m => m.ForceAgents > 0) / g.Count(),5:F1} %, decoys {100.0 * g.Count(m => m.ForceDecoys > 0) / g.Count(),5:F1} %"));
        }
        text.AppendLine("characters per side (alive / captured / dead / not yet in play, average per game), freed later, recruited");
        foreach (var who in new[] { "openswr", "original" })
        {
            var line = new StringBuilder($"  {who,-9}");
            foreach (int day in new[] { 0, 100, 500, 1000 })
            {
                var v = runs.Select(x => x.Recorder.Counts.TryGetValue((day, who == "openswr" ? x.Result.OpenSWR : Other(x.Result.OpenSWR)), out var c) ? c : ((int, int, int, int)?)null).OfType<(int, int, int, int)>().ToList();
                if (v.Count > 0) line.Append(string.Create(CultureInfo.InvariantCulture, $"  d{day} {v.Average(x => x.Item1):F1} / {v.Average(x => x.Item2):F1} / {v.Average(x => x.Item3):F1} / {v.Average(x => x.Item4):F1}"));
            }
            int freed = runs.Sum(x => x.Recorder.Freed.GetValueOrDefault(who == "openswr" ? x.Result.OpenSWR : Other(x.Result.OpenSWR)));
            int recruited = runs.Sum(x => x.Recorder.Recruited.GetValueOrDefault(who == "openswr" ? x.Result.OpenSWR : Other(x.Result.OpenSWR)));
            var rec = missions.Where(x => x.Who == who && x.m.Kind == 85).Select(x => x.m).ToList();
            line.Append(string.Create(CultureInfo.InvariantCulture, $"; freed {(double)freed / games:F1}; recruitment missions {(double)rec.Count / games:F1} (succeeded {(double)rec.Count(m => m.Succeeded) / games:F1}), characters recruited {(double)recruited / games:F1} per game"));
            text.AppendLine(line.ToString());
        }
        return text.ToString();
    }

    static Owner Other(Owner side) => side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
}
