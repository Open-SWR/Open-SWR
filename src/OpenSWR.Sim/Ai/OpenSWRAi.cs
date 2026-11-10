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
    /// <summary>Support share below which an own system counts as at risk: the map filter's support glyph threshold (the PopularSupport filter, level 2 starts at 60).</summary>
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
    readonly SortedDictionary<(CommandType Type, uint Target, uint Arg, uint Field), int> _refused = [];
    public int Refusals => _refused.Count;

    /// <summary>Optional log of every issued command (result Ok) and every refused validation (tests).</summary>
    public List<(Command Command, OrderResult Result)>? IssueTrace { get; set; }

    Owner Enemy => Side == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    /// <summary>The doctrine parts.</summary>
    public static readonly ImmutableHashSet<V7> Doctrine =
        [V7.DoctrineProduction, V7.DoctrineGather, V7.DoctrineTroops, V7.DoctrineDefence, V7.DoctrineFighterYard];

    /// <summary>The steps an "all" run plays with: every step from Experience through Siege (the later ones stay off, so "all" runs are unchanged).</summary>
    public static readonly ImmutableHashSet<V7> AllFeatures = [.. Enum.GetValues<V7>().Where(f => f <= V7.Siege)];

    /// <summary>
    /// The steps the OpenSWR AI plays with by default: each one beat the set without it in computer-against-computer
    /// benchmarks before it was added.
    /// </summary>
    public static readonly ImmutableHashSet<V7> DefaultFeatures = [V7.Fighters, V7.Watch, V7.LateEconomy, V7.DoctrineDefence, V7.Recruit, V7.CovertExperience,
        V7.Agents, V7.Decoys, V7.Swarm, V7.DoctrineProduction, V7.Wings, V7.RimFirst];

    /// <summary>The optional steps in use (default <see cref="DefaultFeatures"/>); a step off takes the older path. Not saved (a run setting).</summary>
    public ImmutableHashSet<V7> Features { get; set; } = DefaultFeatures;

    (GameState, StarSystem)? _battle;
    int? _lastTaunt;
    readonly int[] _taunts = new int[OpenSWRTaunts.Keys.Length];
    SortedSet<uint>? _owned; // own systems at the last decision; null before the first
    SortedSet<uint> _enemyHeld = []; // systems this side saw as the other side's at the last decision
    uint? _launchedAt; // the target of the last launch, until an own fleet is there (its chat line waits for the arrival)

    /// <summary>The own troop move orders (null = off; aimatch only, no effect on play).</summary>
    public List<TroopMove>? TroopMoveLog { get; set; }

    /// <summary>The step whose orders are being issued (measurement only).</summary>
    string _step = "other";

    // ---- own objects (own data: real values) ----

    /// <summary>The troop need of the current decision (<see cref="TroopNeed"/>); not saved.</summary>
    internal TroopNeeds Need { get; private set; } = TroopNeeds.None;

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

    /// <summary>Every refused estimate (measurement; null = off).</summary>
    public List<RefusedEstimate>? RefusedEstimates { get; set; }

    /// <summary>Why the last <see cref="Build"/> placed nothing (measurement).</summary>
    string _buildFail = "";

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
                >= Families.CapitalShip and < Families.Fighter => d.CapitalShips.FirstOrDefault(r => r.Common.Ref == type) is { } c ? (int)(c.TotalAttackStrength + c.ShieldStrength + c.Hull) : 0,
                >= Families.Fighter and < Families.Headquarters => d.Fighters.FirstOrDefault(r => r.Common.Ref == type) is { } f ? (int)(f.TotalAttackStrength + f.ShieldStrength) : 0,
                >= Families.Troop and < Families.CapitalShip => d.Troops.FirstOrDefault(r => r.Common.Ref == type) is { } t ? (int)(t.AttackStrength + t.DefenseStrength) : 0,
                >= Families.IonCannon and < Families.Shipyard => d.DefenseFacilities.FirstOrDefault(r => r.Common.Ref == type) is { } x ? (int)(x.AttackStrength + x.ShieldStrength) : 0,
                _ => 0,
            };
            return _values[type] = v;
        }
    }
}
