using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Story and Force messages, types 0x1B, 0x1C, 0x1D, 0x24-0x29.
/// Each record of <see cref="GameState.StoryTrace"/> is one post; a side sees only the posts of its views (<see cref="StoryEvent.Live"/>).
/// The handlers read the side's views at the update (= the end of the tick). Kind-4 groups other than type 0x24's give
/// one message per acting post (Inferred: one acting post per context for 0x200 / 0x212 / 0x231).
/// shortcut: Injury, Force and the story flags of a frozen character view read the real values (View keeps no copy of them);
/// upgrade with View snapshot fields if a case shows the difference.
/// </summary>
public sealed partial class Messages
{
    /// <summary>A view of the object exists: every object has both side views from its creation until its deletion.</summary>
    static bool Exists(GameObject? o) => o is { Deleted: false };

    static bool IsCharacter(GameObject? o) => o is Character && Family.Of(o.Type) is >= 0x30 and < 0x3C;

    Character? Major(uint type) => _game.Characters.FirstOrDefault(c => c.Type == type);

    bool Captured(GameObject o) => Knowledge.See(_game, o, _side).Captured;

    static bool Jedi(Character? c) => c is not null && c.DagobahCompleted;

    /// <summary>RoleMissionKey of side s's view: the real mission while live, else the frozen view's.</summary>
    Mission? Role(Character c, Owner side) => Knowledge.IsLive(c, side) ? c.Mission : _game.Views.TryGetValue((c, side), out var v) ? v.RoleMission : null;

    /// <summary>Character advisor code: majors [0x30, 0x38) by key (others 0), anything outside the range or none 0x15.</summary>
    static int CharacterCode(GameObject? o) => o is null || Family.Of(o.Type) is not (>= 0x30 and < 0x38) ? 0x15 : o.Type switch
    {
        Mothma => 0x17, Refs.Leia => 0x18, Refs.Luke => 0x16, Refs.Han => 0x19, Refs.Palpatine => 0x1A, Refs.Vader => 0x1B, _ => 0,
    };

    /// <summary>Force rank title (placeholder kind 7): TEXTSTRA 34644 / 34633 / 34640 / 34641 / 34642 / 34643 at &lt; 10 / 20 / 80 / 100 / 120 / more.</summary>
    public static int RankTitle(int force) => force < 10 ? 34644 : force < 20 ? 34633 : force < 80 ? 34640 : force < 100 ? 34641 : force < 120 ? 34642 : 34643;

    // the name function resolves a StringId of module 2 through the whole TEXTSTRA string table (GalaxyMap.DisplayName)
    string Text(int stringId) => _name(new SdCommon(0, 0, 0, 0, 0, (ushort)stringId, 2));

