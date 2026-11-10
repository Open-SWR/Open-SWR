/// <summary>TEXTTACT.DLL string ids (<see cref="TacticalScreen.Text"/>): tactical HUD and panel tooltips and labels.</summary>
public static class TacticalTexts
{
    // ---- HUD: task forces and fighter groups ----
    // Task-force button digits "1".."8": base + slot.
    public const int TaskForceDigitBase = 44417;
    public const int TaskForcesLabel = 44433;
    public const int UnassignedTaskForce = 44434;
    // Used task-force button tooltips: base + slot.
    public const int TaskForceBase = 44435;
    public const int FighterSquadronsLabel = 44443;
    public const int InactiveFighterGroup = 44444;
    // Fighter group tooltips: base + colour (red, blue, green, gold).
    public const int FighterGroupBase = 44445;

    // ---- HUD: Death Star, nav points, highlight, camera ----
    public const int TargetSuperlaser = 44449;
    // Nav-point set buttons: base + set.
    public const int NavPointSetBase = 44450;
    public const int HighlightAlliance = 44454;
    public const int DimAlliance = 44455;
    public const int HighlightEmpire = 44456;
    public const int DimEmpire = 44457;
    public const int GameOptions = 44458;
    public const int RememberCamera = 44459;
    public const int ResetCameraView = 44461;
    public const int ResetCameraSubject = 44464;
    public const int TiltDown = 44465;
    public const int TiltUp = 44466;
    public const int RotateRight = 44467;
    public const int RotateLeft = 44468;
    public const int ZoomOut = 44469;
    public const int ZoomIn = 44470;
    public const int PauseBattle = 44471;
    public const int ResumeBattle = 44472;

    // ---- Right panel: ship and fighter pages ----
    /// <summary>Tooltip text id for the previous-ship button on a task force ship page. Meaning in the original is unknown.</summary>
    public const int PreviousShipInTaskForce = 56705;
    /// <summary>Tooltip text id for the next-ship button on a task force ship page. Meaning in the original is unknown.</summary>
    public const int NextShipInTaskForce = 56706;
    /// <summary>Tooltip text id for the previous-ship button. Meaning in the original is unknown.</summary>
    public const int PreviousShip = 56708;
    /// <summary>Tooltip text id for the next-ship button. Meaning in the original is unknown.</summary>
    public const int NextShip = 56709;
    // Fighter group names: base + colour.
    public const int GroupNameBase = 56712;
    public const int Missions = 56716;
    public const int HullIntegrity = 56730;
    public const int ShieldStrength = 56731;
    // Followed by the slot number.
    public const int TaskForceNumber = 56732;
    public const int TaskForceUnassigned = 56735;
    public const int AssignMission = 56736;
    public const int Cancel = 56737;

    // ---- Right panel: tactics, options, withdraw ----
    public const int AssignManeuver = 56761;
    public const int Withdraw = 56762;
    public const int ImmediateResult = 56763;
    // Toggle tooltips name both states, the current one in capitals.
    public const int ObserveOrTakeCommand = 56764;
    public const int PanelGameOptions = 56765;
    public const int PanelClose = 56766;
    public const int ConfirmWithdraw = 56770;
    public const int WithdrawQuestion = 56771;
    public const int WithdrawConfirmation = 56772;
    // Fighter count labels: base + (type - 29); other types use GenericFighterCount.
    public const int FighterCountBase = 56773;
    public const int GenericFighterCount = 56777;
    public const int ManeuversAndTactics = 56781;
    public const int StandOffOrSurround = 56782;
    public const int SurroundOrStandOff = 56783;
    public const int LeftHook = 56784;
    public const int RightHook = 56785;
    public const int Hammer = 56786;
    public const int Anvil = 56787;
    public const int Stop = 56788;
    // System power tooltips: base + i (shields, weapons, tractor beam, engines, hyperspace engines).
    public const int SystemPowerBase = 56789;
    public const int Tactics = 56794;
    public const int StandOff = 56795;
    public const int Surround = 56796;
    public const int TakeCommandOrObserve = 56813;

    // ---- View overlay ----
    public const int BattlePaused = 56816;
    public const int ObservingBattle = 56818;
}
