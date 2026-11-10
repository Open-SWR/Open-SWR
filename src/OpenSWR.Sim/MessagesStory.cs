using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Voice request numbers of the story and Force messages (the sprite DLL voice ids, picked to a WAVE by the message).
/// Each name is the message's situation and the speaker.
/// </summary>
public static class StoryVoice
{
    public const int EmperorArrives = 304, HanCaptured = 287, HanAttacked = 288, LeiaDiscovery = 292, LukeDiscovery = 309,
        VaderDiscovery = 310, LeiaGrowth = 308, LukeGrowth = 306, TrainedLukeGrowth = 307;
    public const int FinalBattle = 284, TrainedLukeFinalBattle = 303, Heritage = 286, LeiaVsVader = 311, LeiaVsPalpatine = 312;
    public const int LukeVsVader = 280, LukeVsPalpatine = 281, TrainedLukeVsVader = 282, TrainedLukeVsPalpatine = 283;
    public const int TrainedLukeDetected = 218, LukeDetected = 208, LeiaDetected = 228, LukeGoesDagobah = 285;
    public const int LeiaTraitor = 295, LukeTraitor = 293, TrainedLukeTraitor = 294, VaderTraitor = 296, PalpatineTraitor = 297;
    public const int LeiaRescue = 229, LukeRescue = 209, TrainedLukeRescue = 219;
    public const int PrisonersDelivered = 291, TrainedPrisonersDelivered = 313;
}

/// <summary>
/// Story and Force messages, types MessageType.BountyHunters, MessageType.ForceDiscovered, MessageType.ForceGrowth, MessageType.ForceEncounter-MessageType.JabbaPrisoners.
/// Each record of <see cref="GameState.StoryTrace"/> is one post; a side sees only the posts of its views (<see cref="StoryEvent.Live"/>).
/// The handlers read the side's views at the update (= the end of the tick). Kind-4 groups other than type MessageType.ForceEncounter's give
/// one message per acting post (Inferred: one acting post per context for StoryPostKind.PalaceAgentEnroute / StoryPostKind.BountyAttack / StoryPostKind.PickupInProgress).
/// shortcut: Injury, Force and the story flags of a frozen character view read the real values (View keeps no copy of them);
/// upgrade with View snapshot fields if a case shows the difference.
/// </summary>
public sealed partial class Messages
{
    /// <summary>A view of the object exists: every object has both side views from its creation until its deletion.</summary>
    static bool Exists(GameObject? o) => o is { Deleted: false };

    static bool IsCharacter(GameObject? o) => o is Character && Family.Of(o.Type) is >= Families.MajorCharacter and < Families.SpecialForce;

    bool Captured(GameObject o) => Knowledge.See(_game, o, _side).Captured;

    static bool Jedi(Character? c) => c is not null && c.DagobahCompleted;

    /// <summary>RoleMissionKey of side s's view: the real mission while live, else the frozen view's.</summary>
    Mission? Role(Character c, Owner side) => Knowledge.IsLive(c, side) ? c.Mission : _game.Views.TryGetValue((c, side), out var v) ? v.RoleMission : null;

    /// <summary>Character advisor code: majors [48, 56) by key (others 0), anything outside the range or none AdvisorCode.MinorCharacterMessage.</summary>
    static AdvisorCode CharacterCode(GameObject? o) => o is null || Family.Of(o.Type) is not (>= Families.MajorCharacter and < Families.MinorCharacter) ? AdvisorCode.MinorCharacterMessage : o.Type switch
    {
        Mothma => AdvisorCode.MothmaMessage, Refs.Leia => AdvisorCode.LeiaMessage, Refs.Luke => AdvisorCode.LukeMessage, Refs.Han => AdvisorCode.HanMessage, Refs.Palpatine => AdvisorCode.PalpatineMessage, Refs.Vader => AdvisorCode.VaderMessage, _ => AdvisorCode.None,
    };

