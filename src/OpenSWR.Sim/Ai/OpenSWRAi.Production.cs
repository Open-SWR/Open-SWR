using System.Collections.Immutable;
using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class OpenSWRController
{

    internal void Production(GameState game, Known k, HashSet<GameObject> used, int deficit, SectorMap map)
    {
        var state = game.Sides[Side];
        IdleReasons?.Clear();
        var reconTypes = ReconTypes(game, Side);
        int scoutsMissing = ScoutUnitsMissing(game, map);
        // no training facility, neither Usable nor on its way (under construction, or Completed and still travelling from
        // the yard to its site: Enroute, not yet Usable) (own facilities: real state)
        bool trainingWanted = !game.Facilities.Any(f => f.Owner == Side && f.Existing && Family.Of(f.Type) == Families.TrainingFacility);
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
        if (RimFirsting) defenceSites.AddRange(RimDefenceSites(game)); // rim-first: a shield and a gun at the rim drops before facilities
        var fighterYards = On(V7.DoctrineFighterYard) ? FighterYards(game) : null;
        // fighters ordered into each carrier this decision: the orders run only after the decision, so two yards would otherwise
        // both fill the same free room and the later order is refused at execution (category 20, code 1)
        var hangarOrdered = new Dictionary<Fleet, int>();
        int HangarLeft(Fleet f) => FighterRoom(game, f) - hangarOrdered.GetValueOrDefault(f);
        int BuildFighters(ManufacturingManager m, SdRecord fighter, Fleet carrier, int count, ref int free)
        {
            int n = Build(game, CommandType.ProduceShip, m, fighter, count - hangarOrdered.GetValueOrDefault(carrier), ref free, used);
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
                        if (facility is not null && Family.Of(facility.Common.Ref) == Families.ConstructionYard) newYards++;
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
                        uint family = Capacity(Families.Mine) <= Capacity(Families.Refinery) ? Families.Mine : Families.Refinery;
                        facility = Pick(game, Families.ConstructionYard, family);
                        if (facility is null) Why(m, "none buildable");
                        // the own system with the most free room for it (a Mine needs a raw-material and an energy slot, a
                        // Refinery an energy slot); the yard's current site first when it has room (no retarget), then nearest the yard
                        int RoomFor(StarSystem x) => family == Families.Mine ? Math.Min(x.RawMaterials - x.UsedRaw, x.Energy - x.UsedEnergy) : x.Energy - x.UsedEnergy;
                        site = !On(V7.LateEconomy)
                            ? own.Where(x => x.Energy > x.UsedEnergy && (family != Families.Mine || x.RawMaterials > x.UsedRaw)).OrderBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault()
                            : own.Where(x => RoomFor(x) > 0).OrderBy(x => x == m.TargetKey ? 0 : 1).ThenByDescending(RoomFor)
                                .ThenBy(x => Orders.Distance(x, s)).ThenBy(x => x.Key).FirstOrDefault();
                    }
                    if (facility is not null && site is null) Why(m, "no site");
                    if (facility is null || site is null) break;
                    if (m.TargetKey != site)
                    {
                        Why(m, Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = site }, used) ? "retarget" : Room ? "refused" : "budget");
                        break; // the order follows at the next decision, once the TargetKey has moved (validation places at the current one)
                    }
                    Why(m, Build(game, CommandType.ProduceFacility, m, facility, 1, ref free, used) > 0 ? "ordered" : _buildFail);
                    break;
                }
                case ManufacturingKind.Training:
                {
                    var scout = scoutsMissing > 0 ? Research.Buildable(game, Side, Families.TrainingFacility).Where(r => reconTypes.Contains(r.Common.Ref))
                        .OrderBy(r => game.MaintenanceCost(r.Common.Ref)).ThenBy(r => r.Common.Ref).FirstOrDefault() : null;
                    // the garrisons at risk first; then scouting units when their shortfall is the larger
                    if (deficit == 0 && scout is not null && plan.SpecialForces >= plan.Troops)
                    {
                        int built = Build(game, CommandType.ProduceTroop, m, scout, scoutsMissing, ref free, used);
                        Why(m, built > 0 ? "ordered" : _buildFail);
                        scoutsMissing -= built;
                        if (built > 0) break;
                    }
                    // Agents: the broad pool, espionage types first, when the special forces' shortfall is the larger
                    if (On(V7.Agents) && deficit == 0 && plan.SpecialForces >= plan.Troops && TrainAgents(game, m, ref free, used, agentsOrdered)) break;
                    // AssaultTroops rule 1: the best assault troop up to the attack groups' need, also while the total need is met
                    if (AssaultTroops && AssaultShortfall(game, deficit) is > 0 and var shortfall && BestFor(game, UnitKind.Troop, UnitRole.Assault, free) is { } assault)
                    {
                        int built = Build(game, CommandType.ProduceTroop, m, assault, shortfall, ref free, used);
                        Why(m, built > 0 ? "ordered" : _buildFail);
                        troops += built;
                        if (built > 0) break;
                    }
                    int want = deficit > 0 ? deficit : Math.Max(0, Need.Total - troops);
                    Why(m, want > 0 ? "none buildable" : "need met");
                    if (want > 0 && (On(V7.DoctrineTroops) ? DoctrineTroop(game, deficit, free) : Pick(game, Families.TrainingFacility, Families.Troop, cheapest: true)) is { } troop)
                    {
                        if (deficit > 0 && own.FirstOrDefault(x => AtRisk(x) && x.Contents.Count(y => OwnTroop(y, Side)) < Keep(x)) is { } risk && m.TargetKey != risk)
                        {
                            Why(m, Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = risk }, used) ? "retarget" : Room ? "refused" : "budget");
                            break; // troops at the next decision, at the at-risk system
                        }
                        int n = Build(game, CommandType.ProduceTroop, m, troop, want, ref free, used);
                        Why(m, n > 0 ? "ordered" : _buildFail);
                        if (deficit > 0) deficit -= n;
                        troops += n;
                        if (n > 0) break;
                    }
                    if (deficit == 0 && scout is not null)
                    {
                        int built = Build(game, CommandType.ProduceTroop, m, scout, scoutsMissing, ref free, used);
                        if (built > 0) Why(m, "ordered");
                        scoutsMissing -= built;
                    }
                    if (On(V7.Agents) && deficit == 0 && m.Orders.Count == 0 && !used.Contains(m)) TrainAgents(game, m, ref free, used, agentsOrdered);
                    // Teams and Intel: covert and intel special forces up to the demand, when their shortfall is the larger
                    if ((On(V7.Teams) || On(V7.Intel)) && deficit == 0 && m.Orders.Count == 0 && !used.Contains(m) && plan.SpecialForces >= plan.Troops
                        && ForceNeed - (SpecialForces(game).Count - ScoutUnits(game).Count) is > 0 and var wantedForces && CovertForce(game) is { } force)
                    {
                        int built = Build(game, CommandType.ProduceTroop, m, force, wantedForces, ref free, used);
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
                        if ((DoctrineFighter(game, k, free, jumping: !here) ?? (here ? Pick(game, Families.Shipyard, Families.Fighter, cheapest: true) : null)) is not { } yardFighter) break;
                        if (m.TargetKey != group) Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = group }, used);
                        else BuildFighters(m, yardFighter, group, FighterRoom(game, group), ref free);
                        break;
                    }
                    // with Fighters: the free room net of the fighters already queued for the carrier
                    var carrier = s.Contents.OfType<Fleet>().Where(f => f.Owner == Side && f.Existing && f.Completed && !f.Enroute && HangarLeft(f) > 0
                        && (!On(V7.Fighters) || FreeHangar(game, f) - hangarOrdered.GetValueOrDefault(f) > 0))
                        .OrderBy(f => f.Key).FirstOrDefault();
                    var fighter = On(V7.DoctrineProduction) ? DoctrineFighter(game, k, free) ?? Pick(game, Families.Shipyard, Families.Fighter, cheapest: true) : Pick(game, Families.Shipyard, Families.Fighter, cheapest: true);
                    // a carrier with room at this yard gets its fighters first when the fighter shortfall is not the smaller
                    bool fightersFirst = On(V7.Fighters) ? plan.Fighters > 0 && plan.Fighters >= plan.CapitalShips && carrier is not null && fighter is not null
                        : plan.Fighters > plan.CapitalShips && carrier is not null && fighter is not null;
                    if (On(V7.DoctrineProduction) && carrier is not null && fighter is not null) fightersFirst = true; // fill all hangar room
                    // no carrier here: fighters that can jump for the nearest own carrier with room elsewhere (the yard's
                    // current target first), when the fighter shortfall is not the smaller one
                    if (On(V7.DoctrineProduction) && carrier is null && plan.Fighters > 0 && plan.Fighters >= plan.CapitalShips
                        && DoctrineFighter(game, k, free, jumping: true) is { } jumper && RemoteCarrier(game, m, s, HangarLeft) is { } remote)
                    {
                        if (m.TargetKey != remote) Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = remote }, used);
                        else BuildFighters(m, jumper, remote, FighterRoom(game, remote), ref free);
                        break;
                    }
                    var buildable = Research.Buildable(game, Side, Families.Shipyard).Where(r => Family.IsCapitalShip(r.Common.Ref) && !DeathStar.IsDeathStar(r.Common.Ref)).ToList();
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
                            Why(m, Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = s }, used) ? "retarget" : Room ? "refused" : "budget");
                            break;
                        }
                        bool ordered = Build(game, CommandType.ProduceShip, m, ships[0], 1, ref free, used) > 0;
                        Why(m, ordered ? "ordered" : _buildFail);
                        if (ordered) break;
                    }
                    if (fighter is null || carrier is null)
                    {
                        // no fighter order here: a target still on a fleet goes back to the yard's own system
                        if (On(V7.Fighters) && StaleTarget(m, s))
                            Why(m, Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = s }, used) ? "retarget" : Room ? "refused" : "budget");
                        break;
                    }
                    if (m.TargetKey != carrier)
                    {
                        Why(m, Issue(game, new Command(CommandType.SetProductionTarget, Side) { Targets = [(m, GroupMode.Object)], ArgRef = carrier }, used) ? "retarget" : Room ? "refused" : "budget");
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
        int yards = game.Facilities.Count(f => f.Owner == Side && f.Existing && Family.Of(f.Type) == Families.ConstructionYard) + ordered;
        if (yards < YardTarget(own.Count) && Pick(game, Families.ConstructionYard, Families.ConstructionYard) is { } yard
            && game.MaintenanceCost(yard.Common.Ref) <= free && Safe() is { } at)
            return (yard, at);
        int Count(uint family) => game.Facilities.Count(f => f.Owner == Side && f.Existing && Family.Of(f.Type) == family);
        uint family = Count(Families.Mine) <= Count(Families.Refinery) ? Families.Mine : Families.Refinery;
        int RoomFor(StarSystem x) => family == Families.Mine ? Math.Min(x.RawMaterials - x.UsedRaw, x.Energy - x.UsedEnergy) : x.Energy - x.UsedEnergy;
        if (Pick(game, Families.ConstructionYard, family) is { } economy
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
        game.Sides[Side].ManufacturingWaitList.Count == 0 && Pick(game, Families.ConstructionYard, Families.TrainingFacility) is { } t && game.MaintenanceCost(t.Common.Ref) <= free ? t : null;

    /// <summary>The buildable product of the manager family in [family, family + 4) (the strongest, or the cheapest in maintenance).</summary>
    SdRecord? Pick(GameState game, uint manager, uint family, bool cheapest = false)
    {
        var list = Research.Buildable(game, Side, manager).Where(r => Family.Of(r.Common.Ref) >= family && Family.Of(r.Common.Ref) < family + (family is Families.Troop or Families.Fighter ? 4u : 1u));
        return (cheapest ? list.OrderBy(r => game.MaintenanceCost(r.Common.Ref)) : list.OrderByDescending(r => game.MaintenanceCost(r.Common.Ref))).ThenBy(r => r.Common.Ref).FirstOrDefault();
    }

    /// <summary>A production order of up to <paramref name="count"/> units the free maintenance allows (the production window's estimate); the units ordered.</summary>
    int Build(GameState game, CommandType type, ManufacturingManager m, SdRecord product, int count, ref int free, HashSet<GameObject> used)
    {
        int cost = game.MaintenanceCost(product.Common.Ref);
        int n = Math.Min(Math.Min(count, 255), cost == 0 ? count : free / cost);
        if (n <= 0)
        {
            _buildFail = "gated: maintenance";
            return 0;
        }
        var c = new Command(type, Side) { Targets = [(m, GroupMode.Object)], Fields = [product.Common.Ref, (uint)n] };
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
            .Sum(x => x.Orders.Count(o => Family.Of(o.Type) is >= Families.Fighter and < Families.Headquarters));

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
        var yards = Own(game).Where(x => !threatened.Contains(x) && x.Contents.Any(f => f.Owner == Side && f.Usable && Family.Of(f.Type) == Families.Shipyard)).ToList();
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
        (int)(game.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == ship.Type)?.FighterCapacity ?? 0) - ship.Contents.Count(y => Family.Of(y.Type) is >= Families.Fighter and < Families.Headquarters));

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
            return PredictGround(game, target, attackers, [.. seen.Where(v => v.In(Families.Troop, Families.CapitalShip)).Select(v => v.Object.Type)],
                [.. k.Contents(target).Where(v => v.In(Families.IonCannon, Families.Shipyard)).Select(v => v.Object.Type)]) >= 0.7;
        }
        bool Ready(List<Fleet> group) => group.Count > 0 && group.Any(Carrying) && TroopsAboard(group) >= troops && (accept ?? Predicted)(group);
        if (!Ready(gathered) || !Room) return false;
        if (Issue(game, new Command(CommandType.Move, Side) { Targets = [.. gathered.Select(f => ((GameObject)f, GroupMode.Object))], ArgRef = target }, used)) return true;
        var valid = gathered.Where(f => Valid(game, Move(Side, f, target))).ToList();
        if (!Ready(valid) || Budget - Issued < valid.Count) return false;
        foreach (var f in valid) Issue(game, Move(Side, f, target), used);
        return true;
    }

    // ---- save ----
}
