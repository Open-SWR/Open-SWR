using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// openswr's own save format (original saves are not read): JSON {"format": "openswr-save", "version": 4, ...} holding the complete
/// live state: every object (class, ids, flags, fields, contents in key order; references as object refs: family and id),
/// the game lists, sides, views, timer slots, the timed queue's timer events, pending commands, the clock, the id counter and
/// the generator state, the event log (optional), the side controllers (optional; with them the UI generator state, which a load discards). Definitions are not saved: <see cref="Load"/> takes them from the install and resolves records by SdRef.
/// A save is taken between host-loop ticks (<see cref="Clock.Step"/> drains the priority and FIFO queues, so the timed queue holds
/// only timer events); queued notifications refuse the save. Not saved: trace hooks (<see cref="GameState.FactTrace"/>,
/// <see cref="GameRandom.Trace"/>, <see cref="Clock.TimerTrace"/>), the replay input <see cref="GameState.BattleResults"/>, the
/// result callbacks of pending commands, the distance-list cache (recomputed, no draw).
/// </summary>
public static class SaveGame
{
    public const string Format = "openswr-save";
    public const int Version = 4; // version 4: message categories, helper kind, group modes, prisoner states and window kinds are enum names (version 3 stored numbers)

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        // internal codes have no fixed numbers: enums are saved and read by name only, so a number is refused instead of misread
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    // ---- DTOs ----

