using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// The strategy screen's object windows: one window per id (opening an open id raises it, restoring it
/// from its slot first), close (main-screen message: sound 605 for the sector window, 607 for the system sub-windows; focus to the next window),
/// focus / raise (main-screen message), the 12 minimise slots (first free slot, else the oldest minimised window is closed for good), slot image
/// (icon + the object's location name in font 10, RGB (240, 240, 0), observed in the original), double-click restore, Alt+W (close every window not in a slot),
/// Ctrl+Tab (focus cycling skipping slotted windows), hide / show for the filter picker (slotted windows stay hidden). Zoom
/// animations are left out.
/// </summary>
public sealed class WindowManager(MainScreen screen, Control layer)
{
    readonly Dictionary<WindowAddress, GameWindow> _open = [];
    readonly WindowAddress[] _slots = new WindowAddress[12]; // window id per slot, default = free
    readonly List<WindowAddress> _order = [];            // minimise order
    readonly WindowSlot?[] _slotViews = new WindowSlot?[12];
    List<GameWindow> _hidden = [];

    public GameWindow? Focused { get; private set; }
    /// <summary>A game load re-creates the windows: open sounds 604 / 606 skipped.</summary>
    public bool Loading { get; set; }
    public IEnumerable<GameWindow> Windows => _open.Values;
    public GameWindow? Find(WindowAddress id) => _open.GetValueOrDefault(id);
    public bool IsMinimised(GameWindow w) => Array.IndexOf(_slots, w.Id) >= 0;

    // Slot rects: A x 544-606, E x 21-75, 18 high.
    static readonly int[] TopsA = [61, 83, 105, 127, 149, 171, 193, 214, 236, 258, 279, 301], TopsE = [48, 72, 98, 122, 147, 172, 196, 221, 245, 270, 295, 319];
    Rect2 SlotRect(int i) => screen.Side == SimOwner.Alliance ? new Rect2(544, TopsA[i], 62, 18) : new Rect2(21, TopsE[i], 54, 18);

    /// <summary>Opens window <paramref name="id"/>, or raises the open one (restored from its slot first). Sound <paramref name="sound"/> on a new window.</summary>
    public T Open<T>(WindowAddress id, Func<T> create, int sound = 0) where T : GameWindow
    {
        if (_open.TryGetValue(id, out var w))
        {
            int slot = Array.IndexOf(_slots, id);
            if (slot >= 0) Restore(slot);
            else Focus(w);
            return (T)w;
        }
        var window = create();
        _open[id] = window;
        layer.AddChild(window);
        if (sound != 0 && !Loading) Assets.PlayStrategy(sound);
        Focus(window);
        return window;
    }

    /// <summary>Close message: sound by kind, window destroyed, record removed, focus to the next open window.</summary>
    public void Close(GameWindow w, bool sound = true)
    {
        if (!_open.ContainsKey(w.Id)) return;
        if (w is MessageWindow m) m.Closing();
        _open.Remove(w.Id);
        if (sound) Assets.PlayStrategy(w.Kind == WindowKind.Sector ? StrategySounds.SectorWindowClose : w.Kind is WindowKind.SystemFleet or WindowKind.SystemManufacturing or WindowKind.SystemPlanet or WindowKind.SystemMissions ? StrategySounds.SubWindowClose : 0);
        int slot = Array.IndexOf(_slots, w.Id);
        if (slot >= 0) FreeSlot(slot);
        _hidden.Remove(w);
        w.QueueFree();
        if (Focused == w) Focused = null;
        FocusTop();
        if (w is MessageWindow) screen.MessageWindowClosed();
    }

    /// <summary>Focus / raise message: shown, raised, focused.</summary>
    public void Focus(GameWindow w)
    {
        if (IsMinimised(w)) return;
        w.Visible = true;
        w.MoveToFront();
        SetFocus(w);
    }

    void SetFocus(GameWindow? w)
    {
        var old = Focused;
        Focused = w;
        if (old != w) { old?.QueueRedraw(); w?.QueueRedraw(); }
    }

    void FocusTop() => SetFocus(Stack().LastOrDefault());

    /// <summary>Visible windows not in a slot, bottom: top.</summary>
    List<GameWindow> Stack() => [.. layer.GetChildren().OfType<GameWindow>().Where(w => _open.ContainsValue(w) && w.Visible && !IsMinimised(w))];

