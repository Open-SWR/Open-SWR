using OpenSWR.Data.Dat;

namespace OpenSWR.Sim.Ai;

/// <summary>
/// Rule 5 of the OpenSWR AI and the sector rings it shares with rule 4: home
/// sector, ring distance, power base, frontier. Missions are CreateMission commands built as the mission window builds them (Team = one
/// character, ArgRef = the target, Fields [0, 0, type]); a type is used at a target only when <see cref="MissionEligibility"/>
/// passes it there (the window's list).
/// </summary>
public sealed partial class OpenSWRController
{
    /// <summary>Days after which a system the side has not seen counts as unscouted again.</summary>
    public const int ScoutAgainDays = 60;
    /// <summary>At most this many characters scout at a time.</summary>
    public const int MaxScouts = 2;
    /// <summary>Fewer free characters than this after steps 1-4: one goes recruiting.</summary>
    public const int RecruitBelow = 4;

    /// <summary>
    /// The last decision day each system's view was live for the side. Missing = never seen
    /// since the AI started keeping it: views store no "last seen" day, so the controller records it at each decision.
    /// </summary>
    readonly SortedDictionary<uint, int> _seen = [];

    void RecordSeen(GameState game)
    {
        foreach (var s in game.Systems.Where(s => Knowledge.IsLive(s, Side))) _seen[s.Ref] = game.Day;
    }

    /// <summary>
    /// Espionage and reconnaissance refresh only the side's frozen view of their system, so a success (the
    /// mission's TaskStatus message with TaskStatus 3) stamps its system as seen, and an espionage's extra system (EspionageExtraSystem message)
    /// too. Then the base acknowledgement.
    /// </summary>
    internal override void Receive(GameState game, ViewMessageType type, GameObject o, GameObject? a, GameObject? b)
    {
        if (type == ViewMessageType.TaskStatus && o is Mission { Kind: MissionKind.Espionage or MissionKind.Reconnaissance, TaskStatus: MissionTaskStatus.Succeeded, Objective: StarSystem s } m && m.Owner == Side) _seen[s.Ref] = game.Day;
        if (type is ViewMessageType.TaskStatus or ViewMessageType.CompletionStatus && o is Mission seen && seen.Owner == Side) ObserveMission(seen); // kept for every feature set
        if (type is ViewMessageType.TaskStatus or ViewMessageType.CompletionStatus && o is Mission retried && retried.Owner == Side) ObserveRetry(retried); // measurement
        if (type == ViewMessageType.TaskStatus && o is Mission { Kind: MissionKind.Recruitment, TaskStatus: MissionTaskStatus.Succeeded } recruited && recruited.Owner == Side) Recruited++; // the Recruit count
        if (type == ViewMessageType.EspionageExtraSystem && o is Mission x && x.Owner == Side && a is StarSystem extra) _seen[extra.Ref] = game.Day;
        // rule 4: every foiled own mission (espionage too) marks its objective
        if (On(V7.Agents) && type == ViewMessageType.CompletionStatus && o is Mission { CompletionStatus: MissionCompletion.FoiledEnRoute or MissionCompletion.Foiled, Objective: { } foiledAt } any && any.Owner == Side) _foiledTargets.Add(foiledAt.Ref);
        // a covert mission foiled (CompletionStatus 3 / 4 in the CompletionStatus message): its kind and objective wait CovertRetryDays
        if (type == ViewMessageType.CompletionStatus && o is Mission { CompletionStatus: MissionCompletion.FoiledEnRoute or MissionCompletion.Foiled, Objective: { } objective } f && f.Owner == Side && CovertKinds.Contains(f.Kind))
        {
            _foiled[(f.Kind, objective.Ref)] = game.Day;
            if ((f.TargetLocation ?? Knowledge.Location(game, objective, Side)) is StarSystem at) MarkFoil(at, game.Day);
            KindFoiled(f.Kind, game.Day);
        }
        base.Receive(game, type, o, a, b);
    }

    // ---- sectors ----

    /// <summary>The side's sector picture of one decision: home sector, ring per sector, power base, frontier.</summary>
    internal sealed record SectorMap(Sector? Home, Dictionary<Sector, int> Ring, HashSet<Sector> PowerBase, HashSet<Sector> Frontier)
    {
        public int RingOf(StarSystem s) => Ring.GetValueOrDefault(s.Sector, int.MaxValue);
    }

