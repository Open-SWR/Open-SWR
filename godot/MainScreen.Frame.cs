using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>Frame buttons, top bar, game speed and keys of the strategy screen.</summary>
public partial class MainScreen
{
    Control? _frameControls; // rebuilt per side
    TopBar _topBar = null!;
    FrontButton? _gidButton; // Galactic Information Display
    readonly Dictionary<MessageCategory, FrontButton> _messageButtons = [];
    readonly Dictionary<MessageCategory, bool> _silent = []; // alert silent flag per category (not saved, Inferred)
    int? _adviceSpeed; // speed saved by the Advice tab
    int _speed = SpeedLevel.Normal; // speed while running; default Normal at game start

    // Frame buttons: id, tooltip, A rect + up / down, E rect + up / down.
    static readonly (int Id, int Tip, Rect2I A, ushort AUp, ushort ADown, Rect2I E, ushort EUp, ushort EDown)[] Buttons =
    [
        (ControlIds.SystemFinderButton, StrategyTexts.SystemFinder, new(106, 408, 27, 16), StrategyBitmaps.SystemFinderAllianceUp, StrategyBitmaps.SystemFinderAllianceDown, new(143, 434, 37, 24), StrategyBitmaps.SystemFinderEmpireUp, StrategyBitmaps.SystemFinderEmpireDown),
        (ControlIds.FleetFinderButton, StrategyTexts.FleetFinder, new(157, 407, 27, 15), StrategyBitmaps.FleetFinderAllianceUp, StrategyBitmaps.FleetFinderAllianceDown, new(199, 434, 37, 24), StrategyBitmaps.FleetFinderEmpireUp, StrategyBitmaps.FleetFinderEmpireDown),
        (ControlIds.TroopFinderButton, StrategyTexts.TroopFinder, new(209, 405, 27, 16), StrategyBitmaps.TroopFinderAllianceUp, StrategyBitmaps.TroopFinderAllianceDown, new(253, 433, 34, 22), StrategyBitmaps.TroopFinderEmpireUp, StrategyBitmaps.TroopFinderEmpireDown),
        (ControlIds.PersonnelFinderButton, StrategyTexts.PersonnelFinder, new(258, 405, 27, 16), StrategyBitmaps.PersonnelFinderAllianceUp, StrategyBitmaps.PersonnelFinderAllianceDown, new(412, 433, 34, 22), StrategyBitmaps.PersonnelFinderEmpireUp, StrategyBitmaps.PersonnelFinderEmpireDown),
        (ControlIds.EncyclopediaButton, StrategyTexts.Encyclopedia, new(394, 405, 27, 16), StrategyBitmaps.OpenEncyclopediaAllianceUp, StrategyBitmaps.OpenEncyclopediaAllianceDown, new(465, 434, 35, 24), StrategyBitmaps.OpenEncyclopediaEmpireUp, StrategyBitmaps.OpenEncyclopediaEmpireDown),
        (ControlIds.GalacticInformationDisplayButton, StrategyTexts.GalacticInformationDisplay, new(446, 406, 27, 16), StrategyBitmaps.GalacticInformationDisplayAllianceUp, StrategyBitmaps.GalacticInformationDisplayAllianceDown, new(519, 434, 37, 25), StrategyBitmaps.GalacticInformationDisplayEmpireUp, StrategyBitmaps.GalacticInformationDisplayEmpireDown),
        (ControlIds.GameControlsButton, StrategyTexts.GameControls, new(3, 355, 27, 41), StrategyBitmaps.GameControlsAllianceUp, StrategyBitmaps.GameControlsAllianceDown, new(79, 192, 35, 57), StrategyBitmaps.GameControlsEmpireUp, StrategyBitmaps.GameControlsEmpireDown),
    ];

