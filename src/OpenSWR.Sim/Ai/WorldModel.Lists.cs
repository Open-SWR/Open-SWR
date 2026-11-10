using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

public sealed partial class WorldModel
{
    /// <summary>Sector list: sector records in key order passing the masks, value at <paramref name="offset"/>.</summary>
    public RankedList SectorList(Func<AiSector, bool> pass, Func<AiSector, int> value, RankOrder dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, value(x))), dir, Rand);

    /// <summary>System list: the sector's system slots in slot order that pass.</summary>
    public RankedList SystemList(AiSector sector, Func<AiSystem, bool> pass, Func<AiSystem, int> value, RankOrder dir) =>
        new(sector.Systems.Where(pass).Select(x => ((object)x, value(x))), dir, Rand);

    /// <summary>
    /// Sector by squared distance to the sector containing <paramref name="reference"/> (the reference's location
    /// through the view parents). Inferred: sector and system positions are the SD X / Y.
    /// </summary>
    public RankedList SectorDistanceList(GameObject reference, Func<AiSector, bool> pass, RankOrder dir)
    {
        var at = LocationOf(reference)?.Sector;
        return new(Sectors.Where(pass).Select(x => ((object)x, at is null ? 0 : Squared(
            x.Sector.Definition.X - at.Definition.X, x.Sector.Definition.Y - at.Definition.Y))), dir, Rand);
    }

    /// <summary>The sector's systems by squared distance to the system containing <paramref name="reference"/>.</summary>
    public RankedList SystemDistanceList(AiSector sector, GameObject reference, Func<AiSystem, bool> pass, RankOrder dir)
    {
        var at = LocationOf(reference);
        return new(sector.Systems.Where(pass).Select(x => ((object)x, at is null ? 0 : Squared(
            x.System.Definition.X - at.Definition.X, x.System.Definition.Y - at.Definition.Y))), dir, Rand);
    }

    static int Squared(int dx, int dy) => dx * dx + dy * dy;

    /// <summary>Sector list: value is the sector's field, or 100 when it is 0 (not specced: reads 0, so 100).</summary>
    public RankedList SectorDefaultList(Func<AiSector, bool> pass, RankOrder dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, x.Entry == 0 ? 100 : x.Entry)), dir, Rand);

    /// <summary>Fleet list: fleet records in key order that pass, valued by <paramref name="value"/>.</summary>
    public RankedList FleetList(Func<AiFleet, bool> pass, Func<AiFleet, int> value, RankOrder dir) =>
        new(FleetRecords().Where(pass).Select(f => ((object)f, value(f))), dir, Rand);

    /// <summary>Fleet list: fleet records that pass, by squared distance of their location to the reference's system.</summary>
    public RankedList FleetDistanceList(GameObject reference, Func<AiFleet, bool> pass, RankOrder dir)
    {
        var at = LocationOf(reference);
        return new(FleetRecords().Where(pass).Select(f => ((object)f,
            at is not null && LocationOf(f.Fleet) is { } l ? Squared(l.Definition.X - at.Definition.X, l.Definition.Y - at.Definition.Y) : 0)), dir, Rand);
    }

    /// <summary>Sector list with a sector as the reference (squared distance of sector positions).</summary>
    public RankedList SectorDistanceFrom(AiSector at, Func<AiSector, bool> pass, RankOrder dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, Squared(
            x.Sector.Definition.X - at.Sector.Definition.X, x.Sector.Definition.Y - at.Sector.Definition.Y))), dir, Rand);

    /// <summary>Sector list: sum of days since S of its systems owned or populated and not own, div (the two sector fields added).</summary>
    public RankedList SectorStalenessList(Func<AiSector, bool> pass, RankOrder dir) =>
        new(Sectors.Where(pass).Select(x =>
        {
            int sum = x.Systems.Where(r => r.OwnedOrPopulated && !r.Own).Sum(r => _game.Day - r.BlockadeDay),
                d = x.NeutralSystems + x.EnemySystems;
            return ((object)x, d == 0 ? sum : sum / d);
        }), dir, Rand);

    /// <summary>Dirties the sector record and sets the aggregates flag.</summary>
    public void DirtySector(AiSector s)
    {
        s.Dirty = true;
        Dirty = true;
    }

    /// <summary>Fleet list: fleet records that pass with an op entry &gt; min, valued by the op entry.</summary>
    public RankedList FleetMinList(int min, Func<AiFleet, bool> pass, RankOrder dir) =>
        new(FleetRecords().Where(f => pass(f) && f.OpEntry > min).Select(f => ((object)f, f.OpEntry)), dir, Rand);

    /// <summary>The own, usable troops aboard the fleet's ships picked by <see cref="ShipList"/> by usable troops (ships in list order, contents in order); the unload / deploy planners share it.</summary>
    public List<GameObject> TroopsAboard(AiFleet fleet, Func<AiShip, bool> pass)
    {
        var ships = new ListStack();
        ships.Push(ShipList(fleet, pass, s => s.UsableTroops, RankOrder.Ascending));
        var troops = new List<GameObject>();
        while (ships.Pick() is AiShip s)
            troops.AddRange(Contents(s.Ship).Where(x => Family.IsTroop(x.Type) && Usable(x) && See(x).Owner == Side));
        return troops;
    }

    /// <summary>Ship list: the fleet's ship records that pass, valued by <paramref name="value"/>.</summary>
    public RankedList ShipList(AiFleet fleet, Func<AiShip, bool> pass, Func<AiShip, int> value, RankOrder dir) =>
        new(Contents(fleet.Fleet).Where(x => Ships.ContainsKey(x.Ref)).Select(x => Ships[x.Ref])
            .Where(pass).Select(s => ((object)s, value(s))), dir, Rand);

    /// <summary>Agent list: agent records in key order that pass, valued by <paramref name="value"/> within [lo, hi].</summary>
    public RankedList AgentList(Func<AiAgent, bool> pass, Func<AiAgent, int> value, int lo, int hi, RankOrder dir, bool minPri = false) =>
        new(Agents.Values.Where(a => pass(a) && value(a) >= lo && value(a) <= hi && (!minPri || a.Priority > 0 || a.Unassigned))
            .Select(a => ((object)a, value(a))), dir, Rand);

    /// <summary>Agent list: agents that pass, by squared distance of their location system to the reference.</summary>
    public RankedList AgentDistanceList(StarSystem reference, Func<AiAgent, bool> pass, RankOrder dir) =>
        new(Agents.Values.Where(pass).Select(a => ((object)a, LocationOf(a.Agent) is { } l
                ? Squared(l.Definition.X - reference.Definition.X, l.Definition.Y - reference.Definition.Y) : 0)), dir, Rand);

    /// <summary>Random-key sector list: per passing sector in key order one rand(20) as its value, then its insertion.</summary>
    public RankedList SectorRandomList(Func<AiSector, bool> pass, RankOrder dir) =>
        new(Sectors.Where(pass).Select(x => ((object)x, Rand(20))), dir, Rand);

    /// <summary>Random-key system list: per passing slot in slot order one rand(10) as its value, then its insertion.</summary>
    public RankedList SystemRandomList(AiSector sector, Func<AiSystem, bool> pass, RankOrder dir) =>
        new(sector.Systems.Where(pass).Select(x => ((object)x, Rand(10))), dir, Rand);

    /// <summary>System list: value = days since S (own systems 0).</summary>
    public RankedList SystemDaysList(AiSector sector, Func<AiSystem, bool> pass, RankOrder dir) =>
        new(sector.Systems.Where(pass).Select(x => ((object)x, x.Own ? 0 : _game.Day - x.BlockadeDay)), dir, Rand);

    /// <summary>Fighter order key: SD TotalAttackStrength x squadron size.</summary>
    public int FighterKey(GameObject o) => _game.Definitions.Fighters.FirstOrDefault(f => f.Common.Ref == o.Type) is { } f ? (int)(f.TotalAttackStrength * f.SquadronSize) : 0;

    /// <summary>The location system of an object through its view parents (null outside systems).</summary>
    public StarSystem? Where(GameObject o) => LocationOf(o);

    // ---- round 6 containers ----

    StarSystem? LocationOf(GameObject o)
    {
        for (GameObject? x = o; x is not null; x = ViewParent(x))
            if (x is StarSystem s) return s;
        return null;
    }
}
