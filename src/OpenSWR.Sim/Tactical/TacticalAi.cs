namespace OpenSWR.Sim;

/// <summary>
/// A mission object of a tactical group: the mission number, a scratch value, a done flag, a started flag and a hook flag. Status is
/// 1 when started, else 2 when done, else 0. Created by the set-mission message handler for missions 1-11, every field 0.
/// </summary>
public class TacticalMission
{
    internal TacticalMission(TacticalMissionNumber number) => Number = number;
    public TacticalMissionNumber Number { get; }
    internal int Scratch;
    /// <summary>
    /// Started: the Death Star run's init is done; recovery: its voice ("no carrier" or "group recovered") played. Hooked: the
    /// hook manoeuvre was ordered. Named by these uses. Meaning in the original is unknown.
    /// </summary>
    internal bool Done, Started, Hooked;
    /// <summary>Status code, see the summary above.</summary>
    public TacticalMissionStatus Status => Started ? TacticalMissionStatus.Started : Done ? TacticalMissionStatus.Done : TacticalMissionStatus.NotStarted;
}

public sealed partial class TacticalUnit
{
    /// <summary>Recovery state: 0 none, 1 hangar place reserved, 2 carrier assigned, 3 docking, 4 docked.</summary>
    public TacticalRecovery Recovery { get; internal set; }
}

public sealed partial class TacticalGroup
{
    /// <summary>The mission object, none for mission 0 and refused missions.</summary>
    public TacticalMission? Object { get; internal set; }
    /// <summary>Nearest-enemy list: at most one unit, rebuilt every turn.</summary>
    internal readonly List<TacticalUnit> NearestList = [];
    public IReadOnlyList<TacticalUnit> NearestEnemy => NearestList;
}

/// <summary>
/// Tactical missions and the computer side's tactical AI:
/// the set-mission handler with the mission objects, the per-turn group step (mission step, target refresh), missions 2 / 4 / 5 / 7-10, the
/// side centroids, the tactical candidate build, scorer and evaluator kind 2, and the computer side's rebuild and targeting.
/// </summary>
public sealed partial class TacticalBattle
{
    readonly List<BattleEngine.Unit>[] _candidates = [[], []]; // one list per side
    readonly bool[] _rebuildDue = new bool[2]; // per side; true means a group rebuild is due
    readonly bool[] _deathStarGiven = new bool[2]; // per side
    readonly (List<BattleEngine.Unit> Heavy, List<BattleEngine.Unit> Laser, List<BattleEngine.Unit> Unarmed)[] _setupLists = new (List<BattleEngine.Unit>, List<BattleEngine.Unit>, List<BattleEngine.Unit>)[2];
    readonly V3[] _centroid = new V3[2], _fresh = new V3[2]; // the centroids (one frame old) and this frame's

    /// <summary>Side centroid, one frame old.</summary>
    public V3 Centroid(int side) => _centroid[side];

    /// <summary>True while a Death Star run is in progress (a mission-6 object lives; set and cleared by its constructor and destructor).</summary>
    public bool DeathStarRun => _groups.Any(g => g.Object is { Number: TacticalMissionNumber.AttackDeathStar });

    /// <summary>The side's candidate list, rebuilt in each of its side turns.</summary>
    internal IReadOnlyList<TacticalUnit> Candidates(int side) => [.. _candidates[side].Select(u => _byUnit[u])];

    /// <summary>
    /// Computer-flag setter: stores the flag. When it is on (Inferred throughout), the group rebuild is due again at the side's
    /// next targeting and the Death Star run flag is taken from the current run. Called with true for the non-local side at
    /// world init and by the Observe toggle (computer-control command) for the local side.
    /// </summary>
    public void SetComputer(int side, bool on)
    {
        Computer[side] = on;
        if (!on) return;
        _rebuildDue[side] = true;
        _deathStarGiven[side] = DeathStarRun;
    }

    // ---- set mission ----

