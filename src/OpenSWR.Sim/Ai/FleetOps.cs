using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>
/// Fleet-op produce, with the balance builders, the destination chooser, the status, the character-op requests,
/// the flagship, the task machine, and the task slots for need targets, mission requests and status.
/// </summary>
public static partial class FleetOps
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
        (op.State, op.Refresh, op.FighterNeed) = ((int)FleetOpState.Start, false, 0);
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
        switch ((FleetOpState)op.State)
        {
            case FleetOpState.Start:
                (op.Refresh, op.State) = (true, (int)FleetOpState.SplitDamaged);
                return (null, false);
            case FleetOpState.SplitDamaged: // Zeroes two counters, then splits the damaged ships of the first such member fleet.
            {
                op.ClearStatus();
                op.State = (int)FleetOpState.Needs;
                var f = op.Fleets.FirstOrDefault(f => (f.DamagedMoving || f.DamagedStopped)
                    && !f.Claimed && !f.EnrouteShip && !f.Enroute);
                if (f is null || f.Location is null) return (null, false);
                var split = m.ShipsOf(f).Where(s => op.Ships.Contains(s) && (s.DamagedStopped || s.DamagedMoving)
                    && !s.Enroute).ToList();
                foreach (var s in split) Leave(op, s);
                return (split.Count == 0 ? null : new Command(CommandType.CreateFleet, side) { Targets = [.. split.Select(s => (s.Ship, GroupMode.Object))], ArgRef = f.Location }, false);
            }
            case FleetOpState.Needs:
                Needs(m, op);
                op.State = (int)FleetOpState.Balance;
                return (null, false);
            case FleetOpState.Balance: // the shortfall builders and their low flags after them, else the surplus builder
            {
                op.State = (int)FleetOpState.Requests;
                Command? c = null;
                if (Short(op))
                {
                    c = ShortfallNear(m, op, side) ?? ShortfallAny(m, op, side);
                    ShortBits(op);
                }
                if (c is null && Surplus(op)) c = Surplus(m, op, side);
                return (c, false);
            }
            case FleetOpState.Requests:
                MissionRequests(op, side);
                Status(op);
                CharacterRequests(op);
                if (op.CharacterRequest != CharacterOpType.None || op.RetireRequest is not null) op.CharacterRequested = true;
                op.State = (int)FleetOpState.Task;
                return (null, false);
            case FleetOpState.Task:
            {
                var task = op.Task!;
                var (c, done) = Machine(game, m, op, task, side);
                if (done) op.State = (int)FleetOpState.Finish;
                return (c, false);
            }
            default: // Finish: flagship, then the gather system, then the slot step.
            {
                Flagship(m, op);
                if (op.CharacterOpAttached) Gather(m, op);
                else op.Gather = null;
                TaskStatus(m, op, op.Task!);
                if (!op.AnyShort) op.Complete = op.StrongEnough = op.NotEmpty = true;
                else if (!op.HasAny) op.Empty = true;
                else if (!op.ForceShort) op.StrongEnough = op.NotEmpty = true;
                else op.NotEmpty = true;
                op.State = (int)FleetOpState.Start;
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
        var type = task.Type;
        if (type == FleetTaskType.Site) SitePick(m, task, s);
        var site = type == FleetTaskType.Site ? m.Record(task.Site) : null;
        switch (type)
        {
            case FleetTaskType.Strike:
                task.Firepower = Math.Max(task.Firepower, X12(s.EnemyFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X12(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, X12(s.MaxEnemyFighters + s.EnemyFleetFighters));
                task.Bombardment = Math.Max(task.Bombardment, Math.Max(X11(s.MinUnguardedGenCoreStrength), m.BestBombardment));
                break;
            case FleetTaskType.Scout:
                task.Firepower = Math.Max(task.Firepower, Math.Max(X15(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X15(s.MaxEnemyFighterAntiFighterFirepower) + X15(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, Math.Max(X15(s.MaxEnemyFighters + s.EnemyFleetFighters), m.BestFighterCapacity));
                break;
            case FleetTaskType.Reserve:
                task.Firepower = Math.Max(task.Firepower, Math.Max(X12(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X12(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, X12(s.EnemyFleetFighters + s.MaxEnemyFighters));
                break;
            case FleetTaskType.Site:
            {
                task.Firepower = Math.Max(task.Firepower, Math.Max(X15(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X15(s.EnemyAntiFighterFirepower) + (site is null ? 0 : X15(site.FighterAntiFighterFirepower)), m.BestAntiFighterFirepower));
                int fighters = Math.Max(X11(s.EnemyFleetFighters + s.MaxEnemyFighters), m.BestFighterCapacity);
                task.FighterCapacity = Math.Max(task.FighterCapacity, fighters);
                task.TroopCapacity = Math.Max(task.TroopCapacity, site?.TroopsToTake ?? fighters); // As coded: without a site, the fighter value is used.
                break;
            }
            case FleetTaskType.Defence:
                task.Firepower = Math.Max(task.Firepower, X15(s.EnemyFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, s.EnemyNoBatteries ? Math.Max(X15(s.EnemyAntiFighterFirepower), m.BestAntiFighterFirepower) : X15(s.EnemyAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, X12(s.MaxEnemyFighters + s.EnemyFleetFighters));
                task.TroopCapacity = Math.Max(task.TroopCapacity, 1);
                break;
            default: // Assault
                task.Firepower = Math.Max(task.Firepower, Math.Max(X15(s.EnemyFirepower), m.BestArmedFirepower));
                task.AntiFighterFirepower = Math.Max(task.AntiFighterFirepower, Math.Max(X15(s.EnemyAntiFighterFirepower) + X15(s.MaxEnemyFighterAntiFighterFirepower), m.BestAntiFighterFirepower));
                task.FighterCapacity = Math.Max(task.FighterCapacity, Math.Max(X15(s.EnemyFleetFighters + s.MaxEnemyFighters), m.BestFighterCapacity));
                task.TroopCapacity = Math.Max(task.TroopCapacity, Math.Max(s.MaxEnemyTroopsToTake, m.BestTroopCapacity));
                break;
        }
        if (type is FleetTaskType.Strike or FleetTaskType.Scout or FleetTaskType.Reserve) (task.TroopCapacity, task.DeathStars, task.Troops) = (0, 0, 0);
        else
        {
            if (m.OwnDeathStar && s.VictoryCharacters > 0) task.DeathStars = 1; // Inferred: ":= 1 if" leaves the target otherwise
            task.Troops = op.TroopCapacitySum;
        }
        if (m.DeathStarShieldAvailable) task.Interdictors = 1; // Inferred, as the Death Star target
        (task.Fighters, task.AntiFighters) = (op.FighterCapacitySum, 1);
    }

    /// <summary>
    /// Site pick for type 4: only with a sector theater. The theater argument (shipyards, training or construction) selects one of three site fields.
    /// Both candidate lists are built on every call (their ties draw). Every call stores the first rank-sum pick and picks
    /// on while the stored site's field is below 1; when exhausted, none. There is no keep of the old site (an earlier keep
    /// was withdrawn).
    /// </summary>
    static void SitePick(WorldModel m, FleetTask task, AiSector s)
    {
        Func<AiSystem, int>? k = s.TheaterArgument switch { TheaterArgument.Shipyards => r => r.Shipyards, TheaterArgument.Training => r => r.TrainingFacilities, TheaterArgument.Construction => r => r.ConstructionYards, _ => null };
        if (k is null) { task.Site = null; return; }
        var stack = new ListStack();
        stack.Push(m.SystemList(s, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, k, RankOrder.Descending));
        stack.Push(m.SystemList(s, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, r => r.UnrestTroops, RankOrder.Descending));
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
            FleetTaskType.Site => m.Record(task.Site) is { } site ? b.UnrestTroops - b.Troops + site.TroopsToTake : 0,
            FleetTaskType.Defence => s is null ? 0 : b.UnrestTroops - b.Troops - s.Held + 10,
            FleetTaskType.Assault => s is null ? 0 : s.EnemyTroopsToTake + b.UnrestTroops - b.Troops,
            _ => 0,
        };
    }

    /// <summary>State 1's mark priority: move &gt; attack &gt; assault &gt; bombard.</summary>
    static readonly ClaimKind[] MarkPriority = [ClaimKind.Move, ClaimKind.Attack, ClaimKind.Assault, ClaimKind.Bombard];
}
