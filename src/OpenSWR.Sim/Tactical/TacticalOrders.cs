namespace OpenSWR.Sim;

/// <summary>
/// A nav point of the fixed grid: own key, position, layer
/// (0 below the plane, 1 on it, 2 above), the set 1-4 it belongs to, and hidden (true at the start).
/// </summary>
public sealed class TacticalNavPoint
{
    internal TacticalNavPoint(int key, int set, V3 position, int layer) => (Key, Set, Position, Layer) = (key, set, position, layer);
    /// <summary>Own key (Inferred numbering: 2000000 + creation index; the original's key space is shared with the units).</summary>
    public int Key { get; }
    public int Set { get; }
    public V3 Position { get; }
    public int Layer { get; }
    public bool Hidden { get; internal set; } = true;
}

public sealed partial class TacticalBattle
{
    readonly List<TacticalNavPoint> _navPoints = [];
    /// <summary>The 105 nav points of the grid, in creation order.</summary>
    public IReadOnlyList<TacticalNavPoint> NavPoints => _navPoints;

    /// <summary>
    /// Nav-point grid (called after the placement, W known): four nested cubes, set s = 1...4 with spacing d = s * W * 0.125,
    /// points (i*d, j*d, k*d) for i, j, k in {-1, 0, 1}; set 1 includes the origin (27 points), sets 2-4 skip it (26 each). Layer
    /// = (j mod 3) + 1 with the C remainder: 0 for j = -1, 1 for j = 0, 2 for j = 1. Inferred: loop order i, then j, then k.
    /// </summary>
    void BuildNavGrid()
    {
        for (int s = 1; s <= 4; s++)
        {
            double d = s * Size * 0.125;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    for (int k = -1; k <= 1; k++)
                    {
                        if (s > 1 && i == 0 && j == 0 && k == 0) continue;
                        _navPoints.Add(new TacticalNavPoint(2000000 + _navPoints.Count, s, new V3(i * d, j * d, k * d), j % 3 + 1));
                    }
        }
    }
}

public sealed partial class TacticalGroup
{
    internal readonly List<TacticalUnit> UiMemberList = [], UiTargetList = [];
    internal readonly List<TacticalNavPoint> UiNavList = [];
    /// UI members (the orders write them at once).
    public IReadOnlyList<TacticalUnit> UiMembers => UiMemberList;
    /// UI target list.
    public IReadOnlyList<TacticalUnit> UiTargets => UiTargetList;
    /// UI nav-point list.
    public IReadOnlyList<TacticalNavPoint> UiNav => UiNavList;
    /// UI mission.
    public TacticalMissionNumber UiMission { get; internal set; }
    /// Tactic (1 Surround, 2 Stand Off) = the requested acceleration mode.
    public TacticalTactic Tactic => RequestedMode;
}

public sealed partial class TacticalBattle
{
    /// <summary>The UI controller H.</summary>
    public TacticalUi Ui { get; }

    // ---- task forces ----

    /// <summary>
    /// Create a task force for the unit: the unit leaves its old task force (task-force leave message); a new task force (a line navigator founded on
    /// the unit, modes 2 / 2), registered with the world; the task-force create message (group, unit); the unit's group is set to the new group. The simulation
    /// membership follows when the message runs.
    /// </summary>
    public TacticalGroup CreateTaskForce(TacticalUnit u)
    {
        LeaveTaskForce(u);
        var g = new TacticalGroup(new BattleEngine.Group(false), u.Side, -1); // side membership by the task-force create message (Join)
        _byGroup[g.G] = g;
        _groups.Add(g);
        g.Nav = new Navigator(TacticalFormation.Line, u.Key);
        Post(Stamp, TacticalMessage.TaskForceCreate, () => { if (_groups.Contains(g)) Join(g, u, true); });
        g.UiMemberList.Add(u);
        u.UiGroup = g;
        g.UiLeader = u; // the creating unit leads the group
        return g;
    }

