using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>A new ship record: the catalogue class, free; dirty.</summary>
    AiShip NewShip(GameObject o)
    {
        var c = P(o.Type);
        return new(o)
        {
            Key = (int)o.Ref, Side = (int)Side, Free = true,
            Armed = c.Armed, AntiFighter = c.AntiFighter, TroopCarrier = c.TroopCarrier, ArmedTroopCarrier = c.ArmedTroopCarrier, SmallHull = c.SmallHull,
            MediumHull = c.MediumHull, LargeHull = c.LargeHull, DeathStar = c.DeathStar, Interdictor = c.Interdictor,
        };
    }

    /// <summary>
    /// Ship record update: keep the kept flags, zero the counters; from the view: own, Enroute, not
    /// w / w2 = weapon recharge / second recharge, each minus its reduction.
    /// shortcut: the reductions are not modelled (0). Damaged moving / stopped from the side's view:
    /// Damaged with the view's speed != 0 / = 0 (a frozen view keeps the values of its last copy).
    /// </summary>
    void UpdateShip(AiShip r)
    {
        r.Dirty = false;
        r.ClearUnkept();
        r.ClearCounters();
        if (!HasView(r.Ship)) return;
        var v = See(r.Ship);
        if (v.Destroyed) return;
        if (v.Owner == Side) r.Own = true;
        if (v.Enroute) r.Enroute = true;
        if (!v.Completed) (r.Named, r.NotCompleted) = (false, true);
        r.Fleet = ViewParent(r.Ship) as Fleet;
        // Damaged with the view's speed != 0 / = 0
        var (damaged, speed) = Knowledge.Damage(_game, r.Ship, Side);
        if (damaged && speed != 0) r.DamagedMoving = true;
        else if (damaged) r.DamagedStopped = true;
        var q = P(r.Ship.Type);
        int w = q.WeaponRechargeRate, w2 = q.ShieldRechargeRate;
        r.Hull = q.Hull;
        r.Bombardment = r.DamagedMoving ? 0 : q.Bombardment;
        if (q.ShieldRechargeRate != 0) r.ShieldRecharge = q.Shields * w2 / q.ShieldRechargeRate;
        r.FighterCapacity = q.FighterCapacity;
        r.TroopCapacity = q.TroopCapacity;
        r.Maintenance = q.MaintenanceCost;
        if (q.WeaponRechargeRate != 0)
        {
            r.Firepower = r.FirepowerAllFighters = q.Firepower * w / q.WeaponRechargeRate;
            if (r.AntiFighter) r.AntiFighterFirepower = r.AntiFighterFirepowerAllFighters = q.AntiFighterFirepower * w / q.WeaponRechargeRate;
        }
        r.Units = 1;
        foreach (var x in Contents(r.Ship))
        {
            var s = See(x);
            bool usable = s.Completed && !s.EnrouteActive; // EnrouteActive, not Enroute
            if (s.EnrouteActive) r.AboardEnroute = true;
            uint f = Family.Of(x.Type);
            if (f is >= Families.Fighter and < Families.Headquarters)
            {
                var p = P(x.Type);
                r.HasFighters = true;
                r.Fighters++;
                r.FighterMaintenance += p.MaintenanceCost;
                if (!s.Completed) r.AboardNotReady = true;
                int squad = p.SquadronSize, left = squad - x.SquadSizeDamage;
                int Damaged(int strength) => squad > 0 ? strength * left / squad : 0;
                if (s.Completed)
                {
                    // fighters count whatever the ship's AntiFighter bit
                    r.FirepowerAllFighters += Damaged(p.Firepower);
                    r.AntiFighterFirepowerAllFighters += Damaged(p.AntiFighterFirepower);
                }
                if (!usable) continue;
                r.UsableFighters++;
                r.Units++;
                r.Bombardment += p.Bombardment;
                r.Firepower += Damaged(p.Firepower);
                r.AntiFighterFirepower += Damaged(p.AntiFighterFirepower);
                r.FighterShields += Damaged(p.Shields);
            }
            else if (Family.IsTroop(x.Type))
            {
                r.HasTroops = true;
                r.Troops++;
                r.TroopMaintenance += P(x.Type).MaintenanceCost;
                if (!s.Completed || s.EnrouteActive) r.AboardNotReady = true;
                if (!usable) continue;
                r.UsableTroops++;
                r.Units++;
                r.TroopAttack += P(x.Type).Firepower;
            }
            else if (f is >= Families.MajorCharacter and < Families.SpecialForce && Agents.TryGetValue(x.Ref, out var a))
            {
                bool own = a.Own, captured = a.Captured;
                if (own && captured) r.OwnCaptured = true;
                else if (own) r.OwnCharacter = true;
                else if (captured) r.EnemyCaptured = true;
                else if (!s.Enroute)
                {
                    if (a.Major) r.EnemyMajor = true;
                    else r.EnemyMinor = true;
                    if (a.VictoryCharacter && _game.Settings.VictoryFlag != 1) r.VictoryCharacters++;
                }
                if (!own && a.HasCommandKind) r.EnemyCommander = true;
                if (a.CommandKind3) r.CommandKind3 = true;
            }
        }
    }

    /// <summary>
    /// Fleet record update: keep the kept flags, zero the sums; with a live view: own (the
    /// owner at creation), Enroute, location, the flags and sums over its ship records.
    /// </summary>
    void UpdateFleet(AiFleet r)
    {
        r.ClearUnkept();
        r.ClearSums();
        if (!HasView(r.Fleet) || See(r.Fleet).Destroyed) return;
        if (r.Owner == (int)Side) r.Own = true;
        if (See(r.Fleet).Enroute) r.Enroute = true;
        r.Location = Where(r.Fleet);
        foreach (var s in ShipsOf(r))
        {
            var b = s;
            bool completed = !b.NotCompleted;
            bool ready = !(b.NotCompleted || b.Enroute);
            bool undamaged = !(b.DamagedStopped || b.DamagedMoving);
            r.Ships++;
            if (completed)
            {
                r.Hull += s.Hull;
                r.Firepower += s.Firepower;
                r.AntiFighterFirepower += s.AntiFighterFirepower;
                r.ShieldRecharge += s.ShieldRecharge;
                r.FighterShields += s.FighterShields;
                r.Bombardment += s.Bombardment;
                r.TroopAttack += s.TroopAttack;
                r.Fighters += s.Fighters;
                r.UsableFighters += s.UsableFighters;
                r.FighterCapacity += s.FighterCapacity;
                r.Troops += s.Troops;
                r.UsableTroops += s.UsableTroops;
                r.TroopCapacity += s.TroopCapacity;
                r.Units += s.Units;
                r.FirepowerAllFighters += s.FirepowerAllFighters;
                r.AntiFighterFirepowerAllFighters += s.AntiFighterFirepowerAllFighters;
            }
            r.Maintenance += s.Maintenance;
            r.TroopMaintenance += s.TroopMaintenance;
            r.FighterMaintenance += s.FighterMaintenance;
            if (!b.Named) r.Unnamed++;
            if (undamaged)
            {
                if (b.Armed) r.Armed++;
                if (b.AntiFighter) r.AntiFighter++;
                if (b.TroopCarrier) r.TroopCarriers++;
            }
            if (b.EnemyMajor) r.VictoryCharacters += s.VictoryCharacters;
            if (b.TroopCarrier) r.CarrierTroopCapacity += s.TroopCapacity;
            if (ready)
            {
                r.TroopRoom += s.TroopCapacity - s.Troops;
                r.FighterRoom += s.FighterCapacity - s.Fighters;
            }
            if (b.DeathStar) r.DeathStar = true;
            if (b.Interdictor) r.Interdictor = true;
            if (b.AboardNotReady) r.AboardNotReady = true;
            if (b.AboardEnroute) r.AboardEnroute = true;
            if (b.NotCompleted) r.NotCompletedShip = true;
            if (b.Enroute) r.EnrouteShip = true;
            if (b.DamagedMoving) r.DamagedMoving = true;
            if (b.DamagedStopped) r.DamagedStopped = true;
            if (b.HasFighters) r.HasFighters = true;
            if (b.HasTroops) r.HasTroops = true;
            if (b.EnemyMajor) r.EnemyMajor = true;
            if (b.EnemyMinor) r.EnemyMinor = true;
            if (b.EnemyCaptured) r.EnemyCaptured = true;
            if (b.OwnCaptured) r.OwnCaptured = true;
            if (b.EnemyCommander) r.EnemyCommander = true;
            if (b.OwnCharacter) r.OwnCharacter = true;
            if (b.CommandKind3) r.CommandKind3 = true;
            if (ready) r.ReadyShip = true;
            if (undamaged) r.UndamagedShip = true;
        }
        if (r.TroopRoom > 0) r.HasTroopRoom = true;
        if (r.FighterRoom > 0) r.HasFighterRoom = true;
    }

    /// <summary>The fleet record's ship records (the capital ships in the fleet's view contents with a record, key order).</summary>
    public IEnumerable<AiShip> ShipsOf(AiFleet f) => Contents(f.Fleet).Where(x => Ships.ContainsKey(x.Ref)).Select(x => Ships[x.Ref]);

    /// <summary>The fleet records in key order.</summary>
    public List<AiFleet> FleetRecords() => [.. FleetsById.Values];

    /// <summary>
    /// Fleet aggregates: own fleets' sums, the Death Star and interdictor flags, free fleets' sums, and other fleets' sums.
    /// </summary>
    void FleetAggregates()
    {
        // zero every fleet aggregate
        (ShipMaintenance, FleetTroopMaintenance, FleetFighterMaintenance, FleetFirepower, FleetTroops, CarrierTroopCapacity, UnnamedShips) = (0, 0, 0, 0, 0, 0, 0);
        (FreeArmed, FreeAntiFighter, FreeTroopCarriers, FreeFirepower, FreeAntiFighterFirepower, FreeFighterCapacity, FreeBombardment) = (0, 0, 0, 0, 0, 0, 0);
        (OtherFleets, OtherFirepower, OtherFighterCapacity, OtherAntiFighterFirepower, StrongestFirepower, StrongestAntiFighterFirepower, StrongestFighterCapacity) = (0, 0, 0, 0, 0, 0, 0);
        OwnDeathStar = OwnInterdictor = false;
        AiFleet? strongest = null;
        foreach (var f in FleetsById.Values)
        {
            if (f.Own)
            {
                ShipMaintenance += f.Maintenance;
                FleetTroopMaintenance += f.TroopMaintenance;
                FleetFighterMaintenance += f.FighterMaintenance;
                FleetFirepower += f.FirepowerAllFighters;
                FleetTroops += f.Troops;
                CarrierTroopCapacity += f.CarrierTroopCapacity;
                UnnamedShips += f.Unnamed;
                if (f.DeathStar) OwnDeathStar = true;
                if (f.Interdictor) OwnInterdictor = true;
                if (f.Free)
                {
                    FreeArmed += f.Armed;
                    FreeAntiFighter += f.AntiFighter;
                    FreeTroopCarriers += f.TroopCarriers;
                    FreeFirepower += f.FirepowerAllFighters;
                    FreeAntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
                    FreeFighterCapacity += f.FighterCapacity;
                    FreeBombardment += f.Bombardment;
                }
            }
            else if (f.Ships > 0)
            {
                OtherFleets++;
                OtherFirepower += f.FirepowerAllFighters;
                OtherFighterCapacity += f.FighterCapacity;
                OtherAntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
                if (strongest is null || f.FirepowerAllFighters > strongest.FirepowerAllFighters) strongest = f;
            }
        }
        if (strongest is not null) (StrongestFirepower, StrongestAntiFighterFirepower, StrongestFighterCapacity) = (strongest.FirepowerAllFighters, strongest.AntiFighterFirepowerAllFighters, strongest.FighterCapacity);
    }
}
