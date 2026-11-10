using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>
/// A player or computer order (command object). Targets are (object, selection mode) pairs in list order. ArgRef is the
/// destination or location (null = none). Fields are the class fields as u32 (ProduceFacility to ProduceTroop: product type SdRef, count;
/// CreateMission / CreateMoveMission: A, B, mission type SdRef).
/// </summary>
public sealed class Command(CommandType type, Owner side)
{
    public CommandType Type { get; } = type;
    public Owner Side { get; } = side;
    public List<(GameObject Target, GroupMode Mode)> Targets { get; init; } = [];
    public GameObject? ArgRef { get; init; }
    public uint ArgInt { get; init; }
    public uint[] Fields { get; init; } = [];
    /// <summary>CreateMission / CreateMoveMission: the second list, the mission's decoys.</summary>
    public List<GameObject> Decoys { get; init; } = [];
    /// <summary>Serial, set on successful execution.</summary>
    public uint Serial { get; set; }
    /// <summary>Controller sequence: outbox and pending-list order; 0 = unnumbered (tests, replays, immediate UI orders).</summary>
    public uint Sequence { get; set; }
    /// <summary>Rename / SetProductName: the name string (the command's string object); null = unnamed. No sim effect.</summary>
    public string? Text { get; init; }
}

/// <summary>Command (order) types, also the object menu's row ids.</summary>
public enum CommandType
{
    /// <summary>Controller tick token: records the side's finished tick; no sim state, no serial.</summary>
    TickToken = 1,
    /// <summary>Menu row only: open the encyclopedia.</summary>
    Encyclopedia = 256,
    /// <summary>Menu row only: open the status window.</summary>
    Status = 259,
    Scrap = 512,
    Move = 513,
    /// <summary>Move variant. Meaning in the original is unknown.</summary>
    MoveVariant = 514,
    Rename = 515,
    /// <summary>Move one object to the destination (a sub-command of Move).</summary>
    MoveObject = 516,
    AcknowledgeDestroyed = 517,
    ProduceFacility = 528,
    ProduceShip = 529,
    ProduceTroop = 530,
    CancelProduction = 531,
    SetProductionTarget = 532,
    SetProductName = 533,
    /// <summary>Set the manager's Reserved value; no sim reader.</summary>
    SetReserved = 534,
    /// <summary>Bombard (first of four types that differ in validation only).</summary>
    Bombard = 544,
    /// <summary>Bombard order variant; the strike setup tests it apart from Bombard. Meaning in the original is unknown.</summary>
    BombardVariant1 = 545,
    /// <summary>Bombard order variant; the strike setup treats it like BombardDestroy. Meaning in the original is unknown.</summary>
    BombardVariant2 = 546,
    /// <summary>Bombard; with a Death Star in the fleet, destroy the planet.</summary>
    BombardDestroy = 547,
    Assault = 564,
    CreateMission = 576,
    /// <summary>Create a Move mission for agents (a sub-command of Move).</summary>
    CreateMoveMission = 577,
    /// <summary>Character order (computer only). Meaning in the original is unknown.</summary>
    CharacterOrder = 578,
    /// <summary>Mission order (Inferred: abort / recall).</summary>
    MissionOrder = 592,
    /// <summary>Set the character's command kind to type - ClearCommand: 0 none, 1 commander (fighters), 2 admiral, 3 general.</summary>
    ClearCommand = 608,
    MakeCommander = 609,
    MakeAdmiral = 610,
    MakeGeneral = 611,
    /// <summary>Character manager order. Meaning in the original is unknown.</summary>
    CharacterManagerOrder = 616,
    /// <summary>Move ships out of a fleet into a new fleet (Create Fleet).</summary>
    CreateFleet = 624,
    /// <summary>Game speed; not a command-base class, takes no serial.</summary>
    GameSpeed = 640,
    /// <summary>Placement check type for an object whose parent was destroyed; not a command.</summary>
    DestroyedParentPlacement = 1008,
}

/// <summary>Result record {category, code}; the success result is <see cref="Refusal.Accepted"/>.</summary>
public readonly record struct OrderResult(int Category, int Code)
{
    public static readonly OrderResult Ok = Refusal.Accepted;
    public bool Success => this == Ok;
}

