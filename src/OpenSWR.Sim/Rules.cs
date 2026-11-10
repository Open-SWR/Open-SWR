using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>Integer helpers used by the rules.</summary>
public static class GameMath
{
    /// <summary>trunc(n * p / 100)</summary>
    public static int Pct(int n, int p) => n * p / 100;

    public static int Clamp(int v, int lo, int hi) => Math.Min(hi, Math.Max(lo, v));

    /// <summary>(a + b - 1) div b</summary>
    public static int CeilDiv(int a, int b) => (a + b - 1) / b;
}

/// <summary>GNPRTB / SDPRTB parameters read with the settings column.</summary>
public sealed class Params(GameDefinitions defs, int column)
{
    readonly Dictionary<uint, ParamTableEntry> _general = defs.GeneralParams.GroupBy(p => p.ParamId).ToDictionary(g => g.Key, g => g.First());
    readonly Dictionary<uint, ParamTableEntry> _side = defs.SideParams.GroupBy(p => p.ParamId).ToDictionary(g => g.Key, g => g.First());

    public int Column => column;

    /// <summary>G id: Values[col], 0 when the id is missing or col is outside 0-7.</summary>
    internal int General(GeneralParam id) => column is >= 0 and <= 7 && _general.TryGetValue((uint)id, out var p) ? p.Values[column] : 0;

    /// <summary>S id[side]: Values[2*col + side - 1], 0 when missing.</summary>
    internal int Side(SideParam id, Owner side) =>
        side is Owner.Alliance or Owner.Empire && _side.TryGetValue((uint)id, out var p) ? p.Values[2 * column + (int)side - 1] : 0;
}