    /// <summary>
    /// Home sector: the Empire's is Coruscant's sector, the Alliance's its HQ system's (else its first own system's). Rings: a
    /// breadth-first walk from the home sector where a sector's neighbours are the sectors within 1.5 x the distance to its nearest
    /// one (Inferred, openswr's own choice: the game has no sector adjacency); a sector the walk misses joins one ring above the
    /// reached sector nearest to it. Power base: the home sector and every sector where the side owns a majority of the populated
    /// systems its views know of. Frontier: the sectors outside the power base next to a power-base sector (the walk's neighbour
    /// relation, from the power-base sector).
    /// </summary>
    internal SectorMap Sectors(GameState game)
    {
        var all = game.Systems.Select(s => s.Sector).Distinct().OrderBy(x => x.Ref).ToList();
        Sector? home = Side == Owner.Empire ? game.Known.Coruscant?.Sector
            : game.HeadquartersSystem is { Owner: var o } hq && o == Side ? hq.Sector : Own(game).FirstOrDefault()?.Sector;
        var ring = new Dictionary<Sector, int>();
        static int D(Sector a, Sector b)
        {
            int dx = a.Definition.X - b.Definition.X, dy = a.Definition.Y - b.Definition.Y;
            return Orders.Isqrt(dx * dx + dy * dy);
        }
        var near = all.ToDictionary(a => a, a => all.Where(b => b != a).Select(b => D(a, b)).DefaultIfEmpty(0).Min());
        bool Next(Sector a, Sector b) => a != b && D(a, b) * 2 <= near[a] * 3;
        if (home is not null)
        {
            ring[home] = 0;
            var queue = new Queue<Sector>([home]);
            while (true)
            {
                while (queue.Count > 0)
                {
                    var a = queue.Dequeue();
                    foreach (var b in all.Where(b => !ring.ContainsKey(b) && Next(a, b)))
                    {
                        ring[b] = ring[a] + 1;
                        queue.Enqueue(b);
                    }
                }
                var missed = all.Where(b => !ring.ContainsKey(b)).ToList();
                if (missed.Count == 0) break;
                var (from, to) = missed.SelectMany(b => ring.Keys.Select(a => (a, b))).OrderBy(p => D(p.a, p.b)).ThenBy(p => p.a.Ref).ThenBy(p => p.b.Ref).First();
                ring[to] = ring[from] + 1;
                queue.Enqueue(to);
            }
        }
        var power = new HashSet<Sector>();
        if (home is not null) power.Add(home);
        foreach (var sector in all)
        {
            var known = game.Systems.Where(s => s.Sector == sector).Select(s => Knowledge.See(game, s, Side)).Where(v => v.Populated && !v.Destroyed).ToList();
            if (known.Count > 0 && 2 * known.Count(v => v.Owner == Side) > known.Count) power.Add(sector);
        }
        var frontier = all.Where(x => !power.Contains(x) && power.Any(p => Next(p, x))).ToHashSet();
        return new(home, ring, power, frontier);
    }

    // ---- rule 5: missions ----

    static MissionKind KindOf(uint missionType) => (MissionKind)Family.Of(missionType);

    /// <summary>The skill a mission type's success uses: diplomacy, espionage (espionage / reconnaissance), leadership (recruitment, subdue).</summary>
    static int SkillFor(MissionKind kind) => kind switch { MissionKind.Diplomacy => SkillIndex.Diplomacy, MissionKind.Espionage or MissionKind.Reconnaissance => SkillIndex.Espionage, _ => SkillIndex.Leadership };

    /// <summary>
    /// An own character free for a mission: Existing, Completed, not Enroute, not on a mission, not captured or injured, no command
    /// post, at a system, and not aboard a ship or fleet ordered this decision (it would be Enroute at the execution).
    /// </summary>
    bool Free(Character c, HashSet<GameObject> used)
    {
        if (c.Owner != Side || !c.Existing || !c.Completed || c.Enroute || c.OnMission || c.Captured || c.Injury != 0
            || c.CommandKind != 0 || used.Contains(c) || Missions.Location(c) is not StarSystem) return false;
        for (var p = c.Container; p is not null and not StarSystem; p = p.Container) if (used.Contains(p)) return false;
        return true;
    }

    // ---- scouting special forces ----

    /// <summary>Scouting units kept while known unexplored systems remain in the power base, the frontier or the home sector's ring 1.</summary>
    public const int ScoutUnitsWanted = 2;

    /// <summary>
    /// The special-force types the reconnaissance mission accepts for the side: the eligibility list's mask and side check
    /// (MissionEligibility: MissionMask subset of the type's SpecialForceMask, the side's flag), i.e. what the mission window would offer.
    /// </summary>
    static HashSet<uint> ReconTypes(GameState game, Owner side)
    {
        if (TypeOf(game, MissionKind.Reconnaissance) is not { } recon) return [];
        return [.. game.Definitions.SpecialForces.Where(f => f.MissionMask != 0 && recon.AllowsSpecialForce(f.MissionMask)
            && (side == Owner.Alliance ? f.Alliance : f.Empire) != 0).Select(f => f.Common.Ref)];
    }

