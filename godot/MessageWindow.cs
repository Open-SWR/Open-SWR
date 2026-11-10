using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>Message texts: TEXTSTRA RCDATA templates with the stored arguments (names; sides by placeholder kind, <see cref="Assets.Side"/>).</summary>
public static class MessageFormat
{
    static object Arg(MessageArg a) => a.Side is SimOwner.Alliance or SimOwner.Empire ? Assets.Side(a.Side == SimOwner.Alliance) : a.Text ?? "";

    public static string Text(MessageText? t) => t is null ? "" : Assets.Message(t.Template, [.. t.Args.Select(Arg)]);

    /// <summary>The title (free text for adaptations and older entries).</summary>
    public static string Title(GameMessage m) => m.Text ?? Text(m.Title);

    public static string Body(GameMessage m) => m.Text ?? string.Concat((m.Body ?? []).Select(Text));
}

/// <summary>
/// Message window (window id 0xD, 470 x 331 centred over the map rect: A (62, 49), E (125, 52)): list mode (tabs
/// 0x79-0x82, list of the stored messages of the tab's category newest first, title only (observed in the original), bold (font 13) until
/// read, category icons, Select All / Delete, Display, alert toggle, go-to / encyclopedia) and display mode (picture 1000-1075
/// or an object picture, body in a text box with skin 10653, Previous / Next, Yes / No for kind 5, go-to). Tab exit clears the
/// tab category's new bits; the Advice tab runs the game at speed 1 while shown. Compose / chat (multiplayer) is left
/// out: 0x68 always disabled.
/// </summary>
public partial class MessageWindow : GameWindow
{
    static readonly int[] TabX = [0, 38, 76, 114, 151, 189, 227, 263, 301, 338];
    static readonly int[] TabTips = [32784, 32785, 32791, 32788, 32786, 32787, 32790, 32792, 32789, 32818];
    // (up, down) per tab 0x79-0x82, A then E
    static readonly (int, int, int, int)[] TabImages =
    [
        (10830, 10831, 10830, 10831), (10832, 10833, 10834, 10835), (10840, 10841, 10838, 10839), (10842, 10843, 10844, 10845),
        (10836, 10837, 10836, 10837), (10852, 10853, 10852, 10853), (10856, 10857, 10856, 10857), (10854, 10855, 10854, 10855),
        (10850, 10851, 10850, 10851), (10846, 10847, 10848, 10849),
    ];

    int _tab, _mode = 1; // 1 list, 2 display
    List<GameMessage> _list = [];
    GameMessage? _shown;
    readonly List<FrontButton> _tabs = [];
    FrontButton _display = null!, _alert = null!, _goTo = null!, _compose = null!, _all = null!, _delete = null!, _prev = null!, _next = null!, _yes = null!, _no = null!;
    CellList _rows = null!;
    TextBox _body = null!;

    public MessageWindow(MainScreen screen, int tab) : base(screen, 0xD, new Vector2(470, 331))
    {
        _tab = tab is >= 0x79 and <= 0x82 ? tab : 0x79;
        Position = screen.Side == SimOwner.Alliance ? new Vector2(62, 49) : new Vector2(125, 52);
    }

    protected override bool Framed => false;
    protected override string Title => "";
    int Mask => MessageCategory.OfTab(_tab);

    static Texture2D? Bmp(int id, string module = "STRATEGY.DLL")
    {
        if (id == 0) return null;
        return Assets.TryBitmap((ushort)id, module);
    }

    FrontButton Btn(Rect2 r, int up, int down, int disabled, int tip, Action command)
    {
        var b = new FrontButton { Up = Bmp(up), Down = Bmp(down), DisabledImage = Bmp(disabled), Command = command, TooltipText = tip == 0 ? "" : UiFrame.Label(tip) };
        b.Place((int)r.Position.X, (int)r.Position.Y, (int)r.Size.X, (int)r.Size.Y);
        AddChild(b);
        return b;
    }

    Rect2 Strip(int ax, int ay, int ex, int ey) => A ? new Rect2(ax, ay, 32, 31) : new Rect2(ex, ey, 44, 41);