/// <summary>
/// GNPRTB row ids. Pairs named Add / Roll form a value add + roll(Roll); pairs named Days / Variance are timer Base and
/// Variance.
/// </summary>
public enum GeneralParam : uint
{
    /// <summary>Hyperdrive modifier (percent) of a travelling object without its own.</summary>
    HyperdriveModifier = 1,
    /// <summary>Divisor of a defence's attack strength in the assault fire chance.</summary>
    AssaultFireDivisor = 512,
    DuelLeadershipDivisor = 513,
    DuelRoll = 514,
    /// <summary>Duel values at or below this kill the attacking troop.</summary>
    DuelAttackerLoss = 515,
    /// <summary>Duel values at or above this kill the defending troop.</summary>
    DuelDefenderLoss = 516,
    CollateralDamageChance = 517,
    /// <summary>Divisor of the system general's leadership in the defending ships' fire.</summary>
    DefenseLeadershipDivisor = 1536,
    BombardLeadershipDivisor = 1537,
    BombardHitMin = 1538,
    BombardHitMax = 1539,
    EnergyBombardDefense = 1540,
    RawMaterialBombardDefense = 1541,
    /// <summary>Chance per minor character on a destroyed planet to be wounded.</summary>
    DestroyedPlanetWoundChance = 1542,
    /// <summary>Highest level of one critical damage kind.</summary>
    CriticalDamageLimit = 2048,
    CriticalHitRoll = 2049,
    CriticalHitThreshold = 2050,
    CriticalKindRoll = 2051,
    CapitalShipFastRepairDays = 2052,
    CapitalShipRepairDays = 2053,
    /// <summary>Force at or above which a character heals fast.</summary>
    FastHealForce = 2560,
    FastHealDays = 2561,
    HealDays = 2562,
    FastHealAmount = 2563,
    HealAmount = 2564,
    /// <summary>Lowest injury chance of a foiled mission member.</summary>
    MissionInjuryMinChance = 2565,
    MissionInjuryAdd = 2566,
    MissionInjuryRoll = 2567,
    /// <summary>Lowest injury chance of a character on a lost ship.</summary>
    ShipLossInjuryMinChance = 2568,
    ShipLossInjuryAdd = 2569,
    ShipLossInjuryRoll = 2570,
    DuelWoundMinChance = 2571,
    DuelWoundAdd = 2572,
    DuelWoundRoll = 2573,
    /// <summary>Chance (and first roll) of the wound an assassination deals.</summary>
    AssassinationWoundChance = 2574,
    AssassinationWoundAdd = 2575,
    AssassinationWoundRoll = 2576,
    /// <summary>Chance that a wounded minor character dies.</summary>
    MinorDeathChance = 2577,
    DuelCombatGain = 2578,
    /// <summary>Force at or above which a Jedi trainer discovers Force users.</summary>
    ForceDiscoveryMinForce = 2580,
    JediTeacherMinForce = 2581,
    ForceDiscoveryDays = 2582,
    ForceDiscoveryVariance = 2583,
    EscapeDays = 2584,
    EscapeVariance = 2585,
    /// <summary>Percent of the Force gap Luke gains when meeting Vader.</summary>
    LukeVaderForcePercent = 3072,
    LukePalpatineForcePercent = 3073,
    LeiaVaderForcePercent = 3074,
    LeiaPalpatineForcePercent = 3075,
    /// <summary>Leadership bonus (percent) of Empire characters while the Emperor is at Coruscant.</summary>
    EmperorLeadershipBonus = 3076,
    /// <summary>Luke is wounded by learning his heritage while his Force is below this.</summary>
    HeritageWoundMaxForce = 3077,
    FinalBattleMinForce = 3078,
    HeritageWoundAdd = 3079,
    HeritageWoundRoll = 3080,
    LukeEvasionForceGain = 3081,
    LeiaEvasionForceGain = 3082,
    /// <summary>Hyperdrive modifier (percent) of a mission while Han Solo resolves.</summary>
    HanHyperdriveModifier = 3083,
    LukeVaderForceMinGain = 3084,
    LukePalpatineForceMinGain = 3085,
    LeiaVaderForceMinGain = 3086,
    LeiaPalpatineForceMinGain = 3087,
    FoilKeyOffset = 3584,
    /// <summary>Lowest Force of a character a mission team can meet.</summary>
    ForceEncounterMinForce = 3586,
    ForceDetectionOffset = 3587,
    DecoyKeyPercent = 3588,
    FoilKeyPercent = 3589,
    /// <summary>Percent of a mine or refinery's capacity per cycle step.</summary>
    CycleCapacityPercent = 4096,
    FighterFastRepairDays = 4608,
    FighterRepairDays = 4609,
    /// <summary>Distance unit of travel time.</summary>
    TravelDistanceDivisor = 5120,
    DestroyedPlanetRimDelta = 5121,
    DestroyedPlanetRimLimit = 5122,
    DestroyedPlanetCoreDelta = 5123,
    DestroyedPlanetCoreLimit = 5124,
    HqLossDelta = 5127,
    HqLossLimit = 5128,
    AllianceFleetAtYavinChance = 5129,
    YavinUnitsFirstKey = 5130,
    YavinUnitsLastKey = 5131,
    HqUnitsFirstKey = 5132,
    HqUnitsLastKey = 5133,
    CoruscantUnitsFirstKey = 5134,
    CoruscantUnitsLastKey = 5135,
    AllianceFleetUnitsFirstKey = 5136,
    AllianceFleetUnitsLastKey = 5137,
    EmpireFleetUnitsFirstKey = 5138,
    EmpireFleetUnitsLastKey = 5139,
    HqFacilitiesFirstKey = 5140,
    HqFacilitiesLastKey = 5141,
    CoruscantFacilitiesFirstKey = 5142,
    CoruscantFacilitiesLastKey = 5143,
    BattleSupportDivisor = 5152,
    UprisingLeadershipDivisor = 6144,
    InciteSupportDelta = 6145,
    DagobahDays = 6146,
    DagobahVariance = 6147,
    BountyHuntersDays = 6148,
    BountyHuntersVariance = 6149,
    BountyHuntersChance = 6150,
    /// <summary>Luke wins the final battle at or above this Force.</summary>
    FinalBattleWinForce = 6151,
    FinalBattleWoundAdd = 6152,
    FinalBattleWoundRoll = 6153,
    PalaceEspionageDivisor = 6154,
    PalaceCombatDivisor = 6155,
    DiplomacySkillGain = 6156,
    EspionageSkillGain = 6157,
    ResearchSkillGain = 6158,
    RecruitmentSkillGain = 6159,
    InciteSkillGain = 6160,
    SubdueSkillGain = 6161,
    RescueSkillGain = 6162,
    AbductionSkillGain = 6163,
    AssassinationSkillGain = 6164,
    SabotageEspionageGain = 6165,
    SabotageCombatGain = 6166,
    DeathStarSabotageEspionageGain = 6167,
    DeathStarSabotageCombatGain = 6168,
    HanRescueEspionageGain = 6169,
    HanRescueCombatGain = 6170,
    ResearchPointsAdd = 6171,
    ResearchPointsRoll = 6172,
    /// <summary>Highest Force gain in Jedi training, percent of the gap to the teacher.</summary>
    JediTrainingForcePercent = 6173,
    /// <summary>Force gain (percent) of a completed Dagobah training.</summary>
    DagobahForcePercent = 6174,
    /// <summary>Days per percent of Force gain of an interrupted Dagobah training.</summary>
    DagobahDaysPerPercent = 6175,
    EspionageRevealAdd = 6176,
    EspionageRevealRoll = 6177,
    CoruscantRevealAdd = 6178,
    CoruscantRevealRoll = 6179,
    HqRevealAdd = 6180,
    HqRevealRoll = 6181,
    DiplomacyOwnSupportAdd = 6183,
    DiplomacyOwnSupportRoll = 6184,
    DiplomacyNeutralSupportAdd = 6185,
    DiplomacyNeutralSupportRoll = 6186,
    SubdueOwnSupportAdd = 6187,
    SubdueOwnSupportRoll = 6188,
    SubdueNeutralSupportAdd = 6189,
    SubdueNeutralSupportRoll = 6190,
    MaintenanceEnforcementDays = 7168,
    ResearchDays = 7169,
    ResearchVariance = 7170,
    /// <summary>Divisor (and troop factor) of support effects near the Death Star.</summary>
    DeathStarSupportDivisor = 7680,
    DeathStarSupportChangeDivisor = 7681,
    UprisingGarrisonFactor = 7682,
    /// <summary>Planetary shields that block a bombardment.</summary>
    BombardShieldCount = 7683,
    BlockadeShipPenalty = 7684,
    BlockadeFighterPenalty = 7685,
    SmugglingShipPenalty = 7686,
    SmugglingFighterPenalty = 7687,
    SmugglingTroopPenalty = 7688,
    LowSupportDelta = 7689,
    LowSupportThreshold = 7690,
    GarrisonSupportDelta = 7691,
    GarrisonSupportDays = 7692,
    BlockadePopularDelta = 7693,
    BlockadePopularDays = 7694,
    BlockadeUnpopularDelta = 7695,
    BlockadeUnpopularDays = 7696,
    UprisingSupportDelta = 7697,
    UprisingSupportDays = 7698,
    SuppressDays = 7699,
    SuppressVariance = 7700,
    UprisingIncidentDays = 7701,
    UprisingIncidentVariance = 7702,
    InformantDays = 7703,
    InformantVariance = 7704,
    CivilianBombardSupportDelta = 7705,
    UprisingRollAdd = 7707,
    UprisingRollRoll = 7708,
    InformantAdd = 7709,
    InformantRoll = 7710,
    RawMaterialsMin = 7711,
    RawMaterialsMax = 7712,
    EnergyMin = 7713,
    EnergyMax = 7714,
    ResourceLossFactor = 7715,
    DisasterChance = 7716,
    DisasterDays = 7717,
    DisasterVariance = 7718,
    ResourceEventDays = 7719,
    ResourceEventVariance = 7720,
    CoreEnergyAdd = 7721,
    CoreEnergyRoll = 7722,
    CoreRawMaterialsAdd = 7723,
    CoreRawMaterialsRoll = 7724,
    RimEnergyAdd = 7725,
    RimEnergyFirstRoll = 7726,
    RimEnergySecondRoll = 7727,
    RimRawMaterialsAdd = 7728,
    RimRawMaterialsRoll = 7729,
    CorePopulatedChance = 7730,
    RimPopulatedChance = 7731,
    /// <summary>Highest owner support at which smuggling happens.</summary>
    SmugglingMaxSupport = 7732,
    SmugglingLowRate = 7733,
    SmugglingMidSupport = 7734,
    SmugglingMidRate = 7735,
    SmugglingHighSupport = 7736,
    SmugglingHighRate = 7737,
    /// <summary>Support share that gives a side support control.</summary>
    SupportControlShare = 7760,
    /// <summary>Support share below which a system needs a garrison.</summary>
    GarrisonSupportThreshold = 7761,
    /// <summary>Support per garrison troop (negative).</summary>
    GarrisonSupportPerTroop = 7762,
    ProductionModifierBase = 7763,
    NeutralCoreSupportRoll = 7764,
    RimSupportRoll = 7765,
    CoreMineFactor = 7766,
    RimMineFactor = 7767,
}