    /// <summary>
    /// Minimise: sound 611 (A) / 610 (E); the slot already holding the window, else the first free one; all full: 
    /// the oldest minimised window is closed for good and its slot reused; the window is hidden.
    /// </summary>
    public void Minimise(GameWindow w)
    {
        Assets.PlayStrategy(screen.Side == SimOwner.Alliance ? StrategySounds.MinimiseAlliance : StrategySounds.MinimiseEmpire);
        int slot = Array.IndexOf(_slots, w.Id);
        if (slot < 0) slot = Array.IndexOf(_slots, default);
        if (slot < 0)
        {
            var oldest = _order[0];
            slot = Array.IndexOf(_slots, oldest);
            if (_open.TryGetValue(oldest, out var evicted)) Close(evicted, sound: false);
        }
        var r0 = SlotRect(slot);
        // the zoom window: slot, then hidden (one skip flag of the original is not modelled)
        screen.Zoom(w.GetGlobalRect(), new Rect2(layer.GetGlobalRect().Position + r0.Position, r0.Size), () => { if (GodotObject.IsInstanceValid(w) && IsMinimised(w)) w.Visible = false; });
        PlaceInSlot(w, slot);
    }

    /// <summary>The window into <paramref name="slot"/> (minimise order, slot image), no sound or zoom; hidden at once unless a zoom hides it.</summary>
    void PlaceInSlot(GameWindow w, int slot, bool hide = false)
    {
        _order.Remove(w.Id);
        _order.Add(w.Id);
        _slots[slot] = w.Id;
        if (hide) w.Visible = false;
        // label: the location of the window's object (its system; a fleet's planet), observed in the original
        var place = w.System ?? (w.Subject is { } o && screen.Game is { } g ? Knowledge.Location(g, o, screen.Side) : null);
        var view = new WindowSlot(this, slot, w.Icon, place is null ? "" : screen.ObjectName(place));
        var r = SlotRect(slot);
        view.Position = r.Position;
        view.Size = r.Size;
        _slotViews[slot]?.QueueFree();
        _slotViews[slot] = view;
        layer.AddChild(view);
        layer.MoveChild(view, 0);
        if (Focused == w) FocusTop();
    }

    /// <summary>Restore: sound 613 (A) / 612 (E), slot cleared, window shown, raised, focused.</summary>
    public void Restore(int slot)
    {
        if (!_open.TryGetValue(_slots[slot], out var w)) return;
        Assets.PlayStrategy(screen.Side == SimOwner.Alliance ? StrategySounds.RestoreAlliance : StrategySounds.RestoreEmpire);
        var r = SlotRect(slot);
        FreeSlot(slot);
        // the zoom slot: the window's last rect, then shown, raised, focused
        screen.Zoom(new Rect2(layer.GetGlobalRect().Position + r.Position, r.Size), w.GetGlobalRect(), () => { if (GodotObject.IsInstanceValid(w) && !IsMinimised(w)) Focus(w); });
    }

    void FreeSlot(int slot)
    {
        _order.Remove(_slots[slot]);
        _slots[slot] = default;
        _slotViews[slot]?.QueueFree();
        _slotViews[slot] = null;
    }

    /// <summary>Alt+W: close every open window that is not minimised in a slot.</summary>
    public void CloseUnminimised()
    {
        foreach (var w in _open.Values.Where(w => !IsMinimised(w)).ToList()) Close(w);
    }

    /// <summary>Ctrl+Tab / Ctrl+Shift+Tab: focus the next / previous open window (slotted ones skipped).</summary>
    public void Cycle(bool back)
    {
        var stack = Stack();
        if (stack.Count < 2) return;
        if (back)
        {
            layer.MoveChild(stack[^1], 0);
            FocusTop();
        }
        else Focus(stack[0]);
    }

    /// <summary>Galactic Information Display command: every open window hidden; <see cref="ShowHidden"/> (filter command) shows those not in a slot again.</summary>
    public void HideAll()
    {
        _hidden = [.. _open.Values.Where(w => w.Visible)];
        foreach (var w in _hidden) w.Visible = false;
    }

    public void ShowHidden()
    {
        foreach (var w in _hidden.Where(w => _open.ContainsValue(w) && !IsMinimised(w))) w.Visible = true;
        _hidden = [];
    }

    /// <summary>The open windows for the save: stacking order bottom: top, then the minimised ones in minimise order.</summary>
    public List<SavedWindow> Snapshot()
    {
        var shown = layer.GetChildren().OfType<GameWindow>().Where(w => _open.ContainsValue(w) && !IsMinimised(w));
        return [.. shown.Select(w => new SavedWindow(w.Id.Kind, w.Id.Index, (int)w.Position.X, (int)w.Position.Y, -1)),
            .. _order.Where(_open.ContainsKey).Select(id => new SavedWindow(id.Kind, id.Index, (int)_open[id].Position.X, (int)_open[id].Position.Y, Array.IndexOf(_slots, id)))];
    }

