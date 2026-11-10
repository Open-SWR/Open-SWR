using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent play for the victory condition: Guard, Hunt, and Siege.</summary>
public class Step21Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 Guard = OpenSWRController.V7.Guard, Hunt = OpenSWRController.V7.Hunt, Siege = OpenSWRController.V7.Siege;

    static (GameState Game, OpenSWRController Ai) Game(Owner side, OpenSWRController.V7 add)
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(side) { Features = OpenSWRController.DefaultFeatures.Add(add), IssueTrace = [] };
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        game.Controllers[Owner.Alliance] = new HumanController(Owner.Alliance);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    static StarSystem HqSystem(GameState game) =>
        game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key)!.System!;

    static uint TypeRef(GameState game, uint kind) => OpenSWRController.TypeOf(game, kind)!.Common.Ref;

    /// <summary>Guard: a mission of Vader into an Alliance system is refused (not issued); one into Coruscant is not refused by Guard.</summary>
    [GameFact]
    public void GuardRefusesEnemySystem()
    {
        var (game, ai) = Game(Owner.Empire, Guard);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        var enemy = game.Systems.Where(s => s.Owner == Owner.Alliance).OrderBy(s => s.Key).First();
        var into = new Command(0x240, Owner.Empire) { Targets = [(vader, 0)], ArgRef = enemy, Fields = [0, 0, TypeRef(game, 0x52)] };
        Assert.True(ai.GuardRefuses(game, into));
        Assert.False(ai.Issue(game, into, []));
        var home = game.Systems.First(s => s.Type == Refs.Coruscant);
        Assert.False(ai.GuardRefuses(game, new Command(0x240, Owner.Empire) { Targets = [(vader, 0)], ArgRef = home, Fields = [0, 0, TypeRef(game, 0x55)] }));
        ai.Features = ai.Features.Remove(Guard);
        Assert.False(ai.GuardRefuses(game, into));
    }

    /// <summary>Hunt: an abduction of a known Mon Mothma ranks first, ahead of a covert candidate of higher worth.</summary>
    [GameFact]
    public void HuntRanksMothmaAbductionFirst()
    {
        var (game, ai) = Game(Owner.Empire, Hunt);
        var mothma = game.Characters.First(c => c.Ref == 0x30000240);
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        var other = game.Systems.First(s => s.Owner == Owner.Alliance);
        List<OpenSWRController.Covert> ranked = [new(0x69, other, [vader], 100, 0.9), new(0x62, mothma, [vader], 5, 0.3)];
        Assert.Same(mothma, ai.HuntFirst(ranked)[0].Target);
    }

    /// <summary>Hunt: a found Mon Mothma (the HQ system seen by espionage) gets an abduction before any diplomacy mission of the decision (gate 0).</summary>
    [GameFact]
    public void FoundMothmaAbductedBeforeDiplomacy()
    {
        var (game, ai) = Game(Owner.Empire, Hunt);
        var hq = HqSystem(game);
        Knowledge.Update(game, hq, 10, Owner.Empire);
        game.Clock.Flush(game);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var mothma = game.Characters.First(c => c.Ref == 0x30000240);
        Assert.Same(hq, Knowledge.Location(game, mothma, Owner.Empire));
        ai.HuntGate = 0; // step 22's gate is Step22Tests' subject: here the order only
        ai.Assign(game, k, ai.Sectors(game), []);
        var missions = ai.IssueTrace!.Where(x => x.Result.Success && x.Command.Type == 0x240).Select(x => x.Command).ToList();
        output.WriteLine(string.Join(", ", missions.Select(c => $"{c.Fields[2]:X8} → {c.ArgRef!.Ref:X8}")));
        int abduction = missions.FindIndex(c => Family.Of(c.Fields[2]) == 0x62 && c.ArgRef == mothma);
        int diplomacy = missions.FindIndex(c => Family.Of(c.Fields[2]) == 0x51);
        Assert.True(abduction >= 0);
        Assert.True(diplomacy < 0 || abduction < diplomacy);
    }

    /// <summary>Hunt rule 1a: an unknown Palpatine is searched at Coruscant first, and the decision sends an espionage there.</summary>
    [GameFact]
    public void UnknownPalpatineSearchedAtCoruscant()
    {
        var (game, ai) = Game(Owner.Alliance, Hunt);
        var k = new OpenSWRController.Known(game, Owner.Alliance);
        var palpatine = game.Characters.First(c => c.Ref == Refs.Palpatine);
        var coruscant = game.Systems.First(s => s.Type == Refs.Coruscant);
        output.WriteLine($"Palpatine seen at {Knowledge.Location(game, palpatine, Owner.Alliance)?.Ref:X8}");
        var search = ai.HuntSearch(game, k, palpatine);
        Assert.NotEmpty(search);
        Assert.Same(coruscant, search[0]);
        ai.Assign(game, k, ai.Sectors(game), []);
        Assert.Contains(ai.IssueTrace!, x => x.Result.Success && x.Command.Type == 0x240 && x.Command.ArgRef == coruscant && Family.Of(x.Command.Fields[2]) == 0x52);
    }

    /// <summary>Hunt: the Empire's day-0 HQ candidates are every rim system its view has not explored, minus Yavin (the HQ system among them).</summary>
    [GameFact]
    public void DayZeroCandidates()
    {
        var (game, ai) = Game(Owner.Empire, Hunt);
        var k = new OpenSWRController.Known(game, Owner.Empire);
        var expected = game.Systems.Where(s => Family.Of(s.Type) is >= 0x92 and < 0x94 && s.Type != Refs.Yavin && !Knowledge.See(game, s, Owner.Empire).Explored)
            .Select(s => s.Ref).Order().ToList();
        ai.UpdateHqSearch(game, k);
        output.WriteLine($"candidates {ai.HqCandidates.Count}");
        Assert.Equal(expected, ai.HqCandidates.Order().ToList());
        Assert.Contains(HqSystem(game).Ref, ai.HqCandidates);
        Assert.DoesNotContain(game.Systems.First(s => s.Type == Refs.Yavin).Ref, ai.HqCandidates);
    }

    /// <summary>Siege: with the HQ system known and one swarm group on it, a second fleet in reach takes it too (one-group-per-target exception).</summary>
    [GameFact]
    public void SiegeOpensHqToTwoGroups()
    {
        var (game, ai) = Game(Owner.Empire, Siege);
        var hq = HqSystem(game);
        Knowledge.Update(game, hq, 10, Owner.Empire);
        var from = game.Systems.Where(s => s != hq).OrderBy(s => Orders.Distance(s, hq)).ThenBy(s => s.Key).First();
        var f1 = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, from, completed: true);
        game.Clock.Flush(game);
        Assert.Same(hq, ai.SiegeTarget(game));
        ai.AddSwarmGroup(hq, from, f1);
        Assert.Same(hq, ai.SiegeTargetFor(game, from)); // a second group at the same place gets the HQ system as well
        ai.Features = ai.Features.Remove(Siege);
        Assert.Null(ai.SiegeTargetFor(game, from));
        // what removes the HQ: its SD sides decide whether a system capture keeps it or destroys it
        var record = Definitions.Facilities.First(r => r.Common.Ref == game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key)!.Type);
        output.WriteLine($"HQ {record.Common.Ref:X8}: Alliance {record.Alliance}, Empire {record.Empire}");
    }

    /// <summary>With all three flags off, play is the default (the flags are off in the default; All covers every bit).</summary>
    [GameFact]
    public void FlagsOffAreTheDefault()
    {
        Assert.False(OpenSWRController.DefaultFeatures.Overlaps([Guard, Hunt, Siege]));
        Assert.Equal(Enum.GetValues<OpenSWRController.V7>().Where(f => f <= OpenSWRController.V7.Siege), OpenSWRController.AllFeatures.OrderBy(f => f));
    }
}