/// <summary>SDPRTB row ids (one value per side).</summary>
public enum SideParam : uint
{
    /// <summary>Minor characters placed in a small galaxy.</summary>
    MinorCharactersSmall = 5120,
    MinorCharactersMedium = 5121,
    MinorCharactersLarge = 5122,
    RimBombardDelta = 5128,
    RimBombardLimit = 5129,
    CoreBombardDelta = 5130,
    CoreBombardLimit = 5131,
    BattleWaveLimit = 5136,
    BattleWaveMaxDelta = 5137,
    BattleWinnerBase = 5138,
    BattleWinnerScale = 5139,
    BattleLoserBase = 5140,
    BattleLoserScale = 5141,
    OwnerChangeDelta = 5152,
    OwnerChangeLimit = 5153,
    CoruscantOwnerChangeDelta = 5154,
    CoruscantOwnerChangeLimit = 5155,
    /// <summary>Starting unit budget (percent of free maintenance) in a small galaxy.</summary>
    StartingUnitBudgetSmall = 5168,
    StartingUnitBudgetMedium = 5169,
    StartingUnitBudgetLarge = 5170,
    MilitaryGainedDelta = 5184,
    MilitaryGainedLimit = 5185,
    MilitaryLostDelta = 5186,
    MilitaryLostLimit = 5187,
    MilitaryKeptDelta = 5188,
    MilitaryKeptLimit = 5189,
    /// <summary>Share (percent) of core systems a side controls loyally at setup. Meaning in the original is unknown.</summary>
    SetupLoyalShare = 7680,
    /// <summary>Setup share (percent) paired with SetupLoyalShare. Meaning in the original is unknown.</summary>
    SetupDisloyalShare = 7681,
    LoyalSupportAdd = 7682,
    LoyalSupportRoll = 7683,
    DisloyalSupportAdd = 7684,
    DisloyalSupportRoll = 7685,
}

/// <summary>GNPRTB values of one settings column, read once from a Params.</summary>
public sealed class RuleSettings
{
    readonly SideSettings _alliance, _empire, _none;