    /// <summary>
    /// Set-mission message: the old mission object is deleted, and old mission 1 clears the simulation target list. The group's
    /// mission number becomes m. Then, per m (nav and targets are the group's two lists): 1 clears nav, and a task force
    /// switches to the sphere formation; 2 applies only to a fighter group (else the old number stays, with no object), and
    /// the local side's group leaves its fighter button and clears nav and targets; 3, 4 and 5 clear nav and targets; 6 applies
    /// only to a fighter group of side index 0 (else the old number stays), clears nav and targets, and sets side index 0's
    /// Death Star flag to false; 7-11 clear nav. Missions 1-11 get their object; other numbers get none.
    /// </summary>
    internal void MissionMessage(TacticalGroup g, TacticalMissionNumber m)
    {
        if (!_groups.Contains(g)) return;
        TacticalMissionNumber old = g.Mission;
        g.Object = null;
        if (old == TacticalMissionNumber.Escort) ClearTargets(g);
        g.Mission = m;
        switch (m)
        {
            case TacticalMissionNumber.Escort:
                g.NavPoints.Clear();
                if (!g.Fighters) ChangeFormation(g, TacticalFormation.Sphere);
                break;
            case TacticalMissionNumber.Recover when !g.Fighters:
            case TacticalMissionNumber.AttackDeathStar when !(g.Fighters && g.Side == 0):
                g.Mission = old;
                return;
            case TacticalMissionNumber.Recover or TacticalMissionNumber.Withdraw or TacticalMissionNumber.AttackFighters or TacticalMissionNumber.AttackCapitalShips or TacticalMissionNumber.AttackDeathStar:
                if (m == TacticalMissionNumber.Recover && g.Side == Local) Ui.Free(g);
                if (m == TacticalMissionNumber.AttackDeathStar) _deathStarGiven[0] = false;
                g.NavPoints.Clear();
                ClearTargets(g);
                break;
            case TacticalMissionNumber.LeftHook or TacticalMissionNumber.RightHook or TacticalMissionNumber.Hammer or TacticalMissionNumber.Anvil or TacticalMissionNumber.Stop: g.NavPoints.Clear(); break;
            default: return;
        }
        g.Object = m == TacticalMissionNumber.AttackDeathStar ? NewRun(g) : new TacticalMission(m); // mission 3's object is initialised at once (scratch value 0)
    }

    void ClearTargets(TacticalGroup g)
    {
        g.G.Target = null;
        g.More.Clear();
    }

    // ---- per-turn group step ----

    /// <summary>Per-turn group step (single player): the mission object's step, then the target refresh.</summary>
    void GroupStep(TacticalGroup g)
    {
        switch (g.Object?.Number)
        {
            case TacticalMissionNumber.Recover: Recover(g); break;
            case TacticalMissionNumber.AttackFighters or TacticalMissionNumber.AttackCapitalShips: AttackStep(g); break;
            case TacticalMissionNumber.LeftHook or TacticalMissionNumber.RightHook or TacticalMissionNumber.Hammer or TacticalMissionNumber.Anvil when !g.Object.Hooked: Hook(g); break;
            case TacticalMissionNumber.AttackDeathStar: RunStep(g, (TacticalRun)g.Object); break;
        }
        RefreshTargets(g);
    }

    /// <summary>
    /// Target refresh: the nearest-enemy list is cleared; L = the first member; a laser picks the nearest active enemy fighter,
    /// else an ion cannon or a turbolaser picks the nearest active enemy capital ship, else nothing; measured from the group's
    /// nav point; no fallback to the other type.
    /// </summary>
    internal void RefreshTargets(TacticalGroup g)
    {
        g.NearestList.Clear();
        if (g.Members.FirstOrDefault() is not { } l || !l.Armed) return;
        if (NearestOf(g.NavPoint, 1 - g.Side, BattleEngine.HasLaser(l.U)) is { } u) g.NearestList.Add(u);
    }

