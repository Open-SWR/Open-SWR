using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

public static partial class FleetOps
{
    // ---- target slots ----

    /// <summary>A fleet-op task's claim on its target system.</summary>
    enum ClaimKind { Attack, Assault, Bombard, Move }

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
        if (f.Firepower > 0 && task.Type == FleetTaskType.Site && m.Record(task.Site) is { } site && site.EnemyFleet && AttackCheck(m, f, site)) s = site;
        if (s is null && f.Firepower > 0) s = FirstPassing(m, x => AttackCheck(m, f, x), m.SystemList(t, r => r.EnemyFleet && !r.AttackMark, r => r.EnemyFirepower, RankOrder.Ascending));
        if (s is null && task.Type is FleetTaskType.Strike or FleetTaskType.Scout && f.AntiFighterFirepower > 0)
            s = FirstPassing(m, x => AttackCheck(m, f, x), m.SystemList(t, r => r.EnemyFighters && r.Enemy && !r.AttackMark, r => r.FighterAntiFighterFirepower, RankOrder.Ascending));
        if (s is null) return false;
        if (task.Type == FleetTaskType.Scout) task.Visited.Add(s.System); // Type 2 remembers the visited system.
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
            FleetTaskType.Site => m.Record(task.Site) is { } site && AssaultCheck(m, f, site) ? site : null,
            FleetTaskType.Assault => (t.VictoryCharacters > 0 ? FirstPassing(m, x => AssaultCheck(m, f, x), m.SystemList(t, r => r.Enemy && !excl(r), r => r.VictoryCharacters, RankOrder.Ascending)) : null)
                ?? FirstPassing(m, x => AssaultCheck(m, f, x), m.SystemList(t, r => r.Enemy && !excl(r), r => r.ProducerMaintenance, RankOrder.Ascending), m.SystemList(t, r => r.Enemy && r.OwnedOrPopulated && !excl(r), r => r.UnrestTroops, RankOrder.Descending)),
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
        if (task.Fleet is not { } f || op.Theater is not { } t || task.Type is FleetTaskType.Scout or FleetTaskType.Site || f.Bombardment < 1) return false;
        bool req(AiSystem r) => r.Enemy && (task.Type is not (FleetTaskType.Defence or FleetTaskType.Assault) || r.OwnedOrPopulated);
        static bool excl(AiSystem r) => r.AttackMark || r.AssaultMark || r.BombardMark || r.EnemyFleet;
        RankedList[] lists = task.Type switch
        {
            FleetTaskType.Strike => [m.SystemList(t, r => req(r) && !excl(r), r => r.GenCoreStrength, RankOrder.Descending), m.SystemList(t, r => req(r) && !excl(r), r => r.ProducerMaintenance, RankOrder.Ascending), m.SystemList(t, r => req(r) && !excl(r), r => r.Support, RankOrder.Ascending)],
            FleetTaskType.Reserve => [m.SystemList(t, r => req(r) && !excl(r), r => r.GenCoreStrength, RankOrder.Descending), m.SystemList(t, r => req(r) && !excl(r), r => r.Troops, RankOrder.Ascending)],
            _ => [m.SystemList(t, r => req(r) && !excl(r), r => r.GenCoreStrength, RankOrder.Descending), m.SystemList(t, r => req(r) && !excl(r), r => r.ProducerMaintenance, RankOrder.Ascending)],
        };
        var s = FirstPassing(m, x => BombardType(m, f, x) != 0, lists);
        if (s is null) return false;
        Claim(op, f, s, ClaimKind.Bombard);
        return true;
    }

    /// <summary>Move slot, type 5 with the fleet's value at least 1: a pick with the mark, excluding others, by another field (direction 1); then the move check.</summary>
    static bool MoveTarget(WorldModel m, FleetOp op, FleetTask task)
    {
        if (task.Type != FleetTaskType.Defence || task.Fleet is not { } f || op.Theater is not { } t || f.UsableTroops < 1) return false;
        var s = FirstPassing(m, x => MoveCheck(f, x), m.SystemList(t, r => r.Neutral && !r.Populated && !r.EnemyFleet && !r.AttackMark, r => r.FreeEnergy, RankOrder.Ascending));
        if (s is null) return false;
        Claim(op, f, s, ClaimKind.Move);
        return true;
    }

    /// <summary>
    /// State 12: with at least two member fleets, the nearest other op fleet record (own, excluding the flagged ones; the task
    /// fleet ranks too and is skipped when popped) at a location without the system mark gets a Move command moving the task
    /// fleet there. Otherwise (one fleet, or the walk found none) the slot's system gets a Move command for the task fleet.
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
                ? (l.Definition.X - at.Definition.X) * (l.Definition.X - at.Definition.X) + (l.Definition.Y - at.Definition.Y) * (l.Definition.Y - at.Definition.Y) : 0)), RankOrder.Descending, m.Rand));
            while (stack.Pick() is AiFleet other)
                if (other != f && m.Record(other.Location) is { } r && !r.EnemyFleet) return new Command(CommandType.Move, side) { Targets = [(f.Fleet, GroupMode.Object)], ArgRef = other.Fleet };
            // None found: falls through to the slot's system step
        }
        var dest = Fallback(m, op, task, f);
        if (dest is null) return null;
        // No location test here: the original repeats the move too (a no-effect MoveObject at execution); the
        // command budget of 10 per day limits it.
        return new Command(CommandType.Move, side) { Targets = [(f.Fleet, GroupMode.Object)], ArgRef = dest.System };
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
        var type = task.Type;
        AiSystem? s = null;
        var kind = type;
        if (kind is FleetTaskType.Strike or FleetTaskType.Scout or FleetTaskType.Defence or FleetTaskType.Assault && op.Fleets.Count == 1 && !(f.EnrouteShip || f.AboardEnroute)
            && (kind is not (FleetTaskType.Defence or FleetTaskType.Assault) || op.Base is null) && (kind is not (FleetTaskType.Scout or FleetTaskType.Assault) || m.BestArmedFirepower <= f.Firepower && m.BestAntiFighterFirepower <= f.AntiFighterFirepower))
        {
            if (kind == FleetTaskType.Scout)
            {
                s = Unvisited(m, task, t); // Visited picks are skipped, and the result is added.
                if (s is not null) task.Visited.Add(s.System);
            }
            else
                s = Pick(kind switch
                {
                    FleetTaskType.Strike => m.SystemDaysList(t, r => !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, RankOrder.Ascending),
                    FleetTaskType.Defence => m.SystemRandomList(t, r => !r.Explored && !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, RankOrder.Ascending),
                    _ => m.SystemRandomList(t, r => !r.Own && !r.EnemyFleet && !r.AttackMark && !r.AssaultMark && !r.BombardMark, RankOrder.Ascending),
                });
        }
        if (s is null && m.Record(op.Base) is { } b && (type == FleetTaskType.Site || !b.Blockade)) s = b;
        bool excl(AiSystem r) => type != FleetTaskType.Site && r.Blockade;
        s ??= Pick(m.SystemList(t, r => r.KeySystem && r.Own && !excl(r), r => r.GenCores, RankOrder.Descending));
        s ??= Pick(m.SystemList(t, r => r.ShipyardRole && r.TrainingRole && r.ConstructionRole && r.MineRole && r.DefenceRole && r.Own && !excl(r), r => r.ProducerMaintenance, RankOrder.Ascending));
        if (s is null && m.Record(f.Location) is { } at && at.Enemy) s = Pick(m.SystemDistanceList(t, f.Fleet, r => !r.Enemy && !r.EnemyFleet, RankOrder.Descending));
        return s;
    }

    /// <summary>
    /// State 14, the strike walk. For each member fleet from the cursor that has a claim and is not Enroute: away from its
    /// target it loses the claim; at its target only the attack check runs. Assault gives a BombardDestroy command (when the theater's
    /// Death Star field is at least 1), else Assault or the bombard type; bombard gives BombardDestroy or the bombard type; move takes the
    /// first ship in key order with usable troops, and its first usable troop gets a Move command to the theater. A failed
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
        CommandType type = 0;
        if (f.AttackClaim) { if (AttackCheck(m, f, s)) return null; }
        else if (f.AssaultClaim) type = deathStar ? CommandType.BombardDestroy : AssaultCheck(m, f, s) ? CommandType.Assault : BombardType(m, f, s);
        else if (f.BombardClaim) type = deathStar ? CommandType.BombardDestroy : BombardType(m, f, s);
        else if (MoveCheck(f, s))
        {
            var regiment = m.ShipsOf(f).OrderBy(x => x.Ship.Ref).Where(x => x.UsableTroops > 0)
                .Select(x => m.Contents(x.Ship).FirstOrDefault(t => Family.IsTroop(t.Type) && m.Usable(t))).FirstOrDefault(t => t is not null);
            if (regiment is not null) return new Command(CommandType.Move, side) { Targets = [(regiment, GroupMode.Object)], ArgRef = s.System };
            return null;
        }
        if (type != 0) return new Command(type, side) { Targets = [(f.Fleet, GroupMode.Object)], ArgRef = s.System };
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

    /// <summary>The bombard check: returns 0, BombardVariant2 or Bombard.</summary>
    static CommandType BombardType(WorldModel m, AiFleet f, AiSystem s)
    {
        if (s.Sector is not { } sector || !s.Enemy) return 0;
        int r = f.Location == s.System ? 0 : Math.Max(s.FighterAntiFighterFirepower, m.BestAntiFighterFirepower);
        if (r > f.AntiFighterFirepower || f.Bombardment <= s.GenCoreStrength || s.Batteries != 0) return 0;
        int t = s.GenCores + s.UsableTroops, h = (sector.Held - sector.EnemySystems + 1) / 2;
        if (h < 1) return s.UsableTroops > 0 || s.ProducerMaintenance > 0 ? CommandType.BombardVariant2 : 0;
        return t > h && s.UsableTroops > 0 ? CommandType.Bombard : 0;
    }

    /// <summary>The move check: not populated, neutral (Inferred: neither own nor enemy), no enemy fleet, and the fleet has usable troops.</summary>
    static bool MoveCheck(AiFleet f, AiSystem s) => !s.OwnedOrPopulated && !s.Own && !s.Enemy && !s.EnemyFleet && f.UsableTroops > 0;
}
