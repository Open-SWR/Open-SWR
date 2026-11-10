namespace OpenSWR.Sim;

/// <summary>
/// Kinds of the stamped tactical messages (<see cref="TacticalBattle"/> queue). One kind space for all handlers: the
/// mission, formation and mode kinds are posted by <c>TacticalOrders</c>, <c>TacticalMovement</c> and <c>TacticalDeathStar</c>
/// with the same queue. The kind names are the handlers' uses.
/// </summary>
public enum TacticalMessage
{
    /// <summary>Superlaser hit of a Death Star scene: the target is destroyed (stamped one turn after the fire).</summary>
    SuperlaserHit,
    /// <summary>Mission message (group, mission number, local); see <c>TacticalAi.MissionMessage</c>.</summary>
    SetMission,
    /// <summary>Shot displays (bolts and sounds) of the four weapons.</summary>
    LaserShot, TurbolaserShot, IonShot, TorpedoShot,
    /// <summary>Hits (damage on the target) of the four weapons, stamped one turn after their shot display.</summary>
    LaserHit, IonHit, TurbolaserHit, TorpedoHit,
    /// <summary>Shield overflow on a capital ship: engine-down and tractor-down time, arcs reset. Meaning in the original is unknown beyond this.</summary>
    ShieldOverflow,
    /// <summary>Fighter launch (launch point, carrier set) and the docking of a recovered fighter.</summary>
    FighterLaunch, FighterDock,
    /// <summary>Critical hit of each critical kind (the automatic battle's critical hit), one kind per counter.</summary>
    CriticalHitShield, CriticalHitWeapon, CriticalHitTractor, CriticalHitSpeed, CriticalHitHyperdrive,
    /// <summary>Damage-control repairs of one counter (shield, weapon, tractor, speed, hyperdrive).</summary>
    RepairShield, RepairWeapon, RepairTractor, RepairSpeed, RepairHyperdrive,
    /// <summary>Tractor beam: the holder takes (hold) or lets go of (release) its target.</summary>
    TractorHold, TractorRelease,
    /// <summary>Carrier assignment of a recovering fighter (carrier set, or cleared with no carrier).</summary>
    CarrierAssignment,
    /// <summary>Jump start: an active unit that can jump enters hyperspace, one that cannot is scuttled (see <c>Scuttle</c>).</summary>
    JumpStart,
    /// <summary>After the hyperspace timer: the unit leaves the battle (state 9).</summary>
    Retreat,
    /// <summary>Scuttle: an active unit with hull above 0 is destroyed (it cannot jump when its side withdraws).</summary>
    Scuttle,
    /// <summary>Task force of a new group (create), join and leave, and the deletion of an empty group.</summary>
    TaskForceCreate, TaskForceJoin, TaskForceLeave, GroupDelete,
    /// <summary>Target and nav orders: add target / remove target / replace targets, add / remove / replace nav points.</summary>
    AddTarget, RemoveTarget, ReplaceTargets, AddNavPoint, RemoveNavPoint, ReplaceNavPoints,
    /// <summary>Order kinds: set the nav point, set formation, set acceleration mode.</summary>
    SetNavPoint, SetFormation, SetMode,
    /// <summary>Death Star run-state message: a run state request for a group (the state is the argument).</summary>
    RunState,
    /// <summary>Superlaser-fire message: a superlaser fire (the beam, and the target's destruction queued behind it).</summary>
    SuperlaserFire,
    /// <summary>Scene-withdraw message: the Death Star scene withdraws (the Empire's jump start).</summary>
    SceneWithdraw,
}
