using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

public static partial class MissionOps
{
    // ---- target search ----

    static AiSystem? Pick(params RankedList[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) stack.Push(l);
        return stack.Pick() as AiSystem;
    }

    static AiSector? PickSector(params RankedList[] lists)
    {
        var stack = new ListStack();
        foreach (var l in lists) stack.Push(l);
        return stack.Pick() as AiSector;
    }

    static void TargetSearch(GameState game, WorldModel m, MissionOp op)
    {
        op.TargetFound = false;
        // only recruitment, research 1-3, subdue, incite and diplomacy need the complete team; recon, espionage,
        // rescue, abduction and Death Star sabotage search on every pass.
        if (!op.TeamComplete && op.Class.Family is not (MissionKind.Reconnaissance or MissionKind.Espionage or MissionKind.Rescue or MissionKind.Abduction or MissionKind.DeathStarSabotage)) return;
        var target = op.Class.Family switch
        {
            MissionKind.Recruitment => Recruitment(m, op),
            MissionKind.Research => (OpSubtype)op.Class.Subtype switch
            {
                OpSubtype.ShipResearch => Research(m, op, r => r.ShipProduction, x => x.HasShipProduction, x => x.ShipProduction),
                OpSubtype.TroopResearch => Research(m, op, r => r.TrainingProduction, x => x.HasTrainingProduction, x => x.TrainingProduction),
                _ => Research(m, op, r => r.ConstructionProduction, x => x.HasConstructionProduction, x => x.ConstructionProduction),
            },
            MissionKind.SubdueUprising => Research(m, op, r => r.UprisingNoFleet, x => x.HasUprisingNoFleet, x => x.UprisingNoFleet),
            MissionKind.Diplomacy => Diplomacy(m, op),
            MissionKind.Reconnaissance => Recon(game, m, op),
            MissionKind.Espionage => Espionage(m, op),
            MissionKind.InciteUprising => Incite(m, op),
            MissionKind.Rescue or MissionKind.Abduction => RescueOrAbduction(m, op),
            _ => DeathStar(m, op),
        };
        if (target is null) return;
        op.Target = target.System;
        if (op.Class.Family is not (MissionKind.Rescue or MissionKind.Abduction or MissionKind.DeathStarSabotage)) op.Objective = target.System; // Inferred: system classes send to the target system
        op.TargetFound = true;
    }

    static AiSystem? Keep(WorldModel m, MissionOp op, Func<AiSystem, bool> test) => m.Record(op.Target) is { } r && test(r) ? r : null;

    /// <summary>Recruitment: keep while the target is a character site; else the first member's system when it is one; else by staging distance or by values.</summary>
    static AiSystem? Recruitment(WorldModel m, MissionOp op) =>
        Keep(m, op, r => r.CharacterSite)
        ?? (op.Team.Count > 0 && m.Where(op.Team[0].Agent) is { } at && m.Systems[at].CharacterSite ? m.Systems[at] : null)
        ?? ByStagingOrValue(m, op, x => x.HasCharacterSite, x => x.CharacterSites, r => r.CharacterSite);

    /// <summary>Research and subdue: keep while the flag is set; staging: sector distance to the target plus system distance; else the sector by its count, then the system by support.</summary>
    static AiSystem? Research(WorldModel m, MissionOp op, Func<AiSystem, bool> b, Func<AiSector, bool> s, Func<AiSector, int> count) =>
        Keep(m, op, b) ?? ByStagingOrValue(m, op, s, count, b);

    static AiSystem? ByStagingOrValue(WorldModel m, MissionOp op, Func<AiSector, bool> sectorBit, Func<AiSector, int> count, Func<AiSystem, bool> systemBit)
    {
        if (op.Staging is { } staging)
            return PickSector(m.SectorDistanceList(staging, sectorBit, RankOrder.Descending)) is { } sector
                ? Pick(m.SystemDistanceList(sector, staging, systemBit, RankOrder.Descending)) : null;
        return PickSector(m.SectorList(sectorBit, count, RankOrder.Ascending)) is { } x ? Pick(m.SystemList(x, systemBit, r => r.Support, RankOrder.Ascending)) : null;
    }

