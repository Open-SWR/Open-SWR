using System;
using System.Collections.Generic;
using Godot;
using OpenSWR.Sim;

/// <summary>
/// The fixed controls of the tactical frame: labels, task-force
/// buttons 1-8, fighter buttons Red / Blue / Green / Gold, nav-point sets 1-4, highlight Empire / Alliance, pause, game options and the
/// camera buttons (zoom, rotate and tilt repeat every 100 ms while held), TACTICAL bitmaps and TEXTTACT tooltips; the Death Star panel
/// for an Empire player with a Death Star. Button states follow the UI controller every frame.
/// </summary>
public partial class TacticalHud : Control
{
    readonly TacticalScreen _s;
    readonly HudButton[] _taskForces = new HudButton[8], _fighters = new HudButton[4], _navs = new HudButton[4];
    readonly HudButton _highlightEmpire, _highlightAlliance, _pause, _superlaser;
    readonly Control _deathStar = new() { Position = new Vector2(474, 0), Size = new Vector2(166, 25), Visible = false, MouseFilter = MouseFilterEnum.Ignore };

    /// <summary>A TACTICAL bitmap; none when this install lacks it (1184 / 1185 of the Attack Death Star radio are absent).</summary>
    static Texture2D? Bmp(int id)
    {
        return Assets.TryBitmap((ushort)id, TacticalScreen.Dll);
    }

    HudButton Button(Control parent, int x, int y, int w, int h, int up, int down, int tooltip, Action? command = null, int? disabled = null, bool toggle = false)
    {
        var b = new HudButton { Up = Bmp(up), Down = Bmp(down), CheckedImage = Bmp(down), DisabledImage = disabled is { } d ? Bmp(d) : null, Toggle = toggle, Command = command, TooltipText = Assets.Tip(TacticalScreen.Text(tooltip)), FocusMode = FocusModeEnum.None };
        b.Place(x, y, w, h);
        parent.AddChild(b);
        return b;
    }

    /// <summary>A camera button: the operation at press, repeated every 100 ms while held when <paramref name="repeat"/>.</summary>
    void Camera(int x, int y, int w, int h, int up, int down, int tooltip, int op, bool repeat)
    {
        var b = Button(this, x, y, w, h, up, down, tooltip);
        if (repeat) { b.Pressed = () => _s.Hold(op); b.Released = _s.Release; }
        else b.Command = () => _s.Operate(op);
    }

    public TacticalHud(TacticalScreen screen)
    {
        _s = screen;
        Size = new Vector2(640, 480);
        MouseFilter = MouseFilterEnum.Ignore;
        var ui = screen.Ui;
        bool alliance = screen.Battle.Local == 0;
        AddChild(new TextureRect { Texture = Bmp(alliance ? 1001 : 1004), Position = new Vector2(9, 1), Size = new Vector2(49, 19), TooltipText = Assets.Tip(TacticalScreen.Text(44433)), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });
        AddChild(new TextureRect { Texture = Bmp(alliance ? 1008 : 1010), Position = new Vector2(296, 1), Size = new Vector2(49, 19), TooltipText = Assets.Tip(TacticalScreen.Text(44443)), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });
        for (int i = 0; i < 8; i++)
        {
            int slot = i;
            // images and caption per slot state are set in Refresh
            _taskForces[i] = Button(this, 60 + 29 * i, 2, 25, 17, 1007, 1007, 44434, () => ui.PressTaskForce(slot), 1007);
        }
        for (int c = 0; c < 4; c++)
        {
            int colour = c;
            _fighters[c] = Button(this, 347 + 29 * c, 2, 25, 15, 1012 + c, 1016 + c, 44444, () => ui.Command(0xB + colour), 1020);
        }
        for (int k = 0; k < 4; k++)
        {
            int id = 0x10 + k;
            _navs[k] = Button(this, 485 + 39 * k, 272, 27, 27, 1026 + k, 1030 + k, 44450 + k, () => ui.Command(id));
        }
        _highlightEmpire = Button(this, 483, 305, 30, 26, 1036, 1037, 44456, () => ui.Command(0x15));
        _highlightAlliance = Button(this, 515, 305, 30, 26, 1034, 1035, 44454, () => ui.Command(0x14));
        _pause = Button(this, 561, 308, 28, 21, 1060, 1061, 44471, screen.TogglePause);
        Button(this, 608, 309, 20, 20, 1038, 1039, 44458, () => ui.ShowPage(0x1B));
        Camera(491, 343, 24, 24, 1044, 1045, 44470, TacticalCamera.ZoomIn, true);
        Camera(607, 343, 24, 24, 1046, 1047, 44469, TacticalCamera.ZoomOut, true);
        Camera(539, 338, 43, 43, 1052, 1053, 44466, TacticalCamera.TiltUp, true);
        Camera(497, 381, 42, 43, 1048, 1049, 44468, TacticalCamera.RotateLeft, true);
        Camera(582, 381, 43, 43, 1050, 1051, 44467, TacticalCamera.RotateRight, true);
        Camera(549, 391, 23, 23, 1058, 1059, 44464, TacticalCamera.ResetSubject, false);
        Camera(540, 424, 43, 43, 1055, 1056, 44465, TacticalCamera.TiltDown, true);
        Camera(486, 444, 43, 25, 1042, 1043, 44461, TacticalCamera.ResetView, false);
        Camera(594, 444, 43, 25, 1040, 1041, 44459, TacticalCamera.Remember, false);
        AddChild(_deathStar);
        _deathStar.Draw += DrawDeathStar;
        _superlaser = Button(_deathStar, 137, 2, 21, 20, 1021, 1022, 44449, () => ui.Command(0xF), 1023);
    }

