using OpenSWR.Data.Dat;
using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

public static partial class Missions
{
    /// <summary>
    /// Evasion success (the evasion hook; special forces reach the same routine):
    /// MissionResignRequest := 1 only with the ResignOnInjury flag and RoleMissionKey set; then characters run the injury check, special
    /// forces draw nothing; Luke / Leia then gain Force rank.
    /// </summary>
    internal static void Evaded(GameState game, Agent a)
    {
        if (a.ResignOnInjury && a.Mission is not null) a.ResignRequest = true;
        if (a is Character c) InjuryCheck(game, c);
        Jedi.EvasionGain(game, a); // Luke and Leia
    }

    /// <summary>
    /// Force detection (stage 1). Members M1 = majors [48, 56), M2 = minors [56, 60) of Team then Decoy
    /// (filter, skipping IsDecoying). Opponents with Force >= G ForceEncounterMinForce, not Captured, owner != the mission's, Usable: with SystemUnits directly
    /// in X, with FleetUnits aboard every Usable capital ship of every Usable fleet in X; O1 = the majors, O2 = the minors. Per picked
    /// opponent a (O1: Luke, Vader, else roll(k - 1) over the unmarked; then O2: random): unmark the members, then per picked
    /// member b (M1: Luke, Vader, random; then M2: random): p = Force(a) + Force(b) +
    /// G ForceDetectionOffset if both Forces != 0, else 0; chance(p) if p &gt; 0; success: foiled unless b is a Decoy member, the duel pairing
    /// (the deferred step is not modelled), and the member loop ends.
    /// </summary>
    static void ForceDetection(GameState game, Mission m, Resolver r)
    {
        var m1 = Of(m.Team, Families.MajorCharacter, Families.MinorCharacter, true).Concat(Of(m.Decoys, Families.MajorCharacter, Families.MinorCharacter, true)).ToList();
        var m2 = Of(m.Team, Families.MinorCharacter, Families.SpecialForce, true).Concat(Of(m.Decoys, Families.MinorCharacter, Families.SpecialForce, true)).ToList();
        bool Keep(Character c) => c.Usable && c.Force >= game.Rules.ForceEncounterMinForce && !c.Captured && c.Owner != m.Owner;
        var (o1, o2) = (new List<Agent>(), new List<Agent>());
        void Add(GameObject at) { foreach (var c in at.Contents.OfType<Character>().Where(Keep)) (Family.Of(c.Type) < Families.MinorCharacter ? o1 : o2).Add(c); }
        if (r.SystemUnits) Add(r.X);
        if (r.FleetUnits)
            foreach (var fleet in r.X.Contents.OfType<Fleet>().Where(f => f.Usable))
                foreach (var ship in fleet.Contents.Where(x => Family.IsCapitalShip(x.Type) && x.Usable))
                    Add(ship);
        var marked = new HashSet<Agent>();
        while ((Pick(o1, true) ?? Pick(o2, false)) is Character a)
        {
            marked.ExceptWith(m1.Concat(m2));
            while ((Pick(m1, true) ?? Pick(m2, false)) is { } b)
            {
                int fb = b is Character cb ? cb.Force : 0;
                int p = a.Force != 0 && fb != 0 ? a.Force + fb + game.Rules.ForceDetectionOffset : 0;
                if (p <= 0 || !game.Random.Chance(p, DrawSite.ForceDetect)) continue;
                if (!m.Decoys.Contains(b)) r.Foiled = true;
                if (b is Character cb2) Jedi.Pair(game, cb2, a); // duel pairing
                break;
            }
        }

        Agent? Pick(List<Agent> list, bool named)
        {
            var unmarked = list.Where(x => !marked.Contains(x)).ToList();
            var pick = named ? unmarked.FirstOrDefault(x => x.Ref == Refs.Luke) ?? unmarked.FirstOrDefault(x => x.Ref == Refs.Vader) : null;
            if (pick is null && unmarked.Count > 0) pick = unmarked[game.Random.Roll(unmarked.Count - 1, DrawSite.ForcePick)];
            if (pick is not null) marked.Add(pick);
            return pick;
        }
    }

