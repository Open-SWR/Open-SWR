using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Sector-window entries, system sub-window lists, object pictures, drop commands, go-to. All from the player side's views.</summary>
public class ScreenViewTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game(Owner side = Owner.Alliance) => NewGame.Create(new GameSettings(side, 1, 0, 0, 532947196), Definitions);

    static Fleet OwnFleet(GameState game, Owner side) =>
        game.Fleets.First(f => f.Owner == side && f.Existing && f.Container is StarSystem && f.Contents.Any(x => Family.IsCapitalShip(x.Type)));

    // One entry per system of the sector (key order). Picture 14000 + the record picture, position formula, view owner, bars, HQ marker,
    // and the four icons (fleets: the player's count in the player's colour).
    [GameFact]
    public void SectorEntries()
    {
        var game = Game();
        var fleet = OwnFleet(game, Owner.Alliance);
        var home = (StarSystem)fleet.Container!;
        var entries = ScreenViews.Sector(game, home.Sector, Owner.Alliance);
        Assert.Equal(game.Systems.Where(s => s.Sector == home.Sector).OrderBy(s => s.Key).Select(s => s), entries.Select(e => e.System));
        var e = entries.First(x => x.System == home);
        Assert.Equal((int)home.Definition.Picture, e.Picture);
        Assert.Equal((int)((home.Definition.X - home.Sector.Definition.X) * 13 * 235 / 1024.0), e.X);
        Assert.Equal((int)((home.Definition.Y - home.Sector.Definition.Y) * 10 * 360 / 1024.0), e.Y);
        Assert.Equal(Owner.Alliance, e.Owner);
        Assert.Equal(home.Support, e.Support);
        var fleets = e.Icons.Single(i => i.Mode == GroupMode.Fleets);
        Assert.Equal(home.Contents.OfType<Fleet>().Count(f => f.Owner == Owner.Alliance && f.Existing), fleets.Count);
        Assert.Equal(Owner.Alliance, fleets.Colour);
        Assert.True(e.Icons.Single(i => i.Mode == GroupMode.Facilities).Shown == home.Contents.Any(o => Family.Of(o.Type) is >= Families.Shipyard and < Families.MajorCharacter && o.Existing));
        // the HQ marker: the Alliance HQ system (headquarters facility family) as the Alliance sees it
        var hq = game.Systems.First(s => s.Contents.Any(o => Family.Of(o.Type) == Families.Headquarters && o.Owner == Owner.Alliance));
        Assert.True(ScreenViews.SectorEntry(game, hq, Owner.Alliance).Headquarters);
        // an enemy fleet the Alliance does not see is not counted
        var empire = OwnFleet(game, Owner.Empire);
        var at = (StarSystem)empire.Container!;
        if (!Knowledge.IsLive(empire, Owner.Alliance) && !game.Views.ContainsKey((empire, Owner.Alliance)))
            Assert.DoesNotContain(ScreenViews.Fleets(game, at, Owner.Alliance), v => v.Object == empire);
    }

    // Slot rule: none open gives right when SD X >= 511. One open gives the other slot. Both gives the sector's half.
    [GameFact]
    public void SectorSlots()
    {
        var game = Game();
        var east = game.Sectors.First(s => s.Definition.X >= 511);
        var west = game.Sectors.First(s => s.Definition.X < 511);
        Assert.Equal(1, ScreenViews.SectorSlot(east, []));
        Assert.Equal(0, ScreenViews.SectorSlot(west, []));
        Assert.Equal(0, ScreenViews.SectorSlot(east, [1]));
        Assert.Equal(0, ScreenViews.SectorSlot(west, [0, 1])); // both open: the sector's half
        Assert.Equal(1, ScreenViews.SectorSlot(east, [0, 1]));
    }

    // Fleet / ship / contents lists, planet tabs of the view owner, facility pictures.
    [GameFact]
    public void SubWindowLists()
    {
        var game = Game();
        var fleet = OwnFleet(game, Owner.Alliance);
        var s = (StarSystem)fleet.Container!;
        Assert.Contains(ScreenViews.Fleets(game, s, Owner.Alliance), v => v.Object == fleet);
        var ships = ScreenViews.Ships(game, fleet, Owner.Alliance);
        Assert.Equal(fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing), ships.Select(v => v.Object));
        Assert.Equal(ships, ScreenViews.FleetContents(game, [fleet], 102, Owner.Alliance));
        var troops = fleet.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents).Where(x => Family.IsTroop(x.Type)).ToList();
        Assert.Equal(troops, ScreenViews.FleetContents(game, [fleet], 104, Owner.Alliance).Select(v => v.Object));

        var own = game.Systems.First(x => x.Owner == Owner.Alliance && x.Contents.Any(o => Family.IsTroop(o.Type)));
        Assert.Equal(own.Contents.Where(o => Family.IsTroop(o.Type) && o.Existing), ScreenViews.Planet(game, own, 2, Owner.Alliance).Select(v => v.Object));
        var agents = ScreenViews.Planet(game, own, 1, Owner.Alliance);
        Assert.All(agents, v => Assert.Null(v.Mission));

        var yard = game.Systems.SelectMany(x => x.Contents).First(o => Family.Of(o.Type) is >= Families.Shipyard and < Families.ManufacturingEnd && o.Existing && o.Owner is Owner.Alliance or Owner.Empire);
        var view = ScreenViews.View(game, yard, yard.Owner) with { Owner = Owner.Alliance };
        int b = SdRef.IndexOf(yard.Type) switch { 1 => 9006, 2 => 9014, 3 => 9022, 4 => 9010, 5 => 9018, _ => 9026 };
        Assert.Equal(b, ScreenViews.FacilityPicture(view));
        Assert.Equal(b + 2, ScreenViews.FacilityPicture(view with { Completed = false }));  // side-1 owner: base + 2
        Assert.Equal(b + 1, ScreenViews.FacilityPicture(view with { Completed = false, Owner = Owner.Empire }));
        Assert.Equal(b + 3, ScreenViews.FacilityPicture(view with { Enroute = true }));
        var other = yard.Owner == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        Assert.Equal(ScreenViews.Managers(yard.System!, yard.Owner).Select(m => m.Kind).ToList(),
            new[] { ManufacturingKind.Ship, ManufacturingKind.Training, ManufacturingKind.Construction }.Where(k => yard.System!.Managers[(int)k] is not null));
        Assert.Empty(ScreenViews.Managers(yard.System!, other));
    }

    // Picture layers in drawing order: fleet small 10400 + 25. Troops: frame 11513 (+1 small) first. An Enroute,
    // not-Completed troop lists 11513, 11515, 11570 / 11572 before the base. Overlays come before the base, Captured after.
    [GameFact]
    public void ObjectPictures()
    {
        var game = Game();
        var fleet = OwnFleet(game, Owner.Alliance);
        var v = ScreenViews.View(game, fleet, Owner.Alliance);
        var B = ScreenViews.PictureRole.Base;
        var F = ScreenViews.PictureRole.Frame;
        Assert.Equal([new(false, 10425, B)], ScreenViews.Picture(game, v, Owner.Alliance, small: true, overlays: true));
        Assert.Equal([new(false, 10400, B)], ScreenViews.Picture(game, v, Owner.Alliance, small: false, overlays: false));
        var troop = game.Created.First(o => Family.IsTroop(o.Type) && o.Existing && o.Type is not (Refs.AllianceArmy or Refs.EmpireArmy));
        int p = Definitions.Resolve(troop.Type)!.Common.StringNumber;
        var tv = ScreenViews.View(game, troop, troop.Owner);
        Assert.Equal([new(false, 11514, F), new(true, p, B)], ScreenViews.Picture(game, tv, troop.Owner, small: true, overlays: true));
        int c = tv.Owner == Owner.Alliance ? 11570 : 11572;
        Assert.Equal([new(false, 11513, F), new(false, 11515), new(false, c), new(true, p + 16384, B)],
            ScreenViews.Picture(game, tv with { Enroute = true, Completed = false }, troop.Owner, small: false, overlays: true));
        foreach (var x in game.Created.Where(o => o.Type is Refs.AllianceArmy or Refs.EmpireArmy or Refs.LongprobeRecon or Refs.ProbeDroids).Take(4)) // generic GOKRES Enroute overlay
        {
            int px = ScreenViews.PictureId(game, x);
            var xv = ScreenViews.View(game, x, x.Owner) with { Enroute = true, Destroyed = false, Damaged = false, Completed = true };
            Assert.Contains(new ScreenViews.PictureLayer(true, px + 4096), ScreenViews.Picture(game, xv, x.Owner, small: true, overlays: true));
        }
        var ship = fleet.Contents.First(x => Family.IsCapitalShip(x.Type));
        var damaged = ScreenViews.View(game, ship, Owner.Alliance) with { Damaged = true, Completed = false };
        int q = ScreenViews.PictureId(game, ship);
        Assert.Equal([new(true, q + 24576), new(false, 11570), new(true, q + 16384, B)], ScreenViews.Picture(game, damaged, Owner.Alliance, small: false, overlays: true));
    }

    // issue #17: the generic GOKRES Enroute / Damaged overlays have their base's size in both forms (capital ships; fighters'
    // 66 x 25 icons carry 61 x 25 overlays and are left out)
    [GameFact]
    public void GenericOverlaysMatchBaseSize()
    {
        var game = Game();
        var gokres = PeResources.Load(GameDir.File("GOKRES.DLL"));
        (int, int)? Size(int id) => gokres.Find(ResId.Bitmap, (ushort)id) is { } r ? (IndexedBitmap.FromDib(r.Data.Span) is var b ? (b.Width, b.Height) : null) : null;
        int checkedLayers = 0;
        foreach (var ship in game.Created.Where(o => Family.IsCapitalShip(o.Type)).DistinctBy(o => o.Type))
        {
            var v = ScreenViews.View(game, ship, ship.Owner) with { Enroute = true, Damaged = true, Destroyed = false, Completed = true };
            foreach (bool small in new[] { true, false })
            {
                var layers = ScreenViews.Picture(game, v, ship.Owner, small, overlays: true);
                var @base = Size(layers.Single(l => l.Role == ScreenViews.PictureRole.Base).Id);
                foreach (var l in layers.Where(l => l.Gokres && l.Role == ScreenViews.PictureRole.Overlay))
                    if (Size(l.Id) is { } s) { Assert.Equal(@base, s); checkedLayers++; }
            }
        }
        Assert.True(checkedLayers > 0);
    }

    // Composition in palette indices: the fill takes the base's key, the frame is copied opaque (key := its bottom-left
    // index), and the rest is keyed on their own bottom-left index. An overlay keyed on 0 copies its index-252 pixels,
    // which end transparent (= the result's key 252).
    [Fact]
    public void PictureComposition()
    {
        var pal = new byte[768];
        // top-down rows; the key is the bottom-left pixel
        var @base = new IndexedBitmap(4, 2, [252, 5, 5, 252, 252, 6, 6, 252], pal);
        var overlay = new IndexedBitmap(4, 2, [252, 0, 9, 0, 0, 0, 0, 9], pal);
        Assert.Equal((252, 0), (@base.Key, overlay.Key));
        var (d, key) = IndexedBitmap.Compose(4, 2, @base.Key, pal, [(overlay, false), (@base, false)]);
        Assert.Equal(252, key);
        Assert.Equal(new byte[] { 252, 5, 5, 252, 252, 6, 6, 9 }, d.Pixels); // (0, 0) from the overlay = transparent; (3, 1) overlay shows under the base's key
        var frame = new IndexedBitmap(4, 2, [1, 1, 1, 1, 7, 1, 1, 1], pal);
        var (e, k2) = IndexedBitmap.Compose(4, 2, @base.Key, pal, [(frame, true), (@base, false)]);
        Assert.Equal(7, k2);
        Assert.Equal(new byte[] { 1, 5, 5, 1, 7, 6, 6, 1 }, e.Pixels); // the troop over the frame; only the frame's key marker is transparent
    }

    // Drop commands by source kind. An own fleet dropped on another system gives the move command there.
    [GameFact]
    public void DropCommands()
    {
        var game = Game();
        var fleet = OwnFleet(game, Owner.Alliance);
        var from = (StarSystem)fleet.Container!;
        var to = game.Systems.Where(s => s.Owner == Owner.Alliance && s != from).MinBy(s => Orders.Distance(from, s))!;
        // One transit line per moved object, with its travel days.
        var transit = ScreenViews.Transit(game, new Command(CommandType.Move, Owner.Alliance) { Targets = [(fleet, GroupMode.Object)], ArgRef = to });
        var line = Assert.Single(transit);
        Assert.Same(fleet, line.Object);
        Assert.Equal(Orders.TravelDays(game, from, to, Orders.Speed(game, fleet)), line.Days);
        var drop = ScreenViews.Drop(WindowKind.SystemFleet, [(fleet, GroupMode.Object)], to, to, ctrl: false);
        var (id, selection, target) = Assert.Single(drop);
        Assert.Equal((CommandType.Move, to), (id, target));
        var command = ObjectCommands.Build(id, Owner.Alliance, selection, target);
        Assert.Equal(CommandType.Move, command.Type);
        Assert.True(Orders.Validate(game, command).Success);
        OrderResult? result = null;
        Orders.Submit(game, command, r => result = r);
        while (game.Clock.DispatchOne(game)) { }
        Assert.True(result!.Value.Success);
        Assert.True(fleet.Enroute || fleet.Container == to || fleet.Destination == to);

        Assert.Equal(CommandType.MoveVariant, ScreenViews.Drop(WindowKind.SystemPlanet, [(fleet, GroupMode.Object)], from, to, ctrl: true)[0].Id);
        Assert.Empty(ScreenViews.Drop(WindowKind.SystemFleet, [(fleet, GroupMode.Object)], to, null, ctrl: false));
        Assert.Empty(ScreenViews.Drop(WindowKind.SystemMissions, [(fleet, GroupMode.Object)], to, to, ctrl: false));
        // sector window: a fleets and a facilities entry give the move (fleets) first, then the target command with (system, facilities) to T_dest
        var split = ScreenViews.Drop(WindowKind.Sector, [(from, GroupMode.Fleets), (from, GroupMode.Facilities)], to, game.Systems[0], ctrl: false);
        Assert.Equal([(CommandType.Move, GroupMode.Fleets, game.Systems[0]), (CommandType.SetProductionTarget, GroupMode.Facilities, (GameObject)to)], split.Select(c => (c.Id, c.Selection[0].Mode, c.Target)));
        var manager = from.Managers.First(m => m is not null);
        Assert.Equal((CommandType.SetProductionTarget, (GameObject)to), ScreenViews.Drop(WindowKind.SystemManufacturing, [(manager, 0)], from, to, false).Select(c => (c.Id, c.Target)).Single());
    }

    // openswr deviation (product owner): units dropped on an own fleet go where they fit - capital ships into the fleet, troops and
    // fighters aboard a ship with room, characters and special forces aboard a ship (Personnel tab); a unit without room is refused,
    // the rest still move; from another system they travel
    [GameFact]
    public void DropOntoFleet()
    {
        var game = Game();
        // a fleet of one ship that carries fighters and troops (none at setup holds both)
        var carrier = Definitions.CapitalShips.Select(r => Definitions.Resolve(r.Common.Ref)!).First(r => r.Get(SdField.FighterCapacity) > 0 && r.Get(SdField.TroopCapacity) > 0).Common.Ref;
        var home = (StarSystem)OwnFleet(game, Owner.Alliance).Container!;
        var fleet = (Fleet)Objects.Create(game, OwnFleet(game, Owner.Alliance).Type, Owner.Alliance, home, completed: true);
        Objects.Create(game, carrier, Owner.Alliance, fleet, completed: true);
        // another system: one where an own character stands (moving one there would start its travel)
        var farCharacter = game.Characters.First(c => c.Owner == Owner.Alliance && c.Existing && c.Completed && !c.Enroute && c.Container is StarSystem s && s != home);
        var other = (StarSystem)farCharacter.Container!;
        var ship = fleet.Contents.First(x => Family.IsCapitalShip(x.Type));
        uint Of(uint lo, uint hi) => game.Created.First(o => Family.Of(o.Type) >= lo && Family.Of(o.Type) < hi && o.Owner == Owner.Alliance).Type;
        uint troopType = Of(Families.Troop, Families.CapitalShip), fighterType = Of(Families.Fighter, Families.Headquarters), agentType = Of(Families.SpecialForce, Families.Mission);

        List<OrderResult> Drop(StarSystem from, params GameObject[] units)
        {
            var results = new List<OrderResult>();
            foreach (var (id, selection, target) in ScreenViews.Drop(game, Owner.Alliance, WindowKind.SystemPlanet, [.. units.Select(u => (u, GroupMode.Object))], from, fleet, ctrl: false))
            {
                OrderResult? r = Orders.Validate(game, ObjectCommands.Build(id, Owner.Alliance, selection, target));
                if (r.Value.Success) Orders.Submit(game, ObjectCommands.Build(id, Owner.Alliance, selection, target), x => r = x);
                while (game.Clock.DispatchOne(game)) { }
                results.Add(r!.Value);
            }
            return results;
        }
        bool Aboard(GameObject u) => u.Container is { } c && Family.IsCapitalShip(c.Type) && c.Container == fleet;

        // same system: one of each kind
        var newFleet = Objects.Create(game, fleet.Type, Owner.Alliance, home, completed: true);
        var capital = Objects.Create(game, ship.Type, Owner.Alliance, newFleet, completed: true);
        var troop = Objects.Create(game, troopType, Owner.Alliance, home, completed: true);
        var fighter = Objects.Create(game, fighterType, Owner.Alliance, home, completed: true);
        var character = game.Characters.First(c => c.Owner == Owner.Alliance && c.Existing && c.Completed && c.Container is StarSystem && c != farCharacter);
        Objects.SetParent(game, character, home);
        var force = Objects.Create(game, agentType, Owner.Alliance, home, completed: true);
        Assert.All(Drop(home, capital, troop, fighter, character, force), r => Assert.True(r.Success));
        game.Clock.RunUntilDay(game, game.Clock.Day + 3);
        Assert.Same(fleet, capital.Container);
        Assert.All(new[] { troop, fighter, character, force }, u => Assert.True(Aboard(u), $"{Family.Of(u.Type)}:{SdRef.IndexOf(u.Type)} in {u.Container?.Type}"));
        Assert.Contains(ScreenViews.FleetContents(game, [fleet], 105, Owner.Alliance), v => v.Object == character);
        Assert.Contains(ScreenViews.FleetContents(game, [fleet], 105, Owner.Alliance), v => v.Object == force);
        Assert.Contains(ScreenViews.FleetContents(game, [fleet], 104, Owner.Alliance), v => v.Object == troop);
        Assert.Contains(ScreenViews.FleetContents(game, [fleet], 103, Owner.Alliance), v => v.Object == fighter);

        // the sector window's fleet icon: the side's first Usable fleet there
        Assert.Same(home.Contents.OfType<Fleet>().Where(f => f.Owner == Owner.Alliance && f.Existing && f.Usable).MinBy(f => f.Ref), ScreenViews.IconFleet(home, Owner.Alliance));

        // no room: troops beyond the fleet's troop capacity are refused (capital ship family, 2), the others board
        int left = fleet.Contents.Sum(x => Definitions.Resolve(x.Type)!.Get(SdField.TroopCapacity)) - fleet.Contents.SelectMany(s => s.Contents).Count(o => Family.IsTroop(o.Type));
        var troops = Enumerable.Range(0, left + 1).Select(_ => Objects.Create(game, troopType, Owner.Alliance, home, completed: true)).ToArray();
        var results = Drop(home, troops);
        Assert.Equal(Refusal.NoTroopRoom, results[0]);
        Assert.True(results[^1].Success);
        Assert.Equal(left, troops.Count(Aboard));

        // another system: one move; the agent travels by the move mission, the ship and the troop by fleet travel
        var farAgent = Objects.Create(game, agentType, Owner.Alliance, other, completed: true);
        var farFleet = Objects.Create(game, fleet.Type, Owner.Alliance, other, completed: true);
        var farShip = Objects.Create(game, ship.Type, Owner.Alliance, farFleet, completed: true);
        var farTroop = Objects.Create(game, troopType, Owner.Alliance, other, completed: true);
        Objects.SetParent(game, troops.First(Aboard), home); // room for one troop
        var farFighter = Objects.Create(game, fighterType, Owner.Alliance, other, completed: true);
        var far = Drop(other, farAgent, farShip, farTroop, farFighter, farCharacter);
        game.Clock.RunUntilDay(game, game.Clock.Day + 40);
        Assert.True(Aboard(farAgent), $"agent in {farAgent.Container?.Type}");
        Assert.Same(fleet, farShip.Container);
        Assert.True(Assert.Single(far).Success);
        Assert.True(Aboard(farTroop), $"troop in {farTroop.Container?.Type}");
        Assert.True(Aboard(farFighter) && Aboard(farCharacter));
    }

    // Go to: system gives sector window only. Fleet / ship gives fleet window of its system. Manager gives manufacturing.
    [GameFact]
    public void GoTo()
    {
        var game = Game();
        var fleet = OwnFleet(game, Owner.Alliance);
        var s = (StarSystem)fleet.Container!;
        var ship = fleet.Contents.First(x => Family.IsCapitalShip(x.Type));
        Assert.Equal((s, WindowKind.None, (GameObject)s), ScreenViews.GoTo(game, s, Owner.Alliance));
        Assert.Equal((s, WindowKind.SystemFleet, (GameObject)s), ScreenViews.GoTo(game, fleet, Owner.Alliance));
        Assert.Equal((s, WindowKind.SystemFleet, (GameObject)fleet), ScreenViews.GoTo(game, ship, Owner.Alliance));
        var m = game.Systems.First(x => x.Owner == Owner.Alliance && x.Managers[0] is not null).Managers[0];
        Assert.Equal(WindowKind.SystemManufacturing, ScreenViews.GoTo(game, m, Owner.Alliance)!.Value.Kind);
        Assert.Null(ScreenViews.GoTo(game, game.Staging, Owner.Alliance));
    }

    // Missions whose type is hidden (MISSNSD file 52, e.g. the Move mission type) are not listed. Player missions are.
    [GameFact]
    public void HiddenMissionsNotListed()
    {
        var types = Definitions.Missions.ToDictionary(r => r.Common.Ref);
        Assert.False(ScreenViews.Listed(new Mission(types[Refs.MoveMission], SdRef.Make(65, 2304))));
        Assert.True(ScreenViews.Listed(new Mission(types[Refs.DiplomacyMission], SdRef.Make(81, 2304))));
        Assert.False(ScreenViews.Listed(null));
    }

    // Presence: fighters / troops / characters aboard a capital ship, immobile at speed 0. A fleet has what any of its ships has.
    // Icons 10404 / 10405 / 10406 / 10430 at x 25 / 41 / 57 / 9, + 50 for a non-Alliance owner.
    [GameFact]
    public void PresenceMask()
    {
        var game = Game();
        foreach (var fleet in game.Fleets.Where(f => f.Owner == Owner.Alliance && f.Existing && f.Container is StarSystem))
        {
            var any = Presence.None;
            foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Existing))
            {
                var aboard = ship.Contents.Where(x => x.Existing).Select(x => Family.Of(x.Type)).ToList();
                var expected = new Presence(aboard.Any(f => f is >= Families.Fighter and < Families.Headquarters), aboard.Any(f => f is >= Families.Troop and < Families.CapitalShip),
                    aboard.Any(f => f is >= Families.MajorCharacter and < Families.Mission), Orders.Speed(game, ship) == 0);
                Assert.Equal(expected, ScreenViews.PresenceMask(game, ship, Owner.Alliance));
                any = any.Or(expected);
            }
            Assert.Equal(any, ScreenViews.PresenceMask(game, fleet, Owner.Alliance));
        }
        Assert.Contains(game.Fleets, f => f.Owner == Owner.Alliance && ScreenViews.PresenceMask(game, f, Owner.Alliance) != Presence.None);
        Assert.Equal([(10404, 25), (10405, 41), (10406, 57), (10430, 9)], ScreenViews.PresenceIcons(new Presence(true, true, true, true), Owner.Alliance));
        Assert.Equal([(10455, 41)], ScreenViews.PresenceIcons(new Presence(Troops: true), Owner.Empire));
    }
}
