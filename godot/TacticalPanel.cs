using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenSWR.Data;
using OpenSWR.Sim;

/// <summary>
/// The right panel of the tactical screen: child (482, 28) 149 x 236, one page at a time (0x15 blank,
/// 0x16 ship, 0x17 fighter group, 0x18 task-force ship, 0x19 missions, 0x1A maneuvers &amp; tactics, 0x1B game options, 0x1C withdraw
/// confirmation) on its background bitmap; texts, bars, system icons, the officer strip with the TACTICAL RCDATA 1350 horizontal
/// scroll bar, buttons with TEXTTACT tooltips. Page and object come from <see cref="TacticalUi.Page"/>; the controls are rebuilt when
/// they change. Text font and colours Inferred (Arial 8 px, the own side's label colour).
/// </summary>
public partial class TacticalPanel : Control
{
    readonly TacticalScreen _s;
    int _page = -1;
    object? _object;
    int _mission, _tactic;
    readonly List<HudButton> _radios = [];
    HudButton? _tacticToggle, _orders1, _orders2, _withdraw, _immediate, _observe;

    public TacticalPanel(TacticalScreen screen)
    {
        _s = screen;
        Position = new Vector2(482, 28);
        Size = new Vector2(149, 236);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>A TACTICAL bitmap; none when the install lacks it (every id used here exists in the original TACTICAL.DLL).</summary>
    static Texture2D? Bmp(int id)
    {
        return Assets.TryBitmap((ushort)id, TacticalScreen.Dll);
    }
    TacticalUi Ui => _s.Ui;

    HudButton Button(int x, int y, int w, int h, int up, int down, int tooltip, Action? command = null, int? disabled = null)
    {
        var (u, dn) = (Bmp(up), Bmp(down));
        var dis = disabled is { } d ? Bmp(d) : null;
        var b = new HudButton { Up = u, Down = dn, CheckedImage = dn, DisabledImage = dis, Command = command, TooltipText = tooltip == 0 ? "" : Assets.Tip(TacticalScreen.Text(tooltip)), FocusMode = FocusModeEnum.None };
        b.Place(x, y, w, h);
        AddChild(b);
        return b;
    }

    void Tip(int x, int y, int w, int h, int tooltip) =>
        AddChild(new Control { Position = new Vector2(x, y), Size = new Vector2(w, h), TooltipText = Assets.Tip(TacticalScreen.Text(tooltip)), MouseFilter = MouseFilterEnum.Pass });

    /// <summary>The unit a ship page shows and the group whose orders a page edits (the unit's UI task force, or the fighter group).</summary>
    TacticalUnit? Unit => _object as TacticalUnit;
    TacticalGroup? Group => _object as TacticalGroup ?? Unit?.UiGroup;
    /// <summary>The page drawn: page 0x1C while it shows as a child window of page 0x1B (the withdraw confirmation is not a page switch), else the UI page.</summary>
    int Shown => Ui.WithdrawShown ? 0x1C : Ui.Page;
    int Side => _object switch { TacticalUnit u => u.Side, TacticalGroup g => g.Side, _ => _s.Battle.Local };

    public void Refresh()
    {
        if (Shown != _page || Ui.PageObject != _object) Build();
        bool orders = Ui.OrdersEnabled(Side);
        if (_orders1 is not null) _orders1.Disabled = !orders;
        if (_orders2 is not null) _orders2.Disabled = !orders;
        if (_withdraw is not null) _withdraw.Disabled = !_s.Battle.RetreatPossible(_s.Battle.Local);
        if (_immediate is not null) _immediate.Disabled = !Ui.ImmediateEnabled;
        if (_observe is not null)
        {
            _observe.Disabled = _s.Battle.SideState(_s.Battle.Local) != 0;
            _observe.Checked = !Ui.InCommand; // Inferred: checked while observing
            _observe.TooltipText = Assets.Tip(TacticalScreen.Text(Ui.InCommand ? 56764 : 56813));
        }
        for (int i = 0; i < _radios.Count; i++) _radios[i].Checked = (int)_radios[i].GetMeta("value") == _mission;
        if (_tacticToggle is not null)
        {
            _tacticToggle.Checked = _tactic == 1;
            _tacticToggle.TooltipText = Assets.Tip(TacticalScreen.Text(_tactic == 1 ? 56782 : 56783));
        }
        QueueRedraw();
    }

    /// <summary>The page's controls, rebuilt on each page switch.</summary>
    void Build()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        _radios.Clear();
        _tacticToggle = _orders1 = _orders2 = _withdraw = _immediate = _observe = null;
        (_page, _object) = (Shown, Ui.PageObject);
        _mission = Group?.UiMission ?? 0;
        _tactic = Group?.Tactic ?? 2;
        switch (_page)
        {
            case 0x16 or 0x18:
                bool tf = _page == 0x18;
                Button(7, 10, 11, 53, 1101, 1102, tf ? 56705 : 56708, () => Ui.Cycle(false));
                Button(132, 10, 11, 53, 1103, 1104, tf ? 56706 : 56709, () => Ui.Cycle(true));
                Tip(34, 81, 37, 10, 56730);
                Tip(103, 81, 37, 10, 56731);
                for (int i = 0; i < 5; i++) Tip(10 + 27 * i, 103, 22, 15, 56789 + i);
                OrderButtons();
                break;
            case 0x17:
                OrderButtons();
                break;
            case 0x19:
                bool a = _s.Battle.Local == 0;
                bool fighters = Group?.Fighters == true;
                // ids checked: every id exists in TACTICAL.DLL
                Radio(18, 129, 46, 26, a ? 1179 : 1182, a ? 1180 : 1183, 5, true, null); // 0xDD03 Attack Capital Ships
                Radio(86, 129, 46, 26, a ? 1170 : 1173, a ? 1171 : 1174, 2, fighters, a ? 1172 : 1175); // 0xDD05 Recover
                Radio(18, 159, 46, 26, a ? 1191 : 1194, a ? 1192 : 1195, 4, true, null); // 0xDD04 Attack Fighters
                // 0xDD06 Attack Death Star: an Alliance player's fighter group while the attack is possible
                Radio(86, 159, 46, 26, 1176, 1177, 6, a && fighters && _s.Battle.AttackPossible, 1178);
                Button(66, 202, 27, 25, 1113, 1114, 56736, () => { int m = _mission; Ui.ClosePage(); Ui.AssignMission(m); });
                Button(106, 202, 27, 25, 1115, 1116, 56737, Ui.ClosePage);
                break;
            case 0x1A:
                Radio(13, 159, 28, 28, 1133, 1134, 7, true, null, 56784);
                Radio(43, 159, 28, 28, 1135, 1136, 8, true, null, 56785);
                Radio(13, 129, 28, 28, 1137, 1138, 9, true, null, 56786);
                Radio(43, 129, 28, 28, 1139, 1140, 10, true, null, 56787);
                Radio(75, 144, 28, 28, 1141, 1142, 11, true, null, 56788);
                _tacticToggle = Button(111, 132, 24, 52, 1117, 1118, 56783, () => _tactic = _tactic == 1 ? 2 : 1);
                int oldMission = _mission, oldTactic = _tactic;
                Button(66, 202, 27, 25, 1113, 1114, 56761, () =>
                {
                    int m = _mission, t = _tactic;
                    Ui.ClosePage();
                    if (m != oldMission) Ui.AssignMission(m);
                    if (t != oldTactic) Ui.AssignTactic(t);
                });
                Button(106, 202, 27, 25, 1115, 1116, 56737, Ui.ClosePage);
                // a maneuver radio starts checked only when the current UI mission is one (Inferred)
                if (_mission is < 7 or > 11) _mission = 0;
                break;
            case 0x1B:
                _withdraw = Button(18, 19, 48, 34, 1149, 1150, 56762, Ui.WithdrawPressed);
                _immediate = Button(14, 93, 44, 25, 1151, 1152, 56763, () => Ui.Command(0x7C));
                _observe = Button(87, 93, 45, 25, 1153, 1154, 56764, () => Ui.Command(0x7B));
                Button(23, 184, 52, 33, 1155, 1156, 56765, () =>
                {
                    if (!_s.Battle.Paused) _s.TogglePause();
                    _s.OptionsRequested?.Invoke();
                });
                Button(97, 184, 29, 33, 1157, 1158, 56766, () => { Ui.ShowPage(0x15); Ui.Command(0x7D); });
                break;
            case 0x1C:
                Button(66, 202, 27, 25, 1113, 1114, 56770, () => Ui.WithdrawAnswer(true));
                Button(106, 202, 27, 25, 1115, 1116, 56737, () => Ui.WithdrawAnswer(false));
                break;
        }
    }

