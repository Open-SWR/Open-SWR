using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Data.Advisor;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// The advisor on the strategy screen: two sprite displays per side with their idle commands,
/// the side's script (C3POACT / IMP22ACT) played through the runtime (<see cref="Advisor"/>), post-action commands (guided UI
/// steps of the briefing, the side's overview page), the briefing input hooks. The runtime ticks once per frame after the
/// frame's clock work and message building. Env OPENSWR_DEMO_ADVISOR = refuse | idle | tips | menu | build (render checks); OPENSWR_DEMO_BRIEFING=1 plays the briefing from the install's renamed DLL.
/// </summary>
public partial class MainScreen
{
    // Hotspot rects per side (main 0xDEAD, secondary 0xBEEF), see AdvisorRect
    const ushort MainIdle = 403, SecondaryEnd = 11575;

    Advisor? _advisor;
    AdvisorSprites? _sprites, _briefing;
    Dictionary<uint, SptAction> _script = [];
    AdvisorDisplay? _advMain, _advSecondary;
    bool _hooks; // briefing input hooks (briefing action start ... step 0xD; original: from step 0xC)

    /// <summary>
    /// The human controller's advisor for this screen: kept when the game brings one (a save), else a new one: a new game
    /// pre-requests the briefing when ALBRIEF.DLL / EMBRIEF.DLL exists (else no briefing is requested), a loaded game without advisor
    /// state gets none. Then the displays and the script of the player's side.
    /// </summary>
    void AttachAdvisor(GameState game, bool newGame)
    {
        var side = game.Settings.PlayerSide;
        bool one = side == SimOwner.Alliance;
        _sprites = new AdvisorSprites(new SpriteDll(PeResources.Load(Path.Combine(_gameDir, one ? "ALSPRITE.DLL" : "EMSPRITE.DLL"))));
        if (DrawLog.On) _sprites.Module = one ? "ALSPRITE.DLL" : "EMSPRITE.DLL";
        string brief = Path.Combine(_gameDir, one ? "ALBRIEF.DLL" : "EMBRIEF.DLL");
        // env OPENSWR_DEMO_BRIEFING=1 (render check): the install's renamed copy (ALBRIEF.DLL.bak, 1.02 mod) stands in for the DLL
        if (!File.Exists(brief) && System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_BRIEFING") == "1" && File.Exists(brief + ".bak")) brief += ".bak";
        _briefing = File.Exists(brief) ? new AdvisorSprites(new SpriteDll(PeResources.Load(brief))) : null;
        if (DrawLog.On && _briefing is not null) _briefing.Module = one ? "ALBRIEF.DLL" : "EMBRIEF.DLL";
        _script = SptFile.Read(File.ReadAllBytes(Path.Combine(_gameDir, "GData", one ? "C3POACT.SPT" : "IMP22ACT.SPT"))).ToDictionary(a => a.Id);
        var human = (OpenSWR.Sim.Ai.HumanController)game.Controllers[side];
        human.Advisor ??= new Advisor(side, () => game.Clock.Tick, newGame ? _briefing is not null : null, tips: game.Settings.Column is 1 or 4);
        _advisor = human.Advisor;
        _advisor.Now = () => game.Clock.Tick;
        _advisor.IdleNow = () => (int)(_advisorSeconds * 4); // idle chatter on screen time in 250 ms ticks, also while paused (openswr)
        _advisor.MessageWindow(false);
        _advisor.Play = PlayAction;
        _advisor.Post = PostCommand;
        _advisor.Flush = () => { _advMain?.Flush(); _advSecondary?.Flush(); _hooks = false; };
        _advisor.TopicSource = LoadTopics;
        _hooks = false;
        BuildDisplays();
    }

    /// <summary>Displays: each first shows its idle RCDATA as a start command (5, 5), then idles.</summary>
    void BuildDisplays()
    {
        _advMain?.QueueFree();
        _advSecondary?.QueueFree();
        var sprites = _sprites!;
        _advMain = new AdvisorDisplay { Position = AdvisorRect(0).Position };
        _advSecondary = new AdvisorDisplay { Position = AdvisorRect(1).Position };
        var hud = GetNode("Hud");
        hud.AddChild(_advMain);
        hud.AddChild(_advSecondary);
        hud.MoveChild(_advMain, 0); // under the windows
        hud.MoveChild(_advSecondary, 0);
        var main = sprites.Bitmap(sprites.Dll.Words(MainIdle)[1]);
        var second = sprites.Bitmap(sprites.Dll.Words(_side == 0 ? (ushort)402 : (ushort)401)[1]);
        _advMain.Idle = () => new StillCommand(main, 15, () => _advisor?.GateOpen()); // type 18 hold, callback = the gate
        _advSecondary.Idle = () => new StillCommand(second, 15, () => { if (_advisor is not null) _advisor.SecondaryIdle = 1; }); // type 5 hold
        _advMain.Enqueue(new StillCommand(main, 5));
        _advSecondary.Enqueue(new StillCommand(second, 5));
    }

    AudioStreamPlayer? _music;
    bool _musicOn;

    /// <summary>
    /// Strategy music: the advisor's pick (UI-generator draws; the advisor's mood byte)
    /// plays MDATA.301-312 on the Music bus, only with Play Music on. Called at screen entry (new game: after the game start's
    /// catalogue lists), after an event movie, at the track's end (Inferred: the music-end notification) and when the
    /// screen comes back with Play Music turned on.
    /// </summary>
    void PickMusic()
    {
        _musicOn = FrontSettings.MusicOn;
        if (_advisor is null || !_musicOn)
        {
            _music?.Stop();
            return;
        }
        int track = _advisor.MusicTrack();
        if (_music is null)
        {
            // at the scene root, not under this screen: the options screen takes the screen out of the tree, and the track plays on
            // there (openswr: the volume sliders are heard while they move); freed with the screen
            _music = new AudioStreamPlayer { Bus = "Music" };
            _music.Finished += PickMusic;
        }
        try
        {
            _music.Stream = AudioStreamWav.LoadFromBuffer(File.ReadAllBytes(Path.Combine(_gameDir, "MDATA", $"MDATA.{track}")));
            if (_music.IsInsideTree()) _music.Play();
            else
            {
                var music = _music; // the root may be busy setting up its children: added and started at the frame's end
                Callable.From(() =>
                {
                    if (!IsInstanceValid(music)) return;
                    if (!music.IsInsideTree()) ((SceneTree)Engine.GetMainLoop()).Root.AddChild(music);
                    if (FrontSettings.MusicOn) music.Play();
                }).CallDeferred();
            }
        }
        catch (IOException ex)
        {
            GD.PushWarning("strategy music: " + ex.Message);
        }
    }

    /// <summary>Back from the options screen: the music switch turned on gives a new pick; still on, the track plays on; off, silence.</summary>
    public override void _EnterTree()
    {
        if (_game is null || _advisor is null) return;
        if (!FrontSettings.MusicOn) _music?.Stop();
        else if (!_musicOn || _music?.Stream is null) PickMusic();
        else if (!_music.Playing) _music.Play();
        _musicOn = FrontSettings.MusicOn;
    }

    /// <summary>Play Music switched off on the options screen: the strategy track stops at once (the screen is out of the tree).</summary>
    public void MusicSwitched()
    {
        if (!FrontSettings.MusicOn) _music?.Stop();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && _music is not null && IsInstanceValid(_music)) _music.QueueFree();
    }

