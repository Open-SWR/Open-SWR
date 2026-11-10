namespace OpenSWR.Sim.Ai;

/// <summary>System record: the side's view of one system and its counters.</summary>
public sealed class AiSystem(StarSystem system, WorldModel model)
{
    public StarSystem System { get; } = system;
    /// <summary>Its sector record.</summary>
    public AiSector? Sector { get; set; }
    public bool Dirty { get; set; } = true;

    /// <summary>A planner's role: a change dirties the record, its sector and the model's aggregates flag.</summary>
    public void SetRole(SystemRole role, bool on)
    {
        if (HasRoleOf(role) == on) return;
        switch (role)
        {
            case SystemRole.Shipyard: ShipyardRole = on; break;
            case SystemRole.Training: TrainingRole = on; break;
            case SystemRole.Construction: ConstructionRole = on; break;
            case SystemRole.Mine: MineRole = on; break;
            default: DefenceRole = on; break;
        }
        model.DirtySystem(this);
    }

    public bool HasRoleOf(SystemRole role) => role switch
    {
        SystemRole.Shipyard => ShipyardRole, SystemRole.Training => TrainingRole, SystemRole.Construction => ConstructionRole,
        SystemRole.Mine => MineRole, _ => DefenceRole,
    };

    /// <summary>Clears the assault and defense base marks; a change dirties the record.</summary>
    public void ClearBaseMarks()
    {
        if (!AssaultBase && !DefenseBase) return;
        (AssaultBase, DefenseBase) = (false, false);
        model.DirtySystem(this);
    }

    /// <summary>Sets or clears the fleet-op base mark; a change dirties the record.</summary>
    public void MarkBase(bool on)
    {
        if (Base == on) return;
        Base = on;
        model.DirtySystem(this);
    }

    /// <summary>Clears the base and both base marks; a change dirties the record.</summary>
    public void ClearBases()
    {
        if (!AssaultBase && !DefenseBase && !Base) return;
        (AssaultBase, DefenseBase, Base) = (false, false, false);
        model.DirtySystem(this);
    }

    /// <summary>Planner 1 takes the system: the other construction roles are dropped; always dirties the record.</summary>
    public void TakeConstructionRole()
    {
        (ConstructionRole, MineRole, DefenceRole) = (true, false, false);
        model.DirtySystem(this);
    }

    /// <summary>Clears the four claim marks (a plain write, no dirty).</summary>
    public void ClearClaimMarks() => (AttackMark, AssaultMark, BombardMark, MoveMark) = (false, false, false, false);

