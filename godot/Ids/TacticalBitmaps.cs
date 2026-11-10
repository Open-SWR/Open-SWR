/// <summary>
/// TACTICAL.DLL bitmap ids (HUD frame, right panel) and the type-303 image and palette ids the 3-D scene uses as textures.
/// Pairs by side: Alliance / Empire = the local player's side. Button images: Up, Down (also the checked image).
/// </summary>
public static class TacticalBitmaps
{
    // ---- Frame and HUD ----
    public const int Frame = 1000;
    public const int TaskForcesLabelAlliance = 1001;
    public const int TaskForcesLabelEmpire = 1004;
    public const int TaskForceUp = 1005;
    public const int TaskForceDown = 1006;
    /// <summary>Task-force button of an empty slot, up and down.</summary>
    public const int TaskForceEmpty = 1007;
    public const int FighterSquadronsLabelAlliance = 1008;
    public const int FighterSquadronsLabelEmpire = 1010;
    /// <summary>Fighter group buttons: base + colour (Red, Blue, Green, Gold).</summary>
    public const int FighterGroupUpBase = 1012;
    public const int FighterGroupDownBase = 1016;
    public const int FighterGroupDisabled = 1020;
    public const int SuperlaserUp = 1021;
    public const int SuperlaserDown = 1022;
    public const int SuperlaserDisabled = 1023;
    public const int DeathStarPanel = 1024;
    /// <summary>Nav point set buttons: base + set (0-3).</summary>
    public const int NavPointSetUpBase = 1026;
    public const int NavPointSetDownBase = 1030;
    public const int HighlightAllianceUp = 1034;
    public const int HighlightAllianceDown = 1035;
    public const int HighlightEmpireUp = 1036;
    public const int HighlightEmpireDown = 1037;
    public const int GameOptionsUp = 1038;
    public const int GameOptionsDown = 1039;
    public const int RememberCameraUp = 1040;
    public const int RememberCameraDown = 1041;
    public const int ResetCameraViewUp = 1042;
    public const int ResetCameraViewDown = 1043;
    public const int ZoomInUp = 1044;
    public const int ZoomInDown = 1045;
    public const int ZoomOutUp = 1046;
    public const int ZoomOutDown = 1047;
    public const int RotateLeftUp = 1048;
    public const int RotateLeftDown = 1049;
    public const int RotateRightUp = 1050;
    public const int RotateRightDown = 1051;
    public const int TiltUpUp = 1052;
    public const int TiltUpDown = 1053;
    public const int TiltDownUp = 1055;
    public const int TiltDownDown = 1056;
    public const int ResetCameraSubjectUp = 1058;
    public const int ResetCameraSubjectDown = 1059;
    public const int PauseUp = 1060;
    public const int PauseDown = 1061;

    // ---- Right panel buttons ----
    /// <summary>Right panel button, up frame; the name follows its tooltip (previous ship). Meaning in the original is unknown.</summary>
    public const int PreviousShipUp = 1101;
    /// <summary>Right panel button, down frame; the name follows its tooltip (previous ship). Meaning in the original is unknown.</summary>
    public const int PreviousShipDown = 1102;
    /// <summary>Right panel button, up frame; the name follows its tooltip (next ship). Meaning in the original is unknown.</summary>
    public const int NextShipUp = 1103;
    /// <summary>Right panel button, down frame; the name follows its tooltip (next ship). Meaning in the original is unknown.</summary>
    public const int NextShipDown = 1104;
    /// <summary>Right panel button, up frame; the name follows its tooltip (missions). Meaning in the original is unknown.</summary>
    public const int MissionsUp = 1105;
    /// <summary>Right panel button, down frame; the name follows its tooltip (missions). Meaning in the original is unknown.</summary>
    public const int MissionsDown = 1106;
    /// <summary>Right panel button, up frame; the name follows its tooltip (maneuvers and tactics). Meaning in the original is unknown.</summary>
    public const int ManeuversUp = 1107;
    /// <summary>Right panel button, down frame; the name follows its tooltip (maneuvers and tactics). Meaning in the original is unknown.</summary>
    public const int ManeuversDown = 1108;
    /// <summary>Assign Mission, Assign Maneuver / Tactics and Confirm Withdraw.</summary>
    public const int AssignUp = 1113;
    public const int AssignDown = 1114;
    public const int CancelUp = 1115;
    public const int CancelDown = 1116;
    /// <summary>Tactic toggle: checked = Surround, unchecked = Stand Off.</summary>
    public const int TacticToggleUp = 1117;
    public const int TacticToggleDown = 1118;
    public const int LeftHookUp = 1133;
    public const int LeftHookDown = 1134;
    public const int RightHookUp = 1135;
    public const int RightHookDown = 1136;
    public const int HammerUp = 1137;
    public const int HammerDown = 1138;
    public const int AnvilUp = 1139;
    public const int AnvilDown = 1140;
    public const int StopUp = 1141;
    public const int StopDown = 1142;
    public const int WithdrawUp = 1149;
    public const int WithdrawDown = 1150;
    public const int ImmediateResultUp = 1151;
    public const int ImmediateResultDown = 1152;
    public const int ObserveUp = 1153;
    public const int ObserveDown = 1154;
    /// <summary>Game Options button of the game options page (opens the options screen).</summary>
    public const int OptionsScreenUp = 1155;
    public const int OptionsScreenDown = 1156;
    public const int CloseUp = 1157;
    public const int CloseDown = 1158;
    public const int RecoverAllianceUp = 1170;
    public const int RecoverAllianceDown = 1171;
    public const int RecoverAllianceDisabled = 1172;
    public const int RecoverEmpireUp = 1173;
    public const int RecoverEmpireDown = 1174;
    public const int RecoverEmpireDisabled = 1175;
    /// <summary>Attack Death Star radio, both sides.</summary>
    public const int AttackDeathStarUp = 1176;
    public const int AttackDeathStarDown = 1177;
    public const int AttackDeathStarDisabled = 1178;
    public const int AttackCapitalShipsAllianceUp = 1179;
    public const int AttackCapitalShipsAllianceDown = 1180;
    public const int AttackCapitalShipsEmpireUp = 1182;
    public const int AttackCapitalShipsEmpireDown = 1183;
    public const int AttackFightersAllianceUp = 1191;
    public const int AttackFightersAllianceDown = 1192;
    public const int AttackFightersEmpireUp = 1194;
    public const int AttackFightersEmpireDown = 1195;

