/// <summary>
/// STRATEGY.DLL bitmap ids: the strategy screen, its object windows and the 470 x 331 panel windows.
/// Pairs by side: Alliance / Empire = the player's side unless a comment says otherwise. Button images: Up, Down, Disabled.
/// </summary>
public static class StrategyBitmaps
{
    // ---- Strategy screen ----
    public const ushort FrameAlliance = 900;
    public const ushort FrameEmpire = 901;
    public const ushort Galaxy = 902;
    /// <summary>Galaxy background while a map display filter is on.</summary>
    public const ushort MapFilterBackground = 903;

    // ---- Frame buttons ----
    public const ushort SystemFinderAllianceDown = 10001;
    public const ushort SystemFinderAllianceUp = 10002;
    public const ushort FleetFinderAllianceDown = 10003;
    public const ushort FleetFinderAllianceUp = 10004;
    public const ushort PersonnelFinderAllianceDown = 10005;
    public const ushort PersonnelFinderAllianceUp = 10006;
    public const ushort TroopFinderAllianceDown = 10007;
    public const ushort TroopFinderAllianceUp = 10008;
    public const ushort OpenEncyclopediaAllianceDown = 10009;
    public const ushort OpenEncyclopediaAllianceUp = 10010;
    public const ushort GalacticInformationDisplayAllianceDown = 10011;
    public const ushort GalacticInformationDisplayAllianceUp = 10012;
    public const ushort GameControlsAllianceDown = 10013;
    public const ushort GameControlsAllianceUp = 10014;
    public const ushort SystemFinderEmpireDown = 10015;
    public const ushort SystemFinderEmpireUp = 10016;
    public const ushort FleetFinderEmpireDown = 10017;
    public const ushort FleetFinderEmpireUp = 10018;
    public const ushort PersonnelFinderEmpireDown = 10019;
    public const ushort PersonnelFinderEmpireUp = 10020;
    public const ushort TroopFinderEmpireDown = 10021;
    public const ushort TroopFinderEmpireUp = 10022;
    public const ushort OpenEncyclopediaEmpireDown = 10023;
    public const ushort OpenEncyclopediaEmpireUp = 10024;
    public const ushort GalacticInformationDisplayEmpireDown = 10025;
    public const ushort GalacticInformationDisplayEmpireUp = 10026;
    public const ushort GameControlsEmpireDown = 10027;
    public const ushort GameControlsEmpireUp = 10028;

    // ---- Message-category buttons of the frame (Lit = a new message, Dark = none) ----
    public const ushort SupportMessagesEmpireDark = 10030;
    public const ushort FleetMessagesEmpireDark = 10031;
    public const ushort MissionMessagesEmpireDark = 10032;
    public const ushort ResourceMessagesEmpireDark = 10033;
    public const ushort ManufacturingMessagesEmpireDark = 10034;
    public const ushort DefenseMessagesEmpireDark = 10035;
    public const ushort ConflictMessagesEmpireDark = 10036;
    public const ushort AdviceMessagesEmpireDark = 10037;
    public const ushort ChatMessagesEmpireDark = 10038;
    public const ushort SupportMessagesEmpireLit = 10040;
    public const ushort FleetMessagesEmpireLit = 10041;
    public const ushort MissionMessagesEmpireLit = 10042;
    public const ushort ResourceMessagesEmpireLit = 10043;
    public const ushort ManufacturingMessagesEmpireLit = 10044;
    public const ushort DefenseMessagesEmpireLit = 10045;
    public const ushort ConflictMessagesEmpireLit = 10046;
    public const ushort AdviceMessagesEmpireLit = 10047;
    public const ushort ChatMessagesEmpireLit = 10048;
    public const ushort SupportMessagesAllianceDark = 10050;
    public const ushort FleetMessagesAllianceDark = 10051;
    public const ushort MissionMessagesAllianceDark = 10052;
    public const ushort ResourceMessagesAllianceDark = 10053;
    public const ushort ManufacturingMessagesAllianceDark = 10054;
    public const ushort DefenseMessagesAllianceDark = 10055;
    public const ushort ConflictMessagesAllianceDark = 10056;
    public const ushort AdviceMessagesAllianceDark = 10057;
    public const ushort ChatMessagesAllianceDark = 10058;
    public const ushort SupportMessagesAllianceLit = 10060;
    public const ushort FleetMessagesAllianceLit = 10061;
    public const ushort MissionMessagesAllianceLit = 10062;
    public const ushort ResourceMessagesAllianceLit = 10063;
    public const ushort ManufacturingMessagesAllianceLit = 10064;
    public const ushort DefenseMessagesAllianceLit = 10065;
    public const ushort ConflictMessagesAllianceLit = 10066;
    public const ushort AdviceMessagesAllianceLit = 10067;
    public const ushort ChatMessagesAllianceLit = 10068;

