using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>Object owner: side 1, side 2, or 3 = neutral / galaxy.</summary>
public enum Owner { None = 0, Alliance = 1, Empire = 2, Neutral = 3 }

/// <summary>
/// Observable state event, as the trace tool logs it: Kind 0x300 / 0x301 / 0x302 / 0x306 / 0x308 / 0x30C / 0x30D / 0x320 /
/// 0x321 = notification run (Old / New as carried: Existing, Usable, owner, HyperdriveActive, Completed, mission ready, phase,
/// parent SdRef (no parent = the staging container), agent mission SdRef (none = 2)); 0x60 / 0x64 = mission TaskStatus /
/// CompletionStatus change;
/// 0x387 = travel timer start (Old = Base, New = fire day).
/// </summary>
public readonly record struct Fact(int Day, int Kind, uint Object, long Old, long New);

/// <summary>The computer side's controller (openswr option, not in the original): the original-faithful AI or openswr's own AI.</summary>
public enum ComputerOpponent { Original = 0, OpenSWR = 1 }

/// <summary>New-game options. <see cref="Opponent"/> is an openswr option, not in the original.</summary>
public sealed record GameSettings(Owner PlayerSide, int GalaxySize, int Difficulty, int VictoryFlag, int Seed, bool HeadToHead = false,
    ComputerOpponent Opponent = ComputerOpponent.Original)
{
    /// <summary>TB settings column: 1 + 3*(side - 1) + difficulty; head to head = 7.</summary>
    public int Column => HeadToHead ? 7 : 1 + 3 * ((int)PlayerSide - 1) + Difficulty;
}

/// <summary>A yes / no per side: which sides observe an object, see it live, or acknowledged it.</summary>
public readonly record struct SideSet(bool Alliance, bool Empire)
{
    public static readonly SideSet None = default, Both = new(true, true);
    /// <summary>The value of a side; false for any other owner.</summary>
    public bool this[Owner side] => side == Owner.Alliance ? Alliance : side == Owner.Empire && Empire;
    /// <summary>This set with the value of one side replaced (other owners change nothing).</summary>
    public SideSet With(Owner side, bool value) =>
        side == Owner.Alliance ? this with { Alliance = value } : side == Owner.Empire ? this with { Empire = value } : this;
    /// <summary>Only the given side (none for neutral).</summary>
    public static SideSet Only(Owner side) => None.With(side, true);
}

