using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Message categories, one per message (never combined): None (shown on the All tab only), the tab categories, and Event
/// (full-screen events). The values are the original's.
/// </summary>
public static class MessageCategory
{
    public const int None = 0, Support = 1, Resource = 4, Manufacturing = 8, Mission = 0x10, Chat = 0x20, Defense = 0x40, Fleet = 0x80,
        Conflict = 0x100, Advice = 0x200, Event = 0x400;

    /// <summary>Message-window tab id (0x79 All ... 0x82 Advice) to its category (None = All).</summary>
    public static int OfTab(int tab) => tab switch
    {
        0x7A => Support, 0x7B => Fleet, 0x7C => Mission, 0x7D => Resource, 0x7E => Manufacturing, 0x7F => Defense, 0x80 => Conflict,
        0x81 => Chat, 0x82 => Advice, _ => None,
    };

    /// <summary>The tab of a category (0x79 for none or a category without a tab).</summary>
    public static int TabOf(int category) => category switch
    {
        Support => 0x7A, Fleet => 0x7B, Mission => 0x7C, Resource => 0x7D, Manufacturing => 0x7E, Defense => 0x7F, Conflict => 0x80,
        Chat => 0x81, Advice => 0x82, _ => 0x79,
    };
}

/// <summary>A template argument: a name (text) or a side (formatted by the placeholder's kind in the UI).</summary>
public readonly record struct MessageArg(string? Text = null, Owner Side = Owner.None)
{
    public static implicit operator MessageArg(string text) => new(text);
    public static MessageArg Of(Owner side) => new(null, side);
}

/// <summary>A TEXTSTRA RCDATA template id with its arguments (slot n = argument n).</summary>
public sealed record MessageText(int Template, MessageArg[] Args);

/// <summary>
/// Message object: id (increasing), day of creation, category, kind (3 normal, 4 encyclopedia, 5
/// continue-question, 7 strike, 8 no target), message type, title / body templates (the body = parts in order: text,
/// then the per-unit lines), pictures, sound / voice, subject (go-to key, a Ref), encyclopedia key, mission (kind 5), new and
/// read. <see cref="Text"/>: free text for openswr adaptations and older saves (no template).
/// </summary>
public sealed class GameMessage
{
    public int Id { get; set; }
    public int Day { get; set; }
    public int Category { get; set; }
    public int Kind { get; set; } = 3;
    public int Type { get; set; }
    public MessageText? Title { get; set; }
    public List<MessageText>? Body { get; set; }
    public string? Text { get; set; }
    public int Picture { get; set; }
    public int Overlay { get; set; }
    public int Sound { get; set; }
    /// <summary>Voice: a WAVE of the player side's sprite DLL (module 9), picked at creation; 0 none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Voice { get; set; }
    /// <summary>Advisor code (0 none): fed to the advisor when the message is added (kinds 3, 4, 5, 8, alert on).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int AdvisorCode { get; set; }
    public uint Subject { get; set; }
    public uint Encyclopedia { get; set; }
    public uint Mission { get; set; }
    /// <summary>Strike assault flag (type 0xF): 1 = assault, 0 = bombardment. Saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Assault { get; set; }
    /// <summary>Strike defender side (the system view's owner at the start; Neutral 3; None without the start pulse). Saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public Owner Defender { get; set; }
    /// <summary>Strike attacker side (the fleet view's owner; None names side 2). Not saved.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Owner Attacker { get; set; }
    /// <summary>Strike key lists L70, L78, L80, L88, L90, L98; not saved: null after a load.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<GameObject>[]? Strike { get; set; }
    /// <summary>New: lights the category's button until the tab exit.</summary>
    public bool New { get; set; }
    /// <summary>Displayed (row font 10). Displaying only marks read; New (lit button) is cleared by the tab exit.</summary>
    public bool Read { get; set; }

    /// <summary>Listed in the message window (kinds 3, 4, 5, 7, 8) on the tab of category <paramref name="tab"/> (None = All: everything but Advice).</summary>
    public bool Listed(int tab) => Kind is 3 or 4 or 5 or 7 or 8 && (tab == MessageCategory.None ? Category != MessageCategory.Advice : Category == tab);
}

/// <summary>
/// Player messages (UI only, not a mechanic): the store (<see cref="GameState.Log"/>, saved; 300-day expiry,
/// no count cap) and the builder for one side. OpenSWR reads the post-tick state instead of the notification bundles:
/// the observable state events (<see cref="GameState.FactTrace"/>: completions 0x308 to type 0xE, arrivals 0x306 to 0xD / 0x14,
/// losses 0x300 by DestroyedReason to 0xC / 0x10 / 0x13 / 0x22 / 0x2B, mission TaskStatus / CompletionStatus 0x60 / 0x64 to 0x16
/// / 0x17) and differences since the previous update (system views: owner 4, Uprising 3, Blockade 7; research orders 5 and
/// exhaustion 0x2C, recruitment done 0x2D; characters: captured / escaped 0x19, injured / healed / killed 0x1A; manager queues
/// running empty 0x21; kind-6 events 0x15 / 0x1E / 0x1F / 0x23, subject = the scene id). Only what the side owns or sees. Battles build no row (type 1 = the battle windows, kind 1, not
/// listed. Body argument slots of handlers not read line by line follow the template text
/// (Inferred). Reads state only; no draw.
/// </summary>
public sealed partial class Messages
{
    public const int Expiry = 300;

    /// <summary>Kind-6 scene id to MDATA movie (from the file table).</summary>
    public static readonly IReadOnlyDictionary<uint, string> EventMovies = new Dictionary<uint, string>
    {
        [0x6F] = "MDATA.101", [0x74] = "MDATA.103", [0x73] = "MDATA.102", [0x72] = "MDATA.104",
    };
    static readonly int[] ExhaustedBody = [0x7219, 0x721B, 0x721A]; // research line k: ships, facilities, troops

    readonly GameState _game;
    readonly Owner _side;
    readonly Func<SdCommon, string> _name;
    readonly Func<int, int>? _voice;
    readonly bool _ownsTrace, _ownsStrikes, _ownsInformants, _ownsResources, _ownsStory;
    int _fact, _strike, _informant, _resource, _story;
    bool _emperor; // Emperor at Coruscant (type 0x26)
    Dictionary<StarSystem, Seen> _systems = [];
    int[] _research = [];
    bool[] _exhausted = [];
    bool _recruitment;
    VictoryStatus _victory;
    Dictionary<Character, (Owner Owner, bool Captured, int Injury)> _characters = [];
    Dictionary<ManufacturingManager, (int Count, Owner SystemOwner, int Real)> _queues = [];
    Dictionary<Mission, int> _missionViews = [];
    Dictionary<StarSystem, int> _surplus = []; // garrison surplus of the side's live system views (type 0xB)
    Dictionary<StarSystem, int?> _smuggling = []; // SmugglingPercent of the side's view (type 9; null = never seen live)
    Dictionary<GameObject, bool> _damaged = []; // Damaged of the side's capital-ship / Death Star / squadron views (type 6)

    /// <summary>
    /// Attaches to the game (creates <see cref="GameState.FactTrace"/> if there is none) and takes the first snapshot.
    /// <paramref name="voice"/>: voice variant list id (sprite-DLL RCDATA [n, wave...]) to the picked WAVE (UI generator draw); null (tests, replays) = no pick, no draw, <see cref="GameMessage.Voice"/> stays 0.
    /// </summary>
    public Messages(GameState game, Owner side, Func<SdCommon, string> name, Func<int, int>? voice = null)
    {
        (_game, _side, _name, _voice) = (game, side, name, voice);
        _ownsTrace = game.FactTrace is null;
        game.FactTrace ??= [];
        _fact = game.FactTrace.Count;
        _ownsStrikes = game.StrikeTrace is null;
        game.StrikeTrace ??= [];
        _strike = game.StrikeTrace.Count;
        _ownsInformants = game.InformantTrace is null;
        game.InformantTrace ??= [];
        _informant = game.InformantTrace.Count;
        _ownsResources = game.ResourceTrace is null;
        game.ResourceTrace ??= [];
        _resource = game.ResourceTrace.Count;
        _ownsStory = game.StoryTrace is null;
        game.StoryTrace ??= [];
        _story = game.StoryTrace.Count;
        Snapshot();
    }

    Owner Enemy => _side == Owner.Alliance ? Owner.Empire : Owner.Alliance;

