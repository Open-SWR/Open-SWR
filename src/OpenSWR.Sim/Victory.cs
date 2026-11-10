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
    const uint Mothma = 0x30000240;

    /// <summary>
    /// Recompute: nothing unless Luke, Mon Mothma, Vader, Palpatine and Coruscant resolve. HQgone = the first object of families
    /// [0x20, 0x22) in key order is missing or Destroyed. Mode (the victory setting) 0: Empire = Luke and Mothma Captured
    /// and HQgone, Alliance = Vader and Palpatine Captured and (Coruscant owner 1 or Destroyed); 1: Empire = HQgone, Alliance = Coruscant
    /// owner 1 or Destroyed; other modes nothing. Both: the previous winner stays, and no winner means Alliance. Game over = either wins.
    /// </summary>
    public static void Recompute(GameState game)
    {
        Character? C(uint r) => game.Characters.FirstOrDefault(c => c.Ref == r);
        if (C(Refs.Luke) is not { } luke || C(Mothma) is not { } mothma || C(Refs.Vader) is not { } vader || C(Refs.Palpatine) is not { } pal
            || game.Systems.FirstOrDefault(s => s.Type == Refs.Coruscant) is not { } cor) return;
        int mode = game.Settings.VictoryFlag;
        if (mode is not (0 or 1)) return;
        bool hqGone = game.Created.Where(o => Family.Of(o.Type) is >= 0x20 and < 0x22).MinBy(o => o.Key) is not { Destroyed: false };
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
    /// Handler parts for a 0x300 post (Existing from 1 to 0). A system recomputes. An HQ in families [0x20, 0x22) first raises
    /// the HQ-loss wave over its sector [1, G 0x1408, 1, G 0x1407] when it is at a system, then recomputes.
    /// </summary>
    internal static void ExistingLost(GameState game, GameObject o, StarSystem? location)
    {
        if (o is StarSystem) Recompute(game);
        else if (Family.Of(o.Type) is >= 0x20 and < 0x22)
        {
            if (location is not null)
                Systems.Wave(game, location.Sector, Owner.Alliance, Owner.Alliance, game.Params.General(0x1408), game.Params.General(0x1407));
            Recompute(game);
        }
    }
}
