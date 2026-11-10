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
        [.. UiRcData.BuildMenu(rows.Select(r => ((int)r.Id, r.Enabled, r.Checked)), Entries())
            .Select(r => ((int)r.Item.Label, r.Enabled, r.Children.Select(c => (int)c.Item.Label).ToList()))];

    // Key order, Parent group rows, and key dedupe (one Build row of the three build ids). Missing ids are skipped.
    [GameFact]
    public void BuilderOrdersAndDedupes()
    {
        var rows = UiRcData.BuildMenu([(529, true, false), (528, true, false), (516, true, false), (546, true, false), (544, false, false), (531, true, false)], Entries());
        Assert.Equal([529, 531, 288], rows.Select(r => (int)r.Item.Id));     // keys 100, 110, 200; the build id dropped, the id with no entry skipped
        Assert.Equal([544, 546], rows[2].Children.Select(c => (int)c.Item.Id)); // keys 202, 206
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
        var menu = Menu(ObjectCommands.Rows(game, Owner.Empire, [(fleet, GroupMode.Object)]));
        Assert.Equal([12312, 12311, 12313, 12318, 12292, 12293, 12295], menu.Select(r => r.Label));
        Assert.Equal([12314, 12315, 12316, 12317], menu[2].Children);
        Assert.Equal([true, true, true, false, true, false, true], menu.Select(r => r.Enabled));
        // greying = the sim's validation of the command built with the selection (combat commands: ArgRef = the fleet's system)
        var bombard = UiRcData.BuildMenu(ObjectCommands.Rows(game, Owner.Empire, [(fleet, GroupMode.Object)]).Select(r => ((int)r.Id, r.Enabled, r.Checked)), Entries())[2];
        Assert.All(bombard.Children, c => Assert.Equal(Orders.Validate(game, ObjectCommands.Build((CommandType)c.Item.Id, Owner.Empire, [(fleet, GroupMode.Object)])).Success, c.Enabled));
        Assert.False(Orders.Validate(game, ObjectCommands.Build(CommandType.Assault, Owner.Empire, [(fleet, GroupMode.Object)])).Success);
        Assert.Equal(Refusal.NoDestination, Orders.Validate(game, ObjectCommands.Build(CommandType.Move, Owner.Empire, [(fleet, GroupMode.Object)]))); // destination pending gives enabled
        var to = game.Systems.Where(s => s.Owner == Owner.Empire && s != fleet.Container).MinBy(s => Orders.Distance((StarSystem)fleet.Container!, s));
        Assert.True(ObjectCommands.NeedsConfirm(game, Owner.Empire, ObjectCommands.Build(CommandType.MoveVariant, Owner.Empire, [(fleet, GroupMode.Object)], to), [(fleet, GroupMode.Object)]));
        Assert.False(ObjectCommands.NeedsConfirm(game, Owner.Empire, ObjectCommands.Build(CommandType.Move, Owner.Empire, [(fleet, GroupMode.Object)], to), [(fleet, GroupMode.Object)]));

        // the other side's fleet: every command refused (owner check), Encyclopedia / Status stay
        var enemy = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Alliance && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        Assert.All(ObjectCommands.Rows(game, Owner.Empire, [(enemy, GroupMode.Object)]).Where(r => r.Id >= CommandType.Scrap), r => Assert.False(r.Enabled));

        // fleet + capital ship: only the shared commands
        var ship = fleet.Contents.First(x => Family.IsCapitalShip(x.Type));
        Assert.Equal([CommandType.Move, CommandType.MoveVariant, CommandType.MoveObject, CommandType.Scrap], ObjectCommands.Intersect([(fleet, GroupMode.Object), (ship, GroupMode.Object)]));
    }

    // Validated before confirming: a refused scrap or a move asks nothing (the dispatch gives the refusal).
    // A valid one asks with a non-empty transit list.
    [GameFact]
    public void RefusedCommandsAskNoConfirmation()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var fleet = game.Systems.Where(s => s.Owner == Owner.Empire).SelectMany(s => s.Contents.OfType<Fleet>())
            .First(f => f.Owner == Owner.Empire && f.Usable && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        var enemy = game.Systems.SelectMany(s => s.Contents.OfType<Fleet>()).First(f => f.Owner == Owner.Alliance && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));
        var to = game.Systems.Where(s => s.Owner == Owner.Empire && s != fleet.Container).MinBy(s => Orders.Distance((StarSystem)fleet.Container!, s))!;
        foreach (var c in new[] { ObjectCommands.Build(CommandType.Scrap, Owner.Empire, [(enemy, GroupMode.Object)]), ObjectCommands.Build(CommandType.MoveVariant, Owner.Empire, [(enemy, GroupMode.Object)], to) })
        {
            Assert.False(Orders.Validate(game, c).Success);
            Assert.False(ObjectCommands.NeedsConfirm(game, Owner.Empire, c, [(enemy, GroupMode.Object)]));
            Assert.Empty(ScreenViews.Transit(game, c));
        }
        var move = ObjectCommands.Build(CommandType.MoveVariant, Owner.Empire, [(fleet, GroupMode.Object)], to);
        Assert.True(ObjectCommands.NeedsConfirm(game, Owner.Empire, move, [(fleet, GroupMode.Object)]));
        Assert.Contains(ScreenViews.Transit(game, move), t => t.Object == fleet);
        Assert.True(ObjectCommands.NeedsConfirm(game, Owner.Empire, ObjectCommands.Build(CommandType.Scrap, Owner.Empire, [(fleet, GroupMode.Object)]), [(fleet, GroupMode.Object)]));
    }

    // The scrap body walks the resolved target set. A group icon of the facilities mode gives the system's facilities. The ground mode gives
    // its troops, fighters and special forces (own, Existing), never the system. The unit group keeps its raw target list.
    [GameFact]
    public void ScrapConfirmationListsTheResolvedTargets()
    {
        var game = NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);
        var system = game.Systems.Where(s => s.Owner == Owner.Empire)
            .First(s => s.Contents.Any(x => Family.Of(x.Type) is >= Families.Headquarters and < Families.MajorCharacter && x.Owner == Owner.Empire && x.Existing)
                && s.Contents.Any(x => Family.IsTroop(x.Type) && x.Owner == Owner.Empire && x.Existing));
        bool Own(GameObject x) => x.Owner == Owner.Empire && x.Existing;
        var facilities = Orders.Targets(ObjectCommands.Build(CommandType.Scrap, Owner.Empire, [(system, GroupMode.Facilities)]));
        Assert.NotEmpty(facilities);
        Assert.Equal(system.Contents.Where(x => Own(x) && Family.Of(x.Type) is >= Families.Headquarters and < Families.MajorCharacter).OrderBy(x => x.Ref), facilities);
        var units = Orders.Targets(ObjectCommands.Build(CommandType.Scrap, Owner.Empire, [(system, GroupMode.Ground)]));
        Assert.Contains(units, x => Family.IsTroop(x.Type));
        Assert.Equal(system.Contents.Where(x => Own(x) && Family.Of(x.Type) is (>= Families.Troop and < Families.CapitalShip) or (>= Families.Fighter and < Families.Headquarters) or (>= Families.SpecialForce and < Families.Mission))
            .OrderBy(x => x.Ref), units);
        Assert.DoesNotContain(system, facilities.Concat(units));
    }

    // A character's rows build without throwing (the mission sub-command carries the mission fields). Move / Confirmed Move are enabled
    // before the destination is picked (the sub-command's missing ArgRef, (64, 32)).
    [GameFact]
    public void CharacterRows()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var ch = game.Systems.SelectMany(s => s.Contents).First(x => Family.Of(x.Type) is >= Families.MajorCharacter and < Families.Mission && x.Owner == Owner.Alliance);
        var rows = ObjectCommands.Rows(game, Owner.Alliance, [(ch, GroupMode.Object)]);
        Assert.True(rows.First(r => r.Id == CommandType.Move).Enabled);
        Assert.True(rows.First(r => r.Id == CommandType.MoveVariant).Enabled);
    }

    // A command kind has one holder per post. Giving it to a second character aboard the same fleet clears the first.
    [GameFact]
    public void CommandKindUniquePerPost()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        var a = game.Fleets.Where(f => f.Owner == Owner.Alliance).SelectMany(f => f.Contents).SelectMany(x => x.Contents).OfType<Character>().First();
        var b = game.Systems.SelectMany(s => s.Contents).OfType<Character>().First(c => c.Owner == Owner.Alliance && c != a);
        Objects.SetParent(game, b, a.Container!); // a second character aboard the same ship
        void Give(Character ch, CommandType id)
        {
            Orders.Submit(game, ObjectCommands.Build(id, Owner.Alliance, [(ch, GroupMode.Object)]));
            while (game.Clock.DispatchOne(game)) { }
        }
        Give(a, CommandType.MakeGeneral);
        Give(b, CommandType.MakeAdmiral);
        Assert.Equal((3, 2), (a.CommandKind, b.CommandKind));
        Give(b, CommandType.MakeGeneral);
        Assert.Equal((0, 3), (a.CommandKind, b.CommandKind));
    }
}
