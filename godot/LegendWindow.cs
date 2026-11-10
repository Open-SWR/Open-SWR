using System;
using Godot;
using TextFormat = OpenSWR.Data.TextFormat;
using OpenSWR.Data;

/// <summary>
/// Map legend window (panels from RCDATA ids 10183-10201, 11600-11607): a static key for the filter mode's markers.
/// Created 180 x 240, shrunk to the entry's (Width, Height); frame drawn by <see cref="UiFrame"/>; items: bitmaps at (x, y), texts at (x, y) in their font
/// and colour, a text with y = 1 is the title centred over the width. Close button 10108 / 10109 (command 1) at the top-right
/// corner (Inferred: position not read). Fixed position: it cannot be dragged (observed in the original).
/// No entry (0): the window stays 180 x 240 and empty (Inferred).
/// </summary>
public partial class LegendWindow : Control
{
    readonly LegendPanel? _data;
    readonly FrontButton _close;

    public LegendWindow(int entry, Vector2 at, Action close)
    {
        Size = new Vector2(180, 240);
        if (entry != 0 && Assets.Legend(entry) is { } panel)
        {
            _data = panel;
            Size = new Vector2(Mathf.Min(180, panel.Width), Mathf.Min(240, panel.Height));
        }
        Position = at;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true; // the 210-wide divider 10182 ends at the window edge
        var size = (Vector2)Assets.BitmapSize(10108, "STRATEGY.DLL");
        _close = new FrontButton { Up = Assets.Strategy(10108), Down = Assets.Strategy(10109), Command = close };
        _close.Place((int)(Size.X - size.X - 3), 3, (int)size.X, (int)size.Y);
        AddChild(_close);
        AddChild(new Darkened()); // the main-screen picture under the window, darkened
    }

    public override void _GuiInput(InputEvent e) => AcceptEvent(); // not draggable (observed in the original)

    public override void _Draw()
    {
        UiFrame.Draw(this, new Rect2(Vector2.Zero, Size), fill: false);
        if (_data is null) return;
        foreach (var item in _data.Items)
            switch (item)
            {
                case LegendImage i:
                    var image = Assets.Strategy(i.Bitmap);
                    var at = new Rect2(i.X, i.Y, Assets.BitmapSize(i.Bitmap, "STRATEGY.DLL"));
                    DrawTextureRect(image, at, false);
                    DrawLog.Bitmap(this, image, at);
                    break;
                case LegendText t:
                    var colour = Color.Color8((byte)t.R, (byte)t.G, (byte)t.B);
                    var h = Assets.LineHeight(t.Font);
                    if (t.Y == 1) Assets.DrawText(this, new Rect2(0, 1, Size.X, h), Assets.Stra(t.String), t.Font, colour, TextFormat.CentredRow); // title: centred over the width
                    else Assets.DrawText(this, new Rect2(t.X, t.Y, Size.X - t.X, h), Assets.Stra(t.String), t.Font, colour, TextFormat.Row);
                    break;
            }
    }
}
