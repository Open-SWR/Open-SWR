using System.Collections.Immutable;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent swarm groups: idle fleets, stale targets, and the won-assault rule.</summary>
public class OpenSWRAiSwarmTests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static readonly ImmutableHashSet<OpenSWRController.V7> Swarm = OpenSWRController.DefaultFeatures.Add(OpenSWRController.V7.Swarm);

    static (GameState Game, OpenSWRController Ai) Game(bool control)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Empire) { IssueTrace = [], Features = Swarm };
        game.Controllers[Owner.Empire] = control ? ai : new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = control ? new ComputerController(Owner.Alliance) { Rand = _ => 0 } : new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    /// <summary>Rules 1 and 2: an idle fleet past IdleDays forms a group of its own, and two groups never take the same target.</summary>
    [GameFact]
    public void IdleFleetsFormGroupsWithTheirOwnTargets()
    {
        var (game, ai) = Game(control: true);
        int most = 0;
        while (game.Day < 150 && !game.GameOver)
        {
            game.Clock.Step(game);
            var groups = ai.Groups;
            most = Math.Max(most, groups.Count);
            Assert.Equal(groups.Count, groups.Select(g => g.Target).Distinct().Count());
            Assert.All(groups, g => Assert.NotNull(g.Fleet));
        }
        output.WriteLine($"most groups at once {most}");
        Assert.True(most >= 1);
    }

    /// <summary>Rule 2: a stale target is accepted at the median enemy fleet strength seen, refused below.</summary>
    [GameFact]
    public void StaleTargetAtTheMedian()
    {
        var (game, ai) = Game(control: false);
        ai.SawEnemyFleets(game.Day, 100, 200, 300);
        Assert.Equal(200, ai.MedianEnemyFleet);
        Assert.True(ai.SwarmStaleAccept(200));
        Assert.False(ai.SwarmStaleAccept(199));
    }

    /// <summary>
    /// GitHub issue 1: an unscouted enemy system is stale, not fresh, so on day 2 a loaded swarm fleet weaker than the median enemy
    /// fleet does not launch there (before the fix it did: 1.5 x the target's unknown, 0, known strength).
    /// </summary>
    [GameFact]
    public void WeakFleetWaitsAtAnUnscoutedTarget()
    {
        var (game, ai) = Game(control: false);
        game.Controllers[Owner.Empire] = ai;
        var target = game.Systems.Where(s => s.Owner == Owner.Alliance && ai.Unscouted(game, s)).OrderBy(s => s.Key).First();
        var at = game.Systems.Where(s => s.Owner == Owner.Empire).OrderBy(s => Orders.Distance(s, target)).First();
        var row = game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity >= OpenSWRController.SwarmTroops).OrderBy(r => r.Common.Ref).First();
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, at, completed: true);
        var ship = Objects.Create(game, row.Common.Ref, Owner.Empire, fleet, completed: true);
        var troop = game.Definitions.Troops.Where(t => t.Empire != 0).OrderBy(t => t.MaintenanceCost).First().Common.Ref;
        for (int i = 0; i < OpenSWRController.SwarmTroops; i++) Objects.Create(game, troop, Owner.Empire, ship, completed: true);
        game.Clock.Flush(game);
        ai.AddSwarmGroup(target, at, fleet);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        ai.SawEnemyFleets(game.Day - 1, 10 * k.Strength(fleet, Owner.Empire)); // the median enemy fleet: far stronger
        ai.Think(game);
        var launches = ai.IssueTrace!.Where(t => t.Result.Success && t.Command.ArgRef == target && t.Command.Targets.Any(x => x.Target == fleet)).ToList();
        output.WriteLine($"target {Family.Of(target.Ref)}:{SdRef.IndexOf(target.Ref)} from {Family.Of(at.Ref)}:{SdRef.IndexOf(at.Ref)}, fleet strength {k.Strength(fleet, Owner.Empire)}, median {ai.MedianEnemyFleet}, launches {launches.Count}");
        Assert.Empty(launches);
    }

    /// <summary>Rule 4: after a won assault 2 troops stay (the rest re-embark) and the group moves on to the next target.</summary>
    [GameFact]
    public void WonAssaultLeavesTwoAndMovesOn()
    {
        var (game, ai) = Game(control: false);
        // the "taken" system: an own system the fleet sits at, with 5 own troops
        var taken = game.Systems.Where(s => s.Owner == Owner.Empire).OrderBy(s => s.Key).First();
        var troopType = game.Definitions.Troops.First(t => t.Empire != 0).Common.Ref;
        foreach (var t in taken.Contents.Where(x => Family.IsTroop(x.Type) && x.Owner == Owner.Empire).ToList()) Objects.Destroy(game, t, DestroyedReason.Scrapped);
        for (int i = 0; i < 5; i++) Objects.Create(game, troopType, Owner.Empire, taken, completed: true);
        var ship = game.Definitions.CapitalShips.Where(r => r.Empire != 0 && r.TroopCapacity >= 2).OrderBy(r => r.MaintenanceCost).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, taken, completed: true);
        var x0 = Objects.Create(game, ship, Owner.Empire, fleet, completed: true);
        Objects.Create(game, troopType, Owner.Empire, x0, completed: true);
        game.Clock.Flush(game);
        Assert.Equal(3, ai.SwarmSpareForTest(game, taken).Count); // 5 - 2 stay
        ai.AddSwarmGroup(taken, taken, fleet); // the group's target was taken (now own)
        ai.RefreshValues(game);
        ai.DayDue();
        var k = new OpenSWRController.Known(game, Owner.Empire);
        ai.Attack(game, k, [], [], ai.Sectors(game));
        var g = Assert.Single(ai.Groups, x => x.Fleet == fleet.Ref);
        output.WriteLine($"staging {g.Staging?.Ref}, next target {Family.Of(g.Target.Ref)}:{SdRef.IndexOf(g.Target.Ref)}");
        Assert.Same(taken, g.Staging);
        Assert.NotSame(taken, g.Target);
        Assert.Equal(Owner.Alliance, Knowledge.See(game, g.Target, Owner.Empire).Owner);
    }

    /// <summary>The measurement changes nothing, flag on or off: the same orders for 120 days with the no-launch log on and off.</summary>
    [GameFact]
    public void FlagOffIsTheDefault()
    {
        List<string> Orders(bool measure, ImmutableHashSet<OpenSWRController.V7> features)
        {
            var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
            var ai = new OpenSWRController(Owner.Empire) { IssueTrace = [], NoLaunchLog = measure ? [] : null, Features = features };
            game.Controllers[Owner.Empire] = ai;
            game.Controllers[Owner.Alliance] = new ComputerController(Owner.Alliance) { Rand = _ => 0 };
            foreach (var s in game.Sides.Values) s.Speed = 12;
            game.BattleResults = _ => null;
            while (game.Day < 120 && !game.GameOver) game.Clock.Step(game);
            return [.. ai.IssueTrace!.Select(t => $"{(int)t.Command.Type} {string.Join(",", t.Command.Targets.Select(x => x.Target.Ref))} {t.Command.ArgRef?.Ref} {string.Join(",", t.Command.Fields)}")];
        }
        Assert.Contains(OpenSWRController.V7.Swarm, OpenSWRController.DefaultFeatures); // in the default since 2026-11-08
        var off = OpenSWRController.DefaultFeatures.Remove(OpenSWRController.V7.Swarm);
        Assert.Equal(Orders(false, off), Orders(true, off));
        Assert.Equal(Orders(false, OpenSWRController.DefaultFeatures), Orders(true, OpenSWRController.DefaultFeatures));
    }
    /// <summary>
    /// AssaultTroops: rule 2, a swarm base's spare troops leave highest attack strength first (key order without the flag); rule 1,
    /// the assault pick is a buildable troop with at least the cheapest troop's attack strength.
    /// </summary>
    [GameFact]
    public void AssaultTroopsLeaveStrongestFirst()
    {
        var (game, ai) = Game(control: false);
        var s = game.Systems.Where(x => x.Owner == Owner.Empire).OrderBy(x => x.Key).First();
        foreach (var t in s.Contents.Where(x => Family.IsTroop(x.Type)).ToList()) Objects.Destroy(game, t, DestroyedReason.Scrapped);
        var types = Definitions.Troops.Where(t => t.Empire != 0).OrderBy(t => t.AttackStrength).ThenBy(t => t.Common.Ref).ToList();
        for (int i = 0; i < 6; i++) Objects.Create(game, types[0].Common.Ref, Owner.Empire, s, completed: true);
        for (int i = 0; i < 2; i++) Objects.Create(game, types[^1].Common.Ref, Owner.Empire, s, completed: true);
        game.Clock.Flush(game);
        uint Attack(GameObject t) => Definitions.Troops.First(r => r.Common.Ref == t.Type).AttackStrength;
        var off = ai.SwarmSpareForTest(game, s);
        Assert.NotEmpty(off);
        Assert.Equal(types[0].AttackStrength, Attack(off[0]));
        ai.Features = Swarm.Add(OpenSWRController.V7.AssaultTroops);
        var on = ai.SwarmSpareForTest(game, s);
        Assert.Equal(off.Count, on.Count);
        Assert.Equal(types[^1].AttackStrength, Attack(on[0]));
        Assert.Equal(on.Select(Attack).OrderByDescending(a => a), on.Select(Attack));
        uint cheapest = Research.Buildable(game, Owner.Empire, Families.TrainingFacility).Where(r => Family.IsTroop(r.Common.Ref))
            .OrderBy(r => game.MaintenanceCost(r.Common.Ref)).First().Common.Ref;
        var pick = ai.BestFor(game, UnitKind.Troop, UnitRole.Assault, int.MaxValue);
        Assert.NotNull(pick);
        output.WriteLine($"assault pick {pick!.Common.Ref}, cheapest {cheapest}");
        Assert.True(pick.GetOrZero(SdField.TroopAttackStrength) >= Definitions.Troops.First(r => r.Common.Ref == cheapest).AttackStrength);
    }

    /// <summary>
    /// Generals: an attack group fleet at a system with a free character that can be a general and leads better than it negotiates
    /// or spies takes that minor character aboard as its general; a diplomat (diplomacy above leadership) or a major character is never taken.
    /// </summary>
    [GameFact]
    public void GroupFleetTakesAGeneral()
    {
        var (game, ai) = Game(control: false);
        ai.Features = Swarm.Add(OpenSWRController.V7.Generals);
        int Skill(Character c, int s) => Missions.Enhanced(game, c, s);
        var c = game.Characters.Where(c => c.Owner == Owner.Empire && c.Definition.CanBeGeneral != 0 && Missions.Location(c) is StarSystem && Family.Of(c.Type) >= Families.MinorCharacter
                && Skill(c, SkillIndex.Leadership) >= Skill(c, SkillIndex.Diplomacy) && Skill(c, SkillIndex.Leadership) >= Skill(c, SkillIndex.Espionage))
            .OrderBy(c => c.Key).First();
        var at = (StarSystem)Missions.Location(c)!;
        var others = at.Contents.OfType<Character>().Where(x => x.Owner == Owner.Empire && (Skill(x, SkillIndex.Diplomacy) > Skill(x, SkillIndex.Leadership) || Family.Of(x.Type) < Families.MinorCharacter)).ToList();
        uint plain = Definitions.CapitalShips.Where(r => r.Empire != 0).OrderBy(r => r.MaintenanceCost).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, at, completed: true);
        Objects.Create(game, plain, Owner.Empire, fleet, completed: true);
        game.Clock.Flush(game);
        Assert.False(OpenSWRController.HasGeneral(fleet));
        ai.AssignGenerals(game, [], [fleet]);
        foreach (var command in ai.IssueTrace!.Select(x => x.Command)) game.Clock.Submit(command);
        for (int i = 0; i < 24 && !OpenSWRController.HasGeneral(fleet); i++) game.Clock.Step(game);
        output.WriteLine($"general {c.Key} leadership {Skill(c, SkillIndex.Leadership)}, kind {c.CommandKind}");
        Assert.True(OpenSWRController.HasGeneral(fleet));
        Assert.All(others, x => Assert.Equal(0, x.CommandKind));
    }
}
