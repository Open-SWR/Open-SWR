using static OpenSWR.Sim.GameMath;

namespace OpenSWR.Sim;

/// <summary>
/// Story: per-character story flags, manager 0xF8 story flags and their hooks, the story check,
/// the story mission creators and class slots (Palace 0x64, Bounty 0x65, Dagobah 0x71, Pickup 0x73, Vacation 0x72), the
/// bounty timer 0x393, the approach moves and the final battle. No RNG of its own except the final-battle wound;
/// the created missions draw as standard missions.
/// Knowledge updates: <see cref="Knowledge"/>. UI notifications 0x210-0x212, 0x220, 0x221, 0x231: <see cref="GameState.StoryPost"/>. Not modelled: PickupInProgress (no sim reader),
/// the victory recompute and game end.
/// </summary>
/// <summary>One story flag of manager 0xF8 (selects a flag of <see cref="StoryFlags"/>).</summary>
public enum StoryFlag { LukeDagobahRequired, LukeDagobah, LukePalace, HanCapturedAtPalace, LeiaPalace, ChewbaccaPalace, DarthPickup, DarthToLukeFinalBattle, DarthToEmperorFinalBattle, FinalBattleReady, FinalBattle, BountyHuntersActive }

/// <summary>The story flags of manager 0xF8. Changes after setup go through <see cref="Story.Set"/>, which runs the hooks.</summary>
public sealed class StoryFlags
{
    public bool LukeDagobahRequired { get; set; }
    public bool LukeDagobah { get; set; }
    public bool LukePalace { get; set; }
    public bool HanCapturedAtPalace { get; set; }
    public bool LeiaPalace { get; set; }
    public bool ChewbaccaPalace { get; set; }
    public bool DarthPickup { get; set; }
    public bool DarthToLukeFinalBattle { get; set; }
    public bool DarthToEmperorFinalBattle { get; set; }
    public bool FinalBattleReady { get; set; }
    public bool FinalBattle { get; set; }
    public bool BountyHuntersActive { get; set; }

    public bool this[StoryFlag flag]
    {
        get => flag switch
        {
            StoryFlag.LukeDagobahRequired => LukeDagobahRequired,
            StoryFlag.LukeDagobah => LukeDagobah,
            StoryFlag.LukePalace => LukePalace,
            StoryFlag.HanCapturedAtPalace => HanCapturedAtPalace,
            StoryFlag.LeiaPalace => LeiaPalace,
            StoryFlag.ChewbaccaPalace => ChewbaccaPalace,
            StoryFlag.DarthPickup => DarthPickup,
            StoryFlag.DarthToLukeFinalBattle => DarthToLukeFinalBattle,
            StoryFlag.DarthToEmperorFinalBattle => DarthToEmperorFinalBattle,
            StoryFlag.FinalBattleReady => FinalBattleReady,
            StoryFlag.FinalBattle => FinalBattle,
            StoryFlag.BountyHuntersActive => BountyHuntersActive,
            _ => throw new ArgumentOutOfRangeException(nameof(flag)),
        };
        set
        {
            switch (flag)
            {
                case StoryFlag.LukeDagobahRequired: LukeDagobahRequired = value; break;
                case StoryFlag.LukeDagobah: LukeDagobah = value; break;
                case StoryFlag.LukePalace: LukePalace = value; break;
                case StoryFlag.HanCapturedAtPalace: HanCapturedAtPalace = value; break;
                case StoryFlag.LeiaPalace: LeiaPalace = value; break;
                case StoryFlag.ChewbaccaPalace: ChewbaccaPalace = value; break;
                case StoryFlag.DarthPickup: DarthPickup = value; break;
                case StoryFlag.DarthToLukeFinalBattle: DarthToLukeFinalBattle = value; break;
                case StoryFlag.DarthToEmperorFinalBattle: DarthToEmperorFinalBattle = value; break;
                case StoryFlag.FinalBattleReady: FinalBattleReady = value; break;
                case StoryFlag.FinalBattle: FinalBattle = value; break;
                case StoryFlag.BountyHuntersActive: BountyHuntersActive = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(flag));
            }
        }
    }
}