    public override void _Ready()
    {
        for (int i = 0; i < 10; i++)
        {
            int tab = 0x79 + i;
            var (au, ad, eu, ed) = TabImages[i];
            var b = Btn(new Rect2(22 + TabX[i], 46, i == 9 ? 37 : 36, 41), A ? au : eu, A ? ad : ed, 0, TabTips[i], () => SelectTab(tab));
            b.CheckedImage = b.Down;
            _tabs.Add(b);
        }
        var close = Btn(Strip(423, 25, 426, 21), A ? 10370 : 10376, A ? 10371 : 10377, 0, 6484, () => Screen.Windows.Close(this));      // 0x28
        _display = Btn(Strip(423, 93, 426, 89), 0, 0, 0, 0, DisplayOrIndex);                                                             // 0x65
        _alert = Btn(Strip(423, 147, 426, 148), 0, 0, 0, 0, ToggleAlert);                                                                // 0x66
        _goTo = Btn(Strip(423, 201, 426, 207), 0, 0, A ? 10959 : 10960, 0, GoTo);                                                         // 0x67
        _compose = Btn(Strip(423, 255, 426, 266), A ? 10876 : 10879, A ? 10877 : 10880, A ? 10878 : 10881, 32802, () => { });            // 0x68
        _compose.Disabled = true; // single player
        foreach (var b in new[] { close, _display, _alert, _goTo, _compose }) b.Sound = Assets.Click; // 0x28, 0x65-0x68 click; tabs silent
        _all = Btn(new Rect2(282, 87, 56, 20), 10900, 10901, 0, 32776, () => { for (int i = 0; i < _list.Count; i++) _rows.Selected.Add(i); _rows.QueueRedraw(); Update(); });
        _delete = Btn(new Rect2(340, 87, 56, 20), 10902, 10903, 0, 32777, Delete);
        _prev = Btn(new Rect2(367, 15, 19, 15), 10948, 10949, 10950, 0, () => Step(-1));                                                // 0x9A
        _next = Btn(new Rect2(390, 15, 19, 15), 10919, 10920, 10921, 0, () => Step(1));                                                 // 0x96
        _yes = Btn(new Rect2(355, 244, 51, 35), 10926, 10927, 0, 0, () => Answer(true));
        _no = Btn(new Rect2(355, 281, 51, 35), 10929, 10930, 0, 0, () => Answer(false));
        _rows = new CellList(10653) { Cell = new Vector2(346, 21), Position = new Vector2(25, 108), Size = new Vector2(368, 194), DrawCell = DrawRow };
        _rows.Pressed = () => Screen.Windows.Focus(this);
        _rows.SelectionChanged = Update;
        _rows.Activated = _ => DisplayOrIndex();
        AddChild(_rows);
        _body = new TextBox { Position = new Vector2(17, 234), Size = new Vector2(395, 80) };
        AddChild(_body);
        SelectTab(_tab, exit: false);
    }

    /// <summary>Tab click: tab exit for the old tab, then the list for the new one; Advice runs the game at speed 1.</summary>
    void SelectTab(int tab, bool exit = true)
    {
        if (exit && tab == _tab) return;
        if (exit) Screen.MessageTabExit(_tab);
        if (_tab == 0x82 && tab != 0x82) Screen.RestoreAdviceSpeed();
        if (tab == 0x82 && (!exit || _tab != 0x82)) Screen.AdviceSpeed();
        _tab = tab;
        ShowList();
    }

    void ShowList()
    {
        _mode = 1;
        _shown = null;
        _list = Messages.List(Game, Mask);
        _rows.Count = _list.Count;
        _rows.Selected.Clear();
        _rows.First = 0;
        if (_list.Count > 0) _rows.Selected.Add(0); // the first row when nothing is selected
        Update();
    }

