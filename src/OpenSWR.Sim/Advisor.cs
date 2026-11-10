using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

/// <summary>
/// The human player's advisor: request slots with expiry and
/// cooldown, option and request flags, the gate, the post-action command, the refusal memory and the counters. UI-side runtime: the
/// Sim never runs it; MainScreen builds one for the human
/// controller and calls <see cref="Tick"/> once per frame. Every draw goes through <see cref="Rand"/> (default the shared UI
/// generator). Time = the strategic clock's tick counter (<see cref="Now"/>).
/// </summary>
public sealed partial class Advisor
{
    /// <summary>Delegation records: Production (Manage Production), Garrisons (Manage Garrisons), Helper (build helper).</summary>
    public const int Production = 20, Garrisons = 21, Helper = 23;

    /// <summary>Record states: enabled and idle, has work, disabled.</summary>
    public const int RecordEnabled = 0, RecordHasWork = 1, Disabled = 2;

    /// <summary>Ticks after a played line before a new line may play; the idle chatter counts this many ticks since the last action.</summary>
    const int CooldownTicks = 60, IdleTicks = 50;
    /// <summary>Ticks between two tip timer posts.</summary>
    const int TipInterval = 300;
    /// <summary>Strategy music: the counter after a mood pick, the rand range of the counter's pick and of the mood's pick.</summary>
    const int MusicRepeat = 3, MusicDraw = 6, MoodDraw = 3;

    readonly int[] _due, _cooldown;
    OrderResult? _pair;
    bool _repeated;
    int _lastPlay, _lastPlayIdle;
    bool _briefingFallback;

    /// <param name="side">The human side: Alliance gives C-3PO (88 slots), Empire gives IMP-22 (86 slots).</param>
    /// <param name="now">The strategic clock's tick counter (<see cref="Clock.Tick"/>).</param>
    /// <param name="briefing">
    /// New game with the briefing DLL (module 13) present: pre-request action 41 / 74 at the absolute tick 40000. New game
    /// without it: the openswr adaptation posts (2, BriefingLastStep) once at the first gate open (Inferred). Null = a loaded game
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
        if (briefing == true) _due[AdvisorSlot.Briefing.For(Imp)] = Validity.Long;
        _briefingFallback = briefing == false;
    }

    public Owner Side { get; }
    public bool Imp => Side == Owner.Empire;
    /// <summary>Request slots: C-3PO 88 (ids 1-87), IMP-22 86 (ids 1-85).</summary>
    public int Slots => Imp ? AdvisorSlot.ImpSlots : AdvisorSlot.C3poSlots;
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
    public AdvisorMood Mood { get; internal set; } = AdvisorMood.Neutral;
    /// <summary>
    /// Model totals i = 2 / 3: the systems whose player view the player / the other side owns, from
    /// the cycle's model update (state 5, every cycle whatever the toggles). Zero until the first cycle (model ctor).
    /// </summary>
    public int StandingOwn { get; private set; }
    public int StandingEnemy { get; private set; }
    /// <summary>Main-screen state word set by the game-over status mask (Inferred meaning: the game-over UI state).</summary>
    public bool GameOverUi { get; private set; }
    /// <summary>Record states of Production / Garrisons / Helper; all start disabled.</summary>
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

    void Request(SideSlot slot, int validity) => Request(slot.For(Imp), validity);

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
            if (p.P1 >= PostKind.GuidedStep) Post?.Invoke(p.P1, p.P2);
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
                _cooldown[id] = now + CooldownTicks;
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
    /// first opening of a new game without the briefing DLL posts the briefing's last step (2, BriefingLastStep).
    /// </summary>
    public void GateOpen()
    {
        Gate = true;
        if (IdleNow is { } idle ? idle() > _lastPlayIdle + IdleTicks : Now() > _lastPlay + IdleTicks) Idle = true;
        if (_briefingFallback)
        {
            _briefingFallback = false;
            PostCommand = (PostKind.GuidedStep, PostKind.BriefingLastStep);
        }
    }

    /// <summary>Status step: closes the gate, then the first set request flag (in table order) is cleared and raises its request.</summary>
    void Rules(int now)
    {
        Gate = false;
        if (Hidden)
        {
            Hidden = false;
            Request(AdvisorSlot.Hidden, Imp ? Validity.Medium : Validity.Brief);
        }
        else if (Shown)
        {
            Shown = false;
            Request(AdvisorSlot.Shown, Imp ? Validity.Normal : Validity.Long);
        }
        else if (SkipBriefing)
        {
            SkipBriefing = false;
            Request(AdvisorSlot.SkipBriefing, Validity.Long);
            Flush?.Invoke();
        }
        else if (HelperDone)
        {
            HelperDone = false;
            Request(AdvisorSlot.HelperDone, Validity.Normal);
        }
        else if (ManageGarrisonsOn)
        {
            ManageGarrisonsOn = false;
            Request(AdvisorSlot.ManageGarrisonsOn, Validity.Normal);
        }
        else if (ManageProductionOn)
        {
            ManageProductionOn = false;
            Request(AdvisorSlot.ManageProductionOn, Validity.Normal);
        }
        else if (ManageGarrisonsOff)
        {
            ManageGarrisonsOff = false;
            Request(AdvisorSlot.ManageGarrisonsOff, Validity.Medium);
        }
        else if (ManageProductionOff)
        {
            ManageProductionOff = false;
            Request(AdvisorSlot.ManageProductionOff, Validity.Medium);
        }
        else if (!Imp && ToggledOften)
        {
            ToggledOften = false;
            Request(AdvisorSlot.ToggledOften, Validity.Brief);
        }
        else if (MenuOpened)
        {
            MenuOpened = false;
            // the first two clicks both request the first row; the clicks wrap after the last of the three rows
            int lo = AdvisorSlot.MenuFirst.For(Imp), hi = lo + MenuLastOffset, slot = MenuClicks + lo - MenuClickBase;
            if (slot > hi) (slot, MenuClicks) = (hi, 0);
            Request(Math.Max(slot, lo), Validity.Brief);
        }
        else if (Idle)
        {
            Idle = false;
            int r = Rand(AdvisorSlot.IdleDraw);
            if (r < AdvisorSlot.IdleRequests) Request(AdvisorSlot.IdleFirst.For(Imp) + r, Imp ? Validity.Brief : Validity.Long);
            Gate = true;
        }
    }

    /// <summary>Last offset of the window-menu request rows (lo to lo + 2) and the clicks before the first row (click 1 and 2 request lo).</summary>
    const int MenuLastOffset = 2, MenuClickBase = 2;

    /// <summary>Toggles counted before a toggle sets ToggledOften (the counter above this).</summary>
    const int ToggleLimit = 3;

    public const int DailyBudget = 5;

    static readonly int[] RecordKeys = [Production, Garrisons, Helper];
    int _urgent = -1, _roundRobin = -1; // pick cursors (-1 = none)

    /// <summary>Mood scale: the share is own * 100 div enemy (own * 10 without enemy systems); thresholds 300, 200 and 50 per cent.</summary>
    const int MoodShareScale = 100, MoodShareScaleNoEnemy = 10, MoodShareHigh = 300, MoodShareMid = 200, MoodShareLow = 50;

    List<AdviceTopic> _topics = [];

    List<int>? _savedTopics; // remaining topic numbers of a loaded game, resolved against the source on first use

    /// <summary>MDATA music file numbers (MDATA.3xx) by track slot 0 to 9; the pick uses slots 1 to 9 (slot 0 unused).</summary>
    static readonly int[] Tracks = [300, 301, 302, 303, 304, 305, 306, 310, 311, 312];
}