    GameMessage Say(int day, int type, int kind, int category, MessageText title, IEnumerable<MessageText> body, GameObject? subject,
        int picture, int sound, int code, int voice, int overlay = 0)
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
                case 0x212 when e.New == 0 && e.Key is Character { Type: Refs.Han } han: Bounty(e.Day, han); break;
                case 0x362: Discovered(e.Day, e.Key, e.Other); break;
                case 0x1E1: ForceGrowth(e); break;
                case 0x220: Encounter(e.Day, [e]); break;
                // kind 4: the pairing's sets and the resolution's clears + 0x211 form one group each, so one duel gives two messages
                // (Inferred context reading); a post outside a duel context is a group of its own
                case 0x360 or 0x211 when e.Group == 0: Encounter(e.Day, [e]); break;
                case 0x360 or 0x211 when groups.Add(e.Group): Encounter(e.Day, [.. posts.Where(x => x.Group == e.Group && x.Kind is 0x360 or 0x211)]); break;
                case 0x221 when e.New != 0: Dagobah(e.Day); break;
                case 0x210 when e.New != 0: DagobahLeft(e.Day); break;
                case 0x361: Traitor(e.Day, e.Key, e.Other); break;
                case 0x200 when e.New != 0: Rescue(e.Day, e.Key, e.Other); break;
                case 0x231 when e.New != 0: Pickup(e.Day); break;
            }
        if (_ownsStory) { trace.Clear(); _story = 0; }
        else _story = trace.Count;
        // type 0x26 (0x230 set): manager 0xFA's Emperor-at-Coruscant flag (derived in openswr, compared per update), side 2 only
        if (_side == Owner.Empire && !_emperor && _game.EmperorAtCoruscant)
            Say(_game.Day, 0x26, 3, MessageCategory.Mission, T(0x71C8), [T(0x71C9)], Major(Refs.Palpatine), 1068, 1147, 0x1A, 0x130);
    }

    /// <summary>
    /// Type 0x1B (0x212 clear): H = the side's view of Han. Captured gives 0x716C / 0x716D, picture 1053, sound 1142, code
    /// 0x21, voice 0x11F, no subject; else side 1 gives 0x7168 / 0x7169, 1052, 1141, code 0x19, voice 0x120; else side 2 gives the first
    /// system view up H's view parents: 0x716A / 0x716B (system name), 1054, 1143, code 0x2D, no voice; none gives no message. Subject Han.
    /// </summary>
    void Bounty(int day, Character han)
    {
        if (Captured(han)) { Say(day, 0x1B, 3, MessageCategory.Mission, T(0x716C), [T(0x716D)], null, 1053, 1142, 0x21, 0x11F); return; }
        if (_side == Owner.Alliance) { Say(day, 0x1B, 3, MessageCategory.Mission, T(0x7168), [T(0x7169)], han, 1052, 1141, 0x19, 0x120); return; }
        for (var p = Knowledge.ViewParent(_game, han, _side); p is not null; p = Knowledge.ViewParent(_game, p, _side))
            if (p is StarSystem s)
            {
                Say(day, 0x1B, 3, MessageCategory.Mission, T(0x716A), [T(0x716B, Name(s))], han, 1054, 1143, 0x2D, 0);
                return;
            }
    }

    /// <summary>
    /// Type 0x1C (0x362 of the discoverer D, value X): both characters with views, D's definition side = the message
    /// side. X Leia: 0x71D8 / 0x71D9, overlay X's portrait, code 0x18, voice 0x124. Else 0x7178 (X, D) with body 0x7179 (X) when D
    /// passes the teacher test (IsJediTrainer, Force >= G 0xA15) else 0x717A (X); overlay D's portrait; code by D; voices Leia 0x124,
    /// Luke 0x135, Vader 0x136. Picture 1042 / 1043, sound 1148 / 1149, subject X.
    /// </summary>
    void Discovered(int day, GameObject? d, GameObject? x)
    {
        if (!Exists(d) || !Exists(x) || !IsCharacter(d) || !IsCharacter(x) || Finders.DefinitionSide((Character)d!) != _side) return;
        var (dc, xc) = ((Character)d!, (Character)x!);
        if (xc.Type == Refs.Leia)
        {
            Say(day, 0x1C, 3, MessageCategory.Mission, T(0x71D8), [T(0x71D9)], xc, SidePicture, SideSound, 0x18, 0x124, Pic(xc));
            return;
        }
        bool teacher = dc.Definition.IsJediTrainer != 0 && dc.Force >= _game.Params.General(0xA15);
        int voice = dc.Type switch { Refs.Leia => 0x124, Refs.Luke => 0x135, Refs.Vader => 0x136, _ => 0 };
        Say(day, 0x1C, 3, MessageCategory.Mission, T(0x7178, Name(xc), Name(dc)), [T(teacher ? 0x7179 : 0x717A, Name(xc))], xc,
            SidePicture, SideSound, CharacterCode(dc), voice, Pic(dc));
    }

    /// <summary>
    /// Type 0x1D (0x1E1): a character with a view, old != 0 and new != 0, definition side not the enemy's. 0x7180 (name),
    /// body 0x7181 (the rank title of the view's Force, placeholder kind 7); overlay its portrait; picture 1042 / 1043, sound 1148 /
    /// 1149; code by C; voices Leia 0x134, Luke 0x132 (0x133 with DagobahCompleted). Subject C.
    /// </summary>
    void ForceGrowth(StoryEvent e)
    {
        if (e.Key is not Character c || !Exists(c) || !IsCharacter(c) || e.Old == 0 || e.New == 0 || Finders.DefinitionSide(c) == Enemy) return;
        int voice = c.Type switch { Refs.Leia => 0x134, Refs.Luke => Jedi(c) ? 0x133 : 0x132, _ => 0 };
        Say(e.Day, 0x1D, 3, MessageCategory.Mission, T(0x7180, Name(c)), [T(0x7181, Text(RankTitle(c.Force)))], c, SidePicture, SideSound,
            CharacterCode(c), voice, Pic(c));
    }

    /// <summary>
    /// Type 0x24 for one group: 0x360 gives A (definition side 1) / E (side 2), subject = the participant of the
    /// message side; a missing view, a non-character or definition side 0 gives kill (no message); 0x211 gives heritage; 0x220 gives final battle.
    /// Close: final battle gives 0x71E0 + 0x71E1 / 0x71E2 by Luke's view Captured, picture / sound / voice 1064 / 1139 / 0x12F
    /// with DagobahCompleted else 1063 / 1138 / 0x11C, subject Luke; Luke or Leia against Vader or Palpatine gives heritage (F 1) or
    /// confrontation; else detection. Code by the subject (none gives 0x15), voices as the builder sets them.
    /// </summary>
    void Encounter(int day, List<StoryEvent> group)
    {
        bool kill = false, heritage = false, finalBattle = false;
        Character? a = null, en = null;
        GameObject? subject = null;
        foreach (var e in group)
            switch (e.Kind)
            {
                case 0x360 when !Exists(e.Key) || !IsCharacter(e.Key) || Finders.DefinitionSide((Character)e.Key!) == Owner.None:
                    kill = true;
                    break;
                case 0x360 when Finders.DefinitionSide((Character)e.Key!) == Owner.Alliance:
                    a = (Character)e.Key!;
                    if (_side == Owner.Alliance) subject = a;
                    break;
                case 0x360:
                    en = (Character)e.Key!;
                    if (_side == Owner.Empire) subject = en;
                    break;
                case 0x211: heritage = true; break;
                case 0x220: finalBattle = true; break;
            }
        if (kill) return;
        if (finalBattle)
        {
            if (Major(Refs.Luke) is not { } luke || !Exists(luke)) return;
            bool j = Jedi(luke);
            Say(day, 0x24, 3, MessageCategory.Mission, T(0x71E0), [T(Captured(luke) ? 0x71E2 : 0x71E1)], luke, j ? 1064 : 1063, j ? 1139 : 1138,
                CharacterCode(luke), j ? 0x12F : 0x11C);
        }
        else if (a is { Type: Refs.Luke or Refs.Leia } && en is { Type: Refs.Vader or Refs.Palpatine })
        {
            if (heritage) Heritage(day, a, en, subject);
            else Confrontation(day, a, en, subject);
        }
        else Detection(day, a, en, subject);
    }

    /// <summary>
    /// Heritage: only Luke against Vader, both views present. 0x71A0, body 0x71A1 + Luke Captured: 0x71A2 when the
    /// Empire's views of Luke and Vader hold one RoleMissionKey whose side-2 view targets Palpatine's side-2 view parent
    /// (DarthToEmperor), else 0x71A3; not Captured: 0x71A4 when injured, else 0x71A5 when Vader is not Captured; then Vader Captured
    /// 0x71A6, else injured 0x71A7. Picture 1058, sound 1133, voice 0x11E.
    /// </summary>
    void Heritage(int day, Character luke, Character vader, GameObject? subject)
    {
        if (luke.Type != Refs.Luke || vader.Type != Refs.Vader || !Exists(luke) || !Exists(vader)) return;
        var body = new List<MessageText> { T(0x71A1) };
        bool vaderCaptured = Captured(vader);
        if (Captured(luke))
        {
            var pal = Major(Refs.Palpatine);
            var m = Role(luke, Owner.Empire);
            // shortcut: a frozen side-2 mission view keeps no TargetKey, so only a live one can match (the Empire's own mission is live);
            // upgrade with a View TargetKey snapshot if an Alliance mission can ever be the role mission of both Luke and Vader
            bool emperor = m is not null && m == Role(vader, Owner.Empire) && pal is not null && Exists(pal) && Exists(m) && Knowledge.IsLive(m, Owner.Empire)
                && m.TargetKey is { } tk && tk == Knowledge.ViewParent(_game, pal, Owner.Empire);
            body.Add(T(emperor ? 0x71A2 : 0x71A3));
        }
        else if (luke.Injury != 0) body.Add(T(0x71A4));
        else if (!vaderCaptured) body.Add(T(0x71A5));
        if (vaderCaptured) body.Add(T(0x71A6));
        else if (vader.Injury != 0) body.Add(T(0x71A7));
        Say(day, 0x24, 3, MessageCategory.Mission, T(0x71A0), body, subject, 1058, 1133, CharacterCode(subject), 0x11E);
    }

    /// <summary>
    /// Confrontation: 0x71A8 / 0x71A9 (A, E) + A Captured 0x71AA / injured 0x71AB, E Captured 0x71AC / injured 0x71AD,
    /// none of them 0x71AE; picture / sound / voice by A (Leia; Luke without / with DagobahCompleted) and E (Vader / Palpatine).
    /// </summary>
    void Confrontation(int day, Character a, Character e, GameObject? subject)
    {
        if (!Exists(a) || !Exists(e)) return;
        var body = new List<MessageText> { T(0x71A9, Name(a), Name(e)) };
        if (Captured(a)) body.Add(T(0x71AA, Name(a)));
        else if (a.Injury != 0) body.Add(T(0x71AB, Name(a)));
        if (Captured(e)) body.Add(T(0x71AC, Name(e)));
        else if (e.Injury != 0) body.Add(T(0x71AD, Name(e)));
        if (body.Count == 1) body.Add(T(0x71AE));
        bool vader = e.Type == Refs.Vader;
        var (picture, sound, voice) = a.Type == Refs.Leia ? vader ? (1055, 1130, 0x137) : (1056, 1131, 0x138)
            : !Jedi(a) ? vader ? (1059, 1134, 0x118) : (1061, 1136, 0x119)
            : vader ? (1060, 1135, 0x11A) : (1062, 1137, 0x11B);
        Say(day, 0x24, 3, MessageCategory.Mission, T(0x71A8, Name(a), Name(e)), body, subject, picture, sound, CharacterCode(subject), voice);
    }

    /// <summary>
    /// Detection: own = A (side 1) / E (side 2), other = the other participant; either missing gives none. 0x71B0 / 0x71B1
    /// (own, other), picture 1042 / 1043, sound 1148 / 1149, overlay own's portrait; voice own Luke 0xDA (DagobahCompleted) / 0xD0,
    /// own Leia 0xE4.
    /// </summary>
    void Detection(int day, Character? a, Character? e, GameObject? subject)
    {
        var (own, other) = _side == Owner.Alliance ? (a, e) : (e, a);
        if (own is null || other is null || !Exists(own) || !Exists(other)) return;
        int voice = own.Type switch { Refs.Luke => Jedi(own) ? 0xDA : 0xD0, Refs.Leia => 0xE4, _ => 0 };
        Say(day, 0x24, 3, MessageCategory.Mission, T(0x71B0, Name(own), Name(other)), [T(0x71B1, Name(own), Name(other))], subject,
            SidePicture, SideSound, CharacterCode(subject), voice, Pic(own));
    }

    /// <summary>Type 0x25 (0x221 set): side 1 only, the Alliance view of Luke on a Dagobah mission (family 0x71): 0x71B8 / 0x71B9, 1057, 1132, code 0x16, voice 0x11D, kind 8, no subject.</summary>
    void Dagobah(int day)
    {
        if (_side != Owner.Alliance || Major(Refs.Luke) is not { } luke || Role(luke, Owner.Alliance) is not { Kind: 0x71 }) return;
        Say(day, 0x25, 8, MessageCategory.Mission, T(0x71B8), [T(0x71B9)], null, 1057, 1132, 0x16, 0x11D);
    }

    /// <summary>Type 0x25 (0x210 set): side 1 only, Luke's view present: 0x71BA / 0x71BB, picture 1042, sound 1148, overlay Luke's portrait, code 0x16, no voice, kind 8, no subject.</summary>
    void DagobahLeft(int day)
    {
        if (_side != Owner.Alliance || Major(Refs.Luke) is not { } luke || !Exists(luke)) return;
        Say(day, 0x25, 8, MessageCategory.Mission, T(0x71BA), [T(0x71BB)], null, 1042, 1148, 0x16, 0, Pic(luke));
    }

    /// <summary>
    /// Type 0x27 (0x361): discoverer D and traitor T characters with views, D's definition side not the enemy's: 0x71E8
    /// (D, T), 0x71E9 (D, T, the enemy side's name); overlay D's portrait, picture 1042 / 1043, sound 1148 / 1149, code by D; voices
    /// Leia 0x127, Luke 0x125 (0x126 with DagobahCompleted), Palpatine 0x129, Vader 0x128. Subject D.
    /// </summary>
    void Traitor(int day, GameObject? d, GameObject? t)
    {
        if (!IsCharacter(d) || !IsCharacter(t) || !Exists(d) || !Exists(t) || Finders.DefinitionSide((Character)d!) == Enemy) return;
        var dc = (Character)d!;
        int voice = dc.Type switch { Refs.Leia => 0x127, Refs.Luke => Jedi(dc) ? 0x126 : 0x125, Refs.Palpatine => 0x129, Refs.Vader => 0x128, _ => 0 };
        Say(day, 0x27, 3, MessageCategory.Mission, T(0x71E8, Name(dc), Name(t)), [T(0x71E9, Name(dc), Name(t), MessageArg.Of(Enemy))], dc,
            SidePicture, SideSound, CharacterCode(dc), voice, Pic(dc));
    }

    /// <summary>
    /// Type 0x28 (0x200 set of a Palace-mission agent): side 1 only, a character on a mission of family 0x64 that leads
    /// it (the mission's LeaderKey): 0x71EA / 0x71EB (C), overlay its portrait, picture 1042, sound 1148, code by C; voices Leia 0xE5, Luke 0xD1
    /// (0xDB with DagobahCompleted). Kind 8, no category. Subject C.
    /// </summary>
    void Rescue(int day, GameObject? c, GameObject? mission)
    {
        if (_side != Owner.Alliance || c is not Character ch || !IsCharacter(ch) || mission is not Mission { Kind: 0x64 } pm || pm.Leader != ch) return;
        int voice = ch.Type switch { Refs.Leia => 0xE5, Refs.Luke => Jedi(ch) ? 0xDB : 0xD1, _ => 0 };
        Say(day, 0x28, 8, 0, T(0x71EA, Name(ch)), [T(0x71EB, Name(ch))], ch, 1042, 1148, CharacterCode(ch), voice, Pic(ch));
    }

    /// <summary>Type 0x29 (0x231 set): either side; picture / sound / voice by Luke's DagobahCompleted: 1067 / 1146 / 0x139, else 1066 / 1145 / 0x123; 0x7200 / 0x7201, code 0x1B, subject Vader.</summary>
    void Pickup(int day)
    {
        bool j = Jedi(Major(Refs.Luke));
        Say(day, 0x29, 3, MessageCategory.Mission, T(0x7200), [T(0x7201)], Major(Refs.Vader), j ? 1067 : 1066, j ? 1146 : 1145, 0x1B, j ? 0x139 : 0x123);
    }
}
