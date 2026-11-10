using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Menu rows from the STRATEGY RCDATA entries, object command lists, and greying from Validate.</summary>
public class ObjectMenuTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);
    static Func<int, MenuItem?>? _entries;

    static Func<int, MenuItem?> Entries()
    {
        if (_entries is not null) return _entries;
        var strategy = PeResources.Load(GameDir.File("STRATEGY.DLL"));
        return _entries = id => id is (>= 32 and <= 41) or (>= 256 and <= 624) && strategy.Find(ResId.RcData, (ushort)id) is { } r ? MenuItem.Decode(r.Data.Span) : null;
    }

    static List<(int Label, bool Enabled, List<int> Children)> Menu(IEnumerable<CommandRow> rows) =>
        [.. UiRcData.BuildMenu(rows.Select(r => (r.Id, r.Enabled, r.Checked)), Entries())
            .Select(r => ((int)r.Item.Label, r.Enabled, r.Children.Select(c => (int)c.Item.Label).ToList()))];

    // Key order, Parent group rows, and key dedupe (one Build row of 0x210-0x212). Missing ids are skipped.
    [GameFact]
    public void BuilderOrdersAndDedupes()
    {
        var rows = UiRcData.BuildMenu([(0x211, true, false), (0x210, true, false), (0x204, true, false), (0x222, true, false), (0x220, false, false), (0x213, true, false)], Entries());
        Assert.Equal([0x211, 0x213, 0x120], rows.Select(r => (int)r.Item.Id));     // keys 100, 110, 200; 0x210 dropped, 0x204 has no entry
        Assert.Equal([0x220, 0x222], rows[2].Children.Select(c => (int)c.Item.Id)); // keys 202, 206
        Assert.False(rows[2].Children[0].Enabled);
    }

    // An own idle fleet in an own system: Move, Confirmed Move, Planetary Bombardment >, Planetary Assault, Encyclopedia,
    // Status (greyed), Scrap. No Rename. Assault is greyed by validation (own system).
    [GameFact]
    public void OwnFleetMenu()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var fleet = game.Systems.Where(s => s.Owner == Owner.Empire).SelectMany(s => s.Contents.OfType<Fleet>())
            .First(f => f.Owner == Owner.Empire && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        var menu = Menu(ObjectCommands.Rows(game, Owner.Empire, [(fleet, 0)]));
        Assert.Equal([12312, 12311, 12313, 12318, 12292, 12293, 12295], menu.Select(r => r.Label));
        Assert.Equal([12314, 12315, 12316, 12317], menu[2].Children);
        Assert.Equal([true, true, true, false, true, false, true], menu.Select(r => r.Enabled));
        // greying = the sim's validation of the command built with the selection (combat commands: ArgRef = the fleet's system)
        var bombard = UiRcData.BuildMenu(ObjectCommands.Rows(game, Owner.Empire, [(fleet, 0)]).Select(r => (r.Id, r.Enabled, r.Checked)), Entries())[2];
        Assert.All(bombard.Children, c => Assert.Equal(Orders.Validate(game, ObjectCommands.Build(c.Item.Id, Owner.Empire, [(fleet, 0)])).Success, c.Enabled));
        Assert.False(Orders.Validate(game, ObjectCommands.Build(0x234, Owner.Empire, [(fleet, 0)])).Success);
        Assert.Equal(new OrderResult(1, 0x22), Orders.Validate(game, ObjectCommands.Build(0x201, Owner.Empire, [(fleet, 0)]))); // destination pending -> enabled
        var to = game.Systems.Where(s => s.Owner == Owner.Empire && s != fleet.Container).MinBy(s => Orders.Distance((StarSystem)fleet.Container!, s));
        Assert.True(ObjectCommands.NeedsConfirm(game, Owner.Empire, ObjectCommands.Build(0x202, Owner.Empire, [(fleet, 0)], to), [(fleet, 0)]));
        Assert.False(ObjectCommands.NeedsConfirm(game, Owner.Empire, ObjectCommands.Build(0x201, Owner.Empire, [(fleet, 0)], to), [(fleet, 0)]));

        // the other side's fleet: every command refused (owner check), Encyclopedia / Status stay
        var enemy = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Alliance && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        Assert.All(ObjectCommands.Rows(game, Owner.Empire, [(enemy, 0)]).Where(r => r.Id >= 0x200), r => Assert.False(r.Enabled));

        // fleet + capital ship: only the shared commands
        var ship = fleet.Contents.First(x => Family.IsCapitalShip(x.Type));
        Assert.Equal([0x201, 0x202, 0x204, 0x200], ObjectCommands.Intersect([(fleet, 0), (ship, 0)]));
    }

    // Validated before confirming: a refused scrap or 0x202 move asks nothing (the dispatch gives the refusal).
    // A valid one asks with a non-empty transit list.
    [GameFact]
    public void RefusedCommandsAskNoConfirmation()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var fleet = game.Systems.Where(s => s.Owner == Owner.Empire).SelectMany(s => s.Contents.OfType<Fleet>())
            .First(f => f.Owner == Owner.Empire && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        var enemy = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Alliance && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        var to = game.Systems.Where(s => s.Owner == Owner.Empire && s != fleet.Container).MinBy(s => Orders.Distance((StarSystem)fleet.Container!, s))!;
        foreach (var c in new[] { ObjectCommands.Build(0x200, Owner.Empire, [(enemy, 0)]), ObjectCommands.Build(0x202, Owner.Empire, [(enemy, 0)], to) })
        {
            Assert.False(Orders.Validate(game, c).Success);
            Assert.False(ObjectCommands.NeedsConfirm(game, Owner.Empire, c, [(enemy, 0)]));
            Assert.Empty(ScreenViews.Transit(game, c));
        }
        var move = ObjectCommands.Build(0x202, Owner.Empire, [(fleet, 0)], to);
        Assert.True(ObjectCommands.NeedsConfirm(game, Owner.Empire, move, [(fleet, 0)]));
        Assert.Contains(ScreenViews.Transit(game, move), t => t.Object == fleet);
        Assert.True(ObjectCommands.NeedsConfirm(game, Owner.Empire, ObjectCommands.Build(0x200, Owner.Empire, [(fleet, 0)]), [(fleet, 0)]));
    }

    // The scrap body walks the resolved target set. A group icon of mode 4 gives the system's facilities. Mode 8 gives
    // its troops, fighters and special forces (own, Existing), never the system. 0x242 keeps the raw list.
    [GameFact]
    public void ScrapConfirmationListsTheResolvedTargets()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var system = game.Systems.Where(s => s.Owner == Owner.Empire)
            .First(s => s.Contents.Any(x => Family.Of(x.Type) is >= 0x20 and < 0x30 && x.Owner == Owner.Empire && x.Existing)
                && s.Contents.Any(x => Family.IsTroop(x.Type) && x.Owner == Owner.Empire && x.Existing));
        bool Own(GameObject x) => x.Owner == Owner.Empire && x.Existing;
        var facilities = Orders.Targets(ObjectCommands.Build(0x200, Owner.Empire, [(system, 4)]));
        Assert.NotEmpty(facilities);
        Assert.Equal(system.Contents.Where(x => Own(x) && Family.Of(x.Type) is >= 0x20 and < 0x30).OrderBy(x => x.Ref), facilities);
        var units = Orders.Targets(ObjectCommands.Build(0x200, Owner.Empire, [(system, 8)]));
        Assert.Contains(units, x => Family.IsTroop(x.Type));
        Assert.Equal(system.Contents.Where(x => Own(x) && Family.Of(x.Type) is (>= 0x10 and < 0x14) or (>= 0x1C and < 0x20) or (>= 0x3C and < 0x40))
            .OrderBy(x => x.Ref), units);
        Assert.DoesNotContain(system, facilities.Concat(units));
    }

    // A character's rows build without throwing (0x241 carries the 0x240 fields). Move / Confirmed Move are enabled
    // before the destination is picked (the 0x241 sub-command's missing ArgRef, (0x40, 0x20)).
    [GameFact]
    public void CharacterRows()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var ch = game.Systems.SelectMany(s => s.Contents).First(x => Family.Of(x.Type) is >= 0x30 and < 0x40 && x.Owner == Owner.Alliance);
        var rows = ObjectCommands.Rows(game, Owner.Alliance, [(ch, 0)]);
        Assert.True(rows.First(r => r.Id == 0x201).Enabled);
        Assert.True(rows.First(r => r.Id == 0x202).Enabled);
    }

    // A command kind has one holder per post. Giving it to a second character aboard the same fleet clears the first.
    [GameFact]
    public void CommandKindUniquePerPost()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var a = game.Fleets.Where(f => f.Owner == Owner.Alliance).SelectMany(f => f.Contents).SelectMany(x => x.Contents).OfType<Character>().First();
        var b = game.Systems.SelectMany(s => s.Contents).OfType<Character>().First(c => c.Owner == Owner.Alliance && c != a);
        Objects.SetParent(game, b, a.Container!); // a second character aboard the same ship
        void Give(Character ch, int id)
        {
            Orders.Submit(game, ObjectCommands.Build(id, Owner.Alliance, [(ch, 0)]));
            while (game.Clock.DispatchOne(game)) { }
        }
        Give(a, 0x263);
        Give(b, 0x262);
        Assert.Equal((3, 2), (a.CommandKind, b.CommandKind));
        Give(b, 0x263);
        Assert.Equal((0, 3), (a.CommandKind, b.CommandKind));
    }
}