    /// <summary>
    /// Per frame: the advisor tick, after the frame's clock work and message building; then the 300-tick tip timer (the human controller
    /// checks it every tick; here it runs per frame, so a tip posted during a day run is stamped with the day's last tick, Inferred).
    /// </summary>
    double _advisorSeconds; // screen time for the idle chatter clock

    void AdvisorFrame(double delta)
    {
        _advisorSeconds += delta;
        if (_game is null || _advisor is null) return;
        _advisor.Tick();
        if (_advisor.TipTimer(_game) is { Count: > 0 } tips) MessagesArrived(tips);
    }

    /// <summary>A system sub-window opened from a sector window: the tip trigger of its group (manufacturing, fleet, planet, missions).</summary>
    internal void SubWindowTip(int mode)
    {
        AdvisorTip? tip = mode switch { 4 => AdvisorTip.Manufacturing, 0x10 => AdvisorTip.Fleet, 8 => AdvisorTip.Planet, 0x40 => AdvisorTip.Missions, _ => null };
        if (tip is { } t && _game is not null && _advisor is not null) MessagesArrived(_advisor.TipTrigger(_game, t));
    }

    /// <summary>The side's advice topic list.</summary>
    IReadOnlyList<AdviceTopic> LoadTopics() => AdviceTopic.Read(Assets.Module("TEXTSTRA.DLL"), _side == 0);

