namespace OpenSWR.Sim;

/// <summary>A tactical world vector (x, y, z; y up). Normalising leaves a zero vector unchanged.</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static readonly V3 Zero = new(0, 0, 0);
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
    public static V3 operator *(V3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static implicit operator V3((double X, double Y, double Z) t) => new(t.X, t.Y, t.Z);
    public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public double LengthSquared => Dot(this);
    public double Length => Math.Sqrt(LengthSquared);
    public bool IsZero => X == 0 && Y == 0 && Z == 0;
    public V3 Normalised() => IsZero ? this : this * (1 / Length);
    /// <summary>Rotation about y by theta: (x, z) to (x cos theta - z sin theta, x sin theta + z cos theta).</summary>
    public V3 RotateY(double theta) => new(X * Math.Cos(theta) - Z * Math.Sin(theta), Y, X * Math.Sin(theta) + Z * Math.Cos(theta));
}

public sealed partial class TacticalUnit
{
    /// <summary>
    /// Unit key of the tactical world: the engine key (1, 2, ... per side), side index 1's + 1000000 (Inferred: keys
    /// >= 1000000 belong to side index 1). Orders the spatial trees and the vertical tie-break.
    /// </summary>
    public int Key { get; internal set; }
    /// <summary>Facing (unit vector; a capital ship's has y = 0).</summary>
    public V3 Facing { get; set; }
    /// <summary>Half height and size: from the model's _MEDIUM box (<see cref="TacticalBattle.SetBox"/>), else the constructor defaults.</summary>
    public double HalfHeight { get; internal set; }
    public double Size { get; internal set; }
    /// <summary>The _MEDIUM model box extents (dx, dy, dz; used by the follow camera and the selection box); before <see cref="TacticalBattle.SetBox"/> Inferred (size, 2 * half height, size).</summary>
    public V3 Box { get; internal set; }
    /// <summary>Turn rate (x component of (r, 0, 0), rad/s), set once at construction.</summary>
    public double TurnRate { get; internal set; }
    /// <summary>TractorBeamPower (capital ships).</summary>
    internal double TractorPower;
    /// <summary>The units holding this one in a tractor beam (a dead or withdrawn holder stays, as read).</summary>
    internal List<TacticalUnit> Holders { get; } = [];
    /// <summary>Desired facing (the last result of the facing step).</summary>
    public V3 DesiredFacing { get; internal set; }
    /// <summary>Desired vector (the last result of the desired-vector step).</summary>
    public V3 Desired { get; internal set; }
    /// <summary>Velocity (units per second).</summary>
    public V3 Velocity { get; internal set; }
    /// <summary>Floor and ceiling of the last frame update.</summary>
    internal double Floor, Ceiling;
    /// <summary>Blocked in the last frame update.</summary>
    internal bool Blocked;
    /// <summary>Sidestep time (world clock) and override index.</summary>
    internal double SideTimer;
    internal int OverrideIndex;
    /// <summary>Hyperspace timer (ms in state 7).</summary>
    internal double JumpTimer;
}

/// <summary>Formation navigator of a group: kind 1 sphere, 2 line, 3 fighter.</summary>
internal sealed class Navigator
{
    public Navigator(int kind, int founderKey) =>
        (Kind, Heading) = (kind, founderKey < 1000000 ? new V3(0, 0, 1) : new V3(0, 0, -1)); // Inferred: keys >= 1000000 = side index 1
    public int Kind;
    /// <summary>Heading and desired heading.</summary>
    public V3 Heading, Desired;
    /// <summary>Nav point, the previous turn's nav point, its velocity, last move direction.</summary>
    public V3 Point, Previous, Velocity, LastMove;
    /// <summary>Turn-rate vector (its length is the turn rate of the formation).</summary>
    public V3 TurnRate;
    /// <summary>Slot records and override records (sidesteps): offset per member.</summary>
    public readonly Dictionary<TacticalUnit, V3> Slots = [], Overrides = [];
    public TacticalUnit? Leader;
    /// <summary>Group speed, member count, largest count seen, stuck counter, slot spacing.</summary>
    public double GroupSpeed, Spacing = 12.0;
    public int Count, MaxCount, Stuck;

    /// <summary>Slot offset of a member (none: 0).</summary>
    public V3 Slot(TacticalUnit u) => Slots.TryGetValue(u, out var o) ? o : V3.Zero;

    /// <summary>Rotation of every slot and override offset about y.</summary>
    public void Rotate(double theta)
    {
        foreach (var k in Slots.Keys.ToList()) Slots[k] = Slots[k].RotateY(theta);
        foreach (var k in Overrides.Keys.ToList()) Overrides[k] = Overrides[k].RotateY(theta);
    }
}

/// <summary>Range object of a group: mode 1 or mode 2 = the weapon band.</summary>
internal sealed class RangeBand(int mode)
{
    public int Mode { get; } = mode;
    /// <summary>Distance, band low and high, value.</summary>
    public double Distance = 5.0, Lo, Hi = 5.0, Value;
}

public sealed partial class TacticalGroup
{
    internal Navigator? Nav;
    /// <summary>Simulation nav-point list (nav objects; their position).</summary>
    internal List<TacticalNavPoint> NavPoints { get; } = [];
    public IReadOnlyList<TacticalNavPoint> Waypoints => NavPoints;
    /// <summary>The formation anchor (the navigator's nav point).</summary>
    public V3 NavPoint => Nav?.Point ?? V3.Zero;
    /// <summary>Formation: 1 sphere, 2 line, 3 fighter.</summary>
    public int Formation => Nav?.Kind ?? 0;
    /// <summary>Requested and applied acceleration mode (2 at creation).</summary>
    internal int RequestedMode = 2, AppliedMode = 2;
    internal RangeBand Range = new(2); // Inferred: the mode-2 object at creation (both modes 2)
    /// <summary>Remote-group flag (the single-player registration writes 0), and the target-lost notification flag.</summary>
    internal bool Hold, Notified;
    /// <summary>Fighter group orbit angle.</summary>
    internal double Orbit;
}