    // Message-category buttons: id, tooltip, category, A y + up / down, E y + up / down; 27 x 22 at x 3 (A) / 611 (E).
    static readonly (int Id, int Tip, MessageCategory Category, int AY, ushort AUp, ushort ADown, int EY, ushort EUp, ushort EDown)[] MessageButtons =
    [
        (ControlIds.SupportMessagesButton, StrategyTexts.SupportMessages, MessageCategory.Support, 109, StrategyBitmaps.SupportMessagesAllianceLit, StrategyBitmaps.SupportMessagesAllianceDark, 110, StrategyBitmaps.SupportMessagesEmpireLit, StrategyBitmaps.SupportMessagesEmpireDark),
        (ControlIds.FleetMessagesButton, StrategyTexts.FleetMessages, MessageCategory.Fleet, 134, StrategyBitmaps.FleetMessagesAllianceLit, StrategyBitmaps.FleetMessagesAllianceDark, 135, StrategyBitmaps.FleetMessagesEmpireLit, StrategyBitmaps.FleetMessagesEmpireDark),
        (ControlIds.MissionMessagesButton, StrategyTexts.MissionMessages, MessageCategory.Mission, 159, StrategyBitmaps.MissionMessagesAllianceLit, StrategyBitmaps.MissionMessagesAllianceDark, 160, StrategyBitmaps.MissionMessagesEmpireLit, StrategyBitmaps.MissionMessagesEmpireDark),
        (ControlIds.ResourceMessagesButton, StrategyTexts.ResourceMessages, MessageCategory.Resource, 184, StrategyBitmaps.ResourceMessagesAllianceLit, StrategyBitmaps.ResourceMessagesAllianceDark, 185, StrategyBitmaps.ResourceMessagesEmpireLit, StrategyBitmaps.ResourceMessagesEmpireDark),
        (ControlIds.ManufacturingMessagesButton, StrategyTexts.ManufacturingMessages, MessageCategory.Manufacturing, 209, StrategyBitmaps.ManufacturingMessagesAllianceLit, StrategyBitmaps.ManufacturingMessagesAllianceDark, 210, StrategyBitmaps.ManufacturingMessagesEmpireLit, StrategyBitmaps.ManufacturingMessagesEmpireDark),
        (ControlIds.DefenseMessagesButton, StrategyTexts.DefenseMessages, MessageCategory.Defense, 234, StrategyBitmaps.DefenseMessagesAllianceLit, StrategyBitmaps.DefenseMessagesAllianceDark, 235, StrategyBitmaps.DefenseMessagesEmpireLit, StrategyBitmaps.DefenseMessagesEmpireDark),
        (ControlIds.ConflictMessagesButton, StrategyTexts.ConflictMessages, MessageCategory.Conflict, 259, StrategyBitmaps.ConflictMessagesAllianceLit, StrategyBitmaps.ConflictMessagesAllianceDark, 260, StrategyBitmaps.ConflictMessagesEmpireLit, StrategyBitmaps.ConflictMessagesEmpireDark),
        (ControlIds.AdviceMessagesButton, StrategyTexts.AdviceMessages, MessageCategory.Advice, 284, StrategyBitmaps.AdviceMessagesAllianceLit, StrategyBitmaps.AdviceMessagesAllianceDark, 285, StrategyBitmaps.AdviceMessagesEmpireLit, StrategyBitmaps.AdviceMessagesEmpireDark),
        (ControlIds.ChatMessagesButton, StrategyTexts.ChatMessages, MessageCategory.Chat, 309, StrategyBitmaps.ChatMessagesAllianceLit, StrategyBitmaps.ChatMessagesAllianceDark, 310, StrategyBitmaps.ChatMessagesEmpireLit, StrategyBitmaps.ChatMessagesEmpireDark),
    ];

