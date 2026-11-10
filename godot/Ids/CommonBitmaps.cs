/// <summary>COMMON.DLL bitmap ids: the front-end screens (main menu, options / saved games).</summary>
public static class CommonBitmaps
{
    // ---- Main menu ----
    // Galaxy-size lever: rest image per position (size = 3 - position) and the in-between images of a step.
    public const ushort GalaxyLeverHuge = 10001;
    public const ushort GalaxyLeverLarge = 10002;
    public const ushort GalaxyLeverStandard = 10003;
    // Stills of the animated buttons (shown while pressed).
    public const ushort HeadToHeadStill = 10005;
    public const ushort StartAllianceStill = 10007;
    public const ushort StartEmpireStill = 10009;
    public const ushort ExitStill = 10011;
    public const ushort LoadGameStill = 10013;
    public const ushort GalaxyLeverHugeToLarge = 10014;
    public const ushort GalaxyLeverLargeToStandard = 10015;
    // Galaxy-size indicator drawn in the selected bay (opaque).
    public const ushort GalaxySizeHuge = 10017;
    public const ushort GalaxySizeLarge = 10018;
    public const ushort GalaxySizeStandard = 10019;
    public const ushort VictoryToggleUp = 10158;
    public const ushort VictoryToggleDown = 10159;
    // First frames of the animated buttons (15 or 30 frames each).
    public const ushort StartEmpireFrames = 11001;
    public const ushort StartAllianceFrames = 11031;
    public const ushort NoviceFrames = 11061;
    public const ushort IntermediateFrames = 11091;
    public const ushort ExpertFrames = 11121;
    public const ushort HeadToHeadFrames = 11151;
    public const ushort ExitFrames = 11181;
    public const ushort LoadGameFrames = 11241;
    public const ushort CreditsUp = 11271;
    public const ushort CreditsDown = 11272;
    // Difficulty stills, also the checked image of the radio button.
    public const ushort NoviceStill = 11273;
    public const ushort ExpertStill = 11274;
    public const ushort IntermediateStill = 11275;
    public const ushort MainMenuBackground = 20001;

    // ---- Options / saved-games screen ----
    // Return button: up; down = up + 1, disabled = up + 2. By the screen it was opened from.
    public const ushort ReturnAllianceUp = 10020;
    public const ushort ReturnEmpireUp = 10023;
    public const ushort ReturnTacticalUp = 10026;
    public const ushort RestartUp = 10035;
    public const ushort RestartDown = 10036;
    public const ushort ExitUp = 10038;
    public const ushort ExitDown = 10039;
    public const ushort MusicToggleUp = 10040;
    public const ushort MusicToggleChecked = 10041;
    public const ushort MusicToggleDisabled = 10042;
    // Tactical display option toggles (opaque).
    public const ushort DisplayToggleOff = 10043;
    public const ushort DisplayToggleOn = 10044;
    public const ushort DisplayToggleDisabled = 10045;
    public const ushort SaveUp = 10046;
    public const ushort SaveDown = 10047;
    public const ushort SaveDisabled = 10048;
    public const ushort LoadUp = 10049;
    public const ushort LoadDown = 10050;
    public const ushort LoadDisabled = 10051;
    // Sliders and save-slot side icons (opaque).
    public const ushort MusicSliderTrack = 10052;
    public const ushort SoundSliderTrack = 10053;
    public const ushort SliderThumb = 10054;
    public const ushort SaveSideEmpire = 10055;
    public const ushort SaveSideAlliance = 10056;
    public const ushort SaveSideHeadToHead = 10057;
    public const ushort OptionsBackground = 20002;
}
