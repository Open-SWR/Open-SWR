namespace OpenSWR.Sim.Ai;

/// <summary>
/// Fleet operation: key, produce state, flagship, gather
/// system (written by state 6), theater sector, base system, theater entry,
/// task, status, refresh flag, need counters, copies,
/// fighter need, attached object-op counts, mission request, attached character-op counts
/// (types 3, 4 and 5), character-op create / retire requests. Members: ships, fleets,
/// attached character ops, attached object-kind mission ops.
/// </summary>
public sealed class FleetOp(int key)
{
    public int Key { get; } = key;
    public int State { get; set; }
    public AiSector? Theater { get; set; }
    public StarSystem? Base { get; set; }
    public int Entry { get; set; }
    public FleetTask? Task { get; set; }
    public FleetTaskType TaskKind => Task?.Type ?? FleetTaskType.None;
    public bool Refresh { get; set; }
    public AiShip? Flagship { get; set; }
    /// <summary>The gather system.</summary>
    public StarSystem? Gather { get; set; }
    public List<AiShip> Ships { get; } = [];
    public List<AiFleet> Fleets { get; } = [];
    public List<CharacterOp> CharacterOps { get; } = [];
    /// <summary>Attached object-kind mission ops, counted in <see cref="ObjectOps"/> by class index.</summary>
    public List<MissionOp> MissionOps { get; } = [];
    /// <summary>The attached character op to retire.</summary>
    public CharacterOp? RetireRequest { get; set; }
    // status, rebuilt by every produce cycle
    /// <summary>Some need is short and the op has nothing that counts. Meaning in the original is unknown.</summary>
    public bool Empty { get; set; }
    /// <summary>The task type's goal holds (Inferred meaning).</summary>
    public bool Goal { get; set; }
    /// <summary>Nothing is short. Meaning in the original is unknown.</summary>
    public bool Complete { get; set; }
    /// <summary>No strength or capacity is short. Meaning in the original is unknown.</summary>
    public bool StrongEnough { get; set; }
    /// <summary>Set by the last produce state unless <see cref="Empty"/> is. Meaning in the original is unknown.</summary>
    public bool NotEmpty { get; set; }
    /// <summary>A type-4 character op whose character is aboard the flagship with its command kind issued.</summary>
    public bool GeneralAtShip { get; set; }
    /// <summary>A character op whose character is aboard the flagship without its command kind issued.</summary>
    public bool CharacterAtShip { get; set; }
    /// <summary>An attached character op.</summary>
    public bool CharacterOpAttached { get; set; }
    /// <summary>
    /// Set by the task status every cycle; between the character status and the task status it says an attached character op
    /// waits at the gather system (planner 8's create gate). Meaning in the original is unknown.
    /// </summary>
    public bool CharacterWaiting { get; set; }
    /// <summary>Set with a theater; cleared by the theater check.</summary>
    public bool TheaterSet { get; set; }
    // needs: a counter below 0 after the shortfall builders
    public bool ShortFirepower { get; set; }
    public bool ShortAntiFighterFirepower { get; set; }
    public bool ShortTroopCapacity { get; set; }
    public bool ShortFighterCapacity { get; set; }
    public bool ShortBombardment { get; set; }
    public bool ShortTroops { get; set; }
    public bool ShortFighters { get; set; }
    public bool ShortAntiFighters { get; set; }
    public bool ShortInterdictors { get; set; }
    /// <summary>A mission-op create request was set.</summary>
    public bool MissionRequested { get; set; }
    /// <summary>A character-op create or retire request.</summary>
    public bool CharacterRequested { get; set; }
    /// <summary>An attached op without a full team.</summary>
    public bool OpWithoutTeam { get; set; }
    // needs: a counter above 0 after the member sums
    public bool HasInterdictors { get; set; }
    public bool HasFirepower { get; set; }
    public bool HasAntiFighterFirepower { get; set; }
    public bool HasTroopCapacity { get; set; }
    public bool HasFighterCapacity { get; set; }
    public bool HasBombardment { get; set; }
    public bool HasTroops { get; set; }
    public bool HasFighters { get; set; }
    public bool HasAntiFighters { get; set; }
    /// <summary>An attached mission op.</summary>
    public bool HasMissionOps { get; set; }
    /// <summary>An attached character op.</summary>
    public bool HasCharacterOps { get; set; }
    /// <summary>An attached op with its team.</summary>
    public bool OpTeamed { get; set; }
    /// <summary>A strength, capacity or bombardment need is short.</summary>
    public bool ForceShort => ShortFirepower || ShortAntiFighterFirepower || ShortTroopCapacity || ShortFighterCapacity || ShortBombardment;
    /// <summary>Any need is short, or an attached op lacks its team.</summary>
    public bool AnyShort => ForceShort || ShortTroops || ShortFighters || ShortAntiFighters || OpWithoutTeam;
    /// <summary>Something counts: a positive counter other than interdictors, or an attached op with its team.</summary>
    public bool HasAny => HasFirepower || HasAntiFighterFirepower || HasTroopCapacity || HasFighterCapacity || HasBombardment || HasTroops
        || HasFighters || HasAntiFighters || OpTeamed;

