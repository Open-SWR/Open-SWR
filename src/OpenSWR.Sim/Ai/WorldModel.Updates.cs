using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>
    /// Object changed: walk from the object up its view parents to the first family with records: systems gives dirty the system
    /// record; characters / special forces gives create the agent record if missing (view present, not Destroyed), else dirty it;
    /// capital ships, fleets, missions gives their records (not built yet, nothing); families 241, 242, 248 and 152 to 159 gives nothing.
    /// </summary>
    public void Changed(GameState game, GameObject o)
    {
        _game = game;
        for (GameObject? x = o; x is not null; x = ViewParent(x))
        {
            uint f = Family.Of(x.Type);
            switch (f)
            {
                case >= Families.CoreSystem and < Families.Abode:
                    if (x is StarSystem s && Systems.TryGetValue(s, out var sr)) DirtySystem(sr);
                    return;
                case >= Families.MajorCharacter and < Families.Mission:
                    if (x is not Agent a) return;
                    if (Agents.TryGetValue(a.Ref, out var ar)) DirtyAgent(ar);
                    else if (HasView(a) && !See(a).Destroyed) CreateAgent(a);
                    return;
                case >= Families.CapitalShip and < Families.Fighter:
                    if (Ships.TryGetValue(x.Ref, out var ship)) ship.Dirty = true;
                    else if (HasView(x) && !See(x).Destroyed) Ships[x.Ref] = NewShip(x);
                    Dirty = true;
                    return;
                case >= Families.Mission and < Families.Sector: // a mission view with a UserID gives its op (unless finished)
                    if (x is Mission { Kind: >= MissionKind.Diplomacy, Mandatory: false, UserId: > 0 } mission && Ops.TryGetValue((int)mission.UserId, out var op)
                        && !op.Finished.Contains(mission) && !See(mission).Destroyed)
                        (op.Mission, op.Running) = (mission, true);
                    return;
                case >= Families.Fleet and < Families.Troop: // fleet records (round 7)
                case Families.Unmapped or Families.Staging or Families.MissionManager or >= Families.Abode and < Families.ConstructionManager:
                    return;
            }
        }
    }

    /// <summary>
    /// "Name changed": the ship record's Named flag (kept across updates). Inferred: posted when a
    /// Rename runs (the sim's rename has no other effect).
    /// </summary>
    public void Named(GameState game, GameObject o)
    {
        _game = game;
        if (!Family.IsCapitalShip(o.Type)) return;
        Changed(game, o);
        if (Ships.TryGetValue(o.Ref, out var r)) r.Named = true;
    }

    /// <summary>The system and its sector dirty, and the aggregates flag.</summary>
    internal void DirtySystem(AiSystem r)
    {
        r.Dirty = true;
        if (r.Sector is { } sector) sector.Dirty = true;
        Dirty = true;
    }

    /// <summary>Sets the aggregates flag, then the record of its location.</summary>
    internal void DirtyAgent(AiAgent r)
    {
        r.Dirty = true;
        Dirty = true;
        if (r.Location is { } l) Changed(_game, l);
    }

    // ---- update: agents, fleets, sectors with their systems, aggregates ----

    public void Update(GameState game)
    {
        _game = game;
        _frozenContents = null;
        foreach (var r in Agents.Values.Where(r => r.Dirty).ToList())
        {
            UpdateAgent(r);
            AgentsChanged = Dirty = true;
        }
        // shortcut: every ship and fleet record is refreshed on every update (the original refreshes the dirty ones; all record
        // inputs are view fields whose changes are messaged, so the values agree), upgrade if an AI replay diverges on a ship /
        // fleet record value or Update shows in a profile; fleet records are created with a ship record
        foreach (var r in Ships.Values)
        {
            UpdateShip(r);
            if (ViewParent(r.Ship) is Fleet fleet && !FleetsById.ContainsKey(fleet.Ref))
                FleetsById[fleet.Ref] = new AiFleet(fleet) { Key = (int)fleet.Ref, Owner = (int)See(fleet).Owner, Free = true };
        }
        foreach (var f in FleetsById.Values) UpdateFleet(f);
        if (FleetsById.Count > 0) FleetsChanged = Dirty = true;
        foreach (var r in Sectors.Where(r => r.Dirty))
        {
            UpdateSector(r);
            SectorsChanged = Dirty = true;
        }
        if (!Dirty) return;
        if (SectorsChanged) SectorAggregates();
        if (FleetsChanged) FleetAggregates();
        if (AgentsChanged) AgentAggregates();
        AgentsChanged = SectorsChanged = FleetsChanged = Dirty = false;
    }

    /// <summary>cap / dem = the side's maintenance capacity and demand, copied at init.</summary>
    void Budget(GameState game) => (MaintenanceCapacity, MaintenanceDemand) = (game.Sides[Side].MaintenanceCapacity, game.Sides[Side].Demand);
}
