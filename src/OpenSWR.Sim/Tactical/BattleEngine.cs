using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Automatic space-battle resolution: the abstract damage-rate model of the original's automatic branch,
/// with its own Lehmer generator (seed 12345678 stepped 16 times) per battle. Setup order sets the list order, and so the draws.
/// Fighters aboard that cannot jump start docked and stay pending until side index 1's first update. Attacker groups are
/// made at deploy: fighter formations first, then capital-ship groups from three sorted lists. The step loop runs with dt 1.0,
/// side 2 then side 1, and ends after a 1200 s stalemate measured from the deploy baselines. Candidates are sorted by threat.
/// Each group keeps its target (sticky targeting), with laser and other pools. Targeting draws nothing.
/// Arc choice and rate follow the weapon data. Damage is applied once per group, with two rand(100) critical-hit draws and the
/// crit effects. Destroyed and retreated units leave the lists and groups. Strength uses a per-group left-out rule and a pending
/// term. Withdraw uses the side's decision float. Jump, GravityWell blocker and retreat allowed (v3) decide the rest.
/// Recharge, finish and result follow. The result is a <see cref="BattleResult"/> for <see cref="Combat.ApplyBattle"/>.
/// No game-generator draw happens. Later rule corrections replace the first draft.
/// Death Star: one current scene object per battle (the last registration wins, including the unfinished Death Star of the
/// system's ship manager). It has no unit, candidate, target or strength term. The superlaser fires at the head of side index 1's
/// update, and the Alliance fighter attack runs at the head of side index 0's. The strength bonus, retreat, stall, done and result
/// rules use it. One shared D record is written. Fighters carried by a Death Star use the scene hangar.
/// Fighter-formation balancing uses bookkeeping counts. Units move from the front of a formation (Alliance m units, Empire one
/// per empty slot). Drained formations stay. Empty formations are ordinary groups. Stored unit fields, weapon and shield rates,
/// the rate and group rate, damage application and recharge round to 32-bit float. Threat, TTK, strength and withdraw stay in
/// double precision (Inferred harmless).
/// Inferred: the passenger records are the Usable characters, troop regiments and special forces aboard; the TTK
/// distance term 0; the slots of the fighter classes in type-index order (used classes first); "can travel" for v3;
/// admiral / commander skills = base Leadership / Combat; a carrier's hangar capacity = its SD FighterCapacity; the scene
/// hangar's dock takes the same capacity test.
/// </summary>
public static partial class BattleEngine
{
    const double Dt = 1.0, Stalemate = 1200, Destroy = 100000, Horizon = 180;
    static readonly double Base = 0.26666668f, LaserVsShip = 0.167f, Torpedo = 0.53333336f; // float constants as stored in the binary

    /// <summary>Test hook: the retreat-allowed value v3 per side (default: the autoroute destination search). The test hooks are per thread (parallel test classes).</summary>
    [ThreadStatic] internal static Func<StarSystem, Owner, bool>? RetreatAllowedOverride;
    /// <summary>When set, the battle generator's draws are appended (site = the damaged unit; tests).</summary>
    [ThreadStatic] internal static List<(string Site, int N, int Result)>? Trace;
    /// <summary>When set, one line per step with both sides' strengths, retreat-possible, engagements and units (tests, analyst reports).</summary>
    [ThreadStatic] internal static List<string>? StateTrace;
    /// <summary>When set, every critical hit (step, unit, k) is appended (tests).</summary>
    [ThreadStatic] internal static List<(long Step, GameObject Unit, int K)>? CritTrace;
    /// <summary>One damage application: the unit, its state, d, shield / hull before and after, draws, critical-hit kinds.</summary>
    internal sealed record Application(GameObject Unit, TacticalUnitState State, double D, double Shield0, double Hull0, double Shield1, double Hull1, int Draws, List<int> Crits);
    /// <summary>When set, every damage application is appended, also those on a unit with hull 0 (tests, pinned battles).</summary>
    [ThreadStatic] internal static List<Application>? DamageTrace;
    [ThreadStatic] static long stepNo;

