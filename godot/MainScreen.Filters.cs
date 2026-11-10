using System.Collections.Generic;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>Map display filters, picker, title and legend of the strategy screen.</summary>
public partial class MainScreen
{
    Control _title = null!; // the filter title, drawn by DrawFilterTitle
    string _titleText = "";
    int _filter = MapFilter.Off;
    LegendWindow? _legend;
    FrontButton? _legendButton;

    bool SideOne => _map.Side == SimOwner.Alliance;

    /// <summary>
    /// Command 0x140 / Alt+1...9: redraw the map in the mode, the title in the side colour
    /// centred over the map rect's top edge (<see cref="MapFilter.Title"/>: System font, centred, unclipped), and rebuild the legend. <paramref name="list"/> = the one-system
    /// list (SD indexes) of the advisor-only modes 0x92 / 0x93; any other mode drops it.
    /// </summary>
    void SetFilter(int mode, IReadOnlyCollection<int>? list = null, bool? sound = null)
    {
        sound ??= mode != MapFilter.Off && (mode != _filter || list is not null);
        _filter = mode;
        _map.FilterList = mode is 0x92 or 0x93 ? list : null;
        _map.FilterMode = mode;
        if (sound == true) Assets.PlayStrategy(601); // after the redraw: a changed mode or a list, never 0x80
        if (_gidButton is not null) _gidButton.Checked = mode != MapFilter.Off;
        if (_game is not null) _game.FilterMode = mode; // saved with the game
        int title = mode == MapFilter.Off ? 0 : FilterTitle(mode);
        SetTitle(title == 0 ? "" : Assets.Stra(title)); // 0x92 / 0x93: no title of their own (only a guided step's)
        RebuildLegend(mode);
    }

    /// <summary>The filter title text (raw TEXTSTRA: "&" prefixes are processed by the draw).</summary>
    void SetTitle(string text)
    {
        _titleText = text;
        _title.QueueRedraw();
    }

    /// <summary>Filter title: PALETTERGB (255, 0, 0) / (0, 255, 0) by side.</summary>
    void DrawFilterTitle()
    {
        if (_titleText == "") return;
        var (x, y, w, h, format) = MapFilter.Title(_side == 0, _titleText);
        Assets.DrawText(_title, new Rect2(x, y, w, h), _titleText, OpenSWR.Data.TextLayout.SystemFont, UiFrame.SideColour(_side == 0), format);
    }

    /// <summary>Title id (TEXTSTRA) per mode: the picker's titles; advisor-only 0x13 / 0x15: 5663 / 5664 by side, 0x14 / 0x16: 5665, 0x17: 0x1629, 0x91: 0x162A; 0x92 / 0x93 get the guided step's own title.</summary>
    int FilterTitle(int mode) => mode switch
    {
        0x13 => SideOne ? 5663 : 5664,
        0x15 => SideOne ? 5664 : 5663,
        0x14 or 0x16 => 5665,
        0x17 => 0x1629,
        0x91 => 0x162A,
        0x92 or 0x93 => 0,
        _ => FilterPicker.Titles.GetValueOrDefault(mode),
    };

    /// <summary>
    /// Legend on a mode change: the open window is destroyed (its position kept when visible); mode 0x80 hides the
    /// legend button; any other mode shows the button and creates the window visible over it, top left of the map (openswr: the
    /// original opens it at (150, 120).
    /// </summary>
    void RebuildLegend(int mode)
    {
        if (_legend is not null)
        {
            _legend.QueueFree();
            _legend = null;
        }
        _legendButton?.QueueFree();
        _legendButton = null;
        int entry = FilterPicker.Legend(mode, _side == 0);
        // openswr deviation: modes without a legend panel (advisor modes 0x13-0x17, 0x91-0x93) get no window and no button, not an empty 180 x 240 box
        if (mode == MapFilter.Off || entry == 0) return;
        var size = Assets.BitmapSize(10168, "STRATEGY.DLL");
        _legendButton = new FrontButton { Up = Assets.Strategy(10168), TooltipText = UiFrame.Label(5948), Command = ShowLegend };
        var at = new Vector2(_side == 0 ? 55 : 113, 50);
        _legendButton.Place((int)at.X, (int)at.Y, size.X, size.Y); // button 0x134
        GetNode("Hud").AddChild(_legendButton);
        _legend = new LegendWindow(entry, at, () => HideLegend(zoom: true));
        GetNode("Hud").AddChild(_legend);
        // openswr: button and legend sit under the window layer, so open object windows cover them
        GetNode("Hud").MoveChild(_legendButton, _windowLayer.GetIndex());
        GetNode("Hud").MoveChild(_legend, _windowLayer.GetIndex());
    }

