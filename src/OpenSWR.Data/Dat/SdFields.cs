namespace OpenSWR.Data.Dat;

/// <summary>
/// Named fields of the unit and facility records read through <see cref="SdRecord"/> (the value is the field's record offset).
/// One offset can hold different fields in different files; each name says which records it belongs to.
/// Names marked Inferred follow their use: meaning in the original is unknown.
/// </summary>
public enum SdField
{
    // every unit, facility, character and special force
    Alliance = 24, Empire = 28, RefinedMaterialCost = 32, MaintenanceCost = 36, ResearchOrder = 40, ResearchDifficulty = 44,

    // capital ships, fighters and troops
    /// <summary>Combat value (Inferred): what a destroyed unit adds to its side's battle losses.</summary>
    CombatValue = 48,
    /// <summary>Detection (Inferred): the unit's strength in mission encounters.</summary>
    Detection = 52,

    // capital ships and fighters
    ShieldStrength = 56,
    /// <summary>Sublight speed (Inferred).</summary>
    Speed = 60,
    Maneuverability = 64, Hyperdrive = 68, HyperdriveIfDamaged = 72,
    ForeTurbolaser = 76, ForeIonCannon = 80, ForeLaserCannon = 84,
    AftTurbolaser = 88, AftIonCannon = 92, AftLaserCannon = 96,
    PortTurbolaser = 100, PortIonCannon = 104, PortLaserCannon = 108,
    StarboardTurbolaser = 112, StarboardIonCannon = 116, StarboardLaserCannon = 120,
    TurbolaserRange = 124, IonCannonRange = 128, LaserCannonRange = 132,
    /// <summary>Attack strength per weapon type: the sum of its four arcs in the shipped data.</summary>
    TurbolaserAttackStrength = 136, IonCannonAttackStrength = 140, LaserCannonAttackStrength = 144,
    /// <summary>The sum of the three attack strengths in the shipped data.</summary>
    TotalAttackStrength = 148,

    // capital ships
    Hull = 152, TractorBeamPower = 156, TractorBeamRange = 160,
    /// <summary>Gravity well (Inferred): nonzero on interdictors.</summary>
    GravityWell = 164,
    InterdictionStrength = 168, Bombardment = 172, DamageControl = 176, WeaponRechargeRate = 180, ShieldRechargeRate = 184,
    FighterCapacity = 188, TroopCapacity = 192, CapitalShipUnkC4 = 196,

    // fighters
    /// <summary>Torpedoes per fighter (Inferred).</summary>
    Torpedoes = 152,
    /// <summary>Torpedo range (Inferred).</summary>
    TorpedoRange = 156,
    SquadronSize = 160,
    FighterBombardment = 164,

    // troops
    TroopBombardmentDefense = 56, TroopAttackStrength = 60, TroopDefenseStrength = 64,

    // facilities
    FacilityBombardmentDefense = 48,
    /// <summary>Manufacturing and processing facilities.</summary>
    ProcessingRate = 52,
    /// <summary>Defence facilities: the attack strength of a planetary battery or ion cannon.</summary>
    DefenseAttackStrength = 52,
    /// <summary>Defence facilities: the shield strength of a shield generator.</summary>
    DefenseShieldStrength = 56,

    // sectors; X and Y are u16
    SectorGroup = 24, SectorGalaxySize = 28, SectorX = 32, SectorY = 34,

    // systems; X and Y are u16
    /// <summary>The Index of the system's sector.</summary>
    SystemSector = 24,
    SystemPicture = 28, SystemUnk20 = 32, SystemX = 36, SystemY = 38, SystemUnk28 = 40,

    // characters and special forces: skill base and variance
    DiplomacyBase = 48, DiplomacyVariance = 52, EspionageBase = 56, EspionageVariance = 60,
    ShipDesignBase = 64, ShipDesignVariance = 68, TroopTrainingBase = 72, TroopTrainingVariance = 76,
    FacilityDesignBase = 80, FacilityDesignVariance = 84, CombatBase = 88, CombatVariance = 92,
    LeadershipBase = 96, LeadershipVariance = 100, LoyaltyBase = 104, LoyaltyVariance = 108,

    // characters
    CharacterUnk20 = 32, CharacterUnk24 = 36, CharacterUnk28 = 40, CharacterUnk2C = 44,
    JediProbability = 112, IsKnownJedi = 116, JediLevelBase = 120, JediLevelVariance = 124,
    CanBeAdmiral = 128, CanBeCommander = 132, CanBeGeneral = 136, IsUnableToBetray = 140, IsJediTrainer = 144,

    // special forces
    SpecialForceMissionMask = 112,

    // missions
    MissionSpecialForceMask = 32,
}

public static class SdFields
{
    /// <summary>The value of a field; the record must have it.</summary>
    public static int Get(this SdRecord r, SdField field) => (int)r.U32At((int)field);

    /// <summary>Whether the record is long enough to hold the field.</summary>
    public static bool Has(this SdRecord r, SdField field) => ((int)field - SdFile.CommonSize) / 4 < r.Body.Length;

    /// <summary>The value of a field, 0 when the record is shorter (fighter and Death Star records are shorter than ships').</summary>
    public static int GetOrZero(this SdRecord r, SdField field) => r.Has(field) ? r.Get(field) : 0;
}