    /// <summary>
    /// Stage 2: every member (Team, Decoy, Captured) draws its loyalty; the last betrayer is the traitor, whose only
    /// consequence is the foil (the caller sets it); then identify traitor: every other member of the foiled-member
    /// walk draws chance(Force), also for 0 (the TraitorDiscoveredKey it sets is UI / AI only, not kept). Returns the traitor.
    /// </summary>
    internal static Agent? Betrayal(GameState game, Mission m)
    {
        Agent? traitor = null;
        foreach (var a in m.Members.ToList()) if (Loyalty(game, a)) traitor = a;
        if (traitor is null) return null;
        // a success writes TraitorDiscoveredKey := none, then := the traitor (not kept): UI StoryPostKind.Traitor from the member's live views; the
        // none post is invalid for MessageType.Traitor and is not recorded
        foreach (var c in FoiledWalk(m).OfType<Character>().Where(c => c != traitor).ToList())
            if (game.Random.Chance(c.Force, DrawSite.Traitor)) game.StoryPost(StoryPostKind.Traitor, c, 0, 1, traitor);
        return traitor;
    }

    /// <summary>
    /// Loyalty "not betray": betrays = chance(100 - EnhancedLoyalty), drawn always. The betrayal
    /// consequences: <see cref="Betrayal"/>. The draw has its own trace label.
    /// </summary>
    public static bool Loyalty(GameState game, Agent a) => game.Random.Chance(100 - Enhanced(game, a, SkillIndex.Loyalty), DrawSite.Loyalty);

    /// <summary>Foil p: T(FOILTB, a1 - a4 - a5 - pct(a2, G FoilKeyPercent) - G FoilKeyOffset); 100 (always foiled) without the table.</summary>
    public static int FoilChance(GameState game, int a1, int a2, int a4, int a5) =>
        Table(game, "FOILTB", a1 - a4 - a5 - Pct(a2, game.Rules.FoilKeyPercent) - game.Rules.FoilKeyOffset) ?? 100;

    /// <summary>Decoy p: T(TDECOYTB, or FDECOYTB at a fleet, a2 - a4 - pct(a3, G DecoyKeyPercent)).</summary>
    public static int DecoyChance(GameState game, int a2, int a3, int a4, bool fleet) =>
        Table(game, fleet ? "FDECOYTB" : "TDECOYTB", a2 - a4 - Pct(a3, game.Rules.DecoyKeyPercent)) ?? 0;

    /// <summary>
    /// Injury check: q = max(G MissionInjuryMinChance, 100 - EnhancedCombat), chance(q); on success Injury +=
    /// roll(q) + roll(G MissionInjuryRoll) + G MissionInjuryAdd, then the death test: only one class draws chance(G MinorDeathChance) gives
    /// DestroyedReason 10 (Inferred: minor characters [56, 60)); the others draw nothing. Also on both foiled-member outcomes.
    /// <paramref name="shipLoss"/>: the ship-loss injury check (the ship-loss hook), same shape with G ShipLossInjuryMinChance,
    /// G ShipLossInjuryRoll, G ShipLossInjuryAdd (capital-ship class step).
    /// </summary>
    public static void InjuryCheck(GameState game, Character c, bool shipLoss = false)
    {
        var g = game.Rules;
        var (min, add, roll) = shipLoss ? (g.ShipLossInjuryMinChance, g.ShipLossInjuryAdd, g.ShipLossInjuryRoll)
            : (g.MissionInjuryMinChance, g.MissionInjuryAdd, g.MissionInjuryRoll);
        string site = shipLoss ? DrawSite.ShipLossInjury : DrawSite.Injury;
        int q = Math.Max(min, 100 - Enhanced(game, c, SkillIndex.Combat));
        if (!game.Random.Chance(q, site)) return;
        ApplyWound(game, c, game.Random.Roll(q, site) + game.Random.Roll((roll), site) + (add));
    }