    /// <summary>The active unit of <paramref name="side"/> and type with the smallest squared distance (the first in world list order wins ties).</summary>
    internal TacticalUnit? NearestOf(V3 at, int side, bool fighters)
    {
        TacticalUnit? best = null;
        double d = double.MaxValue;
        foreach (var u in _units.Where(u => u.Side == side && u.Active && u.Fighter == fighters))
            if ((u.Position - at).LengthSquared < d) (best, d) = (u, (u.Position - at).LengthSquared);
        return best;
    }

    /// <summary>
    /// Missions 4 / 5 step: nothing when done or when the scorer's skip test holds; else the first wanted unit of the side's
    /// candidate list of type 1 (mission 4) or 0 (mission 5) becomes the sole UI target, done is set, and the replace-targets message is sent
    /// (which clears done when it runs).
    /// </summary>
    void AttackStep(TacticalGroup g)
    {
        var o = g.Object!;
        if (o.Done || Skip(g)) return;
        var type = o.Number == TacticalMissionNumber.AttackFighters ? TacticalUnitType.Fighter : TacticalUnitType.CapitalShip;
        if (_candidates[g.Side].FirstOrDefault(u => u.Type == type && Wanted(u)) is not { } t) return;
        o.Done = true;
        Order(g, TacticalMessage.ReplaceTargets, _byUnit[t]);
    }

    // ---- hooks ----

    /// <summary>
    /// Side centroids, run every frame from the end test: the last frame's results become the centroids, then each side's sum
    /// over its state-2 units is divided by the count of its units in states 2, 4, 6, 7, 8, 10 (zero with count 0; as read).
    /// </summary>
    void Centroids()
    {
        for (int s = 0; s < 2; s++)
        {
            _centroid[s] = _fresh[s];
            var sum = V3.Zero;
            foreach (var u in _units.Where(u => u.Side == s && u.State == TacticalUnitState.Active)) sum += u.Position;
            int n = _units.Count(u => u.Side == s && u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered or TacticalUnitState.Accelerating or TacticalUnitState.InHyperspace or TacticalUnitState.DeathStarRun);
            _fresh[s] = n == 0 ? V3.Zero : sum * (1.0 / n);
        }
    }

    /// <summary>The hook pickers, once per object (while its second flag is clear), with the enemy centroid.</summary>
    void Hook(TacticalGroup g) => Hook(g, _centroid[1 - g.Side]);

    /// <summary>
    /// Hook picker: N = nav point, d = C - N; D = Left (-d.z, d.y, d.x), Right (d.z, d.y, -d.x), Hammer (d.x, d.z, -d.y),
    /// Anvil (d.x, d.z, d.y); a zero D becomes the negated forward of the first target's side, normalised. T = the simulation
    /// target with the largest position * D above 0, else the enemy unit (active, not state 6) with the largest position * D,
    /// scanning the capital-ship tree then the fighter tree in tree order (Inferred: one running maximum, strictly larger wins).
    /// No T gives mission 0. Q = N + 0.75 * R(T - N), with Left plus pi/8 and Right minus pi/8 about y, and Hammer minus pi/8
    /// and Anvil plus pi/8 about x. The nearest of the 105 nav points to Q (strictly smaller wins) becomes the sole UI nav
    /// list entry, the replace-nav-points message is sent, and both flags are set.
    /// Called only with LeftHook, RightHook, Hammer or Anvil.
    /// </summary>
    internal void Hook(TacticalGroup g, V3 c)
    {
        var o = g.Object!;
        TacticalMissionNumber m = o.Number;
        o.Scratch = 0;
        var n = g.NavPoint;
        var d = c - n;
        V3 dir = m switch { TacticalMissionNumber.LeftHook => (-d.Z, d.Y, d.X), TacticalMissionNumber.RightHook => (d.Z, d.Y, -d.X), TacticalMissionNumber.Hammer => (d.X, d.Z, -d.Y), _ => (d.X, d.Z, d.Y) };
        // Inferred: with no target, use the enemy side's forward
        if (dir.IsZero) dir = -Forward(TargetList(g).FirstOrDefault()?.Side ?? 1 - g.Side);
        dir = dir.Normalised();
        TacticalUnit? t = null;
        double best = 0;
        foreach (var u in TargetList(g))
            if (u.Position.Dot(dir) > best) (t, best) = (u, u.Position.Dot(dir));
        if (t is null)
        {
            best = double.NegativeInfinity;
            foreach (var u in TreeOrder(false).Concat(TreeOrder(true)).Where(u => u.Side != g.Side && u.Active && u.State != TacticalUnitState.JumpOrdered))
                if (u.Position.Dot(dir) > best) (t, best) = (u, u.Position.Dot(dir));
        }
        if (t is null)
        {
            SetMission(g, TacticalMissionNumber.None);
            return;
        }
        var v = t.Position - n;
        double th = m is TacticalMissionNumber.LeftHook or TacticalMissionNumber.Anvil ? Math.PI / 8 : -Math.PI / 8;
        var r = m is TacticalMissionNumber.LeftHook or TacticalMissionNumber.RightHook ? v.RotateY(th) : new V3(v.X, v.Y * Math.Cos(th) + v.Z * Math.Sin(th), v.Z * Math.Cos(th) - v.Y * Math.Sin(th));
        var q = n + r * 0.75;
        var p = _navPoints.MinBy(x => (x.Position - q).LengthSquared)!;
        OrderNav(g, TacticalMessage.ReplaceNavPoints, p);
        o.Started = o.Hooked = true;
    }