    /// <summary>
    /// UI leave: the unit's UI group is cleared when it is this group; out of the UI member
    /// list; the UI leader handed to the lowest remaining UI member key, else none. Inferred: the UI remove of
    /// the task-force leave paths is this same slot (only the destroy path was verified to use it).
    /// </summary>
    internal static void UiLeave(TacticalGroup g, TacticalUnit u)
    {
        if (u.UiGroupKey == g) u.UiGroup = null;
        g.UiMemberList.Remove(u);
        if (g.UiLeader == u) g.UiLeader = g.UiMemberList.MinBy(m => m.Key);
    }

    /// Create from a list: the first unit creates, every further unit joins (leaves its old task force, then UI add + task-force join message).
    public TacticalGroup CreateTaskForce(IReadOnlyList<TacticalUnit> units)
    {
        var g = CreateTaskForce(units[0]);
        foreach (var u in units.Skip(1)) JoinTaskForce(g, u);
        return g;
    }

    /// Join: leave the old task force (task-force leave message), UI add, the task-force join message (group, unit).
    public void JoinTaskForce(TacticalGroup g, TacticalUnit u)
    {
        LeaveTaskForce(u);
        g.UiMemberList.Add(u);
        u.UiGroup = g;
        Post(Stamp, TacticalMessage.TaskForceJoin, () => { if (_groups.Contains(g)) Join(g, u); });
    }

    /// <summary>
    /// Leave: UI remove and the task-force leave message (group, unit); a group of the local side left without UI members
    /// frees its HUD slot.
    /// </summary>
    public void LeaveTaskForce(TacticalUnit u)
    {
        if (u.UiGroup is not { } old) return;
        UiLeave(old, u);
        Post(Stamp, TacticalMessage.TaskForceLeave, () => { if (u.Group == old) Leave(u); });
        if (old.Side == Local && old.UiMemberList.Count == 0) Ui.Free(old);
    }

    // ---- targets and nav points: the UI list at once, the simulation list by the message ----

    /// <summary>
    /// A target order: the UI list changes at once (add appends, remove drops, replace makes it [target]), then the group
    /// message through the queue changes the simulation list (Inferred list rules: add appends once, the first entry
    /// becomes the current target; removing the current target promotes the next entry).
    /// </summary>
    public bool Order(TacticalGroup g, TacticalMessage kind, TacticalUnit target)
    {
        if (Done || !_groups.Contains(g) || kind is not (TacticalMessage.AddTarget or TacticalMessage.RemoveTarget or TacticalMessage.ReplaceTargets)) return false;
        switch (kind)
        {
            case TacticalMessage.AddTarget: g.UiTargetList.Add(target); break;
            case TacticalMessage.RemoveTarget: g.UiTargetList.Remove(target); break;
            default: g.UiTargetList.Clear(); g.UiTargetList.Add(target); break;
        }
        Post(Stamp, kind, () => Apply(g, kind, target));
        return true;
    }

    void Apply(TacticalGroup g, TacticalMessage kind, TacticalUnit t)
    {
        if (!_groups.Contains(g)) return;
        switch (kind)
        {
            case TacticalMessage.ReplaceTargets: // the target list becomes [t]; the mission object is no longer done
                g.G.Target = t.U;
                g.More.Clear();
                g.Notified = false;
                if (g.Object is { } o) o.Done = false;
                break;
            case TacticalMessage.AddTarget:
                if (g.G.Target is null) g.G.Target = t.U;
                else if (g.G.Target != t.U && !g.More.Contains(t)) g.More.Add(t);
                break;
            case TacticalMessage.RemoveTarget: // clears the notified flag
                g.Notified = false;
                if (g.G.Target == t.U)
                {
                    g.G.Target = g.More.Count > 0 ? g.More[0].U : null;
                    if (g.More.Count > 0) g.More.RemoveAt(0);
                }
                else g.More.Remove(t);
                break;
        }
    }

    /// <summary>The group's simulation target list: the current target, then the further entries.</summary>
    public IReadOnlyList<TacticalUnit> TargetList(TacticalGroup g) => Current(g) is { } c ? [c, .. g.More] : [.. g.More];

