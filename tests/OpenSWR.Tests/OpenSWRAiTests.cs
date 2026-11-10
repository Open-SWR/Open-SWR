using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>The OpenSWR AI: save / load with its state, the controller kind switch, the per-day budget.</summary>
public class OpenSWRAiTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>The OpenSWR AI on <paramref name="openswr"/>'s side, the original AI on the other; battles run by the engine.</summary>
    static GameState NewGame(int seed, Owner openswr)
    {
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, seed, Opponent: ComputerOpponent.OpenSWR), Definitions);
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            int state = seed ^ (int)side;
            var ui = new UiRandom(() => state);
            game.Controllers[side] = side == openswr ? new OpenSWRController(side) { IssueTrace = [] } : new ComputerController(side) { Rand = n => ui.Rand(n) };
            game.Sides[side].Speed = 12;
        }
        game.BattleResults = _ => null;
        return game;
    }

    static byte[] Save(GameState game)
    {
        using var stream = new MemoryStream();
        SaveGame.Save(game, stream);
        return stream.ToArray();
    }

    static GameState Load(byte[] save)
    {
        using var stream = new MemoryStream(save);
        return SaveGame.Load(stream, Definitions, _ => _ => 0);
    }

    static string Text(byte[] save) => Regex.Replace(System.Text.Encoding.UTF8.GetString(save), "\"uiRngState\":-?[0-9]+", "");

    [GameFact]
    public void SaveAndLoadKeepTheController()
    {
        var game = NewGame(874334749, Owner.Empire);
        while (game.Day < 60 && !game.GameOver) game.Clock.Step(game);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        output.WriteLine($"day {game.Day}: commands {ai.IssueTrace!.Count(t => t.Result.Success)}, refused {ai.IssueTrace!.Count(t => !t.Result.Success)}, " +
            $"staging {ai.Staging?.Ref:X8}, target {ai.Target?.Ref:X8}, refusals remembered {ai.Refusals}");
        Assert.Contains(ai.IssueTrace!, t => t.Result.Success);

        var first = Save(game);
        var loaded = Load(first);
        Assert.Equal(ComputerOpponent.OpenSWR, loaded.Settings.Opponent);
        var b = Assert.IsType<OpenSWRController>(loaded.Controllers[Owner.Empire]);
        Assert.IsType<ComputerController>(loaded.Controllers[Owner.Alliance]);
        Assert.Equal((ai.Issued, ai.Decided, ai.Staging?.Ref, ai.Target?.Ref, ai.Refusals, ai.Save().ToJsonString()),
            (b.Issued, b.Decided, b.Staging?.Ref, b.Target?.Ref, b.Refusals, b.Save().ToJsonString()));
        Assert.Equal(Text(first), Text(Save(loaded)));

        // the loaded game goes on
        while (loaded.Day < 70 && !loaded.GameOver) loaded.Clock.Step(loaded);
        Assert.Equal(Text(Save(loaded)), Text(Save(Load(Save(loaded)))));
    }

    /// <summary>The launcher's pairing: a human side (no advisor) against the OpenSWR AI; the save has no computer object.</summary>
    [GameFact]
    public void SaveAndLoadAgainstHuman()
    {
        var game = NewGame(532947196, Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 40 && !game.GameOver) game.Clock.Step(game);
        var first = Save(game);
        var loaded = Load(first);
        Assert.IsType<HumanController>(loaded.Controllers[Owner.Alliance]);
        Assert.IsType<OpenSWRController>(loaded.Controllers[Owner.Empire]);
        Assert.Equal(Text(first), Text(Save(loaded)));
        while (loaded.Day < 50 && !loaded.GameOver) loaded.Clock.Step(loaded);
        Assert.Equal(Text(Save(loaded)), Text(Save(Load(Save(loaded)))));
    }

    /// <summary>The OpenSWR AI with the repo's lines against a human side (no advisor).</summary>
    static (GameState Game, OpenSWRController Ai) AgainstHuman(int seed, Owner openswr)
    {
        var game = NewGame(seed, openswr);
        var ai = (OpenSWRController)game.Controllers[openswr];
        ai.Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        var human = openswr == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        game.Controllers[human] = new HumanController(human) { Rand = _ => 0 };
        return (game, ai);
    }

    static Owner Other(Owner side) => side == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    /// <summary>
    /// Group launch with one fleet that cannot move (Enroute): the group order is refused under its own key, the lead fleet's own
    /// move stays allowed, the invalid fleet gets no move, and the remaining fleets go together or not at all.
    /// </summary>
    [GameFact]
    public void GroupLaunchFallbackLeavesNoFleetStranded()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        game.Clock.Step(game);
        var fleets = game.Systems.SelectMany(x => x.Contents.OfType<Fleet>())
            .Where(f => f.Owner == Owner.Empire && f.Usable && !f.Enroute && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing)).OrderBy(f => f.Key).ToList();
        Assert.True(fleets.Count >= 3, $"{fleets.Count} fleets");
        var target = game.Systems.First(x => x.Owner == Owner.Alliance);
        var bad = fleets[^1];
        bad.Enroute = true;
        int before = ai.Outbox.Count;
        ai.DayDue(); // a fresh budget: the day's decision has used some of it
        // value per fleet 1, defence 0: the group is strong enough whenever a troop is aboard (both outcomes are checked below)
        bool launched = ai.Launch(game, fleets, target, 0, _ => 1, []);
        var issued = ai.Outbox.Skip(before).ToList();
        output.WriteLine($"launched {launched}, issued {issued.Count}, carrying {fleets.Count(f => f.Contents.Any(x => x.Contents.Any(y => Family.IsTroop(y.Type))))}");
        Assert.True(ai.Refused(new Command(0x201, Owner.Empire) { Targets = [.. fleets.Select(f => ((GameObject)f, 0))], ArgRef = target })
            || !launched, "the group order is refused (Enroute fleet) or nothing launched");
        Assert.DoesNotContain(issued, c => c.Targets.Count > 1);
        Assert.DoesNotContain(issued, c => c.Targets.Any(t => t.Target == bad));
        Assert.False(ai.Refused(new Command(0x201, Owner.Empire) { Targets = [(fleets[0], 0)], ArgRef = target }));
        if (launched) Assert.Equal(fleets.Take(fleets.Count - 1).Select(f => f.Ref), issued.Select(c => c.Targets[0].Target.Ref));
        else Assert.Empty(issued);
    }

    /// <summary>A fleet holding a threatened own system is not sent to help another threatened system.</summary>
    [GameFact]
    public void DefenceKeepsAHoldingFleet()
    {
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 3, 0, 0, 532947196), Definitions); // large galaxy: enough fleets
        var ai = new OpenSWRController(Owner.Empire);
        game.Controllers[Owner.Empire] = ai;
        var k = new OpenSWRController.Known(game, Owner.Empire);
        static IEnumerable<Fleet> Of(GameState g, Owner o) => g.Systems.SelectMany(x => x.Contents.OfType<Fleet>())
            .Where(f => f.Owner == o && f.Usable && !f.Enroute && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing));
        var holder = Of(game, Owner.Empire).OrderByDescending(f => k.Strength(f, Owner.Empire)).First();
        var ka = new OpenSWRController.Known(game, Owner.Alliance); // the enemy fleets' real values (their own side's view): picked by the test only
        var enemies = Of(game, Owner.Alliance).Where(f => ka.Strength(f, Owner.Alliance) > 0 && ka.Strength(f, Owner.Alliance) <= k.Strength(holder, Owner.Empire))
            .OrderBy(f => f.Key).Take(2).ToList();
        Assert.Equal(2, enemies.Count);
        var own = game.Systems.Where(x => x.Owner == Owner.Empire && !Of(game, Owner.Empire).Any(f => f.Container == x) && !Of(game, Owner.Alliance).Any(f => f.Container == x)).OrderBy(x => x.Key).Take(2).ToList();
        Assert.Equal(2, own.Count);
        Objects.SetParent(game, holder, own[0]);
        Objects.SetParent(game, enemies[0], own[0]); // weaker than the holder: own[0] holds
        Objects.SetParent(game, enemies[1], own[1]); // own[1] has no fleet: the holder alone would win there
        foreach (var x in own) Knowledge.RecomputeSystem(game, x); // ObservedBy of the own systems (owner bit): the enemy fleets there turn live
        k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Contains(enemies[0], k.Fleets(own[0], Owner.Alliance));
        Assert.Contains(enemies[1], k.Fleets(own[1], Owner.Alliance));
        int before = ai.Outbox.Count;
        // every other own fleet busy: the holder is the only fleet that could help own[1]
        var threatened = ai.Defence(game, k, [.. Of(game, Owner.Empire).Where(f => f != holder)]);
        Assert.Contains(own[0], threatened);
        Assert.DoesNotContain(ai.Outbox.Skip(before), c => c.Targets.Any(t => t.Target == holder));
    }

    /// <summary>
    /// A construction yard whose destination system has no energy left moves its destination first (0x214) and orders the Mine /
    /// Refinery at the next decision, so the order is never validated against the old destination (no 0x210 refusal).
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
        Assert.Contains(ai.IssueTrace!.Skip(from), t => t.Command.Type == 0x214 && t.Command.Targets[0].Target == yard && t.Result.Success);
        game.Clock.Step(game); // the 0x214 runs
        Assert.NotEqual(full, yard.TargetKey);
        Decide();
        var trace = ai.IssueTrace!.Skip(from).Where(t => t.Command.Type == 0x210 && t.Command.Targets[0].Target == yard).ToList();
        Assert.Contains(trace, t => t.Result.Success && Family.Of(t.Command.Fields[0]) is 0x2C or 0x2D);
        Assert.DoesNotContain(trace, t => !t.Result.Success);
    }

    /// <summary>The openswr Empire's first 60 days: per day its 0x240 commands (issue order) with the sector map of that day.</summary>
    static List<(int Day, OpenSWRController.SectorMap Map, List<Command> Missions, int Scouting)> MissionDays(int seed, int days, Dictionary<Mission, (StarSystem Target, int Day)> seen)
    {
        var game = NewGame(seed, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Agents); // the version 1-7 mission rules (Agents' espionage budget and pool change them)
        var list = new List<(int, OpenSWRController.SectorMap, List<Command>, int)>();
        while (game.Day < days && !game.GameOver)
        {
            int day = game.Day, from = ai.IssueTrace!.Count;
            var map = ai.Sectors(game);
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            foreach (var m in game.Missions.Where(m => m.Owner == Owner.Empire && m.Kind is 0x52 or 0x54 && m.TaskStatus == 3 && m.Objective is StarSystem))
                seen.TryAdd(m, ((StarSystem)m.Objective!, day)); // the day the scouting succeeded
            int scouting = game.Missions.Count(m => m.Owner == Owner.Empire && !m.Deleted && m.CompletionStatus == 0 && m.Kind is 0x52 or 0x54 && m.Team.Any(a => a is Character)); // character scouts (special-force scouts come on top, version 3)
            list.Add((day, map, [.. ai.IssueTrace!.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240).Select(t => t.Command)], scouting));
        }
        return list;
    }

    static uint Kind(Command c) => Family.Of(c.Fields[2]);

    /// <summary>Scouting starts in the home sector (Coruscant's for the Empire) and goes out by ring; never more than 2 scouts at a time.</summary>
    [GameFact]
    public void ScoutsHomeSectorFirstAndAtMostTwo()
    {
        var seen = new Dictionary<Mission, (StarSystem Target, int Day)>();
        var days = MissionDays(874334749, 60, seen);
        // version 7: espionage on own systems is the watch, not scouting by ring
        var scouts = days.SelectMany(d => d.Missions.Where(c => Kind(c) is 0x52 or 0x54 && ((StarSystem)c.ArgRef!).Owner != Owner.Empire).Select(c => (d.Day, d.Map, Target: (StarSystem)c.ArgRef!))).ToList();
        foreach (var (day, map, target) in scouts) output.WriteLine($"day {day}: scout {target.Ref:X8} ring {map.RingOf(target)} (home {map.Home?.Ref:X8})");
        Assert.NotEmpty(scouts);
        // the first character scout goes to the home sector (version 7: the watches come first and can take the characters, so a
        // scouting special force may reach an unexplored outer system before it)
        var first = days.SelectMany(d => d.Missions.Where(c => Kind(c) == 0x52 && c.Targets[0].Target is Character && ((StarSystem)c.ArgRef!).Owner != Owner.Empire)
            .Select(c => (d.Map, Target: (StarSystem)c.ArgRef!))).First();
        Assert.Equal(0, first.Map.RingOf(first.Target));
        Assert.All(scouts.Zip(scouts.Skip(1)).Where(p => p.First.Day == p.Second.Day), p => Assert.True(p.First.Map.RingOf(p.First.Target) <= p.Second.Map.RingOf(p.Second.Target)));
        Assert.All(days, d => Assert.InRange(d.Scouting, 0, OpenSWRController.MaxScouts));
        Assert.All(days, d => Assert.InRange(d.Missions.Count(c => Kind(c) is 0x52 or 0x54 && c.Targets[0].Target is Character), 0, OpenSWRController.MaxScouts));
        // a successful scouting counts as a sighting: no system is scouted again within ScoutAgainDays of it (a failed one, as
        // 90000104's day-1 espionage since the issue-1 fix, is no sighting)
        foreach (var (day, _, target) in scouts)
            foreach (int success in seen.Values.Where(e => e.Target == target && e.Day <= day).Select(e => e.Day))
                Assert.True(day - success >=OpenSWRController.ScoutAgainDays, $"{target.Ref:X8} seen on day {success}, scouted again on day {day}");
        Assert.NotEmpty(seen);
    }

    /// <summary>Gives the side's frozen view of a system the values (a constructed knowledge state; the real system is unchanged).</summary>
    static void View(GameState game, StarSystem s, Owner side, Owner owner, int share)
    {
        if (!game.Views.TryGetValue((s, side), out var v)) game.Views[(s, side)] = v = new View();
        (v.Owner, v.Populated, v.Explored, v.Completed, v.Created, v.Usable) = (owner, true, true, true, true, true);
        v.Support = side == Owner.Alliance ? share : 100 - share;
    }

    /// <summary>
    /// Diplomacy order within one decision, on a constructed knowledge state: the home sector (and, version 6, own systems below the
    /// safe share in any sector) first, then neutral systems in the
    /// power base outside it, then the frontier. The views make sector P a power-base sector (two systems seen as own) with one
    /// neutral system, and show one neutral system in a frontier sector F; the home sector's targets are left as they are.
    /// </summary>
    [GameFact]
    public void DiplomacyHomeThenPowerBaseThenFrontier()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        game.Clock.Step(game);
        var home = ai.Sectors(game).Home!;
        bool Unseen(StarSystem s) => s.Owner != Owner.Empire && !Knowledge.IsLive(s, Owner.Empire);
        var sectors = game.Systems.GroupBy(s => s.Sector).Where(g => g.Key != home && g.Count(Unseen) >= 3).Select(g => g.Key).OrderBy(x => x.Ref).ToList();
        var p = sectors.First();
        var inP = game.Systems.Where(s => s.Sector == p && Unseen(s)).OrderBy(s => s.Key).Take(3).ToList();
        View(game, inP[0], Owner.Empire, Owner.Empire, 90);
        View(game, inP[1], Owner.Empire, Owner.Empire, 90);
        View(game, inP[2], Owner.Empire, Owner.Neutral, 40);
        var map = ai.Sectors(game);
        Assert.Contains(p, map.PowerBase);
        var f = map.Frontier.OrderBy(x => x.Ref).First(x => game.Systems.Any(s => s.Sector == x && Unseen(s)));
        var inF = game.Systems.First(s => s.Sector == f && Unseen(s));
        View(game, inF, Owner.Empire, Owner.Neutral, 40);
        // the day's budget (10) reaches past the home sector: its neutral systems but one are seen at share 100 (no diplomacy there)
        foreach (var s in game.Systems.Where(s => s.Sector == home && Unseen(s) && Knowledge.See(game, s, Owner.Empire).Owner == Owner.Neutral).OrderBy(s => s.Key).Skip(1))
            View(game, s, Owner.Empire, Owner.Neutral, 100);
        map = ai.Sectors(game);
        Assert.Contains(f, map.Frontier);
        // only diplomacy: no scouting (every system counts as seen today), plenty of budget
        foreach (var s in game.Systems) ai.MarkSeen(s, game.Day);
        foreach (var s in game.Systems) ai.MarkWatched(s, game.Day); // version 7: no watch either
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Recruit); // version 9: recruitment would take the characters first
        ai.DayDue();
        int from = ai.IssueTrace!.Count;
        ai.Assign(game, new OpenSWRController.Known(game, Owner.Empire), map, []);
        var targets = ai.IssueTrace!.Skip(from).Where(t => t.Result.Success && Kind(t.Command) == 0x51).Select(t => (StarSystem)t.Command.ArgRef!).ToList();
        // version 6: own systems below the safe share (any sector) come with the home sector
        int Rank(StarSystem s) => s.Sector == home || Knowledge.See(game, s, Owner.Empire).Owner == Owner.Empire ? 0 : map.PowerBase.Contains(s.Sector) ? 1 : map.Frontier.Contains(s.Sector) ? 2 : 3;
        foreach (var s in targets) output.WriteLine($"diplomacy {s.Ref:X8} rank {Rank(s)}");
        Assert.Contains(targets, s => Rank(s) == 1);
        Assert.Contains(targets, s => Rank(s) == 2); // the frontier (inF or another neutral frontier system with more support)
        Assert.All(targets.Zip(targets.Skip(1)), q => Assert.True(Rank(q.First) <= Rank(q.Second)));
        Assert.DoesNotContain(targets, s => Rank(s) == 3);
    }

    /// <summary>Covert worth on hand-made cases.</summary>
    [GameFact]
    public void CovertWorth()
    {
        var game = NewGame(874334749, Owner.Empire);
        game.Clock.Step(game); // placed characters with rolled skills
        var placed = game.Characters.Where(c => c.Existing && c.Owner == Owner.Empire).ToList();
        var minor = placed.Where(c => Family.Of(c.Type) is >= 0x38 and < 0x3C).OrderByDescending(c => c.Skills[SkillIndex.Combat] + c.Skills[SkillIndex.Espionage]).First();
        var major = placed.First(c => Family.Of(c.Type) is >= 0x30 and < 0x38);
        int minorValue = OpenSWRController.CharacterValue(minor, live: true), majorValue = OpenSWRController.CharacterValue(major, live: true);
        Assert.Equal(100, majorValue - (major.Force > 0 ? 20 : 0));
        Assert.InRange(minorValue, 10, 35 + 15 + 20);
        var team = new List<Agent> { minor }; // the rescuer: a minor character
        // heavy: 4 capital ships, 12 fighter squadrons and 4 troop regiments, commanders shown (espionage and combat 80); light: one troop
        var guarded = new OpenSWRController.Detector(10, 80, 80);
        var heavy = new OpenSWRController.CovertKnown([.. Enumerable.Repeat(guarded, 4), .. Enumerable.Repeat(guarded with { Value = 12 }, 12)], [.. Enumerable.Repeat(guarded with { Value = 2 }, 4)]);
        var light = new OpenSWRController.CovertKnown([], [new(1)]);
        int p = Missions.Table(game, "RESCMSTB", Missions.Enhanced(game, minor, SkillIndex.Combat)) ?? 0;
        double heavyFoil = OpenSWRController.FoilChance(game, team, heavy), lightFoil = OpenSWRController.FoilChance(game, team, light);
        double heavyLost = OpenSWRController.LostChance(game, minor, heavy), lightLost = OpenSWRController.LostChance(game, minor, light);
        output.WriteLine($"rescue p {p}; foil heavy {heavyFoil:F2}, light {lightFoil:F2}; lost heavy {heavyLost:F2}, light {lightLost:F2}; values minor {minorValue}, major {majorValue}");
        Assert.True(heavyFoil > lightFoil);
        Assert.True(heavyLost >= lightLost);
        Assert.True(OpenSWRController.Worth(game, 0x61, team, p, 15, heavy) < 0); // a heavily guarded minor captive (value 15): no rescue
        Assert.True(OpenSWRController.Worth(game, 0x61, team, p, 100, light) > 0); // a lightly guarded major captive: rescue
        // the formula: p_success * value - foil * lost * loss; a plain failure costs nothing
        double success = (1 - lightFoil) * p / 100.0;
        Assert.Equal(success * 100 - lightFoil * lightLost * minorValue, OpenSWRController.Worth(game, 0x61, team, p, 100, light), 6);
        Assert.Equal(0.4 * 40, OpenSWRController.Worth(game, 0x58, team, 40, 40, heavy), 6); // Jedi training: no encounter, no loss
    }

    /// <summary>
    /// A stale target (not seen within ScoutAgainDays) takes the pools of the median known enemy system, and none without one; the
    /// enemy system seen today is the median (the only one). Its view is live: an own fleet is moved there.
    /// </summary>
    [GameFact]
    public void StaleTargetUsesTheMedianSystem()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        game.Clock.Step(game);
        var enemy = game.Systems.Where(s => s.Owner == Owner.Alliance && Knowledge.See(game, s, Owner.Empire).Owner == Owner.Alliance).OrderBy(s => s.Key).ToList();
        Assert.True(enemy.Count >= 2);
        var seen = enemy.OrderByDescending(s => s.Contents.Count(x => Family.IsTroop(x.Type) && x.Usable)).First();
        var stale = enemy.First(s => s != seen && !Knowledge.IsLive(s, Owner.Empire));
        var k0 = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Null(ai.MedianSystem(game, k0)); // no enemy system seen yet
        Assert.Equal(OpenSWRController.CovertKnown.None, ai.KnownAt(game, k0, stale, true, null));
        var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Empire && f.Existing && f.Usable && !f.Enroute);
        Objects.SetParent(game, fleet, seen);
        Knowledge.RecomputeSystem(game, seen); // the own fleet makes the system's view live
        Assert.True(Knowledge.IsLive(seen, Owner.Empire));
        var k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Equal(seen, ai.MedianSystem(game, k));
        var own = ai.KnownAt(game, k, seen, true, null);
        var viaMedian = ai.KnownAt(game, k, stale, true, ai.MedianSystem(game, k));
        output.WriteLine($"median {seen.Ref:X8}: phase 7 {own.Phase7.Count}, phase 9 {own.Phase9.Count}");
        Assert.True(own.Count > 0);
        Assert.Equal(own.Phase7, viaMedian.Phase7);
        Assert.Equal(own.Phase9, viaMedian.Phase9);
    }

    /// <summary>Over two games: never more than MaxCovert covert missions and at most one Jedi training running at a time.</summary>
    [GameFact]
    public void CovertLimits()
    {
        foreach (var side in new[] { Owner.Empire, Owner.Alliance })
        {
            var game = NewGame(401859171, side);
            while (game.Day < 200 && !game.GameOver)
            {
                game.Clock.Step(game);
                var running = game.Missions.Where(m => m.Owner == side && !m.Deleted && m.CompletionStatus == 0).ToList();
                Assert.InRange(running.Count(m => m.Kind is 0x56 or 0x58 or 0x61 or 0x62 or 0x63 or 0x69), 0, OpenSWRController.MaxCovert);
                Assert.InRange(running.Count(m => m.Kind == 0x58), 0, 1);
            }
        }
    }

    /// <summary>Within each decision no character goes on two missions, and no target gets a second own mission while one runs.</summary>
    [GameFact]
    public void NoDoubleAssignmentOrTarget()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        while (game.Day < 120 && !game.GameOver)
        {
            int day = game.Day, from = ai.IssueTrace!.Count;
            var running = game.Missions.Where(m => m.Owner == Owner.Empire && !m.Deleted && m.CompletionStatus == 0 && m.Kind >= 0x51).ToList();
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            // still running at the decision (a mission that ended during the day frees its target: version 7 watches end and repeat)
            var before = running.Where(m => m.CompletionStatus == 0).Select(m => m.Objective).ToHashSet();
            var issued = ai.IssueTrace!.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240).Select(t => t.Command).ToList();
            var members = issued.SelectMany(c => c.Targets.Select(t => t.Target)).ToList();
            Assert.Equal(members.Count, members.Distinct().Count());
            // recruitment (version 9 Recruit, in the default) goes to the recruiter's own system: several recruiters may share it
            var targetedOnce = issued.Where(c => Kind(c) != 0x55).ToList();
            Assert.Equal(targetedOnce.Count, targetedOnce.Select(c => c.ArgRef).Distinct().Count());
            Assert.DoesNotContain(targetedOnce, c => before.Contains(c.ArgRef));
        }
    }

    /// <summary>A refused mission order is not validated or issued again within RefusalDays; the mission type is part of the key.</summary>
    [GameFact]
    public void NoReissueAfterARefusal()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        game.Clock.Step(game);
        var c = game.Characters.First(x => x.Owner == Owner.Empire && x.Existing && !x.OnMission);
        uint espionage = game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x52).Common.Ref;
        uint diplomacy = game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x51).Common.Ref;
        var refused = new Command(0x240, Owner.Empire) { Targets = [(c, 0)], ArgRef = c, Fields = [0, 0, espionage] }; // a character is no espionage target
        ai.DayDue();
        Assert.False(ai.Issue(game, refused, []));
        int traced = ai.IssueTrace!.Count;
        Assert.True(ai.Refused(refused));
        Assert.False(ai.Issue(game, new Command(0x240, Owner.Empire) { Targets = [(c, 0)], ArgRef = c, Fields = [0, 0, espionage] }, []));
        Assert.Equal(traced, ai.IssueTrace!.Count); // not validated again
        Assert.False(ai.Refused(new Command(0x240, Owner.Empire) { Targets = [(c, 0)], ArgRef = c, Fields = [0, 0, diplomacy] }));
    }

    /// <summary>
    /// The aimatch game of seed 532947196 (galaxy 2, Novice) with the OpenSWR AI as Alliance, where its majors get captured: per
    /// decision the rescue commands (day, captive, the captive's view: Enroute, location) and the 0x240 kinds in issue order.
    /// </summary>
    static List<(int Day, List<(uint Kind, GameObject? Target, Seen? Captive)> Missions)> RescueGame(int days, int seed = DefaultSeed)
    {
        var (game, ai) = AllianceGame(seed: seed);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.CovertExperience); // version 9: its 10 % prior stops the rescues this test checks
        var list = new List<(int, List<(uint, GameObject?, Seen?)>)>();
        while (game.Day < days && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count;
            game.Clock.Step(game);
            // after the decision tick: the captives' views as at the decision (the tick's dispatch ran before it; nothing after it moves them)
            var issued = ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240)
                .Select(t => (Kind(t.Command), t.Command.ArgRef, t.Command.ArgRef is Character c ? Knowledge.See(game, c, Owner.Alliance) : (Seen?)null)).ToList();
            if (issued.Count > 0) list.Add((ai.Decided, issued));
        }
        return list;
    }

    /// <summary>The aimatch game of seed 532947196 (galaxy 2, Novice): the OpenSWR AI on <paramref name="side"/> (Alliance: home sector in the rim) against the original AI.</summary>
    const int DefaultSeed = 532947196;

    static (GameState Game, OpenSWRController Ai) AllianceGame(Owner side = Owner.Alliance, int seed = DefaultSeed)
    {
        var other = side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, seed, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { IssueTrace = [] };
        int state = seed ^ (int)other;
        var ui = new UiRandom(() => state);
        game.Controllers[side] = ai;
        game.Controllers[other] = new ComputerController(other) { Rand = n => ui.Rand(n) };
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        return (game, ai);
    }

    /// <summary>
    /// Version 3 scouting units: the side builds the reconnaissance special forces (its starting ones removed), never holds more
    /// than 2, and sends them on reconnaissance to systems its views show unexplored, home sector and lower rings first within each
    /// decision; characters scout explored systems only. The Empire: the Alliance of this seed has no training facility.
    /// </summary>
    [GameFact]
    public void ScoutingSpecialForces()
    {
        const Owner side = Owner.Empire;
        var (game, ai) = AllianceGame(side);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Agents); // version 3 scouting units (the Agents pool trains reconnaissance types beyond them)
        var recon = game.Definitions.SpecialForces.Where(f => f.Empire != 0 && game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x54).AllowsSpecialForce(f.MissionMask))
            .Select(f => f.Common.Ref).ToHashSet();
        Assert.NotEmpty(recon);
        // the side starts with such units: remove them, so the AI has to build its own
        foreach (var unit in game.Created.Where(o => o.Owner == side && o.Existing && recon.Contains(o.Type)).ToList()) Objects.Destroy(game, unit, 0x14);
        while (game.Clock.DispatchOne(game, commands: false)) { }
        int built = 0, sent = 0;
        while (game.Day < 150 && !game.GameOver)
        {
            int day = game.Day, from = ai.IssueTrace!.Count;
            var map = ai.Sectors(game);
            var explored = game.Systems.ToDictionary(s => s, s => Knowledge.See(game, s, side).Explored);
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            var issued = ai.IssueTrace.Skip(from).Where(t => t.Result.Success).Select(t => t.Command).ToList();
            built += issued.Where(c => c.Type == 0x212 && recon.Contains(c.Fields[0])).Sum(c => (int)c.Fields[1]);
            var scouting = issued.Where(c => c.Type == 0x240 && Kind(c) == 0x54).Select(c => (StarSystem)c.ArgRef!).ToList();
            sent += scouting.Count;
            foreach (var s in scouting) output.WriteLine($"day {day}: reconnaissance {s.Ref:X8} ring {map.RingOf(s)}");
            Assert.All(scouting, s => Assert.False(explored[s]));
            Assert.All(scouting.Zip(scouting.Skip(1)), p => Assert.True(map.RingOf(p.First) <= map.RingOf(p.Second)));
            Assert.All(issued.Where(c => c.Type == 0x240 && Kind(c) == 0x52), c => Assert.True(explored[(StarSystem)c.ArgRef!])); // characters: explored only
            Assert.InRange(game.Created.Count(o => o.Owner == side && o.Existing && recon.Contains(o.Type)), 0, OpenSWRController.ScoutUnitsWanted);
        }
        output.WriteLine($"scouting units ordered {built}, reconnaissance missions {sent}");
        Assert.InRange(built, 1, 2 * OpenSWRController.ScoutUnitsWanted);
        Assert.True(sent > 0);
    }

    /// <summary>
    /// Version 4 training facilities: the seed 532947196 Alliance has none, orders one within its first 60 days (one at a time) and,
    /// once it stands and scouting units are missing, builds the reconnaissance special forces there.
    /// </summary>
    [GameFact]
    public void TrainingFacilityThenScouts()
    {
        var (game, ai) = AllianceGame();
        Assert.DoesNotContain(game.Facilities, f => f.Owner == Owner.Alliance && f.Existing && Family.Of(f.Type) == 0x29);
        var recon = game.Definitions.SpecialForces.Where(f => f.Alliance != 0 && game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x54).AllowsSpecialForce(f.MissionMask))
            .Select(f => f.Common.Ref).ToHashSet();
        // its starting scouting units are removed, so that it needs new ones
        foreach (var unit in game.Created.Where(o => o.Owner == Owner.Alliance && o.Existing && recon.Contains(o.Type)).ToList()) Objects.Destroy(game, unit, 0x14);
        while (game.Clock.DispatchOne(game, commands: false)) { }
        int ordered = -1, scouts = -1, taken = -1;
        while (game.Day < 250 && !game.GameOver && scouts < 0)
        {
            int from = ai.IssueTrace!.Count;
            game.Clock.Step(game);
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success).Select(t => t.Command))
            {
                // once one stands, more come from "production facilities first" (version 6), not from the version 4 rule
                if (c.Type == 0x210 && Family.Of(c.Fields[0]) == 0x29 && !game.Facilities.Any(f => f.Owner == Owner.Alliance && f.Usable && Family.Of(f.Type) == 0x29))
                {
                    Assert.Equal(-1, ordered); // one at a time: no second while the first is built, travels or stands
                    // and none while it holds one (taken with a system: version 7 may lose that system again, then it orders one)
                    Assert.DoesNotContain(game.Facilities, f => f.Owner == Owner.Alliance && f.Existing && f.Completed && Family.Of(f.Type) == 0x29);
                    ordered = game.Day;
                }
                if (c.Type == 0x212 && recon.Contains(c.Fields[0])) scouts = game.Day;
            }
            if (ordered < 0 && taken < 0 && game.Facilities.Any(f => f.Owner == Owner.Alliance && f.Existing && Family.Of(f.Type) == 0x29)) taken = game.Day;
            if (ordered >= 0 && !game.Facilities.Any(f => f.Owner == Owner.Alliance && f.Usable && Family.Of(f.Type) == 0x29))
                Assert.InRange(game.Facilities.Count(f => f.Owner == Owner.Alliance && f.Existing && Family.Of(f.Type) == 0x29), 0, 1);
        }
        output.WriteLine($"training facility ordered on day {ordered} (one taken with a system on day {taken}), scouting units ordered on day {scouts}");
        // ordered after the Refinery when manufacturing waits for refined material and once its maintenance fits; or a training facility
        // came with a captured system first (version 5 takes systems early in this seed), and then none is ordered
        if (taken < 0) Assert.InRange(ordered, 1, 60);
        Assert.True(scouts > Math.Max(ordered, taken));
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
        var training = Research.Buildable(game, Owner.Alliance, 0x2A).First(r => Family.Of(r.Common.Ref) == 0x29);
        int cost = game.MaintenanceCost(training.Common.Ref);
        output.WriteLine($"training facility {training.Common.Ref:X8} maintenance {cost}");
        Assert.True(cost > 0, "the training facility costs no maintenance: the condition cannot bind");
        // the choice itself: not below its maintenance, not while manufacturing waits for refined material
        Assert.Null(ai.TrainingFacility(game, cost - 1));
        Assert.Equal(training.Common.Ref, ai.TrainingFacility(game, cost)?.Common.Ref);
        var waiting = game.Facilities.First(f => f.Owner == Owner.Alliance);
        side.ManufacturingWaitList.Add(new(waiting, 0));
        Assert.Null(ai.TrainingFacility(game, cost));
        side.ManufacturingWaitList.Clear();
        // free below the facility's maintenance, and below the cheapest unit (so that a Mine or Refinery is wanted at all)
        int cheapest = Research.Buildable(game, Owner.Alliance, 0x28).Where(r => Family.IsCapitalShip(r.Common.Ref))
            .Concat(Research.Buildable(game, Owner.Alliance, 0x29).Where(r => Family.IsTroop(r.Common.Ref)))
            .Select(r => game.MaintenanceCost(r.Common.Ref)).Where(c => c > 0).Min();
        side.MaintenanceAllocated = side.MaintenanceCapacity - (Math.Min(cost, cheapest) - 1);
        ai.DayDue();
        ai.Production(game, new OpenSWRController.Known(game, Owner.Alliance), [], 0, ai.Sectors(game));
        var issued = ai.IssueTrace!.Where(t => t.Result.Success).Select(t => t.Command).ToList();
        foreach (var c in issued) output.WriteLine($"{c.Type:X} [{string.Join(",", c.Fields.Select(f => f.ToString("X8")))}] -> {c.ArgRef?.Ref:X8}");
        Assert.DoesNotContain(issued, c => c.Type == 0x210 && Family.Of(c.Fields[0]) == 0x29);
        Assert.Contains(issued, c => c.Type == 0x210 && Family.Of(c.Fields[0]) is 0x2C or 0x2D);
    }

    /// <summary>
    /// Version 5 troops by need: (version 6) each defended target is at least 2 and the total covers them and the attack troops; training
    /// facilities order troops; in a decision with troops below 75 % of the need, troop orders come before capital ships; a launch
    /// waits for the target's attack troops aboard.
    /// </summary>
    [GameFact]
    public void TroopsByNeed()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        int troopOrders = 0, lowDecisions = 0;
        while (game.Day < 300 && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count, decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            var need = ai.Need;
            Assert.All(need.Defend.Values, n => Assert.True(n >= 2));
            Assert.All(need.Attack.Values, n => Assert.True(n >= 2));
            Assert.True(need.Total >= need.Defend.Values.Sum() + need.Attack.Values.Sum());
            var issued = ai.IssueTrace.Skip(from).Where(t => t.Result.Success).Select(t => t.Command).ToList();
            int troopIndex = issued.FindIndex(c => c.Type == 0x212 && Family.IsTroop(c.Fields[0]));
            int shipIndex = issued.FindIndex(c => c.Type == 0x211 && Family.IsCapitalShip(c.Fields[0]));
            if (troopIndex >= 0) troopOrders++;
            int troops = game.Created.Count(o => Family.IsTroop(o.Type) && o.Owner == Owner.Empire && o.Existing);
            // version 6 production plan: the larger weighted shortfall first (the version 5 troops-first rule is gone)
            if (troops < 0.75 * need.Total && troopIndex >= 0 && shipIndex >= 0 && ai.CurrentPlan.Troops > Math.Max(ai.CurrentPlan.CapitalShips, ai.CurrentPlan.Fighters))
            {
                lowDecisions++;
                Assert.True(troopIndex < shipIndex, $"day {game.Day}: a capital ship before the troops");
            }
        }
        output.WriteLine($"decisions with troop orders {troopOrders}, low-troop decisions with both orders {lowDecisions}; need at the end {ai.Need.Total}, defended {ai.Need.Defend.Count}");
        Assert.True(troopOrders > 0);
        // the launch condition: not without the attack troops aboard
        var fleets = game.Systems.SelectMany(x => x.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Empire && f.Usable && !f.Enroute
            && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing)).ToList();
        ai.DayDue();
        Assert.False(ai.Launch(game, fleets, game.Systems.First(x => x.Owner == Owner.Alliance), 0, _ => 1, [], troops: 1000));
    }

    /// <summary>
    /// Version 5 counter-attack and parallel attacks: a system lost within 60 days becomes a group's target ahead of the usual order;
    /// over a game never more than 3 groups, each with its own target; groups and losses survive save -> load -> save byte-equal.
    /// </summary>
    [GameFact]
    public void CounterAttackAndParallelGroups()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Swarm); // the version 5 groups (Swarm replaces them)
        int most = 0;
        while (game.Day < 250 && !game.GameOver)
        {
            game.Clock.Step(game);
            Assert.InRange(ai.Groups.Count, 0, OpenSWRController.MaxGroups);
            Assert.Equal(ai.Groups.Count, ai.Groups.Select(g => g.Target).Distinct().Count());
            most = Math.Max(most, ai.Groups.Count);
        }
        output.WriteLine($"most groups at once {most}");
        // a counter-attack target: the known enemy system in reach that the usual order puts last, marked lost today
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var own = game.Systems.Where(s => s.Owner == Owner.Empire).ToList();
        var inReach = game.Systems.Where(s => Knowledge.See(game, s, Owner.Empire) is { Owner: Owner.Alliance, Destroyed: false }
            && own.Any(b => Orders.TravelDays(game, b, s, 100) <= OpenSWRController.ReachDays) && ai.Groups.All(g => g.Target != s)).OrderBy(s => s.Key).ToList();
        Assert.NotEmpty(inReach);
        var lost = inReach[^1];
        ai.MarkLost(lost, game.Day);
        ai.DayDue();
        ai.Attack(game, k, [], [], ai.Sectors(game));
        output.WriteLine($"lost {lost.Ref:X8}; targets {string.Join(",", ai.Groups.Select(g => g.Target.Ref.ToString("X8")))}");
        // a committed group (fleets at its target) keeps its target: then a controller without groups shows the order
        bool committed = ai.Groups.Count > 0 && ai.Groups.All(g => game.Systems.SelectMany(x => x.Contents.OfType<Fleet>()).Any(f => f.Owner == Owner.Empire && f.Existing && f.Container == g.Target));
        if (committed)
        {
            var fresh = new OpenSWRController(Owner.Empire);
            fresh.MarkLost(lost, game.Day);
            fresh.DayDue();
            fresh.Attack(game, k, [], [], fresh.Sectors(game));
            Assert.Equal(lost, fresh.Groups[0].Target);
        }
        else Assert.Contains(ai.Groups, g => g.Target == lost);
        var first = Save(game);
        Assert.Contains("\"lost\":", System.Text.Encoding.UTF8.GetString(first));
        Assert.Equal(Text(first), Text(Save(Load(first))));
    }

    /// <summary>
    /// Version 5 stricter covert gate: every covert candidate of the gated kinds has p_success >= 0.5 (a major's rescue >= 0.3); three
    /// foils of one kind within 90 days pause that kind for 90 days, two do not; the pause and foil counts survive save -> load -> save.
    /// </summary>
    [GameFact]
    public void StricterCovertGate()
    {
        var (game, ai) = AllianceGame();
        int checkedCandidates = 0;
        while (game.Day < 300 && !game.GameOver)
        {
            int decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided || game.Day % 10 != 0) continue;
            var k = new OpenSWRController.Known(game, Owner.Alliance);
            var map = ai.Sectors(game);
            var free = game.Characters.Where(c => c.Owner == Owner.Alliance && c.Existing && !c.OnMission && !c.Captured && c.Injury == 0 && c.CommandKind == 0).ToList();
            foreach (var x in ai.CovertCandidates(game, k, map, free, new HashSet<GameObject?>(), rescueOnly: false))
            {
                if (OpenSWRController.MinSuccess(x.Kind, x.Target) == 0) continue;
                var at = x.Target as StarSystem ?? (StarSystem)Knowledge.Location(game, x.Target, Owner.Alliance)!;
                var known = ai.KnownAt(game, k, at, x.Target is StarSystem || Knowledge.ViewParent(game, x.Target, Owner.Alliance) is StarSystem, ai.MedianSystem(game, k));
                double success = OpenSWRController.Success(game, x.Team, ai.Attempt(game, k, x.Kind, x.Team[0], x.Target), known);
                Assert.True(success >= OpenSWRController.MinSuccess(x.Kind, x.Target), $"day {game.Day}: {x.Kind:X2} at {x.Target.Ref:X8} p_success {success:F2}");
                checkedCandidates++;
            }
        }
        output.WriteLine($"gated candidates checked {checkedCandidates}");
        // the type pause (a fresh controller: the game's own foils do not count here)
        var fresh = new OpenSWRController(Owner.Alliance);
        int day = 100;
        fresh.KindFoiled(0x62, day);
        fresh.KindFoiled(0x62, day + 10);
        Assert.False(fresh.Paused(0x62, day + 10));
        fresh.KindFoiled(0x63, day);
        fresh.KindFoiled(0x63, day + OpenSWRController.PauseDays); // the first one is out of the window
        fresh.KindFoiled(0x63, day + OpenSWRController.PauseDays + 1);
        Assert.False(fresh.Paused(0x63, day + OpenSWRController.PauseDays + 1));
        fresh.KindFoiled(0x62, day + 20);
        Assert.True(fresh.Paused(0x62, day + 20));
        Assert.True(fresh.Paused(0x62, day + 20 + OpenSWRController.PauseDays - 1));
        Assert.False(fresh.Paused(0x62, day + 20 + OpenSWRController.PauseDays));
        for (int i = 0; i < OpenSWRController.PauseFoils; i++) ai.KindFoiled(0x69, game.Day); // a pause in the game's controller, for the save
        var first = Save(game);
        Assert.Contains("\"paused\":", System.Text.Encoding.UTF8.GetString(first));
        Assert.Equal(Text(first), Text(Save(Load(first))));
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
        int yardsAtStart = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Usable && Family.Of(f.Type) == 0x28);
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
                var kind = Family.Of(facility.Common.Ref) switch { 0x28 => ManufacturingKind.Ship, 0x29 => ManufacturingKind.Training, _ => ManufacturingKind.Construction };
                Assert.False(Idle(kind));
                foreach (var before in new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction }.TakeWhile(x => x != kind))
                    Assert.True(Idle(before));
            }
        }
        int yards = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Usable && Family.Of(f.Type) == 0x28);
        output.WriteLine($"shipyards {yardsAtStart} → {yards}; unused maintenance after day 200 {unused.Average():P0}; phase {ai.CurrentPlan.Phase}");
        Assert.True(yards > yardsAtStart);
        Assert.True(unused.Average() < 0.55); // version 7: 0.5 -> 0.55 (ships by value per maintenance use less of it; the benchmark tracks it)
    }

    /// <summary>
    /// Version 6 defence by value: every defence target is a high-value own system (the top third by value) with threat, its target
    /// is at least 2; the covered ones are targets and fit the troops left after the at-risk garrisons; the defence sites are at most
    /// 3 own systems, each wanting a shield or a battery it lacks; a counter-attack takes the higher-valued loss first; a lost entry
    /// keeps its value through save -> load -> save byte-equal.
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
                Assert.True(ai.HighValue(s) && s.Owner == Owner.Empire, $"day {game.Day}: target {s.Ref:X8} not high-value own");
                Assert.True(n >= 2);
            }
            Assert.All(c, e => Assert.Equal(t[e.Key], e.Value));
            Assert.True(c.Values.Sum() <= ai.OwnTroops(game));
            foreach (var (site, product) in ai.DefenceSites(game, k))
            {
                Assert.Equal(Owner.Empire, site!.Owner);
                uint family = Family.Of(product!.Common.Ref);
                Assert.True(family is 0x23 or 0x24);
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
        output.WriteLine($"targets {string.Join(",", fresh.Groups.Select(g => g.Target.Ref.ToString("X8")))}");
        Assert.Equal(inReach[^1], fresh.Groups[0].Target);
        ai.MarkLost(inReach[^1], game.Day, 50);
        var first = Save(game);
        Assert.Contains($"[{inReach[^1].Ref},{game.Day},50]", System.Text.Encoding.UTF8.GetString(first));
        Assert.Equal(Text(first), Text(Save(Load(first))));
    }

    /// <summary>
    /// Version 7 garrisons from experience: the expected landing is 4 before any landing, then the mean of the last 10 rounded up;
    /// in a game every high-value border system is a defence target of at least the expected landing + 1; landings are recorded
    /// from lost assaults and survive save -> load -> save byte-equal.
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
                Assert.True(t.TryGetValue(s, out int n) && n >= ai.ExpectedLanding + 1, $"day {game.Day}: border {s.Ref:X8} target {n}");
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
    /// only Mines and Refineries; every Mine / Refinery order goes to a site with a free slot for it; the flag survives save -> load -> 
    /// save byte-equal. Logs the 0x214 retargets per construction build (the suspected churn).
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
                if (t.Command.Type == 0x214)
                {
                    if (!retargets.TryGetValue(m, out var l)) retargets[m] = l = [];
                    l.Add(t.Command.ArgRef!.Ref);
                }
                if (t.Command.Type != 0x210) continue;
                builds++;
                uint family = Family.Of(t.Command.Fields[0]);
                if (ai.EconomyOnly) Assert.True(family is 0x2C or 0x2D, $"day {game.Day}: {t.Command.Fields[0]:X8} while economy-only");
                if (family is not (0x2C or 0x2D)) continue;
                economy++;
                var site = (StarSystem)m.TargetKey!;
                Assert.True(site.Energy > site.UsedEnergy - 1 && (family == 0x2D || site.RawMaterials > site.UsedRaw - 1), $"day {game.Day}: no room at {site.Ref:X8}");
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
                var yards = game.Systems.Where(x => x.Owner == Owner.Empire && x.Contents.Any(f => f.Owner == Owner.Empire && f.Usable && Family.Of(f.Type) == 0x28)).ToHashSet();
                var fleets = game.Systems.SelectMany(x => x.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Empire && f.Existing && !f.Enroute).ToList();
                int Room(Fleet f) => f.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing)
                    .Sum(x => (int)game.Definitions.CapitalShips.First(r => r.Common.Ref == x.Type).FighterCapacity - x.Contents.Count(y => Family.Of(y.Type) is >= 0x1C and < 0x20));
                withRoom += fleets.Count(f => Room(f) > 0);
                atYards += fleets.Count(f => Room(f) > 0 && yards.Contains((StarSystem)f.Container!));
                var side = game.Sides[Owner.Empire];
                free.Add($"{game.Day}:{side.MaintenanceCapacity - side.MaintenanceAllocated}/{side.MaintenanceCapacity}");
            }
            foreach (var t in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x211 && Family.Of(t.Command.Fields[0]) is >= 0x1C and < 0x20))
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
        uint yardType = Research.Buildable(game, Owner.Empire, 0x2A).First(r => Family.Of(r.Common.Ref) == 0x2A).Common.Ref;
        var own = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).ToList();
        int wanted = ai.YardTarget(own.Count);
        for (int i = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Existing && Family.Of(f.Type) == 0x2A); i < wanted + 1; i++)
            Objects.Create(game, yardType, Owner.Empire, own[i % own.Count], completed: true);
        game.Clock.Flush(game);
        var side = game.Sides[Owner.Empire];
        Assert.True(4 * (side.MaintenanceCapacity - side.MaintenanceAllocated) > side.MaintenanceCapacity, "free maintenance not above 25 %");
        Assert.Contains(own, x => x.RawMaterials > x.UsedRaw && x.Energy > x.UsedEnergy);
        // an idle construction yard's choice
        var m = own.SelectMany(x => ScreenViews.Managers(x, Owner.Empire)).First(x => x.Kind == ManufacturingKind.Construction && Economy.Members(x).Any());
        var (facility, site) = ai.EconomyFirstPick(game, m, (StarSystem)m.System!, side.MaintenanceCapacity - side.MaintenanceAllocated, 0);
        output.WriteLine($"pick {facility?.Common.Ref:X8} at {site?.Ref:X8}");
        Assert.NotNull(facility);
        uint family = Family.Of(facility!.Common.Ref);
        Assert.True(family is 0x2C or 0x2D, $"picked {facility.Common.Ref:X8}");
        Assert.True(family == 0x2D ? site!.Energy > site.UsedEnergy : site!.RawMaterials > site.UsedRaw && site.Energy > site.UsedEnergy);
    }

    /// <summary>
    /// EconomyFirst yard ratio: with fewer construction yards than max(2, ceil(r x owned systems)) an idle yard's
    /// order is another construction yard; at the target it is not; the capital-ship gate of step 1b is gone (an idle shipyard
    /// still orders a capital ship while a construction yard finds nothing to build).
    /// </summary>
    [GameFact]
    public void EconomyFirstYardRatio()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = [OpenSWRController.V7.Fighters, OpenSWRController.V7.Watch, OpenSWRController.V7.LateEconomy, OpenSWRController.V7.EconomyFirst];
        game.Clock.Step(game);
        var own = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).ToList();
        int yards = game.Facilities.Count(f => f.Owner == Owner.Empire && f.Existing && Family.Of(f.Type) == 0x2A);
        var side = game.Sides[Owner.Empire];
        int free = side.MaintenanceCapacity - side.MaintenanceAllocated;
        var m = own.SelectMany(x => ScreenViews.Managers(x, Owner.Empire)).First(x => x.Kind == ManufacturingKind.Construction && Economy.Members(x).Any());
        ai.YardRatio = 1.0; // target = owned systems: short of yards
        Assert.True(yards < ai.YardTarget(own.Count));
        Assert.Equal(0x2Au, Family.Of(ai.EconomyFirstPick(game, m, (StarSystem)m.System!, free, 0).Facility!.Common.Ref));
        ai.YardRatio = 0.0; // target = the minimum 2: met
        Assert.Equal(OpenSWRController.MinConstructionYards, ai.YardTarget(own.Count));
        if (yards >= OpenSWRController.MinConstructionYards)
            Assert.NotEqual(0x2Au, Family.Of(ai.EconomyFirstPick(game, m, (StarSystem)m.System!, free, 0).Facility?.Common.Ref ?? 0));
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
        Assert.Contains(ai.IssueTrace.Skip(from), t => t.Result.Success && t.Command.Type == 0x211 && Family.IsCapitalShip(t.Command.Fields[0]));
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
        uint shipType = game.Definitions.CapitalShips.First(r => r.Alliance != 0 && Family.Of(r.Common.Ref) == 0x14).Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, at, completed: true);
        Objects.Create(game, shipType, Owner.Alliance, fleet, completed: true);
        game.Clock.Flush(game);
        Assert.True(Orders.Execute(game, new Command(0x214, Owner.Alliance) { Targets = [(yard, 0)], ArgRef = fleet }).Success);
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
                output.WriteLine($"day {game.Day}: {t.Command.Type:X} {(t.Command.Fields.Length > 0 ? t.Command.Fields[0] : 0):X8} -> {t.Command.ArgRef?.Ref:X8}");
                if (t.Command.Type == 0x214 && t.Command.ArgRef == at) back = true;
                if (t.Command.Type == 0x211 && Family.IsCapitalShip(t.Command.Fields[0])) ship = true;
            }
            from = ai.IssueTrace.Count;
        }
        Assert.True(back, "the target did not come back to the yard's system");
        Assert.True(ship, "no capital ship within two decisions");
    }

    /// <summary>
    /// Watch missions, the check first: an enemy fleet sent
    /// towards an own system is not in the side's views before; an own espionage on the own system (validated, run to its success)
    /// makes it visible there, Enroute, through the mode-10 update.
    /// </summary>
    [GameFact]
    public void WatchEspionageShowsIncomingFleets()
    {
        // the first day the incoming fleet shows in the side's views while still Enroute (-1: never), with and without the watch
        (int Seen, int Arrived, int Travel) Run(bool watch)
        {
            var (game, _) = AllianceGame(Owner.Empire);
            game.Controllers[Owner.Empire] = new HumanController(Owner.Empire); // quiet sides: only the test's orders
            game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
            game.Clock.Step(game);
            var type = OpenSWRController.TypeOf(game, 0x52)!;
            // an Empire character free, at an own system; the farthest Alliance fleet with a ship, sent to that system
            var spy = game.Characters.Where(c => c.Owner == Owner.Empire && c.Existing && !c.OnMission && !c.Captured && c.Injury == 0 && c.CommandKind == 0
                && Missions.Location(c) is StarSystem { Owner: Owner.Empire }).OrderByDescending(c => Missions.Enhanced(game, c, SkillIndex.Espionage)).First();
            var target = (StarSystem)Missions.Location(spy)!;
            var fleet = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Alliance && f.Usable && !f.Enroute
                && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing) && Orders.Speed(game, f) > 0)
                .OrderByDescending(f => Orders.TravelDays(game, (StarSystem)f.Container!, target, Orders.Speed(game, f))).First();
            int travel = Orders.TravelDays(game, (StarSystem)fleet.Container!, target, Orders.Speed(game, fleet));
            Fleets.Move(game, fleet, target);
            game.Clock.Step(game);
            Assert.True(fleet.Enroute);
            bool Shown() => new OpenSWRController.Known(game, Owner.Empire).Fleets(target, Owner.Alliance).Contains(fleet);
            Assert.False(Shown());
            if (watch)
            {
                Assert.Null(MissionEligibility.Validate(game, 0x240, Owner.Empire, type.Common.Ref, [spy], [], target));
                Assert.True(Orders.Execute(game, new Command(0x240, Owner.Empire) { Targets = [(spy, 0)], ArgRef = target, Fields = [0, 0, type.Common.Ref] }).Success);
            }
            int start = game.Day, seen = -1;
            while (fleet.Enroute && game.Day < start + 120)
            {
                game.Clock.Step(game);
                if (seen < 0 && fleet.Enroute && Shown()) seen = game.Day; // before the arrival only
            }
            return (seen, game.Day, travel);
        }
        var with = Run(true);
        var without = Run(false);
        output.WriteLine($"travel {with.Travel} days; with the watch shown from day {with.Seen} (arrival day {with.Arrived}); without: {without.Seen} (arrival day {without.Arrived})");
        Assert.True(with.Seen >= 0, "the incoming fleet never showed");
        Assert.Equal(-1, without.Seen);
    }

    /// <summary>
    /// Version 7 watch missions: over a game the side watches its own high-value border systems (at most 2 running, each system not
    /// again within 15 days); an enemy fleet sent towards a watched own system shows after the watch (mode 10, as the espionage's
    /// success post does); a holdable high-value system is reinforced, a weak one evacuated before the arrival.
    /// </summary>
    [GameFact]
    public void WatchHoldOrEvacuate()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Except([OpenSWRController.V7.CovertExperience, OpenSWRController.V7.Decoys, OpenSWRController.V7.Swarm]); // the game this test was written for (two Alliance fleets at day 200)
        int watchesStarted = 0;
        var started = new Dictionary<uint, int>();
        while (game.Day < 200 && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count;
            game.Clock.Step(game);
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240 && Family.Of(t.Command.Fields[2]) == 0x52).Select(t => t.Command))
                if (c.ArgRef is StarSystem { Owner: Owner.Empire } w)
                {
                    Assert.True(!started.TryGetValue(w.Ref, out int d) || game.Day - d >= OpenSWRController.WatchDays, $"day {game.Day}: {w.Ref:X8} watched again");
                    started[w.Ref] = game.Day;
                    watchesStarted++;
                }
            Assert.True(game.Missions.Count(m => m.Owner == Owner.Empire && m.Kind == 0x52 && m.CompletionStatus == 0 && m.TargetLocation is StarSystem { Owner: Owner.Empire }) <= OpenSWRController.MaxWatches);
        }
        output.WriteLine($"watches started {watchesStarted}");
        Assert.True(watchesStarted > 0);

        // incoming forces: the weakest Alliance fleet at a high-value own system with own strength, the strongest at a weak one
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var alliance = game.Systems.SelectMany(x => x.Contents.OfType<Fleet>()).Where(f => f.Owner == Owner.Alliance && f.Usable && !f.Enroute
            && f.Contents.Any(x => Family.IsCapitalShip(x.Type) && x.Existing) && Orders.Speed(game, f) > 0).ToList();
        int Real(GameObject f) => new OpenSWRController.Known(game, Owner.Alliance).Strength(f, Owner.Alliance);
        var weakest = alliance.OrderBy(Real).ThenBy(f => f.Key).First();
        var strongest = alliance.OrderByDescending(Real).ThenBy(f => f.Key).First(f => f != weakest);
        var own = game.Systems.Where(x => x.Owner == Owner.Empire && x.Populated).ToList();
        var held = own.Where(ai.HighValue).OrderByDescending(x => k.Strength(x, Owner.Empire)).ThenBy(x => x.Key).First();
        // the weak one: the own system with the most ready troops (more than 1), every other Alliance fleet sent there
        var weak = own.Where(x => x != held).OrderByDescending(x => x.Contents.Count(t => Family.IsTroop(t.Type) && t.Owner == Owner.Empire && t.Usable && !t.Enroute))
            .ThenBy(x => x.Key).First();
        Assert.True(weak.Contents.Count(t => Family.IsTroop(t.Type) && t.Owner == Owner.Empire && t.Usable && !t.Enroute) > 1);
        Fleets.Move(game, weakest, held);
        foreach (var f in alliance.Where(f => f != weakest)) Fleets.Move(game, f, weak);
        game.Clock.Step(game);
        Assert.True(weakest.Enroute && strongest.Enroute);
        bool Shown(Fleet f, StarSystem at) => new OpenSWRController.Known(game, Owner.Empire).Fleets(at, Owner.Alliance).Contains(f);
        // the watch's effect (WatchEspionageShowsIncomingFleets runs the real mission)
        Knowledge.Update(game, held, 10, Owner.Empire);
        Knowledge.Update(game, weak, 10, Owner.Empire);
        Assert.True(Shown(weakest, held) && Shown(strongest, weak));
        // the decision's watch part
        ai.DayDue();
        int start = ai.IssueTrace!.Count;
        k = new OpenSWRController.Known(game, Owner.Empire);
        ai.RefreshValues(game);
        ai.RefreshIncoming(game, k);
        ai.HoldOrEvacuate(game, k, []);
        var (targets, _) = ai.Defence(game, k);
        var x1 = ai.Incomings[held];
        var x2 = ai.Incomings[weak];
        output.WriteLine($"held {held.Ref:X8}: force {x1.Force} defence {x1.Defence} hold {x1.Hold}; weak {weak.Ref:X8}: force {x2.Force} defence {x2.Defence} hold {x2.Hold}");
        var issued = ai.IssueTrace.Skip(start).Where(t => t.Result.Success).Select(t => t.Command).ToList();
        foreach (var c in issued.Where(c => c.Type == 0x201)) output.WriteLine($"  move {c.Targets[0].Target.Ref:X8} from {Missions.Location(c.Targets[0].Target)?.Ref:X8} to {c.ArgRef?.Ref:X8}");
        Assert.False(x2.Hold);
        Assert.Contains(issued, c => c.Type == 0x201 && c.Targets.Any(t => t.Target.Container == weak || t.Target.Container?.Container == weak) && c.ArgRef != weak);
        Assert.True(x1.Hold);
        Assert.True(targets.TryGetValue(held, out int target) && target >= x1.Troops + 1 && targets.Keys.First() == held);
        Assert.DoesNotContain(weak, targets.Keys);
    }

    /// <summary>
    /// Version 7 battle prediction: the predictor changes neither the game state nor the game generator (save and generator state
    /// equal before and after); it is deterministic (a fresh controller gives the same numbers); own fighters against a fleet
    /// without lasers predict better than the plain value sum (which says they lose); BattleFloat withdraws from a known superior
    /// force.
    /// </summary>
    [GameFact]
    public void BattlePrediction()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Add(OpenSWRController.V7.PredictBattle); // off by default: BattleFloat through the predictor
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        game.Clock.Step(game);
        int Sd(uint type, int offset) => (int)game.Definitions.Resolve(type)!.U32At(offset);
        int Lasers(uint t) => new[] { 76, 112, 88, 100 }.Sum(a => Sd(t, a + 8));
        int Heavy(uint t) => new[] { 76, 112, 88, 100 }.Sum(a => Sd(t, a) + Sd(t, a + 4));
        var noLaser = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < 0x18 && Lasers(r.Common.Ref) == 0 && Heavy(r.Common.Ref) > 0)
            .OrderByDescending(r => r.Hull).First().Common.Ref;
        var fighterType = game.Definitions.Fighters.Where(r => r.Empire != 0).OrderByDescending(r => r.TotalAttackStrength).First().Common.Ref;
        var k0 = new OpenSWRController.Known(game, Owner.Empire);
        int vShip = k0.Value(noLaser), vFighter = k0.Value(fighterType);
        var systems = game.Systems.Where(x => x.Owner == Owner.Empire && !x.Contents.OfType<Fleet>().Any(f => f.Usable)).OrderBy(x => x.Key).ToList();
        var s = systems[0];
        // the enemy: one fleet with two ships without lasers; own: fighters directly in the system, value sum below the enemy's
        var enemyFleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, s, completed: true);
        for (int i = 0; i < 2; i++) Objects.Create(game, noLaser, Owner.Alliance, enemyFleet, completed: true);
        const int n = 24; // two carriers' load
        for (int i = 0; i < n; i++) Objects.Create(game, fighterType, Owner.Empire, s, completed: true);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        Assert.Contains(enemyFleet, k.Fleets(s, Owner.Alliance));

        var before = Save(game);
        int draws = game.Random.State;
        var p = ai.PredictSpace(game, k, s, [], k.Fleets(s, Owner.Alliance));
        double ground = ai.PredictGround(game, s, [Refs.AllianceArmy, Refs.AllianceArmy, Refs.AllianceArmy], [Refs.EmpireArmy], []);
        Assert.Equal(draws, game.Random.State);
        Assert.Equal(Text(before), Text(Save(game)));
        var again = new OpenSWRController(Owner.Empire);
        again.RefreshValues(game);
        Assert.Equal(p, again.PredictSpace(game, new OpenSWRController.Known(game, Owner.Empire), s, [], k.Fleets(s, Owner.Alliance)));
        Assert.Equal(ground, again.PredictGround(game, s, [Refs.AllianceArmy, Refs.AllianceArmy, Refs.AllianceArmy], [Refs.EmpireArmy], []));
        output.WriteLine($"{n} fighters {fighterType:X8} (value {vFighter} each) against 2 × {noLaser:X8} (value {vShip}): win {p.Win:F2}, own lost {p.OwnLost:F0}, enemy lost {p.EnemyLost:F0}; ground (3 against 1) {ground:F2}");
        Assert.True(n * vFighter < 2 * vShip); // the value sum says: lose
        // the composition: the fighters take more enemy value than their own, and more than they lose
        Assert.True(p.EnemyLost > n * vFighter && p.EnemyLost > p.OwnLost, "the predictor does not see the fighters' edge");

        // BattleFloat: one own cheap ship against a known strong enemy fleet
        var s2 = game.Systems.Where(x => x.Owner == Owner.Empire && x != s).OrderBy(x => x.Key).First();
        var weakType = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < 0x18 && r.Empire != 0).OrderBy(r => r.Hull).First().Common.Ref;
        var strongType = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < 0x18 && r.Alliance != 0).OrderByDescending(r => r.Hull).First().Common.Ref;
        var mine = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, s2, completed: true);
        Objects.Create(game, weakType, Owner.Empire, mine, completed: true);
        var theirs = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Alliance, s2, completed: true);
        for (int i = 0; i < 4; i++) Objects.Create(game, strongType, Owner.Alliance, theirs, completed: true);
        game.Clock.Flush(game);
        Assert.Equal(10000.0, ai.BattleFloat(game, s2));
    }

    /// <summary>
    /// Version 6 uprising diplomacy: every own populated system below the safe share may get diplomacy, highest value first; a
    /// diplomacy at a high-value own system below the safe share is not recalled.
    /// </summary>
    [GameFact]
    public void UprisingDiplomacyByValue()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        int outsideHome = 0, highKept = 0;
        while (game.Day < 300 && !game.GameOver)
        {
            int from = ai.IssueTrace!.Count, decided = ai.Decided;
            game.Clock.Step(game);
            if (ai.Decided == decided) continue;
            var home = OpenSWRController.HomeSector(game, Owner.Empire);
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x240 && t.Command.Fields[2] == 0x51).Select(t => t.Command))
                if (c.ArgRef is StarSystem { Owner: Owner.Empire } s && s.Sector != home) outsideHome++;
            highKept += game.Missions.Count(m => m.Owner == Owner.Empire && m.Kind == 0x51 && m.Objective is StarSystem { Owner: Owner.Empire } s2
                && ai.HighValue(s2) && s2.SupportFor(Owner.Empire) < OpenSWRController.SafeShare);
            // never a recall of a high-value own system's diplomacy below the safe share
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == 0x250).Select(t => t.Command))
                if (game.Missions.FirstOrDefault(m => m.Ref == c.ArgRef?.Ref) is { Kind: 0x51, Objective: StarSystem { Owner: Owner.Empire } s3 })
                    Assert.False(ai.HighValue(s3) && s3.SupportFor(Owner.Empire) < OpenSWRController.SafeShare, $"day {game.Day}: recall at {s3.Ref:X8}");
        }
        output.WriteLine($"diplomacy at own systems outside the home sector {outsideHome}, high-value low-share diplomacy-days {highKept}");
    }

    /// <summary>
    /// Version 4 foil marks: after a foil at a system every covert estimate there uses a foil chance of at least 0.5, a second
    /// foil raises it to 0.65, and the mark ends 60 days after the last foil.
    /// </summary>
    [GameFact]
    public void FoilMarkRaisesTheEstimate()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        game.Clock.Step(game);
        var s = game.Systems.First(x => x.Owner == Owner.Alliance);
        var team = new List<Agent> { game.Characters.First(c => c.Owner == Owner.Empire && c.Existing) };
        var k = new OpenSWRController.Known(game, Owner.Empire);
        double Foil() => OpenSWRController.FoilChance(game, team, ai.KnownAt(game, k, s, true, null));
        double before = Foil();
        int day = game.Day;
        ai.MarkFoil(s, day);
        Assert.Equal(0.5, ai.FoilMark(s, day), 6);
        Assert.True(Foil() >= 0.5 && Foil() >= before);
        Assert.True(OpenSWRController.FoilChance(game, team, ai.KnownAt(game, k, s, false, null)) >= 0.5); // any objective at s
        ai.MarkFoil(s, day + 5);
        Assert.Equal(0.65, ai.FoilMark(s, day + 5), 6);
        Assert.Equal(0.65, ai.FoilMark(s, day + 5 + OpenSWRController.FoilMarkDays - 1), 6);
        Assert.Equal(0, ai.FoilMark(s, day + 5 + OpenSWRController.FoilMarkDays));
        for (int i = 0; i < 6; i++) ai.MarkFoil(s, day + 6 + i);
        Assert.Equal(0.9, ai.FoilMark(s, day + 12), 6); // capped
    }

    /// <summary>Foil marks appear in a real game (seed 532947196, openswr Alliance) and save -> load -> save stays byte-equal with them.</summary>
    [GameFact]
    public void FoilMarksSaveByteEqual()
    {
        var (game, ai) = AllianceGame();
        ai.Features = [OpenSWRController.V7.Fighters, OpenSWRController.V7.Watch, OpenSWRController.V7.LateEconomy]; // the game with a foil by day 400
        while (game.Day < 400 && !game.GameOver && !ai.Save().ContainsKey("foilMarks")) game.Clock.Step(game);
        Assert.True(ai.Save().ContainsKey("foilMarks"), "no foil by day 400");
        output.WriteLine($"day {game.Day}: {ai.Save()["foilMarks"]!.ToJsonString()}");
        var first = Save(game);
        var loaded = Load(first);
        Assert.Equal(Text(first), Text(Save(loaded)));
        Assert.Equal(ai.Save().ToJsonString(), ((OpenSWRController)loaded.Controllers[Owner.Alliance]).Save().ToJsonString());
    }

    /// <summary>
    /// Version 3 minimum chance: no diplomacy or subdue starts below MinChance (the member's attempt chance at the decision, from
    /// the side's view of the target); recalls (0x250) of low-chance missions pass validation.
    /// </summary>
    [GameFact]
    public void DiplomacyAndSubdueOnlyAtTheMinimumChance()
    {
        int started = 0, recalls = 0, recallable = 0;
        foreach (var side in new[] { Owner.Alliance, Owner.Empire })
        {
            var (game, ai) = AllianceGame(side);
            while (game.Day < 250 && !game.GameOver)
            {
                int from = ai.IssueTrace!.Count;
                game.Clock.Step(game);
                // the recall order exists for a repeating mission between attempts (phase 8) and is refused otherwise
                foreach (var m in game.Missions.Where(m => m.Owner == side && m.Kind == 0x51 && m.CompletionStatus == 0))
                {
                    var result = Orders.Validate(game, new Command(0x250, side) { Targets = [(m, 0)] });
                    Assert.Equal(m.Phase == 8, result.Success);
                    if (result.Success) recallable++;
                }
                var k = new OpenSWRController.Known(game, side);
                foreach (var (c, r) in ai.IssueTrace.Skip(from))
                {
                    if (c.Type == 0x250) { recalls++; Assert.True(r.Success); }
                    if (c.Type != 0x240 || !r.Success || Kind(c) is not (0x51 or 0x57)) continue;
                    started++;
                    int chance = ai.Chance(game, k, Kind(c), (Agent)c.Targets[0].Target, (StarSystem)c.ArgRef!);
                    Assert.True(chance >= OpenSWRController.MinChance, $"day {game.Day}: {Kind(c):X2} at {c.ArgRef!.Ref:X8} chance {chance}");
                }
            }
        }
        output.WriteLine($"diplomacy / subdue started {started}, recalls {recalls}, recallable checks {recallable}");
        Assert.True(started > 0);
        Assert.True(recallable > 0);
    }

    /// <summary>
    /// Rescues go only to captives seen arrived (not Enroute, at a system), at most 2 rescue missions per captive within 30 days
    /// (a foil holds the captive back for CovertRetryDays), and a major's rescue goes right after "hold home" in its decision.
    /// </summary>
    [GameFact]
    public void RescuesArrivedCaptivesWithBackOff()
    {
        // version 7: the first of these seeds whose game has rescues within 400 days (the AI's changes move captures between games)
        List<(int Day, List<(uint Kind, GameObject? Target, Seen? Captive)> Missions)> days = [];
        List<(int Day, GameObject? Target, Seen? Captive)> rescues = [];
        foreach (int seed in new[] { DefaultSeed, 445898697, 401859171, 874334749, 12345, 99991 })
        {
            days = RescueGame(400, seed);
            rescues = [.. days.SelectMany(d => d.Missions.Where(m => m.Kind == 0x61).Select(m => (d.Day, m.Target, m.Captive)))];
            output.WriteLine($"seed {seed}: rescues {rescues.Count}");
            if (rescues.Count > 0) break;
        }
        foreach (var (day, target, captive) in rescues) output.WriteLine($"day {day}: rescue {target!.Ref:X8} view enroute {captive?.Enroute} at {captive?.Location?.Ref:X8}");
        Assert.NotEmpty(rescues);
        Assert.All(rescues, r => Assert.True(r.Captive is { Captured: true, Enroute: false, Location: StarSystem }));
        foreach (var g in rescues.GroupBy(r => r.Target))
            foreach (var r in g) Assert.InRange(g.Count(x => x.Day >= r.Day && x.Day < r.Day + 30), 1, 2);
        // the jump: in each decision with a major's rescue, only subdue ("hold home") comes before it
        foreach (var (day, missions) in days.Where(d => d.Missions.Any(m => m.Kind == 0x61 && Family.Of(m.Target!.Type) is >= 0x30 and < 0x38)))
        {
            int rescue = missions.FindIndex(m => m.Kind == 0x61 && Family.Of(m.Target!.Type) is >= 0x30 and < 0x38);
            Assert.All(missions.Take(rescue), m => Assert.Equal(0x57u, m.Kind));
        }
    }

    /// <summary>A save from before the `seen` field (removed from the text) loads, and the AI goes on.</summary>
    [GameFact]
    public void OldSaveWithoutSeenLoads()
    {
        var game = NewGame(874334749, Owner.Empire);
        while (game.Day < 20 && !game.GameOver) game.Clock.Step(game);
        string text = System.Text.Encoding.UTF8.GetString(Save(game));
        string old = Regex.Replace(text, ",\"seen\":\\[(\\[[0-9]+,[0-9]+\\],?)*\\]", "");
        Assert.NotEqual(text, old);
        var loaded = Load(System.Text.Encoding.UTF8.GetBytes(old));
        Assert.IsType<OpenSWRController>(loaded.Controllers[Owner.Empire]);
        while (loaded.Day < 30 && !loaded.GameOver) loaded.Clock.Step(loaded);
    }

    /// <summary>Save -> load -> save byte-equal after chat lines were posted (the chat state is saved).</summary>
    [GameFact]
    public void SaveAndLoadWithChatLines()
    {
        var (game, _) = AgainstHuman(874334749, Owner.Empire);
        while (game.Day < 150 && !game.GameOver) game.Clock.Step(game);
        Assert.Contains(game.Log, m => m.Category == MessageCategory.Chat);
        var first = Save(game);
        var loaded = Load(first);
        Assert.Equal(Text(first), Text(Save(loaded)));
        ((OpenSWRController)loaded.Controllers[Owner.Empire]).Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        while (loaded.Day < 200 && !loaded.GameOver) loaded.Clock.Step(loaded);
        Assert.Equal(Text(Save(loaded)), Text(Save(Load(Save(loaded)))));
    }

    /// <summary>A chat line names a system (and gives a go-to) only when the human side sees it at that moment.</summary>
    [GameFact]
    public void ChatNamesOnlySeenSystems()
    {
        foreach (var openswr in new[] { Owner.Empire, Owner.Alliance })
        {
            var (game, ai) = AgainstHuman(874334749, openswr);
            ai.Names = c => "NAMED";
            int seen = 0;
            while (game.Day < 300 && !game.GameOver)
            {
                game.Clock.Step(game);
                foreach (var m in game.Log.Where(m => m.Category == MessageCategory.Chat && m.Id > seen).ToList())
                {
                    seen = m.Id;
                    output.WriteLine($"{openswr} day {m.Day}: {m.Text}");
                    if (m.Text!.Contains("NAMED")) Assert.True(ScreenViews.Sees(game.Systems.First(x => x.Ref == m.Subject), Other(openswr)));
                    else Assert.Equal(0u, m.Subject);
                }
            }
        }
    }

    /// <summary>No chat line when an own system goes neutral; a line when the human side takes one.</summary>
    [GameFact]
    public void NoChatLineForNeutralOwnerChanges()
    {
        var (game, ai) = AgainstHuman(874334749, Owner.Empire);
        game.Clock.Step(game);
        ai.OwnerChanges(game); // baseline
        var own = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).ToList();
        Systems.StoreControl(game, own[0], own[0].Control with { Owner = Owner.Neutral });
        Assert.Equal(Owner.Neutral, own[0].Owner);
        ai.OwnerChanges(game);
        Assert.DoesNotContain(game.Log, m => m.Category == MessageCategory.Chat);
        Systems.StoreControl(game, own[1], own[1].Control with { Owner = Owner.Alliance });
        Assert.Equal(Owner.Alliance, own[1].Owner);
        ai.OwnerChanges(game);
        Assert.Single(game.Log, m => m.Category == MessageCategory.Chat);
    }

    /// <summary>Chat lines go to a human opponent only, at most one per cooldown; none against the original AI.</summary>
    [GameFact]
    public void ChatLinesHaveACooldown()
    {
        var game = NewGame(874334749, Owner.Empire);
        ((OpenSWRController)game.Controllers[Owner.Empire]).Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 300 && !game.GameOver) game.Clock.Step(game);
        var chat = game.Log.Where(m => m.Category == MessageCategory.Chat).OrderBy(m => m.Id).ToList();
        foreach (var m in chat) output.WriteLine($"day {m.Day}: {m.Text}");
        Assert.NotEmpty(chat);
        Assert.All(chat.Zip(chat.Skip(1)), p => Assert.True(p.Second.Day - p.First.Day >= OpenSWRController.TauntCooldownDays));
        Assert.All(chat, m => Assert.DoesNotContain("{system}", m.Text));

        var against = NewGame(874334749, Owner.Empire);
        ((OpenSWRController)against.Controllers[Owner.Empire]).Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        while (against.Day < 100 && !against.GameOver) against.Clock.Step(against);
        Assert.DoesNotContain(against.Log, m => m.Category == MessageCategory.Chat);
    }

    /// <summary>The repo's taunt file (godot/Data/openswr-taunts.json), found from the test binaries upwards.</summary>
    static string TauntFile
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "OpenSWR.sln"))) return Path.Combine(dir.FullName, "godot", "Data", "openswr-taunts.json");
            throw new FileNotFoundException("OpenSWR.sln not found above the test binaries.");
        }
    }

    [Fact]
    public void TauntFileHasLinesForEveryOccasion()
    {
        var taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        foreach (var occasion in Enum.GetValues<OpenSWRTaunts.Occasion>())
        {
            Assert.NotEmpty(taunts.Lines(occasion));
            Assert.All(taunts.Lines(occasion), line => Assert.Contains("{system}", line));
        }
    }

    [Fact]
    public void TauntParser()
    {
        Assert.Throws<InvalidDataException>(() => OpenSWRTaunts.Parse("{ \"battleWon\": [ "));
        Assert.Throws<InvalidDataException>(() => OpenSWRTaunts.Parse("[\"a\"]"));
        // missing occasion, empty list, unknown key, non-string entries: silent occasions, nothing thrown
        var t = OpenSWRTaunts.Parse("{ \"battleWon\": [\"Won at {system}.\", 3, null], \"battleLost\": [], \"systemLost\": \"no list\", \"extra\": [\"x\"] }");
        Assert.Equal(["Won at {system}."], t.Lines(OpenSWRTaunts.Occasion.BattleWon));
        Assert.Empty(t.Lines(OpenSWRTaunts.Occasion.BattleLost));
        Assert.Empty(t.Lines(OpenSWRTaunts.Occasion.SystemLost));
        Assert.Empty(t.Lines(OpenSWRTaunts.Occasion.AttackLaunched));
    }

    /// <summary>Saved line counters beyond a shorter list after the file changed: they wrap, no crash; no lines: no messages.</summary>
    [GameFact]
    public void TauntCountersSurviveAFileChange()
    {
        var game = NewGame(874334749, Owner.Empire);
        var ai = (OpenSWRController)game.Controllers[Owner.Empire];
        ai.Taunts = OpenSWRTaunts.Parse(File.ReadAllText(TauntFile));
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance) { Rand = _ => 0 };
        while (game.Day < 100 && !game.GameOver) game.Clock.Step(game);
        int before = game.Log.Count(m => m.Category == MessageCategory.Chat);
        var loaded = Load(Save(game));
        var b = (OpenSWRController)loaded.Controllers[Owner.Empire];
        b.Taunts = OpenSWRTaunts.Parse("{ \"battleWon\": [\"W {system}\"], \"battleLost\": [\"L {system}\"], \"systemTaken\": [\"T {system}\"], \"systemLost\": [\"S {system}\"], \"attackLaunched\": [\"A {system}\"] }");
        int resume = loaded.Day;
        while (loaded.Day < resume + OpenSWRController.TauntCooldownDays && !loaded.GameOver) loaded.Clock.Step(loaded); // past the cooldown
        b.OwnerChanges(loaded);
        var lost = loaded.Systems.First(x => x.Owner == Owner.Empire);
        Systems.StoreControl(loaded, lost, lost.Control with { Owner = Owner.Alliance }); // an own system lost to the human side
        b.OwnerChanges(loaded);
        var after = loaded.Log.Where(m => m.Category == MessageCategory.Chat).Skip(before).ToList();
        Assert.NotEmpty(after);
        Assert.All(after, m => Assert.Matches("^[WLTSA] ", m.Text));

        var silent = Load(Save(game)); // Taunts not set: no chat lines
        while (silent.Day < 300 && !silent.GameOver) silent.Clock.Step(silent);
        Assert.Equal(before, silent.Log.Count(m => m.Category == MessageCategory.Chat));
    }

    [GameFact]
    public void OriginalChoiceIsNotSaved()
    {
        var game = Sim.NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        Assert.DoesNotContain("opponent", System.Text.Encoding.UTF8.GetString(Save(game)));
    }

    [GameFact]
    public void UnknownControllerKindIsAnError()
    {
        var game = NewGame(532947196, Owner.Alliance);
        game.Clock.Step(game);
        string text = System.Text.Encoding.UTF8.GetString(Save(game));
        Assert.Contains("\"kind\":4", text);
        var bad = System.Text.Encoding.UTF8.GetBytes(text.Replace("\"kind\":4", "\"kind\":9"));
        Assert.Throws<InvalidDataException>(() => Load(bad));
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