    // ---- Shared 2-px window frame and window title buttons (14 x 14) ----
    public const ushort WindowFrameTopRight = 10100;
    public const ushort WindowFrameTopLeft = 10101;
    public const ushort WindowFrameBottomRight = 10102;
    public const ushort WindowFrameBottomLeft = 10103;
    public const ushort WindowFrameRight = 10104;
    public const ushort WindowFrameTop = 10105;
    public const ushort WindowFrameBottom = 10106;
    public const ushort WindowFrameLeft = 10107;
    public const ushort WindowCloseUp = 10108;
    public const ushort WindowCloseDown = 10109;
    public const ushort GoToSectorDown = 10208;
    public const ushort GoToSectorUp = 10209;
    /// <summary>Sector window: move the window to the other sector-window slot.</summary>
    public const ushort SwapSectorSlotUp = 10210;
    public const ushort SwapSectorSlotDown = 10211;
    public const ushort MinimiseUp = 10253;
    public const ushort MinimiseDown = 10254;

    // ---- Map display filters: picker row icons, legend button ----
    // Several icons serve more than one row (e.g. Fleets is also Idle Fleets); the name is one of its rows.
    public const ushort LoyaltyFilterAlliance = 10110;
    public const ushort FleetsFilterAlliance = 10111;
    public const ushort PersonnelFilterAlliance = 10112;
    public const ushort ResourcesFilterAlliance = 10113;
    public const ushort ManufacturingFilterAlliance = 10114;
    public const ushort DefenseFilterAlliance = 10115;
    /// <summary>Submenu arrow of the group rows, normal and highlighted.</summary>
    public const ushort SubmenuArrowAlliance = 10117;
    public const ushort SubmenuArrowHighlightedAlliance = 10118;
    public const ushort LoyaltyFilterEmpire = 10119;
    public const ushort FleetsFilterEmpire = 10120;
    /// <summary>Also the Idle Personnel row.</summary>
    public const ushort PersonnelFilterEmpire = 10121;
    public const ushort ResourcesFilterEmpire = 10122;
    public const ushort ManufacturingFilterEmpire = 10123;
    /// <summary>Also the Planetary Shield Generators row.</summary>
    public const ushort DefenseFilterEmpire = 10124;
    public const ushort FighterSquadronsFilterEmpire = 10126;
    /// <summary>Also the Active Personnel row.</summary>
    public const ushort TroopersFilterEmpire = 10127;
    public const ushort SubmenuArrowEmpire = 10128;
    public const ushort SubmenuArrowHighlightedEmpire = 10129;
    /// <summary>Shipyards and Idle Shipyards.</summary>
    public const ushort ShipyardsFilter = 10131;
    public const ushort TrainingFacilitiesFilter = 10132;
    public const ushort ConstructionYardsFilter = 10133;
    public const ushort AvailableEnergyFilter = 10134;
    public const ushort AvailableRawMaterialFilter = 10135;
    public const ushort MinesFilter = 10136;
    public const ushort RefineriesFilter = 10137;
    public const ushort IdlePersonnelFilterAlliance = 10138;
    /// <summary>Also the Active Personnel row.</summary>
    public const ushort TroopersFilterAlliance = 10140;
    public const ushort FighterSquadronsFilterAlliance = 10141;
    public const ushort DeathStarShieldsFilter = 10142;
    public const ushort ShieldGeneratorsFilterAlliance = 10143;
    /// <summary>White dot of an unexplored or destroyed system while a filter is on.</summary>
    public const ushort HiddenSystemDot = 10158;
    public const ushort LegendButton = 10168;
    public const ushort UprisingsFilterAlliance = 11608;
    public const ushort UprisingsFilterEmpire = 11609;
    public const ushort DefenseBatteriesFilter = 11610;
    public const ushort FleetsEnrouteFilterAlliance = 11613;
    public const ushort FleetsEnrouteFilterEmpire = 11614;

