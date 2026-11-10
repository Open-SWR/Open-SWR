using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>DeathStarNearby, the one-per-system record, planet destruction.</summary>
public class DeathStarTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    static uint DeathStarType => Definitions.CapitalShips.First(r => Family.Of(r.Common.Ref) is >= Families.DeathStar and < Families.Fighter).Common.Ref;

    /// <summary>A Usable Empire fleet moved (placed) into the system, with a completed Death Star aboard.</summary>
    static (Fleet Fleet, GameObject Ship) Arm(GameState game, StarSystem s)
    {
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        s.Add(fleet);
        var ship = Objects.Create(game, DeathStarType, Owner.Empire, fleet, completed: true);
        game.Clock.Flush(game);
        return (fleet, ship);
    }

    // A Usable Death Star marks every Usable system of its sector; other sectors stay clear. A second one is
    // refused only when carried into the system itself
    [GameFact]
    public void NearbyAndOnePerSystem()
    {
        var game = Game();
        var s = game.Systems.First(x => x.Owner == Owner.Alliance && x.Type != Refs.Yavin);
        var (fleet, ship) = Arm(game, s);
        Assert.True(ship.Usable);
        Assert.All(game.Systems.Where(x => x.Usable), x => Assert.Equal(x.Sector == s.Sector, x.DeathStarNearby));
        // a Death Star into a fleet checks only the fleet; a fleet carrying one into the system meets the system's second record
        Assert.True(Fleets.CanPlace(game, fleet, DeathStarType, Owner.Empire, CommandType.MoveObject));
        var other = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable && f != fleet);
        Objects.Create(game, DeathStarType, Owner.Empire, other, completed: true);
        game.Clock.Flush(game);
        s.Owner = Owner.Empire; // owner check out of the way
        Assert.False(Fleets.CanPlace(game, s, other.Type, Owner.Empire, CommandType.MoveObject, unit: other));
        Assert.True(Fleets.CanPlace(game, s, other.Type, Owner.Empire, CommandType.MoveObject, unit: game.Fleets.First(f => f.Owner == Owner.Empire && f != fleet && f != other)));
        s.Owner = Owner.Alliance;
        foreach (var x in other.Contents.Where(x => DeathStar.IsDeathStar(x.Type)).ToList()) Objects.Destroy(game, x, DestroyedReason.Scrapped);

        Objects.Destroy(game, ship, DestroyedReason.Battle);
        game.Clock.Flush(game);
        Assert.All(game.Systems.Where(x => x.Usable), x => Assert.False(x.DeathStarNearby));
    }

    // BombardDestroy with a Death Star: the system is destroyed (Bombardment, not the attacker's), its troops and
    // facilities with it, fleets stay, characters go adrift, one chance(G DestroyedPlanetWoundChance) per minor character; no shots;
    // the attacker's -20 applies; Populated clears later
    [GameFact]
    public void PlanetDestruction()
    {
        var game = Game();
        var s = game.Systems.First(x => x.Owner == Owner.Alliance && x.Populated && x.Contents.Any(y => Family.IsTroop(y.Type))
            && x.Contents.Any(y => Family.IsFacility(y.Type)) && x.Type != Refs.Yavin);
        var minor = game.Characters.First(c => c.Owner == Owner.Alliance && !c.Placed && Family.Of(c.Type) >= Families.MinorCharacter);
        s.Add(minor);
        minor.SetSetupFlags();
        var major = game.Characters.First(c => c.Owner == Owner.Alliance && c.Placed && c.Usable && Family.Of(c.Type) < Families.MinorCharacter && !c.OnMission);
        s.Add(major);
        var units = s.Contents.Where(x => (Family.IsTroop(x.Type) || Family.IsFacility(x.Type)) && x.Completed).ToList();
        var (fleet, _) = Arm(game, s);
        fleet.Bombard = true;
        int support = s.Support;
        var draws = game.Random.Trace = [];
        Combat.Bombard(game, s, CommandType.BombardDestroy);
        Assert.True(s.Support > support || support == 100); // -20 for the attacker (Alliance share up), still Populated then
        Assert.Equal((true, DestroyedReason.Bombardment), (s.Destroyed, s.DestroyedReason)); // owned by the defender (OwnBombardment only for the attacker's own)
        Assert.All(units, u => Assert.Equal((true, DestroyedReason.Bombardment), (u.Destroyed, u.DestroyedReason)));
        Assert.False(fleet.Destroyed);
        Assert.True(major.Adrift);
        Assert.Equal(DrawSite.PlanetMinor, draws[0].Site);
        Assert.DoesNotContain(draws, d => d.Site == DrawSite.BombardShot);
        game.Clock.Flush(game);
        Assert.Same(s, fleet.Container);
        Assert.False(s.Populated); // a destroyed facility's usable notification
        // destroyed system: agents refused under CreateMoveMission (so never a regroup destination), forgiven under CreateMission
        Assert.False(Fleets.CanPlace(game, s, major.Type, Owner.Alliance, CommandType.CreateMoveMission));
        Assert.True(Fleets.CanPlace(game, s, major.Type, Owner.Alliance, CommandType.CreateMission));
        Assert.NotSame(s, major.Container);
    }
}
