using OpenSWR.Data;

namespace OpenSWR.Sim;

/// <summary>Tip triggers: each unlocks one topic kind the first time it fires.</summary>
public enum AdvisorTip { GoTo, Manufacturing, Fleet, Planet, Missions }

/// <summary>
/// The human player's advisor: request slots with expiry and
/// cooldown, option and request flags, the gate, the post-action command, the refusal memory and the counters. UI-side runtime: the
/// Sim never runs it; MainScreen builds one for the human
/// controller and calls <see cref="Tick"/> once per frame. Every draw goes through <see cref="Rand"/> (default the shared UI
/// generator). Time = the strategic clock's tick counter (<see cref="Now"/>).
/// </summary>
public sealed class Advisor
{
    /// <summary>Delegation records: 0x14 Manage Production, 0x15 Manage Garrisons, 0x17 build helper.</summary>
    public const int Production = 0x14, Garrisons = 0x15, Helper = 0x17;

    /// <summary>Record state: 0 idle / enabled, 1 has work, 2 disabled.</summary>
    public const int Disabled = 2;

    readonly int[] _due, _cooldown;
    (int Category, int Code)? _pair;
    bool _repeated;
    int _lastPlay, _lastPlayIdle;
    bool _briefingFallback;

    /// <param name="side">The human side: Alliance gives C-3PO (88 slots), Empire gives IMP-22 (86 slots).</param>
    /// <param name="now">The strategic clock's tick counter (<see cref="Clock.Tick"/>).</param>
    /// <param name="briefing">
    /// New game with the briefing DLL (module 13) present: pre-request action 41 / 74 at the absolute tick 40000. New game
    /// without it: the openswr adaptation posts (2, 0xD) once at the first gate open (Inferred). Null = a loaded game
    /// without saved advisor state: no briefing at all.
    /// </param>
    /// <param name="tips">Agent Advice on (<see cref="TipsOff"/> clear): default only for settings column 1 or 4.</param>
    public Advisor(Owner side, Func<int> now, bool? briefing, bool tips, Func<int, int>? rand = null)
    {
        Side = side;
        Now = now;
        Rand = rand ?? (n => UiRandom.Shared.Rand(n));
        _due = new int[Slots];
        _cooldown = new int[Slots];
        Translate = true; // on by default (both ctors)
        TipsOff = !tips;
        if (briefing == true) _due[Imp ? 74 : 41] = 40000;
        _briefingFallback = briefing == false;
    }

    public Owner Side { get; }
    public bool Imp => Side == Owner.Empire;
    /// <summary>Request slots: C-3PO 88 (ids 1-87), IMP-22 86 (ids 1-85).</summary>
    public int Slots => Imp ? 86 : 88;
    public Func<int> Now { get; set; }
    /// <summary>
    /// Clock of the idle-chatter test; null = <see cref="Now"/>. openswr: the screen passes real time in 250 ms ticks, so
    /// the advisor also chatters while the game is paused (the original counts game ticks and starts a game running).
    /// </summary>
    public Func<int>? IdleNow { get; set; }
    public Func<int, int> Rand { get; set; }

    // option and request flags; the status rules take the request flags in this order: Hidden, Shown, SkipBriefing, HelperDone,
    // ManageGarrisonsOn, ManageProductionOn, ManageGarrisonsOff, ManageProductionOff, ToggledOften, MenuOpened, Idle
    /// <summary>The build helper's watched Build command was accepted.</summary>
    public bool HelperDone { get; private set; }
    public bool ManageGarrisonsOn { get; private set; }
    public bool ManageProductionOn { get; private set; }
    public bool ManageGarrisonsOff { get; private set; }
    public bool ManageProductionOff { get; private set; }
    /// <summary>Translate Counterpart (commentary steps play).</summary>
    public bool Translate { get; private set; }
    public bool SkipBriefing { get; private set; }
    public bool MessageWindowOpen { get; private set; }
    /// <summary>Agent Advice off.</summary>
    public bool TipsOff { get; private set; }
    /// <summary>A delegation toggle was enabled with the toggle counter above 3.</summary>
    public bool ToggledOften { get; private set; }
    public bool MenuOpened { get; private set; }
    /// <summary>Advisor shown / hidden requests (no setter in this build).</summary>
    public bool Shown { get; private set; }
    public bool Hidden { get; private set; }
    /// <summary>The production window holds the build helper's command (<see cref="Watched"/>).</summary>
    public bool Watching { get; private set; }
    /// <summary>More than 50 ticks passed since the last action when the gate opened.</summary>
    public bool Idle { get; private set; }
    /// <summary>The "may speak" gate, opened by the main display's idle completion.</summary>
    public bool Gate { get; private set; }
    /// <summary>Set by the secondary display's idle completion.</summary>
    public int SecondaryIdle { get; set; }
    /// <summary>Pending post-action command.</summary>
    public (int P1, int P2)? PostCommand { get; private set; }
    /// <summary>Menu-click counter, delegation toggle counter, music counter.</summary>
    public int MenuClicks { get; private set; }
    public int ToggleCounter { get; private set; }
    public int MusicCounter { get; private set; }
    /// <summary>Mood byte, recomputed at every DayDue; read only by the music pick.</summary>
    public int Mood { get; internal set; } = 8;
    /// <summary>
    /// Model totals i = 2 / 3: the systems whose player view the player / the other side owns, from
    /// the cycle's model update (state 5, every cycle whatever the toggles). Zero until the first cycle (model ctor).
    /// </summary>
    public int StandingOwn { get; private set; }
    public int StandingEnemy { get; private set; }
    /// <summary>Main-screen state word set by the game-over status mask (Inferred meaning: the game-over UI state).</summary>
    public bool GameOverUi { get; private set; }
    /// <summary>Record states of 0x14 / 0x15 / 0x17; all start disabled.</summary>
    public Dictionary<int, int> Records { get; } = new() { [Production] = Disabled, [Garrisons] = Disabled, [Helper] = Disabled };

