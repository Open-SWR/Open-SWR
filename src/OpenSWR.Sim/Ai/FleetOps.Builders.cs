namespace OpenSWR.Sim.Ai;

public static partial class FleetOps
{
    // ---- task slot and the gather system ----

    /// <summary>
    /// Task slot per type: the op always gets its first mark, a second mark when there is a theater, and a third when the
    /// type's goal holds (Inferred meaning). Type 2's goal: a theater flag test, else a pick that is not in the task's visited
    /// set, else the theater's flag is set.
    /// </summary>
    static void TaskStatus(WorldModel m, FleetOp op, FleetTask task)
    {
        op.CharacterWaiting = true;
        if (op.Theater is not { } s) return;
        op.TheaterSet = true;
        bool goal;
        switch (task.Type)
        {
            case FleetTaskType.Strike: goal = s.EnemyAssets; break;
            case FleetTaskType.Scout:
                goal = s.EnemyFleet;
                if (!goal)
                {
                    goal = Unvisited(m, task, s) is not null; // A pick not in the task's visited set.
                    if (!goal) s.NoUnvisited = true;
                }
                break;
            case FleetTaskType.Reserve: goal = s.Own; break;
            case FleetTaskType.Site: goal = m.Record(task.Site) is { } site && !site.Own; break;
            case FleetTaskType.Defence: goal = !s.AllHeld || s.EnemyNoBatteries; break;
            default: goal = s.EnemyNoBatteries && s.EnemyFewGenCores && (s.EnemyNoCommandKind3 || op.GeneralAtShip); break;
        }
        if (goal) op.Goal = true;
    }

    /// <summary>Type 2's walk: the theater pick, then the first system not in the visited set.</summary>
    static AiSystem? Unvisited(WorldModel m, FleetTask task, AiSector s)
    {
        var stack = new ListStack();
        stack.Push(m.SystemDaysList(s, r => r.OwnedOrPopulated && !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, RankOrder.Ascending));
        while (stack.Pick() is AiSystem pick)
            if (!task.Visited.Contains(pick.System)) return pick;
        return null;
    }

    /// <summary>
    /// Task slot for the gather system. The base is the gather system while it is own and calm. A gather system other than
    /// the base is cleared once it is not own, or has an Uprising or a Blockade. With none, random-key lists over the theater
    /// are tried (key systems, role systems, then own systems; own and free of Uprising and Blockade; one draw of 10 per
    /// qualifying system), then the nearest search. Inferred reading of the keep rule.
    /// </summary>
    static void Gather(WorldModel m, FleetOp op)
    {
        if (m.Record(op.Base) is { } b && b.Own && !b.Unrest) { op.Gather = op.Base; return; }
        if (op.Gather is { } g && g != op.Base && !(m.Record(g) is { } gr && gr.Own && !gr.Unrest)) op.Gather = null;
        if (op.Gather is not null || op.Theater is not { } s) return;
        op.Gather = (Pick(m.SystemRandomList(s, r => r.KeySystem && r.Own && !r.Unrest, RankOrder.Ascending))
            ?? Pick(m.SystemRandomList(s, r => r.ShipyardRole && r.TrainingRole && r.ConstructionRole && r.MineRole && r.DefenceRole && r.Own && !r.Unrest, RankOrder.Ascending))
            ?? Pick(m.SystemRandomList(s, r => r.Own && !r.Unrest, RankOrder.Ascending))
            ?? MissionOps.Nearest(m, s, r => r.Own && !r.Unrest))?.System;
    }

    /// <summary>A ship matches a need when it fits any negative counter.</summary>
    static bool Matches(FleetOp op, AiShip s)
    {
        var b = s;
        return op.Firepower < 0 && b.Armed || op.AntiFighterFirepower < 0 && (b.HasFighters || b.AntiFighter)
            || op.AntiFighters < 0 && b.AntiFighter || op.TroopCapacity < 0 && (b.TroopCarrier || b.ArmedTroopCarrier)
            || op.FighterCapacity < 0 && s.FighterCapacity > 0 || op.Bombardment < 0 && s.Bombardment > 0
            || op.DeathStars < 0 && b.DeathStar || op.Interdictors < 0 && b.Interdictor;
    }

    /// <summary>The short marks of the negative counters (the Death Star count sets none).</summary>
    static void ShortBits(FleetOp op)
    {
        if (op.Firepower < 0) op.ShortFirepower = true;
        if (op.AntiFighterFirepower < 0) op.ShortAntiFighterFirepower = true;
        if (op.TroopCapacity < 0) op.ShortTroopCapacity = true;
        if (op.FighterCapacity < 0) op.ShortFighterCapacity = true;
        if (op.Bombardment < 0) op.ShortBombardment = true;
        if (op.Troops < 0) op.ShortTroops = true;
        if (op.Fighters < 0) op.ShortFighters = true;
        if (op.AntiFighters < 0) op.ShortAntiFighters = true;
        if (op.Interdictors < 0) op.ShortInterdictors = true;
    }

