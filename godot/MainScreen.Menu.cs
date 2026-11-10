using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>Object context menu and targeting state of the strategy screen.</summary>
public partial class MainScreen
{
    /// <summary>
    /// Right button up: inside the speed rect: the speed menu; over an open panel (openswr's object windows): the row
    /// object's menu (object mode): the selection's command list, greyed by validation (<see cref="ObjectCommands"/>), plus
    /// Encyclopedia and Status. A right click on the galaxy map itself does nothing.
    /// </summary>
    void RightClick(Vector2 at)
    {
        if (AdvisorRect(0).HasPoint(at)) OpenAdvisorMenu(at, window: true);
        else if (AdvisorRect(1).HasPoint(at)) OpenAdvisorMenu(at, window: false);
        else if (SpeedRect.HasPoint(at)) OpenSpeedMenu(at);
    }

    /// <summary>Advisor displays (rects Inferred from the argument order): the main hotspot on the first, the secondary on the second.</summary>
    Rect2 AdvisorRect(int i) => (_side == 0, i) switch
    {
        (true, 0) => new(541, 337, 67, 116), (true, _) => new(316, 411, 47, 69), (false, 0) => new(0, 347, 107, 133), _ => new(302, 401, 101, 79),
    };

    // alert rows: menu id to message category
    static readonly Dictionary<int, MessageCategory> AlertRows = new()
    {
        [AdvisorAlertMenu.SupportAlerts] = MessageCategory.Support, [AdvisorAlertMenu.FleetAlerts] = MessageCategory.Fleet, [AdvisorAlertMenu.MissionAlerts] = MessageCategory.Mission,
        [AdvisorAlertMenu.ResourceAlerts] = MessageCategory.Resource, [AdvisorAlertMenu.ManufacturingAlerts] = MessageCategory.Manufacturing, [AdvisorAlertMenu.DefenseAlerts] = MessageCategory.Defense,
        [AdvisorAlertMenu.ConflictAlerts] = MessageCategory.Conflict, [AdvisorAlertMenu.AdviceAlerts] = MessageCategory.Advice, [AdvisorAlertMenu.ChatAlerts] = MessageCategory.Chat,
    };

    /// <summary>
    /// Window menu (main hotspot: the window commands) / alert menu (secondary hotspot: the messages command and the alert toggles, checked = alert on);
    /// rows enabled / checked by the advisor. Opening either menu is the advisor's menu click.
    /// </summary>
    void OpenAdvisorMenu(Vector2 at, bool window)
    {
        if (_game is null) return;
        _advisor?.MenuOpen(); // opening either menu is the advisor's menu click
        IEnumerable<(int, bool, bool)> rows = window
            ? new[] { AdvisorMenu.BuildShips, AdvisorMenu.TrainTroops, AdvisorMenu.BuildFacilities, AdvisorMenu.GalaxyOverview, AdvisorMenu.Objectives, AdvisorMenu.ManageGarrisons, AdvisorMenu.ManageProduction, AdvisorMenu.Translate, AdvisorMenu.Tips }
                .Select(id => _advisor?.Row(id) is { } r ? (id, r.Enabled, r.Checked) : (id, id is AdvisorMenu.GalaxyOverview or AdvisorMenu.Objectives, false))
            : [(AdvisorAlertMenu.Messages, true, false), .. AlertRows.Select(r => (r.Key, true, !AlertSilent(r.Value)))];
        OpenMenu(new GameMenu(OpenSWR.Data.UiRcData.BuildMenu(rows, Assets.Menu), at, SideOne, AdvisorCommand));
    }

