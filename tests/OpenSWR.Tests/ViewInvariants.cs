using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>
/// Invariants the original keeps by construction, checked between ticks (test only):
/// no non-live view entry is parented to a deleted object and no character / special-force view holds a deleted mission; every
/// mission's lock counter equals the views holding it plus the missions whose objective it is; no lock decrement hits the floor.
/// A live view's entry is skipped: it is not original state .
/// </summary>
sealed class ViewInvariants
{
    static readonly Owner[] Sides = [Owner.Alliance, Owner.Empire];
    readonly GameState _game;
    List<GameObject> _known;
    readonly List<GameObject> _floored = [];

    public ViewInvariants(GameState game)
    {
        _game = game;
        _known = [.. game.Created];
        game.LockFloor = o => _floored.Add(o);
    }

    /// <summary>The mission side's views holding a RoleMissionKey: live -> the real key, frozen -> the entry.</summary>
    static IEnumerable<(Agent Agent, Owner Side, Mission Mission)> Held(GameState game) =>
        from a in game.Created.OfType<Agent>().Concat(game.Characters).Distinct()
        from side in Sides
        let m = Knowledge.IsLive(a, side) ? a.Mission : game.Views.TryGetValue((a, side), out var v) ? v.RoleMission : null
        where m is not null
        select (a, side, m);

    /// <summary>Checks the invariants after a tick; returns the breaks (empty when all hold).</summary>
    public List<string> Check(string where)
    {
        var errors = new List<string>();
        var deleted = _known.Where(o => o.Deleted).ToHashSet();
        _known = [.. _game.Created];
        if (deleted.Count > 0)
        {
            foreach (var ((x, side), v) in _game.Views)
                if (!Knowledge.IsLive(x, side) && v.Parent is { } p && deleted.Contains(p))
                    errors.Add($"{where}: {side} view of {x.Ref:X8} parented to deleted {p.Ref:X8}");
            foreach (var (a, side, m) in Held(_game))
                if (deleted.Contains(m)) errors.Add($"{where}: {side} view of {a.Ref:X8} holds deleted mission {m.Ref:X8}");
        }
        var count = Held(_game).GroupBy(h => h.Mission).ToDictionary(g => g.Key, g => g.Count());
        foreach (var m in _game.Missions)
            if (m.Objective is Mission om) count[om] = count.GetValueOrDefault(om) + 1;
        foreach (var m in _game.Missions)
            if (m.LockCount != count.GetValueOrDefault(m))
                errors.Add($"{where}: mission {m.Ref:X8} lock {m.LockCount}, views {count.GetValueOrDefault(m)}");
        foreach (var o in _floored) errors.Add($"{where}: lock of {o.Ref:X8} decremented at 0");
        _floored.Clear();
        return errors;
    }
}