    /// <summary>
    /// Action <paramref name="id"/>: every step whose flags allow it (Translate Counterpart on, message window
    /// closed) becomes a sprite command on the main display, from module 9 or, for a briefing step, the briefing DLL; a missing
    /// DLL or RCDATA enqueues nothing.
    /// </summary>
    void PlayAction(int id)
    {
        if (!_script.TryGetValue((uint)id, out var action) || action.Enabled == 0 || _advMain is null) return;
        // openswr deviation: the hooks go on with the briefing itself, not at its step 0xC, so Esc skips the opening clip too
        if (id is 41 or 74 && _briefing is not null) _hooks = true;
        foreach (var step in action.Steps)
        {
            if (!_advisor!.Plays(step.Commentary)) continue;
            var sprites = step.Briefing ? _briefing : _sprites;
            if (sprites is null || !sprites.Dll.Has(step.RcId)) continue;
            var command = sprites.Dll.Command(step.RcId);
            var (p1, p2) = command.Parameters(step);
            if (Build(sprites, command, () => _advisor.Posted((int)p1, (int)p2)) is { } c) _advMain.Enqueue(c);
        }
    }

    /// <summary>Sprite command of a type; type 17 finishes with <paramref name="finished"/> (the post command), 21 with the relay's done.</summary>
    AdvisorCommand? Build(AdvisorSprites sprites, SpriteCommand command, System.Action? finished = null)
    {
        var dll = sprites.Dll;
        switch (command.Type)
        {
            case SpriteCommand.Clip or SpriteCommand.VoiceClip:
                if (!dll.Has(command.ResId)) return null;
                var clip = dll.Clip(command.ResId);
                return sprites.Clip(command.ResId, clip.VoiceModule == 13 ? _briefing : _sprites);
            case SpriteCommand.Still or SpriteCommand.SecondaryStill:
                return new StillCommand(sprites.Bitmap(dll.Words(command.ResId)[0]), 2, finished);
            case SpriteCommand.Hold or 5:
                return new StillCommand(sprites.Bitmap(dll.Words(command.ResId)[1]), command.W2 > 0 ? command.W2 : 15);
            case SpriteCommand.Relay:
                var words = dll.Words(command.ResId);
                var main = _sprites!; // the secondary's commands are module 9
                return new RelayCommand(sprites.Bitmap(words[1]), _advSecondary!, done =>
                    new[] { Build(main, main.Dll.Command(words[2])), Build(main, main.Dll.Command(SecondaryEnd), done) }.OfType<AdvisorCommand>());
        }
        return null; // unregistered types play nothing
    }

    /// <summary>Post-action command: (2, n) guided-UI step, (3, x) the end of the game.</summary>
    void PostCommand(int p1, int p2)
    {
        if (p1 == 3) EndGame();
        else if (p1 == 2) Guided(p2);
    }

    bool _endPending; // an end post came during an event movie

