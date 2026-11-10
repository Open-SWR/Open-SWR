using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

public sealed partial class Advisor
{
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
        public AdvisorMood Mood { get; set; }
        public int LastTip { get; set; }
        public bool TopicsBuilt { get; set; }
        public bool GoToTipUsed { get; set; }
        public bool ManufacturingTipUsed { get; set; }
        public bool FleetTipUsed { get; set; }
        public bool PlanetTipUsed { get; set; }
        public bool MissionsTipUsed { get; set; }
        public List<int> Topics { get; set; } = [];
        public Dictionary<int, int> Records { get; set; } = [];
        public BuildHelperKind HelperKind { get; set; }
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
}
