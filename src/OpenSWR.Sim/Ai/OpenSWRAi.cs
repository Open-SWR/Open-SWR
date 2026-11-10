using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// openswr's own computer opponent, selectable instead of <see cref="ComputerController"/>. Not the
/// original's behaviour. It reads only what its side knows: own objects, the side's views (<see cref="Knowledge"/>,
/// <see cref="ScreenViews"/>) and the player-side queries; it acts only through outbox commands that pass
/// <see cref="Orders.Validate"/>, at most <see cref="Budget"/> per day, and draws nothing. Called every tick (stage 8); it decides
/// once per day, on the day's first think, so the commands of one decision have run before the next. Rules in order: garrisons,
/// defence, production, attack. Never takes tactical command; characters, missions, research and the Death Star are left alone.
/// </summary>
public sealed partial class OpenSWRController(Owner side) : Controller(side)
{
    public override int Kind => 4;

    /// <summary>Counted commands per day.</summary>
    public const int Budget = 10;
    /// <summary>Days a refused command (type, first target, argument) is not issued again.</summary>
    public const int RefusalDays = 5;
    /// <summary>Attack targets within this many travel days at speed 100 (the slowest speed; real fleets are faster) of the staging system.</summary>
    public const int ReachDays = 60;
    /// <summary>Support share below which an own system counts as at risk: the map filter's support glyph threshold (filter 0x10 level 2 starts at 60).</summary>
    public const int SafeShare = 60;
    /// <summary>Days between two chat lines to a human opponent (<see cref="OpenSWRTaunts"/>); an occasion inside the pause gets none.</summary>
    public const int TauntCooldownDays = 10;
    /// <summary>Days a target may stay too strong for the gathered fleets before it is passed over, and how long it then stays passed over.</summary>
    public const int TargetPatienceDays = 10, PassDays = 30;

    /// <summary>A system's display name for the chat lines (set by the strategy screen; null = "that system").</summary>
    public Func<SdCommon, string>? Names { get; set; }
    /// <summary>The chat lines (<see cref="OpenSWRTaunts.Parse"/>, supplied by the front end; null = no chat lines). Not saved.</summary>
    public OpenSWRTaunts? Taunts { get; set; }

    /// <summary>Counted commands of the current day.</summary>
    public int Issued { get; private set; }
    /// <summary>The day of the last decision (0 = none yet).</summary>
    public int Decided { get; private set; }
    readonly SortedDictionary<uint, int> _passed = []; // targets passed over: per system ref, the first day they count again

    /// <summary>Refusal memory, keyed by (type, first target ref, argument ref, first field = the product), giving the first day it may be issued again.</summary>
    readonly SortedDictionary<(int Type, uint Target, uint Arg, uint Field), int> _refused = [];
    public int Refusals => _refused.Count;

    /// <summary>Optional log of every issued command (result Ok) and every refused validation (tests).</summary>
    public List<(Command Command, OrderResult Result)>? IssueTrace { get; set; }

    Owner Enemy => Side == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    protected override void Start(GameState game) { }

    internal override void DayDue() => Issued = 0;

    /// <summary>
    /// The optional steps, for ablation runs. Predict: every use of the battle predictor; PredictBattle: only the engage-or-withdraw
    /// float (BattleFloat). Doctrine (<see cref="Doctrine"/>) is the five parts: DoctrineProduction (shipyards by role shortfall,
    /// fighters by enemy and hangar room), DoctrineGather (fleets join groups by role), DoctrineTroops (garrison / assault troops by
    /// role), DoctrineDefence (planetary guns and shields), DoctrineFighterYard (one shipyard per carrier group takes only fighter
    /// orders while hangars have room). EconomyFirst, Recruit, CovertExperience, Teams, Intel, Agents, Decoys, ExploreTrial,
    /// ExplorePrior, PoolUseful, EspionageIdle, Swarm, TroopFit, LandGate, Hold, Holdable, Pacify, Guard, Hunt, Siege, HuntStill,
    /// Bombard, Wings, Cascade and RimFirst are the newer parts; each is described where it is implemented.
    /// </summary>
    public enum V7
    {
        Experience, LateEconomy, Fighters, Watch, Predict, PredictBattle,
        DoctrineProduction, DoctrineGather, DoctrineTroops, DoctrineDefence, DoctrineFighterYard, EconomyFirst,
        Recruit, CovertExperience, Teams, Intel, Agents, Decoys,
        ExploreTrial, ExplorePrior, PoolUseful, EspionageIdle, TroopFit, Swarm, LandGate, Hold, Holdable, Pacify,
        Guard, Hunt, Siege, HuntStill, Bombard, Wings, Cascade, RimFirst,
    }

    /// <summary>The doctrine parts.</summary>
    public static readonly ImmutableHashSet<V7> Doctrine =
        [V7.DoctrineProduction, V7.DoctrineGather, V7.DoctrineTroops, V7.DoctrineDefence, V7.DoctrineFighterYard];

    /// <summary>The steps an "all" run plays with: every step from Experience through Siege (the later ones stay off, so "all" runs are unchanged).</summary>
    public static readonly ImmutableHashSet<V7> AllFeatures = [.. Enum.GetValues<V7>().Where(f => f <= V7.Siege)];