    /// <summary>Play action <c>id</c> of the side's script.</summary>
    public Action<int>? Play { get; set; }
    /// <summary>A post-action command (P1 >= 2) runs: (2, n) guided UI step, (3, x) the side object's view.</summary>
    public Action<int, int>? Post { get; set; }
    /// <summary>Flush both displays.</summary>
    public Action? Flush { get; set; }

    /// <summary>A due request (0 = none) per slot id.</summary>
    public int Due(int id) => _due[id];
    public bool Pending(int id) => _due[id] != 0;

    /// <summary>Request: due[id] := now + validity.</summary>
    public void Request(int id, int validity)
    {
        if (id > 0 && id < Slots) _due[id] = Now() + validity;
    }

    /// <summary>Whether a step plays: a commentary step needs Translate Counterpart on and the message window closed.</summary>
    public bool Plays(bool commentary) => !commentary || Translate && !MessageWindowOpen;

    /// <summary>
    /// Advisor tick: a pending post command runs and ends the tick; with the gate open the slots are
    /// scanned in ascending id (expired requests cleared; the first due one is cleared and plays unless on cooldown, a cooled one
    /// is lost and the scan goes on); if nothing started, the status rules.
    /// </summary>
    public void Tick()
    {
        int now = Now();
        if (PostCommand is { } p)
        {
            PostCommand = null;
            if (p.P1 >= 2) Post?.Invoke(p.P1, p.P2);
            return;
        }
        if (Gate)
            for (int id = 1; id < Slots; id++)
            {
                int due = _due[id];
                if (due == 0) continue;
                _due[id] = 0;
                if (due < now) continue;
                if (_cooldown[id] >= now) continue; // on cooldown: the request is lost
                _cooldown[id] = now + 60;
                _lastPlay = now;
                _lastPlayIdle = IdleNow?.Invoke() ?? now;
                Play?.Invoke(id);
                return;
            }
        Rules(now);
    }

    /// <summary>The type-17 still's finish posts its (Param1, Param2); the next tick runs it.</summary>
    public void Posted(int p1, int p2) => PostCommand = (p1, p2);

    /// <summary>
    /// Main display idle completion: the gate opens; more than 50 ticks since the last action sets <see cref="Idle"/>. The
    /// first opening of a new game without the briefing DLL posts the briefing's last step (2, 0xD).
    /// </summary>
    public void GateOpen()
    {
        Gate = true;
        if (IdleNow is { } idle ? idle() > _lastPlayIdle + 50 : Now() > _lastPlay + 50) Idle = true;
        if (_briefingFallback)
        {
            _briefingFallback = false;
            PostCommand = (2, 0xD);
        }
    }

    /// <summary>Status step: closes the gate, then the first set request flag (in table order) is cleared and raises its request.</summary>
    void Rules(int now)
    {
        Gate = false;
        if (Hidden)
        {
            Hidden = false;
            Request(Imp ? 27 : 34, Imp ? 50 : 5);
        }
        else if (Shown)
        {
            Shown = false;
            Request(Imp ? 26 : 33, Imp ? 10 : 40000);
        }
        else if (SkipBriefing)
        {
            SkipBriefing = false;
            Request(Imp ? 73 : 40, 40000);
            Flush?.Invoke();
        }
        else if (HelperDone)
        {
            HelperDone = false;
            Request(Imp ? 28 : 35, 10);
        }
        else if (ManageGarrisonsOn)
        {
            ManageGarrisonsOn = false;
            Request(Imp ? 29 : 36, 10);
        }
        else if (ManageProductionOn)
        {
            ManageProductionOn = false;
            Request(Imp ? 31 : 38, 10);
        }
        else if (ManageGarrisonsOff)
        {
            ManageGarrisonsOff = false;
            Request(Imp ? 30 : 37, 50);
        }
        else if (ManageProductionOff)
        {
            ManageProductionOff = false;
            Request(Imp ? 32 : 39, 50);
        }
        else if (!Imp && ToggledOften)
        {
            ToggledOften = false;
            Request(79, 5);
        }
        else if (MenuOpened)
        {
            MenuOpened = false;
            int lo = Imp ? 75 : 76, hi = lo + 2, slot = MenuClicks + lo - 2;
            if (slot > hi) (slot, MenuClicks) = (hi, 0);
            Request(Math.Max(slot, lo), 5);
        }
        else if (Idle)
        {
            Idle = false;
            int r = Rand(12);
            if (r < 6) Request((Imp ? 78 : 80) + r, Imp ? 5 : 40000);
            Gate = true;
        }
    }

    // ---- request sources ----

    // Code maps to (C-3PO id, IMP id, C validity, IMP validity); "a / b" validity cells read as C-3PO / IMP (Inferred: the
    // validity lives in the per-advisor handler, the message carries only the code)
    static readonly Dictionary<int, (int C, int I, int Cv, int Iv)> Codes = new()
    {
        [1] = (52, 48, 10, 10), [2] = (53, 49, 10, 10), [3] = (56, 52, 5, 10), [4] = (58, 54, 10, 10), [5] = (59, 55, 10, 10),
        [6] = (61, 57, 10, 10), [8] = (60, 56, 10, 10), [9] = (62, 58, 10, 10), [0x0C] = (57, 53, 10, 10), [0x0D] = (65, 61, 10, 10),
        [0x0E] = (67, 62, 10, 5), [0x14] = (42, 64, 10, 10), [0x15] = (66, 63, 10, 10), [0x16] = (45, 64, 40000, 40000),
        [0x17] = (43, 64, 40000, 40000), [0x18] = (44, 64, 40000, 40000), [0x19] = (46, 64, 40000, 40000), [0x1A] = (42, 34, 40000, 40000),
        [0x1B] = (42, 33, 40000, 40000), [0x1C] = (55, 51, 10, 40000), [0x1E] = (28, 42, 40000, 40000), [0x1F] = (27, 41, 40000, 40000),
        [0x20] = (29, 43, 40000, 40000), [0x21] = (30, 44, 40000, 40000), [0x22] = (32, 46, 40000, 40000), [0x23] = (31, 45, 40000, 40000),
        [0x24] = (51, 47, 40000, 40000), [0x25] = (48, 38, 40000, 40000), [0x26] = (47, 37, 40000, 40000), [0x27] = (50, 40, 40000, 40000),
        [0x28] = (49, 39, 40000, 40000), [0x29] = (68, 65, 50, 50), [0x2C] = (75, 0, 10, 0), [0x2D] = (0, 72, 0, 10),
        [0x2E] = (25, 35, 10, 10), [0x2F] = (26, 36, 10, 10),
    };

