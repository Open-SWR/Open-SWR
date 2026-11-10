namespace OpenSWR.Sim;

/// <summary>Box style of a unit: none, normal (model only), marked (green own / red enemy), primary (white), selected (yellow).</summary>
public enum TacticalBox { Hidden, Normal, Marked, Primary, Selected }

/// <summary>Nav marker style: hidden, by layer (light blue below, red on the plane, white above), first (yellow), other (magenta).</summary>
public enum TacticalNavStyle { Hidden, Layer, First, Other }

/// <summary>
/// The tactical UI controller: the current mode, the three event queues drained once per frame (ship cycling, commands,
/// picks), the per-frame refresh (the mode's refresh step runs until it returns no new mode, then the box flags), the
/// right-panel page switch and the cameras. Plain C#: the front end feeds mouse picks, buttons and keys and draws from
/// <see cref="Box"/>, <see cref="NavStyle"/>, <see cref="Page"/> and <see cref="Camera"/>. Voices are recorded as request
/// numbers; the front end plays them through <see cref="TacticalSounds"/>.
/// </summary>
public sealed partial class TacticalUi
{
    readonly TacticalBattle _b;
    internal TacticalUi(TacticalBattle battle)
    {
        _b = battle;
        _mode = new IdleMode(this);
        Camera = new TacticalCamera(battle);
    }

    /// <summary>True while in command, false while observing. True at creation.</summary>
    public bool InCommand { get; internal set; } = true;
    /// <summary>The info unit shown on the ship pages.</summary>
    public TacticalUnit? Info { get; internal set; }
    /// <summary>Highlight flags of side 0 and side 1.</summary>
    public bool[] Highlight { get; } = new bool[2];
    /// <summary>Whether nav list k (set k + 1) is visible.</summary>
    public bool[] NavShown { get; } = new bool[4];
    /// <summary>Task-force slots 1-8 (group or none).</summary>
    public TacticalGroup?[] TaskForces { get; } = new TacticalGroup?[8];
    /// <summary>Fighter-group slots Red / Blue / Green / Gold (from setup).</summary>
    public TacticalGroup?[] Fighters { get; } = new TacticalGroup?[4];
    /// <summary>The current mode.</summary>
    public Mode Current => _mode;
    Mode _mode;
    /// <summary>Voice requests in order (request numbers; played by the front end).</summary>
    public List<int> Voices { get; } = [];
    public TacticalCamera Camera { get; }

    readonly List<bool> _cycle = [];
    readonly List<int> _commands = [];
    readonly List<(object? Key, bool Ctrl)> _picks = [];

    /// <summary>Task-force slot of a group (0-7), -1 when unassigned.</summary>
    public int SlotOf(TacticalGroup? g) => g is null ? -1 : Array.IndexOf(TaskForces, g);

    /// <summary>
    /// The unit's UI task force when that group is in no task-force slot (an "unassigned" task force), else none.
    /// Inferred: only the task-force slots count, so a fighter's group (in a fighter slot) is its unassigned task force.
    /// </summary>
    public TacticalGroup? Unassigned(TacticalUnit u) => u.UiGroup is { } g && SlotOf(g) < 0 ? g : null;

    /// <summary>Bind a group to a task-force slot.</summary>
    public void Bind(int slot, TacticalGroup g) => TaskForces[slot] = g;

    /// <summary>A group leaves its HUD slot: its task-force or fighter slot is cleared.</summary>
    /// <remarks>A mode showing that very slot (mode - 1 = task-force slot,
    /// mode - 9 = fighter slot) is replaced by a new idle mode.</remarks>
    internal void Free(TacticalGroup g)
    {
        for (int i = 0; i < 8; i++)
            if (TaskForces[i] == g)
            {
                TaskForces[i] = null;
                if (_mode.Id == 1 + i) Switch(new IdleMode(this));
            }
        for (int c = 0; c < 4; c++)
            if (Fighters[c] == g)
            {
                Fighters[c] = null;
                if (_mode.Id == 9 + c) Switch(new IdleMode(this));
            }
    }

    /// <summary>The first free task-force slot, -1 when none.</summary>
    public int FreeSlot() => Array.IndexOf(TaskForces, null);

    // ---- input ----

    /// <summary>Queue 1: next (Tab, page arrow) or previous ship.</summary>
    public void Cycle(bool next) => _cycle.Add(next);

    /// <summary>Queue 2: a command id for the dispatcher.</summary>
    public void Command(int id) => _commands.Add(id);

    /// <summary>Queue 3: a pick (unit, nav point or none = empty space; ctrl).</summary>
    public void Pick(object? key, bool ctrl) => _picks.Add((key, ctrl));

    /// <summary>
    /// Left button in the 3-D view with the first visible hit of the five-point pick. With no hit and Ctrl nothing happens;
    /// with no hit otherwise the empty pick is made. A hit with Ctrl is picked as (key, 1). Otherwise (the task-force and
    /// fighter buttons unchecked) the empty pick is made, then the hit as (key, 0), so a plain left click selects.
    /// </summary>
    public void LeftDown(object? hit, bool ctrl)
    {
        if (hit is null) { if (!ctrl) Pick(null, false); return; }
        if (ctrl) { Pick(hit, true); return; }
        Pick(null, false);
        Pick(hit, false);
    }