    internal sealed class Unit(GameObject o, int side)
    {
        public GameObject Object { get; } = o;
        public int Side { get; } = side; // 0 = side 1, 1 = side 2
        public TacticalUnitType Type; // in no side list for a Death Star scene
        public int Key; // 1, 2, ... per side in setup order; the tie-break key in sorts
        public int TypeIndex; // the class's place in the engine type table (the string number, see TypeStrings)
        public TacticalUnitState State = TacticalUnitState.Active; // see TacticalUnitState
        public Group? Group; // the attacker group of the unit
        public double Hull, HullMax, Shield, ShieldMax, Wrr, Srr, BaseSrr, Maneuverability, BaseSpeed, Torpedo;
        public double TlRange, IonRange, LaserRange; // SD weapon range fields (Inferred mapping)
        public readonly double[,] Arcs = new double[4, 3]; // fore, starboard, aft, port; each with laser, ion, turbolaser
        public int SpeedCh, ShieldCh, HyperCh, TractorCh, WeaponCh, Hyperdrives;
        public bool Blocker;
        public Unit? Carrier;
        /// <summary>The setup dock found the hangar full and wrote nothing: no counts, no list entry. The fighter constructor sets its carrier.</summary>
        public bool Overflow;
        public int Docked, Capacity; // hangar count and capacity; a Death Star's scene hangar has the same two
        /// <summary>Passenger records carried in setup order: the characters first, then the troop and special-force records whose ship key is this unit's. The tactical battle reads the same records.</summary>
        public readonly List<GameObject> Carried = [];
        public int Passengers => Carried.Count;
        public float Charge; // superlaser charge of a Death Star scene
        public bool Shielded, Unfinished, Withdrawn; // Death Star scene flags: shield facility, unfinished, withdrawn
        // tactical only: arc energy, per-arc ready flags, the recharge queue, the fighter
        // torpedo distance, engine-down and tractor-down times
        public readonly double[] Energy = new double[4];
        public readonly bool[] Ready = new bool[4];
        public readonly List<int> Queue = [];
        public double TorpedoRange, EngineDown, TractorDown;
        /// <summary>Arc capacity = laser + ion + turbolaser counts (a fighter's: int of the scaled sum).</summary>
        public double ArcCapacity(int a) => Math.Truncate(Arcs[a, 0] + Arcs[a, 1] + Arcs[a, 2]);
        public double Hf => HullMax > 0 ? Hull / HullMax : 0;
        public bool Live => State is TacticalUnitState.Active or TacticalUnitState.Withdrawn;
    }

    /// <summary>Attacker group: members (leader = the first), at most one target; type 4 = fighter formation, else 3.</summary>
    internal sealed class Group(bool formation)
    {
        public bool Formation { get; } = formation;
        public List<Unit> Members { get; } = [];
        public Unit? Target;
        public Unit? Leader => Members.Count > 0 ? Members[0] : null; // none for a drained formation
    }

    internal sealed class Side(int index, double threshold, bool retreatAllowed)
    {
        public int Index { get; } = index;
        public double Threshold { get; } = threshold; // withdraw threshold (the controller's decision float)
        public bool RetreatAllowed { get; } = retreatAllowed; // retreat allowed (v3)
        /// <summary>Every unit of the side in setup order; the result reads it.</summary>
        public List<Unit> Units { get; } = [];
        /// <summary>Side lists of non-fighters (ships) and fighters; destroyed / retreated units leave them.</summary>
        public List<Unit> Ships { get; } = [];
        public List<Unit> Fighters { get; } = [];
        /// <summary>Attacker groups of the side.</summary>
        public List<Group> Groups { get; } = [];
        /// <summary>Shared by both sides: docked fighters pending until side index 1's first update.</summary>
        public List<Unit> Pending { get; init; } = [];
        /// <summary>Shared by both sides: every unit in setup order (the finish walk reads it).</summary>
        public List<Unit> Battle { get; init; } = [];
        /// <summary>Shared: the units undocked at deploy, minus destroyed and retreated ones.</summary>
        public List<Unit> Undocked { get; init; } = [];
        /// <summary>The current Death Star scene, kept on side index 1; every Death Star registered (D records).</summary>
        public Unit? Scene;
        public List<GameObject> DeathStars { get; } = [];
        public int Keys; // running key number (the scene takes side index 1's)
        public double Strength, Lowest, Stall;
        public bool Withdrawing;
        public TacticalSideState State;
        public IEnumerable<Unit> Lists => Ships.Concat(Fighters);
        public double Commander = 1.0; // bonus; no state test, survives the ship's loss
        /// <summary>The commander's ship unit (the tactical commander bonus needs it active); null = no record.</summary>
        public Unit? CommanderShip;
        /// <summary>The admiral's ship unit and clamp value ; null = no admiral record.</summary>
        public Unit? AdmiralShip;
        public double AdmiralValue = 1.0;
        /// <summary>Admiral bonus: only while the admiral's ship unit is active (state exactly 2), else 1.0.</summary>
        public double Admiral => AdmiralShip is { State: TacticalUnitState.Active } ? AdmiralValue : 1.0;
    }

    /// <summary>The result of an automatic battle at <paramref name="s"/> (no state change).</summary>
    public static BattleResult Run(GameState game, StarSystem s)
    {
        var rand = new GameRandom(12345678);
        for (int i = 0; i < 16; i++) rand.Rand(2); // the original steps the generator 16 times first
        rand.Trace = Trace;
        var sides = Build(game, s);
        foreach (var side in sides) Deploy(side);
        foreach (var side in sides) side.Lowest = side.Strength = Strength(side, sides); // deploy baselines
        for (stepNo = 1; !Step(sides, rand); stepNo++)
            if (stepNo > 10_000_000) throw new InvalidOperationException("battle did not end");
        Finish(sides);
        return Result(sides);
    }

