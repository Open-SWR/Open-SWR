using System.Collections.Generic;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Sim;
using SimOwner = OpenSWR.Sim.Owner;

/// <summary>
/// Window id: the kind of window and, for kinds with one window per object (sector, system sub-windows) or per dialog, the
/// index of that object or dialog (0 for the single windows). A plain number is a kind with index 0.
/// </summary>
public readonly record struct WindowAddress(int Kind, int Index = 0)
{
    public static implicit operator WindowAddress(int kind) => new(kind);
}

/// <summary>
/// Object window base ("CustomDialogBox"): window id <see cref="WindowAddress"/>, title strip with the
/// object's name, close button 10108 / 10109 (and Esc): <see cref="WindowManager.Close"/>; sub-windows (system windows)
/// also have the minimise button 10253 / 10254 left of the close button (: a window slot), the go-to-sector button
/// 10209 / 10208 at (3, 3) and drag by the title strip inside the map rect (Inferred: base drag mode 2). A click anywhere focuses
/// and raises the window. The selection, drop targets, the selected object and the slot icon are per class.
/// </summary>
public abstract partial class GameWindow : Control
{
    public WindowAddress Id { get; }
    public int Kind => Id.Kind;
    protected readonly MainScreen Screen;
    protected GameState Game => Screen.Game!;
    protected SimOwner Side => Screen.Side;
    protected bool A => Side == SimOwner.Alliance;

    /// <summary>Title strip and close button 10108 / 10109 of the object windows; false for the 470-wide panel windows (messages, finders, encyclopedia: own buttons).</summary>
    protected virtual bool Framed => true;
    /// <summary>Sub-window: minimise and go-to-sector buttons, draggable by the title strip, 18-px title in font 5 black.</summary>
    protected virtual bool SubWindow => false;
    /// <summary>Dragged by the title strip inside the map rect (sub-windows, the production window).</summary>
    protected virtual bool Draggable => SubWindow;
    /// <summary>The system whose sector the go-to-sector button opens (sub-windows).</summary>
    public virtual StarSystem? System => null;
    /// <summary>Window title (the object's name).</summary>
    protected abstract string Title { get; }
    /// <summary>Slot icon (a STRATEGY bitmap; Inferred per class) and the object named in the slot.</summary>
    public virtual ushort Icon => 0;
    public virtual GameObject? Subject => System;
    /// <summary>The window's selection: (object, group mode).</summary>
    public virtual List<(GameObject Object, int Mode)> Selection => [];
    /// <summary>Drop / targeting targets at a window-local point: T_dest and T_move.</summary>
    public virtual GameObject? DestAt(Vector2 local) => null;
    public virtual GameObject? MoveAt(Vector2 local) => null;
    /// <summary>Selects an object (the go-to helper).</summary>
    public virtual void SelectObject(GameObject o) { }
    /// <summary>Re-reads the views (daily, after orders).</summary>
    public virtual void Refresh() => QueueRedraw();
    /// <summary>Keys while focused; true = consumed. Esc closes in the manager.</summary>
    public virtual bool HandleKey(InputEventKey key) => false;

    protected GameWindow(MainScreen screen, WindowAddress id, Vector2 size)
    {
        (Screen, Id) = (screen, id);
        Size = size;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public override void _Ready()
    {
        if (!Framed) return;
        var close = Button(10108, 10109, (int)Size.X - 14 - 3, 3, () => Screen.Windows.Close(this));
        if (!SubWindow) return;
        Button(10253, 10254, close - 14, 3, () => Screen.Windows.Minimise(this));
        Button(10209, 10208, 3, 3, () => { if (System is { } s) Screen.Windows.OpenSector(s.Sector); });
    }

    protected int Button(ushort up, ushort down, int x, int y, global::System.Action command, int w = 14, int h = 14, AudioStream? sound = null)
    {
        var b = new FrontButton { Up = Assets.Strategy(up), Down = Assets.Strategy(down), Command = command, Sound = sound };
        b.Place(x, y, w, h);
        AddChild(b);
        return x;
    }

    /// <summary>Title strip: (2, 2)-(w - 3, 16) font 4 RGB (240, 240, 240); sub-windows (2, 2)-(w - 2, 18) font 5 black; centred (Inferred).</summary>
    protected void DrawTitle()
    {
        if (SubWindow) Assets.DrawText(this, new Rect2(2, 2, Size.X - 4, 16), Title, 5, Colors.Black, TextFormat.CentredRow);
        else Assets.DrawText(this, new Rect2(2, 2, Size.X - 5, 14), Title, 4, Color.Color8(240, 240, 240), TextFormat.CentredRow);
    }

    Vector2? _drag;

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { Pressed: true } b:
                Screen.Windows.Focus(this); // 0x467
                if (Draggable && b.ButtonIndex == MouseButton.Left && b.Position.Y < 19) _drag = b.Position;
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }:
                _drag = null;
                break;
            case InputEventMouseMotion m when _drag is { } grab:
                var map = Screen.MapRect;
                var at = GetGlobalMousePosition() - grab;
                Position = new Vector2(Mathf.Clamp(at.X, map.Position.X, map.End.X - Size.X), Mathf.Clamp(at.Y, map.Position.Y, map.End.Y - Size.Y));
                AcceptEvent();
                return;
        }
        WindowInput(e);
        AcceptEvent();
    }

    /// <summary>Mouse input inside the window (after focus / drag handling).</summary>
    protected virtual void WindowInput(InputEvent e) { }
}
