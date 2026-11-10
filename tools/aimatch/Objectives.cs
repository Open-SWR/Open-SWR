using System.Globalization;
using System.Text;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>
/// Measurement only: the standard-mode victory objectives per game. The four major characters (captures, releases,
/// where they sat), the Alliance HQ (the first object of families [0x20, 0x22) by key, as Victory.Recompute) and Coruscant (enemy
/// fleets arriving, battles, bombardments, troop landings, owner changes, destroyed), missions against the four characters, and
/// the conditions met at the last day. An observer: reads the state once per day, draws nothing, writes nothing.
/// </summary>
public sealed class ObjectivesRecorder
{
    static readonly uint[] Characters = [Refs.Luke, 0x30000240, Refs.Vader, Refs.Palpatine];
    static string Name(uint r) => r switch { Refs.Luke => "Luke", 0x30000240 => "Mon Mothma", Refs.Vader => "Vader", _ => "Palpatine" };

    /// <summary>Per character: capture history and days per place.</summary>
    public sealed class Person
    {
        public int? FirstCaptured;
        public int Captures, Rescued, Escaped;
        public bool CapturedAtEnd;
        public Dictionary<string, int> Days { get; } = [];
    }

    /// <summary>The HQ or Coruscant: events by the side that wants it.</summary>
    public sealed class Target
    {
        public int? KnownDay, DestroyedDay;
        public int Arrivals, Battles, Bombardments, Landings, LandingsWon, OwnerChanges, Relocations;
    }

    /// <summary>A mission of either side against one of the four characters.</summary>
    public sealed class Attempt(Owner side, uint target, uint kind, int day = 0)
    {
        public int Day { get; } = day;
        public Owner Side { get; } = side;
        public uint Target { get; } = target;
        public uint Kind { get; } = kind;
        public bool Succeeded;
        public string Result = "running";
        /// <summary>Step 23b: the ending (day, TaskStatus, CompletionStatus, target in the mission's target system) and the target's true EnhCombat at the order and the end.</summary>
        public int EndDay, Task, Completion, CombatAtOrder, CombatAtEnd;
        public bool InSystemAtEnd;
        /// <summary>Step 24: the target directly in a system at the order (truth), and the team's travel days from the mission origin to the target system (slowest member; -1 unknown).</summary>
        public bool InSystemAtOrder;
        public int TravelDays = -1;
    }

    public Dictionary<uint, Person> People { get; } = Characters.ToDictionary(r => r, _ => new Person());
    public Target Hq { get; } = new();
    public Target Coruscant { get; } = new();
    public List<Attempt> Attempts { get; } = [];
    public int Mode = -1;
    public int? VictoryDay;
    public Owner Winner = Owner.None;
    public int EmpireMet, AllianceMet; // of the 3 conditions, at the last day seen
    /// <summary>The openswr side's hunt (step 21): espionage sent, track gaps, HQ search (Empire side).</summary>
    public int HuntSpies, HqLeft, HqRelocations, SiegeDays, SiegeTries, SiegeBombards;
    public int? HqFound;
    public List<(uint Character, int Days)> TrackGaps { get; } = [];
    /// <summary>Step 22: the openswr side's objective abduction estimates, siege groups formed, siege launches (strength, known defence), days the Empire view knew the HQ system.</summary>
    public List<(int Day, uint Target, double Estimate, int AssumedCombat, bool Seen)> HuntEstimates { get; } = [];
    public int SiegeGroups, HqKnownDays;
    public List<(int Day, int Strength, int Defence)> SiegeLaunches { get; } = [];

    /// <summary>Copies the openswr controller's hunt figures at the game end.</summary>
    public void Hunted(OpenSWRController ai)
    {
        (HuntSpies, HqLeft, HqRelocations, HqFound) = (ai.HuntSpiesSent, ai.HqCandidatesLeft, ai.HqRelocationsSeen, ai.HqFoundDay);
        (SiegeDays, SiegeTries, SiegeBombards) = (ai.SiegeDays, ai.SiegeBombardTries, ai.SiegeBombards);
        TrackGaps.AddRange(ai.TrackGaps);
        HuntEstimates.AddRange(ai.HuntEstimates);
        SiegeLaunches.AddRange(ai.SiegeLaunches);
        SiegeGroups = ai.SiegeGroupsFormed;
    }
    readonly Dictionary<Mission, Attempt> _missions = [];
    readonly Dictionary<uint, bool> _captured = [];
    HashSet<Fleet> _atHq = [], _atCoruscant = [];
    StarSystem? _hqAt;
    int _battles, _strikes, _assaults;