    /// <summary>Advisor menu commands: Galaxy Overview / Objectives = the status window's side / objectives pages, messages, alert toggles.</summary>
    void AdvisorCommand(int id)
    {
        var game = _game!;
        switch (id)
        {
            case AdvisorMenu.GalaxyOverview:
                OpenStatus(null, overview: true);
                break;
            case AdvisorMenu.Objectives:
                OpenStatus(null);
                break;
            case AdvisorAlertMenu.Messages:
                OpenMessages(MessageTab.All);
                break;
            case AdvisorMenu.BuildShips or AdvisorMenu.TrainTroops or AdvisorMenu.BuildFacilities when _advisor is not null: // build helper: targeting state without a command
                _advisor.HelperStart(id);
                _pending = (id, []);
                Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
                _message = $"{RowLabel(id)}: click the target (Esc / right click: cancel)";
                UpdateStatus();
                break;
            case AdvisorMenu.ManageGarrisons or AdvisorMenu.ManageProduction or AdvisorMenu.Translate or AdvisorMenu.Tips:
                _advisor?.Toggle(id); // the record runs in the delegation cycle
                break;
            case var a when AlertRows.TryGetValue(a, out var category):
                ToggleAlert(category);
                break;
        }
    }

    internal void OpenObjectMenu(List<(GameObject Object, GroupMode Mode)> selection, Vector2 at)
    {
        var rows = ObjectCommands.Rows(_game!, _map.Side, selection);
        var built = OpenSWR.Data.UiRcData.BuildMenu(rows.Select(r => ((int)r.Id, r.Enabled, r.Checked)), Assets.Menu);
        if (built.Count > 0) OpenMenu(new GameMenu(built, at, _map.Side == SimOwner.Alliance, id => Dispatch(id, selection)));
    }

    /// <summary>One game menu at a time.</summary>
    void OpenMenu(GameMenu menu, System.Action? closed = null)
    {
        _menu?.Close();
        _menu = menu;
        menu.Closed = () => { _menu = null; closed?.Invoke(); };
        GetNode("Hud").AddChild(menu);
    }

    static string RowLabel(int id) => Assets.Menu(id) is { } m ? UiFrame.Label(m.Label) : $"{id}";

    /// <summary>
    /// Menu command (UI dispatcher): Encyclopedia / Status (the status window); move, destination and mission commands without a target:
    /// targeting state; the rename commands: name entry; the production commands: the production window;
    /// the mission command with a target: the mission window; everything else (with its system): confirmation
    /// when required, then validate and submit.
    /// </summary>
    internal void Dispatch(int id, List<(GameObject Object, GroupMode Mode)> selection, GameObject? target = null, bool confirm = true)
    {
        var first = selection[0].Object;
        switch (id)
        {
            case (int)ObjectCommands.Encyclopedia:
                OpenEncyclopedia(first, 0);
                break;
            case (int)ObjectCommands.Status:
                OpenStatus(first);
                break;
            case (int)CommandType.Move or (int)CommandType.MoveVariant or (int)CommandType.SetProductionTarget or (int)CommandType.CreateMission when target is null:
                _pending = (id, selection);
                Input.SetDefaultCursorShape(Input.CursorShape.Cross); // REBEXE group cursor 1002 (GameCursors)
                DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
                _message = $"{RowLabel(id)}: click the target (Shift: normal click; Esc / right click: cancel)";
                break;
            case (int)CommandType.Rename or (int)CommandType.SetProductName:
                TextEntry(RowLabel(id), name => Execute(id, selection, null, name));
                return;
            case >= (int)CommandType.ProduceFacility and <= (int)CommandType.ProduceTroop:
                OpenProduction(ObjectCommands.Build((CommandType)id, _map.Side, selection));
                break;
            case (int)CommandType.CreateMission:
                OpenMission(selection, target!);
                break;
            default:
                var command = ObjectCommands.Build((CommandType)id, _map.Side, selection, target);
                // validated first: a refused command gets no dialog, Execute below gives its refusal and drops it;
                // the dialog holds the command built now; Yes validates again and submits it
                if (confirm && ObjectCommands.NeedsConfirm(_game!, _map.Side, command, selection))
                    ConfirmWindow.Show(this, id, selection, ConfirmText(command, selection), () => Submit(command));
                else Execute(id, selection, target);
                return;
        }
        UpdateStatus();
    }