    /// <summary>
    /// The wound hook, same routine as the injury check (reason 10): only for w &gt; 0: Injury += w, then the death test (minor characters [56, 60)
    /// draw chance(G MinorDeathChance) gives DestroyedReason 10; Inferred class mapping).
    /// </summary>
    internal static void ApplyWound(GameState game, Character c, int w)
    {
        if (w <= 0) return;
        Prisoners.SetInjury(game, c, c.Injury + w);
        if (Family.Of(c.Type) is >= Families.MinorCharacter and < Families.SpecialForce && game.Random.Chance(game.Rules.MinorDeathChance, DrawSite.Death)) Objects.Destroy(game, c, DestroyedReason.Killed);
    }

    // ---- execution ----

    /// <summary>
    /// Phase 10: class pre-step; characters attempt lowest p first (strictly smaller replaces: ties keep list order)
    /// until one stops; then, if not stopped, special forces in list order; post-step (TaskStatus 0 gives 2, 3 gives the success effect).
    /// Finalize (knowledge, moving members to the target; Inferred) is not modelled.
    /// </summary>
    internal static void Execute(GameState game, Mission m)
    {
        m.Recruited = null;
        m.Accumulator = 0;
        m.LevelsGained = 0; // research pre-step
        bool stop = false;
        var collected = new List<(Agent Agent, int P)>();
        foreach (var c in m.Team.OfType<Character>().ToList())
            if (Probability(game, m, c) is { } p) collected.Add((c, p));
        while (!stop && collected.Count > 0)
        {
            int k = 0;
            for (int i = 1; i < collected.Count; i++) if (collected[i].P < collected[k].P) k = i;
            var (a, p) = collected[k];
            collected.RemoveAt(k);
            stop = Attempt(game, m, a, p);
        }
        foreach (var a in m.Team.Where(a => Family.IsSpecialForce(a.Type)).ToList())
        {
            if (stop) break;
            if (Probability(game, m, a) is { } p) stop = Attempt(game, m, a, p);
        }
        if (m.Kind == MissionKind.Research) ResearchPost(game, m);
        else if (m.Kind == MissionKind.Reconnaissance && m.TaskStatus == MissionTaskStatus.Succeeded && m.TargetLocation is StarSystem seen) Knowledge.Update(game, seen, 8, m.Owner); // recon post
        else if (m.Kind == MissionKind.Bounty) { } // Bounty: no post
        else if (m.Kind == MissionKind.Palace && m.TaskStatus == MissionTaskStatus.Pending) Story.PalaceFailed(game, m);
        else if (m.TaskStatus == MissionTaskStatus.Pending) m.TaskStatus = MissionTaskStatus.Failed;
        else if (m.TaskStatus == MissionTaskStatus.Succeeded) Succeeded(game, m);
        Knowledge.Finalize(game, m);
    }

    /// <summary>
    /// Research post, owner 1 or 2: points of line k += accumulator (through the setter and the level-advance chain),
    /// LevelsGained := order after - before; TaskStatus := 3 if accumulator != 0, else 2.
    /// </summary>
    static void ResearchPost(GameState game, Mission m)
    {
        if (m.Owner is Owner.Alliance or Owner.Empire)
        {
            var side = game.Sides[m.Owner];
            int k = ResearchLine(m.Type), before = side.ResearchOrder[k];
            Research.SetPoints(game, side, k, side.ResearchPoints[k] + m.Accumulator);
            m.LevelsGained = side.ResearchOrder[k] - before;
        }
        m.TaskStatus = m.Accumulator != 0 ? MissionTaskStatus.Succeeded : MissionTaskStatus.Failed;
    }

    /// <summary>share(sys, s): the system's share for s = 1, else 100 - that.</summary>
    static int Share(StarSystem s, Owner side) => side == Owner.Alliance ? s.Support : 100 - s.Support;