    /// <summary>
    /// Called after every clock step: fleets of the wanting side at the HQ system and Coruscant (a fleet that arrives, fights and
    /// leaves or dies within one day still counts).
    /// </summary>
    public void Tick(GameState game)
    {
        if (_hqAt is not null) _atHq = Arrivals(Hq, _hqAt, Owner.Empire, _atHq);
        _cor ??= game.Systems.FirstOrDefault(s => s.Type == Refs.Coruscant);
        if (_cor is not null) _atCoruscant = Arrivals(Coruscant, _cor, Owner.Alliance, _atCoruscant);
    }

    StarSystem? _cor;

    static HashSet<Fleet> Arrivals(Target t, StarSystem s, Owner by, HashSet<Fleet> before)
    {
        var now = s.Contents.OfType<Fleet>().Where(f => f.Owner == by && f.Existing).ToHashSet();
        t.Arrivals += now.Count(f => !before.Contains(f));
        return now;
    }

    /// <summary>Called once at the end of every game day, after <see cref="BombardRecorder.Day"/>.</summary>
    public void Day(GameState game, IReadOnlyList<OwnerChange> changes, BombardRecorder bombard)
    {
        Mode = game.Settings.VictoryFlag;
        foreach (var m in game.Missions.Where(m => !_missions.ContainsKey(m) && m.Objective is Character c && Characters.Contains(c.Ref)))
            Attempts.Add(_missions[m] = new(m.Owner, m.Objective!.Ref, m.Kind, game.Day)
            {
                CombatAtOrder = Missions.Enhanced(game, (Character)m.Objective, SkillIndex.Combat),
                InSystemAtOrder = m.Objective.Container is StarSystem,
                TravelDays = m.TargetLocation?.System is { } to && m.Team.Count > 0
                    ? m.Team.Max(a => m.Origin == to ? 0 : m.Origin is StarSystem from ? Orders.TravelDays(game, from, to, Orders.Speed(game, a)) : 1) : -1,
            });
        foreach (var (m, a) in _missions.Where(x => x.Value.Result == "running"))
        {
            if (m.TaskStatus == 3) a.Succeeded = true;
            if (m.CompletionStatus != 0 || m.Deleted)
            {
                a.Result = a.Succeeded ? "succeeded" : m.CompletionStatus is 3 or 4 ? "foiled" : "failed";
                (a.EndDay, a.Task, a.Completion) = (game.Day, m.TaskStatus, m.CompletionStatus);
                if (game.Characters.FirstOrDefault(x => x.Ref == a.Target) is { } c)
                {
                    a.CombatAtEnd = Missions.Enhanced(game, c, SkillIndex.Combat);
                    a.InSystemAtEnd = m.TargetLocation?.System is { } s && c.System == s;
                }
            }
        }
        var chars = Characters.ToDictionary(r => r, r => game.Characters.FirstOrDefault(c => c.Ref == r));
        foreach (var (r, c) in chars)
        {
            if (c is null) continue;
            var p = People[r];
            bool was = _captured.GetValueOrDefault(r);
            if (c.Captured && !was) { p.Captures++; p.FirstCaptured ??= game.Day; }
            if (!c.Captured && was)
            {
                if (_missions.Any(x => x.Key.Kind == 0x61 && x.Value.Target == r && x.Value.Side == c.Owner && x.Key.TaskStatus == 3)) p.Rescued++;
                else p.Escaped++;
            }
            _captured[r] = c.Captured;
            p.CapturedAtEnd = c.Captured;
            string where = c.Destroyed ? "dead" : c.Captured ? "captured"
                : game.Missions.FirstOrDefault(m => !m.Deleted && m.CompletionStatus == 0 && (m.Team.Contains(c) || m.Decoys.Contains(c))) is { } on
                    ? on.Kind is (>= 0x41 and <= 0x44) or 0x72 or 0x73 ? "moving" : "mission" : Place(c);
            p.Days[where] = p.Days.GetValueOrDefault(where) + 1;
        }
        var newBattles = (game.BattleTrace ?? []).Skip(_battles).ToList();
        var newStrikes = bombard.Strikes.Skip(_strikes).ToList();
        var newAssaults = bombard.Assaults.Skip(_assaults).ToList();
        (_battles, _strikes, _assaults) = (game.BattleTrace?.Count ?? 0, bombard.Strikes.Count, bombard.Assaults.Count);
        // the Alliance HQ, wanted by the Empire
        var hq = game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key);
        bool hqGone = hq is not { Destroyed: false };
        if (hq is not null && hq.Destroyed) Hq.DestroyedDay ??= game.Day;
        var hqAt = hq is { Destroyed: false } ? hq.System : null;
        if (hqAt is not null && Knowledge.Location(game, hq!, Owner.Empire) == hqAt) HqKnownDays++; // the Empire side's view knows the HQ system
        if (hqAt is not null)
        {
            if (_hqAt is not null && hqAt != _hqAt) Hq.Relocations++;
            _hqAt = hqAt;
            if (Hq.KnownDay is null && Knowledge.Location(game, hq!, Owner.Empire) == hqAt) Hq.KnownDay = game.Day;
            Events(Hq, hqAt, Owner.Empire, newBattles, newStrikes, newAssaults, changes, game.Day);
        }
        // Coruscant, wanted by the Alliance
        var cor = game.Systems.FirstOrDefault(s => s.Type == Refs.Coruscant);
        if (cor is not null)
        {
            if (cor.Destroyed) Coruscant.DestroyedDay ??= game.Day;
            Events(Coruscant, cor, Owner.Alliance, newBattles, newStrikes, newAssaults, changes, game.Day);
            Coruscant.OwnerChanges += changes.Count(c => c.Day == game.Day && c.System == cor.Ref);
        }
        bool C(uint r) => chars[r]?.Captured ?? false;
        EmpireMet = (C(Refs.Luke) ? 1 : 0) + (C(0x30000240) ? 1 : 0) + (hqGone ? 1 : 0);
        AllianceMet = (C(Refs.Vader) ? 1 : 0) + (C(Refs.Palpatine) ? 1 : 0) + (cor is not null && (cor.Owner == Owner.Alliance || cor.Destroyed) ? 1 : 0);
        if (game.GameOver && VictoryDay is null) (VictoryDay, Winner) = (game.Day, game.Winner);
    }

    static void Events(Target t, StarSystem s, Owner by, List<BattleReport> battles, List<BombardRecorder.Strike> strikes,
        List<BombardRecorder.AssaultStrike> assaults, IReadOnlyList<OwnerChange> changes, int day)
    {
        t.Battles += battles.Count(b => b.System == s);
        t.Bombardments += strikes.Count(b => b.System == s.Ref && b.Attacker == by);
        var landings = assaults.Where(a => a.System == s.Ref && a.Attacker == by).ToList();
        t.Landings += landings.Count;
        t.LandingsWon += landings.Count(a => changes.Any(c => c.Day == a.Day && c.System == s.Ref && c.To == by && c.Cause == ChangeCause.Troops));
    }

    /// <summary>Where a free character sits: on a mission, in a fleet, or in a system by its owner relative to the character's side.</summary>
    static string Place(Character c)
    {
        for (GameObject? x = c.Container; x is not null; x = x.Container)
            if (x is Fleet) return "fleet";
        return c.System is not { } s ? "no system (not placed / abode / in transit)" : s.Owner == c.Owner ? "own system" : s.Owner is Owner.Alliance or Owner.Empire ? "enemy system" : "neutral system";
    }

    public static string Table(IReadOnlyList<(GameResult Result, ObjectivesRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        string P(int n, int of) => of == 0 ? "-" : (100.0 * n / of).ToString("F0", CultureInfo.InvariantCulture) + "%";
        string Who(GameResult r, Owner side) => side == r.OpenSWR ? "openswr" : "original";
        var modes = runs.GroupBy(x => x.Recorder.Mode).OrderBy(g => g.Key).Select(g => $"{(g.Key == 0 ? "standard (0)" : g.Key == 1 ? "headquarters (1)" : $"{g.Key}")} {g.Count()} games");
        text.AppendLine($"victory objectives (measurement; aimatch victory mode: {string.Join(", ", modes)})");
        foreach (var (wanting, who) in new[] { (Owner.Empire, "openswr"), (Owner.Empire, "original"), (Owner.Alliance, "openswr"), (Owner.Alliance, "original") })
        {
            var g = runs.Where(x => Who(x.Result, wanting) == who).ToList();
            int n = Math.Max(1, g.Count);
            text.AppendLine($"  {who} as {wanting} ({g.Count} games): {(wanting == Owner.Empire ? "Luke, Mon Mothma captured and the Alliance HQ gone" : "Vader, Palpatine captured and Coruscant Alliance or destroyed")}");
            foreach (uint r in wanting == Owner.Empire ? new[] { Refs.Luke, 0x30000240u } : new[] { Refs.Vader, Refs.Palpatine })
            {
                var ps = g.Select(x => x.Recorder.People[r]).ToList();
                var cap = ps.Where(p => p.FirstCaptured is not null).ToList();
                text.AppendLine($"    {Name(r),-10} captured in {P(cap.Count, g.Count)} of games (first day {(cap.Count == 0 ? "-" : F(cap.Average(p => p.FirstCaptured!.Value)))}), captures {F((double)ps.Sum(p => p.Captures) / n)} per game, held at the end {P(ps.Count(p => p.CapturedAtEnd), g.Count)}, rescued {F((double)ps.Sum(p => p.Rescued) / n)}, escaped or freed otherwise {F((double)ps.Sum(p => p.Escaped) / n)}");
            }
            var ts = g.Select(x => wanting == Owner.Empire ? x.Recorder.Hq : x.Recorder.Coruscant).ToList();
            string label = wanting == Owner.Empire ? "Alliance HQ" : "Coruscant";
            var known = ts.Where(t => t.KnownDay is not null).ToList();
            var gone = ts.Where(t => t.DestroyedDay is not null).ToList();
            text.AppendLine($"    {label,-10} " + (wanting == Owner.Empire ? $"system known to the Empire view in {P(known.Count, g.Count)} of games (first day {(known.Count == 0 ? "-" : F(known.Average(t => t.KnownDay!.Value)))}), relocations {F((double)ts.Sum(t => t.Relocations) / n)}; " : $"owner changes {F((double)ts.Sum(t => t.OwnerChanges) / n)}; ")
                + $"{wanting} fleets arriving {F((double)ts.Sum(t => t.Arrivals) / n)}, battles {F((double)ts.Sum(t => t.Battles) / n)}, bombardments {F((double)ts.Sum(t => t.Bombardments) / n)}, troop landings {F((double)ts.Sum(t => t.Landings) / n)} (won {ts.Sum(t => t.LandingsWon)}), destroyed in {P(gone.Count, g.Count)} of games");
            var met = g.Select(x => wanting == Owner.Empire ? x.Recorder.EmpireMet : x.Recorder.AllianceMet).ToList();
            text.AppendLine($"    conditions met at the last day: " + string.Join(", ", Enumerable.Range(0, 4).Select(k => $"{k} of 3 {P(met.Count(m => m == k), g.Count)}")));
            var won = g.Where(x => x.Recorder.Winner == wanting).ToList();
            text.AppendLine($"    victories {won.Count}" + (won.Count == 0 ? "" : $" (days {string.Join(" ", won.Select(x => x.Recorder.VictoryDay).OrderBy(d => d))})"));
        }
        // the openswr side's hunt (step 21 Hunt; zero without the flag)
        var gaps = runs.SelectMany(x => x.Recorder.TrackGaps).ToList();
        text.AppendLine($"  openswr hunt: espionage sent {F((double)runs.Sum(x => x.Recorder.HuntSpies) / Math.Max(1, runs.Count))} per game; track regained {gaps.Count} times, days from losing track to finding again {(gaps.Count == 0 ? "-" : F(gaps.Average(g => g.Days)))}"
            + (gaps.Count == 0 ? "" : " (" + string.Join(", ", gaps.GroupBy(g => g.Character).OrderBy(g => Array.IndexOf(Characters, g.Key)).Select(g => $"{Name(g.Key)} {g.Count()} × {F(g.Average(x => x.Days))}")) + ")"));
        var empire = runs.Where(x => x.Result.OpenSWR == Owner.Empire).ToList();
        var found = empire.Where(x => x.Recorder.HqFound is not null).ToList();
        text.AppendLine($"  openswr HQ search (as Empire, {empire.Count} games): HQ system found in {P(found.Count, empire.Count)} of games (first day {(found.Count == 0 ? "-" : F(found.Average(x => x.Recorder.HqFound!.Value)))}), candidates left at the end {(empire.Count == 0 ? "-" : F(empire.Average(x => x.Recorder.HqLeft)))}, relocations seen {(empire.Count == 0 ? "-" : F(empire.Average(x => x.Recorder.HqRelocations)))} per game");
        double G(Func<ObjectivesRecorder, int> f) => (double)runs.Sum(x => f(x.Recorder)) / Math.Max(1, runs.Count);
        text.AppendLine($"  openswr siege: decisions with a siege target {F(G(x => x.SiegeDays))} per game, bombardments tried {F(G(x => x.SiegeTries))}, issued {F(G(x => x.SiegeBombards))}, siege groups formed {F(G(x => x.SiegeGroups))}");
        // step 22: the HQ siege (openswr as Empire): days known, launches with strength against the known defence
        var asEmpire = runs.Where(x => x.Result.OpenSWR == Owner.Empire).ToList();
        var launches = asEmpire.SelectMany(x => x.Recorder.SiegeLaunches).ToList();
        int en = Math.Max(1, asEmpire.Count);
        text.AppendLine($"  openswr HQ siege (as Empire, {asEmpire.Count} games): days the HQ system was known {F((double)asEmpire.Sum(x => x.Recorder.HqKnownDays) / en)} per game, siege groups formed {F((double)asEmpire.Sum(x => x.Recorder.SiegeGroups) / en)}, launches {F((double)launches.Count / en)}"
            + (launches.Count == 0 ? "" : $" (combined strength {F(launches.Average(l => l.Strength))} against known defence {F(launches.Average(l => l.Defence))})")
            + $", bombardments {F((double)asEmpire.Sum(x => x.Recorder.Hq.Bombardments) / en)}, landings {F((double)asEmpire.Sum(x => x.Recorder.Hq.Landings) / en)} (won {asEmpire.Sum(x => x.Recorder.Hq.LandingsWon)}), HQ destroyed in {P(asEmpire.Count(x => x.Recorder.Hq.DestroyedDay is not null), asEmpire.Count)} of games"
            + (asEmpire.Any(x => x.Recorder.Hq.DestroyedDay is not null) ? $" (days {string.Join(" ", asEmpire.Where(x => x.Recorder.Hq.DestroyedDay is not null).Select(x => x.Recorder.Hq.DestroyedDay).OrderBy(d => d))})" : ""));
        // step 22: objective abductions, estimate at the order against the outcome
        var hunted = new List<(double Estimate, string Result)>();
        foreach (var (r, rec) in runs)
            foreach (var e in rec.HuntEstimates)
                if (rec.Attempts.FirstOrDefault(a => a.Side == r.OpenSWR && a.Kind == 0x62 && a.Target == e.Target && a.Day >= e.Day && a.Day <= e.Day + 1) is { } a)
                    hunted.Add((e.Estimate, a.Result));
        text.AppendLine($"  openswr objective abductions, estimate at the order against the outcome ({hunted.Count} matched of {runs.Sum(x => x.Recorder.HuntEstimates.Count)} sent):");
        foreach (var (label, lo, hi) in new[] { ("0.25-0.40", 0.25, 0.40), ("0.40-0.60", 0.40, 0.60), ("0.60-0.80", 0.60, 0.80), (">=0.80", 0.80, 9.0), ("<0.25", -1.0, 0.25) })
        {
            var b = hunted.Where(h => h.Estimate >= lo && h.Estimate < hi).ToList();
            if (b.Count == 0) continue;
            int ended = b.Count(h => h.Result != "running");
            text.AppendLine($"    {label,-10} {b.Count,5}  mean estimate {F(b.Average(h => h.Estimate))}, succeeded {P(b.Count(h => h.Result == "succeeded"), ended)}, foiled {P(b.Count(h => h.Result == "foiled"), ended)}, failed {P(b.Count(h => h.Result == "failed"), ended)} (ended {ended})");
        }
        text.AppendLine("  missions against the four characters (per game; ended: succeeded / foiled / failed)");
        var attempts = runs.SelectMany(x => x.Recorder.Attempts.Select(a => (Who: Who(x.Result, a.Side), a))).ToList();
        foreach (var grp in attempts.GroupBy(x => (x.Who, x.a.Target, Kind: x.a.Kind is 0x62 or 0x63 ? Match.MissionName(x.a.Kind) : "other")).OrderBy(x => x.Key.Who).ThenBy(x => Array.IndexOf(Characters, x.Key.Target)).ThenBy(x => x.Key.Kind))
        {
            var l = grp.Select(x => x.a).ToList();
            text.AppendLine($"    {grp.Key.Who,-8} {Name(grp.Key.Target),-10} {grp.Key.Kind,-13} {F((double)l.Count / Math.Max(1, runs.Count)),6}  {l.Count(a => a.Result == "succeeded")} / {l.Count(a => a.Result == "foiled")} / {l.Count(a => a.Result == "failed")}");
        }
        if (attempts.Count == 0) text.AppendLine("    none");
        // step 23b: abductions against the four characters by ending; Hunt orders matched as in the estimate table above
        var hunt = new Dictionary<Attempt, (int AssumedCombat, bool Seen)>();
        foreach (var (r, rec) in runs)
            foreach (var e in rec.HuntEstimates)
                if (rec.Attempts.FirstOrDefault(a => a.Side == r.OpenSWR && a.Kind == 0x62 && a.Target == e.Target && a.Day >= e.Day && a.Day <= e.Day + 1) is { } a)
                    hunt.TryAdd(a, (e.AssumedCombat, e.Seen));
        text.AppendLine("  abductions against the four characters by ending (step 23b; ended: success / attempt lost (TaskStatus 2) / foiled (CompletionStatus 3, 4) / target gone (6) / other (TaskStatus/CompletionStatus × count); step 24: split by the target directly in a system at the order (truth), travel days order to target system)");
        var abd = attempts.Where(x => x.a.Kind == 0x62).ToList();
        foreach (var grp in abd.GroupBy(x => (x.Who, x.a.Target, Still: x.a.InSystemAtOrder)).OrderBy(x => x.Key.Who).ThenBy(x => Array.IndexOf(Characters, x.Key.Target)).ThenByDescending(x => x.Key.Still))
        {
            var ended = grp.Select(x => x.a).Where(a => a.Result != "running").ToList();
            string End(Attempt a) => a.Succeeded ? "success" : a.Completion is 3 or 4 ? "foiled" : a.Task == 2 ? "lost" : a.Completion == 6 ? "gone" : "other";
            int N(string k) => ended.Count(a => End(a) == k);
            var other = ended.Where(a => End(a) == "other").GroupBy(a => $"{a.Task}/{a.Completion}").OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} × {g.Count()}");
            var h = ended.Where(hunt.ContainsKey).ToList();
            string M(IEnumerable<Attempt> l, Func<Attempt, double> f) => l.Any() ? F(l.Average(f)) : "-";
            text.AppendLine($"    {grp.Key.Who,-8} {Name(grp.Key.Target),-10} {(grp.Key.Still ? "in system" : "elsewhere"),-9} {grp.Count(),4} sent, {ended.Count,4} ended: {N("success")} / {N("lost")} / {N("foiled")} / {N("gone")} / {N("other")}"
                + (N("other") == 0 ? "" : $" ({string.Join(", ", other)})")
                + $"; in the target system at the end {ended.Count(a => a.InSystemAtEnd)}; days order to end {M(ended, a => a.EndDay - a.Day)}; travel days {M(grp.Select(x => x.a).Where(a => a.TravelDays >= 0), a => a.TravelDays)}; EnhCombat true at the order {M(ended, a => a.CombatAtOrder)}, at the end {M(ended, a => a.CombatAtEnd)}"
                + (h.Count == 0 ? "" : $"; Hunt {h.Count}: assumed {M(h, a => hunt[a].AssumedCombat)} (true at the order {M(h, a => a.CombatAtOrder)}), seen live at the order {h.Count(a => hunt[a].Seen)}"));
        }
        if (abd.Count == 0) text.AppendLine("    none");
        text.AppendLine("  where the four characters sat (share of days, by the side that owns them)");
        foreach (uint r in Characters)
            foreach (var who in new[] { "openswr", "original" })
            {
                // the character's side: Luke and Mon Mothma Alliance, Vader and Palpatine Empire
                var side = r is Refs.Luke or 0x30000240 ? Owner.Alliance : Owner.Empire;
                var days = runs.Where(x => Who(x.Result, side) == who).SelectMany(x => x.Recorder.People[r].Days).GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.Sum(y => y.Value));
                int total = days.Values.Sum();
                text.AppendLine($"    {Name(r),-10} ({who}) " + (total == 0 ? "none" : string.Join(", ", days.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {P(x.Value, total)}"))));
            }
        return text.ToString();
    }
}