public static class Story
{

    /// <summary>Fixed objects.</summary>
    public const uint Dagobah = 0x98000441, Hermitage = 0x98000442, JabbasPalace = 0x98000481, BountyHunter = 0x3C000009,
        PalaceType = 0x64000044, BountyType = 0x65000083, DagobahType = 0x71000043, PickupType = 0x73000082;

    const uint Manager = 0xF8000006;

    public static bool Has(GameState game, StoryFlag flag) => game.StoryFlags[flag];

    /// <summary>A story character that resolves with its parent: a registry lookup with a parent set; Destroyed is not tested.</summary>
    static Character? Get(GameState game, uint sdRef) => game.Characters.FirstOrDefault(c => c.Ref == sdRef && c.Container is not null);

    static bool IsStory(uint sdRef) => sdRef is Refs.Luke or Refs.Leia or Refs.Han or Refs.Chewbacca or Refs.Vader or Refs.Palpatine;

    /// <summary>free = not Captured and Usable and not Autorouting and not Adrift; idle = free and not OnMission.</summary>
    static bool Free(Character c) => !c.Captured && c.Usable && !c.Autorouting && !c.Adrift;
    static bool Idle(Character c) => Free(c) && !c.OnMission;
    /// <summary>held = Captured and Usable and not Adrift and not OnMission and not Autorouting.</summary>
    static bool Held(Character c) => c.Captured && c.Usable && !c.Adrift && !c.OnMission && !c.Autorouting;

    // ---- character flags ----

    /// <summary>Sets Luke's KnowsHeritage to 1. Its hook recomputes FinalBattleReady.</summary>
    internal static void SetLukeKnowsHeritage(GameState game, Character luke)
    {
        if (luke.KnowsHeritage) return;
        luke.KnowsHeritage = true;
        game.StoryPost(0x211, luke, 0, 1); // UI 0x211, kind 4: joins the running duel's group
        RecomputeLukeReady(game, luke);
    }

    /// <summary>Luke FinalBattleReady := KnowsHeritage and Force >= G 0xC06. Its hook on change posts 0x313 to the story check.</summary>
    internal static void RecomputeLukeReady(GameState game, Character luke)
    {
        bool ready = luke.KnowsHeritage && luke.Force >= game.Params.General(0xC06);
        if (ready == luke.FinalBattleReady) return;
        luke.FinalBattleReady = ready;
        Objects.Notify(game, Check);
    }

    // ---- manager 0xF8 flags ----

    /// <summary>Setter for one manager flag. Hooks run on set; one flag also acts on clear.</summary>
    internal static void Set(GameState game, StoryFlag flag, bool value)
    {
        if (Has(game, flag) == value) return;
        game.StoryFlags[flag] = value;
        // UI 0x221 LukeDagobah / 0x220 FinalBattle
        if (flag is StoryFlag.LukeDagobah or StoryFlag.FinalBattle) game.StoryPost(flag == StoryFlag.LukeDagobah ? 0x221 : 0x220, null, value ? 0 : 1, value ? 1 : 0, live: SideSet.Both);
        if (flag == StoryFlag.BountyHuntersActive) { TimerCatalogue.Slot(game, 0x393, Manager, null).SetActive(game, value); return; }
        if (!value) return;
        switch (flag)
        {
            case StoryFlag.LukeDagobahRequired:
                DagobahCheck(game);
                TimerCatalogue.Slot(game, 0x392, Manager, null).SetActive(game, false);
                break;
            case StoryFlag.LukeDagobah: Create(game, game.Abode(Dagobah), DagobahType, Get(game, Refs.Luke)); break;
            case StoryFlag.LukePalace: Create(game, Get(game, Refs.Han), PalaceType, Get(game, Refs.Luke)); break;
            case StoryFlag.HanCapturedAtPalace:
                Set(game, StoryFlag.BountyHuntersActive, false);
                if (Get(game, Refs.Han) is { } held) Knowledge.Reveal(game, held, Owner.Alliance); // Reveal Han to the Alliance
                break;
            case StoryFlag.LeiaPalace: Create(game, Get(game, Refs.Han), PalaceType, Get(game, Refs.Leia)); break;
            case StoryFlag.ChewbaccaPalace: Create(game, Get(game, Refs.Han), PalaceType, Get(game, Refs.Chewbacca)); break;
            case StoryFlag.DarthPickup: Create(game, game.Abode(JabbasPalace), PickupType, Get(game, Refs.Vader)); break;
            case StoryFlag.DarthToLukeFinalBattle: ToLuke(game); break;
            case StoryFlag.DarthToEmperorFinalBattle: ToEmperor(game); break;
            case StoryFlag.FinalBattle: Battle(game); break;
        }
    }