public sealed partial class TacticalBattle
{
    /// <summary>Unit sizes before the model box: capital ship half height 3.0, size 3.6 (19.0 for type 0x15); fighter 0.3 / 1.2.</summary>
    void Kinematics(GameState game, TacticalUnit t)
    {
        var u = t.U;
        t.Key = u.Key + 1000000 * u.Side; // Inferred
        bool ssd = u.Type == 0 && u.TypeIndex == 0x15;
        (t.HalfHeight, t.Size) = u.Type == 1 ? (0.3, 1.2) : (3.0, ssd ? 19.0 : 3.6);
        t.Box = new V3(t.Size, 2 * t.HalfHeight, t.Size);
        if (u.Type == 0)
        {
            double Sd(Data.Dat.SdField field) => game.Definitions.Resolve(u.Object.Type) is { } r ? Data.Dat.SdFields.GetOrZero(r, field) : 0;
            (t.TractorPower, t.TractorRange, t.DamageControl) = (Sd(Data.Dat.SdField.TractorBeamPower), Sd(Data.Dat.SdField.TractorBeamRange), Sd(Data.Dat.SdField.DamageControl));
            t.CritTotal = u.SpeedCh + u.ShieldCh + u.HyperCh + u.TractorCh; // without WeaponCH
        }
        // Turn rate: M evaluated once with the bonus 1.0 (Inferred: the officer records are registered after the ship's ctor)
        double m = Maneuverability(t, 1.0);
        t.TurnRate = m > 1 ? m * Math.PI / 18 : Math.PI / 12;
        if (ssd) t.TurnRate = Math.Min(t.TurnRate, Math.PI / 60);
    }

    /// <summary>
    /// The model box at battle start (GetBox of mesh id + 1): half height max(0.5 * dy, 0.5), size
    /// max(sqrt(dx^2 + dz^2), 0.5). The front end calls it with the _MEDIUM mesh's extents before the first frame.
    /// </summary>
    public void SetBox(TacticalUnit t, double dx, double dy, double dz) =>
        (t.HalfHeight, t.Size, t.Box) = (Math.Max(0.5 * Math.Abs(dy), 0.5), Math.Max(Math.Sqrt(dx * dx + dz * dz), 0.5), new V3(Math.Abs(dx), Math.Abs(dy), Math.Abs(dz)));

    /// <summary>Unit active: state 2, 4 or 6.</summary>
    static bool IsActive(BattleEngine.Unit? u) => u is { State: 2 or 4 or 6 };

    /// <summary>Admiral bonus A: the side's admiral value while the admiral's ship is active (2, 4 or 6), else 1.0.</summary>
    double Admiral(int side) => IsActive(Sides[side].AdmiralShip) ? Sides[side].AdmiralValue : 1.0;

    /// <summary>Commander bonus C: the side's commander value while the commander's ship is active, else 1.0.</summary>
    double Commander(int side) => IsActive(Sides[side].CommanderShip) ? Sides[side].Commander : 1.0;

    /// <summary>
    /// Speed: 0 while the engines are down (engine-down value not zero); else
    /// v = bonus + base speed, speed = v - (v * 0.25 * SpeedCH + drag), 0 unless &gt; 0. Drag = sum over the holders of
    /// max(0, P * hf - P * 0.25 * TractorCH).
    /// </summary>
    public double Speed(TacticalUnit t) => Speed(t, t.Fighter ? Commander(t.Side) : Admiral(t.Side));

    double Speed(TacticalUnit t, double bonus)
    {
        var u = t.U;
        if (u.EngineDown != 0) return 0;
        double drag = 0;
        foreach (var h in t.Holders) drag += TractorPowerOf(h); // no state test on the holders
        double v = bonus + u.BaseSpeed, speed = v - (v * 0.25 * u.SpeedCh + drag);
        return speed > 0 ? speed : 0;
    }

    /// <summary>
    /// Maneuverability: 1.0 at speed 0; capital ship min(9, A + SD Maneuverability), fighter
    /// min(9, SD Maneuverability + 1) (the commander bonus is computed and discarded).
    /// </summary>
    public double Maneuverability(TacticalUnit t) => Maneuverability(t, t.Fighter ? Commander(t.Side) : Admiral(t.Side));

    double Maneuverability(TacticalUnit t, double bonus) =>
        Speed(t, bonus) == 0 ? 1.0 : Math.Min(9, t.U.Maneuverability + (t.Fighter ? 1 : bonus));

    /// <summary>Can jump: hyperdrive count - hyperdrive crits &gt; 0 and speed != 0.</summary>
    public bool CanJump(TacticalUnit t) => t.U.Hyperdrives - t.U.HyperCh > 0 && Speed(t) != 0;
}

public sealed partial class TacticalBattle
{
    // ---- frames and spatial trees ----

    /// <summary>
    /// openswr design (not original): the frame update runs at this fixed step, 20 per turn, so a battle is reproducible for
    /// one seed; the original integrates the real frame time.
    /// </summary>
    public const double FrameMs = 50;
    const int Latency = 3; // the world's latency in turns
    static readonly double Bound = (float)(2.0 / 3); // the clamp constants are float( -/+ 2/3)

    /// <summary>Side forward vector: side index 0 faces +z, side index 1 -z.</summary>
    static V3 Forward(int side) => new(0, 0, side == 0 ? 1 : -1);

    /// <summary>
    /// One frame update of <paramref name="dt"/> ms: the group per-frame updates (side 0's groups, then
    /// side 1's), then every unit of the world list in state 2 (taken out of its tree, frame update, each coordinate clamped to
    /// +/- (2/3)*W, put back), a unit in state 7 runs the hyperspace timer.
    /// </summary>
    internal void Frame(double dt)
    {
        foreach (var u in _units) u.Previous = u.Position;
        foreach (var g in GroupsInOrder())
        {
            if (g.Nav is null) continue;
            if (g.Fighters) FighterGroupFrame(g, dt);
            else TaskForceFrame(g, dt);
        }
        double lim = Size * Bound;
        foreach (var t in _units)
        {
            if (t.State == 2)
            {
                if (t.Fighter) FighterFrame(t, dt);
                else ShipFrame(t, dt);
                var p = t.Position;
                t.Position = new V3(Math.Clamp(p.X, -lim, lim), Math.Clamp(p.Y, -lim, lim), Math.Clamp(p.Z, -lim, lim));
            }
            else if (t.State == 7) Hyperspace(t, dt);
        }
    }

    /// <summary>Side 0's groups, then side 1's, in creation order; groups without members are skipped.</summary>
    List<TacticalGroup> GroupsInOrder() => [.. _groups.Where(g => g.Side == 0 && g.Members.Count > 0), .. _groups.Where(g => g.Side == 1 && g.Members.Count > 0)];

    /// <summary>In a spatial tree: the units of the world unit list (setup: not docked; launch adds; world remove takes out).</summary>
    static bool InTree(TacticalUnit t) => t.InWorld && !t.Hidden; // a run member (state 10) stays in the unit list but leaves its tree