    /// <summary>A message added for the player with advisor code <paramref name="code"/>.</summary>
    public void MessageCode(int code)
    {
        switch (code)
        {
            case 0x2A: // tip: 69-72 / 66-69 by rand(4), only when none of the four is pending (IMP guard Inferred)
                int first = Imp ? 66 : 69;
                if (Enumerable.Range(first, 4).Any(Pending)) return;
                int r = Rand(4);
                if (r < 4) Request(first + r, 10); // r = 4 (rare out-of-range result): nothing (Inferred)
                return;
            case 0x2B: // no setter in this build
                int s = Rand(2);
                if (s < 2) Request((Imp ? 70 : 73) + s, 10);
                return;
        }
        if (Codes.TryGetValue(code, out var e) && (Imp ? e.I : e.C) is var id and > 0) Request(id, Imp ? e.Iv : e.Cv);
    }

    /// <summary>"Random generic": rand(2) gives C-3PO 17 / 18, IMP 18 / 19; result 2 requests nothing.</summary>
    void Generic(int validity = 10)
    {
        int r = Rand(2);
        if (r < 2) Request((Imp ? 18 : 17) + r, validity);
    }

    /// <summary>
    /// A refused player order: the same refusal as the previous one requests 15 / 16
    /// (IMP 16 / 17) and the second repeat forgets it; otherwise the pair is stored and dispatched by category with the exact
    /// tests. <paramref name="game"/> gives the advisor side's views (null: every view test fails). Inferred:
    /// openswr's views keep no manager TargetKey, so the (1, 0x24) / (1, 0x28) side-1 manager test reads the real TargetKey while
    /// side 1's view of the manager is live and fails otherwise.
    /// </summary>
    public void Refused(int category, int code, Command? command, GameState? game = null)
    {
        if (_pair == (category, code))
        {
            Request(Imp ? (_repeated ? 17 : 16) : (_repeated ? 16 : 15), 10);
            if (_repeated) _pair = null;
            _repeated = !_repeated;
            return;
        }
        (_pair, _repeated) = ((category, code), false);
        var arg = command?.ArgRef;
        uint argFamily = arg is null ? 0 : Family.Of(arg.Type);
        bool fleet = argFamily is >= 0x08 and < 0x10, system = argFamily is >= 0x90 and < 0x98;
        switch (category)
        {
            case 0x90:
                switch (code)
                {
                    case 1: Request(Imp ? 12 : 11, 10); break;
                    case 2: Request(Imp ? 13 : 12, 10); break;
                    case 4: Request(Imp ? 22 : 21, 10); break;
                    case 3 when Imp: Request(11, 10); break;
                    default: Generic(); break;
                }
                return;
            case 0xA0:
                if (code == 2) Request(2, 10);
                else if (code == 3) Request(Imp ? 25 : 24, 10);
                else Generic();
                return;
            case >= 0x90 and <= 0x97 or >= 0xA0 and <= 0xAF or >= 0x08 and <= 0x0F or >= 0x15 and <= 0x1B:
                Generic();
                return;
            case 0x14:
                // V = the advisor side's capital-ship view of the ArgRef (every object has one; only families [0x14, 0x1C))
                if (code is 1 or 2 && argFamily is >= 0x14 and < 0x1C)
                {
                    var ship = game?.Definitions.CapitalShips.FirstOrDefault(r => r.Common.Ref == arg!.Type);
                    uint capacity = ship is null ? 0 : code == 1 ? ship.FighterCapacity : ship.TroopCapacity;
                    Request(capacity != 0 ? 8 : 7, 5);
                }
                else if (code is 1 or 2 && fleet) Request(9, 5);
                else Generic();
                return;
            case 0xF3:
                if (code == 1) Request(10, 10);
                else Generic();
                return;
        }
        switch ((category, code))
        {
            case (1, 0x16) or (1, 0x12) or (1, 0x26): return;
            case (0x40, 0x92):
                if (arg is not null) Request(3, 10);
                else Generic();
                return;
            case (0x40, 0x91):
                if (arg is not null && command!.Type == 0x240 && command.Targets.Count == 0 && command.Decoys.Count > 0) Request(Imp ? 21 : 20, 10);
                else Generic();
                return;
            case (1, 0x18): Request(5, 10); return;
            case (1, 0x22): // a Destroyed system view gives 6; otherwise nothing at all (no generic line, no draw)
                if (system && game is not null && Knowledge.See(game, arg!, Side).Destroyed) Request(6, 10);
                return;
            case (1, 0x25) or (1, 0x27): Request(fleet ? 7 : Imp ? 24 : 23, 10); return;
            case (1, 0x20): Request(Imp ? 23 : 22, 10); return;
            case (1, 0x11) or (1, 0x21): Request(1, 10); return;
            case (1, 0x24) or (1, 0x28):
                // side 1's view of a manager first target
                // the view's TargetKey of its last refresh: a frozen Empire manager view normally holds its own system
                bool managerAimed = command?.Targets.FirstOrDefault().Target is ManufacturingManager m && Knowledge.TargetKey(m, Owner.Alliance) is { } key
                    && Family.Of(key.Type) is >= 0x90 and < 0x98;
                Request(system || managerAimed ? 2 : Imp ? 24 : 23, 10);
                return;
            default: Generic(); return;
        }
    }