    /// <summary>Control states per mode and selection.</summary>
    void Update()
    {
        bool list = _mode == 1;
        foreach (var t in _tabs) t.Visible = list;
        for (int i = 0; i < _tabs.Count; i++) _tabs[i].Checked = 0x79 + i == _tab;
        (_rows.Visible, _all.Visible, _delete.Visible, _alert.Visible) = (list, list, list, list);
        (_body.Visible, _prev.Visible, _next.Visible) = (!list, !list, !list);
        if (list)
        {
            (_display.Up, _display.Down, _display.DisabledImage) = (Bmp(A ? 10870 : 10738), Bmp(A ? 10871 : 10739), Bmp(A ? 10961 : 10962));
            _display.TooltipText = UiFrame.Label(32800);
            _display.Disabled = _list.Count == 0;
        }
        else
        {
            (_display.Up, _display.Down) = (Bmp(A ? 10372 : 10378), Bmp(A ? 10373 : 10379));
            _display.TooltipText = UiFrame.Label(32793);
            _display.Disabled = false;
        }
        bool silent = Screen.AlertSilent(Mask);
        (_alert.Up, _alert.Down) = silent ? (Bmp(A ? 10951 : 10953), Bmp(A ? 10873 : 10875)) : (Bmp(A ? 10872 : 10874), Bmp(A ? 10873 : 10875));
        _alert.TooltipText = UiFrame.Label(silent ? 32774 : 32773);
        // 0x67: one selected (or shown) message: kind 3, or 5 with a game running: go-to; kind 4: encyclopedia; else disabled
        var m = Current;
        bool ency = m?.Kind == 4;
        (_goTo.Up, _goTo.Down) = ency ? (Bmp(A ? 11552 : 11591), Bmp(A ? 11553 : 11592)) : (Bmp(A ? 10518 : 10520), Bmp(A ? 10519 : 10521));
        _goTo.TooltipText = UiFrame.Label(ency ? 32817 : 32816);
        _goTo.Disabled = m is null || m.Kind is not (3 or 4 or 5) || m.Kind != 4 && m.Subject == 0;
        int index = m is null ? -1 : _list.IndexOf(m);
        _prev.Disabled = index <= 0;
        _next.Disabled = index < 0 || index >= _list.Count - 1;
        bool question = !list && m?.Kind == 5;
        (_yes.Visible, _no.Visible) = (question, question);
        _body.Size = new Vector2(question ? 320 : 395, 80);
        _rows.QueueRedraw();
        QueueRedraw();
    }

    GameMessage? Current => _mode == 2 ? _shown : _rows.Selected.Count == 1 ? _list.ElementAtOrDefault(_rows.Selected.Min) : null;

    /// <summary>0x65: list to display (the first selected row); display to list (rebuilt, tab kept). Leaving a displayed message marks it read.</summary>
    void DisplayOrIndex()
    {
        if (_mode == 2)
        {
            Leave();
            ShowList();
            return;
        }
        if (_rows.Selected.Count == 0) return;
        Show(_list[_rows.Selected.Min]);
    }

    /// <summary>
    /// Display: a type-0xF message (kind 3 as well as 7, the test is on the type; observed in the original: the defender's too)
    /// skips display mode: the window remembers the message and its tab, closes, and the strike window opens; its OK / Esc reopen
    /// the list on that tab.
    /// </summary>
    static bool OpensStrike(GameMessage m) => m.Type == 0xF;

    void Show(GameMessage m)
    {
        if (OpensStrike(m))
        {
            int tab = _tab;
            m.Read = true;
            var screen = Screen;
            screen.Windows.Close(this);
            screen.OpenStrike(m, () => screen.OpenMessages(tab));
            return;
        }
        _mode = 2;
        _shown = m;
        _body.Text = MessageFormat.Body(m);
        Screen.PlayMessage(m); // sound (STRATEGY) + voice (sprite DLL, module 9), the previous message's stopped
        Update();
    }

    void Leave()
    {
        if (_shown is null) return;
        _shown.Read = true; // only the tab exit clears new
    }

    /// <summary>0x96 / 0x9A: the next (older, down the list) / previous (newer) row (direction Inferred).</summary>
    void Step(int dir)
    {
        if (_shown is null) return;
        int i = _list.IndexOf(_shown) + dir;
        if (i < 0 || i >= _list.Count) return;
        Leave();
        Show(_list[i]);
    }

    /// <summary>0x91: list mode deletes every selected message and selects the row after the last deleted; display mode the shown one, then the next (else previous; none, then list).</summary>
    void Delete()
    {
        if (_mode == 2 && _shown is { } m)
        {
            int i = _list.IndexOf(m);
            Game.Log.Remove(m);
            _list.Remove(m);
            if (_list.Count == 0) { ShowList(); return; }
            Show(_list[Math.Min(i, _list.Count - 1)]);
            return;
        }
        if (_rows.Selected.Count == 0) return;
        int last = _rows.Selected.Max - _rows.Selected.Count + 1;
        foreach (var i in _rows.Selected.Reverse()) Game.Log.Remove(_list[i]);
        _list = Messages.List(Game, Mask);
        _rows.Count = _list.Count;
        _rows.Selected.Clear();
        if (_list.Count > 0) _rows.Selected.Add(Math.Clamp(last, 0, _list.Count - 1));
        Update();
    }