    // ---- Sector window ----
    public const ushort HeadquartersOverlay = 904;
    public const ushort UprisingOverlayAlliance = 905;
    public const ushort UprisingOverlayEmpire = 906;
    /// <summary>System pictures of an unmodded install: p 1-23 = base + p, p 24-26 = high base + p.</summary>
    public const ushort PlainSystemPictureBase = 10211;
    public const ushort PlainSystemPictureHighBase = 10213;
    public const ushort DestroyedSystem = 10240;
    // Sector-window icons by colour side, per mode facilities / planet units / fleets / missions: normal = base + 2 * mode index,
    // selected = normal + 1. The first of each side is the base. Also the window-slot icons of the system windows.
    public const ushort FacilitiesIconAlliance = 10771;
    public const ushort PlanetUnitsIconAlliance = 10773;
    public const ushort FleetsIconAlliance = 10775;
    public const ushort MissionsIconAlliance = 10777;
    public const ushort FacilitiesIconEmpire = 10779;
    public const ushort FacilitiesIconNeutral = 10787;
    /// <summary>System picture of the 25th Anniversary install: base + p (p 1-200).</summary>
    public const ushort SystemPictureBase = 14000;

    // ---- System windows: owner skins (by the system's view owner) ----
    public const ushort TitleStripAlliance = 10200;
    public const ushort TitleStripEmpireFocused = 10201;
    public const ushort TitleStripAllianceFocused = 10299;
    public const ushort TitleStripEmpire = 10302;
    public const ushort TitleStripNeutralFocused = 10303;
    public const ushort TitleStripNeutral = 10304;

    // ---- System manufacturing window ----
    /// <summary>Empty deposit cell of the Mines tab.</summary>
    public const ushort EmptyDeposit = 9005;
    /// <summary>Selected facility cell frame, by owner.</summary>
    public const ushort FacilitySelectedAlliance = 10262;
    public const ushort FacilitySelectedEmpire = 10263;
    public const ushort FacilitySelectedNeutral = 10264;
    /// <summary>Manager row header bar by owner: selected; + 1 not selected.</summary>
    public const ushort ManagerHeaderAlliance = 10291;
    public const ushort ManagerHeaderEmpire = 10293;
    public const ushort ManagerHeaderNeutral = 10295;
    public const ushort ManufacturingWindowBackground = 10297;
    /// <summary>Left strip of the Manufacturing tab: facility pictures and count boxes.</summary>
    public const ushort FacilityCountStrip = 10298;
    /// <summary>Manager row drag image for 1 selected row; + 1 for 2, + 2 for 3 or more.</summary>
    public const ushort ManagerDragAlliance = 10305;
    public const ushort ManagerDragEmpire = 10308;
    /// <summary>Manufacturing tab up image by owner; down = up - 1.</summary>
    public const ushort ManufacturingTabAllianceUp = 10312;
    public const ushort ManufacturingTabEmpireUp = 10315;
    public const ushort ManufacturingTabNeutralUp = 10318;
    // Facility tabs: Up = facilities there, None = no facilities.
    public const ushort MinesTabDown = 10320;
    public const ushort MinesTabUp = 10321;
    public const ushort MinesTabNone = 10322;
    public const ushort RefineriesTabDown = 10323;
    public const ushort RefineriesTabUp = 10324;
    public const ushort RefineriesTabNone = 10325;
    public const ushort ShipyardsTabDown = 10326;
    public const ushort ShipyardsTabUp = 10327;
    public const ushort ShipyardsTabNone = 10328;
    public const ushort TrainingFacilitiesTabDown = 10329;
    public const ushort TrainingFacilitiesTabUp = 10330;
    public const ushort TrainingFacilitiesTabNone = 10331;
    public const ushort ConstructionYardsTabDown = 10332;
    public const ushort ConstructionYardsTabUp = 10333;
    public const ushort ConstructionYardsTabNone = 10334;

    // ---- Fleet window ----
    /// <summary>Selected fleet / ship row, by the fleet's owner.</summary>
    public const ushort FleetRowSelectedAlliance = 10401;
    /// <summary>Starfield panel over the right side while a fleet is selected, by panel side.</summary>
    public const ushort FleetPanelAlliance = 10407;
    // Tabs Ships, Fighter Squadrons, Troops, Characters: base + tab; disabled up = up + 3.
    public const ushort FleetTabAllianceUpBase = 10409;
    public const ushort FleetTabAllianceDownBase = 10416;
    public const ushort ContentsSelectedAlliance = 10420;
    public const ushort FleetPictureAlliance = 10425;
    public const ushort FleetEnrouteOverlayAlliance = 10426;
    public const ushort FleetDamagedOverlayAlliance = 10427;
    public const ushort FleetRowSelectedEmpire = 10451;
    public const ushort FleetPanelEmpire = 10457;
    public const ushort FleetTabEmpireUpBase = 10459;
    public const ushort FleetTabEmpireDownBase = 10466;
    public const ushort ContentsSelectedEmpire = 10470;
    public const ushort FleetPictureEmpire = 10475;
    public const ushort FleetEnrouteOverlayEmpire = 10476;
    public const ushort FleetDamagedOverlayEmpire = 10477;
    public const ushort FleetWindowBackground = 10770;

