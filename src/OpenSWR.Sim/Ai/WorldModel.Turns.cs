using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>
    /// Advance the producer-facility turn: no requests gives the turn := the next turn, the next turn moves on one slot (after the last
    /// the first); otherwise the turn stays when its own slot is requested, else moves on (wrapping) to the next requesting slot; that
    /// request is consumed.
    /// </summary>
    public void AdvanceTurn()
    {
        if (!TurnRequests.Contains(true))
        {
            Turn = NextTurn;
            NextTurn = (NextTurn + 1) % TurnSlots;
            return;
        }
        int t = Turn;
        while (!TurnRequests[t]) t = (t + 1) % TurnSlots; // the current slot is tested first
        Turn = t;
        TurnRequests[t] = false;
    }

    /// <summary>Adds 1 to the constant at <paramref name="offset"/>, then every system record is dirtied.</summary>
    public void RaiseConstant(ModelConstant k)
    {
        Constant(k)++;
        foreach (var r in Systems.Values) DirtySystem(r);
    }

    /// <summary>The system record of a system object (null for anything else).</summary>
    public AiSystem? Record(GameObject? o) => o is StarSystem s ? Systems.GetValueOrDefault(s) : null;

    // ---- model aggregates over the sectors ----
}
