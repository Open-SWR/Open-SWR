namespace OpenSWR.Sim.Ai;

/// <summary>
/// Fleet-op produce, with the balance builders, the destination chooser, the status, the character-op requests,
/// the flagship, the task machine, and the task slots for need targets, mission requests and status.
/// </summary>
public static class FleetOps
{
    /// <summary>Ships a shortfall builder does not take: Enroute, not Completed, damaged, or with an own character aboard.</summary>
    static bool Unavailable(AiShip s) => s.Enroute || s.DamagedMoving || s.NotCompleted || s.OwnCharacter || s.DamagedStopped;

    // ---- members ----

    /// <summary>
    /// The ship joins: the op key and entry are set and it is no longer free. The op's counters gain the ship's figures,
    /// the inverse of <see cref="Leave(FleetOp, AiShip)"/>. The strengths, capacities and the extra value are added only
    /// for a positive ship value, each then setting its Has flag. Troops and fighters always count; Death Star, interdictor
    /// and anti-fighter ships count one each.
    /// </summary>
    static void Join(FleetOp op, AiShip s)
    {
        (s.OpKey, s.OpEntry) = (op.Key, op.Entry);
        s.Free = false;
        if (!op.Ships.Contains(s)) op.Ships.Add(s);
        if (s.Firepower > 0) { op.Firepower += s.Firepower; op.HasFirepower = true; }
        if (s.AntiFighterFirepower > 0) { op.AntiFighterFirepower += s.AntiFighterFirepower; op.HasAntiFighterFirepower = true; }
        if (s.TroopCapacity > 0) { op.TroopCapacity += s.TroopCapacity; op.HasTroopCapacity = true; }
        if (s.FighterCapacity > 0) { op.FighterCapacity += s.FighterCapacity; op.HasFighterCapacity = true; }
        if (s.Bombardment > 0) { op.Bombardment += s.Bombardment; op.HasBombardment = true; }
        op.Troops += s.Troops;
        op.Fighters += s.Fighters;
        if (s.DeathStar) op.DeathStars++;
        if (s.Interdictor) op.Interdictors++;
        if (s.AntiFighter) op.AntiFighters++;
    }

    /// <summary>A balanced need counter is still negative (tested in this order).</summary>
    static bool Short(FleetOp op) => op.Firepower < 0 || op.AntiFighterFirepower < 0 || op.TroopCapacity < 0 || op.FighterCapacity < 0 || op.Bombardment < 0
        || op.DeathStars < 0 || op.Interdictors < 0 || op.AntiFighters < 0;

    /// <summary>A balanced need counter is positive; the balance step evens out every counter but troops and fighters.</summary>
    static bool Surplus(FleetOp op) => op.Firepower > 0 || op.AntiFighterFirepower > 0 || op.TroopCapacity > 0 || op.FighterCapacity > 0 || op.Bombardment > 0
        || op.DeathStars > 0 || op.Interdictors > 0 || op.AntiFighters > 0;

    /// <summary>
    /// When the record is this op's, it is free again, loses the task marks, and loses its op key and entry. The op's
    /// counters then lose the ship's own figures (Inferred: also for a record held elsewhere).
    /// </summary>
    static void Leave(FleetOp op, AiShip s)
    {
        if (s.OpKey == op.Key)
        {
            (s.FlagshipMark, s.FlagshipWanted, s.Free) = (false, false, true);
            (s.OpKey, s.OpEntry) = (0, 0);
        }
        op.Firepower -= s.Firepower;
        op.AntiFighterFirepower -= s.AntiFighterFirepower;
        op.TroopCapacity -= s.TroopCapacity;
        op.FighterCapacity -= s.FighterCapacity;
        op.Bombardment -= s.Bombardment;
        op.Troops -= s.Troops;
        op.Fighters -= s.Fighters;
        if (s.DeathStar) op.DeathStars--;
        if (s.Interdictor) op.Interdictors--;
        if (s.AntiFighter) op.AntiFighters--;
        op.Ships.Remove(s);
        if (op.Flagship == s) op.Flagship = null;
    }

    /// <summary>A released fleet is free again and loses its op key and entry; Inferred: it also loses its claim.</summary>
    static void Leave(WorldModel m, FleetOp op, AiFleet f)
    {
        f.Free = true;
        DropClaim(m, f);
        (f.OpKey, f.OpEntry) = (0, 0);
        op.Fleets.Remove(f);
    }

    /// <summary>
    /// Member refresh. Ships in key order: a ship is kept while its record exists, it still belongs to this op and it is own;
    /// otherwise it is released. A kept ship's fleet that is not a member yet joins when its record is free (its flag is
    /// cleared, its op key is set, and its entry follows the op's). Fleets are kept while the record exists, the op still owns
    /// them, they are own and they still hold ship records; otherwise they are released.
    /// </summary>
    static void RefreshMembers(WorldModel m, FleetOp op)
    {
        foreach (var s in op.Ships.OrderBy(s => s.Ship.Ref).ToList())
        {
            if (m.Ships.GetValueOrDefault(s.Ship.Ref) != s || s.OpKey != op.Key || !s.Own) { Leave(op, s); continue; }
            if (s.Fleet is { } fl && m.FleetsById.GetValueOrDefault(fl.Ref) is { } f && !op.Fleets.Contains(f) && f.Free)
            {
                f.Free = false;
                (f.OpKey, f.OpEntry) = (op.Key, op.Entry);
                op.Fleets.Add(f);
            }
        }
        foreach (var f in op.Fleets.ToList())
            if (m.FleetsById.GetValueOrDefault(f.Fleet.Ref) != f || f.OpKey != op.Key || !f.Own || !m.ShipsOf(f).Any()) Leave(m, op, f);
        op.Fleets.Sort((a, b) => a.Fleet.Ref.CompareTo(b.Fleet.Ref));
    }

    /// <summary>
    /// Runs only on change: the op's entry becomes the entry key, member ships and fleets of this op take the same entry,
    /// and attached mission ops store the key in their slot. The character ops' slot is not modelled.
    /// </summary>
    internal static void PushEntry(WorldModel m, FleetOp op, int entry)
    {
        if (op.Entry == entry) return;
        op.Entry = entry;
        foreach (var s in op.Ships.Where(s => s.OpKey == op.Key)) s.OpEntry = entry;
        foreach (var f in op.Fleets.Where(f => f.OpKey == op.Key)) f.OpEntry = entry;
        foreach (var mo in op.MissionOps) MissionOps.SetPriority(m, mo, entry);
    }

    /// <summary>
    /// Reset: the task is deleted, its counters and slots are zeroed, the theater is released, and all ship and fleet members
    /// are released. Attached mission ops retire in ascending key order (full detach), then attached character ops retire.
    /// The remaining fields are zeroed, the base's marks are cleared, and there is no base and the entry is 0.
    /// </summary>
    internal static void Reset(WorldModel m, FleetOp op)
    {
        op.Task = null;
        (op.State, op.Refresh, op.FighterNeed) = (0, false, 0);
        if (op.Theater is { } t && t.FleetOpKey == op.Key) { t.FleetOpKey = 0; m.DirtySector(t); }
        op.Theater = null;
        foreach (var s in op.Ships.ToList()) Leave(op, s);
        foreach (var f in op.Fleets.ToList()) Leave(m, op, f);
        foreach (var mo in op.MissionOps.OrderBy(x => x.Key).ToList()) MissionOps.Retire(m, mo);
        foreach (var c in op.CharacterOps.ToList()) CharacterOpPlanner.Retire(m, c);
        op.ClearCounters();
        m.Record(op.Base)?.ClearBases();
        (op.Base, op.Entry) = (null, 0);
    }

    // ---- produce ----

    public static (Command? Command, bool Finished) Produce(GameState game, WorldModel m, FleetOp op, Owner side)
    {
        if (op.Task is null) return (null, true);
        if (op.Refresh) { RefreshMembers(m, op); op.Refresh = false; }
        var (c, finished) = Step(game, m, op, side);
        if (c is not null) op.Refresh = true;
        return (c, finished);
    }

