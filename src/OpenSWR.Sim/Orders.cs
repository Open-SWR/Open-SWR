using OpenSWR.Data.Dat;
namespace OpenSWR.Sim;

/// <summary>
/// A player or computer order (command object). Targets are (object, selection mode) pairs in list order. ArgRef is the
/// destination or location (null = none). Fields are the class fields as u32 (0x210-0x212: product type SdRef, count;
/// 0x240 / 0x241: A, B, mission type SdRef).
/// </summary>
public sealed class Command(int type, Owner side)
{
    public int Type { get; } = type;
    public Owner Side { get; } = side;
    public List<(GameObject Target, int Mode)> Targets { get; init; } = [];
    public GameObject? ArgRef { get; init; }
    public uint ArgInt { get; init; }
    public uint[] Fields { get; init; } = [];
    /// <summary>0x240 / 0x241: the second list, the mission's decoys.</summary>
    public List<GameObject> Decoys { get; init; } = [];
    /// <summary>Serial, set on successful execution.</summary>
    public uint Serial { get; set; }
    /// <summary>Controller sequence: outbox and pending-list order; 0 = unnumbered (tests, replays, immediate UI orders).</summary>
    public uint Sequence { get; set; }
    /// <summary>0x203 / 0x215: the name string (the command's string object); null = unnamed. No sim effect.</summary>
    public string? Text { get; init; }
}

/// <summary>Result record {category, code}; (1, -1) = success.</summary>
public readonly record struct OrderResult(int Category, int Code)
{
    public static readonly OrderResult Ok = new(1, -1);
    public bool Success => this == Ok;
}

/// <summary>
/// Selection (group) mode of a target node, one value per mode, never combined: Object (the object itself, AI), Picture (a
/// system's picture: the object itself), or one group of a system: Facilities (facilities / managers), Ground (troops, fighters,
/// characters on the planet), Fleets, Missions. The values are the original's command-stream values.
/// </summary>
public static class GroupMode
{
    public const int Object = 0, Picture = 1, Facilities = 4, Ground = 8, Fleets = 0x10, Missions = 0x40;
    /// <summary>The group a mode selects: the picture selects the object itself.</summary>
    public static int Group(int mode) => mode == Picture ? Object : mode;
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
    /// Execute (composites included): build events, validate, then serial and FIFO posts. 0x280 (speed message) is not a
    /// command-base class: it sets the side's ticks per day (ignored when 0) and takes no serial.
    /// </summary>
    public static OrderResult Execute(GameState game, Command c)
    {
        switch (c.Type)
        {
            case 1: // tick token: records the side's finished tick, no sim state, no serial
                return OrderResult.Ok;
            case 0x280:
                if (c.ArgInt != 0) game.Sides[c.Side].Speed = (int)c.ArgInt;
                return OrderResult.Ok;
            case 0x201 or 0x202:
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
    /// characters and special forces (0x30-0x3F) are candidates; the first one's location is the group location; every candidate
    /// there forms one 0x241 (mission type 0x41000001, A = B = 0), executed at once; until no candidate is left. Then all remaining
    /// targets form one 0x204 (none left: no sub-command, Inferred). All with the composite's ArgRef; the composite takes no
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

    /// <summary>The move composite's sub-commands, built once: the 0x241 groups, then the 0x204 of the rest.</summary>
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
            subs.Add(new Command(0x241, c.Side) { Targets = [.. group.Select(t => (t, 0))], ArgRef = c.ArgRef, Fields = [0, 0, 0x41000001] });
        }
        if (targets.Count > 0) subs.Add(new Command(0x204, c.Side) { Targets = [.. targets.Select(t => (t, 0))], ArgRef = c.ArgRef });
        return (subs, OrderResult.Ok);

        static bool IsCandidate(GameObject t) => Family.Of(t.Type) is >= 0x30 and < 0x40 && t.Completed;
    }

