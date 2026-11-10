namespace OpenSWR.Sim.Ai;

/// <summary>Ship record.</summary>
public sealed class AiShip(GameObject ship)
{
    public GameObject Ship { get; } = ship;
    /// <summary>The ship's fleet.</summary>
    public Fleet? Fleet { get; set; }
    public bool Dirty { get; set; } = true;
    public int Key { get; set; }
    public int Side { get; set; }
    // class, copied from the catalogue at creation (kept)
    public bool Armed { get; set; }
    public bool AntiFighter { get; set; }
    public bool TroopCarrier { get; set; }
    /// <summary>An armed ship with troop capacity.</summary>
    public bool ArmedTroopCarrier { get; set; }
    public bool SmallHull { get; set; }
    public bool MediumHull { get; set; }
    public bool LargeHull { get; set; }
    public bool DeathStar { get; set; }
    public bool Interdictor { get; set; }
    // state from the side's view, rebuilt by every update
    /// <summary>Carries fighters.</summary>
    public bool HasFighters { get; set; }
    /// <summary>Carries troops.</summary>
    public bool HasTroops { get; set; }
    public bool Enroute { get; set; }
    /// <summary>Damaged, with the view's speed != 0.</summary>
    public bool DamagedMoving { get; set; }
    public bool NotCompleted { get; set; }
    public bool Own { get; set; }
    /// <summary>A fighter / troop aboard is not Completed (or a troop is Enroute).</summary>
    public bool AboardNotReady { get; set; }
    /// <summary>Something aboard is EnrouteActive.</summary>
    public bool AboardEnroute { get; set; }
    /// <summary>Not taken by a task (kept).</summary>
    public bool Free { get; set; }
    /// <summary>Enemy major character aboard, not Enroute.</summary>
    public bool EnemyMajor { get; set; }
    /// <summary>Own character aboard, Captured.</summary>
    public bool OwnCaptured { get; set; }
    /// <summary>Enemy minor character aboard, not Enroute.</summary>
    public bool EnemyMinor { get; set; }
    /// <summary>Enemy character aboard, Captured.</summary>
    public bool EnemyCaptured { get; set; }
    /// <summary>Own character aboard, free.</summary>
    public bool OwnCharacter { get; set; }
    /// <summary>Damaged, with the view's speed = 0.</summary>
    public bool DamagedStopped { get; set; }
    /// <summary>The fleet op's flagship (kept).</summary>
    public bool FlagshipMark { get; set; }
    /// <summary>A balance builder wanted the flagship for another place, so the flagship is chosen again (kept).</summary>
    public bool FlagshipWanted { get; set; }
    /// <summary>Enemy character with a command kind aboard.</summary>
    public bool EnemyCommander { get; set; }
    /// <summary>Kept across updates; cleared while not Completed.</summary>
    public bool Named { get; set; }
    /// <summary>Taken by a garrison task (kept).</summary>
    public bool Garrison { get; set; }
    /// <summary>A character aboard with command kind 3.</summary>
    public bool CommandKind3 { get; set; }
    public bool Damaged => DamagedStopped || DamagedMoving;
    /// <summary>The fleet-op key.</summary>
    public int OpKey { get; set; }
    /// <summary>The fleet-op task slot.</summary>
    public int OpEntry { get; set; }
    /// <summary>Hull, from the ship's stats.</summary>
    public int Hull { get; set; }
    /// <summary>The ship's bombardment value, unless <see cref="DamagedMoving"/>, plus the usable fighters' bombardment values.</summary>
    public int Bombardment { get; set; }
    /// <summary>Sum of the usable troops' AttackStrength.</summary>
    public int TroopAttack { get; set; }
    /// <summary>The ship's stat scaled by the second weapon value, div another stat.</summary>
    public int ShieldRecharge { get; set; }
    public int Fighters { get; set; }
    public int UsableFighters { get; set; }
    public int FighterCapacity { get; set; }
    public int Troops { get; set; }
    public int UsableTroops { get; set; }
    public int TroopCapacity { get; set; }
    /// <summary>The ship's MaintenanceCost.</summary>
    public int Maintenance { get; set; }
    /// <summary>Maintenance of the troops aboard.</summary>
    public int TroopMaintenance { get; set; }
    /// <summary>Maintenance of the fighters aboard.</summary>
    public int FighterMaintenance { get; set; }
    /// <summary>The ship's stat scaled by the weapon value, plus the usable fighters' damaged stat.</summary>
    public int Firepower { get; set; }
    /// <summary>Class 2 only: the second ship stat scaled by the weapon value, plus the fighters' damaged second stat.</summary>
    public int AntiFighterFirepower { get; set; }
    /// <summary>The fighters' damaged third stat.</summary>
    public int FighterShields { get; set; }
    /// <summary>One plus the usable fighters and usable troops.</summary>
    public int Units { get; set; }
    /// <summary>Enemy victory characters aboard, not Enroute, standard victory mode only.</summary>
    public int VictoryCharacters { get; set; }
    /// <summary>As the stat sum above, counting every Completed fighter.</summary>
    public int FirepowerAllFighters { get; set; }
    /// <summary>As the class 2 sum above, counting every Completed fighter.</summary>
    public int AntiFighterFirepowerAllFighters { get; set; }