    static (Command?, bool) Step(GameState game, WorldModel m, FleetOp op, Owner side)
    {
        switch (op.State)
        {
            case 0:
                (op.Refresh, op.State) = (true, 1);
                return (null, false);
            case 1: // Zeroes two counters, then splits the damaged ships of the first such member fleet.
            {
                op.ClearStatus();
                op.State = 2;
                var f = op.Fleets.FirstOrDefault(f => (f.DamagedMoving || f.DamagedStopped)
                    && !f.Claimed && !f.EnrouteShip && !f.Enroute);
                if (f is null || f.Location is null) return (null, false);
                var split = m.ShipsOf(f).Where(s => op.Ships.Contains(s) && (s.DamagedStopped || s.DamagedMoving)
                    && !s.Enroute).ToList();
                foreach (var s in split) Leave(op, s);
                return (split.Count == 0 ? null : new Command(0x270, side) { Targets = [.. split.Select(s => (s.Ship, 0))], ArgRef = f.Location }, false);
            }
            case 2:
                Needs(m, op);
                op.State = 3;
                return (null, false);
            case 3: // Balance: the shortfall builders and their low flags after them, else the surplus builder.
            {
                op.State = 4;
                Command? c = null;
                if (Short(op))
                {
                    c = ShortfallNear(m, op, side) ?? ShortfallAny(m, op, side);
                    ShortBits(op);
                }
                if (c is null && Surplus(op)) c = Surplus(m, op, side);
                return (c, false);
            }
            case 4:
                MissionRequests(op, side);
                Status(op);
                CharacterRequests(op);
                if (op.CharacterRequest != 0 || op.RetireRequest is not null) op.CharacterRequested = true;
                op.State = 5;
                return (null, false);
            case 5:
            {
                var task = op.Task!;
                var (c, done) = Machine(game, m, op, task, side);
                if (done) op.State = 6;
                return (c, false);
            }
            default: // 6: flagship, then the gather system, then the slot step.
            {
                Flagship(m, op);
                if (op.CharacterOpAttached) Gather(m, op);
                else op.Gather = null;
                TaskStatus(m, op, op.Task!);
                if (!op.AnyShort) op.Complete = op.StrongEnough = op.NotEmpty = true;
                else if (!op.HasAny) op.Empty = true;
                else if (!op.ForceShort) op.StrongEnough = op.NotEmpty = true;
                else op.NotEmpty = true;
                op.State = 0;
                return (null, true);
            }
        }
    }

    /// <summary>
    /// State 2: member-fleet sums, then the capacity copies, then the task's need targets are raised and each counter loses
    /// its target. The Has flags come from the sums before the subtraction (the Death Star count sets none).
    /// </summary>
    static void Needs(WorldModel m, FleetOp op)
    {
        op.ClearCounters();
        foreach (var f in op.Fleets)
        {
            op.Firepower += f.FirepowerAllFighters;
            op.AntiFighterFirepower += f.AntiFighterFirepowerAllFighters;
            op.TroopCapacity += f.TroopCapacity;
            op.FighterCapacity += f.FighterCapacity;
            op.Bombardment += f.Bombardment;
            op.Troops += f.Troops;
            op.Fighters += f.Fighters;
            op.AntiFighters += f.AntiFighter;
            if (f.DeathStar) op.DeathStars++;
            if (f.Interdictor) op.Interdictors++;
        }
        (op.TroopCapacitySum, op.FighterCapacitySum) = (op.TroopCapacity, op.FighterCapacity);
        op.HasInterdictors = op.HasFirepower = op.HasAntiFighterFirepower = op.HasTroopCapacity = op.HasFighterCapacity = op.HasBombardment = op.HasTroops = op.HasFighters = op.HasAntiFighters = false;
        if (op.Firepower > 0) op.HasFirepower = true;
        if (op.AntiFighterFirepower > 0) op.HasAntiFighterFirepower = true;
        if (op.TroopCapacity > 0) op.HasTroopCapacity = true;
        if (op.FighterCapacity > 0) op.HasFighterCapacity = true;
        if (op.Bombardment > 0) op.HasBombardment = true;
        if (op.Troops > 0) op.HasTroops = true;
        if (op.Fighters > 0) op.HasFighters = true;
        if (op.AntiFighters > 0) op.HasAntiFighters = true;
        if (op.Interdictors > 0) op.HasInterdictors = true;
        var task = op.Task!;
        Targets(m, op, task);
        op.Firepower -= task.Firepower;
        op.AntiFighterFirepower -= task.AntiFighterFirepower;
        op.TroopCapacity -= task.TroopCapacity;
        op.FighterCapacity -= task.FighterCapacity;
        op.DeathStars -= task.DeathStars;
        op.Bombardment -= task.Bombardment;
        op.Interdictors -= task.Interdictors;
        op.Troops -= task.Troops;
        op.Fighters -= task.Fighters;
        op.AntiFighters -= task.AntiFighters;
    }

    // ---- need targets ----

    static int X12(int v) => v * 12 / 10;
    static int X11(int v) => v * 11 / 10;
    static int X15(int v) => v * 3 / 2;

