using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>Periodic timer types.</summary>
public enum TimerType
{
    /// <summary>Not set (the default value).</summary>
    None,
    Repair,
    Research,
    /// <summary>Maintenance enforcement (autoscrap).</summary>
    Maintenance,
    Suppress,
    UprisingSupport,
    /// <summary>Support change of a garrisoned Empire system.</summary>
    GarrisonSupport,
    BlockadeSupport,
    /// <summary>Hyperdrive travel.</summary>
    Travel,
    Heal,
    ForceDiscovery,
    Escape,
    MissionPhase,
    MissionCompletion,
    UprisingIncident,
    Informant,
    Disaster,
    ResourceEvent,
    Dagobah,
    BountyHunters,
    /// <summary>Mine or refinery cycle.</summary>
    FacilityCycle,
}

/// <summary>
/// Periodic timer types and their actions.
/// Draw-site labels: the functions containing the RNG call, verified.
/// </summary>
public static class TimerCatalogue
{
    /// <summary>Gets or creates the slot of timer <paramref name="type"/> on target <paramref name="target"/>.</summary>
    public static TimerSlot Slot(GameState game, TimerType type, uint target, object? subject)
    {
        if (!game.Timers.TryGetValue((type, target), out var slot))
            game.Timers[(type, target)] = slot = new TimerSlot(type, target, subject, Act);
        return slot;
    }

    /// <summary>Start/restart: delay = Base + roll(Variance) from today.</summary>
    public static void Start(GameState game, TimerType type, uint target, object? subject, int @base, int variance) =>
        Slot(game, type, target, subject).Start(game, @base, variance);

    /// <summary>
    /// Target family range of each type, half-open, read from the catalogue's target column (verified for the Force discovery timer only;
    /// the side, manager and object ranges are read off the column, single families as [f, f + 1)).
    /// </summary>
    public static bool InRange(TimerType type, uint target)
    {
        uint f = Family.Of(target);
        return type switch
        {
            TimerType.Repair => f is >= Families.CapitalShip and < Families.Headquarters,
            TimerType.Research or TimerType.Maintenance => f == Families.Side,
            TimerType.Suppress or TimerType.UprisingSupport or TimerType.GarrisonSupport or TimerType.BlockadeSupport or TimerType.UprisingIncident or TimerType.Informant => f is >= Families.CoreSystem and < Families.Abode,
            TimerType.Travel => f is >= 1 and < 256,
            TimerType.Heal or TimerType.ForceDiscovery or TimerType.Escape => f is >= Families.MajorCharacter and < Families.SpecialForce,
            TimerType.MissionPhase or TimerType.MissionCompletion => f is >= Families.Mission and < Families.Sector,
            TimerType.Disaster or TimerType.ResourceEvent => f == Families.GameManager,
            TimerType.Dagobah or TimerType.BountyHunters => f == Families.MissionManager,
            TimerType.FacilityCycle => f is >= Families.Shipyard and < Families.MajorCharacter,
            _ => false,
        };
    }

    /// <summary>
    /// Recompute of the character's DiscoveringForceUser flag, and its hook. The Force discovery timer Active follows the flag, with Base
    /// and Variance from the Force discovery parameters. The flag is set when IsJediTrainer is nonzero, Force (rank + bonus) >= the Force discovery threshold, the character
    /// is not OnMission, is Usable and is not Captured. Inferred: Base/Variance written just before activation (the call site
    /// is not known; serials agree with the trace).
    /// </summary>
    public static void RecomputeForceActive(GameState game, Character c)
    {
        bool active = c.Definition.IsJediTrainer != 0 && c.ForceRank + c.ForceBonus >= game.Rules.ForceDiscoveryMinForce && !c.OnMission && c.Usable && !c.Captured;
        if (active == c.ForceActive) return;
        c.ForceActive = active;
        var slot = Slot(game, TimerType.ForceDiscovery, c.Ref, c);
        if (active) slot.Start(game, game.Rules.ForceDiscoveryDays, game.Rules.ForceDiscoveryVariance);
        else slot.SetActive(game, false);
    }

    static void Act(GameState game, TimerSlot slot)
    {
        switch (slot.Type)
        {
            case TimerType.Repair: Repair.Step(game, (GameObject)slot.Subject!); break;
            case TimerType.Research: Research.Accrue(game, (SideState)slot.Subject!); break;
            case TimerType.Maintenance: Economy.Autoscrap(game, (SideState)slot.Subject!); break;
            case TimerType.Suppress: Systems.SuppressTimer(game, (StarSystem)slot.Subject!); break;
            case TimerType.UprisingSupport: Systems.UprisingSupport(game, (StarSystem)slot.Subject!); break;
            case TimerType.UprisingIncident: Systems.Incident(game, (StarSystem)slot.Subject!); break; // UprisingIncident pulse leads to an incident.
            case TimerType.GarrisonSupport: break; // Garrison support change parameter is 0 in shipped data. Loyalty is not done here. No draw.
            case TimerType.BlockadeSupport: Combat.BlockadeSupport(game, (StarSystem)slot.Subject!); break;
            case TimerType.Travel:
                // If HyperdriveActive: HyperdriveActive := 0 (hook: slot inactive), ETA := 0.
                if (slot.Subject is GameObject o && o.HyperdriveActive) { Objects.SetHyperdriveActive(game, o, false); o.Eta = 0; }
                break;
            case TimerType.Heal: Prisoners.Heal(game, (Character)slot.Subject!); break;
            case TimerType.Escape: Prisoners.EscapeTimer(game, (Character)slot.Subject!); break;
            case TimerType.ForceDiscovery: Jedi.Discover(game, (Character)slot.Subject!); break; // Force user discovery
            case TimerType.MissionPhase: Missions.PhaseTimer(game, (Mission)slot.Subject!); break; // mission phase timer
            case TimerType.MissionCompletion: Missions.CompletionTimer(game, (Mission)slot.Subject!); break; // mission completion timer
            case TimerType.Informant:
                // Owner != 3: chance(s), where s is the owner's support share. Failure gives an InformantIncident pulse.
                if (slot.Subject is StarSystem system && system.Owner != Owner.Neutral
                    && !game.Random.Chance(system.SupportFor(system.Owner), DrawSite.Incident))
                    Systems.Informant(game, system);
                break;
            case TimerType.Disaster: Systems.DisasterEvent(game); break;
            case TimerType.ResourceEvent: Systems.ResourceEvent(game); break;
            // One-shot: LukeDagobahRequired := 1 (Dagobah check, slot inactive). The
            // firing still draws its reschedule, and the event dies stale.
            case TimerType.Dagobah: Story.DagobahTimer(game); break;
            case TimerType.BountyHunters: Story.BountyTimer(game); break;

            case TimerType.FacilityCycle: Economy.FacilityTimer(game, (Facility)slot.Subject!); break;
            default: throw new InvalidOperationException($"Timer type {slot.Type} has no action.");
        }
    }
}