    /// <summary>Right button with the first hit of the 13-point pick: no hit does nothing; a hit is picked (key, ctrl) into the current mode as an order.</summary>
    public void RightDown(object? hit, bool ctrl)
    {
        if (hit is not null) Pick(hit, ctrl);
    }

    /// <summary>A task-force button 0xAD02 + i (also keys 1-8): a used slot, or an empty slot while the mode is own ship or multi-selection, gives command 2 + i.</summary>
    public void PressTaskForce(int i)
    {
        if (TaskForceEnabled(i)) Command(2 + i);
    }

    /// <summary>Whether task-force button i reacts.</summary>
    public bool TaskForceEnabled(int i) => TaskForces[i] is not null || _mode is OwnShipMode or MultiMode;

    /// <summary>Assign mission (right-panel pages 0x19 / 0x1A).</summary>
    public void AssignMission(int m) => Switch(_mode.Mission(m));

    /// <summary>Assign tactic (page 0x1A).</summary>
    public void AssignTactic(int t) => Switch(_mode.Tactic(t));

    void Switch(Mode? next)
    {
        if (next is null) return;
        _mode = next; // the new mode replaces and deletes the current one
        next.Enter();
    }

    /// <summary>
    /// Once per frame: the three queues in order (cycling, commands, picks), then the mode's refresh step until it returns
    /// no new mode.
    /// </summary>
    public void Frame()
    {
        Drain();
        if (_b.SceneAlive) PanelValue = (int)Math.Truncate(_b.Scene!.Charge);
        Camera.Frame();
    }

    /// <summary>The three queues drained in order (cycling, commands, picks), then the mode refresh until it returns no new mode.</summary>
    internal void Drain()
    {
        foreach (bool next in _cycle.ToList()) Switch(next ? _mode.Next() : _mode.Previous());
        _cycle.Clear();
        foreach (int id in _commands.ToList()) Dispatch(id);
        _commands.Clear();
        foreach (var (key, ctrl) in _picks.ToList()) Route(key, ctrl);
        _picks.Clear();
        for (int i = 0; i < 16 && _mode.Refresh() is { } next; i++) Switch(next);
    }

    /// <summary>
    /// Pick routing: none goes to the mode's empty-click handler (ctrl 0 only); a unit goes to the mode's unit handler when in
    /// command, else to own-ship or enemy mode; a nav point goes to the mode's nav handler when in command, else to nav-point mode.
    /// </summary>
    void Route(object? key, bool ctrl)
    {
        switch (key)
        {
            case null: if (!ctrl) Switch(_mode.Empty()); break;
            case TacticalUnit u: Switch(InCommand ? _mode.Unit(u, ctrl) : u.Side == _b.Local ? new OwnShipMode(this, u) : new EnemyMode(this, u)); break;
            case TacticalNavPoint n when !n.Hidden: Switch(InCommand ? _mode.Nav(n, ctrl) : new NavMode(this, n)); break;
        }
    }

    /// <summary>Command dispatcher.</summary>
    void Dispatch(int id)
    {
        switch (id)
        {
            case >= 2 and <= 9:
                int i = id - 2;
                if (TaskForces[i] is not null) Switch(_mode is TaskForceMode t && t.Slot == i ? new IdleMode(this) : new TaskForceMode(this, i));
                else Switch(_mode.EmptyTaskForce(i));
                break;
            case >= 0xB and <= 0xE:
                int c = id - 0xB;
                if (Fighters[c] is not null) Switch(_mode is FighterMode f && f.Colour == c ? new IdleMode(this) : new FighterMode(this, c));
                break;
            case 0xF:
                if (_mode is SuperlaserMode) Switch(new IdleMode(this));
                else if (InCommand) Switch(_mode.Superlaser());
                break;
            case >= 0x10 and <= 0x13:
                int list = 3 - (id - 0x10); // ids 0x10 to 0x13 toggle lists 3 to 0
                NavShown[list] = !NavShown[list];
                foreach (var p in _b.NavPoints.Where(p => p.Set == list + 1)) p.Hidden = !NavShown[list];
                break;
            case 0x14 or 0x15: Highlight[id - 0x14] = !Highlight[id - 0x14]; break;
            case 0x20: if (InCommand) Switch(_mode.Space()); break;
            case 0x7A:
                if ((InCommand || _b.Computer[_b.Local]) && _b.RetreatPossible(_b.Local))
                {
                    Locked = false;
                    DeathStarPanel = false;
                    InCommand = false;
                    _b.Withdraw(_b.Local);
                }
                break;
            case 0x7B:
                bool computer = !_b.Computer[_b.Local];
                _b.SetComputer(_b.Local, computer); // switching to computer control re-arms the rebuild
                InCommand = !computer;
                break;
            case 0x7C:
                if ((InCommand || _b.Computer[_b.Local]) && _b.SideState(1 - _b.Local) == 0) _b.Abort(); // single-player world
                break;
        }
    }

    // ---- right panel ----

