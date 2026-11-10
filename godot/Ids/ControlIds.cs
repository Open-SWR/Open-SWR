/// <summary>UI-only control, tab and page ids of the front end, the strategy frame and the object windows.</summary>
public static class ControlIds
{
    // ---- Main menu ----
    public const int MainMenuStartAlliance = 101;
    public const int MainMenuStartEmpire = 102;
    public const int MainMenuHeadToHead = 103;
    public const int MainMenuLoadGame = 104;
    public const int MainMenuExit = 105;
    public const int MainMenuGalaxySizeLever = 106;
    public const int MainMenuStandardGalaxy = 107;
    public const int MainMenuLargeGalaxy = 108;
    public const int MainMenuHugeGalaxy = 109;
    public const int MainMenuNovice = 110;
    public const int MainMenuIntermediate = 111;
    public const int MainMenuExpert = 112;
    public const int MainMenuVictoryConditions = 113;
    public const int MainMenuCredits = 115;

    // ---- Options screen ----
    /// <summary>Save button of slot n (1-6) = this + n.</summary>
    public const int OptionsSaveSlotBase = 110;
    /// <summary>Load button of slot n (1-6) = this + n.</summary>
    public const int OptionsLoadSlotBase = 120;
    public const int OptionsMusicVolume = 141;
    public const int OptionsSoundVolume = 142;
    public const int OptionsExit = 143;
    public const int OptionsReturn = 144;
    public const int OptionsRestart = 145;
    public const int OptionsPlayMusic = 153;
    public const int OptionsShowStarfield = 161;
    public const int OptionsShowPlanet = 162;
    public const int OptionsShowPyrotechnics = 163;
    public const int OptionsHighDetailModels = 164;
    public const int OptionsDisplayHolocube = 166;

    // ---- Strategy frame buttons ----
    public const int SystemFinderButton = 301;
    public const int FleetFinderButton = 302;
    public const int PersonnelFinderButton = 303;
    public const int TroopFinderButton = 304;
    public const int EncyclopediaButton = 305;
    /// <summary>Opens the map display filter picker.</summary>
    public const int GalacticInformationDisplayButton = 306;
    /// <summary>Opens the options screen.</summary>
    public const int GameControlsButton = 307;

    // ---- Speed menu rows (row for speed s = first + s, s 0-4) ----
    public const int SpeedMenuAllianceFirst = 32;
    public const int SpeedMenuEmpireFirst = 37;

    // ---- Strategy frame message-category buttons ----
    public const int SupportMessagesButton = 310;
    public const int FleetMessagesButton = 311;
    public const int MissionMessagesButton = 312;
    public const int ResourceMessagesButton = 313;
    public const int ManufacturingMessagesButton = 314;
    public const int DefenseMessagesButton = 315;
    public const int ConflictMessagesButton = 316;
    public const int AdviceMessagesButton = 317;
    public const int ChatMessagesButton = 318;

    // ---- System manufacturing window tabs ----
    /// <summary>The managers page.</summary>
    public const int ManufacturingTab = 103;
    public const int ShipyardsTab = 104;
    public const int TrainingFacilitiesTab = 105;
    public const int ConstructionYardsTab = 106;
    public const int RefineriesTab = 107;
    public const int MinesTab = 108;

    // ---- System fleet window tabs (the contents shown of the selected fleets) ----
    public const int FleetShipsTab = 102;
    public const int FleetFightersTab = 103;
    public const int FleetTroopsTab = 104;
    public const int FleetPersonnelTab = 105;

    // ---- System planet window tabs (the unit lists of the system) ----
    public const int PlanetPersonnelTab = 1;
    public const int PlanetTroopsTab = 2;
    public const int PlanetFightersTab = 3;
    public const int PlanetShieldsTab = 4;
    public const int PlanetBatteriesTab = 5;

    // ---- Encyclopedia topics ----
    public const int AllDatabasesTopic = 111;
    public const int SystemDatabaseTopic = 112;
    public const int ShipDatabaseTopic = 113;
    public const int FacilitiesDatabaseTopic = 114;
    public const int MissionsDatabaseTopic = 115;
    public const int TroopDatabaseTopic = 116;
    public const int PersonnelDatabaseTopic = 117;

    // ---- Map filter picker group rows (each opens its submenu of filter modes) ----
    public const int FilterLoyaltyGroup = 16;
    public const int FilterFleetsGroup = 32;
    public const int FilterPersonnelGroup = 64;
    public const int FilterResourcesGroup = 80;
    public const int FilterManufacturingGroup = 96;
    public const int FilterDefenseGroup = 112;

    // ---- Mission window pages ----
    public const int MissionPage = 151;
    public const int PersonnelPage = 152;
}
