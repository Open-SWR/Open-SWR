using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>The research lines, as indices of the side's ResearchOrder. Meaning in the original is unknown.</summary>
public enum ResearchLine { Ship, Facility, Troop }






/// <summary>
/// The computer player's world model: system, sector, agent and (minimal) ship records built from the side's
/// views only (<see cref="Knowledge"/>), the product catalogue, dirtied by the controller messages, refreshed per think cycle
/// (agents, ships, sectors with their systems, then the aggregates); the candidate lists, product pick, budgets and the
/// producer-facility turn of the planners. The update draws nothing; list builds draw tie flips through
/// <see cref="Rand"/>. Not built yet: fleet records and ship fields beyond the planner-14 flags (round 7), the agent aggregate
/// field list (Inferred parts below).
/// </summary>
public sealed partial class WorldModel(Owner side)
{
    public Owner Side { get; } = side;
    // ---- model fields ----

    /// <summary>The side.</summary>
    public int SideNumber { get; set; }
    /// <summary>The game's victory flag is 1 (headquarters mode).</summary>
    public bool HeadquartersMode { get; set; }
    /// <summary>The strategy planner rebuilds its theaters at its next theater step (set by owner and HQ changes and mode changes).</summary>
    public bool TheatersDue { get; set; }
    /// <summary>An own fleet holds a Death Star.</summary>
    public bool OwnDeathStar { get; set; }
    /// <summary>An own fleet holds an interdictor.</summary>
    public bool OwnInterdictor { get; set; }
    /// <summary>An own sector with <see cref="AiSector.AssaultBase"/>.</summary>
    public bool AssaultBase { get; set; }
    /// <summary>An own sector with <see cref="AiSector.DefenseBase"/>.</summary>
    public bool DefenseBase { get; set; }
    /// <summary>Planner 1's hub step B ended without a system.</summary>
    public bool NoConstructionSite { get; set; }
    /// <summary>An own sector with <see cref="AiSector.OwnCoruscant"/>.</summary>
    public bool OwnCoruscant { get; set; }
    /// <summary>An own sector with <see cref="AiSector.OwnLowSupport"/>.</summary>
    public bool OwnLowSupport { get; set; }
    /// <summary>An own character record Captured.</summary>
    public bool OwnCaptured { get; set; }
    /// <summary>A sector with <see cref="AiSector.Headquarters"/>.</summary>
    public bool Headquarters { get; set; }
    /// <summary>The strategy's mode choice found the side strong with nothing short (mode 5). Meaning in the original is unknown.</summary>
    public bool Dominant { get; set; }
    /// <summary>The strategy's mode choice found the strength ratio below 1.5 or turn requests pending. Meaning in the original is unknown.</summary>
    public bool Outgunned { get; set; }
    /// <summary>The Death Star shield (Refs.DeathStarShieldType) became buildable.</summary>
    public bool DeathStarShieldAvailable { get; set; }
    /// <summary>Inferred: the ship research line is exhausted.</summary>
    public bool ShipLineDone { get; set; }
    /// <summary>Inferred: the troop research line is exhausted.</summary>
    public bool TroopLineDone { get; set; }
    /// <summary>Inferred: the facility research line is exhausted.</summary>
    public bool FacilityLineDone { get; set; }
    /// <summary>Agent records changed in this update.</summary>
    public bool AgentsChanged { get; set; }
    /// <summary>Sector records changed in this update.</summary>
    public bool SectorsChanged { get; set; }
    /// <summary>Fleet records changed in this update.</summary>
    public bool FleetsChanged { get; set; }
    /// <summary>
    /// The producer-facility turn's requests, one per turn slot. The eight producer-facility planners take turns in a fixed order
    /// of slots: planner 1 (slot 0), 2, 6, 10, 7, 5, 11, then 3 (slot 7).
    /// </summary>
    public bool[] TurnRequests { get; } = new bool[TurnSlots];
    /// <summary>The slot whose turn it is.</summary>
    public int Turn { get; set; }
    /// <summary>The next turn slot.</summary>
    public int NextTurn { get; set; }
    /// <summary>The number of producer-facility turn slots.</summary>
    public const int TurnSlots = 8;
    /// <summary>Own minor characters still to come.</summary>
    public int MinorCharactersToCome { get; set; }
    // catalogue summary
    /// <summary>The largest ResearchOrder of the side's ships and fighters.</summary>
    public int MaxShipOrder { get; set; }
    /// <summary>The largest ResearchOrder of the side's troops.</summary>
    public int MaxTroopOrder { get; set; }
    /// <summary>The largest ResearchOrder of all defence and production facilities.</summary>
    public int MaxFacilityOrder { get; set; }
    /// <summary>The side's minor characters.</summary>
    public int MinorCharacters { get; set; }
    /// <summary>The ship research level.</summary>
    public int ShipLevel { get; set; }
    /// <summary>The troop research level.</summary>
    public int TroopLevel { get; set; }
    /// <summary>The facility research level.</summary>
    public int FacilityLevel { get; set; }
    /// <summary>The best buildable troop DefenseStrength.</summary>
    public int BestTroopDefense { get; set; }
    /// <summary>The best buildable fighter score vs fighters.</summary>
    public int BestFighterAntiFighterFirepower { get; set; }
    /// <summary>The best buildable armed capital ship's score vs ships.</summary>
    public int BestArmedFirepower { get; set; }
    /// <summary>The best buildable anti-fighter capital ship's score vs fighters.</summary>
    public int BestAntiFighterFirepower { get; set; }
    /// <summary>The best buildable capital ship's fighter capacity.</summary>
    public int BestFighterCapacity { get; set; }
    /// <summary>The best buildable capital ship's troop capacity.</summary>
    public int BestTroopCapacity { get; set; }
    /// <summary>The best buildable bombardment ship's bombardment value.</summary>
    public int BestBombardment { get; set; }
    /// <summary>The side's maintenance capacity (cap).</summary>
    public int MaintenanceCapacity { get; set; }
    /// <summary>The side's maintenance demand (dem).</summary>
    public int MaintenanceDemand { get; set; }
    /// <summary>10 per sector record.</summary>
    public int SystemSlots { get; set; }
    // sector aggregates
    public int OwnSystems { get; set; }
    public int EnemySystems { get; set; }
    public int NeutralSystems { get; set; }
    /// <summary>Sectors with an own system.</summary>
    public int OwnSectors { get; set; }
    /// <summary>Sectors with an enemy system.</summary>
    public int EnemySectors { get; set; }
    /// <summary>Sectors with a neutral system.</summary>
    public int NeutralSectors { get; set; }
    /// <summary>Sectors without an own or neutral key system.</summary>
    public int SectorsWithoutKey { get; set; }
    /// <summary>Sectors not all held.</summary>
    public int SectorsNotHeld { get; set; }
    public int TroopSurplus { get; set; }
    /// <summary>Own Mines.</summary>
    public int Mines { get; set; }
    /// <summary>Own Refineries.</summary>
    public int Refineries { get; set; }
    /// <summary>Troops in own systems.</summary>
    public int TroopMaintenance { get; set; }
    /// <summary>Own defences.</summary>
    public int DefenceMaintenance { get; set; }
    /// <summary>Fighters in own systems.</summary>
    public int FighterMaintenance { get; set; }
    public int FreeEnergy { get; set; }
    public int FreeRawMaterial { get; set; }
    public int EnemyUsedEnergy { get; set; }
    public int ProducerMaintenance { get; set; }
    public int EnemyProducerMaintenance { get; set; }
    public int GarrisonTarget { get; set; }
    public int Troops { get; set; }
    /// <summary>Sum of sector theater balances (&lt; 0: a theater without a fleet op).</summary>
    public int TheaterBalance { get; set; }
    /// <summary>The most enemy fighters in a system.</summary>
    public int MaxEnemyFighters { get; set; }
    /// <summary>The largest enemy troops-to-take.</summary>
    public int MaxEnemyTroopsToTake { get; set; }
    /// <summary>The smallest nonzero sector enemy troops-to-take.</summary>
    public int MinEnemyTroopsToTake { get; set; }
    /// <summary>Sum of sector <see cref="AiSector.DiplomacyTargets"/>, then div 5 + 1 when positive.</summary>
    public int DiplomacyTargets { get; set; }
    /// <summary>Sum of sector enemy-supported systems, then div 6 + 1 when positive.</summary>
    public int EnemySupported { get; set; }
    public int UprisingNoFleet { get; set; }
    public int LowSupportHubs { get; set; }
    public int UnblockadedKeySystems { get; set; }
    // fleet aggregates
    /// <summary>Own fleets' capital-ship maintenance.</summary>
    public int ShipMaintenance { get; set; }
    /// <summary>Own fleets' troop maintenance.</summary>
    public int FleetTroopMaintenance { get; set; }
    /// <summary>Own fleets' fighter maintenance.</summary>
    public int FleetFighterMaintenance { get; set; }
    public int FleetFirepower { get; set; }
    public int FleetTroops { get; set; }
    public int CarrierTroopCapacity { get; set; }
    /// <summary>Own unnamed ships (gates planner 14).</summary>
    public int UnnamedShips { get; set; }
    /// <summary>Free own fleets' undamaged armed ships.</summary>
    public int FreeArmed { get; set; }
    public int FreeAntiFighter { get; set; }
    public int FreeTroopCarriers { get; set; }
    public int FreeFirepower { get; set; }
    public int FreeAntiFighterFirepower { get; set; }
    public int FreeFighterCapacity { get; set; }
    public int FreeBombardment { get; set; }
    /// <summary>Other fleets with ships.</summary>
    public int OtherFleets { get; set; }
    public int OtherFirepower { get; set; }
    public int OtherFighterCapacity { get; set; }
    public int OtherAntiFighterFirepower { get; set; }
    /// <summary>The strongest other fleet's strength vs ships.</summary>
    public int StrongestFirepower { get; set; }
    public int StrongestAntiFighterFirepower { get; set; }
    public int StrongestFighterCapacity { get; set; }
    // agent aggregates (Inferred from the planner-5 readers)
    /// <summary>Own special forces' MaintenanceCost.</summary>
    public int SpecialForceMaintenance { get; set; }
    /// <summary>Own characters with the facility-design capability.</summary>
    public int FacilityDesigners { get; set; }
    public int ShipDesigners { get; set; }
    public int TroopTrainers { get; set; }
    /// <summary>Own special forces of type k + 1.</summary>
    public ref int SpecialForces(int k) => ref _specialForces[k];
    readonly int[] _specialForces = new int[4];
    /// <summary>Own special forces of type k + 1 with <see cref="AgentCapabilities.Unused"/> (no writer: always 0). Meaning in the original is unknown.</summary>
    public ref int MarkedSpecialForces(int k) => ref _markedSpecialForces[k];
    readonly int[] _markedSpecialForces = new int[4];
    /// <summary>The largest enemy units (a sector maximum inside the agent table, zeroed only with it).</summary>
    public int MaxEnemyUnits { get; set; }
    /// <summary>The mission op to retire (planner 13 to planner 4).</summary>
    public int MissionRetireKey { get; set; }
    /// <summary>The mission kind to create.</summary>
    public int MissionRequestKind { get; set; }
    /// <summary>The mission subtype to create.</summary>
    public int MissionRequestSubtype { get; set; }
    /// <summary>System-kind mission ops per class index.</summary>
    public ref int SystemOps(int index) => ref _systemOps[index];
    readonly int[] _systemOps = new int[13];
    /// <summary>The character op to retire.</summary>
    public int CharacterRetireKey { get; set; }
    /// <summary>The character-op type to create (1 or 6).</summary>
    public CharacterOpType CharacterRequestType { get; set; }
    /// <summary>Type-1 character ops.</summary>
    public int Type1CharacterOps { get; set; }
    /// <summary>Type-6 character ops.</summary>
    public int Type6CharacterOps { get; set; }
    /// <summary>The garrison and defence constants.</summary>
    public ref int Constant(ModelConstant k) => ref _constants[(int)k];
    readonly int[] _constants = new int[12];

