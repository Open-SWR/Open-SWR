using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

/// <summary>Planner 13's phases (the planner's phase field, an int in the save format).</summary>
enum StrategyPhase
{
    /// <summary>Not set (the default value).</summary>
    None,
    /// <summary>Mode choice and the sub-object steps (both sides).</summary>
    Strategy,
    /// <summary>The headquarters machine (side 1); side 2 is done at once.</summary>
    Headquarters,
}

/// <summary>Planner 13's states within <see cref="StrategyPhase.Strategy"/> (the state field, an int in the save format).</summary>
enum StrategyPlannerState
{
    /// <summary>Empty call.</summary>
    Start,
    /// <summary>The mode choice.</summary>
    ModeChoice,
    /// <summary>One sub-object step per call (<see cref="StrategyStep"/>).</summary>
    SubStep,
}

/// <summary>The sub-object steps of planner 13 (the step index, an int in the save format). Steps 1 to 6 run in order, then the phase exits.</summary>
enum StrategyStep
{
    /// <summary>Index 0: an empty call.</summary>
    Empty,
    /// <summary>Theaters, only when they are due.</summary>
    Theaters,
    /// <summary>The theater check.</summary>
    TheaterCheck,
    /// <summary>Mission requests.</summary>
    MissionRequests,
    /// <summary>Character-op requests.</summary>
    CharacterRequests,
    /// <summary>Fleet-task balance: a garrison task is added or released.</summary>
    FleetTasks,
    /// <summary>The Coruscant and HQ mark (step 6, the default).</summary>
    Mark,
}

/// <summary>States of the headquarters machine (headquarters phase, side 1; the state field, an int in the save format).</summary>
enum HeadquartersState
{
    /// <summary>Any state below <see cref="Decide"/> moves to it.</summary>
    Start,
    /// <summary>Decide the headquarters site (its relocation check).</summary>
    Decide,
    /// <summary>The headquarters scrap order.</summary>
    Scrap,
    /// <summary>The relocation move.</summary>
    Relocate,
}

/// <summary>
/// Strategy mode (the mode field, an int in the save format). The names follow the mode's keep and choice conditions; the
/// original's own meaning of the modes is unknown.
/// </summary>
public enum StrategyMode
{
    None,
    /// <summary>Outgunned, not short for a quarter, and the troops are at most the enemy's to take.</summary>
    Defensive,
    /// <summary>Economy short with unheld sectors to take.</summary>
    ShortEconomy,
    /// <summary>Outgunned, not short, and troops aboard.</summary>
    OutgunnedTroops,
    /// <summary>Requests pending: the producer-facility turn requests.</summary>
    Requests,
    /// <summary>Dominant: not outgunned, not short, and the troops at most the enemy's to take.</summary>
    Dominant,
    /// <summary>Not outgunned, and troops aboard above the enemy's to take.</summary>
    Troops,
    /// <summary>Economy short and outgunned, with troops aboard.</summary>
    ShortEconomyTroops,
}

/// <summary>
/// Planner 13: both sides run strategy phase (state 1 mode choice, state 2 one strategy sub-object step per
/// call: 1 theaters, 2 theater check, 3 mission requests, 4 character-op requests, 5 fleet-task balance, 6 Coruscant / HQ mark);
/// then headquarters phase: side 1 the HQ machine, side 2 done. No phase issues a command (the HQ machine excepted).
/// </summary>
public sealed class StrategyPlanner() : Planner(PlannerType.Strategy)
{

