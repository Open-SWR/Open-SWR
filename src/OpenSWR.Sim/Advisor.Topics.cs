using OpenSWR.Data.Dat;
using OpenSWR.Data;
using OpenSWR.Sim.Ai;

namespace OpenSWR.Sim;

public sealed partial class Advisor
{
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

    /// <summary>Topics not shown yet, in key order (empty until built).</summary>
    public IReadOnlyList<AdviceTopic> Topics => _topics;

    bool TipsOn => !TipsOff;

    /// <summary>
    /// Topic list, built once on the first need: every <see cref="AdviceKind.Immediate"/> topic is posted at once from the
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
        foreach (var t in _topics.Where(t => t.Kind == AdviceKind.Immediate).OrderByDescending(t => t.Key).ToList()) posted.Add(PostTopic(game, t));
        return posted;
    }

    GameMessage PostTopic(GameState game, AdviceTopic t)
    {
        _topics.Remove(t);
        return Messages.Advice(game, Side, t.N);
    }

    static int KindOf(AdvisorTip tip) => tip switch
    {
        AdvisorTip.GoTo => AdviceKind.GoTo, AdvisorTip.Manufacturing => AdviceKind.Manufacturing, AdvisorTip.Fleet => AdviceKind.Fleet,
        AdvisorTip.Planet => AdviceKind.Planet, _ => AdviceKind.Missions,
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
        if (!TipsOn || now < LastTip + TipInterval) return [];
        var posted = BuildTopics(game);
        bool Available(AdviceTopic t) => t.Kind is < AdviceKind.Manufacturing or > AdviceKind.GoTo || Enum.GetValues<AdvisorTip>().Any(x => KindOf(x) == t.Kind && Used(x));
        if (_topics.FirstOrDefault(Available) is { N: > 0 } topic) posted.Add(PostTopic(game, topic));
        LastTip = now;
        return posted;
    }
}