    Owner Enemy => Side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
    public Dictionary<StarSystem, AiSystem> Systems { get; } = [];
    /// <summary>Sector records in key order.</summary>
    public List<AiSector> Sectors { get; } = [];
    /// <summary>Agent records by SdRef (key order).</summary>
    public SortedDictionary<uint, AiAgent> Agents { get; } = [];
    /// <summary>Ship records by SdRef (key order).</summary>
    public SortedDictionary<uint, AiShip> Ships { get; } = [];
    /// <summary>Product catalogue (built at init).</summary>
    public Catalogue Catalogue { get; private set; } = null!;
    /// <summary>The UI generator's rand(n) (the controller's source).</summary>
    public Func<int, int> Rand { get; set; } = n => UiRandom.Shared.Rand(n);
    /// <summary>Aggregates dirty (every dirty mark sets it).</summary>
    public bool Dirty { get; set; }

    GameState _game = null!;

    // ---- init ----

    Dictionary<GameObject, List<GameObject>>? _frozenContents;

    /// <summary>Fleet records by fleet key, created with their first ship record (owner fixed at creation, free flag 4).</summary>
    public SortedDictionary<uint, AiFleet> FleetsById { get; } = [];

    /// <summary>Mission type gives the capability it shows when the unit alone may run it.</summary>
    static readonly (uint Type, Action<AgentCapabilities> Set)[] Capabilities =
    [
        (Refs.DiplomacyMission, c => c.Diplomacy = true), (Refs.RescueMission, c => c.Rescue = true), (Refs.SabotageMission, c => c.Sabotage = true),
        (Refs.EspionageMission, c => c.Espionage = true), (Refs.AbductionMission, c => c.Abduction = true), (Refs.SubdueMission, c => c.Subdue = true),
        (Refs.AssassinationMission, c => c.Assassination = true), (Refs.DeathStarSabotageMission, c => c.DeathStarSabotage = true), (Refs.InciteMission, c => c.Incite = true),
        (Refs.ReconnaissanceMission, c => c.Recon = true), (Refs.RecruitmentMission, c => c.Recruitment = true), (Refs.ShipResearchMission, c => c.ShipDesign = true),
        (Refs.TroopResearchMission, c => c.TroopTraining = true), (Refs.FacilityResearchMission, c => c.FacilityDesign = true),
    ];