    /// <summary>The build helper's prepared Build command, watched while <see cref="Watching"/> is set.</summary>
    public Command? Watched { get; private set; }

    /// <summary>
    /// An accepted player order: refusal memory and menu-click counter cleared; the helper's watched Build command
    /// sets <see cref="HelperDone"/> (action 35 / 28); <see cref="Watching"/> cleared. The production window builds a new command from the prepared one, so
    /// "the watched command" = the same type for the same manager (Inferred).
    /// </summary>
    public void Accepted(Command command)
    {
        (_pair, _repeated, MenuClicks) = (null, false, 0);
        if (Watching && Watched is { } w && command.Type == w.Type && command.Targets.Select(t => t.Target).SequenceEqual(w.Targets.Select(t => t.Target)))
            HelperDone = true;
        Watching = false;
        Watched = null;
    }

    // ---- build helper ----

    /// <summary>Helper kind: 0 none, 1 facilities (0x112), 2 ships (0x110), 3 troops (0x111).</summary>
    public int HelperKind { get; private set; }
    /// <summary>0x110 / 0x111 / 0x112 (only while the helper is idle): the kind, target reset; the screen then enters the targeting state without a command.</summary>
    public void HelperStart(int id)
    {
        HelperKind = id switch { 0x110 => 2, 0x111 => 3, 0x112 => 1, _ => 0 };
    }

    void HelperReset() => (HelperKind, Records[Helper]) = (0, Disabled);

    /// <summary>
    /// The target pick and the helper machine in one call (openswr: no delegation cycle yet; the
    /// original runs the machine in the next think, Inferred equivalent). Key none gives reset; not a system / fleet / capital ship gives
    /// pair (1, 0x27); troops to a system whose view the player does not own and that holds no Usable fleet of the player
    /// then a 0x214 (ArgRef = the system) refused as (1, 0x24); else the producer search
    /// (<see cref="Producer"/>), nothing found gives pair (0xA0, 3). Returns the 0x214 Destination (manager to target; the caller
    /// validates and sends it, a refusal goes to <see cref="Refused"/>) and the Build command 0x210 / 0x211 / 0x212 for the
    /// production window (not submitted). The helper resets either way.
    /// </summary>
    public (Command Destination, Command Build)? HelperPick(GameState game, GameObject? target)
    {
        int kind = HelperKind;
        HelperReset();
        if (target is null || kind == 0) return null;
        if (Family.Of(target.Type) is not ((>= 0x90 and < 0x98) or (>= 0x08 and < 0x10) or (>= 0x14 and < 0x1C)))
        {
            Refused(1, 0x27, null, game);
            return null;
        }
        if (kind == 3 && target is StarSystem s && Knowledge.See(game, s, Side).Owner != Side && !s.Contents.OfType<Fleet>().Any(f => f.Owner == Side && f.Usable))
        {
            Refused(1, 0x24, new Command(0x214, Side) { ArgRef = target }, game);
            return null;
        }
        if (Producer(game, kind, target) is not { } manager)
        {
            Refused(0xA0, 3, null, game);
            return null;
        }
        var destination = new Command(0x214, Side) { Targets = [(manager, 0)], ArgRef = target };
        var build = new Command(kind switch { 1 => 0x210, 2 => 0x211, _ => 0x212 }, Side) { Targets = [(manager, 0)], Fields = [0, 0] };
        return (destination, build);
    }

    /// <summary>
    /// Producer search: (1) sectors in key order holding an own system and a system with the kind's idle flag,
    /// by squared distance (SD X / Y) to the sector of the target's location (player's view), nearest first, rand(10) per tie
    /// (<see cref="Rand"/>), picked alone; (2) that sector's systems in slot order with the idle flag and own, by squared distance
    /// to the target's system, the same way; (3) the picked system's manager of the kind (facilities construction, ships
    /// shipyard, troops training). Idle flag: the view Populated and Explored, without
    /// Uprising and Blockade, >= 1 Existing facility of the kind of which one is Usable, the manager with an empty queue (a queue makes
    /// it busy whatever its progress). Inferred: the flags
    /// are computed from the player's views at pick time (the original reads its advisor model as last updated); facilities and
    /// queues are read only where the player sees the system live (own or observed); an unknown target location counts distance 0.
    /// </summary>
    public ManufacturingManager? Producer(GameState game, int kind, GameObject target)
    {
        var mk = kind switch { 1 => ManufacturingKind.Construction, 2 => ManufacturingKind.Ship, _ => ManufacturingKind.Training };
        uint family = kind switch { 1 => 0x2A, 2 => 0x28, _ => 0x29 };
        var at = target as StarSystem ?? Knowledge.Location(game, target, Side) as StarSystem;
        bool Own(StarSystem x) => Knowledge.See(game, x, Side).Owner == Side;
        bool Idle(StarSystem x)
        {
            var v = Knowledge.See(game, x, Side);
            if (!v.Populated || !v.Explored || v.Uprising || v.Blockade || !(x.Owner == Side || Knowledge.IsLive(x, Side))) return false;
            var yards = x.Contents.Where(o => Family.Of(o.Type) == family && o.Existing).ToList(); // mode 3, then >= 1 Usable
            return yards.Count > 0 && yards.Any(o => o.Usable) && x.Managers[(int)mk] is { Destroyed: false, Orders.Count: 0 };
        }
        static int Squared(int dx, int dy) => dx * dx + dy * dy;
        var systems = game.Systems.ToLookup(x => x.Sector);
        var sectors = new Ai.ListStack();
        sectors.Push(new Ai.RankedList(game.Sectors.OrderBy(x => x.Ref).Where(x => systems[x].Any(Own) && systems[x].Any(Idle)).Select(x => ((object)x,
            at is null ? 0 : Squared(x.Definition.X - at.Sector.Definition.X, x.Definition.Y - at.Sector.Definition.Y))), 2, Rand));
        if (sectors.Pick() is not Sector sector) return null;
        var list = new Ai.ListStack();
        list.Push(new Ai.RankedList(systems[sector].Where(x => Idle(x) && Own(x)).Select(x => ((object)x,
            at is null ? 0 : Squared(x.Definition.X - at.Definition.X, x.Definition.Y - at.Definition.Y))), 2, Rand));
        return list.Pick() is StarSystem picked ? picked.Managers[(int)mk] : null;
    }