/// <summary>
/// Order results of validation, by name. Install table: the category is an object family (DAT family value, the
/// Families constants) and the code is the refusal code of the original's result record. Values are kept as the
/// original gives them.
/// </summary>
public static class Refusal
{
    /// <summary>The command is accepted (the success result).</summary>
    public static readonly OrderResult Accepted = new(1, -1);
    /// <summary>The target class does not accept the command.</summary>
    public static readonly OrderResult WrongClass = new(1, 1);
    public static readonly OrderResult NotCompleted = new(1, 16);
    public static readonly OrderResult Enroute = new(1, 17);
    /// <summary>A target or the destination is missing or destroyed.</summary>
    public static readonly OrderResult Missing = new(1, 18);
    public static readonly OrderResult NotOwner = new(1, 20);
    /// <summary>Rename with more than one target.</summary>
    public static readonly OrderResult SeveralTargets = new(1, 21);
    public static readonly OrderResult NoLocation = new(1, 22);
    public static readonly OrderResult CannotRename = new(1, 23);
    public static readonly OrderResult CannotTravel = new(1, 24);
    /// <summary>The unit has no placement category.</summary>
    public static readonly OrderResult NotPlaceable = new(1, 25);
    public static readonly OrderResult NotCreated = new(1, 26);
    public static readonly OrderResult DestinationNotCompleted = new(1, 32);
    public static readonly OrderResult DestinationEnroute = new(1, 33);
    /// <summary>The destination is missing or destroyed.</summary>
    public static readonly OrderResult NoDestination = new(1, 34);
    public static readonly OrderResult DestinationOtherOwner = new(1, 36);
    public static readonly OrderResult NotInSystem = new(1, 37);
    public static readonly OrderResult WrongDestinationKind = new(1, 39);
    public static readonly OrderResult DestinationRefused = new(1, 40);
    public static readonly OrderResult DestinationClosed = new(1, 41);
    public static readonly OrderResult NoShips = new(8, 1);
    public static readonly OrderResult NoTroops = new(8, 5);
    public static readonly OrderResult NoBombardStrength = new(8, 6);
    public static readonly OrderResult NoBlockade = new(8, 7);
    public static readonly OrderResult NoDeathStar = new(8, 8);
    public static readonly OrderResult NoFighterRoom = new(20, 1);
    public static readonly OrderResult NoTroopRoom = new(20, 2);
    public static readonly OrderResult OnMission = new(48, 1);
    public static readonly OrderResult Captured = new(48, 2);
    public static readonly OrderResult Injured = new(48, 3);
    /// <summary>The mission does not take this kind of agent.</summary>
    public static readonly OrderResult AgentNotAllowed = new(64, 1);
    /// <summary>The agents are not at one location.</summary>
    public static readonly OrderResult NotTogether = new(64, 3);
    public static readonly OrderResult NoSkill = new(64, 4);
    public static readonly OrderResult NoForce = new(64, 5);
    public static readonly OrderResult MajorsOnly = new(64, 6);
    /// <summary>Dagobah / Vacation: the team must be Luke.</summary>
    public static readonly OrderResult NotLukeTeam = new(64, 7);
    public static readonly OrderResult DecoyNotAllowed = new(64, 8);
    public static readonly OrderResult CapturedMember = new(64, 9);
    /// <summary>Palace: Luke, Leia or Chewbacca only.</summary>
    public static readonly OrderResult NotPalaceTeam = new(64, 10);
    public static readonly OrderResult BountyHuntersOnly = new(64, 11);
    /// <summary>Pickup: the team must be Vader.</summary>
    public static readonly OrderResult NotVaderTeam = new(64, 12);
    public static readonly OrderResult TeacherNotStrongest = new(64, 13);
    public static readonly OrderResult TargetUnexplored = new(64, 17);
    public static readonly OrderResult TargetDestroyed = new(64, 18);
    public static readonly OrderResult TargetEnroute = new(64, 19);
    public static readonly OrderResult NotJabbasPalace = new(64, 21);
    public static readonly OrderResult TargetNotPopulated = new(64, 22);
    public static readonly OrderResult NoTargetLocation = new(64, 32);
    public static readonly OrderResult TargetLocationDestroyed = new(64, 33);
    public static readonly OrderResult ObjectiveEnroute = new(64, 34);
    public static readonly OrderResult ObjectiveOwnerRefused = new(64, 35);
    public static readonly OrderResult ObjectiveNotSystem = new(64, 36);
    public static readonly OrderResult SabotageCharacter = new(64, 37);
    public static readonly OrderResult ObjectiveNotCharacter = new(64, 38);
    public static readonly OrderResult ObjectiveNotUnit = new(64, 39);
    public static readonly OrderResult SabotageDeathStar = new(64, 40);
    public static readonly OrderResult NotDeathStar = new(64, 41);
    /// <summary>The target is a captured character and the mission does not take captured characters.</summary>
    public static readonly OrderResult CapturedCharacterRefused = new(64, 42);
    /// <summary>The target is a free character and the mission does not take free characters.</summary>
    public static readonly OrderResult FreeCharacterRefused = new(64, 43);
    public static readonly OrderResult NoResearchFacility = new(64, 44);
    public static readonly OrderResult NotAbode = new(64, 45);
    public static readonly OrderResult NotHermitage = new(64, 46);
    public static readonly OrderResult NotDagobah = new(64, 47);
    public static readonly OrderResult NotHan = new(64, 48);
    public static readonly OrderResult PickupNotAtPalace = new(64, 49);
    public static readonly OrderResult ResearchExhausted = new(64, 50);
    public static readonly OrderResult FullSupport = new(64, 51);
    public static readonly OrderResult ObjectiveNotCompleted = new(64, 52);
    public static readonly OrderResult RecruitmentDone = new(64, 53);
    /// <summary>Mission order before the mission reached its target phase.</summary>
    public static readonly OrderResult MissionNotStarted = new(64, 64);
    public static readonly OrderResult MissionNotAtTarget = new(64, 65);
    public static readonly OrderResult NoTeacher = new(64, 144);
    public static readonly OrderResult NoFreeAgent = new(64, 145);
    public static readonly OrderResult NoMissionType = new(64, 146);
    public static readonly OrderResult TeamTooSmall = new(64, 148);
    public static readonly OrderResult NoEnergy = new(144, 1);
    public static readonly OrderResult NoRawMaterials = new(144, 2);
    public static readonly OrderResult DeathStarLimit = new(144, 3);
    public static readonly OrderResult Blockaded = new(144, 4);
    public static readonly OrderResult ShieldsBlockBombard = new(144, 5);
    public static readonly OrderResult SystemNotBlockaded = new(144, 6);
    public static readonly OrderResult EmptyQueue = new(160, 1);
    public static readonly OrderResult AllSuspended = new(160, 3);
    /// <summary>Manager refusal code 2; meaning in the original is unknown (Inferred). No sim path returns it yet; the advisor answers it with its UnknownManager line.</summary>
    public static readonly OrderResult UnknownManager = new(160, 2);
    /// <summary>What the MoveObject wrapper rewrites NotInSystem and WrongDestinationKind to (Inferred); the advisor stays silent on it. The rewrite is not modelled yet.</summary>
    public static readonly OrderResult RewrittenDestination = new(1, 38);
    public static readonly OrderResult NoMaintenance = new(243, 1);
}

/// <summary>
/// Selection (group) mode of a target node, one value per mode, never combined: Object (the object itself, AI), Picture (a
/// system's picture: the object itself), or one group of a system: Facilities (facilities / managers), Ground (troops, fighters,
/// characters on the planet), Fleets, Missions.
/// The original encodes these as bit-like values; the picture mode shares the object's group (Inferred).
/// </summary>
public enum GroupMode { Object, Picture, Facilities, Ground, Fleets, Missions }

public static class GroupModes
{
    /// <summary>The group a mode selects: the picture selects the object itself.</summary>
    public static GroupMode Group(GroupMode mode) => mode == GroupMode.Picture ? GroupMode.Object : mode;
}

/// <summary>
/// Command execution: the sim's only input after setup. Commands wait in the clock's pending list and run only when the
/// priority, timed and FIFO queues are empty; a successful command takes a serial and posts one order event per target to the
/// FIFO queue.
/// </summary>
public static class Orders
{
    /// <summary>Appends a command to the pending list; <paramref name="done"/> receives the result when it runs.</summary>
    public static void Submit(GameState game, Command command, Action<OrderResult>? done = null) =>
        game.Clock.Submit(command, done);