    /// <summary>The page shown (0x15 ... 0x1C) and the object it shows (a unit or a group).</summary>
    public int Page { get; private set; } = 0x15;
    public object? PageObject { get; private set; }
    int _previous = 0x15;
    object? _previousObject;
    /// <summary>
    /// Panel lock: set by the show handlers of pages 0x19 (missions), 0x1A (maneuvers &amp; tactics) and 0x1B (game options),
    /// not 0x1C; released by Assign / Cancel on 0x19 / 0x1A, Close on 0x1B, the hide slot of 0x19 / 0x1A (Inferred) and any
    /// switch to page 0x15.
    /// </summary>
    public bool Locked { get; private set; }

    /// <summary>
    /// Page switch: 0x15 always (releases the lock); while locked every other switch is ignored; page 0 means the previous
    /// page and its object. A switch hides page 0x1B's withdraw child window.
    /// </summary>
    public void ShowPage(int page, object? obj = null, bool fromPanel = false)
    {
        if (page == 0) (page, obj) = (_previous, _previousObject);
        if (page != 0x15 && Locked && !fromPanel) return;
        WithdrawShown = false;
        if (page == 0x15) Locked = false;
        (_previous, _previousObject) = (Page, PageObject);
        (Page, PageObject) = (page, obj);
        if (page is 0x19 or 0x1A or 0x1B) Locked = true;
    }

    /// <summary>Close a page (Assign / Cancel of 0x19 / 0x1A, 0x1C's buttons): the lock released, then the previous page and its object.</summary>
    public void ClosePage()
    {
        Locked = false;
        ShowPage(0);
    }

    /// <summary>Orders buttons of the ship / fighter pages: in command, the unit's (group's) side is the local side and the local side is not withdrawing.</summary>
    public bool OrdersEnabled(int side) => InCommand && side == _b.Local && _b.SideState(_b.Local) == 0;

    /// <summary>Get Immediate Result enabled: neither side is withdrawing.</summary>
    public bool ImmediateEnabled => _b.SideState(0) == 0 && _b.SideState(1) == 0;

    /// <summary>Page 0x1C (withdraw confirmation) shown as a child window of page 0x1B at (0, 0); the page and its lock stay 0x1B's.</summary>
    public bool WithdrawShown { get; private set; }

    /// <summary>
    /// Withdraw button 0xDD01 of page 0x1B: when the local side may not retreat, the button is reset and nothing else happens.
    /// Otherwise voice 0x95 (A) / 0x10E (E) plays when an own capital ship cannot jump, and page 0x1C is built directly as
    /// 0x1B's child window (no page switch, so the lock does not apply).
    /// </summary>
    public void WithdrawPressed()
    {
        if (!_b.RetreatPossible(_b.Local)) return;
        if (Ships(_b.Local).Any(u => u.Active && !_b.CanJump(u))) Voice(0x95, 0x10E);
        WithdrawShown = true;
    }

    /// <summary>Page 0x1C's OK (command 0x7A, which unlocks the panel) or Cancel: both hide 0x1C; page 0x1B shows again.</summary>
    public void WithdrawAnswer(bool ok)
    {
        WithdrawShown = false;
        if (ok) Command(0x7A);
    }

    /// <summary>
    /// Orders line (pages 0x16, 0x17, 0x18) as parts: TEXTTACT string ids (int) and unit names (a unit). A side that is not
    /// the local side gets 56767. Otherwise the UI mission of the unit's UI group picks the string: 1 Escort 56801, 2 nothing,
    /// 3 Withdraw 56803 or Disabled 56721 at speed 0, 4 56811, 5 56805, 6 56806, 7-10 56807-56810, anything else No Orders 56719.
    /// Then the simulation group: a non-empty UI nav list gives Navigating 56718 (Disabled at speed 0), else a non-empty UI
    /// target list gives Attacking 56717, and on mission 1 the first target's name is added. Inferred: the second part replaces
    /// the first, except the escorted name, which follows it.
    /// </summary>
    public List<object> OrdersLine(int side, TacticalGroup? ui, TacticalGroup? sim, TacticalUnit? unit)
    {
        if (side != _b.Local) return [56767];
        bool stopped = unit is not null && _b.Speed(unit) == 0;
        int m = ui?.UiMission ?? 0;
        List<object> line = m switch
        {
            1 => [56801],
            2 => [],
            3 => [stopped ? 56721 : 56803],
            4 => [56811],
            5 => [56805],
            6 => [56806],
            >= 7 and <= 10 => [56807 + m - 7],
            _ => [56719],
        };
        if (sim is null) return line;
        if (sim.UiNav.Count > 0) return [stopped ? 56721 : 56718];
        if (sim.UiTargets.Count > 0) return m == 1 ? [.. line, sim.UiTargets[0]] : [56717];
        return line;
    }

    /// <summary>
    /// The five system icon levels of the ship page: v = trunc(100 * fraction), fraction 0 when the base is 0. The fractions are
    /// shield recharge against base shield recharge rate, weapon rate against 3.75 times the base weapon rate, tractor power
    /// against its base, speed against base speed (above 100 with a bonus), and working hyperdrives against the full count.
    /// Level v &gt; 74 is 4, &gt; 49 is 3, &gt; 24 is 2, v != 0 is 1, else 0.
    /// </summary>
    public int[] SystemLevels(TacticalUnit t)
    {
        var u = t.U;
        static int V(double value, double b) => b != 0 ? (int)Math.Truncate(100 * (value / b)) : 0;
        int[] v =
        [
            V(BattleEngine.ShieldRate(u), u.BaseSrr),
            V(BattleEngine.WeaponRate(u), 3.75 * u.Wrr),
            V(TacticalBattle.TractorPowerOf(t), t.TractorPower),
            V(_b.Speed(t), u.BaseSpeed),
            V(u.Hyperdrives - u.HyperCh, u.Hyperdrives),
        ];
        return [.. v.Select(x => x > 74 ? 4 : x > 49 ? 3 : x > 24 ? 2 : x != 0 ? 1 : 0)];
    }