    /// <summary>Death Star panel: bitmap 1024; charge bar (34, 9) 95 x 6 green (colour shade Inferred).</summary>
    void DrawDeathStar()
    {
        var panel = Bmp(1024);
        _deathStar.DrawTextureRect(panel, new Rect2(0, 0, 166, 25), false);
        DrawLog.Bitmap(_deathStar, panel, new Rect2(0, 0, 166, 25));
        var charge = new Rect2(34, 9, (float)(95 * Math.Clamp(_s.Ui.PanelValue / 100.0, 0, 1)), 6);
        _deathStar.DrawRect(charge, new Color(0, 0.8f, 0));
        DrawLog.Fill(_deathStar, charge, new Color(0, 0.8f, 0));
    }

    /// <summary>
    /// Button states from the UI controller and the mode: checked = the current mode's slot, tooltips per state. Task-force buttons
    /// an empty slot shows 1007 up and down without its digit (style flag cleared;
    /// Inferred: the flag draws the digit "1"..."8", 44417 + i), tooltip 44434. A used slot shows 1005 / 1006 with the digit, tooltip 44435 + i.
    /// </summary>
    public void Refresh()
    {
        var ui = _s.Ui;
        for (int i = 0; i < 8; i++)
        {
            var b = _taskForces[i];
            bool used = ui.TaskForces[i] is not null;
            (b.Up, b.Down, b.CheckedImage) = used ? (Bmp(1005), Bmp(1006), Bmp(1006)) : (Bmp(1007), Bmp(1007), Bmp(1007));
            b.Caption = used ? TacticalScreen.Text(44417 + i) : "";
            b.Disabled = !ui.TaskForceEnabled(i);
            b.Checked = ui.Current is TacticalUi.TaskForceMode t && t.Slot == i;
            b.TooltipText = Assets.Tip(TacticalScreen.Text(used ? 44435 + i : 44434));
            b.QueueRedraw();
        }
        for (int c = 0; c < 4; c++)
        {
            var b = _fighters[c];
            b.Disabled = ui.Fighters[c] is null;
            b.Checked = ui.Current is TacticalUi.FighterMode f && f.Colour == c;
            b.TooltipText = Assets.Tip(TacticalScreen.Text(ui.Fighters[c] is null ? 44444 : 44445 + c));
        }
        for (int k = 0; k < 4; k++) _navs[k].Checked = ui.NavShown[3 - k];
        _highlightEmpire.Checked = ui.Highlight[1];
        _highlightEmpire.TooltipText = Assets.Tip(TacticalScreen.Text(ui.Highlight[1] ? 44457 : 44456));
        _highlightAlliance.Checked = ui.Highlight[0];
        _highlightAlliance.TooltipText = Assets.Tip(TacticalScreen.Text(ui.Highlight[0] ? 44455 : 44454));
        _pause.Checked = _s.Battle.Paused;
        _pause.Disabled = _s.Battle.InIntro; // disabled during the intro
        _pause.TooltipText = Assets.Tip(TacticalScreen.Text(_s.Battle.Paused ? 44472 : 44471));
        _deathStar.Visible = ui.DeathStarPanel; // the panel shows while the UI has it
        if (_deathStar.Visible)
        {
            _superlaser.Disabled = ui.PanelValue != 100;
            _superlaser.Checked = ui.Current is TacticalUi.SuperlaserMode;
            _deathStar.QueueRedraw();
        }
    }
}