    /// <summary>
    /// Tree walk: the capital-ship tree or the fighter tree, ordered by (x, key);
    /// from the unit's own place first toward lower x until the first entry <paramref name="beyond"/> the bound, then toward higher
    /// x likewise; never the unit itself. The trees are kept implicitly (sorted on each query): a unit is out of its tree only while
    /// its own frame update runs, and that is the query's own unit.
    /// </summary>
    IEnumerable<TacticalUnit> Walk(TacticalUnit t, bool fighters, Func<TacticalUnit, bool> beyond)
    {
        var tree = _units.Where(x => x.Fighter == fighters && x != t && InTree(x)).OrderBy(x => x.Position.X).ThenBy(x => x.Key).ToList();
        int Compare(TacticalUnit x) => x.Position.X != t.Position.X ? x.Position.X.CompareTo(t.Position.X) : x.Key.CompareTo(t.Key);
        var lower = tree.Where(x => Compare(x) < 0).Reverse().TakeWhile(x => !beyond(x));
        var higher = tree.Where(x => Compare(x) > 0).TakeWhile(x => !beyond(x));
        return lower.Concat(higher);
    }

    /// <summary>Reach: |velocity| * 0.5 + size * 0.5556.</summary>
    static double Reach(TacticalUnit u) => u.Velocity.Length * 0.5 + u.Size * 0.5556;

    /// <summary>
    /// Neighbour query on the capital-ship tree: other capital ships with |dx| <= R_N + R_own, |dy| <= N.h + h + 0.4 * q0
    /// and |dz| <= R_N + R_own, in walk order, whatever their state. Inferred: each walk ends at the first entry whose |dx| exceeds
    /// its own R_N + R_own.
    /// </summary>
    internal List<TacticalUnit> Neighbours(TacticalUnit t, double q0)
    {
        double own = Reach(t);
        return [.. Walk(t, false, x => Math.Abs(x.Position.X - t.Position.X) > Reach(x) + own)
            .Where(x => Math.Abs(x.Position.Y - t.Position.Y) <= x.HalfHeight + t.HalfHeight + 0.4 * q0
                && Math.Abs(x.Position.Z - t.Position.Z) <= Reach(x) + own)];
    }

    /// <summary>Overlap test: |dy| &lt; h1 + h2 and horizontal distance &lt; (size1 + size2) * 0.5.</summary>
    static bool Overlaps(TacticalUnit a, IEnumerable<TacticalUnit> others) => others.Any(b =>
    {
        double dx = b.Position.X - a.Position.X, dz = b.Position.Z - a.Position.Z;
        return Math.Abs(b.Position.Y - a.Position.Y) < a.HalfHeight + b.HalfHeight && Math.Sqrt(dx * dx + dz * dz) < (a.Size + b.Size) * 0.5;
    });

    /// <summary>Vertical classification: above A / below B, ceiling / floor, CV / FV.</summary>
    sealed class Vertical
    {
        public bool A, B, CV, FV;
        public double Floor = double.MinValue, Ceiling = double.MaxValue;
    }

    /// <summary>
    /// Neighbours above (A, ceiling, CV), below (B, floor, FV), then the level ones by the key tie-break; with both
    /// marks set, more room below (y - floor &gt; ceiling - y) makes the neighbour count as above, A and B unchanged.
    /// </summary>
    static Vertical Classify(TacticalUnit t, List<TacticalUnit> near)
    {
        var v = new Vertical();
        double y = t.Position.Y, h = t.HalfHeight;
        void Above(TacticalUnit n)
        {
            v.Ceiling = Math.Min(v.Ceiling, n.Position.Y - n.HalfHeight - h);
            if (y > v.Ceiling) v.CV = true;
        }
        void Below(TacticalUnit n)
        {
            v.Floor = Math.Max(v.Floor, n.Position.Y + n.HalfHeight + h);
            if (y < v.Floor) v.FV = true;
        }
        var level = new List<TacticalUnit>();
        foreach (var n in near)
        {
            double dy = n.Position.Y - y;
            if (dy > 0) { v.A = true; Above(n); }
            else if (dy < 0) { v.B = true; Below(n); }
            else if (!level.Contains(n)) level.Add(n);
        }
        foreach (var n in level)
        {
            bool higher = n.Key > t.Key;
            if (higher ? !v.B : v.A && !v.B) { v.A = true; Above(n); } // higher key: B = 0 gives above; lower: A = 1, B = 0 gives above
            else if (!v.A) { v.B = true; Below(n); } // higher key: B = 1, A = 0 gives below; lower: A = 0 gives below
            else if (y - v.Floor > v.Ceiling - y) Above(n); // both set: more room
            else Below(n);
        }
        return v;
    }

    // ---- groups and navigators ----

    /// <summary>
    /// A navigator for a group: line for a task force, fighter for a fighter group; founded by the first member (heading by its
    /// key, nav point at its position), then every member added in the order it joined.
    /// </summary>
    void Found(TacticalGroup g)
    {
        var members = g.G.Members.Select(m => _byUnit[m]).Where(g.Members.Contains).ToList();
        g.Nav = new Navigator(g.Fighters ? 3 : 2, members.FirstOrDefault()?.Key ?? 0);
        foreach (var u in members) AddToNav(g.Nav, u);
    }

    /// <summary>
    /// Add member: count + 1; largest count; spacing max(the spacing, size * 1.2); the first member sets the nav point and
    /// becomes leader; then the slot record for n = the new count.
    /// </summary>
    static void AddToNav(Navigator n, TacticalUnit u)
    {
        n.Count++;
        n.MaxCount = Math.Max(n.MaxCount, n.Count);
        n.Spacing = Math.Max(n.Spacing, u.Size * 1.2);
        if (n.Slots.Count == 0)
        {
            n.Point = u.Position;
            n.Leader = u;
        }
        n.Slots[u] = SlotOffset(n, n.Count, u);
    }

    /// <summary>Remove member: leader handover to the next member key (Inferred: the next higher key, else the lowest), records dropped, count - 1.</summary>
    static void RemoveFromNav(Navigator n, TacticalUnit u)
    {
        if (!n.Slots.Remove(u)) return;
        n.Overrides.Remove(u);
        n.Count = Math.Max(0, n.Count - 1); // members at speed 0 are not counted (per-turn reset), so removals can outnumber the count
        if (n.Leader == u)
            n.Leader = n.Slots.Keys.Where(k => k.Key > u.Key).MinBy(k => k.Key) ?? n.Slots.Keys.MinBy(k => k.Key);
    }