    void Snapshot()
    {
        _systems = _game.Systems.ToDictionary(s => s, s => Knowledge.See(_game, s, _side));
        var side = _game.Sides[_side];
        _research = [.. side.ResearchOrder];
        _exhausted = [.. side.ResearchExhausted];
        _recruitment = side.RecruitmentDone;
        _victory = _game.VictoryState;
        _characters = _game.Characters.Where(c => c.Placed).ToDictionary(c => c, c => (c.Owner, c.Captured, c.Injury));
        _queues = _game.Systems.SelectMany(s => s.Managers).OfType<ManufacturingManager>().ToDictionary(m => m, m => (Knowledge.QueueCount(_game, m, _side), m.System.Owner, m.Orders.Count));
        _missionViews = _game.Views.Where(e => e.Key.Item2 == _side && e.Key.Item1 is Mission).ToDictionary(e => (Mission)e.Key.Item1, e => e.Value.CompletionStatus);
        _surplus = _game.Systems.Where(s => Knowledge.IsLive(s, _side)).ToDictionary(s => s, s => s.GarrisonSurplus);
        _smuggling = _game.Systems.ToDictionary(s => s, s => Knowledge.IsLive(s, _side) ? s.SmugglingPercent : _smuggling.GetValueOrDefault(s));
        _damaged = _game.Created.Where(Repairable).ToDictionary(o => o, o => ViewDamaged(o));
        _emperor = _game.EmperorAtCoruscant;
    }

    /// <summary>Damaged of the side's view (as <see cref="Knowledge.Damage"/>, without its speed).</summary>
    bool ViewDamaged(GameObject o) => Knowledge.IsLive(o, _side) ? o.Damaged : _game.Views.TryGetValue((o, _side), out var v) && v.Damaged;

    static bool Repairable(GameObject o) => !o.Destroyed && Family.Of(o.Type) is >= 0x14 and < 0x20;

    // ---- store ----

    /// <summary>Expiry: every message whose day of creation + 300 is before today is deleted.</summary>
    public static void Expire(GameState game) => game.Log.RemoveAll(m => m.Day + Expiry < game.Day);

    /// <summary>Lit buttons: the categories of the messages that are still new.</summary>
    public static HashSet<int> LitCategories(GameState game) => [.. game.Log.Where(m => m.New).Select(m => m.Category)];

    /// <summary>Tab exit: every message of the tab's category stops being new (All tab, category None: every message).</summary>
    public static void ClearNew(GameState game, int tab)
    {
        foreach (var m in game.Log.Where(m => tab == MessageCategory.None || m.Category == tab)) m.New = false;
    }

    /// <summary>The message window's list for a tab's category: listed messages, newest first (descending id).</summary>
    public static List<GameMessage> List(GameState game, int tab) => [.. game.Log.Where(m => m.Listed(tab)).OrderByDescending(m => m.Id)];

    /// <summary>Loading: entries from older saves (no id; category = a tab id 0x7A-0x82) get ids and categories; they count as read.</summary>
    public static void Migrate(GameState game)
    {
        int next = game.Log.Count == 0 ? 1 : game.Log.Max(m => m.Id) + 1;
        foreach (var m in game.Log.Where(m => m.Id == 0))
        {
            m.Id = next++;
            if (m.Category is >= 0x79 and <= 0x82) m.Category = MessageCategory.OfTab(m.Category);
            if (m.Kind == 0) m.Kind = 3;
        }
        foreach (var m in game.Log.Where(m => m.Category == MessageCategory.Advice && m.Type == 0)) (m.Type, m.Kind) = (0x2A, 8); // advice saved by earlier versions
        foreach (var m in game.Log.Where(m => m.Type == 0xF)) m.Kind = 3; // the attacker side is not saved, so a loaded strike evaluates to kind 3
    }

    /// <summary>Adds a message built outside the update (advisor tips) with the next id.</summary>
    public static GameMessage Post(GameState game, GameMessage m)
    {
        m.Id = game.Log.Count == 0 ? 1 : game.Log.Max(x => x.Id) + 1;
        game.Log.Add(m);
        return m;
    }

    /// <summary>
    /// Advice topic n: type 0x2A, kind 8 (listed, go-to disabled), category Advice,
    /// advisor code 0x2A, title / body TEXTSTRA RCDATA base + 3n + 1 / + 2 (base 0x6000 side 1, 0x6800 side 2), picture 1071 /
    /// 1072, sound 1121 / 1122, no voice.
    /// </summary>
    public static GameMessage Advice(GameState game, Owner side, int n)
    {
        bool one = side == Owner.Alliance;
        int b = (one ? 0x6000 : 0x6800) + 3 * n;
        return Post(game, new GameMessage
        {
            Day = game.Day, Type = 0x2A, Category = MessageCategory.Advice, Kind = 8, Title = new(b + 1, []), Body = [new(b + 2, [])],
            Picture = one ? 1071 : 1072, Sound = one ? 1121 : 1122, AdvisorCode = 0x2A, New = true,
        });
    }

    /// <summary>The object of a Ref (subject / mission keys), or null.</summary>
    public static GameObject? Find(GameState game, uint key) =>
        key == 0 ? null : game.Systems.Cast<GameObject>().Concat(game.Characters).Concat(game.Created).Concat(game.Missions).FirstOrDefault(o => o.Ref == key);

    // ---- builder ----

    readonly List<GameMessage> _added = [];

    GameMessage Add(int day, int type, int category, int kind, MessageText title, IEnumerable<MessageText> body, GameObject? subject = null,
        int picture = 0, int sound = 0)
    {
        var m = new GameMessage
        {
            Day = day, Type = type, Category = category, Kind = kind, Title = title, Body = [.. body], Subject = subject?.Ref ?? 0,
            Picture = picture, Sound = sound, New = true,
        };
        _added.Add(m);
        return m;
    }

    static MessageText T(int template, params MessageArg[] args) => new(template, args);

    /// <summary>
    /// Object picture id = the definition's string number + 0x1000, the object's encyclopedia id
    /// (the UI loads STRATEGY first, else the EData file named by ENCYBMAP); 0 without an object or definition, and for a mod record
    /// (no install picture).
    /// </summary>
    public static int Picture(GameState game, GameObject? o) =>
        o is null || game.Definitions.Resolve(o.Type) is not { } r || r.Common.StringModule == GameDefinitions.ModStringModule ? 0 : r.Common.StringNumber + 0x1000;

    int Pic(GameObject? o) => Picture(_game, o);

    /// <summary>Overlay is set to the object's picture.</summary>
    static GameMessage Over(GameMessage m, int overlay) { m.Overlay = overlay; return m; }

    // ---- advisor codes and voices ----

    const uint Mothma = 0x30000240;

    /// <summary>Character code by major character: Mothma 0x17, Leia 0x18, Luke 0x16, Han 0x19, Palpatine 0x1A, Vader 0x1B, anyone else 0x15.</summary>
    static int Code(GameObject? o) => o?.Type switch
    {
        Mothma => 0x17, Refs.Leia => 0x18, Refs.Luke => 0x16, Refs.Han => 0x19, Refs.Palpatine => 0x1A, Refs.Vader => 0x1B, _ => 0x15,
    };

    /// <summary>Captured: Luke 0x1E, Mothma 0x1F, Leia 0x20, Han 0x21, Palpatine 0x22, Vader 0x23; minor characters and special forces 0x15.</summary>
    static int CapturedCode(GameObject c) => c.Type switch
    {
        Refs.Luke => 0x1E, Mothma => 0x1F, Refs.Leia => 0x20, Refs.Han => 0x21, Refs.Palpatine => 0x22, Refs.Vader => 0x23, _ => 0x15,
    };

    /// <summary>Escaped: Mothma 0x26, Luke 0x25, Palpatine 0x27, Vader 0x28, Leia 0x18, Han 0x19, others 0x24.</summary>
    static int EscapedCode(GameObject c) => c.Type switch
    {
        Mothma => 0x26, Refs.Luke => 0x25, Refs.Palpatine => 0x27, Refs.Vader => 0x28, Refs.Leia => 0x18, Refs.Han => 0x19, _ => 0x24,
    };

    // voice lists per handler, by Mothma, Leia, Luke, Luke J (DagobahCompleted), Han, Palpatine, Vader
    static readonly int[] ArriveVoices = [0xF0, 0xDC, 0xC8, 0xD2, 0xE6, 0x104, 0xFA], EscapedVoices = [0xF6, 0xE2, 0xCE, 0xD8, 0xEC, 0x10A, 0x100],
        FailedVoices = [0xF3, 0xDF, 0xCB, 0xD5, 0xE9, 0x107, 0xFD], SuccessVoices = [0xF2, 0xDE, 0xCA, 0xD4, 0xE8, 0x106, 0xFC],
        HealedVoices = [0xF7, 0xE3, 0xCF, 0xD9, 0xED, 0x10B, 0x101];