    /// <summary>nST: Stormtrooper Regiments (type Refs.Stormtroopers) directly in the system, owner 2, Usable, for both sides' missions.</summary>
    internal static int Stormtroopers(StarSystem s) => s.Contents.Count(x => x.Type == Refs.Stormtroopers && x.Owner == Owner.Empire && x.Usable);

    internal static int? Table(GameState game, string name, int key) =>
        game.Definitions.Tables.TryGetValue(name, out var t) && TableLookup.Threshold(t, key) is { } e ? e.Value : null;

    /// <summary>
    /// Enhanced skill (character getters): Diplomacy / Espionage + pct(*, Force); Combat max(0, * + pct(*, Force) -
    /// Injury); Leadership + pct(*, LeadershipAdjustment) (G EmperorLeadershipBonus for Empire characters while manager 250's flag is set); others
    /// the base value. Special forces: the base skill.
    /// </summary>
    public static int Enhanced(GameState game, Agent a, int skill)
    {
        int b = a.Skills[skill];
        if (a is not Character c) return b;
        return skill switch
        {
            SkillIndex.Diplomacy or SkillIndex.Espionage => b + Pct(b, c.Force),
            SkillIndex.Combat => Math.Max(0, b + Pct(b, c.Force) - c.Injury),
            SkillIndex.Leadership => b + Pct(b, c.Owner == Owner.Empire && game.EmperorAtCoruscant ? game.Rules.EmperorLeadershipBonus : 0),
            _ => b,
        };
    }

    /// <summary>
    /// Class probability: null = inputs missing (the member is skipped). Jedi training and story types are computed in their own modules.
    /// </summary>
    public static int? Probability(GameState game, Mission m, Agent a)
    {
        var sys = m.Objective as StarSystem;
        var target = m.Objective as Character;
        int E(int skill) => Enhanced(game, a, skill);
        int? T(string table, int key) => Table(game, table, key) ?? 0;
        var opp = Systems.Opp(m.Owner);
        return m.Kind switch
        {
            MissionKind.Diplomacy when sys is not null => T("DIPLMSTB", E(SkillIndex.Diplomacy) - Share(sys, opp) + Stormtroopers(sys)),
            MissionKind.Espionage => T("ESPIMSTB", E(SkillIndex.Espionage)),
            MissionKind.Research => 0,
            MissionKind.Reconnaissance => 100,
            MissionKind.Recruitment when sys is not null => T("RCRTMSTB", E(SkillIndex.Leadership) - Share(sys, opp)),
            MissionKind.InciteUprising when sys is not null => T("INCTMSTB", E(SkillIndex.Leadership) - Share(sys, opp) - Stormtroopers(sys)),
            MissionKind.SubdueUprising when sys is not null => T("SUBDMSTB", E(SkillIndex.Leadership) - Share(sys, opp) + Stormtroopers(sys)),
            MissionKind.Rescue => T("RESCMSTB", E(SkillIndex.Combat)),
            MissionKind.Abduction when target is not null => T("ABDCMSTB", E(SkillIndex.Combat) - Enhanced(game, target, SkillIndex.Combat)),
            MissionKind.Assassination when target is not null => T("ASSNMSTB", E(SkillIndex.Combat) - Enhanced(game, target, SkillIndex.Combat)),
            MissionKind.Sabotage => T("SBTGMSTB", (E(SkillIndex.Espionage) + E(SkillIndex.Combat)) / 2),
            MissionKind.DeathStarSabotage => T("DSSBMSTB", (E(SkillIndex.Espionage) + E(SkillIndex.Combat)) / 2),
            MissionKind.JediTraining => Jedi.Probability(m, a),
            _ => Story.Probability(game, m, a),
        };
    }

