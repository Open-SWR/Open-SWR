using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Production, facilities, economy, garrisons, carriers and the per-day budget.</summary>
public class OpenSWRAiProductionTests(ITestOutputHelper output) : OpenSWRAiTests(output)
{
    /// <summary>
    /// A construction yard whose destination system has no energy left moves its destination first (the target command) and orders the Mine /
    /// Refinery at the next decision, so the order is never validated against the old destination (no refusal for the old destination).
    /// </summary>
    [GameFact]
    public void ConstructionRetargetsThenBuilds()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        game.Clock.Step(game); // the day's own decision has run
        var yard = game.Systems.Where(x => x.Owner == Owner.Empire).SelectMany(x => ScreenViews.Managers(x, Owner.Empire))
            .First(m => m.Kind == ManufacturingKind.Construction && m.Orders.Count == 0 && Economy.Members(m).Count > 0);
        var full = (StarSystem)yard.TargetKey;
        int facilities = full.Contents.Count(x => Family.IsFacility(x.Type) && x.Existing);
        (full.Energy, full.UsedEnergy) = (facilities, facilities); // no room at the current destination
        Assert.Contains(game.Systems, x => x.Owner == Owner.Empire && x != full && x.Energy > x.UsedEnergy);
        void Decide()
        {
            var side = game.Sides[Owner.Empire];
            side.MaintenanceAllocated = side.MaintenanceCapacity; // free 0: below the cheapest unit, so a Mine or Refinery is wanted
            ai.DayDue(); // a fresh budget
            ai.Production(game, new OpenSWRController.Known(game, Owner.Empire), [], 0, ai.Sectors(game));
        }
        int from = ai.IssueTrace!.Count;
        Decide();
        Assert.Contains(ai.IssueTrace!.Skip(from), t => t.Command.Type == CommandType.SetProductionTarget && t.Command.Targets[0].Target == yard && t.Result.Success);
        game.Clock.Step(game); // the destination command runs
        Assert.NotEqual(full, yard.TargetKey);
        Decide();
        var trace = ai.IssueTrace!.Skip(from).Where(t => t.Command.Type == CommandType.ProduceFacility && t.Command.Targets[0].Target == yard).ToList();
        Assert.Contains(trace, t => t.Result.Success && Family.Of(t.Command.Fields[0]) is Families.Mine or Families.Refinery);
        Assert.DoesNotContain(trace, t => !t.Result.Success);
    }

    /// <summary>
    /// The training facility only when its maintenance fits: the seed 532947196 Alliance (none) with free maintenance below it
    /// orders a Mine or Refinery (or moves a yard for one) instead of nothing.
    /// </summary>
    [GameFact]
    public void TrainingFacilityWaitsForMaintenance()
    {
        var (game, ai) = AllianceGame();
        var side = game.Sides[Owner.Alliance];
        var training = Research.Buildable(game, Owner.Alliance, Families.ConstructionYard).First(r => Family.Of(r.Common.Ref) == Families.TrainingFacility);
        int cost = game.MaintenanceCost(training.Common.Ref);
        output.WriteLine($"training facility {Family.Of(training.Common.Ref)}:{SdRef.IndexOf(training.Common.Ref)} maintenance {cost}");
        Assert.True(cost > 0, "the training facility costs no maintenance: the condition cannot bind");
        // the choice itself: not below its maintenance, not while manufacturing waits for refined material
        Assert.Null(ai.TrainingFacility(game, cost - 1));
        Assert.Equal(training.Common.Ref, ai.TrainingFacility(game, cost)?.Common.Ref);
        var waiting = game.Facilities.First(f => f.Owner == Owner.Alliance);
        side.ManufacturingWaitList.Add(new(waiting, 0));
        Assert.Null(ai.TrainingFacility(game, cost));
        side.ManufacturingWaitList.Clear();
        // free below the facility's maintenance, and below the cheapest unit (so that a Mine or Refinery is wanted at all)
        int cheapest = Research.Buildable(game, Owner.Alliance, Families.Shipyard).Where(r => Family.IsCapitalShip(r.Common.Ref))
            .Concat(Research.Buildable(game, Owner.Alliance, Families.TrainingFacility).Where(r => Family.IsTroop(r.Common.Ref)))
            .Select(r => game.MaintenanceCost(r.Common.Ref)).Where(c => c > 0).Min();
        side.MaintenanceAllocated = side.MaintenanceCapacity - (Math.Min(cost, cheapest) - 1);
        ai.DayDue();
        ai.Production(game, new OpenSWRController.Known(game, Owner.Alliance), [], 0, ai.Sectors(game));
        var issued = ai.IssueTrace!.Where(t => t.Result.Success).Select(t => t.Command).ToList();
        foreach (var c in issued) output.WriteLine($"{(int)c.Type} [{string.Join(",", c.Fields.Select(f => f.ToString()))}] -> {c.ArgRef?.Ref}");
        Assert.DoesNotContain(issued, c => c.Type == CommandType.ProduceFacility && Family.Of(c.Fields[0]) == Families.TrainingFacility);
        Assert.Contains(issued, c => c.Type == CommandType.ProduceFacility && Family.Of(c.Fields[0]) is Families.Mine or Families.Refinery);
    }

    /// <summary>
    /// Version 6 production plan: the phase is expansion before day 150 and the weighted shortfalls lie within 0-1.5; production
    /// facilities first: the chosen kind has no idle manager and every kind before it in the order (shipyard, training facility,
    /// construction yard) has one; over 400 days (version 7: was 300) the side adds shipyards and keeps no more than 55 % of its maintenance unused on
    /// average after day 200.
    /// </summary>
    [GameFact]
    public void ProductionPlanAndFacilitiesFirst()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        game.Clock.Step(game);
        var plan = ai.CurrentPlan;
        Assert.Equal("expansion", plan.Phase);
        int yardsAtStart = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Usable && Family.Of(f.Type) == Families.Shipyard);
        var unused = new List<double>();
        while (game.Day < 400 && !game.GameOver)
        {
            int decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            plan = ai.CurrentPlan;
            Assert.All(new[] { plan.Troops, plan.CapitalShips, plan.Fighters, plan.SpecialForces, plan.Defence }, x => Assert.InRange(x, 0, 1.5));
            if (game.Day < 150) Assert.Equal("expansion", plan.Phase);
            var side = game.Sides[Owner.Empire];
            if (game.Day > 200 && side.MaintenanceCapacity > 0) unused.Add(1 - (double)side.MaintenanceAllocated / side.MaintenanceCapacity);
            // the facility order
            var managers = game.Systems.Where(s => s.Owner == Owner.Empire).SelectMany(s => ScreenViews.Managers(s, Owner.Empire))
                .Where(m => Economy.Members(m).Any(f => !f.Suspended)).ToList();
            bool Idle(ManufacturingKind kind) => managers.Any(m => m.Kind == kind && m.Orders.Count == 0);
            if (ai.ProductionFacility(game, 100000) is { } facility)
            {
                var kind = Family.Of(facility.Common.Ref) switch { Families.Shipyard => ManufacturingKind.Ship, Families.TrainingFacility => ManufacturingKind.Training, _ => ManufacturingKind.Construction };
                Assert.False(Idle(kind));
                foreach (var before in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction }.TakeWhile(x => x != kind))
                    Assert.True(Idle(before));
            }
        }
        int yards = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Usable && Family.Of(f.Type) == Families.Shipyard);
        output.WriteLine($"shipyards {yardsAtStart} → {yards}; unused maintenance after day 200 {unused.Average():P0}; phase {ai.CurrentPlan.Phase}");
        Assert.True(yards > yardsAtStart);
        Assert.True(unused.Average() < 0.55); // version 7: 0.5 gives 0.55 (ships by value per maintenance use less of it; the benchmark tracks it)
    }

    /// <summary>
    /// Version 6 defence by value: every defence target is a high-value own system (the top third by value) with threat, its target
    /// is at least 2; the covered ones are targets and fit the troops left after the at-risk garrisons; the defence sites are at most
    /// 3 own systems, each wanting a shield or a battery it lacks; a counter-attack takes the higher-valued loss first; a lost entry
    /// keeps its value through save gives load gives save byte-equal.
    /// </summary>
    [GameFact]
    public void DefenceByValue()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Swarm); // the version 5 attack groups this game was written for
        int targets = 0, covered = 0, sites = 0;
        while (game.Day < 300 && !game.GameOver)
        {
            int decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            var k = new OpenSWRController.Known(game, Owner.Empire);
            var own = game.Systems.Where(s => s.Owner == Owner.Empire).ToList();
            int high = own.Count(ai.HighValue);
            Assert.InRange(high, own.Count / 3, (own.Count + 2) / 3);
            var (t, c) = ai.Defence(game, k);
            foreach (var (s, n) in t)
            {
                Assert.True(ai.HighValue(s) && s.Owner == Owner.Empire, $"day {game.Day}: target {Family.Of(s.Ref)}:{SdRef.IndexOf(s.Ref)} not high-value own");
                Assert.True(n >= 2);
            }
            Assert.All(c, e => Assert.Equal(t[e.Key], e.Value));
            Assert.True(c.Values.Sum() <= ai.OwnTroops(game));
            foreach (var (site, product) in ai.DefenceSites(game, k))
            {
                Assert.Equal(Owner.Empire, site!.Owner);
                uint family = Family.Of(product!.Common.Ref);
                Assert.True(family is Families.Gun or Families.PlanetaryShield);
                Assert.DoesNotContain(site.Contents, x => x.Owner == Owner.Empire && x.Existing && Family.Of(x.Type) == family);
                sites++;
            }
            Assert.True(ai.DefenceSites(game, k).Count <= 3);
            targets += t.Count;
            covered += c.Count;
        }
        output.WriteLine($"defence targets {targets}, covered {covered}, defence sites {sites}");
        Assert.True(targets > 0);
        // counter by value: two lost systems in reach, the higher value one (put last by the usual order) becomes the target
        var known = new OpenSWRController.Known(game, Owner.Empire);
        var mine = game.Systems.Where(s => s.Owner == Owner.Empire).ToList();
        var inReach = game.Systems.Where(s => Knowledge.See(game, s, Owner.Empire) is { Owner: Owner.Alliance, Destroyed: false }
            && mine.Any(b => Orders.TravelDays(game, b, s, 100) <= OpenSWRController.ReachDays) && ai.Groups.All(g => g.Target != s)).OrderBy(s => s.Key).ToList();
        Assert.True(inReach.Count >= 2);
        var fresh = new OpenSWRController(Owner.Empire) { Features = OpenSWRController.DefaultFeatures.Remove(OpenSWRController.V7.Swarm) }; // the version 5 groups
        fresh.MarkLost(inReach[0], game.Day, 5);
        fresh.MarkLost(inReach[^1], game.Day, 50);
        fresh.DayDue();
        fresh.Attack(game, known, [], [], fresh.Sectors(game));
        output.WriteLine($"targets {string.Join(",", fresh.Groups.Select(g => g.Target.Ref.ToString()))}");
        Assert.Equal(inReach[^1], fresh.Groups[0].Target);
        ai.MarkLost(inReach[^1], game.Day, 50);
        var first = Save(game);
        Assert.Contains($"[{inReach[^1].Ref},{game.Day},50]", System.Text.Encoding.UTF8.GetString(first));
        Assert.Equal(Text(first), Text(Save(Load(first))));
    }

    /// <summary>
    /// Version 7 garrisons from experience: the expected landing is 4 before any landing, then the mean of the last 10 rounded up;
    /// in a game every high-value border system is a defence target of at least the expected landing + 1; landings are recorded
    /// from lost assaults and survive save gives load gives save byte-equal.
    /// </summary>
    [GameFact]
    public void GarrisonsFromExperience()
    {
        var fresh = new OpenSWRController(Owner.Empire) { Features = [OpenSWRController.V7.Experience] }; // off by default
        Assert.Equal(OpenSWRController.FirstLanding, fresh.ExpectedLanding);
        fresh.RecordLanding(3);
        fresh.RecordLanding(6);
        Assert.Equal(5, fresh.ExpectedLanding); // 4.5 rounded up
        for (int i = 0; i < OpenSWRController.LandingsKept; i++) fresh.RecordLanding(2);
        Assert.Equal(2, fresh.ExpectedLanding); // the first two are out
        Assert.Equal(OpenSWRController.LandingsKept, fresh.Landings.Count);
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Add(OpenSWRController.V7.Experience);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Swarm); // the version 5 attack groups this game was written for (lost assaults to learn from)
        int borderTargets = 0, lost = 0;
        var owned = new HashSet<StarSystem>();
        while (game.Day < 800 && !game.GameOver && (game.Day < 300 || ai.Landings.Count == 0))
        {
            int decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            lost += owned.Count(s => s.Owner == Owner.Alliance);
            owned = [.. game.Systems.Where(s => s.Owner == Owner.Empire)];
            var k = new OpenSWRController.Known(game, Owner.Empire);
            var enemies = game.Systems.Where(s => Knowledge.See(game, s, Owner.Empire) is { Owner: Owner.Alliance, Destroyed: false }).ToList();
            var (t, _) = ai.Defence(game, k);
            foreach (var s in game.Systems.Where(s => s.Owner == Owner.Empire && s.Populated && ai.HighValue(s)
                         && enemies.Any(e => Orders.TravelDays(game, s, e, 100) <= OpenSWRController.ReachDays)))
            {
                Assert.True(t.TryGetValue(s, out int n) && n >= ai.ExpectedLanding + 1, $"day {game.Day}: border {Family.Of(s.Ref)}:{SdRef.IndexOf(s.Ref)} target {n}");
                borderTargets++;
            }
        }
        output.WriteLine($"day {game.Day}: systems lost to the Alliance {lost}; border targets {borderTargets}; landings {string.Join(",", ai.Landings)}, expected {ai.ExpectedLanding}");
        Assert.True(borderTargets > 0);
        Assert.NotEmpty(ai.Landings);
        var first = Save(game);
        Assert.Contains("\"landings\":", System.Text.Encoding.UTF8.GetString(first));
        Assert.Equal(Text(first), Text(Save(Load(first))));
    }

    /// <summary>
    /// Version 7 late economy: while economy-only (set below 10 % free maintenance, cleared above 25 %) the construction yards order
    /// only Mines and Refineries; every Mine / Refinery order goes to a site with a free slot for it; the flag survives save gives load gives
    /// save byte-equal. Logs the destination retargets per construction build (the suspected churn).
    /// </summary>
    [GameFact]
    public void LateEconomy()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = OpenSWRController.AllFeatures.Remove(OpenSWRController.V7.EconomyFirst); // the game whose free maintenance drops below 10 % (it does not with the default steps)
        var retargets = new Dictionary<GameObject, List<uint>>();
        int builds = 0, economy = 0, economyOnlyDays = 0, yardDays = 0, idleDays = 0;
        while (game.Day < 800 && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count, decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            if (ai.EconomyOnly) economyOnlyDays++;
            foreach (var m in game.Systems.Where(x => x.Owner == Owner.Empire).SelectMany(x => ScreenViews.Managers(x, Owner.Empire)).Where(m => m.Kind == ManufacturingKind.Construction && Economy.Members(m).Any(f => !f.Suspended)))
                (yardDays, idleDays) = (yardDays + 1, idleDays + (m.Orders.Count == 0 ? 1 : 0));
            var side = game.Sides[Owner.Empire];
            foreach (var t in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Targets[0].Target is ManufacturingManager { Kind: ManufacturingKind.Construction }))
            {
                var m = (ManufacturingManager)t.Command.Targets[0].Target;
                if (t.Command.Type == CommandType.SetProductionTarget)
                {
                    if (!retargets.TryGetValue(m, out var l)) retargets[m] = l = [];
                    l.Add(t.Command.ArgRef!.Ref);
                }
                if (t.Command.Type != CommandType.ProduceFacility) continue;
                builds++;
                uint family = Family.Of(t.Command.Fields[0]);
                if (ai.EconomyOnly) Assert.True(family is Families.Mine or Families.Refinery, $"day {game.Day}: {t.Command.Fields[0]} while economy-only");
                if (family is not (Families.Mine or Families.Refinery)) continue;
                economy++;
                var site = (StarSystem)m.TargetKey!;
                Assert.True(site.Energy > site.UsedEnergy - 1 && (family == Families.Refinery || site.RawMaterials > site.UsedRaw - 1), $"day {game.Day}: no room at {Family.Of(site.Ref)}:{SdRef.IndexOf(site.Ref)}");
            }
        }
        int flips = retargets.Values.Sum(l => l.Zip(l.Skip(2)).Count(x => x.First == x.Second));
        output.WriteLine($"day {game.Day}: construction builds {builds} (mines / refineries {economy}), retargets {retargets.Values.Sum(l => l.Count)}, A-B-A {flips}, economy-only decisions {economyOnlyDays}");
        Assert.True(economyOnlyDays > 0);
        Assert.True(economy > 0);
        output.WriteLine($"economy-only at the end {ai.EconomyOnly}; construction managers idle at {idleDays} of {yardDays} manager-decisions");
        var first = Save(game);
        Assert.Equal(ai.EconomyOnly, System.Text.Encoding.UTF8.GetString(first).Contains("\"economyOnly\":true"));
        Assert.Equal(Text(first), Text(Save(Load(first))));
    }

    /// <summary>
    /// Version 7 fighters and carriers: fighters go into an own fleet with room in the yard's system; own fleets with fighter room
    /// gather at own shipyard systems (sampled every 50 days, the travelling ones aside); fighters are ordered. Logs the fighter
    /// orders per 100 days and the free maintenance (fighters need maintenance: a full economy blocks them).
    /// </summary>
    [GameFact]
    public void FightersAndCarriers()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Wings); // the version 7 fleet-only fighter orders this test was written for (Wings aims fighters at own systems too)
        var periods = new int[6];
        int withRoom = 0, atYards = 0;
        var free = new List<string>();
        while (game.Day < 600 && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count, decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided != decided && game.Day % 50 == 0 && game.Day > 100)
            {
                var yards = game.Systems.Where(x => x.Owner == Owner.Empire && x.Contents.Any(f => f.Owner == Owner.Empire && f.Usable && Family.Of(f.Type) == Families.Shipyard)).ToHashSet();
                var fleets = game.Systems.SelectMany(x => x.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Empire && f.Existing && !f.Enroute).ToList();
                int Room(Fleet f) => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)
                    .Sum(x => (int)game.Definitions.CapitalShips.First(r => r.Common.Ref == x.Type).FighterCapacity - x.Contents.Count(y => Family.Of(y.Type) is >= Families.Fighter and < Families.Headquarters));
                withRoom += fleets.Count(f => Room(f) > 0);
                atYards += fleets.Count(f => Room(f) > 0 && yards.Contains((StarSystem)f.Container!));
                var side = game.Sides[Owner.Empire];
                free.Add($"{game.Day}:{side.MaintenanceCapacity - side.MaintenanceAllocated}/{side.MaintenanceCapacity}");
            }
            foreach (var t in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.ProduceShip && Family.Of(t.Command.Fields[0]) is >= Families.Fighter and < Families.Headquarters))
            {
                var m = (ManufacturingManager)t.Command.Targets[0].Target;
                periods[(game.Day - 1) / 100] += (int)t.Command.Fields[1];
                Assert.True(m.TargetKey is Fleet { Owner: Owner.Empire } f && f.Container == m.System, $"day {game.Day}: fighters not into an own fleet at the yard");
            }
        }
        output.WriteLine($"fighters ordered per 100 days {string.Join(",", periods)}; fleets with room at yards {atYards} of {withRoom}; free maintenance {string.Join(" ", free)}");
        Assert.True(periods.Sum() > 0);
        Assert.True(2 * atYards >= withRoom);
    }

    /// <summary>
    /// EconomyFirst: with the side's construction yards at their target count, free maintenance above 25 % and a
    /// free raw-material slot, an idle construction yard's order is a Mine or a Refinery at a system with room for it.
    /// </summary>
    [GameFact]
    public void EconomyFirstYardOrdersAMineOrRefinery()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = [OpenSWRController.V7.Fighters, OpenSWRController.V7.Watch, OpenSWRController.V7.LateEconomy, OpenSWRController.V7.EconomyFirst];
        game.Clock.Step(game);
        // enough construction yards: the EconomyFirst target (max(2, ceil(r x owned systems)))
        uint yardType = Research.Buildable(game, Owner.Empire, Families.ConstructionYard).First(r => Family.Of(r.Common.Ref) == Families.ConstructionYard).Common.Ref;
        var own = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).ToList();
        int wanted = ai.YardTarget(own.Count);
        for (int i = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Existing && Family.Of(f.Type) == Families.ConstructionYard); i < wanted + 1; i++)
            Objects.Create(game, yardType, Owner.Empire, own[i % own.Count], completed: true);
        game.Clock.Flush(game);
        var side = game.Sides[Owner.Empire];
        Assert.True(4 * (side.MaintenanceCapacity - side.MaintenanceAllocated) > side.MaintenanceCapacity, "free maintenance not above 25 %");
        Assert.Contains(own, x => x.RawMaterials > x.UsedRaw && x.Energy > x.UsedEnergy);
        // an idle construction yard's choice
        var m = own.SelectMany(x => ScreenViews.Managers(x, Owner.Empire)).First(x => x.Kind == ManufacturingKind.Construction && Economy.Members(x).Any());
        var (facility, site) = ai.EconomyFirstPick(game, m, (StarSystem)m.System!, side.MaintenanceCapacity - side.MaintenanceAllocated, 0);
        output.WriteLine($"pick {facility?.Common.Ref} at {site?.Ref}");
        Assert.NotNull(facility);
        uint family = Family.Of(facility!.Common.Ref);
        Assert.True(family is Families.Mine or Families.Refinery, $"picked {Family.Of(facility.Common.Ref)}:{SdRef.IndexOf(facility.Common.Ref)}");
        Assert.True(family == Families.Refinery ? site!.Energy > site.UsedEnergy : site!.RawMaterials > site.UsedRaw && site.Energy > site.UsedEnergy);
    }

    /// <summary>
    /// EconomyFirst yard ratio: with fewer construction yards than max(2, ceil(r x owned systems)) an idle yard's
    /// order is another construction yard; at the target it is not; the earlier capital-ship gate is gone (an idle shipyard
    /// still orders a capital ship while a construction yard finds nothing to build).
    /// </summary>
    [GameFact]
    public void EconomyFirstYardRatio()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = [OpenSWRController.V7.Fighters, OpenSWRController.V7.Watch, OpenSWRController.V7.LateEconomy, OpenSWRController.V7.EconomyFirst];
        game.Clock.Step(game);
        var own = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).ToList();
        int yards = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Existing && Family.Of(f.Type) == Families.ConstructionYard);
        var side = game.Sides[Owner.Empire];
        int free = side.MaintenanceCapacity - side.MaintenanceAllocated;
        var m = own.SelectMany(x => ScreenViews.Managers(x, Owner.Empire)).First(x => x.Kind == ManufacturingKind.Construction && Economy.Members(x).Any());
        ai.YardRatio = 1.0; // target = owned systems: short of yards
        Assert.True(yards < ai.YardTarget(own.Count));
        Assert.Equal(Families.ConstructionYard, Family.Of(ai.EconomyFirstPick(game, m, (StarSystem)m.System!, free, 0).Facility!.Common.Ref));
        ai.YardRatio = 0.0; // target = the minimum 2: met
        Assert.Equal(OpenSWRController.MinConstructionYards, ai.YardTarget(own.Count));
        if (yards >= OpenSWRController.MinConstructionYards)
            Assert.NotEqual(Families.ConstructionYard, Family.Of(ai.EconomyFirstPick(game, m, (StarSystem)m.System!, free, 0).Facility?.Common.Ref ?? 0));
        Assert.Equal(4, new OpenSWRController(Owner.Empire) { YardRatio = 0.75 }.YardTarget(5)); // ceil(3.75)
    }

    /// <summary>
    /// With EconomyFirst a shipyard no longer waits for idle construction yards. Every own system is made full
    /// (no free energy slot), so the construction yards find nothing to build; the shipyards still order capital ships.
    /// </summary>
    [GameFact]
    public void EconomyFirstShipsDoNotWait()
    {
        var (game, ai) = AllianceGame(Owner.Alliance);
        ai.Features = [OpenSWRController.V7.Fighters, OpenSWRController.V7.Watch, OpenSWRController.V7.LateEconomy, OpenSWRController.V7.EconomyFirst];
        bool IdleKind(ManufacturingKind kind) => game.Systems.Where(x => x.Owner == Owner.Alliance).SelectMany(x => ScreenViews.Managers(x, Owner.Alliance))
            .Any(m => m.Kind == kind && m.Orders.Count == 0 && Economy.Members(m).Any(f => !f.Suspended));
        do game.Clock.Step(game);
        while (game.Day < 140 && !(IdleKind(ManufacturingKind.Ship) && IdleKind(ManufacturingKind.Construction)));
        foreach (var x in game.Systems.Where(x => x.Owner == Owner.Alliance)) x.Energy = x.UsedEnergy;
        ai.DayDue();
        int from = ai.IssueTrace!.Count;
        ai.Production(game, new OpenSWRController.Known(game, Owner.Alliance), [], 0, ai.Sectors(game));
        Assert.Contains(ai.IssueTrace.Skip(from), t => t.Result.Success && t.Command.Type == CommandType.ProduceShip && Family.IsCapitalShip(t.Command.Fields[0]));
    }

    /// <summary>
    /// Shipyard target fix: a shipyard aimed at a fleet that then leaves points its target back at its own system
    /// and orders a capital ship within two decisions.
    /// </summary>
    [GameFact]
    public void ShipyardTargetLeftOnADepartedFleetComesBack()
    {
        var (game, ai) = AllianceGame(Owner.Alliance);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.DoctrineProduction); // the version 9 yard target this test was written for (DoctrineProduction aims the yard at a group fleet instead)
        bool Working(ManufacturingManager m) => m.Kind == ManufacturingKind.Ship && m.Orders.Count == 0 && Economy.Members(m).Any(f => !f.Suspended);
        ManufacturingManager? yard = null;
        while (game.Day < 200 && yard is null)
        {
            game.Clock.Step(game);
            yard = game.Systems.Where(x => x.Owner == Owner.Alliance).SelectMany(x => ScreenViews.Managers(x, Owner.Alliance)).FirstOrDefault(Working);
        }
        Assert.NotNull(yard);
        var at = yard!.System!;
        // a fleet of the side at the yard, the yard aimed at it, then the fleet leaves
        uint shipType = game.Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) == Families.CapitalShip).Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, at, completed: true);
        Objects.Create(game, shipType, Owner.Alliance, fleet, completed: true);
        game.Clock.Flush(game);
        Assert.True(Orders.Execute(game, new Command(CommandType.SetProductionTarget, Owner.Alliance) { Targets = [(yard, GroupMode.Object)], ArgRef = fleet }).Success);
        game.Clock.Flush(game);
        Assert.Equal(fleet, yard.TargetKey);
        var away = game.Systems.Where(x => x.Owner == Owner.Alliance && x != at).OrderBy(x => Orders.Distance(x, at)).First();
        Fleets.Move(game, fleet, away);
        game.Clock.Step(game);
        Assert.True(fleet.Enroute);
        int from = ai.IssueTrace!.Count, decisions = 0, decided = ai.Decided;
        bool back = false, ship = false;
        while (decisions < 2 && !ship)
        {
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            decided = ai.Decided;
            decisions++;
            foreach (var t in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Targets[0].Target == yard))
            {
                output.WriteLine($"day {game.Day}: {(int)t.Command.Type} {(t.Command.Fields.Length > 0 ? t.Command.Fields[0] : 0)} -> {t.Command.ArgRef?.Ref}");
                if (t.Command.Type == CommandType.SetProductionTarget && t.Command.ArgRef == at) back = true;
                if (t.Command.Type == CommandType.ProduceShip && Family.IsCapitalShip(t.Command.Fields[0])) ship = true;
            }
            from = ai.IssueTrace.Count;
        }
        Assert.True(back, "the target did not come back to the yard's system");
        Assert.True(ship, "no capital ship within two decisions");
    }

    /// <summary>At most <see cref="OpenSWRController.Budget"/> counted commands per day.</summary>
    [GameFact]
    public void DailyBudget()
    {
        var game = NewGame(401859171, Owner.Alliance);
        var ai = (OpenSWRController)game.Controllers[Owner.Alliance];
        // one decision per day, on the tick the day begins: count the issued commands between those ticks
        int count = 0, decided = 0, total = 0;
        while (game.Day < 30)
        {
            game.Clock.Step(game);
            int now = ai.IssueTrace!.Count(t => t.Result.Success);
            if (ai.Decided != decided) (decided, count) = (ai.Decided, 0);
            count += now - total;
            total = now;
            Assert.InRange(count, 0, OpenSWRController.Budget);
        }
        Assert.True(total > 0);
    }
}