    static void Pulse(GameState game, StoryFlag flag)
    {
        Set(game, flag, true);
        Set(game, flag, false);
    }

    /// <summary>
    /// Creates the mission for an objective of a member: owner = the member's, ObjectiveKey = objective, TargetKey = the objective's
    /// location as the owner's view sees it, origin = the member's location, Team = [member], no decoys,
    /// standard encounters, UserID 0, Mandatory; refused silently without a resolving member. An unknown target location still
    /// creates the mission (TargetKey / TargetLocationKey none; the location lookup only returns an error).
    /// </summary>
    static void Create(GameState game, GameObject? objective, uint type, Agent? member)
    {
        if (member?.Container is null || objective is null) return;
        var target = Knowledge.Location(game, objective, member.Owner);
        var m = Missions.Create(game, type, member.Owner, objective, [member], [], 0, mandatory: true, Missions.Location(member), targetKey: target);
        if (target is null) (m.TargetKey, m.TargetLocation) = (null, null); // failed target test: created anyway
    }

    // ---- story check ----

    /// <summary>Manager 0xF8 hooks of 0x300, 0x301, 0x320, 0x307 / 0x30E (after the grouping), 0x312, 0x30A (first step): the story check for the six story characters.</summary>
    internal static void Notified(GameState game, GameObject o)
    {
        if (o is Character c && IsStory(c.Ref)) Check(game);
    }