    void ToggleAlert()
    {
        Screen.ToggleAlert(Mask);
        Update();
    }

    /// <summary>0x67: kind 3 / 5 goes to the subject, kind 4 opens the encyclopedia; then the window closes.</summary>
    void GoTo()
    {
        if (Current is not { } m) return;
        Leave();
        if (m.Kind == 4) Screen.OpenEncyclopedia(Messages.Find(Game, m.Encyclopedia), m.Encyclopedia);
        else if (Messages.Find(Game, m.Subject) is { } o) Screen.GoTo(o);
        Screen.Windows.Close(this);
    }

    /// <summary>0x98 Yes: the mission continues; 0x99 No: the 0x250 abort for the mission while it is in the player's view; both back to the list.</summary>
    void Answer(bool yes)
    {
        if (!yes && _shown is { } m && Messages.Find(Game, m.Mission) is Mission mission && !mission.Destroyed)
            Screen.Dispatch(0x250, [(mission, 0)], confirm: false);
        Leave();
        ShowList();
    }

    /// <summary>Close (0x28, Esc): the displayed message read, speed restored, tab exit; the manager then destroys the window and recomputes.</summary>
    public void Closing()
    {
        Leave();
        if (_tab == 0x82) Screen.RestoreAdviceSpeed();
        Screen.MessageTabExit(_tab);
    }

    public override bool HandleKey(InputEventKey key)
    {
        switch (key.Keycode)
        {
            case Key.I when key.AltPressed: // 0xBBE: display to list
                if (_mode == 2) DisplayOrIndex();
                return true;
            case Key.Enter or Key.KpEnter when _mode == 1:
                DisplayOrIndex();
                return true;
            case Key.Up or Key.Down when _mode == 1 && _list.Count > 0:
                _rows.Select(Math.Clamp((_rows.Selected.Count == 0 ? 0 : _rows.Selected.Min) + (key.Keycode == Key.Up ? -1 : 1), 0, _list.Count - 1));
                return true;
            case Key.Left or Key.Right when _mode == 2:
                Step(key.Keycode == Key.Left ? -1 : 1);
                return true;
        }
        return false;
    }

    /// <summary>Row icon of a message category (none for an event or no category); A / E variants.</summary>
    int RowIcon(int category) => category switch
    {
        MessageCategory.Support => A ? 10906 : 10907,
        MessageCategory.Defense => 10963,
        MessageCategory.Manufacturing => A ? 10908 : 10909,
        MessageCategory.Mission => A ? 10910 : 10911,
        MessageCategory.Chat => 10916,
        MessageCategory.Fleet => A ? 10964 : 10965,
        MessageCategory.Resource => 10966,
        MessageCategory.Conflict => 10967,
        MessageCategory.Advice => A ? 10968 : 10969,
        _ => 0,
    };

    static readonly Dictionary<(int Icon, int Bar), (Texture2D Texture, Vector2 Size)?> RowPictures = [];

    /// <summary>
    /// Row picture N (<paramref name="bar"/> 0) or S (bar 10914 / 10915) of an icon (<see cref="OpenSWR.Data.MessageRowPicture"/>),
    /// drawn keyed on index 0 like the list's cell blit; logical size = the picture's.
    /// </summary>
    static (Texture2D Texture, Vector2 Size)? RowPicture(int icon, int bar)
    {
        if (RowPictures.TryGetValue((icon, bar), out var p)) return p;
        OpenSWR.Data.IndexedBitmap? Load(int id) => id != 0 && Assets.Module("STRATEGY.DLL").Find(OpenSWR.Data.ResId.Bitmap, (ushort)id) is { } r
            ? OpenSWR.Data.IndexedBitmap.FromDib(r.Data.Span) : null;
        if (Load(icon) is not { } i) return RowPictures[(icon, bar)] = null;
        var b = OpenSWR.Data.MessageRowPicture.Build(i, Load(bar));
        var image = Image.CreateFromData(b.Width, b.Height, false, Image.Format.Rgba8, OpenSWR.Data.IndexedBitmap.ToRgba(b.Pixels, b.Palette, 0));
        var texture = ImageTexture.CreateFromImage(image);
        if (DrawLog.On) DrawLog.Tag(texture, $"rowpic:{icon}:{bar}", 0);
        return RowPictures[(icon, bar)] = (texture, new Vector2(b.Width, b.Height));
    }