    static readonly V3[] SphereDirections =
    [
        new(1, -1, 0), new(0, 0, 1), new(0, 0, -1), new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(1, 1, 1),
        new(-1, -1, -1), new(-1, 1, 1), new(1, -1, -1), new(1, -1, 1), new(-1, 1, -1), new(-1, -1, 1), new(1, 1, -1), new(0, -1, -1),
        new(0, -1, 1), new(-1, 1, 0), new(1, 1, 0), new(0, -1, -1), new(0, 1, 1), new(-1, -1, 0),
    ];

    /// <summary>
    /// Slot offsets (desired position = nav point - offset; h = the heading at the add): line, sphere
    /// (absolute directions), fighter (x / y outputs from the decompile only).
    /// </summary>
    internal static V3 SlotOffset(Navigator nav, int n, TacticalUnit u)
    {
        var h = nav.Heading;
        double sigma = n % 2 == 1 ? 1 : -1;
        if (nav.Kind == 1) return SphereDirections[n % 22].Normalised() * (((n / 22) * 0.25 + 1) * 12.5);
        if (n == 1) return V3.Zero;
        if (nav.Kind == 2)
        {
            double s = Math.Max(nav.Spacing, u.Size * 1.2), k = Math.Truncate(sigma * s) * (Math.Max(n, nav.MaxCount) / 2);
            return new V3(-h.Z * k, 0, h.X * k);
        }
        double i = Math.Truncate(sigma * 3 * -1.5), m = 1 - i * (n / 2), a = m * Math.PI;
        double al = a * 2 / 3, be = a * 0.5, ga = a * 0.4;
        double z1 = Math.Sin(al) * h.X + Math.Cos(al) * h.Z, x1 = Math.Cos(al) * h.X - Math.Sin(al) * h.Z;
        double y2 = Math.Sin(be) * z1 + Math.Cos(be) * h.Y, z2 = Math.Cos(be) * z1 - Math.Sin(be) * h.Y;
        return new V3(Math.Sin(ga) * x1 + Math.Cos(ga) * y2, Math.Cos(ga) * x1 - Math.Sin(ga) * y2, z2);
    }

    /// <summary>
    /// Formation change (message 0x0B): 1 sphere, 3 fighter (only with a fighter leader), else 2 line; the navigator is
    /// deleted and every member re-added in member-list order (Inferred: the new navigator is founded by the first member).
    /// </summary>
    internal void ChangeFormation(TacticalGroup g, int kind)
    {
        if (!_groups.Contains(g)) return;
        kind = kind == 1 ? 1 : kind == 3 && g.Members.FirstOrDefault() is { Fighter: true } ? 3 : 2;
        // The new navigator is founded on the simulation leader, then the other members are added
        var founder = g.SimLeader is { } l && g.Members.Contains(l) ? l : g.Members.FirstOrDefault();
        g.Nav = new Navigator(kind, founder?.Key ?? 0);
        if (founder is not null) AddToNav(g.Nav, founder);
        foreach (var u in g.Members) if (u != founder) AddToNav(g.Nav, u);
    }

    /// <summary>A unit leaves its group (destroyed, withdrawn, moved to another group): member lists and navigator.</summary>
    void Leave(TacticalUnit t)
    {
        if (t.Group is not { } g) return;
        g.Members.Remove(t);
        g.G.Members.Remove(t.U);
        if (g.SimLeader == t) g.SimLeader = g.Members.FirstOrDefault(); // lowest other key of the member list (key order), else none
        if (g.G.Members.Count == 0) Sides[g.Side].Groups.Remove(g.G); // an empty group leaves the side's group list
        if (g.Nav is { } n) RemoveFromNav(n, t);
        t.Group = null;
        t.U.Group = null;
    }

    /// <summary>
    /// A unit joins a group in the simulation (messages 0x30 / 0x33): out of its old group, into the member list (key order) and the
    /// navigator. Inferred: a withdrawn or destroyed unit does not join; a group emptied before takes its place in the side's group list again.
    /// </summary>
    void Join(TacticalGroup g, TacticalUnit t, bool leader = false)
    {
        if (t.Group == g || t.State is 9 or 11) return;
        if (leader || g.SimLeader is null) g.SimLeader = t; // set by message 0x30; a join only while the simulation leader is none
        Leave(t);
        if (!Sides[g.Side].Groups.Contains(g.G)) Sides[g.Side].Groups.Add(g.G);
        g.G.Members.Add(t.U);
        int at = g.Members.FindIndex(m => m.Key > t.Key);
        g.Members.Insert(at < 0 ? g.Members.Count : at, t);
        (t.Group, t.U.Group) = (g, g.G);
        if (g.Nav is { } n) AddToNav(n, t);
    }

    // ---- navigator per turn ----

    static readonly V3[] Sidesteps = [new(20, 0, 0), new(-20, 10, 5), new(5, 0, 20), new(0, -10, -20)];

    /// <summary>
    /// Group speed = the slowest moving member's speed, turn rate = the slowest-turning moving member's, sidesteps of
    /// blocked capital ships in a stuck formation (every 12 clock units), the stuck counter. Clock = the stamp base.
    /// </summary>
    void NavTurn(TacticalGroup g)
    {
        var n = g.Nav!;
        double speed = float.MaxValue;
        var rate = new V3(99, 99, 99);
        int count = 0;
        double clock = Stamp;
        foreach (var u in g.Members)
        {
            double s = Speed(u);
            if (s <= 0) continue;
            speed = Math.Min(speed, s);
            if (u.TurnRate < rate.Length) rate = new V3(u.TurnRate, 0, 0);
            count++;
            if (u.Fighter) continue;
            if (u.Blocked && n.Velocity.IsZero && n.Stuck >= 5)
            {
                if (clock - u.SideTimer > 12.0)
                {
                    u.SideTimer = clock;
                    n.Overrides.Remove(u);
                    u.OverrideIndex = (u.Key + 1 + u.OverrideIndex) % 4; // keys are positive
                    n.Overrides[u] = Sidesteps[u.OverrideIndex];
                }
            }
            else if (!u.Blocked && clock - u.SideTimer > 12.0) n.Overrides.Remove(u);
        }
        n.GroupSpeed = speed == float.MaxValue ? 0 : speed;
        n.TurnRate = rate;
        n.Count = count;
        n.Stuck = n.Point == n.Previous ? n.Stuck + 1 : 0;
        n.Previous = n.Point;
    }

    // ---- movers and the turn function ----

    /// <summary>
    /// Turn: sign -1 when h.x*d.z - h.z*d.x + h.y*d.y &lt; 0; f = R * dt / (angle * 1000); f &lt; 1 then h rotated about y by
    /// theta = sign * f * angle and renormalised; else h := d, theta = sign * angle.
    /// </summary>
    internal static (V3 H, double Theta) TurnStep(V3 h, V3 d, double angle, double rate, double dt)
    {
        double sign = h.X * d.Z - h.Z * d.X + h.Y * d.Y < 0 ? -1 : 1;
        double f = rate * dt / (angle * 1000);
        if (f < 1)
        {
            double theta = sign * f * angle;
            return (h.RotateY(theta).Normalised(), theta);
        }
        return (d, sign * angle);
    }

