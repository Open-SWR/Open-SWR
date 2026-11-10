using System.Text.Json.Nodes;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// ExploreTrial, ExplorePrior,
/// PoolUseful, EspionageIdle. Views, own messages and definition data only; no draws.
/// </summary>
public sealed partial class OpenSWRController
{
    public const int TrialDays = 90, UsefulDays = 90;

    /// <summary>ExploreTrial: the day of its last trial per kind (saved).</summary>
    readonly SortedDictionary<MissionKind, int> _lastTrial = [];

    /// <summary>PoolUseful: per kind, the last decision day a candidate of it passed the minimum chance (saved).</summary>
    readonly SortedDictionary<MissionKind, int> _passDay = [];

    /// <summary>ExploreTrial: the kind may run a trial today (an experience kind with fewer than <see cref="ExperienceSeen"/> ended, no trial within <see cref="TrialDays"/>).</summary>
    internal bool TrialOpen(MissionKind kind, int day) =>
        On(V7.ExploreTrial) && ExperienceKinds.Contains(kind) && Experience(kind).Ended < ExperienceSeen
        && (!_lastTrial.TryGetValue(kind, out int last) || day - last >= TrialDays);

    void TrialSent(MissionKind kind, int day) => _lastTrial[kind] = day;

    /// <summary>ExplorePrior: the experience cap (successes + base x <see cref="ExperienceSeen"/>) / (ended + <see cref="ExperienceSeen"/>).</summary>
    internal double PriorCap(MissionKind kind, double baseEstimate)
    {
        var (succeeded, ended) = Experience(kind);
        return (succeeded + baseEstimate * ExperienceSeen) / (ended + ExperienceSeen);
    }

    /// <summary>PoolUseful: a candidate of the kind passed the minimum chance at this decision.</summary>
    void Passed(MissionKind kind, int day) => _passDay[kind] = day;

    /// <summary>
    /// PoolUseful: a type is useful when it can do espionage, when it can do reconnaissance while an unexplored system is in reach
    /// (<paramref name="unexplored"/>), or when a covert kind it serves had a passing candidate within <see cref="UsefulDays"/>.
    /// </summary>
    internal bool Useful(AgentType t, int day, bool unexplored) =>
        t.Kinds.Contains(MissionKind.Espionage) || t.Kinds.Contains(MissionKind.Reconnaissance) && unexplored
        || t.Kinds.Any(k => k is not (MissionKind.Espionage or MissionKind.Reconnaissance) && _passDay.TryGetValue(k, out int d) && day - d <= UsefulDays);

    /// <summary>
    /// The unexplored systems in reach for reconnaissance: as for the scouting units, unexplored systems the views
    /// show in the power base, the frontier or ring 0-1. Inferred: the doc's "in reach" read as the scouting rule's area.
    /// </summary>
    bool UnexploredInReach(GameState game, SectorMap map) =>
        game.Systems.Any(s => Knowledge.See(game, s, Side) is { Explored: false, Destroyed: false }
            && (map.PowerBase.Contains(s.Sector) || map.Frontier.Contains(s.Sector) || map.RingOf(s) <= 1));

    /// <summary>EspionageIdle: idle espionage-capable special forces above <see cref="IdleReserve"/> of the espionage-capable pool.</summary>
    internal bool EspionageIdleOn(GameState game)
    {
        if (!On(V7.EspionageIdle)) return false;
        var types = AgentKindsByType(game);
        var spies = SpecialForces(game).Where(a => types.TryGetValue(a.Type, out var kinds) && kinds.Contains(MissionKind.Espionage)).ToList();
        return spies.Count > 0 && spies.Count(a => !a.OnMission && a.Completed) > IdleReserve * spies.Count;
    }

    /// <summary>Every own special-force type's kinds (buildable or not).</summary>
    Dictionary<uint, List<MissionKind>> AgentKindsByType(GameState game) =>
        game.Definitions.SpecialForces.ToDictionary(f => f.Common.Ref, f => KindsFor(game, Side, f));

    void SaveIdle(JsonObject j)
    {
        if (_lastTrial.Count > 0) j["trials"] = new JsonArray([.. _lastTrial.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
        if (_passDay.Count > 0) j["passDays"] = new JsonArray([.. _passDay.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
    }

    void LoadIdle(JsonObject j)
    {
        if (j["trials"] is JsonArray t) foreach (var e in t) _lastTrial[(MissionKind)(uint)e![0]!] = (int)e[1]!;
        if (j["passDays"] is JsonArray p) foreach (var e in p) _passDay[(MissionKind)(uint)e![0]!] = (int)e[1]!;
    }
}
