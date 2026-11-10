namespace OpenSWR.Sim.Ai;

/// <summary>
/// Planner 13: both sides run phase 0x3FB (state 1 mode choice, state 2 one strategy sub-object step per
/// call: 1 theaters, 2 theater check, 3 mission requests, 4 character-op requests, 5 fleet-task balance, 6 Coruscant / HQ mark);
/// then phase 0x3FC: side 1 the HQ machine, side 2 done. No phase issues a command (the HQ machine excepted).
/// </summary>
public sealed class StrategyPlanner() : Planner(13)
{

    /// <summary>Mode 1 to 7 (0 = none).</summary>
    public int Mode { get; private set; }
    int _step = 1; // sub-object step
    /// <summary>The last theater build found fewer theaters than wanted.</summary>
    bool _theatersShort;
    StarSystem? _hq; // side 1's HQ system
    StarSystem? _site; // the HQ's system, then its new site
    int _relocateDay = 200; // the day of the next routine relocation check

    public override Command? Step(GameState game, ComputerController controller)
    {
        if (Status != 1) { (Phase, State, Done) = (0, 0, true); return null; }
        var m = controller.Model;
        if (Phase is not (0x3FB or 0x3FC)) { (Phase, State) = (0x3FB, 0); return null; }
        if (Phase == 0x3FC)
        {
            if (controller.Side == Owner.Alliance) return Headquarters(game, m, controller.Side);
            (Phase, State, Done) = (0, 0, true); // side 2: done
            return null;
        }
        switch (State)
        {
            case 0:
                State = 1;
                return null;
            case 1:
                ChooseMode(m);
                State = 2;
                return null;
            default:
                SubStep(game, m, controller.Side, _step);
                if (_step++ == 6) { _step = 1; (Phase, State) = (0x3FC, 0); }
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
        bool keep = Mode switch
        {
            2 => shortHalf && (r < 1.5 || t <= T) && R == 0 && U > 0,
            3 => r < 1.5 && R == 0 && t > 0 && notShort,
            4 => R != 0,
            5 => r >= 1.5 && R == 0 && t <= T && notShort,
            6 => r >= 1.5 && R == 0 && t > 0,
            7 => shortHalf && (r < 1.5 || t <= T) && R == 0 && U > 0 && t > 0,
            _ => false,
        };
        if (keep) return;
        m.Dominant = m.Outgunned = false;
        int mode;
        bool shortQuarter = !notShort;
        if (r < 1.5 || R != 0)
        {
            m.Outgunned = true;
            mode = R != 0 ? 4 : shortQuarter && U > 0 ? (t <= T ? 2 : 7) : t <= T ? 1 : 3;
        }
        else if (t > T) mode = 6;
        else if (shortHalf && U > 0) mode = 2;
        else { m.Dominant = true; mode = 5; }
        if (mode == Mode) return;
        Mode = mode;
        // A fresh strategy object: its add and drop flags := 0, sub-step index 0; the theater
        // list lives in planner 13 (new object's theater list := planner 13 + 0x64) and survives the mode change
        (_theatersShort, _step, _add, _drop) = (false, 0, false, false);
        m.TheatersDue = true;
        foreach (var op in m.FleetOps.Values) op.TheaterSet = false;
    }

    void SubStep(GameState game, WorldModel m, Owner side, int step)
    {
        switch (step)
        {
            case 0: // index 0: an empty call
                return;
            case 1: // theaters: only when they are due
                if (!m.TheatersDue) return;
                _theatersShort = false;
                m.TheatersDue = false;
                Theaters(m, side);
                return;
            case 2:
                TheaterCheck(m);
                return;
            case 3:
                MissionRequests(m, side);
                return;
            case 4:
                CharacterRequests(m, side);
                return;
            case 5: // fleet-task balance: below -> a new garrison task; above -> release the first, removed only without a target
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
    readonly List<(AiSector Sector, int Kind, int Arg, int Key)> _theaters = [];
    bool _add, _drop; // add and drop requests

    /// <summary>
    /// Strategy slot: target = N +/- 1 by the add / drop requests (Inferred: both cleared after use); a new list
    /// from the mode's head stage (at most one entry) and the standard tail T1-T4 (each while count &lt; target); with N != 0 every
    /// stage also pushes two lists (value sector, 100 for a non-theater sector) and, in the head stages that list
    /// it, by distance to the first old theater. Rank-sum picks, duplicates skipped. A non-empty list is committed
    /// (old sectors cleared, new ones marked; each dirties its sector and the model); still short -> _theatersShort set. Only tie flips draw.
    /// </summary>
    void Theaters(WorldModel m, Owner side)
    {
        int n = _theaters.Count, target = n + (_add ? 1 : _drop ? -1 : 0);
        (_add, _drop) = (false, false);
        if (target <= 0) return;
        var list = new List<(AiSector Sector, int Kind, int Arg, int Key)>();
        var anchor = n > 0 ? _theaters[0].Sector : null;

        bool Stage(int kind, int arg, bool one, Func<AiSector, bool> pass, Func<RankedList> main, bool distance)
        {
            if (list.Count >= target) return false;
            var stack = new ListStack();
            stack.Push(main());
            if (n != 0)
            {
                stack.Push(m.SectorDefaultList(pass, 2));
                stack.Push(m.SectorDefaultList(pass, 2));
                if (distance && anchor is not null) stack.Push(m.SectorDistanceFrom(anchor, pass, 2));
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
        bool Head(int kind, Func<AiSector, bool> pass, Func<AiSector, int> value, int dir, bool distance = true) =>
            Stage(kind, 0, true, pass, () => m.SectorList(pass, value, dir), distance);

        static bool held(AiSector x) => x.AllHeld;
        static bool enemy(AiSector x) => x.Enemy && x.EnemyFewGenCores;
        static bool enemyFleet(AiSector x) => x.EnemyFleet;
        switch (Mode)
        {
            case 2:
                Head(5, x => !held(x), x => x.EnemySystems, 2);
                break;
            case 7:
                Head(5, x => !held(x), x => x.EnemySystems, 2);
                if (!Head(6, x => enemy(x) && !enemyFleet(x), x => x.EnemyTroopsToTake, 2, false)) Head(6, x => enemy(x), x => x.EnemyFirepower, 2, false);
                break;
            case 3:
                if (!Head(6, x => enemy(x) && !enemyFleet(x), x => x.EnemyTroopsToTake, 2) && !Head(6, x => enemy(x), x => x.EnemyFirepower, 2))
                    Head(5, x => !held(x), x => x.OwnSystems, 1);
                break;
            case 4: // site loop
                for (int slot = 0; slot < WorldModel.TurnSlots; slot++)
                {
                    if (!m.TurnRequests[slot]) continue;
                    // the construction planners 1, 2 and 3, the ship planners 10, 7 and 11, the training planners 6 and 5
                    var (kind, sectorField, systemField) = slot is 0 or 1 or 7 ? (3, (Func<AiSector, int>)(x => x.OtherConstructionYards), (Func<AiSystem, int>)(r => r.ConstructionYards))
                        : slot is 3 or 4 or 6 ? (1, x => x.OtherShipyards, r => r.Shipyards) : (2, x => x.OtherTrainingFacilities, r => r.TrainingFacilities);
                    var stack = new ListStack();
                    stack.Push(m.SectorList(x => x.AllHeld, x => x.EnemyTroopsToTake, 2));
                    if (anchor is not null) stack.Push(m.SectorDistanceFrom(anchor, x => x.AllHeld, 2));
                    if (n != 0) { stack.Push(m.SectorDefaultList(x => x.AllHeld, 2)); stack.Push(m.SectorDefaultList(x => x.AllHeld, 2)); }
                    bool found = false;
                    while (!found && stack.Pick() is AiSector sector)
                    {
                        if (sectorField(sector) <= 0) continue;
                        var systems = new ListStack();
                        systems.Push(m.SystemList(sector, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, systemField, 2));
                        systems.Push(m.SystemList(sector, r => r.OwnedOrPopulated && !r.Own && !r.ManyGenCores, r => r.UnrestTroops, 2));
                        while (systems.Pick() is AiSystem site)
                            if (systemField(site) >= 1) { found = true; break; }
                        if (found) list.Add((sector, 4, kind, list.Count + 1));
                    }
                    if (found) break;
                }
                break;
            case 5:
                if (!Head(2, x => enemyFleet(x), x => x.EnemyFirepower, 1))
                    Stage(2, 0, true, x => x.Enemy && !x.NoUnvisited, () => m.SectorStalenessList(x => x.Enemy && !x.NoUnvisited, 1), true);
                break;
            case 6:
                _ = side == Owner.Empire && m.Headquarters && Stage(6, 0, true, x => x.Enemy && x.EnemyFewGenCores && x.Headquarters, () => m.SectorList(x => x.Enemy && x.EnemyFewGenCores && x.Headquarters, x => x.VictoryCharacters, 1), false)
                    || Head(6, x => enemy(x), x => x.VictoryCharacters, 1)
                    || Stage(6, 0, true, x => enemy(x), () => m.SectorList(x => x.Enemy && x.EnemyFewGenCores, x => x.EnemyProducerMaintenance, 1), true)
                    || Head(1, x => x.EnemyAssets && x.EnemyNoBatteries, x => x.EnemySystems, 1)
                    || Head(5, x => !held(x), x => x.OwnSystems, 1);
                break;
        }
        // standard tail T1-T4
        Stage(3, 0, false, x => x.KeySystem && x.Own, () => m.SectorList(x => x.KeySystem && x.Own, x => x.ProducerMaintenance, 1), false);
        Stage(3, 0, false, x => x.AllHeld && x.Own && x.Neutral && !x.Enemy, () => m.SectorList(x => x.AllHeld && x.Own && x.Neutral && !x.Enemy, x => x.ProducerMaintenance, 1), false);
        Stage(3, 0, false, x => x.AllHeld && x.Own, () => m.SectorList(x => x.AllHeld && x.Own, x => x.ProducerMaintenance, 1), false);
        bool t4(AiSector x) => Mode is 1 or 2 or 7 && x.Own;
        Stage(1, 0, false, x => x.EnemyAssets && !t4(x), () => m.SectorList(x => x.EnemyAssets && !t4(x), x => x.EnemySystems, 1), false);
        if (list.Count < target) _theatersShort = true;
        if (list.Count == 0) return;
        foreach (var (s, _, _, _) in _theaters)
        {
            s.TheaterMark = s.NoUnvisited = false;
            (s.TheaterKind, s.TheaterArgument, s.Entry) = (0, 0, 0);
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
    /// each. Then a counted op: the last op examined is empty and count &gt; 1 -> theaters due, drop one; else nothing open and the
    /// last build not short -> theaters due, add one.
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
        int N(int i) => m.SystemOps(i);

        int fleets = m.FleetOperationCount;
        var rules = new (uint Family, int Subtype, bool Create, bool Retire)[]
        {
            (0x55, 0, N(0) < 2 && m.MinorCharactersToCome > 0, N(0) > 2 || m.MinorCharactersToCome == 0 && N(0) > 0),
            (0x53, 1, !m.ShipLineDone && N(1) < m.ShipDesigners, N(1) > m.ShipDesigners || m.ShipLineDone && N(1) > 0),
            (0x53, 2, !m.TroopLineDone && N(2) < m.TroopTrainers, N(2) > m.TroopTrainers || m.TroopLineDone && N(2) > 0),
            (0x53, 3, !m.FacilityLineDone && N(3) < m.FacilityDesigners, N(3) > m.FacilityDesigners || m.FacilityLineDone && N(3) > 0),
            (0x51, 0, N(0xB) < m.DiplomacyTargets, N(0xB) > m.DiplomacyTargets),
            (0x57, 0, m.OwnLowSupport && N(4) < m.UprisingNoFleet + 1, N(4) > m.UprisingNoFleet + 1 || !m.OwnLowSupport && N(4) > 0),
            (0x56, 0, N(0xC) < m.EnemySupported, N(0xC) > m.EnemySupported),
            (0x6A, 0, side == Owner.Alliance && N(5) < 1 && m.OwnDeathStar, side == Owner.Alliance && (N(5) > 1 || !m.OwnDeathStar && N(5) > 0)), // Inferred: the model flag for the Death Star is 0x4
            (0x62, 0, N(6) < 1, N(6) > 1),
            (0x61, 0, m.OwnCaptured && N(8) < 1, N(8) > 1 || !m.OwnCaptured && N(8) > 0),
            (0x54, 5, N(9) < fleets, fleets < N(9)),
            (0x52, 6, N(0xA) < m.EnemySectors - fleets, m.EnemySectors - fleets < N(0xA)),
        };
        bool created = false, retired = false;
        foreach (var (family, subtype, create, retire) in rules)
        {
            if (!created && create) { (m.MissionRequestKind, m.MissionRequestSubtype, created) = ((int)family, subtype, true); }
            if (!retired && retire)
            {
                retired = true;
                // the lowest-score live op of that family / subtype (first in key order on ties; start 2000000)
                MissionOp? low = null;
                int best = 2000000;
                foreach (var op in m.Ops.Values)
                    if (op.Class.Family == family && op.Class.Subtype == subtype && !op.Retired && MissionOps.Score(op) < best)
                        (low, best) = (op, MissionOps.Score(op));
                m.MissionRetireKey = low?.Key ?? 0;
            }
        }
    }

    /// <summary>
    /// Character-operation requests -> model (planner 12, round 8): kind 1 by one count vs another; kind 6
    /// (side 2) by a rank value, the own Death Star and a count. No character operations exist yet (those counts read 0, no retire target).
    /// </summary>
    static void CharacterRequests(WorldModel m, Owner side)
    {
        m.CharacterRequestType = m.Type1CharacterOps < m.LowSupportHubs ? 1 : side == Owner.Empire && m.MinorCharactersToCome == 0 && m.OwnCoruscant && m.Type6CharacterOps == 0 ? 6 : 0;
        // retire: the first op of the kind without a valid target (Inferred)
        int retire = m.Type1CharacterOps > m.LowSupportHubs ? 1 : side == Owner.Empire && (m.MinorCharactersToCome > 0 || !m.OwnCoruscant) && m.Type6CharacterOps == 1 ? 6 : 0;
        m.CharacterRetireKey = retire == 0 ? 0 : m.CharacterOps.Values.FirstOrDefault(o => o.Type == retire && !o.TargetValid)?.Key ?? 0;
    }

    /// <summary>
    /// Step 6: Coruscant's record hub mark := own. Side 1: a set HQ key without a record does nothing;
    /// its record keeps the hub mark while it is own with the HQ facility, else the hub mark is cleared and the key reset
    /// to none. With no key: sectors with an HQ by a rank value (dir 1), then systems with an HQ by a rank value (dir 1); an own pick is
    /// marked and stored, a foreign pick only loses its hub mark. Plain writes, no dirty.
    /// </summary>
    void Mark(WorldModel m, Owner side)
    {
        if (m.Systems.Values.FirstOrDefault(r => r.System.Id == 0x109) is { } cor)
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
        stack.Push(m.SectorList(x => x.Headquarters, x => x.OwnSystems, 1));
        if (stack.Pick() is not AiSector s) return;
        var systems = new ListStack();
        systems.Push(m.SystemList(s, r => r.Headquarters, r => r.Support, 1));
        if (systems.Pick() is not AiSystem pick) return;
        if (pick.Own) { _hq = pick.System; pick.Hub = true; }
        else pick.Hub = false;
    }

    // ---- phase 0x3FC, side 1: the HQ machine ----

    Command? Headquarters(GameState game, WorldModel m, Owner side)
    {
        switch (State)
        {
            case 3:
                if (Decide(game, m)) { State = 4; return null; }
                (Phase, State, Done) = (0, 0, true);
                return null;
            case 4:
            {
                State = 5;
                var c = HeadquartersScrap(m, side);
                if (c is not null) { Priority++; Done = true; }
                return c;
            }
            case 5:
            {
                (Phase, State, Done) = (0, 0, true);
                if (Hq(game) is not { } hq || _site is null) return null;
                _relocateDay = game.Day + 300; // Inferred: the current day
                return new Command(0x201, side) { Targets = [(hq, 0)], ArgRef = _site };
            }
            default:
                State = 3;
                return null;
        }
    }

    static GameObject? Hq(GameState game) => game.Created.FirstOrDefault(o => o.Ref == 0x20000005);

    /// <summary>
    /// Decide: the HQ's view exists and is not Enroute; locate it (sectors with an HQ by a rank value, systems with an HQ,
    /// excl Blockade, by a rank value); keep it while its sector has no enemy fleet and the day is before the relocation day; else the sector with the
    /// fewest enemy systems (dir 2; no enemy fleet, then without that test) and in it a random-key own system without an
    /// enemy fleet, with free energy first. Inferred: the new site may be the current system.
    /// </summary>
    bool Decide(GameState game, WorldModel m)
    {
        if (Hq(game) is not { } hq || !m.Live(hq) && game.Views.GetValueOrDefault((hq, m.Side)) is null || m.See(hq).Enroute) return false;
        var stack = new ListStack();
        stack.Push(m.SectorList(x => x.Headquarters, x => x.OwnSystems, 1));
        if (stack.Pick() is not AiSector sector) return false;
        var systems = new ListStack();
        systems.Push(m.SystemList(sector, r => r.Headquarters && !r.Blockade, r => r.Support, 1));
        if (systems.Pick() is not AiSystem) return false;
        _site = null;
        if (!sector.EnemyFleet && game.Day < _relocateDay) return false;
        var pick = new ListStack();
        pick.Push(m.SectorList(x => x.Own && !x.EnemyFleet, x => x.EnemySystems, 2));
        if (pick.Pick() is not AiSector to)
        {
            var any = new ListStack();
            any.Push(m.SectorList(x => x.Own, x => x.EnemySystems, 2));
            to = any.Pick() as AiSector;
        }
        if (to is null) return false;
        var site = new ListStack();
        site.Push(m.SystemRandomList(to, r => r.HasFreeEnergy && r.Own && !r.EnemyFleet, 1));
        if (site.Pick() is not AiSystem found)
        {
            var other = new ListStack();
            other.Push(m.SystemRandomList(to, r => r.Own && !r.EnemyFleet, 1));
            found = other.Pick() as AiSystem;
        }
        _site = found?.System;
        return found is not null;
    }

    /// <summary>State 4: a site without free energy (S = 0) scraps its first Usable facility in the original order -> 0x200.</summary>
    Command? HeadquartersScrap(WorldModel m, Owner side)
    {
        if (m.Record(_site) is not { } r || r.FreeEnergy != 0) return null;
        var own = m.Contents(r.System).Where(x => Family.IsFacility(x.Type) && m.Usable(x) && m.See(x).Owner == m.Side).ToList();
        int mines = own.Count(x => Family.Of(x.Type) == 0x2C), refineries = own.Count(x => Family.Of(x.Type) == 0x2D);
        var order = new List<uint>();
        if (mines < refineries || mines == 0) order.Add(0x2D);
        if (refineries <= mines || refineries == 0) order.Add(0x2C);
        order.AddRange([0x22, 0x23, 0x25, 0x24, 0x29, 0x28, 0x2A]);
        foreach (uint family in order)
            if (own.FirstOrDefault(x => Family.Of(x.Type) == family) is { } facility) return new Command(0x200, side) { Targets = [(facility, 0)] };
        return null;
    }
}