    /// <summary>
    /// Message-category buttons: animated buttons without animation (Inferred); each opens the message window on its
    /// category's tab. The per-pixel hit test is left out (rect hit). Lit = the first image (checked), dark = the still
    /// image: lit while a message of the category has the new bit; recomputed after messages arrive and when the
    /// message window closes, frozen while it is open; a tab exit darkens that category at once.
    /// </summary>
    void BuildMessageButtons()
    {
        bool a = _side == 0;
        _messageButtons.Clear();
        foreach (var m in MessageButtons)
        {
            var lit = Assets.Strategy(a ? m.AUp : m.EUp);
            var button = new FrontButton { Up = Assets.Strategy(a ? m.ADown : m.EDown), Down = lit, CheckedImage = lit, TooltipText = UiFrame.Label(m.Tip) };
            int tab = MessageTab.TabOf(m.Category);
            button.Command = () => OpenMessages(tab);
            button.Place(a ? 3 : 611, a ? m.AY : m.EY, 27, 22);
            _messageButtons[m.Category] = button;
            _frameControls!.AddChild(button);
        }
        LightMessageButtons();
    }

    /// <summary>Message window on a tab: only when none is open and a game runs.</summary>
    internal void OpenMessages(int tab)
    {
        if (_game is null || Windows.Find(WindowKind.Messages) is not null) return;
        Only(null);
        Windows.Open(WindowKind.Messages, () => new MessageWindow(this, tab));
        _advisor?.MessageWindow(true);
    }

    /// <summary>
    /// Messages added: kinds 3, 4, 5, 8 with an advisor code and the category's alert on feed the advisor
    /// then the lit recompute, skipped while the message window is open.
    /// </summary>
    void MessagesArrived(IEnumerable<GameMessage> added)
    {
        foreach (var m in added)
            if (m.Kind is MessageKinds.Normal or MessageKinds.Encyclopedia or MessageKinds.ContinueQuestion or MessageKinds.NoTarget && m.AdvisorCode != 0 && !AlertSilent(m.Category)) _advisor?.MessageCode(m.AdvisorCode);
        LightMessageButtons();
    }

    void LightMessageButtons()
    {
        if (_game is null || Windows.Find(WindowKind.Messages) is not null) return;
        var lit = Messages.LitCategories(_game);
        foreach (var (category, button) in _messageButtons) button.Checked = lit.Contains(category);
    }

    /// <summary>Tab exit: the tab category's new bits cleared and its button dark (All: every message and button).</summary>
    internal void MessageTabExit(int tab)
    {
        var category = MessageTab.CategoryOf(tab);
        Messages.ClearNew(_game!, category);
        foreach (var (buttonCategory, button) in _messageButtons) if (category == MessageCategory.None || buttonCategory == category) button.Checked = false;
    }

    /// <summary>The message window closed: the recompute runs again.</summary>
    internal void MessageWindowClosed()
    {
        _advisor?.MessageWindow(false);
        LightMessageButtons();
    }

    /// <summary>Alert silent flag per category; the All tab (category None) holds the global setting.</summary>
    internal bool AlertSilent(MessageCategory category) => _silent.GetValueOrDefault(category, _silent.GetValueOrDefault(MessageCategory.None));
    internal void ToggleAlert(MessageCategory category) => _silent[category] = !AlertSilent(category);

    /// <summary>Advice tab: the speed is saved and set to 1; leaving the tab or closing restores it.</summary>
    internal void AdviceSpeed()
    {
        _adviceSpeed ??= _auto ? _speed : 0;
        if (_auto) SetSpeed(SpeedLevel.Slowest);
    }

    internal void RestoreAdviceSpeed()
    {
        if (_adviceSpeed is not { } s) return;
        _adviceSpeed = null;
        if (s > 0) SetSpeed(s);
    }

    Rect2 SpeedRect => _side == 0 ? new Rect2(103, 21, 82, 12) : new Rect2(499, 19, 81, 12);