    /// <summary>"Maneuvers &amp; Tactics" (12, 210): page 0x1A and "Missions" (80, 210): page 0x19. The tooltip is TEXTTACT 56781. Its "&amp;" is stripped, so the original shows "Maneuvers  Tactics" with two spaces (Inferred). TEXTTACT 44473 has the same text and seems unused.</summary>
    void OrderButtons()
    {
        _orders1 = Button(12, 210, 58, 22, 1107, 1108, 56781, () => Ui.ShowPage(0x1A, _object));
        _orders2 = Button(80, 210, 58, 22, 1105, 1106, 56716, () => Ui.ShowPage(0x19, _object));
    }

    void Radio(int x, int y, int w, int h, int up, int down, int value, bool enabled, int? disabled, int tooltip = 0)
    {
        var b = Button(x, y, w, h, up, down, tooltip, () => _mission = value, disabled);
        b.Disabled = !enabled;
        b.SetMeta("value", value);
        _radios.Add(b);
    }

    /// <summary>
    /// Right-panel texts: white, font 8 on pages 0x16-0x18, font 10 on page 0x1C. Text items are clipped and not fixed in size, so each is measured on
    /// every text set and drawn whole from the top-left at (x, y). The w x h rect is not used. A centred item loses its centring and starts at x - 1.
    /// </summary>
    void Text(int x, int y, int w, int h, string text, TextFormat format = default, int font = 8) =>
        Assets.DrawTextItem(this, new Rect2(x, y, w, h), text, font, Colors.White, format, new TextItemSize(Clipped: true));