    /// <summary>
    /// Class attempt: true = stop. Standard: chance(p) at the class attempt site (one step, also for p = 0 or
    /// >= 100), success gives stop, TaskStatus 3 and the per-agent effect; skill growth (base +1, G DiplomacySkillGain ...) only for characters.
    /// Reconnaissance succeeds without a draw; research draws its own chance and never stops; assassination wounds the target and
    /// succeeds only if it dies; subdue draws only while the system has Uprising (no draw gives no stop, the loop goes on); abduction
    /// captures the target (the member becomes its captor), rescue frees it.
    /// </summary>
    static bool Attempt(GameState game, Mission m, Agent a, int p)
    {
        var g = game.Rules;
        void Grow(int skill, int param) { if (a is Character) a.Skills[skill] += (param); }
        switch (m.Kind)
        {
            case MissionKind.JediTraining: Jedi.Attempt(game, m, a, p); return false; // never stops
            case MissionKind.Palace or MissionKind.Bounty or MissionKind.Dagobah or MissionKind.Vacation or MissionKind.Pickup: return Story.Attempt(game, m, a, p);
            case MissionKind.Reconnaissance:
                m.TaskStatus = MissionTaskStatus.Succeeded;
                return true;
            case MissionKind.Research: // k 0 gives ShipyardRD, 1 gives ConstructionYardRD, 2 gives TrainingFacilRD (enhanced)
                // mission system: the ObjectiveKey object if it is a system
                if (m.Objective is not StarSystem) return false;
                int skill = ResearchLine(m.Type) switch { 0 => SkillIndex.ShipDesign, 1 => SkillIndex.FacilityDesign, _ => SkillIndex.TroopTraining };
                if (game.Random.Chance(Enhanced(game, a, skill), DrawSite.Research))
                {
                    m.Accumulator += g.ResearchPointsAdd + game.Random.Roll(g.ResearchPointsRoll, DrawSite.ResearchPoints);
                    Grow(skill, g.ResearchSkillGain);
                }
                return false;
        }
        string site = m.Kind switch
        {
            MissionKind.Diplomacy => DrawSite.Diplomacy, MissionKind.Espionage => DrawSite.Espionage, MissionKind.Recruitment => DrawSite.Recruitment, MissionKind.InciteUprising => DrawSite.Incite,
            MissionKind.SubdueUprising => DrawSite.Subdue, MissionKind.Rescue => DrawSite.Rescue, MissionKind.Abduction => DrawSite.Abduction, MissionKind.Assassination => DrawSite.Assassination,
            MissionKind.Sabotage => DrawSite.Sabotage, MissionKind.DeathStarSabotage => DrawSite.DeathStarSabotage, _ => "",
        };
        if (m.Kind == MissionKind.SubdueUprising && m.Objective is StarSystem { Uprising: false }) return false;
        if (!game.Random.Chance(p, site)) return false;
        switch (m.Kind)
        {
            case MissionKind.Recruitment:
                m.Recruited = RecruitPick(game, (StarSystem)m.Objective!, m.Owner);
                m.TaskStatus = MissionTaskStatus.Succeeded;
                Grow(SkillIndex.Leadership, g.RecruitmentSkillGain);
                break;
            case MissionKind.Diplomacy: m.TaskStatus = MissionTaskStatus.Succeeded; Grow(SkillIndex.Diplomacy, g.DiplomacySkillGain); break;
            case MissionKind.Espionage:
                m.TaskStatus = MissionTaskStatus.Succeeded;
                if (m.TargetLocation?.Owner != m.Owner) Grow(SkillIndex.Espionage, g.EspionageSkillGain);
                break;
            case MissionKind.Sabotage: m.TaskStatus = MissionTaskStatus.Succeeded; Grow(SkillIndex.Espionage, g.SabotageEspionageGain); Grow(SkillIndex.Combat, g.SabotageCombatGain); break;
            case MissionKind.DeathStarSabotage: m.TaskStatus = MissionTaskStatus.Succeeded; Grow(SkillIndex.Espionage, g.DeathStarSabotageEspionageGain); Grow(SkillIndex.Combat, g.DeathStarSabotageCombatGain); break;
            case MissionKind.Abduction:
                m.TaskStatus = MissionTaskStatus.Succeeded;
                Grow(SkillIndex.Combat, g.AbductionSkillGain);
                if (m.Objective is Character abducted) Prisoners.Capture(game, abducted, a);
                break;
            case MissionKind.Rescue:
                m.TaskStatus = MissionTaskStatus.Succeeded;
                Grow(SkillIndex.Combat, g.RescueSkillGain);
                if (m.Objective is Character rescued) Prisoners.Rescue(game, rescued);
                break;
            case MissionKind.Assassination when m.Objective is Character target: // assassination wound on the target
                Wound(game, target);
                if (target.Destroyed) { m.TaskStatus = MissionTaskStatus.Succeeded; Grow(SkillIndex.Combat, g.AssassinationSkillGain); }
                break;
            case MissionKind.InciteUprising when m.Objective is StarSystem x:
                Systems.Incident(game, x);
                m.TaskStatus = x.Control.Support == Systems.Opp(m.Owner)
                    || x.Contents.Any(t => Family.IsTroop(t.Type) && t.Usable && t.Owner == x.Owner) ? MissionTaskStatus.Failed : MissionTaskStatus.Succeeded;
                if (m.TaskStatus == MissionTaskStatus.Succeeded) Grow(SkillIndex.Leadership, g.InciteSkillGain);
                break;
            case MissionKind.SubdueUprising when m.Objective is StarSystem x:
                int d = x.Owner == m.Owner ? g.SubdueOwnSupportAdd + game.Random.Roll(g.SubdueOwnSupportRoll, DrawSite.SubdueDelta)
                    : x.Owner == Owner.Neutral ? g.SubdueNeutralSupportAdd + game.Random.Roll(g.SubdueNeutralSupportRoll, DrawSite.SubdueDelta)
                    : 0;
                Systems.ChangeSupport(game, x, m.Owner, d);
                if (x.Uprising) Systems.SubdueEnd(game, x);
                m.TaskStatus = x.Uprising ? MissionTaskStatus.Failed : MissionTaskStatus.Succeeded;
                if (m.TaskStatus == MissionTaskStatus.Succeeded) Grow(SkillIndex.Leadership, g.SubdueSkillGain);
                break;
        }
        return true;
    }

