namespace OpenSWR.Data;

/// <summary>
/// Drop-down box pop-up geometry. Plain .NET so the rule is testable.
/// The pop-up at the box's x, directly below the box, the box's width, height = row height * visible
/// rows + 4; when its bottom passes <paramref name="bottomBound"/> it goes above the box (y = box y - height),
/// clamped at <paramref name="topBound"/> with the height shrunk to fit. Inside: the cell list at (2, 2), (w - 4) x (h - 4),
/// cells (w - 4) x row height.
/// </summary>
public static class DropDownLayout
{
    public readonly record struct Popup(float X, float Y, float Width, float Height, float ListX, float ListY, float ListWidth, float ListHeight, float CellWidth, float CellHeight);

    public static Popup Of(float boxX, float boxY, float boxWidth, float boxHeight, int rowHeight, int rows, float topBound, float bottomBound)
    {
        float h = rowHeight * rows + 4, y = boxY + boxHeight;
        if (y + h > bottomBound)
        {
            y = boxY - h;
            if (y < topBound)
            {
                h -= topBound - y;
                y = topBound;
            }
        }
        return new(boxX, y, boxWidth, h, 2, 2, boxWidth - 4, h - 4, boxWidth - 4, rowHeight);
    }
}