    static double Angle(V3 a, V3 b) => Math.Acos(Math.Clamp(a.Dot(b), -1, 1));

    /// <summary>The kind's mover (line, sphere, fighter): true while moving, false on arrival.</summary>
    internal static bool Move(Navigator n, V3 p, double dt, double r)
    {
        if (n.Kind == 3) return FighterMove(n, p, dt, r);
        bool moving = PointMove(n, p, dt, r);
        if (n.Kind == 2) LineHeading(n, dt);
        else SphereHeading(n, dt);
        return moving;
    }

    /// <summary>Line / sphere nav-point motion: s = 0.95 * group speed, y at 0.4 of it; arrival within r stops the nav point.</summary>
    static bool PointMove(Navigator n, V3 p, double dt, double r)
    {
        double s = 0.95 * n.GroupSpeed;
        var d = p - n.Point;
        if (!d.IsZero && d.Length > r)
        {
            var u = d.Normalised();
            n.Velocity = new V3(u.X * s, u.Y * s * 0.4, u.Z * s);
            n.Point += n.Velocity * (dt * 0.001);
            n.LastMove = u;
            return true;
        }
        n.Velocity = V3.Zero;
        return false;
    }

    /// <summary>
    /// Line heading step: if desired x and z are both 0, desired := heading; else both normalised, a negative dot reverses the heading at
    /// once, an angle >= pi/12 turns the formation (offsets rotate with it); no turn and a stopped nav point gives velocity := (0, 1, 0)
    /// (as read).
    /// </summary>
    static void LineHeading(Navigator n, double dt)
    {
        if (n.Desired.X == 0 && n.Desired.Z == 0) { n.Desired = n.Heading; return; }
        n.Heading = n.Heading.Normalised();
        n.Desired = n.Desired.Normalised();
        if (n.Heading.Dot(n.Desired) < 0) n.Heading = -n.Heading;
        double angle = Angle(n.Heading, n.Desired);
        if (angle != 0 && angle >= Math.PI / 12) TurnFormation(n, angle, dt);
        else if (n.Velocity.IsZero) n.Velocity = new V3(0, 1, 0);
    }

    /// <summary>
    /// Sphere heading step: desired := the last move direction with y := 0 (zero: the heading; still zero: the first member's side
    /// forward; no member: (0, 0, 1)); a negative dot negates the heading's x and z and recomputes it; always turns.
    /// </summary>
    static void SphereHeading(Navigator n, double dt)
    {
        V3 Desired()
        {
            var d = n.LastMove with { Y = 0 };
            if (d.IsZero) d = n.Heading;
            if (d.IsZero) d = n.Leader is { } l ? Forward(l.Side) : new V3(0, 0, 1);
            return d.Normalised();
        }
        n.Desired = Desired();
        if (n.Heading.Dot(n.Desired) < 0)
        {
            n.Heading = new V3(-n.Heading.X, n.Heading.Y, -n.Heading.Z);
            n.Desired = Desired();
        }
        TurnFormation(n, Angle(n.Heading, n.Desired), dt);
    }

    /// <summary>One turn step of the formation at the turn-rate length; slot and override offsets rotate by the same angle.</summary>
    static void TurnFormation(Navigator n, double angle, double dt)
    {
        (n.Heading, double theta) = TurnStep(n.Heading, n.Desired, angle, n.TurnRate.Length, dt);
        n.Rotate(theta);
    }

    /// <summary>Fighter mover: s = group speed, full 3-D; last move vector := d (not normalised); no heading step.</summary>
    static bool FighterMove(Navigator n, V3 p, double dt, double r)
    {
        double s = n.GroupSpeed;
        var d = p - n.Point;
        if (!d.IsZero && d.Length > r)
        {
            var u = d.Normalised();
            n.Point += u * (s * dt * 0.001);
            n.Velocity = u * s;
            n.LastMove = d;
            return true;
        }
        n.Velocity = V3.Zero;
        return false;
    }

    // ---- range band ----

    /// <summary>
    /// Group range band per turn: mode 1 band [0, 2.5]; mode 2 = the weapon band: own members' intervals
    /// against the current target (positive) and the target's against the first member (negative), split into pieces; A from the
    /// pieces beyond the target's max range, best = the largest value among the pieces within the own max range (ties: the later).
    /// </summary>
    internal void UpdateBand(TacticalGroup g)
    {
        var r = g.Range;
        if (r.Mode == 1) { (r.Lo, r.Hi) = (0, 2.5); return; }
        (r.Lo, r.Hi, r.Value, r.Distance) = (0, 0, 0, 5.0);
        if (Current(g) is not { } target || g.Members.Count == 0) return;
        var pieces = Pieces(g, target, out double rt, out double rm);
        double a = 0;
        (double Lo, double Hi, double V)? best = null;
        foreach (var p in pieces)
        {
            if (p.Hi >= a && p.Hi > rt) a = p.Hi - rt;
            if (p.Hi <= rm && (best is null || p.V >= best.Value.V)) best = p;
        }
        if (a > 0)
        {
            r.Distance = a;
            if (best is { } b) (r.Lo, r.Hi, r.Value) = b;
        }
        else if (best is { } b) (r.Distance, r.Lo, r.Hi) = (b.Lo + (b.Hi - b.Lo) / 3, b.Lo, b.Hi);
        if (rm == 0) (r.Distance, r.Lo, r.Hi) = (Math.Max(rt * 1.2, 10), Math.Max(rt * 1.2, 10), 999999);
        if (r.Hi == 0) r.Hi = 10.0;
        r.Lo = Math.Max(r.Lo, 5.0);
    }

    /// <summary>The interval pieces of the weapon band: [lo, hi] with the net value; RT, Rm.
    internal List<(double Lo, double Hi, double V)> Pieces(TacticalGroup g, TacticalUnit target, out double rt, out double rm)
    {
        var list = new List<(double Hi, double V)>();
        rt = BattleEngine.MaxRange(target.U);
        rm = 0;
        foreach (var m in g.Members)
        {
            Intervals(m, target, 1, list);
            rm = Math.Max(rm, BattleEngine.MaxRange(m.U));
        }
        Intervals(target, g.Members[0], -1, list);
        list.RemoveAll(e => e.Hi <= 0); // an entry with hi <= lo is dropped
        var pieces = new List<(double, double, double)>();
        double lo = 0;
        foreach (double hi in list.Select(e => e.Hi).Distinct().Order())
        {
            pieces.Add((lo, hi, list.Where(e => e.Hi >= hi).Sum(e => e.V)));
            lo = hi;
        }
        return pieces;
    }