    /// <summary>Force rank title (placeholder kind 7): TEXTSTRA 34644 / 34633 / 34640 / 34641 / 34642 / 34643 at &lt; 10 / 20 / 80 / 100 / 120 / more.</summary>
    public static int RankTitle(int force) => force < 10 ? 34644 : force < 20 ? 34633 : force < 80 ? 34640 : force < 100 ? 34641 : force < 120 ? 34642 : 34643;

    // the name function resolves a StringId of module 2 through the whole TEXTSTRA string table (GalaxyMap.DisplayName)
    string Text(int stringId) => _name(new SdCommon(0, 0, 0, 0, 0, (ushort)stringId, 2));

    GameMessage Say(int day, MessageType type, int kind, MessageCategory category, MessageText title, IEnumerable<MessageText> body, GameObject? subject,
        int picture, int sound, AdvisorCode code, int voice, int overlay = 0)
    {
        var m = Tag(Add(day, type, category, kind, title, body, subject, picture, sound), code);
        m.Overlay = overlay;
        if (voice != 0 && _voice is not null) m.Voice = _voice(voice);
        return m;
    }

    int SidePicture => _side == Owner.Alliance ? 1042 : 1043;
    int SideSound => _side == Owner.Alliance ? 1148 : 1149;

    void StoryAndForce()
    {
        var trace = _game.StoryTrace!;
        var posts = trace.Skip(_story).Where(e => e.Live[_side]).ToList();
        var groups = new HashSet<int>();
        foreach (var e in posts)
            switch (e.Kind)
            {
                case StoryPostKind.BountyAttack when e.New == 0 && e.Key is Character { Type: Refs.Han } han: Bounty(e.Day, han); break;
                case StoryPostKind.ForceUserDiscovered: Discovered(e.Day, e.Key, e.Other); break;
                case StoryPostKind.ForceChanged: ForceGrowth(e); break;
                case StoryPostKind.FinalBattle: Encounter(e.Day, [e]); break;
                // kind 4: the pairing's sets and the resolution's clears + StoryPostKind.Heritage form one group each, so one duel gives two messages
                // (Inferred context reading); a post outside a duel context is a group of its own
                case StoryPostKind.ForceEncounter or StoryPostKind.Heritage when e.Group == 0: Encounter(e.Day, [e]); break;
                case StoryPostKind.ForceEncounter or StoryPostKind.Heritage when groups.Add(e.Group): Encounter(e.Day, [.. posts.Where(x => x.Group == e.Group && x.Kind is StoryPostKind.ForceEncounter or StoryPostKind.Heritage)]); break;
                case StoryPostKind.LukeDagobah when e.New != 0: Dagobah(e.Day); break;
                case StoryPostKind.DagobahCompleted when e.New != 0: DagobahLeft(e.Day); break;
                case StoryPostKind.Traitor: Traitor(e.Day, e.Key, e.Other); break;
                case StoryPostKind.PalaceAgentEnroute when e.New != 0: Rescue(e.Day, e.Key, e.Other); break;
                case StoryPostKind.PickupInProgress when e.New != 0: Pickup(e.Day); break;
            }
        if (_ownsStory) { trace.Clear(); _story = 0; }
        else _story = trace.Count;
        // type MessageType.EmperorArrives (the Emperor reaching Coruscant): manager Families.CharacterManager's Emperor-at-Coruscant flag (derived in openswr, compared per update), side 2 only
        if (_side == Owner.Empire && !_emperor && _game.EmperorAtCoruscant)
            Say(_game.Day, MessageType.EmperorArrives, 3, MessageCategory.Mission, T(TextId.EmperorArrivesCoruscant), [T(TextId.ReturnedToSeatOfPower)], _game.Known.Palpatine, 1068, 1147, AdvisorCode.PalpatineMessage, StoryVoice.EmperorArrives);
    }

