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
public static class BattleEngine
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
    internal sealed record Application(GameObject Unit, int State, double D, double Shield0, double Hull0, double Shield1, double Hull1, int Draws, List<int> Crits);
    /// <summary>When set, every damage application is appended, also those on a unit with hull 0 (tests, pinned battles).</summary>
    [ThreadStatic] internal static List<Application>? DamageTrace;
    [ThreadStatic] static long stepNo;

    internal sealed class Unit(GameObject o, int side)
    {
        public GameObject Object { get; } = o;
        public int Side { get; } = side; // 0 = side 1, 1 = side 2
        public int Type; // 0 ship, 1 fighter, 2 Death Star scene (in no list)
        public int Key; // 1, 2, ... per side in setup order; the tie-break key in sorts
        public int TypeIndex; // the class's place in the engine type table (the string number, see TypeStrings)
        public int State = 2; // 1 docked / pending, 2 active, 5 landed, 9 retreated, 0xB destroyed
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
        public bool Live => State is 2 or 9;
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
        public int State; // 5 = withdrawn
        public IEnumerable<Unit> Lists => Ships.Concat(Fighters);
        public double Commander = 1.0; // bonus; no state test, survives the ship's loss
        /// <summary>The commander's ship unit (the tactical commander bonus needs it active); null = no record.</summary>
        public Unit? CommanderShip;
        /// <summary>The admiral's ship unit and clamp value ; null = no admiral record.</summary>
        public Unit? AdmiralShip;
        public double AdmiralValue = 1.0;
        /// <summary>Admiral bonus: only while the admiral's ship unit is active (state exactly 2), else 1.0.</summary>
        public double Admiral => AdmiralShip is { State: 2 } ? AdmiralValue : 1.0;
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
            ?? s.Contents.FirstOrDefault(x => Family.Of(x.Type) is >= 0x1C and < 0x20 && x.Owner == owner && x.Usable && CanTravel(x));
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
        0x740, 0x741, 0x742, 0x743, 0x744, 0x745, 0x746, 0x747, 0x748, 0x749, 0x74A, 0x74D, 0x74B, 0x74C, 0x74E, // 0-14 Alliance ships
        0x780, 0x781, 0x782, 0x783, 0x784, 0x785, 0x786, 0x787, 0x789, 0x78A, 0x78B, 0x78C, 0x78D, 0x78E, // 15-28 Empire ships
        0x642, 0x643, 0x640, 0x641, 0x680, 0x681, 0x682, 0x683, // 29-36 X-, Y-, A-, B-wing, TIE Fighter, Interceptor, Bomber, Defender
        0x788, // 37 Death Star
    ];

    /// <summary>Type index of a class: its string number in the engine table; not found gives 0 (a mod class: Inferred harmless).</summary>
    public static int TypeIndex(GameDefinitions defs, uint type) =>
        defs.Resolve(type) is { } r && Array.IndexOf(TypeStrings, r.Common.StringNumber) is var i and >= 0 ? i : 0;

    /// <summary>SD field; 0 beyond the record (fighter and Death Star records are shorter than CAPSHPSD).</summary>
    static int Sd(GameState game, GameObject o, SdField field) => game.Definitions.Resolve(o.Type) is { } r ? r.GetOrZero(field) : 0;

    static void Setup(GameState game, StarSystem s, Side[] sides)
    {
        var shipUnits = new Dictionary<GameObject, Unit>();
        Unit? Add(GameObject o, Unit? carrier = null)
        {
            if (o.Owner is not (Owner.Alliance or Owner.Empire)) return null;
            var u = new Unit(o, o.Owner == Owner.Alliance ? 0 : 1) { Carrier = carrier };
            var side = sides[u.Side];
            u.Key = ++side.Keys;
            u.TypeIndex = TypeIndex(game.Definitions, o.Type);
            uint f = Family.Of(o.Type);
            u.Type = f is >= 0x1C and < 0x20 ? 1 : 0;
            // per arc in engine order (fore, starboard, aft, port; the SD order is fore, aft, port, starboard): turbolaser, ion, laser
            SdField[][] sdArc =
            [
                [SdField.ForeTurbolaser, SdField.ForeIonCannon, SdField.ForeLaserCannon],
                [SdField.StarboardTurbolaser, SdField.StarboardIonCannon, SdField.StarboardLaserCannon],
                [SdField.AftTurbolaser, SdField.AftIonCannon, SdField.AftLaserCannon],
                [SdField.PortTurbolaser, SdField.PortIonCannon, SdField.PortLaserCannon],
            ];
            double scale = 1;
            if (u.Type == 1)
            {
                int sq = Sd(game, o, SdField.SquadronSize);
                (u.HullMax, u.Hull, scale) = (sq * 8.0, (sq - o.SquadSizeDamage) * 8.0, sq);
                u.Torpedo = Sd(game, o, SdField.Torpedoes) * sq;
                u.TorpedoRange = Sd(game, o, SdField.TorpedoRange);
                u.BaseSpeed = Sd(game, o, SdField.Speed) * 0.4;
            }
            else
            {
                (u.HullMax, u.Hull) = (Sd(game, o, SdField.Hull), Sd(game, o, SdField.Hull) - o.HullDamage);
                (u.ShieldCh, u.WeaponCh, u.TractorCh, u.SpeedCh, u.HyperCh) = (o.Critical(0), o.Critical(1), o.Critical(2), o.Critical(3), o.Critical(4) + o.Critical(5));
                u.Blocker = Sd(game, o, SdField.GravityWell) != 0;
                u.BaseSpeed = Sd(game, o, SdField.Speed) * 0.4 * 0.6;
                shipUnits[o] = u;
            }
            u.Hyperdrives = (Sd(game, o, SdField.Hyperdrive) != 0 ? 1 : 0) + (Sd(game, o, SdField.HyperdriveIfDamaged) != 0 ? 1 : 0);
            for (int a = 0; a < 4; a++)
                (u.Arcs[a, 2], u.Arcs[a, 1], u.Arcs[a, 0]) = (Sd(game, o, sdArc[a][0]) * scale, Sd(game, o, sdArc[a][1]) * scale, Sd(game, o, sdArc[a][2]) * scale);
            (u.TlRange, u.IonRange, u.LaserRange) = (Sd(game, o, SdField.TurbolaserRange), Sd(game, o, SdField.IonCannonRange), Sd(game, o, SdField.LaserCannonRange));
            u.ShieldMax = u.Shield = Sd(game, o, SdField.ShieldStrength) * scale;
            (u.Wrr, u.Srr) = (Sd(game, o, SdField.WeaponRechargeRate), Sd(game, o, SdField.ShieldRechargeRate));
            if (u.Type == 0) u.BaseSrr = u.Srr; // a fighter's is rewritten by every recharge read, so only ships keep it
            u.Maneuverability = Sd(game, o, SdField.Maneuverability);
            if (u.Type == 0) u.Capacity = Sd(game, o, SdField.FighterCapacity); // Inferred mapping
            side.Units.Add(u);
            side.Battle.Add(u);
            // deploy: ships go to the non-fighter list; a fighter aboard a found carrier (a Death Star's: the current scene's hangar) that
            // cannot jump itself is docked: state 1, pending in the shared pending list; other fighters go to the fighter list
            if (u.Type == 0) side.Ships.Add(u);
            else if (Resolve(carrier, sides) is { } c && !CanJump(u, side))
            {
                u.State = 1;
                side.Pending.Add(u);
                // setup dock: +1 only below the capacity; a full hangar writes nothing, the
                // fighter keeps its carrier key (Inferred)
                if (c.Docked < c.Capacity) c.Docked++;
                else u.Overflow = true;
            }
            else side.Fighters.Add(u);
            if (u.State == 2) side.Undocked.Add(u);
            return u;
        }
        // Each registration replaces the current scene; side index 1's next key is used whatever the owner
        Unit Scene(GameObject ds, bool unfinished)
        {
            bool Fighter(GameObject y) => Family.Of(y.Type) is >= 0x1C and < 0x20;
            var sc = new Unit(ds, 1) { Type = 2, Key = ++sides[1].Keys, Charge = 100f, Unfinished = unfinished };
            (sc.HullMax, sc.Hull) = (F(Sd(game, ds, SdField.Hull)), F(Sd(game, ds, SdField.Hull) - ds.HullDamage));
            sc.Shielded = s.Contents.Any(x => Family.Of(x.Type) == 0x25 && x.Usable); // any owner
            // capacity = FighterCapacity - fighters aboard (Inferred: Existing) + Usable fighters aboard
            sc.Capacity = Sd(game, ds, SdField.FighterCapacity) - ds.Contents.Count(y => Fighter(y) && y.Existing) + ds.Contents.Count(y => Fighter(y) && y.Usable);
            sides[1].Scene = sc;
            sides[1].DeathStars.Add(ds);
            return sc;
        }
        foreach (var fleet in s.Contents.OfType<Fleet>().Where(f => f.Existing).ToList())
        {
            var ships = fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable).ToList();
            var units = new Dictionary<GameObject, Unit?>();
            foreach (var x in ships.Where(x => Family.Of(x.Type) < 0x18)) units[x] = Add(x);
            foreach (var x in ships.Where(x => Family.Of(x.Type) >= 0x18)) units[x] = Scene(x, false); // Usable implies Completed (Inferred)
            // Inside every fleet's iteration: the ship manager's unit under construction (the queue
            // head, Inferred) when of the Death-Star class and not Completed
            if (s.Managers[(int)ManufacturingKind.Ship] is { } mm && mm.Orders.FirstOrDefault() is { } q
                && Family.Of(q.Type) is >= 0x18 and < 0x1C && !q.Completed)
                Scene(q, true);
            // Per ship: its fighters, then its
            // characters, troop regiments and special forces, Usable as the fighters (Inferred);
            // each record takes its side's next key and names the carrying ship's unit
            foreach (var ship in ships)
            {
                foreach (var f in ship.Contents.Where(y => Family.Of(y.Type) is >= 0x1C and < 0x20 && y.Usable)) Add(f, units[ship]);
                foreach (var p in ship.Contents.OfType<Character>().Where(y => y.Usable)
                    .Concat(ship.Contents.Where(y => Family.IsTroop(y.Type) && y.Usable))
                    .Concat(ship.Contents.Where(y => Family.IsSpecialForce(y.Type) && y.Usable)))
                {
                    if (p.Owner is not (Owner.Alliance or Owner.Empire)) continue;
                    sides[p.Owner == Owner.Alliance ? 0 : 1].Keys++;
                    units[ship]?.Carried.Add(p);
                }
            }
        }
        foreach (var f in s.Contents.Where(y => Family.Of(y.Type) is >= 0x1C and < 0x20 && y.Usable).ToList()) Add(f);
        foreach (var side in sides) Officers(game, s, side, shipUnits);
    }

    /// <summary>
    /// Commander / admiral: commander = first the first character directly in the system of
    /// the system's owner with command kind 1, then per Usable fleet of the side (key order) the first character with the kind aboard
    /// its capital ships (Existing, fleet owner); a strictly greater Combat (admiral: Leadership, fleets only) replaces. A bonus only
    /// when the pick has a record, i.e. sits aboard a ship unit: commander clamp(f32(Combat / 20) + 1, 1, 9), admiral
    /// clamp(9 - Leadership / 10, 1, 9).
    /// </summary>
    static void Officers(GameState game, StarSystem s, Side side, Dictionary<GameObject, Unit> shipUnits)
    {
        var owner = side.Index == 0 ? Owner.Alliance : Owner.Empire;
        Character? Pick(int kind, int skill, Character? pick)
        {
            foreach (var fleet in s.Contents.OfType<Fleet>().Where(f => f.Owner == owner && f.Usable))
                if (fleet.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents.OfType<Character>())
                        .FirstOrDefault(c => c.Existing && c.Owner == fleet.Owner && c.CommandKind == kind) is { } c
                    && (pick is null || c.Skills[skill] > pick.Skills[skill]))
                    pick = c;
            return pick;
        }
        bool Record(Character? c) => c?.Container is { } ship && shipUnits.ContainsKey(ship);
        var commander = Pick(1, SkillIndex.Combat, s.Contents.OfType<Character>().FirstOrDefault(c => c.Owner == s.Owner && c.CommandKind == 1));
        var admiral = Pick(2, SkillIndex.Leadership, null);
        if (Record(commander)) (side.Commander, side.CommanderShip) = (Math.Clamp(F(commander!.Skills[SkillIndex.Combat] / 20.0) + 1, 1, 9), shipUnits[commander.Container!]);
        if (Record(admiral)) (side.AdmiralShip, side.AdmiralValue) = (shipUnits[admiral!.Container!], Math.Clamp(9 - admiral.Skills[SkillIndex.Leadership] / 10, 1, 9));
    }

    /// <summary>
    /// Groups at deploy, in the order of the side's group list. 1. Fighter formations: one per fighter class of
    /// the side's units (docked included, setup order); class order = the engine type index (Alliance X-, Y-, A-,
    /// B-wing; Empire = SdRef order), the used classes in slots 0, 1, ... of four (Inferred). Balancing: per slot with bookkeeping count 0 (slots 0 ... 3 in order), b = the
    /// first slot with the strictly largest count; above 7, m = count / 2 moves in the counts only; units move from the front of
    /// b's formation (Alliance m, Empire one) into a new formation appended to the side's group list for the slot; a drained formation stays.
    /// 2. Capital-ship groups from the side's non-fighter list sorted into turbolaser /
    /// ion only, any laser, unarmed, each by -(speed + 10000 * max range) ascending, ties by key.
    /// </summary>
    static void Deploy(Side side)
    {
        // The used classes take slots 0, 1, ... (Inferred)
        var classes = side.Units.Where(u => u.Type == 1).GroupBy(u => u.Object.Type).OrderBy(g => g.First().TypeIndex).ToList();
        var count = new int[Math.Max(4, classes.Count)];
        var formation = new Group?[count.Length];
        for (int c = 0; c < classes.Count; c++) (count[c], formation[c]) = (classes[c].Count(), AddGroup(side, true, classes[c]));
        for (int slot = 0; slot < 4; slot++)
        {
            if (count[slot] != 0) continue;
            int b = 0;
            for (int j = 1; j < 4; j++) if (count[j] > count[b]) b = j; // first strictly largest bookkeeping count
            if (count[b] <= 7) continue;
            int m = count[b] / 2;
            (count[slot], count[b]) = (m, count[b] - m); // bookkeeping only, whatever number of units moves
            if (formation[b] is not { } src) continue;
            var moved = src.Members.Take(side.Index == 0 ? m : 1).ToList(); // from the front; Empire exactly one
            src.Members.RemoveRange(0, moved.Count); // a drained formation stays in the side's group list
            if (moved.Count > 0) formation[slot] = AddGroup(side, true, moved);
        }
        CapitalGroups(side);
    }

    /// <summary>Sort the non-fighter list into turbolaser / ion only, any laser and unarmed lists, each by -(speed + 10000 * max range) ascending, ties by key.</summary>
    internal static (List<Unit> Heavy, List<Unit> Laser, List<Unit> Unarmed) SortCapital(Side side)
    {
        List<Unit> Sorted(Func<Unit, bool> pick) => [.. side.Ships.Where(pick).OrderBy(u => -(Speed(u, side) + 10000 * MaxRange(u))).ThenBy(u => u.Key)];
        bool Unarmed(Unit u) => !Has(u, 0) && !Has(u, 1) && !Has(u, 2);
        return (Sorted(u => !Unarmed(u) && !Has(u, 0)), Sorted(u => Has(u, 0)), Sorted(Unarmed));
    }

    /// <summary>Group count g of a list of n >= 1 units in slots lo ... hi: 1 for n &lt; 2, else the largest i <= hi with i^2 <= n; hi - lo + 1 when that is <= g.</summary>
    internal static int GroupCount(int n, int lo, int hi)
    {
        int g = 1;
        if (n >= 2) for (int i = 1; i <= hi && i * i <= n; i++) g = i;
        if (hi - lo + 1 <= g) g = hi - lo + 1;
        return Math.Max(1, g); // a slot range exhausted (n1 + n2 > 7): one group (Inferred, unreachable with shipped fleets)
    }

    /// <summary>Whether unit j of a list split into <paramref name="groups"/> groups of <paramref name="stride"/> starts the next group (the automatic and the tactical battle share it).</summary>
    internal static bool GroupStart(int j, int stride, int made, int groups) => j % stride == 0 && made < groups;

    /// <summary>The withdraw test: the enemy has strength and own / enemy is below the side's threshold or zero.</summary>
    internal static bool Outmatched(double own, double enemy, double threshold) => enemy > 0 && (own / enemy < threshold || own / enemy == 0);

    static bool Has(Unit u, int w) => Enumerable.Range(0, 4).Any(a => u.Arcs[a, w] > 0);
    /// <summary>Max range: the largest range of a weapon type the unit has in some arc.</summary>
    internal static double MaxRange(Unit u) => Math.Max(Has(u, 1) ? u.IonRange : 0, Math.Max(Has(u, 0) ? u.LaserRange : 0, Has(u, 2) ? u.TlRange : 0));

    /// <summary>Capital-ship groups: the non-fighter list split into the turbolaser / ion, laser and unarmed lists, groups appended to the side's group list.</summary>
    internal static void CapitalGroups(Side side)
    {
        var (heavy, laser, unarmed) = SortCapital(side);
        int n1 = Groups(heavy, 0, 3), n2 = Groups(laser, n1, n1 + 3);
        Groups(unarmed, n1 + n2, 7);

        int Groups(List<Unit> list, int lo, int hi)
        {
            int n = list.Count;
            if (n == 0) return 0;
            int g = GroupCount(n, lo, hi), stride = n / g, made = 0;
            Group? current = null;
            for (int j = 0; j < n; j++)
            {
                if (GroupStart(j, stride, made, g)) { current = AddGroup(side, false, []); made++; }
                current!.Members.Add(list[j]);
                list[j].Group = current;
            }
            return made;
        }
    }

    internal static Group AddGroup(Side side, bool formation, IEnumerable<Unit> members)
    {
        var g = new Group(formation);
        g.Members.AddRange(members);
        foreach (var u in g.Members) u.Group = g;
        side.Groups.Add(g);
        return g;
    }

    // ---- unit getters ----

    /// <summary>Speed: (bonus + base)*(1 - 0.25*SpeedCH) - tractor load, 0 unless &gt; 0; tractor load 0 (no holds in automatic battles, Inferred).</summary>
    internal static double Speed(Unit u, Side side)
    {
        if (u.EngineDown != 0) return 0; // set only by tactical ion hits
        double v = side.Admiral + u.BaseSpeed, speed = v - v * 0.25 * u.SpeedCh;
        return speed > 0 ? speed : 0;
    }
    internal static double Agility(Unit u, Side side) => Speed(u, side) == 0 ? 1.0 : Math.Min(9, u.Maneuverability + (u.Type == 1 ? 1 : side.Admiral));
    /// <summary>Rounds the value to float32.</summary>
    internal static double F(double x) => (float)x;
    /// <summary>Scale: (hull * factor) / hull max, multiplication first.</summary>
    internal static double Scale(Unit u, double x) => u.HullMax > 0 ? u.Hull * x / u.HullMax : 0;
    // Capital ship: f(Scale(B)) - B * 0.25 * counter
    // (at least 0), fighter f(hull * 3.751f / 8) and f(hull * 0.025f / 8)
    internal static double WeaponRate(Unit u) => u.Type == 1 ? F(u.Hull * (double)3.751f / 8) : Math.Max(0, F(Scale(u, F(3.75 * u.Wrr))) - F(3.75 * u.Wrr) * 0.25 * u.WeaponCh);
    internal static double ShieldRate(Unit u) => u.Type == 1 ? F(u.Hull * (double)0.025f / 8) : Math.Max(0, F(Scale(u, u.Srr)) - u.Srr * 0.25 * u.ShieldCh);
    internal static bool HasLaser(Unit u) => Enumerable.Range(0, 4).Any(a => u.Arcs[a, 0] > 0);
    internal static bool CanJump(Unit u, Side side) => u.Hyperdrives - u.HyperCh > 0 && Speed(u, side) != 0;

    static double LaserF(bool ship) => ship ? LaserVsShip : 1.0;
    static double HeavyF(bool ship) => ship ? 1.0 : 0.5; // ion / turbolaser

    /// <summary>Arc choice: the first arc with the strictly largest total against the target kind; 4 = none.</summary>
    internal static int Arc(Unit a, bool ship)
    {
        int best = 4;
        double top = 0;
        for (int i = 0; i < 4; i++)
        {
            double t = a.Arcs[i, 0] * Base * LaserF(ship) + (ship ? a.Arcs[i, 1] * Base : 0) + a.Arcs[i, 2] * Base * HeavyF(ship);
            if (t > top) (best, top) = (i, t);
        }
        return best;
    }

    /// <summary>
    /// Rate (with float rounding at each step); <paramref name="ratio"/> applies against fighters only; strength
    /// passes none. <paramref name="unshielded"/>: the target's shield fraction is 0 (torpedo term).
    /// </summary>
    internal static double Rate(Unit a, bool ship, double ratio, bool unshielded)
    {
        double w = F(WeaponRate(a));
        int arc = Arc(a, ship);
        if (w == 0 || arc == 4) return 0;
        double l = a.Arcs[arc, 0], i = a.Arcs[arc, 1], t = a.Arcs[arc, 2];
        bool fighter = a.Type == 1;
        double n = l + i + t;
        if (fighter) n = Math.Truncate(Scale(a, F(n))); // 80 hull of 96 with n = 60 gives exactly 50
        double per = F(n / w);
        if (per <= 0) return 0;
        double acc = 0;
        void Add(double term) => acc = F((fighter ? Scale(a, term) : term) + acc);
        if (i > 0) Add(ship ? F(i * (1.0 * Base)) : 0);
        if (l > 0) Add(ship ? F(l * (LaserF(ship) * Base)) : F(F(l * (LaserF(ship) * Base)) * ratio));
        if (t > 0) Add(ship ? F(t * (Base * HeavyF(ship))) : F(F(t * (Base * HeavyF(ship))) * ratio));
        if (fighter && ship && unshielded && a.Torpedo != 0) return (Scale(a, F(Scale(a, F(a.Torpedo * Torpedo)))) + acc) / per;
        return acc / per;
    }

    internal static double Rate(Unit a, Unit t, Side[] sides)
    {
        double ratio = F(Agility(a, sides[a.Side]) / Agility(t, sides[t.Side]));
        if (ratio < (double)0.1f) ratio = 0.1f;
        return Rate(a, t.Type != 1, ratio, t.ShieldMax == 0 || t.Shield / t.ShieldMax == 0);
    }

    // ---- step ----

    static bool Step(Side[] sides, GameRandom rand)
    {
        foreach (var side in sides) side.Strength = Strength(side, sides);
        bool done2 = Update(sides[1], sides, rand), done1 = Update(sides[0], sides, rand);
        foreach (var side in sides)
        {
            side.Strength = Strength(side, sides);
            if (side.Strength >= side.Lowest) side.Stall += Dt;
            else (side.Lowest, side.Stall) = (side.Strength, 0);
        }
        StateTrace?.Add($"step {stepNo}: " + string.Join(" | ", sides.Select(x =>
            $"side {x.Index + 1} str {x.Strength:G6} low {x.Lowest:G6} stall {x.Stall} state {x.State} retreat {RetreatPossible(x, sides)} groups ["
            + string.Join(", ", x.Groups.Select(g => $"{string.Join("+", g.Members.Select(a => $"{a.Object.Ref:X8}"))}->{g.Target?.Object.Ref:X8}"))
            + "] units " + string.Join(", ", x.Units.Select(u => $"{u.Object.Ref:X8} st{u.State} h{u.Hull:G6} s{u.Shield:G6} ch{u.ShieldCh}{u.WeaponCh}{u.TractorCh}{u.SpeedCh}{u.HyperCh}")))));
        if (done1 || done2) return true;
        if (sides.All(x => x.Stall >= Stalemate))
        {
            // Step-end strengths (side index 0's is -1 while the scene is alive), side 2 first, then side 1
            double a = Alive(sides[1].Scene) ? -1 : sides[0].Strength, b = sides[1].Strength;
            if (b <= a) Lose(sides[1]);
            if (a <= b) Lose(sides[0]); // side 1 withdrawing: the step returns at once
        }
        return false;

        // a side whose retreat is possible is set withdrawing; else every unit of its non-fighter and fighter lists is destroyed
        void Lose(Side loser)
        {
            if (RetreatPossible(loser, sides)) loser.Withdrawing = true;
            else foreach (var u in loser.Lists.ToList()) Kill(u, sides, rand);
        }
    }

    /// <summary>
    /// Side update: side index 1 launches every pending unit (state 2, appended to side index 1's
    /// fighter list, then the pending list is emptied); candidates and targeting; damage once per group with a target
    /// (no target state test); withdraw; recharge of every unit of both lists with the "done" count.
    /// </summary>
    static bool Update(Side side, Side[] sides, GameRandom rand)
    {
        var enemy = sides[1 - side.Index];
        var scene = sides[1].Scene;
        if (side.Index == 1 && Alive(scene)) Superlaser(scene!, side, enemy, sides, rand);
        // Alliance attack: the scene is attackable and side index 1's fighter list is shorter than side index 0's
        if (side.Index == 0 && Attackable(scene) && sides[1].Fighters.Count < side.Fighters.Count && side.Groups.FirstOrDefault(g => g.Formation) is { } fg)
            Attack(fg, scene!, sides, rand);
        if (side.Index == 1)
        {
            foreach (var u in side.Pending)
            {
                u.State = 2;
                if (!u.Overflow && Resolve(u.Carrier, sides) is { Docked: > 0 } c) c.Docked--; // launch -1; an overflow fighter was never counted (Inferred)
                side.Fighters.Add(u); // the updating side's list, whatever the unit's side (Inferred harmless)
            }
            side.Pending.Clear();
        }
        Target(side, enemy, sides);
        foreach (var g in side.Groups.ToList())
        {
            if (g.Target is not { } t) continue;
            Damage(t, GroupRate(g, t, sides) * Dt, rand);
            if (t.Hull <= 0) Kill(t, sides, rand);
        }
        if (Withdraw(side, sides))
        {
            foreach (var u in side.Fighters.ToList())
                if (CanJump(u, side)) Retreat(u, sides);
                else if (!Land(u, side, sides)) Kill(u, sides, rand);
            if (side.Index == 1 && Alive(scene)) scene!.Withdrawn = true; // Between the fighter loop and the non-fighter loop
            foreach (var u in side.Ships.ToList())
                if (CanJump(u, side)) Retreat(u, sides);
                else Kill(u, sides, rand);
            side.State = 5;
        }
        bool active = false;
        foreach (var u in side.Lists)
        {
            active = active || u.State == 2;
            Recharge(u);
        }
        return !active && !(side.Index == 1 && Alive(scene)); // Side index 1 with a live scene is never done
    }

    /// <summary>
    /// Superlaser (head of side index 1's update): below 100 the charge grows by f32(dt * 0.33333334f * 0.001f) (only
    /// with hull and finished), capped at 100; at exactly 100 the candidate list is rebuilt and its first capital ship (type 0) is
    /// destroyed with its draws, charge := 0; none gives the charge stays 100.
    /// </summary>
    static void Superlaser(Unit scene, Side side, Side enemy, Side[] sides, GameRandom rand)
    {
        if (scene.Charge != 100f && scene.Hull > 0 && !scene.Unfinished)
        {
            float inc = (float)Dt * 0.33333334f * 0.001f;
            scene.Charge += inc;
            if (scene.Charge >= 100f) scene.Charge = 100f;
        }
        if (scene.Charge != 100f || Candidates(side, enemy).FirstOrDefault(u => u.Type == 0) is not { } t) return;
        scene.Charge = 0;
        Kill(t, sides, rand);
    }

    /// <summary>
    /// Alliance attack: the formation's members by ascending key, walked circularly (next taken first);
    /// visit 101 destroys every member left. Per visit a = trunc(agility), r1 = 1 + rand(9); r1 &gt; a: t = trunc(hull / 8), t <= 1
    /// destroyed, else r2 = 1 + rand(t - 1) and f32(8*r2) damage. Not destroyed (hull &gt; 0): value = f32((b + hull * f32(torpedo *
    /// 0.53333336f) / hull max) * 0.5), r3 = 1 + rand(99) <= trunc(value) gives the Death Star is destroyed (hull and charge 0, its hangar's
    /// units destroyed) and the call ends. An empty (drained) formation does nothing (Inferred).
    /// </summary>
    static void Attack(Group g, Unit scene, Side[] sides, GameRandom rand)
    {
        var copy = g.Members.OrderBy(u => u.Key).ToList();
        var side = sides[0];
        int visits = 0;
        var cur = copy.FirstOrDefault();
        while (cur is not null)
        {
            var next = copy[(copy.IndexOf(cur) + 1) % copy.Count];
            if (++visits > 100)
            {
                foreach (var u in copy) Kill(u, sides, rand);
                return;
            }
            int a = (int)Math.Truncate(Agility(cur, side));
            bool dead = false;
            if (1 + rand.Rand(9) > a)
            {
                int t = (int)Math.Truncate(cur.Hull / 8.0);
                if (t > 1) Damage(cur, F((1 + rand.Rand(t - 1)) * 8.0), rand);
                if (t <= 1 || cur.Hull <= 0) { Kill(cur, sides, rand); dead = true; }
            }
            if (dead)
            {
                copy.Remove(cur);
                if (copy.Count == 0) return;
            }
            else if (cur.Hull > 0)
            {
                double value = F((side.Commander + F(Scale(cur, F(cur.Torpedo * Torpedo)))) * 0.5);
                if (1 + rand.Rand(99) <= Math.Truncate(value))
                {
                    (scene.Hull, scene.Charge) = (0, 0);
                    // the scene hangar's lists are empty whenever the attack runs (Inferred for the second)
                    foreach (var u in sides.SelectMany(x => x.Units).Where(u => u.State is 1 or 5 && u.Carrier is { Type: 2 } && !u.Overflow).ToList()) Kill(u, sides, rand);
                    return;
                }
            }
            cur = next;
        }
    }

    /// <summary>Retreat: state 9 and out of the lists and its group; skips retreated / destroyed units.</summary>
    static void Retreat(Unit u, Side[] sides)
    {
        if (u.State is 9 or 0xB) return;
        u.State = 9;
        sides[0].Undocked.Remove(u);
        Remove(u, sides);
    }

    /// <summary>Out of the pending list, its group (an empty group leaves the side's group list) and the side's fighter and non-fighter lists.</summary>
    internal static void Remove(Unit u, Side[] sides)
    {
        sides[0].Pending.Remove(u);
        if (u.Group is { } g)
        {
            g.Members.Remove(u);
            if (g.Members.Count == 0) sides[u.Side].Groups.Remove(g);
        }
        foreach (var side in sides) { side.Ships.Remove(u); side.Fighters.Remove(u); } // an activated unit sits in side index 1's list
    }

    /// <summary>Scene alive: finished, hull != 0, not withdrawn.</summary>
    internal static bool Alive(Unit? scene) => scene is { Unfinished: false, Withdrawn: false } && scene.Hull != 0;
    /// <summary>Scene attackable: alive and no Death Star shield facility.</summary>
    static bool Attackable(Unit? scene) => Alive(scene) && !scene!.Shielded;
    /// <summary>Carrier lookup: a Death Star's key finds no unit and gives the current scene's hangar.</summary>
    internal static Unit? Resolve(Unit? carrier, Side[] sides) => carrier is { Type: 2 } ? sides[1].Scene : carrier;

    // ---- candidates and targeting (no draws) ----

    /// <summary>
    /// Threat: 15 * strength + 1.0 per passenger record aboard - 0.01 * R /
    /// base SRR - 0.01 * hf - 0.01 in state 1 / 3; the fire-record term is empty here. A fighter's base is rewritten by the read itself
    /// 1.0 while it has hull, 0 otherwise (Inferred for the 0 / 0 case).
    /// </summary>
    internal static double Threat(Unit u)
    {
        double shield = u.Type == 1 ? (u.Hull > 0 ? 1 : 0) : u.BaseSrr > 0 ? ShieldRate(u) / u.BaseSrr : 0;
        return Rate(u, true, 1, false) * 15 + u.Passengers - 0.01 * shield - 0.01 * u.Hf - (u.State is 1 or 3 ? 0.01 : 0);
    }

    /// <summary>Wanted test: state 2 and weapon rate != 0.</summary>
    static bool Wanted(Unit u) => u.State == 2 && WeaponRate(u) != 0;

    /// <summary>Group rate: sum over the group's members in state 2 / 4 / 6 of the unit rate on <paramref name="t"/>, rounded to float per member.</summary>
    internal static double GroupRate(Group g, Unit t, Side[] sides)
    {
        double acc = 0;
        foreach (var u in g.Members)
            if (u.State is 2 or 4 or 6) acc = F(acc + Rate(u, t, sides));
        return acc;
    }

    /// <summary>
    /// Candidates and targeting: the other side's non-fighter units and fighter units in state 2,
    /// wanted gives C, else state 2 gives U; its pending units in state 1 / 3 gives C; each by -threat, ties by key; U appended. L = the
    /// fighters of C, then its non-fighters; C := its non-fighters, then L. Each own group not skipped (scorer skip test) goes to S2 if
    /// its leader has a laser, else S1; evaluator(S1, C), evaluator(S2, L). Groups keep their target until pooled again; an empty
    /// candidate list assigns nothing.
    /// </summary>
    internal static void Target(Side side, Side enemy, Side[] sides)
    {
        TtkFn ttk = (Unit t, Group g, ref double acc) => TimeToKill(t, g, ref acc, sides);
        // scorer skip test (Inferred: a group field stays 0 throughout)
        Targeting(Candidates(side, enemy), side.Groups, new Scorer(g => SkipRule(g, side.Groups, Wanted, ttk), Wanted, ttk, (g, t) => g.Target = t), sides);
    }

    /// <summary>Time to kill (the scorer's TTK) with a cumulative rate: acc += the group's rate on t.</summary>
    internal delegate double TtkFn(Unit t, Group g, ref double acc);

    /// <summary>The side's targeting objects: scorer skip test, wanted test, TTK; the evaluator's assignment.</summary>
    internal sealed record Scorer(Func<Group, bool> Skip, Func<Unit, bool> Wanted, TtkFn Ttk, Action<Group, Unit> Assign);

    /// <summary>
    /// Targeting (shared by both engines): L = the fighters of C, then its non-fighters; C := its non-fighters, then
    /// L; each group not skipped goes to S2 if its leader has a laser, else S1; evaluator(S1, C), evaluator(S2, L).
    /// </summary>
    internal static void Targeting(List<Unit> c, IEnumerable<Group> groups, Scorer s, Side[] sides)
    {
        if (c.Count == 0) return;
        var l = c.Where(u => u.Type == 1).Concat(c.Where(u => u.Type != 1)).ToList();
        c = [.. c.Where(u => u.Type != 1), .. l];
        List<Group> s1 = [], s2 = [];
        foreach (var g in groups.ToList())
            if (!s.Skip(g)) (g.Leader is { } leader && HasLaser(leader) ? s2 : s1).Add(g); // no leader gives S1
        Evaluate(s1, c, s, sides);
        Evaluate(s2, l, s, sides);
    }

    /// <summary>
    /// Skip rule: a fighter formation with a member in state 1 / 3; else the current target wanted and TTK &lt; 300 ( != -1)
    /// alone or cumulative over the side's other groups on it (each adds its rate, the last value counts).
    /// </summary>
    internal static bool SkipRule(Group g, IEnumerable<Group> sideGroups, Func<Unit, bool> wanted, TtkFn ttk)
    {
        if (g.Formation && g.Members.Any(u => u.State is 1 or 3)) return true;
        if (g.Target is not { } t || !wanted(t)) return false;
        double acc = 0, v = ttk(t, g, ref acc);
        if (v < 300 && v != -1) return true;
        foreach (var o in sideGroups.Where(o => o != g && o.Target == t)) v = ttk(t, o, ref acc);
        return v < 300 && v != -1;
    }

    /// <summary>Candidate list.</summary>
    internal static List<Unit> Candidates(Side side, Side enemy)
    {
        static List<Unit> Sort(IEnumerable<Unit> units) => [.. units.OrderBy(u => -Threat(u)).ThenBy(u => u.Key)];
        var listed = enemy.Ships.Concat(enemy.Fighters.Where(u => u.State == 2)).ToList();
        var c = Sort(listed.Where(Wanted).Concat(side.Pending.Where(u => u.Side == enemy.Index && u.State is 1 or 3)));
        c.AddRange(Sort(listed.Where(u => !Wanted(u) && u.State == 2)));
        return c;
    }

    /// <summary>Time to kill: acc += the group's rate on t; max(1, 0.65 * hull + shield) / (acc - R(t)) when positive, else -1 (distance term 0 in automatic battles, Inferred).</summary>
    static double TimeToKill(Unit t, Group g, ref double acc, Side[] sides)
    {
        acc += GroupRate(g, t, sides);
        double value = Math.Max(1, 0.65 * t.Hull + t.Shield), net = acc - ShieldRate(t);
        return net > 0 ? value / net : -1;
    }

    /// <summary>
    /// Evaluator over groups: per wanted candidate, the pool ranked by key (TTK in [0, 180], else
    /// 100000 - rate); one element gives assigned and the evaluator ends; else the walk from the last back to (not including) the first
    /// with the cumulative TTK (acc starting at the first's rate) assigns last...e once within [0, 180]. Leftovers gives the first wanted
    /// candidate, else the first candidate.
    /// </summary>
    static void Evaluate(List<Group> groups, List<Unit> targets, Scorer s, Side[] sides)
    {
        var pool = groups.ToList();
        static bool InRange(double x) => x >= 0 && x <= Horizon;
        foreach (var t in targets.Where(s.Wanted))
        {
            if (pool.Count == 0) return;
            var k = pool.Select(g =>
            {
                double acc = 0, ttk = s.Ttk(t, g, ref acc);
                return (Group: g, Key: InRange(ttk) ? ttk : 100000 - acc);
            }).OrderBy(x => x.Key).Select(x => x.Group).ToList(); // stable
            if (k.Count == 1)
            {
                s.Assign(k[0], t);
                return;
            }
            double sum = GroupRate(k[0], t, sides);
            for (int e = k.Count - 1; e > 0; e--)
                if (InRange(s.Ttk(t, k[e], ref sum)))
                {
                    for (int i = k.Count - 1; i >= e; i--) { s.Assign(k[i], t); pool.Remove(k[i]); }
                    break;
                }
        }
        if ((targets.FirstOrDefault(s.Wanted) ?? targets.FirstOrDefault()) is { } first)
            foreach (var g in pool) s.Assign(g, first);
    }

    // ---- damage, crits, destruction ----

    /// <summary>Damage application with 32-bit float arithmetic; nothing when the hull is 0.</summary>
    static void Damage(Unit t, double d, GameRandom rand)
    {
        double s0 = t.Shield, h0 = t.Hull;
        int draws = 0;
        List<int> crits = [];
        if (t.Hull > 0)
        {
            string site = rand.Trace is null ? "" : $"{t.Object.Ref:X8} {stepNo} ";
            t.Shield = F(s0 - d);
            if (s0 > 0)
            {
                double p = 100 / s0 * (s0 - (s0 - d)); // the unrounded s0 - d
                p = F(p < 1 ? 1 : p);
                draws++;
                if (rand.Rand(100, site + "s") <= p) Hit(0);
            }
            if (t.Shield < 0)
            {
                double o = t.Shield <= -1 ? t.Shield : -1, old = t.Hull, h = F(o + old);
                t.Shield = 0;
                t.Hull = h < 0 ? 0 : h;
                draws++;
                double x = F((t.Hull - old) * 100 / old + rand.Rand(100, site + "h"));
                int k = x > 95 ? 4 : x > 90 ? 3 : x > 80 ? 2 : x > 65 ? 1 : x > 50 ? 0 : -1;
                if (k >= 0) Hit(k);
            }
        }
        DamageTrace?.Add(new Application(t.Object, t.State, d, s0, h0, t.Shield, t.Hull, draws, crits));

        void Hit(int k)
        {
            crits.Add(k);
            Crit(t, k);
        }
    }

    /// <summary>Critical hit k: ShieldCH / WeaponCH / TractorCH / SpeedCH + 1 (cap 4), k = 4 hyperdrive + 1 (cap the hyperdrive count).</summary>
    internal static void Crit(Unit t, int k)
    {
        CritTrace?.Add((stepNo, t.Object, k));
        switch (k)
        {
            case 0: t.ShieldCh = Math.Min(4, t.ShieldCh + 1); break;
            case 1: t.WeaponCh = Math.Min(4, t.WeaponCh + 1); break;
            case 2: t.TractorCh = Math.Min(4, t.TractorCh + 1); break;
            case 3: t.SpeedCh = Math.Min(4, t.SpeedCh + 1); break;
            default: t.HyperCh = Math.Min(t.Hyperdrives, t.HyperCh + 1); break;
        }
    }

    /// <summary>
    /// Destroy: skips only state 0xB; state := 0xB, then 100000 damage (draws while hull != 
    /// 0). The recursion over a capital ship's own lists adds nothing (Inferred empty); docked fighters die with their
    /// carrier on the strategy side.
    /// </summary>
    static void Kill(Unit u, Side[] sides, GameRandom rand)
    {
        if (u.State == 0xB) return;
        u.State = 0xB;
        sides[0].Undocked.Remove(u);
        Damage(u, Destroy, rand);
        u.Hull = 0;
        Remove(u, sides);
    }

    // ---- strength, withdraw, recharge ----

    /// <summary>
    /// Strength: no state test; sum of the rate against a ship over the non-fighter list (a unit
    /// left out while some group of the other side targets it with a rate >= its shield recharge), every unit of the fighter list and, for
    /// side index 1, every pending unit of the shared pending list; while the scene is alive an f32 add of 20000000 for side index 1, and for
    /// side index 0 under the exposure test (the group fields never equal 6, Inferred).
    /// </summary>
    internal static double Strength(Side side, Side[] sides)
    {
        var other = sides[1 - side.Index];
        double sum = 0;
        foreach (var u in side.Ships)
        {
            if (other.Groups.Any(g => g.Target == u && GroupRate(g, u, sides) >= ShieldRate(u))) continue;
            sum += Rate(u, true, 1, false);
        }
        foreach (var u in side.Fighters) sum += Rate(u, true, 1, false);
        if (side.Index == 1) foreach (var u in side.Pending) sum += Rate(u, true, 1, false);
        return Alive(sides[1].Scene) && (side.Index == 1 || Exposed(sides)) ? F(sum + 20000000.0) : sum;
    }

    /// <summary>Exposure test: the scene is attackable and side index 0's fighters in the shared undocked list outnumber side index 1's.</summary>
    static bool Exposed(Side[] sides) =>
        Attackable(sides[1].Scene) && sides[0].Undocked.Count(u => u.Type == 1 && u.Side == 0) > sides[0].Undocked.Count(u => u.Type == 1 && u.Side == 1);

    /// <summary>
    /// Retreat possible: retreat allowed (v3), not withdrawn, no GravityWell blocker in the other side's non-fighter list (any
    /// state), and a unit of the fighter list, then the non-fighter list, in any state can jump, or side index 1 has a scene alive or unfinished.
    /// The original also zeroes one of the side's own fields here; that is not modelled, since every reader recomputes the strength first.
    /// </summary>
    internal static bool RetreatPossible(Side side, Side[] sides) =>
        side.RetreatAllowed && side.State == 0 && !sides[1 - side.Index].Ships.Any(u => u.Blocker)
        && (side.Fighters.Concat(side.Ships).Any(u => CanJump(u, side)) || side.Index == 1 && sides[1].Scene is { } sc && (Alive(sc) || sc.Unfinished));

    /// <summary>Withdraw decision: both strengths recomputed at decision time (the other side's stored).</summary>
    static bool Withdraw(Side side, Side[] sides)
    {
        if (!RetreatPossible(side, sides)) return false;
        if (side.Withdrawing) return true;
        var other = sides[1 - side.Index];
        double own = side.Strength = Strength(side, sides);
        if (own == 0) return true;
        double e = other.Strength = Strength(other, sides);
        return Outmatched(own, e, side.Threshold);
    }

    /// <summary>Recharge (with float rounding): a fighter's shield max compounds by hull / hull max; then current += R * dt up to max.</summary>
    internal static void Recharge(Unit u)
    {
        if (u.Type == 1)
        {
            u.ShieldMax = F(Scale(u, u.ShieldMax));
            u.Shield = F(Math.Min(u.Shield, u.ShieldMax));
        }
        if (u.ShieldMax - u.Shield > 0) u.Shield = F(Math.Min(u.ShieldMax, ShieldRate(u) * Dt + u.Shield));
    }

    // ---- finish and result ----

    /// <summary>
    /// Hangar dock: accepts while its count is below its capacity (the scene hangar too, Inferred).
    /// Docked: the carrier is set, state 1, count + 1.
    /// </summary>
    internal static bool Dock(Unit f, Unit carrier)
    {
        if (carrier.Docked >= carrier.Capacity) return false;
        (f.Carrier, f.State) = (carrier, 1);
        carrier.Docked++;
        return true;
    }

    /// <summary>
    /// Cannot jump, state 2 only: its own carrier (the Death Star scene while alive, else a capital ship in state
    /// 2), else the first ship of the side's non-fighter list in state 2 with room, else (side index 1) the live Death Star scene. Docked:
    /// state 5, out of its lists. Inferred: a full or gone own carrier falls through to the list.
    /// </summary>
    static bool Land(Unit u, Side side, Side[] sides)
    {
        if (u.State != 2) return false;
        var scene = sides[1].Scene;
        bool docked = u.Carrier is { } c && (c.Type == 2 ? Alive(scene) && Dock(u, scene!) : c.State == 2 && Dock(u, c));
        docked = docked || side.Ships.Any(x => x.State == 2 && Dock(u, x));
        docked = docked || side.Index == 1 && Alive(scene) && Dock(u, scene!);
        if (!docked) return false;
        u.State = 5;
        Remove(u, sides);
        return true;
    }

    /// <summary>
    /// Finish: per fighter of the battle list in state 1 / 2 / 5 / 9, the first capital ship of the list from
    /// its start on the fighter's side in state 2 / 9 with room takes it (no old-carrier preference); else, side index 1, the Death
    /// Star scene while alive or withdrawn; else it keeps its state and carrier.
    /// </summary>
    static void Finish(Side[] sides)
    {
        var battle = sides[0].Battle;
        foreach (var f in battle.Where(u => u.Type == 1 && u.State is 1 or 2 or 5 or 9).ToList())
        {
            if (battle.Any(x => x.Type == 0 && x.Side == f.Side && x.State is 2 or 9 && Dock(f, x))) continue;
            if (f.Side == 1 && sides[1].Scene is { } sc && (Alive(sc) || sc.Withdrawn)) Dock(f, sc);
        }
    }

    static BattleResult Result(Side[] sides)
    {
        var S = new int[7];
        bool Active(Side x) => x.Lists.Any(u => u.State == 2);
        S[0] = sides[0].State == 5 && sides[0].Units.Any(u => u.State == 9) ? 1 : 0;
        var scene = sides[1].Scene;
        S[2] = sides[1].State == 5 && (sides[1].Units.Any(u => u.State == 9) || scene?.Withdrawn == true) ? 1 : 0;
        S[1] = !Alive(scene) && !Active(sides[1]) && S[0] == 0 ? 1 : 0; // Not computed while the scene is alive
        S[3] = !Active(sides[0]) && S[2] == 0 ? 1 : 0;
        if (S[1] != 0 && S[3] != 0) S[1] = S[3] = 0;
        return Records(sides, S, true);
    }

    /// <summary>The records F / C / D of every unit of the battle list with the summary S; both branches (the tactical battle uses the same records).</summary>
    internal static BattleResult Records(Side[] sides, int[] S, bool auto)
    {
        var scene = sides[1].Scene;
        var fighters = new Dictionary<GameObject, FighterResult>();
        var ships = new Dictionary<GameObject, ShipResult>();
        var deathStars = new Dictionary<GameObject, DeathStarResult>();
        foreach (var u in sides.SelectMany(x => x.Units))
        {
            double c4 = u.State == 0xB ? 0 : Math.Max(1, u.Hull);
            // fighter record; a Death Star's key resolves to the current scene
            if (u.Type == 1) fighters[u.Object] = new FighterResult(u.State == 0xB ? 0 : Math.Max(1, (int)Math.Truncate(u.Hull / 8.0)), u.State is 1 or 5 ? Resolve(u.Carrier, sides)?.Object : null);
            else ships[u.Object] = new ShipResult((int)Math.Truncate(c4), u.SpeedCh, u.ShieldCh, u.HyperCh, u.TractorCh, u.WeaponCh);
        }
        // One D record from the current scene for every Death Star (its hull, and whether that is above 0)
        foreach (var ds in sides[1].DeathStars) deathStars[ds] = new DeathStarResult((int)Math.Truncate(scene!.Hull), scene.Hull > 0);
        return new BattleResult(auto, S, fighters, ships, deathStars);
    }
}
