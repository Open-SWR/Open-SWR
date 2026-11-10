namespace OpenSWR.Sim;

public sealed partial class TacticalUi
{
    // ---- selection rendering ----

    /// <summary>
    /// Box of a unit, from the flags the mode and the highlight buttons set each frame: selected (the own-ship mode's ship,
    /// the TF / multi modes' info unit), primary (the first active UI target of the shown group), marked (members of the shown numbered
    /// task force, the shown group's further UI targets, a highlighted side, the enemy-mode unit), else normal. Fighters, the follow
    /// camera's subject and every unit once the battle ends have none.
    /// </summary>
    public TacticalBox Box(TacticalUnit u)
    {
        if (u.Fighter || _b.Ending || _b.Done || Camera.Follow?.Subject == u) return TacticalBox.Hidden;
        if (_mode.IsSelected(u)) return TacticalBox.Selected;
        var g = _mode.Shown;
        var primary = g?.UiTargets.FirstOrDefault(t => t.Active);
        if (u == primary) return TacticalBox.Primary;
        if (g is not null && (SlotOf(g) >= 0 && g.UiMembers.Contains(u) || g.UiTargets.Contains(u))) return TacticalBox.Marked;
        if (Highlight[u.Side] || _mode is EnemyMode e && e.U == u) return TacticalBox.Marked;
        return TacticalBox.Normal;
    }

    /// <summary>Nav marker style: hidden; the shown group's first UI nav point; its further ones and the nav-point mode's point; else by layer.</summary>
    public TacticalNavStyle NavStyle(TacticalNavPoint n)
    {
        if (n.Hidden) return TacticalNavStyle.Hidden;
        var list = _mode.Shown?.UiNav;
        if (list is { Count: > 0 } && list[0] == n) return TacticalNavStyle.First;
        if (list?.Contains(n) == true || _mode is NavMode m && m.N == n) return TacticalNavStyle.Other;
        return TacticalNavStyle.Layer;
    }

    // ---- shared rules ----

    void Voice(int alliance, int empire) => Voices.Add(_b.Local == 0 ? alliance : empire);

    /// <summary>May be escorted: the unit's simulation group is not on mission 1 and its UI mission is not 1. Inferred: no simulation group yet means it may be escorted.</summary>
    static bool Escortable(TacticalUnit u) => u.Group is not { } g || g.Mission != TacticalMissionNumber.Escort && g.UiMission != TacticalMissionNumber.Escort;

    /// <summary>Attackable: active and not in state 6, 7, 8 or 10 (so state 2 or 4).</summary>
    static bool Attackable(TacticalUnit u) => u.State is TacticalUnitState.Active or TacticalUnitState.Recovering;

    /// <summary>T := the ship's unassigned task force, else a new task force for it (it leaves a numbered one).</summary>
    TacticalGroup TaskForceOf(TacticalUnit s) => Unassigned(s) ?? _b.CreateTaskForce(s);

    /// <summary>
    /// Inferred: a destroyed unit stays in the UI member list of its UI group when that group is not its simulation group, until the group is deleted; the
    /// readers that pick or aim from the UI member list skip state 11.
    /// </summary>
    static IEnumerable<TacticalUnit> Alive(IEnumerable<TacticalUnit> units) => units.Where(u => u.State != TacticalUnitState.Destroyed);

    static V3 Centroid(IEnumerable<TacticalUnit> units)
    {
        var list = units.ToList();
        return list.Count == 0 ? V3.Zero : list.Aggregate(V3.Zero, (a, u) => a + u.Position) * (1.0 / list.Count);
    }

    /// <summary>
    /// Task-force mode created from a selection: create from the selection, bind to the slot, add each pending target to the
    /// group's UI target list and each pending nav point to its UI nav list; the info unit is the first selected; the first
    /// pending target, if an own ship, gives mission 1 through the task-force mode's mission handler (with its voice).
    /// </summary>
    Mode FromSelection(IReadOnlyList<TacticalUnit> selection, IReadOnlyList<TacticalNavPoint> navs, IReadOnlyList<TacticalUnit> targets, int slot)
    {
        var g = _b.CreateTaskForce(selection);
        Bind(slot, g);
        foreach (var t in targets) _b.Order(g, TacticalMessage.AddTarget, t);
        foreach (var n in navs) _b.OrderNav(g, TacticalMessage.AddNavPoint, n);
        var mode = new TaskForceMode(this, slot) { InfoAtEnter = selection[0] };
        if (targets.Count > 0 && targets[0].Side == g.Side) mode.Mission(TacticalMissionNumber.Escort);
        return mode;
    }

    /// <summary>The side's capital ships (its ship list, setup order).</summary>
    List<TacticalUnit> Ships(int side) => [.. _b.Units.Where(u => u.Side == side && !u.Fighter && _b.Sides[side].Ships.Contains(u.U))];

    /// <summary>Next / previous of a list after <paramref name="from"/> (wraps; not in the list: the first / last, Inferred).</summary>
    static TacticalUnit? Step(List<TacticalUnit> list, TacticalUnit? from, bool next)
    {
        if (list.Count == 0) return null;
        int i = from is null ? -1 : list.IndexOf(from);
        if (i < 0) return next ? list[0] : list[^1];
        return list[(i + (next ? 1 : list.Count - 1)) % list.Count];
    }
}