    /// <summary>
    /// Type MessageType.BountyHunters (StoryPostKind.BountyAttack clear): H = the side's view of Han. Captured gives TextId.HanCapturedTitle / TextId.HanCapturedTakenTo, picture 1053, sound 1142, code
    /// AdvisorCode.HanCaptured, voice StoryVoice.HanCaptured, no subject; else side 1 gives TextId.HanAttackedTitle / TextId.HanAttackedBody, 1052, 1141, code AdvisorCode.HanMessage, voice StoryVoice.HanAttacked; else side 2 gives the first
    /// system view up H's view parents: TextId.BountyHuntersLocateSolo / TextId.BountyHuntersReportedHan (system name), 1054, 1143, code AdvisorCode.BountyHuntersFoundHan, no voice; none gives no message. Subject Han.
    /// </summary>
    void Bounty(int day, Character han)
    {
        if (Captured(han)) { Say(day, MessageType.BountyHunters, 3, MessageCategory.Mission, T(TextId.HanCapturedTitle), [T(TextId.HanCapturedTakenTo)], null, 1053, 1142, AdvisorCode.HanCaptured, StoryVoice.HanCaptured); return; }
        if (_side == Owner.Alliance) { Say(day, MessageType.BountyHunters, 3, MessageCategory.Mission, T(TextId.HanAttackedTitle), [T(TextId.HanAttackedBody)], han, 1052, 1141, AdvisorCode.HanMessage, StoryVoice.HanAttacked); return; }
        for (var p = Knowledge.ViewParent(_game, han, _side); p is not null; p = Knowledge.ViewParent(_game, p, _side))
            if (p is StarSystem s)
            {
                Say(day, MessageType.BountyHunters, 3, MessageCategory.Mission, T(TextId.BountyHuntersLocateSolo), [T(TextId.BountyHuntersReportedHan, Name(s))], han, 1054, 1143, AdvisorCode.BountyHuntersFoundHan, 0);
                return;
            }
    }

    /// <summary>
    /// Type MessageType.ForceDiscovered (StoryPostKind.ForceUserDiscovered of the discoverer D, value X): both characters with views, D's definition side = the message
    /// side. X Leia: TextId.LeiaUsesForce / TextId.MyHeritageAsMember, overlay X's portrait, code AdvisorCode.LeiaMessage, voice StoryVoice.LeiaDiscovery. Else TextId.FutureJediDiscovered (X, D) with body TextId.ForceUserDetectedWe (X) when D
    /// passes the teacher test (IsJediTrainer, Force >= G 2581) else TextId.ForceUserDetectedOnce (X); overlay D's portrait; code by D; voices Leia StoryVoice.LeiaDiscovery,
    /// Luke StoryVoice.LukeDiscovery, Vader StoryVoice.VaderDiscovery. Picture 1042 / 1043, sound 1148 / 1149, subject X.
    /// </summary>
    void Discovered(int day, GameObject? d, GameObject? x)
    {
        if (!Exists(d) || !Exists(x) || !IsCharacter(d) || !IsCharacter(x) || Finders.DefinitionSide((Character)d!) != _side) return;
        var (dc, xc) = ((Character)d!, (Character)x!);
        if (xc.Type == Refs.Leia)
        {
            Say(day, MessageType.ForceDiscovered, 3, MessageCategory.Mission, T(TextId.LeiaUsesForce), [T(TextId.MyHeritageAsMember)], xc, SidePicture, SideSound, AdvisorCode.LeiaMessage, StoryVoice.LeiaDiscovery, Pic(xc));
            return;
        }
        bool teacher = dc.Definition.IsJediTrainer != 0 && dc.Force >= _game.Rules.JediTeacherMinForce;
        int voice = dc.Type switch { Refs.Leia => StoryVoice.LeiaDiscovery, Refs.Luke => StoryVoice.LukeDiscovery, Refs.Vader => StoryVoice.VaderDiscovery, _ => 0 };
        Say(day, MessageType.ForceDiscovered, 3, MessageCategory.Mission, T(TextId.FutureJediDiscovered, Name(xc), Name(dc)), [T(teacher ? TextId.ForceUserDetectedWe : TextId.ForceUserDetectedOnce, Name(xc))], xc,
            SidePicture, SideSound, CharacterCode(dc), voice, Pic(dc));
    }

