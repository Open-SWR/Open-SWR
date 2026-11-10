using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>Catalogue value of a unit type (0 without a record).</summary>
    Product P(uint type) => Catalogue[type] ?? Product.None;

    void UpdateSystem(AiSystem r)
    {
        if (!r.Dirty) return;
        r.Dirty = false;
        var s = r.System;
        r.ClearForUpdate();
        r.ClearCounters();
        var v = See(s);
        // 1-2
        r.Owner = (int)v.Owner;
        if (v.Destroyed) r.Destroyed = true;
        if (Family.Of(s.Type) is >= Families.RimSystem and < Families.RimSystemEnd) r.Rim = true;
        bool own = v.Owner == Side, enemy = v.Owner == Enemy;
        if (own) r.Own = r.OwnedOrPopulated = true;
        else if (enemy) r.Enemy = r.OwnedOrPopulated = true;
        else r.Neutral = true;
        // 3
        if (!v.Explored) { r.NotOwn = true; return; }
        // 4. shortcut: views keep no Energy / RawMaterial / used counts; read from the real system. Free energy / raw material use the
        // live counts of the owner's Existing facilities / Mines at update time (Inferred mechanism), not the stored
        // the stored fields of the system, which keep serving the sim's energy rules
        int e = s.Energy;
        int used = Math.Min(s.Contents.Count(x => Family.IsFacility(x.Type) && x.Existing && x.Owner == s.Owner), e);
        int mined = Math.Min(s.Contents.Count(x => Family.Of(x.Type) == Families.Mine && x.Existing && x.Owner == s.Owner), s.RawMaterials);
        r.Explored = true;
        r.Energy = e;
        r.FreeEnergy = e - used;
        r.FreeRawMaterial = s.RawMaterials - mined;
        // 5. the view's Blockade; the day is kept for neutral or enemy systems only
        if (v.Blockade)
        {
            r.Blockade = true;
            if (!own) r.BlockadeDay = _game.Day;
        }
        // 6
        var contents = Contents(s);
        bool hq = contents.Any(x => Family.Of(x.Type) is >= Families.Headquarters and < Families.IonCannon && See(x).Owner == v.Owner);
        if (s.Id == Refs.CoruscantIndex) { r.KeySystem = true; r.Coruscant = true; }
        if (s.Id == Refs.YavinIndex) r.Yavin = true;
        if (hq) { r.KeySystem = true; r.Headquarters = true; r.HeadquartersSeen = true; }
        // 7
        if (own) r.Support = 100;
        bool role = r.HasRole, special = (r.HasRole || r.KeySystem);
        // 8. troops (against the catalogue summary: the best buildable troop DefenseStrength)
        foreach (var t in contents.Where(x => Family.IsTroop(x.Type)))
        {
            r.Troops++;
            r.TroopMaintenance += P(t.Type).MaintenanceCost;
            if (Usable(t)) r.UsableTroops++;
            if (!own) continue;
            bool attack = P(t.Type).AttackTroop, defense = P(t.Type).DefenseTroop;
            if (!special)
            {
                // TroopDefense counts for C only together with TroopAttack
                if (attack)
                {
                    r.AttackTroops = true;
                    if (defense) r.DefenseTroops = true;
                }
                else { r.GarrisonTroops++; r.DefenseGarrison = true; }
            }
            else if (P(t.Type).AntiFighterFirepower < BestTroopDefense)
            {
                if (attack) r.AttackTroops = true;
                if (defense) r.DefenseTroops = true;
            }
            else
            {
                r.GarrisonTroops++;
                if (attack) r.AttackGarrison = true;
                if (defense) r.DefenseGarrison = true;
            }
        }
        // 9. fleets (families 8 to 15) with a fleet record
        foreach (var x in contents.Where(x => Family.Of(x.Type) is >= Families.Fleet and < Families.Troop))
        {
            if (!FleetsById.TryGetValue(x.Ref, out var f)) continue;
            var fb = f;
            if (!fb.Own && fb.ReadyShip)
            {
                r.EnemyFirepower += f.FirepowerAllFighters;
                r.EnemyAntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
                r.EnemyShieldRecharge += f.ShieldRecharge;
                r.EnemyFleetFighters += f.Fighters;
                r.EnemyUnits += f.Units;
                r.EnemyFleet = true;
                r.EnemyFleetSeen = true;
                if (fb.DeathStar)
                {
                    r.EnemyDeathStar = true;
                    if (Side == Owner.Alliance) r.EnemyDeathStarSeen = true;
                }
            }
            if (fb.EnemyMajor) r.EnemyMajorAboard = r.EnemyCharacterAboard = true;
            if (fb.EnemyMinor) r.EnemyCharacterAboard = true;
            if (fb.EnemyCommander) r.EnemyCommander = true;
            if (fb.OwnCaptured) r.OwnCapturedAboard = true;
        }
        // 10. characters, through their agent records
        foreach (var x in contents.Where(x => Family.Of(x.Type) is >= Families.MajorCharacter and < Families.Mission))
        {
            if (!Agents.TryGetValue(x.Ref, out var a) || !a.Character) continue;
            bool captured = a.Captured, major = a.Major;
            if (!a.Enemy)
            {
                if (captured) { r.OwnCaptured = true; r.Captured++; }
                else if (major) r.OwnMajors++;
                else r.OwnMinors++;
                continue;
            }
            if (captured) { r.EnemyCaptured = true; r.Captured++; continue; }
            if (!a.Enroute) r.EnemyCharacter = true;
            if (a.HasCommandKind)
                r.EnemyCommander = true;
                if (a.CommandKind3) r.CommandKind3 = true;
            if (major)
            {
                r.EnemyMajor = true;
                r.EnemyMajors++;
                if (a.VictoryCharacter) r.VictoryCharacters++;
            }
            else r.EnemyMinors++;
        }
        // 11
        if (own && r.FreeEnergy > 0)
        {
            r.HasFreeEnergy = true;
            if (r.FreeRawMaterial > 0) r.HasFreeRawMaterial = true;
        }
        // 12. the owner's Mines / Refineries
        r.Mines = contents.Count(x => Family.Of(x.Type) == Families.Mine && See(x).Owner == v.Owner);
        r.Refineries = contents.Count(x => Family.Of(x.Type) == Families.Refinery && See(x).Owner == v.Owner);
        // 13. production facilities: shipyards (manager kind 0), training (2), construction yards (1)
        var shipyards = contents.Where(x => Family.Of(x.Type) == Families.Shipyard).ToList();
        foreach (var x in shipyards)
        {
            r.Shipyards++;
            r.ProducerMaintenance += P(x.Type).MaintenanceCost;
            r.ShipyardMaintenance += P(x.Type).MaintenanceCost;
        }
        if (shipyards.Any(Usable))
        {
            r.UsableProducer = true;
            if (r.Shipyards > 0 && !r.Unrest && IdleManager(s, ManufacturingKind.Ship)) r.IdleShipyard = true;
        }
        var training = contents.Where(x => Family.Of(x.Type) == Families.TrainingFacility).ToList();
        foreach (var x in training)
        {
            r.TrainingFacilities++;
            r.ProducerMaintenance += P(x.Type).MaintenanceCost;
            r.TrainingMaintenance += P(x.Type).MaintenanceCost;
        }
        if (training.Any(Usable))
        {
            r.UsableProducer = true;
            if (r.TrainingFacilities > 0 && !r.Unrest && IdleManager(s, ManufacturingKind.Training)) r.IdleTraining = true;
        }
        var yards = contents.Where(x => Family.Of(x.Type) == Families.ConstructionYard).ToList();
        foreach (var x in yards)
        {
            r.ConstructionYards++;
            r.ProducerMaintenance += P(x.Type).MaintenanceCost;
            r.ConstructionMaintenance += P(x.Type).MaintenanceCost;
        }
        if (yards.Any(Usable))
        {
            r.UsableProducer = true;
            if (r.ConstructionYards > 0 && !r.Unrest && IdleManager(s, ManufacturingKind.Construction)) r.IdleConstruction = true;
        }
        // 14. defences
        bool shieldUsable = false;
        foreach (var x in contents)
            switch (Family.Of(x.Type))
            {
                case Families.PlanetaryShield:
                    r.GenCores++;
                    r.DefenceMaintenance += P(x.Type).MaintenanceCost;
                    if (Usable(x)) r.GenCoreStrength += P(x.Type).AntiFighterFirepower;
                    break;
                case Families.DeathStarShield:
                    r.DeathStarShields++;
                    r.DefenceMaintenance += P(x.Type).MaintenanceCost;
                    if (Usable(x)) { r.UsableDeathStarShield = true; shieldUsable = true; }
                    break;
                case Families.IonCannon or Families.Gun:
                    r.Batteries++;
                    r.DefenceMaintenance += P(x.Type).MaintenanceCost;
                    if (Usable(x)) r.BatteryStrength += P(x.Type).Firepower;
                    break;
            }
        if (r.GenCores > 1) r.ManyGenCores = true;
        // 15
        if (!v.Populated)
        {
            if (r.HasFreeEnergy)
            {
                r.RefinerySite = true;
                if (r.HasFreeRawMaterial) r.MineSite = true;
            }
        }
        else Populated(r, v, own, enemy, e, hq, role, special, contents, shieldUsable);
        // 16
        if (own) r.OwnExplored = r.Claimed = true;
        else
        {
            r.NotOwn = true;
            if (enemy) r.Claimed = true;
        }
    }

    /// <summary>The system's manager of the kind has an empty queue (Inferred: the manager's queue field is 0).</summary>
    static bool IdleManager(StarSystem s, ManufacturingKind kind) => s.Managers[(int)kind] is { } m && m.Orders.Count == 0;

    /// <summary>System update step 15, populated systems.</summary>
    void Populated(AiSystem r, Seen v, bool own, bool enemy, int e, bool hq, bool role, bool special, List<GameObject> contents, bool shieldUsable)
    {
        var s = r.System;
        bool coruscant = s.Id == Refs.CoruscantIndex, enemyFleet = r.EnemyFleet;
        // 15.1
        if (v.Uprising) r.Uprising = true;
        r.OwnedOrPopulated = r.Populated = true;
        int support = Side == Owner.Alliance ? v.Support : 100 - v.Support; // the AI side's share
        r.Support = support;
        if (support < 70) r.LowSupport = true;
        // 15.2 fighters
        foreach (var x in contents.Where(x => Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters))
        {
            r.Fighters++;
            r.FighterMaintenance += P(x.Type).MaintenanceCost;
            if (own && (!special || BestFighterAntiFighterFirepower <= P(x.Type).AntiFighterFirepower)) r.StrongFighters++;
            if (!Usable(x)) continue;
            // damaged strengths (each stat times x div a stat); Inferred:
            // x = squad size minus SquadSizeDamage
            if (P(x.Type).SquadronSize is > 0 and var squad)
            {
                int left = squad - x.SquadSizeDamage;
                r.FighterFirepower += P(x.Type).Firepower * left / squad;
                r.FighterAntiFighterFirepower += P(x.Type).AntiFighterFirepower * left / squad;
                r.FighterShields += P(x.Type).Shields * left / squad;
            }
            r.UsableFighters++;
            if (own) r.OwnFighters = true;
        }
        // 15.3
        if (own && !r.Unrest && !role)
        {
            r.CalmWithoutRole = true;
            int c = r.Batteries + r.DeathStarShields + r.Refineries + r.Mines + r.GenCores + (hq ? 1 : 0);
            if (c < e) r.RefinerySite = true;
            if (c < e && r.FreeRawMaterial > 0) r.MineSite = true;
        }
        bool neutral = !own && !enemy;
        if (neutral)
        {
            // 15.4
            if (support < 90) r.UnrestTroops = Math.Min(6, r.UnrestTroops + (99 - support) / 10);
            r.TroopsToTake = r.Batteries + r.UnrestTroops;
            if (!enemyFleet) r.DiplomacyTarget = true;
            if (coruscant) r.VictoryCharacters++;
            return;
        }
        if (enemy)
        {
            // 15.5
            if (support < 80) r.UnrestTroops = Math.Min(6, r.UnrestTroops + (89 - support) / 10);
            r.TroopsToTake = r.Troops + r.UnrestTroops + r.Batteries;
            if (support > 50) r.EnemySupported = true;
            r.EnemyUnits += r.UsableTroops + r.UsableFighters;
            if (coruscant) r.VictoryCharacters++;
            if (r.GenCoreStrength > 0) r.EnemyGenCores = true;
            if (shieldUsable) r.EnemyDeathStarShield = true;
            if (r.BatteryStrength > 0) r.EnemyBatteries = true;
            if (r.UsableTroops > 0) r.EnemyTroops = true;
            if (r.UsableFighters > 0) r.EnemyFighters = true;
            if ((r.Mines > 0 || r.Refineries > 0 || r.Shipyards > 0 || r.TrainingFacilities > 0 || r.ConstructionYards > 0 || r.DeathStarShields > 0
                || r.GenCores > 0) && r.Batteries == 0) r.NoBatteries = true;
            if (r.Shipyards > 0 || r.TrainingFacilities > 0 || r.ConstructionYards > 0) r.EnemyProducers = true;
            if (r.Mines > 0 || r.Refineries > 0) r.EnemyMines = true;
            return;
        }
        // 15.6 own: garrison and defence targets with the model constants
        int K(ModelConstant k) => Constant(k);
        if (support < 70) r.UnrestTroops = Math.Min(6, r.UnrestTroops + (79 - support) / 10);
        int rr = Side == Owner.Empire ? 1 : 0, g, b;
        if (coruscant)
        {
            if (e < rr + 2) (g, b) = (e - rr, 0);
            else
            {
                (g, b) = (K(ModelConstant.CoruscantGenCores), K(ModelConstant.CoruscantBatteries));
                if (e < rr + K(ModelConstant.CoruscantBatteries) + K(ModelConstant.CoruscantGenCores)) b = e - K(ModelConstant.CoruscantGenCores) - rr;
            }
            if (Side == Owner.Empire && DeathStarShieldAvailable && OwnDeathStar && r.DeathStarShields == 0) r.DeathStarShieldWanted = true;
        }
        else if (role)
            (g, b) = K(ModelConstant.RoleBatteries) + K(ModelConstant.RoleGenCores) < e ? (K(ModelConstant.RoleGenCores), K(ModelConstant.RoleBatteries)) : K(ModelConstant.GenCores) < e ? (K(ModelConstant.GenCores), 0) : (0, 0);
        else
            (g, b) = r.UnrestTroops > 0 && K(ModelConstant.Batteries) + K(ModelConstant.GenCores) < e ? (K(ModelConstant.GenCores), K(ModelConstant.Batteries)) : (0, 0);
        r.GenCoreTarget = g;
        r.BatteryTarget = b;
        int d1 = r.GenCores - g, d2 = r.Batteries - b;
        if (d1 < 0) r.GenCoreShort = true;
        if (d1 <= 0 && d2 <= 0)
        {
            r.DefenceBalance = d1 + d2;
            if (r.DefenceBalance < 0) r.DefenceShort = true;
        }
        else r.DefenceBalance = d1 <= 0 ? d2 : d2 <= 0 ? d1 : d1 + d2;
        if (v.Uprising) r.UnrestTroops *= 2;
        bool key = r.KeySystem;
        int t = key ? K(ModelConstant.KeyGarrison) : role ? K(ModelConstant.RoleGarrison) : K(ModelConstant.Garrison);
        r.GarrisonTarget = t = Math.Max(t, r.UnrestTroops);
        if (!(r.Blockade && (r.AssaultBase || r.DefenseBase)))
        {
            if (r.UsableTroops < t) { r.GarrisonShort = true; r.TroopShortfall = t - r.UsableTroops; }
            else
            {
                if (r.UsableTroops > t) { r.GarrisonSurplus = true; r.TroopSurplus = r.UsableTroops - t; }
                if (r.GarrisonTroops < t) { r.GarrisonTroopsShort = true; r.TroopShortfall = t - r.GarrisonTroops; }
            }
        }
        int fk = key ? K(ModelConstant.KeyFighterMargin) : role ? K(ModelConstant.RoleFighterMargin) : K(ModelConstant.FighterMargin);
        if (!key && !role && r.GarrisonTroops < 1) (r.FighterBalance, r.UsableFighterBalance) = (r.Fighters, r.Fighters);
        else (r.FighterBalance, r.UsableFighterBalance) = (r.StrongFighters - fk, r.UsableFighters - fk);
        if (r.FighterBalance < 0) r.FighterShort = true;
        if (r.UsableFighterBalance < 0) r.UsableFighterShort = true;
        int p = (r.AnyConstructionRole ? r.ConstructionYards : 0) + (r.ShipyardRole ? r.Shipyards : 0)
            + (r.TrainingRole ? r.TrainingFacilities : 0);
        r.FreeSlots = e - (p + Math.Max(r.GenCores, r.GenCoreTarget) + Math.Max(r.Batteries, r.BatteryTarget) + (hq ? 1 : 0));
        if (!r.Unrest)
        {
            bool Yard(uint family) => contents.Any(x => Family.Of(x.Type) == family && Usable(x));
            if (MinorCharactersToCome > 0 && s.Id != Refs.YavinIndex) r.CharacterSite = true;
            if (r.ShipyardRole && !ShipLineDone && Yard(Families.Shipyard) && r.UsableTroops > 0) r.ShipProduction = true;
            if (r.TrainingRole && !TroopLineDone && Yard(Families.TrainingFacility) && r.UsableTroops > 0) r.TrainingProduction = true;
            if (r.AnyConstructionRole && !FacilityLineDone && Yard(Families.ConstructionYard) && r.UsableTroops > 0) r.ConstructionProduction = true;
        }
        if (r.Uprising && !enemyFleet) r.UprisingNoFleet = true;
        if (r.LowSupport && !enemyFleet && !r.Uprising) r.DiplomacyTarget = true;
    }

    // ---- sector record ----
}