    /// <summary>The list column of a major character (Luke split by a story flag), -1 for anyone else.</summary>
    static int Column(GameObject? o) => o switch
    {
        { Type: Mothma } => 0, { Type: Refs.Leia } => 1, Character { Type: Refs.Luke } c => c.DagobahCompleted ? 3 : 2,
        { Type: Refs.Han } => 4, { Type: Refs.Palpatine } => 5, { Type: Refs.Vader } => 6, _ => -1,
    };

    /// <summary>Sets the advisor code and, for a major character with a voice list, the voice pick.</summary>
    GameMessage Tag(GameMessage m, int code, GameObject? who = null, int[]? voices = null)
    {
        m.AdvisorCode = code;
        if (voices is not null && _voice is not null && Column(who) is >= 0 and var i) m.Voice = _voice(voices[i]);
        return m;
    }

    /// <summary>
    /// Order acknowledgement list of an accepted order, 0 none: 0x201 / 0x240 with exactly one major character
    /// [0x30, 0x38) selected, or 0x250 of a mission led by one (leader Inferred). Luke's Dagobah case: <see cref="DagobahFirst"/>.
    /// </summary>
    public static int Acknowledgement(Command c)
    {
        if (c.Targets.Count != 1) return 0;
        var o = c.Targets[0].Target;
        if (c.Type is 0x201 or 0x240 && Family.Of(o.Type) is >= 0x30 and < 0x38)
            return o.Type switch { Mothma => 0xF1, Refs.Leia => 0xDD, Refs.Luke => 0xC9, Refs.Han => 0xE7, Refs.Palpatine => 0x105, Refs.Vader => 0xFB, _ => 0 };
        if (c.Type == 0x250 && o is Mission { Leader: { } l })
            return l.Type switch { Mothma => 0xF5, Refs.Leia => 0xE1, Refs.Luke => 0xCD, Refs.Han => 0xEB, Refs.Palpatine => 0x109, Refs.Vader => 0xFF, _ => 0 };
        return 0;
    }

    /// <summary>Luke without DagobahCompleted sent to a system [0x90, 0x98) of SD index 0xF9 (Dagobah, 0x920000F9) tries list 0x131 first, then 0xC9.</summary>
    public static bool DagobahFirst(Command c) => c.Type is 0x201 or 0x240 && c.Targets.Count == 1 && c.Targets[0].Target is Character { Type: Refs.Luke } luke
        && !luke.DagobahCompleted && c.ArgRef is { } at && Family.Of(at.Type) is >= 0x90 and < 0x98 && SdRef.IndexOf(at.Type) == 0xF9;

    /// <summary>Adds the messages for everything since the last update (ids increasing in creation order); returns them.</summary>
    public List<GameMessage> Update()
    {
        _added.Clear();
        Expire(_game);
        Facts();
        Differences();
        Strikes();
        Informants();
        Resources();
        StoryAndForce();
        Snapshot();
        int next = _game.Log.Count == 0 ? 1 : _game.Log.Max(m => m.Id) + 1;
        foreach (var m in _added) m.Id = next++;
        _game.Log.AddRange(_added);
        return [.. _added];
    }

    void Facts()
    {
        var facts = _game.FactTrace!;
        var objects = _game.Created.Concat(_game.Characters).Concat(_game.Systems).Concat(_game.Missions).GroupBy(o => o.Ref).ToDictionary(g => g.Key, g => g.First());
        var deployed = new List<(int Day, GameObject Unit)>();
        var arrived = new List<(int Day, GameObject Unit)>();
        var lost = new List<(int Day, GameObject Unit)>();
        for (; _fact < facts.Count; _fact++)
        {
            var f = facts[_fact];
            if (!objects.TryGetValue(f.Object, out var o)) continue;
            switch (f.Kind)
            {
                case 0x308 when f.New == 1 && o.Owner == _side && o is not Character && Family.Of(o.Type) is >= 0x10 and < 0x30:
                    deployed.Add((f.Day, o));
                    break;
                // agents report only from a movement mission [0x40, 0x50) (message type 0x14); a member arriving at a mission's
                // target stays silent. A movement mission already ended at the build has left Mission empty (Inferred: keep those)
                case 0x306 when f.New == 0 && o.Owner == _side && !o.Destroyed && o is not Agent { Mission: { Kind: not (>= 0x40 and < 0x50) } }:
                    arrived.Add((f.Day, o));
                    break;
                case 0x300 when f.New == 0 && o.Destroyed && (o.Owner == _side || o.DestroyedReason == 6 && DeathStar.IsDeathStar(o.Type)) && Family.Of(o.Type) is >= 0x10 and < 0x40:
                    lost.Add((f.Day, o));
                    break;
                case 0x60 when f.New != 0 && o is Mission m && m.Owner == _side && !m.Destroyed && m.Kind is not (>= 0x40 and < 0x50) && !(m.Kind == 0x6A && f.New == 3):
                    MissionReport(f.Day, m, (int)f.New);
                    break;
                case 0x64 when f.New != 0 && o is Mission m && m.Owner == _side && m.Kind is not (>= 0x40 and < 0x50) && m.TaskStatus == 0:
                    MissionEnded(f.Day, m, (int)f.New);
                    break;
            }
        }
        if (_ownsTrace) { facts.Clear(); _fact = 0; }
        Deployed(deployed);
        Arrived(arrived);
        Lost(lost);
    }

    string Name(GameObject? o) => o switch
    {
        null => "?",
        StarSystem s => _name(s.Definition.Common),
        Character c => _name(c.Definition.Common),
        Fleet f => $"{(_game.Definitions.Resolve(Refs.Fleet) is { } r ? _name(r.Common) : "Fleet")} {f.Id:X}",
        Mission m => _name(m.Record.Common),
        _ => _game.Definitions.Resolve(o.Type) is { } r ? _name(r.Common) : $"{o.Type:X8}",
    };

    GameObject? Where(GameObject o) => Missions.Location(o);

    /// <summary>
    /// Type 0xE (0x107 Deployed, category 8; kind 3: one group per side and context): one message per day and
    /// manufacturing manager (BuilderKey), units in completion order: one fighter squadron 0x708A / 0x708B, one Death Star 0x708E /
    /// 0x708F, one capital ship 0x7086 / 0x7087, one other unit 0x708C / 0x708D; several 0x7088 / 0x7089 + one 0x707F line each;
    /// texts, picture and location from the first deployed unit. Inferred approximation: the original groups by the
    /// context of the facility timer that delivered the point = the order command that woke the facility while the refined stock
    /// lasts (one command may name several managers, two commands to one manager give two messages), but the refill run 0x371 of a
    /// Refinery chain when the facility had to wait for refined points (then one message across managers and systems).
    /// shortcut: grouped per manager, not per context; upgrade (carry a context through events and timer slots) when a recording
    /// shows two 0x107 messages for one manager on one day or one message across managers (front-end spec, open item 16).
    /// </summary>
    void Deployed(List<(int Day, GameObject Unit)> units)
    {
        foreach (var g in units.GroupBy(u => (u.Day, u.Unit.BuilderKey as GameObject ?? Where(u.Unit))))
        {
            var list = g.Select(u => u.Unit).ToList();
            var first = list[0];
            var at = Where(first);
            uint f = Family.Of(first.Type);
            int pic = Pic(first); // picture = the first deployed unit of the bundle (the handler covers only the first 0x107)
            if (list.Count > 1)
                Tag(Add(g.Key.Day, 0xE, MessageCategory.Manufacturing, 3, T(0x7088, Name(first), Name(at)),
                    [T(0x7089, "", Name(at)), .. list.Select(u => T(0x707F, Name(u)))], at, pic), 3);
            else if (f is >= 0x1C and < 0x20) // squadron: sound 1111 / 1112 by the message side; other cases none
                Tag(Add(g.Key.Day, 0xE, MessageCategory.Manufacturing, 3, T(0x708A, Name(first), Name(at)), [T(0x708B, Name(first), Name(at))], first, pic, _side == Owner.Alliance ? 1111 : 1112), 3);
            else if (f is >= 0x18 and < 0x1C) Tag(Add(g.Key.Day, 0xE, MessageCategory.Manufacturing, 3, T(0x708E, Name(first), Name(at)), [T(0x708F, Name(first), Name(at))], first, pic), 3);
            else if (f is >= 0x14 and < 0x18) Tag(Add(g.Key.Day, 0xE, MessageCategory.Manufacturing, 3, T(0x7086, Name(first), Name(at)), [T(0x7087, Name(first), Name(at))], first, pic), 3);
            else Tag(Add(g.Key.Day, 0xE, MessageCategory.Manufacturing, 3, T(0x708C, Name(first), Name(at)), [T(0x708D, Name(first), Name(at))], first, pic), 3);
        }
    }