    public int Key { get; set; }
    public int Side { get; set; }
    /// <summary>The view owner.</summary>
    public int Owner { get; set; }
    // unrest, producer state, shortfalls and the planners' roles
    /// <summary>The view's Uprising (populated systems).</summary>
    public bool Uprising { get; set; }
    /// <summary>The view's Blockade.</summary>
    public bool Blockade { get; set; }
    /// <summary>Own with free energy.</summary>
    public bool HasFreeEnergy { get; set; }
    /// <summary>Own with free energy and free raw material.</summary>
    public bool HasFreeRawMaterial { get; set; }
    /// <summary>A free facility slot with free raw material (Inferred: a Mine site).</summary>
    public bool MineSite { get; set; }
    /// <summary>A free facility slot (Inferred: a Refinery site).</summary>
    public bool RefinerySite { get; set; }
    /// <summary>Shipyards with a Usable one and an empty shipyard manager queue, no Uprising or Blockade.</summary>
    public bool IdleShipyard { get; set; }
    /// <summary>As <see cref="IdleShipyard"/> for training facilities.</summary>
    public bool IdleTraining { get; set; }
    /// <summary>As <see cref="IdleShipyard"/> for construction yards.</summary>
    public bool IdleConstruction { get; set; }
    /// <summary>A Usable production facility of some kind.</summary>
    public bool UsableProducer { get; set; }
    /// <summary>Usable troops below the garrison target.</summary>
    public bool GarrisonShort { get; set; }
    /// <summary>Both defence balances <= 0 with a negative sum.</summary>
    public bool DefenceShort { get; set; }
    /// <summary>GenCores below their target.</summary>
    public bool GenCoreShort { get; set; }
    /// <summary>Usable troops above the garrison target.</summary>
    public bool GarrisonSurplus { get; set; }
    /// <summary>The garrison troops below the garrison target.</summary>
    public bool GarrisonTroopsShort { get; set; }
    /// <summary>Fighter balance &lt; 0.</summary>
    public bool FighterShort { get; set; }
    /// <summary>A Usable own fighter.</summary>
    public bool OwnFighters { get; set; }
    /// <summary>Usable-fighter balance &lt; 0.</summary>
    public bool UsableFighterShort { get; set; }
    /// <summary>Role: shipyard (planners 7, 10, 11; kept).</summary>
    public bool ShipyardRole { get; set; }
    /// <summary>Role: training (planners 5, 6; kept).</summary>
    public bool TrainingRole { get; set; }
    /// <summary>Role: construction (planner 1; kept).</summary>
    public bool ConstructionRole { get; set; }
    /// <summary>Role of the mine planner, planner 2 (kept). Meaning in the original is unknown.</summary>
    public bool MineRole { get; set; }
    /// <summary>Role of the defence planner, planner 3 (kept). Meaning in the original is unknown.</summary>
    public bool DefenceRole { get; set; }
    /// <summary>Own, no Uprising or Blockade, no role.</summary>
    public bool CalmWithoutRole { get; set; }
    /// <summary>A garrison troop that can attack (with a role: at least as strong as the best buildable one). Meaning in the original is unknown.</summary>
    public bool AttackGarrison { get; set; }
    /// <summary>A garrison troop that can defend (without a role: any defense-only troop). Meaning in the original is unknown.</summary>
    public bool DefenseGarrison { get; set; }
    /// <summary>Inferred: a Death Star shield wanted at Coruscant.</summary>
    public bool DeathStarShieldWanted { get; set; }
    /// <summary>Coruscant or an HQ of the owner. Meaning in the original is unknown.</summary>
    public bool KeySystem { get; set; }
    // knowledge, presence and the targets the planners look for
    /// <summary>Own and explored. Meaning in the original is unknown.</summary>
    public bool OwnExplored { get; set; }
    /// <summary>Own minor characters still to come and not Yavin (calm own systems).</summary>
    public bool CharacterSite { get; set; }
    /// <summary>Shipyard role, ship line not exhausted, a Usable shipyard and usable troops (calm own systems).</summary>
    public bool ShipProduction { get; set; }
    /// <summary>As <see cref="ShipProduction"/> for the training role and line.</summary>
    public bool TrainingProduction { get; set; }
    /// <summary>As <see cref="ShipProduction"/> for the construction roles and line.</summary>
    public bool ConstructionProduction { get; set; }
    /// <summary>Own with Uprising and no enemy fleet.</summary>
    public bool UprisingNoFleet { get; set; }
    /// <summary>A diplomacy target: neutral without an enemy fleet, or own with low support, no enemy fleet and no Uprising. Meaning in the original is unknown.</summary>
    public bool DiplomacyTarget { get; set; }
    /// <summary>Not own (also unexplored).</summary>
    public bool NotOwn { get; set; }
    /// <summary>Enemy with support &gt; 50.</summary>
    public bool EnemySupported { get; set; }
    /// <summary>Owned by a side.</summary>
    public bool Claimed { get; set; }
    /// <summary>An enemy-allegiance character here, not Enroute.</summary>
    public bool EnemyCharacter { get; set; }
    /// <summary>An enemy character aboard a fleet here.</summary>
    public bool EnemyCharacterAboard { get; set; }
    /// <summary>An enemy major character here.</summary>
    public bool EnemyMajor { get; set; }
    /// <summary>An enemy major character aboard a fleet here.</summary>
    public bool EnemyMajorAboard { get; set; }
    /// <summary>An own character here, Captured.</summary>
    public bool OwnCaptured { get; set; }
    /// <summary>An own character aboard a fleet here, Captured.</summary>
    public bool OwnCapturedAboard { get; set; }
    /// <summary>Enemy with Mines or Refineries.</summary>
    public bool EnemyMines { get; set; }
    /// <summary>Enemy with production facilities.</summary>
    public bool EnemyProducers { get; set; }
    /// <summary>Enemy with Usable GenCore strength.</summary>
    public bool EnemyGenCores { get; set; }
    /// <summary>Enemy with a Usable Death Star shield.</summary>
    public bool EnemyDeathStarShield { get; set; }
    /// <summary>Enemy with Usable battery strength.</summary>
    public bool EnemyBatteries { get; set; }
    /// <summary>Enemy with usable troops.</summary>
    public bool EnemyTroops { get; set; }
    /// <summary>Enemy with usable fighters.</summary>
    public bool EnemyFighters { get; set; }
    /// <summary>A ready enemy fleet here (set with <see cref="EnemyFleet"/>). Meaning in the original is unknown.</summary>
    public bool EnemyFleetSeen { get; set; }
    /// <summary>A ready enemy fleet with a Death Star here (Alliance side only). Meaning in the original is unknown.</summary>
    public bool EnemyDeathStarSeen { get; set; }
    /// <summary>An HQ of the owner (set with <see cref="Headquarters"/>). Meaning in the original is unknown.</summary>
    public bool HeadquartersSeen { get; set; }
    /// <summary>An enemy-allegiance character here, Captured.</summary>
    public bool EnemyCaptured { get; set; }
    public bool Explored { get; set; }
    /// <summary>The AI side's support &lt; 70.</summary>
    public bool LowSupport { get; set; }
    /// <summary>Kept across updates; cleared when a diplomacy op on the system is retired (writer elsewhere; meaning unknown).</summary>
    public bool DiplomacyMark { get; set; }
    // class, defences, fleet-op base and claim marks
    public bool Own { get; set; }
    public bool Neutral { get; set; }
    public bool Enemy { get; set; }
    public bool Populated { get; set; }
    public bool Destroyed { get; set; }
    /// <summary>Owned by a side or Populated.</summary>
    public bool OwnedOrPopulated { get; set; }
    /// <summary>A rim system.</summary>
    public bool Rim { get; set; }
    public bool UsableDeathStarShield { get; set; }
    /// <summary>More than one GenCore.</summary>
    public bool ManyGenCores { get; set; }
    /// <summary>A ready enemy fleet.</summary>
    public bool EnemyFleet { get; set; }
    /// <summary>A ready enemy fleet with a Death Star.</summary>
    public bool EnemyDeathStar { get; set; }
    /// <summary>Enemy with facilities but no batteries.</summary>
    public bool NoBatteries { get; set; }
    /// <summary>An enemy fleet with a commander aboard, or an enemy character with a command kind.</summary>
    public bool EnemyCommander { get; set; }
    /// <summary>An enemy character with command kind 3 (set with <see cref="EnemyCommander"/>).</summary>
    public bool CommandKind3 { get; set; }
    /// <summary>Own troops that can attack.</summary>
    public bool AttackTroops { get; set; }
    /// <summary>Own troops that can defend (with attack troops when no role).</summary>
    public bool DefenseTroops { get; set; }
    /// <summary>A fleet-op base of task types 4 and 6, where the assault troops gather (kept). Meaning in the original is unknown.</summary>
    public bool AssaultBase { get; set; }
    /// <summary>A fleet-op base of task type 5, where the defense troops gather (kept). Meaning in the original is unknown.</summary>
    public bool DefenseBase { get; set; }
    /// <summary>A fleet-op base (kept).</summary>
    public bool Base { get; set; }
    /// <summary>The target of a garrison task (kept).</summary>
    public bool GarrisonTaskTarget { get; set; }
    /// <summary>Claim mark: attack target (kept).</summary>
    public bool AttackMark { get; set; }
    /// <summary>Claim mark: assault target (kept).</summary>
    public bool AssaultMark { get; set; }
    /// <summary>Claim mark: bombard target (kept).</summary>
    public bool BombardMark { get; set; }
    /// <summary>Claim mark: move target (kept).</summary>
    public bool MoveMark { get; set; }
    public bool Yavin { get; set; }
    /// <summary>A planner's hub system (kept).</summary>
    public bool Hub { get; set; }
    /// <summary>Claimed as the target of a type-1 character op (kept).</summary>
    public bool CharacterClaim { get; set; }
    public bool Coruscant { get; set; }
    /// <summary>An HQ of the owner.</summary>
    public bool Headquarters { get; set; }
    /// <summary>Uprising or Blockade.</summary>
    public bool Unrest => Uprising || Blockade;
    /// <summary>A construction role of planners 1-3.</summary>
    public bool AnyConstructionRole => ConstructionRole || MineRole || DefenceRole;
    public bool HasRole => ShipyardRole || TrainingRole || AnyConstructionRole;