    // ---- System planet window ----
    public const ushort BatteriesTabUp = 10550;
    public const ushort BatteriesTabDown = 10551;
    public const ushort BatteriesTabNone = 10552;
    public const ushort ShieldsTabUp = 10553;
    public const ushort ShieldsTabDown = 10554;
    public const ushort ShieldsTabNone = 10555;
    // Neutral (grey) tab images; by skin owner: Alliance up = id - 6, none = id - 4, down = id - 5;
    // Empire up = id - 3, none = id - 1, down = id - 2.
    public const ushort FighterSquadronsTabNeutral = 10562;
    public const ushort TrooperRegimentsTabNeutral = 10569;
    public const ushort PersonnelTabNeutral = 10576;
    public const ushort PlanetWindowBackground = 10577;
    /// <summary>Selected cell frame: owner Alliance (red); Empire (green) also for neutral.</summary>
    public const ushort PlanetCellSelectedAlliance = 10578;
    public const ushort PlanetCellSelectedEmpire = 10579;

    // ---- System missions window ----
    public const ushort MissionsWindowBackground = 11165;
    public const ushort AgentsTabUp = 11560;
    public const ushort AgentsTabDown = 11561;
    public const ushort DecoysTabUp = 11562;
    public const ushort DecoysTabDown = 11563;

    // ---- Panel windows (470 x 331: battle, report, strike, messages, finders, encyclopedia) ----
    public const ushort PanelFrameAlliance = 10335;
    public const ushort PanelFrameEmpire = 10336;
    /// <summary>Close button of the side strip (OK in the battle report and strike windows).</summary>
    public const ushort PanelCloseAllianceUp = 10370;
    public const ushort PanelCloseAllianceDown = 10371;
    /// <summary>Index (encyclopedia), Message Index (message display mode).</summary>
    public const ushort IndexAllianceUp = 10372;
    public const ushort IndexAllianceDown = 10373;
    public const ushort DisplayTopicAllianceUp = 10374;
    public const ushort DisplayTopicAllianceDown = 10375;
    public const ushort PanelCloseEmpireUp = 10376;
    public const ushort PanelCloseEmpireDown = 10377;
    public const ushort IndexEmpireUp = 10378;
    public const ushort IndexEmpireDown = 10379;
    public const ushort DisplayTopicEmpireUp = 10380;
    public const ushort DisplayTopicEmpireDown = 10381;
    /// <summary>Display / go-to button (finders, message window, the battle report's display tab of the Empire).</summary>
    public const ushort DisplayAllianceUp = 10518;
    public const ushort DisplayAllianceDown = 10519;
    public const ushort DisplayEmpireUp = 10520;
    public const ushort DisplayEmpireDown = 10521;
    /// <summary>Side strip right of the picture: battle, report, strike and message windows.</summary>
    public const ushort PanelStripAlliance = 10820;
    public const ushort PanelStripEmpire = 10821;

    // ---- Category buttons (49 x 41): report / strike unit filters, encyclopedia topics ----
    public const ushort AllDatabasesCategoryDown = 10339;
    public const ushort AllDatabasesCategoryUp = 10340;
    /// <summary>Facilities topic; Defences filter of the strike window.</summary>
    public const ushort FacilitiesCategoryDown = 10341;
    public const ushort FacilitiesCategoryUp = 10342;
    /// <summary>Missions topic; Manufacturing filter of the strike window (Alliance).</summary>
    public const ushort MissionsCategoryDown = 10343;
    public const ushort MissionsCategoryUp = 10344;
    public const ushort PersonnelCategoryAllianceDown = 10345;
    public const ushort PersonnelCategoryAllianceUp = 10346;
    public const ushort CapitalShipsCategoryAllianceDown = 10347;
    public const ushort CapitalShipsCategoryAllianceUp = 10348;
    public const ushort SystemsCategoryDown = 10349;
    public const ushort SystemsCategoryUp = 10350;
    public const ushort TroopsCategoryAllianceDown = 10351;
    public const ushort TroopsCategoryAllianceUp = 10352;
    /// <summary>Fighters filter; also the Ship Database topic (Alliance).</summary>
    public const ushort FightersCategoryAllianceDown = 10353;
    public const ushort FightersCategoryAllianceUp = 10354;
    public const ushort ManufacturingCategoryEmpireDown = 10355;
    public const ushort ManufacturingCategoryEmpireUp = 10356;
    public const ushort PersonnelCategoryEmpireDown = 10357;
    public const ushort PersonnelCategoryEmpireUp = 10358;
    /// <summary>Capital Ships filter; also the Ship Database topic (Empire).</summary>
    public const ushort CapitalShipsCategoryEmpireDown = 10359;
    public const ushort CapitalShipsCategoryEmpireUp = 10360;
    public const ushort TroopsCategoryEmpireDown = 10361;
    public const ushort TroopsCategoryEmpireUp = 10362;
    public const ushort FightersCategoryEmpireDown = 10363;
    public const ushort FightersCategoryEmpireUp = 10364;

