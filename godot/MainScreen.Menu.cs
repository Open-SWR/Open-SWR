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
    /// object's menu (mode 0): the selection's command list, greyed by validation (<see cref="ObjectCommands"/>), plus
    /// Encyclopedia and Status. A right click on the galaxy map itself does nothing.
    /// </summary>
    void RightClick(Vector2 at)
    {
        if (AdvisorRect(0).HasPoint(at)) OpenAdvisorMenu(at, window: true);
        else if (AdvisorRect(1).HasPoint(at)) OpenAdvisorMenu(at, window: false);
        else if (SpeedRect.HasPoint(at)) OpenSpeedMenu(at);
    }

    /// <summary>Advisor displays (rects Inferred from the argument order): hotspot 0xDEAD on the first, 0xBEEF on the second.</summary>
    Rect2 AdvisorRect(int i) => (_side == 0, i) switch
    {
        (true, 0) => new(541, 337, 67, 116), (true, _) => new(316, 411, 47, 69), (false, 0) => new(0, 347, 107, 133), _ => new(302, 401, 101, 79),
    };

    // alert rows 0x171-0x179: category mask
    static readonly Dictionary<int, int> AlertRows = new() { [0x171] = 1, [0x172] = 0x80, [0x173] = 0x10, [0x174] = 4, [0x175] = 8, [0x176] = 0x40, [0x177] = 0x100, [0x178] = 0x200, [0x179] = 0x20 };

    /// <summary>
    /// Window menu (0xDEAD: 0x110-0x116, 0x11B, 0x11E) / alert menu (0xBEEF: 0x11C, 0x171-0x179 checked = alert on);
    /// rows enabled / checked by the advisor. Opening either menu is the advisor's menu click.
    /// </summary>
    void OpenAdvisorMenu(Vector2 at, bool window)
    {
        if (_game is null) return;
        _advisor?.MenuOpen(); // opening either menu is the advisor's menu click
        IEnumerable<(int, bool, bool)> rows = window
            ? new[] { 0x110, 0x111, 0x112, 0x113, 0x114, 0x115, 0x116, 0x11B, 0x11E }.Select(id => _advisor?.Row(id) is { } r ? (id, r.Enabled, r.Checked) : (id, id is 0x113 or 0x114, false))
            : [(0x11C, true, false), .. AlertRows.Select(r => (r.Key, true, !AlertSilent(r.Value)))];
        OpenMenu(new GameMenu(OpenSWR.Data.UiRcData.BuildMenu(rows, Assets.Menu), at, SideOne, AdvisorCommand));
    }

    /// <summary>Advisor menu commands: 0x113 Galaxy Overview / 0x114 Objectives = the status window's side / objectives pages, 0x11C messages, 0x171-0x179 alert toggles.</summary>
    void AdvisorCommand(int id)
    {
        var game = _game!;
        switch (id)
        {
            case 0x113:
                OpenStatus(null, overview: true);
                break;
            case 0x114:
                OpenStatus(null);
                break;
            case 0x11C:
                OpenMessages(0x79);
                break;
            case 0x110 or 0x111 or 0x112 when _advisor is not null: // build helper: targeting state without a command
                _advisor.HelperStart(id);
                _pending = (id, []);
                Input.SetDefaultCursorShape(Input.CursorShape.Cross);
                DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
                _message = $"{RowLabel(id)}: click the target (Esc / right click: cancel)";
                UpdateStatus();
                break;
            case 0x115 or 0x116 or 0x11B or 0x11E:
                _advisor?.Toggle(id); // 0x115 / 0x116: the record runs in the delegation cycle
                break;
            case var a when AlertRows.TryGetValue(a, out int mask):
                ToggleAlert(mask);
                break;
        }
    }

    internal void OpenObjectMenu(List<(GameObject Object, int Mode)> selection, Vector2 at)
    {
        var rows = ObjectCommands.Rows(_game!, _map.Side, selection);
        var built = OpenSWR.Data.UiRcData.BuildMenu(rows.Select(r => (r.Id, r.Enabled, r.Checked)), Assets.Menu);
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

    static string RowLabel(int id) => Assets.Menu(id) is { } m ? UiFrame.Label(m.Label) : $"{id:X}";

    /// <summary>
    /// Menu command (UI dispatcher): Encyclopedia / Status (the status window); 0x201 / 0x202 / 0x214 /
    /// 0x240 without a target: targeting state; 0x203 / 0x215: name entry; 0x210-0x212: the production window;
    /// 0x240 with a target: the mission window; everything else (0x270 with its system): confirmation
    /// when required, then validate and submit.
    /// </summary>
    internal void Dispatch(int id, List<(GameObject Object, int Mode)> selection, GameObject? target = null, bool confirm = true)
    {
        var first = selection[0].Object;
        switch (id)
        {
            case ObjectCommands.Encyclopedia:
                OpenEncyclopedia(first, 0);
                break;
            case ObjectCommands.Status:
                OpenStatus(first);
                break;
            case 0x201 or 0x202 or 0x214 or 0x240 when target is null:
                _pending = (id, selection);
                Input.SetDefaultCursorShape(Input.CursorShape.Cross); // REBEXE group cursor 1002 (GameCursors)
                DisplayServer.CursorSetShape(DisplayServer.CursorShape.Cross);
                _message = $"{RowLabel(id)}: click the target (Shift: normal click; Esc / right click: cancel)";
                break;
            case 0x203 or 0x215:
                TextEntry(RowLabel(id), name => Execute(id, selection, null, name));
                return;
            case >= 0x210 and <= 0x212:
                OpenProduction(ObjectCommands.Build(id, _map.Side, selection));
                break;
            case 0x240:
                OpenMission(selection, target!);
                break;
            default:
                var command = ObjectCommands.Build(id, _map.Side, selection, target);
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
    /// Confirmation body: scrap 0x7050 / retire 0x7051 + one 0x7054 line (line
    /// feed + name) per target of the command (key order, Inferred); stop construction 0x7053 + one 0x7055 line per manager (line
    /// feed, queue count, two spaces, the ProductKey definition's name, empty without one); abort 0x7052; moves 0x7056 (blockade
    /// row) / 0x7057, then per moved object a line feed, its name, ":  " and its days (<see cref="ScreenViews.Transit"/>).
    /// </summary>
    string ConfirmText(Command c, IReadOnlyList<(GameObject Object, int Mode)> selection)
    {
        // 0x200 walks the resolved target set (group modes expanded), 0x242 / 0x213 the raw target list; key order (Inferred)
        var targets = c.Type == 0x200 ? Orders.Targets(c) : c.Targets.Select(t => t.Target).OrderBy(o => o.Ref).ToList();
        string Product(ManufacturingManager m) =>
            m.Orders.FirstOrDefault() is { } head && _game!.Definitions.Resolve(head.Type) is { } d ? _map.DisplayName(d.Common) : "";
        return c.Type switch
        {
            0x200 or 0x242 => Assets.Message(c.Type == 0x200 ? 0x7050 : 0x7051) + string.Concat(targets.Select(o => Assets.Message(0x7054, ObjectName(o)))),
            0x213 => Assets.Message(0x7053) + string.Concat(targets.OfType<ManufacturingManager>().Select(m =>
                Assets.Message(0x7055, (System.Func<uint, string>)(kind => kind == 6 ? m.Orders.Count.ToString() : kind == 5 ? Product(m) : "")))),
            0x201 or 0x202 => Assets.Message(ConfirmWindow.Blockaded(_game!, _map.Side, selection) ? 0x7056 : 0x7057)
                + string.Concat(ScreenViews.Transit(_game!, c).Select(t => $"\n{ObjectName(t.Object)}:  {t.Days}")),
            _ => Assets.Message(0x7052),
        };
    }

    /// <summary>Validate, then submit and dispatch at once; a fleet's bombardment / assault opens the result window.</summary>
    void Execute(int id, List<(GameObject Object, int Mode)> selection, GameObject? target, string? text = null)
    {
        if (id is 0x234 or (>= 0x220 and <= 0x223) && selection is [(Fleet fleet, 0)] && target is null)
        {
            Strike(fleet, id);
            return;
        }
        Submit(ObjectCommands.Build(id, _map.Side, selection, target, text));
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
        _message = result is { Success: true } ? $"{RowLabel(command.Type)}: done" : $"{RowLabel(command.Type)}: refused ({result?.Category:X}, {result?.Code:X})";
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
                // the window under the pointer: its T_move (0x201 / 0x202 / 0x214) or T_dest; else the map's system
                var window = Windows.WindowAt(up.Position);
                var local = window is null ? Vector2.Zero : up.Position - window.GetGlobalRect().Position;
                var target = window is not null ? (id is 0x201 or 0x202 or 0x214 ? window.MoveAt(local) : window.DestAt(local))
                    : _map.HoveredSystem;
                if (id is >= 0x110 and <= 0x112) HelperTarget(target); // the build helper's pick (no command)
                else if (target is not null) Dispatch(id, selection, target);
                else UpdateStatus();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false }:
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                if (_pending!.Value.Id is >= 0x110 and <= 0x112) _advisor?.HelperPick(_game!, null); // cancelled: the helper resets
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
        if (mode == "system") { OpenObjectMenu([(s, 0x10)], at); return; } // a system's fleets group (no map menu in the game; demo of the group mode only)
        if (mode == "target") { Dispatch(0x201, [(fleet, 0)]); return; }
        if (mode == "confirm") { Dispatch(0x200, [(fleet, 0)]); return; }
        if (mode == "confirm2") // two stacked dialogs: the scrap one under a Ctrl-drop move's transit confirmation
        {
            Dispatch(0x200, [(fleet, 0)]);
            Dispatch(0x202, [(fleet, 0)], _game!.Systems.Where(s => s != fleet.System).MinBy(s => Orders.Distance(fleet.System!, s)));
            return;
        }
        OpenObjectMenu([(fleet, 0)], at);
        if (mode == "bombard") _menu?.Demo(2, openSub: true);
        else _menu?.Demo(0, openSub: false);
    }
}