    /// <summary>Clears the state an update rebuilds; the class, the task marks, Free, Named and Garrison are kept.</summary>
    internal void ClearUnkept()
    {
        (HasFighters, HasTroops, Enroute, DamagedMoving, NotCompleted, Own, AboardNotReady, AboardEnroute) = (false, false, false, false, false, false, false, false);
        (EnemyMajor, OwnCaptured, EnemyMinor, EnemyCaptured, OwnCharacter, DamagedStopped, EnemyCommander, CommandKind3) = (false, false, false, false, false, false, false, false);
    }

    /// <summary>Zeroes the update's counters (the trailing ones have no writer).</summary>
    internal void ClearCounters()
    {
        (Hull, Bombardment, TroopAttack, ShieldRecharge, Fighters, UsableFighters, FighterCapacity, Troops, UsableTroops, TroopCapacity) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        (Maintenance, TroopMaintenance, FighterMaintenance, Firepower, AntiFighterFirepower, FighterShields, Units, VictoryCharacters) = (0, 0, 0, 0, 0, 0, 0, 0);
        (FirepowerAllFighters, AntiFighterFirepowerAllFighters) = (0, 0);
    }
}

/// <summary>Fleet record.</summary>
public sealed class AiFleet(Fleet fleet)
{
    public Fleet Fleet { get; } = fleet;
    /// <summary>The fleet's location system.</summary>
    public StarSystem? Location { get; set; }
    /// <summary>The target claimed by a fleet-op task (kept across updates).</summary>
    public StarSystem? Target { get; set; }
    public int Key { get; set; }
    /// <summary>The view owner at creation.</summary>
    public int Owner { get; set; }
    /// <summary>The fleet-op key.</summary>
    public int OpKey { get; set; }
    /// <summary>The fleet-op task slot.</summary>
    public int OpEntry { get; set; }
    /// <summary>The owner at creation is the AI side (fixed for the record's life).</summary>
    public bool Own { get; set; }
    public bool Enroute { get; set; }
    /// <summary>Not taken by a fleet op (kept).</summary>
    public bool Free { get; set; }
    /// <summary>Held by a garrison task (kept).</summary>
    public bool Garrison { get; set; }
    /// <summary>Holds a Death Star.</summary>
    public bool DeathStar { get; set; }
    public bool Interdictor { get; set; }
    /// <summary>A ship with <see cref="AiShip.AboardNotReady"/>.</summary>
    public bool AboardNotReady { get; set; }
    /// <summary>A ship with <see cref="AiShip.AboardEnroute"/>.</summary>
    public bool AboardEnroute { get; set; }
    /// <summary>A not-Completed ship.</summary>
    public bool NotCompletedShip { get; set; }
    /// <summary>An Enroute ship.</summary>
    public bool EnrouteShip { get; set; }
    /// <summary>A ship with <see cref="AiShip.DamagedMoving"/>.</summary>
    public bool DamagedMoving { get; set; }
    /// <summary>A ship with <see cref="AiShip.DamagedStopped"/>.</summary>
    public bool DamagedStopped { get; set; }
    /// <summary>A Completed, not Enroute ship.</summary>
    public bool ReadyShip { get; set; }
    /// <summary>A ship that is not damaged.</summary>
    public bool UndamagedShip { get; set; }
    /// <summary>Carries fighters.</summary>
    public bool HasFighters { get; set; }
    /// <summary>Carries troops.</summary>
    public bool HasTroops { get; set; }
    /// <summary>Has at least one free troop slot.</summary>
    public bool HasTroopRoom { get; set; }
    /// <summary>Has at least one free fighter slot.</summary>
    public bool HasFighterRoom { get; set; }
    public bool EnemyMajor { get; set; }
    public bool EnemyMinor { get; set; }
    public bool EnemyCaptured { get; set; }
    public bool OwnCaptured { get; set; }
    public bool EnemyCommander { get; set; }
    public bool OwnCharacter { get; set; }
    /// <summary>A ship with <see cref="AiShip.CommandKind3"/>.</summary>
    public bool CommandKind3 { get; set; }
    /// <summary>Inferred: a fleet-op task's attack claim on its target (kept).</summary>
    public bool AttackClaim { get; set; }
    /// <summary>Inferred: assault claim (kept).</summary>
    public bool AssaultClaim { get; set; }
    /// <summary>Inferred: bombard claim (kept).</summary>
    public bool BombardClaim { get; set; }
    /// <summary>Inferred: move claim (kept).</summary>
    public bool MoveClaim { get; set; }
    public bool Claimed => AttackClaim || AssaultClaim || BombardClaim || MoveClaim;
    public bool Damaged => DamagedMoving || DamagedStopped;