    /// <summary>
    /// Row: picture S on a selected row (the bar unscaled 356 x 21 at the cell origin, its shadow strip and corners not drawn,
    /// the icon at (1, 1) with its black transparent), else N (the icon at (1, 1), 14 columns); the panel art under the list shows
    /// through elsewhere. Text from (33, 1) by the cell-list rule (left, top, right - 3, clipped), font 13 unread / 10 read,
    /// RGB(120, 120, 120), white when selected.
    /// </summary>
    void DrawRow(CellList list, int i, Rect2 r)
    {
        var m = _list[i];
        bool selected = list.Selected.Contains(i);
        if (RowPicture(RowIcon(m.Category), selected ? (A ? 10914 : 10915) : 0) is var (t, size)) { list.DrawTextureRect(t, new Rect2(r.Position, size), false); DrawLog.Bitmap(list, t, new Rect2(r.Position, size)); }
        Assets.DrawCellText(list, new Rect2(r.Position.X + 32, r.Position.Y + 1, r.Size.X, r.Size.Y), MessageFormat.Title(m), m.Read ? 10 : 13,
            selected ? Colors.White : Color.Color8(120, 120, 120), TextFormat.TopLeft);
    }

    public override void _Draw()
    {
        if (_mode == 1)
        {
            var page = Bmp(10823)!;
            DrawTextureRect(page, new Rect2(12, 13, 400, 306), false);
            DrawLog.Bitmap(this, page, new Rect2(12, 13, 400, 306));
        }
        else
        {
            DrawRect(new Rect2(12, 13, 400, 306), Colors.Black);
            DrawLog.Fill(this, new Rect2(12, 13, 400, 306), Colors.Black);
            var (top, bottom) = (Bmp(10933)!, Bmp(10932)!);
            DrawTextureRect(top, new Rect2(12, 14, 400, 18), false);
            DrawLog.Bitmap(this, top, new Rect2(12, 14, 400, 18));
            DrawTextureRect(bottom, new Rect2(12, 232, 400, 87), false);
            DrawLog.Bitmap(this, bottom, new Rect2(12, 232, 400, 87));
            if (_shown is { } m)
            {
                // The picture comes first (STRATEGY, else EData through ENCYBMAP); only when it loads, the overlay
                // centred on it (colour key not applied, Inferred: the 400 x 200 overlays cover the picture in practice)
                if (Assets.ObjectPicture(m.Picture) is var (pic, _))
                {
                    DrawTextureRect(pic, new Rect2(12, 33, 400, 200), false);
                    DrawLog.Bitmap(this, pic, new Rect2(12, 33, 400, 200));
                    if (Assets.ObjectPicture(m.Overlay) is var (over, size)) CellList.Centred(this, over, size, new Rect2(12, 33, 400, 200));
                }
                Assets.DrawText(this, new Rect2(16, 14, 345, 18), MessageFormat.Title(m), 10, Colors.White, TextFormat.Row);
            }
        }
        var (frame, strip) = (Bmp(A ? 10335 : 10336)!, Bmp(A ? 10820 : 10821)!);
        DrawTextureRect(frame, new Rect2(0, 0, 470, 331), false);
        DrawLog.Bitmap(this, frame, new Rect2(0, 0, 470, 331));
        DrawTextureRect(strip, new Rect2(412, 0, 58, 330), false);
        DrawLog.Bitmap(this, strip, new Rect2(412, 0, 58, 330));
    }

    /// <summary>Env demo: show the first message of the list (display mode).</summary>
    public void DemoDisplay()
    {
        if (_list.Count > 0) Show(_list[0]);
    }
}