    readonly Dictionary<GameObject, (Owner, int, int, int, bool, bool, bool)> _systemFields = [];
    readonly Dictionary<GameObject, int> _queues = [];
    readonly Dictionary<GameObject, (int, int, int, int, (bool, bool, bool), uint, bool, int, bool, bool, CriticalHitCounts)> _unitFields = [];

    /// <summary>Mission operations by key 1, 2, 3 ... in creation order (= the missions' UserID).</summary>
    public SortedDictionary<int, MissionOp> Ops { get; } = [];
    internal int NextOpKey { get; set; } = 1;
    /// <summary>Fleet tasks: garrison tasks created by the strategy planner's fleet-task step (StrategyStep.FleetTasks) (keys 1, 2, ...), run by the fleet-task planner (PlannerType.FleetTask).</summary>
    public List<GarrisonTask> Tasks { get; } = [];
    internal int NextTaskKey { get; set; } = 1;
    /// <summary>Fleet operations by key.</summary>
    public SortedDictionary<int, FleetOp> FleetOps { get; } = [];
    /// <summary>The production planners' shared site slots (not saved).</summary>
    public Dictionary<int, StarSystem?> SiteSlots { get; } = [];
    internal int NextFleetOpKey { get; set; } = 1;
    public int FleetOperationCount => FleetOps.Count;
    /// <summary>Character operations by key.</summary>
    public SortedDictionary<int, CharacterOp> CharacterOps { get; } = [];
    internal int NextCharacterOpKey { get; set; } = 1;

}