    /// <summary>
    /// Officer strip of the ship page: the info unit's character records, then its troop and special-force records,
    /// each in list order (the setup's: characters, troop regiments, special forces aboard).
    /// </summary>
    public IReadOnlyList<GameObject> Officers(TacticalUnit t) => t.U.Carried;

    /// <summary>
    /// Officer icon: the bitmap id from the table keyed by the SD string number (Inferred argument), drawn as that
    /// TACTICAL bitmap. 0 when the key is not in the table (no icon).
    /// </summary>
    public static int OfficerIcon(int key) => OfficerIcons.GetValueOrDefault(key);

    static readonly Dictionary<int, int> OfficerIcons = new()
    {
        // major characters
        [0x840] = 2234, [0x841] = 2241, [0x842] = 2230, [0x843] = 2255, [0x850] = 2229, [0x880] = 2214, [0x881] = 2260,
        // minor characters
        [0xA40] = 2201, [0xA41] = 2204, [0xA42] = 2205, [0xA43] = 2206, [0xA44] = 2210, [0xA45] = 2231, [0xA46] = 2250, [0xA47] = 2202,
        [0xA48] = 2212, [0xA49] = 2217, [0xA4A] = 2249, [0xA4B] = 2209, [0xA4C] = 2220, [0xA4D] = 2225, [0xA4E] = 2235, [0xA4F] = 2237,
        [0xA50] = 2245, [0xA51] = 2254, [0xA52] = 2256, [0xA53] = 2263, [0xA54] = 2264, [0xA55] = 2223, [0xA56] = 2203, [0xA57] = 2248,
        [0xA58] = 2243, [0xA59] = 2232, [0xA80] = 2222, [0xA81] = 2244, [0xA82] = 2247, [0xA83] = 2261, [0xA84] = 2266, [0xA85] = 2207,
        [0xA86] = 2211, [0xA87] = 2215, [0xA88] = 2216, [0xA89] = 2219, [0xA8A] = 2218, [0xA8B] = 2226, [0xA8C] = 2236, [0xA8D] = 2239,
        [0xA8E] = 2242, [0xA8F] = 2246, [0xA90] = 2253, [0xA91] = 2258, [0xA92] = 2265, [0xA93] = 2208, [0xA94] = 2257, [0xA95] = 2228,
        [0xA96] = 2251, [0xA97] = 2240, [0xA98] = 2221, [0xA99] = 2233, [0xA9A] = 2227, [0xA9B] = 2262,
        // special forces
        [0x540] = 2308, [0x541] = 2309, [0x542] = 2318, [0x543] = 2302, [0x580] = 2313, [0x581] = 2305, [0x582] = 2303, [0x583] = 2312,
        // troops
        [0x440] = 2310, [0x441] = 2301, [0x442] = 2314, [0x443] = 2316, [0x444] = 2311, [0x480] = 2317, [0x481] = 2307, [0x482] = 2306,
        [0x483] = 2315, [0x484] = 2304,
    };

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
    static bool Escortable(TacticalUnit u) => u.Group is not { } g || g.Mission != 1 && g.UiMission != 1;

    /// <summary>Attackable: active and not in state 6, 7, 8 or 10 (so state 2 or 4).</summary>
    static bool Attackable(TacticalUnit u) => u.State is 2 or 4;

    /// <summary>T := the ship's unassigned task force, else a new task force for it (it leaves a numbered one).</summary>
    TacticalGroup TaskForceOf(TacticalUnit s) => Unassigned(s) ?? _b.CreateTaskForce(s);

    /// <summary>
    /// Inferred: a destroyed unit stays in the UI member list of its UI group when that group is not its simulation group, until the group is deleted; the
    /// readers that pick or aim from the UI member list skip state 11.
    /// </summary>
    static IEnumerable<TacticalUnit> Alive(IEnumerable<TacticalUnit> units) => units.Where(u => u.State != 11);

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
        foreach (var t in targets) _b.Order(g, TacticalBattle.AddTarget, t);
        foreach (var n in navs) _b.OrderNav(g, TacticalBattle.AddNavPoint, n);
        var mode = new TaskForceMode(this, slot) { InfoAtEnter = selection[0] };
        if (targets.Count > 0 && targets[0].Side == g.Side) mode.Mission(1);
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

    // ---- modes ----