    /// <summary>
    /// Post type 3: end state from the victory word
    /// and the player side; 0 gives nothing (the stopped screen stays, options locked). Else (0x482, application state 0x10-0x13) every
    /// window and pop-up closes, the end movie plays, then the main menu (front end); no save prompt. A playing kind-6 event movie is
    /// not cut: the end waits for its end or skip, then the queued event movies are dropped.
    /// </summary>
    void EndGame()
    {
        if (_game is null || Advisor.EndMovie(Advisor.EndState(_game.VictoryState, _map.Side)) is not { } movie) return;
        if (_endPending = _movie is not null) return;
        _menu?.Close();
        Windows.Clear();
        foreach (var w in _strikeWindows.ToList()) w.QueueFree();
        _strikeWindows.Clear();
        GameEnded?.Invoke(movie);
    }

    /// <summary>
    /// Options request (command 0x15E): Esc, Game Controls 0x133 and the tactical Game Options page;
    /// dropped once the game-over lock is set (with the advisor's request of actions 84-87; Inferred for Esc).
    /// </summary>
    void RequestOptions()
    {
        if (_advisor is { GameOverUi: true }) return;
        OptionsRequested?.Invoke();
    }

    /// <summary>
    /// Guided-UI step (2, n) (C-3PO / IMP-22): map display filters, one-system lists 0x92 / 0x93
    /// with a custom title (list = SD indexes: Coruscant 0x109, Yavin 0x121, or "X's system" = the first system up the player's
    /// view of X: the HQ 0x20000005, Mothma, Luke, Palpatine, Vader), the input hooks (0xC) and the Advice step (0xD).
    /// </summary>
    void Guided(int n)
    {
        bool imp = _side != 0;
        const uint Coruscant = 0x109, Yavin = 0x121, Hq = 0x20000005, Mothma = 0x30000240;
        (int Mode, int Title, uint Of) step = (n, imp) switch
        {
            (1 or 0xB, _) => (0x11, 0, 0),
            (2, false) or (0xE, _) => (0x13, 0, 0),
            (2, true) or (0x12, true) => (0x92, 0x1624, Coruscant),
            (3, false) => (0x93, 0x1623, Hq),
            (3, true) or (8 or 0x14, true) or (0x12, false) => (0x93, 0x1622, Yavin),
            (4, false) or (8 or 0x14, false) => (0x92, 0x1624, Coruscant),
            (4, true) => (0x17, 0, 0),
            (5, _) => (0x20, 0, 0),
            (6, _) => (0x91, 0, 0),
            (7, false) => (0x93, 0x1625, Mothma),
            (7, true) => (0x92, 0x1627, Refs.Palpatine),
            (9, _) => (MapFilter.Off, 0, 0),
            (0xF, _) => (0x15, 0, 0),
            (0x10, false) => (0x16, 0, 0),
            (0x10, true) => (0x14, 0, 0),
            (0x11, _) => (0x17, 0, 0),
            (0x13, false) => (0x93, 0x1626, Refs.Luke),
            (0x13, true) => (0x92, 0x1628, Refs.Vader),
            _ => (0, 0, 0),
        };
        if (n == 0xC) _hooks = true;
        else if (n == 0xD) AdviceStep();
        else if (step.Mode != 0)
        {
            int[]? list = step.Of switch
            {
                0 => null,
                <= 0xFFFFFF => [(int)step.Of],
                _ => _game!.Created.Concat(_game.Characters).FirstOrDefault(o => o.Ref == step.Of) is { } x
                    && Knowledge.Location(_game, x, _map.Side) is StarSystem at ? [(int)new OpenSWR.Data.Dat.SdRef(at.Type).Index] : [],
            };
            SetFilter(step.Mode, list);
            if (step.Title != 0) SetTitle(Assets.Stra(step.Title));
        }
    }

    /// <summary>Step 0xD: the message window closes; tips on, topic list built (kind-7 burst) and the Advice tab opened; hooks removed.</summary>
    void AdviceStep()
    {
        if (Windows.Find(0xD) is { } open) Windows.Close(open, sound: false);
        _hooks = false;
        if (_advisor!.TipsOff) return;
        MessagesArrived(_advisor.BuildTopics(_game!));
        OpenMessages(0x82);
    }