    static string T(int id) => TacticalScreen.Text(id);

    string Line(List<object> parts) => string.Join(" ", parts.Select(p => p is int id ? T(id) : p is TacticalUnit u ? _s.Label(u.Object) : ""));

    /// <summary>A 0-100 bar (fill colour Inferred).</summary>
    void Bar(int x, int y, int w, int h, double value)
    {
        var (r, c) = (new Rect2(x, y, (float)(w * Math.Clamp(value, 0, 100) / 100), h), new Color(0.2f, 0.85f, 0.2f));
        DrawRect(r, c);
        DrawLog.Fill(this, r, c);
    }

    public override void _Draw()
    {
        int bg = _page switch { 0x16 or 0x18 => 1302, 0x17 => 1307, 0x19 => 1308, 0x1A => 1309, 0x1B => 1306, 0x1C => 1310, _ => _s.Battle.Local == 0 ? 1303 : 1304 };
        DrawTextureRect(Bmp(bg), new Rect2(0, 0, 149, 236), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(bg), new Rect2(0, 0, 149, 236));
        switch (_page)
        {
            case 0x16 or 0x18 when Unit is { } u: DrawShip(u); break;
            case 0x17 when _object is TacticalGroup g: DrawFighters(g); break;
            case 0x19: DrawDiagram(_s.Battle.Local == 0 ? _mission switch { 5 => 2151, 2 => 2152, 4 => 2153, 6 => 2154, _ => 2158 } : _mission switch { 5 => 2155, 2 => 2156, 4 => 2157, _ => 2158 }); break; // order of 2151-2157 Inferred
            case 0x1A: if (_mission is >= 7 and <= 11) DrawDiagram(2101 + _mission - 7); break;
            case 0x1C:
                // two text items: format 0 (the line feed in 56771 gives two lines, no wrap). Each rect is the measured text, so nothing is cut.
                Text(20, 28, 0, 0, T(56772), TextFormat.TopLeft, 10);
                Text(14, 92, 0, 0, T(56771), TextFormat.TopLeft, 10);
                break;
        }
    }

    void DrawDiagram(int id) { DrawTextureRect(Bmp(id), new Rect2(14, 14, 124, 106), false); if (DrawLog.On) DrawLog.Bitmap(this, Bmp(id), new Rect2(14, 14, 124, 106)); }

    /// <summary>
    /// Ship page (0x16 / 0x18): picture 2001 + type index (96 x 39) at (26, 17); name, task-force line, orders and tactics lines; hull and
    /// shield bars; the five system icons (bases 1201, 1206, 1211, 1216, 1221 + level); the officer strip with the 1350 scroll bar.
    /// </summary>
    void DrawShip(TacticalUnit u)
    {
        if (!u.Fighter) DrawTextureRect(Bmp(2001 + u.TypeIndex), new Rect2(26, 17, 96, 39), false);
        if (DrawLog.On && !u.Fighter) DrawLog.Bitmap(this, Bmp(2001 + u.TypeIndex), new Rect2(26, 17, 96, 39));
        Text(26, 7, 97, 9, _s.Label(u.Object));
        int slot = Ui.SlotOf(u.UiGroup);
        Text(26, 52, 97, 9, slot >= 0 ? T(56732) + (slot + 1) : T(56735));
        Bar(34, 81, 37, 10, u.HullMax > 0 ? 100 * u.Hull / u.HullMax : 0);
        Bar(103, 81, 37, 10, u.ShieldMax > 0 ? 100 * u.Shield / u.ShieldMax : 0);
        int[] bases = [1201, 1206, 1211, 1216, 1221];
        var levels = Ui.SystemLevels(u);
        for (int i = 0; i < 5; i++) DrawTextureRect(Bmp(bases[i] + levels[i]), new Rect2(10 + 27 * i, 103, 22, 15), false);
        if (DrawLog.On) for (int i = 0; i < 5; i++) DrawLog.Bitmap(this, Bmp(bases[i] + levels[i]), new Rect2(10 + 27 * i, 103, 22, 15));
        OfficerStrip(u);
        Text(15, 182, 121, 9, Line(Ui.OrdersLine(u.Side, u.UiGroup, u.Group, u)));
        if (u.Side == _s.Battle.Local) Text(15, 193, 121, 9, T(56794).TrimEnd() + " " + T((u.UiGroup?.Tactic ?? 2) == 1 ? 56796 : 56795));
    }

