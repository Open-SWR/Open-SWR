using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

public sealed partial class Advisor
{
    // ---- delegation cycle ----

    /// <summary>The advisor model, read by the delegated planners; rebuilt from the views (not saved).</summary>
    public AdvisorModel? Model { get; private set; }
    /// <summary>Delegated planners Production / Garrisons (the build helper Helper runs at its target pick, <see cref="HelperPick"/>).</summary>
    public Dictionary<int, DelegatedPlanner> Planners { get; } = new() { [Production] = new ProductionDelegate(), [Garrisons] = new GarrisonDelegate() };
    /// <summary>Delegated commands of the current day against the budget of 5; reset on every DayDue.</summary>
    public int Issued { get; internal set; }

    /// <summary>Current record, kept across cycles when the budget ran out.</summary>
    public int? Current { get; private set; }

    /// <summary>
    /// Delegation cycle: state 5 the model update, state 6 the think of
    /// every record (0 becomes 1, done := 0), state 4 the issue phase: the pick, then the current record is asked for one command per
    /// frame until it is done (priority := 0 before each ask, so with both toggles on Production and Garrisons take turns); a command is validated, and a refused one goes to the record's failure slot, an accepted one to the human's outbox
    /// (<paramref name="human"/>) and the day budget +1; a budget used up with a current record ends the cycle, keeping it. A done
    /// record goes back to state 0 (unless disabled). shortcut: one call runs the whole cycle (the original runs one state step per
    /// frame, the clock not waiting, Inferred harmless: nothing else reads the model or the records between those frames).
    /// </summary>
    public void Delegate(GameState game, Ai.HumanController human)
    {
        // model totals i = 2 / 3. shortcut: counted straight from the views (the model's sector sums of own / enemy systems),
        // so the full model update still runs only with a record enabled
        var enemy = Side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        (StandingOwn, StandingEnemy) = (0, 0);
        foreach (var s in game.Systems)
        {
            var owner = Knowledge.See(game, s, Side).Owner;
            if (owner == Side) StandingOwn++;
            else if (owner == enemy) StandingEnemy++;
        }
        if (Current is null && Records.Values.All(s => s == Disabled))
        {
            Pick(); // nothing enabled: only the pick's cursors move (the model update draws nothing and nobody reads it)
            return;
        }
        (Model ??= new AdvisorModel(Side)).Rand = Rand;
        Model.Update(game);
        foreach (int k in RecordKeys)
            if (Records[k] == RecordEnabled)
            {
                Records[k] = RecordHasWork;
                if (Planners.TryGetValue(k, out var t)) t.Done = false;
            }
        while (true)
        {
            if (Current is null)
            {
                if ((Current = Pick()) is null) return;
                continue;
            }
            int key = Current.Value;
            if (Issued >= DailyBudget && key != Helper) return;
            var p = Planners[key];
            p.Priority = 0; // priority is zeroed before every ask and is 1 only between a SetProductionTarget and the next ask
            var c = p.Step(game, Model, Records[key]);
            if (c is not null)
            {
                if (Orders.Validate(game, c).Success)
                {
                    human.Append(c);
                    Issued++;
                }
                else p.Failure();
            }
            if (!p.Done) continue;
            if (Records[key] != Disabled) Records[key] = RecordEnabled;
            Current = null;
            return;
        }
    }

    /// <summary>
    /// Record pick: the urgent cursor advances on every call, the state-1
    /// record with the strictly largest priority (unsigned, from 0) from it; else the round-robin cursor advances, then the first
    /// state-1 record cyclically from it.
    /// </summary>
    int? Pick()
    {
        int n = RecordKeys.Length;
        _urgent = (_urgent + 1) % n;
        int? best = null;
        int bestPriority = 0;
        for (int k = 0; k < n; k++)
            if (RecordKeys[(_urgent + k) % n] is var key && Records[key] == RecordHasWork && Planners.TryGetValue(key, out var p) && (uint)p.Priority > (uint)bestPriority)
                (best, bestPriority) = (key, p.Priority);
        if (best is not null) return best;
        _roundRobin = (_roundRobin + 1) % n;
        for (int k = 0; k < n; k++)
            if (Records[RecordKeys[(_roundRobin + k) % n]] == RecordHasWork) return RecordKeys[_roundRobin = (_roundRobin + k) % n];
        return null;
    }

