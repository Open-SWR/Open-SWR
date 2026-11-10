using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

public static partial class FleetOps
{
    // ---- task machine ----

    static (Command?, bool) Machine(GameState game, WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        switch ((FleetTaskState)task.State)
        {
            case FleetTaskState.ClaimMarks: // Clears the marks on the theater's ten systems, then sets one mark per member fleet's claimed system; plain writes, no dirty marking.
                if (op.Theater is { } th)
                    foreach (var r in th.Systems) r.ClearClaimMarks();
                foreach (var f in op.Fleets)
                    if (m.Record(f.Target) is { } r && f.Claimed) Mark(r, MarkPriority.First(c => HasClaim(f, c)));
                task.State = (int)FleetTaskState.Base;
                return (null, false);
            case FleetTaskState.Base:
                task.State = (int)(Base(m, op, task) ? FleetTaskState.Troops : FleetTaskState.NextFleet);
                return (null, false);
            case FleetTaskState.Troops:
                task.State = (int)FleetTaskState.Fighters;
                return (task.Type is FleetTaskType.Strike or FleetTaskType.Scout or FleetTaskType.Reserve ? TroopUnload(m, op, side) : TroopsIntoFleets(m, op, task, side), false);
            case FleetTaskState.Fighters:
                task.State = (int)FleetTaskState.NextFleet;
                return (FightersIntoFleets(m, op, side), false);
            case FleetTaskState.NextFleet: // From the cursor's node (else the leftmost) in key order, wrapping; the cursor moves to that node.
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
                        task.State = (int)FleetTaskState.Characters;
                        return (null, false);
                    }
                task.State = (int)FleetTaskState.Strike;
                return (null, false);
            }
            case FleetTaskState.Characters:
            {
                // (a) only with the flagship (a capital ship) among the task fleet's ships. When the op has its first flag, the flagship is
                // flagged with no command. Else, when the op has its second flag and a gather system without a Blockade, a Move command
                // moves the task fleet there and the flag is set (no own or location test: a fleet already there gets the move again).
                // (b) Runs only when (a) built no command; its Move never sets the flag (state 14 acts only on the flag, and a part-(b)
                // command sets state 7).
                bool flag = false;
                Command? c = null;
                if (op.Flagship is { } fs && Family.Of(fs.Ship.Type) is >= Families.CapitalShip and < Families.Fighter && task.Fleet is { } tf6 && m.ShipsOf(tf6).Contains(fs))
                {
                    if (op.CharacterAtShip) flag = true;
                    else if (op.CharacterWaiting && op.Gather is { } g && m.Record(g) is { } gr && !gr.Blockade)
                        (flag, c) = (true, new Command(CommandType.Move, side) { Targets = [(tf6.Fleet, GroupMode.Object)], ArgRef = g });
                }
                c ??= Characters(m, op, task, side);
                task.State = (int)(flag ? FleetTaskState.Strike : FleetTaskState.Attack);
                return (c, false);
            }
            case FleetTaskState.Attack: task.State = (int)(Attack(m, op, task) ? FleetTaskState.Destination : FleetTaskState.Assault); return (null, false);
            case FleetTaskState.Assault: task.State = (int)(Assault(m, op, task) ? FleetTaskState.Destination : FleetTaskState.MoveTarget); return (null, false);
            case FleetTaskState.MoveTarget: task.State = (int)(MoveTarget(m, op, task) ? FleetTaskState.Destination : FleetTaskState.Bombard); return (null, false);
            case FleetTaskState.Bombard: task.State = (int)(Bombard(m, op, task) ? FleetTaskState.Destination : FleetTaskState.Regroup); return (null, false);
            case FleetTaskState.Regroup:
                task.State = (int)FleetTaskState.Strike;
                return (Regroup(m, op, task, side), false);
            case FleetTaskState.Destination: // No location test
                task.State = (int)FleetTaskState.Strike;
                return (task.Fleet is { Target: { } dest } tf ? new Command(CommandType.Move, side) { Targets = [(tf.Fleet, GroupMode.Object)], ArgRef = dest } : null, false);
            case FleetTaskState.Strike: // Done: back to state 0 and produce is done, with the returned command
            {
                var (c, done) = Strike(m, op, task, side);
                if (done) task.State = (int)FleetTaskState.Idle;
                return (c, done);
            }
            default: // Idle (Inferred: a finished machine starts at state 1 on the next produce pass)
                task.State = (int)FleetTaskState.ClaimMarks;
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
        if (task.Type is FleetTaskType.Site or FleetTaskType.Defence or FleetTaskType.Assault)
        {
            if (m.Record(op.Base) is { } b)
            {
                if (b.Own && !b.Blockade) { BaseMarks(m, b, task.Type); return true; }
                if (task.Type == FleetTaskType.Site) (b.AssaultBase, b.Base) = (false, false);
                else (b.AssaultBase, b.DefenseBase, b.Base) = (false, false, false);
                m.DirtySystem(b);
                op.Base = null;
            }
            var site = Pick(m.SystemList(t, r => r.KeySystem && r.Own && !r.Blockade && !r.Base, r => r.TroopSurplus, RankOrder.Ascending))
                ?? Pick(m.SystemList(t, r => r.Own && !r.Blockade && !r.Base, r => r.TroopSurplus, RankOrder.Ascending))
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
        var found = Pick(m.SystemList(t, r => r.KeySystem && r.Own && !r.Blockade && !r.Base, r => r.FighterBalance, RankOrder.Ascending))
            ?? Pick(m.SystemList(t, r => r.Own && !r.Blockade && !r.Base, r => r.FighterBalance, RankOrder.Ascending))
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
    static void BaseMarks(WorldModel m, AiSystem b, FleetTaskType type, bool always = false)
    {
        if (!always && (type == FleetTaskType.Defence ? b.DefenseBase : b.AssaultBase)) return;
        if (type == FleetTaskType.Defence) b.AssaultBase = false;
        if (type == FleetTaskType.Assault) b.DefenseBase = false;
        if (type == FleetTaskType.Defence) b.DefenseBase = b.Base = true;
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
        op.FighterNeed = Members(m, op, f => f.Own, f => f.FighterRoom, RankOrder.Ascending).Entries.Sum(e => e.Value) - b.Fighters;
        return op.FighterNeed > 0 ? op.Base : null;
    }

    /// <summary>Every fleet record of the op that passes (model order), valued by <paramref name="value"/>.</summary>
    static RankedList Members(WorldModel m, FleetOp op, Func<AiFleet, bool> pass, Func<AiFleet, int> value, RankOrder dir) =>
        new(m.FleetRecords().Where(f => f.OpKey == op.Key).Where(pass).Select(f => ((object)f, value(f))), dir, m.Rand);

    /// <summary>Unload slot for types 1-3: the first member fleet at an own system with usable troops unloads them, then a Move command to the location.</summary>
    static Command? TroopUnload(WorldModel m, FleetOp op, Owner side)
    {
        var stack = new ListStack();
        stack.Push(Members(m, op, f => f.Own && f.HasTroops && !f.Claimed && !f.Enroute, f => f.UsableTroops, RankOrder.Ascending));
        while (stack.Pick() is AiFleet f)
        {
            if (m.Record(f.Location) is not { } at || !at.Own) continue;
            var troops = m.TroopsAboard(f, s => s.Own && !s.Free && !s.Enroute);
            if (troops.Count > 0) return new Command(CommandType.Move, side) { Targets = [.. troops.Select(x => (x, GroupMode.Object))], ArgRef = f.Location };
        }
        return null;
    }

    /// <summary>
    /// Unload slot for types 4-6: only with base surplus above 0. Member fleets are taken by free troop slots (direction 1) until
    /// one is at the base. The count n is the smaller of the fleet's free slots and the base surplus; that many usable own troops
    /// of the base are chosen, each the one with the largest attack value not yet chosen (type 5 ranks by defence value first,
    /// then attack), and each goes to the fleet by a Move command. Inferred getters: attack is the catalogue's attack value,
    /// defence is the catalogue's DefenseStrength; ties in key order.
    /// </summary>
    static Command? TroopsIntoFleets(WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        if (op.Base is not { } b || m.Record(b) is not { } br || br.TroopSurplus <= 0) return null;
        var stack = new ListStack();
        stack.Push(Members(m, op, f => f.Own && !f.Enroute, f => f.TroopRoom, RankOrder.Ascending));
        while (stack.Pick() is AiFleet f)
        {
            if (f.Location != b) continue;
            int n = Math.Min(f.TroopRoom, br.TroopSurplus);
            Product P(GameObject t) => m.Catalogue[t.Type] ?? Product.None;
            var usable = m.Contents(b).Where(x => Family.IsTroop(x.Type) && m.Usable(x) && m.See(x).Owner == m.Side);
            var ordered = task.Type == FleetTaskType.Defence ? usable.OrderByDescending(t => P(t).AntiFighterFirepower).ThenByDescending(t => P(t).Firepower) : usable.OrderByDescending(t => P(t).Firepower);
            var troops = ordered.Take(Math.Max(0, n)).ToList();
            return troops.Count == 0 ? null : new Command(CommandType.Move, side) { Targets = [.. troops.Select(x => (x, GroupMode.Object))], ArgRef = f.Fleet };
        }
        return null;
    }

    /// <summary>
    /// Fighter unload (state 4): only with the base's fighter surplus above 0. The first member fleet (by free fighter slots,
    /// direction 1) at the base with at least one free slot is used; n is the smaller of the fleet's free slots and the base
    /// surplus; the first n usable own fighters in list order each go to the fleet by a Move command.
    /// </summary>
    static Command? FightersIntoFleets(WorldModel m, FleetOp op, Owner side)
    {
        if (op.Base is not { } b || m.Record(b) is not { } br || br.UsableFighters <= 0) return null;
        var stack = new ListStack();
        stack.Push(Members(m, op, f => f.Own && !f.Enroute, f => f.FighterRoom, RankOrder.Ascending));
        while (stack.Pick() is AiFleet f)
        {
            if (f.Location != b || f.FighterRoom < 1) continue;
            var fighters = m.Contents(b).Where(x => Family.Of(x.Type) is >= Families.Fighter and < Families.Headquarters && m.Usable(x) && m.See(x).Owner == m.Side)
                .Take(Math.Min(f.FighterRoom, br.UsableFighters)).ToList();
            return fighters.Count == 0 ? null : new Command(CommandType.Move, side) { Targets = [.. fighters.Select(x => (x, GroupMode.Object))], ArgRef = f.Fleet };
        }
        return null;
    }

    /// <summary>
    /// State 6, the flagship and character step. (a) The flagship branches (a flag, or a Move whose target goes to the op's
    /// target slot, written by the gather-system step under the op's flag) are in the machine. (b) A fleet with an own character
    /// aboard, not Enroute, at an own system: the usable characters aboard ships with the matching flags are used, except on ships that are
    /// Enroute. An enemy-allegiance one (prisoners) joins the targets without triggering; an own-allegiance one joins and
    /// triggers when its agent's field is 0 or it is not one of the fleet op's character ops. The Move command needs a trigger.
    /// </summary>
    static Command? Characters(WorldModel m, FleetOp op, FleetTask task, Owner side)
    {
        if (task.Fleet is not { } f || !f.OwnCharacter || f.Enroute || m.Record(f.Location) is not { } at
            || !at.Own) return null;
        var go = new List<GameObject>();
        bool trigger = false;
        foreach (var s in m.ShipsOf(f).Where(s => !s.Enroute && (s.OwnCharacter || s.EnemyCaptured)))
            foreach (var x in m.Contents(s.Ship).Where(x => Family.Of(x.Type) is >= Families.MajorCharacter and < Families.Mission && m.Usable(x)))
            {
                if (!m.Agents.TryGetValue(x.Ref, out var a)) continue;
                go.Add(x);
                if (a.Own && (a.CharacterOpKey == 0 || op.CharacterOps.All(c => c.Key != a.CharacterOpKey))) trigger = true;
            }
        return !trigger ? null : new Command(CommandType.Move, side) { Targets = [.. go.Select(x => (x, GroupMode.Object))], ArgRef = f.Location };
    }
}
