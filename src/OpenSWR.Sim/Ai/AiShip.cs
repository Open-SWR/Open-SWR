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
    /// <summary>An armed ship with troop capacity. Meaning in the original is unknown.</summary>
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
    /// <summary>The fleet op's flagship (kept). Meaning in the original is unknown.</summary>
    public bool FlagshipMark { get; set; }
    /// <summary>A balance builder wanted the flagship for another place, so the flagship is chosen again (kept). Meaning in the original is unknown.</summary>
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
    /// <summary>The ship's bombardment value, unless <see cref="DamagedMoving"/>, plus the usable fighters' bombardment values. Meaning in the original is unknown.</summary>
    public int Bombardment { get; set; }
    /// <summary>Sum of the usable troops' AttackStrength.</summary>
    public int TroopAttack { get; set; }
    /// <summary>The ship's stat scaled by the second weapon value, div another stat. Meaning in the original is unknown.</summary>
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
    /// <summary>The ship's stat scaled by the weapon value, plus the usable fighters' damaged stat. Meaning in the original is unknown.</summary>
    public int Firepower { get; set; }
    /// <summary>Class 2 only: the second ship stat scaled by the weapon value, plus the fighters' damaged second stat. Meaning in the original is unknown.</summary>
    public int AntiFighterFirepower { get; set; }
    /// <summary>The fighters' damaged third stat. Meaning in the original is unknown.</summary>
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