    /// <summary>
    /// The features of an aimatch command line: comma-separated step names, "all" (<see cref="AllFeatures"/>) and "doctrine"
    /// (<see cref="Doctrine"/>) among them; "none" for no step. Null for an unknown name.
    /// </summary>
    public static ImmutableHashSet<V7>? ParseFeatures(string list)
    {
        if (list.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        var set = ImmutableHashSet<V7>.Empty;
        foreach (var name in list.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            if (name.Equals("all", StringComparison.OrdinalIgnoreCase)) set = set.Union(AllFeatures);
            else if (name.Equals("doctrine", StringComparison.OrdinalIgnoreCase)) set = set.Union(Doctrine);
            else if (Enum.TryParse(name, ignoreCase: true, out V7 f) && Enum.IsDefined(f) && !int.TryParse(name, out _)) set = set.Add(f);
            else return null;
        return set;
    }

    /// <summary>
    /// The steps the OpenSWR AI plays with by default: the ablation's best over seeds 200001 and 300001 for the older steps, plus
    /// planetary guns and shields, which beat it over seeds 200001, 300001 and 400001 (aimatch, 3 x 100 games), and
    /// recruitment first (61 against 32 ahead or won, 41.07 % against 37.53 % share over the same seeds) and its covert gate from
    /// experience (73 against 61 ahead or won, 41.40 % against 41.07 %, characters lost 8.6 to 5.1 per game), and the
    /// special-force economy (193 against 73 ahead or won, 53.63 % against 41.40 % share), and the decoys from the idle pool
    /// (197 against 193 ahead or won, 53.70 % against 53.63 %, characters lost 5.57 to 4.10 per game), and the idle army
    /// (227 against 197 ahead or won over seeds 200001, 300001 and 400001; 150 against 133 on fresh seeds 988378518 and 722797669),
    /// and the production by role shortfall, DoctrineProduction (237 against 227 ahead or won, 57.8 % against
    /// 56.5 % share over the same seeds; 154 against 138 on fresh seeds 807195870 and 678857879), and the wing yards, Wings
    /// (day 3000, 100 games per seed: 29 + 35 against 21 + 34 ahead over seeds 200001 and 300001; 38 + 37 against 34 + 29 on fresh
    /// seeds 1025446208 and 346323676, 39.5 % against 37.5 % share).
    /// </summary>
    public static readonly ImmutableHashSet<V7> DefaultFeatures = [V7.Fighters, V7.Watch, V7.LateEconomy, V7.DoctrineDefence, V7.Recruit, V7.CovertExperience,
        V7.Agents, V7.Decoys, V7.Swarm, V7.DoctrineProduction, V7.Wings];

    /// <summary>The optional steps in use (default <see cref="DefaultFeatures"/>); a step off takes the older path. Not saved (a run setting).</summary>
    public ImmutableHashSet<V7> Features { get; set; } = DefaultFeatures;

    bool On(V7 f) => Features.Contains(f);

    internal override void Think(GameState game)
    {
        if (game.Day == Decided || game.GameOver) return;
        Decided = game.Day;
        foreach (var key in _refused.Where(e => e.Value <= game.Day).Select(e => e.Key).ToList()) _refused.Remove(key);
        foreach (var key in _foiled.Where(e => game.Day - e.Value >= CovertRetryDays).Select(e => e.Key).ToList()) _foiled.Remove(key);
        foreach (var key in _foilMarks.Where(e => game.Day - e.Value.Day >= FoilMarkDays).Select(e => e.Key).ToList()) _foilMarks.Remove(key);
        PruneGate(game.Day);
        var k = new Known(game, Side);
        var used = new HashSet<GameObject>();
        RefreshValues(game);
        foreach (var key in _watched.Where(e => game.Day - e.Value >= WatchDays).Select(e => e.Key).ToList()) _watched.Remove(key);
        if (On(V7.Watch)) RefreshIncoming(game, k);
        Need = TroopNeed(game, k);
        _step = "garrison"; // measurement only (TroopMoveLog)
        int deficit = Garrisons(game, used);
        _step = "defence";
        if (On(V7.Watch)) HoldOrEvacuate(game, k, used);
        var threatened = Defence(game, k, used);
        WatchThreats(game, k); // measurement (aimatch only; no effect on play)
        _step = "other";
        var map = Sectors(game);
        Production(game, k, used, deficit, map);
        if (On(V7.Bombard)) Bombard(game, k, used); // Bombard: before the landings
        if (RimFirsting) RimDrops(game, k, used); // RimFirst: before the attack, so Swarm leaves the drop fleet alone
        _step = "attack loading";
        Attack(game, k, used, threatened, map);
        _step = "other";
        if (On(V7.Fighters)) Carriers(game, used, threatened);
        if (Hunting) // Hunt: the HQ search set and the track measurement
        {
            UpdateHqSearch(game, k);
            TrackHunt(game, k);
        }
        Assign(game, k, map, used);
        if (Probe is not null) ClassifyIdle(game, used); // measurement only
        OwnerChanges(game, k);
        Arrivals(game);
    }

    /// <summary>Fight when the predicted win chance is at least 0.5 or the enemy's expected loss is at least 1.5 x the own (0.0 = never withdraw), else withdraw at once.</summary>
    internal override double BattleFloat(GameState game, StarSystem s)
    {
        var k = new Known(game, Side);
        int own = k.Strength(s, Side);
        if (own > 0 || s.Owner == Side) _battle = (game, s); // taking part: the result is read when the battle is done
        // the space predictor; fight at a win chance of 0.5 or more, or when the enemy is expected to lose at least 1.5
        // times the own value lost
        if (!On(V7.Predict) && !On(V7.PredictBattle)) return own >= k.Strength(s, Enemy) ? 0.0 : 10000.0;
        var p = PredictSpace(game, k, s, s.Contents.OfType<Fleet>().Where(f => f.Owner == Side && f.Usable), k.Fleets(s, Enemy));
        return p.Win >= 0.5 || p.EnemyLost >= 1.5 * p.OwnLost ? 0.0 : 10000.0;
    }

    /// <summary>A battle the side took part in has run: a chat line on a win or a loss.</summary>
    internal override void BattleDone()
    {
        if (_battle is not var (game, s)) return;
        _battle = null;
        // the real result, read only for a battle this side fought in: the participant learns it anyway (the battle report)
        if (s.BattleWon == Side) Taunt(game, OpenSWRTaunts.Occasion.BattleWon, s);
        else if (s.BattleWon == Enemy) Taunt(game, OpenSWRTaunts.Occasion.BattleLost, s);
    }

    // ---- chat lines (not a game rule: no command, no draw) ----

    (GameState, StarSystem)? _battle;
    int? _lastTaunt;
    readonly int[] _taunts = new int[OpenSWRTaunts.Keys.Length];
    SortedSet<uint>? _owned; // own systems at the last decision; null before the first
    SortedSet<uint> _enemyHeld = []; // systems this side saw as the other side's at the last decision
    uint? _launchedAt; // the target of the last launch, until an own fleet is there (its chat line waits for the arrival)

    /// <summary>
    /// Systems taken from or lost to the other side since the last decision: a chat line, losses first. Lost = own before, now
    /// seen as the other side's (not destroyed); taken = own now, seen as the other side's before. Neutral changes say nothing.
    /// </summary>
    internal void OwnerChanges(GameState game, Known? k = null)
    {
        var now = new SortedSet<uint>(Own(game).Select(s => s.Ref));
        if (_owned is not null)
        {
            foreach (uint r in _owned.Except(now))
                if (game.Systems.First(s => s.Ref == r) is var lost && Knowledge.See(game, lost, Side) is { Destroyed: false } v && v.Owner == Enemy)
                {
                    MarkLost(lost, game.Day); // a counter-attack target
                    RecordAssaultSeen(game, k, lost); // measurement (aimatch only)
                    // the troops the side sees there now landed in the assault (a loss without troops seen is no landing)
                    if (On(V7.Experience) && k is not null && EnemyTroops(k, lost) is > 0 and var landed) RecordLanding(landed);
                    Taunt(game, OpenSWRTaunts.Occasion.SystemLost, lost);
                }
            if (On(V7.Cascade)) RecordSupportGains(game, now.Except(_owned).Where(_enemyHeld.Contains)); // step 36
            foreach (uint r in now.Except(_owned).Where(_enemyHeld.Contains)) Taunt(game, OpenSWRTaunts.Occasion.SystemTaken, game.Systems.First(s => s.Ref == r));
        }
        _owned = now;
        _enemyHeld = [.. game.Systems.Where(s => Knowledge.See(game, s, Side) is { Destroyed: false } v && v.Owner == Enemy).Select(s => s.Ref)];
    }

    /// <summary>The attack's chat line once an own fleet has arrived at the launch target (not at the launch: the move is no news yet).</summary>
    void Arrivals(GameState game)
    {
        if (_launchedAt is not { } r || game.Systems.First(s => s.Ref == r) is not { } at) return;
        if (!Fleets(game).Any(f => f.Container == at && !f.Enroute)) return;
        _launchedAt = null;
        Taunt(game, OpenSWRTaunts.Occasion.AttackLaunched, at);
    }

    /// <summary>
    /// One chat line (category Chat; the system named, with go-to, only when the human side sees it, else "that system") when the
    /// other side is a human and the last line is at least
    /// <see cref="TauntCooldownDays"/> old. Each occasion's list is walked in turn from an offset of the game seed (the counter
    /// wraps, so a saved counter stays valid when the file's list changed length); an occasion without lines stays silent.
    /// </summary>
    void Taunt(GameState game, OpenSWRTaunts.Occasion occasion, StarSystem s)
    {
        if (Taunts?.Lines(occasion) is not { Count: > 0 } lines) return;
        if (!game.Controllers.TryGetValue(Enemy, out var other) || other is not HumanController) return;
        if (_lastTaunt is { } last && game.Day - last < TauntCooldownDays) return;
        string line = lines[(int)((uint)(game.Settings.Seed + _taunts[(int)occasion]++) % (uint)lines.Count)];
        _lastTaunt = game.Day;
        bool seen = ScreenViews.Sees(s, Enemy);
        Messages.Post(game, new GameMessage
        {
            Day = game.Day, Category = MessageCategory.Chat, Kind = 3,
            Text = line.Replace("{system}", seen ? Names?.Invoke(s.Definition.Common) ?? "that system" : "that system"),
            Subject = seen ? s.Ref : 0, New = true,
        });
    }

    // ---- command issue ----

    /// <summary>
    /// Refusal key: type, first target, argument, and in the last slot the product (production) or the mission type (0x240); a
    /// command with several targets keeps its own key (the high bit set, with the target count in that slot), apart from the lead target's single command.
    /// </summary>
    static (int, uint, uint, uint) KeyOf(Command c) => (c.Type, c.Targets.Count > 0 ? c.Targets[0].Target.Ref : 0, c.ArgRef?.Ref ?? 0,
        c.Targets.Count > 1 ? 0x80000000u + (uint)c.Targets.Count : c.Type == 0x240 ? c.Fields[2] : c.Fields.FirstOrDefault());

    /// <summary>Remembered as refused (tests).</summary>
    internal bool Refused(Command c) => _refused.ContainsKey(KeyOf(c));

    /// <summary>Validation only, with the refusal remembered as by <see cref="Issue"/>; nothing goes to the outbox.</summary>
    bool Valid(GameState game, Command c)
    {
        var key = KeyOf(c);
        if (_refused.ContainsKey(key)) return false;
        var result = Orders.Validate(game, c);
        if (result.Success) return true;
        IssueTrace?.Add((c, result)); // refusals here; an issued command is traced by Issue
        _refused[key] = game.Day + RefusalDays;
        return false;
    }

    bool Room => Issued < Budget;

    /// <summary>Validate, then the outbox (counted); a refusal is remembered for <see cref="RefusalDays"/>.</summary>
    internal bool Issue(GameState game, Command c, HashSet<GameObject> used)
    {
        if (!Room || GuardRefuses(game, c) || !Valid(game, c)) return false; // Guard
        IssueTrace?.Add((c, OrderResult.Ok));
        if (TroopMoveLog is not null && c.Type == 0x201) // measurement only: own troop moves by step
            foreach (var (t, _) in c.Targets)
                if (Family.IsTroop(t.Type) && Missions.Location(t) is StarSystem from) TroopMoveLog.Add(new(game.Day, t.Ref, from.Ref, _step));
        Append(c);
        Issued++;
        foreach (var (t, _) in c.Targets) used.Add(t);
        foreach (var d in c.Decoys) used.Add(d);
        return true;
    }

    static Command Move(Owner side, GameObject o, GameObject to) => new(0x201, side) { Targets = [(o, 0)], ArgRef = to };

    /// <summary>One own troop move order (measurement): day, troop, the system it left, the step that ordered it.</summary>
    public sealed record TroopMove(int Day, uint Troop, uint From, string Step);

    /// <summary>The own troop move orders (null = off; aimatch only, no effect on play).</summary>
    public List<TroopMove>? TroopMoveLog { get; set; }

    /// <summary>The step whose orders are being issued (measurement only).</summary>
    string _step = "other";

    // ---- own objects (own data: real values) ----

    List<StarSystem> Own(GameState game) => [.. game.Systems.Where(s => s.Owner == Side && !s.Destroyed).OrderBy(s => s.Key)];

    IEnumerable<Fleet> Fleets(GameState game) => game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == Side && f.Existing);

    /// <summary>An own fleet free to move: Usable, not Enroute, in a system, with a ship and a speed.</summary>
    bool Idle(GameState game, Fleet f, HashSet<GameObject> used) =>
        !used.Contains(f) && f.Usable && !f.Enroute && f.Container is StarSystem && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing) && Orders.Speed(game, f) > 0;