    /// <summary>A spatial tree in tree order (x, then key).</summary>
    IEnumerable<TacticalUnit> TreeOrder(bool fighters) => _units.Where(u => InTree(u) && u.Fighter == fighters).OrderBy(u => u.Position.X).ThenBy(u => u.Key);

    // ---- recover ----

    /// <summary>
    /// Recover step: per active member without a reservation, its own carrier; else the first active ship of the side's
    /// ship list with a free place; else (side index 1) a live Death Star scene. When none is found, the "no carrier" voice
    /// plays once. When every hangar is full and empty, once: if not withdrawing, the local side's fighter button comes back
    /// and the mission becomes 0; if withdrawing, the members without a reservation scuttle (the scuttle message). Members with a
    /// carrier within 2.0 of its hangar's launch point (in 3-D) dock (the fighter-dock message); when all are docked, the recovered voice
    /// plays. Orphans (carrier none after its death) never dock (as read).
    /// </summary>
    void Recover(TacticalGroup g)
    {
        var o = g.Object!;
        var scene = Sides[1].Scene;
        foreach (var f in g.Members.ToList())
        {
            if (!f.Active) continue;
            bool r = f.Recovery != TacticalRecovery.None, allFull = true;
            if (!r && f.U.Carrier is { } c)
                r = c.Type == TacticalUnitType.DeathStarScene ? BattleEngine.Alive(scene) && Reserve(BattleEngine.Resolve(c, Sides)!, f) : IsActive(c) && Reserve(c, f);
            if (f.Recovery == TacticalRecovery.None)
            {
                foreach (var s in Sides[f.Side].Ships.Where(IsActive).ToList())
                {
                    if (Reserve(s, f)) { r = true; break; }
                    allFull = FullEmpty(s) && allFull;
                }
                if (!r && f.Side == 1 && BattleEngine.Alive(scene))
                {
                    r = Reserve(scene!, f);
                    allFull = FullEmpty(scene!) && allFull;
                }
            }
            if (!r && !o.Started)
            {
                o.Started = true;
                Ui.Voices.Add((g.Side == 0 ? TacticalVoice.FighterGroupNoCarrier : TacticalVoice.FighterGroupNoCarrierEmpire) + g.Colour); // "no carrier" voice
            }
            if (!r && allFull && o.Scratch == 0)
            {
                o.Scratch = 1;
                if (_state[g.Side] == TacticalSideState.Fighting)
                {
                    if (g.Side == Local && g.Colour >= 0) Ui.Fighters[g.Colour] = g;
                    SetMission(g, TacticalMissionNumber.None);
                }
                else
                    foreach (var x in g.Members.Where(x => x.Recovery == TacticalRecovery.None)) Post(Stamp, TacticalMessage.Scuttle, () => Scuttle(x));
            }
        }
        foreach (var f in g.Members.Where(f => f.Recovery == TacticalRecovery.CarrierAssigned).ToList())
        {
            if (BattleEngine.Resolve(f.U.Carrier, Sides) is not { } h || (f.Position - LaunchPoint(HangarOf(h))).Length >= 2.0) continue; // the Death Star scene's point is (-W/2, 0, 0)
            f.Recovery = TacticalRecovery.Docking;
            Post(Stamp, TacticalMessage.FighterDock, () => DockMessage(f, h));
        }
        if (g.Members.Count > 0 && g.Members.All(f => f.Recovery == TacticalRecovery.Docked) && !o.Started)
        {
            o.Started = true;
            Ui.Voices.Add((g.Side == 0 ? TacticalVoice.FighterGroupRecovered : TacticalVoice.FighterGroupRecoveredEmpire) + g.Colour); // "group recovered" voice
        }
    }