    /// <summary>The side's frame controls: bitmap buttons with tooltips, top bar, tooltip rects of the speed box and the monitors.</summary>
    void BuildFrame()
    {
        _frameControls?.QueueFree();
        _frameControls = new Control { Size = LogicalSize, MouseFilter = Control.MouseFilterEnum.Ignore };
        GetNode("Hud").AddChild(_frameControls);
        GetNode("Hud").MoveChild(_frameControls, 0); // under the panels and windows
        bool a = _side == 0;
        foreach (var b in Buttons)
        {
            var (rect, up, down) = a ? (b.A, b.AUp, b.ADown) : (b.E, b.EUp, b.EDown);
            var button = new FrontButton { Up = Assets.Strategy(up), Down = Assets.Strategy(down), TooltipText = UiFrame.Label(b.Tip) };
            int id = b.Id;
            button.Sound = id == ControlIds.GalacticInformationDisplayButton ? Assets.Wave(StrategySounds.FilterPickerButton, "STRATEGY.DLL") : Assets.Click; // 600 for the picker button, else 608
            if (id == ControlIds.GalacticInformationDisplayButton)
            {
                // lit while a filter is on, unlit for Display Off; lit image = the down image (Inferred)
                button.CheckedImage = button.Down;
                button.Checked = _filter != MapFilter.Off;
                _gidButton = button;
            }
            button.Command = () => FrameCommand(id);
            button.Place(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
            _frameControls.AddChild(button);
        }
        _topBar = new TopBar { SideOne = a };
        _frameControls.AddChild(_topBar);
        // tooltip tools: speed rect 5395, monitors 5392-5394
        Rect2[] tools = a ? [SpeedRect, new(240, 16, 90, 17), new(335, 16, 90, 17), new(430, 16, 90, 17)]
            : [SpeedRect, new(149, 18, 92, 19), new(248, 18, 92, 19), new(345, 18, 92, 19)];
        int[] tips = [StrategyTexts.GameSpeedControl, StrategyTexts.RawMaterialsMonitor, StrategyTexts.RefinedMaterialsMonitor, StrategyTexts.MaintenanceMonitor];
        for (int i = 0; i < tools.Length; i++)
            _frameControls.AddChild(new Control { Position = tools[i].Position, Size = tools[i].Size, TooltipText = UiFrame.Label(tips[i]), MouseFilter = Control.MouseFilterEnum.Pass });
        BuildMessageButtons();
        UpdateTopBar();
    }

    void UpdateTopBar()
    {
        if (_game is null) return;
        var side = _game.Sides[_map.Side];
        (_topBar.Day, _topBar.Speed, _topBar.Raw, _topBar.Refined) = (_game.Day, _auto ? _speed : 0, side.RawStock, side.RefinedStock);
        (_topBar.Maintenance, _topBar.Capacity) = (side.MaintenanceCapacity - side.MaintenanceAllocated, side.MaintenanceCapacity);
        _topBar.QueueRedraw();
    }

    /// <summary>
    /// Frame button commands: the four finders, Encyclopedia, Galactic Information Display: the filter picker,
    /// Game Controls: the options screen. Nothing without a game.
    /// </summary>
    void FrameCommand(int id)
    {
        if (_game is null) return;
        switch (id)
        {
            case ControlIds.SystemFinderButton: OpenFinder(WindowKind.SystemFinder); break;
            case ControlIds.FleetFinderButton: OpenFinder(WindowKind.FleetFinder); break;
            case ControlIds.TroopFinderButton: OpenFinder(WindowKind.TroopFinder); break;
            case ControlIds.PersonnelFinderButton: OpenFinder(WindowKind.PersonnelFinder); break;
            case ControlIds.EncyclopediaButton: OpenEncyclopedia(null, 0); break;
            case ControlIds.GalacticInformationDisplayButton: OpenPicker(); break;
            case ControlIds.GameControlsButton: RequestOptions(); break;
        }
        UpdateStatus();
    }

    /// <summary>
    /// Speed: speed s 1-4 runs the days and sends the speed command with ticks per day 600 / 60 / 12 / 4; s 0
    /// pauses. Real time per day (T + 1) * 0.25 s (Inferred).
    /// </summary>
    void SetSpeed(int s)
    {
        if (_game is null) return;
        if (s == SpeedLevel.Paused)
        {
            _auto = false;
        }
        else
        {
            _speed = s;
            _auto = !_game.GameOver;
            SendSpeed();
        }
        _autoTimer = 0;
        UpdateTopBar();
        UpdateStatus();
    }

    void SendSpeed()
    {
        Orders.Submit(_game!, new Command(CommandType.GameSpeed, _map.Side) { ArgInt = (uint)Clock.TicksForSpeed(_speed) });
        while (_game!.Clock.DispatchOne(_game)) { }
    }

    double DayInterval => (Clock.TicksForSpeed(_speed) + 1) * 0.25;

    /// <summary>Speed menu (right click in the speed rect): rows 32-36 (A) / 37-41 (E), all enabled.</summary>
    void OpenSpeedMenu(Vector2 at)
    {
        int first = _side == 0 ? ControlIds.SpeedMenuAllianceFirst : ControlIds.SpeedMenuEmpireFirst;
        var rows = OpenSWR.Data.UiRcData.BuildMenu(Enumerable.Range(first, 5).Select(id => (id, true, false)), Assets.Menu);
        OpenMenu(new GameMenu(rows, at, SideOne, id => SetSpeed(id - first)));
    }

    /// <summary>
    /// Keys that map to existing features: F1-F7 (not with Ctrl held), Alt+P pause, Alt+= / Alt+Numpad+ faster, Alt+- /
    /// Alt+Numpad- slower, Alt+Y messages, Alt+S status, Ctrl+X exit (with a confirmation, own). True = consumed.
    /// </summary>
    bool FrameKey(InputEventKey k)
    {
        if (_game is null) return false;
        if (k.CtrlPressed)
        {
            if (k.Keycode != Key.X) return false;
            Assets.Confirm(GetNode("Hud"), DialogText.QuitConfirm is { Length: > 0 } t ? t : "Exit?", () => GetTree().Quit());
            return true;
        }
        if (k.AltPressed)
        {
            switch (k.Keycode)
            {
                case Key.P when _auto: SetSpeed(0); return true;
                case Key.Equal or Key.KpAdd when _auto && _speed < 4: SetSpeed(_speed + 1); return true;
                case Key.Minus or Key.KpSubtract when _speed > 1: _speed--; if (_auto) SetSpeed(_speed); else UpdateTopBar(); return true;
                case Key.Y: OpenMessages(MessageTab.All); return true;
                case Key.O when _pending is null: AdvisorCommand(AdvisorMenu.GalaxyOverview); return true; // window-menu rows that are enabled
                case Key.H when _pending is null: AdvisorCommand(AdvisorMenu.Objectives); return true;
                case Key.S when _pending is null: StatusKey(); return true;
                case Key.I when Windows.Find(WindowKind.Messages) is { } m: return m.HandleKey(k);
            }
            return false;
        }
        if (k.Keycode is >= Key.F1 and <= Key.F7 && !(k.Keycode == Key.F5 && k.ShiftPressed) && FinderOpen) return true; // ignored while a finder is open
        switch (k.Keycode)
        {
            case Key.F1: FrameCommand(ControlIds.GameControlsButton); return true;
            case Key.F2: FrameCommand(ControlIds.SystemFinderButton); return true;
            case Key.F3: FrameCommand(ControlIds.FleetFinderButton); return true;
            case Key.F4: FrameCommand(ControlIds.TroopFinderButton); return true;
            case Key.F5 when !k.ShiftPressed: FrameCommand(ControlIds.PersonnelFinderButton); return true;
            case Key.F6: OpenMessages(MessageTab.All); return true;
            case Key.F7: FrameCommand(ControlIds.EncyclopediaButton); return true;
        }
        return false;
    }
}