    /// <summary>
    /// Validation without execution (the controllers' pre-check before the outbox): the execute
    /// path's checks on the current state, no serial, no events. Composites: each sub-command's validation in order, the first
    /// error stands (0x201 / 0x202). Tick tokens and 0x280 pass.
    /// </summary>
    public static OrderResult Validate(GameState game, Command c)
    {
        if (c.Type is 1 or 0x280) return OrderResult.Ok;
        if (c.Type is not (0x201 or 0x202)) return BuildEvents(game, c).Result;
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
    /// fleet ArgRef its direct Existing contents of the fleet's owner (key order; its capital ships), else itself. Mode 0/1: the
    /// object itself. A system
    /// expands other modes by the mode rounded down to even and the command type over its direct Existing contents (mode-3 iterators) owned by the
    /// issuing side (Inferred: no further filter; run 2 day 5 excludes the side's placeholder fleets, facts vector): 4 to facilities (0x200) or its managers (0x214, 0x216); 8 to troops, fighters,
    /// then further groups (0x200: special forces, Inferred) / troops, fighters, characters (0x201, 0x202) / characters (0x240,
    /// 0x242); 0x10 to fleets; 0x40 to characters with a listed mission (the record's listed-mission flag clear, <see cref="ScreenViews.Listed"/>; Inferred reading); else refused (1, 0x12).
    /// </summary>
    static (List<GameObject> Targets, OrderResult Result) Resolve(Command c)
    {
        var set = new List<GameObject>();
        foreach (var (o, mode) in c.Targets)
        {
            if (o.Deleted) return ([], new(1, 0x12)); // missing key
            IEnumerable<GameObject> add;
            if (o is Fleet fleet) add = c.ArgRef is Fleet ? fleet.Contents.Where(x => x.Existing && x.Owner == fleet.Owner) : [o];
            else if (GroupMode.Group(mode) == GroupMode.Object) add = [o];
            else if (o is StarSystem s)
            {
                var own = s.Contents.Where(x => x.Owner == c.Side && x.Existing).ToList(); // mode-3 iterators: Existing (placeholder fleets are not)
                IEnumerable<GameObject> Of(uint lo, uint hi) => own.Where(x => Family.Of(x.Type) >= lo && Family.Of(x.Type) < hi);
                IEnumerable<GameObject>? expanded = (GroupMode.Group(mode), c.Type) switch
                {
                    (GroupMode.Facilities, 0x200) => Of(0x20, 0x30),
                    (GroupMode.Facilities, 0x214 or 0x216) => s.Managers,
                    (GroupMode.Ground, 0x200) => Of(0x10, 0x14).Concat(Of(0x1C, 0x20)).Concat(Of(0x3C, 0x40)),
                    (GroupMode.Ground, 0x201 or 0x202) => Of(0x10, 0x14).Concat(Of(0x1C, 0x20)).Concat(Of(0x30, 0x3C)),
                    (GroupMode.Ground, 0x240 or 0x242) => Of(0x30, 0x3C),
                    (GroupMode.Fleets, _) => Of(0x08, 0x10),
                    (GroupMode.Missions, _) => Of(0x30, 0x3C).Where(x => x is Character { Mission: { } m } && ScreenViews.Listed(m)),
                    _ => null,
                };
                if (expanded is null) return ([], new(1, 0x12));
                add = expanded;
            }
            else return ([], new(1, 0x12));
            foreach (var x in add)
                if (!set.Contains(x)) set.Add(x);
        }
        return (set, OrderResult.Ok);
    }

    /// <summary>
    /// Generic object order check: owner not in {1, 2} or != issuing side (1, 0x14); not Completed unless the type is
    /// 0x203, 0x204, 0x215 or 0x241 (1, 0x10); Destroyed (1, 0x12); Enroute (1, 0x11). The class-accepts-the-type test (1, 1) is
    /// not modelled.
    /// </summary>
    static OrderResult Check(Command c, GameObject o) =>
        o.Owner is not (Owner.Alliance or Owner.Empire) || o.Owner != c.Side ? new(1, 0x14)
        : !o.Completed && c.Type is not (0x203 or 0x204 or 0x215 or 0x241) ? new(1, 0x10)
        : o.Destroyed ? new(1, 0x12)
        : o.Enroute ? new(1, 0x11)
        : OrderResult.Ok;

    static readonly OrderResult Fail = new(1, 1);

    // ---- check pipeline (per family) ----

    /// <summary>
    /// Command check pipeline: pre-check (target expansion, empty (1, 0x16), per event the
    /// pre-check slot then the single-owner test (1, 0x14), own pre-check parts), class validation, event checks; the first error
    /// stands, a sub-step returning 0 gives (1, 1). The serial is taken by the caller only on success. Missions: <see cref="MissionEvents"/>.
    /// </summary>
    static (List<Action<GameState>> Events, OrderResult Result) BuildEvents(GameState game, Command c)
    {
        var (targets, result) = Resolve(c);
        if (!result.Success) return ([], result);
        if (c.Type is 0x240 or 0x241) return MissionEvents(game, c, targets);
        if (targets.Count == 0) return ([], new(1, 0x16));
        // 1. pre-check over the event list: the pre-check slot, (missing-only lookup: never fails here), single owner
        foreach (var t in targets)
        {
            if (PreSlot(game, c, t) is { Success: false } slot) return ([], slot);
            if (t.Owner != targets[0].Owner) return ([], new(1, 0x14));
        }
        bool combat = c.Type is 0x234 or (>= 0x220 and <= 0x223);
        if (c.Type == 0x203 && targets.Count > 1) return ([], new(1, 0x15)); // own pre-check
        if (combat)
        {
            // own pre-checks: the prepared system event list: system direct (1, 0x12), then the
            // system check; then the assault pre-check (8, 7) / (8, 5) or the bombard fleet scan (8, 6) / (8, 8)
            if (c.ArgRef is null || c.ArgRef.Destroyed) return ([], new(1, 0x12));
            if (c.ArgRef is not StarSystem) return ([], Fail);
            if (Combat.SystemCheck(game, c) is { Success: false } systemFail) return ([], systemFail);
            var check = c.Type == 0x234 ? Combat.AssaultPreCheck(targets) : Combat.BombardCheck(game, c, targets);
            if (!check.Success) return ([], check);
        }
        // 2. class validation, 3. event checks
        if (c.Type is >= 0x210 and <= 0x212) return ProductionEvents(game, c, targets);
        if (c.Type == 0x214) return TargetEvents(game, c, targets);
        var destinations = new List<GameObject?>();
        if (c.Type is 0x204 or 0x270)
        {
            if (c.Type == 0x204 && MoveCheck(c, targets) is { Success: false } refused) return ([], refused);
            if (c.ArgRef is null or { Deleted: true }) return ([], Fail); // 0x270: no resolve; the sandbox load fails
            var sandbox = new Fleets.Sandbox { Second = targets[0].Owner };
            foreach (var t in targets)
            {
                if (t.Container is { } from) sandbox.Commit(from, t.Type, -1);
                if (Fleets.Place(game, c.ArgRef, t.Type, c.Side, c.Type, out var error, sandbox, t) is not { } destination) return ([], error);
                destinations.Add(destination);
            }
            // Not modelled: the 0x204 wrapper's rewrite of (1, 0x25) and (1, 0x27) to (1, 0x26) (command info set, Inferred meaning).
            for (int i = 0; i < targets.Count; i++)
                if (MoveEventCheck(game, c, targets[i], destinations[i]!) is { Success: false } fail) return ([], fail);
        }
        // 0x250 recall: after the base check, phase 4 gives (0x40, 0x40), phase not 8 gives (0x40, 0x41)
        if (c.Type == 0x250)
            foreach (var t in targets)
                if (t is not Mission rm) return ([], Fail);
                else if (rm.Phase == 4) return ([], new(0x40, 0x40));
                else if (rm.Phase != 8) return ([], new(0x40, 0x41));
        // 0x216: if any target manager is Reserved, every event clears, else every event sets (a toggle over the selection);
        // read when the events are built, here at execution (Inferred: validation or execution not checked)
        bool reserve = c.Type == 0x216 && !targets.Any(t => ((ManufacturingManager)t).Reserved);
        // event checks of the other families repeat their pre-check slots on unchanged state and pass
        var events = new List<Action<GameState>>();
        for (int i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var destination = destinations.Count > 0 ? destinations[i] : null;
            events.Add(c.Type switch
            {
                0x200 => g => Objects.Destroy(g, t, 0x14),                       // scrap
                0x204 or 0x270 => g => Fleets.Move(g, t, destination!),
                0x203 => g => g.ViewMessage(0x500, t),                             // the views' Name message
                0x213 => g => { foreach (var u in ((ManufacturingManager)t).Orders.ToList()) Objects.Destroy(g, u, 0x16); },
                // Command kind = type - 0x260, applied at the character's post (<see cref="SetCommandKind"/>)
                >= 0x260 and <= 0x263 => g => { if (t is Character ch) SetCommandKind(ch, c.Type - 0x260); },
                0x234 => g => ((Fleet)t).Assault = true,                        // event 0x221
                >= 0x220 and <= 0x223 => g => ((Fleet)t).Bombard = true,        // event 0x220
                // 0x216: Reserved := payload; the side-view copy needs nothing here (live views
                // read the real value, Knowledge.Reserved)
                0x216 => g => ((ManufacturingManager)t).Reserved = reserve,
                // 0x205 run: the command side's view of the target acknowledges
                0x205 => g => Objects.Acknowledge(g, t, c.Side),
                // 0x203 rename, 0x215 name: no sim effect.
                // 0x250 recall: in phase 8 CompletionStatus := 2, set through a virtual call
                0x250 => g => { if (t is Mission { Phase: 8 } rm) Missions.SetCompletion(g, rm, 2); },
                // 0x242, 0x268: effects deferred, not modelled.
                _ => _ => { },
            });
        }
        // 0x234 / 0x220-0x223: the prepared system event (0x225 assault, 0x224 bombardment) goes to the FIFO after the fleet events
        if (combat)
        {
            var s = (StarSystem)c.ArgRef!;
            events.Add(c.Type == 0x234 ? g => Combat.Assault(g, s) : g => Combat.Bombard(g, s, c.Type));
        }
        return (events, OrderResult.Ok);
    }

    /// <summary>
    /// Event pre-check slot per family: every slot resolves its target direct (missing or Destroyed
    /// (1, 0x12)) except 0x205 (no check). 0x200: the generic check. 0x203: fleets and capital ships get the generic check,
    /// characters / special forces pass, others (1, 0x17). 0x204 / 0x270: fleets, capital ships, fighters, troops, the
    /// headquarters: the generic check; characters / special forces: pass; others (1, 0x17)), then the headquarters blockade rule.
    /// 0x210-0x212, 0x216: manager resolver (another class to (1, 1)), the generic check, a non-Suspended member (0xA0, 3). 0x213: manager
    /// resolver, the generic check, a non-empty queue (0xA0, 1). 0x214: manager resolver, the generic check. Combat fleets: fleet
    /// resolver (another family to (1, 1)), the generic check, a Completed non-Enroute capital ship ((1, 0x10) / (1, 0x11) /
    /// (8, 1)); 0x234 also the Blockade flag (8, 7).
    /// Other types (Inferred): the generic check.
    /// </summary>
    static OrderResult PreSlot(GameState game, Command c, GameObject t)
    {
        if (c.Type == 0x205) return OrderResult.Ok;
        bool combat = c.Type is 0x234 or (>= 0x220 and <= 0x223);
        bool manager = c.Type is (>= 0x210 and <= 0x214) or 0x216;
        if (combat && t is not Fleet || manager && t is not ManufacturingManager) return Fail; // resolver: wrong family
        if (t.Destroyed) return new(1, 0x12);
        uint f = Family.Of(t.Type);
        switch (c.Type)
        {
            case 0x203: // fleets / capital ships the generic check, characters / special forces pass, others (1, 0x17)
                return t is Fleet || Family.IsCapitalShip(t.Type) ? Check(c, t) : f is >= 0x30 and < 0x40 ? OrderResult.Ok : new(1, 0x17);
            case 0x204 or 0x270:
                if (MovedByGenericCheck(f) && Check(c, t) is { Success: false } fail) return fail;
                if (!MovedByGenericCheck(f) && f is not (>= 0x30 and < 0x40)) return new(1, 0x17);
                return HeadquartersBlockade(game, t);
            case 0x213: // the generic check, then a non-empty queue (0xA0, 1)
                if (Check(c, t) is { Success: false } cancelFail) return cancelFail;
                return ((ManufacturingManager)t).Orders.Count > 0 ? OrderResult.Ok : new(0xA0, 1);
            case (>= 0x210 and <= 0x212) or 0x216: // 0x216: the event slot runs the same checks
                if (Check(c, t) is { Success: false } managerFail) return managerFail;
                return Economy.Members((ManufacturingManager)t).Any(x => !x.Suspended) ? OrderResult.Ok : new(0xA0, 3);
            case 0x234:
                return Check(c, t) is { Success: false } fleetFail ? fleetFail : Combat.AssaultFleetCheck(t);
            case >= 0x220 and <= 0x223:
                return Check(c, t) is { Success: false } bombardFail ? bombardFail : Combat.ShipCheck(t);
            default:
                return Check(c, t);
        }
    }

    /// <summary>The generic check: fleets, troops, capital ships, fighters and the headquarters (family 0x20).</summary>
    static bool MovedByGenericCheck(uint family) => family is >= 0x08 and < 0x20 or 0x20;

    /// <summary>
    /// Move pre-check part: an object that can travel, of families [0x20, 0x22) (headquarters), in
    /// a system with Blockade gives (0x90, 4) when withdraw(S, F) is less than 31 (S, F as for WithdrawPercent).
    /// </summary>
    internal static OrderResult HeadquartersBlockade(GameState game, GameObject t) =>
        Family.Of(t.Type) is >= 0x20 and < 0x22 && Fleets.CanTravel(t) && t.System is { Blockade: true } s && Combat.Withdraw(game, s) < 31
            ? new(0x90, 4) : OrderResult.Ok;

    /// <summary>
    /// Move validation: side = the first target's owner; the destination missing-only (1, 0x22) (a Destroyed one
    /// passes and meets container acceptance); a destination of another owner is refused (1, 0x28) unless all targets are fleets
    /// / capital ships, or all are troops and the destination is a system with Existing and without Populated (Inferred reading).
    /// The sandbox move per target is in BuildEvents.
    /// </summary>
    static OrderResult MoveCheck(Command c, List<GameObject> targets)
    {
        if (c.ArgRef is not { Deleted: false } d) return new(1, 0x22); // missing-only: a deleted key
        if (d.Owner == targets[0].Owner) return OrderResult.Ok;
        bool ships = targets.All(t => Family.Of(t.Type) is >= 0x08 and < 0x10 || Family.IsCapitalShip(t.Type));
        bool troops = targets.All(t => Family.IsTroop(t.Type)) && d is StarSystem { Existing: true, Populated: false };
        return ships || troops ? OrderResult.Ok : new(1, 0x28);
    }

    /// <summary>
    /// Move event check: target direct (1, 0x12); target class check (fleets, capital ships, fighters, troops, characters,
    /// special forces: the generic check; others (1, 0x17)); a fresh sandbox (side = the target's owner) re-places the target
    /// directly into the chosen container to its acceptance error; for an object that can travel, to another system: speed 0 gives
    /// (1, 0x18), a troop whose container's owner differs from its own gives (1, 0x28).
    /// </summary>
    static OrderResult MoveEventCheck(GameState game, Command c, GameObject t, GameObject destination)
    {
        if (t.Destroyed) return new(1, 0x12);
        uint family = Family.Of(t.Type);
        if (!MovedByGenericCheck(family) && family is not (>= 0x30 and < 0x40)) return new(1, 0x17);
        if (Check(c, t) is { Success: false } fail) return fail;
        var sandbox = new Fleets.Sandbox { Second = t.Owner };
        if (t.Container is { } from) sandbox.Commit(from, t.Type, -1);
        if (Fleets.Accept(game, destination, t.Type, t.Owner, c.Type, sandbox, t) is { Success: false } placement) return placement;
        if (Fleets.CanTravel(t) && Missions.Location(t) != Missions.Location(destination))
        {
            if (Speed(game, t) == 0) return new(1, 0x18);
            if (Family.IsTroop(t.Type) && destination.Owner != t.Owner) return new(1, 0x28);
        }
        return OrderResult.Ok;
    }

    // ---- production ----

    /// <summary>Accepted manager kind per production type: 0x210 construction, 0x211 shipyard, 0x212 training.</summary>
    static ManufacturingKind KindFor(int type) => type switch { 0x210 => ManufacturingKind.Construction, 0x211 => ManufacturingKind.Ship, _ => ManufacturingKind.Training };

    /// <summary>
    /// Validation (after the pre-check slots): per manager (class refusing the type to (1, 1)) its TargetKey
    /// missing-only (never: TargetKey is always set; a Destroyed one meets container acceptance) and, if owned by another side,
    /// only families [0x14, 0x1C) (1, 0x28); product check (unresolvable product of [0x10, 0x40) gives (1, 1); MaintenanceCost *
    /// count > free gives (0xF3, 1)); count split over F = sum members (suspended included): q = count div F, r = count mod F,
    /// n_m = members(m)*q + min(members(m), r), r -= min(members(m), r); per manager its queue leaves the sandbox and each node
    /// is placed by the solver at TargetKey, which gives the kept placement error. Event checks: the slot again (unchanged
    /// state, passes), the second sandbox (equal sides, not modelled), maintenance of the event's units (0xF3, 1).
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
            if (m.TargetKey.Deleted) return ([], new(1, 0x22)); // TargetKey missing-only
            if (m.TargetKey.Owner != m.Owner && !Family.IsCapitalShip(product)) return ([], new(1, 0x28));
            managers.Add(m);
        }
        if (Family.Of(product) is >= 0x10 and < 0x40 && game.Definitions.Resolve(product) is null) return ([], Fail);
        if (game.Sides.TryGetValue(managers[0].Owner, out var owner)
            && game.MaintenanceCost(product) * count > owner.MaintenanceCapacity - owner.MaintenanceAllocated) return ([], new(0xF3, 1));
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
                // per-node placement first runs a check: a product in no placement category gives (1, 0x19)
                if (Fleets.Category(product) < 0) return ([], new(1, 0x19));
                if (Fleets.Place(game, m.TargetKey, product, c.Side, c.Type, out var error, sandbox) is not { } parent) return ([], error);
                parents.Add(parent);
            }
            events.Add(g => Production(g, m, product, parents));
            counts.Add(n);
        }
        for (int i = 0; i < managers.Count; i++)
        {
            var side = game.Sides[managers[i].Owner];
            if (game.MaintenanceCost(product) * counts[i] > side.MaintenanceCapacity - side.MaintenanceAllocated) return ([], new(0xF3, 1));
        }
        return (events, OrderResult.Ok);
    }

    /// <summary>
    /// Production target 0x214, validation (after the pre-check slots: manager direct, the generic check): the
    /// destination missing-only (1, 0x22); a destination of another owner with any queued unit that is not a capital ship
    /// [0x14, 0x1C) gives (1, 0x28) (empty queues pass). One sandbox, per manager in list order: empty queue - the destination direct
    /// (Destroyed (1, 0x22)), then construction: owner != command side (1, 0x24), != the managers' owner (1, 0x28), not Completed
    /// (1, 0x20), Enroute (1, 0x21), not a system (1, 0x25); shipyard / training: not Completed (1, 0x20), Enroute (1, 0x21), not a
    /// system / fleet / capital ship (1, 0x25). Non-empty queue: the queue leaves the sandbox, then each queued unit (queue order)
    /// gets its own solver container under 0x214, giving the kept placement error. Event checks repeat the slot (passes); not modelled:
    /// the side's view of the manager (unreachable after the generic check) and the second sandbox (equal sides).
    /// </summary>
    static (List<Action<GameState>>, OrderResult) TargetEvents(GameState game, Command c, List<GameObject> targets)
    {
        var managers = targets.Cast<ManufacturingManager>().ToList();
        var side = managers[0].Owner;
        if (c.ArgRef is not { Deleted: false } d) return ([], new(1, 0x22)); // missing-only: a deleted key
        if (d.Owner != side && managers.Any(m => m.Orders.Any(u => !Family.IsCapitalShip(u.Type)))) return ([], new(1, 0x28));
        var sandbox = new Fleets.Sandbox { Second = side }; // first side = the command's side
        var events = new List<Action<GameState>>();
        foreach (var m in managers)
        {
            var nodes = new List<(GameObject Unit, GameObject Container)>();
            if (m.Orders.Count == 0)
            {
                bool system = d is StarSystem;
                var refused = d.Destroyed ? new OrderResult(1, 0x22)
                    : m.Kind == ManufacturingKind.Construction
                    ? d.Owner != c.Side ? new OrderResult(1, 0x24)
                    : d.Owner != side ? new(1, 0x28)
                    : !d.Completed ? new(1, 0x20)
                    : d.Enroute ? new(1, 0x21)
                    : !system ? new(1, 0x25) : OrderResult.Ok
                    : !d.Completed ? new OrderResult(1, 0x20)
                    : d.Enroute ? new(1, 0x21)
                    : !(system || d is Fleet || Family.IsCapitalShip(d.Type)) ? new(1, 0x25) : OrderResult.Ok;
                if (!refused.Success) return ([], refused);
            }
            else
            {
                foreach (var unit in m.Orders) if (unit.Container is { } at) sandbox.Commit(at, unit.Type, -1);
                foreach (var unit in m.Orders)
                {
                    if (Fleets.Place(game, d, unit.Type, c.Side, 0x214, out var error, sandbox, unit) is not { } container) return ([], error);
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
    /// Production event: cancel the queue (every queued unit DestroyedReason 0x16, no refund: not Completed), then
    /// create the units node by node (owner = manager's owner, Created 1, Completed 0) in each node's parent, BuilderKey := manager.
    /// A production order replaces the queue.
    /// </summary>
    static void Production(GameState game, ManufacturingManager m, uint product, List<GameObject> parents)
    {
        foreach (var unit in m.Orders.ToList()) Objects.Destroy(game, unit, 0x16);
        foreach (var parent in parents) Objects.Create(game, product, m.Owner, parent, completed: false).BuilderKey = m;
    }

    // ---- travel time ----

    /// <summary>
    /// Days: dist = isqrt'(dx^2 + dy^2) over the systems' SD X/Y; t = ((dist div G 0x1400) * speed)
    /// div 100; dist != 0 and t = 0 to 1.
    /// </summary>
    public static int TravelDays(GameState game, StarSystem from, StarSystem to, int speed)
    {
        int dist = Distance(from, to);
        int t = dist / game.Params.General(0x1400) * speed / 100;
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
    /// modifiers 0, so Hyperdrive, else HyperdriveIfDamaged, else 100 (0 unless Existing). Characters: MissionHyperdriveModifier (initial G 0x1) if Existing,
    /// else 0. Fleets: over their Completed, not EnrouteActive capital ships in key order, the first sets v, a faster one or speed
    /// 0 replaces it, 0 sticks; no ship to 0. Others: G 0x1 if Existing, else 0.
    /// </summary>
    public static int Speed(GameState game, GameObject o)
    {
        uint f = Family.Of(o.Type);
        if (f is >= 0x14 and < 0x20)
        {
            bool fighter = f >= 0x1C;
            if (!o.Existing) return 0; // fighters too
            var r = game.Definitions.Resolve(o.Type)!;
            int primary = r.Get(SdField.Hyperdrive), backup = r.Get(SdField.HyperdriveIfDamaged);
            int v = primary - (fighter ? 0 : primary * o.Critical(4));
            if (v == 0) v = backup - (fighter ? 0 : backup * o.Critical(5));
            return v == 0 ? 100 : v;
        }
        if (o is Character c) return c.Existing ? c.MissionHyperdriveModifier ?? game.Params.General(0x1) : 0;
        if (f is >= 0x08 and < 0x10)
        {
            int? v = null;
            foreach (var ship in o.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Completed && !x.EnrouteActive))
            {
                int s = Speed(game, ship);
                if (v is null || (v != 0 && (s > v || s == 0))) v = s;
            }
            return v ?? 0;
        }
        return o.Existing ? game.Params.General(0x1) : 0;
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
    /// Mission creation (0x240 / 0x241, one event for the mission manager): team = the targets (characters, special forces), decoys = the
    /// second list, type = Fields[2] (a MISSNSD record), UserID = Fields[0], ObjectiveKey = TargetKey = ArgRef; validation:
    /// <see cref="MissionEligibility.Validate"/>.
    /// </summary>
    static (List<Action<GameState>>, OrderResult) MissionEvents(GameState game, Command c, List<GameObject> targets)
    {
        uint type = c.Fields[2], userId = c.Fields[0];
        // pre-check slot (always present): team resolve, common location
        if (MissionEligibility.PreCheck(game, c.Side, c.Type, targets, c.Decoys) is { } pre) return ([], pre);
        // class validation
        if (targets.Count == 0 && c.Decoys.Count == 0) return ([], new(1, 0x16));
        if (targets.Count > 0 && targets[0].Destroyed) return ([], new(1, 0x12));
        if (type == 0) return ([], new(0x40, 0x92)); // the fixed "none" type (Inferred: SdRef 0)
        if (game.Definitions.Missions.All(m => m.Common.Ref != type)) return ([], Fail); // (0x40, 0x93) set, but returns 0, which gives (1, 1)
        var side = targets.Count > 0 ? targets[0].Owner : c.Side;
        if (side is not (Owner.Alliance or Owner.Empire)) return ([], new(1, 0x14));
        if (c.ArgRef is null or { Deleted: true }) return ([], new(0x40, 0x20)); // the side's view of the ArgRef (every object has one until deleted)
        // callback 0: the TargetKey (its own errors refuse); movement types with a foreign TargetKey (1, 0x28); then Team and Decoys
        // placed together into one container at the TargetKey (side = the first member's owner, mode 3), giving the kept error, a
        // TargetKey that is no container gives (1, 1). Event fields (step 9): Header.ArgRef := the ArgRef if the chosen container is the
        // TargetKey, else the container; the event's container field is set to the container; the mission takes ObjectiveKey := Header.ArgRef, TargetKey := that field.
        var (key, keyError) = MissionEligibility.TargetKey(game, type, side, c.ArgRef);
        if (keyError is { } ke) return ([], ke);
        GameObject objective = c.ArgRef, targetKey = c.ArgRef; // no TargetKey known: no placement (Inferred)
        if (key is not null)
        {
            if (Family.Of(type) is >= 0x40 and < 0x50 && Knowledge.See(game, key, side).Owner != side) return ([], new(1, 0x28));
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