    /// <summary>A mode object: subject, id, and handlers for unit, nav point, empty task-force button, empty click, Space, mission, tactic, superlaser, refresh, next and previous. Handlers return a new mode or none.</summary>
    public abstract class Mode(TacticalUi ui)
    {
        protected readonly TacticalUi Ui = ui;
        protected TacticalBattle B => Ui._b;
        /// <summary>Mode id: 0 idle, 1-8 task force, 9-12 fighter group, 0xD nav point, 0xE own ship, 0xF enemy ship, 0x10 multi-selection, 0x11 superlaser target.</summary>
        public abstract int Id { get; }
        /// <summary>Subject for Reset Camera Subject (+4).</summary>
        public virtual TacticalUnit? Subject => null;
        /// <summary>The unit drawn selected.</summary>
        public virtual TacticalUnit? Selected => null;
        /// <summary>Drawn selected (yellow).</summary>
        public virtual bool IsSelected(TacticalUnit u) => Selected == u;
        /// <summary>The shown group.</summary>
        public virtual TacticalGroup? Shown => null;
        /// <summary>The constructor's side effects: info unit and page.</summary>
        internal virtual void Enter() { Ui.Info = null; Ui.ShowPage(0x15); }
        public virtual Mode? Unit(TacticalUnit u, bool ctrl) => null;
        public virtual Mode? Nav(TacticalNavPoint n, bool ctrl) => null;
        public virtual Mode? EmptyTaskForce(int slot) => null;
        public virtual Mode? Empty() => new IdleMode(Ui);
        public virtual Mode? Space() => null;
        public virtual Mode? Mission(int m) => null;
        public virtual Mode? Tactic(int t) => null;
        /// <summary>
        /// Enters the superlaser target mode only when a Death Star scene exists, its charge is 100.0 and its hull (SD Hull) &gt; 0
        /// (no alive test: an unfinished scene enters and the next refresh leaves).
        /// </summary>
        public virtual Mode? Superlaser() => B.Scene is { } s && s.Charge == 100f && s.HullMax > 0 ? new SuperlaserMode(Ui) : null;
        public virtual Mode? Refresh() => null;
        public virtual Mode? Next() => null;
        public virtual Mode? Previous() => null;
        /// <summary>The selection selects: an own unit gives own-ship mode, an enemy gives enemy mode.</summary>
        protected Mode Select(TacticalUnit u) => u.Side == B.Local ? new OwnShipMode(Ui, u) : new EnemyMode(Ui, u);
        protected Mode? Attack(V3 at) => B.Nearest(at, B.Local) is { } n ? Unit(n, false) : null;
    }

    /// <summary>Idle (0): own unit selects own-ship mode, enemy selects enemy mode, nav point selects nav-point mode; page 0x15.</summary>
    public sealed class IdleMode(TacticalUi ui) : Mode(ui)
    {
        public override int Id => 0;
        public override Mode? Unit(TacticalUnit u, bool ctrl) => Select(u);
        public override Mode? Nav(TacticalNavPoint n, bool ctrl) => new NavMode(Ui, n);
    }

    /// <summary>Enemy ship (0xF): the unit marked; unit selects own-ship or enemy mode, nav selects nav mode; page 0x16.</summary>
    public sealed class EnemyMode(TacticalUi ui, TacticalUnit u) : Mode(ui)
    {
        public TacticalUnit U { get; } = u;
        public override int Id => 0xF;
        public override TacticalUnit? Subject => U;
        internal override void Enter() { Ui.Info = U; Ui.ShowPage(0x16, U); }
        public override Mode? Unit(TacticalUnit u, bool ctrl) => Select(u);
        public override Mode? Nav(TacticalNavPoint n, bool ctrl) => new NavMode(Ui, n);
    }

    /// <summary>Nav point (0xD): page 0x15; the point marked (magenta), with the first-point mark cleared; unit selects own-ship or enemy mode, nav selects nav mode.</summary>
    public sealed class NavMode(TacticalUi ui, TacticalNavPoint n) : Mode(ui)
    {
        public TacticalNavPoint N { get; } = n;
        public override int Id => 0xD;
        public override Mode? Unit(TacticalUnit u, bool ctrl) => Select(u);
        public override Mode? Nav(TacticalNavPoint n, bool ctrl) => new NavMode(Ui, n);
    }

    /// <summary>Own ship (0xE): one selected ship S; page 0x16.</summary>
    public sealed class OwnShipMode(TacticalUi ui, TacticalUnit s) : Mode(ui)
    {
        public TacticalUnit S { get; } = s;
        public override int Id => 0xE;
        public override TacticalUnit? Subject => S;
        public override TacticalUnit? Selected => S;
        public override TacticalGroup? Shown => S.UiGroup;
        internal override void Enter() { Ui.Info = S; Ui.ShowPage(0x16, S); }

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (u.Side == S.Side)
            {
                if (ctrl) return u != S ? new MultiMode(Ui, [S, u]) : null;
                if (u == S || !Escortable(u)) return null;
                var t = Ui.TaskForceOf(S);
                Ui.Voice(0x77, 0xF5);
                B.Order(t, TacticalBattle.ReplaceTargets, u);
                B.SetMission(t, 1);
                return null;
            }
            if (!Attackable(u)) return new EnemyMode(Ui, u);
            var g = Ui.TaskForceOf(S);
            if (!ctrl)
            {
                if (g.UiNav.Count == 0) Ui.Voice(0x3B, 0xB5);
                B.SetMission(g, 0);
                B.Order(g, TacticalBattle.ReplaceTargets, u);
            }
            else if (!g.UiTargets.Contains(u))
            {
                if (g.UiTargets.Count == 0 && g.UiNav.Count == 0) Ui.Voice(0x3B, 0xB5);
                if (g.UiMission == 1) { B.SetMission(g, 0); B.Order(g, TacticalBattle.ReplaceTargets, u); }
                else B.Order(g, TacticalBattle.AddTarget, u);
            }
            else B.Order(g, TacticalBattle.RemoveTarget, u);
            return null;
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            var g = Ui.TaskForceOf(S);
            if (!ctrl)
            {
                B.SetMission(g, 0);
                B.OrderNav(g, TacticalBattle.ReplaceNavPoints, n);
                Ui.Voice(0x21, 0x9B);
            }
            else if (!g.UiNav.Contains(n))
            {
                if (g.UiNav.Count == 0) Ui.Voice(0x21, 0x9B);
                B.OrderNav(g, TacticalBattle.AddNavPoint, n);
            }
            else B.OrderNav(g, TacticalBattle.RemoveNavPoint, n);
            return null;
        }