    // ---- tactical targeting objects ----

    /// <summary>
    /// Candidate build: the other side's ship list (a wanted unit goes to C, else an active one to U), its second list's units
    /// active and not in state 10 (wanted goes to C, else U), and its pending units in state 1 or 3 go to C. Each group is sorted
    /// by negative threat (tactical) ascending, ties by key; U is appended after C.
    /// </summary>
    internal void BuildCandidates(int i)
    {
        var other = Sides[1 - i];
        List<BattleEngine.Unit> Sort(IEnumerable<BattleEngine.Unit> units) => [.. units.OrderBy(u => -Threat(u)).ThenBy(u => u.Key)];
        var listed = other.Ships.Concat(other.Fighters.Where(u => u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered)).ToList();
        var c = Sort(listed.Where(Wanted).Concat(Sides[0].Pending.Where(u => u.Side == other.Index && u.State is TacticalUnitState.Docked or TacticalUnitState.Launching)));
        c.AddRange(Sort(listed.Where(u => !Wanted(u) && u.State is TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered)));
        _candidates[i] = c;
    }

    /// <summary>
    /// Scorer kind 2 threat: the automatic formula with the same passenger records (the setup's records held in the world
    /// lists, as the battle's own lists hold them); the fire-record term is always 0 (as read).
    /// </summary>
    static double Threat(BattleEngine.Unit u) => BattleEngine.Threat(u);

    /// <summary>
    /// Wanted test: active and, with the side that targets the unit: not withdrawing means weapon rate != 0; withdrawing means
    /// the unit can jump, or its working hyperdrives != 0 and its engines are not out (SpeedCH >= 4 or engine-down >= 10000.0).
    /// </summary>
    bool Wanted(BattleEngine.Unit u)
    {
        if (u.State is not (TacticalUnitState.Active or TacticalUnitState.Recovering or TacticalUnitState.JumpOrdered)) return false;
        if (_state[1 - u.Side] == TacticalSideState.Fighting) return BattleEngine.WeaponRate(u) != 0;
        return CanJump(_byUnit[u]) || u.Hyperdrives - u.HyperCh != 0 && !(u.SpeedCh >= 4 || u.EngineDown >= 10000.0);
    }

