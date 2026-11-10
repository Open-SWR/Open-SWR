using OpenSWR.Data.Dat;
using OpenSWR.Sim;

namespace OpenSWR.Tests;

public class CommandTests
{
    static GameDefinitions? _definitions;
    static GameDefinitions Definitions => _definitions ??= GameDefinitions.Load(GameDir.Path);

    /// <summary>
    /// Every CompletionStatus is stored through a virtual setter, which consumes one command serial (statuses 1 and
    /// 5 included); with a status already set the phase check does nothing.
    /// </summary>
    [GameFact]
    public void CompletionStatusConsumesASerial()
    {
        var game = NewGame.Create(new GameSettings(Owner.Alliance, 1, 0, 0, 532947196), Definitions);
        Mission Move()
        {
            var c = game.Characters.First(c => c.Owner == Owner.Alliance && c.Usable && c.Mission is null && c.System is { Owner: Owner.Alliance });
            return Missions.Create(game, Refs.MoveMission, Owner.Alliance, c.System!, [c], [], 0, mandatory: false);
        }
        var done = Move();
        done.Phase = 11;
        uint serial = game.Clock.Serial;
        Missions.OwnerChanged(game, done.Objective!);
        Assert.Equal((MissionCompletion.Done, serial + 1), (done.CompletionStatus, game.Clock.Serial));
        Missions.OwnerChanged(game, done.Objective!);
        Assert.Equal(serial + 1, game.Clock.Serial);

        var empty = Move();
        empty.Team[0].ResignRequest = true;
        serial = game.Clock.Serial;
        Missions.OwnerChanged(game, empty.Objective!);
        Assert.Equal((MissionCompletion.NoTeam, 11, serial + 1), (empty.CompletionStatus, empty.Phase, game.Clock.Serial));
    }

    /// <summary>
    /// Run 3 day 39 (Han + Chewbacca espionage at Coruscant): foil p = 35 for inputs [95, 0, 2, 10 | 12, 0, 1];
    /// decoy p 96 / 96 / 95 for key 90 - D (D = 10, 1, 12); Han's injury check from the traced generator state: chance(1) r = 0,
    /// then roll(1) = 0, roll(29) = 24, + 1 gives Injury 25.
    /// </summary>
    [GameFact]
    public void EncounterFormulasOfRun3()
    {
        var game = new GameState(new GameSettings(Owner.Alliance, 1, 0, 0, 1570241797), Definitions);
        Assert.Equal(35, Missions.FoilChance(game, 95, 0, 10, 0));
        Assert.Equal(35, Missions.FoilChance(game, 95, 0, 12, 0));
        Assert.Equal([96, 96, 95], new[] { 10, 1, 12 }.Select(d => Missions.DecoyChance(game, 90, 0, d, fleet: true)));
        var han = new Character(Definitions.MajorCharacters.First(c => c.Common.Ref == Refs.Han), Owner.Alliance);
        han.Skills[SkillIndex.Combat] = 100;
        var draws = new List<(string, int, int)>();
        game.Random.Trace = draws;
        Missions.InjuryCheck(game, han);
        Assert.Equal(25, han.Injury);
        Assert.Equal([(DrawSite.Injury, 100, 0), (DrawSite.Injury, 2, 0), (DrawSite.Injury, 30, 24)], draws);
    }

    // isqrt' quirk; verified exhaustively in the original
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    [InlineData(15, 3)]
    [InlineData(16, 3)]
    [InlineData(17, 4)]
    [InlineData(64, 7)]
    [InlineData(100, 10)]
    [InlineData(1048576, 1023)]
    [InlineData(1048577, 1024)]
    [InlineData(7417, 86)] // Coruscant (423, 392) gives (462, 315): 39^2 + 77^2
    public void IsqrtQuirk(int n, int expected) => Assert.Equal(expected, Orders.Isqrt(n));
}