    /// <summary>Story check: items 1 to 10 in order, each independent; no draws.</summary>
    internal static void Check(GameState game)
    {
        var palace = game.Abode(JabbasPalace);
        Character? luke = Get(game, Refs.Luke), leia = Get(game, Refs.Leia), han = Get(game, Refs.Han),
            chewie = Get(game, Refs.Chewbacca), vader = Get(game, Refs.Vader), pal = Get(game, Refs.Palpatine);
        // Item 1
        Set(game, StoryFlag.HanCapturedAtPalace, han is not null && Held(han) && han.Container == palace);
        // Item 2
        RecomputeFinalBattleReady(game);
        // Item 3
        DagobahCheck(game);
        // Item 4
        if (Has(game, StoryFlag.HanCapturedAtPalace) && luke is not null && Free(luke))
        {
            if (!luke.OnMission) Pulse(game, StoryFlag.LukePalace);
            else if (luke.Mission is { Kind: 0x71, Phase: 8 } dagobah) Missions.SetCompletion(game, dagobah, 2); // Dagobah mission completion step
        }
        // Items 5 and 6
        if (Has(game, StoryFlag.HanCapturedAtPalace) && leia is not null && Idle(leia)) Pulse(game, StoryFlag.LeiaPalace);
        if (Has(game, StoryFlag.HanCapturedAtPalace) && chewie is not null && Idle(chewie)) Pulse(game, StoryFlag.ChewbaccaPalace);
        // Item 7
        if (luke is not null && leia is not null && han is not null && chewie is not null && vader is not null)
        {
            bool all = luke.Captured && leia.Captured && han.Captured && (chewie.Captured || chewie.Destroyed);
            bool any = palace.Contents.OfType<Character>().Any(c => Family.Of(c.Type) < 0x3C && c.Owner == Owner.Empire && c.Usable && c.Captured);
            if (any && all && Free(vader))
            {
                if (!vader.OnMission) Pulse(game, StoryFlag.DarthPickup);
                else if (vader.Mission is { Kind: not 0x73 } && vader.ResignOnInjury) vader.ResignRequest = true; // Vader requests resignation on injury unless he is on a Pickup mission
            }
        }
        // Items 8 to 10
        if (Has(game, StoryFlag.FinalBattleReady) && luke is not null && vader is not null && pal is not null && vader.Container != luke.Container)
            Pulse(game, StoryFlag.DarthToLukeFinalBattle);
        if (Has(game, StoryFlag.FinalBattleReady) && luke is not null && vader is not null && pal is not null && vader.Container == luke.Container && luke.Container != pal.Container)
            Pulse(game, StoryFlag.DarthToEmperorFinalBattle);
        if (Has(game, StoryFlag.FinalBattleReady) && luke is not null && vader is not null && pal is not null && vader.Container == luke.Container && luke.Container == pal.Container)
        {
            Set(game, StoryFlag.FinalBattle, true);
            RecomputeFinalBattleReady(game);
        }
    }

    /// <summary>
    /// Item 2: FinalBattle set gives 0; else Luke, Vader, Palpatine resolve and Luke held and Luke's parent is not Jabba's Palace and Luke
    /// FinalBattleReady and Vader idle and Palpatine idle.
    /// </summary>
    static void RecomputeFinalBattleReady(GameState game)
    {
        Character? luke = Get(game, Refs.Luke), vader = Get(game, Refs.Vader), pal = Get(game, Refs.Palpatine);
        Set(game, StoryFlag.FinalBattleReady, !Has(game, StoryFlag.FinalBattle) && luke is not null && vader is not null && pal is not null && Held(luke)
            && luke.Container != game.Abode(JabbasPalace) && luke.FinalBattleReady && Idle(vader) && Idle(pal));
    }

    /// <summary>Item 3: LukeDagobahRequired and not HanCapturedAtPalace and Luke idle pulses LukeDagobah.</summary>
    static void DagobahCheck(GameState game)
    {
        if (Has(game, StoryFlag.LukeDagobahRequired) && !Has(game, StoryFlag.HanCapturedAtPalace) && Get(game, Refs.Luke) is { } luke && Idle(luke)) Pulse(game, StoryFlag.LukeDagobah);
    }

    // ---- timers ----

    /// <summary>Timer 0x392 sets LukeDagobahRequired to 1. Its hook runs the Dagobah check, then the slot goes inactive.</summary>
    internal static void DagobahTimer(GameState game) => Set(game, StoryFlag.LukeDagobahRequired, true);

    /// <summary>
    /// Timer 0x393 action: chance(G 0x1806); if Han resolves, not Captured and Usable: a bounty hunter special
    /// force (0x3C000009) created in Jabba's Palace (palace owner, Created, Completed), then its Bounty mission against Han; a
    /// failed chance does nothing.
    /// </summary>
    internal static void BountyTimer(GameState game)
    {
        if (!game.Random.Chance(game.Params.General(0x1806), DrawSite.BountyHunters)) return;
        if (Get(game, Refs.Han) is not { Captured: false, Usable: true } han) return;
        var palace = game.Abode(JabbasPalace);
        var hunter = (Agent)Objects.Create(game, BountyHunter, palace.Owner, palace, completed: true);
        Knowledge.Reveal(game, han, Owner.Empire); // Reveal Han to the Empire
        Create(game, han, BountyType, hunter);
    }

