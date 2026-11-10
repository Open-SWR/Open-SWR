using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>The game's victory state: the objective facts of the last recompute, the winner and game over.</summary>
public readonly record struct VictoryStatus
{
    public bool HqGone { get; init; }
    public bool LukeCaptured { get; init; }
    public bool MothmaCaptured { get; init; }
    public bool CoruscantAlliance { get; init; }
    public bool CoruscantDestroyed { get; init; }
    public bool VaderCaptured { get; init; }
    public bool PalpatineCaptured { get; init; }
    public bool EmpireWins { get; init; }
    public bool AllianceWins { get; init; }
    public bool GameOver { get; init; }
}

/// <summary>
/// Victory recompute, its callers and game over.
/// Not modelled: presentation (end movies, score).
/// </summary>
public static class Victory
{
    const uint Mothma = 805306944; // family 48, index 576

    /// <summary>
    /// Recompute: nothing unless Luke, Mon Mothma, Vader, Palpatine and Coruscant resolve. HQgone = the first object of families
    /// [32, 34) in key order is missing or Destroyed. Mode (the victory setting) 0: Empire = Luke and Mothma Captured
    /// and HQgone, Alliance = Vader and Palpatine Captured and (Coruscant owner 1 or Destroyed); 1: Empire = HQgone, Alliance = Coruscant
    /// owner 1 or Destroyed; other modes nothing. Both: the previous winner stays, and no winner means Alliance. Game over = either wins.
    /// </summary>
    public static void Recompute(GameState game)
    {
        if (game.Known.Luke is not { } luke || game.Known.Mothma is not { } mothma || game.Known.Vader is not { } vader || game.Known.Palpatine is not { } pal
            || game.Known.Coruscant is not { } cor) return;
        int mode = game.Settings.VictoryFlag;
        if (mode is not (0 or 1)) return;
        bool hqGone = game.Created.Where(o => Family.Of(o.Type) is >= Families.Headquarters and < Families.IonCannon).MinBy(o => o.Key) is not { Destroyed: false };
        bool corA = cor.Owner == Owner.Alliance, corD = cor.Destroyed;
        bool empire = hqGone && (mode == 1 || luke.Captured && mothma.Captured);
        bool alliance = (corA || corD) && (mode == 1 || vader.Captured && pal.Captured);
        var old = game.VictoryState;
        // both: the previous winner stays when it was the Empire alone, else the Alliance
        bool empireWins = empire && alliance ? old.EmpireWins && !old.AllianceWins : empire;
        bool allianceWins = empire && alliance ? !empireWins : alliance;
        var word = new VictoryStatus
        {
            HqGone = hqGone, LukeCaptured = luke.Captured, MothmaCaptured = mothma.Captured, CoruscantAlliance = corA, CoruscantDestroyed = corD,
            VaderCaptured = vader.Captured, PalpatineCaptured = pal.Captured, EmpireWins = empireWins, AllianceWins = allianceWins,
            GameOver = empireWins || allianceWins,
        };
        game.VictoryState = word;
        // The human side's view copy feeds the advisor's status check (copy path Inferred).
        if (word != old)
            foreach (var h in game.Controllers.Values.OfType<Ai.HumanController>()) h.Advisor?.StatusMask(word);
    }

    /// <summary>
    /// Handler parts for an Existing-changed post (Existing from 1 to 0). A system recomputes. An HQ in families [32, 34) first raises
    /// the HQ-loss wave over its sector [1, HqLossLimit, 1, HqLossDelta] when it is at a system, then recomputes.
    /// </summary>
    internal static void ExistingLost(GameState game, GameObject o, StarSystem? location)
    {
        if (o is StarSystem) Recompute(game);
        else if (Family.Of(o.Type) is >= Families.Headquarters and < Families.IonCannon)
        {
            if (location is not null)
                Systems.Wave(game, location.Sector, Owner.Alliance, Owner.Alliance, game.Rules.HqLossLimit, game.Rules.HqLossDelta);
            Recompute(game);
        }
    }
}
