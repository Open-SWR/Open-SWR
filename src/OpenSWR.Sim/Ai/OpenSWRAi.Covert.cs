using System.Text.Json.Nodes;
using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Covert missions of the OpenSWR AI: rescue, abduction,
/// assassination, sabotage, incite uprising and Jedi training, each run only when its worth is above 0. Inferred, openswr's own
/// estimate: arithmetic on the chances of the success rule and the foil check over the detectors the
/// side's views show; no draw.
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>At most this many covert missions run at a time (at most one of them Jedi training).</summary>
    public const int MaxCovert = 3;

    static readonly MissionKind[] CovertKinds = [MissionKind.InciteUprising, MissionKind.JediTraining, MissionKind.Rescue, MissionKind.Abduction, MissionKind.Assassination, MissionKind.Sabotage];

    /// <summary>Days a covert kind waits before it goes for the same objective again after a foil.</summary>
    public const int CovertRetryDays = 30;

    /// <summary>
    /// Foiled covert missions: (kind, objective ref) mapped to the day of the foil. The side learns of the foil from its own mission; its
    /// views often do not show the guards that foiled it (a frozen view of the system), so the same estimate would send the
    /// next team into them at once.
    /// </summary>
    readonly SortedDictionary<(MissionKind Kind, uint Target), int> _foiled = [];

    /// <summary>
    /// One detecting unit the side's views show: its detection value (foil a4) and the defender for its kind
    /// (the commander of the matching rank aboard its fleet or in the system), whose espionage (foil a2) and combat
    /// (evasion v) count only when the views show that commander (a live view); else 0.
    /// </summary>
    public readonly record struct Detector(int Value, int DefenderEspionage = 0, int DefenderCombat = 0, bool Fleet = false);

    /// <summary>
    /// What the side knows of the defence at a target system: the unit pools of the encounter resolver's phase 7 (at the target
    /// after travel) and phase 9 (execution), from the side's views. A system not seen within
    /// <see cref="ScoutAgainDays"/> takes the pools of the median known enemy system (<see cref="MedianSystem"/>).
    /// </summary>
    public sealed record CovertKnown(IReadOnlyList<Detector> Phase7, IReadOnlyList<Detector> Phase9, double MinFoil = 0)
    {
        public static readonly CovertKnown None = new([], []);
        public int Count => Phase7.Count + Phase9.Count;
    }

    /// <summary>
    /// Character value: a major character 100; else 10 + best skill / 4, + 15 with a command rank; + 20 for a Jedi. Not live
    /// (an enemy the views do not show): the definition's skills (base + variance / 2), its known-Jedi flag, no command rank.
    /// </summary>
    public static int CharacterValue(Character c, bool live)
    {
        bool major = Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter;
        var d = c.Definition.Skills;
        int best = live ? c.Skills.Take(SkillIndex.Loyalty).Max()
            : (int)new[] { d.Diplomacy, d.Espionage, d.ShipDesign, d.TroopTraining, d.FacilityDesign, d.Combat, d.Leadership }.Max(s => s.Base + s.Variance / 2);
        bool jedi = live ? c.Force > 0 : c.Definition.IsKnownJedi != 0;
        int v = major ? 100 : 10 + best / 4 + (live && c.CommandKind != 0 ? 15 : 0);
        return v + (jedi ? 20 : 0);
    }

    /// <summary>
    /// Chance (0-1) that the mission is foiled: each phase pool is one pass of foil checks, 1 - product (1 - foil p) over its units
    /// (a1 the team's average EnhancedEspionage, a2 the unit's defender's espionage, a4 its detection value, a5 the
    /// team's special forces). Inferred (openswr's estimate): the decoy stage, Force detection, betrayal and the origin phases 2 / 3
    /// are left out; the phase-7 and phase-9 passes count as independent.
    /// </summary>
    public static double FoilChance(GameState game, IReadOnlyList<Agent> team, CovertKnown known, IReadOnlyList<Agent>? decoys = null)
    {
        if (team.Count == 0) return 1;
        int a1 = team.Sum(a => Missions.Enhanced(game, a, SkillIndex.Espionage)) / team.Count, a5 = team.Count(a => Family.IsSpecialForce(a.Type));
        double through = 1;
        // the decoy stage comes first: a decoyed unit drops out of the foil pass
        var (active, _) = DecoyPass(game, decoys ?? [], known);
        int i = 0;
        foreach (var d in known.Phase7.Concat(known.Phase9))
            through *= 1 - active[i++] * Math.Clamp(Missions.FoilChance(game, a1, d.DefenderEspionage, d.Value, a5), 0, 100) / 100.0;
        return Math.Max(1 - through, known.MinFoil); // a foil mark of the system is the floor
    }

    /// <summary>
    /// Chance (0-1) that a foiled member is lost: the evasion fails, T(RLEVADTB, EnhCombat - v) averaged over the
    /// pool units (the unit pick is uniform; v = the unit's defender's combat), or, for a minor character, the injury check then
    /// the death test GeneralParam.MinorDeathChance kill it after an evasion. Inferred: the average over both pools.
    /// </summary>
    public static double LostChance(GameState game, Agent a, CovertKnown known)
    {
        var units = known.Phase7.Concat(known.Phase9).ToList();
        int combat = Missions.Enhanced(game, a, SkillIndex.Combat);
        double evade = units.Count == 0 ? 1 : units.Average(d => Math.Clamp(Missions.Table(game, "RLEVADTB", combat - d.DefenderCombat) ?? 0, 0, 100) / 100.0);
        double death = 0;
        if (Family.Of(a.Type) is >= Families.MinorCharacter and < Families.SpecialForce)
        {
            var g = game.Rules;
            death = Math.Max(g.MissionInjuryMinChance, 100 - combat) / 100.0 * g.MinorDeathChance / 100.0;
        }
        return 1 - evade + evade * Math.Clamp(death, 0, 1);
    }

    /// <summary>
    /// Worth = p_success * value - p_lost * loss: p_success = (1 - foil) *
    /// the success rule's chance; p_lost = foil * the lost chance (<see cref="LostChance"/>, the team's worst member); loss = the
    /// team's character value. A plain failed attempt costs nothing. Jedi training: no encounter (foil 0, loss 0). Pure arithmetic.
    /// <paramref name="attempt"/> = the success rule's p (0-100) for the team at the target, <paramref name="value"/> the target's.
    /// </summary>
    public static double Worth(GameState game, MissionKind kind, IReadOnlyList<Agent> team, int attempt, int value, CovertKnown known,
        IReadOnlyList<Agent>? decoys = null, double? cap = null) =>
        kind == MissionKind.JediTraining ? Math.Clamp(attempt, 0, 100) / 100.0 * value : TeamWorth(game, team, [.. team.Select(_ => attempt)], value, known, decoys ?? [], cap);

    /// <summary>
    /// Worth of a team: every agent makes its own attempt in phase 10, so the
    /// success rule's chance is 1 - product (1 - p_i) over <paramref name="attempts"/>; p_success = (1 - foil) * that, at most
    /// <paramref name="cap"/>; on a foil every member draws its own evasion: the expected loss is foil * sum lost chance * value; the
    /// decoys' expected loss comes on top (<see cref="DecoyPass"/>). One agent: the plain worth.
    /// </summary>
    public static double TeamWorth(GameState game, IReadOnlyList<Agent> team, IReadOnlyList<int> attempts, int value, CovertKnown known,
        IReadOnlyList<Agent> decoys, double? cap = null)
    {
        double foil = FoilChance(game, team, known, decoys);
        double none = attempts.Aggregate(1.0, (x, p) => x * (1 - Math.Clamp(p, 0, 100) / 100.0));
        double success = Math.Min((1 - foil) * (1 - none), cap ?? 1);
        double lost = foil * team.Sum(a => LostChance(game, a, known) * LossValue(game, [a]));
        return success * value - lost - DecoyPass(game, decoys, known).Loss;
    }

    /// <summary>
    /// Loss value of a team: its characters' value (<see cref="CharacterValue"/>); a special force its own value,
    /// its maintenance x <see cref="SpecialForceValueFactor"/> (Inferred).
    /// </summary>
    static double LossValue(GameState game, IEnumerable<Agent> team) =>
        team.OfType<Character>().Sum(c => CharacterValue(c, live: true)) + team.Where(a => Family.IsSpecialForce(a.Type)).Sum(a => game.MaintenanceCost(a.Type) * SpecialForceValueFactor);

    /// <summary>A special force's value per maintenance point.</summary>
    public const double SpecialForceValueFactor = 1.0;

    /// <summary>A team member joins when its worth gain is at least this share of the target's value; its expected loss is inside worth already.</summary>
    public const double MinJoinGain = 0.01;

    /// <summary>
    /// Decoy check p (0-1) of the decoys against one known unit: one decoy is picked at random
    /// per unit, so the mean over the decoys of T(TDECOYTB, or FDECOYTB through a fleet, EnhancedEspionage(decoy) - the unit's
    /// value - pct(the defender's EnhancedEspionage, GeneralParam.DecoyKeyPercent)) (<see cref="Missions.DecoyChance"/>). No decoy: 0.
    /// </summary>
    public static double DecoyChance(GameState game, IReadOnlyList<Agent> decoys, Detector d) =>
        decoys.Count == 0 ? 0 : decoys.Average(x => Math.Clamp(Missions.DecoyChance(game, Missions.Enhanced(game, x, SkillIndex.Espionage), d.DefenderEspionage, d.Value, d.Fleet), 0, 100) / 100.0);

    /// <summary>
    /// The decoy stage over the known units in walk order, as expected values: each unit draws
    /// one decoy at random among those left; a success decoys the unit (it drops out of the foil pass); a failure is a foil of that
    /// decoy (its lost chance at its own value) and the decoy leaves the count. Returns per unit the chance it stays in the foil pass,
    /// and the decoys' expected loss. Inferred: the remaining decoys count by their mean (the distribution over how many are left is
    /// exact, which ones are left is not); each unit is visited once.
    /// </summary>
    public static (double[] Active, double Loss) DecoyPass(GameState game, IReadOnlyList<Agent> decoys, CovertKnown known)
    {
        var units = known.Phase7.Concat(known.Phase9).ToList();
        var active = Enumerable.Repeat(1.0, units.Count).ToArray();
        if (decoys.Count == 0) return (active, 0);
        double lossEach = decoys.Average(x => LostChance(game, x, known) * LossValue(game, [x]));
        var left = new double[decoys.Count + 1];
        left[decoys.Count] = 1;
        double loss = 0;
        for (int i = 0; i < units.Count; i++)
        {
            double p = DecoyChance(game, decoys, units[i]), stays = left[0];
            var next = new double[left.Length];
            next[0] += left[0];
            for (int n = 1; n < left.Length; n++)
            {
                next[n] += left[n] * p;
                next[n - 1] += left[n] * (1 - p);
                stays += left[n] * (1 - p);
                loss += left[n] * (1 - p) * lossEach;
            }
            (active[i], left) = (stays, next);
        }
        return (active, loss);
    }

    /// <summary>p_success = (1 - the foil chance) * the success rule's chance (0-1).</summary>
    public static double Success(GameState game, IReadOnlyList<Agent> team, int attempt, CovertKnown known) =>
        (1 - FoilChance(game, team, known)) * Math.Clamp(attempt, 0, 100) / 100.0;

    // ---- covert gate from experience, teams with special forces ----

    /// <summary>The covert kinds whose own observed success rate caps the chance (CovertExperience): incite, rescue, abduction.</summary>
    static readonly MissionKind[] ExperienceKinds = [MissionKind.InciteUprising, MissionKind.Rescue, MissionKind.Abduction];

    /// <summary>Until this many of a kind have ended, the observed rate is the prior <see cref="ExperiencePrior"/>.</summary>
    public const int ExperienceSeen = 5;
    public const double ExperiencePrior = 0.1;

    /// <summary>Own ended missions per experience kind, as (succeeded, ended): missions seen succeed (TaskStatus 3) until they end; missions counted.</summary>
    readonly SortedDictionary<MissionKind, (int Succeeded, int Ended)> _experience = [];
    readonly SortedSet<uint> _succeeding = [], _counted = [];

    /// <summary>The observed success rate of a kind (the prior until <see cref="ExperienceSeen"/> have ended).</summary>
    internal double ObservedRate(MissionKind kind) =>
        _experience.TryGetValue(kind, out var e) && e.Ended >= ExperienceSeen ? (double)e.Succeeded / e.Ended : ExperiencePrior;

    internal (int Succeeded, int Ended) Experience(MissionKind kind) => _experience.GetValueOrDefault(kind);

    /// <summary>Test hook: one ended mission of the kind.</summary>
    internal void RecordExperience(MissionKind kind, bool succeeded)
    {
        var (s, n) = _experience.GetValueOrDefault(kind);
        _experience[kind] = (s + (succeeded ? 1 : 0), n + 1);
    }

    /// <summary>An own mission's TaskStatus 3 (success seen) or end (CompletionStatus != 0): the experience counts.</summary>
    internal void ObserveMission(Mission m)
    {
        if (!ExperienceKinds.Contains(m.Kind) || m.Owner != Side) return;
        if (m.TaskStatus == MissionTaskStatus.Succeeded) _succeeding.Add(m.Ref);
        if (m.CompletionStatus == MissionCompletion.None || !_counted.Add(m.Ref)) return;
        var (s, n) = _experience.GetValueOrDefault(m.Kind);
        _experience[m.Kind] = (s + (_succeeding.Remove(m.Ref) ? 1 : 0), n + 1);
    }

    void SaveExperience(JsonObject j)
    {
        if (_experience.Count > 0) j["covertSeen"] = new JsonArray([.. _experience.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value.Succeeded, e.Value.Ended))]);
        if (_succeeding.Count > 0) j["covertSucceeding"] = new JsonArray([.. _succeeding.Select(r => (JsonNode)r)]);
        if (_counted.Count > 0) j["covertCounted"] = new JsonArray([.. _counted.Select(r => (JsonNode)r)]);
    }

    void LoadExperience(JsonObject j)
    {
        if (j["covertSeen"] is JsonArray seen) foreach (var e in seen) _experience[(MissionKind)(uint)e![0]!] = ((int)e[1]!, (int)e[2]!);
        if (j["covertSucceeding"] is JsonArray s) foreach (var r in s) _succeeding.Add((uint)r!);
        if (j["covertCounted"] is JsonArray c) foreach (var r in c) _counted.Add((uint)r!);
    }

    /// <summary>
    /// The greedy team: from <paramref name="first"/>, repeatedly add the member of
    /// <paramref name="pool"/> - a further agent, or a special force as a decoy (only with a known detecting unit) - whose addition
    /// raises <see cref="TeamWorth"/> most; stop when none raises it. <paramref name="valid"/> checks a team (eligibility).
    /// </summary>
    internal (List<Agent> Team, List<Agent> Decoys, double Worth) GrowTeam(GameState game, Known k, MissionKind kind, GameObject target, int value, CovertKnown known,
        double? cap, Agent first, IReadOnlyList<Agent> pool, Func<List<Agent>, List<Agent>, bool> valid)
    {
        var team = new List<Agent> { first };
        var decoys = new List<Agent>();
        var attempts = new Dictionary<Agent, int>();
        double W(List<Agent> t, List<Agent> d) => TeamWorth(game, t, [.. t.Select(a => attempts.TryGetValue(a, out int p) ? p : attempts[a] = Attempt(game, k, kind, a, target))], value, known, d, cap);
        double worth = W(team, decoys);
        int max = TeamCap(kind, target);
        var rejected = new HashSet<(Agent, bool)>();
        while (team.Count + decoys.Count < max)
        {
            (List<Agent> Team, List<Agent> Decoys, Agent Member, bool Decoy, double Worth)? best = null;
            foreach (var a in pool.Where(a => !team.Contains(a) && !decoys.Contains(a)))
            {
                bool force = Family.IsSpecialForce(a.Type);
                foreach (bool decoy in force && known.Count > 0 ? new[] { false, true } : [false])
                {
                    if (rejected.Contains((a, decoy))) continue;
                    // mixed teams: with a named character in the team every special force is a decoy, only characters are agents
                    List<Agent> t, d;
                    if (decoy) (t, d) = (team, [.. decoys, a]);
                    else if (force && team.Any(x => x is Character)) continue;
                    else if (!force && team.Any(x => Family.IsSpecialForce(x.Type)))
                        (t, d) = ([.. team.Where(x => x is Character), a], [.. decoys, .. team.Where(x => Family.IsSpecialForce(x.Type))]);
                    else (t, d) = ([.. team, a], decoys);
                    double w = W(t, d);
                    // a member joins when its gain is at least MinJoinGain of the target's value
                    if (w > worth && w - worth >= MinJoinGain * value && w > (best?.Worth ?? double.MinValue)) best = (t, d, a, decoy, w);
                }
            }
            if (best is not { } add) break;
            if (!valid(add.Team, add.Decoys))
            {
                rejected.Add((add.Member, add.Decoy));
                continue;
            }
            (team, decoys, worth) = (add.Team, add.Decoys, add.Worth);
        }
        return (team, decoys, worth);
    }

    /// <summary>
    /// The team cap: <see cref="MaxTeamSmall"/> for espionage, reconnaissance,
    /// sabotage, incite and the abduction, rescue or assassination of a minor character; none (worth only) for a major character's.
    /// </summary>
    internal static int TeamCap(MissionKind kind, GameObject target) =>
        kind is MissionKind.Rescue or MissionKind.Abduction or MissionKind.Assassination && Family.Of(target.Type) is >= Families.MajorCharacter and < Families.MinorCharacter ? int.MaxValue : MaxTeamSmall;

    /// <summary>Members in all of a team that is not after a major character.</summary>
    public const int MaxTeamSmall = 3;

    /// <summary>
    /// The single agent of a covert candidate: the first of the 3 best-suited characters (by the kind's skill) that passes the
    /// stricter gate, has worth above 0 and is valid; and the same among <paramref name="forces"/>: the
    /// special force is chosen when its worth is at least the character's. Null: none.
    /// </summary>
    internal Covert? FirstAgent(GameState game, Known k, MissionKind kind, GameObject target, int value, CovertKnown known, double? cap,
        IEnumerable<Agent> characters, IEnumerable<Agent> forces, Func<List<Agent>, bool> valid)
    {
        Covert? Try(Agent c)
        {
            List<Agent> team = [c];
            int attempt = Attempt(game, k, kind, c, target);
            double success = Math.Min(Success(game, team, attempt, known), cap ?? 1);
            if (success < MinSuccess(kind, target)) return null; // the stricter gate
            double worth = Worth(game, kind, team, attempt, value, known, cap: cap);
            if (worth <= 0 || !valid(team)) return null;
            return new(kind, target, team, worth, success);
        }
        Covert? First(IEnumerable<Agent> agents)
        {
            foreach (var a in agents.OrderByDescending(a => CovertSkill(game, kind, a)).ThenBy(a => a.Key).Take(3))
                if (Try(a) is { } x) return x;
            return null;
        }
        var best = First(characters);
        if (First(forces) is { } force && (best is null || force.Worth >= best.Value.Worth)) best = force;
        return best;
    }

    /// <summary>The kinds a special force may run as the agent (Teams; espionage goes through <c>Send</c>).</summary>
    static readonly MissionKind[] TeamKinds = [MissionKind.InciteUprising, MissionKind.Rescue, MissionKind.Abduction, MissionKind.Assassination, MissionKind.Sabotage];

    /// <summary>
    /// Covert special forces wanted (Teams): the special forces of the greedy teams of the last covert decision's candidates, plus
    /// one per candidate where no special force was free (Inferred: a special force's loss value, its maintenance, is far below a
    /// character's, so it would be preferred). No fixed count: the free maintenance bounds the training.
    /// </summary>
    internal int SpecialForceDemand { get; private set; }

    /// <summary>Own special forces (Existing; on a mission or under construction too).</summary>
    List<Agent> SpecialForces(GameState game) =>
        [.. game.Created.OfType<Agent>().Where(a => a.Owner == Side && a.Existing && Family.IsSpecialForce(a.Type)).OrderBy(a => a.Key)];

    /// <summary>Own special forces free for a mission now (<see cref="FreeUnit"/>).</summary>
    List<Agent> FreeSpecialForces(GameState game, HashSet<GameObject> used) => [.. SpecialForces(game).Where(a => FreeUnit(a, used))];

    /// <summary>The special force to train for covert work: buildable, offered for the most team kinds (Intel: and espionage), then the highest (espionage + combat) base per maintenance.</summary>
    Data.Dat.SdRecord? CovertForce(GameState game)
    {
        var buildable = Research.Buildable(game, Side, Families.TrainingFacility).Where(r => Family.IsSpecialForce(r.Common.Ref)).Select(r => r.Common.Ref).ToHashSet();
        // the type the eligibility lists offer for the most team kinds (a type's MissionMask within the mission's SpecialForceMask)
        MissionKind[] kinds = [.. On(V7.Teams) ? TeamKinds : [], .. On(V7.Intel) ? new MissionKind[] { MissionKind.Espionage } : []];
        int Kinds(SpecialForceRecord f) => kinds.Count(kind => TypeOf(game, kind) is { } m && f.MissionMask != 0 && m.AllowsSpecialForce(f.MissionMask));
        var best = game.Definitions.SpecialForces.Where(f => buildable.Contains(f.Common.Ref) && Kinds(f) > 0)
            .OrderByDescending(Kinds).ThenByDescending(f => (double)(f.Skills.Espionage.Base + f.Skills.Combat.Base) / Math.Max(1, f.MaintenanceCost)).ThenBy(f => f.Common.Ref).FirstOrDefault();
        return best is null ? null : game.Definitions.Resolve(best.Common.Ref);
    }

    // ---- stricter covert gate ----

    /// <summary>The covert kinds that need p_success of at least <see cref="MinCovertSuccess"/> (a major's rescue <see cref="MinMajorRescueSuccess"/>).</summary>
    static readonly MissionKind[] GatedKinds = [MissionKind.InciteUprising, MissionKind.Rescue, MissionKind.Abduction, MissionKind.Assassination, MissionKind.Sabotage];
    public const double MinCovertSuccess = 0.5, MinMajorRescueSuccess = 0.3;
    /// <summary>After <see cref="PauseFoils"/> foils of one kind within <see cref="PauseDays"/>, side-wide, that kind pauses <see cref="PauseDays"/>.</summary>
    public const int PauseFoils = 3, PauseDays = 90;

    /// <summary>Foil days per covert kind (within <see cref="PauseDays"/>), and the paused kinds, each with the first day it runs again.</summary>
    readonly SortedDictionary<MissionKind, List<int>> _kindFoils = [];
    readonly SortedDictionary<MissionKind, int> _paused = [];

    /// <summary>A foil of an own covert mission of this kind: counted; the third within <see cref="PauseDays"/> pauses the kind.</summary>
    internal void KindFoiled(MissionKind kind, int day)
    {
        if (!_kindFoils.TryGetValue(kind, out var days)) _kindFoils[kind] = days = [];
        days.RemoveAll(d => day - d >= PauseDays);
        days.Add(day);
        if (days.Count < PauseFoils) return;
        _paused[kind] = day + PauseDays;
        days.Clear();
    }

    /// <summary>The kind is paused on that day.</summary>
    internal bool Paused(MissionKind kind, int day) => _paused.TryGetValue(kind, out int until) && day < until;

    /// <summary>The minimum p_success of a covert kind for a target (0 = no gate).</summary>
    internal static double MinSuccess(MissionKind kind, GameObject target) =>
        kind == MissionKind.Rescue && Family.Of(target.Type) is >= Families.MajorCharacter and < Families.MinorCharacter ? MinMajorRescueSuccess : GatedKinds.Contains(kind) ? MinCovertSuccess : 0;

    void SaveGate(JsonObject j)
    {
        if (_kindFoils.Any(e => e.Value.Count > 0))
            j["kindFoils"] = new JsonArray([.. _kindFoils.Where(e => e.Value.Count > 0)
                .Select(e => (JsonNode)new JsonArray([(uint)e.Key, .. e.Value.Select(d => (JsonNode)d)]))]);
        if (_paused.Count > 0) j["paused"] = new JsonArray([.. _paused.Select(e => (JsonNode)new JsonArray((uint)e.Key, e.Value))]);
    }

    void LoadGate(JsonObject j)
    {
        if (j["kindFoils"] is JsonArray foils)
            foreach (var e in foils) _kindFoils[(MissionKind)(uint)e![0]!] = [.. e.AsArray().Skip(1).Select(d => (int)d!)];
        if (j["paused"] is JsonArray paused) foreach (var e in paused) _paused[(MissionKind)(uint)e![0]!] = (int)e[1]!;
    }

    /// <summary>Pauses that ended are dropped at the decision.</summary>
    void PruneGate(int day)
    {
        foreach (var kind in _paused.Where(e => e.Value <= day).Select(e => e.Key).ToList()) _paused.Remove(kind);
        foreach (var days in _kindFoils.Values) days.RemoveAll(d => day - d >= PauseDays);
        foreach (var kind in _kindFoils.Where(e => e.Value.Count == 0).Select(e => e.Key).ToList()) _kindFoils.Remove(kind);
    }

    static int Detection(GameState game, uint type) => game.Definitions.Resolve(type) is { } r ? r.Get(SdField.Detection) : 0;

    /// <summary>A system not seen within <see cref="ScoutAgainDays"/> (its view not live, not stamped since).</summary>
    bool Stale(GameState game, StarSystem s) => !Knowledge.IsLive(s, Side) && (!_seen.TryGetValue(s.Ref, out int day) || game.Day - day >= ScoutAgainDays);

    /// <summary>
    /// The known pools at a system for an objective, from the side's views, Usable units only:
    /// - phase 7 (`A`): with an enemy fleet there (`F`) and no own Usable ion cannon (ion cannon family 34, Index 1, `C`): the enemy
    ///   fleets' capital ships and their fighters;
    /// - phase 9: an objective that is the system or sits directly in it (`T`): the system's own troops when the enemy
    ///   owns it (`O`; no fighters); else, with `F`: the fleets' capital ships and fighters again.
    /// Defenders: the commander of the unit's kind (CommandKind 2 ships, 1 fighters, 3 troops) aboard the fleet's ships, else directly
    /// in the system, only if the views show it live. A stale system takes <paramref name="median"/>'s pools (none without one).
    /// </summary>
    internal CovertKnown KnownAt(GameState game, Known k, StarSystem s, bool objectiveInSystem, StarSystem? median) =>
        (Stale(game, s) ? median is null || median == s ? CovertKnown.None : Pools(game, k, median, objectiveInSystem) : Pools(game, k, s, objectiveInSystem))
        with { MinFoil = FoilMark(s, game.Day) };

    /// <summary>The pools of <see cref="KnownAt"/> at a system the side has seen (no foil mark).</summary>
    CovertKnown Pools(GameState game, Known k, StarSystem s, bool objectiveInSystem)
    {
        var here = k.Contents(s);
        bool owned = Knowledge.See(game, s, Side).Owner == Enemy;
        bool cannon = here.Any(v => v.Owner == Side && v.Usable && v.Object.Type == Refs.IonCannonType);
        var commanders = here.Where(v => v.Object is Character && ScreenViews.Sees(v.Object, Side)).Select(v => (Character)v.Object).Where(c => c.Owner == Enemy).ToList();
        Detector Unit(GameObject unit, int kind, IEnumerable<Character> aboard)
        {
            var c = aboard.Concat(owned ? commanders : []).FirstOrDefault(x => x.CommandKind == kind);
            return new(Detection(game, unit.Type), c is null ? 0 : Missions.Enhanced(game, c, SkillIndex.Espionage), c is null ? 0 : Missions.Enhanced(game, c, SkillIndex.Combat));
        }
        var fleet = new List<Detector>();
        foreach (var f in here.Where(v => v.Object is Fleet && v.Owner == Enemy && v.Usable))
        {
            var ships = k.Contents(f.Object).Where(v => Family.IsCapitalShip(v.Object.Type) && v.Usable).ToList();
            var aboard = ships.SelectMany(x => k.Contents(x.Object)).Where(v => v.Object is Character && ScreenViews.Sees(v.Object, Side)).Select(v => (Character)v.Object).ToList();
            foreach (var ship in ships)
            {
                fleet.Add(Unit(ship.Object, 2, aboard) with { Fleet = true });
                fleet.AddRange(k.Contents(ship.Object).Where(v => v.In(Families.Fighter, Families.Headquarters) && v.Usable).Select(v => Unit(v.Object, 1, aboard) with { Fleet = true }));
            }
        }
        var phase7 = fleet.Count > 0 && !cannon ? fleet : [];
        var phase9 = objectiveInSystem
            ? owned ? [.. here.Where(v => v.Owner == Enemy && v.Usable && v.In(Families.Troop, Families.CapitalShip)).Select(v => Unit(v.Object, 3, []))] : new List<Detector>()
            : fleet;
        return new(phase7, phase9);
    }

    /// <summary>The median known enemy system of this decision by the size of its known pools (seen within <see cref="ScoutAgainDays"/>), for stale targets.</summary>
    internal StarSystem? MedianSystem(GameState game, Known k)
    {
        var known = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy && !Stale(game, s))
            .Select(s => (s, Pools(game, k, s, true).Count)).OrderBy(x => x.Count).ThenBy(x => x.s.Key).ToList();
        return known.Count == 0 ? null : known[known.Count / 2].s;
    }

    // ---- foil marks per system ----

    /// <summary>Days a foil mark lasts after the system's last foil.</summary>
    public const int FoilMarkDays = 60;

    /// <summary>
    /// Foil marks per target system ref: (the day of its last foil, foils while marked). The side learns of a foil from its own
    /// mission; a live view can still miss the guards that foiled it, so only the days clear a mark.
    /// </summary>
    readonly SortedDictionary<uint, (int Day, int Count)> _foilMarks = [];

    /// <summary>A foil at the system: a new mark, or one more foil on a running one (its 60 days start again).</summary>
    internal void MarkFoil(StarSystem s, int day) =>
        _foilMarks[s.Ref] = _foilMarks.TryGetValue(s.Ref, out var m) && day - m.Day < FoilMarkDays ? (day, m.Count + 1) : (day, 1);

    /// <summary>The mark's foil chance on a day: 0.5, + 0.15 per further foil while marked, at most 0.9; 0 without a running mark.</summary>
    internal double FoilMark(StarSystem s, int day) =>
        _foilMarks.TryGetValue(s.Ref, out var m) && day - m.Day < FoilMarkDays ? Math.Min(0.9, 0.5 + 0.15 * (m.Count - 1)) : 0;

    /// <summary>Test hook: the side saw the system on that day.</summary>
    internal void MarkSeen(StarSystem s, int day) => _seen[s.Ref] = day;

    /// <summary>The success rule's p for one member at the target, from the side's knowledge (enemy skills: live values, else the definition's).</summary>
    internal int Attempt(GameState game, Known k, MissionKind kind, Agent a, GameObject target)
    {
        int E(int skill) => Missions.Enhanced(game, a, skill);
        int T(string table, int key) => Missions.Table(game, table, key) ?? 0;
        int Combat(Character c) => ScreenViews.Sees(c, Side) ? Missions.Enhanced(game, c, SkillIndex.Combat) : (int)c.Definition.Skills.Combat.Base;
        var sys = target as StarSystem;
        int opp = sys is null ? 0 : Side == Owner.Alliance ? 100 - Knowledge.See(game, sys, Side).Support : Knowledge.See(game, sys, Side).Support;
        int st = sys is null ? 0 : k.Contents(sys).Count(v => v.Object.Type == Refs.Stormtroopers && v.Owner == Owner.Empire && v.Usable);
        return kind switch
        {
            MissionKind.InciteUprising => T("INCTMSTB", E(SkillIndex.Leadership) - opp - st),
            MissionKind.Rescue => T("RESCMSTB", E(SkillIndex.Combat)),
            MissionKind.Abduction when target is Character c => T("ABDCMSTB", E(SkillIndex.Combat) - Combat(c)),
            MissionKind.Assassination when target is Character c => T("ASSNMSTB", E(SkillIndex.Combat) - Combat(c)),
            MissionKind.Sabotage => T("SBTGMSTB", (E(SkillIndex.Espionage) + E(SkillIndex.Combat)) / 2),
            _ => 0,
        };
    }

    /// <summary>The covert skill of a kind, for choosing the best-suited member.</summary>
    static int CovertSkill(GameState game, MissionKind kind, Agent a) => kind switch
    {
        MissionKind.InciteUprising => Missions.Enhanced(game, a, SkillIndex.Leadership),
        MissionKind.Sabotage => (Missions.Enhanced(game, a, SkillIndex.Espionage) + Missions.Enhanced(game, a, SkillIndex.Combat)) / 2,
        _ => Missions.Enhanced(game, a, SkillIndex.Combat),
    };

    /// <summary>One covert candidate: kind, target, team, the target's value, worth.</summary>
    internal readonly record struct Covert(MissionKind Kind, GameObject Target, List<Agent> Team, double Worth, double Success = -1, List<Agent>? Decoys = null, bool Trial = false);

    /// <summary>
    /// The covert candidates with worth above 0, highest first. Per target
    /// the best-suited free character (by the type's skill) for whom the eligibility list offers the type there. <paramref name="rescueOnly"/>:
    /// only rescues of major characters (they go right after "hold home").
    /// </summary>
    internal List<Covert> CovertCandidates(GameState game, Known k, SectorMap map, List<Character> free, ISet<GameObject?> targeted, bool rescueOnly,
        HashSet<GameObject>? usedAgents = null)
    {
        var result = new List<Covert>();
        // Teams: the idle own special forces (agents and decoys); one candidate each
        var freeForces = TeamsOn ? FreeSpecialForces(game, usedAgents ?? []) : [];
        var reserved = new HashSet<Agent>();
        int demand = 0;
        var pending = new List<(int Index, int Value, CovertKnown Known, double? Cap, uint Type, GameObject Target)>();
        var usedNow = new HashSet<Agent>();
        // grows the pending teams, best single-agent worth first (members are not taken from a better mission)
        void GrowPending()
        {
            foreach (var (index, value, known, cap, typeRef, target) in pending.OrderByDescending(x => result[x.Index].Worth).ThenBy(x => x.Index))
            {
                var chosen = result[index];
                if (chosen.Team.Any(a => a is Character c ? usedNow.Contains(c) : false))
                {
                    if (On(V7.Agents)) result[index] = chosen with { Worth = double.NegativeInfinity }; // its agent went to a better mission
                    continue;
                }
                var pool = (chosen.Trial ? [] : free.Cast<Agent>()).Concat(freeForces).Where(a => !reserved.Contains(a) && !usedNow.Contains(a) && !chosen.Team.Contains(a)).Distinct().ToList();
                var (team, decoys, worth) = GrowTeam(game, k, chosen.Kind, target, value, known, cap, chosen.Team[0], pool,
                    (t, d) => MissionEligibility.Validate(game, CommandType.CreateMission, Side, typeRef, t, d, target) is null);
                if (On(V7.Agents))
                {
                    // Character agents take special-force decoys against a known detector; a retry needs decoys (rules 7, 4)
                    var left = pool.Where(a => !team.Contains(a) && !decoys.Contains(a));
                    if (AgentDecoys(game, chosen.Kind, target, known, team, decoys, left, (t, d) => MissionEligibility.Validate(game, CommandType.CreateMission, Side, typeRef, t, d, target) is null, out int shortBy, k, value, cap) is not { } agents)
                    {
                        _decoyShort += shortBy;
                        result[index] = chosen with { Worth = double.NegativeInfinity }; // waits for decoys
                        continue;
                    }
                    (team, decoys) = agents;
                    worth = TeamWorth(game, team, [.. team.Select(a => Attempt(game, k, chosen.Kind, a, target))], value, known, decoys, cap);
                    chosen = chosen with { Success = TeamSuccess(game, k, chosen.Kind, target, team, decoys, known, cap) };
                }
                result[index] = chosen with { Team = team, Decoys = decoys, Worth = worth };
                foreach (var a in team.Concat(decoys)) usedNow.Add(a);
            }
            pending.Clear();
        }
        if (free.Count == 0 && freeForces.Count == 0) return result;
        var enemySystems = game.Systems.Where(s => Knowledge.See(game, s, Side) is { Owner: var o, Destroyed: false } && o == Enemy).ToList();
        var median = MedianSystem(game, k); // stale targets take its pools; the foil is computed per candidate team
        bool Important(StarSystem s) => Targets().Contains(s) || map.PowerBase.Contains(s.Sector) || map.Frontier.Contains(s.Sector);

        void Consider(MissionKind kind, GameObject target, StarSystem at, int value)
        {
            void Note(string outcome) => Probe?.Considered.Add((kind, target, outcome)); // measurement only
            if (targeted.Contains(target) || value <= 0 || TypeOf(game, kind) is not { } type || Paused(kind, game.Day))
            {
                Note(targeted.Contains(target) ? "own mission running" : Paused(kind, game.Day) ? "kind paused" : "no value or type");
                return;
            }
            if (_foiled.TryGetValue((kind, target.Ref), out int foiled) && game.Day - foiled < CovertRetryDays)
            {
                Note("foiled within CovertRetryDays");
                return;
            }
            // T: the objective is the system or sits directly in it (its view parent)
            // Intel: look before acting; the system's espionage is ordered first
            if (On(V7.Intel) && !Fresh(game, at))
            {
                if (!_lookFirst.Contains(at)) _lookFirst.Add(at);
                return;
            }
            var known = KnownAt(game, k, at, target is StarSystem || Knowledge.ViewParent(game, target, Side) is StarSystem, median);
            known = Assume(game, k, at, known, median); // Decoys: assumed detectors on enemy-held systems
            // CovertExperience: the observed success rate caps the chance
            double? cap = On(V7.CovertExperience) && ExperienceKinds.Contains(kind) ? ObservedRate(kind) : null;
            bool teams = TeamsOn && TeamKinds.Contains(kind);
            var forces = teams ? freeForces.Where(a => !reserved.Contains(a)).ToList() : [];
            bool Fits(List<Agent> team) => MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, team, [], target) is null;
            // ExploreTrial (no cap, a special-force agent) or ExplorePrior (the cap from the base estimate)
            bool trial = cap is not null && TrialOpen(kind, game.Day);
            if (trial) cap = null;
            else if (cap is not null && On(V7.ExplorePrior) && FirstAgent(game, k, kind, target, value, known, null, free, forces, Fits) is { } raw) cap = PriorCap(kind, raw.Success);
            var best = FirstAgent(game, k, kind, target, value, known, cap, trial ? [] : free, forces, Fits);
            if (trial && best is { } tried) best = tried with { Trial = true };
            if (best is not null && On(V7.PoolUseful)) Passed(kind, game.Day); // PoolUseful
            // Teams: then decoys and further agents while they raise the worth
            if (teams)
            {
                if (forces.Count == 0) demand++;
                if (best is { } single && Family.IsSpecialForce(single.Team[0].Type)) reserved.Add(single.Team[0]);
                // the greedy team grows after every candidate is known, in worth order
                if (best is not null)
                    pending.Add((result.Count, value, known, cap, type.Common.Ref, target));
            }
            if (best is { } pick) result.Add(pick);
            Note(best is null ? "no agent passed the gate or worth" : "candidate");
        }

        // rescue: own characters the side's views show captured and arrived (not Enroute) at a system
        foreach (var c in game.Characters.Where(c => Finders.DefinitionSide(c) == Side).OrderBy(c => c.Key))
            if (Knowledge.See(game, c, Side) is { Captured: true, Destroyed: false, Enroute: false, Location: StarSystem at }
                && (!rescueOnly || Family.Of(c.Type) is >= Families.MajorCharacter and < Families.MinorCharacter))
                Consider(MissionKind.Rescue, c, at, CharacterValue(c, live: true));
        if (rescueOnly)
        {
            GrowPending();
            if (On(V7.Agents)) return Rank(result.Where(x => !double.IsNegativeInfinity(x.Worth)));
            return [.. result.OrderByDescending(x => x.Worth)];
        }

        // abduction / assassination: enemy characters the views show at a system
        foreach (var c in game.Characters.OrderBy(c => c.Key))
            if (Knowledge.See(game, c, Side) is { Owner: var o, Captured: false, Destroyed: false, Location: StarSystem at } && o == Enemy)
            {
                int value = CharacterValue(c, ScreenViews.Sees(c, Side));
                Consider(MissionKind.Abduction, c, at, value + 10);
                if (!(Hunting && EnemyObjective(c))) Consider(MissionKind.Assassination, c, at, value); // Hunt: capture, never assassination
            }
        // sabotage: per known enemy system the most valuable unit or facility its views show (stale systems: contents unknown)
        foreach (var s in enemySystems.Where(s => !Stale(game, s)).OrderBy(s => s.Key))
        {
            int Score(GameObject o) => (Family.Of(o.Type) is >= Families.Headquarters and < Families.MajorCharacter ? game.MaintenanceCost(o.Type) * 5 : k.Value(o.Type)) * (Important(s) ? 2 : 1);
            var units = k.Contents(s).Where(v => v.Owner == Enemy && v.Completed && Family.Of(v.Object.Type) is >= Families.Troop and < Families.MajorCharacter && !DeathStar.IsDeathStar(v.Object.Type))
                .Select(v => v.Object)
                .Concat(k.Contents(s).Where(v => v.Owner == Enemy && v.Object is Fleet).SelectMany(f => k.Contents(f.Object))
                    .Where(v => v.Completed && Family.IsCapitalShip(v.Object.Type) && !DeathStar.IsDeathStar(v.Object.Type)).Select(v => v.Object));
            if (units.OrderByDescending(Score).ThenBy(o => o.Key).FirstOrDefault() is { } best) Consider(MissionKind.Sabotage, best, s, Score(best));
        }
        // incite: enemy systems where the views show the enemy's support share below 50
        foreach (var s in enemySystems.OrderBy(s => s.Key))
            if (Knowledge.See(game, s, Side) is { Populated: true } v && (Side == Owner.Alliance ? 100 - v.Support : v.Support) < 50)
                Consider(MissionKind.InciteUprising, s, s, map.PowerBase.Contains(s.Sector) || map.Frontier.Contains(s.Sector) ? 30 : 10);
        GrowPending();
        if (!rescueOnly && On(V7.Teams)) SpecialForceDemand = demand + result.Sum(x => x.Team.Concat(x.Decoys ?? []).Count(a => Family.IsSpecialForce(a.Type)));
        if (On(V7.Agents))
        {
            // pivots (rule 3); abduction / assassination only for pivot targets or the top band (rule 6); rank (rule 5)
            if (Probe is { } probe)
                foreach (var x in result)
                {
                    probe.CandidatesBefore[x.Kind] = probe.CandidatesBefore.GetValueOrDefault(x.Kind) + 1;
                    if (double.IsNegativeInfinity(x.Worth)) probe.Dropped.TryAdd((x.Kind, x.Target.Ref), "waits for decoys or its agent went elsewhere");
                }
            result.RemoveAll(x => double.IsNegativeInfinity(x.Worth));
            StartPivots(game, result);
            double top = TopWorth(result);
            if (Probe is { } probe2)
                foreach (var x in result.Where(x => x.Kind is MissionKind.Abduction or MissionKind.Assassination && x.Worth < top && _pivots.All(p => p.Target != x.Target.Ref)))
                    probe2.Dropped.TryAdd((x.Kind, x.Target.Ref), "abduction / assassination band rule");
            result.RemoveAll(x => x.Kind is MissionKind.Abduction or MissionKind.Assassination && x.Worth < top && _pivots.All(p => p.Target != x.Target.Ref) && !(Hunting && EnemyObjective(x.Target))); // (Hunt: kept)
            if (Probe is { } probe3)
            {
                foreach (var x in result) probe3.CandidatesAfter[x.Kind] = probe3.CandidatesAfter.GetValueOrDefault(x.Kind) + 1;
                probe3.Final.AddRange(result);
            }
            _plannedAt.Clear();
            foreach (var x in result)
                if ((x.Target as StarSystem ?? Knowledge.Location(game, x.Target, Side) as StarSystem) is { } at) _plannedAt.Add(at.Ref);
            return Rank(result);
        }
        return [.. result.OrderByDescending(x => x.Worth).ThenBy(x => x.Kind).ThenBy(x => x.Target.Key)];
    }

    /// <summary>Jedi training (worth 40, no loss): a free teacher and a free student at the same system, when the list offers it there.</summary>
    internal Covert? JediTraining(GameState game, List<Character> free)
    {
        if (TypeOf(game, MissionKind.JediTraining) is not { } type) return null;
        foreach (var teacher in free.Where(c => c.Definition.IsJediTrainer != 0).OrderByDescending(c => c.Force).ThenBy(c => c.Key))
            foreach (var student in free.Where(c => c != teacher && c.Force > 0 && c.Force < teacher.Force && Missions.Location(c) == Missions.Location(teacher))
                         .OrderBy(c => c.Key))
            {
                if (Missions.Location(teacher) is not StarSystem at) break;
                List<Agent> team = [teacher, student];
                if (MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, team, [], at) is not null) continue;
                double worth = Worth(game, MissionKind.JediTraining, team, teacher.Force - student.Force, 40, CovertKnown.None);
                if (worth > 0) return new(MissionKind.JediTraining, at, team, worth);
            }
        return null;
    }
}