    /// <summary>
    /// Assassination wound: w = chance(G AssassinationWoundChance) ? roll(G AssassinationWoundChance) + roll(G AssassinationWoundRoll) + G AssassinationWoundAdd : 0; w &gt; 0 gives
    /// Injury += w, then the death test chance(G MinorDeathChance) gives DestroyedReason 7 "DestroyedAssassination".
    /// </summary>
    static void Wound(GameState game, Character target)
    {
        var g = game.Rules;
        int w = game.Random.Chance(g.AssassinationWoundChance, DrawSite.Wound)
            ? game.Random.Roll(g.AssassinationWoundChance, DrawSite.Wound) + game.Random.Roll(g.AssassinationWoundRoll, DrawSite.Wound) + g.AssassinationWoundAdd : 0;
        if (w <= 0) return;
        Prisoners.SetInjury(game, target, target.Injury + w);
        if (game.Random.Chance(g.MinorDeathChance, DrawSite.Death)) Objects.Destroy(game, target, DestroyedReason.Assassination);
    }

    /// <summary>
    /// Post-step success effects. Diplomacy: support change. Espionage: the
    /// extra-system reveal (knowledge modes deferred). Sabotage / Death Star sabotage: the ObjectiveKey gets DestroyedReason 6
    /// "DestroyedSabotage" (Death Star effects deferred).
    /// </summary>
    static void Succeeded(GameState game, Mission m)
    {
        var g = game.Rules;
        if (m.Kind == MissionKind.Diplomacy && m.Objective is StarSystem system)
        {
            int d = system.Owner == m.Owner ? g.DiplomacyOwnSupportAdd + game.Random.Roll(g.DiplomacyOwnSupportRoll, DrawSite.SupportDelta)
                : system.Owner == Owner.Neutral ? g.DiplomacyNeutralSupportAdd + game.Random.Roll(g.DiplomacyNeutralSupportRoll, DrawSite.SupportDelta)
                : 0;
            Systems.ChangeSupport(game, system, m.Owner, d);
        }
        else if (m.Kind == MissionKind.Espionage && m.TargetLocation is StarSystem target)
        {
            Knowledge.Update(game, target, 10, m.Owner);
            Reveal(game, m, target);
        }
        else if (m.Kind is MissionKind.Sabotage or MissionKind.DeathStarSabotage && m.Objective is { } objective) Objects.Destroy(game, objective, DestroyedReason.Sabotage);
    }

