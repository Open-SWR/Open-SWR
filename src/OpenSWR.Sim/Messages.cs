using OpenSWR.Data.Dat;

namespace OpenSWR.Sim;

/// <summary>
/// Player messages (UI only, not a mechanic): the store (<see cref="GameState.Log"/>, saved; 300-day expiry,
/// no count cap) and the builder for one side. OpenSWR reads the post-tick state instead of the notification bundles:
/// the observable state events (<see cref="GameState.FactTrace"/>: completions FactKind.Completed to type MessageType.Deployed, arrivals FactKind.HyperdriveChanged to MessageType.Arrived / MessageType.PersonnelArrived,
/// losses FactKind.ExistingChanged by DestroyedReason to MessageType.MaintenanceScrap / MessageType.FacilityLost / MessageType.EvacuationLosses / MessageType.Sabotage / MessageType.Assassination, mission FactKind.TaskStatus / FactKind.CompletionStatus to MessageType.MissionReport
/// / MessageType.MissionEnded) and differences since the previous update (system views: owner MessageType.SystemJoins, Uprising MessageType.Uprising, Blockade MessageType.Blockade; research orders MessageType.ResearchComplete and
/// exhaustion MessageType.ResearchExhausted, recruitment done MessageType.RecruitmentDone; characters: captured / escaped MessageType.CapturedOrEscaped, injured / healed / killed MessageType.InjuredOrHealed; manager queues
/// running empty MessageType.QueueEmpty; kind-6 events MessageType.PlanetDestroyed / MessageType.CoruscantCaptured / MessageType.HeadquartersDestroyed / MessageType.DeathStarSabotaged, subject = the scene id). Only what the side owns or sees. Battles build no row (MessageType.Battle = the battle windows, kind 1, not
/// listed. Body argument slots of handlers not read line by line follow the template text
/// (Inferred). Reads state only; no draw.
/// </summary>
public sealed partial class Messages
{
    public const int Expiry = 300;

    /// <summary>Kind-6 scene id to MDATA movie (from the file table).</summary>
    public static readonly IReadOnlyDictionary<uint, string> EventMovies = new Dictionary<uint, string>
    {
        [EventScene.PlanetDestroyed] = "MDATA.101", [EventScene.CoruscantCaptured] = "MDATA.103", [EventScene.HeadquartersDestroyed] = "MDATA.102",
        [EventScene.DeathStarSabotaged] = "MDATA.104",
    };
    static readonly int[] ExhaustedBody = [TextId.NoFurtherShipAdvances, TextId.NoFurtherFacilityAdvances, TextId.NoFurtherTroopAdvances]; // research line k: ships, facilities, troops

    readonly GameState _game;
    readonly Owner _side;
    readonly Func<SdCommon, string> _name;
    readonly Func<int, int>? _voice;
    readonly bool _ownsTrace, _ownsStrikes, _ownsInformants, _ownsResources, _ownsStory;
    int _fact, _strike, _informant, _resource, _story;
    bool _emperor; // Emperor at Coruscant (type MessageType.EmperorArrives)
    Dictionary<StarSystem, Seen> _systems = [];
    int[] _research = [];
    bool[] _exhausted = [];
    bool _recruitment;
    VictoryStatus _victory;
    Dictionary<Character, (Owner Owner, bool Captured, int Injury)> _characters = [];
    Dictionary<ManufacturingManager, (int Count, Owner SystemOwner, int Real)> _queues = [];
    Dictionary<Mission, MissionCompletion> _missionViews = [];
    Dictionary<StarSystem, int> _surplus = []; // garrison surplus of the side's live system views (type MessageType.NearUprising)
    Dictionary<StarSystem, int?> _smuggling = []; // SmugglingPercent of the side's view (MessageType.Smuggling; null = never seen live)
    Dictionary<GameObject, bool> _damaged = []; // Damaged of the side's capital-ship / Death Star / squadron views (MessageType.Repaired)

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

    static bool Repairable(GameObject o) => !o.Destroyed && Family.Of(o.Type) is >= Families.CapitalShip and < Families.Headquarters;

    // ---- store ----

    /// <summary>Expiry: every message whose day of creation + 300 is before today is deleted.</summary>
    public static void Expire(GameState game) => game.Log.RemoveAll(m => m.Day + Expiry < game.Day);

    /// <summary>Lit buttons: the categories of the messages that are still new.</summary>
    public static HashSet<MessageCategory> LitCategories(GameState game) => [.. game.Log.Where(m => m.New).Select(m => m.Category)];