    /// <summary>Fleets nearest the theater (own, free and undamaged, not Enroute). In the first that yields, every free matching ship joins, each by a CreateFleet command, then the base step runs.</summary>
    static Command? ShortfallNear(WorldModel m, FleetOp op, Owner side)
    {
        if (op.Theater is not { } t) return null;
        var x = t.Sector.Definition;
        var stack = new ListStack();
        stack.Push(new RankedList(m.FleetRecords().Where(f => f.Own && f.Free && f.UndamagedShip && !f.Enroute).Select(f => ((object)f,
            f.Location is { } l ? (l.Definition.X - x.X) * (l.Definition.X - x.X) + (l.Definition.Y - x.Y) * (l.Definition.Y - x.Y) : 0)), RankOrder.Descending, m.Rand));
        while (stack.Pick() is AiFleet f)
        {
            // Ship by ship: stop once no counter is negative; a taken ship joins at once
            var taken = new List<AiShip>();
            bool stop = false;
            foreach (var s in m.ShipsOf(f).Where(s => s.Free && !Unavailable(s)).ToList())
            {
                if (!Short(op)) { stop = true; break; }
                if (Matches(op, s)) { Join(op, s); taken.Add(s); }
            }
            if (taken.Count > 0) return Move(m, op, f, taken, side);
            if (stop) return null;
        }
        return null;
    }

    /// <summary>
    /// Own fleets with an undamaged ship, neither free, Enroute nor claimed, taken by the smallest op entry first. Their matching
    /// ships that are neither free nor unavailable join one at a time while a counter is short (a flagship gets task mark 25 instead).
    /// </summary>
    static Command? ShortfallAny(WorldModel m, FleetOp op, Owner side)
    {
        var stack = new ListStack();
        stack.Push(m.FleetMinList(op.Entry, f => f.Own && f.UndamagedShip && !f.Claimed && !f.Free && !f.Enroute, RankOrder.Ascending));
        while (stack.Pick() is AiFleet f)
        {
            var taken = new List<AiShip>();
            bool stop = false;
            foreach (var s in m.ShipsOf(f).Where(s => !Unavailable(s) && !s.Free).ToList())
            {
                if (!Short(op)) { stop = true; break; }
                if (!Matches(op, s)) continue;
                if (s.FlagshipMark) s.FlagshipWanted = true;
                else { Join(op, s); taken.Add(s); }
            }
            if (taken.Count > 0) return Move(m, op, f, taken, side);
            if (stop) return null;
        }
        return null;
    }

    /// <summary>
    /// The first op ship the op can spare (ship fields against the op counters) is released, then a CreateFleet command for the ship,
    /// then the base step. The flagship gets task mark 25 instead while the op holds more than one ship.
    /// </summary>
    static Command? Surplus(WorldModel m, FleetOp op, Owner side)
    {
        foreach (var s in op.Ships.OrderBy(s => s.Ship.Ref).ToList())
        {
            var b = s;
            var f = s.Fleet is { } fl ? m.FleetsById.GetValueOrDefault(fl.Ref) : null;
            bool held = f is not null && (m.Record(f.Location)?.Enemy == true || f.Claimed);
            if ((b.OwnCharacter || b.NotCompleted || b.Enroute) || held) continue;
            bool spare = s.Firepower <= op.Firepower && s.AntiFighterFirepower <= op.AntiFighterFirepower && (!b.AntiFighter || op.AntiFighters >= 1)
                && (!(b.TroopCarrier || b.ArmedTroopCarrier) || s.TroopCapacity <= op.TroopCapacity)
                && (s.FighterCapacity <= 0 || s.FighterCapacity <= op.FighterCapacity) && (s.Bombardment <= 0 || s.Bombardment <= op.Bombardment)
                && (!b.DeathStar || op.DeathStars >= 1) && (!b.Interdictor || op.Interdictors >= 1);
            if (!spare) continue;
            if (s == op.Flagship && op.Ships.Count > 1) { s.FlagshipWanted = true; continue; }
            Leave(op, s);
            return f is null ? null : Move(m, op, f, [s], side);
        }
        return null;
    }

    static Command? Move(WorldModel m, FleetOp op, AiFleet from, List<AiShip> ships, Owner side) =>
        Destination(m, op, from) is { } dest ? new Command(CommandType.CreateFleet, side) { Targets = [.. ships.Select(s => (s.Ship, GroupMode.Object))], ArgRef = dest.System } : null;

    /// <summary>The base without a Blockade; then the theater's own key systems by GenCores (dir Descending), then its own systems by distance to the fleet; then the nearest search.</summary>
    static AiSystem? Destination(WorldModel m, FleetOp op, AiFleet fleet)
    {
        if (m.Record(op.Base) is { } b && !b.Blockade) return b;
        if (op.Theater is not { } t) return null;
        return Pick(m.SystemList(t, r => r.KeySystem && r.Own && !r.Blockade, r => r.GenCores, RankOrder.Descending))
            ?? Pick(m.SystemDistanceList(t, fleet.Fleet, r => r.Own && !r.Blockade, RankOrder.Descending))
            ?? MissionOps.Nearest(m, t, r => r.Own && !r.Blockade);
    }

    static AiSystem? Pick(RankedList list)
    {
        var stack = new ListStack();
        stack.Push(list);
        return stack.Pick() as AiSystem;
    }
}