    /// <summary>Own scouting units (Existing; on a mission or under construction too).</summary>
    List<Agent> ScoutUnits(GameState game)
    {
        var types = ReconTypes(game, Side);
        return [.. game.Created.OfType<Agent>().Where(a => a.Owner == Side && a.Existing && types.Contains(a.Type)).OrderBy(a => a.Key)];
    }

    /// <summary>An own unit free for a mission: Completed, not Enroute, not on a mission, at a system, not aboard something ordered this decision.</summary>
    bool FreeUnit(Agent a, HashSet<GameObject> used)
    {
        if (!a.Completed || a.Enroute || a.OnMission || used.Contains(a) || Missions.Location(a) is not StarSystem) return false;
        for (var p = a.Container; p is not null and not StarSystem; p = p.Container) if (used.Contains(p)) return false;
        return true;
    }

    /// <summary>Scouting units still to build: <see cref="ScoutUnitsWanted"/> while the views show unexplored systems in the power base, the frontier or ring 0-1, minus those the side has.</summary>
    internal int ScoutUnitsMissing(GameState game, SectorMap map)
    {
        bool unexplored = game.Systems.Any(s => Knowledge.See(game, s, Side) is { Explored: false, Destroyed: false }
            && (map.PowerBase.Contains(s.Sector) || map.Frontier.Contains(s.Sector) || map.RingOf(s) <= 1));
        return unexplored ? Math.Max(0, ScoutUnitsWanted - ScoutUnits(game).Count) : 0;
    }

    // ---- minimum chance ----

    /// <summary>Diplomacy and subdue start only at this attempt chance (%) or more.</summary>
    public const int MinChance = 25;
    /// <summary>A running diplomacy or subdue whose attempt chance stays below this (%) for <see cref="RecallDays"/> is recalled (MissionOrder command).</summary>
    public const int RecallChance = 15, RecallDays = 10;

    /// <summary>Running diplomacy / subdue missions whose chance is below <see cref="RecallChance"/>: per mission ref, the first such decision day.</summary>
    readonly SortedDictionary<uint, int> _lowSince = [];

    /// <summary>
    /// The attempt chance (%) of diplomacy or subdue for one member at a system:
    /// T(DIPLMSTB, EnhDipl - share(sys, opp) + nST), T(SUBDMSTB, EnhLead - share(sys, opp) + nST); the member's real skill (an own
    /// unit), the opponent's share and the Stormtrooper Regiments (nST) from the side's view of the system.
    /// </summary>
    internal int Chance(GameState game, Known k, MissionKind kind, Agent a, StarSystem s)
    {
        var v = Knowledge.See(game, s, Side);
        int opp = Side == Owner.Alliance ? 100 - v.Support : v.Support;
        int st = k.Contents(s).Count(x => x.Object.Type == Refs.Stormtroopers && x.Owner == Owner.Empire && x.Usable);
        return kind == MissionKind.Diplomacy
            ? Missions.Table(game, "DIPLMSTB", Missions.Enhanced(game, a, SkillIndex.Diplomacy) - opp + st) ?? 0
            : Missions.Table(game, "SUBDMSTB", Missions.Enhanced(game, a, SkillIndex.Leadership) - opp + st) ?? 0;
    }

    /// <summary>
    /// Recall (MissionOrder command, the mission's order: CompletionStatus := 2, accepted in phase 8 only) of a running
    /// diplomacy or subdue whose best member's attempt chance has been below <see cref="RecallChance"/> for <see cref="RecallDays"/>.
    /// </summary>
    void Recall(GameState game, Known k, List<Mission> running, HashSet<GameObject> used)
    {
        var watched = running.Where(m => m.Kind is MissionKind.Diplomacy or MissionKind.SubdueUprising && m.Objective is StarSystem).ToList();
        foreach (var r in _lowSince.Keys.Where(r => watched.All(m => m.Ref != r)).ToList()) _lowSince.Remove(r);
        foreach (var m in watched)
        {
            int best = m.Team.Select(a => Chance(game, k, m.Kind, a, (StarSystem)m.Objective!)).DefaultIfEmpty(0).Max();
            if (best >= RecallChance)
            {
                _lowSince.Remove(m.Ref);
                continue;
            }
            // a high-value own system below the safe share keeps its diplomacy until it is back above
            if (m.Kind == MissionKind.Diplomacy && m.Objective is StarSystem { } held && held.Owner == Side && HighValue(held) && held.SupportFor(Side) < SafeShare) continue;
            if (!_lowSince.TryGetValue(m.Ref, out int since)) _lowSince[m.Ref] = since = game.Day;
            if (game.Day - since >= RecallDays && m.Phase == 8 && Room) Issue(game, new Command(CommandType.MissionOrder, Side) { Targets = [(m, GroupMode.Object)] }, used);
        }
    }