/// <summary>
/// Read-only text box = the original's text scroll field (message body 0xA0,
/// confirmation body): white text laid out by <see cref="OpenSWR.Data.TextLayout.ScrollField"/> (own word wrap from the left
/// edge, only whole lines). The scroll bar (skin 10653) is hidden while the text fits the full width; the scroll position is the
/// character index of the top line: a step down moves it to the next laid-out line and pushes the old one, a step up pops it.
/// </summary>
public partial class TextBox : Control
{
    readonly OpenSWR.Data.ScrollSkin? _skin = Assets.Skin(10653);
    string _text = "";
    int _top;
    readonly Stack<int> _tops = [];
    public string Text { get => _text; set { _text = value; _top = 0; _tops.Clear(); QueueRedraw(); } }

    readonly int _font;

    /// <summary>White text in font <paramref name="font"/> (the message window's 10; the confirmation dialog's 4).</summary>
    public TextBox(int font = 10)
    {
        _font = font;
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    OpenSWR.Data.TextLayout.ScrollLayout Layout(int top) => OpenSWR.Data.TextLayout.ScrollField(_font, _text, (int)Size.X, (int)Size.Y, _skin?.Thickness ?? 0, top);

    float? _grab; // thumb drag: mouse y minus thumb top

    Rect2 Bounds => new(Vector2.Zero, Size);

    // bar in pixels: thumb = the measured height of the text above the top line at the wrap width, page = the field height,
    // range = the whole text's height
    (int First, int Visible, int Total) Bar(OpenSWR.Data.TextLayout.ScrollLayout layout) =>
        (OpenSWR.Data.TextLayout.ThumbPosition(_font, _text, layout.Width, (int)Size.Y, _top), (int)Size.Y, OpenSWR.Data.TextLayout.ScrollRange(_font, _text, layout.Width, _top).Range);

    // one step up (pop) or down (push the old top) while lines remain below the shown ones (the next line's first character)
    bool Step(int dir)
    {
        if (dir < 0 && _tops.Count > 0) _top = _tops.Pop();
        else if (dir > 0 && Layout(_top) is var layout && layout.Lines.Count > layout.Visible && layout.Starts.Count > 1)
        {
            _tops.Push(_top);
            _top = layout.Starts[1];
        }
        else return false;
        return true;
    }

    void ScrollTo(int first) => OpenSWR.Data.TextLayout.ScrollTo(_font, _text, (int)Size.X, (int)Size.Y, _skin?.Thickness ?? 0, first, ref _top, _tops);

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) { _grab = null; return; }
        var layout = Layout(_top);
        var (first, visible, total) = Bar(layout);
        bool bar = layout.Scrolls && total > visible && _skin is not null;
        if (e is InputEventMouseMotion m && _grab is { } grab && bar && Input.IsMouseButtonPressed(MouseButton.Left))
        {
            ScrollTo(ScrollBar.FirstAt(_skin!, Bounds, m.Position.Y - grab, visible, total));
            QueueRedraw();
            AcceptEvent();
            return;
        }
        if (e is not InputEventMouseButton { Pressed: true } b || !layout.Scrolls) return;
        int dir = b.ButtonIndex switch
        {
            MouseButton.WheelUp => -1, MouseButton.WheelDown => 1,
            MouseButton.Left when bar => ScrollBar.Button(_skin!, Bounds, b.Position), _ => 0,
        };
        if (dir != 0) Step(dir);
        else if (b.ButtonIndex == MouseButton.Left && bar && ScrollBar.InBar(_skin!, Bounds, b.Position))
        {
            var (y0, len) = ScrollBar.Thumb(_skin!, Bounds, first, visible, total);
            if (b.Position.Y < y0) ScrollTo(first - visible); // track above / below the thumb: a page
            else if (b.Position.Y >= y0 + len) ScrollTo(first + visible);
            else _grab = b.Position.Y - y0;
        }
        else return;
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        var layout = Layout(_top);
        int h = Assets.LineHeight(_font);
        for (int i = 0; i < layout.Visible && i < layout.Lines.Count; i++)
            foreach (var (x, word) in layout.Lines[i])
                Assets.DrawText(this, new Rect2(x, i * h, Assets.TextWidth(_font, word), h), word, _font, Colors.White, new TextFormat { SingleLine = true, NoClip = true, NoPrefix = true }); // TextOut: "&" literal
        if (layout.Scrolls && _skin is { } k && Bar(layout) is var (first, visible, total))
            ScrollBar.Draw(this, k, Bounds, first, visible, total, 0);
    }
}
