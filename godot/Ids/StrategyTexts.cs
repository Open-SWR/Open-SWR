/// <summary>TEXTSTRA.DLL string ids (<see cref="Assets.Stra"/>): strategy-screen labels, titles and tooltips.</summary>
public static class StrategyTexts
{
    // ---- Confirmation window ----
    public const int Yes = 4864;
    public const int No = 4865;

    // ---- Top bar and frame buttons ----
    // Shown instead of the maintenance number when the capacity exceeds 99999.
    public const int MaintenanceMax = 4866;
    public const int GameControls = 5376;
    public const int SystemFinder = 5377;
    public const int FleetFinder = 5378;
    public const int PersonnelFinder = 5379;
    public const int TroopFinder = 5380;
    // Frame button tooltip, also the status window's encyclopedia button.
    public const int Encyclopedia = 5381;
    public const int GalacticInformationDisplay = 5382;
    public const int RawMaterialsMonitor = 5392;
    public const int RefinedMaterialsMonitor = 5393;
    public const int MaintenanceMonitor = 5394;
    public const int GameSpeedControl = 5395;

    // ---- Map display filters (picker rows and titles) ----
    public const int FilterLoyalty = 5632;
    public const int FilterFleets = 5633;
    public const int FilterPersonnel = 5634;
    public const int FilterResources = 5637;
    public const int FilterAvailableEnergy = 5638;
    public const int FilterAvailableRawMaterial = 5639;
    public const int FilterMines = 5640;
    public const int FilterRefineries = 5641;
    public const int FilterManufacturing = 5642;
    // Same text as FilterManufacturing: title of mode 97.
    public const int FilterManufacturingTitle = 5643;
    public const int FilterShipyards = 5644;
    public const int FilterTrainingFacilities = 5645;
    public const int FilterConstructionYards = 5646;
    public const int FilterDefense = 5647;
    public const int FilterTroopers = 5648;
    public const int FilterFighterSquadrons = 5649;
    public const int FilterDeathStarShields = 5650;
    public const int FilterShieldGenerators = 5651;
    public const int FilterDisplayOff = 5652;
    public const int FilterPopularSupport = 5653;
    public const int FilterUprisings = 5654;
    public const int FilterIdleFleets = 5655;
    public const int FilterFleetsEnroute = 5656;
    public const int FilterDefenseBatteries = 5657;
    public const int FilterIdleConstructionYards = 5658;
    public const int FilterIdleTrainingFacilities = 5659;
    public const int FilterIdleShipyards = 5660;
    public const int FilterIdlePersonnel = 5661;
    public const int FilterActivePersonnel = 5662;
    // Advisor-only filter titles.
    public const int FilterLoyalToAlliance = 5663;
    public const int FilterLoyalToEmpire = 5664;
    public const int FilterMilitaryControl = 5665;
    // Guided-step titles of the advisor: the system or character the filter shows.
    public const int GuidedYavin = 5666;
    public const int GuidedHeadquarters = 5667;
    public const int GuidedCoruscant = 5668;
    public const int GuidedMothma = 5669;
    public const int GuidedLuke = 5670;
    public const int GuidedPalpatine = 5671;
    public const int GuidedVader = 5672;
    public const int FilterUnexploredSystems = 5673;
    public const int FilterAllDefenses = 5674;
    public const int Legend = 5948;

    // ---- System windows ----
    // Planet window tabs.
    public const int PlanetBatteries = 5952;
    public const int PlanetShields = 5953;
    public const int PlanetFighterSquadrons = 5954;
    public const int PlanetTrooperRegiments = 5955;
    public const int PlanetPersonnel = 5956;
    // Sector window bar tooltips: label, separator, used, "/", total.
    public const int PopularSupportBar = 6177;
    public const int EnergyBar = 6178;
    public const int RawMaterialsBar = 6179;
    public const int BarSeparator = 6180;
    public const int BarSlash = 6181;
    // Manufacturing window: facility tabs, manager rows.
    public const int MinesTab = 6182;
    public const int RefineriesTab = 6183;
    public const int ShipyardsTab = 6184;
    public const int ShipConstruction = 6185;
    public const int TrainingFacilitiesTab = 6192;
    public const int TroopsInTraining = 6193;
    public const int ConstructionYardsTab = 6194;
    public const int FacilitiesUnderConstruction = 6195;
    // Manufacturing tab, also the strike window's manufacturing filter.
    public const int Manufacturing = 6197;
    public const int NoShipsBuilt = 6198;
    public const int NoTroopsTraining = 6199;
    public const int NoFacilitiesBuilt = 6200;
    public const int Destination = 6201;
    public const int Training = 6208;
    public const int Building = 6209;
    // Fleet window tabs.
    public const int FleetFighterSquadrons = 14368;
    public const int FleetTroopRegiments = 14369;
    public const int FleetPersonnel = 14370;
    public const int FleetShips = 14371;
    // Mission list tabs.
    public const int AgentsTab = 34048;
    public const int DecoysTab = 34049;

    // ---- Encyclopedia ----
    public const int EncyclopediaTitle = 6210;
    public const int EncyclopediaTopic = 6211;
    public const int AllDatabases = 6224;
    public const int MissionsDatabase = 6225;
    public const int FacilitiesDatabase = 6226;
    public const int PersonnelDatabase = 6227;
    public const int ShipDatabase = 6228;
    public const int SystemDatabase = 6229;
    public const int TroopDatabase = 6230;
    public const int NextTopic = 6231;
    public const int PreviousTopic = 6232;
    public const int EncyclopediaIndex = 6233;
    public const int DisplayTopic = 6240;