    static bool OwnTroop(GameObject x, Owner side) => Family.IsTroop(x.Type) && x.Owner == side && x.Existing;

    /// <summary>
    /// Troops an own system keeps: the garrison requirement when at risk (at least 1), else 1 in a populated system; a defended system
    /// at least its target garrison.
    /// </summary>
    int Keep(StarSystem s) => Math.Max(Garrison(s), Need.Defend.GetValueOrDefault(s));

    /// <summary>Rule 1's garrison of an own system: the garrison requirement when at risk (at least 1), else 1 in a populated system (step 37 RimFirst: 1 in an unpopulated rim system, the drop's troop).</summary>
    int Garrison(StarSystem s) => !s.Populated ? (RimFirsting && !s.Sector.IsCore ? 1 : 0) : AtRisk(s) ? Math.Max(1, s.GarrisonRequirement) : 1;

    // ---- troops by need ----

    /// <summary>The troop need of one decision: the total, the defended systems' target garrisons (those the troops cover), the attack troops per target.</summary>
    internal sealed record TroopNeeds(int Total, Dictionary<StarSystem, int> Defend, Dictionary<StarSystem, int> Attack)
    {
        public static readonly TroopNeeds None = new(0, [], []);
    }

    /// <summary>The troop need of the current decision (<see cref="TroopNeed"/>); not saved.</summary>
    internal TroopNeeds Need { get; private set; } = TroopNeeds.None;

    /// <summary>Known enemy troops at a system: the enemy's troops the side's view shows there.</summary>
    int EnemyTroops(Known k, StarSystem s) => k.Contents(s).Count(v => v.Owner == Enemy && v.In(0x10, 0x14));

    /// <summary>Attack troops for a target: 1.5 x the known enemy troops there + 1, at least 2.</summary>
    internal int AttackTroopsFor(Known k, StarSystem target) => Math.Max(2, (int)Math.Ceiling(1.5 * EnemyTroops(k, target)) + 1);

    /// <summary>
    /// The troop need, the sum of: the garrisons (rule 1's need of each own system at risk,
    /// 1 in every other populated own system); the defence garrisons beyond those (<see cref="Defence"/>: every high-value threatened
    /// system's target garrison, covered or not); the attack troops of each current target. <see cref="TroopNeeds.Defend"/> holds the
    /// systems the side's troops cover.
    /// </summary>
    internal TroopNeeds TroopNeed(GameState game, Known k)
    {
        var own = Own(game).Where(s => s.Populated).ToList();
        var (targets, covered) = Defence(game, k);
        var attack = Targets().ToDictionary(t => t, t => AttackTroopsFor(k, t));
        return new(own.Sum(Garrison) + targets.Sum(e => Math.Max(0, e.Value - Garrison(e.Key))) + attack.Values.Sum(), covered, attack);
    }


    /// <summary>Own troops: Existing, anywhere (in systems, aboard ships, under construction).</summary>
    internal int OwnTroops(GameState game) => game.Created.Count(o => OwnTroop(o, Side));

    bool AtRisk(StarSystem s) => s.Populated && (s.SupportFor(Side) < SafeShare || s.Uprising);

    /// <summary>Own troops in the system beyond <see cref="Keep"/> that can leave now.</summary>
    List<GameObject> Spare(StarSystem s, HashSet<GameObject> used)
    {
        var ready = s.Contents.Where(x => OwnTroop(x, Side) && x.Usable && !x.Enroute && !used.Contains(x)).OrderBy(x => x.Key).ToList();
        int extra = s.Contents.Count(x => OwnTroop(x, Side) && !used.Contains(x)) - Keep(s);
        return [.. ready.Take(Math.Clamp(extra, 0, ready.Count))];
    }