    public RuleSettings(Params p)
    {
        _alliance = new SideSettings(p, Owner.Alliance);
        _empire = new SideSettings(p, Owner.Empire);
        _none = new SideSettings(p, Owner.None);
        HyperdriveModifier = p.General(GeneralParam.HyperdriveModifier);
        AssaultFireDivisor = p.General(GeneralParam.AssaultFireDivisor);
        DuelLeadershipDivisor = p.General(GeneralParam.DuelLeadershipDivisor);
        DuelRoll = p.General(GeneralParam.DuelRoll);
        DuelAttackerLoss = p.General(GeneralParam.DuelAttackerLoss);
        DuelDefenderLoss = p.General(GeneralParam.DuelDefenderLoss);
        CollateralDamageChance = p.General(GeneralParam.CollateralDamageChance);
        DefenseLeadershipDivisor = p.General(GeneralParam.DefenseLeadershipDivisor);
        BombardLeadershipDivisor = p.General(GeneralParam.BombardLeadershipDivisor);
        BombardHitMin = p.General(GeneralParam.BombardHitMin);
        BombardHitMax = p.General(GeneralParam.BombardHitMax);
        EnergyBombardDefense = p.General(GeneralParam.EnergyBombardDefense);
        RawMaterialBombardDefense = p.General(GeneralParam.RawMaterialBombardDefense);
        DestroyedPlanetWoundChance = p.General(GeneralParam.DestroyedPlanetWoundChance);
        CriticalDamageLimit = p.General(GeneralParam.CriticalDamageLimit);
        CriticalHitRoll = p.General(GeneralParam.CriticalHitRoll);
        CriticalHitThreshold = p.General(GeneralParam.CriticalHitThreshold);
        CriticalKindRoll = p.General(GeneralParam.CriticalKindRoll);
        CapitalShipFastRepairDays = p.General(GeneralParam.CapitalShipFastRepairDays);
        CapitalShipRepairDays = p.General(GeneralParam.CapitalShipRepairDays);
        FastHealForce = p.General(GeneralParam.FastHealForce);
        FastHealDays = p.General(GeneralParam.FastHealDays);
        HealDays = p.General(GeneralParam.HealDays);
        FastHealAmount = p.General(GeneralParam.FastHealAmount);
        HealAmount = p.General(GeneralParam.HealAmount);
        MissionInjuryMinChance = p.General(GeneralParam.MissionInjuryMinChance);
        MissionInjuryAdd = p.General(GeneralParam.MissionInjuryAdd);
        MissionInjuryRoll = p.General(GeneralParam.MissionInjuryRoll);
        ShipLossInjuryMinChance = p.General(GeneralParam.ShipLossInjuryMinChance);
        ShipLossInjuryAdd = p.General(GeneralParam.ShipLossInjuryAdd);
        ShipLossInjuryRoll = p.General(GeneralParam.ShipLossInjuryRoll);
        DuelWoundMinChance = p.General(GeneralParam.DuelWoundMinChance);
        DuelWoundAdd = p.General(GeneralParam.DuelWoundAdd);
        DuelWoundRoll = p.General(GeneralParam.DuelWoundRoll);
        AssassinationWoundChance = p.General(GeneralParam.AssassinationWoundChance);
        AssassinationWoundAdd = p.General(GeneralParam.AssassinationWoundAdd);
        AssassinationWoundRoll = p.General(GeneralParam.AssassinationWoundRoll);
        MinorDeathChance = p.General(GeneralParam.MinorDeathChance);
        DuelCombatGain = p.General(GeneralParam.DuelCombatGain);
        ForceDiscoveryMinForce = p.General(GeneralParam.ForceDiscoveryMinForce);
        JediTeacherMinForce = p.General(GeneralParam.JediTeacherMinForce);
        ForceDiscoveryDays = p.General(GeneralParam.ForceDiscoveryDays);
        ForceDiscoveryVariance = p.General(GeneralParam.ForceDiscoveryVariance);
        EscapeDays = p.General(GeneralParam.EscapeDays);
        EscapeVariance = p.General(GeneralParam.EscapeVariance);
        LukeVaderForcePercent = p.General(GeneralParam.LukeVaderForcePercent);
        LukePalpatineForcePercent = p.General(GeneralParam.LukePalpatineForcePercent);
        LeiaVaderForcePercent = p.General(GeneralParam.LeiaVaderForcePercent);
        LeiaPalpatineForcePercent = p.General(GeneralParam.LeiaPalpatineForcePercent);
        EmperorLeadershipBonus = p.General(GeneralParam.EmperorLeadershipBonus);
        HeritageWoundMaxForce = p.General(GeneralParam.HeritageWoundMaxForce);
        FinalBattleMinForce = p.General(GeneralParam.FinalBattleMinForce);
        HeritageWoundAdd = p.General(GeneralParam.HeritageWoundAdd);
        HeritageWoundRoll = p.General(GeneralParam.HeritageWoundRoll);
        LukeEvasionForceGain = p.General(GeneralParam.LukeEvasionForceGain);
        LeiaEvasionForceGain = p.General(GeneralParam.LeiaEvasionForceGain);
        HanHyperdriveModifier = p.General(GeneralParam.HanHyperdriveModifier);
        LukeVaderForceMinGain = p.General(GeneralParam.LukeVaderForceMinGain);
        LukePalpatineForceMinGain = p.General(GeneralParam.LukePalpatineForceMinGain);
        LeiaVaderForceMinGain = p.General(GeneralParam.LeiaVaderForceMinGain);
        LeiaPalpatineForceMinGain = p.General(GeneralParam.LeiaPalpatineForceMinGain);
        FoilKeyOffset = p.General(GeneralParam.FoilKeyOffset);
        ForceEncounterMinForce = p.General(GeneralParam.ForceEncounterMinForce);
        ForceDetectionOffset = p.General(GeneralParam.ForceDetectionOffset);
        DecoyKeyPercent = p.General(GeneralParam.DecoyKeyPercent);
        FoilKeyPercent = p.General(GeneralParam.FoilKeyPercent);
        CycleCapacityPercent = p.General(GeneralParam.CycleCapacityPercent);
        FighterFastRepairDays = p.General(GeneralParam.FighterFastRepairDays);
        FighterRepairDays = p.General(GeneralParam.FighterRepairDays);
        TravelDistanceDivisor = p.General(GeneralParam.TravelDistanceDivisor);
        DestroyedPlanetRimDelta = p.General(GeneralParam.DestroyedPlanetRimDelta);
        DestroyedPlanetRimLimit = p.General(GeneralParam.DestroyedPlanetRimLimit);
        DestroyedPlanetCoreDelta = p.General(GeneralParam.DestroyedPlanetCoreDelta);
        DestroyedPlanetCoreLimit = p.General(GeneralParam.DestroyedPlanetCoreLimit);
        HqLossDelta = p.General(GeneralParam.HqLossDelta);
        HqLossLimit = p.General(GeneralParam.HqLossLimit);
        AllianceFleetAtYavinChance = p.General(GeneralParam.AllianceFleetAtYavinChance);
        YavinUnitsFirstKey = p.General(GeneralParam.YavinUnitsFirstKey);
        YavinUnitsLastKey = p.General(GeneralParam.YavinUnitsLastKey);
        HqUnitsFirstKey = p.General(GeneralParam.HqUnitsFirstKey);
        HqUnitsLastKey = p.General(GeneralParam.HqUnitsLastKey);
        CoruscantUnitsFirstKey = p.General(GeneralParam.CoruscantUnitsFirstKey);
        CoruscantUnitsLastKey = p.General(GeneralParam.CoruscantUnitsLastKey);
        AllianceFleetUnitsFirstKey = p.General(GeneralParam.AllianceFleetUnitsFirstKey);
        AllianceFleetUnitsLastKey = p.General(GeneralParam.AllianceFleetUnitsLastKey);
        EmpireFleetUnitsFirstKey = p.General(GeneralParam.EmpireFleetUnitsFirstKey);
        EmpireFleetUnitsLastKey = p.General(GeneralParam.EmpireFleetUnitsLastKey);
        HqFacilitiesFirstKey = p.General(GeneralParam.HqFacilitiesFirstKey);
        HqFacilitiesLastKey = p.General(GeneralParam.HqFacilitiesLastKey);
        CoruscantFacilitiesFirstKey = p.General(GeneralParam.CoruscantFacilitiesFirstKey);
        CoruscantFacilitiesLastKey = p.General(GeneralParam.CoruscantFacilitiesLastKey);
        BattleSupportDivisor = p.General(GeneralParam.BattleSupportDivisor);
        UprisingLeadershipDivisor = p.General(GeneralParam.UprisingLeadershipDivisor);
        InciteSupportDelta = p.General(GeneralParam.InciteSupportDelta);
        DagobahDays = p.General(GeneralParam.DagobahDays);
        DagobahVariance = p.General(GeneralParam.DagobahVariance);
        BountyHuntersDays = p.General(GeneralParam.BountyHuntersDays);
        BountyHuntersVariance = p.General(GeneralParam.BountyHuntersVariance);
        BountyHuntersChance = p.General(GeneralParam.BountyHuntersChance);
        FinalBattleWinForce = p.General(GeneralParam.FinalBattleWinForce);
        FinalBattleWoundAdd = p.General(GeneralParam.FinalBattleWoundAdd);
        FinalBattleWoundRoll = p.General(GeneralParam.FinalBattleWoundRoll);
        PalaceEspionageDivisor = p.General(GeneralParam.PalaceEspionageDivisor);
        PalaceCombatDivisor = p.General(GeneralParam.PalaceCombatDivisor);
        DiplomacySkillGain = p.General(GeneralParam.DiplomacySkillGain);
        EspionageSkillGain = p.General(GeneralParam.EspionageSkillGain);
        ResearchSkillGain = p.General(GeneralParam.ResearchSkillGain);
        RecruitmentSkillGain = p.General(GeneralParam.RecruitmentSkillGain);
        InciteSkillGain = p.General(GeneralParam.InciteSkillGain);
        SubdueSkillGain = p.General(GeneralParam.SubdueSkillGain);
        RescueSkillGain = p.General(GeneralParam.RescueSkillGain);
        AbductionSkillGain = p.General(GeneralParam.AbductionSkillGain);
        AssassinationSkillGain = p.General(GeneralParam.AssassinationSkillGain);
        SabotageEspionageGain = p.General(GeneralParam.SabotageEspionageGain);
        SabotageCombatGain = p.General(GeneralParam.SabotageCombatGain);
        DeathStarSabotageEspionageGain = p.General(GeneralParam.DeathStarSabotageEspionageGain);
        DeathStarSabotageCombatGain = p.General(GeneralParam.DeathStarSabotageCombatGain);
        HanRescueEspionageGain = p.General(GeneralParam.HanRescueEspionageGain);
        HanRescueCombatGain = p.General(GeneralParam.HanRescueCombatGain);
        ResearchPointsAdd = p.General(GeneralParam.ResearchPointsAdd);
        ResearchPointsRoll = p.General(GeneralParam.ResearchPointsRoll);
        JediTrainingForcePercent = p.General(GeneralParam.JediTrainingForcePercent);
        DagobahForcePercent = p.General(GeneralParam.DagobahForcePercent);
        DagobahDaysPerPercent = p.General(GeneralParam.DagobahDaysPerPercent);
        EspionageRevealAdd = p.General(GeneralParam.EspionageRevealAdd);
        EspionageRevealRoll = p.General(GeneralParam.EspionageRevealRoll);
        CoruscantRevealAdd = p.General(GeneralParam.CoruscantRevealAdd);
        CoruscantRevealRoll = p.General(GeneralParam.CoruscantRevealRoll);
        HqRevealAdd = p.General(GeneralParam.HqRevealAdd);
        HqRevealRoll = p.General(GeneralParam.HqRevealRoll);
        DiplomacyOwnSupportAdd = p.General(GeneralParam.DiplomacyOwnSupportAdd);
        DiplomacyOwnSupportRoll = p.General(GeneralParam.DiplomacyOwnSupportRoll);
        DiplomacyNeutralSupportAdd = p.General(GeneralParam.DiplomacyNeutralSupportAdd);
        DiplomacyNeutralSupportRoll = p.General(GeneralParam.DiplomacyNeutralSupportRoll);
        SubdueOwnSupportAdd = p.General(GeneralParam.SubdueOwnSupportAdd);
        SubdueOwnSupportRoll = p.General(GeneralParam.SubdueOwnSupportRoll);
        SubdueNeutralSupportAdd = p.General(GeneralParam.SubdueNeutralSupportAdd);
        SubdueNeutralSupportRoll = p.General(GeneralParam.SubdueNeutralSupportRoll);
        MaintenanceEnforcementDays = p.General(GeneralParam.MaintenanceEnforcementDays);
        ResearchDays = p.General(GeneralParam.ResearchDays);
        ResearchVariance = p.General(GeneralParam.ResearchVariance);
        DeathStarSupportDivisor = p.General(GeneralParam.DeathStarSupportDivisor);
        DeathStarSupportChangeDivisor = p.General(GeneralParam.DeathStarSupportChangeDivisor);
        UprisingGarrisonFactor = p.General(GeneralParam.UprisingGarrisonFactor);
        BombardShieldCount = p.General(GeneralParam.BombardShieldCount);
        BlockadeShipPenalty = p.General(GeneralParam.BlockadeShipPenalty);
        BlockadeFighterPenalty = p.General(GeneralParam.BlockadeFighterPenalty);
        SmugglingShipPenalty = p.General(GeneralParam.SmugglingShipPenalty);
        SmugglingFighterPenalty = p.General(GeneralParam.SmugglingFighterPenalty);
        SmugglingTroopPenalty = p.General(GeneralParam.SmugglingTroopPenalty);
        LowSupportDelta = p.General(GeneralParam.LowSupportDelta);
        LowSupportThreshold = p.General(GeneralParam.LowSupportThreshold);
        GarrisonSupportDelta = p.General(GeneralParam.GarrisonSupportDelta);
        GarrisonSupportDays = p.General(GeneralParam.GarrisonSupportDays);
        BlockadePopularDelta = p.General(GeneralParam.BlockadePopularDelta);
        BlockadePopularDays = p.General(GeneralParam.BlockadePopularDays);
        BlockadeUnpopularDelta = p.General(GeneralParam.BlockadeUnpopularDelta);
        BlockadeUnpopularDays = p.General(GeneralParam.BlockadeUnpopularDays);
        UprisingSupportDelta = p.General(GeneralParam.UprisingSupportDelta);
        UprisingSupportDays = p.General(GeneralParam.UprisingSupportDays);
        SuppressDays = p.General(GeneralParam.SuppressDays);
        SuppressVariance = p.General(GeneralParam.SuppressVariance);
        UprisingIncidentDays = p.General(GeneralParam.UprisingIncidentDays);
        UprisingIncidentVariance = p.General(GeneralParam.UprisingIncidentVariance);
        InformantDays = p.General(GeneralParam.InformantDays);
        InformantVariance = p.General(GeneralParam.InformantVariance);
        CivilianBombardSupportDelta = p.General(GeneralParam.CivilianBombardSupportDelta);
        UprisingRollAdd = p.General(GeneralParam.UprisingRollAdd);
        UprisingRollRoll = p.General(GeneralParam.UprisingRollRoll);
        InformantAdd = p.General(GeneralParam.InformantAdd);
        InformantRoll = p.General(GeneralParam.InformantRoll);
        RawMaterialsMin = p.General(GeneralParam.RawMaterialsMin);
        RawMaterialsMax = p.General(GeneralParam.RawMaterialsMax);
        EnergyMin = p.General(GeneralParam.EnergyMin);
        EnergyMax = p.General(GeneralParam.EnergyMax);
        ResourceLossFactor = p.General(GeneralParam.ResourceLossFactor);
        DisasterChance = p.General(GeneralParam.DisasterChance);
        DisasterDays = p.General(GeneralParam.DisasterDays);
        DisasterVariance = p.General(GeneralParam.DisasterVariance);
        ResourceEventDays = p.General(GeneralParam.ResourceEventDays);
        ResourceEventVariance = p.General(GeneralParam.ResourceEventVariance);
        CoreEnergyAdd = p.General(GeneralParam.CoreEnergyAdd);
        CoreEnergyRoll = p.General(GeneralParam.CoreEnergyRoll);
        CoreRawMaterialsAdd = p.General(GeneralParam.CoreRawMaterialsAdd);
        CoreRawMaterialsRoll = p.General(GeneralParam.CoreRawMaterialsRoll);
        RimEnergyAdd = p.General(GeneralParam.RimEnergyAdd);
        RimEnergyFirstRoll = p.General(GeneralParam.RimEnergyFirstRoll);
        RimEnergySecondRoll = p.General(GeneralParam.RimEnergySecondRoll);
        RimRawMaterialsAdd = p.General(GeneralParam.RimRawMaterialsAdd);
        RimRawMaterialsRoll = p.General(GeneralParam.RimRawMaterialsRoll);
        CorePopulatedChance = p.General(GeneralParam.CorePopulatedChance);
        RimPopulatedChance = p.General(GeneralParam.RimPopulatedChance);
        SmugglingMaxSupport = p.General(GeneralParam.SmugglingMaxSupport);
        SmugglingLowRate = p.General(GeneralParam.SmugglingLowRate);
        SmugglingMidSupport = p.General(GeneralParam.SmugglingMidSupport);
        SmugglingMidRate = p.General(GeneralParam.SmugglingMidRate);
        SmugglingHighSupport = p.General(GeneralParam.SmugglingHighSupport);
        SmugglingHighRate = p.General(GeneralParam.SmugglingHighRate);
        SupportControlShare = p.General(GeneralParam.SupportControlShare);
        GarrisonSupportThreshold = p.General(GeneralParam.GarrisonSupportThreshold);
        GarrisonSupportPerTroop = p.General(GeneralParam.GarrisonSupportPerTroop);
        ProductionModifierBase = p.General(GeneralParam.ProductionModifierBase);
        NeutralCoreSupportRoll = p.General(GeneralParam.NeutralCoreSupportRoll);
        RimSupportRoll = p.General(GeneralParam.RimSupportRoll);
        CoreMineFactor = p.General(GeneralParam.CoreMineFactor);
        RimMineFactor = p.General(GeneralParam.RimMineFactor);
    }

