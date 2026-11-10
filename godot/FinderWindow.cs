using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data;
using OpenSWR.Data.Dat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Finder windows (System, Fleet, Troop and Personnel; 470 x 330 centred in the map rect,
/// not draggable, no minimise): page picture + frame 10335 / 10336 + right strip, title / label / filter caption, name box with
/// type-ahead (Enter: go to), single-selection list sorted by text (skin 10653; row text grey, selected white), filter radio
/// group, Fleet modes (ships / fleets), Personnel categories (characters / special forces), Close 200 and Display. Rows
/// from <see cref="Finders"/>; double click / Enter / Display: go to (a system: its sector and planet windows, observed
/// F5) and close. Right click on a row: Encyclopedia and Status (observed F4). Refilled on a filter / mode change only.
/// </summary>
public partial class FinderWindow : GameWindow
{
    int _filter, _mode = 1, _category = 3; // Fleet: mode 1 fleets / 2 ships; Personnel: category 3 characters / 2 special forces
    List<FinderRow> _rows = [];
    GameObject? _current;
    readonly List<(int Id, FrontButton Button, int Tip)> _filters = [];
    readonly List<(int Id, FrontButton Button)> _modes = [];
    CellList _list = null!;
    NameField _name = null!;
    bool _typing;

    public FinderWindow(MainScreen screen, WindowKind id) : base(screen, id, new Vector2(470, 330))
    {
        var r = screen.MapRect;
        Position = (r.Position + (r.Size - Size) / 2).Floor(); // centred in the map rect
        _filter = id is WindowKind.TroopFinder or WindowKind.PersonnelFinder ? (screen.Side == SimOwner.Alliance ? 1 : 2) : 1;
    }

    protected override bool Framed => false;
    protected override string Title => "";
    bool Wide => Kind is WindowKind.FleetFinder or WindowKind.PersonnelFinder; // strip 10586 / 10590 and button x 423
    bool Locations => Kind == WindowKind.TroopFinder || Kind == WindowKind.PersonnelFinder && _category == 2;

    static Texture2D? Bmp(int id)
    {
        return id == 0 ? null : Assets.TryBitmap((ushort)id, "STRATEGY.DLL");
    }

    FrontButton Btn(Rect2 r, int up, int down, int tip, Action command)
    {
        var b = new FrontButton { Up = Bmp(up), Down = Bmp(down), Command = command, TooltipText = tip == 0 ? "" : UiFrame.Label(tip) };
        b.CheckedImage = b.Down;
        b.Place((int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y);
        AddChild(b);
        return b;
    }

    public override void _Ready()
    {
        int x = A ? (Wide ? 423 : 427) : 426;
        Btn(A ? new Rect2(x, 25, 32, 31) : new Rect2(426, 21, 44, 41), A ? StrategyBitmaps.FinderCloseAllianceUp : StrategyBitmaps.FinderCloseEmpireUp, A ? StrategyBitmaps.FinderCloseAllianceDown : StrategyBitmaps.FinderCloseEmpireDown, StrategyTexts.Close, () => Screen.Windows.Close(this)).Sound = Assets.Click; // 200
        Btn(A ? new Rect2(x, 93, 32, 31) : new Rect2(426, 89, 44, 41), A ? StrategyBitmaps.DisplayAllianceUp : StrategyBitmaps.DisplayEmpireUp, A ? StrategyBitmaps.DisplayAllianceDown : StrategyBitmaps.DisplayEmpireDown, StrategyTexts.FinderDisplay, Display).Sound = Assets.Click; // display button
        (int Id, int Up, int Down, int Tip)[] filters = Kind switch
        {
            WindowKind.SystemFinder => [(1, StrategyBitmaps.FinderAllUp, StrategyBitmaps.FinderAllDown, StrategyTexts.AllSystems), (2, StrategyBitmaps.FinderAllianceUp, StrategyBitmaps.FinderAllianceDown, StrategyTexts.AllianceSystems), (3, StrategyBitmaps.FinderEmpireUp, StrategyBitmaps.FinderEmpireDown, StrategyTexts.EmpireSystems), (4, StrategyBitmaps.FinderNeutralUp, StrategyBitmaps.FinderNeutralDown, StrategyTexts.NeutralSystems), (5, StrategyBitmaps.FinderUnexploredUp, StrategyBitmaps.FinderUnexploredDown, StrategyTexts.UnexploredSystems)],
            WindowKind.FleetFinder => [(1, StrategyBitmaps.FinderAllUp, StrategyBitmaps.FinderAllDown, 0), (2, StrategyBitmaps.FinderAllianceUp, StrategyBitmaps.FinderAllianceDown, 0), (3, StrategyBitmaps.FinderEmpireUp, StrategyBitmaps.FinderEmpireDown, 0)],
            WindowKind.TroopFinder => [(1, StrategyBitmaps.FinderAllianceUp, StrategyBitmaps.FinderAllianceDown, StrategyTexts.AllianceTroops), (2, StrategyBitmaps.FinderEmpireUp, StrategyBitmaps.FinderEmpireDown, StrategyTexts.EmpireTroops)],
            _ => [(1, StrategyBitmaps.FinderAllianceUp, StrategyBitmaps.FinderAllianceDown, StrategyTexts.AlliancePersonnel), (2, StrategyBitmaps.FinderEmpireUp, StrategyBitmaps.FinderEmpireDown, StrategyTexts.EmpirePersonnel)],
        };
        int fy = Kind is WindowKind.SystemFinder or WindowKind.FleetFinder ? 78 : 72;
        for (int i = 0; i < filters.Length; i++)
        {
            var f = filters[i];
            _filters.Add((f.Id, Btn(new Rect2(36 + 52 * i, fy, 49, 42), f.Up, f.Down, f.Tip, () => SetFilter(f.Id)), f.Tip));
            if (Kind == WindowKind.TroopFinder) _filters[^1].Button.Sound = Assets.Click; // Inferred: the troop finder's "mode buttons"
        }
        // Fleet modes / Personnel categories on the strip; button y 0 and 54 (E second y Inferred the same)
        (int Id, int AUp, int ADown, int EUp, int EDown, int Tip)[] modes = Kind switch
        {
            WindowKind.FleetFinder => [(2, StrategyBitmaps.ShipModeAllianceUp, StrategyBitmaps.ShipModeAllianceDown, StrategyBitmaps.ShipModeEmpireUp, StrategyBitmaps.ShipModeEmpireDown, StrategyTexts.ShipFinderTitle), (1, StrategyBitmaps.FleetModeAllianceUp, StrategyBitmaps.FleetModeAllianceDown, StrategyBitmaps.FleetModeEmpireUp, StrategyBitmaps.FleetModeEmpireDown, StrategyTexts.FleetFinderTitle)],
            WindowKind.PersonnelFinder => [(3, StrategyBitmaps.CharactersCategoryAllianceUp, StrategyBitmaps.CharactersCategoryAllianceDown, StrategyBitmaps.CharactersCategoryEmpireUp, StrategyBitmaps.CharactersCategoryEmpireDown, StrategyTexts.Characters), (2, StrategyBitmaps.SpecialForcesCategoryAllianceUp, StrategyBitmaps.SpecialForcesCategoryAllianceDown, StrategyBitmaps.SpecialForcesCategoryEmpireUp, StrategyBitmaps.SpecialForcesCategoryEmpireDown, StrategyTexts.SpecialForces)],
            _ => [],
        };
        for (int i = 0; i < modes.Length; i++)
        {
            var m = modes[i];
            var r = A ? new Rect2(423, 147 + 54 * i, 32, 31) : new Rect2(426, 143 + 54 * i, Kind == WindowKind.FleetFinder ? 44 : 42, 41);
            _modes.Add((m.Id, Btn(r, A ? m.AUp : m.EUp, A ? m.ADown : m.EDown, m.Tip, () => SetMode(m.Id))));
            if (Kind == WindowKind.FleetFinder) _modes[^1].Button.Sound = Assets.Click; // fleet finder mode buttons
        }
        var list = Kind switch { WindowKind.TroopFinder => new Rect2(37, 144, 349, 159), WindowKind.PersonnelFinder => new Rect2(37, 143, 349, 161), _ => new Rect2(36, 138, 350, 165) };
        _list = new CellList(StrategyRcData.PanelScrollSkin) { Single = true, Cell = new Vector2(330, Kind == WindowKind.TroopFinder ? 25 : 20), Position = list.Position, Size = list.Size, DrawCell = DrawRow };
        _list.Pressed = () => Screen.Windows.Focus(this);
        _list.SelectionChanged = Selected;
        _list.Activated = i => GoTo(_rows[i].Key);
        _list.ContextMenu = RowMenu;
        AddChild(_list);
        _name = new NameField(new Vector2(143, 45), new Vector2(250, Kind == WindowKind.FleetFinder ? 16 : 18))
        {
            TextChanged = TypeAhead, Submitted = () => { if (_current is { } c) GoTo(c); }, OtherKey = NameKey,
        };
        AddChild(_name);
        Fill();
        _name.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Filter radio (command 100): refill (nothing for the same filter), then type-ahead with the name box text.</summary>
    void SetFilter(int id)
    {
        if (id == _filter) { Fill(keep: true); return; }
        _filter = id;
        Fill();
        TypeAhead(_name.Text);
    }

    /// <summary>Fleet mode change: name box cleared, page switched, refilled with the filter kept; Personnel category likewise (Inferred).</summary>
    void SetMode(int id)
    {
        if (Kind == WindowKind.FleetFinder) { if (_mode == id) { Fill(keep: true); return; } _mode = id; }
        else { if (_category == id) { Fill(keep: true); return; } _category = id; }
        _name.Clear();
        _current = null;
        Fill();
    }

    void Fill(bool keep = false)
    {
        string N(GameObject o) => Screen.ObjectName(o, title: false);
        var side = Side;
        var s = _filter == 1 ? SimOwner.Alliance : SimOwner.Empire;
        _rows = Kind switch
        {
            WindowKind.SystemFinder => Finders.Systems(Game, side, _filter, N),
            WindowKind.FleetFinder => Finders.Fleets(Game, side, _mode == 2, _filter, N),
            WindowKind.TroopFinder => Finders.Locations(Game, side, s, Families.Troop, Families.CapitalShip, Finders.TroopTypes[_filter - 1], N),
            _ when _category == 2 => Finders.Locations(Game, side, s, Families.SpecialForce, Families.Mission, Finders.SpecialForceTypes[_filter - 1], N),
            _ => Finders.Characters(Game, side, s, N, Assets.Stra),
        };
        _list.Count = _rows.Count;
        if (!keep) { _list.Selected.Clear(); _list.First = 0; }
        foreach (var (id, b, _) in _filters) b.Checked = id == _filter;
        foreach (var (id, b) in _modes) b.Checked = id == (Kind == WindowKind.FleetFinder ? _mode : _category);
        QueueRedraw();
    }

    /// <summary>
    /// text changed notification / filter re-selection: current key := the type-ahead row (none for an empty text or list); the row is selected
    /// and scrolled into view unless already selected; the select posts no command notification (the name box text stays).
    /// </summary>
    void TypeAhead(string text)
    {
        int i = Finders.TypeAhead(_rows, text);
        _current = i < 0 ? null : _rows[i].Key;
        _typing = true;
        if (i < 0) { _list.Selected.Clear(); _list.QueueRedraw(); }
        else if (!_list.Selected.Contains(i)) _list.Select(i);
        _typing = false;
    }

    /// <summary>Row click (command notification): current key := the row's; the name box shows the row's name (Personnel: only when the view is Existing).</summary>
    void Selected()
    {
        if (_list.Selected.Count == 0 || _typing) return;
        var row = _rows[_list.Selected.Min];
        _current = row.Key;
        if (Kind == WindowKind.PersonnelFinder && !ScreenViews.View(Game, row.Key, Side).Existing) return;
        string name = Screen.ObjectName(row.Key);
        if (_name.Text != name) _name.SetText(name);
    }

    /// <summary>Display: go to the current key and close; nothing without one (Fleet Finder: closes anyway).</summary>
    void Display()
    {
        if (_current is { } c) GoTo(c);
        else if (Kind == WindowKind.FleetFinder) Screen.Windows.Close(this);
    }

    /// <summary>Go to: troop / special-force rows: the location's first unit; a system: its sector and planet windows (F5).</summary>
    void GoTo(GameObject o)
    {
        if (Locations) o = Finders.FirstUnit(Game, o, Side, Kind == WindowKind.TroopFinder ? Families.Troop : Families.SpecialForce, Kind == WindowKind.TroopFinder ? Families.CapitalShip : Families.Mission) ?? o;
        Screen.Windows.Close(this);
        Screen.GoTo(o, planet: true);
    }

    /// <summary>Right click on a row (observed F4): Encyclopedia and Status (Status disabled for systems).</summary>
    void RowMenu(Vector2 at)
    {
        if (_list.Selected.Count == 0) return;
        var key = _rows[_list.Selected.Min].Key;
        var rows = OpenSWR.Data.UiRcData.BuildMenu([((int)ObjectCommands.Encyclopedia, true, false), ((int)ObjectCommands.Status, key is not StarSystem, false)], Assets.Menu);
        Screen.OpenMenuAt(rows, at, id => Screen.Dispatch(id, [(key, GroupMode.Object)]));
    }

    bool NameKey(InputEventKey k)
    {
        if (k.Keycode == Key.Down && _rows.Count > 0) { _list.GrabFocus(); if (_list.Selected.Count == 0) _list.Select(0); return true; }
        if (k.Keycode == Key.Escape) { Screen.Windows.Close(this); return true; }
        return false;
    }

    /// <summary>Keys: Left / Right previous / next filter (Right wraps, Left stops at the first), Up = name box, Down = list; Esc closes (manager).</summary>
    public override bool HandleKey(InputEventKey key)
    {
        int i = _filters.FindIndex(f => f.Id == _filter);
        switch (key.Keycode)
        {
            case Key.Right: SetFilter(_filters[(i + 1) % _filters.Count].Id); return true;
            case Key.Left: SetFilter(_filters[Math.Max(0, i - 1)].Id); return true;
            case Key.Up when _list.Selected.Count == 0 || _list.Selected.Min == 0: _name.GrabFocus(); return true;
            case Key.Up or Key.Down when _rows.Count > 0:
                _list.Select(Math.Clamp((_list.Selected.Count == 0 ? -1 : _list.Selected.Min) + (key.Keycode == Key.Up ? -1 : 1), 0, _rows.Count - 1));
                return true;
            case Key.Enter or Key.KpEnter when _current is { } c: GoTo(c); return true;
            case Key.Tab: return true;
        }
        return false;
    }

    /// <summary>Env demo: Fleet Finder ships mode / Personnel special forces; then the first row selected.</summary>
    public void Demo(string mode)
    {
        if (mode == "ships") SetMode(2);
        if (mode == "specforces") SetMode(2);
        if (_rows.Count > 0) _list.Select(0);
    }

    /// <summary>Env demo: <paramref name="text"/> typed into the name box (type-ahead runs).</summary>
    public void DemoType(string text)
    {
        _name.Clear();
        foreach (char c in text) _name.Type(c);
        TypeAhead(_name.Text);
    }

    (int Picture, int Title, int Label) Page => Kind switch
    {
        WindowKind.SystemFinder => (StrategyBitmaps.SystemFinderPage, StrategyTexts.SystemFinderTitle, StrategyTexts.SystemFinderLabel),
        WindowKind.FleetFinder => _mode == 2 ? (A ? StrategyBitmaps.ShipFinderPageAlliance : StrategyBitmaps.ShipFinderPageEmpire, StrategyTexts.ShipFinderTitle, StrategyTexts.ShipFinderLabel) : (A ? StrategyBitmaps.FleetFinderPageAlliance : StrategyBitmaps.FleetFinderPageEmpire, StrategyTexts.FleetFinderTitle, StrategyTexts.FleetFinderLabel),
        WindowKind.TroopFinder => (_filter == 1 ? StrategyBitmaps.TroopFinderPageAlliance : StrategyBitmaps.TroopFinderPageEmpire, StrategyTexts.TroopFinderTitle, StrategyTexts.TroopFinderLabel),
        _ => (_category == 3 ? (_filter == 1 ? StrategyBitmaps.CharactersPageAlliance : StrategyBitmaps.CharactersPageEmpire) : (_filter == 1 ? StrategyBitmaps.SpecialForcesPageAlliance : StrategyBitmaps.SpecialForcesPageEmpire), StrategyTexts.PersonnelFinderTitle, StrategyTexts.PersonnelFinderLabel),
    };

    /// <summary>Filter caption: the selected filter's tooltip (Fleet Finder: 6274-6276 fleets / 6279-6281 ships).</summary>
    string Caption => Kind == WindowKind.FleetFinder ? Assets.Stra((_mode == 2 ? StrategyTexts.AllShips : StrategyTexts.AllFleets) + _filter - 1) : Assets.Stra(_filters.FirstOrDefault(f => f.Id == _filter).Tip);

    public override void _Draw()
    {
        var (picture, title, label) = Page;
        if (Bmp(picture) is { } p) { DrawTextureRect(p, new Rect2(12, 13, 400, 306), false); DrawLog.Bitmap(this, p, new Rect2(12, 13, 400, 306)); }
        var frame = Bmp(A ? StrategyBitmaps.PanelFrameAlliance : StrategyBitmaps.PanelFrameEmpire)!;
        DrawTextureRect(frame, new Rect2(0, 0, 470, 331), false);
        DrawLog.Bitmap(this, frame, new Rect2(0, 0, 470, 331));
        var strip = Bmp(Wide ? (A ? StrategyBitmaps.WideFinderStripAlliance : StrategyBitmaps.WideFinderStripEmpire) : (A ? StrategyBitmaps.FinderStripAlliance : StrategyBitmaps.FinderStripEmpire))!;
        DrawTextureRect(strip, new Rect2(412, 0, 58, 330), false);
        DrawLog.Bitmap(this, strip, new Rect2(412, 0, 58, 330));
        // text items: title fixed width 350 centred, height measured; label measured, top-left at (36, 48)
        Assets.DrawTextItem(this, new Rect2(36, 14, 350, 0), Assets.Stra(title), 5, Colors.White, TextFormat.Centred, new TextItemSize(FixedWidth: true));
        Assets.DrawTextItem(this, new Rect2(36, 48, 0, 0), Assets.Stra(label), 4, Colors.White, TextFormat.TopLeft, new TextItemSize());
        Assets.DrawText(this, new Rect2(40, 119, 283, 16), Caption, 0, Colors.White, TextFormat.Row);
    }

    /// <summary>Row (no marker: the Inferred 10365 / 10366 row markers dropped, openswr): text grey RGB (120, 120, 120) or white when selected, font 10 (Inferred); location rows add the count cells.</summary>
    void DrawRow(CellList list, int i, Rect2 r)
    {
        var row = _rows[i];
        bool selected = list.Selected.Contains(i);
        float textWidth = row.Counts is null ? r.Size.X - 4 : (Kind == WindowKind.TroopFinder ? 194 : 222) - 4;
        Assets.DrawCellText(list, new Rect2(r.Position.X + 2, r.Position.Y, textWidth, r.Size.Y), row.Text, 10, selected ? Colors.White : Color.Color8(120, 120, 120), TextFormat.Row);
        if (row.Counts is not { } counts) return;
        int x0 = Kind == WindowKind.TroopFinder ? 194 : 222;
        bool one = _filter == 1;
        for (int c = 0; c < counts.Length; c++)
        {
            var cell = new Rect2(r.Position.X + x0 + 28 * c, r.Position.Y + 2, 21, 16);
            if (counts[c] == 0)
            {
                Assets.DrawText(list, cell, "-", 10, Colors.White, TextFormat.CentredRow);
                continue;
            }
            bool usable = row.Usable![c];
            var fill = one ? (usable ? Color.Color8(255, 0, 0) : Color.Color8(128, 0, 0)) : (usable ? Color.Color8(0, 255, 0) : Color.Color8(0, 128, 0));
            list.DrawRect(cell, fill);
            DrawLog.Fill(list, cell, fill);
            Assets.DrawText(list, cell, counts[c].ToString(), 10, one ? Colors.White : Colors.Black, TextFormat.CentredRow);
        }
    }
}

/// <summary>
/// Finder name box (`CoolStringField`) over <see cref="StringField"/>: no font set, so the stock
/// System font; no frame, no fill (the page shows through). Paint = the w x h window from x = s of the whole text
/// drawn at x 0: plainly (unfocused or no selection) the black shadow at (+1, +1), then the text white, left on a single
/// line, vertically centred in h - 1, "&amp;" literal; while focused with a selection: the text before it shadowed, the selected characters
/// unshadowed black on white glyph cells (text extent x tmHeight, vertically centred; Inferred from GDI's opaque mode), the rest
/// shadowed as a list row (not modelled: its prefix processing, an "&amp;" would underline the next character). Caret = the
/// system caret: 2 px x the control height at (X - s, 0), shown by inverting the pixels under it, blinking at
/// <see cref="StringField.BlinkTime"/>, only while focused (Inferred: a caret move restarts the blink with the caret shown).
/// Keys: Home / End / Left / Right (Shift: selection), Backspace / Delete, Insert (overwrite), Enter: <see cref="Submitted"/>,
/// characters: <see cref="TextChanged"/>; other keys go to the owner (not accepted). Mouse: press = caret /
/// selection start, drag = selection, double click = word.
/// </summary>
public partial class NameField : Control
{
    readonly StringField _field;
    readonly ColorRect _caret = new() { MouseFilter = MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = ZoomOutline.Invert } };
    readonly int _blink = StringField.BlinkTime();
    double _phase;
    int _caretX = int.MinValue;
    public Action<string>? TextChanged { get; set; }
    public Action? Submitted { get; set; }
    /// <summary>Keys the field does not take; they go to the owner. True = taken.</summary>
    public Func<InputEventKey, bool>? OtherKey { get; set; }
    /// <summary>Typed characters the field takes (null = every printable one); a refused one goes to <see cref="OtherKey"/>.</summary>
    public Func<char, bool>? Accept { get; set; }
    /// <summary>The black shadow under the text (finder name boxes); off = plain white.</summary>
    public bool Shadow { get; set; } = true;

    public NameField(Vector2 position, Vector2 size, int font = TextLayout.SystemFont, int limit = 0)
    {
        Position = position;
        Size = size;
        _field = new StringField(font, (int)size.X, limit);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        _caret.Size = new Vector2(2, size.Y);
        AddChild(_caret);
        FocusEntered += () => { _field.Focus(); Changed(); }; // WM_SETFOCUS: scroll rule
        FocusExited += () => { _field.MouseUp(); QueueRedraw(); }; // WM_KILLFOCUS: selecting off, the selection kept but not drawn
    }

    public string Text => _field.Text;

    /// <summary>Text set (no text changed notification): caret -1 (the end); s and the selection kept.</summary>
    public void SetText(string text)
    {
        _field.SetText(text);
        Changed();
    }

    /// <summary>Clear (no text changed notification): empty, caret -1, s := 0.</summary>
    public void Clear()
    {
        _field.Clear();
        Changed();
    }

    /// <summary>A typed character (WM_CHAR; the demo path), without the text changed notification.</summary>
    public void Type(char c)
    {
        _field.Type(c);
        Changed();
    }

    /// <summary>Repaint; the blink restarts with the caret shown only when its x changed (SetCaretPos to a new place, Inferred).</summary>
    void Changed()
    {
        if (_field.CaretX != _caretX) (_phase, _caretX) = (0, _field.CaretX);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _phase += delta * 1000;
        _caret.Visible = HasFocus() && (_blink == 0 || (int)(_phase / _blink) % 2 == 0);
        _caret.Position = new Vector2(_field.CaretX, 0);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } b:
                if (b.Pressed)
                {
                    GrabFocus();
                    if (b.DoubleClick) _field.DoubleClick();
                    else _field.MouseDown(b.Position.X);
                }
                else _field.MouseUp();
                break;
            case InputEventMouseMotion m:
                _field.MouseMove(m.Position.X, Input.IsMouseButtonPressed(MouseButton.Left));
                break;
            case InputEventKey { Pressed: true } k:
                if (!Key(k))
                {
                    if (OtherKey?.Invoke(k) == true) AcceptEvent();
                    return;
                }
                break;
            default:
                return;
        }
        Changed();
        AcceptEvent();
    }

    bool Key(InputEventKey k)
    {
        StringField.Keys? key = k.Keycode switch
        {
            Godot.Key.Home => StringField.Keys.Home, Godot.Key.End => StringField.Keys.End, Godot.Key.Left => StringField.Keys.Left,
            Godot.Key.Right => StringField.Keys.Right, Godot.Key.Backspace => StringField.Keys.Backspace, Godot.Key.Delete => StringField.Keys.Delete,
            Godot.Key.Insert => StringField.Keys.Insert, _ => null,
        };
        if (key is { } c)
        {
            if (_field.Key(c, k.ShiftPressed)) TextChanged?.Invoke(_field.Text);
            return true;
        }
        if (k.Keycode is Godot.Key.Enter or Godot.Key.KpEnter)
        {
            Submitted?.Invoke();
            return true;
        }
        if (k.Unicode is > 0 and <= 65535 && StringField.Typed((char)k.Unicode, k.CtrlPressed, k.AltPressed) && Accept?.Invoke((char)k.Unicode) != false) // WM_CHAR: printable only
        {
            if (_field.Type((char)k.Unicode)) TextChanged?.Invoke(_field.Text);
            return true;
        }
        return false;
    }

    public override void _Draw()
    {
        int font = _field.Font;
        string text = _field.Text;
        float s = _field.Scroll, h = Size.Y - 1;
        int all = _field.X(text.Length);
        // the whole text at x 0 of a bitmap at least 2 * (text width + 1) wide, the control showing x = s ... s + w
        void Shadowed(float x, string part, TextFormat format)
        {
            var r = new Rect2(x - s, 0, Math.Max(all, Size.X) + 2, h);
            if (Shadow) Assets.DrawText(this, new Rect2(r.Position + Vector2.One, r.Size), part, font, Colors.Black, format with { NoClip = true });
            Assets.DrawText(this, r, part, font, Colors.White, format with { NoClip = true });
        }
        if (!HasFocus() || !_field.HasSelection)
        {
            Shadowed(0, text, new TextFormat { VCenter = true, SingleLine = true, NoPrefix = true });
            return;
        }
        var (a, b) = _field.Selection;
        float xa = _field.X(a), xb = _field.X(b), line = Assets.LineHeight(font);
        Shadowed(0, text[..a], new TextFormat { VCenter = true, SingleLine = true, NoPrefix = true });
        DrawRect(new Rect2(xa - s, (int)(h - line) / 2, xb - xa, line), Colors.White);
        DrawLog.Fill(this, new Rect2(xa - s, (int)(h - line) / 2, xb - xa, line), Colors.White);
        Assets.DrawText(this, new Rect2(xa - s, 0, xb - xa, h), text[a..b], font, Colors.Black, new TextFormat { VCenter = true, SingleLine = true, NoClip = true, NoPrefix = true });
        Shadowed(xb, text[b..], TextFormat.Row);
    }
}
