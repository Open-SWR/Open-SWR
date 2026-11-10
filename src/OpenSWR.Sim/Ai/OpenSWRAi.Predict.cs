using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Battle prediction from known compositions. Space: the
/// automatic battle engine on a sandbox (<see cref="BattleEngine.Predict"/>) with the own real units against the enemy units the
/// side's views show (their damage unknown: none, the pessimistic choice), <see cref="PredictRuns"/> runs from private generators
/// seeded by (game seed, day, system, run). Ground: the assault rule with the
/// known troops and defence facilities, the leaders' Leadership left out (Inferred), the same private generators. Neither touches
/// the game generator, the UI generator or the game state; results are cached per (system, day, forces).
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>Runs per prediction.</summary>
    public const int PredictRuns = 5;

    /// <summary>A space prediction: the own side's win chance and the expected value lost by each side.</summary>
    internal readonly record struct SpacePrediction(double Win, double OwnLost, double EnemyLost);

    readonly Dictionary<(uint System, int Day, int Forces), SpacePrediction> _spaceCache = [];
    readonly Dictionary<(uint System, int Day, int Forces), double> _groundCache = [];
    int _cacheDay = -1;

    /// <summary>Predictions run so far (cache misses; aimatch / tests).</summary>
    public int Predictions { get; private set; }

    static int RunSeed(GameState game, int day, uint system, int forces, int run)
    {
        long h = (uint)game.Settings.Seed;
        foreach (long x in new long[] { day, system, (uint)forces, run }) h = (h * 1_000_003 + x) % 0x7FFFFFFD;
        return (int)h + 1;
    }

    void ClearCaches(int day)
    {
        if (day == _cacheDay) return;
        _cacheDay = day;
        _spaceCache.Clear();
        _groundCache.Clear();
    }

    /// <summary>
    /// Space prediction at <paramref name="s"/>: the own fleets given (real contents: Usable capital ships, their Usable fighters,
    /// their damage) plus the own Usable fighters directly in the system, against the enemy fleets given (view contents: capital
    /// ships, their fighters) plus the enemy fighters the view shows directly in the system.
    /// </summary>
    internal SpacePrediction PredictSpace(GameState game, Known k, StarSystem s, IEnumerable<Fleet> own, IEnumerable<GameObject> enemy)
    {
        ClearCaches(game.Day);
        var ownList = own.Distinct().OrderBy(f => f.Key).ToList();
        var enemyList = enemy.Distinct().OrderBy(f => f.Key).ToList();
        // a fixed hash (HashCode.Combine is seeded per process: predictions would differ between runs)
        int forces = unchecked(ownList.Aggregate(17, (h, f) => h * 31 + (int)f.Ref) * 1_000_003 + enemyList.Aggregate(19, (h, f) => h * 31 + (int)f.Ref));
        if (_spaceCache.TryGetValue((s.Ref, game.Day, forces), out var hit)) return hit;
        Predictions++;
        var sandbox = new StarSystem(s.Definition, s.Sector);
        uint id = 0x7F0000;
        var values = new Dictionary<GameObject, (bool Own, int Value, int Size)>();
        GameObject Copy(GameObject real, Owner owner, GameObject into, bool damage)
        {
            var o = Family.Of(real.Type) is >= 0x08 and < 0x10 ? new Fleet(real.Type, owner, ++id) : new GameObject(real.Type, owner, ++id);
            if (damage)
            {
                (o.HullDamage, o.SquadSizeDamage, o.CriticalHits) = (real.HullDamage, real.SquadSizeDamage, real.CriticalHits);
            }
            into.Add(o);
            bool fighter = Family.Of(real.Type) is >= 0x1C and < 0x20;
            if (fighter || Family.IsCapitalShip(real.Type))
                values[o] = (owner == Side, k.Value(real.Type), fighter ? 0 : (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == real.Type)?.Hull ?? 0));
            return o;
        }
        foreach (var f in ownList)
        {
            var fleet = Copy(f, Side, sandbox, false);
            foreach (var ship in f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable && Family.Of(x.Type) < 0x18))
            {
                var copy = Copy(ship, Side, fleet, true);
                foreach (var fighter in ship.Contents.Where(y => Family.Of(y.Type) is >= 0x1C and < 0x20 && y.Usable)) Copy(fighter, Side, copy, true);
            }
        }
        foreach (var fighter in s.Contents.Where(y => Family.Of(y.Type) is >= 0x1C and < 0x20 && y.Usable && y.Owner == Side)) Copy(fighter, Side, sandbox, true);
        foreach (var f in enemyList)
        {
            var fleet = Copy(f, Enemy, sandbox, false);
            foreach (var ship in k.Contents(f).Where(v => Family.IsCapitalShip(v.Object.Type) && Family.Of(v.Object.Type) < 0x18))
            {
                var copy = Copy(ship.Object, Enemy, fleet, false);
                foreach (var fighter in k.Contents(ship.Object).Where(v => v.In(0x1C, 0x20))) Copy(fighter.Object, Enemy, copy, false);
            }
        }
        foreach (var fighter in k.Contents(s).Where(v => v.Owner == Enemy && v.In(0x1C, 0x20))) Copy(fighter.Object, Enemy, sandbox, false);

        double wins = 0, ownLost = 0, enemyLost = 0;
        bool anyOwn = values.Values.Any(v => v.Own), anyEnemy = values.Values.Any(v => !v.Own);
        if (!anyEnemy) (wins, ownLost, enemyLost) = (PredictRuns, 0, 0);
        else if (!anyOwn) (wins, ownLost, enemyLost) = (0, 0, 0);
        else
            for (int run = 0; run < PredictRuns; run++)
            {
                var r = BattleEngine.Predict(game, sandbox, RunSeed(game, game.Day, s.Ref, forces, run));
                // S[1]: side 2 (Empire) has no active unit; S[3]: side 1 (Alliance) has none
                bool won = Side == Owner.Alliance ? r.S[1] != 0 : r.S[3] != 0;
                if (won) wins++;
                foreach (var (o, (isOwn, value, size)) in values)
                {
                    double lost = r.Ships.TryGetValue(o, out var sr) ? (size <= 0 ? (sr.Hull <= 0 ? 1 : 0) : Math.Clamp(1 - (double)sr.Hull / size, 0, 1))
                        : r.Fighters.TryGetValue(o, out var fr) ? (fr.Left <= 0 ? 1 : 0) : 0;
                    if (isOwn) ownLost += lost * value;
                    else enemyLost += lost * value;
                }
            }
        var p = new SpacePrediction(wins / PredictRuns, ownLost / PredictRuns, enemyLost / PredictRuns);
        _spaceCache[(s.Ref, game.Day, forces)] = p;
        return p;
    }

    /// <summary>
    /// Ground prediction: the attacker's win chance in an assault of <paramref name="attackers"/> (troop types) against
    /// <paramref name="defenders"/> (troop types) and <paramref name="facilities"/> (defence facility types [0x22, 0x28)) at
    /// <paramref name="s"/>.
    /// </summary>
    internal double PredictGround(GameState game, StarSystem s, IReadOnlyList<uint> attackers, IReadOnlyList<uint> defenders, IReadOnlyList<uint> facilities)
    {
        ClearCaches(game.Day);
        if (attackers.Count == 0) return 0;
        if (defenders.Count == 0 && facilities.Count == 0) return 1;
        int forces = unchecked((attackers.Aggregate(23, (h, t) => h * 31 + (int)t) * 1_000_003 + defenders.Aggregate(29, (h, t) => h * 31 + (int)t)) * 1_000_003
            + facilities.Aggregate(37, (h, t) => h * 31 + (int)t));
        if (_groundCache.TryGetValue((s.Ref, game.Day, forces), out double hit)) return hit;
        Predictions++;
        var g = game.Params;
        int Sd(uint type, SdField field) => game.Definitions.Resolve(type) is { } r ? r.GetOrZero(field) : 0;
        int wins = 0;
        for (int run = 0; run < PredictRuns; run++)
        {
            var rand = new GameRandom(RunSeed(game, game.Day, s.Ref, forces, 100 + run));
            var a = attackers.ToList();
            var d = defenders.ToList();
            int a0 = a.Count, d0 = d.Count;
            foreach (uint f in facilities)
            {
                if (a.Count == 0) break;
                if (!rand.Chance(Sd(f, SdField.DefenseAttackStrength) / Math.Max(1, g.General(0x200)))) continue;
                int i = rand.Roll(a0 - 1);
                if (i >= 0 && i < a.Count) a.RemoveAt(i);
            }
            if (a.Count > 0 && d.Count > 0)
                foreach (uint t in a.ToList())
                {
                    int r = rand.Roll(d0 - 1);
                    if (r >= d.Count) continue;
                    int v = Combat.Duel(game, rand.Roll(g.General(0x202)), Sd(t, SdField.TroopAttackStrength), 0, Sd(d[r], SdField.TroopDefenseStrength), 0);
                    if (v > 0) d.RemoveAt(r);
                    else if (v < 0) a.Remove(t);
                    if (a.Count == 0 || d.Count == 0) break;
                }
            if (a.Count > 0 && d.Count == 0) wins++;
        }
        double p = (double)wins / PredictRuns;
        _groundCache[(s.Ref, game.Day, forces)] = p;
        return p;
    }

    /// <summary>The enemy's standard troop type (an incoming troop capacity of unknown troops is assumed to be these; Inferred).</summary>
    uint EnemyArmy => Enemy == Owner.Alliance ? Refs.AllianceArmy : Refs.EmpireArmy;
}