    /// <summary>Hyperdrive modifier (percent) of a travelling object without its own.</summary>
    public int HyperdriveModifier { get; }
    /// <summary>Divisor of a defence's attack strength in the assault fire chance.</summary>
    public int AssaultFireDivisor { get; }
    public int DuelLeadershipDivisor { get; }
    public int DuelRoll { get; }
    /// <summary>Duel values at or below this kill the attacking troop.</summary>
    public int DuelAttackerLoss { get; }
    /// <summary>Duel values at or above this kill the defending troop.</summary>
    public int DuelDefenderLoss { get; }
    public int CollateralDamageChance { get; }
    /// <summary>Divisor of the system general's leadership in the defending ships' fire.</summary>
    public int DefenseLeadershipDivisor { get; }
    public int BombardLeadershipDivisor { get; }
    public int BombardHitMin { get; }
    public int BombardHitMax { get; }
    public int EnergyBombardDefense { get; }
    public int RawMaterialBombardDefense { get; }
    /// <summary>Chance per minor character on a destroyed planet to be wounded.</summary>
    public int DestroyedPlanetWoundChance { get; }
    /// <summary>Highest level of one critical damage kind.</summary>
    public int CriticalDamageLimit { get; }
    public int CriticalHitRoll { get; }
    public int CriticalHitThreshold { get; }
    public int CriticalKindRoll { get; }
    public int CapitalShipFastRepairDays { get; }
    public int CapitalShipRepairDays { get; }
    /// <summary>Force at or above which a character heals fast.</summary>
    public int FastHealForce { get; }
    public int FastHealDays { get; }
    public int HealDays { get; }
    public int FastHealAmount { get; }
    public int HealAmount { get; }
    /// <summary>Lowest injury chance of a foiled mission member.</summary>
    public int MissionInjuryMinChance { get; }
    public int MissionInjuryAdd { get; }
    public int MissionInjuryRoll { get; }
    /// <summary>Lowest injury chance of a character on a lost ship.</summary>
    public int ShipLossInjuryMinChance { get; }
    public int ShipLossInjuryAdd { get; }
    public int ShipLossInjuryRoll { get; }
    public int DuelWoundMinChance { get; }
    public int DuelWoundAdd { get; }
    public int DuelWoundRoll { get; }
    /// <summary>Chance (and first roll) of the wound an assassination deals.</summary>
    public int AssassinationWoundChance { get; }
    public int AssassinationWoundAdd { get; }
    public int AssassinationWoundRoll { get; }
    /// <summary>Chance that a wounded minor character dies.</summary>
    public int MinorDeathChance { get; }
    public int DuelCombatGain { get; }
    /// <summary>Force at or above which a Jedi trainer discovers Force users.</summary>
    public int ForceDiscoveryMinForce { get; }
    public int JediTeacherMinForce { get; }
    public int ForceDiscoveryDays { get; }
    public int ForceDiscoveryVariance { get; }
    public int EscapeDays { get; }
    public int EscapeVariance { get; }
    /// <summary>Percent of the Force gap Luke gains when meeting Vader.</summary>
    public int LukeVaderForcePercent { get; }
    public int LukePalpatineForcePercent { get; }
    public int LeiaVaderForcePercent { get; }
    public int LeiaPalpatineForcePercent { get; }
    /// <summary>Leadership bonus (percent) of Empire characters while the Emperor is at Coruscant.</summary>
    public int EmperorLeadershipBonus { get; }
    /// <summary>Luke is wounded by learning his heritage while his Force is below this.</summary>
    public int HeritageWoundMaxForce { get; }
    public int FinalBattleMinForce { get; }
    public int HeritageWoundAdd { get; }
    public int HeritageWoundRoll { get; }
    public int LukeEvasionForceGain { get; }
    public int LeiaEvasionForceGain { get; }
    /// <summary>Hyperdrive modifier (percent) of a mission while Han Solo resolves.</summary>
    public int HanHyperdriveModifier { get; }
    public int LukeVaderForceMinGain { get; }
    public int LukePalpatineForceMinGain { get; }
    public int LeiaVaderForceMinGain { get; }
    public int LeiaPalpatineForceMinGain { get; }
    public int FoilKeyOffset { get; }
    /// <summary>Lowest Force of a character a mission team can meet.</summary>
    public int ForceEncounterMinForce { get; }
    public int ForceDetectionOffset { get; }
    public int DecoyKeyPercent { get; }
    public int FoilKeyPercent { get; }
    /// <summary>Percent of a mine or refinery's capacity per cycle step.</summary>
    public int CycleCapacityPercent { get; }
    public int FighterFastRepairDays { get; }
    public int FighterRepairDays { get; }
    /// <summary>Distance unit of travel time.</summary>
    public int TravelDistanceDivisor { get; }
    public int DestroyedPlanetRimDelta { get; }
    public int DestroyedPlanetRimLimit { get; }
    public int DestroyedPlanetCoreDelta { get; }
    public int DestroyedPlanetCoreLimit { get; }
    public int HqLossDelta { get; }
    public int HqLossLimit { get; }
    public int AllianceFleetAtYavinChance { get; }
    public int YavinUnitsFirstKey { get; }
    public int YavinUnitsLastKey { get; }
    public int HqUnitsFirstKey { get; }
    public int HqUnitsLastKey { get; }
    public int CoruscantUnitsFirstKey { get; }
    public int CoruscantUnitsLastKey { get; }
    public int AllianceFleetUnitsFirstKey { get; }
    public int AllianceFleetUnitsLastKey { get; }
    public int EmpireFleetUnitsFirstKey { get; }
    public int EmpireFleetUnitsLastKey { get; }
    public int HqFacilitiesFirstKey { get; }
    public int HqFacilitiesLastKey { get; }
    public int CoruscantFacilitiesFirstKey { get; }
    public int CoruscantFacilitiesLastKey { get; }
    public int BattleSupportDivisor { get; }
    public int UprisingLeadershipDivisor { get; }
    public int InciteSupportDelta { get; }
    public int DagobahDays { get; }
    public int DagobahVariance { get; }
    public int BountyHuntersDays { get; }
    public int BountyHuntersVariance { get; }
    public int BountyHuntersChance { get; }
    /// <summary>Luke wins the final battle at or above this Force.</summary>
    public int FinalBattleWinForce { get; }
    public int FinalBattleWoundAdd { get; }
    public int FinalBattleWoundRoll { get; }
    public int PalaceEspionageDivisor { get; }
    public int PalaceCombatDivisor { get; }
    public int DiplomacySkillGain { get; }
    public int EspionageSkillGain { get; }
    public int ResearchSkillGain { get; }
    public int RecruitmentSkillGain { get; }
    public int InciteSkillGain { get; }
    public int SubdueSkillGain { get; }
    public int RescueSkillGain { get; }
    public int AbductionSkillGain { get; }
    public int AssassinationSkillGain { get; }
    public int SabotageEspionageGain { get; }
    public int SabotageCombatGain { get; }
    public int DeathStarSabotageEspionageGain { get; }
    public int DeathStarSabotageCombatGain { get; }
    public int HanRescueEspionageGain { get; }
    public int HanRescueCombatGain { get; }
    public int ResearchPointsAdd { get; }
    public int ResearchPointsRoll { get; }
    /// <summary>Highest Force gain in Jedi training, percent of the gap to the teacher.</summary>
    public int JediTrainingForcePercent { get; }
    /// <summary>Force gain (percent) of a completed Dagobah training.</summary>
    public int DagobahForcePercent { get; }
    /// <summary>Days per percent of Force gain of an interrupted Dagobah training.</summary>
    public int DagobahDaysPerPercent { get; }
    public int EspionageRevealAdd { get; }
    public int EspionageRevealRoll { get; }
    public int CoruscantRevealAdd { get; }
    public int CoruscantRevealRoll { get; }
    public int HqRevealAdd { get; }
    public int HqRevealRoll { get; }
    public int DiplomacyOwnSupportAdd { get; }
    public int DiplomacyOwnSupportRoll { get; }
    public int DiplomacyNeutralSupportAdd { get; }
    public int DiplomacyNeutralSupportRoll { get; }
    public int SubdueOwnSupportAdd { get; }
    public int SubdueOwnSupportRoll { get; }
    public int SubdueNeutralSupportAdd { get; }
    public int SubdueNeutralSupportRoll { get; }
    public int MaintenanceEnforcementDays { get; }
    public int ResearchDays { get; }
    public int ResearchVariance { get; }
    /// <summary>Divisor (and troop factor) of support effects near the Death Star.</summary>
    public int DeathStarSupportDivisor { get; }
    public int DeathStarSupportChangeDivisor { get; }
    public int UprisingGarrisonFactor { get; }
    /// <summary>Planetary shields that block a bombardment.</summary>
    public int BombardShieldCount { get; }
    public int BlockadeShipPenalty { get; }
    public int BlockadeFighterPenalty { get; }
    public int SmugglingShipPenalty { get; }
    public int SmugglingFighterPenalty { get; }
    public int SmugglingTroopPenalty { get; }
    public int LowSupportDelta { get; }
    public int LowSupportThreshold { get; }
    public int GarrisonSupportDelta { get; }
    public int GarrisonSupportDays { get; }
    public int BlockadePopularDelta { get; }
    public int BlockadePopularDays { get; }
    public int BlockadeUnpopularDelta { get; }
    public int BlockadeUnpopularDays { get; }
    public int UprisingSupportDelta { get; }
    public int UprisingSupportDays { get; }
    public int SuppressDays { get; }
    public int SuppressVariance { get; }
    public int UprisingIncidentDays { get; }
    public int UprisingIncidentVariance { get; }
    public int InformantDays { get; }
    public int InformantVariance { get; }
    public int CivilianBombardSupportDelta { get; }
    public int UprisingRollAdd { get; }
    public int UprisingRollRoll { get; }
    public int InformantAdd { get; }
    public int InformantRoll { get; }
    public int RawMaterialsMin { get; }
    public int RawMaterialsMax { get; }
    public int EnergyMin { get; }
    public int EnergyMax { get; }
    public int ResourceLossFactor { get; }
    public int DisasterChance { get; }
    public int DisasterDays { get; }
    public int DisasterVariance { get; }
    public int ResourceEventDays { get; }
    public int ResourceEventVariance { get; }
    public int CoreEnergyAdd { get; }
    public int CoreEnergyRoll { get; }
    public int CoreRawMaterialsAdd { get; }
    public int CoreRawMaterialsRoll { get; }
    public int RimEnergyAdd { get; }
    public int RimEnergyFirstRoll { get; }
    public int RimEnergySecondRoll { get; }
    public int RimRawMaterialsAdd { get; }
    public int RimRawMaterialsRoll { get; }
    public int CorePopulatedChance { get; }
    public int RimPopulatedChance { get; }
    /// <summary>Highest owner support at which smuggling happens.</summary>
    public int SmugglingMaxSupport { get; }
    public int SmugglingLowRate { get; }
    public int SmugglingMidSupport { get; }
    public int SmugglingMidRate { get; }
    public int SmugglingHighSupport { get; }
    public int SmugglingHighRate { get; }
    /// <summary>Support share that gives a side support control.</summary>
    public int SupportControlShare { get; }
    /// <summary>Support share below which a system needs a garrison.</summary>
    public int GarrisonSupportThreshold { get; }
    /// <summary>Support per garrison troop (negative).</summary>
    public int GarrisonSupportPerTroop { get; }
    public int ProductionModifierBase { get; }
    public int NeutralCoreSupportRoll { get; }
    public int RimSupportRoll { get; }
    public int CoreMineFactor { get; }
    public int RimMineFactor { get; }