    // ---- Encyclopedia ----
    public const ushort EncyclopediaEntryPage = 10337;
    public const ushort EncyclopediaIndexPage = 10338;
    public const ushort NextTopicUp = 10382;
    public const ushort NextTopicDown = 10383;
    public const ushort PreviousTopicUp = 10385;
    public const ushort PreviousTopicDown = 10386;
    public const ushort EncyclopediaStripAlliance = 10585;
    public const ushort EncyclopediaStripEmpire = 10589;

    // ---- Finders ----
    // Filter buttons: All (systems, fleets), Alliance, Empire, Neutral, Unexplored.
    public const ushort FinderAllUp = 10500;
    public const ushort FinderAllDown = 10501;
    public const ushort FinderAllianceUp = 10502;
    public const ushort FinderAllianceDown = 10503;
    public const ushort FinderEmpireUp = 10505;
    public const ushort FinderEmpireDown = 10506;
    public const ushort FinderNeutralUp = 10508;
    public const ushort FinderNeutralDown = 10509;
    public const ushort FinderUnexploredUp = 10511;
    public const ushort FinderUnexploredDown = 10512;
    public const ushort FinderCloseAllianceUp = 10514;
    public const ushort FinderCloseAllianceDown = 10515;
    public const ushort FinderCloseEmpireUp = 10516;
    public const ushort FinderCloseEmpireDown = 10517;
    public const ushort SystemFinderPage = 10522;
    public const ushort ShipFinderPageAlliance = 10524;
    public const ushort ShipFinderPageEmpire = 10525;
    public const ushort FleetFinderPageAlliance = 10526;
    public const ushort FleetFinderPageEmpire = 10527;
    // Fleet Finder modes and Personnel Finder categories on the side strip.
    public const ushort FleetModeAllianceUp = 10528;
    public const ushort FleetModeAllianceDown = 10529;
    public const ushort ShipModeAllianceUp = 10530;
    public const ushort ShipModeAllianceDown = 10531;
    public const ushort FleetModeEmpireUp = 10532;
    public const ushort FleetModeEmpireDown = 10533;
    public const ushort ShipModeEmpireUp = 10534;
    public const ushort ShipModeEmpireDown = 10535;
    // Personnel and Troop Finder pages by filter: Alliance, Empire.
    public const ushort CharactersPageAlliance = 10536;
    public const ushort CharactersPageEmpire = 10537;
    public const ushort SpecialForcesPageAlliance = 10538;
    public const ushort SpecialForcesPageEmpire = 10539;
    public const ushort TroopFinderPageAlliance = 10540;
    public const ushort TroopFinderPageEmpire = 10541;
    public const ushort CharactersCategoryAllianceUp = 10542;
    public const ushort CharactersCategoryAllianceDown = 10543;
    public const ushort CharactersCategoryEmpireUp = 10544;
    public const ushort CharactersCategoryEmpireDown = 10545;
    public const ushort SpecialForcesCategoryAllianceUp = 10546;
    public const ushort SpecialForcesCategoryAllianceDown = 10547;
    public const ushort SpecialForcesCategoryEmpireUp = 10548;
    public const ushort SpecialForcesCategoryEmpireDown = 10549;
    /// <summary>Side strip of the System and Troop Finders.</summary>
    public const ushort FinderStripAlliance = 10584;
    /// <summary>Side strip of the Fleet and Personnel Finders (wider button column).</summary>
    public const ushort WideFinderStripAlliance = 10586;
    public const ushort FinderStripEmpire = 10588;
    public const ushort WideFinderStripEmpire = 10590;

