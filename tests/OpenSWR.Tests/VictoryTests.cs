using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

/// <summary>Victory recompute (modes 0 / 1, tie rule), callers, game over.</summary>
public class VictoryTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    static GameState Game(int mode) => NewGame.Create(new GameSettings(Owner.Empire, 1, 0, mode, 532947196), Definitions);

    static Character C(GameState game, uint sdRef) => game.Characters.First(c => c.Ref == sdRef);

    // Headquarters mode: the Alliance HQ destroyed (its 0x300) gives an Empire win and game over. The clock stops.
    [GameFact]
    public void HeadquartersLost()
    {
        var game = Game(1);
        var hq = game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key)!;
        Objects.Destroy(game, hq, 0x14);
        game.Clock.Flush(game);
        Assert.Equal((true, Owner.Empire), (game.GameOver, game.Winner));
        Assert.True(game.VictoryState.HqGone);
        int day = game.Day;
        game.Clock.RunUntilDay(game, day + 5);
        Assert.Equal(day, game.Day);
    }

    // Standard mode: Vader and Palpatine captured and Coruscant Alliance-owned gives an Alliance win. With the Empire
    // condition also true, the previous winner stays.
    [GameFact]
    public void StandardModeAndTie()
    {
        var game = Game(0);
        var yavin = game.System(Refs.Yavin); // held in Alliance territory (else the owner check releases them)
        yavin.Add(C(game, Refs.Vader));
        yavin.Add(C(game, Refs.Palpatine));
        Prisoners.Capture(game, C(game, Refs.Vader), null);
        Prisoners.Capture(game, C(game, Refs.Palpatine), null);
        game.Clock.Flush(game);
        Assert.False(game.GameOver);
        game.System(Refs.Coruscant).Owner = Owner.Alliance;
        Victory.Recompute(game);
        Assert.Equal(Owner.Alliance, game.Winner);

        var coruscant = game.System(Refs.Coruscant);
        coruscant.Owner = Owner.Empire; // Luke and Mon Mothma held in Empire territory
        coruscant.Add(C(game, Refs.Luke));
        coruscant.Add(C(game, 0x30000240));
        Prisoners.Capture(game, C(game, Refs.Luke), null);
        Prisoners.Capture(game, C(game, 0x30000240), null);
        game.Clock.Flush(game);
        coruscant.Owner = Owner.Alliance;
        Objects.Destroy(game, game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key)!, 0x14);
        Victory.Recompute(game);
        Assert.Equal((Owner.Alliance, true), (game.Winner, game.VictoryState is { LukeCaptured: true, MothmaCaptured: true, HqGone: true }));
    }
}