    /// <summary>
    /// Intervals of U against X: U's arc choice for X's type (4 means none); per weapon with count &gt; 0 in that arc
    /// ([0, range], insertion ion, turbolaser, laser): count * W(U) * base * (X ship ? vs-ship : vs-fighter).
    /// </summary>
    static void Intervals(TacticalUnit u, TacticalUnit x, double sign, List<(double Hi, double V)> list)
    {
        bool ship = !x.Fighter;
        int a = BattleEngine.Arc(u.U, ship);
        if (a == 4) return;
        double w = BattleEngine.WeaponRate(u.U);
        void Add(double count, double range, double factor) { if (count > 0) list.Add((range, sign * count * w * Base * factor)); }
        Add(u.U.Arcs[a, 1], u.U.IonRange, ship ? 1.0 : 0.5);
        Add(u.U.Arcs[a, 2], u.U.TlRange, ship ? 1.0 : 0.5);
        Add(u.U.Arcs[a, 0], u.U.LaserRange, ship ? LaserVsShip : 1.0);
    }

    /// <summary>
    /// FORMATION_ACCELERATE request: the requested mode := mode at once and message 0x3D; the range object, the applied mode and a task
    /// force's formation follow at its dispatch.
    /// </summary>
    void RequestMode(TacticalGroup g, int mode)
    {
        g.RequestedMode = mode;
        Post(Stamp, 0x3D, () => Accelerate(g, mode));
    }

    /// <summary>Message 0x3D: a new range object of the mode, applied mode := mode; a task force changes formation (1 to sphere, 2 to line; Inferred: only when different).</summary>
    void Accelerate(TacticalGroup g, int mode)
    {
        if (!_groups.Contains(g)) return;
        g.Range = new RangeBand(mode);
        g.AppliedMode = mode;
        if (!g.Fighters && g.Nav is { } n && n.Kind != (mode == 1 ? 1 : 2)) ChangeFormation(g, mode == 1 ? 1 : 2);
    }

    // ---- group per frame ----

    /// <summary>
    /// Task force per frame: withdraw (mission 3); else the first nav point (mover r = 1, removed on
    /// arrival); else the first target unless on hold (mission 11): stop inside the band (lo <= L + 2 <= hi); outside in mode 2 too
    /// close, hold while the target's group targets one of ours, else back off along -forward * lo * 1.1, and the computer side's
    /// groups of more than 7 switch to mode 1; then the mover toward nav + v * d (r = 2).
    /// </summary>
    void TaskForceFrame(TacticalGroup g, double dt)
    {
        var n = g.Nav!;
        if (g.Mission == 3) { WithdrawFrame(g, dt); return; }
        if (g.NavPoints.Count > 0)
        {
            var np = g.NavPoints[0];
            n.Desired = (np.Position - n.Point).Normalised();
            if (!Move(n, np.Position, dt, 1)) Reached(g, np);
            return;
        }
        if (Current(g) is not { } target || g.Mission == 11) return;
        if (!target.Active && !g.Notified) TargetLost(g, target); // one-time notification; movement goes on
        var fwd = Forward(g.Side);
        var v = target.Position - n.Point;
        if (v.IsZero) { n.Desired = fwd; v = fwd * -5; }
        else n.Desired = v.Normalised();
        double l = v.Length;
        var r = g.Range;
        if (r.Lo <= l + 2 && l + 2 <= r.Hi) { n.Velocity = V3.Zero; return; }
        if (g.AppliedMode == 2)
        {
            if (l < r.Lo)
            {
                if (target.Group is { } tg && Current(tg) is { } tt && g.Members.Contains(tt)) { n.Velocity = V3.Zero; return; }
                v = fwd * (r.Lo * -1.1);
            }
            if (g.RequestedMode == 2 && !g.Hold && Computer[g.Side] && g.Members.Count > 7) RequestMode(g, 1);
        }
        int c = g.Members.Count;
        double d = g.AppliedMode == 1 ? r.Distance : Math.Max(2.0, r.Lo + (c < 3 ? 0.5 : c < 6 ? 0.75 : 0.9) * (r.Hi - r.Lo));
        Move(n, n.Point + v.Normalised() * d, dt, 2.0);
    }

    /// <summary>
    /// Fighter group per frame: withdraw (mission 3); mission 6 (Death Star run: TacticalDeathStar.cs); nav points with the
    /// fighter mover (r = 1, the nav point set exactly to the waypoint on arrival; an emptied list falls through); else the first
    /// target unless on hold or the notification flag set (target found dead: no movement until 0x35 / 0x36): the mover
    /// toward nav + v * distance without band test (r = 2), arrived: orbit the target at 36 deg/s (side index 1 negates the stored angle
    /// every frame, as read).
    /// </summary>
    void FighterGroupFrame(TacticalGroup g, double dt)
    {
        var n = g.Nav!;
        if (g.Mission == 3) { WithdrawFrame(g, dt); return; }
        if (g.Mission == 6) { RunFrame(g, dt); return; }
        if (g.NavPoints.Count > 0)
        {
            var np = g.NavPoints[0];
            n.Desired = (np.Position - n.Point).Normalised();
            if (FighterMove(n, np.Position, dt, 1)) return;
            n.Point = np.Position;
            Reached(g, np);
            if (g.NavPoints.Count > 0) return; // the list empty after it falls through into the target step
        }
        if (Current(g) is not { } target || g.Mission == 11) return;
        if (g.Notified) return; // notification flag set: no movement until 0x35 / 0x36 clear it
        if (!target.Active) TargetLost(g, target); // the movement below runs for this one frame
        var v = target.Position - n.Point;
        if (v.IsZero) v = Forward(g.Side);
        n.Desired = v.Normalised();
        if (FighterMove(n, n.Point + v.Normalised() * g.Range.Distance, dt, 2.0)) return;
        g.Orbit += dt * Math.PI * 0.0002;
        if (g.Orbit >= 2 * Math.PI) g.Orbit -= 2 * Math.PI;
        if (g.Side == 1) g.Orbit = -g.Orbit;
        double rho = Math.Max(target.Size * 2.4, 1.5), th = g.Orbit;
        n.Point = target.Position + new V3(rho * (Math.Cos(th) - Math.Sin(th)), rho / 2, rho * (Math.Cos(th) + Math.Sin(th)));
    }

