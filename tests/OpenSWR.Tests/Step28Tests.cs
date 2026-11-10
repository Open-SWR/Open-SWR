using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using OpenSWR.Sim.Ai;
using Xunit.Abstractions;

namespace OpenSWR.Tests;

/// <summary>Computer opponent Wings rule: fighters as system defence and battle weight. Covers system fighter targets and orders.</summary>
public class Step28Tests(ITestOutputHelper output)
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    const OpenSWRController.V7 Wings = OpenSWRController.V7.Wings;

    static (GameState Game, OpenSWRController Ai) AllianceAiGame()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 2, 0, 0, 532947196, Opponent: ComputerOpponent.OpenSWR), Definitions);
        var ai = new OpenSWRController(Owner.Alliance) { IssueTrace = [], Features = OpenSWRController.DefaultFeatures.Add(Wings) };
        game.Controllers[Owner.Alliance] = ai;
        game.Controllers[Owner.Empire] = new HumanController(Owner.Empire);
        foreach (var s in game.Sides.Values) s.Speed = 12;
        game.BattleResults = _ => null;
        game.Clock.Step(game);
        return (game, ai);
    }

    /// <summary>Target ceil(enemy / 4), at least 2, at most <see cref="OpenSWRController.WingsMax"/>.</summary>
    [Fact]
    public void TargetBounds()
    {
        Assert.Equal(2, OpenSWRController.WingTarget(1));
        Assert.Equal(2, OpenSWRController.WingTarget(8));
        Assert.Equal(3, OpenSWRController.WingTarget(9));
        Assert.Equal(8, OpenSWRController.WingTarget(32));
        Assert.Equal(OpenSWRController.WingsMax, OpenSWRController.WingTarget(500));
    }

    /// <summary>
    /// An Empire fleet of 40 capital ships appears in an Alliance shipyard system (its own fighters there removed): the system's target is capped at
    /// 8, and the system's own shipyard orders fighters into it (none beyond the cap).
    /// </summary>
    [GameFact]
    public void ThreatenedSystemGetsFightersUpToTheCap()
    {
        var (game, ai) = AllianceAiGame();
        bool Fighter(GameObject x) => Family.Of(x.Type) is >= 0x1C and < 0x20;
        var s = game.Systems.Where(x => x.Owner == Owner.Alliance && ScreenViews.Managers(x, Owner.Alliance).Any(m => m.Kind == ManufacturingKind.Ship && Economy.Members(m).Any()))
            .OrderBy(x => x.Key).First();
        foreach (var f in s.Contents.Where(y => y.Owner == Owner.Alliance && Fighter(y)).ToList()) Objects.Destroy(game, f, 0x14);
        var ship = game.Definitions.CapitalShips.Where(r => Family.Of(r.Common.Ref) < 0x18 && r.Empire != 0).OrderBy(r => r.Common.Ref).First().Common.Ref;
        var fleet = (Fleet)Objects.Create(game, Refs.Fleet, Owner.Empire, s, completed: true);
        for (int i = 0; i < 40; i++) Objects.Create(game, ship, Owner.Empire, fleet, completed: true);
        game.Clock.Flush(game);
        ai.RefreshValues(game);
        ai.IdleReasons = [];
        var k = new OpenSWRController.Known(game, Owner.Alliance);
        var needs = ai.WingNeeds(game, k);
        output.WriteLine(string.Join(", ", needs.Select(x => $"{x.System.Ref:X8} {x.Want}")));
        Assert.Contains(needs, x => x.System == s && x.Want == OpenSWRController.WingsMax);
        Assert.All(needs, x => Assert.InRange(x.Want, 1, OpenSWRController.WingsMax));

        int from = ai.IssueTrace!.Count;
        ai.DayDue();
        ai.Production(game, k, [], 0, ai.Sectors(game));
        foreach (var (c, _) in ai.IssueTrace.Skip(from)) output.WriteLine($"{c.Type:X} {string.Join(" ", c.Fields.Select(f => f.ToString("X")))} {c.ArgRef?.Ref:X8}");
        // the yard at s (nearest: distance 0) orders the fighters into its own system, at most the cap
        var order = Assert.Single(ai.IssueTrace.Skip(from), t => t.Command.Type == 0x211 && Family.Of(t.Command.Fields[0]) is >= 0x1C and < 0x20
            && t.Command.Targets[0].Target is ManufacturingManager { TargetKey: var target } && target == s);
        Assert.InRange((int)order.Command.Fields[1], 1, OpenSWRController.WingsMax);

        // once executed, the queued fighters sit in s (placed at the order): its shortfall drops by them, so the cap holds across decisions
        game.Clock.Step(game);
        int queued = s.Contents.Count(y => y.Owner == Owner.Alliance && Fighter(y));
        output.WriteLine($"{queued} fighters in {s.Ref:X8} after the tick");
        Assert.Equal((int)order.Command.Fields[1], queued);
        Assert.DoesNotContain(ai.WingNeeds(game, new OpenSWRController.Known(game, Owner.Alliance)), x => x.System == s && x.Want > OpenSWRController.WingsMax - queued);
    }

    /// <summary>Wings is on in the default.</summary>
    [Fact]
    public void FlagOnInTheDefault()
    {
        Assert.True(OpenSWRController.ParseFeatures("Wings") is { } parsed && parsed.SetEquals([Wings])); // aimatch --features
        Assert.Contains(Wings, OpenSWRController.DefaultFeatures);
    }
}