    /// <summary>
    /// Manager 0xF8 hook for 0x312, either direction, agents [0x30, 0x40) that resolve with OnMission clear at dispatch: the
    /// deployment check, then an Existing bounty hunter (type 0x3C000009) disbands (DestroyedReason 0x14); then the story check.
    /// </summary>
    internal static void OnMissionChanged(GameState game, Agent a)
    {
        if (a.Container is not null && !a.OnMission)
        {
            Fleets.DeploymentCheck(game, a);
            if (a.Existing && a.Type == BountyHunter) Objects.Destroy(game, a, 0x14);
        }
        Notified(game, a);
    }

    // ---- mission classes ----

    /// <summary>Class probability: Palace EnhEsp div G 0x180A + EnhCombat div G 0x180B; Bounty, Dagobah, Vacation, Pickup 100.</summary>
    internal static int? Probability(GameState game, Mission m, Agent a) => m.Kind switch
    {
        0x64 => Missions.Enhanced(game, a, SkillIndex.Espionage) / game.Params.General(0x180A) + Missions.Enhanced(game, a, SkillIndex.Combat) / game.Params.General(0x180B),
        0x65 or 0x71 or 0x72 or 0x73 => 100,
        _ => null,
    };

    /// <summary>Class attempt; true means stop.</summary>
    internal static bool Attempt(GameState game, Mission m, Agent a, int p)
    {
        switch (m.Kind)
        {
            case 0x71 or 0x72:
                m.TaskStatus = 3;
                return true;
            case 0x64: return Palace(game, m, a, p);
            case 0x65: Bounty(game, m, a); return true;
            case 0x73: return Pickup(game, m, a);
        }
        return false;
    }

    /// <summary>
    /// Palace attempt: chance(p); success: stop, TaskStatus 3, a character member's base Combat += G 0x181A and
    /// Espionage += G 0x1819; Han (ObjectiveKey) rescued, then every Usable Captured character in id range [0x30, 0x3C)
    /// directly in the TargetKey (key order) rescued.
    /// </summary>
    static bool Palace(GameState game, Mission m, Agent a, int p)
    {
        if (!game.Random.Chance(p, DrawSite.Palace)) return false;
        m.TaskStatus = 3;
        if (a is Character)
        {
            a.Skills[SkillIndex.Combat] += game.Params.General(0x181A);
            a.Skills[SkillIndex.Espionage] += game.Params.General(0x1819);
        }
        if (m.Objective is Character han) Prisoners.Rescue(game, han);
        if (m.TargetKey is { } target)
            foreach (var c in target.Contents.OfType<Character>().Where(c => Family.Of(c.Type) < 0x3C && c.Usable).ToList())
                if (c.Captured) Prisoners.Rescue(game, c);
        return true;
    }

    /// <summary>Palace post without success: TaskStatus 2; every character member (all lists, list order) captured without captor, then CanEscape := 0.</summary>
    internal static void PalaceFailed(GameState game, Mission m)
    {
        m.TaskStatus = 2;
        foreach (var c in m.Members.OfType<Character>().ToList())
        {
            Prisoners.Capture(game, c, null);
            Prisoners.SetCanEscape(game, c, false);
        }
    }

    /// <summary>
    /// Bounty attempt: stop; ObjectiveKey Han: BountyAttack pulsed (no sim effect); Han's evasion chance(T(RLEVADTB,
    /// EnhCombat(Han))): success effect or failure effect (captor = the hunter); TaskStatus 3 if Han is now Captured (CanEscape := 0,
    /// CapturedByBountyHunters := 1), else 2. No post.
    /// </summary>
    static void Bounty(GameState game, Mission m, Agent hunter)
    {
        if (m.Objective is not Character { Type: Refs.Han } han) return;
        if (game.Random.Chance(Missions.Table(game, "RLEVADTB", Missions.Enhanced(game, han, SkillIndex.Combat)) ?? 0, DrawSite.EncounterCheck))
            Missions.Evaded(game, han);
        else Prisoners.Capture(game, han, hunter);
        if (!han.Captured) { m.TaskStatus = 2; Cleared(game, han); return; }
        m.TaskStatus = 3;
        Prisoners.SetCanEscape(game, han, false);
        han.CapturedByBountyHunters = true;
        Cleared(game, han);
    }