    /// <summary>
    /// Confirmation body: scrap / retire + one line (line
    /// feed + name) per target of the command (key order, Inferred); stop construction + one line per manager (line
    /// feed, queue count, two spaces, the ProductKey definition's name, empty without one); abort; moves (the blockade
    /// text or the plain move text), then per moved object a line feed, its name, ":  " and its days (<see cref="ScreenViews.Transit"/>).
    /// </summary>
    string ConfirmText(Command c, IReadOnlyList<(GameObject Object, GroupMode Mode)> selection)
    {
        // scrap walks the resolved target set (group modes expanded), the others the raw target list; key order (Inferred)
        var targets = c.Type == CommandType.Scrap ? Orders.Targets(c) : c.Targets.Select(t => t.Target).OrderBy(o => o.Ref).ToList();
        string Product(ManufacturingManager m) =>
            m.Orders.FirstOrDefault() is { } head && _game!.Definitions.Resolve(head.Type) is { } d ? _map.DisplayName(d.Common) : "";
        return c.Type switch
        {
            CommandType.Scrap or CommandType.CharacterOrder => Assets.Message(c.Type == CommandType.Scrap ? MessageTemplates.ConfirmScrap : MessageTemplates.ConfirmRetire) + string.Concat(targets.Select(o => MessageTexts.ConfirmTargetLine(ObjectName(o)))),
            CommandType.CancelProduction => MessageTexts.ConfirmStopConstruction() + string.Concat(targets.OfType<ManufacturingManager>().Select(m =>
                MessageTexts.ConfirmManagerLine((System.Func<uint, string>)(kind => kind == MessageArgKinds.QueueCount ? m.Orders.Count.ToString() : kind == MessageArgKinds.ProductName ? Product(m) : "")))),
            CommandType.Move or CommandType.MoveVariant => Assets.Message(ConfirmWindow.Blockaded(_game!, _map.Side, selection) ? MessageTemplates.BlockadeMoveWarning : MessageTemplates.TransitDays)
                + string.Concat(ScreenViews.Transit(_game!, c).Select(t => $"\n{ObjectName(t.Object)}:  {t.Days}")),
            _ => MessageTexts.ConfirmAbortMission(),
        };
    }

    /// <summary>Validate, then submit and dispatch at once; a fleet's bombardment / assault opens the result window.</summary>
    void Execute(int id, List<(GameObject Object, GroupMode Mode)> selection, GameObject? target, string? text = null)
    {
        if (id is (int)CommandType.Assault or (>= (int)CommandType.Bombard and <= (int)CommandType.BombardDestroy) && selection is [(Fleet fleet, GroupMode.Object)] && target is null)
        {
            Strike(fleet, id);
            return;
        }
        Submit(ObjectCommands.Build((CommandType)id, _map.Side, selection, target, text));
    }

    /// <summary>
    /// Validate; refused : the refusal code on the status line (the original sends the refusal to the advisor
    /// as a pair event); accepted: submitted and dispatched at once (no confirmation). Screen and windows refreshed.
    /// </summary>
    internal OrderResult? Submit(Command command)
    {
        var game = _game!;
        var valid = Orders.Validate(game, command);
        OrderResult? result = valid;
        AdvisorOrder(command, valid);
        if (valid.Success) Orders.Submit(game, command, r => result = r);
        while (game.Clock.DispatchOne(game)) { }
        _message = result is { Success: true } ? $"{RowLabel((int)command.Type)}: done" : $"{RowLabel((int)command.Type)}: refused ({result?.Category}, {result?.Code})";
        _map.Selected = null;
        _map.Refresh();
        Windows.Refresh();
        UpdateStatus();
        return result;
    }