    /// <summary>
    /// Execute (composites included): build events, validate, then serial and FIFO posts. GameSpeed (speed message) is not a
    /// command-base class: it sets the side's ticks per day (ignored when 0) and takes no serial.
    /// </summary>
    public static OrderResult Execute(GameState game, Command c)
    {
        switch (c.Type)
        {
            case CommandType.TickToken: // tick token: records the side's finished tick, no sim state, no serial
                return OrderResult.Ok;
            case CommandType.GameSpeed:
                if (c.ArgInt != 0) game.Sides[c.Side].Speed = (int)c.ArgInt;
                return OrderResult.Ok;
            case CommandType.Move or CommandType.MoveVariant:
                return Move(game, c);
        }
        var (events, result) = BuildEvents(game, c);
        if (!result.Success) return result;
        c.Serial = game.Clock.NextSerial();
        foreach (var e in events) game.Clock.PostFifo(new ActionEvent(0, e));
        return OrderResult.Ok;
    }

    /// <summary>
    /// Move composite: the resolved targets form a list by ascending SdRef. Repeat: the Completed
    /// characters and special forces (48-63) are candidates; the first one's location is the group location; every candidate
    /// there forms one CreateMoveMission (mission type family 65 index 1, A = B = 0), executed at once; until no candidate is left. Then all remaining
    /// targets form one MoveObject (none left: no sub-command, Inferred). All with the composite's ArgRef; the composite takes no
    /// serial of its own.
    /// </summary>
    static OrderResult Move(GameState game, Command c)
    {
        var (subs, result) = Expand(c);
        if (!result.Success) return result;
        foreach (var sub in subs)
            if (Execute(game, sub) is { Success: false } r) return r;
        return OrderResult.Ok;
    }

    /// <summary>The move composite's sub-commands, built once: the CreateMoveMission groups, then the MoveObject of the rest.</summary>
    internal static (List<Command> Subs, OrderResult Result) Expand(Command c)
    {
        var (resolved, result) = Resolve(c);
        if (!result.Success) return ([], result);
        var subs = new List<Command>();
        var targets = resolved.OrderBy(t => t.Ref).ToList();
        while (targets.FirstOrDefault(IsCandidate) is { } first)
        {
            var group = targets.Where(t => IsCandidate(t) && t.System == first.System).ToList();
            targets.RemoveAll(group.Contains);
            subs.Add(new Command(CommandType.CreateMoveMission, c.Side) { Targets = [.. group.Select(t => (t, GroupMode.Object))], ArgRef = c.ArgRef, Fields = [0, 0, Refs.MoveMission] });
        }
        if (targets.Count > 0) subs.Add(new Command(CommandType.MoveObject, c.Side) { Targets = [.. targets.Select(t => (t, GroupMode.Object))], ArgRef = c.ArgRef });
        return (subs, OrderResult.Ok);

        static bool IsCandidate(GameObject t) => Family.Of(t.Type) is >= Families.MajorCharacter and < Families.Mission && t.Completed;
    }

    /// <summary>
    /// Validation without execution (the controllers' pre-check before the outbox): the execute
    /// path's checks on the current state, no serial, no events. Composites: each sub-command's validation in order, the first
    /// error stands (Move / MoveVariant). Tick tokens and GameSpeed pass.
    /// </summary>
    public static OrderResult Validate(GameState game, Command c)
    {
        if (c.Type is CommandType.TickToken or CommandType.GameSpeed) return OrderResult.Ok;
        if (c.Type is not (CommandType.Move or CommandType.MoveVariant)) return BuildEvents(game, c).Result;
        var (subs, result) = Expand(c);
        if (!result.Success) return result;
        foreach (var sub in subs)
            if (BuildEvents(game, sub).Result is { Success: false } r) return r;
        return OrderResult.Ok;
    }

    // ---- target resolution ----

    /// <summary>The resolved target set (<see cref="Resolve"/>) in key order; empty when refused (the scrap confirmation body).</summary>
    public static List<GameObject> Targets(Command c) => [.. Resolve(c).Targets.OrderBy(t => t.Ref)];

    /// <summary>
    /// Target set in list order, duplicates collapsed (first occurrence keeps its place). A fleet (any mode): with a
    /// fleet ArgRef its direct Existing contents of the fleet's owner (key order; its capital ships), else itself. Object and picture
    /// modes: the object itself. A system
    /// expands the other modes by the group and the command type over its direct Existing contents (group iterators) owned by the
    /// issuing side (Inferred: no further filter; run 2 day 5 excludes the side's placeholder fleets, facts vector): facilities (Scrap) or its managers (SetProductionTarget, SetReserved); ground to troops, fighters,
    /// then further groups (Scrap: special forces, Inferred) / troops, fighters, characters (Move, MoveVariant) / characters (CreateMission,
    /// CharacterOrder); fleets to fleets; missions to characters with a listed mission (the record's listed-mission flag clear, <see cref="ScreenViews.Listed"/>; Inferred reading); else refused (Missing).
    /// </summary>
    static (List<GameObject> Targets, OrderResult Result) Resolve(Command c)
    {
        var set = new List<GameObject>();
        foreach (var (o, mode) in c.Targets)
        {
            if (o.Deleted) return ([], Refusal.Missing); // missing key
            IEnumerable<GameObject> add;
            if (o is Fleet fleet) add = c.ArgRef is Fleet ? fleet.Contents.Where(x => x.Existing && x.Owner == fleet.Owner) : [o];
            else if (GroupModes.Group(mode) == GroupMode.Object) add = [o];
            else if (o is StarSystem s)
            {
                var own = s.Contents.Where(x => x.Owner == c.Side && x.Existing).ToList(); // mode-3 iterators: Existing (placeholder fleets are not)
                IEnumerable<GameObject> Of(uint lo, uint hi) => own.Where(x => Family.Of(x.Type) >= lo && Family.Of(x.Type) < hi);
                IEnumerable<GameObject>? expanded = (GroupModes.Group(mode), c.Type) switch
                {
                    (GroupMode.Facilities, CommandType.Scrap) => Of(Families.Headquarters, Families.MajorCharacter),
                    (GroupMode.Facilities, CommandType.SetProductionTarget or CommandType.SetReserved) => s.Managers,
                    (GroupMode.Ground, CommandType.Scrap) => Of(Families.Troop, Families.CapitalShip).Concat(Of(Families.Fighter, Families.Headquarters)).Concat(Of(Families.SpecialForce, Families.Mission)),
                    (GroupMode.Ground, CommandType.Move or CommandType.MoveVariant) => Of(Families.Troop, Families.CapitalShip).Concat(Of(Families.Fighter, Families.Headquarters)).Concat(Of(Families.MajorCharacter, Families.SpecialForce)),
                    (GroupMode.Ground, CommandType.CreateMission or CommandType.CharacterOrder) => Of(Families.MajorCharacter, Families.SpecialForce),
                    (GroupMode.Fleets, _) => Of(Families.Fleet, Families.Troop),
                    (GroupMode.Missions, _) => Of(Families.MajorCharacter, Families.SpecialForce).Where(x => x is Character { Mission: { } m } && ScreenViews.Listed(m)),
                    _ => null,
                };
                if (expanded is null) return ([], Refusal.Missing);
                add = expanded;
            }
            else return ([], Refusal.Missing);
            foreach (var x in add)
                if (!set.Contains(x)) set.Add(x);
        }
        return (set, OrderResult.Ok);
    }