    /// <summary>
    /// OpenSWR AI battle prediction (not a game rule): the automatic
    /// battle on a detached sandbox system (objects not registered in the game), both thresholds 0 and no retreat (Inferred: the
    /// enemy's float is unknown), drawn from a private generator of <paramref name="seed"/>. No controller is asked, no game
    /// generator draw, no game state change; the test hooks (traces, step counter) are kept as they were.
    /// </summary>
    public static BattleResult Predict(GameState game, StarSystem sandbox, int seed)
    {
        var (trace, state, crit, damage, step) = (Trace, StateTrace, CritTrace, DamageTrace, stepNo);
        (Trace, StateTrace, CritTrace, DamageTrace) = (null, null, null, null);
        try
        {
            var rand = new GameRandom(seed);
            List<Unit> pending = [], battle = [], b64 = [];
            var sides = new[] { new Side(0, 0, false) { Pending = pending, Battle = battle, Undocked = b64 }, new Side(1, 0, false) { Pending = pending, Battle = battle, Undocked = b64 } };
            Setup(game, sandbox, sides);
            foreach (var side in sides) Deploy(side);
            foreach (var side in sides) side.Lowest = side.Strength = Strength(side, sides);
            for (stepNo = 1; !Step(sides, rand); stepNo++)
                if (stepNo > 10_000_000) throw new InvalidOperationException("battle did not end");
            Finish(sides);
            return Result(sides);
        }
        finally
        {
            (Trace, StateTrace, CritTrace, DamageTrace, stepNo) = (trace, state, crit, damage, step);
        }
    }

    /// <summary>
    /// Both sides with their thresholds (the controllers' decision floats) and v3, and the units of setup, shared
    /// by the automatic and the tactical branch (the tactical battle uses the same setup; only the automatic flag differs).
    /// </summary>
    internal static Side[] Build(GameState game, StarSystem s)
    {
        List<Unit> pending = [], battle = [], b64 = [];
        Side MakeSide(int index)
        {
            var owner = index == 0 ? Owner.Alliance : Owner.Empire;
            double f = game.Controllers.TryGetValue(owner, out var c) ? c.BattleFloat(game, s) : 0;
            return new Side(index, f, RetreatAllowedOverride?.Invoke(s, owner) ?? RetreatDestination(game, s, owner)) { Pending = pending, Battle = battle, Undocked = b64 };
        }
        var sides = new[] { MakeSide(0), MakeSide(1) };
        Setup(game, s, sides);
        return sides;
    }

    /// <summary>
    /// Step 1 of the retreat option: the first own capital ship that can travel over the side's Usable fleets in the system
    /// and their Usable capital ships (key order), else the first own fighter directly in the system that can travel; v2 = it
    /// exists. "Can travel" = a hyperdrive or backup hyperdrive (Inferred).
    /// </summary>
    static GameObject? RetreatUnit(GameState game, StarSystem s, Owner owner)
    {
        bool CanTravel(GameObject x) => Sd(game, x, SdField.Hyperdrive) != 0 || Sd(game, x, SdField.HyperdriveIfDamaged) != 0;
        return s.Contents.OfType<Fleet>().Where(f => f.Owner == owner && f.Usable)
                .SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Owner == owner && x.Usable)).FirstOrDefault(CanTravel)
            ?? s.Contents.FirstOrDefault(x => Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters && x.Owner == owner && x.Usable && CanTravel(x));
    }

    /// <summary>v3: search(o, battle system, none, flag 0, stay 0) finds anything located in a system, the battle system included.</summary>
    static bool RetreatDestination(GameState game, StarSystem s, Owner owner) =>
        RetreatUnit(game, s, owner) is { } o && Fleets.Search(game, o, s, null, flag: false, stay: false)?.System is not null;

    /// <summary>
    /// Battle window retreat option: v1 = 0 (no Usable GravityWell capital ship of the
    /// other side in the system, Inferred), v2 != 0 (an own unit that can travel) and v3 != 0.
    /// </summary>
    public static bool RetreatEnabled(GameState game, StarSystem s, Owner side)
    {
        var ships = s.Contents.OfType<Fleet>().Where(f => f.Usable).SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable)).ToList();
        bool v1 = ships.Any(x => x.Owner != side && Sd(game, x, SdField.GravityWell) != 0);
        return !v1 && RetreatDestination(game, s, side);
    }

    // ---- setup ----

    /// <summary>
    /// Engine type table:
    /// the type index of a class is the position of its SD string number here.
    /// </summary>
    static readonly int[] TypeStrings =
    [
        1856, 1857, 1858, 1859, 1860, 1861, 1862, 1863, 1864, 1865, 1866, 1869, 1867, 1868, 1870, // 0-14 Alliance ships
        1920, 1921, 1922, 1923, 1924, 1925, 1926, 1927, 1929, 1930, 1931, 1932, 1933, 1934, // 15-28 Empire ships
        1602, 1603, 1600, 1601, 1664, 1665, 1666, 1667, // 29-36 X-, Y-, A-, B-wing, TIE Fighter, Interceptor, Bomber, Defender
        1928, // 37 Death Star
    ];

    /// <summary>Arc of no weapon (the arc choice finds none).</summary>
    internal const int NoArc = 4;
}
