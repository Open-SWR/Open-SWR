namespace OpenSWR.Sim;

/// <summary>
/// Periodic timer types and their actions.
/// Draw-site labels: the functions containing the RNG call, verified.
/// </summary>
public static class TimerCatalogue
{
    /// <summary>Gets or creates the slot of timer <paramref name="type"/> on target <paramref name="target"/>.</summary>
    public static TimerSlot Slot(GameState game, int type, uint target, object? subject)
    {
        if (!game.Timers.TryGetValue((type, target), out var slot))
            game.Timers[(type, target)] = slot = new TimerSlot(type, target, subject, Act);
        return slot;
    }

    /// <summary>Start/restart: delay = Base + roll(Variance) from today.</summary>
    public static void Start(GameState game, int type, uint target, object? subject, int @base, int variance) =>
        Slot(game, type, target, subject).Start(game, @base, variance);

    /// <summary>
    /// Target family range of each type, half-open, read from the catalogue's target column (verified for 0x389 only;
    /// the side, manager and object ranges are read off the column, single families as [f, f + 1)).
    /// </summary>
    public static bool InRange(int type, uint target)
    {
        uint f = Family.Of(target);
        return type switch
        {
            0x380 => f is >= 0x14 and < 0x20,
            0x381 or 0x382 => f == 0xF3,
            >= 0x383 and <= 0x386 or 0x38D or 0x38E => f is >= 0x90 and < 0x98,
            0x387 => f is >= 1 and < 0x100,
            >= 0x388 and <= 0x38A => f is >= 0x30 and < 0x3C,
            0x38B or 0x38C => f is >= 0x40 and < 0x80,
            0x38F or 0x390 => f == 0xF9,
            0x392 or 0x393 => f == 0xF8,
            0x394 => f is >= 0x28 and < 0x30,
            _ => false,
        };
    }

    /// <summary>
    /// Recompute of the character's DiscoveringForceUser flag, and its hook. Timer 0x389 Active follows the flag, with Base
    /// G 0xA16 / Variance G 0xA17. The flag is set when IsJediTrainer is nonzero, Force (rank + bonus) >= G 0xA14, the character
    /// is not OnMission, is Usable and is not Captured. Inferred: Base/Variance written just before activation (the call site
    /// is not known; serials agree with the trace).
    /// </summary>
    public static void RecomputeForceActive(GameState game, Character c)
    {
        bool active = c.Definition.IsJediTrainer != 0 && c.ForceRank + c.ForceBonus >= game.Params.General(0xA14) && !c.OnMission && c.Usable && !c.Captured;
        if (active == c.ForceActive) return;
        c.ForceActive = active;
        var slot = Slot(game, 0x389, c.Ref, c);
        if (active) slot.Start(game, game.Params.General(0xA16), game.Params.General(0xA17));
        else slot.SetActive(game, false);
    }

    static void Act(GameState game, TimerSlot slot)
    {
        switch (slot.Type)
        {
            case 0x380: Repair.Step(game, (GameObject)slot.Subject!); break;
            case 0x381: Research.Accrue(game, (SideState)slot.Subject!); break;
            case 0x382: Economy.Autoscrap(game, (SideState)slot.Subject!); break;
            case 0x383: Systems.SuppressTimer(game, (StarSystem)slot.Subject!); break;
            case 0x384: Systems.UprisingSupport(game, (StarSystem)slot.Subject!); break;
            case 0x38D: Systems.Incident(game, (StarSystem)slot.Subject!); break; // UprisingIncident pulse leads to an incident.
            case 0x385: break; // Support change G 0x1E0B is 0 in shipped data. Loyalty is not done here. No draw.
            case 0x386: Combat.BlockadeSupport(game, (StarSystem)slot.Subject!); break;
            case 0x387:
                // If HyperdriveActive: HyperdriveActive := 0 (hook: slot inactive, 0x306), ETA := 0.
                if (slot.Subject is GameObject o && o.HyperdriveActive) { Objects.SetHyperdriveActive(game, o, false); o.Eta = 0; }
                break;
            case 0x388: Prisoners.Heal(game, (Character)slot.Subject!); break;
            case 0x38A: Prisoners.EscapeTimer(game, (Character)slot.Subject!); break;
            case 0x389: Jedi.Discover(game, (Character)slot.Subject!); break; // Force user discovery
            case 0x38B: Missions.PhaseTimer(game, (Mission)slot.Subject!); break; // mission phase timer
            case 0x38C: Missions.CompletionTimer(game, (Mission)slot.Subject!); break; // mission completion timer
            case 0x38E:
                // Owner != 3: chance(s), where s is the owner's support share. Failure gives an InformantIncident pulse.
                if (slot.Subject is StarSystem system && system.Owner != Owner.Neutral
                    && !game.Random.Chance(system.SupportFor(system.Owner), DrawSite.Incident))
                    Systems.Informant(game, system);
                break;
            case 0x38F: Systems.DisasterEvent(game); break;
            case 0x390: Systems.ResourceEvent(game); break;
            // One-shot: LukeDagobahRequired := 1 (Dagobah check, slot inactive). The
            // firing still draws its reschedule, and the event dies stale.
            case 0x392: Story.DagobahTimer(game); break;
            case 0x393: Story.BountyTimer(game); break;

            case 0x394: Economy.FacilityTimer(game, (Facility)slot.Subject!); break;
            default: throw new InvalidOperationException($"Timer type {slot.Type:X} has no action.");
        }
    }
}