    /// <summary>
    /// Arrivals (0x306 to 0) of own objects, type 0xD, none when the
    /// location system's BattlePending is set (an arrival into a pending battle): every fleet to 0x7078 / 0x7079 (category 0x80,
    /// picture 1018 / 1019, sound 1114, code 5); capital ships / Death Stars [0x14, 0x1C) travelling alone (delivery, solo moves) to 
    /// 0x7076 / 0x7077 + one 0x707E line each (0x80, 1018 / 1019, 1114, code 5); the Alliance HQ to 0x707C / 0x707D (0x40, 1022, 1117,
    /// code 6; an Empire HQ gives none); agents to type 0x14 (category 0x10): one 0x70C1 / 0x70C5, several 0x70C0 / 0x70C3 + 0x70C2
    /// lines; other units to 0x707A / 0x707B + 0x707E lines (1020 / 1021, 1115 / 1116, code 6; category by the first unit's family).
    /// Grouped per day and location (Inferred stand-in for the original's bundle per event context, whose first arrival picks the
    /// case); BattlePending read at the build (a battle with the player's arriving unit holds the clock and builds the messages
    /// first, MainScreen.EventFirst; the view of the system is live through the own unit there, Inferred).
    /// </summary>
    void Arrived(List<(int Day, GameObject Unit)> units)
    {
        foreach (var g in units.GroupBy(u => (u.Day, Where(u.Unit))))
        {
            var at = g.Key.Item2;
            bool valid = at is StarSystem { BattlePending: false }; // type 0xD: no S, or a pending battle there, gives none
            int e = _side == Owner.Alliance ? 0 : 1;
            foreach (var fleet in g.Select(u => u.Unit).OfType<Fleet>().Where(_ => valid))
                Tag(Add(g.Key.Day, 0xD, MessageCategory.Fleet, 3, T(0x7078, Name(fleet), Name(at)), [T(0x7079, Name(fleet), Name(at))], fleet, 1018 + e, 1114), 5);
            var ships = g.Select(u => u.Unit).Where(u => valid && Family.Of(u.Type) is >= 0x14 and < 0x1C).ToList();
            if (ships.Count > 0)
                Tag(Add(g.Key.Day, 0xD, MessageCategory.Fleet, 3, T(0x7076, Name(ships[0]), Name(at)), [T(0x7077, Name(ships[0]), Name(at)), .. ships.Select(u => T(0x707E, Name(u)))], at, 1018 + e, 1114), 5);
            if (valid && _side == Owner.Alliance) // case 4 for side 2 falls into the default branch: invalid
                foreach (var hq in g.Select(u => u.Unit).Where(u => Family.Of(u.Type) is >= 0x20 and < 0x22))
                    Tag(Add(g.Key.Day, 0xD, MessageCategory.Defense, 3, T(0x707C, Name(hq), Name(at)), [T(0x707D, Name(hq), Name(at))], hq, 1022, 1117), 6);
            // type 0x14: picture 1042 / 1043, sound 1148 / 1149 by the message side; code = the reported agent's (Inferred: the first
            // member) character code whatever the count, 0x15 for anyone but a major character
            var agents = g.Select(u => u.Unit).OfType<Agent>().ToList();
            int picture = _side == Owner.Alliance ? 1042 : 1043, sound = _side == Owner.Alliance ? 1148 : 1149;
            // overlay = the arriving leader (Inferred: the first agent, a character or a special force)
            int overlay = agents.FirstOrDefault() is { } leader ? Pic(leader) : 0;
            if (agents.Count == 1)
                Over(Tag(Add(g.Key.Day, 0x14, MessageCategory.Mission, 3, T(0x70C1, Name(agents[0]), Name(at)), [T(0x70C5, Name(agents[0]), Name(at))], agents[0], picture, sound), Code(agents[0]), agents[0], ArriveVoices), overlay);
            else if (agents.Count > 1)
                Over(Tag(Add(g.Key.Day, 0x14, MessageCategory.Mission, 3, T(0x70C0, "", Name(at)), [T(0x70C3, "", Name(at)), .. agents.Select(a => T(0x70C2, Name(a)))], at, picture, sound), Code(agents[0])), overlay);
            var units2 = g.Select(u => u.Unit).Where(u => valid && u is not (Fleet or Agent) && Family.Of(u.Type) is not (>= 0x14 and < 0x1C or >= 0x20 and < 0x22 or >= 0x30 and < 0x40)).ToList();
            if (units2.Count > 0)
                Tag(Add(g.Key.Day, 0xD, UnitCategory(units2[0]), 3, T(0x707A, Name(units2[0]), Name(at)), [T(0x707B, Name(units2[0]), Name(at)), .. units2.Select(u => T(0x707E, Name(u)))], at,
                    Family.IsFacility(units2[0].Type) ? Pic(units2[0]) : 1020 + e, _side == Owner.Alliance ? 1115 : 1116), 6); // openswr deviation (player request): a facility shows its own picture, the original 1020 / 1021
        }
    }

    /// <summary>
    /// Type 0xD units case category by the first unit's family: defences [0x22, 0x28) 0x40, processing
    /// facilities [0x28, 0x30) 8, troops / fighters 0x40 in a system (container, Inferred: the parent) else 0x80; other families keep
    /// the constructor's value, not read (Inferred: 0x80).
    /// </summary>
    static int UnitCategory(GameObject u) => Family.Of(u.Type) switch
    {
        >= 0x22 and < 0x28 => MessageCategory.Defense,
        >= 0x28 and < 0x30 => MessageCategory.Manufacturing,
        >= 0x10 and < 0x14 or >= 0x1C and < 0x20 => u.Container is StarSystem ? MessageCategory.Defense : MessageCategory.Fleet,
        _ => MessageCategory.Fleet,
    };

    /// <summary>
    /// Losses (0x300, Destroyed) by DestroyedReason, per day and location: 0x15 maintenance autoscrap to type 0xC (category 8,
    /// 0x7080 / 0x7081 + 0x7082 lines, picture 1013, sound 1110); 6 sabotage to 0x22 (0x100, 0x7083 / 0x7084 + 0x7085 lines);
    /// 7 assassination to 0x2B (0x10, kind 8, 0x7208 / 0x7209, picture 1070, sound 1140); 0xD blockade evacuation to 0x13 (0x100,
    /// 0x70A8 / 0x70A9 + 0x70AA lines, picture 1031 / 1030 and sound 1120 / 1119 for message side A / E); 4 facility overflow (destroyed on arrival, Inferred) to 0x10
    /// (category 8, 0x70B0 / 0x70B1). Scrap (0x14), cancelled production (0x16), battle, bombardment and assault losses: none.
    /// </summary>
    void Lost(List<(int Day, GameObject Unit)> units)
    {
        foreach (var g in units.GroupBy(u => (u.Day, Where(u.Unit) ?? u.Unit.Container, u.Unit.DestroyedReason)))
        {
            var list = g.Select(u => u.Unit).ToList();
            var at = g.Key.Item2;
            switch (g.Key.DestroyedReason)
            {
                case 0x15:
                    Tag(Add(g.Key.Day, 0xC, MessageCategory.Manufacturing, 3, T(0x7080), [T(0x7081, "", "", Name(at)), .. list.Select(u => T(0x7082, Name(u)))], at, 1013, 1110), 0x0C);
                    break;
                case 6 when list.Any(u => DeathStar.IsDeathStar(u.Type)):
                    // type 0x23 (0x305 of a Death Star, category 0x10, kind 6, scene 0x72; both sides, Inferred)
                    foreach (var u in list.Where(u => DeathStar.IsDeathStar(u.Type)))
                        Event(g.Key.Day, 0x23, MessageCategory.Mission, 0x72, T(0x71BD), T(0x71BE, Name(at)));
                    break;
                case 6 when list.Any(u => u.Owner == _side):
                    Add(g.Key.Day, 0x22, MessageCategory.Conflict, 3, T(0x7083, "", "", Name(at)), [T(0x7084, "", "", Name(at)), .. list.Select(u => T(0x7085, Name(u)))], at, 1013, 1110);
                    break;
                case 7:
                    foreach (var u in list)
                        Add(g.Key.Day, 0x2B, MessageCategory.Mission, 8, T(0x7208, Name(u)), [T(0x7209, Name(u), "", Name(at))], at, 1070, 1140);
                    break;
                case 0xD:
                    Tag(Add(g.Key.Day, 0x13, MessageCategory.Conflict, 3, T(0x70A8), [T(0x70A9, "", Name(at)), .. list.Select(u => T(0x70AA, Name(u)))], at,
                        _side == Owner.Alliance ? 1031 : 1030, _side == Owner.Alliance ? 1120 : 1119), 0x10);
                    break;
                case 4 when list.All(u => Family.IsFacility(u.Type)):
                    foreach (var u in list)
                        Tag(Add(g.Key.Day, 0x10, MessageCategory.Manufacturing, 3, T(0x70B0), [T(0x70B1, Name(u), Name(at))], at), 3);
                    break;
            }
        }
    }