    /// <summary>The production window opened with the helper's Build command: watched.</summary>
    public void Watch(Command build)
    {
        Watching = true;
        Watched = build;
    }

    /// <summary>Right click on an advisor display opened its menu: counter +1, <see cref="MenuOpened"/>.</summary>
    public void MenuOpen()
    {
        MenuClicks++;
        MenuOpened = true;
    }

    /// <summary>The message window opened / closed.</summary>
    public void MessageWindow(bool open) => MessageWindowOpen = open;

    /// <summary>Esc / mouse button while the briefing hooks are installed.</summary>
    public void Skip() => SkipBriefing = true;

    /// <summary>
    /// Window-menu toggles: 0x115 / 0x116 flip record 0x15 / 0x14 between disabled and enabled (toggle
    /// counter +/- 1; enabling with the counter above 3 sets ToggledOften, else ManageGarrisonsOn / ManageProductionOn; disabling sets
    /// ManageGarrisonsOff / ManageProductionOff); 0x11B toggles Translate, 0x11E TipsOff. An enabled record is run by the delegation cycle; the toggle
    /// itself leaves its machine alone (the command slot resets it only when the record is the cycle's current one).
    /// </summary>
    public void Toggle(int id)
    {
        switch (id)
        {
            case 0x115 or 0x116:
                int record = id == 0x115 ? Garrisons : Production;
                if (Records[record] == Disabled)
                {
                    Records[record] = 0;
                    if (++ToggleCounter > 3) ToggledOften = true;
                    else if (id == 0x115) ManageGarrisonsOn = true;
                    else ManageProductionOn = true;
                }
                else
                {
                    Records[record] = Disabled;
                    ToggleCounter--;
                    if (id == 0x115) ManageGarrisonsOff = true;
                    else ManageProductionOff = true;
                }
                break;
            case 0x11B: Translate = !Translate; break;
            case 0x11E: TipsOff = !TipsOff; break;
        }
    }

    /// <summary>Window-menu row state: (enabled, checked). "Advisor active" is always true in this build; 0x117 has no entry.</summary>
    public (bool Enabled, bool Checked) Row(int id) => id switch
    {
        0x110 or 0x111 or 0x112 => (Records[Helper] == Disabled, false),
        0x113 or 0x114 => (true, false),
        0x115 => (true, Records[Garrisons] != Disabled),
        0x116 => (true, Records[Production] != Disabled),
        0x11B => (true, Translate),
        0x11E => (true, !TipsOff),
        _ => (false, false),
    };

    // ---- delegation cycle ----

    /// <summary>The advisor model, read by the delegated planners; rebuilt from the views (not saved).</summary>
    public AdvisorModel? Model { get; private set; }
    /// <summary>Delegated planners 0x14 / 0x15 (the build helper 0x17 runs at its target pick, <see cref="HelperPick"/>).</summary>
    public Dictionary<int, DelegatedPlanner> Planners { get; } = new() { [Production] = new ProductionDelegate(), [Garrisons] = new GarrisonDelegate() };
    /// <summary>Delegated commands of the current day against the budget of 5; reset on every DayDue.</summary>
    public int Issued { get; internal set; }
    public const int DailyBudget = 5;
    /// <summary>Current record, kept across cycles when the budget ran out.</summary>
    public int? Current { get; private set; }
    static readonly int[] RecordKeys = [Production, Garrisons, Helper];
    int _urgent = -1, _roundRobin = -1; // pick cursors (-1 = none)

    /// <summary>
    /// Delegation cycle: state 5 the model update, state 6 the think of
    /// every record (0 becomes 1, done := 0), state 4 the issue phase: the pick, then the current record is asked for one command per
    /// frame until it is done (priority := 0 before each ask, so with both toggles on 0x14 and 0x15 take turns); a command is validated, and a refused one goes to the record's failure slot, an accepted one to the human's outbox
    /// (<paramref name="human"/>) and the day budget +1; a budget used up with a current record ends the cycle, keeping it. A done
    /// record goes back to state 0 (unless disabled). shortcut: one call runs the whole cycle (the original runs one state step per
    /// frame, the clock not waiting, Inferred harmless: nothing else reads the model or the records between those frames).
    /// </summary>
    public void Delegate(GameState game, Ai.HumanController human)
    {
        // model totals i = 2 / 3. shortcut: counted straight from the views (the model's sector sums of own / enemy systems),
        // so the full model update still runs only with a record enabled
        var enemy = Side == Owner.Alliance ? Owner.Empire : Owner.Alliance;
        (StandingOwn, StandingEnemy) = (0, 0);
        foreach (var s in game.Systems)
        {
            var owner = Knowledge.See(game, s, Side).Owner;
            if (owner == Side) StandingOwn++;
            else if (owner == enemy) StandingEnemy++;
        }
        if (Current is null && Records.Values.All(s => s == Disabled))
        {
            Pick(); // nothing enabled: only the pick's cursors move (the model update draws nothing and nobody reads it)
            return;
        }
        (Model ??= new AdvisorModel(Side)).Rand = Rand;
        Model.Update(game);
        foreach (int k in RecordKeys)
            if (Records[k] == 0)
            {
                Records[k] = 1;
                if (Planners.TryGetValue(k, out var t)) t.Done = false;
            }
        while (true)
        {
            if (Current is null)
            {
                if ((Current = Pick()) is null) return;
                continue;
            }
            int key = Current.Value;
            if (Issued >= DailyBudget && key != Helper) return;
            var p = Planners[key];
            p.Priority = 0; // priority is zeroed before every ask and is 1 only between a 0x214 and the next ask
            var c = p.Step(game, Model, Records[key]);
            if (c is not null)
            {
                if (Orders.Validate(game, c).Success)
                {
                    human.Append(c);
                    Issued++;
                }
                else p.Failure();
            }
            if (!p.Done) continue;
            if (Records[key] != Disabled) Records[key] = 0;
            Current = null;
            return;
        }
    }