    /// <summary>Tab exit: every message of the tab's category stops being new (All tab, category None: every message).</summary>
    public static void ClearNew(GameState game, MessageCategory category)
    {
        foreach (var m in game.Log.Where(m => category == MessageCategory.None || m.Category == category)) m.New = false;
    }

    /// <summary>The message window's list for a tab's category: listed messages, newest first (descending id).</summary>
    public static List<GameMessage> List(GameState game, MessageCategory category) => [.. game.Log.Where(m => m.Listed(category)).OrderByDescending(m => m.Id)];

    /// <summary>Loading: entries without an id get one (and count as read). Saves older than the current version are refused before this.</summary>
    public static void Migrate(GameState game)
    {
        int next = game.Log.Count == 0 ? 1 : game.Log.Max(m => m.Id) + 1;
        foreach (var m in game.Log.Where(m => m.Id == 0))
        {
            m.Id = next++;
            if (m.Kind == 0) m.Kind = 3;
        }
        foreach (var m in game.Log.Where(m => m.Category == MessageCategory.Advice && m.Type == 0)) (m.Type, m.Kind) = (MessageType.Tip, 8); // advice saved by earlier versions
        foreach (var m in game.Log.Where(m => m.Type == MessageType.Strike)) m.Kind = 3; // the attacker side is not saved, so a loaded strike evaluates to kind 3
    }

    /// <summary>Adds a message built outside the update (advisor tips) with the next id.</summary>
    public static GameMessage Post(GameState game, GameMessage m)
    {
        m.Id = game.Log.Count == 0 ? 1 : game.Log.Max(x => x.Id) + 1;
        game.Log.Add(m);
        return m;
    }


    /// <summary>The object of a Ref (subject / mission keys), or null.</summary>
    public static GameObject? Find(GameState game, uint key) =>
        key == 0 ? null : game.Systems.Cast<GameObject>().Concat(game.Characters).Concat(game.Created).Concat(game.Missions).FirstOrDefault(o => o.Ref == key);

    // ---- builder ----

    readonly List<GameMessage> _added = [];

    GameMessage Add(int day, MessageType type, MessageCategory category, int kind, MessageText title, IEnumerable<MessageText> body, GameObject? subject = null,
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
    /// Object picture id = the definition's string number + 4096, the object's encyclopedia id
    /// (the UI loads STRATEGY first, else the EData file named by ENCYBMAP); 0 without an object or definition, and for a mod record
    /// (no install picture).
    /// </summary>
    public static int Picture(GameState game, GameObject? o) =>
        o is null || game.Definitions.Resolve(o.Type) is not { } r || r.Common.StringModule == GameDefinitions.ModStringModule ? 0 : r.Common.StringNumber + 4096;

    int Pic(GameObject? o) => Picture(_game, o);

    /// <summary>Overlay is set to the object's picture.</summary>
    static GameMessage Over(GameMessage m, int overlay) { m.Overlay = overlay; return m; }

    // voice lists per handler, by Mothma, Leia, Luke, Luke J (DagobahCompleted), Han, Palpatine, Vader (see MajorVoice)
    static readonly int[] ArriveVoices = [MajorVoice.MothmaArrive, MajorVoice.LeiaArrive, MajorVoice.LukeArrive, MajorVoice.TrainedLukeArrive,
        MajorVoice.HanArrive, MajorVoice.PalpatineArrive, MajorVoice.VaderArrive],
        EscapedVoices = [MajorVoice.MothmaEscaped, MajorVoice.LeiaEscaped, MajorVoice.LukeEscaped, MajorVoice.TrainedLukeEscaped,
        MajorVoice.HanEscaped, MajorVoice.PalpatineEscaped, MajorVoice.VaderEscaped],
        FailedVoices = [MajorVoice.MothmaFailed, MajorVoice.LeiaFailed, MajorVoice.LukeFailed, MajorVoice.TrainedLukeFailed,
        MajorVoice.HanFailed, MajorVoice.PalpatineFailed, MajorVoice.VaderFailed],
        SuccessVoices = [MajorVoice.MothmaSuccess, MajorVoice.LeiaSuccess, MajorVoice.LukeSuccess, MajorVoice.TrainedLukeSuccess,
        MajorVoice.HanSuccess, MajorVoice.PalpatineSuccess, MajorVoice.VaderSuccess],
        HealedVoices = [MajorVoice.MothmaHealed, MajorVoice.LeiaHealed, MajorVoice.LukeHealed, MajorVoice.TrainedLukeHealed,
        MajorVoice.HanHealed, MajorVoice.PalpatineHealed, MajorVoice.VaderHealed];

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
}