    /// <summary>
    /// Generic object order check: owner not in {1, 2} or != issuing side (NotOwner); not Completed unless the type is
    /// Rename, MoveObject, SetProductName or CreateMoveMission (NotCompleted); Destroyed (Missing); Enroute (Enroute). The class-accepts-the-type test (1, 1) is
    /// not modelled.
    /// </summary>
    static OrderResult Check(Command c, GameObject o) =>
        o.Owner is not (Owner.Alliance or Owner.Empire) || o.Owner != c.Side ? Refusal.NotOwner
        : !o.Completed && c.Type is not (CommandType.Rename or CommandType.MoveObject or CommandType.SetProductName or CommandType.CreateMoveMission) ? Refusal.NotCompleted
        : o.Destroyed ? Refusal.Missing
        : o.Enroute ? Refusal.Enroute
        : OrderResult.Ok;

    static readonly OrderResult Fail = Refusal.WrongClass;

    // ---- check pipeline (per family) ----

    /// <summary>
    /// Command check pipeline: pre-check (target expansion, empty (NoLocation), per event the
    /// pre-check slot then the single-owner test (NotOwner), own pre-check parts), class validation, event checks; the first error
    /// stands, a sub-step returning 0 gives (1, 1). The serial is taken by the caller only on success. Missions: <see cref="MissionEvents"/>.
    /// </summary>
    static (List<Action<GameState>> Events, OrderResult Result) BuildEvents(GameState game, Command c)
    {
        var (targets, result) = Resolve(c);
        if (!result.Success) return ([], result);
        if (c.Type is CommandType.CreateMission or CommandType.CreateMoveMission) return MissionEvents(game, c, targets);
        if (targets.Count == 0) return ([], Refusal.NoLocation);
        // 1. pre-check over the event list: the pre-check slot, (missing-only lookup: never fails here), single owner
        foreach (var t in targets)
        {
            if (PreSlot(game, c, t) is { Success: false } slot) return ([], slot);
            if (t.Owner != targets[0].Owner) return ([], Refusal.NotOwner);
        }
        bool combat = c.Type is CommandType.Assault or (>= CommandType.Bombard and <= CommandType.BombardDestroy);
        if (c.Type == CommandType.Rename && targets.Count > 1) return ([], Refusal.SeveralTargets); // own pre-check
        if (combat)
        {
            // own pre-checks: the prepared system event list: system direct (Missing), then the
            // system check; then the assault pre-check (8, 7) / (8, 5) or the bombard fleet scan (8, 6) / (8, 8)
            if (c.ArgRef is null || c.ArgRef.Destroyed) return ([], Refusal.Missing);
            if (c.ArgRef is not StarSystem) return ([], Fail);
            if (Combat.SystemCheck(game, c) is { Success: false } systemFail) return ([], systemFail);
            var check = c.Type == CommandType.Assault ? Combat.AssaultPreCheck(targets) : Combat.BombardCheck(game, c, targets);
            if (!check.Success) return ([], check);
        }
        // 2. class validation, 3. event checks
        if (c.Type is >= CommandType.ProduceFacility and <= CommandType.ProduceTroop) return ProductionEvents(game, c, targets);
        if (c.Type == CommandType.SetProductionTarget) return TargetEvents(game, c, targets);
        var destinations = new List<GameObject?>();
        if (c.Type is CommandType.MoveObject or CommandType.CreateFleet)
        {
            if (c.Type == CommandType.MoveObject && MoveCheck(c, targets) is { Success: false } refused) return ([], refused);
            if (c.ArgRef is null or { Deleted: true }) return ([], Fail); // CreateFleet: no resolve; the sandbox load fails
            var sandbox = new Fleets.Sandbox { Second = targets[0].Owner };
            foreach (var t in targets)
            {
                if (t.Container is { } from) sandbox.Commit(from, t.Type, -1);
                if (Fleets.Place(game, c.ArgRef, t.Type, c.Side, c.Type, out var error, sandbox, t) is not { } destination) return ([], error);
                destinations.Add(destination);
            }
            // Not modelled: the MoveObject wrapper's rewrite of (NotInSystem) and (WrongDestinationKind) to (RewrittenDestination) (command info set, Inferred meaning).
            for (int i = 0; i < targets.Count; i++)
                if (MoveEventCheck(game, c, targets[i], destinations[i]!) is { Success: false } fail) return ([], fail);
        }
        // MissionOrder recall: after the base check, phase 4 gives (MissionNotStarted), phase not 8 gives (MissionNotAtTarget)
        if (c.Type == CommandType.MissionOrder)
            foreach (var t in targets)
                if (t is not Mission rm) return ([], Fail);
                else if (rm.Phase == 4) return ([], Refusal.MissionNotStarted);
                else if (rm.Phase != 8) return ([], Refusal.MissionNotAtTarget);
        // SetReserved: if any target manager is Reserved, every event clears, else every event sets (a toggle over the selection);
        // read when the events are built, here at execution (Inferred: validation or execution not checked)
        bool reserve = c.Type == CommandType.SetReserved && !targets.Any(t => ((ManufacturingManager)t).Reserved);
        // event checks of the other families repeat their pre-check slots on unchanged state and pass
        var events = new List<Action<GameState>>();
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var destination = destinations.Count > 0 ? destinations[i] : null;
            events.Add(c.Type switch
            {
                CommandType.Scrap => g => Objects.Destroy(g, t, DestroyedReason.Scrapped),                       // scrap
                CommandType.MoveObject or CommandType.CreateFleet => g => Fleets.Move(g, t, destination!),
                CommandType.Rename => g => g.ViewMessage(ViewMessageType.Name, t),                             // the views' Name message
                CommandType.CancelProduction => g => { foreach (var u in ((ManufacturingManager)t).Orders.ToList()) Objects.Destroy(g, u, DestroyedReason.ProductionCancelled); },
                // Command kind = type - ClearCommand, applied at the character's post (<see cref="SetCommandKind"/>)
                >= CommandType.ClearCommand and <= CommandType.MakeGeneral => g => { if (t is Character ch) SetCommandKind(ch, c.Type - CommandType.ClearCommand); },
                CommandType.Assault => g => ((Fleet)t).Assault = true,                        // the assault event
                >= CommandType.Bombard and <= CommandType.BombardDestroy => g => ((Fleet)t).Bombard = true,        // event Bombard
                // SetReserved: Reserved := payload; the side-view copy needs nothing here (live views
                // read the real value, Knowledge.Reserved)
                CommandType.SetReserved => g => ((ManufacturingManager)t).Reserved = reserve,
                // AcknowledgeDestroyed run: the command side's view of the target acknowledges
                CommandType.AcknowledgeDestroyed => g => Objects.Acknowledge(g, t, c.Side),
                // Rename, SetProductName: no sim effect.
                // MissionOrder recall: in phase 8 CompletionStatus := 2, set through a virtual call
                CommandType.MissionOrder => g => { if (t is Mission { Phase: 8 } rm) Missions.SetCompletion(g, rm, MissionCompletion.Aborted); },
                // CharacterOrder, CharacterManagerOrder: effects deferred, not modelled.
                _ => _ => { },
            });
        }
        // Assault / Bombard to BombardDestroy: the prepared system event (assault event, bombardment event) goes to the FIFO after the fleet events
        if (combat)
        {
            var s = (StarSystem)c.ArgRef!;
            events.Add(c.Type == CommandType.Assault ? g => Combat.Assault(g, s) : g => Combat.Bombard(g, s, c.Type));
        }
        return (events, OrderResult.Ok);
    }

    /// <summary>
    /// Event pre-check slot per family: every slot resolves its target direct (missing or Destroyed
    /// (Missing)) except AcknowledgeDestroyed (no check). Scrap: the generic check. Rename: fleets and capital ships get the generic check,
    /// characters / special forces pass, others (CannotRename). MoveObject / CreateFleet: fleets, capital ships, fighters, troops, the
    /// headquarters: the generic check; characters / special forces: pass; others (CannotRename)), then the headquarters blockade rule.
    /// ProduceFacility to ProduceTroop, SetReserved: manager resolver (another class to (1, 1)), the generic check, a non-Suspended member (AllSuspended). CancelProduction: manager
    /// resolver, the generic check, a non-empty queue (EmptyQueue). SetProductionTarget: manager resolver, the generic check. Combat fleets: fleet
    /// resolver (another family to (1, 1)), the generic check, a Completed non-Enroute capital ship ((NotCompleted) / (Enroute) /
    /// (8, 1)); Assault also the Blockade flag (8, 7).
    /// Other types (Inferred): the generic check.
    /// </summary>
    static OrderResult PreSlot(GameState game, Command c, GameObject t)
    {
        if (c.Type == CommandType.AcknowledgeDestroyed) return OrderResult.Ok;
        bool combat = c.Type is CommandType.Assault or (>= CommandType.Bombard and <= CommandType.BombardDestroy);
        bool manager = c.Type is (>= CommandType.ProduceFacility and <= CommandType.SetProductionTarget) or CommandType.SetReserved;
        if (combat && t is not Fleet || manager && t is not ManufacturingManager) return Fail; // resolver: wrong family
        if (t.Destroyed) return Refusal.Missing;
        uint f = Family.Of(t.Type);
        switch (c.Type)
        {
            case CommandType.Rename: // fleets / capital ships the generic check, characters / special forces pass, others (CannotRename)
                return t is Fleet || Family.IsCapitalShip(t.Type) ? Check(c, t) : f is >= Families.MajorCharacter and < Families.Mission ? OrderResult.Ok : Refusal.CannotRename;
            case CommandType.MoveObject or CommandType.CreateFleet:
                if (MovedByGenericCheck(f) && Check(c, t) is { Success: false } fail) return fail;
                if (!MovedByGenericCheck(f) && f is not (>= Families.MajorCharacter and < Families.Mission)) return Refusal.CannotRename;
                return HeadquartersBlockade(game, t);
            case CommandType.CancelProduction: // the generic check, then a non-empty queue (EmptyQueue)
                if (Check(c, t) is { Success: false } cancelFail) return cancelFail;
                return ((ManufacturingManager)t).Orders.Count > 0 ? OrderResult.Ok : Refusal.EmptyQueue;
            case (>= CommandType.ProduceFacility and <= CommandType.ProduceTroop) or CommandType.SetReserved: // SetReserved: the event slot runs the same checks
                if (Check(c, t) is { Success: false } managerFail) return managerFail;
                return Economy.Members((ManufacturingManager)t).Any(x => !x.Suspended) ? OrderResult.Ok : Refusal.AllSuspended;
            case CommandType.Assault:
                return Check(c, t) is { Success: false } fleetFail ? fleetFail : Combat.AssaultFleetCheck(t);
            case >= CommandType.Bombard and <= CommandType.BombardDestroy:
                return Check(c, t) is { Success: false } bombardFail ? bombardFail : Combat.ShipCheck(t);
            default:
                return Check(c, t);
        }
    }

    /// <summary>The generic check: fleets, troops, capital ships, fighters and the headquarters (family 32).</summary>
    static bool MovedByGenericCheck(uint family) => family is >= Families.Fleet and < Families.Headquarters or Families.Headquarters;

    /// <summary>
    /// Move pre-check part: an object that can travel, of families [32, 34) (headquarters), in
    /// a system with Blockade gives (Blockaded) when withdraw(S, F) is less than 31 (S, F as for WithdrawPercent).
    /// </summary>
    internal static OrderResult HeadquartersBlockade(GameState game, GameObject t) =>
        Family.Of(t.Type) is >= Families.Headquarters and < Families.IonCannon && Fleets.CanTravel(t) && t.System is { Blockade: true } s && Combat.Withdraw(game, s) < 31
            ? Refusal.Blockaded : OrderResult.Ok;

    /// <summary>
    /// Move validation: side = the first target's owner; the destination missing-only (NoDestination) (a Destroyed one
    /// passes and meets container acceptance); a destination of another owner is refused (DestinationRefused) unless all targets are fleets
    /// / capital ships, or all are troops and the destination is a system with Existing and without Populated (Inferred reading).
    /// The sandbox move per target is in BuildEvents.
    /// </summary>
    static OrderResult MoveCheck(Command c, List<GameObject> targets)
    {
        if (c.ArgRef is not { Deleted: false } d) return Refusal.NoDestination; // missing-only: a deleted key
        if (d.Owner == targets[0].Owner) return OrderResult.Ok;
        bool ships = targets.All(t => Family.Of(t.Type) is >= Families.Fleet and < Families.Troop || Family.IsCapitalShip(t.Type));
        bool troops = targets.All(t => Family.IsTroop(t.Type)) && d is StarSystem { Existing: true, Populated: false };
        return ships || troops ? OrderResult.Ok : Refusal.DestinationRefused;
    }

    /// <summary>
    /// Move event check: target direct (Missing); target class check (fleets, capital ships, fighters, troops, characters,
    /// special forces: the generic check; others (CannotRename)); a fresh sandbox (side = the target's owner) re-places the target
    /// directly into the chosen container to its acceptance error; for an object that can travel, to another system: speed 0 gives
    /// (CannotTravel), a troop whose container's owner differs from its own gives (DestinationRefused).
    /// </summary>
    static OrderResult MoveEventCheck(GameState game, Command c, GameObject t, GameObject destination)
    {
        if (t.Destroyed) return Refusal.Missing;
        uint family = Family.Of(t.Type);
        if (!MovedByGenericCheck(family) && family is not (>= Families.MajorCharacter and < Families.Mission)) return Refusal.CannotRename;
        if (Check(c, t) is { Success: false } fail) return fail;
        var sandbox = new Fleets.Sandbox { Second = t.Owner };
        if (t.Container is { } from) sandbox.Commit(from, t.Type, -1);
        if (Fleets.Accept(game, destination, t.Type, t.Owner, c.Type, sandbox, t) is { Success: false } placement) return placement;
        if (Fleets.CanTravel(t) && Missions.Location(t) != Missions.Location(destination))
        {
            if (Speed(game, t) == 0) return Refusal.CannotTravel;
            if (Family.IsTroop(t.Type) && destination.Owner != t.Owner) return Refusal.DestinationRefused;
        }
        return OrderResult.Ok;
    }

    // ---- production ----

    /// <summary>Accepted manager kind per production type: ProduceFacility construction, ProduceShip shipyard, ProduceTroop training.</summary>
    static ManufacturingKind KindFor(CommandType type) => type switch { CommandType.ProduceFacility => ManufacturingKind.Construction, CommandType.ProduceShip => ManufacturingKind.Ship, _ => ManufacturingKind.Training };

    /// <summary>
    /// Validation (after the pre-check slots): per manager (class refusing the type to (1, 1)) its TargetKey
    /// missing-only (never: TargetKey is always set; a Destroyed one meets container acceptance) and, if owned by another side,
    /// only families [20, 28) (DestinationRefused); product check (unresolvable product of [16, 64) gives (1, 1); MaintenanceCost *
    /// count > free gives (NoMaintenance)); count split over F = sum members (suspended included): q = count div F, r = count mod F,
    /// n_m = members(m)*q + min(members(m), r), r -= min(members(m), r); per manager its queue leaves the sandbox and each node
    /// is placed by the solver at TargetKey, which gives the kept placement error. Event checks: the slot again (unchanged
    /// state, passes), the second sandbox (equal sides, not modelled), maintenance of the event's units (NoMaintenance).
    /// </summary>
    static (List<Action<GameState>>, OrderResult) ProductionEvents(GameState game, Command c, List<GameObject> targets)
    {
        uint product = c.Fields[0];
        int count = (int)c.Fields[1];
        var managers = new List<ManufacturingManager>();
        foreach (var t in targets)
        {
            var m = (ManufacturingManager)t;
            if (m.Kind != KindFor(c.Type)) return ([], Fail); // class refuses the type
            if (m.TargetKey.Deleted) return ([], Refusal.NoDestination); // TargetKey missing-only
            if (m.TargetKey.Owner != m.Owner && !Family.IsCapitalShip(product)) return ([], Refusal.DestinationRefused);
            managers.Add(m);
        }
        if (Family.Of(product) is >= Families.Troop and < Families.Mission && game.Definitions.Resolve(product) is null) return ([], Fail);
        if (game.Sides.TryGetValue(managers[0].Owner, out var owner)
            && game.MaintenanceCost(product) * count > owner.MaintenanceCapacity - owner.MaintenanceAllocated) return ([], Refusal.NoMaintenance);
        int f = managers.Sum(m => Economy.Members(m).Count);
        int q = f == 0 ? 0 : count / f, r = f == 0 ? 0 : count % f;
        var events = new List<Action<GameState>>();
        var counts = new List<int>();
        var sandbox = new Fleets.Sandbox { Second = targets[0].Owner }; // first side = the command's side
        foreach (var m in managers)
        {
            int members = Economy.Members(m).Count, n = members * q + Math.Min(members, r);
            r -= Math.Min(members, r);
            foreach (var unit in m.Orders) if (unit.Container is { } at) sandbox.Commit(at, unit.Type, -1); // the order cancels the queue
            var parents = new List<GameObject>();
            for (int i = 0; i < n; i++)
            {
                // per-node placement first runs a check: a product in no placement category gives (NotPlaceable)
                if (Fleets.Category(product) < 0) return ([], Refusal.NotPlaceable);
                if (Fleets.Place(game, m.TargetKey, product, c.Side, c.Type, out var error, sandbox) is not { } parent) return ([], error);
                parents.Add(parent);
            }
            events.Add(g => Production(g, m, product, parents));
            counts.Add(n);
        }
        for (int i = 0; i < managers.Count; i++)
        {
            var side = game.Sides[managers[i].Owner];
            if (game.MaintenanceCost(product) * counts[i] > side.MaintenanceCapacity - side.MaintenanceAllocated) return ([], Refusal.NoMaintenance);
        }
        return (events, OrderResult.Ok);
    }

    /// <summary>
    /// Production target SetProductionTarget, validation (after the pre-check slots: manager direct, the generic check): the
    /// destination missing-only (NoDestination); a destination of another owner with any queued unit that is not a capital ship
    /// [20, 28) gives (DestinationRefused) (empty queues pass). One sandbox, per manager in list order: empty queue - the destination direct
    /// (Destroyed (NoDestination)), then construction: owner != command side (DestinationOtherOwner), != the managers' owner (DestinationRefused), not Completed
    /// (DestinationNotCompleted), Enroute (DestinationEnroute), not a system (NotInSystem); shipyard / training: not Completed (DestinationNotCompleted), Enroute (DestinationEnroute), not a
    /// system / fleet / capital ship (NotInSystem). Non-empty queue: the queue leaves the sandbox, then each queued unit (queue order)
    /// gets its own solver container under SetProductionTarget, giving the kept placement error. Event checks repeat the slot (passes); not modelled:
    /// the side's view of the manager (unreachable after the generic check) and the second sandbox (equal sides).
    /// </summary>
    static (List<Action<GameState>>, OrderResult) TargetEvents(GameState game, Command c, List<GameObject> targets)
    {
        var managers = targets.Cast<ManufacturingManager>().ToList();
        var side = managers[0].Owner;
        if (c.ArgRef is not { Deleted: false } d) return ([], Refusal.NoDestination); // missing-only: a deleted key
        if (d.Owner != side && managers.Any(m => m.Orders.Any(u => !Family.IsCapitalShip(u.Type)))) return ([], Refusal.DestinationRefused);
        var sandbox = new Fleets.Sandbox { Second = side }; // first side = the command's side
        var events = new List<Action<GameState>>();
        foreach (var m in managers)
        {
            var nodes = new List<(GameObject Unit, GameObject Container)>();
            if (m.Orders.Count == 0)
            {
                bool system = d is StarSystem;
                var refused = d.Destroyed ? Refusal.NoDestination
                    : m.Kind == ManufacturingKind.Construction
                    ? d.Owner != c.Side ? Refusal.DestinationOtherOwner
                    : d.Owner != side ? Refusal.DestinationRefused
                    : !d.Completed ? Refusal.DestinationNotCompleted
                    : d.Enroute ? Refusal.DestinationEnroute
                    : !system ? Refusal.NotInSystem : OrderResult.Ok
                    : !d.Completed ? Refusal.DestinationNotCompleted
                    : d.Enroute ? Refusal.DestinationEnroute
                    : !(system || d is Fleet || Family.IsCapitalShip(d.Type)) ? Refusal.NotInSystem : OrderResult.Ok;
                if (!refused.Success) return ([], refused);
            }
            else
            {
                foreach (var unit in m.Orders) if (unit.Container is { } at) sandbox.Commit(at, unit.Type, -1);
                foreach (var unit in m.Orders)
                {
                    if (Fleets.Place(game, d, unit.Type, c.Side, CommandType.SetProductionTarget, out var error, sandbox, unit) is not { } container) return ([], error);
                    nodes.Add((unit, container));
                }
            }
            events.Add(g => Target(g, m, d, nodes));
        }
        return (events, OrderResult.Ok);
    }

    /// <summary>
    /// Target event: TargetKey := destination, then each node's unit is re-parented into its container
    /// in list order, without any capacity, owner, Blockade, Destroyed or Completed test (the walk stops only on a key
    /// deleted from the registry, which the sim never does). A queued unit is not Constructed, so it moves at once without travel
    /// and keeps the manager's points.
    /// </summary>
    static void Target(GameState game, ManufacturingManager m, GameObject destination, List<(GameObject Unit, GameObject Container)> nodes)
    {
        m.TargetKey = destination;
        foreach (var (unit, container) in nodes) Objects.SetParent(game, unit, container);
    }

    /// <summary>
    /// Production event: cancel the queue (every queued unit DestroyedReason ProductionCancelled, no refund: not Completed), then
    /// create the units node by node (owner = manager's owner, Created 1, Completed 0) in each node's parent, BuilderKey := manager.
    /// A production order replaces the queue.
    /// </summary>
    static void Production(GameState game, ManufacturingManager m, uint product, List<GameObject> parents)
    {
        foreach (var unit in m.Orders.ToList()) Objects.Destroy(game, unit, DestroyedReason.ProductionCancelled);
        foreach (var parent in parents) Objects.Create(game, product, m.Owner, parent, completed: false).BuilderKey = m;
    }

    // ---- travel time ----

    /// <summary>
    /// Days: dist = isqrt'(dx^2 + dy^2) over the systems' SD X/Y; t = ((dist div G TravelDistanceDivisor) * speed)
    /// div 100; dist != 0 and t = 0 to 1.
    /// </summary>
    public static int TravelDays(GameState game, StarSystem from, StarSystem to, int speed)
    {
        int dist = Distance(from, to);
        int t = dist / game.Rules.TravelDistanceDivisor * speed / 100;
        return dist != 0 && t == 0 ? 1 : t;
    }

    /// <summary>dist = isqrt'(dx^2 + dy^2) over the systems' SD X/Y.</summary>
    public static int Distance(StarSystem a, StarSystem b)
    {
        int dx = a.Definition.X - b.Definition.X, dy = a.Definition.Y - b.Definition.Y;
        return Isqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// isqrt' = floor(sqrt n), except 0 for n <= 0, 1 for n in {1, 2, 3}, and floor(sqrt n) - 1 when n = 4^k (k >= 1).
    /// </summary>
    public static int Isqrt(int n)
    {
        if (n <= 0) return 0;
        if (n <= 3) return 1;
        int s = (int)Math.Sqrt(n);
        while (s * s > n) s--;
        while ((s + 1) * (s + 1) <= n) s++;
        int k = n;
        while (k % 4 == 0) k /= 4;
        return k == 1 ? s - 1 : s; // n is a power of 4
    }

    /// <summary>
    /// Speed percentage (larger = slower). Capital ships (0 unless Existing): Hyperdrive - m1, if 0
    /// HyperdriveIfDamaged - m2, if 0 (the DestroyedReason check is not modelled: 0) 100; m1 = Hyperdrive *
    /// PrimaryHyperdriveModifier, m2 = HyperdriveIfDamaged * BackupHyperdriveModifier. Fighters:
    /// modifiers 0, so Hyperdrive, else HyperdriveIfDamaged, else 100 (0 unless Existing). Characters: MissionHyperdriveModifier (initial G HyperdriveModifier) if Existing,
    /// else 0. Fleets: over their Completed, not EnrouteActive capital ships in key order, the first sets v, a faster one or speed
    /// 0 replaces it, 0 sticks; no ship to 0. Others: G HyperdriveModifier if Existing, else 0.
    /// </summary>
    public static int Speed(GameState game, GameObject o)
    {
        uint f = Family.Of(o.Type);
        if (f is >= Families.CapitalShip and < Families.Headquarters)
        {
            bool fighter = f >= Families.Fighter;
            if (!o.Existing) return 0; // fighters too
            var r = game.Definitions.Resolve(o.Type)!;
            int primary = r.Get(SdField.Hyperdrive), backup = r.Get(SdField.HyperdriveIfDamaged);
            int v = primary - (fighter ? 0 : primary * o.Critical(4));
            if (v == 0) v = backup - (fighter ? 0 : backup * o.Critical(5));
            return v == 0 ? 100 : v;
        }
        if (o is Character c) return c.Existing ? c.MissionHyperdriveModifier ?? game.Rules.HyperdriveModifier : 0;
        if (f is >= Families.Fleet and < Families.Troop)
        {
            int? v = null;
            foreach (var ship in o.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Completed && !x.EnrouteActive))
            {
                int s = Speed(game, ship);
                if (v is null || (v != 0 && (s > v || s == 0))) v = s;
            }
            return v ?? 0;
        }
        return o.Existing ? game.Rules.HyperdriveModifier : 0;
    }

    /// <summary>
    /// Command kind at the character's post (the system or the fleet): a kind 1-3 held by another own
    /// character of the post (directly in the same system, or aboard the capital ships of the same fleet) is cleared, so each kind has
    /// one holder per post. Inferred (user observation of the original: the previous holder silently loses it).
    /// </summary>
    static void SetCommandKind(Character ch, int kind)
    {
        if (kind != 0)
        {
            var post = ch.Container is { } ship && Family.IsCapitalShip(ship.Type) && ship.Container is Fleet f
                ? f.Contents.Where(x => Family.IsCapitalShip(x.Type)).SelectMany(x => x.Contents)
                : ch.Container?.Contents ?? [];
            foreach (var other in post.OfType<Character>().Where(o => o != ch && o.Owner == ch.Owner && o.CommandKind == kind).ToList())
                other.CommandKind = 0;
        }
        ch.CommandKind = kind;
    }

    // ---- missions ----

    /// <summary>
    /// Mission creation (CreateMission / CreateMoveMission, one event for the mission manager): team = the targets (characters, special forces), decoys = the
    /// second list, type = Fields[2] (a MISSNSD record), UserID = Fields[0], ObjectiveKey = TargetKey = ArgRef; validation:
    /// <see cref="MissionEligibility.Validate"/>.
    /// </summary>
    static (List<Action<GameState>>, OrderResult) MissionEvents(GameState game, Command c, List<GameObject> targets)
    {
        uint type = c.Fields[2], userId = c.Fields[0];
        // pre-check slot (always present): team resolve, common location
        if (MissionEligibility.PreCheck(game, c.Side, c.Type, targets, c.Decoys) is { } pre) return ([], pre);
        // class validation
        if (targets.Count == 0 && c.Decoys.Count == 0) return ([], Refusal.NoLocation);
        if (targets.Count > 0 && targets[0].Destroyed) return ([], Refusal.Missing);
        if (type == 0) return ([], Refusal.NoMissionType); // the fixed "none" type (Inferred: SdRef 0)
        if (game.Definitions.Missions.All(m => m.Common.Ref != type)) return ([], Fail); // (64, 147) set, but returns 0, which gives (1, 1)
        var side = targets.Count > 0 ? targets[0].Owner : c.Side;
        if (side is not (Owner.Alliance or Owner.Empire)) return ([], Refusal.NotOwner);
        if (c.ArgRef is null or { Deleted: true }) return ([], Refusal.NoTargetLocation); // the side's view of the ArgRef (every object has one until deleted)
        // callback 0: the TargetKey (its own errors refuse); movement types with a foreign TargetKey (DestinationRefused); then Team and Decoys
        // placed together into one container at the TargetKey (side = the first member's owner, mode 3), giving the kept error, a
        // TargetKey that is no container gives (1, 1). Event fields (step 9): Header.ArgRef := the ArgRef if the chosen container is the
        // TargetKey, else the container; the event's container field is set to the container; the mission takes ObjectiveKey := Header.ArgRef, TargetKey := that field.
        var (key, keyError) = MissionEligibility.TargetKey(game, type, side, c.ArgRef);
        if (keyError is { } ke) return ([], ke);
        GameObject objective = c.ArgRef, targetKey = c.ArgRef; // no TargetKey known: no placement (Inferred)
        if (key is not null)
        {
            if (Family.Of(type) is >= Families.Mission and < Families.AgentMission && Knowledge.See(game, key, side).Owner != side) return ([], Refusal.DestinationRefused);
            var sandbox = new Fleets.Sandbox { Second = side };
            if (Fleets.PlaceGroup(game, key, [.. targets, .. c.Decoys], side, c.Type, out var error, sandbox) is not { } container) return ([], error);
            (objective, targetKey) = container == key ? (c.ArgRef, key) : (container, container);
        }
        // event check: the full validation chain
        if (MissionEligibility.Validate(game, c.Type, c.Side, type, targets, c.Decoys, c.ArgRef) is { } refused) return ([], refused);
        var team = targets.Cast<Agent>().ToList();
        var decoys = c.Decoys.Cast<Agent>().ToList();
        // event run: the chain again on the event fields, then creation; a failing
        // re-check creates nothing (no object, no CompletionStatus, no mission serial)
        return ([g =>
        {
            if (MissionEligibility.Validate(g, c.Type, c.Side, type, team, decoys, objective) is null)
                Missions.Create(g, type, c.Side, objective, team, decoys, userId, mandatory: false, targetKey: targetKey);
        }], OrderResult.Ok);
    }
}