    /// <summary>
    /// Record pick: the urgent cursor advances on every call, the state-1
    /// record with the strictly largest priority (unsigned, from 0) from it; else the round-robin cursor advances, then the first
    /// state-1 record cyclically from it.
    /// </summary>
    int? Pick()
    {
        int n = RecordKeys.Length;
        _urgent = (_urgent + 1) % n;
        int? best = null;
        int bestPriority = 0;
        for (int k = 0; k < n; k++)
            if (RecordKeys[(_urgent + k) % n] is var key && Records[key] == 1 && Planners.TryGetValue(key, out var p) && (uint)p.Priority > (uint)bestPriority)
                (best, bestPriority) = (key, p.Priority);
        if (best is not null) return best;
        _roundRobin = (_roundRobin + 1) % n;
        for (int k = 0; k < n; k++)
            if (Records[RecordKeys[(_roundRobin + k) % n]] == 1) return RecordKeys[_roundRobin = (_roundRobin + k) % n];
        return null;
    }

    /// <summary>Stage 13 on the DayDue tick: the delegation budget resets and the mood is recomputed.</summary>
    public void DayDue()
    {
        Issued = 0;
        Mood = MoodByte(StandingOwn, StandingEnemy);
    }

    /// <summary>
    /// Mood recompute: byte := 8; d = the first counter minus the second: >= 2 gives 1, 1 gives 2, -1 gives 0x20, <= -2 gives
    /// 0x40; still 8: q = own * 100 div enemy (own * 10 when enemy = 0): >= 300 gives 2, >= 200 gives 4, <= 50 gives 0x10. The counters are never
    /// written (Inferred), so d = 0 and only 2 / 4 / 8 / 0x10 occur.
    /// </summary>
    public static int MoodByte(int own, int enemy, int d = 0)
    {
        int mood = d >= 2 ? 1 : d == 1 ? 2 : d == -1 ? 0x20 : d <= -2 ? 0x40 : 8;
        if (mood != 8) return mood;
        int q = enemy == 0 ? own * 10 : own * 100 / enemy;
        return q >= 300 ? 2 : q >= 200 ? 4 : q <= 50 ? 0x10 : 8;
    }

    /// <summary>
    /// Status mask from the human slot (view message 0x128, the
    /// victory state word): only with game over; Empire wins gives C3PO 87 / IMP 84, else Alliance wins
    /// gives C3PO 86 / IMP 85, validity 40000; then the main-screen state word := 1.
    /// </summary>
    public void StatusMask(VictoryStatus word)
    {
        if (!word.GameOver) return;
        if (word.EmpireWins) Request(Imp ? 84 : 87, 40000);
        else if (word.AllianceWins) Request(Imp ? 85 : 86, 40000);
        else return;
        GameOverUi = true;
    }

    /// <summary>
    /// End-of-game application state, on the post command (3, x) that ends actions 84-87,
    /// from the victory word and the player side: Empire wins gives 0x11 Alliance player / 0x12 Empire
    /// player; else Alliance wins gives 0x10 / 0x13; else 0 (nothing happens).
    /// </summary>
    public static int EndState(VictoryStatus word, Owner player) =>
        word.EmpireWins ? player == Owner.Alliance ? 0x11 : 0x12
        : word.AllianceWins ? player == Owner.Alliance ? 0x10 : 0x13 : 0;

    /// <summary>End movie of states 0x10-0x13, then the main menu; null for 0.</summary>
    public static string? EndMovie(int state) => state switch
    {
        0x10 => "MDATA.105", 0x11 => "MDATA.106", 0x12 => "MDATA.107", 0x13 => "MDATA.108", _ => null,
    };

    // ---- advice topics ----

    /// <summary>The topic list is built.</summary>
    public bool TopicsBuilt { get; private set; }
    /// <summary>Tip triggers used (<see cref="AdvisorTip"/>).</summary>
    public bool GoToTipUsed { get; private set; }
    public bool ManufacturingTipUsed { get; private set; }
    public bool FleetTipUsed { get; private set; }
    public bool PlanetTipUsed { get; private set; }
    public bool MissionsTipUsed { get; private set; }
    /// <summary>Last tip tick (starts at 0, Inferred).</summary>
    public int LastTip { get; private set; }
    /// <summary>The topics of the side's TEXTSTRA topic list (set by the screen; empty = no data).</summary>
    public Func<IReadOnlyList<AdviceTopic>> TopicSource { get; set; } = () => [];
    List<AdviceTopic> _topics = [];
    /// <summary>Topics not shown yet, in key order (empty until built).</summary>
    public IReadOnlyList<AdviceTopic> Topics => _topics;

    bool TipsOn => !TipsOff;

    /// <summary>
    /// Topic list, built once on the first need: every kind-7 topic is posted at once from the
    /// largest key down (so the newest-first list shows them in key order) and removed. Returns the posted messages.
    /// </summary>
    public List<GameMessage> BuildTopics(GameState game)
    {
        if (_savedTopics is { } saved)
        {
            _savedTopics = null;
            var keep = saved.ToHashSet();
            _topics = [.. TopicSource().Where(t => keep.Contains(t.N)).OrderBy(t => t.Key)];
        }
        if (TopicsBuilt) return [];
        TopicsBuilt = true;
        _topics = [.. TopicSource().OrderBy(t => t.Key)];
        var posted = new List<GameMessage>();
        foreach (var t in _topics.Where(t => t.Kind == 7).OrderByDescending(t => t.Key).ToList()) posted.Add(PostTopic(game, t));
        return posted;
    }

    GameMessage PostTopic(GameState game, AdviceTopic t)
    {
        _topics.Remove(t);
        return Messages.Advice(game, Side, t.N);
    }