    /// <summary>
    /// A kind-6 full-screen event: not listed; subject = the scene id. Not New (kind 6 is only queued on
    /// add, so it lights no category button; Inferred).
    /// </summary>
    void Event(int day, int type, int category, uint scene, MessageText title, MessageText body)
    {
        var m = Add(day, type, category, 6, title, [body]);
        (m.Subject, m.New, m.Read) = (scene, false, false);
    }

    bool Mine(Mission m) => Reporter(m) is Character;
    Agent? Reporter(Mission m) => m.Leader ?? m.Team.FirstOrDefault() ?? m.ReportLeader ?? m.ReportMember; // snapshots: an ended mission's members have left

    /// <summary>
    /// Type 0x16 (TaskStatus != 0, category 0x10): the mission report by type and outcome; "My ..." (title 0x70xx "{1} Mission
    /// Report" with the leader) when a character leads, "The ..." otherwise; kind 5 with 0x70DB for diplomacy, research, incite and
    /// subdue while the mission continues (repeating types return to phase 8). Picture / sound: 1042 / 1043 and
    /// 1148 / 1149 by the message side, overwritten for diplomacy 1044 / 1150, espionage 1045 / 1152, research 1017 / 1151, subdue
    /// with TaskStatus 2 1010 / 1108 and 3 1011 / 1012 (A / E) / 1109.
    /// </summary>
    void MissionReport(int day, Mission m, int status)
    {
        bool ok = status == 3, my = Mine(m);
        string who = Name(Reporter(m)), sys = Name(m.TargetLocation as StarSystem ?? m.Objective?.System), target = Name(m.Objective);
        MessageText title;
        var body = new List<MessageText>();
        switch (m.Kind)
        {
            case 0x51:
                title = my ? T(0x70D0, who) : T(0x70D4);
                body.Add(my ? T(ok ? 0x70D1 : 0x70D2, "", sys) : T(ok ? 0x70D5 : 0x70D6, sys));
                if (m.Objective is StarSystem s && s.Populated)
                    body.Add(T(s.Support > 60 ? 0x70D9 : s.Support < 40 ? 0x70D8 : 0x70DA)); // thresholds Inferred
                break;
            case 0x52:
                title = my ? T(0x7108, who) : T(0x710B);
                body.Add(my ? T(ok ? 0x7109 : 0x710A, "", sys) : T(ok ? 0x710C : 0x710D, sys));
                break;
            case 0x53:
                title = my ? T(0x7120, who) : T(0x7124, "", Name(m));
                int r = !ok ? 2 : m.LevelsGained > 0 ? 1 : 0;
                body.Add(my ? T(0x7121 + r, "", sys, Name(m)) : T(0x7125 + r, sys, Name(m)));
                break;
            case 0x54:
                title = T(0x70E8);
                body.Add(T(ok ? 0x70E9 : 0x70EA, sys));
                break;
            case 0x55:
                title = my ? T(0x7118, who) : T(0x711B, who, "", Name(m.Recruited));
                body.Add(T(ok ? 0x7119 : 0x711A, who, sys, Name(m.Recruited)));
                break;
            case 0x56:
                title = my ? T(0x7100, who) : T(0x7103);
                body.Add(my ? T(ok ? 0x7101 : 0x7102, "", sys) : T(ok ? 0x7104 : 0x7105, sys));
                break;
            case 0x57:
                title = my ? T(0x7110, who) : T(0x7113);
                body.Add(my ? T(ok ? 0x7111 : 0x7112, "", sys) : T(ok ? 0x7114 : 0x7115, sys));
                break;
            case 0x58:
                title = my ? T(0x71C0, who) : T(0x71C3);
                body.Add(my ? T(ok ? 0x71C1 : 0x71C2, "", sys) : T(ok ? 0x71C4 : 0x71C5, sys));
                break;
            case 0x61:
                title = my ? T(0x7130, who) : T(0x7133);
                body.Add(my ? T(ok ? 0x7131 : 0x7132, "", sys, target) : T(ok ? 0x7134 : 0x7135, sys, target));
                break;
            case 0x62:
                title = my ? T(0x70F0, who) : T(0x70F3);
                bool killed = m.Objective is { Destroyed: true };
                body.Add(my ? T(killed ? 0x70F6 : ok ? 0x70F1 : 0x70F2, "", sys, target) : T(killed ? 0x70F7 : ok ? 0x70F4 : 0x70F5, sys, target));
                break;
            case 0x63:
                title = my ? T(0x70F8, who) : T(0x70FB);
                bool dead = m.Objective is { Destroyed: true };
                body.Add(my ? T(dead ? 0x70F9 : 0x70FA, "", sys, target) : T(dead ? 0x70FC : ok ? 0x70FE : 0x70FD, sys, target));
                break;
            case 0x69 or 0x6A:
                title = my ? T(0x70E0, who) : T(0x70E1);
                body.Add(my ? T(0x70E3, "", sys, target) : T(0x70E2, sys, target));
                body.Add(T(m.Objective is { Destroyed: true } ? 0x70E4 : 0x70E5));
                break;
            default:
                return;
        }
        bool asks = m.Kind is 0x51 or 0x53 or 0x56 or 0x57 && m.Phase != 11 && m.CompletionStatus == 0;
        if (asks) body.Add(T(0x70DB));
        bool a = _side == Owner.Alliance;
        var (picture, sound) = (m.Kind, status) switch
        {
            (0x51, _) => (1044, 1150),
            (0x52, _) => (1045, 1152),
            (0x53, _) => (1017, 1151),
            (0x57, 2) => (1010, 1108),
            (0x57, 3) => (a ? 1011 : 1012, 1109),
            _ => (a ? 1042 : 1043, a ? 1148 : 1149),
        };
        var reporter = Reporter(m);
        // the reporter through the player's view: a character to overlay; anything else to picture, replacing
        // the type's picture whenever the view exists (a picture that then fails to load shows none); no view to the type's picture
        bool seen = reporter is not null && (Knowledge.IsLive(reporter, _side) || _game.Views.ContainsKey((reporter, _side)));
        if (seen && reporter is not Character) picture = Pic(reporter);
        var msg = Tag(Add(day, 0x16, MessageCategory.Mission, asks ? 5 : 3, title, body, m.TargetLocation as StarSystem ?? m.Objective, picture, sound),
            Code(reporter), reporter, ok ? SuccessVoices : FailedVoices); // anyone but a major character (or none): 0x15
        if (asks) msg.Mission = m.Ref;
        if (seen && reporter is Character) msg.Overlay = Pic(reporter);
    }