    /// <summary>A reached nav point leaves the UI list (when present) and the simulation list at once, without a message (the original's handler there is an empty stub); leaving both: the reached voice.</summary>
    void Reached(TacticalGroup g, TacticalNavPoint p)
    {
        bool ui = g.UiNavList.Remove(p);
        if (g.NavPoints.Remove(p) && ui) ReachedVoice(g);
    }

    /// <summary>
    /// Withdraw movement: heading := desired := -forward; the nav point moves to the own start line (z = W * heading.z * 0.5,
    /// r = 1); arrived and the side's countdown <= 0: the first member (task force) or every member (fighter group) that is active,
    /// can jump, within 8.0 of the line and not in state 6 / 7 / 8 faces the heading, state 6, message 0x2D.
    /// </summary>
    void WithdrawFrame(TacticalGroup g, double dt)
    {
        var n = g.Nav!;
        var fwd = Forward(g.Side);
        n.Heading = n.Desired = -fwd;
        double edge = Size * n.Heading.Z * 0.5;
        if (Move(n, new V3(n.Point.X, n.Point.Y, edge), dt, 1)) return;
        if (_countdown[g.Side] > 0) return;
        foreach (var u in (g.Fighters ? g.Members : g.Members.Take(1)).ToList())
        {
            if (!u.Active || u.State is 6 or 7 or 8 || !CanJump(u) || (u.Position.Z - edge) * fwd.Z > 8.0) continue;
            u.Facing = u.DesiredFacing = n.Heading;
            u.U.State = 6; // the tractor effect hook when held: the front end draws the loop from the holder list
            Post(Stamp, 0x2D, () => JumpOut(u));
        }
    }

    /// <summary>Message 0x2D: active and can jump (re-tested) gives state 7 (hyperspace timer from 0); active but cannot jump gives the scuttle handler at once; not active gives nothing.</summary>
    void JumpOut(TacticalUnit u)
    {
        if (!u.Active) return;
        if (CanJump(u))
        {
            (u.U.State, u.JumpTimer) = (7, 0);
            Sounds.Add(u.Fighter ? 0x1D : 0x1F); // first jump frame
        }
        else Scuttle(u);
    }

    /// <summary>
    /// State 7 per frame: facing := -forward, timer += dt up to 4000 ms; then state 8 and message 0x2E (which leads to:
    /// out of its group, state 9; a carrier takes its launchable fighters along). The drawn push-back is presentation (the front end).
    /// </summary>
    void Hyperspace(TacticalUnit u, double dt)
    {
        u.Facing = -Forward(u.Side);
        u.JumpTimer = Math.Min(4000, u.JumpTimer + dt);
        if (u.JumpTimer < 4000) return;
        u.U.State = 8;
        Post(Stamp, 0x2E, () => Retreat(u));
    }

    // ---- unit frame updates ----

    /// <summary>
    /// Desired vector: no group gives 0; mission 3 gives (0, 0, edge - z) (0 within 4.0 of the line); a fighter of
    /// a mission-2 group with a carrier whose hangar exists gives the hangar's point - position; else the override offset, or nav - slot - position (0 when its squared
    /// length <= 1.0; a fighter's <= 4.0 gives the nav-point velocity instead).
    /// </summary>
    internal V3 DesiredVector(TacticalUnit t)
    {
        if (t.Group is not { Nav: { } n } g) return V3.Zero;
        if (g.Mission == 3)
        {
            double edge = Forward(t.Side).Z * Size * -0.5, dz = edge - t.Position.Z;
            return Math.Abs(dz) <= 4.0 ? V3.Zero : new V3(0, 0, dz);
        }
        if (t.Fighter)
        {
            // Simulation mission 2, a carrier key and its hangar (no alive test) gives
            // the hangar's point (a ship's launch / docking point, the scene's (-W/2, 0, 0))
            if (g.Mission == 2 && t.U.Carrier is { } c && FindHangar(c) is { } h) return LaunchPoint(h) - t.Position;
            var f = n.Point - n.Slot(t) - t.Position;
            return f.LengthSquared <= 4.0 ? n.Velocity : f;
        }
        if (n.Overrides.TryGetValue(t, out var o)) return o;
        var v = n.Point - n.Slot(t) - t.Position;
        return v.LengthSquared <= 1.0 ? V3.Zero : v;
    }

    /// <summary>
    /// Desired facing of a capital ship: mission 3 gives -forward; v != 0 gives v; v = 0 gives the current facing unless
    /// the formation is settled (nav velocity 0, stuck >= 5) on a ship target: then broadside, the target direction turned about y by
    /// a * pi/2 (a = the arc choice against ships, 4 gives 0); y := 0, zero gives current facing, normalised.
    /// </summary>
    V3 ShipFacing(TacticalUnit t, V3 v)
    {
        var g = t.Group;
        V3 d;
        if (g?.Mission == 3) d = -Forward(t.Side);
        else if (!v.IsZero) d = v;
        else if (g?.Nav is not { } n || Current(g) is not { } target || !n.Velocity.IsZero || n.Stuck < 5 || target.Fighter) d = t.Facing;
        else
        {
            int a = BattleEngine.Arc(t.U, true);
            d = (target.Position - t.Position).RotateY((a == 4 ? 0 : a) * Math.PI / 2);
        }
        d = d with { Y = 0 };
        return d.IsZero ? t.Facing.Normalised() : d.Normalised();
    }