    /// <summary>
    /// Espionage extra systems, only for a core target [144, 146) owned by opp(owner): with a
    /// Usable headquarters [32, 34) there n = G HqRevealAdd + roll(G HqRevealRoll) over core and rim; else Coruscant owned by the Empire
    /// n = G CoruscantRevealAdd + roll(G CoruscantRevealRoll) over core and rim; else n = G EspionageRevealAdd + roll(G EspionageRevealRoll) over core only. Candidates: Usable systems
    /// of the range with the target's owner, registry order, target excluded; min(n, count) times roll(len - 1) picks and removes
    /// one (ExtraSystemKey; the knowledge it raises is deferred).
    /// </summary>
    static void Reveal(GameState game, Mission m, StarSystem target)
    {
        if (target.Owner != Systems.Opp(m.Owner) || Family.Of(target.Type) is not (>= Families.CoreSystem and < Families.RimSystem)) return;
        var g = game.Rules;
        (int b, int v, uint hi) = target.Contents.Any(x => Family.Of(x.Type) is >= Families.Headquarters and < Families.IonCannon && x.Usable) ? (g.HqRevealAdd, g.HqRevealRoll, Families.Abode)
            : target.Type == Refs.Coruscant && target.Owner == Owner.Empire ? (g.CoruscantRevealAdd, g.CoruscantRevealRoll, Families.Abode)
            : (g.EspionageRevealAdd, g.EspionageRevealRoll, Families.RimSystem);
        int n = (b) + game.Random.Roll((v), DrawSite.EspionageReveal);
        var candidates = game.Systems.Where(x => Family.Of(x.Type) >= Families.CoreSystem && Family.Of(x.Type) < hi && x.Usable && x.Owner == target.Owner && x != target).ToList();
        for (int k = Math.Min(n, candidates.Count); k > 0; k--)
        {
            int i = game.Random.Roll(candidates.Count - 1, DrawSite.RevealPick);
            // ExtraSystemKey setter: live side views post extra system notification (mission, system) before the hook; key not stored (no sim reader).
            game.ViewMessage(ViewMessageType.EspionageExtraSystem, m, candidates[i]);
            Knowledge.Update(game, candidates[i], 10, m.Owner); // class hook
            candidates.RemoveAt(i);
        }
    }

    /// <summary>
    /// Recruit pick: c = the side's unplaced minors (56-59, Created clear) in ascending Index; c = 0 gives none;
    /// i = roll(c - 1); placed in the system (Created gives stat roll; Completed, as setup placement sets both).
    /// Taking the last one sets the side's RecruitmentDone.
    /// </summary>
    static Character? RecruitPick(GameState game, StarSystem location, Owner side)
    {
        var unplaced = game.Characters.Where(c => c.Owner == side && !c.Placed && Family.Of(c.Type) is >= Families.MinorCharacter and < Families.SpecialForce).ToList();
        if (unplaced.Count == 0) return null;
        var c = unplaced[game.Random.Roll(unplaced.Count - 1, DrawSite.Minor)];
        Objects.SetParent(game, c, location);
        Objects.SetCreated(game, c);
        NewGame.StatRoll(game, c);
        Objects.SetCompleted(game, c);
        if (unplaced.Count == 1) game.Sides[side].RecruitmentDone = true;
        return c;
    }
}
