using Godot;
using TextFormat = OpenSWR.Data.TextFormat;

/// <summary>
/// Strategy-screen top bar: day text (font 10, side colour; content Inferred: the day number), the speed
/// indicator image (s 0-3: 11580 + s A / 11584 + s E, s 4: 11588 / 11589), and the three monitors in font 8, side colour,
/// right-aligned and vertically centred (DrawText format): raw material stock, refined material stock, free maintenance (capacity -
/// allocated, Inferred; TEXTSTRA 4866 "MAX" when the capacity exceeds 99999).
/// </summary>
public partial class TopBar : Control
{
    public bool SideOne { get; set; } = true;
    public int Day, Speed, Raw, Refined, Maintenance, Capacity;

    public TopBar()
    {
        Size = new Vector2(640, 40);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var colour = UiFrame.SideColour(SideOne);
        var (day, indicator) = SideOne ? (new Vector2(104, 18), new Vector2(167, 20)) : (new Vector2(500, 18), new Vector2(561, 20));
        Assets.DrawText(this, new Rect2(day, new Vector2(60, Assets.LineHeight(10))), Day.ToString(), 10, colour, TextFormat.Row);
        int s = System.Math.Clamp(Speed, 0, 4);
        ushort image = (ushort)(s == 4 ? (SideOne ? StrategyBitmaps.SpeedIndicatorFastestAlliance : StrategyBitmaps.SpeedIndicatorFastestEmpire) : (SideOne ? StrategyBitmaps.SpeedIndicatorAllianceBase : StrategyBitmaps.SpeedIndicatorEmpireBase) + s);
        var box = new Rect2(indicator, Assets.BitmapSize(image, "STRATEGY.DLL"));
        var texture = Assets.Strategy(image);
        DrawTextureRect(texture, box, false);
        DrawLog.Bitmap(this, texture, box);
        string max = Capacity > 99999 ? StrategyText.MaintenanceMax : Maintenance.ToString();
        Vector2[] at = SideOne ? [new(265, 16), new(360, 16), new(457, 16)] : [new(175, 19), new(273, 19), new(371, 19)];
        string[] texts = [Raw.ToString(), Refined.ToString(), max];
        for (int i = 0; i < 3; i++) Assets.DrawText(this, new Rect2(at[i], new Vector2(63, 17)), texts[i], 8, colour, new TextFormat { Right = true, VCenter = true, SingleLine = true });
    }
}