    /// <summary>Mode 1 to 7 (0 = none).</summary>
    public int Mode { get; private set; }
    int _step = (int)StrategyStep.Theaters; // sub-object step (<see cref="StrategyStep"/>)
    /// <summary>The last theater build found fewer theaters than wanted.</summary>
    bool _theatersShort;
    StarSystem? _hq; // side 1's HQ system
    StarSystem? _site; // the HQ's system, then its new site
    int _relocateDay = 200; // the day of the next routine relocation check

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != PlannerStatus.Active) { (Phase, State, Done) = (0, 0, true); return null; }
        var m = controller.Model;
        if (Phase is not ((int)StrategyPhase.Strategy or (int)StrategyPhase.Headquarters)) { (Phase, State) = ((int)StrategyPhase.Strategy, (int)StrategyPlannerState.Start); return null; }
        if (Phase == (int)StrategyPhase.Headquarters)
        {
            if (controller.Side == Owner.Alliance) return Headquarters(game, m, controller.Side);
            (Phase, State, Done) = (0, 0, true); // side 2: done
            return null;
        }
        switch ((StrategyPlannerState)State)
        {
            case StrategyPlannerState.Start:
                State = (int)StrategyPlannerState.ModeChoice;
                return null;
            case StrategyPlannerState.ModeChoice:
                ChooseMode(m);
                State = (int)StrategyPlannerState.SubStep;
                return null;
            default:
                SubStep(game, m, controller.Side, _step);
                if (_step++ == (int)StrategyStep.Mark) { _step = (int)StrategyStep.Theaters; (Phase, State) = ((int)StrategyPhase.Headquarters, (int)StrategyPlannerState.Start); }
                return null;
        }
    }

    /// <summary>Mode choice: keep test per mode, else a new mode; a change replaces the strategy object (the theaters are due).</summary>
    void ChooseMode(WorldModel m)
    {
        // r: own / enemy fleet strength (fleet aggregates of round 7, read 0 here); t: troops aboard fleets - targets + garrisons
        double r = m.OtherFirepower <= 0 ? m.FleetFirepower : (double)m.FleetFirepower / m.OtherFirepower;
        int t = m.FleetTroops - m.GarrisonTarget + m.Troops, T = m.MinEnemyTroopsToTake, U = m.SectorsNotHeld, R = m.TurnRequests.Count(x => x);
        bool shortHalf = m.FreeEnergy < m.OwnSystems + 1 || m.FreeRawMaterial < m.OwnSystems / 2 + 1;
        bool notShort = m.FreeEnergy >= m.OwnSystems / 2 + 1 && m.FreeRawMaterial >= m.OwnSystems / 4 + 1;
        bool keep = (StrategyMode)Mode switch
        {
            StrategyMode.ShortEconomy => shortHalf && (r < 1.5 || t <= T) && R == 0 && U > 0,
            StrategyMode.OutgunnedTroops => r < 1.5 && R == 0 && t > 0 && notShort,
            StrategyMode.Requests => R != 0,
            StrategyMode.Dominant => r >= 1.5 && R == 0 && t <= T && notShort,
            StrategyMode.Troops => r >= 1.5 && R == 0 && t > 0,
            StrategyMode.ShortEconomyTroops => shortHalf && (r < 1.5 || t <= T) && R == 0 && U > 0 && t > 0,
            _ => false,
        };
        if (keep) return;
        m.Dominant = m.Outgunned = false;
        StrategyMode mode;
        bool shortQuarter = !notShort;
        if (r < 1.5 || R != 0)
        {
            m.Outgunned = true;
            mode = R != 0 ? StrategyMode.Requests : shortQuarter && U > 0 ? (t <= T ? StrategyMode.ShortEconomy : StrategyMode.ShortEconomyTroops) : t <= T ? StrategyMode.Defensive : StrategyMode.OutgunnedTroops;
        }
        else if (t > T) mode = StrategyMode.Troops;
        else if (shortHalf && U > 0) mode = StrategyMode.ShortEconomy;
        else { m.Dominant = true; mode = StrategyMode.Dominant; }
        if ((int)mode == Mode) return;
        Mode = (int)mode;
        // A fresh strategy object: its add and drop flags := 0, sub-step index 0; the theater
        // list lives in planner 13; the new object shares planner 13's own theater list, so it survives the mode change.
        (_theatersShort, _step, _add, _drop) = (false, 0, false, false);
        m.TheatersDue = true;
        foreach (var op in m.FleetOps.Values) op.TheaterSet = false;
    }

    void SubStep(GameState game, WorldModel m, Owner side, int step)
    {
        switch ((StrategyStep)step)
        {
            case StrategyStep.Empty:
                return;
            case StrategyStep.Theaters: // only when they are due
                if (!m.TheatersDue) return;
                _theatersShort = false;
                m.TheatersDue = false;
                Theaters(m, side);
                return;
            case StrategyStep.TheaterCheck:
                TheaterCheck(m);
                return;
            case StrategyStep.MissionRequests:
                MissionRequests(m, side);
                return;
            case StrategyStep.CharacterRequests:
                CharacterRequests(m, side);
                return;
            case StrategyStep.FleetTasks: // below gives a new garrison task; above gives release the first, removed only without a target
                if (m.Tasks.Count < m.UnblockadedKeySystems) m.Tasks.Add(new GarrisonTask(m.NextTaskKey++));
                else if (m.Tasks.Count > m.UnblockadedKeySystems)
                {
                    var first = m.Tasks[0];
                    first.Release(m);
                    if (first.Target is null) m.Tasks.Remove(first);
                }
                return;
            default:
                Mark(m, side);
                return;
        }
    }

    /// <summary>Theaters: sector, kind, argument and entry key.</summary>
    readonly List<(AiSector Sector, FleetTaskType Kind, TheaterArgument Arg, int Key)> _theaters = [];
    bool _add, _drop; // add and drop requests

    /// <summary>
    /// Strategy slot: target = N +/- 1 by the add / drop requests (Inferred: both cleared after use); a new list
    /// from the mode's head stage (at most one entry) and the standard tail T1-T4 (each while count &lt; target); with N != 0 every
    /// stage also pushes two lists (value sector, 100 for a non-theater sector) and, in the head stages that list
    /// it, by distance to the first old theater. Rank-sum picks, duplicates skipped. A non-empty list is committed
    /// (old sectors cleared, new ones marked; each dirties its sector and the model); still short gives _theatersShort set. Only tie flips draw.
    /// </summary>
    void Theaters(WorldModel m, Owner side)
    {
        int n = _theaters.Count, target = n + (_add ? 1 : _drop ? -1 : 0);
        (_add, _drop) = (false, false);
        if (target <= 0) return;
        var list = new List<(AiSector Sector, FleetTaskType Kind, TheaterArgument Arg, int Key)>();
        var anchor = n > 0 ? _theaters[0].Sector : null;

        bool Stage(FleetTaskType kind, TheaterArgument arg, bool one, Func<AiSector, bool> pass, Func<RankedList> main, bool distance)
        {
            if (list.Count >= target) return false;
            var stack = new ListStack();
            stack.Push(main());
            if (n != 0)
            {
                stack.Push(m.SectorDefaultList(pass, RankOrder.Descending));
                stack.Push(m.SectorDefaultList(pass, RankOrder.Descending));
                if (distance && anchor is not null) stack.Push(m.SectorDistanceFrom(anchor, pass, RankOrder.Descending));
            }
            bool any = false;
            while (list.Count < target && stack.Pick() is AiSector s)
            {
                if (list.Any(t => t.Sector == s)) continue;
                list.Add((s, kind, arg, list.Count + 1));
                any = true;
                if (one) break;
            }
            return any;
        }
        bool Head(FleetTaskType kind, Func<AiSector, bool> pass, Func<AiSector, int> value, RankOrder dir, bool distance = true) =>
            Stage(kind, TheaterArgument.None, true, pass, () => m.SectorList(pass, value, dir), distance);

        static bool held(AiSector x) => x.AllHeld;
        static bool enemy(AiSector x) => x.Enemy && x.EnemyFewGenCores;
        static bool enemyFleet(AiSector x) => x.EnemyFleet;
        switch ((StrategyMode)Mode)
        {
            case StrategyMode.ShortEconomy:
                Head(FleetTaskType.Defence, x => !held(x), x => x.EnemySystems, RankOrder.Descending);
                break;
            case StrategyMode.ShortEconomyTroops:
                Head(FleetTaskType.Defence, x => !held(x), x => x.EnemySystems, RankOrder.Descending);
                if (!Head(FleetTaskType.Assault, x => enemy(x) && !enemyFleet(x), x => x.EnemyTroopsToTake, RankOrder.Descending, false)) Head(FleetTaskType.Assault, x => enemy(x), x => x.EnemyFirepower, RankOrder.Descending, false);
                break;
            case StrategyMode.OutgunnedTroops:
                if (!Head(FleetTaskType.Assault, x => enemy(x) && !enemyFleet(x), x => x.EnemyTroopsToTake, RankOrder.Descending) && !Head(FleetTaskType.Assault, x => enemy(x), x => x.EnemyFirepower, RankOrder.Descending))
                    Head(FleetTaskType.Defence, x => !held(x), x => x.OwnSystems, RankOrder.Ascending);
                break;
            case StrategyMode.Requests: // site loop
                for (int slot = 0; slot < WorldModel.TurnSlots; slot++)
                {
                    if (!m.TurnRequests[slot]) continue;
                    // the construction planners 1, 2 and 3, the ship planners 10, 7 and 11, the training planners 6 and 5
                    var (kind, sectorField, systemField) = slot is (int)TurnSlot.Construction or (int)TurnSlot.Mine or (int)TurnSlot.Defence
                        ? (TheaterArgument.Construction, (Func<AiSector, int>)(x => x.OtherConstructionYards), (Func<AiSystem, int>)(r => r.ConstructionYards))
                        : slot is (int)TurnSlot.FleetFighter or (int)TurnSlot.Fighter or (int)TurnSlot.CapitalShip
                        ? (TheaterArgument.Shipyards, x => x.OtherShipyards, r => r.Shipyards) : (TheaterArgument.Training, x => x.OtherTrainingFacilities, r => r.TrainingFacilities);
                    var stack = new ListStack();
                    stack.Push(m.SectorList(x => x.AllHeld, x => x.EnemyTroopsToTake, RankOrder.Descending));
                    if (anchor is not null) stack.Push(m.SectorDistanceFrom(anchor, x => x.AllHeld, RankOrder.Descending));
                    if (n != 0) { stack.Push(m.SectorDefaultList(x => x.AllHeld, RankOrder.Descending)); stack.Push(m.SectorDefaultList(x => x.AllHeld, RankOrder.Descending)); }
                    bool found = false;
                    while (!found && stack.Pick() is AiSector sector)
                    {
                        if (sectorField(sector) <= 0) continue;
                        var systems = new ListStack();
                        systems.Push(m.SystemList(sector, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, systemField, RankOrder.Descending));
                        systems.Push(m.SystemList(sector, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, r => r.UnrestTroops, RankOrder.Descending));
                        while (systems.Pick() is AiSystem site)
                            if (systemField(site) >= 1) { found = true; break; }
                        if (found) list.Add((sector, FleetTaskType.Site, kind, list.Count + 1));
                    }
                    if (found) break;
                }
                break;
            case StrategyMode.Dominant:
                if (!Head(FleetTaskType.Scout, x => enemyFleet(x), x => x.EnemyFirepower, RankOrder.Ascending))
                    Stage(FleetTaskType.Scout, TheaterArgument.None, true, x => x.Enemy && !x.NoUnvisited, () => m.SectorStalenessList(x => x.Enemy && !x.NoUnvisited, RankOrder.Ascending), true);
                break;
            case StrategyMode.Troops:
                _ = side == Owner.Empire && m.Headquarters && Stage(FleetTaskType.Assault, TheaterArgument.None, true, x => x.Enemy && x.EnemyFewGenCores && x.Headquarters, () => m.SectorList(x => x.Enemy && x.EnemyFewGenCores && x.Headquarters, x => x.VictoryCharacters, RankOrder.Ascending), false)
                    || Head(FleetTaskType.Assault, x => enemy(x), x => x.VictoryCharacters, RankOrder.Ascending)
                    || Stage(FleetTaskType.Assault, TheaterArgument.None, true, x => enemy(x), () => m.SectorList(x => x.Enemy && x.EnemyFewGenCores, x => x.EnemyProducerMaintenance, RankOrder.Ascending), true)
                    || Head(FleetTaskType.Strike, x => x.EnemyAssets && x.EnemyNoBatteries, x => x.EnemySystems, RankOrder.Ascending)
                    || Head(FleetTaskType.Defence, x => !held(x), x => x.OwnSystems, RankOrder.Ascending);
                break;
        }
        // standard tail T1-T4
        Stage(FleetTaskType.Reserve, TheaterArgument.None, false, x => x.KeySystem && x.Own, () => m.SectorList(x => x.KeySystem && x.Own, x => x.ProducerMaintenance, RankOrder.Ascending), false);
        Stage(FleetTaskType.Reserve, TheaterArgument.None, false, x => x.AllHeld && x.Own && x.Neutral && !x.Enemy, () => m.SectorList(x => x.AllHeld && x.Own && x.Neutral && !x.Enemy, x => x.ProducerMaintenance, RankOrder.Ascending), false);
        Stage(FleetTaskType.Reserve, TheaterArgument.None, false, x => x.AllHeld && x.Own, () => m.SectorList(x => x.AllHeld && x.Own, x => x.ProducerMaintenance, RankOrder.Ascending), false);
        bool t4(AiSector x) => Mode is (int)StrategyMode.Defensive or (int)StrategyMode.ShortEconomy or (int)StrategyMode.ShortEconomyTroops && x.Own;
        Stage(FleetTaskType.Strike, TheaterArgument.None, false, x => x.EnemyAssets && !t4(x), () => m.SectorList(x => x.EnemyAssets && !t4(x), x => x.EnemySystems, RankOrder.Ascending), false);
        if (list.Count < target) _theatersShort = true;
        if (list.Count == 0) return;
        foreach (var (s, _, _, _) in _theaters)
        {
            s.TheaterMark = s.NoUnvisited = false;
            (s.TheaterKind, s.TheaterArgument, s.Entry) = (AiSector.NoTheaterKind, TheaterArgument.None, 0);
            m.DirtySector(s);
        }
        _theaters.Clear();
        foreach (var t in list)
        {
            t.Sector.TheaterMark = true;
            t.Sector.NoUnvisited = false;
            (t.Sector.TheaterKind, t.Sector.TheaterArgument, t.Sector.Entry) = (t.Kind, t.Arg, t.Key);
            m.DirtySector(t.Sector);
            _theaters.Add(t);
        }
    }

    /// <summary>
    /// Step 2, theater check: per theater its fleet op (of the sector) gets the theater entry pushed
    /// Evaluated only when every theater has an op with its theater set and a waiting character (no theater: vacuously):
    /// open = an op not strong enough; count the empty ops; the theaters are due when an op misses its goal; the theater mark cleared on
    /// each. Then a counted op: the last op examined is empty and count &gt; 1 gives theaters due, drop one; else nothing open and the
    /// last build not short gives theaters due, add one.
    /// </summary>
    void TheaterCheck(WorldModel m)
    {
        var ops = new List<FleetOp>();
        bool every = true;
        foreach (var (sector, _, _, _) in _theaters)
        {
            if (m.FleetOps.GetValueOrDefault(sector.FleetOpKey) is not { } op) { every = false; continue; }
            FleetOps.PushEntry(m, op, sector.Entry); // the strategy entry key is the sector's
            ops.Add(op);
            if (!(op.TheaterSet && op.CharacterWaiting)) every = false;
        }
        if (!every) return;
        bool open = false;
        int count = 0;
        FleetOp? last = null;
        foreach (var op in ops)
        {
            if (!op.StrongEnough) open = true;
            if (op.Empty) count++;
            if (!op.Goal) m.TheatersDue = true;
            op.TheaterSet = false;
            last = op;
        }
        if (count > 0)
        {
            if (last is not null && last.Empty && count > 1) { m.TheatersDue = true; _drop = true; }
        }
        else if (!open && !_theatersShort) { m.TheatersDue = true; _add = true; }
    }

    /// <summary>Mission request rules: the first satisfied create rule and the first satisfied retire rule.</summary>
    static void MissionRequests(WorldModel m, Owner side)
    {
        int N(SystemOpIndex i) => m.SystemOps((int)i);

        int fleets = m.FleetOperationCount;
        var rules = new (MissionKind Family, OpSubtype Subtype, bool Create, bool Retire)[]
        {
            (MissionKind.Recruitment, OpSubtype.None, N(SystemOpIndex.Recruitment) < 2 && m.MinorCharactersToCome > 0, N(SystemOpIndex.Recruitment) > 2 || m.MinorCharactersToCome == 0 && N(SystemOpIndex.Recruitment) > 0),
            (MissionKind.Research, OpSubtype.ShipResearch, !m.ShipLineDone && N(SystemOpIndex.ShipResearch) < m.ShipDesigners, N(SystemOpIndex.ShipResearch) > m.ShipDesigners || m.ShipLineDone && N(SystemOpIndex.ShipResearch) > 0),
            (MissionKind.Research, OpSubtype.TroopResearch, !m.TroopLineDone && N(SystemOpIndex.TroopResearch) < m.TroopTrainers, N(SystemOpIndex.TroopResearch) > m.TroopTrainers || m.TroopLineDone && N(SystemOpIndex.TroopResearch) > 0),
            (MissionKind.Research, OpSubtype.FacilityResearch, !m.FacilityLineDone && N(SystemOpIndex.FacilityResearch) < m.FacilityDesigners, N(SystemOpIndex.FacilityResearch) > m.FacilityDesigners || m.FacilityLineDone && N(SystemOpIndex.FacilityResearch) > 0),
            (MissionKind.Diplomacy, OpSubtype.None, N(SystemOpIndex.Diplomacy) < m.DiplomacyTargets, N(SystemOpIndex.Diplomacy) > m.DiplomacyTargets),
            (MissionKind.SubdueUprising, OpSubtype.None, m.OwnLowSupport && N(SystemOpIndex.SubdueUprising) < m.UprisingNoFleet + 1, N(SystemOpIndex.SubdueUprising) > m.UprisingNoFleet + 1 || !m.OwnLowSupport && N(SystemOpIndex.SubdueUprising) > 0),
            (MissionKind.InciteUprising, OpSubtype.None, N(SystemOpIndex.InciteUprising) < m.EnemySupported, N(SystemOpIndex.InciteUprising) > m.EnemySupported),
            (MissionKind.DeathStarSabotage, OpSubtype.None, side == Owner.Alliance && N(SystemOpIndex.DeathStarSabotage) < 1 && m.OwnDeathStar, side == Owner.Alliance && (N(SystemOpIndex.DeathStarSabotage) > 1 || !m.OwnDeathStar && N(SystemOpIndex.DeathStarSabotage) > 0)), // Inferred: the model flag for the Death Star is 4
            (MissionKind.Abduction, OpSubtype.None, N(SystemOpIndex.Abduction) < 1, N(SystemOpIndex.Abduction) > 1),
            (MissionKind.Rescue, OpSubtype.None, m.OwnCaptured && N(SystemOpIndex.Rescue) < 1, N(SystemOpIndex.Rescue) > 1 || !m.OwnCaptured && N(SystemOpIndex.Rescue) > 0),
            (MissionKind.Reconnaissance, OpSubtype.Reconnaissance, N(SystemOpIndex.Reconnaissance) < fleets, fleets < N(SystemOpIndex.Reconnaissance)),
            (MissionKind.Espionage, OpSubtype.Espionage, N(SystemOpIndex.Espionage) < m.EnemySectors - fleets, m.EnemySectors - fleets < N(SystemOpIndex.Espionage)),
        };
        bool created = false, retired = false;
        foreach (var (family, subtype, create, retire) in rules)
        {
            if (!created && create) { (m.MissionRequestKind, m.MissionRequestSubtype, created) = ((int)family, (int)subtype, true); }
            if (!retired && retire)
            {
                retired = true;
                // the lowest-score live op of that family / subtype (first in key order on ties; start 2000000)
                MissionOp? low = null;
                int best = 2000000;
                foreach (var op in m.Ops.Values)
                    if (op.Class.Family == family && op.Class.Subtype == (int)subtype && !op.Retired && MissionOps.Score(op) < best)
                        (low, best) = (op, MissionOps.Score(op));
                m.MissionRetireKey = low?.Key ?? OpCodes.NoOp;
            }
        }
    }

    /// <summary>
    /// Character-operation requests gives model (planner 12, round 8): kind 1 by one count vs another; kind 6
    /// (side 2) by a rank value, the own Death Star and a count. No character operations exist yet (those counts read 0, no retire target).
    /// </summary>
    static void CharacterRequests(WorldModel m, Owner side)
    {
        m.CharacterRequestType = m.Type1CharacterOps < m.LowSupportHubs ? CharacterOpType.GeneralAtSystem
            : side == Owner.Empire && m.MinorCharactersToCome == 0 && m.OwnCoruscant && m.Type6CharacterOps == 0 ? CharacterOpType.PalpatineToCoruscant : CharacterOpType.None;
        // retire: the first op of the kind without a valid target (Inferred)
        var retire = m.Type1CharacterOps > m.LowSupportHubs ? CharacterOpType.GeneralAtSystem
            : side == Owner.Empire && (m.MinorCharactersToCome > 0 || !m.OwnCoruscant) && m.Type6CharacterOps == 1 ? CharacterOpType.PalpatineToCoruscant : CharacterOpType.None;
        m.CharacterRetireKey = retire == CharacterOpType.None ? OpCodes.NoOp : m.CharacterOps.Values.FirstOrDefault(o => o.Type == retire && !o.TargetValid)?.Key ?? OpCodes.NoOp;
    }

    /// <summary>
    /// Step 6: Coruscant's record hub mark := own. Side 1: a set HQ key without a record does nothing;
    /// its record keeps the hub mark while it is own with the HQ facility, else the hub mark is cleared and the key reset
    /// to none. With no key: sectors with an HQ by a rank value (dir Ascending), then systems with an HQ by a rank value (dir Ascending); an own pick is
    /// marked and stored, a foreign pick only loses its hub mark. Plain writes, no dirty.
    /// </summary>
    void Mark(WorldModel m, Owner side)
    {
        if (m.Systems.Values.FirstOrDefault(r => r.System.Id == Refs.CoruscantIndex) is { } cor)
        {
            if (cor.Own) cor.Hub = true;
            else cor.Hub = false;
        }
        if (side != Owner.Alliance) return;
        if (_hq != null)
        {
            if (m.Record(_hq) is not { } hq) return;
            if (hq.Own && hq.Headquarters) { hq.Hub = true; return; }
            hq.Hub = false;
            _hq = null;
        }
        var stack = new ListStack();
        stack.Push(m.SectorList(x => x.Headquarters, x => x.OwnSystems, RankOrder.Ascending));
        if (stack.Pick() is not AiSector s) return;
        var systems = new ListStack();
        systems.Push(m.SystemList(s, r => r.Headquarters, r => r.Support, RankOrder.Ascending));
        if (systems.Pick() is not AiSystem pick) return;
        if (pick.Own) { _hq = pick.System; pick.Hub = true; }
        else pick.Hub = false;
    }

    // ---- headquarters phase, side 1: the HQ machine ----

    Command? Headquarters(GameState game, WorldModel m, Owner side)
    {
        switch ((HeadquartersState)State)
        {
            case HeadquartersState.Decide:
                if (Decide(game, m)) { State = (int)HeadquartersState.Scrap; return null; }
                (Phase, State, Done) = (0, 0, true);
                return null;
            case HeadquartersState.Scrap:
            {
                State = (int)HeadquartersState.Relocate;
                var c = HeadquartersScrap(m, side);
                if (c is not null) { Priority++; Done = true; }
                return c;
            }
            case HeadquartersState.Relocate:
            {
                (Phase, State, Done) = (0, 0, true);
                if (game.Known.Headquarters is not { } hq || _site is null) return null;
                _relocateDay = game.Day + 300; // Inferred: the current day
                return new Command(CommandType.Move, side) { Targets = [(hq, GroupMode.Object)], ArgRef = _site };
            }
            default: // Start: any state below Decide moves to it
                State = (int)HeadquartersState.Decide;
                return null;
        }
    }


    /// <summary>
    /// Decide: the HQ's view exists and is not Enroute; locate it (sectors with an HQ by a rank value, systems with an HQ,
    /// excl Blockade, by a rank value); keep it while its sector has no enemy fleet and the day is before the relocation day; else the sector with the
    /// fewest enemy systems (dir Descending; no enemy fleet, then without that test) and in it a random-key own system without an
    /// enemy fleet, with free energy first. Inferred: the new site may be the current system.
    /// </summary>
    bool Decide(GameState game, WorldModel m)
    {
        if (game.Known.Headquarters is not { } hq || !m.Live(hq) && game.Views.GetValueOrDefault((hq, m.Side)) is null || m.See(hq).Enroute) return false;
        var stack = new ListStack();
        stack.Push(m.SectorList(x => x.Headquarters, x => x.OwnSystems, RankOrder.Ascending));
        if (stack.Pick() is not AiSector sector) return false;
        var systems = new ListStack();
        systems.Push(m.SystemList(sector, r => r.Headquarters && !r.Blockade, r => r.Support, RankOrder.Ascending));
        if (systems.Pick() is not AiSystem) return false;
        _site = null;
        if (!sector.EnemyFleet && game.Day < _relocateDay) return false;
        var pick = new ListStack();
        pick.Push(m.SectorList(x => x.Own && !x.EnemyFleet, x => x.EnemySystems, RankOrder.Descending));
        if (pick.Pick() is not AiSector to)
        {
            var any = new ListStack();
            any.Push(m.SectorList(x => x.Own, x => x.EnemySystems, RankOrder.Descending));
            to = any.Pick() as AiSector;
        }
        if (to is null) return false;
        var site = new ListStack();
        site.Push(m.SystemRandomList(to, r => r.HasFreeEnergy && r.Own && !r.EnemyFleet, RankOrder.Ascending));
        if (site.Pick() is not AiSystem found)
        {
            var other = new ListStack();
            other.Push(m.SystemRandomList(to, r => r.Own && !r.EnemyFleet, RankOrder.Ascending));
            found = other.Pick() as AiSystem;
        }
        _site = found?.System;
        return found is not null;
    }

    /// <summary>State 4: a site without free energy (S = 0) scraps its first Usable facility in the original order gives Scrap.</summary>
    Command? HeadquartersScrap(WorldModel m, Owner side)
    {
        if (m.Record(_site) is not { } r || r.FreeEnergy != 0) return null;
        var own = m.Contents(r.System).Where(x => Family.IsFacility(x.Type) && m.Usable(x) && m.See(x).Owner == m.Side).ToList();
        int mines = own.Count(x => Family.Of(x.Type) == Families.Mine), refineries = own.Count(x => Family.Of(x.Type) == Families.Refinery);
        var order = new List<uint>();
        if (mines < refineries || mines == 0) order.Add(Families.Refinery);
        if (refineries <= mines || refineries == 0) order.Add(Families.Mine);
        order.AddRange([Families.IonCannon, Families.Gun, Families.DeathStarShield, Families.PlanetaryShield, Families.TrainingFacility, Families.Shipyard, Families.ConstructionYard]);
        foreach (uint family in order)
            if (own.FirstOrDefault(x => Family.Of(x.Type) == family) is { } facility) return new Command(CommandType.Scrap, side) { Targets = [(facility, GroupMode.Object)] };
        return null;
    }
}