    /// <summary>SDPRTB values of one side. Alliance and Empire have their own; other owners give zeros.</summary>
    public SideSettings For(Owner side) => side switch { Owner.Alliance => _alliance, Owner.Empire => _empire, _ => _none };
}

/// <summary>SDPRTB values of one side in the settings column, read once from a Params.</summary>
public sealed class SideSettings
{
    internal SideSettings(Params p, Owner side)
    {
        MinorCharactersSmall = p.Side(SideParam.MinorCharactersSmall, side);
        MinorCharactersMedium = p.Side(SideParam.MinorCharactersMedium, side);
        MinorCharactersLarge = p.Side(SideParam.MinorCharactersLarge, side);
        RimBombardDelta = p.Side(SideParam.RimBombardDelta, side);
        RimBombardLimit = p.Side(SideParam.RimBombardLimit, side);
        CoreBombardDelta = p.Side(SideParam.CoreBombardDelta, side);
        CoreBombardLimit = p.Side(SideParam.CoreBombardLimit, side);
        BattleWaveLimit = p.Side(SideParam.BattleWaveLimit, side);
        BattleWaveMaxDelta = p.Side(SideParam.BattleWaveMaxDelta, side);
        BattleWinnerBase = p.Side(SideParam.BattleWinnerBase, side);
        BattleWinnerScale = p.Side(SideParam.BattleWinnerScale, side);
        BattleLoserBase = p.Side(SideParam.BattleLoserBase, side);
        BattleLoserScale = p.Side(SideParam.BattleLoserScale, side);
        OwnerChangeDelta = p.Side(SideParam.OwnerChangeDelta, side);
        OwnerChangeLimit = p.Side(SideParam.OwnerChangeLimit, side);
        CoruscantOwnerChangeDelta = p.Side(SideParam.CoruscantOwnerChangeDelta, side);
        CoruscantOwnerChangeLimit = p.Side(SideParam.CoruscantOwnerChangeLimit, side);
        StartingUnitBudgetSmall = p.Side(SideParam.StartingUnitBudgetSmall, side);
        StartingUnitBudgetMedium = p.Side(SideParam.StartingUnitBudgetMedium, side);
        StartingUnitBudgetLarge = p.Side(SideParam.StartingUnitBudgetLarge, side);
        MilitaryGainedDelta = p.Side(SideParam.MilitaryGainedDelta, side);
        MilitaryGainedLimit = p.Side(SideParam.MilitaryGainedLimit, side);
        MilitaryLostDelta = p.Side(SideParam.MilitaryLostDelta, side);
        MilitaryLostLimit = p.Side(SideParam.MilitaryLostLimit, side);
        MilitaryKeptDelta = p.Side(SideParam.MilitaryKeptDelta, side);
        MilitaryKeptLimit = p.Side(SideParam.MilitaryKeptLimit, side);
        SetupLoyalShare = p.Side(SideParam.SetupLoyalShare, side);
        SetupDisloyalShare = p.Side(SideParam.SetupDisloyalShare, side);
        LoyalSupportAdd = p.Side(SideParam.LoyalSupportAdd, side);
        LoyalSupportRoll = p.Side(SideParam.LoyalSupportRoll, side);
        DisloyalSupportAdd = p.Side(SideParam.DisloyalSupportAdd, side);
        DisloyalSupportRoll = p.Side(SideParam.DisloyalSupportRoll, side);
    }