    /// <summary>
    /// Type MessageType.ForceGrowth (StoryPostKind.ForceChanged): a character with a view, old != 0 and new != 0, definition side not the enemy's. TextId.ForceGrowth (name),
    /// body TextId.MyRecentExperienceGiven (the rank title of the view's Force, placeholder kind 7); overlay its portrait; picture 1042 / 1043, sound 1148 /
    /// 1149; code by C; voices Leia StoryVoice.LeiaGrowth, Luke StoryVoice.LukeGrowth (StoryVoice.TrainedLukeGrowth with DagobahCompleted). Subject C.
    /// </summary>
    void ForceGrowth(StoryEvent e)
    {
        if (e.Key is not Character c || !Exists(c) || !IsCharacter(c) || e.Old == 0 || e.New == 0 || Finders.DefinitionSide(c) == Enemy) return;
        int voice = c.Type switch { Refs.Leia => StoryVoice.LeiaGrowth, Refs.Luke => Jedi(c) ? StoryVoice.TrainedLukeGrowth : StoryVoice.LukeGrowth, _ => 0 };
        Say(e.Day, MessageType.ForceGrowth, 3, MessageCategory.Mission, T(TextId.ForceGrowth, Name(c)), [T(TextId.MyRecentExperienceGiven, Text(RankTitle(c.Force)))], c, SidePicture, SideSound,
            CharacterCode(c), voice, Pic(c));
    }

    /// <summary>
    /// Type MessageType.ForceEncounter for one group: StoryPostKind.ForceEncounter gives A (definition side 1) / E (side 2), subject = the participant of the
    /// message side; a missing view, a non-character or definition side 0 gives kill (no message); StoryPostKind.Heritage gives heritage; StoryPostKind.FinalBattle gives final battle.
    /// Close: final battle gives TextId.FinalBattle + TextId.LukeBroughtBeforeEmperorVaderLuke / TextId.LukeBroughtBeforeEmperorVaderEmperor by Luke's view Captured, picture / sound / voice 1064 / 1139 / StoryVoice.TrainedLukeFinalBattle
    /// with DagobahCompleted else 1063 / 1138 / StoryVoice.FinalBattle, subject Luke; Luke or Leia against Vader or Palpatine gives heritage (F 1) or
    /// confrontation; else detection. Code by the subject (none gives AdvisorCode.MinorCharacterMessage), voices as the builder sets them.
    /// </summary>
    void Encounter(int day, List<StoryEvent> group)
    {
        bool kill = false, heritage = false, finalBattle = false;
        Character? a = null, en = null;
        GameObject? subject = null;
        foreach (var e in group)
            switch (e.Kind)
            {
                case StoryPostKind.ForceEncounter when !Exists(e.Key) || !IsCharacter(e.Key) || Finders.DefinitionSide((Character)e.Key!) == Owner.None:
                    kill = true;
                    break;
                case StoryPostKind.ForceEncounter when Finders.DefinitionSide((Character)e.Key!) == Owner.Alliance:
                    a = (Character)e.Key!;
                    if (_side == Owner.Alliance) subject = a;
                    break;
                case StoryPostKind.ForceEncounter:
                    en = (Character)e.Key!;
                    if (_side == Owner.Empire) subject = en;
                    break;
                case StoryPostKind.Heritage: heritage = true; break;
                case StoryPostKind.FinalBattle: finalBattle = true; break;
            }
        if (kill) return;
        if (finalBattle)
        {
            if (_game.Known.Luke is not { } luke || !Exists(luke)) return;
            bool j = Jedi(luke);
            Say(day, MessageType.ForceEncounter, 3, MessageCategory.Mission, T(TextId.FinalBattle), [T(Captured(luke) ? TextId.LukeBroughtBeforeEmperorVaderEmperor : TextId.LukeBroughtBeforeEmperorVaderLuke)], luke, j ? 1064 : 1063, j ? 1139 : 1138,
                CharacterCode(luke), j ? StoryVoice.TrainedLukeFinalBattle : StoryVoice.FinalBattle);
        }
        else if (a is { Type: Refs.Luke or Refs.Leia } && en is { Type: Refs.Vader or Refs.Palpatine })
        {
            if (heritage) Heritage(day, a, en, subject);
            else Confrontation(day, a, en, subject);
        }
        else Detection(day, a, en, subject);
    }