    /// <summary>Stage 13 on the DayDue tick: the delegation budget resets and the mood is recomputed.</summary>
    public void DayDue()
    {
        Issued = 0;
        Mood = MoodByte(StandingOwn, StandingEnemy);
    }


    /// <summary>
    /// Mood recompute: byte := 8; d = the first counter minus the second: >= 2 gives <see cref="AdvisorMood.Ahead"/>, 1 gives
    /// <see cref="AdvisorMood.SlightlyAhead"/>, -1 gives <see cref="AdvisorMood.SlightlyBehind"/>, <= -2 gives <see cref="AdvisorMood.Behind"/>;
    /// still neutral: q = own * 100 div enemy (own * 10 when enemy = 0): >= 300 gives SlightlyAhead, >= 200 gives Strong, <= 50 gives Weak. The counters are never
    /// written (Inferred), so d = 0 and only SlightlyAhead / Strong / Neutral / Weak occur.
    /// </summary>
    public static AdvisorMood MoodByte(int own, int enemy, int d = 0)
    {
        var mood = d >= 2 ? AdvisorMood.Ahead : d == 1 ? AdvisorMood.SlightlyAhead : d == -1 ? AdvisorMood.SlightlyBehind : d <= -2 ? AdvisorMood.Behind : AdvisorMood.Neutral;
        if (mood != AdvisorMood.Neutral) return mood;
        int q = enemy == 0 ? own * MoodShareScaleNoEnemy : own * MoodShareScale / enemy;
        return q >= MoodShareHigh ? AdvisorMood.SlightlyAhead : q >= MoodShareMid ? AdvisorMood.Strong : q <= MoodShareLow ? AdvisorMood.Weak : AdvisorMood.Neutral;
    }

    /// <summary>
    /// Status mask from the human slot (the view message for the victory state word, the
    /// victory state word): only with game over; Empire wins gives C3PO 87 / IMP 84, else Alliance wins
    /// gives C3PO 86 / IMP 85, validity 40000; then the main-screen state word := 1.
    /// </summary>
    public void StatusMask(VictoryStatus word)
    {
        if (!word.GameOver) return;
        if (word.EmpireWins) Request(AdvisorSlot.StatusEmpireWins, Validity.Long);
        else if (word.AllianceWins) Request(AdvisorSlot.StatusAllianceWins, Validity.Long);
        else return;
        GameOverUi = true;
    }

    /// <summary>
    /// End-of-game application state, on the post command (3, x) that ends actions 84-87,
    /// from the victory word and the player side: Empire wins gives Alliance player / Empire player states; else Alliance wins
    /// gives the matching Alliance / Empire player state; else null (nothing happens).
    /// </summary>
    public static AdvisorEndState? EndState(VictoryStatus word, Owner player) =>
        word.EmpireWins ? player == Owner.Alliance ? AdvisorEndState.EmpireWinsPlayerAlliance : AdvisorEndState.EmpireWinsPlayerEmpire
        : word.AllianceWins ? player == Owner.Alliance ? AdvisorEndState.AllianceWinsPlayerAlliance : AdvisorEndState.AllianceWinsPlayerEmpire : (AdvisorEndState?)null;

    /// <summary>End movie of each end state (MDATA.105 to .108), then the main menu; null for no state.</summary>
    public static string? EndMovie(AdvisorEndState? state) => state switch
    {
        AdvisorEndState.AllianceWinsPlayerAlliance => "MDATA.105",
        AdvisorEndState.EmpireWinsPlayerAlliance => "MDATA.106",
        AdvisorEndState.EmpireWinsPlayerEmpire => "MDATA.107",
        AdvisorEndState.AllianceWinsPlayerEmpire => "MDATA.108",
        _ => null,
    };
}