    /// <summary>
    /// Type 0x17 branch (b), an own mission: dropped without the leader's view (LeaderKey; the
    /// real Leader, Inferred: an existing leader's view resolves) or when TargetLocationKey T is not a system. By the new
    /// CompletionStatus: 0-2 dropped; 3-5 foiled 0x7148 / 0x7149 (major leader [0x30, 0x38)) or 0x714A / 0x714B, picture 1046 /
    /// 1047 and sound 1152; 6, 7, 9, 10 failed 0x7138 / 0x7139 or 0x713A / 0x713B (research 0x53 dropped); 8 aborted 0x7140 /
    /// 0x7142 with body 0x7144 / 0x7145 (T's view owner 3) or 0x7141 / 0x7143 with the side of T's view owner (1 to side 1, else 2);
    /// 0xC research only 0x7138 / 0x713C or 0x713A / 0x713D; others dropped. Arguments (leader, mission, T[, side]); the body ends
    /// with the location line 0x70E6 (W) / 0x70E7. Picture 1042 / 1043, sound 1148 / 1149 by the message side; advisor code and
    /// voice by a major leader (other majors none), 0x15 for a minor character. A character leader's portrait is the overlay, and a
    /// special-force leader's picture is kept by a recon mission's foil. Both come from the object picture id.
    /// </summary>
    void MissionEnded(int day, Mission m, int status)
    {
        if ((m.Leader ?? m.ReportLeader) is not { } leader || m.TargetLocation is not StarSystem t) return;
        bool major = leader is Character && Family.Of(leader.Type) < 0x38, research = m.Kind == 0x53, a = _side == Owner.Alliance;
        string who = Name(leader), type = Name(m), sys = Name(t);
        int picture = a ? 1042 : 1043, sound = a ? 1148 : 1149;
        if (Family.IsSpecialForce(leader.Type)) picture = Pic(leader); // A special-force leader's picture replaces 1042 / 1043
        MessageText title, body;
        switch (status)
        {
            case 3 or 4 or 5:
                (title, body) = major ? (T(0x7148, who, type), T(0x7149, who, type, sys)) : (T(0x714A, who, type), T(0x714B, who, type, sys));
                sound = 1152;
                if (!(Family.IsSpecialForce(leader.Type) && m.Kind == 0x54)) picture = a ? 1046 : 1047; // r: recon led by a special force keeps the leader's picture
                break;
            case 6 or 7 or 9 or 10 when !research:
                (title, body) = major ? (T(0x7138, who, type), T(0x7139, who, type, sys)) : (T(0x713A, who, type), T(0x713B, who, type, sys));
                break;
            case 8:
                var owner = Knowledge.See(_game, t, _side).Owner;
                title = T(major ? 0x7140 : 0x7142, who, type);
                body = owner == Owner.Neutral ? T(major ? 0x7144 : 0x7145, who, type, sys)
                    : T(major ? 0x7141 : 0x7143, who, type, sys, MessageArg.Of(owner == Owner.Alliance ? Owner.Alliance : Owner.Empire));
                break;
            case 0xC when research:
                (title, body) = major ? (T(0x7138, who, type), T(0x713C, who, type, sys)) : (T(0x713A, who, type), T(0x713D, who, type, sys));
                break;
            default:
                return;
        }
        int code = leader.Type switch
        {
            Mothma => 0x17, Refs.Leia => 0x18, Refs.Luke => 0x16, Refs.Han => 0x19, Refs.Palpatine => 0x1A, Refs.Vader => 0x1B,
            _ => leader is Character ? major ? 0 : 0x15 : 0x14, // special forces: the class default 0x14
        };
        var msg = Tag(Add(day, 0x17, MessageCategory.Mission, 3, title, [body, Where(m)], t, picture, sound), code, leader, FailedVoices);
        if (leader is Character) msg.Overlay = Pic(leader); // the leader's portrait
    }

    /// <summary>Location line: the first member (key order, Inferred) whose view is present, not Destroyed and not Captured, with a system or fleet above its view to 0x70E6 (its name), else <see cref="Mission.ReportPlace"/>, else 0x70E7.</summary>
    MessageText Where(Mission m)
    {
        foreach (var x in m.Members.OrderBy(x => x.Key))
        {
            var v = Knowledge.See(_game, x, _side);
            if (!Knowledge.IsLive(x, _side) && !_game.Views.ContainsKey((x, _side)) || v.Destroyed || v.Captured) continue;
            for (var p = Knowledge.ViewParent(_game, x, _side); p is not null; p = Knowledge.ViewParent(_game, p, _side))
                if (p is StarSystem or Fleet) return T(0x70E6, Name(p));
        }
        return m.ReportPlace is { } at ? T(0x70E6, Name(at)) : T(0x70E7); // members already left: the place snapshot at the notification
    }

    /// <summary>
    /// Type 0x17 branch (a), an enemy mission: the side's frozen view of another side's mission takes a
    /// non-zero CompletionStatus with TaskStatus 0 (only the encounter refresh writes it): 0x713E (mission) /
    /// 0x713F (mission, T's system name; none when T is not a system), picture 1073 / 1074, sound 1148 / 1149, subject T.
    /// </summary>
    void EnemyMissions(int day)
    {
        foreach (var ((o, side), v) in _game.Views)
        {
            if (side != _side || o is not Mission m || v.Owner != Enemy || m.Kind is >= 0x40 and < 0x50) continue;
            if (v.CompletionStatus == 0 || v.TaskStatus != 0 || v.CompletionStatus == _missionViews.GetValueOrDefault(m)) continue;
            bool a = _side == Owner.Alliance;
            var t = m.TargetLocation;
            var msg = Add(day, 0x17, MessageCategory.Mission, 3, T(0x713E, Name(m)), [T(0x713F, Name(m), t is StarSystem ? Name(t) : "")], null,
                a ? 1073 : 1074, a ? 1148 : 1149);
            msg.Subject = t?.Ref ?? 0;
        }
    }

    /// <summary>
    /// Type 0xF strike messages from <see cref="GameState.StrikeTrace"/>: one per strike whose system view is
    /// live for the side at the end (only the end pulse validates the message). the defender is the system owner when the view was
    /// live at the start (else 0: the neutral bodies); the attacker is the owner of an attacking fleet live for the side. Lists (mode-1
    /// walks = Usable content views): L70 the system's contents (assault: Captured characters to L90 when owned by the defender, else
    /// L98), L78 the live fleets' capital ships and their contents (Captured characters to L90), the objects destroyed with a view live
    /// at the destroy or with a once-Created frozen view at the staging pass to L80 when not Completed or a system,
    /// else L88. Skipped when the side's system view is Destroyed (type 0x15 instead). Coruscant: the classifier drops only
    /// the start pulse; the end pulse still builds the message, and the type-4 owner message and the 0x1E event are separate groups
    /// (0x100 / 0x128 are kind 2), so all three are built.
    /// Kind 7 for the attacker (opens the strike window; observed in the original), else 3 with advisor code 0x2E / 0x2F (observed in the original); category
    /// Conflict; no sound.
    /// </summary>
    void Strikes()
    {
        var strikes = _game.StrikeTrace!;
        for (; _strike < strikes.Count; _strike++)
        {
            var r = strikes[_strike];
            var s = r.System;
            if (!r.EndLive[_side]) continue;
            var view = Knowledge.See(_game, s, _side);
            if (!r.Assault && view.Destroyed) continue;
            var defender = r.StartLive[_side] ? r.Defender : Owner.None;
            var attacker = r.Fleets.Any(f => f.Live[_side]) ? r.Attacker : Owner.None;
            List<GameObject>[] l = [[], [], [], [], [], []];
            static bool Prisoner(GameObject o) => o is Character { Captured: true };
            foreach (var o in r.Contents)
                l[r.Assault && Prisoner(o) ? o.Owner == defender ? 4 : 5 : 0].Add(o);
            foreach (var f in r.Fleets.Where(f => f.Live[_side]))
            {
                l[1].AddRange(f.Ships);
                foreach (var o in f.Aboard) l[Prisoner(o) ? 4 : 1].Add(o);
            }
            foreach (var (o, live, completed) in r.Destroyed.Where(d => d.Live[_side]))
                l[!completed || o is StarSystem ? 2 : 3].Add(o);
            var att = MessageArg.Of(attacker == Owner.Alliance ? Owner.Alliance : Owner.Empire); // attacker 0 names side 2
            MessageArg? def = (defender is Owner.Alliance or Owner.Empire) ? MessageArg.Of(defender) : (MessageArg?)null;
            string sys = Name(s);
            MessageText title, body;
            int picture;
            if (r.Assault)
            {
                bool taken = view.Owner == attacker;
                title = T(0x7090, sys);
                body = def is { } d ? T(taken ? 0x7091 : 0x7092, att, d, sys) : T(taken ? 0x7093 : 0x7094, att, sys);
                picture = attacker == Owner.Alliance ? 11160 : 11161;
            }
            else
            {
                title = T(0x7098, sys);
                body = def is { } d ? T(0x7099, att, d, sys) : T(0x709A, att, sys);
                picture = l[2].Count > 0 ? 11163 : l[3].Count > 0 ? 11164 : 11162;
            }
            bool mine = attacker == _side;
            var m = Add(r.Day, 0xF, MessageCategory.Conflict, mine ? 7 : 3, title, [body], s, picture);
            (m.Assault, m.Defender, m.Attacker, m.Strike) = (r.Assault, defender, attacker, l);
            if (!mine) m.AdvisorCode = r.Assault ? 0x2F : 0x2E;
        }
        if (_ownsStrikes) { strikes.Clear(); _strike = 0; }
    }