    /// <summary>
    /// Heritage: only Luke against Vader, both views present. TextId.LukeDiscoversHisHeritage, body TextId.LukeLearnedVaderHis + Luke Captured: TextId.LukeTakenToEmperor when the
    /// Empire's views of Luke and Vader hold one RoleMissionKey whose side-2 view targets Palpatine's side-2 view parent
    /// (DarthToEmperor), else TextId.LukeCapturedVader; not Captured: TextId.LukeInjuredDuringBattle when injured, else TextId.LukeEscapedCaptureAfter when Vader is not Captured; then Vader Captured
    /// TextId.DarthVaderCapturedLuke, else injured TextId.VaderInjuredDuringBattle. Picture 1058, sound 1133, voice StoryVoice.Heritage.
    /// </summary>
    void Heritage(int day, Character luke, Character vader, GameObject? subject)
    {
        if (luke.Type != Refs.Luke || vader.Type != Refs.Vader || !Exists(luke) || !Exists(vader)) return;
        var body = new List<MessageText> { T(TextId.LukeLearnedVaderHis) };
        bool vaderCaptured = Captured(vader);
        if (Captured(luke))
        {
            var pal = _game.Known.Palpatine;
            var m = Role(luke, Owner.Empire);
            // shortcut: a frozen side-2 mission view keeps no TargetKey, so only a live one can match (the Empire's own mission is live);
            // upgrade with a View TargetKey snapshot if an Alliance mission can ever be the role mission of both Luke and Vader
            bool emperor = m is not null && m == Role(vader, Owner.Empire) && pal is not null && Exists(pal) && Exists(m) && Knowledge.IsLive(m, Owner.Empire)
                && m.TargetKey is { } tk && tk == Knowledge.ViewParent(_game, pal, Owner.Empire);
            body.Add(T(emperor ? TextId.LukeTakenToEmperor : TextId.LukeCapturedVader));
        }
        else if (luke.Injury != 0) body.Add(T(TextId.LukeInjuredDuringBattle));
        else if (!vaderCaptured) body.Add(T(TextId.LukeEscapedCaptureAfter));
        if (vaderCaptured) body.Add(T(TextId.DarthVaderCapturedLuke));
        else if (vader.Injury != 0) body.Add(T(TextId.VaderInjuredDuringBattle));
        Say(day, MessageType.ForceEncounter, 3, MessageCategory.Mission, T(TextId.LukeDiscoversHisHeritage), body, subject, 1058, 1133, CharacterCode(subject), StoryVoice.Heritage);
    }

    /// <summary>
    /// Confrontation: TextId.Confronts / TextId.Fought (A, E) + A Captured TextId.WasInjuredTitle / injured TextId.WasInjured, E Captured TextId.CombatantCaptured / injured TextId.BothCombatantsEscapedUninjuredTitle,
    /// none of them TextId.BothCombatantsEscapedUninjured; picture / sound / voice by A (Leia; Luke without / with DagobahCompleted) and E (Vader / Palpatine).
    /// </summary>
    void Confrontation(int day, Character a, Character e, GameObject? subject)
    {
        if (!Exists(a) || !Exists(e)) return;
        var body = new List<MessageText> { T(TextId.Fought, Name(a), Name(e)) };
        if (Captured(a)) body.Add(T(TextId.WasInjuredTitle, Name(a)));
        else if (a.Injury != 0) body.Add(T(TextId.WasInjured, Name(a)));
        if (Captured(e)) body.Add(T(TextId.CombatantCaptured, Name(e)));
        else if (e.Injury != 0) body.Add(T(TextId.BothCombatantsEscapedUninjuredTitle, Name(e)));
        if (body.Count == 1) body.Add(T(TextId.BothCombatantsEscapedUninjured));
        bool vader = e.Type == Refs.Vader;
        var (picture, sound, voice) = a.Type == Refs.Leia ? vader ? (1055, 1130, StoryVoice.LeiaVsVader) : (1056, 1131, StoryVoice.LeiaVsPalpatine)
            : !Jedi(a) ? vader ? (1059, 1134, StoryVoice.LukeVsVader) : (1061, 1136, StoryVoice.LukeVsPalpatine)
            : vader ? (1060, 1135, StoryVoice.TrainedLukeVsVader) : (1062, 1137, StoryVoice.TrainedLukeVsPalpatine);
        Say(day, MessageType.ForceEncounter, 3, MessageCategory.Mission, T(TextId.Confronts, Name(a), Name(e)), body, subject, picture, sound, CharacterCode(subject), voice);
    }