    /// <summary>
    /// Need targets per task type, only with a theater: each target is raised to its value (never lowered), except the
    /// plain stores. Type 4 first runs the site pick.
    /// </summary>
    static void Targets(WorldModel m, FleetOp op, FleetTask task)
    {
        if (op.Theater is not { } s) return;
        int type = task.Type;
        if (type == 4) SitePick(m, task, s);
        var site = type == 4 ? m.Record(task.Site) : null;
        switch (type)
        {
            case 1:
                task.Firepower = Math.Max(task.Firepower, X12(s.EnemyFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X12(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, X12(s.MaxEnemyFighters + s.EnemyFleetFighters));
                task.Bombardment = Math.Max(task.Bombardment, Math.Max(X11(s.MinUnguardedGenCoreStrength), m.BestBombardment));
                break;
            case 2:
                task.Firepower = Math.Max(task.Firepower, Math.Max(X15(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X15(s.MaxEnemyFighterAntiFighterFirepower) + X15(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, Math.Max(X15(s.MaxEnemyFighters + s.EnemyFleetFighters), m.BestFighterCapacity));
                break;
            case 3:
                task.Firepower = Math.Max(task.Firepower, Math.Max(X12(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X12(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, X12(s.EnemyFleetFighters + s.MaxEnemyFighters));
                break;
            case 4:
            {
                task.Firepower = Math.Max(task.Firepower, Math.Max(X15(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X15(s.EnemyAntiFighterFirepower) + (site is null ? 0 : X15(site.FighterAntiFighterFirepower)), m.BestAntiFighterFirepower));
                int fighters = Math.Max(X11(s.EnemyFleetFighters + s.MaxEnemyFighters), m.BestFighterCapacity);
                task.FighterCapacity = Math.Max(task.FighterCapacity, fighters);
                task.TroopCapacity = Math.Max(task.TroopCapacity, site?.TroopsToTake ?? fighters); // As coded: without a site, the fighter value is used.
                break;
            }
            case 5:
                task.Firepower = Math.Max(task.Firepower, X15(s.EnemyFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, s.EnemyNoBatteries ? Math.Max(X15(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower) : X15(s.EnemyAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, X12(s.MaxEnemyFighters + s.EnemyFleetFighters));
                task.TroopCapacity = Math.Max(task.TroopCapacity, 1);
                break;
            default: // 6
                task.Firepower = Math.Max(task.Firepower, Math.Max(X15(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X15(s.EnemyAntiFighterFirepower) + X15(s.MaxEnemyFighterAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, Math.Max(X15(s.EnemyFleetFighters + s.MaxEnemyFighters), m.BestFighterCapacity));
                task.TroopCapacity = Math.Max(task.TroopCapacity, Math.Max(s.MaxEnemyTroopsToTake, m.BestTroopCapacity));
                break;
        }
        if (type <= 3) (task.TroopCapacity, task.DeathStars, task.Troops) = (0, 0, 0);
        else
        {
            if (m.OwnDeathStar && s.VictoryCharacters > 0) task.DeathStars = 1; // Inferred: ":= 1 if" leaves the target otherwise
            task.Troops = op.TroopCapacitySum;
        }
        if (m.DeathStarShieldAvailable) task.Interdictors = 1; // Inferred, as the Death Star target
        (task.Fighters, task.AntiFighters) = (op.FighterCapacitySum, 1);
    }

    /// <summary>
    /// Site pick for type 4: only with a sector theater. The theater argument (1, 2 or 3) selects one of three site fields.
    /// Both candidate lists are built on every call (their ties draw). Every call stores the first rank-sum pick and picks
    /// on while the stored site's field is below 1; when exhausted, none. There is no keep of the old site (an earlier keep
    /// was withdrawn).
    /// </summary>
    static void SitePick(WorldModel m, FleetTask task, AiSector s)
    {
        Func<AiSystem, int>? k = s.TheaterArgument switch { 1 => r => r.Shipyards, 2 => r => r.TrainingFacilities, 3 => r => r.ConstructionYards, _ => null };
        if (k is null) { task.Site = null; return; }
        var stack = new ListStack();
        stack.Push(m.SystemList(s, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, k, 2));
        stack.Push(m.SystemList(s, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, r => r.UnrestTroops, 2));
        task.Site = null;
        while (stack.Pick() is AiSystem r)
            if (k(r) >= 1) { task.Site = r.System; return; }
    }

    /// <summary>
    /// Troop need by task type (task vtable). Types 1-3 need 0. Type 4: the base's first troop figure minus its second, plus
    /// the site's value. Type 5: the base's first minus its second, minus the theater's value, plus 10. Type 6: the theater's
    /// value plus the base's first minus its second. 0 when one of the two objects does not resolve.
    /// </summary>
    public static int TroopNeed(WorldModel m, FleetOp op)
    {
        if (op.Task is not { } task || m.Record(op.Base) is not { } b) return 0;
        var s = op.Theater;
        return task.Type switch
        {
            4 => m.Record(task.Site) is { } site ? b.UnrestTroops - b.Troops + site.TroopsToTake : 0,
            5 => s is null ? 0 : b.UnrestTroops - b.Troops - s.Held + 10,
            6 => s is null ? 0 : s.EnemyTroopsToTake + b.UnrestTroops - b.Troops,
            _ => 0,
        };
    }

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
            case 1: goal = s.EnemyAssets; break;
            case 2:
                goal = s.EnemyFleet;
                if (!goal)
                {
                    goal = Unvisited(m, task, s) is not null; // A pick not in the task's visited set.
                    if (!goal) s.NoUnvisited = true;
                }
                break;
            case 3: goal = s.Own; break;
            case 4: goal = m.Record(task.Site) is { } site && !site.Own; break;
            case 5: goal = !s.AllHeld || s.EnemyNoBatteries; break;
            default: goal = s.EnemyNoBatteries && s.EnemyFewGenCores && (s.EnemyNoCommandKind3 || op.GeneralAtShip); break;
        }
        if (goal) op.Goal = true;
    }

    /// <summary>Type 2's walk: the theater pick, then the first system not in the visited set.</summary>
    static AiSystem? Unvisited(WorldModel m, FleetTask task, AiSector s)
    {
        var stack = new ListStack();
        stack.Push(m.SystemDaysList(s, r => r.OwnedOrPopulated && !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, 1));
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
        op.Gather = (Pick(m.SystemRandomList(s, r => r.KeySystem && r.Own && !r.Unrest, 1))
            ?? Pick(m.SystemRandomList(s, r => r.ShipyardRole && r.TrainingRole && r.ConstructionRole && r.MineRole && r.DefenceRole && r.Own && !r.Unrest, 1))
            ?? Pick(m.SystemRandomList(s, r => r.Own && !r.Unrest, 1))
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

    /// <summary>Fleets nearest the theater (own, free and undamaged, not Enroute). In the first that yields, every free matching ship joins, each by a 0x270 command, then the base step runs.</summary>
    static Command? ShortfallNear(WorldModel m, FleetOp op, Owner side)
    {
        if (op.Theater is not { } t) return null;
        var x = t.Sector.Definition;
        var stack = new ListStack();
        stack.Push(new RankedList(m.FleetRecords().Where(f => f.Own && f.Free && f.UndamagedShip && !f.Enroute).Select(f => ((object)f,
            f.Location is { } l ? (l.Definition.X - x.X) * (l.Definition.X - x.X) + (l.Definition.Y - x.Y) * (l.Definition.Y - x.Y) : 0)), 2, m.Rand));
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
        stack.Push(m.FleetMinList(op.Entry, f => f.Own && f.UndamagedShip && !f.Claimed && !f.Free && !f.Enroute, 1));
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
    /// The first op ship the op can spare (ship fields against the op counters) is released, then a 0x270 command for the ship,
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
        Destination(m, op, from) is { } dest ? new Command(0x270, side) { Targets = [.. ships.Select(s => (s.Ship, 0))], ArgRef = dest.System } : null;

    /// <summary>The base without a Blockade; then the theater's own key systems by GenCores (dir 2), then its own systems by distance to the fleet; then the nearest search.</summary>
    static AiSystem? Destination(WorldModel m, FleetOp op, AiFleet fleet)
    {
        if (m.Record(op.Base) is { } b && !b.Blockade) return b;
        if (op.Theater is not { } t) return null;
        return Pick(m.SystemList(t, r => r.KeySystem && r.Own && !r.Blockade, r => r.GenCores, 2))
            ?? Pick(m.SystemDistanceList(t, fleet.Fleet, r => r.Own && !r.Blockade, 2))
            ?? MissionOps.Nearest(m, t, r => r.Own && !r.Blockade);
    }

    static AiSystem? Pick(RankedList list)
    {
        var stack = new ListStack();
        stack.Push(list);
        return stack.Pick() as AiSystem;
    }

    // ---- state 4: mission requests, status, character-op requests ----

    /// <summary>
    /// Mission requests for the task's slot. Per rule: a create is set when it applies and none is set yet, else a retire is set
    /// when none is set yet (Inferred: later rules still record a retire). The counts are of the attached abduction-assassination,
    /// espionage, recon and sabotage ops. Then the attached ops set their flags: one for any op, one when an op lacks its
    /// full team or its decoys, and one when the op's recheck kept a member. The lowest-score attached op of the retire kind and
    /// subtype is recorded (not retired; first in key order on ties).
    /// </summary>
    static void MissionRequests(FleetOp op, Owner side)
    {
        (op.RequestKind, op.RequestSubtype, op.RetireKey) = (0, 0, 0);
        if (op.Theater is not { } s) return;
        bool notOwn = (s.Enemy || s.Neutral), enemy = s.Enemy,
            enemyOrFleet = (s.Enemy || s.EnemyFleet);
        int a0 = op.ObjectOps(0), a4 = op.ObjectOps(1), a8 = op.ObjectOps(2), ac = op.ObjectOps(3);
        var rules = new (uint Kind, int Subtype, bool Create, bool Retire)[]
        {
            (0x54, 0, a8 < 2 && notOwn, a8 > 2 || !notOwn && a8 > 0),
            (0x52, 0, a4 == 0, a4 > 1),
            (0x69, 0, ac == 0 && enemy, ac > 1 || !enemy && ac > 0),
            (side == Owner.Alliance ? 0x62u : 0x63u, 4, a0 == 0 && enemyOrFleet, a0 > 1 || !enemyOrFleet && a0 > 0),
        };
        (uint Kind, int Subtype)? retire = null;
        foreach (var (kind, subtype, create, ret) in rules)
            if (create) { if (op.RequestKind == 0) { (op.RequestKind, op.RequestSubtype) = ((int)kind, subtype); op.MissionRequested = true; } }
            else if (ret) retire ??= (kind, subtype);
        foreach (var mo in op.MissionOps)
        {
            op.HasMissionOps = true;
            if (!(mo.TeamComplete && mo.DecoysDone)) op.OpWithoutTeam = true;
            if (mo.KeptMember) op.OpTeamed = true;
        }
        if (retire is { } r && op.MissionOps.Where(mo => !mo.Retired && MissionOps.Matches(mo.Class, r.Kind, r.Subtype))
                .OrderBy(mo => mo.Key).MinBy(MissionOps.Score) is { } low)
            op.RetireKey = low.Key;
    }

    /// <summary>
    /// Attached character ops. Clears only the first group of task flags, then sets them: one for any op, one for an op with
    /// a waiting character, one for any op whose character is at the ship without its kind issued, and one for a type-4 op at the
    /// ship with its kind issued. The needs get one flag with any op, and each op adds team or no team by whether it is assigned.
    /// </summary>
    static void Status(FleetOp op)
    {
        // only these; the needs are cleared by state 1, its OpWithoutTeam / OpTeamed also carry the mission ops' state
        op.CharacterAtShip = op.CharacterOpAttached = op.CharacterWaiting = false;
        if (op.CharacterOps.Count == 0) return;
        op.CharacterOpAttached = true;
        op.HasCharacterOps = true;
        foreach (var c in op.CharacterOps)
        {
            if (c.Waiting) op.CharacterWaiting = true;
            if (c.AtShip && !c.KindIssued) op.CharacterAtShip = true;
            else if (c.AtShip && c.KindIssued && c.Type == 4) op.GeneralAtShip = true;
            if (!c.Assigned) op.OpWithoutTeam = true;
            else op.OpTeamed = true;
        }
    }

    /// <summary>Task slot for the character op: the type to create, and the op to retire.</summary>
    static void CharacterRequests(FleetOp op)
    {
        int type = op.TaskKind;
        var sector = op.Theater;
        int n3 = op.CharacterOpCount(3), n4 = op.CharacterOpCount(4), n5 = op.CharacterOpCount(5);
        op.CharacterRequest = type is 4 or 6 && n4 < 1 && op.TroopCapacitySum > 0 && sector is { Enemy: true } ? 4
            : n3 < 1 && op.Fleets.Count > 0 ? 3
            : n5 < 1 && op.FighterCapacitySum > 0 && (sector is { Enemy: true } || sector is { EnemyFleet: true }) ? 5 : 0;
        op.RetireRequest = op.CharacterOps.FirstOrDefault(c => c.Type switch
        {
            3 => n3 >= 2 || op.Fleets.Count == 0 && n3 >= 1,
            4 => type is 4 or 6 ? n4 > 1 : n4 > 0,
            _ => n5 > 1 || op.FighterCapacitySum == 0 && n5 > 0,
        });
    }

    /// <summary>
    /// Flagship. Without the op's own flag there is none (the old one loses its flag when it is the op's). A current flagship of
    /// the op with the first flag and without the second is kept; when it is not Enroute and its fleet is a member, the fleet's
    /// best armed ship (from the armed list; the fallback list is always empty) replaces it with a larger value, or when the
    /// flagship is not armed. An Enroute one is kept as is. With no keep: per member fleet the top of the armed list, and
    /// the largest value above 0 wins; the old flagship loses its flag only on the op's own path. After the keep and re-pick
    /// paths only, every member ship loses the op's own flag (none without the op's flag).
    /// </summary>
    static void Flagship(WorldModel m, FleetOp op)
    {
        var cur = op.Flagship;
        bool mine = cur is not null && op.Ships.Contains(cur) && cur.OpKey == op.Key;
        if (!op.CharacterOpAttached)
        {
            if (mine) cur!.FlagshipMark = false;
            op.Flagship = null;
            return;
        }
        AiShip? Top(RankedList list)
        {
            var stack = new ListStack();
            stack.Push(list);
            return stack.Pick() as AiShip;
        }
        if (mine && cur!.FlagshipMark)
        {
            if (!cur.FlagshipWanted)
            {
                if (!cur.Enroute && cur.Fleet is { } cf && m.FleetsById.GetValueOrDefault(cf.Ref) is { } fleet && op.Fleets.Contains(fleet))
                {
                    // An empty armed pick writes none into the fleet key slot, so the fallback list is built over none: empty,
                    // no draw, and the flagship is kept.
                    if (Top(m.ShipList(fleet, s => s.Own && s.Armed && !NotFlagship(s), s => s.ShieldRecharge, 1)) is { } b && b != cur
                        && (b.ShieldRecharge > cur.ShieldRecharge || !cur.Armed))
                    {
                        cur.FlagshipMark = false;
                        op.Flagship = cur = b;
                    }
                }
                cur.FlagshipMark = true;
                foreach (var s in op.Ships) s.FlagshipWanted = false;
                return;
            }
            cur.FlagshipMark = false;
        }
        AiShip? best = null;
        foreach (var f in op.Fleets)
            if (Top(m.ShipList(f, s => s.Own && !NotFlagship(s), s => s.ShieldRecharge, 1)) is { } x && x.ShieldRecharge > (best?.ShieldRecharge ?? 0)) best = x;
        op.Flagship = best;
        if (best is not null) best.FlagshipMark = true;
        foreach (var s in op.Ships) s.FlagshipWanted = false;
    }

    // ---- task machine ----

    static (Command?, bool) Machine(GameState game, WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        switch (task.State)
        {
            case 1: // Clears the marks on the theater's ten systems, then sets one mark per member fleet's claimed system; plain writes, no dirty marking.
                if (op.Theater is { } th)
                    foreach (var r in th.Systems) r.ClearClaimMarks();
                foreach (var f in op.Fleets)
                    if (m.Record(f.Target) is { } r && f.Claimed) Mark(r, MarkPriority.First(c => HasClaim(f, c)));
                task.State = 2;
                return (null, false);
            case 2:
                task.State = Base(m, op, task) ? 3 : 5;
                return (null, false);
            case 3:
                task.State = 4;
                return (task.Type <= 3 ? TroopUnload(m, op, side) : TroopsIntoFleets(m, op, task, side), false);
            case 4:
                task.State = 5;
                return (FightersIntoFleets(m, op, side), false);
            case 5: // From the cursor's node (else the leftmost) in key order, wrapping; the cursor moves to that node.
            {       // After the find, cleared when that is the start; nothing found: cursor cleared, the stored value kept.
                int n = op.Fleets.Count, start = task.Start is { } s0 && op.Fleets.Contains(s0) ? op.Fleets.IndexOf(s0) : 0;
                task.Start = null;
                for (int k = 0; k < n; k++)
                    if (op.Fleets[(start + k) % n] is var f && !f.Claimed && !f.DamagedStopped && !f.DamagedMoving
                        && !f.EnrouteShip && !f.Enroute)
                    {
                        task.Fleet = f;
                        int next = (start + k + 1) % n;
                        if (next != start) task.Start = op.Fleets[next];
                        task.State = 6;
                        return (null, false);
                    }
                task.State = 0xE;
                return (null, false);
            }
            case 6:
            {
                // (a) only with the flagship (a capital ship) among the task fleet's ships. When the op has its first flag, the flagship is
                // flagged with no command. Else, when the op has its second flag and a gather system without a Blockade, a 0x201 command
                // moves the task fleet there and the flag is set (no own or location test: a fleet already there gets the move again).
                // (b) Runs only when (a) built no command; its 0x201 never sets the flag (state 0xE acts only on the flag, and a part-(b)
                // command sets state 7).
                bool flag = false;
                Command? c = null;
                if (op.Flagship is { } fs && Family.Of(fs.Ship.Type) is >= 0x14 and < 0x1C && task.Fleet is { } tf6 && m.ShipsOf(tf6).Contains(fs))
                {
                    if (op.CharacterAtShip) flag = true;
                    else if (op.CharacterWaiting && op.Gather is { } g && m.Record(g) is { } gr && !gr.Blockade)
                        (flag, c) = (true, new Command(0x201, side) { Targets = [(tf6.Fleet, 0)], ArgRef = g });
                }
                c ??= Characters(m, op, task, side);
                task.State = flag ? 0xE : 7;
                return (c, false);
            }
            case 7: task.State = Attack(m, op, task) ? 0xD : 8; return (null, false);
            case 8: task.State = Assault(m, op, task) ? 0xD : 10; return (null, false);
            case 10: task.State = MoveTarget(m, op, task) ? 0xD : 9; return (null, false);
            case 9: task.State = Bombard(m, op, task) ? 0xD : 0xC; return (null, false);
            case 0xC:
                task.State = 0xE;
                return (Regroup(m, op, task, side), false);
            case 0xD: // No location test
                task.State = 0xE;
                return (task.Fleet is { Target: { } dest } tf ? new Command(0x201, side) { Targets = [(tf.Fleet, 0)], ArgRef = dest } : null, false);
            case 0xE: // Done: back to state 0 and produce is done, with the returned command
            {
                var (c, done) = Strike(m, op, task, side);
                if (done) task.State = 0;
                return (c, done);
            }
            default: // 0 (Inferred: a finished machine starts at state 1 on the next produce pass)
                task.State = 1;
                return Machine(game, m, op, task, side);
        }
    }

    /// <summary>
    /// Base system for the slot. Types 1-3: keep an own, non-blockaded base (requiring the key flag when the theater has its own
    /// flag), and clear its marks; else the theater's own key systems (not blockaded, not already marked) by value (direction 1),
    /// the same without the key flag, then the nearest search. A new base gets the key mark in place of the other two (Inferred:
    /// the old one loses that mark). Types 4-6: keep an own, non-blockaded base (no flag test), else the old base loses its marks;
    /// the same searches by the second value; kept and new bases get the type's marks (<see cref="BaseMarks"/>).
    /// </summary>
    static bool Base(WorldModel m, FleetOp op, FleetTask task)
    {
        if (op.Theater is not { } t) return false;
        if (task.Type >= 4)
        {
            if (m.Record(op.Base) is { } b)
            {
                if (b.Own && !b.Blockade) { BaseMarks(m, b, task.Type); return true; }
                if (task.Type == 4) (b.AssaultBase, b.Base) = (false, false);
                else (b.AssaultBase, b.DefenseBase, b.Base) = (false, false, false);
                m.DirtySystem(b);
                op.Base = null;
            }
            var site = Pick(m.SystemList(t, r => r.KeySystem && r.Own && !r.Blockade && !r.Base, r => r.TroopSurplus, 1))
                ?? Pick(m.SystemList(t, r => r.Own && !r.Blockade && !r.Base, r => r.TroopSurplus, 1))
                ?? MissionOps.Nearest(m, t, r => r.Own && !r.Blockade && !r.Base);
            op.Base = site?.System;
            if (site is null) return false;
            BaseMarks(m, site, task.Type, always: true); // A new base: unconditional for all three types.
            return true;
        }
        if (m.Record(op.Base) is { } kept && kept.Own && !kept.Blockade && (!t.KeySystem || kept.KeySystem))
        {
            kept.ClearBaseMarks();
            return true;
        }
        var found = Pick(m.SystemList(t, r => r.KeySystem && r.Own && !r.Blockade && !r.Base, r => r.FighterBalance, 1))
            ?? Pick(m.SystemList(t, r => r.Own && !r.Blockade && !r.Base, r => r.FighterBalance, 1))
            ?? MissionOps.Nearest(m, t, r => r.Own && !r.Blockade && !r.Base);
        m.Record(op.Base)?.MarkBase(false);
        op.Base = found?.System;
        if (found is null) return false;
        found.ClearBaseMarks();
        found.MarkBase(true);
        return true;
    }

    /// <summary>
    /// Base marks for types 4-6. A kept base gets them only when it lacks the type's "have" mark (type 5 tests one mark, and
    /// types 4 and 6 test another); a new base always gets them. Type 4 adds two marks; type 5 clears one mark and sets two;
    /// type 6 clears one mark and sets two. The write marks the record dirty.
    /// </summary>
    static void BaseMarks(WorldModel m, AiSystem b, int type, bool always = false)
    {
        if (!always && (type == 5 ? b.DefenseBase : b.AssaultBase)) return;
        if (type == 5) b.AssaultBase = false;
        if (type == 6) b.DefenseBase = false;
        if (type == 5) b.DefenseBase = b.Base = true;
        else b.AssaultBase = b.Base = true;
        m.DirtySystem(b);
    }

    /// <summary>
    /// Free fighter slots for the base (planners 7 and 10): the op's count is zeroed; with the base at a system without a Blockade,
    /// the count becomes the sum of the member fleets' free fighter slots minus the base's own count; a positive count is recorded
    /// on the base.
    /// </summary>
    internal static StarSystem? FighterNeed(WorldModel m, FleetOp op)
    {
        op.FighterNeed = 0;
        if (m.Record(op.Base) is not { } b || b.Blockade) return null;
        op.FighterNeed = Members(m, op, f => f.Own, f => f.FighterRoom, 1).Entries.Sum(e => e.Value) - b.Fighters;
        return op.FighterNeed > 0 ? op.Base : null;
    }

    /// <summary>Every fleet record of the op that passes (model order), valued by <paramref name="value"/>.</summary>
    static RankedList Members(WorldModel m, FleetOp op, Func<AiFleet, bool> pass, Func<AiFleet, int> value, int dir) =>
        new(m.FleetRecords().Where(f => f.OpKey == op.Key).Where(pass).Select(f => ((object)f, value(f))), dir, m.Rand);

    /// <summary>Unload slot for types 1-3: the first member fleet at an own system with usable troops unloads them, then a 0x201 command to the location.</summary>
    static Command? TroopUnload(WorldModel m, FleetOp op, Owner side)
    {
        var stack = new ListStack();
        stack.Push(Members(m, op, f => f.Own && f.HasTroops && !f.Claimed && !f.Enroute, f => f.UsableTroops, 1));
        while (stack.Pick() is AiFleet f)
        {
            if (m.Record(f.Location) is not { } at || !at.Own) continue;
            var troops = m.TroopsAboard(f, s => s.Own && !s.Free && !s.Enroute);
            if (troops.Count > 0) return new Command(0x201, side) { Targets = [.. troops.Select(x => (x, 0))], ArgRef = f.Location };
        }
        return null;
    }

    /// <summary>
    /// Unload slot for types 4-6: only with base surplus above 0. Member fleets are taken by free troop slots (direction 1) until
    /// one is at the base. The count n is the smaller of the fleet's free slots and the base surplus; that many usable own troops
    /// of the base are chosen, each the one with the largest attack value not yet chosen (type 5 ranks by defence value first,
    /// then attack), and each goes to the fleet by a 0x201 command. Inferred getters: attack is the catalogue's attack value,
    /// defence is the catalogue's DefenseStrength; ties in key order.
    /// </summary>
    static Command? TroopsIntoFleets(WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        if (op.Base is not { } b || m.Record(b) is not { } br || br.TroopSurplus <= 0) return null;
        var stack = new ListStack();
        stack.Push(Members(m, op, f => f.Own && !f.Enroute, f => f.TroopRoom, 1));
        while (stack.Pick() is AiFleet f)
        {
            if (f.Location != b) continue;
            int n = Math.Min(f.TroopRoom, br.TroopSurplus);
            Product P(GameObject t) => m.Catalogue[t.Type] ?? Product.None;
            var usable = m.Contents(b).Where(x => Family.IsTroop(x.Type) && m.Usable(x) && m.See(x).Owner == m.Side);
            var ordered = task.Type == 5 ? usable.OrderByDescending(t => P(t).AntiFighterFirepower).ThenByDescending(t => P(t).Firepower) : usable.OrderByDescending(t => P(t).Firepower);
            var troops = ordered.Take(Math.Max(0, n)).ToList();
            return troops.Count == 0 ? null : new Command(0x201, side) { Targets = [.. troops.Select(x => (x, 0))], ArgRef = f.Fleet };
        }
        return null;
    }

    /// <summary>
    /// Fighter unload (state 4): only with the base's fighter surplus above 0. The first member fleet (by free fighter slots,
    /// direction 1) at the base with at least one free slot is used; n is the smaller of the fleet's free slots and the base
    /// surplus; the first n usable own fighters in list order each go to the fleet by a 0x201 command.
    /// </summary>
    static Command? FightersIntoFleets(WorldModel m, FleetOp op, Owner side)
    {
        if (op.Base is not { } b || m.Record(b) is not { } br || br.UsableFighters <= 0) return null;
        var stack = new ListStack();
        stack.Push(Members(m, op, f => f.Own && !f.Enroute, f => f.FighterRoom, 1));
        while (stack.Pick() is AiFleet f)
        {
            if (f.Location != b || f.FighterRoom < 1) continue;
            var fighters = m.Contents(b).Where(x => Family.Of(x.Type) is >= 0x1C and < 0x20 && m.Usable(x) && m.See(x).Owner == m.Side)
                .Take(Math.Min(f.FighterRoom, br.UsableFighters)).ToList();
            return fighters.Count == 0 ? null : new Command(0x201, side) { Targets = [.. fighters.Select(x => (x, 0))], ArgRef = f.Fleet };
        }
        return null;
    }

    /// <summary>
    /// State 6, the flagship and character step. (a) The flagship branches (a flag, or a 0x201 whose target goes to the op's
    /// target slot, written by the gather-system step under the op's flag) are in the machine. (b) A fleet with an own character
    /// aboard, not Enroute, at an own system: the usable characters aboard ships with the matching flags are used, other than the
    /// 0x800 kind. An enemy-allegiance one (prisoners) joins the targets without triggering; an own-allegiance one joins and
    /// triggers when its agent's field is 0 or it is not one of the fleet op's character ops. The 0x201 command needs a trigger.
    /// </summary>
    static Command? Characters(WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        if (task.Fleet is not { } f || !f.OwnCharacter || f.Enroute || m.Record(f.Location) is not { } at
            || !at.Own) return null;
        var go = new List<GameObject>();
        bool trigger = false;
        foreach (var s in m.ShipsOf(f).Where(s => !s.Enroute && (s.OwnCharacter || s.EnemyCaptured)))
            foreach (var x in m.Contents(s.Ship).Where(x => Family.Of(x.Type) is >= 0x30 and < 0x40 && m.Usable(x)))
            {
                if (!m.Agents.TryGetValue(x.Ref, out var a)) continue;
                go.Add(x);
                if (a.Own && (a.CharacterOpKey == 0 || op.CharacterOps.All(c => c.Key != a.CharacterOpKey))) trigger = true;
            }
        return !trigger ? null : new Command(0x201, side) { Targets = [.. go.Select(x => (x, 0))], ArgRef = f.Location };
    }

    // ---- target slots ----

    /// <summary>A fleet-op task's claim on its target system.</summary>
    enum ClaimKind { Attack, Assault, Bombard, Move }

    /// <summary>State 1's mark priority: move &gt; attack &gt; assault &gt; bombard.</summary>
    static readonly ClaimKind[] MarkPriority = [ClaimKind.Move, ClaimKind.Attack, ClaimKind.Assault, ClaimKind.Bombard];

    static bool HasClaim(AiFleet f, ClaimKind claim) => claim switch
    {
        ClaimKind.Attack => f.AttackClaim, ClaimKind.Assault => f.AssaultClaim, ClaimKind.Bombard => f.BombardClaim, _ => f.MoveClaim,
    };

    /// <summary>The system's claim mark of a kind; a plain write, no dirty.</summary>
    static void Mark(AiSystem s, ClaimKind claim)
    {
        switch (claim)
        {
            case ClaimKind.Attack: s.AttackMark = true; break;
            case ClaimKind.Assault: s.AssaultMark = true; break;
            case ClaimKind.Bombard: s.BombardMark = true; break;
            default: s.MoveMark = true; break;
        }
    }

    static void Claim(FleetOp op, AiFleet f, AiSystem s, ClaimKind claim)
    {
        Mark(s, claim);
        switch (claim)
        {
            case ClaimKind.Attack: f.AttackClaim = true; break;
            case ClaimKind.Assault: f.AssaultClaim = true; break;
            case ClaimKind.Bombard: f.BombardClaim = true; break;
            default: f.MoveClaim = true; break;
        }
        (f.OpKey, f.Target) = (op.Key, s.System);
    }

    /// <summary>A dropped claim: the fleet's claims and target cleared; the system keeps its mark until the next state 1.</summary>
    static void DropClaim(WorldModel m, AiFleet f)
    {
        f.ClearClaims();
        f.Target = null;
    }

    /// <summary>Ships the flagship pick skips: Enroute, damaged, not Completed or with task mark 25.</summary>
    static bool NotFlagship(AiShip s) => s.FlagshipWanted || s.DamagedStopped || s.NotCompleted || s.DamagedMoving || s.Enroute;

    static AiSystem? FirstPassing(WorldModel m, Func<AiSystem, bool> check, params RankedList[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) stack.Push(l);
        while (stack.Pick() is AiSystem s)
            if (check(s)) return s;
        return null;
    }

    /// <summary>
    /// Attack slot, every type: with the fleet's first value above 0 (type 4 first tries the site when it has its mark), a pick
    /// by the fleet's second value (direction 1) among systems with the mark and without the excluded mark. Types 1 and 2 also
    /// try a second pick when their own value is above 0; then the attack check. Type 2 adds the target to its visited set.
    /// </summary>
    static bool Attack(WorldModel m, FleetOp op, FleetTask task)
    {
        if (task.Fleet is not { } f || op.Theater is not { } t) return false;
        AiSystem? s = null;
        if (f.Firepower > 0 && task.Type == 4 && m.Record(task.Site) is { } site && site.EnemyFleet && AttackCheck(m, f, site)) s = site;
        if (s is null && f.Firepower > 0) s = FirstPassing(m, x => AttackCheck(m, f, x), m.SystemList(t, r => r.EnemyFleet && !r.AttackMark, r => r.EnemyFirepower, 1));
        if (s is null && task.Type <= 2 && f.AntiFighterFirepower > 0)
            s = FirstPassing(m, x => AttackCheck(m, f, x), m.SystemList(t, r => r.EnemyFighters && r.Enemy && !r.AttackMark, r => r.FighterAntiFighterFirepower, 1));
        if (s is null) return false;
        if (task.Type == 2) task.Visited.Add(s.System); // Type 2 remembers the visited system.
        Claim(op, f, s, ClaimKind.Attack);
        return true;
    }

    /// <summary>
    /// Assault slot, only with usable troops (the fleet's troop value above 0): type 4 takes the site from the task. Type 6 picks
    /// by its own field only when the theater's value is above 0; otherwise it makes two picks, one by its field and one by
    /// another, both excluding the same mask. Then the assault check.
    /// </summary>
    static bool Assault(WorldModel m, FleetOp op, FleetTask task)
    {
        if (task.Fleet is not { } f || op.Theater is not { } t || f.UsableTroops <= 0) return false;
        static bool excl(AiSystem r) => r.AttackMark || r.AssaultMark || r.EnemyFleet;
        AiSystem? s = task.Type switch
        {
            4 => m.Record(task.Site) is { } site && AssaultCheck(m, f, site) ? site : null,
            6 => (t.VictoryCharacters > 0 ? FirstPassing(m, x => AssaultCheck(m, f, x), m.SystemList(t, r => r.Enemy && !excl(r), r => r.VictoryCharacters, 1)) : null)
                ?? FirstPassing(m, x => AssaultCheck(m, f, x), m.SystemList(t, r => r.Enemy && !excl(r), r => r.ProducerMaintenance, 1), m.SystemList(t, r => r.Enemy && r.OwnedOrPopulated && !excl(r), r => r.UnrestTroops, 2)),
            _ => null,
        };
        if (s is null) return false;
        Claim(op, f, s, ClaimKind.Assault);
        return true;
    }

    /// <summary>
    /// Bombard slot, only with a fleet bombard value of at least 1. Type 1 picks with the mark, excluding others, by the fleet's
    /// value (direction 2), then by two more fields in turn (direction 1). Type 3 picks by the fleet's value (direction 2), then
    /// by another field (direction 1). Types 5 and 6 pick with the other mark, by the fleet's value (direction 2), then by
    /// another field (direction 1). Picks are popped until the bombard check passes.
    /// </summary>
    static bool Bombard(WorldModel m, FleetOp op, FleetTask task)
    {
        if (task.Fleet is not { } f || op.Theater is not { } t || task.Type is 2 or 4 || f.Bombardment < 1) return false;
        bool req(AiSystem r) => r.Enemy && (task.Type is not (5 or 6) || r.OwnedOrPopulated);
        static bool excl(AiSystem r) => r.AttackMark || r.AssaultMark || r.BombardMark || r.EnemyFleet;
        RankedList[] lists = task.Type switch
        {
            1 => [m.SystemList(t, r => req(r) && !excl(r), r => r.GenCoreStrength, 2), m.SystemList(t, r => req(r) && !excl(r), r => r.ProducerMaintenance, 1), m.SystemList(t, r => req(r) && !excl(r), r => r.Support, 1)],
            3 => [m.SystemList(t, r => req(r) && !excl(r), r => r.GenCoreStrength, 2), m.SystemList(t, r => req(r) && !excl(r), r => r.Troops, 1)],
            _ => [m.SystemList(t, r => req(r) && !excl(r), r => r.GenCoreStrength, 2), m.SystemList(t, r => req(r) && !excl(r), r => r.ProducerMaintenance, 1)],
        };
        var s = FirstPassing(m, x => BombardType(m, f, x) != 0, lists);
        if (s is null) return false;
        Claim(op, f, s, ClaimKind.Bombard);
        return true;
    }

    /// <summary>Move slot, type 5 with the fleet's value at least 1: a pick with the mark, excluding others, by another field (direction 1); then the move check.</summary>
    static bool MoveTarget(WorldModel m, FleetOp op, FleetTask task)
    {
        if (task.Type != 5 || task.Fleet is not { } f || op.Theater is not { } t || f.UsableTroops < 1) return false;
        var s = FirstPassing(m, x => MoveCheck(f, x), m.SystemList(t, r => r.Neutral && !r.Populated && !r.EnemyFleet && !r.AttackMark, r => r.FreeEnergy, 1));
        if (s is null) return false;
        Claim(op, f, s, ClaimKind.Move);
        return true;
    }

    /// <summary>
    /// State 0xC: with at least two member fleets, the nearest other op fleet record (own, excluding the flagged ones; the task
    /// fleet ranks too and is skipped when popped) at a location without the system mark gets a 0x201 command moving the task
    /// fleet there. Otherwise (one fleet, or the walk found none) the slot's system gets a 0x201 command for the task fleet.
    /// </summary>
    static Command? Regroup(WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        if (task.Fleet is not { } f) return null;
        if (op.Fleets.Count >= 2)
        {
            // Every own fleet record of the op, neither Enroute nor claimed, the task fleet included
            var at = f.Location;
            var stack = new ListStack();
            stack.Push(new RankedList(m.FleetRecords().Where(o => o.OpKey == op.Key && o.Own && !o.Claimed && !o.Enroute).Select(o => ((object)o, at is not null && o.Location is { } l
                ? (l.Definition.X - at.Definition.X) * (l.Definition.X - at.Definition.X) + (l.Definition.Y - at.Definition.Y) * (l.Definition.Y - at.Definition.Y) : 0)), 2, m.Rand));
            while (stack.Pick() is AiFleet other)
                if (other != f && m.Record(other.Location) is { } r && !r.EnemyFleet) return new Command(0x201, side) { Targets = [(f.Fleet, 0)], ArgRef = other.Fleet };
            // None found: falls through to the slot's system step
        }
        var dest = Fallback(m, op, task, f);
        if (dest is null) return null;
        // No location test here: the original repeats the move too (a no-effect 0x204 at execution); the
        // command budget of 10 per day limits it.
        return new Command(0x201, side) { Targets = [(f.Fleet, 0)], ArgRef = dest.System };
    }

    /// <summary>
    /// Slot for the system to move to, trying steps while none is found. (1) Types 1, 2, 5 and 6 with exactly one member fleet
    /// that lacks the excluded flags; types 5 and 6 without a base; types 2 and 6 when the model's two values are at most the
    /// fleet's two values. The candidates are a days list (types 1 and 2, with one mark) or random keys (type 5 excludes one
    /// mark; type 6), with marks excluded. (2) The base (type 4 without the Blockade test). (3) Own key systems by the key field,
    /// direction 2. (4) Own role systems by the role field, direction 1 (type 4 without its second exclusion). (5) Only from an
    /// enemy system: the theater by distance to the fleet (excluding one mark, direction 2). Inferred: the days lists use
    /// direction 1. Type 2's step (1) skips the task's visited systems and adds its result.
    /// </summary>
    static AiSystem? Fallback(WorldModel m, FleetOp op, FleetTask task, AiFleet f)
    {
        if (op.Theater is not { } t) return null;
        int type = task.Type;
        AiSystem? s = null;
        if (type is 1 or 2 or 5 or 6 && op.Fleets.Count == 1 && !(f.EnrouteShip || f.AboardEnroute)
            && (type is not (5 or 6) || op.Base is null) && (type is not (2 or 6) || m.BestArmedFirepower <= f.Firepower && m.BestAntiFighterFirepower <= f.AntiFighterFirepower))
        {
            if (type == 2)
            {
                s = Unvisited(m, task, t); // Visited picks are skipped, and the result is added.
                if (s is not null) task.Visited.Add(s.System);
            }
            else
                s = Pick(type switch
                {
                    1 => m.SystemDaysList(t, r => !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, 1),
                    5 => m.SystemRandomList(t, r => !r.Explored && !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, 1),
                    _ => m.SystemRandomList(t, r => !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, 1),
                });
        }
        if (s is null && m.Record(op.Base) is { } b && (type == 4 || !b.Blockade)) s = b;
        bool excl(AiSystem r) => type != 4 && r.Blockade;
        s ??= Pick(m.SystemList(t, r => r.KeySystem && r.Own && !excl(r), r => r.GenCores, 2));
        s ??= Pick(m.SystemList(t, r => r.ShipyardRole && r.TrainingRole && r.ConstructionRole && r.MineRole && r.DefenceRole && r.Own && !excl(r), r => r.ProducerMaintenance, 1));
        if (s is null && m.Record(f.Location) is { } at && at.Enemy) s = Pick(m.SystemDistanceList(t, f.Fleet, r => !r.Enemy && !r.EnemyFleet, 2));
        return s;
    }

    /// <summary>
    /// State 0xE, the strike walk. For each member fleet from the cursor that has a claim and is not Enroute: away from its
    /// target it loses the claim; at its target only the attack check runs. Assault gives a 0x223 command (when the theater's
    /// Death Star field is at least 1), else 0x234 or the bombard type; bombard gives 0x223 or the bombard type; move takes the
    /// first ship in key order with usable troops, and its first usable troop gets a 0x201 command to the theater. A failed
    /// check clears the claims and the target. The command is built by the strike builder.
    /// </summary>
    /// Cursor: a fleet key, found exactly in the key-ordered members, else the lowest key. A fleet without a record is skipped.
    /// The first command sets the cursor to the successor's key (none after the last: done with that command). The walk's end
    /// clears the cursor and is done.
    internal static (Command?, bool) Strike(WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        int i = Math.Max(0, op.Fleets.FindIndex(f => f.Fleet.Ref == task.Strike));
        for (; i < op.Fleets.Count; i++)
        {
            var c = Strike(m, op.Fleets[i], side);
            if (c is null) continue;
            bool last = i + 1 == op.Fleets.Count;
            task.Strike = last ? null : op.Fleets[i + 1].Fleet.Ref;
            return (c, last);
        }
        task.Strike = null;
        return (null, true);
    }

    /// <summary>One member fleet of the strike walk; null means nothing for it.</summary>
    static Command? Strike(WorldModel m, AiFleet f, Owner side)
    {
        if (m.FleetsById.GetValueOrDefault(f.Fleet.Ref) != f) return null; // No record: skipped, nothing written.
        if (!f.Claimed || f.Enroute) return null;
        if (f.Target is null || f.Location != f.Target || m.Record(f.Target) is not { } s) { DropClaim(m, f); return null; }
        bool deathStar = f.DeathStar && s.VictoryCharacters >= 1;
        int type = 0;
        if (f.AttackClaim) { if (AttackCheck(m, f, s)) return null; }
        else if (f.AssaultClaim) type = deathStar ? 0x223 : AssaultCheck(m, f, s) ? 0x234 : BombardType(m, f, s);
        else if (f.BombardClaim) type = deathStar ? 0x223 : BombardType(m, f, s);
        else if (MoveCheck(f, s))
        {
            var regiment = m.ShipsOf(f).OrderBy(x => x.Ship.Ref).Where(x => x.UsableTroops > 0)
                .Select(x => m.Contents(x.Ship).FirstOrDefault(t => Family.IsTroop(t.Type) && m.Usable(t))).FirstOrDefault(t => t is not null);
            if (regiment is not null) return new Command(0x201, side) { Targets = [(regiment, 0)], ArgRef = s.System };
            return null;
        }
        if (type != 0) return new Command(type, side) { Targets = [(f.Fleet, 0)], ArgRef = s.System };
        DropClaim(m, f);
        return null;
    }

    // ---- conditions ----

    /// <summary>The attack check: seven rules, applied in order.</summary>
    static bool AttackCheck(WorldModel m, AiFleet f, AiSystem s)
    {
        bool fleet = s.EnemyFleet, enemy = s.Enemy;
        int armed = (fleet ? s.EnemyFirepower : 0) + (enemy ? s.FighterFirepower : 0), anti = (fleet ? s.EnemyAntiFighterFirepower : 0) + (enemy ? s.FighterAntiFighterFirepower : 0),
            fighters = (fleet ? s.EnemyFleetFighters : 0) + (enemy ? s.Fighters : 0);
        if (armed <= 0) return false;
        if (armed * 12 / 10 >= f.Firepower) return false;
        if (fighters <= 0) return true;
        if (f.AntiFighterFirepower <= anti) return false;
        if (!f.DeathStar && !s.EnemyDeathStar) return f.AntiFighter > 0 || f.UsableFighters >= fighters;
        return f.UsableFighters > fighters;
    }

    /// <summary>
    /// The assault check: the system is neither own nor marked, and it is populated. Reach and enemy fighters are 0 at the
    /// fleet's own location. Passes when the fleet's range is at least the reach, usable troops are at least the theater's troop
    /// requirement, and either the base's count is above 0 or the fleet's value is at least the enemy fighters. No Death Star
    /// alternative.
    /// </summary>
    static bool AssaultCheck(WorldModel m, AiFleet f, AiSystem s)
    {
        if (s.Own || s.ManyGenCores || !s.OwnedOrPopulated) return false;
        bool away = f.Location != s.System;
        int reach = away ? Math.Max(s.FighterAntiFighterFirepower, m.BestAntiFighterFirepower) : 0, enemy = away ? s.Fighters : 0;
        return f.AntiFighterFirepower >= reach && f.UsableTroops >= s.TroopsToTake && (f.AntiFighter > 0 || f.UsableFighters >= enemy);
    }

    /// <summary>The bombard check: returns 0, 0x222 or 0x220.</summary>
    static int BombardType(WorldModel m, AiFleet f, AiSystem s)
    {
        if (s.Sector is not { } sector || !s.Enemy) return 0;
        int r = f.Location == s.System ? 0 : Math.Max(s.FighterAntiFighterFirepower, m.BestAntiFighterFirepower);
        if (r > f.AntiFighterFirepower || f.Bombardment <= s.GenCoreStrength || s.Batteries != 0) return 0;
        int t = s.GenCores + s.UsableTroops, h = (sector.Held - sector.EnemySystems + 1) / 2;
        if (h < 1) return s.UsableTroops > 0 || s.ProducerMaintenance > 0 ? 0x222 : 0;
        return t > h && s.UsableTroops > 0 ? 0x220 : 0;
    }

    /// <summary>The move check: not populated, neutral (Inferred: neither own nor enemy), no enemy fleet, and the fleet has usable troops.</summary>
    static bool MoveCheck(AiFleet f, AiSystem s) => !s.OwnedOrPopulated && !s.Own && !s.Enemy && !s.EnemyFleet && f.UsableTroops > 0;
}

/// <summary>
/// Planner 8, the fleet-operation manager. State 0 is empty. State 1 creates an operation for a theater sector that has none
/// (when the model's count is below 0 and every operation has the create mark), or removes one (when the count is above 0).
/// State 2 takes the next operation by key cursor, with no wrap. State 3 assigns theaters: keep the current one; else the
/// nearest sector needing an operation; else random keys over theaters without one, then over all theaters. The sector is
/// bound to the operation, and the task is of the theater kind. State 4 runs the operation's produce (exits as planner 9
/// state 3).
/// </summary>
public sealed class FleetOpPlanner() : Planner(8)
{
    int _current, _next;

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { Done = true; return null; }
        var m = controller.Model;
        switch (State)
        {
            case 0:
                State = 1;
                return null;
            case 1:
            {
                int total = m.TheaterBalance;
                if (total < 0 && m.FleetOps.Values.All(o => o.CharacterWaiting))
                {
                    var op = new FleetOp(m.NextFleetOpKey++);
                    m.FleetOps[op.Key] = op;
                }
                else if (total > 0 && (m.FleetOps.Values.FirstOrDefault(o => o.Theater is null) ?? m.FleetOps.Values.FirstOrDefault(o => o.Theater!.TheaterBalance > 0)) is { } gone)
                {
                    FleetOps.Reset(m, gone);
                    m.FleetOps.Remove(gone.Key);
                }
                State = 2;
                return null;
            }
            case 2:
            {
                var op = m.FleetOps.Values.FirstOrDefault(o => _next != 0 && o.Key == _next) ?? m.FleetOps.Values.FirstOrDefault();
                if (op is null) { (State, Done, _next) = (0, true, 0); return null; }
                _current = op.Key;
                _next = m.FleetOps.Keys.Where(k => k > op.Key).DefaultIfEmpty(0).Min();
                State = 3;
                return null;
            }
            case 3:
            {
                if (!m.FleetOps.TryGetValue(_current, out var op) || !Assign(m, op)) { (State, Done) = (0, true); return null; }
                State = 4;
                return null;
            }
            default: // 4: the operation's produce
            {
                var (c, finished) = m.FleetOps.TryGetValue(_current, out var op) ? FleetOps.Produce(game, m, op, controller.Side) : (null, true);
                if (finished) (State, Done) = (0, true);
                else if (c is not null) { Priority++; Done = true; }
                return c;
            }
        }
    }

    /// <summary>
    /// Theater pick (planner 8): keep the current one; else, for a sector theater, the nearest pick by distance after a reset,
    /// falling through to the random lists when none; else the random lists, first with one set of requirement flags, then with
    /// another. Every success ends in <see cref="Rebind"/>.
    /// </summary>
    static bool Assign(WorldModel m, FleetOp op)
    {
        if (op.Theater is { } t && t.TheaterBalance == 0 && t.FleetOpKey == op.Key) return Rebind(m, op);
        AiSector? pick = null;
        if (op.Theater is { } old)
        {
            var stack = new ListStack();
            stack.Push(m.SectorDistanceFrom(old, x => x.OpenTheater, 2));
            pick = stack.Pick() as AiSector;
            FleetOps.Reset(m, op);
        }
        if (pick is null)
        {
            var stack = new ListStack();
            stack.Push(m.SectorRandomList(x => x.OpenTheater && x.TheaterMark, 2));
            pick = stack.Pick() as AiSector;
            if (pick is null)
            {
                var all = new ListStack();
                all.Push(m.SectorRandomList(x => x.OpenTheater, 2));
                pick = all.Pick() as AiSector;
            }
        }
        if (pick is null) return false;
        // Bind: a pick that is already taken leaves no theater: push 0, reset, then return false.
        if (pick.FleetOpKey != 0)
        {
            FleetOps.PushEntry(m, op, 0);
            return Rebind(m, op);
        }
        // Binds the sector to the op key (dirtying the sector) and sets the op's entry; the theater's entry is pushed at once.
        pick.FleetOpKey = op.Key;
        m.DirtySector(pick);
        op.Theater = pick;
        FleetOps.PushEntry(m, op, pick.Entry);
        return Rebind(m, op);
    }

    /// <summary>
    /// Reset. The theater is the op's theater. A task of another kind than the theater's kind is deleted. With no task, the op's
    /// two counters are zeroed, the produce state is 0, refresh is off, and a new task of the theater's kind (1-6) is made; the
    /// theater's entry is pushed when it differs from the op's entry. Still no task: reset, fail.
    /// </summary>
    static bool Rebind(WorldModel m, FleetOp op)
    {
        if (op.Theater is not { } s) { FleetOps.Reset(m, op); return false; }
        int kind = s.TheaterKind;
        if (op.Task is { } task && task.Type != kind) op.Task = null;
        if (op.Task is null)
        {
            op.ClearStatus();
            (op.State, op.Refresh) = (0, false);
            if (kind is >= 1 and <= 6) op.Task = new FleetTask(kind);
        }
        FleetOps.PushEntry(m, op, s.Entry);
        if (op.Task is not null) return true;
        FleetOps.Reset(m, op);
        return false;
    }
}