    /// <summary>Own troops aboard a fleet's ships that can leave now.</summary>
    List<GameObject> FleetTroops(Fleet f, HashSet<GameObject> used) =>
        [.. f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).SelectMany(x => x.Contents)
            .Where(x => OwnTroop(x, Side) && x.Usable && !x.Enroute && !used.Contains(x)).OrderBy(x => x.Key)];

    // ---- rule 1: garrisons ----

    /// <summary>
    /// Rule 1: every own system at risk (support share below <see cref="SafeShare"/> or an
    /// Uprising) needs max(1, garrison requirement) troops, counting those on the way and under construction; spare troops of the
    /// nearest own systems move there. Then the defended systems short of their target garrison, in priority order. Returns the
    /// troops still missing at the systems at risk (rule 3 trains them first).
    /// </summary>
    int Garrisons(GameState game, HashSet<GameObject> used)
    {
        var own = Own(game);
        int missing = 0;
        foreach (var s in own.Where(AtRisk))
        {
            int have = s.Contents.Count(x => OwnTroop(x, Side));
            int need = Keep(s);
            foreach (var from in own.Where(x => x != s && !AtRisk(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key))
                foreach (var troop in Spare(from, used))
                {
                    if (have >= need || !Room) break;
                    if (Issue(game, Move(Side, troop, s), used)) have++;
                }
            missing += Math.Max(0, need - have);
        }
        foreach (var s in Need.Defend.Keys.Where(x => !AtRisk(x) && own.Contains(x)))
        {
            int have = s.Contents.Count(x => OwnTroop(x, Side));
            // the spare troops, nearest first: those beyond the keep of the systems not defended, and those aboard idle
            // fleets outside the attack groups (fleets at a group's staging system)
            var staging = StagingSystems.ToHashSet();
            var sources = !On(V7.Experience)
                ? own.Where(x => x != s && !AtRisk(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).Select(x => (At: x, Troops: Spare(x, used))).ToList()
                : own.Where(x => x != s && !AtRisk(x) && !Need.Defend.ContainsKey(x)).Select(x => (At: x, Troops: Spare(x, used)))
                    .Concat(Fleets(game).Where(f => Idle(game, f, used) && f.Container is StarSystem at && !staging.Contains(at) && at != s)
                        .Select(f => (At: (StarSystem)f.Container!, Troops: FleetTroops(f, used))))
                    .OrderBy(x => Orders.Distance(x.At, s)).ThenBy(x => x.At.Key).ToList();
            foreach (var (_, troops) in sources)
                foreach (var troop in troops)
                {
                    if (have >= Keep(s) || !Room) break;
                    if (Issue(game, Move(Side, troop, s), used)) have++;
                }
        }
        return missing;
    }

    // ---- rule 2: defence ----

    /// <summary>
    /// Rule 2: an own system with known enemy fleets (seen there, or seen Enroute there: map filter 0x22's reading) where the own
    /// fleets' predicted win chance is below 0.5 gets the idle own fleet (of the 6 nearest) with the highest win chance per value
    /// moved that brings it to 0.5; if none would, the own idle fleets there leave for the nearest own system without
    /// known enemy fleets. Every own fleet at a threatened system is then committed for the day
    /// (rule 4 does not gather it); returns the threatened systems.
    /// </summary>
    internal List<StarSystem> Defence(GameState game, Known k, HashSet<GameObject> used)
    {
        var own = Own(game);
        var threatened = own.Where(s => k.Fleets(s, Enemy).Count > 0).ToList();
        if (!On(V7.Predict)) return DefenceV6(game, k, used, own, threatened);
        // the space predictor's win chance of the own fleets there (plus one more) against the known enemy fleets
        List<Fleet> Here(StarSystem s) => [.. Fleets(game).Where(f => f.Container == s && f.Usable)];
        double Win(StarSystem s, Fleet? extra = null) => PredictSpace(game, k, s, extra is null ? Here(s) : [.. Here(s), extra], k.Fleets(s, Enemy)).Win;
        // fleets holding a threatened system stay; no fleet at a threatened system is sent to help another
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x) && Win(x) >= 0.5).ToList()) used.Add(f);
        foreach (var s in threatened)
        {
            if (!Room) break;
            double win = Win(s);
            if (win >= 0.5) continue;
            // the help with the highest win chance per value moved (the 6 nearest idle fleets tried), when it brings the chance to 0.5
            var help = Fleets(game).Where(f => f.Container is StarSystem x && !threatened.Contains(x) && Idle(game, f, used))
                .OrderBy(f => Orders.Distance((StarSystem)f.Container!, s)).ThenBy(f => f.Key).Take(6)
                .Select(f => (f, w: Win(s, f), v: Math.Max(1, k.Strength(f, Side)))).Where(x => x.w >= 0.5)
                .OrderByDescending(x => x.w / x.v).ThenBy(x => x.f.Key).Select(x => x.f).FirstOrDefault();
            if (help is not null && Issue(game, Move(Side, help, s), used)) continue;
            var safe = own.Where(x => x != s && !threatened.Contains(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault();
            if (safe is null) continue;
            foreach (var f in Fleets(game).Where(f => f.Container == s && Idle(game, f, used)).OrderBy(f => f.Key).ToList())
                Issue(game, Move(Side, f, safe), used);
        }
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x))) used.Add(f);
        return threatened;
    }

    /// <summary>Rule 2 of the value-sum path (value sums), used with <see cref="V7.Predict"/> off.</summary>
    List<StarSystem> DefenceV6(GameState game, Known k, HashSet<GameObject> used, List<StarSystem> own, List<StarSystem> threatened)
    {
        int Enemies(StarSystem s) => k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy));
        int Mine(StarSystem s) => Fleets(game).Where(f => f.Container == s).Sum(f => k.Strength(f, Side));
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x) && Mine(x) >= Enemies(x)).ToList()) used.Add(f);
        foreach (var s in threatened)
        {
            if (!Room) break;
            int enemy = Enemies(s), mine = Mine(s);
            if (mine >= enemy) continue;
            var help = Fleets(game).Where(f => f.Container is StarSystem x && !threatened.Contains(x) && Idle(game, f, used) && mine + k.Strength(f, Side) >= enemy)
                .OrderBy(f => Orders.Distance((StarSystem)f.Container!, s)).ThenBy(f => f.Key).FirstOrDefault();
            if (help is not null && Issue(game, Move(Side, help, s), used)) continue;
            var safe = own.Where(x => x != s && !threatened.Contains(x)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault();
            if (safe is null) continue;
            foreach (var f in Fleets(game).Where(f => f.Container == s && Idle(game, f, used)).OrderBy(f => f.Key).ToList())
                Issue(game, Move(Side, f, safe), used);
        }
        foreach (var f in Fleets(game).Where(f => f.Container is StarSystem x && threatened.Contains(x))) used.Add(f);
        return threatened;
    }

    // ---- rule 3: production ----

    /// <summary>The production need of one decision: a weighted shortfall per category, and the phase.</summary>
    internal sealed record ProductionPlan(string Phase, double Troops, double CapitalShips, double Fighters, double SpecialForces, double Defence);

    /// <summary>
    /// The plan: per category the shortfall (need - have) / need (0 when nothing is needed) x the
    /// phase's weight. Needs: troops the troop need (<see cref="TroopNeed"/>); capital ships a fleet value of max(1.2 x the known enemy
    /// fleet value within reach of the power base, 1.5 x each attack group's target defence); fighters the fighter room of own
    /// capital ships; special forces the scouting units (the covert special forces are not built: deviation); defence
    /// facilities the systems that want one (<see cref="DefenceSites"/>). Phases: expansion (day &lt; 150, or under 30 % of the known
    /// populated systems own): special forces 1.5; build-up (own fleet value below the known enemy's): capital ships 1.5; else hold:
    /// troops 1.5, defence facilities 1.2.
    /// </summary>
    internal ProductionPlan Plan(GameState game, Known k, SectorMap map)
    {
        double Short(double need, double have) => need <= 0 ? 0 : Math.Clamp((need - have) / need, 0, 1);
        var fleets = Fleets(game).ToList();
        int own = fleets.Sum(f => k.Strength(f, Side));
        var powerBase = Own(game).Where(s => map.PowerBase.Contains(s.Sector)).ToList();
        var enemyFleets = game.Systems.Where(s => powerBase.Any(b => Orders.TravelDays(game, b, s, 100) <= ReachDays))
            .Sum(s => k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy)));
        int allEnemy = game.Systems.Sum(s => k.Fleets(s, Enemy).Sum(f => k.Strength(f, Enemy)));
        double fleetNeed = Math.Max(1.2 * enemyFleets, _groups.Sum(g => 1.5 * k.Strength(g.Target, Enemy)));
        var ships = fleets.SelectMany(f => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)).ToList();
        int fighterRoom = ships.Sum(x => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == x.Type)?.FighterCapacity ?? 0));
        int fighters = ships.Sum(x => x.Contents.Count(y => Family.Of(y.Type) is >= 0x1C and < 0x20));
        int scoutsWanted = ScoutUnitsMissing(game, map) > 0 ? ScoutUnitsWanted : 0;
        var known = game.Systems.Select(s => Knowledge.See(game, s, Side)).Where(v => v.Populated && !v.Destroyed).ToList();
        bool expansion = game.Day < 150 || known.Count > 0 && known.Count(v => v.Owner == Side) < 0.3 * known.Count;
        bool buildUp = !expansion && own < allEnemy;
        string phase = expansion ? "expansion" : buildUp ? "build-up" : "hold";
        return new(phase,
            Short(Need.Total, OwnTroops(game)) * (phase == "hold" ? 1.5 : 1),
            Short(fleetNeed, own) * (buildUp ? 1.5 : 1),
            Short(fighterRoom, fighters),
            (On(V7.Agents) ? Short(AgentPool * game.Sides[Side].MaintenanceCapacity, SpecialForces(game).Sum(a => game.MaintenanceCost(a.Type)))
                : On(V7.Teams) || On(V7.Intel) ? Short(scoutsWanted + ForceNeed, SpecialForces(game).Count) : Short(scoutsWanted, ScoutUnits(game).Count)) * (expansion ? 1.5 : 1),
            (DefenceSites(game, k).Count > 0 ? 1 : 0) * (phase == "hold" ? 1.2 : 1));
    }

    /// <summary>Late economy: construction yards order only Mines and Refineries (set below 10 % free maintenance, cleared above 25 %; saved).</summary>
    internal bool EconomyOnly { get; private set; }

    /// <summary>The construction-yard target is max(<see cref="MinConstructionYards"/>, ceil(r x owned systems)), r by default this.</summary>
    public const double DefaultYardRatio = 0.75;

    public const int MinConstructionYards = 2;

    /// <summary>The construction-yard ratio r of EconomyFirst (a run setting for aimatch; not saved).</summary>
    public double YardRatio { get; set; } = DefaultYardRatio;

    /// <summary>The production plan of the current decision; not saved.</summary>
    internal ProductionPlan CurrentPlan { get; private set; } = new("expansion", 0, 0, 0, 0, 0);

    /// <summary>
    /// The production facility kind a construction yard orders: shipyard (0x28), then
    /// training facility (0x29), then construction yard (0x2A); a kind with an idle manager (a non-suspended member, empty queue) is
    /// skipped; the facility must be buildable with its maintenance within <paramref name="free"/>. Null: none.
    /// </summary>
    internal SdRecord? ProductionFacility(GameState game, int free)
    {
        var managers = Own(game).SelectMany(s => ScreenViews.Managers(s, Side)).Where(m => Economy.Members(m).Any(f => !f.Suspended)).ToList();
        foreach (var (kind, family) in new[] { (ManufacturingKind.Ship, 0x28u), (ManufacturingKind.Training, 0x29u), (ManufacturingKind.Construction, 0x2Au) })
        {
            if (managers.Any(m => m.Kind == kind && m.Orders.Count == 0)) continue;
            if (Pick(game, 0x2A, family) is { } facility && game.MaintenanceCost(facility.Common.Ref) <= free) return facility;
        }
        return null;
    }

    /// <summary>
    /// Rule 3. Construction yards first, then the other managers by their category's weighted shortfall (training: troops
    /// or special forces; shipyards: capital ships or fighters), highest first. Construction yards: a training facility when the side
    /// has none; else a shield or battery for a system that wants one (<see cref="DefenceSites"/>); else, with the free
    /// maintenance at 25 % of the capacity or more, a production facility (<see cref="ProductionFacility"/>) at the safe own system
    /// with room, interior before border, highest value first; else, below 25 %, whichever of Mine / Refinery has the lower total
    /// capacity, nearest the yard. Training facilities: troops for the garrisons still missing at the systems at risk, then the higher
    /// shortfall of scouting units and troops. Shipyards: the higher shortfall of capital ships (the strongest that fits) and fighters
    /// (for an own carrier in its system with room). Every order fits the free maintenance (the production window's estimate).
    /// </summary>
    /// <summary>
    /// Measurement (aimatch only; null = off): for every own manager at the last production decision, why it
    /// placed no order (or "ordered" / "busy"). Recording only: it changes no decision.
    /// </summary>
    public Dictionary<ManufacturingManager, string>? IdleReasons { get; set; }

    void Why(ManufacturingManager m, string reason)
    {
        if (IdleReasons is not null) IdleReasons[m] = reason;
    }

    /// <summary>
    /// One production order turned down by the estimate (measurement, recorded only while <see cref="IdleReasons"/> is on): the
    /// validation result, whether the build-day estimate failed (no non-suspended member) while validation passed, the estimate's
    /// maintenance over the AI's free, the product and count, the AI's free against the side's real free maintenance, the manager's
    /// system against its TargetKey, the TargetKey's owner, and whether the queue was non-empty.
    /// </summary>
    public sealed record RefusedEstimate(int Day, ManufacturingManager Manager, uint Product, int Count, OrderResult Validation, bool EstimateNull,
        bool MaintenanceOver, int AiFree, int RealFree, bool SameSystem, Owner TargetOwner, bool QueueNonEmpty);

    /// <summary>Every refused estimate (measurement; null = off).</summary>
    public List<RefusedEstimate>? RefusedEstimates { get; set; }

    /// <summary>Why the last <see cref="Build"/> placed nothing (measurement).</summary>
    string _buildFail = "";

    internal void Production(GameState game, Known k, HashSet<GameObject> used, int deficit, SectorMap map)
    {
        var state = game.Sides[Side];
        IdleReasons?.Clear();
        var reconTypes = ReconTypes(game, Side);
        int scoutsMissing = ScoutUnitsMissing(game, map);
        // no training facility, neither Usable nor on its way (under construction, or Completed and still travelling from
        // the yard to its site: Enroute, not yet Usable) (own facilities: real state)
        bool trainingWanted = !game.Facilities.Any(f => f.Owner == Side && f.Existing && Family.Of(f.Type) == 0x29);
        // late economy: below 10 % free maintenance only economy facilities, until back above 25 %
        int free0 = state.MaintenanceCapacity - state.MaintenanceAllocated;
        if (!On(V7.LateEconomy)) EconomyOnly = false;
        else if (10 * free0 < state.MaintenanceCapacity) EconomyOnly = true;
        else if (4 * free0 > state.MaintenanceCapacity) EconomyOnly = false;
        var homeOrigin = map.Home is { } home ? game.Systems.Where(x => x.Sector == home).OrderBy(x => x.Key).First() : null;
        int free = state.MaintenanceCapacity - state.MaintenanceAllocated, capacity = state.MaintenanceCapacity;
        var own = Own(game);
        int troops = OwnTroops(game);
        var plan = CurrentPlan = Plan(game, k, map);
        var agentsOrdered = new HashSet<uint>(); // Agents: the types ordered this decision (each facility takes the next short type)
        var defenceSites = On(V7.DoctrineDefence) ? DoctrineDefenceSites(game, k) : DefenceSites(game, k);
        if (RimFirsting) defenceSites.AddRange(RimDefenceSites(game)); // step 37: a shield and a gun at the rim drops before facilities
        var fighterYards = On(V7.DoctrineFighterYard) ? FighterYards(game) : null;
        // fighters ordered into each carrier this decision: the orders run only after the decision, so two yards would otherwise
        // both fill the same free room and the later order is refused at execution (0x14, 1)
        var hangarOrdered = new Dictionary<Fleet, int>();
        int HangarLeft(Fleet f) => FighterRoom(game, f) - hangarOrdered.GetValueOrDefault(f);
        int BuildFighters(ManufacturingManager m, SdRecord fighter, Fleet carrier, int count, ref int free)
        {
            int n = Build(game, 0x211, m, fighter, count - hangarOrdered.GetValueOrDefault(carrier), ref free, used);
            hangarOrdered[carrier] = hangarOrdered.GetValueOrDefault(carrier) + n;
            return n;
        }
        var wingYards = On(V7.Wings) ? WingYards(game, k, used, fighterYards) : null; // Wings
        int newYards = 0; // construction yards ordered this decision
        var enemies = game.Systems.Where(x => Knowledge.See(game, x, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        bool Border(StarSystem x) => enemies.Any(e => Orders.TravelDays(game, x, e, 100) <= ReachDays);
        var home2 = map.Home;
        int Capacity(uint family) => game.Facilities.Where(f => f.Owner == Side && Family.Of(f.Type) == family && f.Usable && !f.Suspended).Sum(f => f.Capacity);
        double Score(ManufacturingKind kind) => kind switch
        {
            ManufacturingKind.Construction => double.MaxValue,
            ManufacturingKind.Training => Math.Max(plan.Troops, plan.SpecialForces),
            _ => Math.Max(plan.CapitalShips, plan.Fighters),
        };
        foreach (var (s, m) in own.SelectMany(s => ScreenViews.Managers(s, Side).Select(m => (s, m)))
                     .OrderByDescending(x => Score(x.m.Kind)).ToList()) // stable: else system order
        {
            if (!Room)
            {
                if (IdleReasons is not null)
                    foreach (var (_, rest) in own.SelectMany(x => ScreenViews.Managers(x, Side).Select(y => (x, y))).Where(x => !IdleReasons.ContainsKey(x.y)))
                        Why(rest, rest.Orders.Count > 0 ? "busy" : "budget");
                return;
            }
            if (used.Contains(m) || m.Orders.Count > 0 || !Economy.Members(m).Any(f => !f.Suspended))
            {
                Why(m, m.Orders.Count > 0 ? "busy" : used.Contains(m) ? "used" : Economy.Members(m).Any() ? "suspended" : "no facility");
                continue;
            }
            Why(m, "no order");
            switch (m.Kind)
            {
                case ManufacturingKind.Construction:
                {
                    SdRecord? facility = null;
                    StarSystem? site = null;
                    if (EconomyOnly) { } // straight to the economy facilities below
                    else if (trainingWanted && TrainingFacility(game, free) is { } t)
                    {
                        // a training facility goes to the own system nearest the home sector with room; one at a time
                        facility = t;
                        site = own.Where(x => x.Energy > x.UsedEnergy).OrderBy(map.RingOf).ThenBy(x => homeOrigin is null ? 0 : Orders.Distance(homeOrigin, x)).ThenBy(x => x.Key).FirstOrDefault();
                        trainingWanted = false;
                    }
                    else if (defenceSites.FirstOrDefault() is var (at, product) && at is not null && game.MaintenanceCost(product!.Common.Ref) <= free)
                    {
                        (facility, site) = (product, at); // a shield or battery where the priority wants one
                        defenceSites.RemoveAt(0);
                    }
                    else if (On(V7.EconomyFirst))
                    {
                        (facility, site) = EconomyFirstPick(game, m, s, free, newYards, Border, home2);
                        if (facility is not null && Family.Of(facility.Common.Ref) == 0x2A) newYards++;
                    }
                    else if (4 * free >= capacity && ProductionFacility(game, free) is { } p)
                    {
                        facility = p; // production facilities first
                        site = own.Where(x => x.Energy > x.UsedEnergy && !AtRisk(x)).OrderBy(x => Border(x) ? 1 : 0).ThenByDescending(x => Value(game, x, home2)).ThenBy(x => x.Key).FirstOrDefault();
                    }
                    if (IdleReasons is not null && facility is null && !EconomyOnly && !On(V7.EconomyFirst) && 4 * free >= capacity)
                        Why(m, ProductionFacility(game, int.MaxValue) is null ? "gated: production facility busy-manager + mine 25 %" : "gated: production facility maintenance + mine 25 %");
                    if (facility is null && (EconomyOnly || 4 * free < capacity && !On(V7.EconomyFirst)))
                    {
                        uint family = Capacity(0x2C) <= Capacity(0x2D) ? 0x2Cu : 0x2Du;
                        facility = Pick(game, 0x2A, family);
                        if (facility is null) Why(m, "none buildable");
                        // the own system with the most free room for it (a Mine needs a raw-material and an energy slot, a
                        // Refinery an energy slot); the yard's current site first when it has room (no retarget), then nearest the yard
                        int RoomFor(StarSystem x) => family == 0x2C ? Math.Min(x.RawMaterials - x.UsedRaw, x.Energy - x.UsedEnergy) : x.Energy - x.UsedEnergy;
                        site = !On(V7.LateEconomy)
                            ? own.Where(x => x.Energy > x.UsedEnergy && (family != 0x2C || x.RawMaterials > x.UsedRaw)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault()
                            : own.Where(x => RoomFor(x) > 0).OrderBy(x => x == m.TargetKey ? 0 : 1).ThenByDescending(RoomFor)
                                .ThenBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault();
                    }
                    if (facility is not null && site is null) Why(m, "no site");
                    if (facility is null || site is null) break;
                    if (m.TargetKey != site)
                    {
                        Why(m, Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = site }, used) ? "retarget" : Room ? "refused" : "budget");
                        break; // the order follows at the next decision, once the TargetKey has moved (validation places at the current one)
                    }
                    Why(m, Build(game, 0x210, m, facility, 1, ref free, used) > 0 ? "ordered" : _buildFail);
                    break;
                }
                case ManufacturingKind.Training:
                {
                    var scout = scoutsMissing > 0 ? Research.Buildable(game, Side, 0x29).Where(r => reconTypes.Contains(r.Common.Ref))
                        .OrderBy(r => game.MaintenanceCost(r.Common.Ref)).ThenBy(r => r.Common.Ref).FirstOrDefault() : null;
                    // the garrisons at risk first; then scouting units when their shortfall is the larger
                    if (deficit == 0 && scout is not null && plan.SpecialForces >= plan.Troops)
                    {
                        int built = Build(game, 0x212, m, scout, scoutsMissing, ref free, used);
                        Why(m, built > 0 ? "ordered" : _buildFail);
                        scoutsMissing -= built;
                        if (built > 0) break;
                    }
                    // Agents: the broad pool, espionage types first, when the special forces' shortfall is the larger
                    if (On(V7.Agents) && deficit == 0 && plan.SpecialForces >= plan.Troops && TrainAgents(game, m, ref free, used, agentsOrdered)) break;
                    int want = deficit > 0 ? deficit : Math.Max(0, Need.Total - troops);
                    Why(m, want > 0 ? "none buildable" : "need met");
                    if (want > 0 && (On(V7.DoctrineTroops) ? DoctrineTroop(game, deficit, free) : Pick(game, 0x29, 0x10, cheapest: true)) is { } troop)
                    {
                        if (deficit > 0 && own.FirstOrDefault(x => AtRisk(x) && x.Contents.Count(y => OwnTroop(y, Side)) < Keep(x)) is { } risk && m.TargetKey != risk)
                        {
                            Why(m, Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = risk }, used) ? "retarget" : Room ? "refused" : "budget");
                            break; // troops at the next decision, at the at-risk system
                        }
                        int n = Build(game, 0x212, m, troop, want, ref free, used);
                        Why(m, n > 0 ? "ordered" : _buildFail);
                        if (deficit > 0) deficit -= n;
                        troops += n;
                        if (n > 0) break;
                    }
                    if (deficit == 0 && scout is not null)
                    {
                        int built = Build(game, 0x212, m, scout, scoutsMissing, ref free, used);
                        if (built > 0) Why(m, "ordered");
                        scoutsMissing -= built;
                    }
                    if (On(V7.Agents) && deficit == 0 && m.Orders.Count == 0 && !used.Contains(m)) TrainAgents(game, m, ref free, used, agentsOrdered);
                    // Teams and Intel: covert and intel special forces up to the demand, when their shortfall is the larger
                    if ((On(V7.Teams) || On(V7.Intel)) && deficit == 0 && m.Orders.Count == 0 && !used.Contains(m) && plan.SpecialForces >= plan.Troops
                        && ForceNeed - (SpecialForces(game).Count - ScoutUnits(game).Count) is > 0 and var wantedForces && CovertForce(game) is { } force)
                    {
                        int built = Build(game, 0x212, m, force, wantedForces, ref free, used);
                        if (built > 0) Why(m, "ordered");
                    }
                    break;
                }
                case ManufacturingKind.Ship:
                {
                    // a fighter yard takes only fighter orders while its carrier group has room
                    if (fighterYards is not null && fighterYards.TryGetValue(m, out var group))
                    {
                        bool here = group.Container == s;
                        Why(m, "gated: fighter yard");
                        if ((DoctrineFighter(game, k, free, jumping: !here) ?? (here ? Pick(game, 0x28, 0x1C, cheapest: true) : null)) is not { } yardFighter) break;
                        if (m.TargetKey != group) Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = group }, used);
                        else BuildFighters(m, yardFighter, group, FighterRoom(game, group), ref free);
                        break;
                    }
                    // with Fighters: the free room net of the fighters already queued for the carrier
                    var carrier = s.Contents.OfType<Fleet>().Where(f => f.Owner == Side && f.Existing && f.Completed && !f.Enroute && HangarLeft(f) > 0
                        && (!On(V7.Fighters) || FreeHangar(game, f) - hangarOrdered.GetValueOrDefault(f) > 0))
                        .OrderBy(f => f.Key).FirstOrDefault();
                    var fighter = On(V7.DoctrineProduction) ? DoctrineFighter(game, k, free) ?? Pick(game, 0x28, 0x1C, cheapest: true) : Pick(game, 0x28, 0x1C, cheapest: true);
                    // a carrier with room at this yard gets its fighters first when the fighter shortfall is not the smaller
                    bool fightersFirst = On(V7.Fighters) ? plan.Fighters > 0 && plan.Fighters >= plan.CapitalShips && carrier is not null && fighter is not null
                        : plan.Fighters > plan.CapitalShips && carrier is not null && fighter is not null;
                    if (On(V7.DoctrineProduction) && carrier is not null && fighter is not null) fightersFirst = true; // fill all hangar room
                    // no carrier here: fighters that can jump for the nearest own carrier with room elsewhere (the yard's
                    // current target first), when the fighter shortfall is not the smaller one
                    if (On(V7.DoctrineProduction) && carrier is null && plan.Fighters > 0 && plan.Fighters >= plan.CapitalShips
                        && DoctrineFighter(game, k, free, jumping: true) is { } jumper && RemoteCarrier(game, m, s, HangarLeft) is { } remote)
                    {
                        if (m.TargetKey != remote) Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = remote }, used);
                        else BuildFighters(m, jumper, remote, FighterRoom(game, remote), ref free);
                        break;
                    }
                    var buildable = Research.Buildable(game, Side, 0x28).Where(r => Family.IsCapitalShip(r.Common.Ref) && !DeathStar.IsDeathStar(r.Common.Ref)).ToList();
                    var ships = buildable.Where(r => game.MaintenanceCost(r.Common.Ref) <= free).OrderByDescending(r => k.Value(r.Common.Ref)).ThenBy(r => r.Common.Ref).ToList();
                    // with the fleet need met, or the strongest ship not fitting the maintenance, ships with fighter room
                    // first, by (fighter capacity x fighter value + value) per maintenance
                    var strongest = buildable.OrderByDescending(r => k.Value(r.Common.Ref)).ThenBy(r => r.Common.Ref).FirstOrDefault();
                    if (On(V7.Fighters) && ships.Count > 0 && (plan.CapitalShips <= 0 || strongest is not null && ships[0] != strongest))
                    {
                        int fighterValue = fighter is null ? 0 : k.Value(fighter.Common.Ref);
                        double Worth(SdRecord r) => (double)(CarrierRoom(game, r.Common.Ref) * fighterValue + k.Value(r.Common.Ref)) / Math.Max(1, game.MaintenanceCost(r.Common.Ref));
                        ships = [.. ships.OrderBy(r => CarrierRoom(game, r.Common.Ref) > 0 ? 0 : 1).ThenByDescending(Worth).ThenBy(r => r.Common.Ref)];
                    }
                    if (On(V7.DoctrineProduction)) ships = DoctrineShip(game, k, free) is { } role ? [role] : []; // the role with the largest shortfall
                    // Wings: the threatened system's fighters at its wing yard, after the carriers' room
                    if (wingYards is not null && !fightersFirst && wingYards.TryGetValue(m, out var wing)
                        && WingOrder(game, k, m, s, wing, ships.FirstOrDefault(), ref free, used)) break;
                    Why(m, ships.Count > 0 ? "refused" : buildable.Count > 0 ? "gated: maintenance" : "none buildable");
                    if (!fightersFirst && ships.Count > 0)
                    {
                        // with Fighters: a target left on a fleet that travels, is gone or is elsewhere goes back to the
                        // yard's own system first; the capital ship follows at the next decision
                        if (On(V7.Fighters) && StaleTarget(m, s))
                        {
                            Why(m, Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = s }, used) ? "retarget" : Room ? "refused" : "budget");
                            break;
                        }
                        bool ordered = Build(game, 0x211, m, ships[0], 1, ref free, used) > 0;
                        Why(m, ordered ? "ordered" : _buildFail);
                        if (ordered) break;
                    }
                    if (fighter is null || carrier is null)
                    {
                        // no fighter order here: a target still on a fleet goes back to the yard's own system
                        if (On(V7.Fighters) && StaleTarget(m, s))
                            Why(m, Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = s }, used) ? "retarget" : Room ? "refused" : "budget");
                        break;
                    }
                    if (m.TargetKey != carrier)
                    {
                        Why(m, Issue(game, new Command(0x214, Side) { Targets = [(m, 0)], ArgRef = carrier }, used) ? "retarget" : Room ? "refused" : "budget");
                        break; // fighters at the next decision, into the carrier
                    }
                    Why(m, BuildFighters(m, fighter, carrier, On(V7.Fighters) ? FreeHangar(game, carrier) : FighterRoom(game, carrier), ref free) > 0 ? "ordered" : _buildFail);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// An idle construction yard's order, the first that
    /// applies: another construction yard while the side has fewer than max(<see cref="MinConstructionYards"/>, ceil(<see cref="YardRatio"/>
    /// x owned systems)), at a
    /// safe own system, interior first; else the fewer of Mine / Refinery in the own system with the most free room of its slot kind
    /// (no free-maintenance gate); else a production facility by the older order and gates. (null, null): none.
    /// </summary>
    internal (SdRecord? Facility, StarSystem? Site) EconomyFirstPick(GameState game, ManufacturingManager m, StarSystem s, int free, int ordered,
        Func<StarSystem, bool>? border = null, Sector? home = null)
    {
        var own = Own(game);
        if (border is null)
        {
            var enemies = game.Systems.Where(x => Knowledge.See(game, x, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
            border = x => enemies.Any(e => Orders.TravelDays(game, x, e, 100) <= ReachDays);
        }
        StarSystem? Safe() => own.Where(x => x.Energy > x.UsedEnergy && !AtRisk(x)).OrderBy(x => border(x) ? 1 : 0).ThenByDescending(x => Value(game, x, home)).ThenBy(x => x.Key).FirstOrDefault();
        int yards = game.Facilities.Count(f => f.Owner == Side && f.Existing && Family.Of(f.Type) == 0x2A) + ordered;
        if (yards < YardTarget(own.Count) && Pick(game, 0x2A, 0x2A) is { } yard
            && game.MaintenanceCost(yard.Common.Ref) <= free && Safe() is { } at)
            return (yard, at);
        int Count(uint family) => game.Facilities.Count(f => f.Owner == Side && f.Existing && Family.Of(f.Type) == family);
        uint family = Count(0x2C) <= Count(0x2D) ? 0x2Cu : 0x2Du;
        int RoomFor(StarSystem x) => family == 0x2C ? Math.Min(x.RawMaterials - x.UsedRaw, x.Energy - x.UsedEnergy) : x.Energy - x.UsedEnergy;
        if (Pick(game, 0x2A, family) is { } economy
            && own.Where(x => RoomFor(x) > 0).OrderBy(x => x == m.TargetKey ? 0 : 1).ThenByDescending(RoomFor).ThenBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault() is { } site)
            return (economy, site);
        return ProductionFacility(game, free) is { } pf && Safe() is { } safe ? (pf, safe) : (null, null);
    }

    /// <summary>The construction-yard target of EconomyFirst for a number of owned systems.</summary>
    internal int YardTarget(int owned) => Math.Max(MinConstructionYards, (int)Math.Ceiling(YardRatio * owned));

    /// <summary>
    /// The training facility a construction yard would order now, or null: buildable, its maintenance within
    /// <paramref name="free"/>, and no manufacturing waiting for refined material (the Refinery comes first then). Whether the side
    /// already has one is the caller's test.
    /// </summary>
    internal SdRecord? TrainingFacility(GameState game, int free) =>
        game.Sides[Side].ManufacturingWaitList.Count == 0 && Pick(game, 0x2A, 0x29) is { } t && game.MaintenanceCost(t.Common.Ref) <= free ? t : null;

    /// <summary>The buildable product of the manager family in [family, family + 4) (the strongest, or the cheapest in maintenance).</summary>
    SdRecord? Pick(GameState game, uint manager, uint family, bool cheapest = false)
    {
        var list = Research.Buildable(game, Side, manager).Where(r => Family.Of(r.Common.Ref) >= family && Family.Of(r.Common.Ref) < family + (family is 0x10 or 0x1C ? 4u : 1u));
        return (cheapest ? list.OrderBy(r => game.MaintenanceCost(r.Common.Ref)) : list.OrderByDescending(r => game.MaintenanceCost(r.Common.Ref))).ThenBy(r => r.Common.Ref).FirstOrDefault();
    }

    /// <summary>A production order of up to <paramref name="count"/> units the free maintenance allows (the production window's estimate); the units ordered.</summary>
    int Build(GameState game, int type, ManufacturingManager m, SdRecord product, int count, ref int free, HashSet<GameObject> used)
    {
        int cost = game.MaintenanceCost(product.Common.Ref);
        int n = Math.Min(Math.Min(count, 255), cost == 0 ? count : free / cost);
        if (n <= 0)
        {
            _buildFail = "gated: maintenance";
            return 0;
        }
        var c = new Command(type, Side) { Targets = [(m, 0)], Fields = [product.Common.Ref, (uint)n] };
        var estimate = Economy.ProductionEstimate(game, c);
        if (!estimate.Valid || estimate.Maintenance > free)
        {
            _buildFail = "refused: estimate";
            if (IdleReasons is not null && RefusedEstimates is not null)
            {
                var side = game.Sides[Side];
                var targetSystem = m.TargetKey as StarSystem ?? m.TargetKey?.System;
                RefusedEstimates.Add(new(game.Day, m, product.Common.Ref, n, Orders.Validate(game, c),
                    c.Targets.Select(t => t.Target).OfType<ManufacturingManager>().Any(x => Economy.EstimateDays(game, x, product.Common.Ref, n, false) is null),
                    estimate.Maintenance > free, free, side.MaintenanceCapacity - side.MaintenanceAllocated, targetSystem == m.System,
                    m.TargetKey?.Owner ?? Owner.Neutral, m.Orders.Count > 0));
            }
            return 0;
        }
        if (!Issue(game, c, used))
        {
            _buildFail = Room ? "refused: validate" : "budget";
            return 0;
        }
        free -= estimate.Maintenance;
        return n;
    }

    /// <summary>
    /// A shipyard's target that is not its own system and is Enroute, Destroyed or not in the yard's system (a
    /// fleet that left: capital-ship orders would be placed there and refused).
    /// </summary>
    static bool StaleTarget(ManufacturingManager m, StarSystem s) =>
        m.TargetKey is { } t && t != s && (t.Enroute || t.Destroyed || (t as StarSystem ?? t.System) != s);

    /// <summary>A carrier fleet's free fighter room minus the fighters already queued for it (own shipyards aimed at it).</summary>
    int FreeHangar(GameState game, Fleet carrier) =>
        FighterRoom(game, carrier) - Own(game).SelectMany(x => ScreenViews.Managers(x, Side)).Where(x => x.Kind == ManufacturingKind.Ship && x.TargetKey == carrier)
            .Sum(x => x.Orders.Count(o => Family.Of(o.Type) is >= 0x1C and < 0x20));

    /// <summary>The fighter capacity of a capital-ship type.</summary>
    static int CarrierRoom(GameState game, uint type) => (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == type)?.FighterCapacity ?? 0);

    /// <summary>
    /// An idle own fleet with free fighter room, not used this decision, not at an
    /// attack group's staging system or a threatened system, not carrying troops, and not at an own shipyard system with an empty
    /// ship queue, goes to the nearest own shipyard system that is not threatened, one with an empty ship queue first (where the
    /// shipyard orders its fighters).
    /// </summary>
    internal void Carriers(GameState game, HashSet<GameObject> used, List<StarSystem> threatened)
    {
        var yards = Own(game).Where(x => !threatened.Contains(x) && x.Contents.Any(f => f.Owner == Side && f.Usable && Family.Of(f.Type) == 0x28)).ToList();
        if (yards.Count == 0) return;
        var staging = StagingSystems.ToHashSet();
        foreach (var f in Fleets(game).Where(f => Idle(game, f, used) && FighterRoom(game, f) > 0 && !Carrying(f)).OrderBy(f => f.Key).ToList())
        {
            if (!Room) return;
            var at = (StarSystem)f.Container!;
            bool idleYardHere = ScreenViews.Managers(at, Side).Any(m => m.Kind == ManufacturingKind.Ship && m.Orders.Count == 0 && Economy.Members(m).Any());
            if (staging.Contains(at) || threatened.Contains(at) || yards.Contains(at) && (idleYardHere || !yards.Any(x => x != at
                && ScreenViews.Managers(x, Side).Any(m => m.Kind == ManufacturingKind.Ship && m.Orders.Count == 0)))) continue;
            // a yard whose ship manager has an empty queue first (a long capital ship would keep the fighters waiting)
            var to = yards.OrderBy(x => ScreenViews.Managers(x, Side).Any(m => m.Kind == ManufacturingKind.Ship && m.Orders.Count == 0) ? 0 : 1)
                .ThenBy(x => Orders.TravelDays(game, at, x, Orders.Speed(game, f))).ThenBy(x => x.Key).First();
            Issue(game, Move(Side, f, to), used);
        }
    }

    /// <summary>Free fighter places over a fleet's capital ships (own fleet: real contents).</summary>
    static int FighterRoom(GameState game, Fleet f) => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).Sum(ship =>
        (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == ship.Type)?.FighterCapacity ?? 0) - ship.Contents.Count(y => Family.Of(y.Type) is >= 0x1C and < 0x20));

    static int TroopRoom(GameState game, GameObject ship) =>
        (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == ship.Type)?.TroopCapacity ?? 0) - ship.Contents.Count(y => Family.IsTroop(y.Type));

    // ---- rule 4: attack (OpenSWRAi.Attack.cs) ----

    /// <summary>Own Usable troops aboard the group's Usable capital ships.</summary>
    int TroopsAboard(IEnumerable<Fleet> group) =>
        group.SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type) && x.Usable).Sum(x => x.Contents.Count(y => OwnTroop(y, Side) && y.Usable));

    bool Carrying(Fleet f) => f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Usable && x.Contents.Any(y => OwnTroop(y, Side) && y.Usable));

    /// <summary>
    /// The launch: the group (value >= 1.5 x <paramref name="defence"/>, a troop aboard) in one move. Refused as one order: every
    /// fleet's own move is validated first, the refused ones drop out, and the rest go only if they still meet both conditions and
    /// the day's budget covers all of them (no piecemeal launch, no lead fleet sent alone by accident).
    /// </summary>
    internal bool Launch(GameState game, List<Fleet> gathered, StarSystem target, int defence, Func<Fleet, int> value, HashSet<GameObject> used, int troops = 1, Known? k = null,
        Func<List<Fleet>, bool>? accept = null)
    {
        // with k: space win chance >= 0.7 against the known enemy fleets there and ground win chance >= 0.7 with the troops
        // aboard against the known troops and defence facilities, in place of value >= 1.5 x defence
        bool Predicted(List<Fleet> group)
        {
            if (k is null || !On(V7.Predict)) return group.Sum(value) >= 1.5 * defence;
            if (PredictSpace(game, k, target, group, k.Fleets(target, Enemy)).Win < 0.7) return false;
            var attackers = group.SelectMany(f => f.Contents).Where(x => Family.IsCapitalShip(x.Type) && x.Usable)
                .SelectMany(x => x.Contents.Where(y => OwnTroop(y, Side) && y.Usable)).Select(y => y.Type).ToList();
            var seen = k.Contents(target).Where(v => v.Owner == Enemy).ToList();
            return PredictGround(game, target, attackers, [.. seen.Where(v => v.In(0x10, 0x14)).Select(v => v.Object.Type)],
                [.. k.Contents(target).Where(v => v.In(0x22, 0x28)).Select(v => v.Object.Type)]) >= 0.7;
        }
        bool Ready(List<Fleet> group) => group.Count > 0 && group.Any(Carrying) && TroopsAboard(group) >= troops && (accept ?? Predicted)(group);
        if (!Ready(gathered) || !Room) return false;
        if (Issue(game, new Command(0x201, Side) { Targets = [.. gathered.Select(f => ((GameObject)f, 0))], ArgRef = target }, used)) return true;
        var valid = gathered.Where(f => Valid(game, Move(Side, f, target))).ToList();
        if (!Ready(valid) || Budget - Issued < valid.Count) return false;
        foreach (var f in valid) Issue(game, Move(Side, f, target), used);
        return true;
    }

    // ---- save ----

    internal JsonObject Save()
    {
        var refused = new JsonArray();
        foreach (var ((type, target, arg, field), day) in _refused) refused.Add(new JsonArray(type, target, arg, day, field));
        var j = new JsonObject { ["decided"] = Decided, ["refused"] = refused };
        if (_lastTaunt is { } last) j["lastTaunt"] = last;
        if (_passed.Count > 0) j["passed"] = new JsonArray([.. _passed.Select(e => (JsonNode)new JsonArray(e.Key, e.Value))]);
        if (_taunts.Any(n => n != 0)) j["taunts"] = new JsonArray([.. _taunts.Select(n => (JsonNode)n)]);
        if (_owned is not null) j["owned"] = new JsonArray([.. _owned.Select(r => (JsonNode)r)]);
        if (_enemyHeld.Count > 0) j["enemyHeld"] = new JsonArray([.. _enemyHeld.Select(r => (JsonNode)r)]);
        if (_launchedAt is { } launched) j["launchedAt"] = launched;
        if (_seen.Count > 0) j["seen"] = new JsonArray([.. _seen.Select(e => (JsonNode)new JsonArray(e.Key, e.Value))]);
        if (_foiled.Count > 0) j["foiled"] = new JsonArray([.. _foiled.Select(e => (JsonNode)new JsonArray(e.Key.Kind, e.Key.Target, e.Value))]);
        if (_foilMarks.Count > 0) j["foilMarks"] = new JsonArray([.. _foilMarks.Select(e => (JsonNode)new JsonArray(e.Key, e.Value.Day, e.Value.Count))]);
        if (_lowSince.Count > 0) j["lowSince"] = new JsonArray([.. _lowSince.Select(e => (JsonNode)new JsonArray(e.Key, e.Value))]);
        SaveAttack(j);
        SaveLandings(j);
        SaveWatch(j);
        SaveExperience(j);
        SaveIntel(j);
        SaveAgents(j);
        SaveIdle(j);
        SaveSwarm(j);
        SaveObjectives(j);
        SaveTroopFit(j);
        SaveBombard(j);
        SaveCascade(j);
        if (RecruitTotal is { } total) j["recruitTotal"] = total;
        if (Recruited > 0) j["recruited"] = Recruited;
        if (EconomyOnly) j["economyOnly"] = true;
        SaveGate(j);
        return j;
    }

    internal void Load(GameState game, int issued, JsonObject? j)
    {
        Issued = issued;
        if (j is null) return;
        Decided = (int)j["decided"]!;
        LoadAttack(game, j);
        LoadLandings(j);
        LoadWatch(j);
        LoadExperience(j);
        LoadIntel(j);
        LoadAgents(j);
        LoadIdle(j);
        LoadSwarm(j);
        LoadObjectives(j);
        LoadTroopFit(j);
        LoadBombard(j);
        LoadCascade(j);
        RecruitTotal = (int?)j["recruitTotal"];
        Recruited = (int?)j["recruited"] ?? 0;
        EconomyOnly = (bool?)j["economyOnly"] ?? false;
        LoadGate(j);
        foreach (var e in j["refused"]!.AsArray()) _refused[((int)e![0]!, (uint)e[1]!, (uint)e[2]!, e.AsArray().Count > 4 ? (uint)e[4]! : 0)] = (int)e[3]!;
        _lastTaunt = (int?)j["lastTaunt"];
        if (j["passed"] is JsonArray passed) foreach (var e in passed) _passed[(uint)e![0]!] = (int)e[1]!;
        if (j["taunts"] is JsonArray taunts) for (int i = 0; i < Math.Min(taunts.Count, _taunts.Length); i++) _taunts[i] = (int)taunts[i]!;
        if (j["owned"] is JsonArray owned) _owned = [.. owned.Select(r => (uint)r!)];
        if (j["enemyHeld"] is JsonArray held) _enemyHeld = [.. held.Select(r => (uint)r!)];
        _launchedAt = (uint?)j["launchedAt"];
        if (j["seen"] is JsonArray seen) foreach (var e in seen) _seen[(uint)e![0]!] = (int)e[1]!;
        if (j["foiled"] is JsonArray foiled) foreach (var e in foiled) _foiled[((uint)e![0]!, (uint)e[1]!)] = (int)e[2]!;
        if (j["foilMarks"] is JsonArray marks) foreach (var e in marks) _foilMarks[(uint)e![0]!] = ((int)e[1]!, (int)e[2]!);
        if (j["lowSince"] is JsonArray low) foreach (var e in low) _lowSince[(uint)e![0]!] = (int)e[1]!;
    }

    /// <summary>
    /// What the side knows of contents and strength, for one decision: the side's view contents of a container (real contents it
    /// sees plus frozen views parented to it, Existing and not Destroyed: <see cref="ScreenViews.Existing"/>'s reading, indexed once).
    /// </summary>
    internal sealed class Known
    {
        readonly GameState _game;
        readonly Owner _side;
        readonly ILookup<GameObject, GameObject> _frozen;
        readonly Dictionary<uint, int> _values = [];

        public Known(GameState game, Owner side)
        {
            (_game, _side) = (game, side);
            _frozen = game.Views.Where(e => e.Key.Item2 == side && e.Value.Parent is not null && !ScreenViews.Sees(e.Key.Item1, side))
                .ToLookup(e => e.Value.Parent!, e => e.Key.Item1);
        }

        public List<ViewObject> Contents(GameObject container) =>
            [.. container.Contents.Where(o => ScreenViews.Sees(o, _side)).Concat(_frozen[container]).Distinct()
                .Select(o => ScreenViews.View(_game, o, _side)).Where(v => v.Existing && !v.Destroyed).OrderBy(v => v.Object.Key)];

        /// <summary>Fleets of <paramref name="owner"/> in the side's view of a system (Enroute ones included).</summary>
        public List<GameObject> Fleets(StarSystem s, Owner owner) => [.. Contents(s).Where(v => v.Object is Fleet && v.Owner == owner).Select(v => v.Object)];

        /// <summary>Known combat value of <paramref name="owner"/>'s units in a container and in its fleets and ships, from the definitions.</summary>
        public int Strength(GameObject container, Owner owner)
        {
            int sum = 0;
            foreach (var v in Contents(container).Where(v => v.Owner == owner))
            {
                sum += Value(v.Object.Type);
                if (v.Object is Fleet || Family.IsCapitalShip(v.Object.Type)) sum += Strength(v.Object, owner);
            }
            return sum;
        }

        /// <summary>
        /// Unit value: capital ships total attack + shield + hull, fighters total
        /// attack + shield, troops attack + defence, defence facilities attack + shield; others 0.
        /// </summary>
        public int Value(uint type)
        {
            if (_values.TryGetValue(type, out int v)) return v;
            var d = _game.Definitions;
            v = Family.Of(type) switch
            {
                >= 0x14 and < 0x1C => d.CapitalShips.FirstOrDefault(r => r.Common.Ref == type) is { } c ? (int)(c.TotalAttackStrength + c.ShieldStrength + c.Hull) : 0,
                >= 0x1C and < 0x20 => d.Fighters.FirstOrDefault(r => r.Common.Ref == type) is { } f ? (int)(f.TotalAttackStrength + f.ShieldStrength) : 0,
                >= 0x10 and < 0x14 => d.Troops.FirstOrDefault(r => r.Common.Ref == type) is { } t ? (int)(t.AttackStrength + t.DefenseStrength) : 0,
                >= 0x22 and < 0x28 => d.DefenseFacilities.FirstOrDefault(r => r.Common.Ref == type) is { } x ? (int)(x.AttackStrength + x.ShieldStrength) : 0,
                _ => 0,
            };
            return _values[type] = v;
        }
    }
}
