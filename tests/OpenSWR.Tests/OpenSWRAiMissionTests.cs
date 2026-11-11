using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Mission choice: groups, fleets, scouting, diplomacy, covert operations and troops.</summary>
public class OpenSWRAiMissionTests(ITestOutputHelper output) : OpenSWRAiTests(output)
{
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
        Assert.True(ai.Refused(new Command(CommandType.Move, Owner.Empire) { Targets = [.. fleets.Select(f => ((GameObject)f, GroupMode.Object))], ArgRef = target })
            || !launched, "the group order is refused (Enroute fleet) or nothing launched");
        Assert.DoesNotContain(issued, c => c.Targets.Count > 1);
        Assert.DoesNotContain(issued, c => c.Targets.Any(t => t.Target == bad));
        Assert.False(ai.Refused(new Command(CommandType.Move, Owner.Empire) { Targets = [(fleets[0], 0)], ArgRef = target }));
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

    /// <summary>Scouting starts in the home sector (Coruscant's for the Empire) and goes out by ring; never more than 2 scouts at a time.</summary>
    [GameFact]
    public void ScoutsHomeSectorFirstAndAtMostTwo()
    {
        var seen = new Dictionary<Mission, (StarSystem Target, int Day)>();
        var days = MissionDays(874334749, 60, seen);
        // version 7: espionage on own systems is the watch, not scouting by ring
        var scouts = days.SelectMany(d => d.Missions.Where(c => Kind(c) is MissionKind.Espionage or MissionKind.Reconnaissance && ((StarSystem)c.ArgRef!).Owner != Owner.Empire).Select(c => (d.Day, d.Map, Target: (StarSystem)c.ArgRef!))).ToList();
        foreach (var (day, map, target) in scouts) output.WriteLine($"day {day}: scout {Family.Of(target.Ref)}:{SdRef.IndexOf(target.Ref)} ring {map.RingOf(target)} (home {map.Home?.Ref})");
        Assert.NotEmpty(scouts);
        // the first character scout goes to the home sector (version 7: the watches come first and can take the characters, so a
        // scouting special force may reach an unexplored outer system before it)
        var first = days.SelectMany(d => d.Missions.Where(c => Kind(c) == MissionKind.Espionage && c.Targets[0].Target is Character && ((StarSystem)c.ArgRef!).Owner != Owner.Empire)
            .Select(c => (d.Map, Target: (StarSystem)c.ArgRef!))).First();
        Assert.Equal(0, first.Map.RingOf(first.Target));
        Assert.All(scouts.Zip(scouts.Skip(1)).Where(p => p.First.Day == p.Second.Day), p => Assert.True(p.First.Map.RingOf(p.First.Target) <= p.Second.Map.RingOf(p.Second.Target)));
        Assert.All(days, d => Assert.InRange(d.Scouting, 0, OpenSWRController.MaxScouts));
        Assert.All(days, d => Assert.InRange(d.Missions.Count(c => Kind(c) is MissionKind.Espionage or MissionKind.Reconnaissance && c.Targets[0].Target is Character), 0, OpenSWRController.MaxScouts));
        // a successful scouting counts as a sighting: no system is scouted again within ScoutAgainDays of it (a failed one, as
        // 90000104's day-1 espionage since the issue-1 fix, is no sighting)
        foreach (var (day, _, target) in scouts)
            foreach (int success in seen.Values.Where(e => e.Target == target && e.Day <= day).Select(e => e.Day))
                Assert.True(day - success >=OpenSWRController.ScoutAgainDays, $"{Family.Of(target.Ref)}:{SdRef.IndexOf(target.Ref)} seen on day {success}, scouted again on day {day}");
        Assert.NotEmpty(seen);
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
        var targets = ai.IssueTrace!.Skip(from).Where(t => t.Result.Success && Kind(t.Command) == MissionKind.Diplomacy).Select(t => (StarSystem)t.Command.ArgRef!).ToList();
        // version 6: own systems below the safe share (any sector) come with the home sector
        int Rank(StarSystem s) => s.Sector == home || Knowledge.See(game, s, Owner.Empire).Owner == Owner.Empire ? 0 : map.PowerBase.Contains(s.Sector) ? 1 : map.Frontier.Contains(s.Sector) ? 2 : 3;
        foreach (var s in targets) output.WriteLine($"diplomacy {Family.Of(s.Ref)}:{SdRef.IndexOf(s.Ref)} rank {Rank(s)}");
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
        var minor = placed.Where(c => Family.Of(c.Type) is >= Families.MinorCharacter and < Families.SpecialForce).OrderByDescending(c => c.Skills[SkillIndex.Combat] + c.Skills[SkillIndex.Espionage]).First();
        var major = placed.First(c => Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter);
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
        Assert.True(OpenSWRController.Worth(game, MissionKind.Rescue, team, p, 15, heavy) < 0); // a heavily guarded minor captive (value 15): no rescue
        Assert.True(OpenSWRController.Worth(game, MissionKind.Rescue, team, p, 100, light) > 0); // a lightly guarded major captive: rescue
        // the formula: p_success * value - foil * lost * loss; a plain failure costs nothing
        double success = (1 - lightFoil) * p / 100.0;
        Assert.Equal(success * 100 - lightFoil * lightLost * minorValue, OpenSWRController.Worth(game, MissionKind.Rescue, team, p, 100, light), 6);
        Assert.Equal(0.4 * 40, OpenSWRController.Worth(game, MissionKind.JediTraining, team, 40, 40, heavy), 6); // Jedi training: no encounter, no loss
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
        output.WriteLine($"median {Family.Of(seen.Ref)}:{SdRef.IndexOf(seen.Ref)}: phase 7 {own.Phase7.Count}, phase 9 {own.Phase9.Count}");
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
                var running = game.Missions.Where(m => m.Owner == side && !m.Deleted && m.CompletionStatus == MissionCompletion.None).ToList();
                Assert.InRange(running.Count(m => m.Kind is MissionKind.InciteUprising or MissionKind.JediTraining or MissionKind.Rescue or MissionKind.Abduction or MissionKind.Assassination or MissionKind.Sabotage), 0, OpenSWRController.MaxCovert);
                Assert.InRange(running.Count(m => m.Kind == MissionKind.JediTraining), 0, 1);
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
            var running = game.Missions.Where(m => m.Owner == Owner.Empire && !m.Deleted && m.CompletionStatus == MissionCompletion.None && m.Kind >= MissionKind.Diplomacy).ToList();
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            // still running at the decision (a mission that ended during the day frees its target: version 7 watches end and repeat)
            var before = running.Where(m => m.CompletionStatus == MissionCompletion.None).Select(m => m.Objective).ToHashSet();
            var issued = ai.IssueTrace!.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.CreateMission).Select(t => t.Command).ToList();
            var members = issued.SelectMany(c => c.Targets.Select(t => t.Target)).ToList();
            Assert.Equal(members.Count, members.Distinct().Count());
            // recruitment (version 9 Recruit, in the default) goes to the recruiter's own system: several recruiters may share it
            var targetedOnce = issued.Where(c => Kind(c) != MissionKind.Recruitment).ToList();
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
        uint espionage = game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.Espionage).Common.Ref;
        uint diplomacy = game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.Diplomacy).Common.Ref;
        var refused = new Command(CommandType.CreateMission, Owner.Empire) { Targets = [(c, GroupMode.Object)], ArgRef = c, Fields = [0, 0, espionage] }; // a character is no espionage target
        ai.DayDue();
        Assert.False(ai.Issue(game, refused, []));
        int traced = ai.IssueTrace!.Count;
        Assert.True(ai.Refused(refused));
        Assert.False(ai.Issue(game, new Command(CommandType.CreateMission, Owner.Empire) { Targets = [(c, GroupMode.Object)], ArgRef = c, Fields = [0, 0, espionage] }, []));
        Assert.Equal(traced, ai.IssueTrace!.Count); // not validated again
        Assert.False(ai.Refused(new Command(CommandType.CreateMission, Owner.Empire) { Targets = [(c, GroupMode.Object)], ArgRef = c, Fields = [0, 0, diplomacy] }));
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
        var recon = game.Definitions.SpecialForces.Where(f => f.Empire != 0 && game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.Reconnaissance).AllowsSpecialForce(f.MissionMask))
            .Select(f => f.Common.Ref).ToHashSet();
        Assert.NotEmpty(recon);
        // the side starts with such units: remove them, so the AI has to build its own
        foreach (var unit in game.Created.Where(o => o.Owner == side && o.Existing && recon.Contains(o.Type)).ToList()) Objects.Destroy(game, unit, DestroyedReason.Scrapped);
        while (game.Clock.DispatchOne(game, commands: false)) { }
        int built = 0, sent = 0;
        while (game.Day < 150 && !game.GameOver)
        {
            int day = game.Day, from = ai.IssueTrace!.Count;
            var map = ai.Sectors(game);
            var explored = game.Systems.ToDictionary(s => s, s => Knowledge.See(game, s, side).Explored);
            while (game.Day == day && !game.GameOver) game.Clock.Step(game);
            var issued = ai.IssueTrace.Skip(from).Where(t => t.Result.Success).Select(t => t.Command).ToList();
            built += issued.Where(c => c.Type == CommandType.ProduceTroop && recon.Contains(c.Fields[0])).Sum(c => (int)c.Fields[1]);
            var scouting = issued.Where(c => c.Type == CommandType.CreateMission && Kind(c) == MissionKind.Reconnaissance).Select(c => (StarSystem)c.ArgRef!).ToList();
            sent += scouting.Count;
            foreach (var s in scouting) output.WriteLine($"day {day}: reconnaissance {Family.Of(s.Ref)}:{SdRef.IndexOf(s.Ref)} ring {map.RingOf(s)}");
            Assert.All(scouting, s => Assert.False(explored[s]));
            Assert.All(scouting.Zip(scouting.Skip(1)), p => Assert.True(map.RingOf(p.First) <= map.RingOf(p.Second)));
            Assert.All(issued.Where(c => c.Type == CommandType.CreateMission && Kind(c) == MissionKind.Espionage), c => Assert.True(explored[(StarSystem)c.ArgRef!])); // characters: explored only
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
        Assert.DoesNotContain(game.Facilities, f => f.Owner == Owner.Alliance && f.Existing && Family.Of(f.Type) == Families.TrainingFacility);
        var recon = game.Definitions.SpecialForces.Where(f => f.Alliance != 0 && game.Definitions.Missions.First(r => Family.Of(r.Common.Ref) == (uint)MissionKind.Reconnaissance).AllowsSpecialForce(f.MissionMask))
            .Select(f => f.Common.Ref).ToHashSet();
        // its starting scouting units are removed, so that it needs new ones
        foreach (var unit in game.Created.Where(o => o.Owner == Owner.Alliance && o.Existing && recon.Contains(o.Type)).ToList()) Objects.Destroy(game, unit, DestroyedReason.Scrapped);
        while (game.Clock.DispatchOne(game, commands: false)) { }
        int ordered = -1, scouts = -1, taken = -1;
        while (game.Day < 250 && !game.GameOver && scouts < 0)
        {
            int from = ai.IssueTrace!.Count;
            game.Clock.Step(game);
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success).Select(t => t.Command))
            {
                // once one stands, more come from "production facilities first" (version 6), not from the version 4 rule
                if (c.Type == CommandType.ProduceFacility && Family.Of(c.Fields[0]) == Families.TrainingFacility && !game.Facilities.Any(f => f.Owner == Owner.Alliance && f.Usable && Family.Of(f.Type) == Families.TrainingFacility))
                {
                    Assert.Equal(-1, ordered); // one at a time: no second while the first is built, travels or stands
                    // and none while it holds one (taken with a system: version 7 may lose that system again, then it orders one)
                    Assert.DoesNotContain(game.Facilities, f => f.Owner == Owner.Alliance && f.Existing && f.Completed && Family.Of(f.Type) == Families.TrainingFacility);
                    ordered = game.Day;
                }
                if (c.Type == CommandType.ProduceTroop && recon.Contains(c.Fields[0])) scouts = game.Day;
            }
            if (ordered < 0 && taken < 0 && game.Facilities.Any(f => f.Owner == Owner.Alliance && f.Existing && Family.Of(f.Type) == Families.TrainingFacility)) taken = game.Day;
            if (ordered >= 0 && !game.Facilities.Any(f => f.Owner == Owner.Alliance && f.Usable && Family.Of(f.Type) == Families.TrainingFacility))
                Assert.InRange(game.Facilities.Count(f => f.Owner == Owner.Alliance && f.Existing && Family.Of(f.Type) == Families.TrainingFacility), 0, 1);
        }
        output.WriteLine($"training facility ordered on day {ordered} (one taken with a system on day {taken}), scouting units ordered on day {scouts}");
        // ordered after the Refinery when manufacturing waits for refined material and once its maintenance fits; or a training facility
        // came with a captured system first (version 5 takes systems early in this seed), and then none is ordered
        if (taken < 0) Assert.InRange(ordered, 1, 60);
        Assert.True(scouts > Math.Max(ordered, taken));
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
            int troopIndex = issued.FindIndex(c => c.Type == CommandType.ProduceTroop && Family.IsTroop(c.Fields[0]));
            int shipIndex = issued.FindIndex(c => c.Type == CommandType.ProduceShip && Family.IsCapitalShip(c.Fields[0]));
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
    /// over a game never more than 3 groups, each with its own target; groups and losses survive save gives load gives save byte-equal.
    /// </summary>
    [GameFact]
    public void CounterAttackAndParallelGroups()
    {
        var (game, ai) = AllianceGame(Owner.Empire);
        ai.Features = ai.Features.Remove(OpenSWRController.V7.Swarm).Remove(OpenSWRController.V7.LeanCost); // the version 5 groups (Swarm replaces them) and the threat garrisons this game was written for
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
        output.WriteLine($"lost {Family.Of(lost.Ref)}:{SdRef.IndexOf(lost.Ref)}; targets {string.Join(",", ai.Groups.Select(g => g.Target.Ref.ToString()))}");
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
    /// foils of one kind within 90 days pause that kind for 90 days, two do not; the pause and foil counts survive save gives load gives save.
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
                Assert.True(success >= OpenSWRController.MinSuccess(x.Kind, x.Target), $"day {game.Day}: {(uint)x.Kind} at {Family.Of(x.Target.Ref)}:{SdRef.IndexOf(x.Target.Ref)} p_success {success:F2}");
                checkedCandidates++;
            }
        }
        output.WriteLine($"gated candidates checked {checkedCandidates}");
        // the type pause (a fresh controller: the game's own foils do not count here)
        var fresh = new OpenSWRController(Owner.Alliance);
        int day = 100;
        fresh.KindFoiled(MissionKind.Abduction, day);
        fresh.KindFoiled(MissionKind.Abduction, day + 10);
        Assert.False(fresh.Paused(MissionKind.Abduction, day + 10));
        fresh.KindFoiled(MissionKind.Assassination, day);
        fresh.KindFoiled(MissionKind.Assassination, day + OpenSWRController.PauseDays); // the first one is out of the window
        fresh.KindFoiled(MissionKind.Assassination, day + OpenSWRController.PauseDays + 1);
        Assert.False(fresh.Paused(MissionKind.Assassination, day + OpenSWRController.PauseDays + 1));
        fresh.KindFoiled(MissionKind.Abduction, day + 20);
        Assert.True(fresh.Paused(MissionKind.Abduction, day + 20));
        Assert.True(fresh.Paused(MissionKind.Abduction, day + 20 + OpenSWRController.PauseDays - 1));
        Assert.False(fresh.Paused(MissionKind.Abduction, day + 20 + OpenSWRController.PauseDays));
        for (int i = 0; i < OpenSWRController.PauseFoils; i++) ai.KindFoiled(MissionKind.Sabotage, game.Day); // a pause in the game's controller, for the save
        var first = Save(game);
        Assert.Contains("\"paused\":", System.Text.Encoding.UTF8.GetString(first));
        Assert.Equal(Text(first), Text(Save(Load(first))));
    }
}
