using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Live and frozen side views, ObservedBy, system knowledge updates, setup step 24, mission readers.</summary>
public class KnowledgeTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game() => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, 0, 532947196), Definitions);

    // Own objects are live. Core systems are known to both sides. An unrefreshed rim system of the other side shows
    // no owner. Yavin is fully known to the Empire (mode 10, its characters included).
    [GameFact]
    public void SetupKnowledge()
    {
        var game = Game();
        var yavin = game.System(Refs.Yavin);
        var coruscant = game.System(Refs.Coruscant);
        Assert.True(Knowledge.IsLive(coruscant, Owner.Empire));
        Assert.False(Knowledge.IsLive(coruscant, Owner.Alliance));
        Assert.Equal(Owner.Empire, Knowledge.See(game, coruscant, Owner.Alliance).Owner);
        var rim = game.Systems.First(s => Family.Of(s.Type) == 0x92 && s.Owner == Owner.Alliance && s != yavin);
        Assert.Equal(Owner.Neutral, Knowledge.See(game, rim, Owner.Empire).Owner); // fresh view: owner 3
        Assert.Equal(Owner.Alliance, Knowledge.See(game, yavin, Owner.Empire).Owner);
        var luke = game.Characters.First(c => c.Ref == Refs.Luke);
        Assert.Same(yavin, luke.Container);
        Assert.Same(yavin, Knowledge.Location(game, luke, Owner.Empire));
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        Assert.Null(Knowledge.Location(game, vader, Owner.Alliance));
    }

    // A Usable Empire fleet makes an Alliance system and its Completed, non-Enroute units and facilities live for the
    // Empire. Characters never go live this way. Without the fleet they freeze and keep the values seen last.
    [GameFact]
    public void FleetObservesSystem()
    {
        var game = Game();
        var s = game.Systems.First(s => s.Owner == Owner.Alliance && s.Contents.Any(x => Family.IsFacility(x.Type)) && s.Contents.OfType<Character>().Any());
        var facility = s.Contents.First(x => Family.IsFacility(x.Type));
        var character = s.Contents.OfType<Character>().First();
        var fleet = game.Fleets.First(f => f.Owner == Owner.Empire && f.Usable);
        var home = fleet.Container!;
        s.Add(fleet);
        Knowledge.RecomputeSystem(game, s);
        Assert.Equal((true, true, false), (Knowledge.IsLive(s, Owner.Empire), Knowledge.IsLive(facility, Owner.Empire), Knowledge.IsLive(character, Owner.Empire)));

        int support = s.Support;
        home.Add(fleet);
        Knowledge.RecomputeSystem(game, s);
        Assert.False(Knowledge.IsLive(s, Owner.Empire));
        s.Support = support == 0 ? 50 : 0;
        Assert.Equal((support, Owner.Alliance), (Knowledge.See(game, s, Owner.Empire).Support, Knowledge.See(game, s, Owner.Empire).Owner));
    }

    // An unseen character has no location for the other side, so mission creation is refused. A mode 9 update of its
    // system shows it there if it sits directly in that system. A mode 10 update shows it there.
    [GameFact]
    public void UpdateRevealsCharacters()
    {
        var game = Game();
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        var at = vader.System!;
        Assert.Null(Knowledge.Location(game, vader, Owner.Alliance));
        Knowledge.Update(game, at, 9, Owner.Alliance);
        Assert.Equal(vader.Container is StarSystem ? at : null, Knowledge.Location(game, vader, Owner.Alliance));
        Knowledge.Update(game, at, 10, Owner.Alliance);
        Assert.Same(at, Knowledge.Location(game, vader, Owner.Alliance));
    }

    // After setup step 24 every system view is a valid mission target, and its owner is still 3. A never refreshed
    // non-system object, such as Vader for the Alliance, is refused with (0x40, 0x20).
    [GameFact]
    public void CommandTargetCheck()
    {
        var game = Game();
        var rim = game.Systems.First(s => Family.Of(s.Type) == 0x92 && s.Owner != Owner.Empire && !Knowledge.IsLive(s, Owner.Empire) && s.Type != Refs.Yavin);
        Assert.Null(Knowledge.TargetCheck(game, rim, Owner.Empire));
        Assert.Equal((Owner.Neutral, false), (Knowledge.See(game, rim, Owner.Empire).Owner, Knowledge.See(game, rim, Owner.Empire).Populated));
        var vader = game.Characters.First(c => c.Ref == Refs.Vader);
        Assert.Equal(new OrderResult(0x40, 0x20), Knowledge.TargetCheck(game, vader, Owner.Alliance));
    }

    // Setup step 24 gives every frozen manager view the manager's own system as TargetKey, and the view keeps that
    // value when the real one changes. A frozen system view gets the control only from a full refresh or an
    // update that covers it. Step 24 fully refreshes core systems but only positions rim systems, so a rim view's
    // control stays empty.
    [GameFact]
    public void FrozenViewsKeepTargetKeyAndControl()
    {
        var game = Game();
        var m = game.Systems.Where(s => s.Owner == Owner.Empire).Select(s => s.Managers[(int)ManufacturingKind.Ship]).OfType<ManufacturingManager>().First();
        Assert.False(Knowledge.IsLive(m, Owner.Alliance));
        Assert.Same(m.System, Knowledge.TargetKey(m, Owner.Alliance));
        var other = game.Systems.First(s => s != m.System);
        m.TargetKey = other;
        Assert.Same(m.System, Knowledge.TargetKey(m, Owner.Alliance));
        Assert.Same(other, Knowledge.TargetKey(m, Owner.Empire));
        var core = game.Systems.First(s => Family.Of(s.Type) == 0x90 && s.Owner == Owner.Empire && s.Control != default);
        Assert.Equal(core.Control, Knowledge.Control(game, core, Owner.Alliance));
        var rim = game.Systems.First(s => Family.Of(s.Type) == 0x92 && s.Control != default && !Knowledge.IsLive(s, Owner.Alliance));
        Assert.Equal(default, Knowledge.Control(game, rim, Owner.Alliance));
        Knowledge.Update(game, rim, 1, Owner.Alliance);
        Assert.Equal(rim.Control, Knowledge.Control(game, rim, Owner.Alliance));
    }

    static int Posts(GameState game, StarSystem s, bool rise) =>
        game.Messages.Count(m => m.Side == Owner.Alliance && m.Type == 0x14C && m.Object == s && (m.A is not null) == rise);

    static (GameState, Fleet) Watched()
    {
        var game = Game();
        game.Controllers[Owner.Alliance] = new OpenSWR.Sim.Ai.HumanController(Owner.Alliance);
        return (game, game.Fleets.First(f => f.Owner == Owner.Alliance && f.Usable));
    }

    // Rim systems keep the real NeverBeenControlled flag from setup, while the Alliance's view holds a fresh 0. The
    // first Alliance fleet arriving posts 0x14C 0 -> 1 once. A second arrival posts nothing.
    [GameFact]
    public void FirstArrivalInNeutralRimPostsTheRise()
    {
        var (game, fleet) = Watched();
        var s = game.Systems.First(s => Family.Of(s.Type) == 0x92 && s.Owner == Owner.Neutral && s.NeverBeenControlled && !Knowledge.IsLive(s, Owner.Alliance));
        Assert.False(Knowledge.NeverControlled(game, s, Owner.Alliance));
        var home = fleet.Container!;
        for (int i = 0; i < 2; i++)
        {
            s.Add(fleet);
            Knowledge.RecomputeSystem(game, s);
            Assert.True(Knowledge.IsLive(s, Owner.Alliance));
            home.Add(fleet);
            Knowledge.RecomputeSystem(game, s);
        }
        Assert.Equal((1, 0), (Posts(game, s, true), Posts(game, s, false)));
        Assert.True(Knowledge.NeverControlled(game, s, Owner.Alliance));
        OpenSWR.Sim.Ai.Controller.Deliver(game);
        Assert.Equal(1, ((OpenSWR.Sim.Ai.HumanController)game.Controllers[Owner.Alliance]).NeverControlledAlarms);
    }

    // Core systems have the flag cleared before setup step 24's full refresh, so an arrival never posts a rise.
    [GameFact]
    public void CoreSystemNeverPostsTheRise()
    {
        var (game, fleet) = Watched();
        var s = game.Systems.First(s => Family.Of(s.Type) == 0x90 && s.Owner != Owner.Alliance && !Knowledge.IsLive(s, Owner.Alliance));
        s.Add(fleet);
        Knowledge.RecomputeSystem(game, s);
        Assert.True(Knowledge.IsLive(s, Owner.Alliance));
        Assert.Equal(0, Posts(game, s, true));
    }

    // Capturing a never-seen populated rim system: the owner hook's full refresh posts 0x14C 1, then the real flag's
    // clear posts 0x14C 0 through the live copy.
    [GameFact]
    public void CaptureOfNeverSeenRimPostsRiseThenClear()
    {
        var (game, _) = Watched();
        var s = game.Systems.First(s => Family.Of(s.Type) == 0x92 && s.Owner == Owner.Neutral && s.Populated && s.NeverBeenControlled && !Knowledge.IsLive(s, Owner.Alliance));
        Systems.StoreControl(game, s, s.Control with { Owner = Owner.Alliance });
        Assert.Equal(Owner.Alliance, s.Owner);
        Assert.False(s.NeverBeenControlled);
        var posts = game.Messages.Where(m => m.Side == Owner.Alliance && m.Type == 0x14C && m.Object == s).Select(m => m.A is not null).ToList();
        Assert.Equal([true, false], posts);
        Assert.False(Knowledge.NeverControlled(game, s, Owner.Alliance));
    }
}