    /// A nav order: the UI list changes at once, the simulation list by the message (add-nav-point appends, remove-nav-point removes, replace-nav-points replaces).
    public bool OrderNav(TacticalGroup g, TacticalMessage kind, TacticalNavPoint n)
    {
        if (Done || !_groups.Contains(g) || kind is not (TacticalMessage.AddNavPoint or TacticalMessage.RemoveNavPoint or TacticalMessage.ReplaceNavPoints)) return false;
        void Edit(List<TacticalNavPoint> list)
        {
            if (kind == TacticalMessage.ReplaceNavPoints) list.Clear();
            if (kind == TacticalMessage.RemoveNavPoint) list.Remove(n);
            else list.Add(n);
        }
        Edit(g.UiNavList);
        Post(Stamp, kind, () => { if (_groups.Contains(g)) Edit(g.NavPoints); });
        return true;
    }

    // ---- mission and tactic setters ----

    /// <summary>
    /// Mission setter: 1. refused when (the UI or the simulation mission is 2 or 6), the group's side
    /// is a computer side other than the local side and m is not TacticalMissionNumber.None; 2. old UI mission 1 means UI targets cleared and tactic 1; 3. UI mission set to m,
    /// with the list clears per m (2 only for a fighter group, 6 only for a fighter group of side index 0, else restored and
    /// refused; 10 clears the simulation nav list, as read); 4. m = 2 or 6 on the local side means the group leaves its HUD slot; 5.
    /// set-mission message (the handler).
    /// </summary>
    public bool SetMission(TacticalGroup g, TacticalMissionNumber m)
    {
        if (Done || !_groups.Contains(g)) return false;
        if ((g.UiMission is TacticalMissionNumber.Recover or TacticalMissionNumber.AttackDeathStar || g.Mission is TacticalMissionNumber.Recover or TacticalMissionNumber.AttackDeathStar) && Computer[g.Side] && g.Side != Local && m != TacticalMissionNumber.None) return false;
        if (g.UiMission == TacticalMissionNumber.Escort)
        {
            g.UiTargetList.Clear();
            SetTactic(g, TacticalTactic.Surround);
        }
        TacticalMissionNumber old = g.UiMission;
        g.UiMission = m;
        switch (m)
        {
            case TacticalMissionNumber.Escort or TacticalMissionNumber.LeftHook or TacticalMissionNumber.RightHook or TacticalMissionNumber.Hammer or TacticalMissionNumber.Stop: g.UiNavList.Clear(); break;
            case TacticalMissionNumber.Recover when !g.Fighters:
            case TacticalMissionNumber.AttackDeathStar when !(g.Fighters && g.Side == 0):
                g.UiMission = old;
                return false;
            case TacticalMissionNumber.None or TacticalMissionNumber.Recover or TacticalMissionNumber.Withdraw or TacticalMissionNumber.AttackFighters or TacticalMissionNumber.AttackCapitalShips or TacticalMissionNumber.AttackDeathStar: g.UiNavList.Clear(); g.UiTargetList.Clear(); break;
            case TacticalMissionNumber.Anvil: g.NavPoints.Clear(); break;
        }
        if (m is TacticalMissionNumber.Recover or TacticalMissionNumber.AttackDeathStar && g.Side == Local) Ui.Free(g);
        Post(Stamp, TacticalMessage.SetMission, () => MissionMessage(g, m));
        return true;
    }

    /// Tactic: sets the tactic to t, then the set-mode message (a task force's formation follows at the dispatch: 1 Surround gives a sphere, 2 Stand Off gives a line).
    public void SetTactic(TacticalGroup g, TacticalTactic t) => RequestMode(g, t);

    /// The local side's world withdraw when it may retreat; the withdraw command.
    public bool WithdrawLocal()
    {
        if (!RetreatPossible(Local)) return false;
        Withdraw(Local);
        return true;
    }

    // ---- helpers ----

    /// <summary>
    /// Nearest enemy: the active unit of the other side than <paramref name="side"/> with the smallest squared 3-D distance
    /// to <paramref name="at"/> (strictly smaller wins, world list order), capital ships first, else fighters.
    /// </summary>
    public TacticalUnit? Nearest(V3 at, int side) => NearestOf(at, 1 - side, false) ?? NearestOf(at, 1 - side, true);

    /// <summary>Runs every queued message now, in queue order (tests: an order's effect without the turn latency).</summary>
    internal void RunQueued()
    {
        var run = _queue.ToList();
        _queue.Clear();
        foreach (var m in run) m.Run();
    }
}
