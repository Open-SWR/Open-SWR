namespace OpenSWR.Sim.Ai;

/// <summary>Sector record: its 10 system records and their sums.</summary>
public sealed class AiSector(Sector sector)
{
    public Sector Sector { get; } = sector;
    public List<AiSystem> Systems { get; } = [];
    public bool Dirty { get; set; } = true;
    public int Key { get; set; }
    // what its own systems need (and its key systems), rebuilt by every update
    /// <summary>An own system without Blockade.</summary>
    public bool OwnUnblockaded { get; set; }
    /// <summary>An own system with Uprising.</summary>
    public bool OwnUprising { get; set; }
    /// <summary>A calm own system (not Yavin) with free slots and no shipyard role.</summary>
    public bool ShipyardSlot { get; set; }
    /// <summary>As <see cref="ShipyardSlot"/> without the training role.</summary>
    public bool TrainingSlot { get; set; }
    /// <summary>As <see cref="ShipyardSlot"/> without a construction role.</summary>
    public bool ConstructionSlot { get; set; }
    /// <summary>A calm own <see cref="AiSystem.MineSite"/>.</summary>
    public bool MineSite { get; set; }
    /// <summary>A calm own <see cref="AiSystem.RefinerySite"/>.</summary>
    public bool RefinerySite { get; set; }
    /// <summary>A calm own system with free energy.</summary>
    public bool HasFreeEnergy { get; set; }
    /// <summary>A calm own system with free raw material.</summary>
    public bool HasFreeRawMaterial { get; set; }
    /// <summary>A calm own system with the construction role and an idle construction manager.</summary>
    public bool IdleConstructionRole { get; set; }
    /// <summary>An own system with an idle construction manager and no construction role.</summary>
    public bool IdleConstruction { get; set; }
    /// <summary>An own role system with a defence shortfall.</summary>
    public bool RoleDefenceShort { get; set; }
    public bool DefenceShort { get; set; }
    public bool DefenceSurplus { get; set; }
    /// <summary>An own system that is not a key system, with batteries and GenCores. Meaning in the original is unknown.</summary>
    public bool DefendedNonKey { get; set; }
    public bool GarrisonShort { get; set; }
    public bool GarrisonSurplus { get; set; }
    /// <summary>A garrison surplus with <see cref="AiSystem.AttackGarrison"/>.</summary>
    public bool AttackGarrisonSurplus { get; set; }
    /// <summary>A garrison surplus with <see cref="AiSystem.DefenseGarrison"/>.</summary>
    public bool DefenseGarrisonSurplus { get; set; }
    public bool GarrisonTroopsShort { get; set; }
    /// <summary>An own role system short of garrison.</summary>
    public bool RoleGarrisonShort { get; set; }
    public bool FighterShort { get; set; }
    /// <summary>Usable fighters above the balance (no fleet-op base, no Blockade).</summary>
    public bool HasUsableFighterSurplus { get; set; }
    public bool RoleFighterShort { get; set; }
    public bool OwnFighters { get; set; }
    /// <summary>A fighter shortfall with <see cref="AiSystem.UsableFighterShort"/>.</summary>
    public bool UsableFighterShort { get; set; }
    /// <summary>An own system with a role.</summary>
    public bool HasRoleSystem { get; set; }
    /// <summary>An own or neutral key system. Meaning in the original is unknown.</summary>
    public bool KeySystem { get; set; }
    /// <summary>An enemy key system.</summary>
    public bool EnemyKeySystem { get; set; }
    // set by its systems' targets
    public bool HasShipProduction { get; set; }
    public bool HasTrainingProduction { get; set; }
    public bool HasConstructionProduction { get; set; }
    public bool HasCharacterSite { get; set; }
    public bool HasUprisingNoFleet { get; set; }
    /// <summary>A system with <see cref="AiSystem.DiplomacyTarget"/>.</summary>
    public bool HasDiplomacyTarget { get; set; }
    public bool HasNotOwn { get; set; }
    /// <summary>An enemy character here or aboard.</summary>
    public bool HasEnemyCharacter { get; set; }
    /// <summary>An enemy major here or aboard.</summary>
    public bool HasEnemyMajor { get; set; }
    public bool HasEnemySupported { get; set; }
    public bool HasClaimed { get; set; }
    public bool HasEnemyMines { get; set; }
    public bool HasEnemyProducers { get; set; }
    /// <summary>A system with <see cref="AiSystem.EnemyFleetSeen"/>. Meaning in the original is unknown.</summary>
    public bool EnemyFleetSeen { get; set; }
    /// <summary>An own captured character here or aboard.</summary>
    public bool HasOwnCaptured { get; set; }
    public bool HasEnemyDeathStar { get; set; }
    /// <summary>A system with <see cref="AiSystem.DiplomacyTarget"/> and without <see cref="AiSystem.DiplomacyMark"/>. Meaning in the original is unknown.</summary>
    public bool OpenDiplomacyTarget { get; set; }
    /// <summary>An own system with low support.</summary>
    public bool OwnLowSupport { get; set; }
    /// <summary>A recon op's sector with a target found (kept).</summary>
    public bool ReconMark { get; set; }
    /// <summary>An espionage op's sector with a target found (kept).</summary>
    public bool EspionageMark { get; set; }
    /// <summary>All ten systems explored.</summary>
    public bool AllExplored { get; set; }
    // the sector's own state
    public bool AllPopulated { get; set; }
    /// <summary>All ten systems owned by a side or populated.</summary>
    public bool AllHeld { get; set; }
    public bool Own { get; set; }
    public bool Enemy { get; set; }
    public bool Neutral { get; set; }
    public bool Rim { get; set; }
    /// <summary>A neutral system owned or populated.</summary>
    public bool NeutralPopulated { get; set; }
    /// <summary>Enemy victory characters in the sector.</summary>
    public bool HasVictoryCharacters { get; set; }
    /// <summary>An enemy system with facilities but no batteries.</summary>
    public bool EnemyNoBatteries { get; set; }
    /// <summary>An own key system that is Coruscant.</summary>
    public bool OwnCoruscant { get; set; }
    /// <summary>An enemy system with at most one GenCore.</summary>
    public bool EnemyFewGenCores { get; set; }
    /// <summary>A garrison surplus with own attack troops.</summary>
    public bool AttackTroopSurplus { get; set; }
    /// <summary>A garrison surplus with own defense troops.</summary>
    public bool DefenseTroopSurplus { get; set; }
    /// <summary>A theater without a fleet op.</summary>
    public bool OpenTheater { get; set; }
    /// <summary>An own shipyard-role system without Blockade.</summary>
    public bool ShipyardRole { get; set; }
    public bool EnemyFleet { get; set; }
    /// <summary>An enemy system with mines, producers, defences, troops, fighters, a fleet or an HQ.</summary>
    public bool EnemyAssets { get; set; }
    /// <summary>An enemy system without <see cref="AiSystem.CommandKind3"/>.</summary>
    public bool EnemyNoCommandKind3 { get; set; }
    /// <summary>An own fleet-op base.</summary>
    public bool Base { get; set; }
    /// <summary>An own base with <see cref="AiSystem.AssaultBase"/>. Meaning in the original is unknown.</summary>
    public bool AssaultBase { get; set; }
    /// <summary>An own base with <see cref="AiSystem.DefenseBase"/> (without <see cref="AiSystem.AssaultBase"/>). Meaning in the original is unknown.</summary>
    public bool DefenseBase { get; set; }
    /// <summary>The sector is a theater (planner 13).</summary>
    public bool Theater { get; set; }
    /// <summary>One of the strategy's theaters (kept; set and cleared by planner 13).</summary>
    public bool TheaterMark { get; set; }
    /// <summary>An own hub not claimed by a character op.</summary>
    public bool Hub { get; set; }
    /// <summary>A type-2 fleet task found no unvisited system here (kept; cleared when the sector becomes a theater).</summary>
    public bool NoUnvisited { get; set; }
    public bool Coruscant { get; set; }
    public bool Headquarters { get; set; }