    /// <summary>Clears the status and the needs.</summary>
    internal void ClearStatus()
    {
        (Empty, Goal, Complete, StrongEnough, NotEmpty, GeneralAtShip, CharacterAtShip, CharacterOpAttached, CharacterWaiting, TheaterSet)
            = (false, false, false, false, false, false, false, false, false, false);
        (ShortFirepower, ShortAntiFighterFirepower, ShortTroopCapacity, ShortFighterCapacity, ShortBombardment, ShortTroops, ShortFighters)
            = (false, false, false, false, false, false, false);
        (ShortAntiFighters, ShortInterdictors, MissionRequested, CharacterRequested, OpWithoutTeam) = (false, false, false, false, false);
        (HasInterdictors, HasFirepower, HasAntiFighterFirepower, HasTroopCapacity, HasFighterCapacity, HasBombardment, HasTroops, HasFighters)
            = (false, false, false, false, false, false, false, false);
        (HasAntiFighters, HasMissionOps, HasCharacterOps, OpTeamed) = (false, false, false, false);
    }
    // need counters (state 2: member-fleet sums minus the task targets; a joining ship adds its figures)
    /// <summary>Fleet <see cref="AiFleet.FirepowerAllFighters"/> / ship <see cref="AiShip.Firepower"/>. Meaning in the original is unknown.</summary>
    public int Firepower { get; set; }
    /// <summary>Fleet <see cref="AiFleet.AntiFighterFirepowerAllFighters"/> / ship <see cref="AiShip.AntiFighterFirepower"/>. Meaning in the original is unknown.</summary>
    public int AntiFighterFirepower { get; set; }
    public int TroopCapacity { get; set; }
    public int FighterCapacity { get; set; }
    /// <summary>Fleets with a Death Star / ships of class Death Star.</summary>
    public int DeathStars { get; set; }
    /// <summary>Fleet <see cref="AiFleet.Bombardment"/> / ship <see cref="AiShip.Bombardment"/>. Meaning in the original is unknown.</summary>
    public int Bombardment { get; set; }
    /// <summary>Fleets with an interdictor / interdictor ships.</summary>
    public int Interdictors { get; set; }
    public int Troops { get; set; }
    public int Fighters { get; set; }
    /// <summary>Fleet <see cref="AiFleet.AntiFighter"/> / ships of class AntiFighter.</summary>
    public int AntiFighters { get; set; }
    /// <summary><see cref="TroopCapacity"/> before the targets.</summary>
    public int TroopCapacitySum { get; set; }
    /// <summary><see cref="FighterCapacity"/> before the targets.</summary>
    public int FighterCapacitySum { get; set; }
    /// <summary>Fighter need.</summary>
    public int FighterNeed { get; set; }
    /// <summary>Attached object-kind mission ops per class index.</summary>
    public ref int ObjectOps(int index) => ref _objectOps[index];
    readonly int[] _objectOps = new int[4];
    /// <summary>The mission request's kind.</summary>
    public int RequestKind { get; set; }
    /// <summary>The mission request's subtype.</summary>
    public int RequestSubtype { get; set; }
    /// <summary>The attached mission op to retire.</summary>
    public int RetireKey { get; set; }
    /// <summary>Attached character ops of type 3, 4 and 5.</summary>
    public ref int CharacterOpCount(CharacterOpType type) => ref _characterOps[type switch { CharacterOpType.FleetAdmiral => 0, CharacterOpType.FleetGeneral => 1, CharacterOpType.FleetCommander => 2, _ => throw new ArgumentOutOfRangeException(nameof(type)) }];
    readonly int[] _characterOps = new int[3];
    /// <summary>The character-op type to create.</summary>
    public CharacterOpType CharacterRequest { get; set; }

    /// <summary>Zeroes the need counters.</summary>
    internal void ClearCounters() =>
        (Firepower, AntiFighterFirepower, TroopCapacity, FighterCapacity, DeathStars, Bombardment, Interdictors, Troops, Fighters, AntiFighters) = (0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
