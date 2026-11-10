using System.Text.RegularExpressions;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Watching the enemy: espionage, battle prediction, uprisings, foil marks and captives.</summary>
public class OpenSWRAiWatchTests(ITestOutputHelper output) : OpenSWRAiTests(output)
{
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
            var type = OpenSWRController.TypeOf(game, MissionKind.Espionage)!;
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
                Assert.Null(MissionEligibility.Validate(game, CommandType.CreateMission, Owner.Empire, type.Common.Ref, [spy], [], target));
                Assert.True(Orders.Execute(game, new Command(CommandType.CreateMission, Owner.Empire) { Targets = [(spy, GroupMode.Object)], ArgRef = target, Fields = [0, 0, type.Common.Ref] }).Success);
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
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.CreateMission && Family.Of(t.Command.Fields[2]) == (uint)MissionKind.Espionage).Select(t => t.Command))
                if (c.ArgRef is StarSystem { Owner: Owner.Empire } w)
                {
                    Assert.True(!started.TryGetValue(w.Ref, out int d) || game.Day - d >= OpenSWRController.WatchDays, $"day {game.Day}: {Family.Of(w.Ref)}:{SdRef.IndexOf(w.Ref)} watched again");
                    started[w.Ref] = game.Day;
                    watchesStarted++;
                }
            Assert.True(game.Missions.Count(m => m.Owner == Owner.Empire && m.Kind == MissionKind.Espionage && m.CompletionStatus == MissionCompletion.None && m.TargetLocation is StarSystem { Owner: Owner.Empire }) <= OpenSWRController.MaxWatches);
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
        output.WriteLine($"held {Family.Of(held.Ref)}:{SdRef.IndexOf(held.Ref)}: force {x1.Force} defence {x1.Defence} hold {x1.Hold}; weak {Family.Of(weak.Ref)}:{SdRef.IndexOf(weak.Ref)}: force {x2.Force} defence {x2.Defence} hold {x2.Hold}");
        var issued = ai.IssueTrace.Skip(start).Where(t => t.Result.Success).Select(t => t.Command).ToList();
        foreach (var c in issued.Where(c => c.Type == CommandType.Move)) output.WriteLine($"  move {Family.Of(c.Targets[0].Target.Ref)}:{SdRef.IndexOf(c.Targets[0].Target.Ref)} from {Missions.Location(c.Targets[0].Target)?.Ref} to {c.ArgRef?.Ref}");
        Assert.False(x2.Hold);
        Assert.Contains(issued, c => c.Type == CommandType.Move && c.Targets.Any(t => t.Target.Container == weak || t.Target.Container?.Container == weak) && c.ArgRef != weak);
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
        var noLaser = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < Families.DeathStar && Lasers(r.Common.Ref) == 0 && Heavy(r.Common.Ref) > 0)
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
        output.WriteLine($"{n} fighters {fighterType} (value {vFighter} each) against 2 × {noLaser} (value {vShip}): win {p.Win:F2}, own lost {p.OwnLost:F0}, enemy lost {p.EnemyLost:F0}; ground (3 against 1) {ground:F2}");
        Assert.True(n * vFighter < 2 * vShip); // the value sum says: lose
        // the composition: the fighters take more enemy value than their own, and more than they lose
        Assert.True(p.EnemyLost > n * vFighter && p.EnemyLost > p.OwnLost, "the predictor does not see the fighters' edge");
        // BattleFloat: one own cheap ship against a known strong enemy fleet
        var s2 = game.Systems.Where(x => x.Owner == Owner.Empire && x != s).OrderBy(x => x.Key).First();
        var weakType = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < Families.DeathStar && r.Empire != 0).OrderBy(r => r.Hull).First().Common.Ref;
        var strongType = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < Families.DeathStar && r.Alliance != 0).OrderByDescending(r => r.Hull).First().Common.Ref;
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
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.CreateMission && t.Command.Fields[2] == 81).Select(t => t.Command))
                if (c.ArgRef is StarSystem { Owner: Owner.Empire } s && s.Sector != home) outsideHome++;
            highKept += game.Missions.Count(m => m.Owner == Owner.Empire && m.Kind == MissionKind.Diplomacy && m.Objective is StarSystem { Owner: Owner.Empire } s2
                && ai.HighValue(s2) && s2.SupportFor(Owner.Empire) < OpenSWRController.SafeShare);
            // never a recall of a high-value own system's diplomacy below the safe share
            foreach (var c in ai.IssueTrace.Skip(from).Where(t => t.Result.Success && t.Command.Type == CommandType.MissionOrder).Select(t => t.Command))
                if (game.Missions.FirstOrDefault(m => m.Ref == c.ArgRef?.Ref) is { Kind: MissionKind.Diplomacy, Objective: StarSystem { Owner: Owner.Empire } s3 })
                    Assert.False(ai.HighValue(s3) && s3.SupportFor(Owner.Empire) < OpenSWRController.SafeShare, $"day {game.Day}: recall at {Family.Of(s3.Ref)}:{SdRef.IndexOf(s3.Ref)}");
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

    /// <summary>
    /// Version 3 minimum chance: no diplomacy or subdue starts below MinChance (the member's attempt chance at the decision, from
    /// the side's view of the target); recalls (the recall command) of low-chance missions pass validation.
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
                foreach (var m in game.Missions.Where(m => m.Owner == side && m.Kind == MissionKind.Diplomacy && m.CompletionStatus == MissionCompletion.None))
                {
                    var result = Orders.Validate(game, new Command(CommandType.MissionOrder, side) { Targets = [(m, GroupMode.Object)] });
                    Assert.Equal(m.Phase == 8, result.Success);
                    if (result.Success) recallable++;
                }
                var k = new OpenSWRController.Known(game, side);
                foreach (var (c, r) in ai.IssueTrace.Skip(from))
                {
                    if (c.Type == CommandType.MissionOrder) { recalls++; Assert.True(r.Success); }
                    if (c.Type != CommandType.CreateMission || !r.Success || Kind(c) is not (MissionKind.Diplomacy or MissionKind.SubdueUprising)) continue;
                    started++;
                    int chance = ai.Chance(game, k, Kind(c), (Agent)c.Targets[0].Target, (StarSystem)c.ArgRef!);
                    Assert.True(chance >= OpenSWRController.MinChance, $"day {game.Day}: {(uint)Kind(c)} at {c.ArgRef!.Ref} chance {chance}");
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
        List<(int Day, List<(MissionKind Kind, GameObject? Target, Seen? Captive)> Missions)> days = [];
        List<(int Day, GameObject? Target, Seen? Captive)> rescues = [];
        foreach (int seed in new[] { DefaultSeed, 445898697, 401859171, 874334749, 12345, 99991 })
        {
            days = RescueGame(400, seed);
            rescues = [.. days.SelectMany(d => d.Missions.Where(m => m.Kind == MissionKind.Rescue).Select(m => (d.Day, m.Target, m.Captive)))];
            output.WriteLine($"seed {seed}: rescues {rescues.Count}");
            if (rescues.Count > 0) break;
        }
        foreach (var (day, target, captive) in rescues) output.WriteLine($"day {day}: rescue {target!.Ref} view enroute {captive?.Enroute} at {captive?.Location?.Ref}");
        Assert.NotEmpty(rescues);
        Assert.All(rescues, r => Assert.True(r.Captive is { Captured: true, Enroute: false, Location: StarSystem }));
        foreach (var g in rescues.GroupBy(r => r.Target))
            foreach (var r in g) Assert.InRange(g.Count(x => x.Day >= r.Day && x.Day < r.Day + 30), 1, 2);
        // the jump: in each decision with a major's rescue, only subdue ("hold home") comes before it
        foreach (var (day, missions) in days.Where(d => d.Missions.Any(m => m.Kind == MissionKind.Rescue && Family.Of(m.Target!.Type) is >= Families.MajorCharacter and < Families.MinorCharacter)))
        {
            int rescue = missions.FindIndex(m => m.Kind == MissionKind.Rescue && Family.Of(m.Target!.Type) is >= Families.MajorCharacter and < Families.MinorCharacter);
            Assert.All(missions.Take(rescue), m => Assert.Equal(MissionKind.SubdueUprising, m.Kind));
        }
    }
}
