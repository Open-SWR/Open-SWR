using OpenSWR.Data.Dat;
namespace OpenSWR.Sim.Ai;

    /// <summary>
    /// Garrison task: key, state, the task's fleet, target system, strength balance, refresh flag, and taken ships.
    /// </summary>
public sealed class GarrisonTask(int key)
{
    public int Key { get; } = key;
    public int State { get; set; }
    public AiFleet? Fleet { get; set; }
    public StarSystem? Target { get; set; }
    public int Balance { get; set; }
    public bool Refresh { get; set; }
    public List<AiShip> Ships { get; } = [];


    /// <summary>Adopt: the fleet becomes the task's, garrison-held instead of free, with no op key or entry.</summary>
    void Adopt(AiFleet f)
    {
        Fleet = f;
        (f.Free, f.Garrison) = (false, true);
        (f.OpKey, f.OpEntry) = (0, 0);
    }

    /// <summary>Release: free again instead of garrison-held, with no op key or entry, and no task fleet.</summary>
    void ReleaseFleet()
    {
        if (Fleet is not { } f) return;
        (f.Garrison, f.Free) = (false, true);
        (f.OpKey, f.OpEntry) = (0, 0);
        Fleet = null;
    }

    /// <summary>Take: the ship is garrison-taken instead of free, with no op key or entry; the balance gains its strength.</summary>
    void Take(AiShip s)
    {
        (s.Free, s.Garrison) = (false, true);
        (s.OpKey, s.OpEntry) = (0, 0);
        Balance += s.Firepower;
        Ships.Add(s);
    }

    /// <summary>Drop: a ship without an op key is free again (garrison and task marks cleared); the balance loses its strength.</summary>
    void Drop(AiShip s)
    {
        if (s.OpKey == 0) (s.Garrison, s.FlagshipMark, s.FlagshipWanted, s.Free) = (false, false, false, true);
        (s.OpKey, s.OpEntry) = (0, 0);
        Balance -= s.Firepower;
        Ships.Remove(s);
    }

    AiFleet? FleetOf(WorldModel m, AiShip s) => m.ViewParent(s.Ship) is Fleet f ? m.FleetsById.GetValueOrDefault(f.Ref) : null;

    /// <summary>
    /// Refresh: the task fleet must be own, held and non-empty, else it is released; each own task ship stays in the task fleet;
    /// without a task fleet the ship's free fleet is adopted; otherwise the ship is dropped.
    /// </summary>
    void RefreshMembers(WorldModel m)
    {
        if (Fleet is { } f && !(f.Own && f.Garrison && m.ShipsOf(f).Any())) ReleaseFleet();
        foreach (var s in Ships.ToList())
        {
            var own = FleetOf(m, s);
            if (s.Own && Fleet is not null && own == Fleet) continue;
            if (s.Own && Fleet is null && own is not null && own.Free) { Adopt(own); continue; }
            Drop(s);
        }
    }

    /// <summary>Release (state 8): drop all ships, release the fleet, clear the target's mark.</summary>
    public void Release(WorldModel m)
    {
        foreach (var s in Ships.ToList()) Drop(s);
        ReleaseFleet();
        if (m.Record(Target) is { } target) target.GarrisonTaskTarget = false;
    }

    /// <summary>Produce, one state per call; a call that returned a command sets the refresh flag.</summary>
    public (Command? Command, bool Finished) Produce(WorldModel m, Owner side)
    {
        if (Refresh) { RefreshMembers(m); Refresh = false; }
        var (c, finished) = Step(m, side);
        if (c is not null) Refresh = true;
        if (finished) State = (int)GarrisonTaskState.Start; // Inferred: a finished task starts again at state 0
        return (c, finished);
    }