    sealed class SaveDto
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public SettingsDto Settings { get; set; } = new();
        public int RngState { get; set; }
        public long RngDraws { get; set; }
        public int Tick { get; set; }
        public int Day { get; set; }
        public int TicksPerDay { get; set; }
        public int SubTick { get; set; }
        public bool DayDue { get; set; }
        public uint Serial { get; set; }
        public uint NextId { get; set; }
        public bool Ready { get; set; }
        public bool BattleRequired { get; set; }
        public VictoryStatus VictoryState { get; set; }
        public StoryFlags? StoryFlags { get; set; }
        public int WaveTick { get; set; }
        public List<int[]> WaveCounts { get; set; } = [];
        public uint? Headquarters { get; set; }
        public List<uint> Sectors { get; set; } = [];
        public List<uint> Systems { get; set; } = [];
        public List<uint> Characters { get; set; } = [];
        public List<uint> Created { get; set; } = [];
        public List<uint> Fleets { get; set; } = [];
        public List<uint> Facilities { get; set; } = [];
        public List<uint> Missions { get; set; } = [];
        public List<uint> Abodes { get; set; } = [];
        public List<SideDto> Sides { get; set; } = [];
        public List<ViewDto> Views { get; set; } = [];
        public List<TimerDto> Timers { get; set; } = [];
        public List<EventDto> Events { get; set; } = [];
        public List<CommandDto> Commands { get; set; } = [];
        public List<ObjectDto> Objects { get; set; } = [];
        /// <summary>Message store.</summary>
        public List<GameMessage>? Log { get; set; }
        /// <summary>Side controllers (optional: absent without controllers and in older version-1 saves).</summary>
        public List<ControllerDto>? Controllers { get; set; }
        /// <summary>UI generator state, written with controllers; a load discards it (never restored).</summary>
        public int? UiRngState { get; set; }
        /// <summary>Active mods in load order. A load needs the same list.</summary>
        public List<ModDto>? Mods { get; set; }
        /// <summary>Map filter mode (optional: absent for the off value 128 and in older saves, version 1).</summary>
        public int? FilterMode { get; set; }
        /// <summary>Open strategy-screen windows.</summary>
        public List<SavedWindow>? Windows { get; set; }
    }

    sealed class ModDto
    {
        public string Id { get; set; } = "";
        public string Version { get; set; } = "";
    }

    sealed class ControllerDto
    {
        public Owner Side { get; set; }
        /// <summary>1 human, 3 computer, 4 OpenSWR AI.</summary>
        public int Kind { get; set; }
        public uint NextSequence { get; set; }
        public List<CommandDto> Outbox { get; set; } = [];
        public int Issued { get; set; }
        public int DaysSinceReset { get; set; }
        /// <summary>The human controller's advisor.</summary>
        public Advisor.State? Advisor { get; set; }
        /// <summary>
        /// The computer controller's AI: think state, cursors, world model with every
        /// record, the planners, operations and tasks. Absent in older saves: the model and planners are rebuilt on the first think.
        /// </summary>
        public JsonObject? Computer { get; set; }
        /// <summary>The OpenSWR AI's state: decision day, staging / target system, refusal memory.</summary>
        public JsonObject? OpenSWR { get; set; }
    }

    sealed class SettingsDto
    {
        public Owner PlayerSide { get; set; }
        public int GalaxySize { get; set; }
        public int Difficulty { get; set; }
        public int VictoryFlag { get; set; }
        public int Seed { get; set; }
        public bool HeadToHead { get; set; }
        /// <summary>The computer opponent choice.</summary>
        public ComputerOpponent Opponent { get; set; }
    }

    sealed class SideDto
    {
        public Owner Side { get; set; }
        public int MaintenanceCapacity { get; set; }
        public int MaintenanceAllocated { get; set; }
        public int Demand { get; set; }
        public bool Exceeded { get; set; }
        public int Speed { get; set; }
        public int RawStock { get; set; }
        public int RefinedStock { get; set; }
        public List<long[]> RefineryWaitList { get; set; } = [];
        public List<long[]> ManufacturingWaitList { get; set; } = [];
        public int[] ResearchPoints { get; set; } = [];
        public int[] ResearchOrder { get; set; } = [];
        public bool[] ResearchExhausted { get; set; } = [];
        public int CostScale { get; set; }
        public bool RecruitmentDone { get; set; }
    }

    sealed class ViewDto
    {
        public uint Object { get; set; }
        public Owner Side { get; set; }
        public Owner Owner { get; set; }
        public uint? Parent { get; set; }
        public bool Destroyed { get; set; }
        public bool Enroute { get; set; }
        public bool Uprising { get; set; }
        public bool Populated { get; set; }
        public bool Explored { get; set; }
        public bool Captured { get; set; }
        public int Support { get; set; }
        public bool Completed { get; set; }
        /// <summary>View Created.</summary>
        public bool Created { get; set; }
        /// <summary>View Usable.</summary>
        public bool Usable { get; set; }
        public uint? BuilderKey { get; set; }
        public bool EnrouteActive { get; set; }
        public uint? RoleMission { get; set; }
        public bool Blockade { get; set; }
        public bool Battle { get; set; }
        public bool Damaged { get; set; }
        public int Speed { get; set; }
        /// <summary>System view control.</summary>
        public SystemControl Control { get; set; }
        /// <summary>Manager view queue count, mission view TaskStatus / CompletionStatus.</summary>
        public int QueueCount { get; set; }
        public MissionTaskStatus TaskStatus { get; set; }
        public MissionCompletion CompletionStatus { get; set; }
        /// <summary>openswr view update day.</summary>
        public int Updated { get; set; }
        /// <summary>System view NeverBeenControlled.</summary>
        public bool NeverBeenControlled { get; set; }
    }

    sealed class TimerDto
    {
        public TimerType Type { get; set; }
        public uint Target { get; set; }
        /// <summary>Subject: an object ref, or a side (<see cref="SubjectSide"/>), or neither.</summary>
        public uint? Subject { get; set; }
        public Owner SubjectSide { get; set; }
        public uint Serial { get; set; }
        public int Base { get; set; }
        public int Variance { get; set; }
        public bool Active { get; set; }
    }

    sealed class EventDto
    {
        public TimerType Type { get; set; }
        public uint Target { get; set; }
        public int FireDay { get; set; }
        public uint Serial { get; set; }
        public int Base { get; set; }
        public int Variance { get; set; }
    }

    sealed class CommandDto
    {
        public int Type { get; set; }
        public Owner Side { get; set; }
        public List<uint> Targets { get; set; } = [];
        public List<GroupMode> Modes { get; set; } = [];
        public uint? ArgRef { get; set; }
        public uint ArgInt { get; set; }
        public uint[] Fields { get; set; } = [];
        public List<uint> Decoys { get; set; } = [];
        public uint Serial { get; set; }
        public uint Sequence { get; set; }
        public string? Text { get; set; }
    }

    sealed class ObjectDto
    {
        /// <summary>Class: object, fleet, agent, character, facility, manager, system, mission, fixed (staging / mission manager).</summary>
        public string Kind { get; set; } = "";
        public uint Type { get; set; }
        public uint Id { get; set; }
        public Owner Owner { get; set; }
        public bool Usable { get; set; }
        public bool Created { get; set; }
        public bool Completed { get; set; }
        public bool Destroyed { get; set; }
        public bool Enroute { get; set; }
        public bool EnrouteActive { get; set; }
        public bool Existing { get; set; }
        public bool Damaged { get; set; }
        public bool HyperdriveActive { get; set; }
        public bool Autorouting { get; set; }
        public bool Locked { get; set; }
        public bool ReadyForDelete { get; set; }
        public bool Constructed { get; set; }
        public bool AutoscrapRequested { get; set; }
        public DestroyedReason DestroyedReason { get; set; }
        public SideSet Observed { get; set; }
        /// <summary>Live sides; absent = not yet computed.</summary>
        public SideSet? LiveSides { get; set; }
        public int LockCount { get; set; }
        /// <summary>Acknowledged instances: the real object and the side views.</summary>
        public bool AckedReal { get; set; }
        public SideSet AckedViews { get; set; }
        public uint? BuilderKey { get; set; }
        public uint? Destination { get; set; }
        public int Eta { get; set; }
        public bool DestroyedRunningBlockade { get; set; }
        public int WithdrawPercent { get; set; }
        public bool DetectorIsDecoyed { get; set; }
        public int HullDamage { get; set; }
        public int SquadSizeDamage { get; set; }
        public CriticalHitCounts CriticalHits { get; set; }
        public bool RepairActive { get; set; }
        public bool FastRepair { get; set; }
        public int IonAccumulator { get; set; }
        public int LaserAccumulator { get; set; }
        public int ShieldsAbsorbed { get; set; }
        public List<uint> Contents { get; set; } = [];
        public FleetDto? Fleet { get; set; }
        public AgentDto? Agent { get; set; }
        public CharacterDto? Character { get; set; }
        public FacilityDto? Facility { get; set; }
        public ManagerDto? Manager { get; set; }
        public SystemDto? System { get; set; }
        public MissionDto? Mission { get; set; }
    }

    sealed class FleetDto
    {
        public bool Real { get; set; }
        public bool Reserve { get; set; }
        public bool NewFleet { get; set; }
        public bool Battle { get; set; }
        public bool Blockade { get; set; }
        public bool Bombard { get; set; }
        public bool Assault { get; set; }
    }

    sealed class AgentDto
    {
        public int[] Skills { get; set; } = [];
        public uint? Mission { get; set; }
        public bool OnMission { get; set; }
        public bool ResignRequest { get; set; }
        public bool ResignOnInjury { get; set; }
        public bool RemoveRequest { get; set; }
        public bool Adrift { get; set; }
        public bool IsDecoying { get; set; }
        public uint? ParentAtMission { get; set; }
        public uint? LocationAtMission { get; set; }
    }

    sealed class CharacterDto
    {
        public int ForceBonus { get; set; }
        public PrisonerState State { get; set; }
        public bool Captured { get; set; }
        public bool CanEscape { get; set; }
        public bool EscapeRequest { get; set; }
        public bool CanHeal { get; set; }
        public bool Healing { get; set; }
        public bool FastHeal { get; set; }
        public uint? Captor { get; set; }
        public int Injury { get; set; }
        public bool KnownJedi { get; set; }
        public bool JediPotential { get; set; }
        public int ForceRank { get; set; }
        public bool KnowsHeritage { get; set; }
        /// <summary>Luke's FinalBattleReady, Han's CapturedByBountyHunters.</summary>
        public bool FinalBattleReady { get; set; }
        public bool DagobahCompleted { get; set; }
        public uint? EncounterKey { get; set; }
        public uint EncounterSerial { get; set; }
        /// <summary>Pre-round-a123 saves only: ForceUserDiscoveredKey stored on the discovered character (its discoverer); read, never written.</summary>
        public uint? DiscoveredBy { get; set; }
        /// <summary>ForceUserDiscoveredKey on the discoverer.</summary>
        public uint? DiscoveredForceUser { get; set; }
        public bool ForceActive { get; set; }
        public int? MissionHyperdriveModifier { get; set; }
        public int CommandKind { get; set; }
    }

    sealed class FacilityDto
    {
        public int ProcessingRate { get; set; }
        public int Capacity { get; set; }
        public int Allocated { get; set; }
        public int Modifier { get; set; }
        public int CycleLength { get; set; }
        public FacilityState State { get; set; }
        public bool Suspended { get; set; }
        public bool PointPresent { get; set; }
        public bool PointProcessed { get; set; }
        public bool Processing { get; set; }
        public bool OnStartupCycle { get; set; }
        public int CompletionDay { get; set; }
        public int WaitCounter { get; set; }
    }

    sealed class ManagerDto
    {
        public uint System { get; set; }
        public ManufacturingKind Kind { get; set; }
        public List<uint> Orders { get; set; } = [];
        public int CompletedPoints { get; set; }
        public int Overflow { get; set; }
        public uint TargetKey { get; set; }
        /// <summary>Frozen side views' TargetKey by side.</summary>
        public uint?[]? ViewTargetKey { get; set; }
        /// <summary>Reserved.</summary>
        public bool? Reserved { get; set; }
    }

    sealed class SystemDto
    {
        public int InitIndex { get; set; }
        public bool Disloyal { get; set; }
        public bool Populated { get; set; }
        public int Support { get; set; }
        public int Energy { get; set; }
        public int RawMaterials { get; set; }
        public int UsedEnergy { get; set; }
        public int UsedRaw { get; set; }
        public int ProductionModifier { get; set; }
        public int SmugglingPercent { get; set; }
        public bool Garrisoned { get; set; }
        public SystemControl Control { get; set; }
        public bool NeverBeenControlled { get; set; }
        public bool Uprising { get; set; }
        public bool Suppressing { get; set; }
        public bool DeathStarNearby { get; set; }
        public bool CombatUnitFastRepair { get; set; }
        public int InciteTotal { get; set; }
        public int SubdueTotal { get; set; }
        public int IncidentSupportDelta { get; set; }
        public int GarrisonRequirement { get; set; }
        public int GarrisonSurplus { get; set; }
        public bool BattlePending { get; set; }
        public bool BattleToHost { get; set; }
        public bool Blockade { get; set; }
        public bool Battle { get; set; }
        public bool Explored { get; set; }
        public Owner BattleReady { get; set; }
        public Owner BattleWon { get; set; }
        public Owner ControlBattleWon { get; set; }
        public Owner BattleWithdrew { get; set; }
        public Owner Blockader { get; set; }
        public int[] BattleLoss { get; set; } = [];
        public int[] AssaultLoss { get; set; } = [];
        public int[] BombardLoss { get; set; } = [];
        public List<uint> Managers { get; set; } = [];
    }

    sealed class MissionDto
    {
        public uint UserId { get; set; }
        public MissionTaskStatus TaskStatus { get; set; }
        public MissionCompletion CompletionStatus { get; set; }
        public int Phase { get; set; }
        public int LastPhase { get; set; }
        public bool Ready { get; set; }
        public bool ImpliedTeam { get; set; }
        public bool Mandatory { get; set; }
        public uint? Origin { get; set; }
        public uint? Objective { get; set; }
        public uint? TargetKey { get; set; }
        public uint? TargetLocation { get; set; }
        public uint? Leader { get; set; }
        public List<uint> Team { get; set; } = [];
        public List<uint> Decoys { get; set; } = [];
        public List<uint> Captured { get; set; } = [];
        public uint? Recruited { get; set; }
        public uint? Teacher { get; set; }
        public int StartDay { get; set; }
        public int Accumulator { get; set; }
        public int LevelsGained { get; set; }
        public EncounterCodes Encounters { get; set; }
    }

    // ---- save ----

    /// <summary>
    /// Writes the game to <paramref name="stream"/>. Throws if notifications are queued (not a tick boundary). <paramref name="compact"/>:
    /// leave out the staged objects nothing can reach again (<see cref="Compactable"/>; false for A/B checks).
    /// </summary>
    public static void Save(GameState game, Stream stream, bool compact = true)
    {
        var omit = compact ? Compactable(game) : [];
        while (true)
        {
            var (d, table) = ToDto(game, omit);
            // a candidate some saved field still names (a mission list, a command, a view, another object...) stays in the save
            var named = table.Where(omit.Contains).ToList();
            if (named.Count == 0) { JsonSerializer.Serialize(stream, d, Options); return; }
            omit.ExceptWith(named);
        }
    }

    /// <summary>
    /// OpenSWR deviation, Inferred: the exact deletion rule keeps forever the staged objects
    /// the other side never saw (fresh view) or whose frozen view stayed outside staging; the original keeps them too. The save
    /// leaves out a staged object that is Destroyed, deletable, empty (real and views), not Locked, without a queued timer event
    /// naming it (its slots, even Active ones, are then dead: a Destroyed target's event acts never again; they are left out with
    /// it), whose every view is fresh (not Created) or Destroyed and Acknowledged and has no view parent
    /// outside staging, and whose key no message of the log names; a candidate any written field still references stays (checked
    /// on the written table). The running game keeps them, so a save changes nothing in it; a reload sees a game without them
    /// (no code path re-parents a staged destroyed object, Inferred).
    /// </summary>
    static HashSet<GameObject> Compactable(GameState game)
    {
        var timed = game.Clock.TimedEvents.OfType<TimerEvent>().Select(e => e.Slot.Target).ToHashSet();
        var logged = game.Log.SelectMany(m => new[] { m.Subject, m.Mission, m.Encyclopedia }).ToHashSet();
        bool Seen(GameObject x, Owner side)
        {
            var parent = Knowledge.ViewParent(game, x, side);
            if (parent is not null && parent != game.Staging || Knowledge.HasContentViews(game, x, side)) return false;
            return !Knowledge.ViewCreated(game, x, side) || Knowledge.ViewDestroyed(game, x, side) == true && x.AckedViews[side];
        }
        return [.. game.Staging.Contents.Where(x => x.Destroyed && Objects.Deletable(x) && x.Contents.Count == 0 && !x.Locked
            && !x.Deleted && !timed.Contains(x.Ref) && !logged.Contains(x.Ref) && Seen(x, Owner.Alliance) && Seen(x, Owner.Empire))];
    }

    static (SaveDto, List<GameObject>) ToDto(GameState game, HashSet<GameObject> omit)
    {
        if (game.HostedBattle is not null)
            throw new InvalidOperationException("A battle request is open: answer it first.");
        if (game.Clock.HasQueuedNotifications)
            throw new InvalidOperationException("Notifications are queued: save between host-loop ticks.");
        var table = new List<GameObject>();
        var index = new Dictionary<uint, GameObject>();
        uint R(GameObject o)
        {
            if (!index.TryGetValue(o.Ref, out var known)) { index[o.Ref] = o; table.Add(o); }
            else if (known != o) throw new InvalidOperationException($"Two objects share the reference {Family.Of(o.Ref)}:{SdRef.IndexOf(o.Ref)}.");
            return o.Ref;
        }
        uint? N(GameObject? o) => o is null ? null : R(o);
        List<uint> L(IEnumerable<GameObject> list) => [.. list.Select(R)];

        var c = game.Clock;
        var d = new SaveDto
        {
            Format = Format, Version = Version,
            Settings = new()
            {
                PlayerSide = game.Settings.PlayerSide, GalaxySize = game.Settings.GalaxySize, Difficulty = game.Settings.Difficulty,
                VictoryFlag = game.Settings.VictoryFlag, Seed = game.Settings.Seed, HeadToHead = game.Settings.HeadToHead,
                Opponent = game.Settings.Opponent,
            },
            RngState = game.Random.State, RngDraws = game.Random.Draws,
            Tick = c.Tick, Day = c.Day, TicksPerDay = c.TicksPerDay, SubTick = c.SubTick, DayDue = c.DayDue, Serial = c.Serial,
            NextId = game.IdCounter, Ready = game.Ready, BattleRequired = game.BattleRequired, VictoryState = game.VictoryState,
            StoryFlags = game.StoryFlags, WaveTick = game.WaveTick,
            WaveCounts = [.. game.WaveCounts.Select(e => new[] { (int)e.Key, e.Value })],
            Sectors = [.. game.Sectors.Select(s => s.Definition.Common.Index)],
        };
        R(game.Staging);
        R(game.MissionManager);
        d.Headquarters = N(game.HeadquartersSystem);
        d.Systems = L(game.Systems);
        d.Characters = L(game.Characters);
        IEnumerable<GameObject> Kept(IEnumerable<GameObject> list) => list.Where(o => !omit.Contains(o));
        d.Created = L(Kept(game.Created));
        d.Fleets = L(Kept(game.Fleets));
        d.Facilities = L(Kept(game.Facilities));
        d.Missions = L(Kept(game.Missions));
        d.Abodes = L(game.Abodes.Values);
        foreach (var (owner, s) in game.Sides)
            d.Sides.Add(new()
            {
                Side = owner, MaintenanceCapacity = s.MaintenanceCapacity, MaintenanceAllocated = s.MaintenanceAllocated, Demand = s.Demand,
                Exceeded = s.Exceeded, Speed = s.Speed, RawStock = s.RawStock, RefinedStock = s.RefinedStock,
                RefineryWaitList = [.. s.RefineryWaitList.Select(w => new long[] { R(w.Facility), w.Counter })],
                ManufacturingWaitList = [.. s.ManufacturingWaitList.Select(w => new long[] { R(w.Facility), w.Counter })],
                ResearchPoints = [.. s.ResearchPoints], ResearchOrder = [.. s.ResearchOrder], ResearchExhausted = [.. s.ResearchExhausted],
                CostScale = s.CostScale, RecruitmentDone = s.RecruitmentDone,
            });
        foreach (var ((o, side), v) in game.Views.Where(e => !omit.Contains(e.Key.Item1)))
            d.Views.Add(new()
            {
                Object = R(o), Side = side, Owner = v.Owner, Parent = N(v.Parent), Destroyed = v.Destroyed, Enroute = v.Enroute,
                Uprising = v.Uprising, Populated = v.Populated, Explored = v.Explored, Captured = v.Captured, Support = v.Support,
                Completed = v.Completed, Created = v.Created, Usable = v.Usable, BuilderKey = N(v.BuilderKey), EnrouteActive = v.EnrouteActive, RoleMission = N(v.RoleMission),
                Blockade = v.Blockade, Battle = v.Battle, Damaged = v.Damaged, Speed = v.Speed, Control = v.Control,
                QueueCount = v.QueueCount, TaskStatus = v.TaskStatus, CompletionStatus = v.CompletionStatus, NeverBeenControlled = v.NeverBeenControlled, Updated = v.Updated,
            });
        // dead slots are not written (audit 2026-10-26 #16: saves grew ~1 MB per 300 days): an inactive slot of an object in staging
        // (destroyed, final move made) that no queued event names never acts again; a later start on it makes a fresh slot (same
        // draws, only its serial restarts). The game itself keeps them, so a save changes nothing in the running game.
        var named = c.TimedEvents.OfType<TimerEvent>().Select(e => e.Slot).ToHashSet();
        var staged = game.Staging.Contents.Select(o => o.Ref).ToHashSet();
        // a deleted object's slot left in the game is dead the same way
        var omitted = omit.Select(o => o.Ref).ToHashSet(); // their slots go with them (Compactable)
        foreach (var slot in game.Timers.Values.Where(t => (t.Active || named.Contains(t) || !staged.Contains(t.Target) && t.Subject is not GameObject { Deleted: true })
            && !omitted.Contains(t.Target)))
            d.Timers.Add(new()
            {
                Type = slot.Type, Target = slot.Target, Serial = slot.Serial, Base = slot.Base, Variance = slot.Variance, Active = slot.Active,
                Subject = slot.Subject switch
                {
                    null or SideState => null,
                    GameObject o => R(o),
                    var x => throw new InvalidOperationException($"Timer {slot.Type} subject {x} cannot be saved."),
                },
                SubjectSide = slot.Subject is SideState side ? side.Owner : Owner.None,
            });
        foreach (var e in c.TimedEvents)
        {
            if (e is not TimerEvent t) throw new InvalidOperationException($"Timed event {e} cannot be saved.");
            var (serial, @base, variance) = t.Copy;
            d.Events.Add(new() { Type = t.Slot.Type, Target = t.Slot.Target, FireDay = t.FireDay, Serial = serial, Base = @base, Variance = variance });
        }
        CommandDto C(Command x) => new()
        {
            Type = (int)x.Type, Side = x.Side, Targets = [.. x.Targets.Select(t => R(t.Target))], Modes = [.. x.Targets.Select(t => t.Mode)],
            ArgRef = N(x.ArgRef), ArgInt = x.ArgInt, Fields = [.. x.Fields], Decoys = L(x.Decoys), Serial = x.Serial, Sequence = x.Sequence, Text = x.Text,
        };
        foreach (var x in c.PendingCommands) d.Commands.Add(C(x));
        // controllers: counters and outbox; the computer's whole AI
        if (game.Controllers.Count > 0)
        {
            d.Controllers = [.. game.Controllers.Values.Select(k => new ControllerDto
            {
                Side = k.Side, Kind = k.Kind, NextSequence = k.NextSequence, Outbox = [.. k.Outbox.Select(C)],
                Issued = k switch { Ai.ComputerController x => x.Issued, Ai.OpenSWRController x => x.Issued, _ => 0 }, DaysSinceReset = (k as Ai.ComputerController)?.DaysSinceReset ?? 0,
                Advisor = (k as Ai.HumanController)?.Advisor?.Save(),
                Computer = k is Ai.ComputerController { Started: true } computer ? computer.Save(R) : null, // before game start: nothing to keep
                OpenSWR = (k as Ai.OpenSWRController)?.Save(),
            })];
            d.UiRngState = UiRandom.Shared.State;
        }
        for (int i = 0; i < table.Count; i++) d.Objects.Add(ToDto(game, table[i], R, N, L, omit)); // the table grows with the references met
        d.Log = game.Log.Count == 0 ? null : [.. game.Log];
        d.Mods = game.Definitions.Mods.Count == 0 ? null : [.. game.Definitions.Mods.Select(m => new ModDto { Id = m.Id, Version = m.Version })];
        d.FilterMode = game.FilterMode == MapFilter.Off ? null : game.FilterMode;
        d.Windows = game.Windows.Count == 0 ? null : [.. game.Windows];
        return (d, table);
    }

    static ObjectDto ToDto(GameState game, GameObject o, Func<GameObject, uint> R, Func<GameObject?, uint?> N, Func<IEnumerable<GameObject>, List<uint>> L,
        HashSet<GameObject> omit)
    {
        var d = new ObjectDto
        {
            Kind = o switch
            {
                _ when o == game.Staging || o == game.MissionManager => "fixed",
                Mission => "mission", StarSystem => "system", ManufacturingManager => "manager", Facility => "facility",
                Character => "character", Agent => "agent", Fleet => "fleet", _ => "object",
            },
            Type = o.Type, Id = o.Id, Owner = o.Owner,
            Usable = o.Usable, Created = o.Created, Completed = o.Completed, Destroyed = o.Destroyed, Enroute = o.Enroute, EnrouteActive = o.EnrouteActive, Existing = o.Existing, Damaged = o.Damaged, HyperdriveActive = o.HyperdriveActive, Autorouting = o.Autorouting, Locked = o.Locked, ReadyForDelete = o.ReadyForDelete, Constructed = o.Constructed,
            AutoscrapRequested = o.AutoscrapRequested,
            DestroyedReason = o.DestroyedReason, Observed = o.Observed, LiveSides = o.LiveSides, LockCount = o.LockCount,
            AckedReal = o.AckedReal, AckedViews = o.AckedViews, BuilderKey = N(o.BuilderKey), Destination = N(o.Destination), Eta = o.Eta, DestroyedRunningBlockade = o.DestroyedRunningBlockade,
            WithdrawPercent = o.WithdrawPercent, DetectorIsDecoyed = o.DetectorIsDecoyed, HullDamage = o.HullDamage,
            SquadSizeDamage = o.SquadSizeDamage, CriticalHits = o.CriticalHits, RepairActive = o.RepairActive, FastRepair = o.FastRepair, IonAccumulator = o.IonAccumulator,
            LaserAccumulator = o.LaserAccumulator, ShieldsAbsorbed = o.ShieldsAbsorbed, Contents = L(o.Contents.Where(x => !omit.Contains(x))),
        };
        if (o is Fleet f)
            d.Fleet = new FleetDto { Real = f.Real, Reserve = f.Reserve, NewFleet = f.NewFleet, Battle = f.Battle, Blockade = f.Blockade, Bombard = f.Bombard, Assault = f.Assault };
        if (o is Agent a)
            d.Agent = new()
            {
                Skills = [.. a.Skills], Mission = N(a.Mission), OnMission = a.OnMission, ResignRequest = a.ResignRequest,
                ResignOnInjury = a.ResignOnInjury, RemoveRequest = a.RemoveRequest, Adrift = a.Adrift, IsDecoying = a.IsDecoying,
                ParentAtMission = N(a.ParentAtMission), LocationAtMission = N(a.LocationAtMission),
            };
        if (o is Character c)
            d.Character = new()
            {
                ForceBonus = c.ForceBonus, State = c.State, Captured = c.Captured, CanEscape = c.CanEscape, EscapeRequest = c.EscapeRequest,
                CanHeal = c.CanHeal, Healing = c.Healing, FastHeal = c.FastHeal, Captor = N(c.Captor), Injury = c.Injury,
                KnownJedi = c.KnownJedi, JediPotential = c.JediPotential, ForceRank = c.ForceRank,
                KnowsHeritage = c.KnowsHeritage, FinalBattleReady = c.FinalBattleReady, DagobahCompleted = c.DagobahCompleted,
                EncounterKey = N(c.EncounterKey), EncounterSerial = c.EncounterSerial, DiscoveredForceUser = N(c.DiscoveredForceUser),
                ForceActive = c.ForceActive, MissionHyperdriveModifier = c.MissionHyperdriveModifier, CommandKind = c.CommandKind,
            };
        if (o is Facility x)
            d.Facility = new()
            {
                ProcessingRate = x.ProcessingRate, Capacity = x.Capacity, Allocated = x.Allocated, Modifier = x.Modifier,
                CycleLength = x.CycleLength, State = x.State, Suspended = x.Suspended, PointPresent = x.PointPresent,
                PointProcessed = x.PointProcessed, Processing = x.Processing, OnStartupCycle = x.OnStartupCycle, CompletionDay = x.CompletionDay, WaitCounter = x.WaitCounter,
            };
        if (o is ManufacturingManager m)
            d.Manager = new()
            {
                System = R(m.System), Kind = m.Kind, Orders = L(m.Orders), CompletedPoints = m.CompletedPoints, Overflow = m.Overflow, TargetKey = R(m.TargetKey),
                ViewTargetKey = m.ViewTargetKey.Any(x => x is not null) ? [.. m.ViewTargetKey.Select(N)] : null,
                Reserved = m.Reserved ? true : null,
            };
        if (o is StarSystem s)
            d.System = new()
            {
                InitIndex = s.InitIndex, Disloyal = s.Disloyal, Populated = s.Populated, Support = s.Support, Energy = s.Energy,
                RawMaterials = s.RawMaterials, UsedEnergy = s.UsedEnergy, UsedRaw = s.UsedRaw, ProductionModifier = s.ProductionModifier, SmugglingPercent = s.SmugglingPercent,
                Garrisoned = s.Garrisoned, Control = s.Control, NeverBeenControlled = s.NeverBeenControlled, Uprising = s.Uprising,
                Suppressing = s.Suppressing, DeathStarNearby = s.DeathStarNearby, CombatUnitFastRepair = s.CombatUnitFastRepair, InciteTotal = s.InciteTotal, SubdueTotal = s.SubdueTotal,
                IncidentSupportDelta = s.IncidentSupportDelta, GarrisonRequirement = s.GarrisonRequirement, GarrisonSurplus = s.GarrisonSurplus,
                BattlePending = s.BattlePending, BattleToHost = s.BattleToHost, Blockade = s.Blockade, Battle = s.Battle, Explored = s.Explored, BattleReady = s.BattleReady,
                BattleWon = s.BattleWon, ControlBattleWon = s.ControlBattleWon, BattleWithdrew = s.BattleWithdrew, Blockader = s.Blockader, BattleLoss = [.. s.BattleLoss], AssaultLoss = [.. s.AssaultLoss],
                BombardLoss = [.. s.BombardLoss], Managers = L(s.Managers),
            };
        if (o is Mission n)
            d.Mission = new()
            {
                UserId = n.UserId, TaskStatus = n.TaskStatus, CompletionStatus = n.CompletionStatus, Phase = n.Phase, LastPhase = n.LastPhase,
                Ready = n.Ready, ImpliedTeam = n.ImpliedTeam, Mandatory = n.Mandatory, Origin = N(n.Origin), Objective = N(n.Objective),
                TargetKey = N(n.TargetKey), TargetLocation = N(n.TargetLocation), Leader = N(n.Leader), Team = L(n.Team), Decoys = L(n.Decoys),
                Captured = L(n.Captured), Recruited = N(n.Recruited), Teacher = N(n.Teacher), StartDay = n.StartDay,
                Accumulator = n.Accumulator, LevelsGained = n.LevelsGained, Encounters = n.Encounters,
            };
        return d;
    }

    // ---- load ----

    /// <summary>
    /// Reads a save written by <see cref="Save"/> with the install's definitions. Throws <see cref="InvalidDataException"/> for
    /// another format or version, or a save that does not fit the definitions. Plain or gzip-compressed JSON.
    /// </summary>
    public static GameState Load(Stream stream, GameDefinitions definitions) => Load(stream, definitions, null);

    /// <summary><see cref="Load(Stream, GameDefinitions)"/> with the controllers' UI-generator source per side (tests; default the process-wide generator).</summary>
    public static GameState Load(Stream stream, GameDefinitions definitions, Func<Owner, Func<int, int>>? rand)
    {
        if (!stream.CanSeek) { var copy = new MemoryStream(); stream.CopyTo(copy); copy.Position = 0; stream = copy; }
        long start = stream.Position;
        bool gzip = stream.ReadByte() == 31 && stream.ReadByte() == 139;
        stream.Position = start;
        using var unzipped = gzip ? new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress, true) : null;
        JsonNode? root;
        try { root = JsonNode.Parse(unzipped ?? stream); }
        catch (JsonException e) { throw new InvalidDataException("Not a save game: " + e.Message, e); }
        // a hand-edited or damaged save: every failure is a clean InvalidDataException, never a crash of the load path
        try
        {
            string? format = root?["format"]?.GetValueKind() == JsonValueKind.String ? (string?)root["format"] : null;
            if (format != Format) throw new InvalidDataException("Not an Open-SWR save game.");
            int version = root!["version"]?.GetValueKind() == JsonValueKind.Number ? (int)root["version"]! : 0;
            if (version is 1 or 2 or 3) throw new InvalidDataException("This save was made by an older version and can't be loaded.");
            if (version != Version) throw new InvalidDataException($"Save game version {version} is not supported (expected {Version}).");
            var d = root.Deserialize<SaveDto>(Options)!;
            // the save names the mods it was played with; the definitions must come from the same mods, in order
            static string List(IEnumerable<(string Id, string Version)> mods) => mods.Any() ? string.Join(", ", mods.Select(m => $"{m.Id} {m.Version}")) : "none";
            var saved = (d.Mods ?? []).Select(m => (m.Id, m.Version)).ToList();
            if (!saved.SequenceEqual(definitions.Mods))
                throw new InvalidDataException($"Save game was played with mods: {List(saved)}; active mods: {List(definitions.Mods)}. Enable the same mods in the same order.");
            return FromDto(d, definitions, rand);
        }
        catch (Exception e) when (e is not InvalidDataException)
        {
            throw new InvalidDataException("Save game does not fit the game data: " + e.Message, e);
        }
    }

    static GameState FromDto(SaveDto d, GameDefinitions defs, Func<Owner, Func<int, int>>? rand)
    {
        var st = d.Settings;
        var game = new GameState(new GameSettings(st.PlayerSide, st.GalaxySize, st.Difficulty, st.VictoryFlag, st.Seed, st.HeadToHead, st.Opponent), defs);
        foreach (uint i in d.Sectors) game.Sectors.Add(new Sector(defs.Sectors.First(r => r.Common.Index == i)));
        var sectors = game.Sectors.ToDictionary(s => s.Definition.Common.Index);

        // pass 1: instances (managers after their systems)
        var map = new Dictionary<uint, GameObject>();
        GameObject O(uint r) => map[r];
        T? N<T>(uint? r) where T : GameObject => r is { } x ? (T)map[x] : null;
        foreach (var o in d.Objects.Where(o => o.Kind != "manager"))
            map[SdRef.Make(Family.Of(o.Type), o.Id)] = o.Kind switch
            {
                "fixed" => new[] { game.Staging, game.MissionManager }.Single(x => x.Type == o.Type && x.Id == o.Id),
                "mission" => new Mission(defs.Missions.First(r => r.Common.Ref == o.Type), o.Id)
                {
                    FactSink = game, UserId = o.Mission!.UserId, ImpliedTeam = o.Mission.ImpliedTeam, Mandatory = o.Mission.Mandatory,
                },
                "system" => Check(new StarSystem(defs.Systems.First(r => r.Common.Ref == o.Type), sectors[defs.Systems.First(r => r.Common.Ref == o.Type).SectorIndex])
                    { InitIndex = o.System!.InitIndex }, o),
                "facility" => new Facility(Typed(o, defs), o.Owner, o.Id, o.Facility!.ProcessingRate),
                "character" => Check(new Character(defs.MajorCharacters.Concat(defs.MinorCharacters).First(r => r.Common.Ref == o.Type), o.Owner), o),
                "agent" => new Agent(Typed(o, defs), o.Owner, o.Id),
                "fleet" => new Fleet(Typed(o, defs), o.Owner, o.Id),
                "object" => new GameObject(Typed(o, defs), o.Owner, o.Id),
                var k => throw new InvalidDataException($"Unknown object kind {k}."),
            };
        foreach (var o in d.Objects.Where(o => o.Kind == "manager"))
            map[SdRef.Make(Family.Of(o.Type), o.Id)] = Check(new ManufacturingManager((StarSystem)O(o.Manager!.System), o.Manager.Kind, o.Id), o);

        // pass 2: fields and references
        foreach (var o in d.Objects)
        {
            var x = O(SdRef.Make(Family.Of(o.Type), o.Id));
            x.Owner = o.Owner;
            (x.Usable, x.Created, x.Completed, x.Destroyed, x.Enroute, x.EnrouteActive, x.Existing, x.Damaged, x.HyperdriveActive, x.Autorouting, x.Locked, x.ReadyForDelete, x.Constructed) =
                (o.Usable, o.Created, o.Completed, o.Destroyed, o.Enroute, o.EnrouteActive, o.Existing, o.Damaged, o.HyperdriveActive, o.Autorouting, o.Locked, o.ReadyForDelete, o.Constructed);
            x.AutoscrapRequested = o.AutoscrapRequested;
            x.DestroyedReason = o.DestroyedReason;
            x.Observed = o.Observed;
            x.LiveSides = o.LiveSides;
            x.LockCount = o.LockCount;
            x.AckedReal = o.AckedReal;
            x.AckedViews = o.AckedViews;
            x.BuilderKey = N<ManufacturingManager>(o.BuilderKey);
            x.Destination = N<GameObject>(o.Destination);
            x.Eta = o.Eta;
            x.DestroyedRunningBlockade = o.DestroyedRunningBlockade;
            x.WithdrawPercent = o.WithdrawPercent;
            x.DetectorIsDecoyed = o.DetectorIsDecoyed;
            x.HullDamage = o.HullDamage;
            x.SquadSizeDamage = o.SquadSizeDamage;
            x.CriticalHits = o.CriticalHits;
            (x.RepairActive, x.FastRepair) = (o.RepairActive, o.FastRepair);
            x.IonAccumulator = o.IonAccumulator;
            x.LaserAccumulator = o.LaserAccumulator;
            x.ShieldsAbsorbed = o.ShieldsAbsorbed;
            foreach (uint r in o.Contents) x.Add(O(r));
            if (x is Fleet f && o.Fleet is { } fl)
                (f.Real, f.Reserve, f.NewFleet, f.Battle, f.Blockade, f.Bombard, f.Assault) = (fl.Real, fl.Reserve, fl.NewFleet, fl.Battle, fl.Blockade, fl.Bombard, fl.Assault);
            if (x is Agent a && o.Agent is { } ad)
            {
                ad.Skills.CopyTo(a.Skills, 0);
                a.Mission = N<Mission>(ad.Mission);
                a.OnMission = ad.OnMission;
                a.ResignRequest = ad.ResignRequest;
                a.ResignOnInjury = ad.ResignOnInjury;
                a.RemoveRequest = ad.RemoveRequest;
                a.Adrift = ad.Adrift;
                a.IsDecoying = ad.IsDecoying;
                a.ParentAtMission = N<GameObject>(ad.ParentAtMission);
                a.LocationAtMission = N<StarSystem>(ad.LocationAtMission);
            }
            if (x is Character c && o.Character is { } cd)
            {
                c.ForceBonus = cd.ForceBonus;
                c.State = cd.State;
                c.Captured = cd.Captured;
                c.CanEscape = cd.CanEscape;
                c.EscapeRequest = cd.EscapeRequest;
                c.CanHeal = cd.CanHeal;
                c.Healing = cd.Healing;
                c.FastHeal = cd.FastHeal;
                c.Captor = N<GameObject>(cd.Captor);
                c.Injury = cd.Injury;
                c.KnownJedi = cd.KnownJedi;
                c.JediPotential = cd.JediPotential;
                c.ForceRank = cd.ForceRank;
                (c.KnowsHeritage, c.FinalBattleReady, c.DagobahCompleted) = (cd.KnowsHeritage, cd.FinalBattleReady, cd.DagobahCompleted);
                c.EncounterKey = N<Character>(cd.EncounterKey);
                c.EncounterSerial = cd.EncounterSerial;
                if (cd.DiscoveredForceUser is { } dk) c.DiscoveredForceUser = N<Character>(dk);
                // an old save kept the key on the discovered character: reverse it (one discoverer with several discovered characters
                // keeps the one loaded last; no sim reader.
                if (N<Character>(cd.DiscoveredBy) is { } by) by.DiscoveredForceUser = c;
                c.ForceActive = cd.ForceActive;
                c.MissionHyperdriveModifier = cd.MissionHyperdriveModifier;
                c.CommandKind = cd.CommandKind;
            }
            if (x is Facility fa && o.Facility is { } fd)
            {
                fa.Capacity = fd.Capacity;
                fa.Allocated = fd.Allocated;
                fa.Modifier = fd.Modifier;
                fa.CycleLength = fd.CycleLength;
                fa.State = fd.State;
                (fa.Suspended, fa.PointPresent, fa.PointProcessed, fa.Processing, fa.OnStartupCycle) = (fd.Suspended, fd.PointPresent, fd.PointProcessed, fd.Processing, fd.OnStartupCycle);
                fa.CompletionDay = fd.CompletionDay;
                fa.WaitCounter = fd.WaitCounter;
            }
            if (x is ManufacturingManager m && o.Manager is { } md)
            {
                m.Orders.AddRange(md.Orders.Select(O));
                m.CompletedPoints = md.CompletedPoints;
                m.Overflow = md.Overflow;
                m.TargetKey = O(md.TargetKey);
                for (int i = 0; i < (md.ViewTargetKey?.Length ?? 0); i++) m.ViewTargetKey[i] = md.ViewTargetKey![i] is { } k ? O(k) : null;
                m.Reserved = md.Reserved == true;
            }
            if (x is StarSystem s && o.System is { } sd)
            {
                s.Disloyal = sd.Disloyal;
                s.Populated = sd.Populated;
                s.Support = sd.Support;
                s.Energy = sd.Energy;
                s.RawMaterials = sd.RawMaterials;
                (s.UsedEnergy, s.UsedRaw) = (sd.UsedEnergy, sd.UsedRaw);
                s.ProductionModifier = sd.ProductionModifier;
                s.SmugglingPercent = sd.SmugglingPercent;
                s.Garrisoned = sd.Garrisoned;
                s.Control = sd.Control;
                s.NeverBeenControlled = sd.NeverBeenControlled;
                s.Uprising = sd.Uprising;
                s.Suppressing = sd.Suppressing;
                s.DeathStarNearby = sd.DeathStarNearby;
                s.CombatUnitFastRepair = sd.CombatUnitFastRepair;
                s.InciteTotal = sd.InciteTotal;
                s.SubdueTotal = sd.SubdueTotal;
                s.IncidentSupportDelta = sd.IncidentSupportDelta;
                s.GarrisonRequirement = sd.GarrisonRequirement;
                s.GarrisonSurplus = sd.GarrisonSurplus;
                s.BattlePending = sd.BattlePending;
                s.BattleToHost = sd.BattleToHost;
                s.Blockade = sd.Blockade;
                s.Battle = sd.Battle;
                s.Explored = sd.Explored;
                s.BattleReady = sd.BattleReady;
                s.BattleWon = sd.BattleWon;
                s.BattleWithdrew = sd.BattleWithdrew;
                s.Blockader = sd.Blockader;
                s.ControlBattleWon = sd.ControlBattleWon;
                sd.BattleLoss.CopyTo(s.BattleLoss, 0);
                sd.AssaultLoss.CopyTo(s.AssaultLoss, 0);
                sd.BombardLoss.CopyTo(s.BombardLoss, 0);
                for (int i = 0; i < sd.Managers.Count; i++) s.Managers[i] = (ManufacturingManager)O(sd.Managers[i]);
            }
            if (x is Mission n && o.Mission is { } nd)
            {
                n.TaskStatus = nd.TaskStatus;
                n.CompletionStatus = nd.CompletionStatus;
                n.Phase = nd.Phase;
                n.LastPhase = nd.LastPhase;
                n.Ready = nd.Ready;
                n.Origin = N<GameObject>(nd.Origin);
                n.Objective = N<GameObject>(nd.Objective);
                n.TargetKey = N<GameObject>(nd.TargetKey);
                n.TargetLocation = N<GameObject>(nd.TargetLocation);
                n.Leader = N<Agent>(nd.Leader);
                n.Team.AddRange(nd.Team.Select(r => (Agent)O(r)));
                n.Decoys.AddRange(nd.Decoys.Select(r => (Agent)O(r)));
                n.Captured.AddRange(nd.Captured.Select(r => (Agent)O(r)));
                n.Recruited = N<Character>(nd.Recruited);
                n.Teacher = N<Character>(nd.Teacher);
                n.StartDay = nd.StartDay;
                n.Accumulator = nd.Accumulator;
                n.LevelsGained = nd.LevelsGained;
                n.Encounters = nd.Encounters;
            }
        }

        // game lists and fields
        game.HeadquartersSystem = N<StarSystem>(d.Headquarters);
        game.Systems.AddRange(d.Systems.Select(r => (StarSystem)O(r)));
        game.Characters.AddRange(d.Characters.Select(r => (Character)O(r)));
        game.Created.AddRange(d.Created.Select(O));
        game.Fleets.AddRange(d.Fleets.Select(r => (Fleet)O(r)));
        game.Facilities.AddRange(d.Facilities.Select(r => (Facility)O(r)));
        game.Missions.AddRange(d.Missions.Select(r => (Mission)O(r)));
        foreach (uint r in d.Abodes) game.Abodes[O(r).Type] = O(r);
        // a save holds ReadyForDelete only on deleted objects (their delete handling ran in the same tick); they are in no container
        foreach (var x in map.Values) x.Deleted = x.ReadyForDelete && x.Container is null;
        game.IdCounter = d.NextId;
        game.Ready = d.Ready;
        // saves before this change kept manager F8 without both live sides
        if (game.Ready) (game.MissionManager.Observed, game.MissionManager.LiveSides) = (SideSet.Both, SideSet.Both);
        game.BattleRequired = d.BattleRequired;
        game.VictoryState = d.VictoryState;
        game.StoryFlags = d.StoryFlags ?? new();
        game.WaveTick = d.WaveTick;
        foreach (var e in d.WaveCounts) game.WaveCounts[(Owner)e[0]] = e[1];
        foreach (var sd in d.Sides)
        {
            var s = game.Sides[sd.Side];
            s.MaintenanceCapacity = sd.MaintenanceCapacity;
            s.MaintenanceAllocated = sd.MaintenanceAllocated;
            s.Demand = sd.Demand;
            s.Exceeded = sd.Exceeded;
            s.Speed = sd.Speed;
            s.RawStock = sd.RawStock;
            s.RefinedStock = sd.RefinedStock;
            s.RefineryWaitList.AddRange(sd.RefineryWaitList.Select(w => new WaitEntry((Facility)O((uint)w[0]), (int)w[1])));
            s.ManufacturingWaitList.AddRange(sd.ManufacturingWaitList.Select(w => new WaitEntry((Facility)O((uint)w[0]), (int)w[1])));
            sd.ResearchPoints.CopyTo(s.ResearchPoints, 0);
            sd.ResearchOrder.CopyTo(s.ResearchOrder, 0);
            sd.ResearchExhausted.CopyTo(s.ResearchExhausted, 0);
            s.CostScale = sd.CostScale;
            s.RecruitmentDone = sd.RecruitmentDone;
        }
        foreach (var v in d.Views)
            game.Views[(O(v.Object), v.Side)] = new View
            {
                Owner = v.Owner, Parent = N<GameObject>(v.Parent), Destroyed = v.Destroyed, Enroute = v.Enroute, Uprising = v.Uprising,
                Populated = v.Populated, Explored = v.Explored, Captured = v.Captured, Support = v.Support, Completed = v.Completed, Created = v.Created, Usable = v.Usable,
                BuilderKey = N<ManufacturingManager>(v.BuilderKey), EnrouteActive = v.EnrouteActive, RoleMission = N<Mission>(v.RoleMission),
                Blockade = v.Blockade, Battle = v.Battle, Damaged = v.Damaged, Speed = v.Speed, Control = v.Control,
                QueueCount = v.QueueCount, TaskStatus = v.TaskStatus, CompletionStatus = v.CompletionStatus, NeverBeenControlled = v.NeverBeenControlled, Updated = v.Updated,
            };
        foreach (var t in d.Timers)
        {
            object? subject = t.Subject is { } r ? O(r) : t.SubjectSide != Owner.None ? game.Sides[t.SubjectSide] : null;
            TimerCatalogue.Slot(game, t.Type, t.Target, subject).Restore(t.Serial, t.Base, t.Variance, t.Active);
        }
        foreach (var e in d.Events)
            game.Clock.RestoreTimed(new TimerEvent(game.Timers[(e.Type, e.Target)], e.Serial, e.Base, e.Variance) { FireDay = e.FireDay });
        Command C(CommandDto c) => new((CommandType)c.Type, c.Side)
        {
            Targets = [.. c.Targets.Select((r, i) => (O(r), c.Modes[i]))], ArgRef = N<GameObject>(c.ArgRef), ArgInt = c.ArgInt,
            Fields = c.Fields, Decoys = [.. c.Decoys.Select(O)], Serial = c.Serial, Sequence = c.Sequence, Text = c.Text,
        };
        foreach (var c in d.Commands) game.Clock.Submit(C(c));
        foreach (var k in d.Controllers ?? [])
        {
            Ai.Controller controller = k.Kind switch
            {
                1 => rand is null ? new Ai.HumanController(k.Side) : new Ai.HumanController(k.Side) { Rand = rand(k.Side) },
                3 => rand is null ? new Ai.ComputerController(k.Side) : new Ai.ComputerController(k.Side) { Rand = rand(k.Side) },
                4 => new Ai.OpenSWRController(k.Side), // draws nothing
                _ => throw new InvalidDataException($"Controller of side {k.Side} has the unknown kind {k.Kind}."),
            };
            controller.NextSequence = k.NextSequence;
            foreach (var c in k.Outbox) controller.Append(C(c));
            if (controller is Ai.ComputerController computer) computer.Restore(k.Issued, k.DaysSinceReset);
            if (controller is Ai.OpenSWRController openswr) openswr.Load(game, k.Issued, k.OpenSWR);
            if (controller is Ai.HumanController human && k.Advisor is { } advisor) human.Advisor = Advisor.Load(game, k.Side, advisor, () => game.Clock.Tick);
            game.Controllers[k.Side] = controller;
        }
        game.Clock.Restore(d.Tick, d.Day, d.TicksPerDay, d.SubTick, d.DayDue, d.Serial);
        game.Random.Restore(d.RngState, d.RngDraws);
        if (d.Log is not null) game.Log.AddRange(d.Log);
        game.FilterMode = d.FilterMode ?? MapFilter.Off;
        if (d.Windows is not null) game.Windows.AddRange(d.Windows);
        Messages.Migrate(game);
        // the computer AI continues: restored, then the original's load effects with their draws (side 1, side 2). An
        // older save without it keeps the rebuild path: game start on the first tick (not the original's behaviour)
        if (d.Controllers?.Any(k => k.Computer is not null) == true)
        {
            foreach (var k in d.Controllers)
                if (k.Computer is { } ai && game.Controllers[k.Side] is Ai.ComputerController computer) computer.Load(ai, game, O);
            Ai.Controller.AfterLoad(game);
        }
        return game;
    }

    /// <summary>An object whose id comes from its definition must have the saved id.</summary>
    // kinds whose type comes straight from the save: it must name a record of the game data (readers resolve it)
    static uint Typed(ObjectDto d, GameDefinitions defs) =>
        defs.Resolve(d.Type) is not null ? d.Type : throw new InvalidDataException($"Object {d.Id} has type {Family.Of(d.Type)}:{SdRef.IndexOf(d.Type)}, not in the game data.");

    static T Check<T>(T o, ObjectDto d) where T : GameObject =>
        o.Id == d.Id ? o : throw new InvalidDataException($"Object {Family.Of(d.Type)}:{SdRef.IndexOf(d.Type)} has id {o.Id}, saved {d.Id}.");
}
