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
    FrontButton? _gidButton; // 0x132 Galactic Information Display
    readonly Dictionary<int, FrontButton> _messageButtons = [];
    readonly Dictionary<int, bool> _silent = []; // alert silent flag per category mask (not saved, Inferred)
    int? _adviceSpeed; // speed saved by the Advice tab
    int _speed = 2; // speed while running; default 2 at game start

    // Frame buttons: id, tooltip, A rect + up / down, E rect + up / down.
    static readonly (int Id, int Tip, Rect2I A, ushort AUp, ushort ADown, Rect2I E, ushort EUp, ushort EDown)[] Buttons =
    [
        (0x12D, 5377, new(106, 408, 27, 16), 10002, 10001, new(143, 434, 37, 24), 10016, 10015),
        (0x12E, 5378, new(157, 407, 27, 15), 10004, 10003, new(199, 434, 37, 24), 10018, 10017),
        (0x130, 5380, new(209, 405, 27, 16), 10008, 10007, new(253, 433, 34, 22), 10022, 10021),
        (0x12F, 5379, new(258, 405, 27, 16), 10006, 10005, new(412, 433, 34, 22), 10020, 10019),
        (0x131, 5381, new(394, 405, 27, 16), 10010, 10009, new(465, 434, 35, 24), 10024, 10023),
        (0x132, 5382, new(446, 406, 27, 16), 10012, 10011, new(519, 434, 37, 25), 10026, 10025),
        (0x133, 5376, new(3, 355, 27, 41), 10014, 10013, new(79, 192, 35, 57), 10028, 10027),
    ];

    // Message-category buttons: id, tooltip, category, A y + up / down, E y + up / down; 27 x 22 at x 3 (A) / 611 (E).
    static readonly (int Id, int Tip, int Category, int AY, ushort AUp, ushort ADown, int EY, ushort EUp, ushort EDown)[] MessageButtons =
    [
        (0x136, 32785, MessageCategory.Support, 109, 10060, 10050, 110, 10040, 10030),
        (0x137, 32791, MessageCategory.Fleet, 134, 10061, 10051, 135, 10041, 10031),
        (0x138, 32788, MessageCategory.Mission, 159, 10062, 10052, 160, 10042, 10032),
        (0x139, 32786, MessageCategory.Resource, 184, 10063, 10053, 185, 10043, 10033),
        (0x13A, 32787, MessageCategory.Manufacturing, 209, 10064, 10054, 210, 10044, 10034),
        (0x13B, 32790, MessageCategory.Defense, 234, 10065, 10055, 235, 10045, 10035),
        (0x13C, 32792, MessageCategory.Conflict, 259, 10066, 10056, 260, 10046, 10036),
        (0x13D, 32818, MessageCategory.Advice, 284, 10067, 10057, 285, 10047, 10037),
        (0x13E, 32789, MessageCategory.Chat, 309, 10068, 10058, 310, 10048, 10038),
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
            int tab = MessageCategory.TabOf(m.Category);
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
        if (_game is null || Windows.Find(0xD) is not null) return;
        Only(null);
        Windows.Open(0xD, () => new MessageWindow(this, tab));
        _advisor?.MessageWindow(true);
    }

    /// <summary>
    /// Messages added: kinds 3, 4, 5, 8 with an advisor code and the category's alert on feed the advisor
    /// then the lit recompute, skipped while the message window is open.
    /// </summary>
    void MessagesArrived(IEnumerable<GameMessage> added)
    {
        foreach (var m in added)
            if (m.Kind is 3 or 4 or 5 or 8 && m.AdvisorCode != 0 && !AlertSilent(m.Category)) _advisor?.MessageCode(m.AdvisorCode);
        LightMessageButtons();
    }

    void LightMessageButtons()
    {
        if (_game is null || Windows.Find(0xD) is not null) return;
        var lit = Messages.LitCategories(_game);
        foreach (var (category, button) in _messageButtons) button.Checked = lit.Contains(category);
    }

    /// <summary>Tab exit: the tab category's new bits cleared and its button dark (All: every message and button).</summary>
    internal void MessageTabExit(int tab)
    {
        int mask = MessageCategory.OfTab(tab);
        Messages.ClearNew(_game!, mask);
        foreach (var (category, button) in _messageButtons) if (mask == 0 || category == mask) button.Checked = false;
    }

    /// <summary>The message window closed: the recompute runs again.</summary>
    internal void MessageWindowClosed()
    {
        _advisor?.MessageWindow(false);
        LightMessageButtons();
    }

    /// <summary>Alert silent flag per category; the All tab (category None) holds the global setting.</summary>
    internal bool AlertSilent(int mask) => _silent.GetValueOrDefault(mask, _silent.GetValueOrDefault(0));
    internal void ToggleAlert(int mask) => _silent[mask] = !AlertSilent(mask);

    /// <summary>Advice tab: the speed is saved and set to 1; leaving the tab or closing restores it.</summary>
    internal void AdviceSpeed()
    {
        _adviceSpeed ??= _auto ? _speed : 0;
        if (_auto) SetSpeed(1);
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
            button.Sound = id == 0x132 ? Assets.Wave(600, "STRATEGY.DLL") : Assets.Click; // 600 for the picker button, else 608
            if (id == 0x132)
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
        int[] tips = [5395, 5392, 5393, 5394];
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
            case 0x12D: OpenFinder(Finders.SystemFinder); break;
            case 0x12E: OpenFinder(Finders.FleetFinder); break;
            case 0x130: OpenFinder(Finders.TroopFinder); break;
            case 0x12F: OpenFinder(Finders.PersonnelFinder); break;
            case 0x131: OpenEncyclopedia(null, 0); break;
            case 0x132: OpenPicker(); break;
            case 0x133: RequestOptions(); break;
        }
        UpdateStatus();
    }

    /// <summary>
    /// Speed: speed s 1-4 runs the days and sends command 0x280 with ticks per day 600 / 60 / 12 / 4; s 0
    /// pauses. Real time per day (T + 1) * 0.25 s (Inferred).
    /// </summary>
    void SetSpeed(int s)
    {
        if (_game is null) return;
        if (s == 0)
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
        Orders.Submit(_game!, new Command(0x280, _map.Side) { ArgInt = (uint)Clock.TicksForSpeed(_speed) });
        while (_game!.Clock.DispatchOne(_game)) { }
    }

    double DayInterval => (Clock.TicksForSpeed(_speed) + 1) * 0.25;

    /// <summary>Speed menu (right click in the speed rect): rows 0x20-0x24 (A) / 0x25-0x29 (E), all enabled.</summary>
    void OpenSpeedMenu(Vector2 at)
    {
        int first = _side == 0 ? 0x20 : 0x25;
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
            Assets.Confirm(GetNode("Hud"), Assets.Dialog(4610) is { Length: > 0 } t ? t : "Exit?", () => GetTree().Quit());
            return true;
        }
        if (k.AltPressed)
        {
            switch (k.Keycode)
            {
                case Key.P when _auto: SetSpeed(0); return true;
                case Key.Equal or Key.KpAdd when _auto && _speed < 4: SetSpeed(_speed + 1); return true;
                case Key.Minus or Key.KpSubtract when _speed > 1: _speed--; if (_auto) SetSpeed(_speed); else UpdateTopBar(); return true;
                case Key.Y: OpenMessages(0x79); return true;
                case Key.O when _pending is null: AdvisorCommand(0x113); return true; // window-menu rows that are enabled
                case Key.H when _pending is null: AdvisorCommand(0x114); return true;
                case Key.S when _pending is null: StatusKey(); return true;
                case Key.I when Windows.Find(0xD) is { } m: return m.HandleKey(k);
            }
            return false;
        }
        if (k.Keycode is >= Key.F1 and <= Key.F7 && !(k.Keycode == Key.F5 && k.ShiftPressed) && FinderOpen) return true; // ignored while a finder is open
        switch (k.Keycode)
        {
            case Key.F1: FrameCommand(0x133); return true;
            case Key.F2: FrameCommand(0x12D); return true;
            case Key.F3: FrameCommand(0x12E); return true;
            case Key.F4: FrameCommand(0x130); return true;
            case Key.F5 when !k.ShiftPressed: FrameCommand(0x12F); return true;
            case Key.F6: OpenMessages(0x79); return true;
            case Key.F7: FrameCommand(0x131); return true;
        }
        return false;
    }
}
