using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>
    /// The model's load effects: the catalogue rebuilt with its seven summary lists at the saved research
    /// levels (tie draws), every record dirty (systems, sectors, ships; agents with their location),
    /// the aggregates flag, the tree key counters := highest live key + 1, the observation snapshot from
    /// the loaded state (no messages for unchanged fields). Fleet records are refreshed on every update anyway (shortcut above).
    /// </summary>
    internal void AfterLoad(GameState game)
    {
        _game = game;
        _frozenContents = null;
        Catalogue = new Catalogue(game.Definitions);
        Summary(game, (ShipLevel, TroopLevel, FacilityLevel));
        foreach (var r in Systems.Values) r.Dirty = true;
        foreach (var r in Sectors) r.Dirty = true;
        foreach (var r in Ships.Values) r.Dirty = true;
        foreach (var r in Agents.Values.ToList()) DirtyAgent(r);
        Dirty = true;
        NextOpKey = Ops.Count == 0 ? 1 : Ops.Keys.Max() + 1;
        NextCharacterOpKey = CharacterOps.Count == 0 ? 1 : CharacterOps.Keys.Max() + 1;
        NextFleetOpKey = FleetOps.Count == 0 ? 1 : FleetOps.Keys.Max() + 1;
        NextTaskKey = Tasks.Count == 0 ? 1 : Tasks.Max(t => t.Key) + 1;
        _systemFields.Clear();
        _queues.Clear();
        _unitFields.Clear();
        Snapshot(game, record: true);
    }

    // ---- observed field changes (messages without a sim notification here) ----

    /// <summary>
    /// Stage 6: the messages of field changes the sim has no hook for, from the side's live views, compared with the previous
    /// tick: side MaintState / MaintRequired (cap / dem, aggregates flag), research orders, system fields
    /// (support, energy, raw material, Populated, Explored, Uprising; ControlKind = owner
    /// gives TheatersDue set), manager queue count, fleet Battle / Bombard / Assault (new != 0), hull / squadron damage,
    /// Damaged and crit counters (six messages), character Force / CommandKind. shortcut: a change reverted within one tick posts nothing here (the original posts both).
    /// </summary>
    public void Observe(GameState game)
    {
        _game = game;
        var side = game.Sides[Side];
        if (side.MaintenanceCapacity != MaintenanceCapacity) { MaintenanceCapacity = side.MaintenanceCapacity; Dirty = true; }
        if (side.Demand != MaintenanceDemand) { MaintenanceDemand = side.Demand; Dirty = true; }
        ResearchLevel(ResearchLine.Ship, ShipLevel);
        ResearchLevel(ResearchLine.Troop, TroopLevel);
        ResearchLevel(ResearchLine.Facility, FacilityLevel);
        Snapshot(game, record: false);
    }

    /// <summary>Research-order messages: line ship, troop or facility.</summary>
    void ResearchLevel(ResearchLine line, int old)
    {
        int level = _game.Sides[Side].ResearchOrder[(int)line];
        if (level == old) return;
        switch (line)
        {
            case ResearchLine.Ship:
                ShipLevel = level;
                if (level == MaxShipOrder) ShipLineDone = true;
                ShipBests();
                break;
            case ResearchLine.Troop:
                TroopLevel = level;
                if (level == MaxTroopOrder) TroopLineDone = true;
                TroopBest();
                break;
            default:
                FacilityLevel = level;
                if (level == MaxFacilityOrder) FacilityLineDone = true;
                if (Research.Candidates(_game, Side, Families.ConstructionYard).Any(r => r.Common.Ref == Refs.DeathStarShieldType && r.Get(SdField.ResearchOrder) == level)) DeathStarShieldAvailable = true;
                break;
        }
    }

    void Snapshot(GameState game, bool record)
    {
        void Diff<T>(Dictionary<GameObject, T> seen, GameObject o, T now, Action<T> changed)
        {
            if (seen.TryGetValue(o, out var was) && !EqualityComparer<T>.Default.Equals(was, now) && !record) changed(was);
            seen[o] = now;
        }
        foreach (var s in game.Systems.Where(Live))
        {
            Diff(_systemFields, s, (s.Owner, s.Support, s.Energy, s.RawMaterials, s.Populated, s.Explored, s.Uprising), was =>
            {
                if (was.Item1 != s.Owner) TheatersDue = true;
                Changed(game, s);
            });
            foreach (var mm in s.Managers.Where(x => x is not null)) Diff(_queues, mm, mm.Orders.Count, _ => Changed(game, mm));
        }
        foreach (var o in game.Created.Concat(game.Characters).Where(Live))
        {
            var bits = o is Fleet fb ? (fb.Battle, fb.Bombard, fb.Assault) : default;
            var c = o as Character;
            var agent = o as Agent; // agent's mission ref / OnMission: changed.
            // Injury / Captured: every change dirties the agent record
            Diff(_unitFields, o, (o.HullDamage, o.SquadSizeDamage, c?.Force ?? 0, c?.CommandKind ?? 0, bits, agent?.Mission?.Ref ?? 0, agent?.OnMission ?? false,
                c?.Injury ?? 0, c?.Captured ?? false, o.Damaged, o.CriticalHits), was =>
            {
                // Damaged, crit counters (six messages): changed
                if (was.Item5 == bits || bits != (false, false, false) || was.Item6 != (agent?.Mission?.Ref ?? 0) || was.Item7 != (agent?.OnMission ?? false)
                    || was.Item8 != (c?.Injury ?? 0) || was.Item9 != (c?.Captured ?? false) || was.Item10 != o.Damaged
                    || was.Item11 != o.CriticalHits) Changed(game, o);
            });
        }
    }

    /// <summary>
    /// Destroyed message: the destroyed object's records are detached: agent, ship,
    /// fleet. Inferred: detached = removed from the model's trees; holders (ops, tasks) drop them on their next
    /// refresh. Missions keep their op link (not specced).
    /// </summary>
    public void Detach(GameObject o)
    {
        Agents.Remove(o.Ref);
        Ships.Remove(o.Ref);
        FleetsById.Remove(o.Ref);
        Dirty = true;
    }

    /// <summary>
    /// Parent message: a system gives dirty; a capital ship gives its ship record; fleets, agents, missions gives changed;
    /// headquarters (families 32 and 33) gives TheatersDue set; then changed(new parent) and changed(old parent).
    /// </summary>
    public void ParentChanged(GameState game, GameObject o, GameObject? parent, GameObject? old)
    {
        _game = game;
        uint f = Family.Of(o.Type);
        if (o is StarSystem s && Systems.TryGetValue(s, out var r)) DirtySystem(r);
        else if (f is >= Families.Headquarters and < Families.IonCannon) TheatersDue = true;
        else if (f is >= Families.MajorCharacter and < Families.Mission && Agents.TryGetValue(o.Ref, out var agent))
        {
            // agent location := the parent key of the message (Inferred: the new parent), record dirty
            agent.Location = parent;
            DirtyAgent(agent);
        }
        else if (f is >= Families.Fleet and < Families.Troop or >= Families.CapitalShip and < Families.Fighter or >= Families.MajorCharacter and < Families.Sector) Changed(game, o);
        if (parent is not null) Changed(game, parent);
        if (old is not null) Changed(game, old);
    }


    // ---- planner queries ----
}