    /// <summary>
    /// Measurement (aimatch only): one mission order as the AI decided it: kind, target, team, the estimated
    /// chance (percent; -1 = no estimate for the kind) and worth (-1 = none), and the known enemy defences at the target's
    /// system (troops, special forces, characters, fleets) from the side's views.
    /// </summary>
    public sealed record MissionEstimate(int Day, MissionKind Kind, GameObject Target, IReadOnlyList<Agent> Team, double Chance, double Worth,
        int Troops, int SpecialForces, int Characters, int Fleets);

    /// <summary>The mission orders of the side with their estimates (measurement; null = off).</summary>
    public List<MissionEstimate>? MissionEstimates { get; set; }

    void RecordEstimate(GameState game, Known k, MissionKind kind, GameObject target, IReadOnlyList<Agent> team, double chance, double worth)
    {
        if (MissionEstimates is null) return;
        var at = target as StarSystem ?? Knowledge.Location(game, target, Side) as StarSystem;
        var seen = at is null ? [] : k.Contents(at).Where(v => v.Owner == Enemy).ToList();
        MissionEstimates.Add(new(game.Day, kind, target, [.. team], chance, worth, seen.Count(v => v.In(Families.Troop, Families.CapitalShip)), seen.Count(v => v.In(Families.SpecialForce, Families.Mission)),
            seen.Count(v => v.In(Families.MajorCharacter, Families.SpecialForce)), seen.Count(v => v.Object is Fleet)));
    }

    /// <summary>
    /// Recruit count: the side's characters not yet in play, counted once at
    /// the first decision that needs it (Inferred: a player knows this from the side's roster); null until then. Saved.
    /// </summary>
    internal int? RecruitTotal { get; private set; }

    /// <summary>Own recruitment successes (the side's own TaskStatus messages for the recruitment kind, TaskStatus 3). Saved.</summary>
    internal int Recruited { get; private set; }

    /// <summary>Characters left to recruit: <see cref="RecruitTotal"/> - <see cref="Recruited"/> (the total counted on first use).</summary>
    internal int RecruitLeft(GameState game)
    {
        RecruitTotal ??= game.Characters.Count(c => Finders.DefinitionSide(c) == Side && !c.Existing && !c.Destroyed);
        return Math.Max(0, RecruitTotal.Value - Recruited);
    }

    /// <summary>Test hook: the recruit counts.</summary>
    internal void SetRecruitCounts(int total, int recruited) => (RecruitTotal, Recruited) = (total, recruited);

    /// <summary>The playable type of a mission kind.</summary>
    internal static MissionRecord? TypeOf(GameState game, MissionKind kind) =>
        game.Definitions.Missions.Where(r => !r.Unlisted && KindOf(r.Common.Ref) == kind).OrderBy(r => r.Common.Ref).FirstOrDefault();