    void Differences()
    {
        int day = _game.Day;
        EnemyMissions(day);
        foreach (var s in _game.Systems)
        {
            if (!_systems.TryGetValue(s, out var was)) continue;
            var now = Knowledge.See(_game, s, _side);
            // type 0x15: the system view turned Destroyed (Death Star; category 0x400, kind 6, scene 0x6F): 0x70C9 owned, 0x70CA neutral
            if (now.Destroyed && !was.Destroyed)
                Event(day, 0x15, MessageCategory.Event, 0x6F, T(0x70C8, "", Name(s)),
                    was.Owner is Owner.Alliance or Owner.Empire ? T(0x70C9, MessageArg.Of(was.Owner), "", Name(s)) : T(0x70CA, "", Name(s)));
            // type 4: owner change of the system view (category 1; picture 1005; sound 1104 joins either side, 1105 neutral)
            if (now.Owner != was.Owner && !now.Destroyed)
            {
                // advisor code 1 joins us / 2 joins the enemy; declaring neutrality none
                if (now.Owner == _side) Tag(Add(day, 4, MessageCategory.Support, 3, T(0x7038, Name(s)), [T(0x703B, Name(s), MessageArg.Of(_side))], s, 1005, 1104), 1);
                else if (now.Owner == Enemy) Tag(Add(day, 4, MessageCategory.Support, 3, T(0x7039, Name(s)), [T(0x703C, Name(s), MessageArg.Of(Enemy))], s, 1005, 1104), 2);
                else if (was.Owner is Owner.Alliance or Owner.Empire) Add(day, 4, MessageCategory.Support, 3, T(0x703A, Name(s)), [T(0x703D, Name(s), MessageArg.Of(was.Owner))], s, 1005, 1105);
            }
            // type 3: Uprising of the system view, owner side 1 or 2: picture 1010 begins, ends 1011 Alliance- / 1012 Empire-owned;
            // sound 1108 / 1109; code 1 (good news) when (view owner = message side) != the new Uprising value, else 2
            if (now.Uprising != was.Uprising && now.Owner is Owner.Alliance or Owner.Empire && !now.Destroyed)
            {
                int code = (now.Owner == _side) != now.Uprising ? 1 : 2;
                if (now.Uprising) Tag(Add(day, 3, MessageCategory.Support, 3, T(0x7030, Name(s)), [T(0x7033, MessageArg.Of(now.Owner), Name(s))], s, 1010, 1108), code);
                else Tag(Add(day, 3, MessageCategory.Support, 3, T(0x7031, Name(s)), [T(0x7032, "", Name(s))], s, now.Owner == Owner.Alliance ? 1011 : 1012, 1109), code);
            }
            NearUprising(day, s, now);
            Smuggling(day, s, now);
            // type 7: Blockade set in the view: an own fleet blockades (0x70A0 / 0x70A1) or the own system is blockaded (0x70A2 / 0x70A3)
            if (now.Blockade && !was.Blockade)
            {
                if (now.Owner == _side) Tag(Add(day, 7, MessageCategory.Conflict, 3, T(0x70A2, Name(s)), [T(0x70A3, MessageArg.Of(Enemy), Name(s))], s, 1028, 1118), 0x0E);
                else if (s.Contents.OfType<Fleet>().FirstOrDefault(f => f.Owner == _side && f.Existing) is { } fleet)
                    Tag(Add(day, 7, MessageCategory.Conflict, 3, T(0x70A0, Name(s)), [T(0x70A1, "", Name(s), Name(fleet))], fleet, 1027, 1118), 0x0D);
            }
        }
        foreach (var o in _game.Created.Where(Repairable))
            if (_damaged.GetValueOrDefault(o) && !ViewDamaged(o)) Repaired(day, o);
        // victory facts newly set: Coruscant held by the Alliance gives type 0x1E (Support, scene 0x74) only when the side's view of
        // Coruscant shows the Alliance; HQ gone gives type 0x1F (full-screen event, scene 0x73)
        var (victoryNow, victoryBefore) = (_game.VictoryState, _victory);
        if (victoryNow.CoruscantAlliance && !victoryBefore.CoruscantAlliance && _game.Systems.FirstOrDefault(s => s.Type == Refs.Coruscant) is { } cor
            && Knowledge.See(_game, cor, _side).Owner == Owner.Alliance)
            Event(day, 0x1E, MessageCategory.Support, 0x74, T(0x7170), T(0x7171));
        if (victoryNow.HqGone && !victoryBefore.HqGone) Event(day, 0x1F, MessageCategory.Event, 0x73, T(0x7188), T(0x7189));
        var side = _game.Sides[_side];
        // type 5: research order raised to one message per newly buildable item (kind 4, encyclopedia key = the item)
        for (int k = 0; k < 3 && k < _research.Length; k++)
            if (side.ResearchOrder[k] > _research[k])
                foreach (var r in Research.Candidates(_game, _side, Research.FacilityFamily(k)).Where(r => r.Get(SdField.ResearchOrder) > _research[k] && r.Get(SdField.ResearchOrder) <= side.ResearchOrder[k]))
                    Tag(Add(day, 5, MessageCategory.Manufacturing, 4, T(0x7048, _name(r.Common)), [T(0x7049, _name(r.Common))], null, 1017, 1113), 4).Encyclopedia = r.Common.Ref;
        // type 0x2C research exhausted (kind 8), 0x2D recruitment done (kind 8)
        for (int k = 0; k < 3 && k < _exhausted.Length; k++)
            if (side.ResearchExhausted[k] && !_exhausted[k]) Add(day, 0x2C, MessageCategory.Manufacturing, 8, T(0x7218), [T(ExhaustedBody[k])], null, 1017, 1113);
        if (side.RecruitmentDone && !_recruitment) Add(day, 0x2D, MessageCategory.Mission, 8, T(0x7210), [T(0x7211)]);
        // characters: type 0x19 captured / escaped (0x7158 own captured by a side, 0x715A enemy captured by us; 0x715E escaped), 0x1A injured / healed / killed
        foreach (var c in _game.Characters.Where(c => c.Placed))
        {
            if (!_characters.TryGetValue(c, out var was)) continue;
            var at = Where(c);
            if (c.Destroyed && was.Owner == _side)
            {
                if (c.DestroyedReason != 7) Over(Tag(Add(day, 0x1A, MessageCategory.Mission, 8, T(0x7164, Name(c)), [T(0x7164, Name(c))], null, 1050, 1140), Code(c)), Pic(c));
                continue;
            }
            if (c.Captured != was.Captured)
            {
                // picture captured 1048 for an Alliance character, 1049 otherwise; escaped 1029; no overlay
                int captured = Finders.DefinitionSide(c) == Owner.Alliance ? 1048 : 1049;
                if (c.Captured && was.Owner == _side) Tag(Add(day, 0x19, MessageCategory.Mission, 3, T(0x7158, Name(c)), [T(0x7159, Name(c), Name(at), MessageArg.Of(c.Owner))], c, captured, 1100), CapturedCode(c));
                else if (c.Captured && c.Owner == _side) Tag(Add(day, 0x19, MessageCategory.Mission, 3, T(0x715A, Name(c)), [T(0x715B, Name(c), Name(at))], c, captured, 1100), CapturedCode(c));
                else if (!c.Captured && c.State == Prisoners.Escaped && c.Owner == _side)
                    Tag(Add(day, 0x19, MessageCategory.Mission, 3, T(0x715E, Name(c)), [T(0x715F, Name(c), Name(at))], c, 1029, 1129), EscapedCode(c), c, EscapedVoices);
            }
            if (c.Owner == _side && !c.Captured)
            {
                // overlay = the character
                if (c.Injury > 0 && was.Injury == 0) Over(Tag(Add(day, 0x1A, MessageCategory.Mission, 3, T(0x7160, Name(c)), [T(0x7161, Name(c))], c, 1050, 1140), Code(c)), Pic(c));
                else if (c.Injury == 0 && was.Injury > 0) Over(Tag(Add(day, 0x1A, MessageCategory.Mission, 3, T(0x7162, Name(c)), [T(0x7163, Name(c))], c, 1051, 1140), Code(c), c, HealedVoices), Pic(c));
            }
        }
        // type 0x21 ("... Idle on {1}"): the side's manager view count goes to 0 with view owner = the side. A view is live
        // when the manager is owned by the side; a live view counts the real queue, a frozen one keeps the count it had.
        // After an owner change the old owner gets none (its view now shows the new owner).
        // The new owner gets one when its frozen view count or the real queue count was not 0 before the
        // change. This holds on every owner-change path; a change to neutral gives nobody one. shortcut: both counts
        // are the ones before the update (a queue filled and cancelled inside one update, or an owner change and its reversal, is
        // not seen), upgrade if a recording shows such a case. The kind's facility family
        // (construction yard 0x2A 0x71F8 / 0x71F9, training 0x29 0x71FA / 0x71FB, shipyard 0x28 0x71FC / 0x71FD) must be among the
        // system's Usable contents (walk mode 1, key order); the first one gives the picture. Advisor
        // code 3, sound 1101.
        foreach (var (m, was) in _queues)
        {
            if (was.Count == 0 && (was.SystemOwner == m.System.Owner || was.Real == 0) || m.Owner != _side || m.Destroyed || Knowledge.QueueCount(_game, m, _side) != 0) continue;
            var (family, title, body) = m.Kind switch
            {
                ManufacturingKind.Construction => (0x2Au, 0x71F8, 0x71F9), ManufacturingKind.Training => (0x29u, 0x71FA, 0x71FB), _ => (0x28u, 0x71FC, 0x71FD),
            };
            if (m.System.Contents.Where(x => x.Usable && Family.Of(x.Type) == family).OrderBy(x => x.Key).FirstOrDefault() is not { } yard) continue;
            Tag(Add(day, 0x21, MessageCategory.Manufacturing, 3, T(title, Name(m.System)), [T(body, Name(m.System))], m.System, Pic(yard), 1101), 3);
        }
    }