    /// <summary>Diplomacy: keep while its flag is set and the system has no mark; five steps; the target gets the diplomacy claim.</summary>
    static AiSystem? Diplomacy(WorldModel m, MissionOp op)
    {
        var found = Keep(m, op, r => r.DiplomacyTarget && !r.EnemyFleet);
        static bool none(AiSystem r) => r.DiplomacyMark;
        if (found is null && PickSector(m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Own, x => x.Support, RankOrder.Descending)) is { } s1 && s1.Support < 70)
            found = Pick(m.SystemRandomList(s1, r => r.DiplomacyTarget && r.Own && !none(r), RankOrder.Descending));
        if (found is null && op.Staging is { } staging)
        {
            if (PickSector(m.SectorDistanceList(staging, x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, RankOrder.Descending)) is { } s2)
                found = Pick(m.SystemRandomList(s2, r => r.DiplomacyTarget && r.Neutral && !none(r), RankOrder.Descending));
            if (found is null && m.Record(staging)?.Sector is { } own)
                found = Pick(m.SystemDistanceList(own, staging, r => r.DiplomacyTarget && !none(r), RankOrder.Descending));
        }
        if (found is null && PickSector(m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, x => x.OwnSystems, RankOrder.Ascending), m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, x => x.NeutralSystems, RankOrder.Ascending),
                m.SectorRandomList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget && x.Neutral, RankOrder.Ascending)) is { } s4)
            found = Pick(m.SystemRandomList(s4, r => r.DiplomacyTarget && r.Neutral && !none(r), RankOrder.Ascending));
        if (found is null && PickSector(m.SectorList(x => x.HasDiplomacyTarget && x.OpenDiplomacyTarget, x => x.OwnSystems, RankOrder.Ascending), m.SectorRandomList(x => x.HasDiplomacyTarget, RankOrder.Ascending)) is { } s5)
            found = Pick(m.SystemRandomList(s5, r => r.DiplomacyTarget && !none(r), RankOrder.Descending));
        if (found is not null) found.DiplomacyMark = true;
        return found;
    }

    /// <summary>
    /// Recon: keep while the flag is set; else the old marked sector loses its recon mark; a sector by random keys (or by staging
    /// distance; Inferred: whenever staging is a system) becomes the op's marked sector, none included, marked only when a system
    /// is found. Its systems come by days since the last visit (model count below 1) or by random keys (direction 2 with staging,
    /// direction 1 without).
    /// </summary>
    static AiSystem? Recon(GameState game, WorldModel m, MissionOp op)
    {
        if (Keep(m, op, r => r.NotOwn) is { } kept) return kept;
        if (op.Marked is { } old) old.ReconMark = false;
        bool few = m.SectorsWithoutKey < 1;
        bool excl(AiSector x) => x.ReconMark || !few && x.AllExplored || x.Theater;
        RankOrder dir = few ? RankOrder.Ascending : RankOrder.Descending;
        var sector = PickSector(op.Staging is { } st ? m.SectorDistanceList(st, x => x.HasNotOwn && !excl(x), RankOrder.Descending) : m.SectorRandomList(x => x.HasNotOwn && !excl(x), dir));
        op.Marked = sector; // None included.
        if (sector is null) return null;
        var found = few ? Pick(m.SystemDaysList(sector, r => r.NotOwn, RankOrder.Ascending))
            : Pick(m.SystemRandomList(sector, r => r.NotOwn && !r.Explored, op.Staging is null ? RankOrder.Ascending : RankOrder.Descending));
        if (found is not null) sector.ReconMark = true; // mark only with a target
        return found;
    }

    /// <summary>Espionage: keep while the system is enemy and the flag is set; else the old sector loses its espionage mark; a sector by random keys (staging: distance) becomes the op's marked sector; systems by days; the mark is set only with a target.</summary>
    static AiSystem? Espionage(WorldModel m, MissionOp op)
    {
        if (Keep(m, op, r => r.Enemy && r.Claimed) is { } kept) return kept;
        if (op.Marked is { } old) old.EspionageMark = false;
        static bool req(AiSector x) => x.HasClaimed && x.Enemy;
        static bool excl(AiSector x) => x.EspionageMark || x.Theater;
        var sector = PickSector(op.Staging is { } st ? m.SectorDistanceList(st, x => req(x) && !excl(x), RankOrder.Descending) : m.SectorRandomList(x => req(x) && !excl(x), RankOrder.Ascending));
        op.Marked = sector; // None included.
        if (sector is null) return null;
        var found = Pick(m.SystemDaysList(sector, r => r.Claimed && r.Enemy, RankOrder.Ascending));
        if (found is not null) sector.EspionageMark = true; // mark only with a target
        return found;
    }

    /// <summary>Incite: keep while the flag is set and the system has no mark; staging distance lists, else values and random keys.</summary>
    static AiSystem? Incite(WorldModel m, MissionOp op)
    {
        if (Keep(m, op, r => r.EnemySupported && !r.EnemyFleet) is { } kept) return kept;
        if (op.Staging is { } st)
            return PickSector(m.SectorDistanceList(st, x => x.HasEnemySupported && x.Enemy, RankOrder.Descending)) is { } s ? Pick(m.SystemDistanceList(s, st, r => r.EnemySupported && r.Enemy, RankOrder.Descending)) : null;
        return PickSector(m.SectorList(x => x.HasEnemySupported && x.Enemy, x => x.EnemySystems, RankOrder.Ascending), m.SectorRandomList(x => x.HasEnemySupported && x.Enemy, RankOrder.Ascending)) is { } x
            ? Pick(m.SystemList(x, r => r.EnemySupported && r.Enemy, r => r.Support, RankOrder.Ascending)) : null;
    }

    /// <summary>The class's decoy count for rescue, abduction and Death Star.</summary>
    const int DecoyCap = 4;

    /// <summary>Decoys with an agent record, none of the agent's status flags set, and an agent location equal to the op's staging.</summary>
    static int DecoysAtStaging(WorldModel m, MissionOp op) =>
        op.Decoys.Count(d => m.Agents.ContainsKey(d.Agent.Ref) && MovableMember(d) && d.Location == op.Staging);

    /// <summary>
    /// Rescue and abduction: sectors by value; per sector in pick order two system lists go into one set; the first set system
    /// (key order) that the decoys at staging can handle is used; one random draw over the candidates (own captured characters or
    /// enemy majors in the system, usable views only; aboard non-own fleets: not collected). Rescue's keep test never passes.
    /// </summary>
    static AiSystem? RescueOrAbduction(WorldModel m, MissionOp op)
    {
        bool rescue = op.Class.Family == MissionKind.Rescue;
        if (!rescue && op.Target is StarSystem && op.Objective is Agent o)
        {
            // Objective cleared first; kept only while the record's location, resolved through the side's view parents, is the target
            op.Objective = null;
            if (m.Agents.TryGetValue(o.Ref, out var ar) && ar.Location is { } l && m.Where(l) == op.Target
                && ar.Major && ar.Enemy && !(ar.Captured || ar.Enroute)
                && m.Record(op.Target) is { } keep)
            {
                op.Objective = o;
                return keep;
            }
        }
        (op.Objective, op.Target) = (null, null);
        var sectors = new ListStack();
        sectors.Push(rescue ? m.SectorList(x => x.HasOwnCaptured, x => x.OwnCaptured, RankOrder.Ascending)
            : m.SectorList(x => x.HasEnemyMajor, x => x.EnemyMajors, RankOrder.Ascending));
        var set = new SortedSet<AiSystem>(Comparer<AiSystem>.Create((a, b) => a.System.Ref.CompareTo(b.System.Ref)));
        Func<AiSystem, int> value = rescue ? r => r.EnemyUnits : r => r.EnemyMajors;
        while (sectors.Pick() is AiSector s)
        {
            if (Pick(m.SystemList(s, r => rescue ? r.OwnCaptured : r.EnemyMajor, value, RankOrder.Ascending)) is { } a) set.Add(a);
            if (Pick(m.SystemList(s, r => rescue ? r.OwnCapturedAboard : r.EnemyMajorAboard, value, RankOrder.Descending)) is { } b) set.Add(b);
        }
        int n = DecoysAtStaging(m, op);
        // The first passing system becomes the target and the walk stops there, candidate or not (the target search keeps the target)
        if (set.FirstOrDefault(r => n == DecoyCap || r.EnemyUnits / 2 <= n) is not { } r) return null;
        op.Target = r.System;
        // The iterator walks usable views only
        var candidates = m.Agents.Values.Where(x => x.Character && m.Where(x.Agent) == r.System && m.ViewParent(x.Agent) == r.System
            && m.Usable(x.Agent) && (rescue ? x.Own && x.Captured
                : x.Enemy && x.Major)).ToList();
        if (candidates.Count == 0) return null;
        int i = m.Rand(candidates.Count);
        if (i >= candidates.Count) return null;
        op.Objective = candidates[i].Agent;
        return r;
    }

    /// <summary>
    /// Death Star sabotage: an objective ship with a ship record gives its fleet's location. Else the non-own Death Star fleets
    /// (sorted by their second value, direction 2) are all popped, their locations taken in order, and the first the team can
    /// handle (rescue rule) is used. Ship choice: the first non-own Death Star fleet there, and its first Death Star ship record.
    /// </summary>
    static AiSystem? DeathStar(WorldModel m, MissionOp op)
    {
        if (op.Objective is { } o && Family.Of(o.Type) is >= Families.DeathStar and < Families.Fighter && m.Ships.ContainsKey(o.Ref) && m.Record(m.Where(o)) is { } at) return at;
        op.Objective = null;
        var stack = new ListStack();
        stack.Push(m.FleetList(f => f.DeathStar && !f.Own, f => f.Units, RankOrder.Descending));
        int n = DecoysAtStaging(m, op);
        while (stack.Pick() is AiFleet f)
        {
            if (m.Record(f.Location) is not { } r || !(n == DecoyCap || r.EnemyUnits / 2 <= n) || !r.EnemyDeathStarSeen) continue;
            var ship = m.FleetRecords().Where(x => !x.Own && x.DeathStar && x.Location == r.System)
                .SelectMany(m.ShipsOf).FirstOrDefault(s => s.DeathStar);
            if (ship is null) continue;
            op.Objective = ship.Ship;
            return r;
        }
        return null;
    }

    // ---- staging ----

    static bool OwnFree(AiSystem r) => r.Own && !r.Blockade;

    /// <summary>
    /// Staging. The staging-found flag is cleared first; if the team is not full, staging is set to none. Steps: (1) a recorded
    /// staging system that is own and free sets the flag, else staging is set to none (a staging system with no record is left as
    /// it is). (2) With the flag clear and a recorded target system that is own and free, staging becomes the target; else staging
    /// is the target sector's random pick, and when that is none, the nearest search runs (its null results are written). (3) A
    /// staging system with no record test sets the flag. (4) Else the first team member whose agent location, resolved through
    /// the side's view parents to its system, is own and not blockaded sets the flag and becomes the staging system.
    /// </summary>
    static bool StagingSearch(WorldModel m, MissionOp op)
    {
        op.StagingFound = false;
        if (!op.TeamComplete) { op.Staging = null; return false; } // Team not full: staging is none.
        if (m.Record(op.Staging) is { } old)
        {
            if (OwnFree(old)) op.StagingFound = true;
            else op.Staging = null;
        }
        if (!op.StagingFound && m.Record(op.Target) is { } t)
        {
            if (OwnFree(t)) op.Staging = t.System;
            else if (t.Sector is { } sector)
            {
                op.Staging = Pick(m.SystemRandomList(sector, r => r.Own && !r.Blockade, RankOrder.Ascending))?.System;
                op.Staging ??= Nearest(m, sector)?.System;
            }
            else op.Staging = null;
        }
        if (op.Staging is not null) { op.StagingFound = true; return true; } // Step 3: no record test.
        var s = op.Team.Select(a => a.Location is { } l ? m.Record(m.Where(l)) : null).FirstOrDefault(r => r is not null && OwnFree(r)); // The agent's location gives its system.
        if (s is null) return false;
        op.Staging = s.System;
        op.StagingFound = true;
        return true;
    }

    /// <summary>Nearest search: every other sector in key order closer than the best so far gets a random-key list (own, excluding marked systems); a pick sets the best.</summary>
    static AiSystem? Nearest(WorldModel m, AiSector from) => Nearest(m, from, r => r.Own && !r.EnemyFleet);

    /// <summary>Nearest search with the caller's masks: every other sector closer than the best so far gets a random-key system list.</summary>
    internal static AiSystem? Nearest(WorldModel m, AiSector from, Func<AiSystem, bool> pass)
    {
        AiSystem? best = null;
        long bestDistance = long.MaxValue;
        foreach (var s in m.Sectors.Where(x => x != from))
        {
            long dx = s.Sector.Definition.X - from.Sector.Definition.X, dy = s.Sector.Definition.Y - from.Sector.Definition.Y;
            long d = dx * dx + dy * dy;
            if (d >= bestDistance) continue;
            if (Pick(m.SystemRandomList(s, pass, RankOrder.Ascending)) is { } pick) (best, bestDistance) = (pick, d);
        }
        return best;
    }

    /// <summary>
    /// Every movable member whose agent location (the first location seen, never refreshed) is not the staging system goes into
    /// one move command (team, then decoys). The flag is cleared at entry, and set only by a team member at staging.
    /// </summary>
    static Command? Moves(WorldModel m, MissionOp op)
    {
        var go = new List<GameObject>();
        op.AtStaging = false;
        foreach (var a in op.Team.Where(MovableMember))
            if (a.Location == op.Staging) op.AtStaging = true;
            else go.Add(a.Agent);
        foreach (var a in op.Decoys.Where(MovableMember))
            if (a.Location != op.Staging) go.Add(a.Agent);
        return go.Count == 0 || op.Staging is null ? null : new Command(CommandType.Move, op.Side) { Targets = [.. go.Select(g => (g, GroupMode.Object))], ArgRef = op.Staging };
    }

    /// <summary>
    /// Mission request, command CreateMission, with the objective, the mission type and the op key as its argument. Team and decoys whose
    /// agent is neither Enroute, on a mission, nor captured are each marked on mission. No CreateMission command with an empty team (Inferred: the
    /// builder's own test).
    /// </summary>
    static Command? Mission(WorldModel m, MissionOp op)
    {
        if (op.Objective is not { } objective || Family.Of(objective.Type) is 0 or 255) return null;
        var team = op.Team.Where(MovableMember).ToList();
        var decoys = op.Decoys.Where(MovableMember).ToList();
        if (team.Count == 0) return null;
        foreach (var a in team.Concat(decoys)) a.MissionSent = true;
        return new Command(CommandType.CreateMission, op.Side)
        {
            Targets = [.. team.Select(a => ((GameObject)a.Agent, GroupMode.Object))], Decoys = [.. decoys.Select(a => (GameObject)a.Agent)],
            ArgRef = objective, Fields = [(uint)op.Key, 0, op.Class.MissionType],
        };
    }

    /// <summary>Review: only with the mission view at phase 8; review without a valid target turns into recall; recall is command MissionOrder for the mission.</summary>
    static Command? Recall(WorldModel m, MissionOp op)
    {
        Command? c = null;
        if (op.Mission is { Phase: 8 } mission)
        {
            if (op.Review && !(m.Record(op.Target) is { } r && Valid(op, r))) op.Recall = true;
            if (op.Recall) c = new Command(CommandType.MissionOrder, op.Side) { Targets = [(mission, GroupMode.Object)] };
        }
        op.Recall = op.Review = false;
        return c;
    }

    /// <summary>The class's check that the target is still valid (Inferred: the class's keep test).</summary>
    static bool Valid(MissionOp op, AiSystem r) => op.Class.Family switch
    {
        MissionKind.Recruitment => r.CharacterSite, MissionKind.Diplomacy => r.DiplomacyTarget && !r.EnemyFleet, MissionKind.InciteUprising => r.EnemySupported && !r.EnemyFleet,
        MissionKind.SubdueUprising => r.UprisingNoFleet, _ => true,
    };
}