    (Command?, bool) Step(WorldModel m, Owner side)
    {
        switch ((GarrisonTaskState)State)
        {
            case GarrisonTaskState.Start:
                (Refresh, State) = (true, (int)GarrisonTaskState.FindTarget);
                return (null, false);
            case GarrisonTaskState.FindTarget:
                State = (int)(FindTarget(m) ? GarrisonTaskState.Split : GarrisonTaskState.Release);
                return (null, false);
            case GarrisonTaskState.Split:
            {
                State = (int)GarrisonTaskState.Balance;
                if (Fleet is not { } f || !(f.DamagedMoving || f.DamagedStopped)
                    || f.Claimed || f.EnrouteShip || f.Enroute) return (null, false);
                var split = m.ShipsOf(f).Where(s => (s.DamagedStopped || s.DamagedMoving) && !s.Enroute)
                    .Select(s => s.Ship).ToList();
                return (split.Count == 0 || f.Location is null ? null : new Command(CommandType.CreateFleet, side) { Targets = [.. split.Select(s => (s, GroupMode.Object))], ArgRef = f.Location }, false);
            }
            case GarrisonTaskState.Balance:
                Balance = (Fleet?.FirepowerAllFighters ?? 0) - m.BestArmedFirepower * (m.HeadquartersMode ? 2 : 1);
                State = (int)GarrisonTaskState.Reinforce;
                return (null, false);
            case GarrisonTaskState.Reinforce:
            {
                if (Fleet is { } moving && moving.Enroute || Balance == 0) { State = (int)GarrisonTaskState.UnloadTroops; return (null, false); }
                if (Balance > 0) { State = (int)GarrisonTaskState.UnloadTroops; return (Surplus(m, side), false); }
                var taken = Reinforce(m);
                if (taken.Count == 0) { State = (int)GarrisonTaskState.UnloadTroops; return (null, false); }
                var targets = taken.Select(s => (s.Ship, GroupMode.Object)).ToList();
                return (Fleet is { } f ? new Command(CommandType.Move, side) { Targets = targets, ArgRef = f.Fleet } : new Command(CommandType.CreateFleet, side) { Targets = targets, ArgRef = Target }, false);
            }
            case GarrisonTaskState.UnloadTroops:
            {
                State = (int)GarrisonTaskState.UnloadCharacters;
                if (Fleet is not { } f || f.UsableTroops <= 0 || m.Record(f.Location) is not { } at || !at.Own) return (null, false);
                var troops = m.TroopsAboard(f, s => s.Own && !s.Free && !s.Enroute);
                return (troops.Count == 0 ? null : new Command(CommandType.Move, side) { Targets = [.. troops.Select(t => (t, GroupMode.Object))], ArgRef = f.Location }, false);
            }
            case GarrisonTaskState.UnloadCharacters: // one list, own and prisoners, in ship then contents order; an own one arms it
            {
                State = (int)GarrisonTaskState.Move;
                if (Fleet is not { } f || !f.OwnCharacter || f.Enroute || m.Record(f.Location) is not { } at
                    || !at.Own) return (null, false);
                var go = new List<GameObject>();
                bool armed = false;
                foreach (var s in Ships.Where(s => !s.Enroute && (s.OwnCharacter || s.EnemyCaptured)))
                    foreach (var x in m.Contents(s.Ship).Where(x => Family.Of(x.Type) is >= Families.MajorCharacter and < Families.Mission && m.Agents.ContainsKey(x.Ref)))
                    {
                        go.Add(x);
                        if (m.Agents[x.Ref].Own) armed = true;
                    }
                return (armed ? new Command(CommandType.Move, side) { Targets = [.. go.Select(x => (x, GroupMode.Object))], ArgRef = f.Location } : null, false);
            }
            case GarrisonTaskState.Move:
            {
                if (m.Record(Target) is { } t && !t.Blockade && Fleet is { } f && !f.Enroute && f.Location != Target)
                    return (new Command(CommandType.Move, side) { Targets = [(f.Fleet, GroupMode.Object)], ArgRef = Target }, true);
                return (null, true);
            }
            default: // Release
                Release(m);
                return (null, true);
        }
    }

    /// <summary>
    /// Target: keep an own target that is a key system and not blockaded, marking it; else sectors with an own key system by
    /// random keys (rand(20)), and per sector in pick order its own key systems that are not blockaded and not already marked,
    /// by random keys (rand(10)); the first pick.
    /// </summary>
    bool FindTarget(WorldModel m)
    {
        if (m.Record(Target) is { } kept)
        {
            if (kept.Own && kept.KeySystem && !kept.Blockade) { kept.GarrisonTaskTarget = true; return true; }
            kept.GarrisonTaskTarget = false;
            Target = null;
        }
        var sectors = new ListStack();
        sectors.Push(m.SectorRandomList(x => x.KeySystem, RankOrder.Ascending));
        while (sectors.Pick() is AiSector s)
        {
            var systems = new ListStack();
            systems.Push(m.SystemRandomList(s, r => r.KeySystem && r.Own && !r.Blockade && !r.GarrisonTaskTarget, RankOrder.Ascending));
            if (systems.Pick() is AiSystem r)
            {
                Target = r.System;
                r.GarrisonTaskTarget = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Surplus: the first task ship in key order that is neither not-Completed nor Enroute, not held back (its fleet is at an enemy system or
    /// has a claim), and with strength at most the balance, is dropped: a CreateFleet command for the ship to the target. One per call.
    /// </summary>
    Command? Surplus(WorldModel m, Owner side)
    {
        foreach (var s in Ships.OrderBy(s => s.Ship.Ref).ToList())
        {
            var f = FleetOf(m, s);
            bool held = f is not null && (m.Record(f.Location)?.Enemy == true || f.Claimed);
            if ((s.NotCompleted || s.Enroute) || held || s.Firepower > Balance) continue;
            Drop(s);
            return Target is null ? null : new Command(CommandType.CreateFleet, side) { Targets = [(s.Ship, GroupMode.Object)], ArgRef = Target };
        }
        return null;
    }

    /// <summary>
    /// Reinforce (balance below 0): fleets nearest the target that are own, free and have an UndamagedShip, and are not Enroute
    /// or held; else the fallback search over fleets whose value is above 0. In the first fleet that gives a ship, every free
    /// armed ship (class 1, none of the excluded flags) joins while the balance is negative.
    /// </summary>
    List<AiShip> Reinforce(WorldModel m)
    {
        var taken = new List<AiShip>();
        static bool Own(AiFleet f) => f.Own && f.UndamagedShip;
        foreach (var list in new[] { Target is null ? null : m.FleetDistanceList(Target, f => Own(f) && f.Free && !f.Garrison && !f.Enroute, RankOrder.Descending),
            m.FleetMinList(0, f => Own(f) && !f.Claimed && !f.Garrison && !f.Free && !f.Enroute, RankOrder.Ascending) })
        {
            if (list is null) continue;
            var stack = new ListStack();
            stack.Push(list);
            while (taken.Count == 0 && stack.Pick() is AiFleet f)
                foreach (var s in m.ShipsOf(f).ToList())
                    if (Balance < 0 && s.Free && s.Armed
                        && !(s.DamagedStopped || s.NotCompleted || s.DamagedMoving || s.Enroute))
                    {
                        Take(s);
                        taken.Add(s);
                    }
            if (taken.Count > 0) break;
        }
        return taken;
    }
}