    static int KindOf(AdvisorTip tip) => tip switch
    {
        AdvisorTip.GoTo => 6, AdvisorTip.Manufacturing => 2, AdvisorTip.Fleet => 3, AdvisorTip.Planet => 5, _ => 4,
    };

    bool Used(AdvisorTip tip) => tip switch
    {
        AdvisorTip.GoTo => GoToTipUsed, AdvisorTip.Manufacturing => ManufacturingTipUsed, AdvisorTip.Fleet => FleetTipUsed,
        AdvisorTip.Planet => PlanetTipUsed, _ => MissionsTipUsed,
    };

    void Use(AdvisorTip tip)
    {
        switch (tip)
        {
            case AdvisorTip.GoTo: GoToTipUsed = true; break;
            case AdvisorTip.Manufacturing: ManufacturingTipUsed = true; break;
            case AdvisorTip.Fleet: FleetTipUsed = true; break;
            case AdvisorTip.Planet: PlanetTipUsed = true; break;
            default: MissionsTipUsed = true; break;
        }
    }

    /// <summary>
    /// Tip trigger: with tips on and the trigger not used yet, the trigger is marked used, the list built if needed, the first
    /// topic (key order) of the trigger's kind posted, the tip timer reset. Triggers: go-to helper, manufacturing, fleet, planet,
    /// missions sub-window.
    /// </summary>
    public List<GameMessage> TipTrigger(GameState game, AdvisorTip tip)
    {
        if (!TipsOn || Used(tip)) return [];
        Use(tip);
        var posted = BuildTopics(game);
        int kind = KindOf(tip);
        if (_topics.FirstOrDefault(t => t.Kind == kind) is { N: > 0 } topic) posted.Add(PostTopic(game, topic));
        LastTip = Now();
        return posted;
    }

    /// <summary>
    /// Tip timer: tips on and 300 ticks since the last tip gives the first available topic in key order (kinds
    /// 2-6 only with their trigger used, others always) is posted; the timer restarts either way.
    /// </summary>
    public List<GameMessage> TipTimer(GameState game)
    {
        int now = Now();
        if (!TipsOn || now < LastTip + 300) return [];
        var posted = BuildTopics(game);
        bool Available(AdviceTopic t) => t.Kind is < 2 or > 6 || Enum.GetValues<AdvisorTip>().Any(x => KindOf(x) == t.Kind && Used(x));
        if (_topics.FirstOrDefault(Available) is { N: > 0 } topic) posted.Add(PostTopic(game, topic));
        LastTip = now;
        return posted;
    }

    // ---- save / load ----

    /// <summary>
    /// Saved advisor state: flags, gate, the secondary display flag, post command, due[], counters (toggle, menu, music), mood,
    /// last tip tick, triggers used, remaining topics, record states, helper kind. Not saved: cooldown[] (zeroed), the last-play tick,
    /// the refusal memory, the display queues; the advisor model is not modelled.
    /// </summary>
    public sealed class State
    {
        public bool HelperDone { get; set; }
        public bool ManageGarrisonsOn { get; set; }
        public bool ManageProductionOn { get; set; }
        public bool ManageGarrisonsOff { get; set; }
        public bool ManageProductionOff { get; set; }
        public bool Translate { get; set; }
        public bool SkipBriefing { get; set; }
        public bool MessageWindowOpen { get; set; }
        public bool TipsOff { get; set; }
        public bool ToggledOften { get; set; }
        public bool MenuOpened { get; set; }
        public bool Shown { get; set; }
        public bool Hidden { get; set; }
        public bool Watching { get; set; }
        public bool Idle { get; set; }
        public bool Gate { get; set; }
        public int SecondaryIdle { get; set; }
        public int[]? Post { get; set; }
        public int[] Due { get; set; } = [];
        public int ToggleCounter { get; set; }
        public int MenuClicks { get; set; }
        public int MusicCounter { get; set; }
        public int Mood { get; set; }
        public int LastTip { get; set; }
        public bool TopicsBuilt { get; set; }
        public bool GoToTipUsed { get; set; }
        public bool ManufacturingTipUsed { get; set; }
        public bool FleetTipUsed { get; set; }
        public bool PlanetTipUsed { get; set; }
        public bool MissionsTipUsed { get; set; }
        public List<int> Topics { get; set; } = [];
        public Dictionary<int, int> Records { get; set; } = [];
        public int HelperKind { get; set; }
        /// <summary>Delegation: the day's count, the current record, the pick cursors (Inferred: part of the record tree), the planners.</summary>
        public int Issued { get; set; }
        public int? Current { get; set; }
        public int[]? Cursors { get; set; }
        public Dictionary<int, PlannerState>? Planners { get; set; }
    }

    /// <summary>A delegated planner's saved fields: done, priority, machine, count, the keys (SdRefs; 0 = none) and the product.</summary>
    public sealed class PlannerState
    {
        public bool Done { get; set; }
        public int Priority { get; set; }
        public int Machine { get; set; }
        public int Count { get; set; }
        public uint Target { get; set; }
        public uint Producer { get; set; }
        public uint Product { get; set; }
    }

    // a key as saved: systems and managers only (sector records and system records stand for their objects only inside one cycle)
    static uint SavedKey(object? o) => o switch { AdvisorSystem r => r.System.Ref, GameObject g => g.Ref, _ => 0 };

    static object? LoadedKey(GameState game, uint key) =>
        key == 0 ? null : game.Systems.FirstOrDefault(s => s.Ref == key) as object
            ?? game.Systems.SelectMany(s => s.Managers).FirstOrDefault(m => m?.Ref == key);