    /// <summary>BountyAttack := 0 at the end of the attempt: UI 0x212 clear, posted by Han's live views.</summary>
    static void Cleared(GameState game, Character han) => game.StoryPost(0x212, han, 1, 0);

    /// <summary>
    /// Pickup attempt: every character [0x30, 0x3C) directly in the TargetLocation (Jabba's Palace), Usable, of the
    /// mission's owner (key order), not OnMission and Captured: stop, joins this mission (Captured list), its ParentAtMission :=
    /// the attempting member's, TaskStatus 3. PickupInProgress is not kept (no sim reader); only its UI pulse 0x231 is posted.
    /// </summary>
    static bool Pickup(GameState game, Mission m, Agent member)
    {
        bool stop = false;
        if (m.TargetLocation is not { } palace) return false;
        // PickupInProgress pulse: UI 0x231 set and clear; manager 0xFA's views posting for both sides (Inferred, as 0xF8)
        game.StoryPost(0x231, null, 0, 1, live: SideSet.Both);
        foreach (var c in palace.Contents.OfType<Character>().Where(c => Family.Of(c.Type) < 0x3C && c.Usable && c.Owner == m.Owner).ToList())
        {
            if (c.OnMission || !c.Captured) continue;
            stop = true;
            Missions.Join(game, m, c, decoy: false);
            (c.ParentAtMission, c.LocationAtMission) = (member.ParentAtMission, member.ParentAtMission?.System);
            m.TaskStatus = 3;
        }
        game.StoryPost(0x231, null, 1, 0, live: SideSet.Both);
        return stop;
    }

    /// <summary>
    /// Dagobah phase hook after the standard one: entering phase 8 StartDay := the game day;
    /// entering 11 with LastPhase >= 8 and a start day, Luke resolving: Force F becomes F + pct(F, G 0x181E) if TaskStatus 3, else
    /// F + pct(F, (day - start) div G 0x181F), written to ForceRank; Luke DagobahCompleted := 1.
    /// </summary>
    internal static void DagobahPhase(GameState game, Mission m, int phase)
    {
        if (phase == 8) m.StartDay = game.Day;
        if (phase != 11 || m.LastPhase < 8 || m.StartDay == 0 || Get(game, Refs.Luke) is not { } luke) return;
        var g = game.Params;
        int f = luke.Force;
        int f2 = f + Pct(f, m.TaskStatus == 3 ? g.General(0x181E) : (game.Day - m.StartDay) / g.General(0x181F));
        Jedi.SetForceRank(game, luke, luke.ForceRank + f2 - f);
        if (!luke.DagobahCompleted) game.StoryPost(0x210, luke, 0, 1); // UI 0x210
        luke.DagobahCompleted = true;
    }

    /// <summary>Part of the 0x30D handler for type 0x71000043 (Dagobah): phase 8 runs the story check; phase 11 with LastPhase >= 8 sets LukeDagobahRequired to 0.</summary>
    internal static void DagobahPhaseChanged(GameState game, Mission m, int now)
    {
        if (m.Type != DagobahType) return;
        if (now == 8) Check(game);
        else if (now == 11 && m.LastPhase >= 8) Set(game, StoryFlag.LukeDagobahRequired, false);
    }

    // ---- final battle ----

