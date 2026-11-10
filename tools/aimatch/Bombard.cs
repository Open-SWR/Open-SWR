using OpenSWR.Data.Dat;
using System.Globalization;
using System.Text;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;

namespace OpenSWR.AiMatch;

/// <summary>
/// Measurement only: bombardments of both sides from the sim's strike records
/// (<see cref="GameState.StrikeTrace"/>, set by aimatch when slices are on; UI only, no draw) and the accepted bombard orders
/// (the four bombardment orders) from the clock's executed-command hook. Per bombardment: attacker, target owner, order type, units destroyed by
/// kind, the target owner's troops before and after, and the attacker's next troop landing there within 30 days with its result.
/// Also troops lost in assault strikes, for the bombardment against assault comparison. An observer: draws nothing, writes nothing.
/// </summary>
public sealed class BombardRecorder
{
    public const int Window = 30;

    /// <summary>One executed bombardment.</summary>
    public sealed class Strike
    {
        public int Day;
        public uint System;
        public Owner Attacker, Defender;
        public int Type; // the side's latest accepted bombard order there (0 = none seen)
        public int Troops, Defence, Other, AttackerLost, TroopsBefore, TroopsAfter;
        public Dictionary<uint, int> Facilities { get; } = [];
        public int? LandingDays; // days to the attacker's next troop landing there (null = none within the window)
        public bool LandingWon;
        /// <summary>No defender defence facility (families from IonCannon up to, not including, Shipyard) or fighter directly in the system before the strike.</summary>
        public bool Undefended;
    }

    /// <summary>Troops of the defender destroyed in one assault strike.</summary>
    public sealed record AssaultStrike(int Day, uint System, Owner Attacker, Owner Defender, int Troops);

    public List<Strike> Strikes { get; } = [];
    public List<AssaultStrike> Assaults { get; } = [];
    public Dictionary<(Owner Side, int Type), int> Orders { get; } = [];
    readonly Dictionary<(Owner, uint), int> _lastOrder = [];
    int _done;

    /// <summary>An accepted bombard order (clock hook, mid-tick).</summary>
    public void Order(Owner side, int type, StarSystem s)
    {
        Orders[(side, type)] = Orders.GetValueOrDefault((side, type)) + 1;
        _lastOrder[(side, s.Ref)] = type;
    }

    /// <summary>Called once at the end of every game day.</summary>
    public void Day(GameState game, IReadOnlyList<OwnerChange> changes)
    {
        var trace = game.StrikeTrace;
        if (trace is null) return;
        for (; _done < trace.Count; _done++)
        {
            var r = trace[_done];
            // StrikeStaged may add a unit twice (frozen-view entry): one count per unit
            var lost = r.Destroyed.Select(d => d.Unit).Distinct().ToList();
            int troopsAfter = r.Contents.Count(x => Family.IsTroop(x.Type) && x.Owner == r.Defender);
            int troops = lost.Count(x => Family.IsTroop(x.Type) && x.Owner == r.Defender);
            if (r.Assault)
            {
                Assaults.Add(new(r.Day, r.System.Ref, r.Attacker, r.Defender, troops));
                // a landing closes the open bombardments of its attacker there
                bool won = changes.Any(c => c.Day == r.Day && c.System == r.System.Ref && c.To == r.Attacker && c.Cause == ChangeCause.Troops)
                    || r.System.Owner == r.Attacker;
                foreach (var b in Strikes.Where(b => b.System == r.System.Ref && b.Attacker == r.Attacker && b.LandingDays is null && r.Day - b.Day <= Window))
                    (b.LandingDays, b.LandingWon) = (r.Day - b.Day, won);
                continue;
            }
            var st = new Strike
            {
                Day = r.Day, System = r.System.Ref, Attacker = r.Attacker, Defender = r.Defender,
                Type = _lastOrder.GetValueOrDefault((r.Attacker, r.System.Ref)), Troops = troops, TroopsAfter = troopsAfter, TroopsBefore = troopsAfter + troops,
                Undefended = !r.Contents.Concat(lost).Any(x => x.Owner == r.Defender && Family.Of(x.Type) is (>= Families.IonCannon and < Families.Shipyard) or (>= Families.Fighter and < Families.Headquarters)),
            };
            foreach (var x in lost.Where(x => x.Owner == r.Defender && !Family.IsTroop(x.Type)))
            {
                uint f = Family.Of(x.Type);
                if (f is >= Families.IonCannon and < Families.Shipyard) st.Defence++;
                else if (f is >= Families.Shipyard and < Families.MajorCharacter) st.Facilities[f] = st.Facilities.GetValueOrDefault(f) + 1;
                else st.Other++;
            }
            st.AttackerLost = lost.Count(x => x.Owner == r.Attacker);
            Strikes.Add(st);
        }
        // the trace is matched by day only (Combat.StrikeStaged): earlier days are no longer read
        trace.RemoveAll(r => r.Day < game.Day);
        _done = trace.Count;
    }