    /// <summary>
    /// Rule 5: the free characters go on missions in this priority order: hold home (subdue), scout (espionage, or
    /// reconnaissance where espionage is not offered; at most <see cref="MaxScouts"/>), strengthen the home sector (diplomacy),
    /// expand (diplomacy in the power base, then the frontier), covert missions worth running (highest worth first; a major
    /// character's rescue right after "hold home"; at most <see cref="MaxCovert"/> running), recruit. Per target the best-suited
    /// free character for whom the type passes there; a target with an own mission running is skipped.
    /// </summary>
    internal void Assign(GameState game, Known k, SectorMap map, HashSet<GameObject> used)
    {
        RecordSeen(game);
        _lookFirst.Clear();
        if (On(V7.Agents))
        {
            EndPivots(game);
            ReserveForPivots(game, k, used);
            _decoyShort = 0;
        }
        var free = game.Characters.Where(c => Free(c, used)).OrderBy(c => c.Key).ToList();
        var scoutUnits = ScoutUnits(game).Where(a => FreeUnit(a, used)).ToList();
        var running = game.Missions.Where(m => m.Owner == Side && !m.Deleted && m.CompletionStatus == MissionCompletion.None).ToList();
        Recall(game, k, running, used);
        // (Teams / Intel: idle special forces alone can still go)
        if (free.Count == 0 && scoutUnits.Count == 0 && !((TeamsOn || On(V7.Intel)) && FreeSpecialForces(game, used).Count > 0) || !Room) return;
        var targeted = running.Select(m => m.Objective).Where(o => o is not null).ToHashSet();
        var views = game.Systems.ToDictionary(s => s, s => Knowledge.See(game, s, Side));
        int Share(StarSystem s) => Side == Owner.Alliance ? views[s].Support : 100 - views[s].Support;
        bool Home(StarSystem s) => s.Sector == map.Home;

        int sentSpying = 0;
        int? spyBudget = null;
        // Agents: the decoys of one espionage (none without Agents); null: not enough free special forces, the mission waits
        List<GameObject>? SpyDecoys(Agent agent, uint typeRef, StarSystem target)
        {
            if (!On(V7.Agents) || views[target].Owner == Side) return [];
            var pool = FreeSpecialForces(game, used).Where(a => a != agent);
            if (AgentDecoys(game, MissionKind.Espionage, target, Assume(game, k, target, KnownAt(game, k, target, true, null), MedianSystem(game, k)), [agent], [], pool,
                    (t, d) => MissionEligibility.Validate(game, CommandType.CreateMission, Side, typeRef, t, d, target) is null, out int shortBy) is not { } team)
            {
                _decoyShort += shortBy;
                return null;
            }
            if (_foiledTargets.Contains(target.Ref)) RetryRecords?.Add(new(game.Day, MissionKind.Espionage, target.Ref, team.Decoys.Count));
            CountFewer(MissionKind.Espionage, target, [agent], team.Decoys.Count);
            return [.. team.Decoys];
        }
        // one mission of kind (kinds tried in order) at the target with the best-suited free character it passes for
        bool Send(StarSystem target, params MissionKind[] kinds)
        {
            bool spying = kinds.Contains(MissionKind.Espionage) && views[target].Owner != Side;
            void Spy(string outcome) { if (spying) Probe?.Espionage.Add((target, outcome)); } // measurement only
            if (!Room || targeted.Contains(target))
            {
                Spy(Room ? "running" : "room");
                return false;
            }
            foreach (MissionKind kind in kinds)
            {
                if (TypeOf(game, kind) is not { } type) continue;
                // rule 6: the espionage budget and no repeat on a system seen within CoverDays unless a target is planned there
                if (On(V7.Agents) && kind == MissionKind.Espionage && views[target].Owner != Side && EspionageBlock(game, target, running, sentSpying, spyBudget ??= SpyBudget(game, map)) is { } block)
                {
                    Spy(block);
                    continue;
                }
                // Teams: espionage by an idle special force the eligibility list offers, before a character
                if (TeamsOn && kind == MissionKind.Espionage)
                    foreach (var a in FreeSpecialForces(game, used).Where(a => !ScoutUnits(game).Contains(a)).OrderByDescending(a => Missions.Enhanced(game, a, SkillIndex.Espionage)).ThenBy(a => a.Key))
                    {
                        if (MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, [a], [], target) is not null) continue;
                        // rule 4: a retry at a foiled target goes only with decoys (else it waits)
                        if (SpyDecoys(a, type.Common.Ref, target) is not { } spyDecoys)
                        {
                            Spy("retry waits");
                            return false;
                        }
                        if (!Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(a, GroupMode.Object)], ArgRef = target, Fields = [0, 0, type.Common.Ref], Decoys = spyDecoys }, used)) continue;
                        RecordEstimate(game, k, kind, target, [a], -1, -1);
                        targeted.Add(target);
                        if (views[target].Owner != Side) sentSpying++;
                        Spy("sent");
                        return true;
                    }
                foreach (var c in free.OrderByDescending(c => Missions.Enhanced(game, c, SkillFor(kind))).ThenBy(c => c.Key))
                {
                    if (!Room) return false;
                    if (kind is MissionKind.Diplomacy or MissionKind.SubdueUprising && Chance(game, k, kind, c, target) < MinChance) break; // the best-suited one too low: skip the target
                    if (MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, [c], [], target) is not null) continue;
                    // rules 7 and 4: a character spy takes a special-force decoy against a known detector; a retry needs decoys
                    var decoys = kind == MissionKind.Espionage ? SpyDecoys(c, type.Common.Ref, target) : [];
                    if (decoys is null)
                    {
                        Spy("retry waits");
                        return false;
                    }
                    if (!Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(c, GroupMode.Object)], ArgRef = target, Fields = [0, 0, type.Common.Ref], Decoys = decoys }, used)) continue;
                    if (kind == MissionKind.Espionage && views[target].Owner != Side) sentSpying++;
                    if (kind == MissionKind.Espionage) Spy("sent");
                    RecordEstimate(game, k, kind, target, [c], kind is MissionKind.Diplomacy or MissionKind.SubdueUprising ? Chance(game, k, kind, c, target) : -1, -1);
                    free.Remove(c);
                    targeted.Add(target);
                    return true;
                }
            }
            Spy("no valid agent");
            return false;
        }

        // covert missions: running count, one send
        int covert = running.Count(m => CovertKinds.Contains(m.Kind));
        bool training = running.Any(m => m.Kind == MissionKind.JediTraining);
        var sentKinds = new List<MissionKind>();
        void SendCovert(IEnumerable<Covert> candidates)
        {
            foreach (var x in candidates)
            {
                if (covert >= MaxCovert || !Room)
                {
                    if (Probe is { } pr) foreach (var y in candidates.SkipWhile(y => !y.Equals(x))) pr.Dropped.TryAdd((y.Kind, y.Target.Ref), Room ? "MaxCovert" : "command budget"); // measurement only
                    return;
                }
                // rule 6: sabotage and incite running at most MaxSabotage / InciteCap (Cascade raises the incite cap)
                if (On(V7.Agents) && (x.Kind == MissionKind.Sabotage && running.Count(m => m.Kind == MissionKind.Sabotage) + sentKinds.Count(k => k == MissionKind.Sabotage) >= MaxSabotage
                    || x.Kind == MissionKind.InciteUprising && running.Count(m => m.Kind == MissionKind.InciteUprising) + sentKinds.Count(k => k == MissionKind.InciteUprising) >= InciteCap(game, x.Target)))
                {
                    Probe?.Dropped.TryAdd((x.Kind, x.Target.Ref), x.Kind == MissionKind.Sabotage ? "MaxSabotage" : "MaxIncite");
                    continue;
                }
                if (x.Trial && !TrialOpen(x.Kind, game.Day)) continue; // one trial per kind in TrialDays
                if (x.Kind == MissionKind.JediTraining && training || targeted.Contains(x.Target) || x.Team.Any(a => a is Character c ? !free.Contains(c) : used.Contains(a))
                    || (x.Decoys ?? []).Any(used.Contains)) continue;
                var command = new Command(CommandType.CreateMission, Side) { Targets = [.. x.Team.Select(a => ((GameObject)a, GroupMode.Object))], ArgRef = x.Target, Fields = [0, 0, TypeOf(game, x.Kind)!.Common.Ref],
                    Decoys = [.. (x.Decoys ?? []).Cast<GameObject>()] };
                if (!Issue(game, command, used)) continue;
                RecordEstimate(game, k, x.Kind, x.Target, x.Team, x.Success < 0 ? -1 : 100 * x.Success, x.Worth);
                if (x.Kind != MissionKind.JediTraining) RecordAge(game, "covert", x.Target as StarSystem ?? Knowledge.Location(game, x.Target, Side) as StarSystem);
                if (On(V7.Agents) && _foiledTargets.Contains(x.Target.Ref)) RetryRecords?.Add(new(game.Day, x.Kind, x.Target.Ref, x.Decoys?.Count ?? 0));
                CountFewer(x.Kind, x.Target, x.Team, x.Decoys?.Count ?? 0);
                if (x.Trial) TrialSent(x.Kind, game.Day);
                if (Probe is { } sentProbe) sentProbe.Sent[x.Kind] = sentProbe.Sent.GetValueOrDefault(x.Kind) + 1;
                sentKinds.Add(x.Kind);
                foreach (var a in x.Team.OfType<Character>()) free.Remove(a);
                foreach (var a in (x.Decoys ?? []).Concat(x.Team)) used.Add(a);
                targeted.Add(x.Target);
                covert++;
                if (x.Kind == MissionKind.JediTraining) training = true;
            }
        }

        // Hunt: the enemy objective characters' abductions, the search espionage and the HQ search come before every other mission
        if (Hunting) Hunt(game, k, map, free, scoutUnits, running, targeted, used, SendCovert);

        // 1. hold home: subdue every own system with an Uprising, home sector first
        foreach (var s in game.Systems.Where(s => views[s].Owner == Side && views[s].Uprising && !views[s].Destroyed)
                     .OrderBy(s => map.RingOf(s)).ThenBy(s => s.Key))
        {
            if (free.Count == 0) break;
            Send(s, MissionKind.SubdueUprising);
        }

        // 1b. a major character's rescue, when worth it
        // 1a. Recruit: recruitment first while characters are left to recruit, counted as the recruit count:
        // running recruitment missions never exceed what is left
        if (On(V7.Recruit) && TypeOf(game, MissionKind.Recruitment) is { } recruit)
        {
            int waiting = RecruitLeft(game) - running.Count(m => m.Kind == MissionKind.Recruitment);
            foreach (var c in free.OrderByDescending(c => c.Skills[SkillIndex.Leadership]).ThenBy(c => c.Key).ToList())
            {
                if (waiting <= 0 || !Room) break;
                if (Missions.Location(c) is not StarSystem at || MissionEligibility.Validate(game, CommandType.CreateMission, Side, recruit.Common.Ref, [c], [], at) is not null) continue;
                if (!Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(c, GroupMode.Object)], ArgRef = at, Fields = [0, 0, recruit.Common.Ref] }, used)) continue;
                RecordEstimate(game, k, MissionKind.Recruitment, at, [c], -1, -1);
                free.Remove(c);
                waiting--;
            }
        }

        SendCovert(CovertCandidates(game, k, map, free, targeted, rescueOnly: true, used));

        // 1c. Watch: espionage on the own high-value border systems (at most MaxWatches running; they count as scouting)
        int watches = running.Count(m => m.Kind == MissionKind.Espionage && m.TargetLocation is StarSystem { } w && w.Owner == Side), sentWatches = 0;
        int scouting = running.Count(m => m.Kind is MissionKind.Espionage or MissionKind.Reconnaissance && m.Team.Any(a => a is Character));
        foreach (var s in On(V7.Watch) ? WatchTargets(game, k) : [])
        {
            if (watches >= MaxWatches || scouting + sentWatches >= MaxScouts || free.Count == 0 || !Room) break;
            if (!Send(s, MissionKind.Espionage)) continue;
            MarkWatched(s, game.Day);
            watches++;
            sentWatches++;
        }

        // 2. scout: systems never seen or not seen for ScoutAgainDays, home sector first, then by ring, nearest system first. The
        // scouting special forces take the unexplored ones by reconnaissance, the characters (at most MaxScouts running)
        // the explored ones by espionage (espionage refuses an unexplored system, reconnaissance refuses characters)
        var origin = map.Home is { } h ? game.Systems.Where(s => s.Sector == h).OrderBy(s => s.Key).First() : null;
        var unscouted = game.Systems.Where(s => views[s].Owner != Side && !views[s].Destroyed && !Knowledge.IsLive(s, Side)
                && (!_seen.TryGetValue(s.Ref, out int day) || game.Day - day >= ScoutAgainDays))
            .OrderBy(s => map.RingOf(s)).ThenBy(s => origin is null ? 0 : Orders.Distance(origin, s)).ThenBy(s => s.Key).ToList();
        if (TypeOf(game, MissionKind.Reconnaissance) is { } recon)
            foreach (var s in unscouted.Where(s => !views[s].Explored))
            {
                if (scoutUnits.Count == 0 || !Room)
                {
                    Probe?.Recon.Add((s, Room ? "no free scouting unit" : "room")); // measurement only (the first target the loop left)
                    break;
                }
                if (targeted.Contains(s))
                {
                    Probe?.Recon.Add((s, "running"));
                    continue;
                }
                var unit = scoutUnits.FirstOrDefault(a => MissionEligibility.Validate(game, CommandType.CreateMission, Side, recon.Common.Ref, [a], [], s) is null);
                if (unit is null || !Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(unit, GroupMode.Object)], ArgRef = s, Fields = [0, 0, recon.Common.Ref] }, used))
                {
                    Probe?.Recon.Add((s, unit is null ? "no valid unit" : "refused"));
                    continue;
                }
                Probe?.Recon.Add((s, "sent"));
                scoutUnits.Remove(unit);
                targeted.Add(s);
            }
        int scouts = running.Count(m => m.Kind is MissionKind.Espionage or MissionKind.Reconnaissance && m.Team.Any(a => a is Character)) + sentWatches;
        var explored = unscouted.Where(s => views[s].Explored).ToList();
        for (int i = 0; i < explored.Count; i++)
        {
            var s = explored[i];
            if (scouts >= MaxScouts || free.Count == 0 || !Room)
            {
                // measurement only: the targets the loop gate left
                if (Probe is { } pr)
                    foreach (var rest in explored.Skip(i).Where(x => views[x].Owner != Side))
                        pr.Espionage.Add((rest, !Room ? "room" : scouts >= MaxScouts ? "maxscouts" : "no free character"));
                break;
            }
            if (Send(s, MissionKind.Espionage)) scouts++;
        }

        // 3. strengthen: every own system below the safe share, highest value first, then the home sector's neutral
        // systems, lowest support for the side first
        // Pacify: the own systems lowest share first, not in uprising, at most PacifyMax diplomacy running on own systems
        int pacifying = running.Count(m => m.Kind == MissionKind.Diplomacy && m.Objective is StarSystem { } x && views[x].Owner == Side);
        var own = Pacifying ? PacifyTargets(game)
            : game.Systems.Where(s => views[s].Owner == Side && views[s].Populated && Share(s) < SafeShare)
                .OrderByDescending(s => _values.GetValueOrDefault(s.Ref)).ThenBy(Share).ThenBy(s => s.Key).ToList();
        foreach (var s in own.Concat(game.Systems.Where(s => Home(s) && views[s].Owner == Owner.Neutral && views[s].Populated).OrderBy(Share).ThenBy(s => s.Key)))
        {
            if (free.Count == 0) break; // the later steps need free characters, or (Teams / Intel) special forces
            bool mine = views[s].Owner == Side;
            if (Pacifying && mine && pacifying >= PacifyMax) continue;
            if (Send(s, MissionKind.Diplomacy) && mine) pacifying++;
        }

        // 4. expand: neutral populated systems in the power base, then the frontier; lowest ring, then highest support for the side
        // (rim-first: also the safe rim systems anywhere, and those first)
        var rimPlaces = RimFirsting ? EnemyPlaces(game, k) : null;
        bool RimSafeAt(StarSystem s) => rimPlaces is not null && RimSafe(game, s, rimPlaces);
        var expand = game.Systems.Where(s => !Home(s) && views[s].Owner == Owner.Neutral && views[s].Populated && !views[s].Destroyed
                         && (map.PowerBase.Contains(s.Sector) || map.Frontier.Contains(s.Sector) || RimSafeAt(s)));
        foreach (var s in expand.OrderBy(s => RimSafeAt(s) ? 0 : 1)
                     .ThenBy(s => map.PowerBase.Contains(s.Sector) ? 0 : 1).ThenBy(s => map.RingOf(s)).ThenByDescending(Share).ThenBy(s => s.Key))
        {
            if (free.Count == 0) break; // the later steps need free characters, or (Teams / Intel) special forces
            Send(s, MissionKind.Diplomacy);
        }

        // covert missions worth running, highest worth first (Intel: the candidates also when the running count is full, for look before acting)
        if ((free.Count > 0 || TeamsOn) && (covert < MaxCovert || On(V7.Intel)))
        {
            var candidates = CovertCandidates(game, k, map, free, targeted, rescueOnly: false, used);
            if (JediTraining(game, free) is { } jedi) candidates.Add(jedi);
            var ranked = On(V7.Agents) ? Rank(candidates) : [.. candidates.OrderByDescending(x => x.Worth)];
            SendCovert(Hunting ? HuntFirst(ranked) : ranked); // Hunt: the objective abductions first
        }
        else if (Probe is { } skipped) skipped.CovertSkipped = covert >= MaxCovert ? $"MaxCovert ({MaxCovert} covert missions running)" : "no free character"; // measurement only

        // Intel: espionage on the look-before-acting systems, then coverage of the stalest systems in reach;
        // special forces first (espionage, else reconnaissance), then characters within MaxScouts
        if (On(V7.Intel))
        {
            int left = 0;
            foreach (var s in IntelTargets(game, map))
            {
                if (!Room) { left++; continue; }
                if (targeted.Contains(s)) continue; // an own mission running there
                bool sent = false;
                foreach (var a in FreeSpecialForces(game, used).OrderByDescending(a => Missions.Enhanced(game, a, SkillIndex.Espionage)).ThenBy(a => a.Key))
                {
                    foreach (MissionKind kind in new MissionKind[] { MissionKind.Espionage, MissionKind.Reconnaissance })
                        if (TypeOf(game, kind) is { } type && MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, [a], [], s) is null
                            && Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(a, GroupMode.Object)], ArgRef = s, Fields = [0, 0, type.Common.Ref] }, used))
                        {
                            RecordEstimate(game, k, kind, s, [a], -1, -1);
                            sent = true;
                            break;
                        }
                    if (sent) break;
                }
                if (sent) { targeted.Add(s); continue; }
                left++; // no special force: one more wanted
                if (scouts < MaxScouts && free.Count > 0 && Send(s, MissionKind.Espionage)) scouts++;
            }
            IntelDemand = left;
        }

        // 5. recruit: fewer than RecruitBelow free characters left and no recruitment running, then the one with the most leadership, at its own system
        // (Recruit: not when nothing is left to recruit)
        if (free.Count is > 0 and < RecruitBelow && Room && !running.Any(m => m.Kind == MissionKind.Recruitment) && (!On(V7.Recruit) || RecruitLeft(game) > 0))
            foreach (var c in free.OrderByDescending(c => c.Skills[SkillIndex.Leadership]).ThenBy(c => c.Key).ToList())
            {
                if (TypeOf(game, MissionKind.Recruitment) is not { } type || Missions.Location(c) is not StarSystem at) break;
                if (MissionEligibility.Validate(game, CommandType.CreateMission, Side, type.Common.Ref, [c], [], at) is not null) continue;
                Issue(game, new Command(CommandType.CreateMission, Side) { Targets = [(c, GroupMode.Object)], ArgRef = at, Fields = [0, 0, type.Common.Ref] }, used);
                break;
            }
        if (Guarding) GuardIdle(game, free, used); // Guard
    }
}