    // ---- Right panel ship page: system icons, base + level (0-4) ----
    public const int ShieldRechargeIconBase = 1201;
    public const int WeaponRechargeIconBase = 1206;
    public const int TractorBeamIconBase = 1211;
    public const int EngineIconBase = 1216;
    public const int HyperdriveIconBase = 1221;

    // ---- Right panel page backgrounds ----
    /// <summary>Ship and task-force ship pages.</summary>
    public const int ShipPage = 1302;
    public const int BlankPageAlliance = 1303;
    public const int BlankPageEmpire = 1304;
    public const int GameOptionsPage = 1306;
    public const int FighterGroupPage = 1307;
    public const int MissionsPage = 1308;
    public const int ManeuversPage = 1309;
    public const int WithdrawPage = 1310;

    // ---- Right panel pictures ----
    /// <summary>Unit picture: base + type index.</summary>
    public const int UnitPictureBase = 2001;
    /// <summary>Maneuver diagram: base + (maneuver - 7): left hook, right hook, hammer, anvil, stop.</summary>
    public const int ManeuverDiagramBase = 2101;
    // Mission diagrams (order of 2151-2157 Inferred).
    public const int AttackCapitalShipsDiagramAlliance = 2151;
    public const int RecoverDiagramAlliance = 2152;
    public const int AttackFightersDiagramAlliance = 2153;
    public const int AttackDeathStarDiagram = 2154;
    public const int AttackCapitalShipsDiagramEmpire = 2155;
    public const int RecoverDiagramEmpire = 2156;
    public const int AttackFightersDiagramEmpire = 2157;
    public const int NoMissionDiagram = 2158;

    // ---- Scene effect images (type 303): first frame of each sequence ----
    public const int SparkEffect = 3060;
    public const int HullExplosionEffect = 3120;
    public const int IonSparkEffect = 3180;
    public const int IonOverflowEffect = 3240;
    public const int TorpedoHitEffect = 3300;
    public const int DeathEffect = 3360;
    /// <summary>Loops of 8 frames.</summary>
    public const int GravityWellLoop = 3520;
    public const int TractorLoop = 3620;

    // ---- Fighter images (type 303): blocks by fighter class, chosen by the local player's side ----
    public const int XWingAllianceView = 4000;
    public const int YWingAllianceView = 4000;
    public const int XWingEmpireView = 4004;
    public const int YWingEmpireView = 4014;
    public const int AWingAllianceView = 4020;
    public const int AWingEmpireView = 4024;
    public const int BWingAllianceView = 4030;
    public const int BWingEmpireView = 4034;
    public const int EmpireFighterEmpireView = 4100;
    public const int EmpireFighterAllianceView = 4104;
    public const int EmpireInterceptorEmpireView = 4110;
    public const int EmpireInterceptorAllianceView = 4114;
    public const int EmpireBomberEmpireView = 4120;
    public const int EmpireBomberAllianceView = 4124;
    public const int EmpireDefenderEmpireView = 4130;
    public const int EmpireDefenderAllianceView = 4134;

    // ---- Scene background images (type 303) ----
    public const int DeathStarImage = 5010;
    public const int DeathStarUnfinishedImage = 5020;
    public const int StarfieldImage = 5030;

    // ---- Planets and palettes (type 303) ----
    /// <summary>Install without the 14000 range: planet image base + picture number (1-26).</summary>
    public const int PlainPlanetImageBase = 5500;
    /// <summary>Install without the 15000 range: planet palette base + picture number (1-26).</summary>
    public const int PlainPlanetPaletteBase = 5530;
    /// <summary>Install without the 15000 range: scene palette, byte-equal to <see cref="ScenePalette"/>.</summary>
    public const int PlainScenePalette = 5557;
    /// <summary>Planet image base + picture number (1-200).</summary>
    public const int PlanetImageBase = 14000;
    /// <summary>Planet palette base + picture number (1-200).</summary>
    public const int PlanetPaletteBase = 15000;
    /// <summary>Scene palette of every effect, fighter and background image.</summary>
    public const int ScenePalette = 15001;
}