    /// <summary>
    /// Time to kill (shared): acc += the group's rate on t; max(1, 0.65 * hull + shield) / (acc - R(t)) when positive, plus
    /// the distance term max(1, |t - nav point| / group speed) * 0.001 when t is not at the nav point (no speed gives 1); else -1.
    /// </summary>
    internal double TimeToKill(BattleEngine.Unit t, BattleEngine.Group g, ref double acc)
    {
        acc += BattleEngine.GroupRate(g, t, Sides);
        double value = Math.Max(1, 0.65 * t.Hull + t.Shield), net = acc - BattleEngine.ShieldRate(t);
        if (net <= 0) return -1;
        double ttk = value / net;
        var tg = _byGroup[g];
        var gap = _byUnit[t].Position - tg.NavPoint;
        if (!gap.IsZero)
        {
            double s = tg.Nav?.GroupSpeed ?? 0;
            ttk += (s > 0 ? Math.Max(1, gap.Length / s) : 1) * 0.001;
        }
        return ttk;
    }

    /// <summary>Skip test: mission 2, 3 or 6; else the automatic rule over the side's group list.</summary>
    bool Skip(TacticalGroup g) => g.Mission is TacticalMissionNumber.Recover or TacticalMissionNumber.Withdraw or TacticalMissionNumber.AttackDeathStar || BattleEngine.SkipRule(g.G, Sides[g.Side].Groups, Wanted, TimeToKill);

    /// <summary>
    /// Targeting with the tactical objects: the shared skeleton over the side's candidate list and group list. Evaluator kind 2
    /// is the automatic evaluator, whose assignment sets the UI target list at once and posts the replace-targets message (applied four turns later).
    /// </summary>
    internal void Targeting(int i) =>
        BattleEngine.Targeting(_candidates[i], Sides[i].Groups, new BattleEngine.Scorer(g => Skip(_byGroup[g]), Wanted, TimeToKill,
            (g, t) => Order(_byGroup[g], TacticalMessage.ReplaceTargets, _byUnit[t])), Sides);

    // ---- the computer side ----

    /// <summary>
    /// Group rebuild on the first targeting after a switch to computer control: group-delete message for every task force of the side's
    /// group list (local side: its HUD slot freed at once); then the builder on the setup-time lists (not re-sorted; lo / hi and
    /// g / stride as in the setup): a leader creates a task force (task-force leave and create messages; local side: HUD slot lo + i), a member joins it
    /// (task-force leave and join messages). Nobody is moved; fighter groups stay.
    /// </summary>
    void Rebuild(int i)
    {
        foreach (var g in _groups.Where(g => g.Side == i && !g.Fighters && Sides[i].Groups.Contains(g.G)).ToList())
        {
            if (i == Local) Ui.Free(g);
            Post(Stamp, TacticalMessage.GroupDelete, () => DeleteGroup(g));
        }
        var (heavy, laser, unarmed) = _setupLists[i];
        int n1 = Build(heavy, 0, 3), n2 = Build(laser, n1, n1 + 3);
        Build(unarmed, n1 + n2, 7);

        int Build(List<BattleEngine.Unit> list, int lo, int hi)
        {
            int n = list.Count;
            if (n == 0) return 0;
            int count = BattleEngine.GroupCount(n, lo, hi), stride = n / count, made = 0;
            TacticalGroup? current = null;
            for (int j = 0; j < n; j++)
            {
                var t = _byUnit[list[j]];
                if (BattleEngine.GroupStart(j, stride, made, count))
                {
                    current = CreateTaskForce(t);
                    if (i == Local && lo + made < 8) Ui.TaskForces[lo + made] = current;
                    made++;
                }
                else JoinTaskForce(current!, t);
            }
            return made;
        }
    }

    /// <summary>
    /// Group-delete message (delete a group): its members leave it, it leaves the side's group list and the world (its mission object
    /// with it); a HUD slot still holding it is freed. Units still in its UI list keep a stale group field.
    /// </summary>
    void DeleteGroup(TacticalGroup g)
    {
        if (!_groups.Contains(g)) return;
        foreach (var u in g.Members.ToList()) Leave(u);
        g.UiMemberList.Clear();
        g.Deleted = true;
        Sides[g.Side].Groups.Remove(g.G);
        _groups.Remove(g);
        g.Object = null;
        Ui.Free(g);
    }
}
