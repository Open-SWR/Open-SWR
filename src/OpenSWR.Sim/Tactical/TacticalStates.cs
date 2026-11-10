namespace OpenSWR.Sim;

/// <summary>
/// State of an engine unit (<see cref="BattleEngine.Unit.State"/>). Names from the original's behaviour.
/// </summary>
public enum TacticalUnitState
{
    /// <summary>No state set (the original's "none").</summary>
    None,
    /// <summary>Docked in a hangar, hidden, not yet launched (also a pending fighter of the automatic battle).</summary>
    Docked,
    /// <summary>In the fight: fires, moves and is a target.</summary>
    Active,
    /// <summary>Launching. No writer found in the original.</summary>
    Launching,
    /// <summary>Recovering. No writer found in the original.</summary>
    Recovering,
    /// <summary>Recovered: landed in its carrier's hangar (a fighter docked after a recover mission).</summary>
    Recovered,
    /// <summary>Jump ordered: at the edge of the battlefield, waiting to accelerate into hyperspace.</summary>
    JumpOrdered,
    /// <summary>Accelerating to the jump (about 4000 ms, the jump-out effect and sound).</summary>
    Accelerating,
    /// <summary>In hyperspace: hidden, moving out of the battle.</summary>
    InHyperspace,
    /// <summary>Withdrawn: jumped out of the battle (counts for the side's result as retreated).</summary>
    Withdrawn,
    /// <summary>Member of a Death Star run: hidden, not active, waiting for the run's result.</summary>
    DeathStarRun,
    /// <summary>Destroyed.</summary>
    Destroyed,
}

/// <summary>
/// State of an engine side (<see cref="BattleEngine.Side.State"/>). Names from the original's behaviour and
/// the automatic battle. The state trace prints a code per state (<c>BattleEngine.TraceSideCode</c>).
/// </summary>
public enum TacticalSideState
{
    /// <summary>Fighting: no withdraw ordered.</summary>
    Fighting,
    /// <summary>Withdraw ordered: the countdown to the jump is running.</summary>
    WithdrawOrdered,
    /// <summary>Jumping: waits for the units that can jump to leave the battle.</summary>
    Jumping,
    /// <summary>Jump finished: every unit that cannot jump has been scuttled.</summary>
    JumpFinished,
    /// <summary>Withdrawn: the automatic battle's withdrawn state; the tactical battle reads it the same way.</summary>
    Withdrawn,
}

/// <summary>Kind of an engine unit (<see cref="BattleEngine.Unit.Type"/>): capital ship, fighter, or the Death Star scene.</summary>
public enum TacticalUnitType
{
    CapitalShip,
    Fighter,
    /// <summary>A Death Star scene: in no side list, the carrier key of the fighters aboard it.</summary>
    DeathStarScene,
}

/// <summary>Status of a tactical mission object (<see cref="TacticalMission.Status"/>).</summary>
public enum TacticalMissionStatus
{
    /// <summary>Not started and not done.</summary>
    NotStarted,
    /// <summary>Started (the mission's init has run).</summary>
    Started,
    /// <summary>Done.</summary>
    Done,
}

/// <summary>Recovery sub-state of a fighter on a recover mission (<see cref="TacticalUnit.Recovery"/>).</summary>
public enum TacticalRecovery
{
    None,
    /// <summary>A hangar place is reserved (the carrier-assignment message is posted).</summary>
    PlaceReserved,
    /// <summary>A carrier is assigned (handler for the carrier-assignment message).</summary>
    CarrierAssigned,
    /// <summary>Docking: the fighter-dock message is posted.</summary>
    Docking,
    /// <summary>Docked.</summary>
    Docked,
}

/// <summary>Weapon of a hit or an effect (<see cref="TacticalEffect.Weapon"/>): the shot's weapon, superlaser for the Death Star and scuttles.</summary>
public enum TacticalWeapon
{
    Laser,
    Turbolaser,
    Ion,
    Torpedo,
    /// <summary>The superlaser, or a scuttle (the death effect of a unit that cannot jump).</summary>
    Superlaser,
}

/// <summary>Kind of an effect request (<see cref="TacticalEffect.Kind"/>): a spark, a hull explosion or a death.</summary>
public enum TacticalEffectKind
{
    Spark,
    HullExplosion,
    Death,
}

/// <summary>A critical hit or a damage-control counter (the automatic battle's critical kinds k = 0 to 4).</summary>
public enum CriticalKind
{
    Shield,
    Weapon,
    Tractor,
    Speed,
    /// <summary>Hyperdrive (the last kind; its counter is capped at the drive count, not at 4).</summary>
    Hyperdrive,
}

/// <summary>Tactic of a group, the acceleration mode of its range object (surround or stand off).</summary>
public enum TacticalTactic
{
    Surround,
    StandOff,
}

/// <summary>Formation of a group's navigator (<see cref="TacticalGroup.Formation"/>): none, sphere, line, or fighter.</summary>
public enum TacticalFormation
{
    None,
    Sphere,
    Line,
    Fighter,
}

/// <summary>
/// Step of the Death Star hint machine (<see cref="TacticalBattle.HintState"/>): the opening lines come first (battle start,
/// Death Star present, Death Star shielded, too few fighters), then Monitor (attack-possible changes) and Waiting (the timer before the monitor starts).
/// </summary>
public enum TacticalHintStep
{
    /// <summary>Opening line: battle start (Alliance) or the Empire's battle start.</summary>
    Opening,
    /// <summary>Opening line: a Death Star is present (Alliance only).</summary>
    DeathStarPresent,
    /// <summary>Opening line: the Death Star is shielded.</summary>
    DeathStarShielded,
    /// <summary>Opening line: too few fighters for a run.</summary>
    TooFewFighters,
    /// <summary>Monitor: attack-possible changes give their voices.</summary>
    Monitor,
    /// <summary>Waiting: the opening timer runs, the monitor starts after it.</summary>
    Waiting,
}