    /// <summary>
    /// Detection: own = A (side 1) / E (side 2), other = the other participant; either missing gives none. TextId.DetectsEnemy / TextId.DetectedBecauseDisturbanceForce
    /// (own, other), picture 1042 / 1043, sound 1148 / 1149, overlay own's portrait; voice own Luke StoryVoice.TrainedLukeDetected (DagobahCompleted) / StoryVoice.LukeDetected,
    /// own Leia StoryVoice.LeiaDetected.
    /// </summary>
    void Detection(int day, Character? a, Character? e, GameObject? subject)
    {
        var (own, other) = _side == Owner.Alliance ? (a, e) : (e, a);
        if (own is null || other is null || !Exists(own) || !Exists(other)) return;
        int voice = own.Type switch { Refs.Luke => Jedi(own) ? StoryVoice.TrainedLukeDetected : StoryVoice.LukeDetected, Refs.Leia => StoryVoice.LeiaDetected, _ => 0 };
        Say(day, MessageType.ForceEncounter, 3, MessageCategory.Mission, T(TextId.DetectsEnemy, Name(own), Name(other)), [T(TextId.DetectedBecauseDisturbanceForce, Name(own), Name(other))], subject,
            SidePicture, SideSound, CharacterCode(subject), voice, Pic(own));
    }

    /// <summary>Type MessageType.Dagobah (StoryPostKind.LukeDagobah set): side 1 only, the Alliance view of Luke on a Dagobah mission (family 113): TextId.LukeGoesDagobah / TextId.LukeSentDagobahTrained, 1057, 1132, code AdvisorCode.LukeMessage, voice StoryVoice.LukeGoesDagobah, kind 8, no subject.</summary>
    void Dagobah(int day)
    {
        if (_side != Owner.Alliance || _game.Known.Luke is not { } luke || Role(luke, Owner.Alliance) is not { Kind: MissionKind.Dagobah }) return;
        Say(day, MessageType.Dagobah, 8, MessageCategory.Mission, T(TextId.LukeGoesDagobah), [T(TextId.LukeSentDagobahTrained)], null, 1057, 1132, AdvisorCode.LukeMessage, StoryVoice.LukeGoesDagobah);
    }

    /// <summary>Type MessageType.Dagobah (StoryPostKind.DagobahCompleted set): side 1 only, Luke's view present: TextId.LukeLeavesDagobah / TextId.DagobahTrainingFinished, picture 1042, sound 1148, overlay Luke's portrait, code AdvisorCode.LukeMessage, no voice, kind 8, no subject.</summary>
    void DagobahLeft(int day)
    {
        if (_side != Owner.Alliance || _game.Known.Luke is not { } luke || !Exists(luke)) return;
        Say(day, MessageType.Dagobah, 8, MessageCategory.Mission, T(TextId.LukeLeavesDagobah), [T(TextId.DagobahTrainingFinished)], null, 1042, 1148, AdvisorCode.LukeMessage, 0, Pic(luke));
    }

