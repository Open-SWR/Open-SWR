/// <summary>TEXTSTRA.DLL message template ids (RCDATA with placeholder slots, <see cref="Assets.Message"/>) that only the UI uses; the sim's are <see cref="OpenSWR.Sim.TextId"/>.</summary>
public static class MessageTemplates
{
    // ---- Battle report window ----
    public const int BattleResultTitle = 28673;
    public const int FleetVictorious = 28674;
    public const int FleetDefeated = 28675;
    public const int BattleIndecisive = 28676;
    public const int SystemDefended = 28677;
    public const int SystemBlockaded = 28678;
    public const int SystemCleared = 28679;
    // Same text as SystemBlockaded, used for the attacking side's result.
    public const int SystemBlockadedByAttack = 28680;
    public const int AttackFailed = 28681;
    public const int NeutralWorldVictory = 28682;
    public const int FleetDestroyed = 28683;
    public const int FleetWithdrawn = 28684;
    public const int FleetWithdrawnTo = 28685;
    public const int NoVictor = 28686;
    public const int AllShipsDestroyed = 28687;
    public const int DefeatedFleetDestroyed = 28688;
    public const int DefeatedFleetWithdrawnTo = 28689;
    public const int VictoriousBlockade = 28690;
    // Footer above the go-to buttons: several buttons / one button.
    public const int GoToButtons = 28691;
    public const int GoToButton = 28692;
    public const int BlockadeMaintained = 28693;
    public const int BlockadeBroken = 28694;

    // ---- Battle start window ----
    public const int BattleStartTitle = 28704;
    public const int FleetThreatening = 28705;
    public const int BreakBlockadeOf = 28706;
    public const int FleetEnteredSystem = 28707;
    public const int BreakBlockadeAt = 28708;
    public const int ForcesAboutToEngage = 28709;

    // ---- Confirmations ----
    public const int ConfirmScrap = 28752;
    public const int ConfirmRetire = 28753;
    public const int ConfirmAbortMission = 28754;
    public const int ConfirmStopConstruction = 28755;
    // One line per target: line feed + name.
    public const int ConfirmTargetLine = 28756;
    // One line per manager: line feed, queue count, product name.
    public const int ConfirmManagerLine = 28757;
    public const int BlockadeMoveWarning = 28758;
    public const int TransitDays = 28759;
}