    public State Save() => new()
    {
        HelperDone = HelperDone, ManageGarrisonsOn = ManageGarrisonsOn, ManageProductionOn = ManageProductionOn, ManageGarrisonsOff = ManageGarrisonsOff,
        ManageProductionOff = ManageProductionOff, Translate = Translate, SkipBriefing = SkipBriefing, MessageWindowOpen = MessageWindowOpen,
        TipsOff = TipsOff, ToggledOften = ToggledOften, MenuOpened = MenuOpened, Shown = Shown, Hidden = Hidden, Watching = Watching, Idle = Idle,
        Gate = Gate, SecondaryIdle = SecondaryIdle, Post = PostCommand is { } p ? [p.P1, p.P2] : null, Due = [.. _due],
        ToggleCounter = ToggleCounter, MenuClicks = MenuClicks, MusicCounter = MusicCounter, Mood = Mood, LastTip = LastTip,
        TopicsBuilt = TopicsBuilt, GoToTipUsed = GoToTipUsed, ManufacturingTipUsed = ManufacturingTipUsed, FleetTipUsed = FleetTipUsed,
        PlanetTipUsed = PlanetTipUsed, MissionsTipUsed = MissionsTipUsed,
        Topics = _savedTopics is { } saved ? [.. saved] : [.. _topics.Select(t => t.N)], Records = new(Records), HelperKind = HelperKind,
        Issued = Issued, Current = Current, Cursors = _urgent < 0 && _roundRobin < 0 ? null : [_urgent, _roundRobin],
        Planners = Planners.Values.All(p => p is { Done: false, Priority: 0, Machine: 0, Count: 0, Product: 0 } && SavedKey(p.Target) == 0 && SavedKey(p.Producer) == 0) ? null
            : Planners.ToDictionary(e => e.Key, e => new PlannerState
            {
                Done = e.Value.Done, Priority = e.Value.Priority, Machine = e.Value.Machine, Count = e.Value.Count,
                Target = SavedKey(e.Value.Target), Producer = SavedKey(e.Value.Producer), Product = e.Value.Product,
            }),
    };

    /// <summary>Restores a saved advisor (no briefing request of its own: the saved due[] holds it if it had not played).</summary>
    public static Advisor Load(GameState game, Owner side, State s, Func<int> now)
    {
        var a = new Advisor(side, now, briefing: null, tips: true)
        {
            HelperDone = s.HelperDone, ManageGarrisonsOn = s.ManageGarrisonsOn, ManageProductionOn = s.ManageProductionOn,
            ManageGarrisonsOff = s.ManageGarrisonsOff, ManageProductionOff = s.ManageProductionOff, Translate = s.Translate,
            SkipBriefing = s.SkipBriefing, MessageWindowOpen = s.MessageWindowOpen, TipsOff = s.TipsOff, ToggledOften = s.ToggledOften,
            MenuOpened = s.MenuOpened, Shown = s.Shown, Hidden = s.Hidden, Watching = s.Watching, Idle = s.Idle, Gate = s.Gate, SecondaryIdle = s.SecondaryIdle, PostCommand = s.Post is [var p1, var p2] ? (p1, p2) : null,
            ToggleCounter = s.ToggleCounter, MenuClicks = s.MenuClicks, MusicCounter = s.MusicCounter, Mood = s.Mood, LastTip = s.LastTip,
            TopicsBuilt = s.TopicsBuilt, GoToTipUsed = s.GoToTipUsed, ManufacturingTipUsed = s.ManufacturingTipUsed,
            FleetTipUsed = s.FleetTipUsed, PlanetTipUsed = s.PlanetTipUsed, MissionsTipUsed = s.MissionsTipUsed, HelperKind = s.HelperKind, _savedTopics = s.Topics,
        };
        Array.Copy(s.Due, a._due, Math.Min(s.Due.Length, a._due.Length));
        foreach (var (k, v) in s.Records) a.Records[k] = v;
        (a.Issued, a.Current) = (s.Issued, s.Current);
        if (s.Cursors is [var u, var rr]) (a._urgent, a._roundRobin) = (u, rr);
        foreach (var (k, p) in s.Planners ?? [])
            if (a.Planners.TryGetValue(k, out var t))
                (t.Done, t.Priority, t.Machine, t.Count, t.Target, t.Producer, t.Product) =
                    (p.Done, p.Priority, p.Machine, p.Count, LoadedKey(game, p.Target), LoadedKey(game, p.Producer), p.Product);
        return a;
    }

    List<int>? _savedTopics; // remaining topic numbers of a loaded game, resolved against the source on first use

    // ---- voices ----

    /// <summary>
    /// Voice variant list pick: sprite-DLL RCDATA [n, wave...]; n = 0 none, 1 the only wave (no draw), else rand(n). The
    /// out-of-range result n reads the word after the list, never a WAVE in the shipped DLLs: silence, the draw consumed.
    /// </summary>
    public static int Voice(IReadOnlyList<ushort> list, Func<int, int> rand)
    {
        int n = list.Count > 0 ? list[0] : 0;
        if (n <= 0) return 0;
        int r = n == 1 ? 0 : rand(n);
        return r < n && r + 1 < list.Count ? list[r + 1] : 0;
    }

    // ---- strategy music ----

    static readonly int[] Tracks = [300, 301, 302, 303, 304, 305, 306, 310, 311, 312];

    /// <summary>
    /// Music pick: counter &gt; 0 gives counter -1, t = rand(6) div 2 + 1; else counter := 3 and t by the
    /// mood byte (1 gives 4, 2 gives 5, 4 gives 6, 0x10 gives 7, 0x20 gives 8, 0x40 gives 9, else rand(3) + 1). Returns the MDATA file number (301 ...).
    /// Unguarded: the out-of-range results rand(6) = 6 and rand(3) = 3 both give t = 4, MDATA.304.
    /// </summary>
    public int MusicTrack()
    {
        int t;
        if (MusicCounter > 0)
        {
            MusicCounter--;
            t = Rand(6) / 2 + 1;
        }
        else
        {
            MusicCounter = 3;
            t = Mood switch { 1 => 4, 2 => 5, 4 => 6, 0x10 => 7, 0x20 => 8, 0x40 => 9, _ => Rand(3) + 1 };
        }
        return Tracks[Math.Min(t, Tracks.Length - 1)];
    }
}