    /// <summary>Clears what an update rebuilds: the recon and espionage marks, the theater mark and the no-unvisited mark are kept.</summary>
    internal void ClearForUpdate()
    {
        (OwnUnblockaded, OwnUprising, ShipyardSlot, TrainingSlot, ConstructionSlot, MineSite, RefinerySite, HasFreeEnergy) = (false, false, false, false, false, false, false, false);
        (HasFreeRawMaterial, IdleConstructionRole, IdleConstruction, RoleDefenceShort, DefenceShort, DefenceSurplus, DefendedNonKey, GarrisonShort)
            = (false, false, false, false, false, false, false, false);
        (GarrisonSurplus, AttackGarrisonSurplus, DefenseGarrisonSurplus, GarrisonTroopsShort, RoleGarrisonShort, FighterShort, HasUsableFighterSurplus)
            = (false, false, false, false, false, false, false);
        (RoleFighterShort, OwnFighters, UsableFighterShort, HasRoleSystem, KeySystem, EnemyKeySystem) = (false, false, false, false, false, false);
        (HasShipProduction, HasTrainingProduction, HasConstructionProduction, HasCharacterSite, HasUprisingNoFleet, HasDiplomacyTarget, HasNotOwn)
            = (false, false, false, false, false, false, false);
        (HasEnemyCharacter, HasEnemyMajor, HasEnemySupported, HasClaimed, HasEnemyMines, HasEnemyProducers, EnemyFleetSeen, HasOwnCaptured)
            = (false, false, false, false, false, false, false, false);
        (HasEnemyDeathStar, OpenDiplomacyTarget, OwnLowSupport, AllExplored) = (false, false, false, false);
        (AllPopulated, AllHeld, Own, Enemy, Neutral, Rim, NeutralPopulated, HasVictoryCharacters) = (false, false, false, false, false, false, false, false);
        (EnemyNoBatteries, OwnCoruscant, EnemyFewGenCores, AttackTroopSurplus, DefenseTroopSurplus, OpenTheater, ShipyardRole, EnemyFleet)
            = (false, false, false, false, false, false, false, false);
        (EnemyAssets, EnemyNoCommandKind3, Base, AssaultBase, DefenseBase, Theater, Hub, Coruscant, Headquarters)
            = (false, false, false, false, false, false, false, false, false);
    }
    /// <summary>Systems per sector (the sector record's slot count).</summary>
    public const int SystemsPerSector = 10;
    /// <summary>The TheaterKind of a sector with no theater.</summary>
    public const FleetTaskType NoTheaterKind = FleetTaskType.None;
    /// <summary>The theater kind 1-6 (planner 13), a <see cref="FleetTaskType"/> value; <see cref="NoTheaterKind"/> for none.</summary>
    public FleetTaskType TheaterKind { get; set; }
    /// <summary>The theater argument (planner 13), the site field of the type-4 site.</summary>
    public TheaterArgument TheaterArgument { get; set; }
    /// <summary>The fleet op bound to the theater.</summary>
    public int FleetOpKey { get; set; }
    /// <summary>The theater entry pushed to the op.</summary>
    public int Entry { get; set; }
    // sums over the ten systems, zeroed by every update
    public int Explored { get; set; }
    public int Populated { get; set; }
    /// <summary>Systems owned by a side or populated.</summary>
    public int Held { get; set; }
    public int OwnSystems { get; set; }
    public int EnemySystems { get; set; }
    public int NeutralSystems { get; set; }
    public int Destroyed { get; set; }
    /// <summary>Own systems' support, averaged after the sum.</summary>
    public int Support { get; set; }
    /// <summary>Own systems' energy.</summary>
    public int Energy { get; set; }
    /// <summary>Own systems' free energy.</summary>
    public int FreeEnergy { get; set; }
    /// <summary>Own systems' free raw material.</summary>
    public int FreeRawMaterial { get; set; }
    /// <summary>Own systems' garrison targets.</summary>
    public int GarrisonTarget { get; set; }
    /// <summary>Enemy systems' <see cref="AiSystem.TroopsToTake"/>.</summary>
    public int EnemyTroopsToTake { get; set; }
    /// <summary>Own Mines.</summary>
    public int Mines { get; set; }
    /// <summary>Own Refineries.</summary>
    public int Refineries { get; set; }
    /// <summary>Shipyards of own shipyard-role systems.</summary>
    public int RoleShipyards { get; set; }
    /// <summary>Shipyards of the other own systems.</summary>
    public int Shipyards { get; set; }
    public int RoleTrainingFacilities { get; set; }
    public int TrainingFacilities { get; set; }
    public int RoleConstructionYards { get; set; }
    public int ConstructionYards { get; set; }
    /// <summary>Own GenCores.</summary>
    public int GenCores { get; set; }
    /// <summary>Own Death Star shields.</summary>
    public int DeathStarShields { get; set; }
    /// <summary>Own batteries.</summary>
    public int Batteries { get; set; }
    /// <summary>Own fighters.</summary>
    public int Fighters { get; set; }
    /// <summary>Own fighters' <see cref="AiSystem.FighterFirepower"/>.</summary>
    public int FighterFirepower { get; set; }
    /// <summary>Own systems with a defence shortfall.</summary>
    public int DefenceShortSystems { get; set; }
    /// <summary>Own systems with a defence surplus.</summary>
    public int DefenceSurplusSystems { get; set; }
    /// <summary>Own garrison shortfalls.</summary>
    public int TroopShortfall { get; set; }
    /// <summary>Own garrison surpluses.</summary>
    public int TroopSurplus { get; set; }
    /// <summary>Own troops.</summary>
    public int Troops { get; set; }
    /// <summary>Own systems with a fighter shortfall.</summary>
    public int FighterShortSystems { get; set; }
    /// <summary>Own usable-fighter surpluses.</summary>
    public int UsableFighterSurplus { get; set; }
    public int TroopMaintenance { get; set; }
    public int DefenceMaintenance { get; set; }
    public int FighterMaintenance { get; set; }
    public int ProducerMaintenance { get; set; }
    /// <summary>Enemy systems' used energy.</summary>
    public int EnemyUsedEnergy { get; set; }
    public int EnemyMines { get; set; }
    public int EnemyRefineries { get; set; }
    /// <summary>Enemy and neutral shipyards.</summary>
    public int OtherShipyards { get; set; }
    /// <summary>Enemy and neutral training facilities.</summary>
    public int OtherTrainingFacilities { get; set; }
    /// <summary>Enemy and neutral construction yards.</summary>
    public int OtherConstructionYards { get; set; }
    public int EnemyGenCores { get; set; }
    public int EnemyDeathStarShields { get; set; }
    public int EnemyProducerMaintenance { get; set; }
    /// <summary>The largest enemy <see cref="AiSystem.TroopsToTake"/>.</summary>
    public int MaxEnemyTroopsToTake { get; set; }
    /// <summary>The most enemy fighters in a system.</summary>
    public int MaxEnemyFighters { get; set; }
    /// <summary>The largest enemy fighter strength (second stat).</summary>
    public int MaxEnemyFighterAntiFighterFirepower { get; set; }
    /// <summary>The largest enemy units.</summary>
    public int MaxEnemyUnits { get; set; }
    /// <summary>The smallest GenCore strength of an enemy system without batteries (0 when none).</summary>
    public int MinUnguardedGenCoreStrength { get; set; }
    // systems counted per target
    public int OwnExploredSystems { get; set; }
    public int ShipProduction { get; set; }
    public int TrainingProduction { get; set; }
    public int ConstructionProduction { get; set; }
    public int CharacterSites { get; set; }
    public int UprisingNoFleet { get; set; }
    public int DiplomacyTargets { get; set; }
    public int NotOwn { get; set; }
    public int EnemyCharacters { get; set; }
    public int EnemyMajors { get; set; }
    public int EnemySupported { get; set; }
    public int Claimed { get; set; }
    public int OwnCaptured { get; set; }
    public int EnemyDeathStars { get; set; }
    /// <summary>Own hubs with low support.</summary>
    public int LowSupportHubs { get; set; }
    /// <summary>+1 for a bound op without a theater, -1 for a theater without an op.</summary>
    public int TheaterBalance { get; set; }
    /// <summary>Enemy fleets' <see cref="AiSystem.EnemyFirepower"/> over all systems.</summary>
    public int EnemyFirepower { get; set; }
    public int EnemyAntiFighterFirepower { get; set; }
    public int EnemyFleetFighters { get; set; }
    public int VictoryCharacters { get; set; }
    /// <summary>Own unblockaded key systems.</summary>
    public int UnblockadedKeySystems { get; set; }