        public override Mode? EmptyTaskForce(int slot) => Ui.FromSelection([S], [], [], slot);
        public override Mode? Space() => Attack(S.Position);

        public override Mode? Mission(int m)
        {
            var g = B.CreateTaskForce(S);
            Ui.Voice(0x77, 0xF5);
            B.SetMission(g, m);
            return null;
        }

        public override Mode? Tactic(int t)
        {
            B.SetTactic(B.CreateTaskForce(S), t);
            return null;
        }

        public override Mode? Next() => Step(Ui.Ships(S.Side), S, true) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Previous() => Step(Ui.Ships(S.Side), S, false) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Refresh() => S.Active ? null : new IdleMode(Ui);
    }

    /// <summary>Multi-selection (0x10): two or more own units; page 0x18.</summary>
    public sealed class MultiMode(TacticalUi ui, List<TacticalUnit> selection) : Mode(ui)
    {
        public List<TacticalUnit> Selection { get; } = selection;
        public override int Id => 0x10;
        public override TacticalUnit? Subject => Selection.FirstOrDefault();
        public override TacticalUnit? Selected => Ui.Info;
        /// <summary>Mode 0x10's per-frame slot: every selected active unit is drawn selected (yellow).</summary>
        public override bool IsSelected(TacticalUnit u) => u.Active && Selection.Contains(u);
        internal override void Enter() { Ui.Info = Selection[^1]; Ui.ShowPage(0x18, Ui.Info); } // Inferred: the unit added last (ctrl 1 add)

        int Side => Selection[0].Side;

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            int k = Ui.FreeSlot();
            if (u.Side == B.Local)
            {
                if (ctrl)
                {
                    if (!Selection.Contains(u)) { Selection.Add(u); Ui.Info = u; return null; }
                    Selection.Remove(u);
                    if (Selection.Count == 1) return new OwnShipMode(Ui, Selection[0]);
                    Ui.Info = Selection[0];
                    return null;
                }
                if (!Escortable(u)) return null;
                if (k < 0) return new OwnShipMode(Ui, u);
                Selection.Remove(u);
                return Selection.Count == 0 ? null : Ui.FromSelection(Selection, [], [u], k);
            }
            if (!Attackable(u)) return new EnemyMode(Ui, u);
            if (k < 0)
            {
                Ui.Voices.Add(Side == 0 ? 0x84 : 0x102);
                foreach (var s in Selection) B.Order(B.CreateTaskForce(s), TacticalBattle.AddTarget, u);
                return new OwnShipMode(Ui, Selection[0]);
            }
            Ui.Voice(0x3C + k, 0xB6 + k);
            return Ui.FromSelection(Selection, [], [u], k);
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            int k = Ui.FreeSlot();
            if (k < 0)
            {
                Ui.Voices.Add(Side == 0 ? 0x84 : 0x102);
                foreach (var s in Selection) B.OrderNav(B.CreateTaskForce(s), TacticalBattle.AddNavPoint, n);
                return new OwnShipMode(Ui, Selection[0]);
            }
            Ui.Voice(0x22 + k, 0x9C + k);
            return Ui.FromSelection(Selection, [n], [], k);
        }

        public override Mode? EmptyTaskForce(int slot) => Ui.FromSelection(Selection, [], [], slot);
        public override Mode? Space() => Attack(Centroid(Selection));

        public override Mode? Mission(int m) => Order(0x78, 0xF6, g => B.SetMission(g, m));
        public override Mode? Tactic(int t) => Order(0x6F, 0xED, g => B.SetTactic(g, t));

        /// <summary>Mission / tactic: no free slot gives every unit its own task force with it, then own-ship mode of the first; else a numbered task force in slot k with the voice, giving task-force mode k.</summary>
        Mode Order(int alliance, int empire, Action<TacticalGroup> set)
        {
            int k = Ui.FreeSlot();
            if (k < 0)
            {
                foreach (var s in Selection) set(B.CreateTaskForce(s));
                return new OwnShipMode(Ui, Selection[0]);
            }
            var mode = (TaskForceMode)Ui.FromSelection(Selection, [], [], k);
            Ui.Voice(alliance + k, empire + k);
            set(Ui.TaskForces[k]!);
            return mode;
        }

        public override Mode? Next() => Step(Ui.Ships(B.Local), Ui.Info, true) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Previous() => Step(Ui.Ships(B.Local), Ui.Info, false) is { } u ? new OwnShipMode(Ui, u) : null;

        public override Mode? Refresh()
        {
            Selection.RemoveAll(u => !u.Active);
            if (Selection.Count == 0) return new IdleMode(Ui);
            if (Selection.Count == 1) return new OwnShipMode(Ui, Selection[0]);
            if (Ui.Info is null || !Selection.Contains(Ui.Info)) Ui.Info = Selection[0];
            return null;
        }
    }

    /// <summary>Task force (1-8; T in slot k = id - 1); page 0x18.</summary>
    public sealed class TaskForceMode(TacticalUi ui, int slot) : Mode(ui)
    {
        public int Slot { get; } = slot;
        public TacticalGroup? T => Ui.TaskForces[Slot];
        internal TacticalUnit? InfoAtEnter;
        public override int Id => 1 + Slot;
        public override TacticalUnit? Subject => Ui.Info ?? T?.UiLeader;
        public override TacticalUnit? Selected => Ui.Info;
        public override TacticalGroup? Shown => T;
        internal override void Enter() { Ui.Info = InfoAtEnter ?? T?.UiLeader; Ui.ShowPage(0x18, Ui.Info); }

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (T is not { } t) return null;
            int k = Slot;
            if (u.Side == t.Side)
            {
                if (!ctrl)
                {
                    if (t.UiMembers.Contains(u))
                    {
                        int j = Ui.FreeSlot();
                        if (j < 0) return new OwnShipMode(Ui, u);
                        var others = t.UiMembers.Where(m => m != u).ToList();
                        return others.Count == 0 ? null : Ui.FromSelection(others, [], [u], j); // Inferred: a one-ship task force does not split
                    }
                    if (!Escortable(u)) return null;
                    B.Order(t, TacticalBattle.ReplaceTargets, u);
                    return Mission(1);
                }
                if (t.UiTargets.Contains(u))
                {
                    B.Order(t, TacticalBattle.RemoveTarget, u);
                    B.SetMission(t, 0);
                    return null;
                }
                if (!t.UiMembers.Contains(u)) { B.JoinTaskForce(t, u); return null; }
                B.LeaveTaskForce(u);
                return t.UiMembers.Count == 0 ? new IdleMode(Ui) : null;
            }
            if (!ctrl)
            {
                if (t.UiNav.Count == 0) Ui.Voice(0x3C + k, 0xB6 + k);
                B.SetMission(t, 0);
                B.Order(t, TacticalBattle.ReplaceTargets, u);
                return null;
            }
            if (t.UiTargets.Contains(u)) { B.Order(t, TacticalBattle.RemoveTarget, u); return null; }
            if (!Attackable(u)) return new EnemyMode(Ui, u);
            if (t.UiNav.Count == 0 && t.UiTargets.Count == 0) Ui.Voice(0x3C + k, 0xB6 + k);
            if (t.UiMission == 1) { B.SetMission(t, 0); B.Order(t, TacticalBattle.ReplaceTargets, u); }
            else B.Order(t, TacticalBattle.AddTarget, u);
            return null;
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            if (T is not { } t) return null;
            if (!ctrl)
            {
                Ui.Voice(0x22 + Slot, 0x9C + Slot);
                B.SetMission(t, 0);
                B.OrderNav(t, TacticalBattle.ReplaceNavPoints, n);
            }
            else if (!t.UiNav.Contains(n))
            {
                if (t.UiNav.Count == 0) Ui.Voice(0x22 + Slot, 0x9C + Slot);
                B.OrderNav(t, TacticalBattle.AddNavPoint, n);
            }
            else B.OrderNav(t, TacticalBattle.RemoveNavPoint, n);
            return null;
        }

        public override Mode? EmptyTaskForce(int slot) => new IdleMode(Ui);
        public override Mode? Space() => T is { } t ? Attack(Centroid(Alive(t.UiMembers))) : null;

        public override Mode? Mission(int m)
        {
            if (T is not { } t) return null;
            Ui.Voice(0x78 + Slot, 0xF6 + Slot);
            B.SetMission(t, m);
            return null;
        }

        public override Mode? Tactic(int tactic)
        {
            if (T is not { } t) return null;
            Ui.Voice(0x6F + Slot, 0xED + Slot);
            B.SetTactic(t, tactic);
            return null;
        }

        public override Mode? Next() { Cycle(true); return null; }
        public override Mode? Previous() { Cycle(false); return null; }

        /// <summary>Info unit becomes the next / previous UI member of T after it (wraps); the mode stays.</summary>
        void Cycle(bool next)
        {
            if (T is { } t && Step([.. Alive(t.UiMembers)], Ui.Info, next) is { } u) Ui.Info = u;
        }

        public override Mode? Refresh()
        {
            if (T is not { } t || t.UiMembers.Count == 0 || t.UiMission == 3) return new IdleMode(Ui);
            if (Ui.Info is not { Active: true } i || !t.UiMembers.Contains(i)) Ui.Info = t.UiLeader; // the UI leader
            return null;
        }
    }

    /// <summary>Fighter group (9-12); G in colour slot c; page 0x17.</summary>
    public sealed class FighterMode(TacticalUi ui, int colour) : Mode(ui)
    {
        public int Colour { get; } = colour;
        public TacticalGroup? G => Ui.Fighters[Colour];
        public override int Id => 9 + Colour;
        // Inferred: a destroyed fighter can stay a UI member until its group is deleted; skip it
        public override TacticalUnit? Subject => Alive(G?.UiMembers ?? []).FirstOrDefault();
        public override TacticalGroup? Shown => G;
        internal override void Enter() { Ui.Info = Subject; Ui.ShowPage(0x17, G); }

        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (G is not { } g) return null;
            int c = Colour;
            if (u.Side == g.Side)
            {
                if (ctrl && g.UiTargets.Contains(u)) { B.Order(g, TacticalBattle.RemoveTarget, u); B.SetMission(g, 0); }
                else if (Escortable(u)) { B.Order(g, TacticalBattle.ReplaceTargets, u); B.SetMission(g, 1); }
                return null;
            }
            if (!ctrl)
            {
                if (g.UiNav.Count == 0) Ui.Voice(0x44 + c, 0xBE + c);
                B.SetMission(g, 0);
                B.Order(g, TacticalBattle.ReplaceTargets, u);
            }
            else if (!g.UiTargets.Contains(u))
            {
                if (g.UiNav.Count == 0 && g.UiTargets.Count == 0) Ui.Voice(0x44 + c, 0xBE + c);
                B.Order(g, TacticalBattle.AddTarget, u);
            }
            else B.Order(g, TacticalBattle.RemoveTarget, u);
            return null;
        }

        public override Mode? Nav(TacticalNavPoint n, bool ctrl)
        {
            if (G is not { } g) return null;
            if (!ctrl)
            {
                Ui.Voice(0x2A + Colour, 0xA4 + Colour);
                B.SetMission(g, 0);
                B.OrderNav(g, TacticalBattle.ReplaceNavPoints, n);
            }
            else if (!g.UiNav.Contains(n))
            {
                if (g.UiNav.Count == 0) Ui.Voice(0x2A + Colour, 0xA4 + Colour);
                B.OrderNav(g, TacticalBattle.AddNavPoint, n);
            }
            else B.OrderNav(g, TacticalBattle.RemoveNavPoint, n);
            return null;
        }

        public override Mode? EmptyTaskForce(int slot) => new IdleMode(Ui);
        public override Mode? Space() => G is { } g ? Attack(Centroid(Alive(g.UiMembers))) : null;

        /// <summary>
        /// Mission: 6 is refused (leads to idle) unless G is side index 0 and the Death Star attack is possible; the voice; the setter.
        /// </summary>
        public override Mode? Mission(int m)
        {
            if (G is not { } g) return null;
            if (m == 6 && !(g.Side == 0 && B.AttackPossible)) return new IdleMode(Ui);
            Ui.Voice(0x80 + Colour, 0xFE + Colour);
            B.SetMission(g, m);
            return null;
        }

        public override Mode? Tactic(int t)
        {
            if (G is { } g) B.SetTactic(g, t);
            return null;
        }

        public override Mode? Next() => Step(Ui.Ships(B.Local), Ui.Info, true) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Previous() => Step(Ui.Ships(B.Local), Ui.Info, false) is { } u ? new OwnShipMode(Ui, u) : null;
        public override Mode? Refresh() => G is not { } g || g.UiMembers.Count == 0 || g.UiMission is 2 or 3 ? new IdleMode(Ui) : null;
    }

    /// <summary>
    /// Superlaser target (0x11): a right-clicked unit: own unit or no scene gives idle; any enemy unit (capital ship or fighter;
    /// no state, alive or charge test) fires the superlaser, plays voice 0x111, and gives idle. Space picks the enemy capital
    /// ship nearest to the scene point, else the nearest fighter, then acts as a right click. Per frame: no scene, hull at or
    /// below 0, or charge under 100 gives idle; a scene not withdrawn, with side index 1 in state 0 and alive, stays while in
    /// command, else idle; otherwise the Death Star panel is removed and idle. Leaving the mode clears the target cursor and
    /// unpresses the panel button.
    /// </summary>
    public sealed class SuperlaserMode(TacticalUi ui) : Mode(ui)
    {
        public override int Id => 0x11;
        internal override void Enter() { }
        public override Mode? Unit(TacticalUnit u, bool ctrl)
        {
            if (u.Side == B.Local || B.Scene is null) return new IdleMode(Ui);
            B.FireSuperlaser(u);
            Ui.Voices.Add(0x111);
            return new IdleMode(Ui);
        }
        public override Mode? Space() => B.Nearest(B.ScenePoint, B.Local) is { } n ? Unit(n, false) : null;
        public override Mode? Superlaser() => null;
        public override Mode? Refresh()
        {
            if (B.Scene is not { } s || !(s.HullMax > 0) || s.Charge < 100f) return new IdleMode(Ui);
            if (!s.Withdrawn && B.SideState(1) == 0 && B.SceneAlive) return Ui.InCommand ? null : new IdleMode(Ui);
            Ui.DeathStarPanel = false;
            return new IdleMode(Ui);
        }
    }
}