    /// <summary>
    /// Move creators (type 0x41000001): ObjectiveKey = TargetKey = objective, origin = the team's common
    /// location (none gives no mission, no serial), no decoys, ImpliedTeam 0, Mandatory 1; the first creator uses the class encounter codes,
    /// the second uses none (no encounters).
    /// </summary>
    static void Move(GameState game, Owner owner, GameObject objective, List<Agent> team, EncounterCodes? encounters)
    {
        var origin = Missions.Location(team[0]);
        if (origin is null || team.Any(a => Missions.Location(a) != origin)) return;
        Missions.Create(game, 0x41000001, owner, objective, team, [], 0, mandatory: true, origin, encounters);
    }

    /// <summary>DarthToLuke: Move of Vader (his owner) to Luke's parent.</summary>
    static void ToLuke(GameState game)
    {
        if (Get(game, Refs.Vader) is { } vader && Get(game, Refs.Luke) is { Container: { } at }) Move(game, vader.Owner, at, [vader], null);
    }

    /// <summary>DarthToEmperor: only if Vader and Luke are owned by side 2 and neither is OnMission: Move of [Vader, Luke] to Palpatine's parent.</summary>
    static void ToEmperor(GameState game)
    {
        if (Get(game, Refs.Vader) is { Owner: Owner.Empire, OnMission: false } vader && Get(game, Refs.Luke) is { Owner: Owner.Empire, OnMission: false } luke
            && Get(game, Refs.Palpatine) is { Container: { } at })
            Move(game, vader.Owner, at, [vader, luke], null);
    }

    /// <summary>
    /// FinalBattle hook: Luke, Vader, Palpatine with one common parent. Luke's Force >= G 0x1807: Luke released,
    /// Vader then Palpatine captured by Luke, both CanEscape := 0; else Luke CanEscape := 0 and wound roll(G 0x1809) + G 0x1808
    /// (wound reason 10). Winner W (Luke / Vader) with an owner that differs from its parent's owner: fallback destination for W (flag 0); if found,
    /// a Move of [Luke, Vader, Palpatine] with owner = outcome (1 / 2) and encounters off.
    /// </summary>
    static void Battle(GameState game)
    {
        Character? luke = Get(game, Refs.Luke), vader = Get(game, Refs.Vader), pal = Get(game, Refs.Palpatine);
        if (luke is null || vader is null || pal is null || luke.Container != vader.Container || luke.Container != pal.Container) return;
        var g = game.Params;
        Knowledge.UpdateAt(game, luke, 6, Owner.Alliance, Owner.Empire); // step 1
        bool won = luke.Force >= g.General(0x1807);
        if (won)
        {
            Prisoners.Release(game, luke);
            Prisoners.Capture(game, vader, luke);
            Prisoners.Capture(game, pal, luke);
            Prisoners.SetCanEscape(game, vader, false);
            Prisoners.SetCanEscape(game, pal, false);
        }
        else
        {
            Prisoners.SetCanEscape(game, luke, false);
            Missions.ApplyWound(game, luke, game.Random.Roll(g.General(0x1809), DrawSite.FinalBattle) + g.General(0x1808));
        }
        var w = won ? luke : vader;
        if (w.Container is { } p && w.Owner != p.Owner && Missions.Fallback(game, w, flag: false) is { } d)
            Move(game, won ? Owner.Alliance : Owner.Empire, d, [luke, vader, pal], EncounterCodes.None);
    }
}

/// <summary>
/// A story / Force UI notification: id (0x1E1, 0x200, 0x210, 0x211, 0x212, 0x220, 0x221, 0x230,
/// 0x231, 0x360, 0x361, 0x362), key, the posting sides (Alliance, Empire, or both: the key's live views; managers post to both), old / new,
/// the other key (0x360 encounter, 0x361 traitor, 0x362 discovered, 0x200 mission), the context group (0 = its own). UI only.
/// </summary>
public sealed record StoryEvent(int Day, int Kind, GameObject? Key, SideSet Live, long Old, long New, GameObject? Other, int Group);