    // ---- Finders ----
    // Each finder: page title, name-box label, filter tooltips.
    public const int SystemFinderTitle = 6245;
    public const int SystemFinderLabel = 6246;
    public const int AllSystems = 6247;
    public const int AllianceSystems = 6248;
    public const int EmpireSystems = 6249;
    public const int NeutralSystems = 6256;
    public const int UnexploredSystems = 6257;
    // Fleet mode title, also the fleet-mode button tooltip.
    public const int FleetFinderTitle = 6272;
    public const int FleetFinderLabel = 6273;
    // Fleet filters 6274-6276 (all, Alliance, Empire) by filter - 1.
    public const int AllFleets = 6274;
    // Ship mode title, also the ship-mode button tooltip.
    public const int ShipFinderTitle = 6277;
    public const int ShipFinderLabel = 6278;
    // Ship filters 6279-6281 (all, Alliance, Empire) by filter - 1.
    public const int AllShips = 6279;
    public const int PersonnelFinderTitle = 6288;
    public const int PersonnelFinderLabel = 6289;
    public const int AlliancePersonnel = 6291;
    public const int EmpirePersonnel = 6292;
    public const int SpecialForces = 6296;
    public const int Characters = 6297;
    public const int TroopFinderTitle = 6400;
    public const int TroopFinderLabel = 6401;
    public const int AllianceTroops = 6480;
    public const int EmpireTroops = 6481;
    public const int FinderDisplay = 6483;
    // Close button of finder, encyclopedia, status and message windows.
    public const int Close = 6484;

    // ---- Production window ----
    public const int ItemsToBuild = 6406;
    public const int Increase = 6407;
    public const int Decrease = 6408;
    public const int ProductionDone = 6409;
    public const int ProductionCancel = 6410;
    public const int ProductionEncyclopedia = 6411;
    public const int EditNumber = 6412;
    public const int BuildSelection = 14352;
    public const int NumberToBuild = 14353;
    public const int BestTimeToCompletion = 14354;
    public const int BestTimeToDeployment = 14355;
    public const int Days = 14357;
    public const int NotAvailable = 14358;

    // ---- Battle start, battle report and strike windows ----
    // Summary tab; the battle start tabs are BattleSummary + i (summary, Alliance forces, Empire forces, system assets).
    public const int BattleSummary = 6434;
    public const int SimulateResults = 6438;
    public const int TakeCommand = 6439;
    public const int CapitalShips = 6440;
    public const int FighterSquadrons = 6441;
    public const int TrooperRegiments = 6448;
    public const int Personnel = 6449;
    // Unit list column heads.
    public const int Operational = 6450;
    public const int Destroyed = 6451;
    public const int Survivors = 6452;
    public const int Captured = 6453;
    public const int Killed = 6454;
    public const int AllianceForces = 6455;
    public const int EmpireForces = 6456;
    public const int BattleDisplay = 6457;
    // Empty-column texts.
    public const int NoCasualties = 6465;
    public const int ReportNone = 34086;
    public const int Fleet = 6466;
    public const int SystemForces = 6467;
    public const int Summary = 6468;
    public const int GoToSystem = 6469;
    public const int DefenseFacilities = 6470;
    public const int Retreat = 6474;
    public const int Filters = 6482;

    // ---- Status window ----
    public const int GalaxyOverview = 12563;
    public const int Objectives = 12564;

    // ---- Side names (message placeholders) ----
    // Side 1 at the id, side 2 at id + 1.
    public const int SideName = 19792;
    public const int SideFullName = 19808;
    public const int SideAdjective = 19824;
    // Kinds 16-25: base + 2 * (kind - 16); not in the module.
    public const int SideNameKindBase = 19840;

    // ---- Message window ----
    public const int PostSilently = 32773;
    public const int PostWithAlert = 32774;
    public const int SelectAll = 32776;
    public const int DeleteSelected = 32777;
    // Category tabs, also the frame's message-category buttons.
    public const int AllMessages = 32784;
    public const int SupportMessages = 32785;
    public const int ResourceMessages = 32786;
    public const int ManufacturingMessages = 32787;
    public const int MissionMessages = 32788;
    public const int ChatMessages = 32789;
    public const int DefenseMessages = 32790;
    public const int FleetMessages = 32791;
    public const int ConflictMessages = 32792;
    public const int MessageIndex = 32793;
    public const int DisplayMessage = 32800;
    public const int ComposeChat = 32802;
    public const int MessageDisplay = 32816;
    // Go-to encyclopedia tooltip, also the mission window's encyclopedia button.
    public const int MessageEncyclopedia = 32817;
    public const int AdviceMessages = 32818;

    // ---- Mission window ----
    public const int BeginMission = 34050;
    public const int MissionCancel = 34051;
    public const int MissionTarget = 34052;
    public const int AgentsToDecoys = 34053;
    public const int DecoysToAgents = 34054;
    public const int CreateMission = 34055;
    public const int MissionTypes = 34064;
    public const int SelectedMission = 34080;

    // ---- Object names ----
    // Rank title before an own character's name: base - command kind (1 Commander, 2 Admiral, 3 General).
    public const int CommandRankTitleBase = 34819;
}