    /// <summary>Zeroes the sums that have a writer.</summary>
    internal void ClearSums()
    {
        (Explored, Populated, Held, OwnSystems, EnemySystems, NeutralSystems, Destroyed, Support, Energy, FreeEnergy, FreeRawMaterial) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        (GarrisonTarget, EnemyTroopsToTake, Mines, Refineries, RoleShipyards, Shipyards, RoleTrainingFacilities, TrainingFacilities) = (0, 0, 0, 0, 0, 0, 0, 0);
        (RoleConstructionYards, ConstructionYards, GenCores, DeathStarShields, Batteries, Fighters, FighterFirepower) = (0, 0, 0, 0, 0, 0, 0);
        (DefenceShortSystems, DefenceSurplusSystems, TroopShortfall, TroopSurplus, Troops, FighterShortSystems, UsableFighterSurplus) = (0, 0, 0, 0, 0, 0, 0);
        (TroopMaintenance, DefenceMaintenance, FighterMaintenance, ProducerMaintenance, EnemyUsedEnergy, EnemyMines, EnemyRefineries) = (0, 0, 0, 0, 0, 0, 0);
        (OtherShipyards, OtherTrainingFacilities, OtherConstructionYards, EnemyGenCores, EnemyDeathStarShields, EnemyProducerMaintenance) = (0, 0, 0, 0, 0, 0);
        (MaxEnemyTroopsToTake, MaxEnemyFighters, MaxEnemyFighterAntiFighterFirepower, MaxEnemyUnits, MinUnguardedGenCoreStrength) = (0, 0, 0, 0, 0);
        (OwnExploredSystems, ShipProduction, TrainingProduction, ConstructionProduction, CharacterSites, UprisingNoFleet, DiplomacyTargets, NotOwn) = (0, 0, 0, 0, 0, 0, 0, 0);
        (EnemyCharacters, EnemyMajors, EnemySupported, Claimed, OwnCaptured, EnemyDeathStars, LowSupportHubs, TheaterBalance) = (0, 0, 0, 0, 0, 0, 0, 0);
        (EnemyFirepower, EnemyAntiFighterFirepower, EnemyFleetFighters, VictoryCharacters, UnblockadedKeySystems) = (0, 0, 0, 0, 0);
    }
}