    /// <summary>
    /// Officer strip (13, 126) 124 x 45 with the horizontal scroll bar of skin TACTICAL RCDATA 1350 at its bottom (thickness word 0;
    /// geometry Inferred): the unit's officer records, each its TACTICAL icon (the record's picture number: bitmap; none: not
    /// drawn) left to right at its own size, as many as fit (scrolling Inferred, not built).
    /// </summary>
    void OfficerStrip(TacticalUnit u)
    {
        if (Assets.Module(TacticalScreen.Dll).Find(ResId.RcData, 1350) is not { } r) return;
        var k = ScrollSkin.Decode(r.Data.Span);
        float y = 126 + 45 - k.Thickness, x0 = 13, x1 = 13 + 124;
        float at = x0;
        foreach (var o in Ui.Officers(u))
        {
            int icon = _s.Definitions.Resolve(o.Type) is { } rec ? TacticalUi.OfficerIcon(ModArt.PictureNumber(rec.Common.StringId)) : 0;
            if (icon == 0 || Bmp(icon) is not { } tex) continue;
            var size = Assets.BitmapSize((ushort)icon, TacticalScreen.Dll);
            if (at + size.X > x1) break;
            DrawTextureRect(tex, new Rect2(at, 126, size.X, Math.Min(size.Y, 45 - k.Thickness)), false);
            DrawLog.Bitmap(this, tex, new Rect2(at, 126, size.X, Math.Min(size.Y, 45 - k.Thickness)));
            at += size.X;
        }
        Vector2 Dim(ushort id) => Assets.BitmapSize(id, TacticalScreen.Dll);
        float left = Dim(k.Up).X, right = Dim(k.Down).X;
        var track = Bmp(k.Track);
        for (float x = x0 + left; x < x1 - right; x += Dim(k.Track).X)
        {
            float part = Math.Min(Dim(k.Track).X, x1 - right - x);
            DrawTextureRectRegion(track, new Rect2(x, y, part, k.Thickness), new Rect2(0, 0, track.GetWidth() * part / Dim(k.Track).X, track.GetHeight()));
            if (DrawLog.On) DrawLog.Bitmap(this, track, new Rect2(x, y, part, k.Thickness), new Rect2(0, 0, part, Dim(k.Track).Y));
        }
        DrawTextureRect(Bmp(k.Up), new Rect2(x0, y, left, k.Thickness), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(k.Up), new Rect2(x0, y, left, k.Thickness));
        DrawTextureRect(Bmp(k.Down), new Rect2(x1 - right, y, right, k.Thickness), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(k.Down), new Rect2(x1 - right, y, right, k.Thickness));
    }

    /// <summary>Fighter page (0x17): name 56712 + c, orders and tactics lines, picture 2001 + the first type's index (102 x 107), the count line of that type.</summary>
    void DrawFighters(TacticalGroup g)
    {
        Text(26, 9, 97, 9, T(56712 + Math.Max(0, g.Colour)), TextFormat.Centred);
        var first = g.UiMembers.FirstOrDefault();
        Text(20, 20, 102, 9, Line(Ui.OrdersLine(g.Side, g, g, first)));
        Text(20, 31, 102, 9, T(56794).TrimEnd() + " " + T(g.Tactic == 1 ? 56796 : 56795));
        if (first is null) return;
        DrawTextureRect(Bmp(2001 + first.TypeIndex), new Rect2(23, 59, 102, 107), false);
        if (DrawLog.On) DrawLog.Bitmap(this, Bmp(2001 + first.TypeIndex), new Rect2(23, 59, 102, 107));
        int label = first.TypeIndex is >= 0x1D and <= 0x24 ? 56773 + first.TypeIndex - 0x1D : 56777;
        Text(23, 146, 102, 9, $"{T(label)} {g.UiMembers.Count(u => u.Object.Type == first.Object.Type && u.Active)}", TextFormat.Centred);
    }
}