    /// <summary>
    /// Type MessageType.Traitor (StoryPostKind.Traitor): discoverer D and traitor T characters with views, D's definition side not the enemy's: TextId.DiscoversTraitor
    /// (D, T), TextId.TraitorThroughForce (D, T, the enemy side's name); overlay D's portrait, picture 1042 / 1043, sound 1148 / 1149, code by D; voices
    /// Leia StoryVoice.LeiaTraitor, Luke StoryVoice.LukeTraitor (StoryVoice.TrainedLukeTraitor with DagobahCompleted), Palpatine StoryVoice.PalpatineTraitor, Vader StoryVoice.VaderTraitor. Subject D.
    /// </summary>
    void Traitor(int day, GameObject? d, GameObject? t)
    {
        if (!IsCharacter(d) || !IsCharacter(t) || !Exists(d) || !Exists(t) || Finders.DefinitionSide((Character)d!) == Enemy) return;
        var dc = (Character)d!;
        int voice = dc.Type switch { Refs.Leia => StoryVoice.LeiaTraitor, Refs.Luke => Jedi(dc) ? StoryVoice.TrainedLukeTraitor : StoryVoice.LukeTraitor, Refs.Palpatine => StoryVoice.PalpatineTraitor, Refs.Vader => StoryVoice.VaderTraitor, _ => 0 };
        Say(day, MessageType.Traitor, 3, MessageCategory.Mission, T(TextId.DiscoversTraitor, Name(dc), Name(t)), [T(TextId.TraitorThroughForce, Name(dc), Name(t), MessageArg.Of(Enemy))], dc,
            SidePicture, SideSound, CharacterCode(dc), voice, Pic(dc));
    }

    /// <summary>
    /// Type MessageType.PalaceRescue (StoryPostKind.PalaceAgentEnroute set of a Palace-mission agent): side 1 only, a character on a mission of family 100 that leads
    /// it (the mission's LeaderKey): TextId.RescueAttempt / TextId.HanTakenToJabbasPalace (C), overlay its portrait, picture 1042, sound 1148, code by C; voices Leia StoryVoice.LeiaRescue, Luke StoryVoice.LukeRescue
    /// (StoryVoice.TrainedLukeRescue with DagobahCompleted). Kind 8, no category. Subject C.
    /// </summary>
    void Rescue(int day, GameObject? c, GameObject? mission)
    {
        if (_side != Owner.Alliance || c is not Character ch || !IsCharacter(ch) || mission is not Mission { Kind: MissionKind.Palace } pm || pm.Leader != ch) return;
        int voice = ch.Type switch { Refs.Leia => StoryVoice.LeiaRescue, Refs.Luke => Jedi(ch) ? StoryVoice.TrainedLukeRescue : StoryVoice.LukeRescue, _ => 0 };
        Say(day, MessageType.PalaceRescue, 8, 0, T(TextId.RescueAttempt, Name(ch)), [T(TextId.HanTakenToJabbasPalace, Name(ch))], ch, 1042, 1148, CharacterCode(ch), voice, Pic(ch));
    }

    /// <summary>Type MessageType.JabbaPrisoners (StoryPostKind.PickupInProgress set): either side; picture / sound / voice by Luke's DagobahCompleted: 1067 / 1146 / StoryVoice.TrainedPrisonersDelivered, else 1066 / 1145 / StoryVoice.PrisonersDelivered; TextId.JabbaDeliversPrisoners / TextId.JabbaDeliveredLukeHan, code AdvisorCode.VaderMessage, subject Vader.</summary>
    void Pickup(int day)
    {
        bool j = Jedi(_game.Known.Luke);
        Say(day, MessageType.JabbaPrisoners, 3, MessageCategory.Mission, T(TextId.JabbaDeliversPrisoners), [T(TextId.JabbaDeliveredLukeHan)], _game.Known.Vader, j ? 1067 : 1066, j ? 1146 : 1145, AdvisorCode.VaderMessage, j ? StoryVoice.TrainedPrisonersDelivered : StoryVoice.PrisonersDelivered);
    }
}
