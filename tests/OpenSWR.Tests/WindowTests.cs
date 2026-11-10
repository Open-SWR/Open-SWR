using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Production estimate, mission-window rank order, status pages (UI helpers over the player's views).</summary>
public class WindowTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game(Owner side = Owner.Empire, int seed = 401859171) => NewGame.Create(new GameSettings(side, 1, 0, 0, seed), Definitions);

    static void Run(GameState game, Command c)
    {
        Assert.True(Orders.Execute(game, c).Success);
        while (game.Clock.DispatchOne(game)) { }
    }

    // Totals = cost * count. Build days = E(m, product, count, 0). Delivery days = travel to the TargetKey's system with
    // the product's Hyperdrive. Invalid (n/a, Done greyed) exactly when the validation refuses: (0xF3, 1).
    [GameFact]
    public void ProductionEstimate()
    {
        var game = Game();
        bool Plain(StarSystem s) => s.Type != Refs.Coruscant && s != game.HeadquartersSystem && !s.Blockade;
        var home = game.SystemsOf(Owner.Empire).First(s => Plain(s) && Economy.Members(s.Managers[(int)ManufacturingKind.Ship]).Count > 0);
        var other = game.SystemsOf(Owner.Empire).Where(s => s != home && Plain(s)).MaxBy(s => Orders.Distance(home, s))!;
        var m = home.Managers[(int)ManufacturingKind.Ship];
        var product = Research.Buildable(game, Owner.Empire, 0x28).First(r => Family.IsCapitalShip(r.Common.Ref));
        uint type = product.Common.Ref;
        Command Order(int count) => new(0x211, Owner.Empire) { Targets = [(m, 0)], Fields = [type, (uint)count] };

        var e = Economy.ProductionEstimate(game, Order(2));
        Assert.True(e.Valid);
        Assert.Equal((game.MaintenanceCost(type) * 2, game.RefinedMaterialCost(type) * 2), (e.Maintenance, e.Refined));
        Assert.Equal(Economy.EstimateDays(game, m, type, 2, false), e.BuildDays);
        Assert.Equal(0, e.DeliveryDays); // TargetKey = the own system

        Run(game, new Command(0x214, Owner.Empire) { Targets = [(m, 0)], ArgRef = other });
        int speed = product.Get(SdField.Hyperdrive) is var h and not 0 ? h : 100;
        Assert.Equal(Orders.TravelDays(game, home, other, speed), Economy.ProductionEstimate(game, Order(2)).DeliveryDays);
        Assert.True(Economy.ProductionEstimate(game, Order(2)).DeliveryDays > 0);

        // a count the side cannot maintain: (0xF3, 1) -> invalid, the numbers still computed
        var side = game.Sides[Owner.Empire];
        int over = (side.MaintenanceCapacity - side.MaintenanceAllocated) / Math.Max(1, game.MaintenanceCost(type)) + 1;
        Assert.Equal(new OrderResult(0xF3, 1), Orders.Validate(game, Order(over)));
        var bad = Economy.ProductionEstimate(game, Order(over));
        Assert.False(bad.Valid);
        Assert.Equal(game.MaintenanceCost(type) * over, bad.Maintenance);
    }

    // Rank order of the listed types (research by line 0x20 / 0x21 / 0x22). The window lists only passing types
    // (filtered, not greyed), in that order.
    [GameFact]
    public void MissionRankOrder()
    {
        uint[] order = [0x55, 0x58, 0x57, 0x53000020, 0x53000021, 0x53000022, 0x51, 0x56, 0x52, 0x54, 0x63, 0x62, 0x61, 0x6A, 0x69];
        var listed = Definitions.Missions.Where(r => r.Unk34 == 0).OrderBy(MissionEligibility.MissionRank).ToList();
        uint Key(MissionRecord r) => Family.Of(r.Common.Ref) == 0x53 ? r.Common.Ref : Family.Of(r.Common.Ref);
        Assert.Equal(order.Where(k => listed.Any(r => Key(r) == k)), listed.Where(r => MissionEligibility.MissionRank(r) < 65).Select(Key));
        Assert.Equal(3, Definitions.Missions.Count(r => Family.Of(r.Common.Ref) == 0x53 && MissionEligibility.MissionRank(r) is >= 4 and <= 6));
        Assert.All(Definitions.Missions.Where(r => Family.Of(r.Common.Ref) is >= 0x40 and < 0x50), r => Assert.Equal(65, MissionEligibility.MissionRank(r)));

        var game = Game(Owner.Alliance, 532947196);
        var a = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Usable && !c.OnMission && c.Container is StarSystem);
        var target = game.Systems.Where(s => s != a.Container && Knowledge.See(game, s, Owner.Alliance) is { Populated: true, Owner: Owner.Neutral })
            .MinBy(s => Orders.Distance((StarSystem)a.Container!, s))!;
        var types = MissionEligibility.WindowTypes(game, Owner.Alliance, [a], [], target);
        Assert.NotEmpty(types);
        Assert.Equal(types.Select(MissionEligibility.MissionRank).Order(), types.Select(MissionEligibility.MissionRank));
        Assert.All(types, t => Assert.Null(MissionEligibility.Validate(game, 0x240, Owner.Alliance, t.Common.Ref, [a], [], target)));
        Assert.Contains(types, t => Family.Of(t.Common.Ref) == 0x51);
    }

    static string Name(GameObject o) => $"{o.Type:X8}#{o.Id:X}";
    static string Def(SdCommon c) => $"#{c.StringId}";
    static string Value(StatusRow r) => (r.Text != 0 ? $"[{r.Text}]" : "") + r.Value;
    static List<(int, string)> Rows(StatusPage p) => [.. p.Rows.Select(r => (r.Label, Value(r)))];

    // System page rows: owner text, general / commander, loyalty shares, energy / raw used:total, facility counts.
    [GameFact]
    public void StatusSystem()
    {
        var game = Game(Owner.Alliance, 532947196);
        var s = game.SystemsOf(Owner.Alliance).First(x => x.Contents.Any(o => Family.Of(o.Type) == 0x2C));
        var page = StatusPage.Build(game, Owner.Alliance, s, Name, Def);
        Assert.Equal(34432, page.Title);
        var rows = Rows(page);
        Assert.Equal([34406, 34433, 34434, 34436, 34437, 34438, 34439, 34440, 34441, 34448, 34449, 34450, 34451, 34452, 34453, 34454, 34455, 34456, 34457], rows.Select(r => r.Item1));
        Assert.Equal("[34648]", rows[0].Item2);
        Assert.Equal((s.Support.ToString(), (100 - s.Support).ToString()), (rows[3].Item2, rows[4].Item2));
        Assert.Equal($"{s.UsedEnergy}:{s.Energy}", rows[5].Item2);
        Assert.Equal(s.Contents.Count(o => Family.Of(o.Type) == 0x2C && o.Existing).ToString(), rows[8].Item2);
        Assert.Equal(s.Contents.Count(o => Family.IsTroop(o.Type) && o.Existing).ToString(), rows[17].Item2);
    }

    // Fleet page: status, commanders, ship count, capacities, embarked units, damaged ships, hyperdrive.
    [GameFact]
    public void StatusFleet()
    {
        var game = Game(Owner.Alliance, 532947196);
        var fleet = game.Fleets.First(f => f.Owner == Owner.Alliance && f.Existing && f.Container is StarSystem && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        var page = StatusPage.Build(game, Owner.Alliance, fleet, Name, Def);
        Assert.Equal(34616, page.Title);
        var rows = Rows(page);
        Assert.Equal((34406, "[34632]"), rows[0]); // awaiting orders, not en route: no ETA row
        Assert.Equal([12358, 12360, 12356], rows.Skip(1).Take(3).Select(r => r.Item1));
        Assert.All(page.Rows.Skip(1).Take(3), r => Assert.True(r.Colon));
        var ships = fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing).ToList();
        Assert.Equal((34624, ships.Count.ToString()), rows[4]);
        int fighters = ships.Sum(x => (int)Definitions.CapitalShips.First(r => r.Common.Ref == x.Type).FighterCapacity);
        Assert.Equal((14368, fighters.ToString()), rows[6]);
        Assert.Equal(ships.SelectMany(x => x.Contents).Count(x => Family.IsTroop(x.Type) && x.Existing).ToString(), rows[10].Item2);
        Assert.Equal((34387, "[34646]"), rows[^1]);
    }

    // Capital ship page: class, fleet, status, maintenance, capacities, damage, hull cur:max, four arcs.
    [GameFact]
    public void StatusCapitalShip()
    {
        var game = Game(Owner.Alliance, 532947196);
        var ship = game.Created.First(o => o.Owner == Owner.Alliance && Family.IsCapitalShip(o.Type) && o.Existing && o.Container is Fleet);
        var r = Definitions.CapitalShips.First(x => x.Common.Ref == ship.Type);
        ship.HullDamage = 3;
        var page = StatusPage.Build(game, Owner.Alliance, ship, Name, Def);
        Assert.Equal(34562, page.Title);
        var rows = Rows(page);
        Assert.Equal((34819, Def(r.Common)), rows[0]);
        Assert.Equal((34563, Name(ship.Container!)), rows[1]);
        Assert.Equal((34385, game.MaintenanceCost(ship.Type).ToString()), rows[3]);
        Assert.Contains((34576, $"{r.Hull - 3}:{r.Hull}"), rows);
        Assert.Equal(40 - 1, rows.Count); // no ETA row
        Assert.Equal((34594, ""), rows[^4]);
        Assert.Equal((34585, r.Port.LaserCannon.ToString()), rows[^1]);
    }

    // Character page: status, Force ranking bands, skills, research Yes / No, possible ranks.
    [GameFact]
    public void StatusCharacter()
    {
        var game = Game(Owner.Alliance, 532947196);
        var c = game.Characters.First(x => x.Owner == Owner.Alliance && x.Placed && x.Usable && x.Container is StarSystem);
        var page = StatusPage.Build(game, Owner.Alliance, c, Name, Def);
        Assert.Equal(34595, page.Title);
        var rows = Rows(page);
        Assert.Equal((34596, "[34644]"), rows[0]);
        Assert.Equal((34736, Name(c.Container!)), rows[1]);
        Assert.Equal((34406, "[34632]"), rows[2]);
        int f = c.Force;
        string band = f < 10 ? "[34644]" : f < 20 ? "[34633]" : f < 80 ? "[34640]" : f < 100 ? "[34641]" : f < 120 ? "[34642]" : "[34643]";
        Assert.Equal((34597, band), rows[3]);
        Assert.Equal((34598, c.Skills[SkillIndex.Diplomacy].ToString()), rows[4]);
        Assert.Equal((34435, c.Definition.CanBeAdmiral != 0 ? "[34646]" : "[34645]"), rows[^3]);
        Prisoners.SetInjury(game, c, 5);
        Assert.Equal((34406, "[34631]"), Rows(StatusPage.Build(game, Owner.Alliance, c, Name, Def))[2]);
    }

    // Mission page: target, team size (decoys included), decoys. Hidden types give no rows.
    [GameFact]
    public void StatusMission()
    {
        var game = Game(Owner.Alliance, 532947196);
        var home = game.SystemsOf(Owner.Alliance).MaxBy(s => s.Contents.OfType<Character>().Count(c => c.Owner == Owner.Alliance && c.Usable))!;
        var agents = home.Contents.OfType<Character>().Where(c => c.Owner == Owner.Alliance && c.Usable && !c.OnMission).Take(2).ToList();
        var target = game.Systems.Where(s => s != home && Knowledge.See(game, s, Owner.Alliance) is { Populated: true, Owner: Owner.Neutral }).MinBy(s => Orders.Distance(home, s))!;
        uint diplomacy = Definitions.Missions.First(r => Family.Of(r.Common.Ref) == 0x51).Common.Ref;
        Run(game, new Command(0x240, Owner.Alliance) { Targets = [(agents[0], 0)], Decoys = [.. agents.Skip(1)], ArgRef = target, Fields = [0, 0, diplomacy] });
        var m = game.Missions.Last();
        var page = StatusPage.Build(game, Owner.Alliance, m, Name, Def);
        Assert.Equal(34422, page.Title);
        var rows = Rows(page).Where(r => r.Item1 != 34662).ToList();
        Assert.Equal([(34423, Name(target)), (34424, agents.Count.ToString()), (34425, (agents.Count - 1).ToString())], rows);
    }

    // Galaxy Overview rows sorted by family with "ccc  mmmm name". Objectives by victory mode.
    [GameFact]
    public void StatusOverviewAndObjectives()
    {
        var game = Game(Owner.Alliance, 532947196);
        var items = StatusPage.Overview(game, Owner.Alliance, Def).Items;
        Assert.NotEmpty(items);
        Assert.Equal(items.Select(i => i.Id).Order(), items.Select(i => i.Id));
        var troops = game.Created.Where(o => o.Owner == Owner.Alliance && o.Existing && Family.IsTroop(o.Type)).GroupBy(o => o.Type).First();
        string text = $"{troops.Count():D3}  {troops.Count() * game.MaintenanceCost(troops.Key):D4} {Def(Definitions.Resolve(troops.Key)!.Common)}";
        Assert.Contains(items, i => i.Value == text);
        Assert.Equal([34689, 34697, 34701, 34692, 34708, 34704], StatusPage.Objectives(game, Owner.Alliance).Items.Select(i => i.Text));
        var hq = StatusPage.Objectives(NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 1, 532947196), Definitions), Owner.Empire).Items;
        Assert.Equal([34693, 34688], hq.Select(i => i.Text));
        game.VictoryState = game.VictoryState with { HqGone = true };
        Assert.Equal(34695, StatusPage.Objectives(game, Owner.Alliance).Items[3].Text);
    }

    // Battle-start rows: header / fleet / ship / fighter at cells 0-3 (y = 40 * i). The ship's picture is at cell x 0
    // (text 70), its fighter's at x 20 (text 90). Fleet row font 5, centred, without picture. Header font 11.
    [GameFact]
    public void BattleStartRows()
    {
        var game = Game(Owner.Alliance);
        static bool Fighter(GameObject x) => Family.Of(x.Type) is >= 0x1C and < 0x20;
        // the system's first Usable Alliance fleet whose first capital ship carries no fighter (fighters come first: the new one is row 3)
        var (s, fleet, ship) = game.Systems.Select(x => (x, f: x.Contents.OfType<Fleet>().FirstOrDefault(f => f.Owner == Owner.Alliance && f.Usable)))
            .Where(p => p.f is not null).Select(p => (p.x, p.f!, ship: p.f!.Contents.FirstOrDefault(y => Family.IsCapitalShip(y.Type) && y.Usable)))
            .First(p => p.ship is not null && !p.ship.Contents.Any(Fighter));
        var fighter = new GameObject(0x1C000003, Owner.Alliance, game.NextId());
        ship!.Add(fighter);
        var rows = ScreenViews.BattleStartRows(s, 1);
        Assert.Equal(new ScreenViews.BattleStartRow(null, 6435, 11, true, -1, 0), rows[0]);
        Assert.Equal(new ScreenViews.BattleStartRow(fleet, 0, 5, true, -1, 0), rows[1]);
        Assert.Equal(new ScreenViews.BattleStartRow(ship, 0, 5, false, 0, 70), rows[2]);
        Assert.Equal(new ScreenViews.BattleStartRow(fighter, 0, 4, false, 20, 90), rows[3]);
        // system fighters: the system's name (font 5, centred), then the fighters at x 0
        if (rows.FindIndex(r => r.Object == s) is var at and >= 0)
        {
            Assert.Equal(new ScreenViews.BattleStartRow(s, 0, 5, true, -1, 0), rows[at]);
            Assert.All(rows.Skip(at + 1), r => Assert.Equal((4, 0, 70), (r.Font, r.PictureX, r.TextX)));
        }
        // tab 4: facilities, then troops, then special forces
        var assets = ScreenViews.BattleStartRows(s, 3);
        Assert.Equal(6437, assets[0].StringId);
        var classes = assets.Skip(1).Select(r => Family.IsFacility(r.Object!.Type) ? 0 : Family.IsTroop(r.Object.Type) ? 1 : 2).ToList();
        Assert.Equal(classes.Order(), classes);
    }

    // Report empty texts. Captured entries past the longer of the other two columns are not shown.
    [Fact]
    public void ReportEmptyTexts()
    {
        GameObject[] none = [], captured = [new(0x30000001, Owner.Alliance, 1), new(0x30000002, Owner.Alliance, 2)];
        var cells = ScreenViews.ReportCells(none, captured, none);
        Assert.Equal([(null, 6464), (captured[0], 0), (null, 6465)], cells);
        Assert.Equal([(null, 34086), (null, 34086), (null, 6465)], ScreenViews.ReportCells(none, none, none));
        Assert.Equal([(null, 34086), (null, 6465)], ScreenViews.ReportCells(none, null, none));
        var lost = new GameObject(0x14000001, Owner.Alliance, 3);
        Assert.Equal([(null, 6464), (lost, 0)], ScreenViews.ReportCells(none, null, [lost]));
    }
}
