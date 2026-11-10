namespace OpenSWR.Data.Dat;

/// <summary>Well-known SdRefs used by setup and the game code.</summary>
public static class Refs
{
    public const uint Coruscant = 2415919369u; // family 144, index 265
    public const uint Yavin = 2449473825u; // family 146, index 289
    public const uint Fleet = 134217732; // family 8, index 4
    public const uint Mine = 738197505; // family 44, index 1
    public const uint AllianceArmy = 268435458; // family 16, index 2
    public const uint EmpireArmy = 268435464; // family 16, index 8
    public const uint Vader = 889193089; // family 53, index 641
    public const uint Luke = 838861378; // family 50, index 578
    public const uint Han = 855638595; // family 51, index 579
    public const uint Leia = 822084161; // family 49, index 577
    public const uint Chewbacca = 939524931; // family 56, index 835
    public const uint Palpatine = 872415872; // family 52, index 640
    /// <summary>The SD index (object id) of Coruscant.</summary>
    public const uint CoruscantIndex = 265;
    /// <summary>The SD index (object id) of Yavin.</summary>
    public const uint YavinIndex = 289;
    public const uint Wedge = 939524929; // family 56, index 833
    public const uint Dodonna = 939524932; // family 56, index 836
    public const uint Jerjerrod = 939524992; // family 56, index 896
    public const uint Ozzel = 939524993; // family 56, index 897
    public const uint Piett = 939524994; // family 56, index 898
    public const uint Veers = 939524995; // family 56, index 899
    public const uint Needa = 939525004; // family 56, index 908
    /// <summary>Troop regiment types (AllianceArmy, EmpireArmy and Stormtroopers above).</summary>
    public const uint AllianceFleetRegiment = 268435457; // family 16, index 1
    public const uint SullustanRegiment = 268435459; // family 16, index 3
    public const uint WookieeRegiment = 268435460; // family 16, index 4
    public const uint MonCalamariRegiment = 268435461; // family 16, index 5
    public const uint ImperialFleetRegiment = 268435463; // family 16, index 7
    public const uint WarDroidRegiment = 268435465; // family 16, index 9
    public const uint DarkTrooperRegiment = 268435466; // family 16, index 10
    /// <summary>Special force types (BountyHunter is the bounty hunter type).</summary>
    public const uint Guerrillas = 1006632961; // family 60, index 1
    public const uint Infiltrators = 1006632962; // family 60, index 2
    public const uint LongprobeRecon = 1006632963; // family 60, index 3
    public const uint BothanSpies = 1006632964; // family 60, index 4
    public const uint ProbeDroids = 1006632965; // family 60, index 5
    public const uint EspionageDroids = 1006632966; // family 60, index 6
    public const uint ImperialCommandos = 1006632967; // family 60, index 7
    public const uint NoghriCommandos = 1006632968; // family 60, index 8
    public const uint BountyHunter = 1006632969; // family 60, index 9
    public const uint Mothma = 805306944; // family 48, index 576
    public const uint Refinery = 754974722; // family 45, index 2
    public const uint Stormtroopers = 268435462; // family 16, index 6
    /// <summary>Capital ship types the mod validator protects.</summary>
    public const uint CC7700Frigate = 335544394; // family 20, index 74
    public const uint ImmobilizerCruiser = 335544450; // family 20, index 130
    /// <summary>The headquarters type, and the fixed object ref of the headquarters facility.</summary>
    public const uint HeadquartersType = 536870913; // family 32, index 1
    public const uint Headquarters = 536870917; // family 32, index 5
    public const uint IonCannonType = 570425345; // family 34, index 1
    public const uint DeathStarShieldType = 620756996; // family 37, index 4
    /// <summary>Death Star object type; XWingSquadron is the fighter squadron type the scene adds four of to the Alliance's first capital ship. Meaning in the original is unknown.</summary>
    public const uint DeathStarType = 402653320; // family 24, index 136
    public const uint XWingSquadron = 469762051; // family 28, index 3
    public const uint MoveMission = 1090519041; // family 65, index 1
    public const uint ReturnMission = 1107296258; // family 66, index 2
    public const uint AutoroutingMission = 1124073475; // family 67, index 3
    public const uint AdriftMission = 1140850692; // family 68, index 4
    public const uint DiplomacyMission = 1358954512; // family 81, index 16
    public const uint EspionageMission = 1375731731; // family 82, index 19
    public const uint ShipResearchMission = 1392508960; // family 83, index 32
    public const uint FacilityResearchMission = 1392508961; // family 83, index 33
    public const uint TroopResearchMission = 1392508962; // family 83, index 34
    public const uint ReconnaissanceMission = 1409286165; // family 84, index 21
    public const uint RecruitmentMission = 1426063382; // family 85, index 22
    public const uint InciteMission = 1442840640; // family 86, index 64
    public const uint SubdueMission = 1459617920; // family 87, index 128
    public const uint RescueMission = 1627389969; // family 97, index 17
    public const uint AbductionMission = 1644167191; // family 98, index 23
    public const uint AssassinationMission = 1660944513; // family 99, index 129
    public const uint SabotageMission = 1761607698; // family 105, index 18
    public const uint DeathStarSabotageMission = 1778384961; // family 106, index 65
    public const uint ConstructionManager = 2684354565u; // family 160, index 5
    public const uint ShipyardManager = 2717908998u; // family 162, index 6
    public const uint TrainingManager = 2751463431u; // family 164, index 7
    /// <summary>The staging container (unknown parent).</summary>
    public const uint Staging = 4060086275u; // family 242, index 3
    public const uint AllianceSide = 4076863503u; // family 243, index 15
    public const uint EmpireSide = 4076863504u; // family 243, index 16
    public const uint MissionManager = 4160749574u; // family 248, index 6
    public const uint GameManager = 4177526791u; // family 249, index 7
}