    /// <summary>
    /// Capital-ship frame update: speed e capped at the group speed; vertical avoidance (neighbours,
    /// key tie-break, half-gap rule, overlap restore); snap onto the slot; desired facing (broadside when settled); turn above pi/48;
    /// move along the facing at e * c (c = cos of the previous desired-facing error, >= 0); horizontal contact cancels the move.
    /// </summary>
    void ShipFrame(TacticalUnit t, double dt)
    {
        t.Blocked = false;
        double s = Speed(t);
        if (s == 0) return;
        double e = t.Group?.Nav is { } gn ? Math.Min(s, gn.GroupSpeed) : s;
        t.Floor = double.MinValue;
        t.Ceiling = double.MaxValue;
        var v = t.Desired = DesiredVector(t);
        bool blocked = false;
        var near = new List<TacticalUnit>();
        var vert = new Vertical();
        if (!v.IsZero)
        {
            near = Neighbours(t, e * dt * 0.001);
            vert = Classify(t, near);
            (t.Floor, t.Ceiling) = (vert.Floor, vert.Ceiling);
        }
        // 3. vertical step
        double q = 0.4 * e * dt * 0.001, y0 = t.Position.Y, y = y0, h = t.HalfHeight;
        if (vert.CV && vert.FV)
        {
            if (vert.Ceiling - vert.Floor < 2 * h) blocked = true;
            else
            {
                double m = (vert.Ceiling - vert.Floor) * 0.5 - y; // as written: half the gap from 0, not from the floor
                y += Math.Sign(m) * Math.Min(Math.Abs(m), q);
            }
        }
        else if (vert.CV) y -= Math.Min(y - vert.Ceiling, q);
        else if (vert.FV) y += Math.Min(vert.Floor - y, q);
        else if (v.Y != 0)
        {
            double step = Math.Min(Math.Abs(v.Y), q);
            if (v.Y > 0 && vert.A) step = Math.Min(step, vert.Ceiling - y);
            if (v.Y < 0 && vert.B) step = Math.Min(step, y - vert.Floor);
            if (step == 0) blocked = true;
            else y += Math.Sign(v.Y) * step;
        }
        if (y != y0)
        {
            t.Position = t.Position with { Y = y };
            blocked = Overlaps(t, near);
            if (blocked) t.Position = t.Position with { Y = y0 };
        }
        // 4. forward factor from the previous frame's desired facing; the position noted
        double c = Math.Max(0, t.Facing.X * t.DesiredFacing.X + t.Facing.Z * t.DesiredFacing.Z);
        var noted = t.Position;
        bool moved = false;
        // 5. snap onto the slot
        var xz = new V3(v.X, 0, v.Z);
        if (!xz.IsZero && xz.Length <= e * dt * 0.001 && c >= 0.707)
        {
            t.Position += xz;
            moved = true;
            v = t.Desired = DesiredVector(t);
        }
        // 6. desired facing, 7. turn (none within pi/48; the constant is pi * 0.0208333)
        t.DesiredFacing = ShipFacing(t, v);
        double angle = Angle(t.Facing, t.DesiredFacing);
        if (dt != 0 && angle > Math.PI * 0.0208333) t.Facing = TurnStep(t.Facing, t.DesiredFacing, angle, t.TurnRate, dt).H;
        // 8. move along the facing (the velocity is kept when this is skipped)
        if (c != 0 && !t.Desired.IsZero)
        {
            t.Velocity = new V3(t.Facing.X * e * c, 0, t.Facing.Z * e * c);
            t.Position += t.Velocity * (dt * 0.001);
            moved = true;
        }
        // 9. horizontal contact cancels the frame's move (blocked stays)
        if (moved && Overlaps(t, near)) t.Position = noted;
        t.Blocked = blocked;
    }

    /// <summary>
    /// Fighter frame update: desired facing (v, the target, the group heading, side forward) in 3-D; the
    /// turn without minimum angle (yaw by f * the horizontal angle, Inferred, y eased by f); c = 3-D dot >= 0.25; vertical correction
    /// against capital ships only; velocity = facing * speed * c (no group cap, no snap, no overlap restore).
    /// </summary>
    void FighterFrame(TacticalUnit t, double dt)
    {
        t.Floor = double.MinValue;
        t.Ceiling = double.MaxValue;
        var v = t.Desired = DesiredVector(t);
        if (v.IsZero && t.Group?.Mission == 3) return;
        var fallback = t.Group?.Nav is { } gn && !gn.Heading.IsZero ? gn.Heading : Forward(t.Side);
        var d = !v.IsZero ? v : t.Group is { } g && Current(g) is { } target ? target.Position - t.Position : fallback;
        if (d.IsZero) d = fallback;
        t.DesiredFacing = d = d.Normalised();
        double speed = Speed(t), angle = Angle(t.Facing, d);
        if (speed != 0 && angle != 0 && dt != 0)
        {
            double f = t.TurnRate * dt / (angle * 1000);
            if (f < 1)
            {
                var h = t.Facing;
                double hl = Math.Sqrt(h.X * h.X + h.Z * h.Z), dl = Math.Sqrt(d.X * d.X + d.Z * d.Z);
                double yaw = hl == 0 || dl == 0 ? 0 : Math.Acos(Math.Clamp((h.X * d.X + h.Z * d.Z) / (hl * dl), -1, 1));
                double sign = d.X * -h.Z + d.Z * h.X < 0 ? -1 : 1;
                var r = h.RotateY(sign * f * yaw);
                t.Facing = (r with { Y = r.Y + (d.Y - r.Y) * f }).Normalised();
            }
            else t.Facing = d;
        }
        double c = Math.Max(0.25, t.Facing.Dot(d));
        var vert = Classify(t, Neighbours(t, speed * c * dt * 0.001));
        (t.Floor, t.Ceiling) = (vert.Floor, vert.Ceiling);
        double rate = 0.4 * speed * dt * 0.001, y = t.Position.Y;
        if (vert.CV && vert.FV)
        {
            if (vert.Ceiling - vert.Floor >= 2 * t.HalfHeight)
            {
                double m = (vert.Ceiling - vert.Floor) * 0.5 - y;
                y += Math.Sign(m) * Math.Min(Math.Abs(m), rate);
            }
        }
        else if (vert.CV) y -= Math.Min(y - vert.Ceiling, rate);
        else if (vert.FV) y += Math.Min(vert.Floor - y, rate);
        t.Velocity = t.Facing * (speed * c);
        t.Position = (t.Position with { Y = y }) + t.Velocity * (dt * 0.001);
    }

    // ---- orders that move ----

    /// <summary>Order kinds: 0x09 set the nav point, 0x0B formation, 0x3D acceleration mode.</summary>
    public const int SetNavPoint = 0x09, SetFormation = 0x0B, SetMode = 0x3D;

    /// <summary>Message 0x09 (payload group, x, y, z) through the message queue: the nav point := (x, y, z) at once when it runs. No UI path posts it.</summary>
    public bool OrderPoint(TacticalGroup g, V3 p)
    {
        if (Done || !_groups.Contains(g)) return false;
        Post(Stamp, SetNavPoint, () => { if (_groups.Contains(g) && g.Nav is not null) g.Nav.Point = p; });
        return true;
    }

    /// <summary>A value message through the queue: 0x0B formation (1 sphere, 3 fighter with a fighter leader, else line), 0x3D acceleration mode 1 / 2 (the request sets the requested mode at once).</summary>
    public bool OrderValue(TacticalGroup g, int kind, int value)
    {
        if (Done || !_groups.Contains(g)) return false;
        switch (kind)
        {
            case SetFormation: Post(Stamp, kind, () => ChangeFormation(g, value)); return true;
            case SetMode when value is 1 or 2: RequestMode(g, value); return true;
            default: return false;
        }
    }
}