    /// <summary>Minor characters placed in a small galaxy.</summary>
    public int MinorCharactersSmall { get; }
    public int MinorCharactersMedium { get; }
    public int MinorCharactersLarge { get; }
    /// <summary>Minor characters placed for galaxy size 1 (small), 2 (medium) or 3 (large).</summary>
    public int MinorCharacters(int galaxySize) => galaxySize switch { 1 => MinorCharactersSmall, 2 => MinorCharactersMedium, 3 => MinorCharactersLarge, _ => 0 };
    public int RimBombardDelta { get; }
    public int RimBombardLimit { get; }
    public int CoreBombardDelta { get; }
    public int CoreBombardLimit { get; }
    public int BattleWaveLimit { get; }
    public int BattleWaveMaxDelta { get; }
    public int BattleWinnerBase { get; }
    public int BattleWinnerScale { get; }
    public int BattleLoserBase { get; }
    public int BattleLoserScale { get; }
    public int OwnerChangeDelta { get; }
    public int OwnerChangeLimit { get; }
    public int CoruscantOwnerChangeDelta { get; }
    public int CoruscantOwnerChangeLimit { get; }
    /// <summary>Starting unit budget (percent of free maintenance) in a small galaxy.</summary>
    public int StartingUnitBudgetSmall { get; }
    public int StartingUnitBudgetMedium { get; }
    public int StartingUnitBudgetLarge { get; }
    /// <summary>Starting unit budget for galaxy size 1 (small), 2 (medium) or 3 (large).</summary>
    public int StartingUnitBudget(int galaxySize) => galaxySize switch { 1 => StartingUnitBudgetSmall, 2 => StartingUnitBudgetMedium, 3 => StartingUnitBudgetLarge, _ => 0 };
    public int MilitaryGainedDelta { get; }
    public int MilitaryGainedLimit { get; }
    public int MilitaryLostDelta { get; }
    public int MilitaryLostLimit { get; }
    public int MilitaryKeptDelta { get; }
    public int MilitaryKeptLimit { get; }
    /// <summary>Share (percent) of core systems a side controls loyally at setup. Meaning in the original is unknown.</summary>
    public int SetupLoyalShare { get; }
    /// <summary>Setup share (percent) paired with SetupLoyalShare. Meaning in the original is unknown.</summary>
    public int SetupDisloyalShare { get; }
    public int LoyalSupportAdd { get; }
    public int LoyalSupportRoll { get; }
    public int DisloyalSupportAdd { get; }
    public int DisloyalSupportRoll { get; }
}

/// <summary>TB threshold lookup.</summary>
public static class TableLookup
{
    /// <summary>The entry before the first one with Key &gt; x (clamped to the first / last entry); null for an empty table.</summary>
    public static T? Threshold<T>(IReadOnlyList<T> entries, Func<T, int> key, int x) where T : class
    {
        if (entries.Count == 0) return null;
        for (int i = 0; i < entries.Count; i++)
            if (key(entries[i]) > x) return entries[Math.Max(0, i - 1)];
        return entries[^1];
    }

    public static TableEntry? Threshold(IReadOnlyList<TableEntry> entries, int x)
    {
        if (entries.Count == 0) return null;
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Key > x) return entries[Math.Max(0, i - 1)];
        return entries[^1];
    }
}