    // ---- Production and mission windows ----
    /// <summary>Encyclopedia button 64 x 33 of the production and mission windows (not a side pair with the 32 x 31 ones).</summary>
    public const ushort EncyclopediaUp = 10592;
    public const ushort EncyclopediaDown = 10593;
    /// <summary>Done (production), Begin Mission (mission window).</summary>
    public const ushort DoneUp = 10594;
    public const ushort DoneDown = 10595;
    public const ushort CancelUp = 10596;
    public const ushort CancelDown = 10597;
    /// <summary>Frame behind the current item of a drop-down list.</summary>
    public const ushort DropDownSelectedFrame = 10598;
    /// <summary>Opens the drop-down: Items to Build (production), Missions (mission window).</summary>
    public const ushort DropDownUp = 10606;
    public const ushort DropDownDown = 10607;
    public const ushort IncreaseUp = 10610;
    public const ushort IncreaseDown = 10611;
    public const ushort DecreaseUp = 10612;
    public const ushort DecreaseDown = 10613;
    public const ushort ProductionBackground = 10800;
    /// <summary>Title strip of the production and mission windows, by the player's side.</summary>
    public const ushort ProductionTitleStripAlliance = 10801;
    public const ushort ProductionTitleStripEmpire = 10802;
    public const ushort MissionPageBackground = 11100;
    public const ushort PersonnelPageBackground = 11101;
    public const ushort MissionTabAllianceUp = 11103;
    public const ushort MissionTabAllianceDown = 11104;
    public const ushort MissionTabEmpireUp = 11105;
    public const ushort MissionTabEmpireDown = 11106;
    public const ushort PersonnelTabAllianceUp = 11107;
    public const ushort PersonnelTabAllianceDown = 11108;
    public const ushort PersonnelTabEmpireUp = 11109;
    public const ushort PersonnelTabEmpireDown = 11110;
    public const ushort ToDecoysUp = 11117;
    public const ushort ToDecoysDown = 11118;
    public const ushort ToAgentsUp = 11119;
    public const ushort ToAgentsDown = 11120;
    public const ushort AgentsHeaderAlliance = 11121;
    public const ushort DecoysHeaderAlliance = 11122;
    public const ushort AgentsHeaderEmpire = 11123;
    public const ushort DecoysHeaderEmpire = 11124;
    public const ushort DoneDisabled = 11620;

    // ---- Battle-start window ----
    public const ushort BattleStartFrameAlliance = 10710;
    public const ushort BattleStartFrameEmpire = 10711;
    public const ushort BattleStartPictureAlliance = 10712;
    public const ushort BattleStartPictureEmpire = 10713;
    /// <summary>Dimmed picture under the force lists.</summary>
    public const ushort BattleStartDimmedAlliance = 10714;
    public const ushort BattleStartDimmedEmpire = 10715;
    public const ushort SimulateEmpireUp = 10716;
    public const ushort SimulateEmpireDown = 10717;
    public const ushort SimulateEmpireDisabled = 10718;
    public const ushort TakeCommandEmpireUp = 10719;
    public const ushort TakeCommandEmpireDown = 10720;
    public const ushort TakeCommandEmpireDisabled = 10721;
    public const ushort SimulateAllianceUp = 10722;
    public const ushort SimulateAllianceDown = 10723;
    public const ushort SimulateAllianceDisabled = 10724;
    public const ushort TakeCommandAllianceUp = 10725;
    public const ushort TakeCommandAllianceDown = 10726;
    public const ushort TakeCommandAllianceDisabled = 10727;
    public const ushort RetreatEmpireUp = 10971;
    public const ushort RetreatEmpireDown = 10972;
    public const ushort RetreatEmpireDisabled = 10973;
    public const ushort RetreatAllianceUp = 10974;
    public const ushort RetreatAllianceDown = 10975;
    public const ushort RetreatAllianceDisabled = 10976;

    // ---- Battle tabs (start, report, strike windows) ----
    // Forces tabs name the forces' side; the Alliance / Empire suffix is the player's side.
    /// <summary>Summary tab; the Empire's also the Display Message button of the message list.</summary>
    public const ushort SummaryTabAllianceUp = 10728;
    public const ushort SummaryTabAllianceDown = 10729;
    public const ushort AllianceForcesTabAllianceUp = 10730;
    public const ushort AllianceForcesTabAllianceDown = 10731;
    public const ushort ImperialForcesTabAllianceUp = 10732;
    public const ushort ImperialForcesTabAllianceDown = 10733;
    public const ushort SystemAssetsTabAllianceUp = 10734;
    public const ushort SystemAssetsTabAllianceDown = 10735;
    /// <summary>Third strike tab of a neutral defender (Inferred).</summary>
    public const ushort NeutralForcesTabAllianceUp = 10736;
    public const ushort NeutralForcesTabAllianceDown = 10737;
    public const ushort SummaryTabEmpireUp = 10738;
    public const ushort SummaryTabEmpireDown = 10739;
    public const ushort AllianceForcesTabEmpireUp = 10740;
    public const ushort AllianceForcesTabEmpireDown = 10741;
    public const ushort ImperialForcesTabEmpireUp = 10742;
    public const ushort ImperialForcesTabEmpireDown = 10743;
    public const ushort SystemAssetsTabEmpireUp = 10744;
    public const ushort SystemAssetsTabEmpireDown = 10745;
    public const ushort NeutralForcesTabEmpireUp = 10746;
    public const ushort NeutralForcesTabEmpireDown = 10747;
    public const ushort DisplayTabAllianceUp = 10791;
    public const ushort DisplayTabAllianceDown = 10792;