    /// <summary>Clears what an update rebuilds: the roles, the diplomacy mark, the base and claim marks, the hub and the character claim are kept.</summary>
    internal void ClearForUpdate()
    {
        (Uprising, Blockade, HasFreeEnergy, HasFreeRawMaterial, MineSite, RefinerySite, IdleShipyard, IdleTraining) = (false, false, false, false, false, false, false, false);
        (IdleConstruction, UsableProducer, GarrisonShort, DefenceShort, GenCoreShort, GarrisonSurplus, GarrisonTroopsShort) = (false, false, false, false, false, false, false);
        (FighterShort, OwnFighters, UsableFighterShort, CalmWithoutRole, AttackGarrison, DefenseGarrison, DeathStarShieldWanted, KeySystem)
            = (false, false, false, false, false, false, false, false);
        (OwnExplored, CharacterSite, ShipProduction, TrainingProduction, ConstructionProduction, UprisingNoFleet, DiplomacyTarget, NotOwn)
            = (false, false, false, false, false, false, false, false);
        (EnemySupported, Claimed, EnemyCharacter, EnemyCharacterAboard, EnemyMajor, EnemyMajorAboard, OwnCaptured, OwnCapturedAboard)
            = (false, false, false, false, false, false, false, false);
        (EnemyMines, EnemyProducers, EnemyGenCores, EnemyDeathStarShield, EnemyBatteries, EnemyTroops, EnemyFighters, EnemyFleetSeen)
            = (false, false, false, false, false, false, false, false);
        (EnemyDeathStarSeen, HeadquartersSeen, EnemyCaptured, Explored, LowSupport) = (false, false, false, false, false);
        (Own, Neutral, Enemy, Populated, Destroyed, OwnedOrPopulated, Rim, UsableDeathStarShield) = (false, false, false, false, false, false, false, false);
        (ManyGenCores, EnemyFleet, EnemyDeathStar, NoBatteries, EnemyCommander, CommandKind3, AttackTroops, DefenseTroops)
            = (false, false, false, false, false, false, false, false);
        (Yavin, Coruscant, Headquarters) = (false, false, false);
    }
    /// <summary>The day the last Blockade was seen (neutral or enemy systems).</summary>
    public int BlockadeDay { get; set; }
    // counters, zeroed by every update
    /// <summary>The AI side's support share (100 for an own unpopulated system).</summary>
    public int Support { get; set; }
    public int Energy { get; set; }
    /// <summary>The owner's Mines.</summary>
    public int Mines { get; set; }
    /// <summary>The owner's Refineries.</summary>
    public int Refineries { get; set; }
    public int Shipyards { get; set; }
    public int TrainingFacilities { get; set; }
    public int ConstructionYards { get; set; }
    public int GenCores { get; set; }
    /// <summary>Usable GenCores' ShieldStrength.</summary>
    public int GenCoreStrength { get; set; }
    /// <summary>The GenCore target (own systems).</summary>
    public int GenCoreTarget { get; set; }
    public int DeathStarShields { get; set; }
    /// <summary>KDY and LNR batteries.</summary>
    public int Batteries { get; set; }
    /// <summary>Usable batteries' AttackStrength.</summary>
    public int BatteryStrength { get; set; }
    /// <summary>The battery target (own systems).</summary>
    public int BatteryTarget { get; set; }
    /// <summary>Raw material minus the owner's Existing Mines (Inferred: free raw material).</summary>
    public int FreeRawMaterial { get; set; }
    /// <summary>Energy minus the owner's Existing facilities (Inferred: free energy).</summary>
    public int FreeEnergy { get; set; }
    public int Troops { get; set; }
    /// <summary>Own troops counted as garrison in the system update's troop step (WorldModel step 8); meaning unknown.</summary>
    public int GarrisonTroops { get; set; }
    public int UsableTroops { get; set; }
    /// <summary>Usable troops above the garrison target.</summary>
    public int TroopSurplus { get; set; }
    /// <summary>Raised by low support (at most 6), doubled with Uprising (meaning unknown).</summary>
    public int UnrestTroops { get; set; }
    /// <summary>Troops short of the garrison target.</summary>
    public int TroopShortfall { get; set; }
    /// <summary>The garrison target.</summary>
    public int GarrisonTarget { get; set; }
    public int Fighters { get; set; }
    /// <summary>Own fighters at least as strong as the best buildable one (any without a role). Meaning in the original is unknown.</summary>
    public int StrongFighters { get; set; }
    public int UsableFighters { get; set; }
    /// <summary>Usable fighters' first damaged stat.</summary>
    public int FighterFirepower { get; set; }
    /// <summary>Usable fighters' second damaged stat.</summary>
    public int FighterAntiFighterFirepower { get; set; }
    /// <summary>Usable fighters' third damaged stat. Meaning in the original is unknown.</summary>
    public int FighterShields { get; set; }
    /// <summary>Enemy fleets' units, plus (enemy systems) usable troops and fighters.</summary>
    public int EnemyUnits { get; set; }
    public int TroopMaintenance { get; set; }
    /// <summary>GenCores, Death Star shields and batteries.</summary>
    public int DefenceMaintenance { get; set; }
    public int FighterMaintenance { get; set; }
    /// <summary>All production facilities.</summary>
    public int ProducerMaintenance { get; set; }
    public int ShipyardMaintenance { get; set; }
    public int TrainingMaintenance { get; set; }
    public int ConstructionMaintenance { get; set; }
    /// <summary>The defence balance (GenCores and batteries against their targets).</summary>
    public int DefenceBalance { get; set; }
    /// <summary>The fighter balance.</summary>
    public int FighterBalance { get; set; }
    /// <summary>Ready enemy fleets' <see cref="AiFleet.FirepowerAllFighters"/>.</summary>
    public int EnemyFirepower { get; set; }
    /// <summary>Ready enemy fleets' <see cref="AiFleet.AntiFighterFirepowerAllFighters"/>.</summary>
    public int EnemyAntiFighterFirepower { get; set; }
    /// <summary>Ready enemy fleets' <see cref="AiFleet.ShieldRecharge"/>.</summary>
    public int EnemyShieldRecharge { get; set; }
    /// <summary>Ready enemy fleets' fighters.</summary>
    public int EnemyFleetFighters { get; set; }
    /// <summary>Own-allegiance major characters, not Captured.</summary>
    public int OwnMajors { get; set; }
    /// <summary>Own-allegiance minor characters, not Captured.</summary>
    public int OwnMinors { get; set; }
    /// <summary>Captured characters.</summary>
    public int Captured { get; set; }
    public int EnemyMajors { get; set; }
    public int EnemyMinors { get; set; }
    /// <summary>Enemy victory characters (+1 for an unowned or enemy Coruscant).</summary>
    public int VictoryCharacters { get; set; }
    /// <summary>Free facility slots (own systems).</summary>
    public int FreeSlots { get; set; }
    /// <summary>Inferred: troops needed to take the system (neutral / enemy).</summary>
    public int TroopsToTake { get; set; }
    /// <summary>The usable-fighter balance.</summary>
    public int UsableFighterBalance { get; set; }

