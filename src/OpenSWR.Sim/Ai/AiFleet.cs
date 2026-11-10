namespace OpenSWR.Sim.Ai;

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
    /// <summary>Completed ships' <see cref="AiShip.Firepower"/>. Meaning in the original is unknown.</summary>
    public int Firepower { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.AntiFighterFirepower"/>. Meaning in the original is unknown.</summary>
    public int AntiFighterFirepower { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.ShieldRecharge"/>. Meaning in the original is unknown.</summary>
    public int ShieldRecharge { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.FighterShields"/>. Meaning in the original is unknown.</summary>
    public int FighterShields { get; set; }
    /// <summary>Completed ships' <see cref="AiShip.Bombardment"/>. Meaning in the original is unknown.</summary>
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