    // ---- economy and unrest: types 6, 9, 0xB, 0x18, 0x20 ----
    // shortcut (types 6, 9, 0xB): diffs between updates stand for the notifications, so a value that changes and changes back
    // inside one update gives no message (the original gives one per notification); upgrade if a recording shows such a case.

    bool HasView(GameObject o) => Knowledge.IsLive(o, _side) || _game.Views.ContainsKey((o, _side));

    /// <summary>
    /// Type 0xB near uprising (0x148): the side's own system (view owner = side), Garrisoned, no Uprising, surplus old != 0
    /// to new = 0: 0x7070 / 0x7071 (system name), picture 1008 / 1009, sound 1106 / 1107 by the owner, category 1, advisor code 2.
    /// Own systems are live views, so the real surplus stands for the view copy (frozen views are not diffed).
    /// </summary>
    void NearUprising(int day, StarSystem s, Seen now)
    {
        if (!_surplus.TryGetValue(s, out int old) || !Knowledge.IsLive(s, _side) || old == 0 || s.GarrisonSurplus != 0) return;
        if (now.Owner != _side || !s.Garrisoned || now.Uprising) return;
        bool a = _side == Owner.Alliance;
        Tag(Add(day, 0xB, MessageCategory.Support, 3, T(0x7070, Name(s)), [T(0x7071, Name(s))], s, a ? 1008 : 1009, a ? 1106 : 1107), 2);
    }

    /// <summary>
    /// Type 9 smuggling (0x145): only a begin (old 0 to nonzero) or an end (nonzero to 0); valid for view owner 1 with
    /// old >= 0 or view owner 2 with old <= 0. Own (view owner = side): 0x7060 / 0x7064 begin, 0x7061 / 0x7065 end; enemy: 0x7062 /
    /// 0x7066, 0x7063 / 0x7067 (system name). Picture 1004, sound 1103 on a begin only, category 1, no advisor code.
    /// shortcut: the side's view value = the real value while live, else the last live value (frozen views keep no copy), so the
    /// first sight of an enemy system that already smuggles gives no "Smuggling Benefits"; upgrade with a View snapshot field.
    /// </summary>
    void Smuggling(int day, StarSystem s, Seen now)
    {
        if (_smuggling.GetValueOrDefault(s) is not int old || !Knowledge.IsLive(s, _side)) return;
        int value = s.SmugglingPercent;
        if (old == value || old != 0 && value != 0) return;
        if (!(now.Owner == Owner.Alliance && old >= 0 || now.Owner == Owner.Empire && old <= 0)) return;
        bool begin = old == 0;
        int title = (now.Owner == _side ? 0x7060 : 0x7062) + (begin ? 0 : 1);
        Add(day, 9, MessageCategory.Support, 3, T(title, Name(s)), [T(title + 4, Name(s))], s, 1004, begin ? 1103 : 0);
    }

    /// <summary>
    /// Type 6 repaired (0x106 Damaged to 0): O = the side's view of a capital ship / Death Star [0x14, 0x1C) or a squadron
    /// [0x1C, 0x20), owner not the enemy. Attached to P: a ship's view parent (the fleet), category 0x80; a squadron's view parent Q,
    /// or Q's parent when Q is a capital ship (category 0x80), else Q itself (category 0x40 Defense). Squadron 0x7040 / 0x7042 (O, P),
    /// sound 1111 / 1112, code 9; capital ship 0x7041 / 0x7043, sound 1118, code 8; picture = O's object picture; subject O.
    /// </summary>
    void Repaired(int day, GameObject o)
    {
        if (!HasView(o) || Knowledge.See(_game, o, _side).Owner == Enemy) return;
        bool squadron = Family.Of(o.Type) >= 0x1C;
        var p = Knowledge.ViewParent(_game, o, _side);
        int category = MessageCategory.Fleet;
        if (squadron && p is not null)
        {
            if (Family.Of(p.Type) is >= 0x14 and < 0x1C) p = Knowledge.ViewParent(_game, p, _side);
            else category = MessageCategory.Defense;
        }
        if (p is null || !HasView(p)) return;
        if (squadron) Tag(Add(day, 6, category, 3, T(0x7040, Name(o), Name(p)), [T(0x7042, Name(o), Name(p))], o, Pic(o), _side == Owner.Alliance ? 1111 : 1112), 9);
        else Tag(Add(day, 6, category, 3, T(0x7041, Name(o), Name(p)), [T(0x7043, Name(o), Name(p))], o, Pic(o), 1118), 8);
    }

    /// <summary>
    /// Type 0x18 informants (0x153 set): one per incident for a side whose view exists and does not show the system as
    /// its own (owner 0 / 3: either side): 0x7150 / 0x7151 (system name), picture 1000, sound 1100, category 1, advisor code 0x1C.
    /// </summary>
    void Informants()
    {
        var trace = _game.InformantTrace!;
        for (; _informant < trace.Count; _informant++)
        {
            var r = trace[_informant];
            if ((_side == Owner.Alliance ? r.AllianceView : r.EmpireView) is not { } owner || owner == _side) continue;
            Tag(Add(r.Day, 0x18, MessageCategory.Support, 3, T(0x7150, Name(r.System)), [T(0x7151, Name(r.System))], r.System, 1000, 1100), 0x1C);
        }
        if (_ownsInformants) { trace.Clear(); _informant = 0; }
    }

    /// <summary>
    /// Type 0x20 natural disaster / resources (decided at the group's close marker): only the system owner's side with a
    /// live view. F: disaster 8; energy changed 4, raw material 2, either increased 1; a facility [0x20, 0x30) destroyed 0x10 (one
    /// 0x719B line each, destroy order). Resources: picture 1002, sound 1101, 0x7194 "New Resources" (F 1) with body 0x719A (F 2) /
    /// 0x7199, else 0x7196 with 0x7198 / 0x7197. Disaster: picture 1003, sound 1102, 0x7190 with 0x7193 (facilities and a resource
    /// change) / 0x7192 (facilities) / 0x7191 (resources only) + the lines; neither gives no message. Category 4, advisor code 0x1C.
    /// Inferred: the bodies take the system name in slot 1 like the titles; F from the values before and after the event (every
    /// reachable setter sequence moves each value one way).
    /// </summary>
    void Resources()
    {
        var trace = _game.ResourceTrace!;
        for (; _resource < trace.Count; _resource++)
        {
            var r = trace[_resource];
            if (!r.Live[_side] || r.Owner != _side) continue;
            // the message facts as named bools: more, raw, energy, disaster, facilities lost
            bool energy = r.EnergyNew != r.EnergyOld, raw = r.RawNew != r.RawOld;
            bool more = energy && r.EnergyNew > r.EnergyOld || raw && r.RawNew > r.RawOld;
            var lost = r.Destroyed.Where(d => d.Live[_side]).Select(d => d.Unit).ToList();
            string sys = Name(r.System);
            MessageText title;
            List<MessageText> body;
            int picture, sound;
            if (!r.Disaster)
                (title, body, picture, sound) = (T(more ? 0x7194 : 0x7196, sys), [T(more ? raw ? 0x719A : 0x7199 : raw ? 0x7198 : 0x7197, sys)], 1002, 1101);
            else
            {
                int text = lost.Count > 0 ? energy || raw ? 0x7193 : 0x7192 : energy || raw ? 0x7191 : 0;
                if (text == 0) continue;
                (title, body, picture, sound) = (T(0x7190, sys), [T(text, sys), .. lost.Select(u => T(0x719B, Name(u)))], 1003, 1102);
            }
            Tag(Add(r.Day, 0x20, MessageCategory.Resource, 3, title, body, r.System, picture, sound), 0x1C);
        }
        if (_ownsResources) { trace.Clear(); _resource = 0; }
    }
}