    // ---- Battle report and strike result windows ----
    public const ushort AllianceWithdrew = 10757;
    public const ushort EmpireWithdrew = 10758;
    public const ushort EmpireDestroyed = 10759;
    public const ushort AllianceDestroyed = 10760;
    // Page backgrounds (Inferred mapping): unit lists, personnel list, display page.
    public const ushort UnitsPage = 10761;
    public const ushort PersonnelPage = 10762;
    public const ushort DisplayPage = 10763;
    public const ushort GoToSystemUp = 10764;
    public const ushort GoToFleetAllianceUp = 10765;
    public const ushort GoToFleetEmpireUp = 10766;
    /// <summary>Down image of both go-to buttons.</summary>
    public const ushort GoToAllianceDown = 10767;
    public const ushort GoToEmpireDown = 10768;
    public const ushort BattleWaitBackground = 10769;
    public const ushort NoVictor = 11619;

    // ---- Message window ----
    public const ushort MessageListPage = 10823;
    // Tabs (both sides unless suffixed).
    public const ushort AllMessagesTabUp = 10830;
    public const ushort AllMessagesTabDown = 10831;
    public const ushort SupportMessagesTabAllianceUp = 10832;
    public const ushort SupportMessagesTabAllianceDown = 10833;
    public const ushort SupportMessagesTabEmpireUp = 10834;
    public const ushort SupportMessagesTabEmpireDown = 10835;
    public const ushort ResourceMessagesTabUp = 10836;
    public const ushort ResourceMessagesTabDown = 10837;
    public const ushort FleetMessagesTabEmpireUp = 10838;
    public const ushort FleetMessagesTabEmpireDown = 10839;
    public const ushort FleetMessagesTabAllianceUp = 10840;
    public const ushort FleetMessagesTabAllianceDown = 10841;
    public const ushort MissionMessagesTabAllianceUp = 10842;
    public const ushort MissionMessagesTabAllianceDown = 10843;
    public const ushort MissionMessagesTabEmpireUp = 10844;
    public const ushort MissionMessagesTabEmpireDown = 10845;
    public const ushort AdviceMessagesTabAllianceUp = 10846;
    public const ushort AdviceMessagesTabAllianceDown = 10847;
    public const ushort AdviceMessagesTabEmpireUp = 10848;
    public const ushort AdviceMessagesTabEmpireDown = 10849;
    public const ushort ChatMessagesTabUp = 10850;
    public const ushort ChatMessagesTabDown = 10851;
    public const ushort ManufacturingMessagesTabUp = 10852;
    public const ushort ManufacturingMessagesTabDown = 10853;
    public const ushort ConflictMessagesTabUp = 10854;
    public const ushort ConflictMessagesTabDown = 10855;
    public const ushort DefenseMessagesTabUp = 10856;
    public const ushort DefenseMessagesTabDown = 10857;
    public const ushort DisplayMessageAllianceUp = 10870;
    public const ushort DisplayMessageAllianceDown = 10871;
    // Alert toggle: Up = alert on; Silent = silent up; Down for both.
    public const ushort AlertAllianceUp = 10872;
    public const ushort AlertAllianceDown = 10873;
    public const ushort AlertEmpireUp = 10874;
    public const ushort AlertEmpireDown = 10875;
    public const ushort ComposeAllianceUp = 10876;
    public const ushort ComposeAllianceDown = 10877;
    public const ushort ComposeAllianceDisabled = 10878;
    public const ushort ComposeEmpireUp = 10879;
    public const ushort ComposeEmpireDown = 10880;
    public const ushort ComposeEmpireDisabled = 10881;
    public const ushort SelectAllUp = 10900;
    public const ushort SelectAllDown = 10901;
    public const ushort DeleteUp = 10902;
    public const ushort DeleteDown = 10903;
    // Row icons by category.
    public const ushort SupportRowIconAlliance = 10906;
    public const ushort SupportRowIconEmpire = 10907;
    public const ushort ManufacturingRowIconAlliance = 10908;
    public const ushort ManufacturingRowIconEmpire = 10909;
    public const ushort MissionRowIconAlliance = 10910;
    public const ushort MissionRowIconEmpire = 10911;
    /// <summary>Bar of a selected row.</summary>
    public const ushort SelectedRowAlliance = 10914;
    public const ushort SelectedRowEmpire = 10915;
    public const ushort ChatRowIcon = 10916;
    public const ushort NextMessageUp = 10919;
    public const ushort NextMessageDown = 10920;
    public const ushort NextMessageDisabled = 10921;
    public const ushort YesUp = 10926;
    public const ushort YesDown = 10927;
    public const ushort NoUp = 10929;
    public const ushort NoDown = 10930;
    /// <summary>Display mode: lower panel (body text) and title bar.</summary>
    public const ushort MessageDisplayBottom = 10932;
    public const ushort MessageDisplayTop = 10933;
    public const ushort PreviousMessageUp = 10948;
    public const ushort PreviousMessageDown = 10949;
    public const ushort PreviousMessageDisabled = 10950;
    public const ushort SilentAllianceUp = 10951;
    public const ushort SilentEmpireUp = 10953;
    public const ushort GoToDisabledAlliance = 10959;
    public const ushort GoToDisabledEmpire = 10960;
    public const ushort DisplayMessageAllianceDisabled = 10961;
    public const ushort DisplayMessageEmpireDisabled = 10962;
    public const ushort DefenseRowIcon = 10963;
    public const ushort FleetRowIconAlliance = 10964;
    public const ushort FleetRowIconEmpire = 10965;
    public const ushort ResourceRowIcon = 10966;
    public const ushort ConflictRowIcon = 10967;
    public const ushort AdviceRowIconAlliance = 10968;
    public const ushort AdviceRowIconEmpire = 10969;
    /// <summary>Encyclopedia button (32 x 31): status window, message window.</summary>
    public const ushort EncyclopediaAllianceUp = 11552;
    public const ushort EncyclopediaAllianceDown = 11553;
    public const ushort EncyclopediaEmpireUp = 11591;
    public const ushort EncyclopediaEmpireDown = 11592;
    public const ushort EncyclopediaDisabled = 11612;