    /// <summary>Briefing hooks: keys swallowed (Esc up skips), mouse button down skips, ups swallowed, moves pass.</summary>
    bool BriefingInput(InputEvent e)
    {
        if (!_hooks || _advisor is null) return false;
        switch (e)
        {
            case InputEventKey k:
                if (!k.Pressed && k.Keycode == Key.Escape) _advisor.Skip();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } b:
                if (b.Pressed) _advisor.Skip();
                break;
            default:
                return false;
        }
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>
    /// Order result for the advisor: a validation refusal goes to the pair event; an accepted order plays the major character's
    /// acknowledgement voice, then <see cref="Advisor.Accepted"/>.
    /// </summary>
    void AdvisorOrder(Command command, OrderResult valid)
    {
        if (_advisor is null) return;
        if (!valid.Success)
        {
            _advisor.Refused(valid.Category, valid.Code, command, _game);
            return;
        }
        int wave = Messages.DagobahFirst(command) ? VoicePick(0x131) : 0;
        if (wave == 0 && Messages.Acknowledgement(command) is var list and > 0) wave = VoicePick(list);
        PlayVoice(wave);
        _advisor.Accepted(command);
    }

    /// <summary>Voice variant list of the side's sprite DLL, drawn through the advisor's UI-generator source; a missing list (e.g. 0x136) is silent without a draw.</summary>
    int VoicePick(int list) =>
        _sprites is null || !_sprites.Dll.Has((ushort)list) ? 0 : Advisor.Voice(_sprites.Dll.Words((ushort)list), _advisor?.Rand ?? (n => UiRandom.Shared.Rand(n)));

    /// <summary>Plays a sprite-DLL WAVE (module 9) on the Sound bus (Inferred: the effects volume), not looped, mixed.</summary>
    internal void PlayVoice(int wave) => Assets.PlaySound(Voice(wave));

    AudioStream? Voice(int wave) => wave == 0 ? null : _sprites?.Wave((ushort)wave);

    /// <summary>
    /// Uprising alarm: 621 when the player's controller got view message 0x14B with the
    /// new value set (a live system entering Uprising, any core system, a view refreshed to Uprising); 620 when it
    /// got 0x14C with the new value set (a never-controlled system newly seen). Each played once after
    /// each run of ticks (openswr adaptation of "at once"), on the same player pool.
    /// </summary>
    void UprisingAlarm(GameState game)
    {
        if (game.Controllers.GetValueOrDefault(_map.Side) is not OpenSWR.Sim.Ai.HumanController human) return;
        if (human.UprisingAlarms > 0) Assets.PlayStrategy(621);
        // openswr deviation: 620 on a planetary assault by or against the player, not on first sight of a never-controlled system
        if (human.AssaultAlarms > 0) Assets.PlayStrategy(620);
        (human.UprisingAlarms, human.NeverControlledAlarms, human.AssaultAlarms) = (0, 0, 0);
    }

    AudioStreamPlayer? _messageSound, _messageVoice;

    /// <summary>
    /// A message displayed in the message window: the previous message's sound and voice stop, then its
    /// sound (STRATEGY WAVE) and voice (sprite-DLL WAVE) start together on two dedicated players. Closing the window
    /// does not stop them.
    /// </summary>
    internal void PlayMessage(GameMessage m)
    {
        if (_messageSound is null)
        {
            AddChild(_messageSound = new AudioStreamPlayer { Bus = "Sound" });
            AddChild(_messageVoice = new AudioStreamPlayer { Bus = "Sound" });
        }
        _messageSound.Stop();
        _messageVoice!.Stop();
        foreach (var (player, stream) in new[] { (_messageSound, m.Sound != 0 ? Assets.Wave((ushort)m.Sound, "STRATEGY.DLL") : null), (_messageVoice, Voice(m.Voice)) })
            if (stream is not null)
            {
                player.Stream = stream;
                player.Play();
            }
    }