    /// <summary>
    /// Close button (message 0x405, kind 3): the zoom legend: legend button, then legend hidden (not destroyed), button
    /// shown. <paramref name="zoom"/> false: at once (a map click collapsing it: no zoom).
    /// </summary>
    void HideLegend(bool zoom = false)
    {
        if (_legend is not { } legend) return;
        var button = _legendButton;
        void Hide()
        {
            if (IsInstanceValid(legend)) legend.Visible = false;
            if (button is not null && IsInstanceValid(button)) button.Visible = true;
        }
        if (zoom && legend.Visible && button is not null) Zoom(legend.GetGlobalRect(), button.GetGlobalRect(), Hide);
        else Hide();
    }

    /// <summary>Legend button 0x134: nothing without a game or a legend window; else the zoom button: legend, then legend shown and raised, button hidden.</summary>
    void ShowLegend()
    {
        if (_legend is not { } legend || _game is null) return;
        var button = _legendButton;
        void Show()
        {
            if (!IsInstanceValid(legend)) return;
            legend.Visible = true;
            legend.MoveToFront();
            if (button is not null && IsInstanceValid(button)) button.Visible = false;
        }
        if (button is not null) Zoom(button.GetGlobalRect(), legend.GetGlobalRect(), Show);
        else Show();
    }

    /// <summary>
    /// Command 0x132: a visible legend collapses into its button, every open panel is hidden, then the picker opens;
    /// choosing a mode or closing it shows the hidden panels again.
    /// </summary>
    void OpenPicker()
    {
        if (_game is null) return;
        if (_legend is { Visible: true } legend && _legendButton is { } button)
        {
            // a visible legend collapses first; the `from` bottom is the legend's height, not top + height (as coded)
            var r = legend.GetGlobalRect();
            Zoom(new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y - r.Position.Y)), button.GetGlobalRect(), () =>
            {
                HideLegend();
                PickerZoom();
            });
            return;
        }
        PickerZoom();
    }

    /// <summary>Panels and windows hidden, then the zoom GID button: picker and the picker shown; closing it zooms back (picker: GID button) before the hidden windows return.</summary>
    void PickerZoom()
    {
        if (_game is null) return;
        var hidden = new List<Control>(new Control[] { _research }.Where(p => p.Visible));
        foreach (var p in hidden) p.Visible = false;
        Windows.HideAll(); // every open window hidden; 0x140 shows again those not in a slot
        var picker = FilterPicker.Create(SideOne, m => SetFilter(m));
        var gid = _gidButton?.GetGlobalRect() ?? new Rect2();
        Rect2 at = new();
        OpenMenu(picker, () =>
        {
            if (picker.ClickedAway) Assets.PlayStrategy(601); // left up on the map closes the picker: 601 before the windows come back
            Zoom(at, gid, () =>
            {
                foreach (var p in hidden) p.Visible = true;
                Windows.ShowHidden();
            });
        });
        at = picker.GetGlobalRect();
        picker.Visible = false;
        Zoom(gid, at, () => { if (IsInstanceValid(picker)) picker.Visible = true; });
    }

    /// <summary>Env OPENSWR_DEMO_FILTER = a hex mode (e.g. 11, 21, 53), or picker[N] (picker open, row N's submenu open).</summary>
    void DemoFilter(string arg)
    {
        Only(null);
        if (arg.StartsWith("picker"))
        {
            OpenPicker();
            if (int.TryParse(arg[6..], out int row)) _menu?.Demo(row, openSub: true);
            return;
        }
        if (int.TryParse(arg, System.Globalization.NumberStyles.HexNumber, null, out int mode)) SetFilter(mode);
    }
}
