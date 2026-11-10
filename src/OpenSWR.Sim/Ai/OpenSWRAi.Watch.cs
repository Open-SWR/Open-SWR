using OpenSWR.Data.Dat;
using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Watch missions: espionage on the own high-value border
/// systems shows enemy fleets Enroute to them; each
/// system with an incoming force is held (reinforced) or evacuated.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>Days between two watches of one system.</summary>
    public const int WatchDays = 15;

    /// <summary>Systems watched: the top this many high-value border systems by priority.</summary>
    public const int WatchTop = 5;

    /// <summary>Watch missions running at once (they also count as scouting for <see cref="MaxScouts"/>).</summary>
    public const int MaxWatches = 2;

    /// <summary>Hold when the defence reaches this many times the incoming force.</summary>
    public const double HoldFactor = 1.2;

    /// <summary>Per system ref, the decision day of its last watch start (saved as "watched").</summary>
    readonly SortedDictionary<uint, int> _watched = [];

    /// <summary>An incoming force at an own system: the enemy fleets seen Enroute there, their force, the estimated days to arrival.</summary>
    internal sealed record Incoming(StarSystem System, List<GameObject> Fleets, int Force, int Troops, int Days, int Defence, bool Hold);

    /// <summary>The incoming forces of the current decision (<see cref="RefreshIncoming"/>); not saved.</summary>
    internal Dictionary<StarSystem, Incoming> Incomings { get; private set; } = [];

    /// <summary>
    /// The incoming forces: every own system whose view shows enemy fleets Enroute there. Force = Known.Strength of those fleets +
    /// their troop capacity (view contents). Days to arrival: the views keep no arrival day, so the travel days (speed 100) from the
    /// nearest system seen as the enemy's (Inferred: the fleet's origin is unknown; at least 1). Defence = the side's known strength
    /// there (garrison, fleets, defence facilities) + the idle own fleets elsewhere that arrive in fewer days (reported). Hold:
    /// high value and a space win chance >= 0.6 with the reinforcements in time, or a ground defence win chance >= 0.6
    /// against the incoming troop capacity; otherwise evacuate.
    /// </summary>
    internal void RefreshIncoming(GameState game, Known k)
    {
        var result = new Dictionary<StarSystem, Incoming>();
        var enemies = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        var used = new HashSet<GameObject>();
        foreach (var s in Own(game))
        {
            var fleets = k.Contents(s).Where(v => v.Object is Fleet && v.Owner == Enemy && v.Enroute).Select(v => v.Object).ToList();
            if (fleets.Count == 0) continue;
            int troops = fleets.Sum(f => k.Contents(f).Where(v => Family.IsCapitalShip(v.Object.Type)).Sum(v => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == v.Object.Type)?.TroopCapacity ?? 0)));
            int force = fleets.Sum(f => k.Strength(f, Enemy)) + troops;
            int days = Math.Max(1, enemies.Select(e => Orders.TravelDays(game, e, s, 100)).DefaultIfEmpty(1).Min());
            int defence = k.Strength(s, Side) + Fleets(game).Where(f => f.Container is StarSystem x && x != s && Idle(game, f, used)
                && Orders.TravelDays(game, x, s, Orders.Speed(game, f)) < days).Sum(f => k.Strength(f, Side));
            // predictor: hold when the space win chance with the reinforcements in time is >= 0.6, or the ground defence
            // wins with chance >= 0.6 against a landing of the incoming troop capacity (the enemy's standard troops, Inferred)
            var reinforcements = Fleets(game).Where(f => f.Container is StarSystem x && (x == s || Idle(game, f, used)
                && Orders.TravelDays(game, x, s, Orders.Speed(game, f)) < days)).ToList();
            if (!On(V7.Predict))
            {
                result[s] = new(s, fleets, force, troops, days, defence, HighValue(s) && defence >= HoldFactor * force); // the value sums
                continue;
            }
            double space = PredictSpace(game, k, s, reinforcements, fleets).Win;
            var defenders = s.Contents.Where(t => OwnTroop(t, Side) && t.Usable).Select(t => t.Type).ToList();
            var facilities = s.Contents.Where(f => f.Owner == Side && f.Usable && Family.Of(f.Type) is >= Families.IonCannon and < Families.Shipyard).Select(f => f.Type).ToList();
            double ground = troops == 0 ? 1 : 1 - PredictGround(game, s, [.. Enumerable.Repeat(EnemyArmy, troops)], defenders, facilities);
            result[s] = new(s, fleets, force, troops, days, defence, HighValue(s) && (space >= 0.6 || ground >= 0.6));
        }
        Incomings = result;
    }

    /// <summary>
    /// Hold or evacuate (after rule 1, before rule 2). Hold: the idle own fleets that arrive in time go there, nearest first, until
    /// the own known strength there reaches <see cref="HoldFactor"/> x the force (rule 2 and the garrison targets take the system
    /// first). Evacuate: the idle own fleets there, the free characters there and the troops beyond 1 leave for the nearest own
    /// system without an incoming force or known enemy fleets.
    /// </summary>
    internal void HoldOrEvacuate(GameState game, Known k, HashSet<GameObject> used)
    {
        var own = Own(game);
        foreach (var x in Incomings.Values.OrderByDescending(x => _values.GetValueOrDefault(x.System.Ref)).ThenBy(x => x.System.Key))
        {
            if (!Room) return;
            var s = x.System;
            if (x.Hold)
            {
                int mine = Fleets(game).Where(f => f.Container == s).Sum(f => k.Strength(f, Side));
                foreach (var f in Fleets(game).Where(f => f.Container is StarSystem at && at != s && !Incomings.ContainsKey(at) && Idle(game, f, used)
                             && Orders.TravelDays(game, at, s, Orders.Speed(game, f)) < x.Days)
                             .OrderBy(f => Orders.TravelDays(game, (StarSystem)f.Container!, s, Orders.Speed(game, f))).ThenBy(f => f.Key).ToList())
                {
                    if (k.Strength(s, Side) + mine >= HoldFactor * x.Force || !Room) break;
                    if (Issue(game, Move(Side, f, s), used)) mine += k.Strength(f, Side);
                }
                foreach (var f in Fleets(game).Where(f => f.Container == s)) used.Add(f); // the holders stay
                continue;
            }
            var safe = own.Where(o => o != s && !Incomings.ContainsKey(o) && k.Fleets(o, Enemy).Count == 0).OrderBy(o => Orders.Distance(o, s)).ThenBy(o => o.Key).FirstOrDefault();
            if (safe is null) continue;
            foreach (var f in Fleets(game).Where(f => f.Container == s && Idle(game, f, used)).OrderBy(f => f.Key).ToList())
                if (!Room || !Issue(game, Move(Side, f, safe), used)) break;
            foreach (var c in s.Contents.OfType<Character>().Where(c => Free(c, used)).OrderBy(c => c.Key).ToList())
                if (!Room || !Issue(game, Move(Side, c, safe), used)) break;
            var troops = s.Contents.Where(t => OwnTroop(t, Side) && t.Usable && !t.Enroute && !used.Contains(t)).OrderBy(t => t.Key).Skip(1).ToList();
            foreach (var t in troops)
                if (!Room || !Issue(game, Move(Side, t, safe), used)) break;
        }
    }

    /// <summary>The watch targets: own high-value border systems, top <see cref="WatchTop"/> by priority, not watched within <see cref="WatchDays"/>.</summary>
    internal List<StarSystem> WatchTargets(GameState game, Known k)
    {
        var enemies = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        var places = EnemyPlaces(game, k);
        return [.. Own(game).Where(s => HighValue(s) && enemies.Any(e => Orders.TravelDays(game, s, e, 100) <= ReachDays))
            .Select(s => (s, p: _values.GetValueOrDefault(s.Ref) * (1 + Threat(game, k, s, places))))
            .OrderByDescending(x => x.p).ThenBy(x => x.s.Key).Take(WatchTop).Select(x => x.s)
            .Where(s => !_watched.TryGetValue(s.Ref, out int day) || game.Day - day >= WatchDays)];
    }

    internal void MarkWatched(StarSystem s, int day) => _watched[s.Ref] = day;

    void SaveWatch(JsonObject j)
    {
        if (_watched.Count > 0) j["watched"] = new JsonArray([.. _watched.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
    }

    void LoadWatch(JsonObject j)
    {
        if (j["watched"] is JsonArray a) foreach (var e in a) _watched[(uint)e![0]!] = (int)e[1]!;
    }
}
