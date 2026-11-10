namespace OpenSWR.Sim.Ai;

public static partial class FleetOps
{
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
        (op.RequestKind, op.RequestSubtype, op.RetireKey) = (OpCodes.NoRequest, (int)OpSubtype.None, OpCodes.NoOp);
        if (op.Theater is not { } s) return;
        bool notOwn = (s.Enemy || s.Neutral), enemy = s.Enemy,
            enemyOrFleet = (s.Enemy || s.EnemyFleet);
        int abduction = op.ObjectOps((int)ObjectOpIndex.AbductionOrAssassination), espionage = op.ObjectOps((int)ObjectOpIndex.Espionage),
            recon = op.ObjectOps((int)ObjectOpIndex.Reconnaissance), sabotage = op.ObjectOps((int)ObjectOpIndex.Sabotage);
        var rules = new (MissionKind Kind, OpSubtype Subtype, bool Create, bool Retire)[]
        {
            (MissionKind.Reconnaissance, OpSubtype.None, recon < 2 && notOwn, recon > 2 || !notOwn && recon > 0),
            (MissionKind.Espionage, OpSubtype.None, espionage == 0, espionage > 1),
            (MissionKind.Sabotage, OpSubtype.None, sabotage == 0 && enemy, sabotage > 1 || !enemy && sabotage > 0),
            (side == Owner.Alliance ? MissionKind.Abduction : MissionKind.Assassination, OpSubtype.Abduction, abduction == 0 && enemyOrFleet, abduction > 1 || !enemyOrFleet && abduction > 0),
        };
        (MissionKind Kind, OpSubtype Subtype)? retire = null;
        foreach (var (kind, subtype, create, ret) in rules)
            if (create) { if (op.RequestKind == OpCodes.NoRequest) { (op.RequestKind, op.RequestSubtype) = ((int)kind, (int)subtype); op.MissionRequested = true; } }
            else if (ret) retire ??= (kind, subtype);
        foreach (var mo in op.MissionOps)
        {
            op.HasMissionOps = true;
            if (!(mo.TeamComplete && mo.DecoysDone)) op.OpWithoutTeam = true;
            if (mo.KeptMember) op.OpTeamed = true;
        }
        if (retire is { } r && op.MissionOps.Where(mo => !mo.Retired && MissionOps.Matches(mo.Class, r.Kind, (int)r.Subtype))
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
            else if (c.AtShip && c.KindIssued && c.Type == CharacterOpType.FleetGeneral) op.GeneralAtShip = true;
            if (!c.Assigned) op.OpWithoutTeam = true;
            else op.OpTeamed = true;
        }
    }

    /// <summary>Task slot for the character op: the type to create, and the op to retire.</summary>
    static void CharacterRequests(FleetOp op)
    {
        var type = op.TaskKind;
        var sector = op.Theater;
        int admirals = op.CharacterOpCount(CharacterOpType.FleetAdmiral), generals = op.CharacterOpCount(CharacterOpType.FleetGeneral),
            commanders = op.CharacterOpCount(CharacterOpType.FleetCommander);
        bool generalTask = type is FleetTaskType.Site or FleetTaskType.Assault;
        op.CharacterRequest = generalTask && generals < 1 && op.TroopCapacitySum > 0 && sector is { Enemy: true } ? CharacterOpType.FleetGeneral
            : admirals < 1 && op.Fleets.Count > 0 ? CharacterOpType.FleetAdmiral
            : commanders < 1 && op.FighterCapacitySum > 0 && (sector is { Enemy: true } || sector is { EnemyFleet: true }) ? CharacterOpType.FleetCommander
            : CharacterOpType.None;
        op.RetireRequest = op.CharacterOps.FirstOrDefault(c => c.Type switch
        {
            CharacterOpType.FleetAdmiral => admirals >= 2 || op.Fleets.Count == 0 && admirals >= 1,
            CharacterOpType.FleetGeneral => generalTask ? generals > 1 : generals > 0,
            _ => commanders > 1 || op.FighterCapacitySum == 0 && commanders > 0,
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
                    if (Top(m.ShipList(fleet, s => s.Own && s.Armed && !NotFlagship(s), s => s.ShieldRecharge, RankOrder.Ascending)) is { } b && b != cur
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
            if (Top(m.ShipList(f, s => s.Own && !NotFlagship(s), s => s.ShieldRecharge, RankOrder.Ascending)) is { } x && x.ShieldRecharge > (best?.ShieldRecharge ?? 0)) best = x;
        op.Flagship = best;
        if (best is not null) best.FlagshipMark = true;
        foreach (var s in op.Ships) s.FlagshipWanted = false;
    }
}