    /// <summary>Clears every claim.</summary>
    internal void ClearClaims() => (AttackClaim, AssaultClaim, BombardClaim, MoveClaim) = (false, false, false, false);

    /// <summary>Clears the state an update rebuilds; Free, Garrison and the claims are kept.</summary>
    internal void ClearUnkept()
    {
        (Own, Enroute, DeathStar, Interdictor, AboardNotReady, AboardEnroute, NotCompletedShip, EnrouteShip) = (false, false, false, false, false, false, false, false);
        (DamagedMoving, DamagedStopped, ReadyShip, UndamagedShip, HasFighters, HasTroops, HasTroopRoom, HasFighterRoom) = (false, false, false, false, false, false, false, false);
        (EnemyMajor, EnemyMinor, EnemyCaptured, OwnCaptured, EnemyCommander, OwnCharacter, CommandKind3) = (false, false, false, false, false, false, false);
    }
    // sums over the ship records; "Completed" = ships without AiShip.NotCompleted
    /// <summary>Ship count.</summary>
    public int Ships { get; set; }
    /// <summary>Completed ships' hull.</summary>
    public int Hull { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.Firepower"/>.</summary>
    public int Firepower { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.AntiFighterFirepower"/>.</summary>
    public int AntiFighterFirepower { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.ShieldRecharge"/>.</summary>
    public int ShieldRecharge { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.FighterShields"/>.</summary>
    public int FighterShields { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.Bombardment"/>.</summary>
    public int Bombardment { get; set; }
    /// <summary>Completed ships' troop AttackStrength.</summary>
    public int TroopAttack { get; set; }
    public int Fighters { get; set; }
    public int UsableFighters { get; set; }
    public int FighterCapacity { get; set; }
    public int Troops { get; set; }
    public int UsableTroops { get; set; }
    public int TroopCapacity { get; set; }
    /// <summary>All ships: ship maintenance.</summary>
    public int Maintenance { get; set; }
    /// <summary>All ships: maintenance of the troops aboard.</summary>
    public int TroopMaintenance { get; set; }
    /// <summary>All ships: maintenance of the fighters aboard.</summary>
    public int FighterMaintenance { get; set; }
    /// <summary>Unnamed ships.</summary>
    public int Unnamed { get; set; }
    /// <summary>Undamaged ships of class Armed.</summary>
    public int Armed { get; set; }
    /// <summary>Undamaged ships of class AntiFighter.</summary>
    public int AntiFighter { get; set; }
    /// <summary>Undamaged ships of class TroopCarrier.</summary>
    public int TroopCarriers { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.Units"/>.</summary>
    public int Units { get; set; }
    /// <summary>Free troop slots over ready ships (neither not Completed nor Enroute).</summary>
    public int TroopRoom { get; set; }
    /// <summary>Free fighter slots over ready ships.</summary>
    public int FighterRoom { get; set; }
    /// <summary>Sum of <see cref="AiShip.VictoryCharacters"/> over the ships with <see cref="AiShip.EnemyMajor"/>.</summary>
    public int VictoryCharacters { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.FirepowerAllFighters"/>.</summary>
    public int FirepowerAllFighters { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.AntiFighterFirepowerAllFighters"/>.</summary>
    public int AntiFighterFirepowerAllFighters { get; set; }
    /// <summary>Troop capacity of the TroopCarrier-class ships.</summary>
    public int CarrierTroopCapacity { get; set; }

    /// <summary>Zeroes the update's sums (the last two have no writer).</summary>
    internal void ClearSums()
    {
        (Ships, Hull, Firepower, AntiFighterFirepower, ShieldRecharge, FighterShields, Bombardment, TroopAttack, Fighters, UsableFighters, FighterCapacity) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        (Troops, UsableTroops, TroopCapacity, Maintenance, TroopMaintenance, FighterMaintenance, Unnamed, Armed, AntiFighter) = (0, 0, 0, 0, 0, 0, 0, 0, 0);
        (TroopCarriers, Units, TroopRoom, FighterRoom, VictoryCharacters, FirepowerAllFighters, AntiFighterFirepowerAllFighters, CarrierTroopCapacity) = (0, 0, 0, 0, 0, 0, 0, 0);
    }
}

/// <summary>
/// Fleet operation: key, produce state, flagship, gather
/// system (written by state 6), theater sector, base system, theater entry,
/// task, status, refresh flag, need counters, copies,
/// fighter need, attached object-op counts, mission request, attached character-op counts
/// (types 3, 4 and 5), character-op create / retire requests. Members: ships, fleets,
/// attached character ops, attached object-kind mission ops.
/// </summary>
public sealed class FleetOp(int key)
{
    public int Key { get; } = key;
    public int State { get; set; }
    public AiSector? Theater { get; set; }
    public StarSystem? Base { get; set; }
    public int Entry { get; set; }
    public FleetTask? Task { get; set; }
    public int TaskKind => Task?.Type ?? 0;
    public bool Refresh { get; set; }
    public AiShip? Flagship { get; set; }
    /// <summary>The gather system.</summary>
    public StarSystem? Gather { get; set; }
    public List<AiShip> Ships { get; } = [];
    public List<AiFleet> Fleets { get; } = [];
    public List<CharacterOp> CharacterOps { get; } = [];
    /// <summary>Attached object-kind mission ops, counted in <see cref="ObjectOps"/> by class index.</summary>
    public List<MissionOp> MissionOps { get; } = [];
    /// <summary>The attached character op to retire.</summary>
    public CharacterOp? RetireRequest { get; set; }
    // status, rebuilt by every produce cycle
    /// <summary>Some need is short and the op has nothing that counts.</summary>
    public bool Empty { get; set; }
    /// <summary>The task type's goal holds (Inferred meaning).</summary>
    public bool Goal { get; set; }
    /// <summary>Nothing is short.</summary>
    public bool Complete { get; set; }
    /// <summary>No strength or capacity is short.</summary>
    public bool StrongEnough { get; set; }
    /// <summary>Set by the last produce state unless <see cref="Empty"/> is.</summary>
    public bool NotEmpty { get; set; }
    /// <summary>A type-4 character op whose character is aboard the flagship with its command kind issued.</summary>
    public bool GeneralAtShip { get; set; }
    /// <summary>A character op whose character is aboard the flagship without its command kind issued.</summary>
    public bool CharacterAtShip { get; set; }
    /// <summary>An attached character op.</summary>
    public bool CharacterOpAttached { get; set; }
    /// <summary>
    /// Set by the task status every cycle; between the character status and the task status it says an attached character op
    /// waits at the gather system (planner 8's create gate). Meaning in the original is unknown.
    /// </summary>
    public bool CharacterWaiting { get; set; }
    /// <summary>Set with a theater; cleared by the theater check.</summary>
    public bool TheaterSet { get; set; }
    // needs: a counter below 0 after the shortfall builders
    public bool ShortFirepower { get; set; }
    public bool ShortAntiFighterFirepower { get; set; }
    public bool ShortTroopCapacity { get; set; }
    public bool ShortFighterCapacity { get; set; }
    public bool ShortBombardment { get; set; }
    public bool ShortTroops { get; set; }
    public bool ShortFighters { get; set; }
    public bool ShortAntiFighters { get; set; }
    public bool ShortInterdictors { get; set; }
    /// <summary>A mission-op create request was set.</summary>
    public bool MissionRequested { get; set; }
    /// <summary>A character-op create or retire request.</summary>
    public bool CharacterRequested { get; set; }
    /// <summary>An attached op without a full team.</summary>
    public bool OpWithoutTeam { get; set; }
    // needs: a counter above 0 after the member sums
    public bool HasInterdictors { get; set; }
    public bool HasFirepower { get; set; }
    public bool HasAntiFighterFirepower { get; set; }
    public bool HasTroopCapacity { get; set; }
    public bool HasFighterCapacity { get; set; }
    public bool HasBombardment { get; set; }
    public bool HasTroops { get; set; }
    public bool HasFighters { get; set; }
    public bool HasAntiFighters { get; set; }
    /// <summary>An attached mission op.</summary>
    public bool HasMissionOps { get; set; }
    /// <summary>An attached character op.</summary>
    public bool HasCharacterOps { get; set; }
    /// <summary>An attached op with its team.</summary>
    public bool OpTeamed { get; set; }
    /// <summary>A strength, capacity or bombardment need is short.</summary>
    public bool ForceShort => ShortFirepower || ShortAntiFighterFirepower || ShortTroopCapacity || ShortFighterCapacity || ShortBombardment;
    /// <summary>Any need is short, or an attached op lacks its team.</summary>
    public bool AnyShort => ForceShort || ShortTroops || ShortFighters || ShortAntiFighters || OpWithoutTeam;
    /// <summary>Something counts: a positive counter other than interdictors, or an attached op with its team.</summary>
    public bool HasAny => HasFirepower || HasAntiFighterFirepower || HasTroopCapacity || HasFighterCapacity || HasBombardment || HasTroops
        || HasFighters || HasAntiFighters || OpTeamed;

    /// <summary>Clears the status and the needs.</summary>
    internal void ClearStatus()
    {
        (Empty, Goal, Complete, StrongEnough, NotEmpty, GeneralAtShip, CharacterAtShip, CharacterOpAttached, CharacterWaiting, TheaterSet)
            = (false, false, false, false, false, false, false, false, false, false);
        (ShortFirepower, ShortAntiFighterFirepower, ShortTroopCapacity, ShortFighterCapacity, ShortBombardment, ShortTroops, ShortFighters)
            = (false, false, false, false, false, false, false);
        (ShortAntiFighters, ShortInterdictors, MissionRequested, CharacterRequested, OpWithoutTeam) = (false, false, false, false, false);
        (HasInterdictors, HasFirepower, HasAntiFighterFirepower, HasTroopCapacity, HasFighterCapacity, HasBombardment, HasTroops, HasFighters)
            = (false, false, false, false, false, false, false, false);
        (HasAntiFighters, HasMissionOps, HasCharacterOps, OpTeamed) = (false, false, false, false);
    }
    // need counters (state 2: member-fleet sums minus the task targets; a joining ship adds its figures)
    /// <summary>Fleet <see cref="AiFleet.FirepowerAllFighters"/> / ship <see cref="AiShip.Firepower"/>.</summary>
    public int Firepower { get; set; }
    /// <summary>Fleet <see cref="AiFleet.AntiFighterFirepowerAllFighters"/> / ship <see cref="AiShip.AntiFighterFirepower"/>.</summary>
    public int AntiFighterFirepower { get; set; }
    public int TroopCapacity { get; set; }
    public int FighterCapacity { get; set; }
    /// <summary>Fleets with a Death Star / ships of class Death Star.</summary>
    public int DeathStars { get; set; }
    /// <summary>Fleet <see cref="AiFleet.Bombardment"/> / ship <see cref="AiShip.Bombardment"/>.</summary>
    public int Bombardment { get; set; }
    /// <summary>Fleets with an interdictor / interdictor ships.</summary>
    public int Interdictors { get; set; }
    public int Troops { get; set; }
    public int Fighters { get; set; }
    /// <summary>Fleet <see cref="AiFleet.AntiFighter"/> / ships of class AntiFighter.</summary>
    public int AntiFighters { get; set; }
    /// <summary><see cref="TroopCapacity"/> before the targets.</summary>
    public int TroopCapacitySum { get; set; }
    /// <summary><see cref="FighterCapacity"/> before the targets.</summary>
    public int FighterCapacitySum { get; set; }
    /// <summary>Fighter need.</summary>
    public int FighterNeed { get; set; }
    /// <summary>Attached object-kind mission ops per class index.</summary>
    public ref int ObjectOps(int index) => ref _objectOps[index];
    readonly int[] _objectOps = new int[4];
    /// <summary>The mission request's kind.</summary>
    public int RequestKind { get; set; }
    /// <summary>The mission request's subtype.</summary>
    public int RequestSubtype { get; set; }
    /// <summary>The attached mission op to retire.</summary>
    public int RetireKey { get; set; }
    /// <summary>Attached character ops of type 3, 4 and 5.</summary>
    public ref int CharacterOpCount(int type) => ref _characterOps[type - 3];
    readonly int[] _characterOps = new int[3];
    /// <summary>The character-op type to create.</summary>
    public int CharacterRequest { get; set; }

    /// <summary>Zeroes the need counters.</summary>
    internal void ClearCounters() =>
        (Firepower, AntiFighterFirepower, TroopCapacity, FighterCapacity, DeathStars, Bombardment, Interdictors, Troops, Fighters, AntiFighters) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Fleet-op task (types 1-6 = the theater kind): machine state, the fleet
/// cursors (current fleet, state-5 start, strike cursor), the need targets (one per fleet-op
/// counter, raised only), type 4's site.
/// </summary>
public sealed class FleetTask(int type)
{
    public int Type { get; } = type;
    public int State { get; set; }
    public AiFleet? Fleet { get; set; }
    public AiFleet? Start { get; set; }
    /// <summary>The strike cursor, a member fleet's key (none = start at the lowest key).</summary>
    public uint? Strike { get; set; }
    /// <summary>The producer site (type 4).</summary>
    public StarSystem? Site { get; set; }
    /// <summary>The visited systems (type 2); lives and dies with the task, not saved.</summary>
    public HashSet<StarSystem> Visited { get; } = [];
    // need targets: the fleet-op counter of the same name loses its target
    public int Firepower { get; set; }
    public int AntiFighterFirepower { get; set; }
    public int TroopCapacity { get; set; }
    public int FighterCapacity { get; set; }
    public int DeathStars { get; set; }
    public int Bombardment { get; set; }
    public int Interdictors { get; set; }
    public int Troops { get; set; }
    public int Fighters { get; set; }
    public int AntiFighters { get; set; }
}

/// <summary>What an agent can do: the missions it may join alone, the command roles of a minor character, the special-force type.</summary>
public sealed class AgentCapabilities
{
    public bool Diplomacy { get; set; }
    public bool Rescue { get; set; }
    public bool Sabotage { get; set; }
    public bool Espionage { get; set; }
    public bool Abduction { get; set; }
    public bool Subdue { get; set; }
    public bool Assassination { get; set; }
    public bool DeathStarSabotage { get; set; }
    public bool Incite { get; set; }
    public bool Recon { get; set; }
    public bool Recruitment { get; set; }
    public bool ShipDesign { get; set; }
    public bool TroopTraining { get; set; }
    public bool FacilityDesign { get; set; }
    /// <summary>No writer: always clear. Meaning in the original is unknown.</summary>
    public bool Unused { get; set; }
    /// <summary>SD CanBeAdmiral (minor characters).</summary>
    public bool Admiral { get; set; }
    /// <summary>SD CanBeCommander.</summary>
    public bool Commander { get; set; }
    /// <summary>SD CanBeGeneral.</summary>
    public bool General { get; set; }
    /// <summary>Special forces: the type 1 to 4 (types 5 to 8 count as 1 to 4); 0 for characters.</summary>
    public int SpecialForceKind { get; set; }
    public bool Commanding => Admiral || Commander || General;
}

/// <summary>Agent record: one per character or special force with a view.</summary>
public sealed class AiAgent(Agent agent)
{
    public Agent Agent { get; } = agent;
    /// <summary>Location key (set once from the view's parent).</summary>
    public GameObject? Location { get; set; }
    public bool Dirty { get; set; }
    public int Key { get; set; }
    public int Side { get; set; }
    /// <summary>The view owner.</summary>
    public int Owner { get; set; }
    /// <summary>The mission op holding the agent.</summary>
    public int MissionOpKey { get; set; }
    /// <summary>The character op holding the agent.</summary>
    public int CharacterOpKey { get; set; }
    /// <summary>The holding op's priority.</summary>
    public int Priority { get; set; }
    // set at create, kept by every update
    /// <summary>Own allegiance (owner = own and not Captured, or owner != own and Captured).</summary>
    public bool Own { get; set; }
    /// <summary>Enemy allegiance.</summary>
    public bool Enemy { get; set; }
    /// <summary>One of the six major characters.</summary>
    public bool Major { get; set; }
    public bool Palpatine { get; set; }
    /// <summary>A mission command (0x240) was sent with the agent; cleared when that mission ends (kept).</summary>
    public bool MissionSent { get; set; }
    /// <summary>Not held by an op: set at create and on release, cleared when an op takes the agent (kept); the list builders treat it like a set priority.</summary>
    public bool Unassigned { get; set; }
    public bool SpecialForce { get; set; }
    public bool Character { get; set; }
    /// <summary>Luke, Mothma, Palpatine or Vader.</summary>
    public bool VictoryCharacter { get; set; }
    // rebuilt by every update
    public bool NotCompleted { get; set; }
    public bool Injured { get; set; }
    public bool Enroute { get; set; }
    public bool Captured { get; set; }
    public bool OnMission { get; set; }
    /// <summary>Force 1-99, not Luke or Vader.</summary>
    public bool ForceTrainee { get; set; }
    /// <summary>Force >= 100, Luke or Vader.</summary>
    public bool ForceMaster { get; set; }
    /// <summary>The special-force pick of planner 5's disband order requires it. No writer: always clear. Meaning in the original is unknown.</summary>
    public bool Disbandable { get; set; }
    /// <summary>On a mandatory mission.</summary>
    public bool MandatoryMission { get; set; }
    public bool CommandKind1 { get; set; }
    public bool CommandKind2 { get; set; }
    public bool CommandKind3 { get; set; }
    /// <summary>A research capability whose line is not exhausted, or Diplomacy capable with Diplomacy &gt; 79: kept out of most teams.</summary>
    public bool Specialist { get; set; }
    public bool HasCommandKind => CommandKind1 || CommandKind2 || CommandKind3;
    /// <summary>Set at create, never recomputed.</summary>
    public AgentCapabilities Can { get; } = new();

    /// <summary>Clears the state an update rebuilds.</summary>
    internal void ClearUnkept()
    {
        (NotCompleted, Injured, Enroute, Captured, OnMission, ForceTrainee, ForceMaster, Disbandable) = (false, false, false, false, false, false, false, false);
        (MandatoryMission, CommandKind1, CommandKind2, CommandKind3, Specialist, Dirty) = (false, false, false, false, false, false);
    }
    /// <summary>The eight enhanced skills by <see cref="SkillIndex"/>.</summary>
    public ref int Skill(int index) => ref _skills[index];
    readonly int[] _skills = new int[8];
    public int Force { get; set; }
    /// <summary>A special force's MaintenanceCost.</summary>
    public int Maintenance { get; set; }

    /// <summary>Zeroes the update's fields.</summary>
    internal void ClearSkills()
    {
        for (int k = 0; k < 8; k++) Skill(k) = 0;
        (Force, Maintenance) = (0, 0);
    }
}

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
    /// <summary>Role of the mine planner, planner 2 (kept).</summary>
    public bool MineRole { get; set; }
    /// <summary>Role of the defence planner, planner 3 (kept).</summary>
    public bool DefenceRole { get; set; }
    /// <summary>Own, no Uprising or Blockade, no role.</summary>
    public bool CalmWithoutRole { get; set; }
    /// <summary>A garrison troop that can attack (with a role: at least as strong as the best buildable one). Meaning in the original is unknown.</summary>
    public bool AttackGarrison { get; set; }
    /// <summary>A garrison troop that can defend (without a role: any defense-only troop). Meaning in the original is unknown.</summary>
    public bool DefenseGarrison { get; set; }
    /// <summary>Inferred: a Death Star shield wanted at Coruscant.</summary>
    public bool DeathStarShieldWanted { get; set; }
    /// <summary>Coruscant or an HQ of the owner.</summary>
    public bool KeySystem { get; set; }
    // knowledge, presence and the targets the planners look for
    /// <summary>Own and explored.</summary>
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
    /// <summary>A diplomacy target: neutral without an enemy fleet, or own with low support, no enemy fleet and no Uprising.</summary>
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
    /// <summary>A ready enemy fleet here (set with <see cref="EnemyFleet"/>).</summary>
    public bool EnemyFleetSeen { get; set; }
    /// <summary>A ready enemy fleet with a Death Star here (Alliance side only).</summary>
    public bool EnemyDeathStarSeen { get; set; }
    /// <summary>An HQ of the owner (set with <see cref="Headquarters"/>).</summary>
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
    /// <summary>A fleet-op base of task types 4 and 6, where the assault troops gather (kept).</summary>
    public bool AssaultBase { get; set; }
    /// <summary>A fleet-op base of task type 5, where the defense troops gather (kept).</summary>
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
    /// <summary>Own troops counted by step 8 (meaning unknown).</summary>
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
    /// <summary>Own fighters at least as strong as the best buildable one (any without a role).</summary>
    public int StrongFighters { get; set; }
    public int UsableFighters { get; set; }
    /// <summary>Usable fighters' first damaged stat.</summary>
    public int FighterFirepower { get; set; }
    /// <summary>Usable fighters' second damaged stat.</summary>
    public int FighterAntiFighterFirepower { get; set; }
    /// <summary>Usable fighters' third damaged stat.</summary>
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
    /// <summary>An own or neutral key system.</summary>
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
    /// <summary>A system with <see cref="AiSystem.EnemyFleetSeen"/>.</summary>
    public bool EnemyFleetSeen { get; set; }
    /// <summary>An own captured character here or aboard.</summary>
    public bool HasOwnCaptured { get; set; }
    public bool HasEnemyDeathStar { get; set; }
    /// <summary>A system with <see cref="AiSystem.DiplomacyTarget"/> and without <see cref="AiSystem.DiplomacyMark"/>.</summary>
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
    /// <summary>An own base with <see cref="AiSystem.AssaultBase"/>.</summary>
    public bool AssaultBase { get; set; }
    /// <summary>An own base with <see cref="AiSystem.DefenseBase"/> (without <see cref="AiSystem.AssaultBase"/>).</summary>
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
    /// <summary>The theater kind 1-6 (planner 13).</summary>
    public int TheaterKind { get; set; }
    /// <summary>The theater argument (planner 13; 1 shipyards, 2 training, 3 construction for the type-4 site).</summary>
    public int TheaterArgument { get; set; }
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

/// <summary>The model's garrison and defence constants; the planners raise some of them.</summary>
public enum ModelConstant
{
    /// <summary>Coruscant battery target.</summary>
    CoruscantBatteries,
    /// <summary>Coruscant GenCore target.</summary>
    CoruscantGenCores,
    /// <summary>Role-system GenCore target.</summary>
    RoleGenCores,
    /// <summary>Role-system battery target.</summary>
    RoleBatteries,
    /// <summary>GenCore target (role fallback and other systems; raised by planner 3).</summary>
    GenCores,
    /// <summary>Battery target of other systems.</summary>
    Batteries,
    /// <summary>Garrison target of other systems (raised by planner 6).</summary>
    Garrison,
    /// <summary>Role-system garrison target (raised by planner 6).</summary>
    RoleGarrison,
    /// <summary>Key-system garrison target (raised by planner 6).</summary>
    KeyGarrison,
    /// <summary>Fighter margin of other systems (raised by planner 7).</summary>
    FighterMargin,
    /// <summary>Role-system fighter margin (raised by planner 7).</summary>
    RoleFighterMargin,
    /// <summary>Key-system fighter margin (raised by planner 7).</summary>
    KeyFighterMargin,
}

/// <summary>The production roles a planner gives a system.</summary>
public enum SystemRole { Shipyard, Training, Construction, Mine, Defence }