    /// <summary>
    /// Build helper target: the 0x214 Destination is validated and sent like a delegated order (dispatched at once like
    /// openswr's player orders, Inferred; a refusal goes to the pair event); then the production window opens with the prepared
    /// Build command, watched until the player confirms it (action 35 or 28).
    /// </summary>
    void HelperTarget(GameObject? target)
    {
        var game = _game!;
        if (_advisor?.HelperPick(game, target) is not var (destination, build))
        {
            _message = "";
            UpdateStatus();
            return;
        }
        var valid = Orders.Validate(game, destination);
        if (!valid.Success)
        {
            _advisor.Refused(valid.Category, valid.Code, destination, game);
            _message = $"{RowLabel(destination.Type)}: refused ({valid.Category:X}, {valid.Code:X})";
            UpdateStatus();
            return;
        }
        Orders.Submit(game, destination);
        while (game.Clock.DispatchOne(game)) { }
        _map.Refresh();
        Windows.Refresh();
        _advisor.Watch(build);
        OpenProduction(build);
        _message = "";
        UpdateStatus();
    }

    /// <summary>
    /// Env OPENSWR_DEMO_DELEGATE = 115 | 116 | both: Manage Garrisons / Production turned on before the demo days;
    /// after them the status line lists the delegated orders (outbox sequence != 0: manager to destination / product) and the last
    /// one's manufacturing window opens.
    /// </summary>
    void DemoDelegate(bool start)
    {
        if (_advisor is null || _game is null || System.Environment.GetEnvironmentVariable("OPENSWR_DEMO_DELEGATE") is not { Length: > 0 } d) return;
        if (start)
        {
            if (d is "115" or "both") _advisor.Toggle(0x115);
            if (d is "116" or "both") _advisor.Toggle(0x116);
            _game.Clock.CommandTrace = [];
            return;
        }
        var sent = _game.Clock.CommandTrace!.Where(c => c.Side == _map.Side && c.Sequence != 0 && c.Type is 0x214 or 0x210 or 0x212).ToList();
        _message = $"delegated {sent.Count}: " + string.Join(", ", sent.TakeLast(6).Select(c => $"{c.Type:X} {c.Targets[0].Target.Ref:X8}"
            + (c.ArgRef is { } r ? $">{r.Ref:X8}" : $" {c.Fields[0]:X8}")));
        UpdateStatus();
        if (sent.LastOrDefault()?.Targets[0].Target is ManufacturingManager m) GoTo(m);
    }

    /// <summary>Env OPENSWR_DEMO_ADVISOR = refuse (a refused order twice) | idle (a research message code) | tips (Agent Advice on) | menu (the window menu) | build (Build Ships at the first own fleet's system).</summary>
    void DemoAdvisor(string mode)
    {
        if (_advisor is null || _game is null) return;
        switch (mode)
        {
            case "refuse":
                var c = new Command(0x240, _map.Side) { Fields = [0, 0, 0] }; // a mission without a team
                Submit(c);
                Submit(new Command(0x240, _map.Side) { Fields = [0, 0, 0] });
                break;
            case "idle":
                _advisor.MessageCode(4); // research complete: the droid relay and the comment
                break;
            case "tips": // Agent Advice on (as at Novice): the briefing's Advice step opens the Advice tab with the kind-7 topics
                if (_advisor.TipsOff) _advisor.Toggle(0x11E);
                break;
            case "menu":
                OpenAdvisorMenu(AdvisorRect(0).GetCenter(), window: true);
                break;
            case "build": // Build Ships aimed at the first own fleet's system: the production window with the helper's shipyard
                AdvisorCommand(0x110);
                if (_game.Systems.SelectMany(OwnFleets).FirstOrDefault()?.System is { } at)
                {
                    EndTargeting();
                    HelperTarget(at);
                }
                break;
        }
    }
}