    static string Family2(uint f) => f switch
    {
        40 => "shipyard", 41 => "training", 42 => "construction yard", 44 => "mine", 45 => "refinery", _ => $"family {f}",
    };

    static string Kind(int type) => type switch
    {
        544 => "544 military", 545 => "545 production", 546 => "546 all + resources", 547 => "547 all + Death Star", _ => "no order seen",
    };

    public static string Table(IReadOnlyList<(GameResult Result, BombardRecorder Recorder)> runs)
    {
        var text = new StringBuilder();
        int games = Math.Max(1, runs.Count);
        string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);
        string P(int n, int of) => of == 0 ? "-" : (100.0 * n / of).ToString("F0", CultureInfo.InvariantCulture) + "%";
        var all = runs.SelectMany(x => x.Recorder.Strikes.Select(s => (x.Result, s))).ToList();
        text.AppendLine($"bombardments (measurement; per game over {runs.Count} games; strikes from the sim's strike records, orders = accepted 544–547)");
        foreach (var who in new[] { "openswr", "original" })
        {
            bool Mine(GameResult r, Owner o) => who == "openswr" ? o == r.OpenSWR : o != r.OpenSWR;
            var l = all.Where(x => Mine(x.Result, x.s.Attacker)).Select(x => (x.Result, x.s)).ToList();
            var orders = runs.SelectMany(x => x.Recorder.Orders.Where(o => Mine(x.Result, o.Key.Side))).GroupBy(o => o.Key.Type).ToDictionary(g => g.Key, g => g.Sum(o => o.Value));
            text.AppendLine($"  {who} bombards: orders {F((double)orders.Values.Sum() / games)} ({(orders.Count == 0 ? "none" : string.Join(", ", orders.OrderBy(o => o.Key).Select(o => $"{Kind(o.Key)} {F((double)o.Value / games)}")))}), executed {F((double)l.Count / games)}");
            if (l.Count == 0) continue;
            var enemy = l.Where(x => x.s.Defender is Owner.Alliance or Owner.Empire).ToList();
            text.AppendLine($"    target owner: enemy {F((double)enemy.Count / games)}, neutral {F((double)(l.Count - enemy.Count) / games)}");
            text.AppendLine("    by target kind: " + string.Join(", ", l.GroupBy(x => x.s.Type).OrderBy(g => g.Key).Select(g => $"{Kind(g.Key)} {F((double)g.Count() / games)}")));
            var facilities = l.SelectMany(x => x.s.Facilities).GroupBy(x => x.Key).OrderBy(g => g.Key).Select(g => $"{Family2(g.Key)} {F((double)g.Sum(x => x.Value) / l.Count)}").ToList();
            text.AppendLine($"    destroyed per bombardment: troops {F(l.Average(x => x.s.Troops))}, facilities {F(l.Average(x => x.s.Facilities.Values.Sum()))} ({(facilities.Count == 0 ? "none" : string.Join(", ", facilities))}), defence {F(l.Average(x => x.s.Defence))}, other {F(l.Average(x => x.s.Other))}; own units lost {F(l.Average(x => x.s.AttackerLost))}");
            text.AppendLine($"    target troops before {F(l.Average(x => x.s.TroopsBefore))}, after {F(l.Average(x => x.s.TroopsAfter))}; bombardments with a troop killed {P(l.Count(x => x.s.Troops > 0), l.Count)}");
            var parts = new List<string>();
            foreach (var (label, lo, hi) in new[] { ("0-3", 0, 3), ("4-10", 4, 10), ("11-30", 11, 30) })
            {
                var w = l.Where(x => x.s.LandingDays is { } d && d >= lo && d <= hi).ToList();
                parts.Add($"{label} days {P(w.Count, l.Count)} (won {w.Count(x => x.s.LandingWon)} / {w.Count})");
            }
            text.AppendLine($"    own landing there after it: {string.Join(", ", parts)}, none {P(l.Count(x => x.s.LandingDays is null), l.Count)}");
            // Bombard, per game, troops / facilities killed, assaults within 10 days after a bombardment and their outcome
            var soon = l.Where(x => x.s.LandingDays is <= 10).ToList();
            text.AppendLine($"    per game: troops killed {F((double)l.Sum(x => x.s.Troops) / games)}, facilities killed {F((double)l.Sum(x => x.s.Facilities.Values.Sum()) / games)}, defence killed {F((double)l.Sum(x => x.s.Defence) / games)}; "
                + $"bombardments with an own assault within 10 days {F((double)soon.Count / games)} (won {F((double)soon.Count(x => x.s.LandingWon) / games)})");
        }
        // the target side's garrison troops: bombardment against assault
        foreach (var who in new[] { "openswr", "original" })
        {
            bool Mine(GameResult r, Owner o) => who == "openswr" ? o == r.OpenSWR : o is (Owner.Alliance or Owner.Empire) && o != r.OpenSWR;
            int bomb = runs.Sum(x => x.Recorder.Strikes.Where(s => Mine(x.Result, s.Defender)).Sum(s => s.Troops));
            int assault = runs.Sum(x => x.Recorder.Assaults.Where(s => Mine(x.Result, s.Defender)).Sum(s => s.Troops));
            text.AppendLine($"  {who} garrison troops lost per game: to bombardment {F((double)bomb / games)}, in assaults {F((double)assault / games)}");
        }
        // openswr systems lost to troop assault: bombarded by the original in the window before
        int losses = 0, bombarded = 0, removed = 0;
        foreach (var (r, rec) in runs)
            foreach (var c in r.Changes.Where(c => c.From == r.OpenSWR && c.Cause == ChangeCause.Troops))
            {
                losses++;
                var before = rec.Strikes.Where(s => s.System == c.System && s.Attacker != r.OpenSWR && s.Defender == r.OpenSWR && s.Day >= c.Day - Window && s.Day <= c.Day).ToList();
                if (before.Count == 0) continue;
                bombarded++;
                removed += before.Sum(s => s.Troops);
            }
        text.AppendLine($"  openswr systems lost to troop assault: {F((double)losses / games)} per game; bombarded by the original in the {Window} days before {P(bombarded, losses)}; troops removed by it {(bombarded == 0 ? "-" : F((double)removed / bombarded))} per bombarded loss");
        // openswr garrison size at an enemy bombardment
        var against = runs.SelectMany(x => x.Recorder.Strikes.Where(s => s.Defender == x.Result.OpenSWR && s.Attacker != x.Result.OpenSWR)).ToList();
        text.AppendLine($"  openswr garrison at an enemy bombardment ({F((double)against.Count / games)} per game): band, bombardments per game, troops killed per bombardment");
        foreach (var (label, lo, hi) in new[] { ("0", 0, 0), ("1-2", 1, 2), ("3-5", 3, 5), ("6-10", 6, 10), (">10", 11, int.MaxValue) })
        {
            var b = against.Where(s => s.TroopsBefore >= lo && s.TroopsBefore <= hi).ToList();
            text.AppendLine($"    {label,-5} {F((double)b.Count / games),7} {(b.Count == 0 ? "-" : F(b.Average(s => s.Troops))),7}");
        }
        return text.ToString();
    }
}
