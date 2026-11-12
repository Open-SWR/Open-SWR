using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    protected override void Start(GameState game) { }

    internal override void DayDue() => Issued = 0;

    internal override void Think(GameState game)
    {
        if (game.Day == Decided || game.GameOver) return;
        Decided = game.Day;
        Part("setup");
        Deciding(true);
        foreach (var key in _refused.Where(e => e.Value <= game.Day).Select(e => e.Key).ToList()) _refused.Remove(key);
        foreach (var key in _foiled.Where(e => game.Day - e.Value >= CovertRetryDays).Select(e => e.Key).ToList()) _foiled.Remove(key);
        foreach (var key in _foilMarks.Where(e => game.Day - e.Value.Day >= FoilMarkDays).Select(e => e.Key).ToList()) _foilMarks.Remove(key);
        PruneGate(game.Day);
        var k = new Known(game, Side);
        var used = new HashSet<GameObject>();
        RefreshValues(game);
        foreach (var key in _watched.Where(e => game.Day - e.Value >= WatchDays).Select(e => e.Key).ToList()) _watched.Remove(key);
        if (On(V7.Watch)) RefreshIncoming(game, k);
        Need = TroopNeed(game, k);
        Part("garrison");
        _step = "garrison"; // measurement only (TroopMoveLog)
        int deficit = Garrisons(game, used);
        Part("defence");
        _step = "defence";
        if (On(V7.Watch)) HoldOrEvacuate(game, k, used);
        var threatened = Defence(game, k, used);
        WatchThreats(game, k); // measurement (aimatch only; no effect on play)
        _step = "other";
        Part("production");
        var map = Sectors(game);
        Production(game, k, used, deficit, map);
        Part("attack");
        if (On(V7.Bombard)) Bombard(game, k, used); // Bombard: before the landings
        if (RimFirsting) RimDrops(game, k, used); // RimFirst: before the attack, so Swarm leaves the drop fleet alone
        _step = "attack loading";
        Attack(game, k, used, threatened, map);
        _step = "other";
        if (On(V7.Fighters)) Carriers(game, used, threatened);
        if (Hunting) // Hunt: the HQ search set and the track measurement
        {
            UpdateHqSearch(game, k);
            TrackHunt(game, k);
        }
        Part("missions");
        Assign(game, k, map, used);
        if (Probe is not null) ClassifyIdle(game, used); // measurement only
        Part("after");
        OwnerChanges(game, k);
        Arrivals(game);
        Deciding(false);
        Part(null);
    }

    /// <summary>Fight when the predicted win chance is at least 0.5 or the enemy's expected loss is at least 1.5 x the own (0.0 = never withdraw), else withdraw at once.</summary>
    internal override double BattleFloat(GameState game, StarSystem s)
    {
        var k = new Known(game, Side);
        int own = k.Strength(s, Side);
        if (own > 0 || s.Owner == Side) _battle = (game, s); // taking part: the result is read when the battle is done
        // the space predictor; fight at a win chance of 0.5 or more, or when the enemy is expected to lose at least 1.5
        // times the own value lost
        if (!On(V7.Predict) && !On(V7.PredictBattle)) return own >= k.Strength(s, Enemy) ? 0.0 : 10000.0;
        var p = PredictSpace(game, k, s, s.Contents.OfType<Fleet>().Where(f => f.Owner == Side && f.Usable), k.Fleets(s, Enemy));
        return p.Win >= 0.5 || p.EnemyLost >= 1.5 * p.OwnLost ? 0.0 : 10000.0;
    }

    /// <summary>A battle the side took part in has run: a chat line on a win or a loss.</summary>
    internal override void BattleDone()
    {
        if (_battle is not var (game, s)) return;
        _battle = null;
        // the real result, read only for a battle this side fought in: the participant learns it anyway (the battle report)
        if (s.BattleWon == Side) Taunt(game, OpenSWRTaunts.Occasion.BattleWon, s);
        else if (s.BattleWon == Enemy) Taunt(game, OpenSWRTaunts.Occasion.BattleLost, s);
    }

    // ---- chat lines (not a game rule: no command, no draw) ----
}
