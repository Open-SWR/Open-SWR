using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Accrual (timer 0x381), level advance and cost scale, exhausted, buildable list, research missions.</summary>
public class ResearchTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game(int galaxySize = 1) => NewGame.Create(new GameSettings(Owner.Alliance, galaxySize, 0, 0, 532947196), Definitions);

    // Step 11 sets the side's cost scale to the number of systems placed: 100 / 150 / 200.
    [GameTheory]
    [InlineData(1, 100)]
    [InlineData(2, 150)]
    [InlineData(3, 200)]
    public void CostScaleIsSystemsPlaced(int size, int systems)
    {
        var game = Game(size);
        Assert.Equal(systems, game.Systems.Count);
        Assert.All(game.Sides.Values, s => Assert.Equal(systems, s.CostScale));
    }

    // One point per non-suspended manufacturing facility of the line's family in owned systems.
    [GameFact]
    public void AccrualCountsNonSuspendedFacilities()
    {
        var game = Game();
        var side = game.Sides[Owner.Empire];
        side.CostScale = 1_000_000; // no level advance
        var facilities = game.SystemsOf(Owner.Empire).SelectMany(s => s.Contents.OfType<Facility>()).ToList();
        var suspended = facilities.First(f => Family.Of(f.Type) is >= 0x28 and < 0x2B);
        suspended.Suspended = true;
        int Count(uint family) => facilities.Count(f => Family.Of(f.Type) == family && !f.Suspended);

        Research.Accrue(game, side);
        Assert.Equal([Count(0x28), Count(0x2A), Count(0x29)], side.ResearchPoints);
        Assert.True(side.ResearchPoints.Sum() > 0);
        Assert.Equal([0, 0, 0], side.ResearchOrder);
        Assert.Equal([0, 0, 0], game.Sides[Owner.Alliance].ResearchPoints);

        // a system without one of its managers aborts the whole update
        var points = side.ResearchPoints.ToArray();
        game.SystemsOf(Owner.Empire).Last().Managers[1] = null!;
        Research.Accrue(game, side);
        Assert.Equal(points, side.ResearchPoints);
    }

    // Only Usable facilities of the manager's owner are members. Suspended ones are included.
    [GameFact]
    public void MembersAreUsableOwnFacilities()
    {
        var game = Game();
        var system = game.SystemsOf(Owner.Empire).First(s => s.Contents.OfType<Facility>().Count(f => Family.Of(f.Type) == 0x28) >= 1);
        var manager = system.Managers[(int)ManufacturingKind.Ship];
        var yards = system.Contents.OfType<Facility>().Where(f => Family.Of(f.Type) == 0x28).ToList();
        Assert.Equal(yards, Economy.Members(manager));
        yards[0].Suspended = true;
        Assert.Equal(yards, Economy.Members(manager));
        yards[0].Usable = false; // e.g. not Completed or Enroute
        Assert.Equal(yards.Skip(1), Economy.Members(manager));
        yards[0].Usable = true;
        yards[0].Owner = Owner.Alliance;
        Assert.Equal(yards.Skip(1), Economy.Members(manager));
    }

    // Alliance facility line 1:24 2:40 4:40 5:60 (no order 3). Cost = difficulty * scale div 100.
    [GameTheory]
    [InlineData(100, 104, 4, 0)]   // 24, 40, gap, 40 -> order 4, next costs 60
    [InlineData(150, 100, 2, 4)]   // 36, 60, then 60 > 4
    [InlineData(125, 29, 0, 29)]   // 24 * 125 div 100 = 30
    [InlineData(125, 30, 1, 0)]    // next costs 50
    public void LevelAdvanceWithGaps(int scale, int points, int order, int left)
    {
        var game = Game();
        var side = game.Sides[Owner.Alliance];
        side.CostScale = scale;
        Research.SetPoints(game, side, 1, points);
        Assert.Equal((order, left, false), (side.ResearchOrder[1], side.ResearchPoints[1], side.ResearchExhausted[1]));
    }

    // Ship line first item difficulty 12: 12 / 18 / 24 points for galaxy size 1 / 2 / 3.
    [GameTheory]
    [InlineData(1, 12)]
    [InlineData(2, 18)]
    [InlineData(3, 24)]
    public void FirstShipItemCost(int size, int cost)
    {
        var game = Game(size);
        var side = game.Sides[Owner.Alliance];
        Research.SetPoints(game, side, 0, cost - 1);
        Assert.Equal(0, side.ResearchOrder[0]);
        Research.SetPoints(game, side, 0, cost);
        Assert.Equal((1, 0), (side.ResearchOrder[0], side.ResearchPoints[0]));
    }

    // Empire troop line 1:46 2:66. The subtraction's own hook finds no next item, so the line is exhausted and points are kept.
    [GameFact]
    public void LastItemSetsExhausted()
    {
        var game = Game();
        var side = game.Sides[Owner.Empire];
        Research.SetPoints(game, side, 2, 46 + 66 + 5);
        Assert.Equal((2, 5, true), (side.ResearchOrder[2], side.ResearchPoints[2], side.ResearchExhausted[2]));
        Assert.False(side.ResearchExhausted[0] || side.ResearchExhausted[1]);
    }

    // Candidates: side flag, RefinedMaterialCost != 0, producer range, with ResearchOrder <= current order.
    [GameFact]
    public void BuildableListFollowsOrder()
    {
        var game = Game();
        var side = game.Sides[Owner.Empire];
        List<uint> Troops() => Research.Buildable(game, Owner.Empire, 0x29).Select(r => r.Common.Ref).ToList();
        Assert.Equal(new uint[] { 0x10000006, 0x10000007, 0x10000008, 0x3C000005, 0x3C000006, 0x3C000007, 0x3C000008 }, Troops().Order());
        Research.SetPoints(game, side, 2, 46);
        Assert.Contains(0x10000009u, Troops());
        Assert.DoesNotContain(0x1000000Au, Troops());
        Assert.DoesNotContain(0x3C000009u, Research.Buildable(game, Owner.Empire, 0x29).Concat(Research.Buildable(game, Owner.Alliance, 0x29)).Select(r => r.Common.Ref));
    }

    // Empire ship line 1:8 2:28. Status 0xC / 0xE. Attempt chance (enhanced ShipyardRD), n = 1 + roll(6), and the post adds to the line.
    [GameFact]
    public void ResearchMissionPoints()
    {
        var game = Game();
        var side = game.Sides[Owner.Empire];
        var system = game.SystemsOf(Owner.Empire).First(s => s.Contents.OfType<Facility>().Any(f => Family.Of(f.Type) == 0x28));
        var team = game.Characters.Where(c => c.Owner == Owner.Empire && c.Placed && c.Existing).Take(2).Cast<Agent>().ToList();
        foreach (var a in team) a.Skills[SkillIndex.ShipDesign] = 60;
        var m = Missions.Create(game, 0x53000020, Owner.Empire, system, team, [], 0, mandatory: false);
        Assert.Equal(0, Missions.ResearchRecruitCheck(game, m));

        var draws = new List<(string Site, int N, int Result)>();
        game.Random.Trace = draws;
        Missions.Execute(game, m);
        var points = draws.Where(d => d.Site == DrawSite.ResearchPoints).ToList();
        Assert.Equal(team.Count, draws.Count(d => d.Site == DrawSite.Research));
        Assert.All(points, d => Assert.Equal(7, d.N)); // roll(6)
        int n = points.Sum(d => 1 + d.Result);
        Assert.Equal(n, m.Accumulator);
        Assert.Equal(n > 0 ? 3 : 2, m.TaskStatus);
        Assert.Equal(team.Count * 60 + points.Count, team.Sum(a => a.Skills[SkillIndex.ShipDesign]));
        int total = side.ResearchPoints[0] + new[] { 8, 28 }.Take(m.LevelsGained).Sum();
        Assert.Equal(n, total);
        Assert.Equal(m.LevelsGained, side.ResearchOrder[0]);

        side.ResearchExhausted[0] = true;
        Assert.Equal(0xE, Missions.ResearchRecruitCheck(game, m));
        foreach (var f in system.Contents.OfType<Facility>().Where(f => Family.Of(f.Type) == 0x28)) f.Suspended = true;
        Assert.Equal(0xC, Missions.ResearchRecruitCheck(game, m));
    }
}
