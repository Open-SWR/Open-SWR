using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

public sealed partial class Messages
{
    /// <summary>
    /// Advice topic n: type MessageType.Tip, kind 8 (listed, go-to disabled), category Advice,
    /// advisor code AdvisorCode.Tip, title / body TEXTSTRA RCDATA base + 3n + 1 / + 2 (base TextId.AdviceAllianceBase side 1, TextId.AdviceEmpireBase side 2), picture 1071 /
    /// 1072, sound 1121 / 1122, no voice.
    /// </summary>
    public static GameMessage Advice(GameState game, Owner side, int n)
    {
        bool one = side == Owner.Alliance;
        int b = (one ? TextId.AdviceAllianceBase : TextId.AdviceEmpireBase) + 3 * n;
        return Post(game, new GameMessage
        {
            Day = game.Day, Type = MessageType.Tip, Category = MessageCategory.Advice, Kind = 8, Title = new(b + 1, []), Body = [new(b + 2, [])],
            Picture = one ? 1071 : 1072, Sound = one ? 1121 : 1122, AdvisorCode = AdvisorCode.Tip, New = true,
        });
    }

    // ---- advisor codes and voices ----

    const uint Mothma = 805306944;

    /// <summary>Character code by major character: Mothma AdvisorCode.MothmaMessage, Leia AdvisorCode.LeiaMessage, Luke AdvisorCode.LukeMessage, Han AdvisorCode.HanMessage, Palpatine AdvisorCode.PalpatineMessage, Vader AdvisorCode.VaderMessage, anyone else AdvisorCode.MinorCharacterMessage.</summary>
    static AdvisorCode Code(GameObject? o) => o?.Type switch
    {
        Mothma => AdvisorCode.MothmaMessage, Refs.Leia => AdvisorCode.LeiaMessage, Refs.Luke => AdvisorCode.LukeMessage, Refs.Han => AdvisorCode.HanMessage, Refs.Palpatine => AdvisorCode.PalpatineMessage, Refs.Vader => AdvisorCode.VaderMessage, _ => AdvisorCode.MinorCharacterMessage,
    };

    /// <summary>Captured: Luke AdvisorCode.LukeCaptured, Mothma AdvisorCode.MothmaCaptured, Leia AdvisorCode.LeiaCaptured, Han AdvisorCode.HanCaptured, Palpatine AdvisorCode.PalpatineCaptured, Vader AdvisorCode.VaderCaptured; minor characters and special forces AdvisorCode.MinorCharacterMessage.</summary>
    static AdvisorCode CapturedCode(GameObject c) => c.Type switch
    {
        Refs.Luke => AdvisorCode.LukeCaptured, Mothma => AdvisorCode.MothmaCaptured, Refs.Leia => AdvisorCode.LeiaCaptured, Refs.Han => AdvisorCode.HanCaptured, Refs.Palpatine => AdvisorCode.PalpatineCaptured, Refs.Vader => AdvisorCode.VaderCaptured, _ => AdvisorCode.MinorCharacterMessage,
    };

    /// <summary>Escaped: Mothma AdvisorCode.MothmaEscaped, Luke AdvisorCode.LukeEscaped, Palpatine AdvisorCode.PalpatineEscaped, Vader AdvisorCode.VaderEscaped, Leia AdvisorCode.LeiaMessage, Han AdvisorCode.HanMessage, others AdvisorCode.CharacterEscaped.</summary>
    static AdvisorCode EscapedCode(GameObject c) => c.Type switch
    {
        Mothma => AdvisorCode.MothmaEscaped, Refs.Luke => AdvisorCode.LukeEscaped, Refs.Palpatine => AdvisorCode.PalpatineEscaped, Refs.Vader => AdvisorCode.VaderEscaped, Refs.Leia => AdvisorCode.LeiaMessage, Refs.Han => AdvisorCode.HanMessage, _ => AdvisorCode.CharacterEscaped,
    };

    /// <summary>The list column of a major character (Luke split by a story flag), -1 for anyone else.</summary>
    static int Column(GameObject? o) => o switch
    {
        { Type: Mothma } => 0, { Type: Refs.Leia } => 1, Character { Type: Refs.Luke } c => c.DagobahCompleted ? 3 : 2,
        { Type: Refs.Han } => 4, { Type: Refs.Palpatine } => 5, { Type: Refs.Vader } => 6, _ => -1,
    };

    /// <summary>Sets the advisor code and, for a major character with a voice list, the voice pick.</summary>
    GameMessage Tag(GameMessage m, AdvisorCode code, GameObject? who = null, int[]? voices = null)
    {
        m.AdvisorCode = code;
        if (voices is not null && _voice is not null && Column(who) is >= 0 and var i) m.Voice = _voice(voices[i]);
        return m;
    }

    /// <summary>
    /// Order acknowledgement list of an accepted order, 0 none: CommandType.Move / CommandType.CreateMission with exactly one major character
    /// [48, 56) selected, or CommandType.MissionOrder of a mission led by one (leader Inferred). Luke's Dagobah case: <see cref="DagobahFirst"/>.
    /// </summary>
    public static int Acknowledgement(Command c)
    {
        if (c.Targets.Count != 1) return 0;
        var o = c.Targets[0].Target;
        if (c.Type is CommandType.Move or CommandType.CreateMission && Family.Of(o.Type) is >= Families.MajorCharacter and < Families.MinorCharacter)
            return o.Type switch { Mothma => 241, Refs.Leia => 221, Refs.Luke => 201, Refs.Han => 231, Refs.Palpatine => 261, Refs.Vader => 251, _ => 0 };
        if (c.Type == CommandType.MissionOrder && o is Mission { Leader: { } l })
            return l.Type switch { Mothma => 245, Refs.Leia => 225, Refs.Luke => 205, Refs.Han => 235, Refs.Palpatine => 265, Refs.Vader => 255, _ => 0 };
        return 0;
    }

    /// <summary>Luke without DagobahCompleted sent to a system [144, 152) of family 146 index 249 (Dagobah) tries the Dagobah list first, then the Luke acknowledgement list.</summary>
    public static bool DagobahFirst(Command c) => c.Type is CommandType.Move or CommandType.CreateMission && c.Targets.Count == 1 && c.Targets[0].Target is Character { Type: Refs.Luke } luke
        && !luke.DagobahCompleted && c.ArgRef is { } at && Family.Of(at.Type) is >= Families.CoreSystem and < Families.Abode && SdRef.IndexOf(at.Type) == 249;
}