    /// <summary>Zeroes the counters that have a writer.</summary>
    internal void ClearCounters()
    {
        (Support, Energy, Mines, Refineries, Shipyards, TrainingFacilities, ConstructionYards, GenCores, GenCoreStrength, GenCoreTarget) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        (DeathStarShields, Batteries, BatteryStrength, BatteryTarget, FreeRawMaterial, FreeEnergy, Troops, GarrisonTroops, UsableTroops, TroopSurplus) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        (UnrestTroops, TroopShortfall, GarrisonTarget, Fighters, StrongFighters, UsableFighters, FighterFirepower, FighterAntiFighterFirepower, FighterShields) = (0, 0, 0, 0, 0, 0, 0, 0, 0);
        (EnemyUnits, TroopMaintenance, DefenceMaintenance, FighterMaintenance, ProducerMaintenance, ShipyardMaintenance, TrainingMaintenance) = (0, 0, 0, 0, 0, 0, 0);
        (ConstructionMaintenance, DefenceBalance, FighterBalance, EnemyFirepower, EnemyAntiFighterFirepower, EnemyShieldRecharge, EnemyFleetFighters) = (0, 0, 0, 0, 0, 0, 0);
        (OwnMajors, OwnMinors, Captured, EnemyMajors, EnemyMinors, VictoryCharacters, FreeSlots, TroopsToTake, UsableFighterBalance) = (0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