    // ---- Confirmation window ----
    /// <summary>Pictures (400 x 200) by command, then the backgrounds.</summary>
    public const ushort MovePictureAlliance = 1018;
    public const ushort MovePictureEmpire = 1019;
    /// <summary>Move from a blockaded system; the pair is reversed (Empire first).</summary>
    public const ushort BlockadeMovePictureEmpire = 1030;
    public const ushort BlockadeMovePictureAlliance = 1031;
    public const ushort ScrapPictureAlliance = 1032;
    public const ushort ScrapPictureEmpire = 1033;
    public const ushort RetirePictureAlliance = 1034;
    public const ushort RetirePictureEmpire = 1035;
    public const ushort AbortMissionPictureAlliance = 1036;
    public const ushort AbortMissionPictureEmpire = 1037;
    public const ushort StopConstructionPictureAlliance = 1038;
    public const ushort StopConstructionPictureEmpire = 1039;
    public const ushort ConfirmBackgroundAlliance = 11125;
    public const ushort ConfirmBackgroundEmpire = 11126;

    // ---- Status window ----
    // Background by the player's side, then the object's owner.
    public const ushort StatusAllianceAllianceObject = 11554;
    public const ushort StatusAllianceEmpireObject = 11555;
    public const ushort StatusAllianceNeutralObject = 11556;
    public const ushort StatusEmpireAllianceObject = 11557;
    public const ushort StatusEmpireEmpireObject = 11558;
    public const ushort StatusEmpireNeutralObject = 11559;
    /// <summary>Picture of the side key (Objectives, Galaxy Overview).</summary>
    public const ushort StatusSidePicture = 11590;

    // ---- Top bar ----
    /// <summary>Speed indicator: base + speed (0 paused - 3); speed 4 has its own image.</summary>
    public const ushort SpeedIndicatorAllianceBase = 11580;
    public const ushort SpeedIndicatorEmpireBase = 11584;
    public const ushort SpeedIndicatorFastestAlliance = 11588;
    public const ushort SpeedIndicatorFastestEmpire = 11589;
}