    /// <summary>Targeting state input: left button up picks the target (panel row, else system); Shift passes the click on; Esc / right click cancel.</summary>
    bool TargetingInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b when b.ShiftPressed:
                return false; // a normal click; the state stays
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } up:
                var (id, selection) = _pending!.Value;
                EndTargeting();
                // the window under the pointer: its T_move (the move commands) or T_dest; else the map's system
                var window = Windows.WindowAt(up.Position);
                var local = window is null ? Vector2.Zero : up.Position - window.GetGlobalRect().Position;
                var target = window is not null ? (id is (int)CommandType.Move or (int)CommandType.MoveVariant or (int)CommandType.SetProductionTarget ? window.MoveAt(local) : window.DestAt(local))
                    : _map.HoveredSystem;
                if (id is >= AdvisorMenu.BuildShips and <= AdvisorMenu.BuildFacilities) HelperTarget(target); // the build helper's pick (no command)
                else if (target is not null) Dispatch(id, selection, target);
                else UpdateStatus();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                if (_pending!.Value.Id is >= AdvisorMenu.BuildShips and <= AdvisorMenu.BuildFacilities) _advisor?.HelperPick(_game!, null); // cancelled: the helper resets
                EndTargeting();
                _message = "cancelled";
                UpdateStatus();
                return true;
        }
        return false;
    }

    void EndTargeting()
    {
        _pending = null;
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
        DisplayServer.CursorSetShape(DisplayServer.CursorShape.Arrow);
    }

    /// <summary>Name entry for Rename (own dialog).</summary>
    void TextEntry(string title, System.Action<string> done)
    {
        var edit = new LineEdit { CustomMinimumSize = new Vector2(200, 0) };
        var dialog = new AcceptDialog { Title = title, Exclusive = true };
        dialog.AddChild(edit);
        bool sent = false;
        void Send()
        {
            if (sent || edit.Text == "") return;
            sent = true;
            done(edit.Text);
        }
        dialog.Confirmed += Send;
        edit.TextSubmitted += _ => { Send(); dialog.Hide(); };
        dialog.VisibilityChanged += () => { if (!dialog.Visible) dialog.QueueFree(); };
        GetNode("Hud").AddChild(dialog);
        dialog.PopupCentered();
        edit.GrabFocus();
    }

    /// <summary>
    /// Env OPENSWR_DEMO_MENU = fleet | bombard | target | system | window | alert (advisor menus) | confirm | confirm2: the object menu of the first own fleet (bombard: its bombardment
    /// submenu open; target: Move chosen: targeting state; confirm: Scrap chosen: the confirmation dialog; confirm2: then a Ctrl-drop move's dialog over it) or of its system (the fleets group), at the system's screen position.
    /// </summary>
    void DemoMenu(string mode)
    {
        if (mode is "window" or "alert") // the advisor window / alert menu at its display
        {
            Only(null);
            var r = AdvisorRect(mode == "window" ? 0 : 1);
            OpenAdvisorMenu(r.GetCenter(), mode == "window");
            return;
        }
        var fleet = _game!.Systems.SelectMany(OwnFleets).FirstOrDefault();
        if (fleet?.System is not { } s) return;
        var at = _map.ScreenPosition(s) + new Vector2(6, 4);
        if (mode == "system") { OpenObjectMenu([(s, GroupMode.Fleets)], at); return; } // a system's fleets group (no map menu in the game; demo of the group mode only)
        if (mode == "target") { Dispatch((int)CommandType.Move, [(fleet, GroupMode.Object)]); return; }
        if (mode == "confirm") { Dispatch((int)CommandType.Scrap, [(fleet, GroupMode.Object)]); return; }
        if (mode == "confirm2") // two stacked dialogs: the scrap one under a Ctrl-drop move's transit confirmation
        {
            Dispatch((int)CommandType.Scrap, [(fleet, GroupMode.Object)]);
            Dispatch((int)CommandType.MoveVariant, [(fleet, GroupMode.Object)], _game!.Systems.Where(s => s != fleet.System).MinBy(s => Orders.Distance(fleet.System!, s)));
            return;
        }
        OpenObjectMenu([(fleet, GroupMode.Object)], at);
        if (mode == "bombard") _menu?.Demo(2, openSub: true);
        else _menu?.Demo(0, openSub: false);
    }
}
