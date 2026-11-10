using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    void UpdateSector(AiSector x)
    {
        x.Dirty = false;
        x.ClearForUpdate(); // shortcut: the derived needs are cleared with the sector (the update "clears its derived flags")
        x.ClearSums();
        x.MinUnguardedGenCoreStrength = 100000;
        foreach (var y in x.Systems)
        {
            UpdateSystem(y);
            AddSystem(x, y);
        }
        if (!x.EnemyNoBatteries) x.MinUnguardedGenCoreStrength = 0;
        if (x.OwnSystems > 0) x.Support /= x.OwnSystems;
        if (x.Explored == AiSector.SystemsPerSector) x.AllExplored = true;
        if (x.Populated == AiSector.SystemsPerSector) x.AllPopulated = true;
        if (x.Held == AiSector.SystemsPerSector) x.AllHeld = true;
        if (x.VictoryCharacters > 0) x.HasVictoryCharacters = true;
        // the theater kind (planner 13) and its fleet op
        if (x.TheaterKind == AiSector.NoTheaterKind && x.FleetOpKey != 0) x.TheaterBalance = 1;
        if (x.TheaterKind != AiSector.NoTheaterKind)
        {
            x.Theater = true;
            if (x.FleetOpKey == 0) { x.TheaterBalance = -1; x.OpenTheater = true; }
        }
    }

    /// <summary>Adds one system record to its sector's flags and sums.</summary>
    static void AddSystem(AiSector x, AiSystem y)
    {
        x.VictoryCharacters += y.VictoryCharacters;
        if (y.Explored) x.Explored++;
        if (y.Populated) x.Populated++;
        if (y.OwnedOrPopulated) x.Held++;
        if (y.Destroyed) x.Destroyed++;
        if (y.Rim) x.Rim = true;
        if (y.Coruscant) x.Coruscant = true;
        if (y.Headquarters) x.Headquarters = true;
        if (y.EnemyFleet) x.EnemyFleet = true;
        // systems counted per target, with the sector's target mark
        if (y.OwnExplored) x.OwnExploredSystems++;
        if (y.CharacterSite) { x.CharacterSites++; x.HasCharacterSite = true; }
        if (y.ShipProduction) { x.ShipProduction++; x.HasShipProduction = true; }
        if (y.TrainingProduction) { x.TrainingProduction++; x.HasTrainingProduction = true; }
        if (y.ConstructionProduction) { x.ConstructionProduction++; x.HasConstructionProduction = true; }
        if (y.UprisingNoFleet) { x.UprisingNoFleet++; x.HasUprisingNoFleet = true; }
        if (y.DiplomacyTarget)
        {
            x.DiplomacyTargets++;
            x.HasDiplomacyTarget = true;
            if (!y.DiplomacyMark) x.OpenDiplomacyTarget = true;
        }
        if (y.NotOwn) { x.NotOwn++; x.HasNotOwn = true; }
        if ((y.EnemyCharacter || y.EnemyCharacterAboard)) { x.EnemyCharacters++; x.HasEnemyCharacter = true; }
        if ((y.EnemyMajor || y.EnemyMajorAboard)) { x.EnemyMajors++; x.HasEnemyMajor = true; }
        if (y.EnemySupported) { x.EnemySupported++; x.HasEnemySupported = true; }
        if (y.Claimed) { x.Claimed++; x.HasClaimed = true; }
        if ((y.OwnCaptured || y.OwnCapturedAboard)) { x.OwnCaptured++; x.HasOwnCaptured = true; }
        if (y.EnemyDeathStarSeen) { x.EnemyDeathStars++; x.HasEnemyDeathStar = true; }
        if (y.EnemyMines) x.HasEnemyMines = true;
        if (y.EnemyProducers) x.HasEnemyProducers = true;
        if (y.EnemyFleetSeen) x.EnemyFleetSeen = true;
        bool role = y.HasRole;
        if (y.Own)
        {
            // own system
            x.Own = true;
            x.OwnSystems++;
            x.Support += y.Support;
            x.Energy += y.Energy;
            x.Mines += y.Mines;
            x.Refineries += y.Refineries;
            x.GenCores += y.GenCores;
            x.DeathStarShields += y.DeathStarShields;
            x.Batteries += y.Batteries;
            x.Fighters += y.Fighters;
            x.FreeRawMaterial += y.FreeRawMaterial;
            x.FreeEnergy += y.FreeEnergy;
            x.GarrisonTarget += y.GarrisonTarget;
            x.Troops += y.Troops;
            x.TroopMaintenance += y.TroopMaintenance;
            x.DefenceMaintenance += y.DefenceMaintenance;
            x.FighterMaintenance += y.FighterMaintenance;
            x.ProducerMaintenance += y.ProducerMaintenance;
            x.FighterFirepower += y.FighterFirepower;
            x.EnemyFirepower += y.EnemyFirepower;
            x.EnemyAntiFighterFirepower += y.EnemyAntiFighterFirepower;
            x.EnemyFleetFighters += y.EnemyFleetFighters;
            if (y.ShipyardRole) x.RoleShipyards += y.Shipyards; else x.Shipyards += y.Shipyards;
            if (y.TrainingRole) x.RoleTrainingFacilities += y.TrainingFacilities; else x.TrainingFacilities += y.TrainingFacilities;
            if (y.AnyConstructionRole) x.RoleConstructionYards += y.ConstructionYards; else x.ConstructionYards += y.ConstructionYards;
            bool calm = !y.Unrest, unblockaded = !y.Blockade;
            if (unblockaded) x.OwnUnblockaded = true;
            if (calm && !y.Yavin && y.FreeSlots > 0)
            {
                if (!y.ShipyardRole) x.ShipyardSlot = true;
                if (!y.TrainingRole) x.TrainingSlot = true;
                if (!y.AnyConstructionRole) x.ConstructionSlot = true;
            }
            if (calm)
            {
                if (y.MineSite) x.MineSite = true;
                if (y.RefinerySite) x.RefinerySite = true;
                if (y.HasFreeEnergy) x.HasFreeEnergy = true;
                if (y.HasFreeRawMaterial) x.HasFreeRawMaterial = true;
                if (y.ConstructionRole && y.IdleConstruction) x.IdleConstructionRole = true;
            }
            if (y.IdleConstruction && !y.AnyConstructionRole) x.IdleConstruction = true;
            if (role)
            {
                x.HasRoleSystem = true;
                if (y.ShipyardRole && unblockaded) x.ShipyardRole = true;
            }
            if (y.Base)
            {
                x.Base = true;
                if (y.AssaultBase) x.AssaultBase = true;
                else if (y.DefenseBase) x.DefenseBase = true;
            }
            if (y.Hub)
            {
                x.LowSupportHubs++;
                if (!y.CharacterClaim) x.Hub = true;
            }
            if (y.DefenceBalance < 0)
            {
                x.DefenceShort = true;
                x.DefenceShortSystems++;
                if (role) x.RoleDefenceShort = true;
            }
            if (y.DefenceBalance > 0) { x.DefenceSurplus = true; x.DefenceSurplusSystems++; }
            if (!y.KeySystem && y.Batteries > 0 && y.GenCores > 0) x.DefendedNonKey = true;
            if (y.GarrisonSurplus)
            {
                x.GarrisonSurplus = true;
                x.TroopSurplus += y.TroopSurplus;
                if (y.AttackGarrison) x.AttackGarrisonSurplus = true;
                if (y.DefenseGarrison) x.DefenseGarrisonSurplus = true;
                if (y.AttackTroops) x.AttackTroopSurplus = true;
                if (y.DefenseTroops) x.DefenseTroopSurplus = true;
            }
            if (y.GarrisonShort)
            {
                x.GarrisonShort = true;
                x.TroopShortfall += y.TroopShortfall;
                if (role) x.RoleGarrisonShort = true;
            }
            if (y.GarrisonTroopsShort) x.GarrisonTroopsShort = true;
            if (y.UsableFighterBalance > 0 && !y.Base && unblockaded)
            {
                x.HasUsableFighterSurplus = true;
                x.UsableFighterSurplus += y.UsableFighterBalance;
            }
            if (y.FighterBalance < 0)
            {
                x.FighterShort = true;
                x.FighterShortSystems++;
                if (role) x.RoleFighterShort = true;
                if (y.UsableFighterShort) x.UsableFighterShort = true;
            }
            if (y.OwnFighters) x.OwnFighters = true;
            if (y.Uprising) x.OwnUprising = true;
            if (y.KeySystem)
            {
                x.KeySystem = true;
                if (y.Coruscant) x.OwnCoruscant = true;
                if (unblockaded) x.UnblockadedKeySystems++;
            }
            if (y.LowSupport) x.OwnLowSupport = true;
        }
        else if (y.Enemy)
        {
            // enemy system
            x.Enemy = true;
            x.EnemySystems++;
            x.EnemyMines += y.Mines;
            x.EnemyRefineries += y.Refineries;
            x.EnemyGenCores += y.GenCores;
            x.EnemyDeathStarShields += y.DeathStarShields;
            x.EnemyProducerMaintenance += y.ProducerMaintenance;
            x.OtherShipyards += y.Shipyards;
            x.OtherTrainingFacilities += y.TrainingFacilities;
            x.OtherConstructionYards += y.ConstructionYards;
            x.EnemyFirepower += y.EnemyFirepower;
            x.EnemyAntiFighterFirepower += y.EnemyAntiFighterFirepower;
            x.EnemyFleetFighters += y.EnemyFleetFighters;
            x.EnemyUsedEnergy += y.Energy - y.FreeEnergy;
            x.EnemyTroopsToTake += y.TroopsToTake;
            x.MaxEnemyTroopsToTake = Math.Max(x.MaxEnemyTroopsToTake, y.TroopsToTake);
            x.MaxEnemyFighters = Math.Max(x.MaxEnemyFighters, y.Fighters);
            x.MaxEnemyFighterAntiFighterFirepower = Math.Max(x.MaxEnemyFighterAntiFighterFirepower, y.FighterAntiFighterFirepower);
            x.MaxEnemyUnits = Math.Max(x.MaxEnemyUnits, y.EnemyUnits);
            if (y.KeySystem) x.EnemyKeySystem = true;
            if (y.NoBatteries)
            {
                x.EnemyNoBatteries = true;
                x.MinUnguardedGenCoreStrength = Math.Min(x.MinUnguardedGenCoreStrength, y.GenCoreStrength);
            }
            if (!y.ManyGenCores) x.EnemyFewGenCores = true;
            if (!y.CommandKind3) x.EnemyNoCommandKind3 = true;
            if (y.EnemyMines || y.EnemyProducers || y.EnemyGenCores || y.EnemyBatteries || y.EnemyTroops || y.EnemyFighters || y.EnemyFleetSeen
                || y.HeadquartersSeen) x.EnemyAssets = true;
        }
        else if (y.Neutral)
        {
            // neutral system
            x.Neutral = true;
            x.NeutralSystems++;
            x.OtherShipyards += y.Shipyards;
            x.OtherTrainingFacilities += y.TrainingFacilities;
            x.OtherConstructionYards += y.ConstructionYards;
            x.EnemyFirepower += y.EnemyFirepower;
            x.EnemyAntiFighterFirepower += y.EnemyAntiFighterFirepower;
            x.EnemyFleetFighters += y.EnemyFleetFighters;
            if (y.KeySystem) x.KeySystem = true;
            if (y.OwnedOrPopulated) x.NeutralPopulated = true;
        }
    }

    // ---- catalogue summary ----
}