    /// <summary>
    /// Load: the saved windows re-created in order by <paramref name="open"/> (the window factory; null = not
    /// re-created), silently, at their saved position; a saved slot puts the window back into it (Inferred: the original's
    /// descriptor holds the rect, the slot handling on load is not verified).
    /// </summary>
    public void Reopen(IEnumerable<SavedWindow> saved, Func<SavedWindow, GameWindow?> open)
    {
        Loading = true;
        try
        {
            foreach (var s in saved)
            {
                var id = new WindowAddress(s.Kind, s.Index);
                if (_open.ContainsKey(id) || open(s) is not { } w || w.Id != id) continue;
                w.Position = new Vector2(s.X, s.Y);
                if (s.Slot is >= 0 and < 12 && _slots[s.Slot] == default) PlaceInSlot(w, s.Slot, hide: true);
            }
            FocusTop();
        }
        finally { Loading = false; }
    }

    /// <summary>Every window and slot gone, silently (new game, load).</summary>
    public void Clear()
    {
        foreach (var w in _open.Values) w.QueueFree();
        _open.Clear();
        for (int i = 0; i < 12; i++) FreeSlot(i);
        _hidden = [];
        Focused = null;
    }

    /// <summary>The top window under a screen point (ChildWindowFromPointEx, invisible ones skipped).</summary>
    public GameWindow? WindowAt(Vector2 global) => Stack().LastOrDefault(w => w.GetGlobalRect().HasPoint(global));

    public void Refresh()
    {
        foreach (var w in _open.Values) w.Refresh();
    }

    // Sector-window slots: left / right per side.
    Vector2 SectorSlot(int right) => (screen.Side == SimOwner.Alliance, right) switch
    {
        (true, 0) => new(60, 35), (true, _) => new(300, 35), (false, 0) => new(120, 40), _ => new(365, 40),
    };

    /// <summary>
    /// Opens (or raises) the sector window: none open: right when the sector's SD X >= 511, else left; one open: 
    /// the free slot; both: the window on the sector's half closes (no sound) and the new one takes its place. Sound 604.
    /// </summary>
    public SectorWindow OpenSector(Sector sector)
    {
        var id = new WindowAddress(WindowKind.Sector, (int)sector.Definition.Common.Index);
        if (_open.TryGetValue(id, out var open)) return Open(id, () => (SectorWindow)open);
        var sectors = _open.Values.OfType<SectorWindow>().ToList();
        var used = sectors.Select(w => w.Position == SectorSlot(0) ? 0 : 1).ToList();
        int slot = ScreenViews.SectorSlot(sector, used);
        if (sectors.Count >= 2 && sectors.FirstOrDefault(w => w.Position == SectorSlot(slot)) is { } replaced) Close(replaced, sound: false);
        return Open(id, () => new SectorWindow(screen, id, sector) { Position = SectorSlot(slot) }, StrategySounds.SectorWindowOpen);
    }

    /// <summary>Swap button: the window moves to the other half (slot); a sector window already there closes (observed in the original).</summary>
    public void SwapSector(SectorWindow w)
    {
        int other = w.Position == SectorSlot(0) ? 1 : 0;
        if (_open.Values.OfType<SectorWindow>().FirstOrDefault(x => x != w) is { } x) Close(x);
        w.Position = SectorSlot(other);
    }
}

/// <summary>A minimised window's slot: its icon at (0, 0), then the object's name, font 10, RGB (240, 240, 0); double click restores.</summary>
public partial class WindowSlot(WindowManager manager, int slot, ushort icon, string name) : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TooltipText = Assets.Tip(name);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true }) manager.Restore(slot);
        AcceptEvent();
    }

    public override void _Draw()
    {
        float x = 0;
        if (icon != 0)
        {
            var size = (Vector2)Assets.BitmapSize(icon, "STRATEGY.DLL");
            var image = Assets.Strategy(icon);
            DrawTextureRect(image, new Rect2(Vector2.Zero, size), false);
            DrawLog.Bitmap(this, image, new Rect2(Vector2.Zero, size));
            x = size.X;
        }
        Assets.DrawText(this, new Rect2(x, 0, Size.X - x, Size.Y), name, 10, Color.Color8(240, 240, 0), TextFormat.Row);
    }
}