/// <summary>
/// Capital ship critical-hit counters, in counter order: 0 ShieldRechargeRateCH, 1 WeaponRechargeRateCH, 2 TractorBeamPowerCH,
/// 3 SpeedCH, 4 PrimaryHyperdriveModifier, 5 BackupHyperdriveModifier.
/// </summary>
public readonly record struct CriticalHitCounts(int ShieldRecharge, int WeaponRecharge, int TractorBeam, int Speed,
    int PrimaryHyperdrive, int BackupHyperdrive)
{
    public int this[int k] => k switch
    {
        0 => ShieldRecharge, 1 => WeaponRecharge, 2 => TractorBeam, 3 => Speed, 4 => PrimaryHyperdrive, 5 => BackupHyperdrive,
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
    public CriticalHitCounts With(int k, int value) => k switch
    {
        0 => this with { ShieldRecharge = value }, 1 => this with { WeaponRecharge = value }, 2 => this with { TractorBeam = value },
        3 => this with { Speed = value }, 4 => this with { PrimaryHyperdrive = value }, 5 => this with { BackupHyperdrive = value },
        _ => throw new ArgumentOutOfRangeException(nameof(k)),
    };
}

/// <summary>A system's control: the owner, the support control and the military control (<see cref="Systems.Control"/>).</summary>
public readonly record struct SystemControl(Owner Owner, Owner Support, Owner Military);

/// <summary>Families (the family part of an SdRef).</summary>
public static class Family
{
    public const uint Fleet = 0x08, System = 0x90, Sector = 0x80;
    public static uint Of(uint sdRef) => SdRef.FamilyOf(sdRef);
    public static bool IsTroop(uint sdRef) => Of(sdRef) is >= 0x10 and < 0x14;
    public static bool IsCapitalShip(uint sdRef) => Of(sdRef) is >= 0x14 and < 0x1C;
    public static bool IsFacility(uint sdRef) => Of(sdRef) is >= 0x20 and < 0x30;
    public static bool IsSpecialForce(uint sdRef) => Of(sdRef) is >= 0x3C and < 0x40;
}

/// <summary>Well-known SdRefs used by setup.</summary>
public static class Refs
{
    public const uint Coruscant = 0x90000109, Yavin = 0x92000121, Fleet = 0x08000004, Mine = 0x2C000001,
        AllianceArmy = 0x10000002, EmpireArmy = 0x10000008, Vader = 0x35000281, Luke = 0x32000242, Han = 0x33000243,
        Leia = 0x31000241, Chewbacca = 0x38000343, Palpatine = 0x34000280;
}

/// <summary>
/// Unit, facility, fleet, system or character: an instance of an SD definition, possibly containing other objects.
/// Contents are kept in key order (family class, object id) ascending, never insertion order.
/// </summary>
public class GameObject(uint type, Owner owner, uint id)
{
    /// <summary>SdRef of the definition (its family and Index).</summary>
    public uint Type { get; } = type;
    /// <summary>Object id: SD Index for characters, sectors, systems, abodes; otherwise auto-assigned (<see cref="GameState.NextId"/>).</summary>
    public uint Id { get; } = id;
    /// <summary>Owner side. Changes after setup go through <see cref="Objects.SetOwner"/> (notification 0x302).</summary>
    public virtual Owner Owner { get; set; } = owner;
    /// <summary>Autoscrap request flag. Set on one unit per firing of timer 0x382 while the side's maintenance is exceeded.</summary>
    public bool AutoscrapRequested { get; set; }
    // Object flags (names from the debug notifications), all stored, the derived ones (Usable, Enroute, Existing) included.
    // Objects created by setup steps 9-18 start Created, Completed, Constructed and so Existing and Usable (SetSetupFlags).
    // Changes after setup go through the Objects setters, which run the flag hooks and post the notifications.
    public bool Usable { get; set; } = true;
    public bool Created { get; set; } = true;
    public bool Completed { get; set; } = true;
    public bool Destroyed { get; set; }
    public bool Enroute { get; set; }
    public bool EnrouteActive { get; set; }
    public bool Existing { get; set; } = true;
    public bool Damaged { get; set; }
    public bool HyperdriveActive { get; set; }
    public bool Autorouting { get; set; }
    public bool Locked { get; set; }
    public bool ReadyForDelete { get; set; }
    public bool Constructed { get; set; } = true;

    /// <summary>Clears every object flag (objects before their creation, unplaced characters).</summary>
    public void ClearFlags() =>
        (Usable, Created, Completed, Destroyed, Enroute, EnrouteActive, Existing, Damaged, HyperdriveActive, Autorouting, Locked, ReadyForDelete, Constructed) =
        (false, false, false, false, false, false, false, false, false, false, false, false, false);

    /// <summary>The setup flags: Created, Completed, Constructed, Existing and Usable, every other flag clear.</summary>
    public void SetSetupFlags()
    {
        ClearFlags();
        (Created, Completed, Constructed, Existing, Usable) = (true, true, true, true, true);
    }
    /// <summary>DestroyedReason: 0x14 scrap, 0x16 production cancelled. Set through <see cref="Objects.Destroy"/>.</summary>
    public int DestroyedReason { get; set; }
    /// <summary>ObservedBy per side.</summary>
    public SideSet Observed { get; set; }
    /// <summary>The sides whose view of the object is live; null = not yet computed (then live for the owner's side).</summary>
    public SideSet? LiveSides { get; set; }
    /// <summary>Lock counter; Locked = counter != 0. Changed through <see cref="Objects.Lock"/>.</summary>
    public int LockCount { get; set; }
    /// <summary>BuilderKey: the manufacturing manager that queued this unit.</summary>
    public ManufacturingManager? BuilderKey { get; set; }
    /// <summary>DestinationLocationAtDepartureKey ("DepDest") and ETA of a travel.</summary>
    public GameObject? Destination { get; set; }
    public int Eta { get; set; }
    /// <summary>Troop flag "DestroyedRunningBlockade": set by a failed withdraw draw; its hook gives DestroyedReason 0xD.</summary>
    public bool DestroyedRunningBlockade { get; set; }
    /// <summary>
    /// Systems: TroopRegWithdrawPercent; troops: their copy, drawn on departure. Initial 100
    /// (Inferred: every traced departure drew chance(100); step 23 computes 100 without a blockade).
    /// </summary>
    public int WithdrawPercent { get; set; } = 100;
    /// <summary>Unit flag DetectorIsDecoyed: set by a successful decoy check of a mission encounter.</summary>
    public bool DetectorIsDecoyed { get; set; }
    /// <summary>Capital ships: HullDamage; fighters: SquadSizeDamage.</summary>
    public int HullDamage { get; set; }
    public int SquadSizeDamage { get; set; }
    /// <summary>Unit flags RepairActive (Damaged and Usable, timer 0x380 Active) and FastRepair (timer Base).</summary>
    public bool RepairActive { get; set; }
    public bool FastRepair { get; set; }
    /// <summary>Capital ship critical-hit counters (<see cref="CriticalHitCounts"/>).</summary>
    public CriticalHitCounts CriticalHits { get; set; }
    /// <summary>Critical-hit counter k in the order of <see cref="CriticalHitCounts"/>.</summary>
    public int Critical(int k) => CriticalHits[k];
    public void SetCritical(int k, int value) => CriticalHits = CriticalHits.With(k, value);
    /// <summary>Capital ship combat block: ion and turbolaser accumulators, shields absorbed.</summary>
    public int IonAccumulator { get; set; }
    public int LaserAccumulator { get; set; }
    public int ShieldsAbsorbed { get; set; }
    /// <summary>Acknowledged instances: the real object, and the side views (<see cref="Objects.Acknowledge"/>).</summary>
    public bool AckedReal { get; set; }
    public SideSet AckedViews { get; set; }
    /// <summary>
    /// Deleted by its 0x31A run: out of every game list, its container and the views; readers still holding the
    /// object see it as the original sees a missing key. A save holds ReadyForDelete only for deleted objects (the 0x31A runs in
    /// the same tick), so a load derives it.
    /// </summary>
    public bool Deleted { get; internal set; }
    public GameObject? Container { get; private set; }
    readonly List<GameObject> _contents = [];
    /// <summary>Contents in key order: ascending family, then ascending id.</summary>
    public IReadOnlyList<GameObject> Contents => _contents;

    /// <summary>
    /// Container key: familyClass, then id. familyClass is taken as the SD record family; the original's class constants
    /// (0x90/0x92 systems, 8 fleets, 0x14 capital ships, 0x30-0x35 majors, 0x38 minors) equal it in shipped data
    /// (Inferred for classes it does not list, e.g. the one 0x18 capital ship).
    /// </summary>
    public ulong Key => (ulong)Family.Of(Type) * 0x1_0000_0000 + Id;

    public T Add<T>(T item) where T : GameObject
    {
        item.Container?._contents.Remove(item);
        item.Container = this;
        int i = _contents.FindLastIndex(o => o.Key <= item.Key); // equal keys go right
        _contents.Insert(i + 1, item);
        return item;
    }

    /// <summary>Takes the object out of its container (destroyed objects leave play).</summary>
    public void Remove()
    {
        Container?._contents.Remove(this);
        Container = null;
    }

    /// <summary>Location: the system of the parent chain; a system returns itself, a manager its system; null outside systems.</summary>
    public virtual StarSystem? System => this as StarSystem ?? Container?.System;

    /// <summary>Object reference as events hold it: family and object id.</summary>
    public uint Ref => SdRef.Make(Family.Of(Type), Id);

    public override string ToString() => $"{Type:X8}#{Id:X} ({Owner})";
}

/// <summary>Fleet (family 0x08): real fleet or placeholder. Flag names Inferred except Battle, Blockade, Bombard, Assault.</summary>
public sealed class Fleet(uint type, Owner owner, uint id) : GameObject(type, owner, id)
{
    /// <summary>A real fleet (it has held a capital ship of its owner), not a placeholder.</summary>
    public bool Real { get; set; }
    /// <summary>The reserve placeholder of its side in a system.</summary>
    public bool Reserve { get; set; }
    /// <summary>The new-fleet placeholder of its side in a system.</summary>
    public bool NewFleet { get; set; }
    public bool Battle { get; set; }
    public bool Blockade { get; set; }
    public bool Bombard { get; set; }
    public bool Assault { get; set; }
}

/// <summary>Characters and special forces: rolled skills and mission membership. Skill order = Skills record order.</summary>
public class Agent(uint type, Owner owner, uint id) : GameObject(type, owner, id)
{
    /// <summary>Base skills, see <see cref="SkillIndex"/>.</summary>
    public int[] Skills { get; } = new int[8];
    /// <summary>RoleMissionKey.</summary>
    public Mission? Mission { get; set; }
    /// <summary>Agent OnMission (= mission ref set or MovingBetweenMissions).</summary>
    public bool OnMission { get; set; }
    /// <summary>Agent MissionResignRequest: set by an evasion of a foiled mission; the member leaves at clean-up.</summary>
    public bool ResignRequest { get; set; }
    /// <summary>Agent flag: the mission record's ResignOnInjury at the join, cleared on leaving; gates the injury and evasion resignations.</summary>
    public bool ResignOnInjury { get; set; }
    /// <summary>Agent MissionRemoveRequest: set through <see cref="Prisoners.SetRemoveRequest"/> (0x30B).</summary>
    public bool RemoveRequest { get; set; }
    /// <summary>Removed from its mission (member filter, leader, phase check).</summary>
    public bool Removed => ResignRequest || RemoveRequest;
    /// <summary>Agent Adrift: set through <see cref="Prisoners.SetAdrift"/> when the parent is destroyed (0x30E).</summary>
    public bool Adrift { get; set; }
    /// <summary>Agent IsDecoying: set by a successful decoy check. The clearing rule is not known.</summary>
    public bool IsDecoying { get; set; }
    /// <summary>ParentAtMissionKey (Inferred name): the parent when OnMission became set, cleared with it.</summary>
    public GameObject? ParentAtMission { get; set; }
    /// <summary>LocationAtMission (Inferred name): the location copy of <see cref="ParentAtMission"/>.</summary>
    public StarSystem? LocationAtMission { get; set; }
}

/// <summary>Skill indexes into <see cref="Agent.Skills"/> (record order; character role block names in brackets).</summary>
public static class SkillIndex
{
    public const int Diplomacy = 0, Espionage = 1, ShipDesign = 2 /* ShipyardRD */, TroopTraining = 3 /* TrainingFacilRD */,
        FacilityDesign = 4 /* ConstructionYardRD */, Combat = 5, Leadership = 6, Loyalty = 7;
}

public sealed class Character(CharacterRecord definition, Owner owner) : Agent(definition.Common.Ref, owner, definition.Common.Index)
{
    public CharacterRecord Definition { get; } = definition;
    /// <summary>Placed = Created (object flag); characters get it at placement. Unplaced characters have no flags.</summary>
    public bool Placed => Created;
    /// <summary>ForceTraining, 0 at placement, written only by Jedi training (<see cref="Jedi.SetForceTraining"/>); Force = rank + training.</summary>
    public int ForceBonus { get; set; }
    /// <summary>
    /// Character State: 0 captured, 1 escaped, 2 released, 3 rescued, 4 free (constructor value); changed only through
    /// <see cref="Prisoners.SetState"/>.
    /// </summary>
    public int State { get; set; } = Prisoners.Free;
    /// <summary>Character Captured = (State = 0), recomputed by the state hook.</summary>
    public bool Captured { get; set; }
    /// <summary>CanEscape (= Captured, drives timer 0x38A), EscapeRequest.</summary>
    public bool CanEscape { get; set; }
    public bool EscapeRequest { get; set; }
    /// <summary>CanHeal, Healing (drives timer 0x388), FastHeal (Force >= G 0xA00).</summary>
    public bool CanHeal { get; set; }
    public bool Healing { get; set; }
    public bool FastHeal { get; set; }
    /// <summary>Captor: the capturing object, or none.</summary>
    public GameObject? Captor { get; set; }
    /// <summary>Injury: lowers EnhancedCombat; healed by timer 0x388. Changes after setup go through <see cref="Prisoners.SetInjury"/>.</summary>
    public int Injury { get; set; }
    /// <summary>Force = ForceExperience (rank) + ForceTraining (bonus).</summary>
    public int Force => ForceRank + ForceBonus;
    /// <summary>ForceAware (rolled at the stat roll; later through discovery, <see cref="Jedi.SetForceAware"/>).</summary>
    public bool KnownJedi { get; set; }
    /// <summary>ForcePotential (a chance roll at the stat roll).</summary>
    public bool JediPotential { get; set; }
    /// <summary>ForceRank: rolled at placement; changes after setup through <see cref="Jedi.SetForceRank"/>.</summary>
    public int ForceRank { get; set; }
    // Story flags, class-specific (Han's BountyAttack pulses within the Bounty attempt and is never stored).
    /// <summary>Luke / Leia KnowsHeritage.</summary>
    public bool KnowsHeritage { get; set; }
    /// <summary>Luke FinalBattleReady.</summary>
    public bool FinalBattleReady { get; set; }
    /// <summary>Luke DagobahCompleted.</summary>
    public bool DagobahCompleted { get; set; }
    /// <summary>Han CapturedByBountyHunters: stored in the same flag as Luke's FinalBattleReady.</summary>
    public bool CapturedByBountyHunters { get => FinalBattleReady; set => FinalBattleReady = value; }
    /// <summary>EncounterKey and the encounter serial: a pending Force duel.</summary>
    public Character? EncounterKey { get; set; }
    public uint EncounterSerial { get; set; }
    /// <summary>ForceUserDiscoveredKey on the discoverer: the Force user it discovered last.</summary>
    public Character? DiscoveredForceUser { get; set; }
    /// <summary>Character flag that drives timer 0x389 Active.</summary>
    public bool ForceActive { get; set; }
    /// <summary>MissionHyperdriveModifier, the character's speed; null = the initial G 0x1. Set on mission phase 2 (Han rule).</summary>
    public int? MissionHyperdriveModifier { get; set; }
    /// <summary>CommandKind, set by commands 0x260-0x263 (kind = type - 0x260); 0 initially (Inferred).</summary>
    public int CommandKind { get; set; }
}

/// <summary>Processing facility state.</summary>
public enum FacilityState { Waiting = 1, Processing = 2, Done = 3, Idle = 4 }

/// <summary>
/// Processing facility (families 0x28-0x2F): shipyard, training facility, construction yard, Mine, Refinery.
/// </summary>
public sealed class Facility(uint type, Owner owner, uint id, int processingRate) : GameObject(type, owner, id)
{
    /// <summary>ProcessingRate.</summary>
    public int ProcessingRate { get; } = processingRate;
    public bool IsMineOrRefinery => Family.Of(Type) is 0x2C or 0x2D;
    /// <summary>Allocation pair: capacity 50 once active, allocated maintenance points (Mine/Refinery).</summary>
    public int Capacity { get; set; }
    public int Allocated { get; set; }
    /// <summary>Production modifier percent, initial 100.</summary>
    public int Modifier { get; set; } = 100;
    /// <summary>Cycle length in days.</summary>
    public int CycleLength { get; set; }
    public FacilityState State { get; set; } = FacilityState.Idle;
    /// <summary>Facility flags, changed through the <see cref="Economy"/> setters.</summary>
    public bool Suspended { get; set; }
    public bool PointPresent { get; set; }
    public bool PointProcessed { get; set; }
    public bool Processing { get; set; }
    public bool OnStartupCycle { get; set; }
    /// <summary>ETC: completion day while processing, else 0.</summary>
    public int CompletionDay { get; set; }
    /// <summary>Wait counter: +1 on every state change into or out of WAITING.</summary>
    public int WaitCounter { get; set; }
}

/// <summary>
/// Side object (family 0xF3): maintenance pair, demand, stocks, wait lists, speed.
/// </summary>
public sealed class SideState(uint sdRef)
{
    /// <summary>Side object reference (0xF300000F Alliance, 0xF3000010 Empire).</summary>
    public uint Ref { get; } = sdRef;
    public int MaintenanceCapacity { get; set; }
    public int MaintenanceAllocated { get; set; }
    public int Demand { get; set; }
    /// <summary>Maintenance exceeded flag.</summary>
    public bool Exceeded { get; set; }
    /// <summary>Ticks per day chosen by this side, default 1.</summary>
    public int Speed { get; set; } = 1;
    /// <summary>Raw and refined material stock.</summary>
    public int RawStock { get; set; }
    public int RefinedStock { get; set; }
    /// <summary>Refineries waiting for raw, manufacturing facilities waiting for refined, FIFO.</summary>
    public List<WaitEntry> RefineryWaitList { get; } = [];
    public List<WaitEntry> ManufacturingWaitList { get; } = [];
    /// <summary>Side number from the side's own SdRef (index 0xF -> 1, 0x10 -> 2).</summary>
    public Owner Owner => SdRef.IndexOf(Ref) == 0xF ? Owner.Alliance : Owner.Empire;
    /// <summary>
    /// Research lines by mission kind k (0 ship, 1 facility, 2 troop): points, current
    /// order, and exhausted flag per kind; all 0 at start.
    /// </summary>
    public int[] ResearchPoints { get; } = new int[3];
    public int[] ResearchOrder { get; } = new int[3];
    public bool[] ResearchExhausted { get; } = new bool[3];
    /// <summary>Research cost scale: 100, set to the number of systems placed in new-game step 11.</summary>
    public int CostScale { get; set; } = 100;
    /// <summary>Recruitment exhausted: set by the recruit pick taking the last candidate, never cleared.</summary>
    public bool RecruitmentDone { get; set; }
}

/// <summary>Wait-list entry: facility and its wait counter when it joined; stale once the counter moved.</summary>
public readonly record struct WaitEntry(Facility Facility, int Counter);

/// <summary>Manufacturing manager kind.</summary>
public enum ManufacturingKind { Ship = 0, Construction = 1, Training = 2 }

/// <summary>
/// Per-system manufacturing manager (MANMGRSD 0xA0 construction, 0xA2 shipyard, 0xA4 training), created at system init; not in
/// the system's contents; counter ids at system creation. Its owner is the system's owner (Inferred).
/// </summary>
public sealed class ManufacturingManager(StarSystem system, ManufacturingKind kind, uint id)
    : GameObject(kind switch { ManufacturingKind.Construction => 0xA0000005u, ManufacturingKind.Ship => 0xA2000006u, _ => 0xA4000007u }, Owner.Neutral, id)
{
    public override StarSystem System { get; } = system;
    public ManufacturingKind Kind { get; } = kind;
    /// <summary>The system's owner; 3 once destroyed.</summary>
    public override Owner Owner { get => Destroyed ? Owner.Neutral : System.Owner; set { } }
    /// <summary>Queue: queued units in key order; the head's type is ProductKey.</summary>
    public List<GameObject> Orders { get; } = [];
    /// <summary>CompletedPointCount, OverflowPointCount.</summary>
    public int CompletedPoints { get; set; }
    public int Overflow { get; set; }
    /// <summary>TargetKey: the manager's own system until order 0x214; reset to it by another routine.</summary>
    public GameObject TargetKey { get; set; } = system;
    /// <summary>
    /// TargetKey of each side's frozen view (index = side) at its last refresh: setup step 24 and the knowledge updates that
    /// refresh managers copy it; null = never refreshed. Live views mirror the real value.
    /// </summary>
    public GameObject?[] ViewTargetKey { get; } = new GameObject?[3];
    /// <summary>
    /// Reserved: toggled by 0x216, cleared by the owner and Existing hooks; read only by the advisor model (no delegated producer here).
    /// </summary>
    public bool Reserved { get; set; }
}

public sealed class Sector(SectorRecord definition)
{
    public SectorRecord Definition { get; } = definition;
    public uint Ref => Definition.Common.Ref;
    public bool IsCore => Definition.Group == 1;
}

public sealed class StarSystem(SystemRecord definition, Sector sector) : GameObject(definition.Common.Ref, Owner.Neutral, definition.Common.Index)
{
    public SystemRecord Definition { get; } = definition;
    public Sector Sector { get; } = sector;
    /// <summary>Core = parent sector Group 1.</summary>
    public bool IsCore => Sector.IsCore;
    /// <summary>Creation position in step 12 (lists A-E, rim, Coruscant, Yavin, HQ).</summary>
    public int InitIndex { get; init; }
    public bool Disloyal { get; set; }
    public bool Populated { get; set; }
    /// <summary>Alliance share of popular support, 0-100; the Empire share is 100 - Support.</summary>
    public int Support { get; set; }
    public int Energy { get; set; }
    public int RawMaterials { get; set; }
    /// <summary>System used energy / raw material, recomputed only by the facilities / Mines checks.</summary>
    public int UsedEnergy { get; set; }
    public int UsedRaw { get; set; }
    /// <summary>Production modifier percent, initial 100.</summary>
    public int ProductionModifier { get; set; } = 100;
    /// <summary>SmugglingPercent (-100...100).</summary>
    public int SmugglingPercent { get; set; }
    /// <summary>Garrisoned (system flag): >= 1 troop directly in the system; drives timer 0x385.</summary>
    public bool Garrisoned { get; set; }
    /// <summary>
    /// Control: owner, support control and military control (<see cref="SystemControl"/>). Inferred: computed by the setup's final
    /// init (step 23), as the run-3 values fit.
    /// </summary>
    public SystemControl Control { get; set; }
    /// <summary>System flag NeverBeenControlled: set by the base init, cleared by the core class init.</summary>
    public bool NeverBeenControlled { get; set; }
    /// <summary>System flags Uprising and Suppressing (set through <see cref="Systems"/>).</summary>
    public bool Uprising { get; set; }
    public bool Suppressing { get; set; }
    /// <summary>System flag DeathStarNearby (<see cref="DeathStar.RecomputeNearby"/>): read by the requirement, support change, smuggling and incident formulas.</summary>
    public bool DeathStarNearby { get; set; }
    /// <summary>System flag CombatUnitFastRepair: the ship manager has >= 1 non-suspended member (<see cref="Repair.SetCombatUnitFastRepair"/>).</summary>
    public bool CombatUnitFastRepair { get; set; }
    /// <summary>Uprising totals (incite, subdue, incident support delta), 0 initially (Inferred).</summary>
    public int InciteTotal { get; set; }
    public int SubdueTotal { get; set; }
    public int IncidentSupportDelta { get; set; }
    /// <summary>Garrison requirement and surplus. Inferred: computed by the setup's final init (step 23).</summary>
    public int GarrisonRequirement { get; set; }
    public int GarrisonSurplus { get; set; }
    /// <summary>System flags BattlePending, Blockade, Battle.</summary>
    public bool BattlePending { get; set; }
    /// <summary>BattleToHost: set when BattlePending turns on, cleared when the battle start walk examines the system.</summary>
    public bool BattleToHost { get; set; }
    public bool Blockade { get; set; }
    public bool Battle { get; set; }
    /// <summary>System flag Explored: set when the system is created, never cleared.</summary>
    public bool Explored { get; set; }
    /// <summary>ControlKind BattleReady: owner of the last fleet / fighter reported Usable directly here; 0 initially.</summary>
    public Owner BattleReady { get; set; }
    /// <summary>BattleWon record and ControlKind BattleWon side.</summary>
    public Owner BattleWon { get; set; }
    public Owner ControlBattleWon { get; set; }
    /// <summary>ControlKind BattleWithdrew: the first side whose fleet / fighter left the BattlePending system.</summary>
    public Owner BattleWithdrew { get; set; }
    /// <summary>Stored blockading side; set when Blockade is set with a blockading side 1 or 2.</summary>
    public Owner Blockader { get; set; }
    /// <summary>
    /// Per-side loss records, index side - 1 (sides 1-3): battle losses (DestroyedReason 0xC), assault
    /// losses (0xE), bombardment losses (0xF).
    /// </summary>
    public int[] BattleLoss { get; } = new int[3];
    public int[] AssaultLoss { get; } = new int[3];
    public int[] BombardLoss { get; } = new int[3];
    /// <summary>Manufacturing managers by kind (created at system init).</summary>
    public ManufacturingManager[] Managers { get; } = new ManufacturingManager[3];

    public int SupportFor(Owner side) => side == Owner.Alliance ? Support : 100 - Support;
}

/// <summary>An open strategy-screen window: window kind and index (the object or dialog the window is for, 0 for the single windows), position, minimise slot (-1 when not minimised; slot Inferred).</summary>
public sealed record SavedWindow(int Kind, int Index, int X, int Y, int Slot);

public sealed class GameState(GameSettings settings, GameDefinitions definitions)
{
    public GameSettings Settings { get; } = settings;
    public GameDefinitions Definitions { get; } = definitions;
    public Params Params { get; } = new(definitions, settings.Column);
    public GameRandom Random { get; } = new(settings.Seed);

    public List<Sector> Sectors { get; } = [];
    /// <summary>All placed systems in registry order: core family (0x90) by Index, then rim family (0x92) by Index.</summary>
    public List<StarSystem> Systems { get; } = [];
    public List<Character> Characters { get; } = [];
    /// <summary>Fleets incl. placeholders in creation order (= registry order: auto ids increase).</summary>
    public List<Fleet> Fleets { get; } = [];
    public StarSystem? HeadquartersSystem { get; set; }
    /// <summary>Processing facilities in creation order (= registry order within a family: auto ids increase).</summary>
    public List<Facility> Facilities { get; } = [];
    /// <summary>Every object with an auto id (units, facilities, fleets, missions), in creation order; destroyed ones stay listed.</summary>
    public List<GameObject> Created { get; } = [];
    /// <summary>Missions (manager 0xF8's contents) in creation order.</summary>
    public List<Mission> Missions { get; } = [];
    /// <summary>Staging container 0xF2000003: destroyed objects end here by their final move 0x372.</summary>
    public GameObject Staging { get; } = new(0xF2000003, Owner.Neutral, 3);
    /// <summary>
    /// Manager 0xF8 (fixed id 6): the container missions are created in; Created, Completed, Existing and Usable from setup step 9
    /// </summary>
    public GameObject MissionManager { get; } = new(0xF8000006, Owner.Neutral, 6);
    /// <summary>
    /// Manager 0xFA flag "Emperor at Coruscant": Palpatine 0x34000280 is Usable, not Captured, his parent
    /// is Coruscant itself, and Coruscant's owner is 2. Empire characters get LeadershipAdjustment G 0xC04 while it is set.
    /// Evaluated on read (the original recomputes it in manager 0xFA hooks).
    /// </summary>
    public bool EmperorAtCoruscant => Characters.FirstOrDefault(c => c.Type == 0x34000280) is { Usable: true, Captured: false } p
        && p.Container is StarSystem { Type: Refs.Coruscant, Owner: Owner.Empire };
    /// <summary>Sector-wave gate: tick of the counters and per-side counts.</summary>
    public int WaveTick { get; set; }
    public Dictionary<Owner, int> WaveCounts { get; } = [];
    public Dictionary<Owner, SideState> Sides { get; } = new() { [Owner.Alliance] = new(0xF300000F), [Owner.Empire] = new(0xF3000010) };
    public Clock Clock { get; } = new();
    /// <summary>Timer slots by (type, target reference).</summary>
    public Dictionary<(int Type, uint Target), TimerSlot> Timers { get; } = [];
    /// <summary>Clock day; setup runs on day 1.</summary>
    public int Day => Clock.Day;
    /// <summary>"battle management required" flag, set by 0x304 (BattlePending set); read by host stage 13.</summary>
    public bool BattleRequired { get; set; }
    /// <summary>The system picked by the battle start walk while a controller has not answered (host state 6; not saved: a save is refused then).</summary>
    public StarSystem? HostedBattle { get; internal set; }
    /// <summary>
    /// Space battle results (replay input): asked for each battle start; null = no recorded result, the battle is run by
    /// the automatic engine or, when a side took command, as a tactical battle.
    /// </summary>
    public Func<StarSystem, BattleResult?>? BattleResults { get; set; }
    /// <summary>
    /// The running tactical battle (host states 7 / 0x14) while the host waits for it with
    /// <see cref="HostedBattle"/> set; the front end runs its turns until <see cref="TacticalBattle.Done"/>. Not saved (a save is refused then).
    /// </summary>
    public TacticalBattle? Tactical { get; internal set; }
    /// <summary>The tactical battle's units for its report (taken at the battle start).</summary>
    internal List<GameObject>? TacticalUnits { get; set; }
    /// <summary>Game-ready flag = (init step = 0x1A), set after step 25; no notification is created before.</summary>
    public bool Ready { get; set; }
    /// <summary>Optional log of observable state events (differential tests, <see cref="Fact"/>); no behaviour change.</summary>
    public List<Fact>? FactTrace { get; set; }
    /// <summary>Test hook: a lock decrement that hit the floor at 0 (a lost lock). Not saved.</summary>
    public Action<GameObject>? LockFloor { get; set; }
    /// <summary>When set, every hosted space battle appends its report (<see cref="Messages"/>, battle windows; not saved).</summary>
    public List<BattleReport>? BattleTrace { get; set; }
    /// <summary>When set, every bombardment / assault appends its record (<see cref="Messages"/> type 0xF; not saved).</summary>
    public List<StrikeReport>? StrikeTrace { get; set; }
    /// <summary>When set, every InformantIncident pulse appends its record (<see cref="Messages"/> type 0x18; not saved).</summary>
    public List<InformantReport>? InformantTrace { get; set; }
    /// <summary>When set, every galaxy event 0x38F / 0x390 that changes the system appends its record (<see cref="Messages"/> type 0x20; not saved).</summary>
    public List<ResourceReport>? ResourceTrace { get; set; }
    /// <summary>When set, every story / Force UI notification appends its record (<see cref="Messages"/> types 0x1B-0x29; not saved).</summary>
    public List<StoryEvent>? StoryTrace { get; set; }
    /// <summary>The context of the running duel pairing / resolution (kind-4 groups of type 0x24); 0 outside one. Not saved.</summary>
    internal int StoryGroup { get; set; }
    int _storyGroups;
    internal int NextStoryGroup() => ++_storyGroups;
    /// <summary>
    /// Posts a story / Force UI notification (only after game-ready): <paramref name="live"/> = the sides whose
    /// views post it (default: the key's live views at the post); key none = a manager (0xF8 / 0xFA). No sim effect.
    /// </summary>
    internal void StoryPost(int kind, GameObject? key, long old, long now, GameObject? other = null, SideSet? live = null)
    {
        if (StoryTrace is null || !Ready) return;
        StoryTrace.Add(new(Day, kind, key, live ?? Combat.LiveSides(key!), old, now, other, StoryGroup));
    }
    /// <summary>Open strike: every object destroyed meanwhile with its live views and Completed at the destroy.</summary>
    internal List<(GameObject Unit, SideSet Live, bool Completed)>? StrikeDestroys { get; set; }
    internal void Record(int kind, uint obj, long old, long now) => FactTrace?.Add(new(Day, kind, obj, old, now));
    /// <summary>
    /// Side controllers by side (side 1 first; <see cref="Ai.Controller"/>): human or computer. Empty = no controller stages (tests,
    /// command replays inject the recorded commands instead).
    /// </summary>
    public SortedDictionary<Owner, Ai.Controller> Controllers { get; } = [];
    /// <summary>
    /// Controller messages: their own type space, posted by side views on a field change and routed by side; queued
    /// during the tick and delivered in stage 6 of the same tick (<see cref="Ai.Controller.Deliver"/>). Only sides with a
    /// controller and a live view of the object get one (a frozen view does not change). shortcut: not saved (a save taken
    /// after a mid-day UI order before the next tick drops them); save them when saves can happen inside a tick.
    /// </summary>
    internal List<(Owner Side, int Type, GameObject Object, GameObject? A, GameObject? B)> Messages { get; } = [];

    /// <summary>Posts a view message to every side with a controller whose view of the object is live (only while the game runs).</summary>
    internal void ViewMessage(int type, GameObject o, GameObject? a = null, GameObject? b = null)
    {
        if (Controllers.Count == 0 || !Ready) return;
        foreach (var side in Controllers.Keys)
            if (Knowledge.IsLive(o, side)) Messages.Add((side, type, o, a, b));
    }

    /// <summary>
    /// A refresh of a frozen view (it became live) posts one message per field that differs from the view:
    /// parent 0x300, owner 0x100, Usable 0x101, Created 0x102, Completed 0x103, Destroyed 0x302 (with a parent), Enroute 0x104, Damaged 0x106, system
    /// fields 0x140 / 0x149 / 0x14A / 0x14B / 0x14C (NeverBeenControlled) / 0x14D (Battle) / 0x14E (Blockade), manager queue count 0x160, mission TaskStatus 0x1F0 /
    /// CompletionStatus 0x1F1. Only the fields the views keep are compared; base order
    /// verified, class-field order Inferred.
    /// </summary>
    internal void ViewRefreshed(GameObject o, Owner side)
    {
        if (!Controllers.ContainsKey(side) || !Ready) return;
        Views.TryGetValue((o, side), out var v); // none = a fresh view: owner 3, no parent, no flags set
        void Post(bool differs, int type, GameObject? a = null, GameObject? b = null) { if (differs) Messages.Add((side, type, o, a, b)); }
        // copy order: parent, owner, flags, Damaged, class fields
        // a fresh view sits in the side's staging view: its first real parent posts old = 0xF2000003;
        // roots without a real container (systems) keep none (Inferred, as Knowledge.InStaging)
        var oldParent = v?.Parent ?? (o.Container is null ? null : Staging);
        Post(o.Container != oldParent, 0x300, o.Container, oldParent);
        Post(o.Owner != (v?.Owner ?? Owner.Neutral), 0x100);
        Post(o.Usable != (v?.Usable ?? false), 0x101);
        Post(o.Created != (v?.Created ?? false), 0x102);
        Post(o.Completed != (v?.Completed ?? false), 0x103);
        Post(o.Destroyed != (v?.Destroyed ?? false) && o.Container is not null, 0x302, o.Container);
        Post(o.Enroute != (v?.Enroute ?? false), 0x104);
        Post(o.Damaged != (v?.Damaged ?? false), 0x106);
        if (o is StarSystem s)
        {
            Post(s.Support != (v?.Support ?? 0), 0x140);
            Post(s.Populated != (v?.Populated ?? false), 0x149);
            Post(s.Explored != (v?.Explored ?? false), 0x14A);
            Post(s.Uprising != (v?.Uprising ?? false), 0x14B);
            // NeverBeenControlled copy between the Uprising and Battle copies; A = the system when the new value is set
            Post(s.NeverBeenControlled != (v?.NeverBeenControlled ?? false), 0x14C, s.NeverBeenControlled ? s : null);
            Post(s.Battle != (v?.Battle ?? false), 0x14D); // full refresh
            Post(s.Blockade != (v?.Blockade ?? false), 0x14E);
        }
        if (o is ManufacturingManager m) Post(m.Orders.Count != (v?.QueueCount ?? 0), 0x160); // queue count of the manager view
        if (o is Mission ms) // mission refresh: TaskStatus before CompletionStatus
        {
            Post(ms.TaskStatus != (v?.TaskStatus ?? 0), 0x1F0);
            Post(ms.CompletionStatus != (v?.CompletionStatus ?? 0), 0x1F1);
        }
    }
    /// <summary>The player's message store (built by <see cref="Messages"/>; UI only; saved).</summary>
    public List<GameMessage> Log { get; } = [];
    /// <summary>The strategy screen's map filter mode (UI only; saved, re-applied on load).</summary>
    public int FilterMode { get; set; } = MapFilter.Off;
    /// <summary>
    /// The strategy screen's open windows (window count, then one descriptor per window); UI only, filled by
    /// the screen before a save, re-opened by it after a load. Stacking order bottom -> top, then the minimised ones in minimise order.
    /// </summary>
    public List<SavedWindow> Windows { get; } = [];
    /// <summary>Manager F9 distance lists, per system: the systems by ascending distance in CRT qsort order.</summary>
    public Dictionary<StarSystem, IReadOnlyList<StarSystem>> DistanceLists { get; } = [];
    /// <summary>Abodes used as mission targets (jails, story abodes), created on first use (<see cref="Abode"/>).</summary>
    public Dictionary<uint, GameObject> Abodes { get; } = [];

    /// <summary>
    /// An abode by SdRef with its step-9 owner (Alliance Jail 1; Dagobah, Hermitage 3; Empire Jail, Jabba's Palace 2), created on
    /// first use. Abodes are direct contents of the game object (no system, no coordinates; not modelled as a container here);
    /// their location is the abode itself (<see cref="Missions.Location"/>), legs to or from one take 1 day.
    /// </summary>
    public GameObject Abode(uint sdRef)
    {
        if (!Abodes.TryGetValue(sdRef, out var abode))
            Abodes[sdRef] = abode = new GameObject(sdRef, sdRef switch { 0x98000440 => Owner.Alliance, 0x98000441 or 0x98000442 => Owner.Neutral, _ => Owner.Empire }, SdRef.IndexOf(sdRef));
        return abode;
    }

    /// <summary>Frozen side views (snapshots) by (object, side).</summary>
    public Dictionary<(GameObject, Owner), View> Views { get; } = [];

    /// <summary>Victory state (<see cref="Victory"/>).</summary>
    public VictoryStatus VictoryState { get; set; }
    /// <summary>Game over (the victory state's GameOver): the clock stops, commands are refused.</summary>
    public bool GameOver => VictoryState.GameOver;
    /// <summary>The winner once the game is over, else none.</summary>
    public Owner Winner => VictoryState.AllianceWins ? Owner.Alliance : VictoryState.EmpireWins ? Owner.Empire : Owner.None;

    /// <summary>Manager 0xF8 story flags (<see cref="Story"/>).</summary>
    public StoryFlags StoryFlags { get; set; } = new();

    uint _nextId = 0x482;
    /// <summary>
    /// Auto id for objects not keyed by SD Index: one global counter in creation order, 0x482 after the step-9 abodes (Index up
    /// to 0x481).
    /// </summary>
    public uint NextId() => _nextId++;
    /// <summary>The id counter itself (save games).</summary>
    internal uint IdCounter { get => _nextId; set => _nextId = value; }
    /// <summary>Ids consumed by objects the sim does not model.</summary>
    public void SkipIds(int count) => _nextId += (uint)count;

    /// <summary>Lists an object with an auto id (and a processing facility in <see cref="Facilities"/>).</summary>
    public T Register<T>(T o) where T : GameObject
    {
        Created.Add(o);
        if (o is Facility f) Facilities.Add(f);
        if (o is Fleet fleet) Fleets.Add(fleet);
        return o;
    }

    public StarSystem System(uint sdRef) => Systems.First(s => s.Type == sdRef);

    public IEnumerable<StarSystem> SystemsOf(Owner side) => Systems.Where(s => s.Owner == side);
    public IEnumerable<Fleet> FleetsOf(Owner side) => Fleets.Where(f => f.Owner == side);

    /// <summary>MaintenanceCost of a unit or facility definition; 0 for anything else.</summary>
    public int MaintenanceCost(uint sdRef) =>
        Definitions.Resolve(sdRef) is { } r && r.Has(SdField.MaintenanceCost) && Family.Of(sdRef) is >= 0x10 and < 0x40 ? r.Get(SdField.MaintenanceCost) : 0;

    /// <summary>RefinedMaterialCost of a unit or facility definition; 0 for anything else.</summary>
    public int RefinedMaterialCost(uint sdRef) =>
        Definitions.Resolve(sdRef) is { } r && r.Has(SdField.MaintenanceCost) && Family.Of(sdRef) is >= 0x10 and < 0x40 ? r.Get(SdField.RefinedMaterialCost) : 0;
}
