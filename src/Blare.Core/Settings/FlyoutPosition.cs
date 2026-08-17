namespace Blight.Blare.Core.Settings;

/// <summary>
/// Where Blare's flyout appears. This is the app's single communication
/// surface — boost notices, safety warnings and every other message use it,
/// so the user picks the spot once rather than per feature.
/// </summary>
public enum FlyoutPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleCenter,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
}

public static class FlyoutPositionExtensions
{
    /// <summary>Column 0/1/2 for left, centre and right.</summary>
    public static int Column(this FlyoutPosition position) => (int)position % 3;

    /// <summary>Row 0/1/2 for top, middle and bottom.</summary>
    public static int Row(this FlyoutPosition position) => (int)position / 3;

    public static FlyoutPosition FromCell(int row, int column) =>
        (FlyoutPosition)(Math.Clamp(row, 0, 2) * 3 + Math.Clamp(column, 0, 2));

    /// <summary>
    /// Places a flyout of the given size inside a work area.
    /// Margins keep it clear of screen edges and the taskbar.
    /// </summary>
    public static (int X, int Y) Locate(
        this FlyoutPosition position,
        int workAreaX,
        int workAreaY,
        int workAreaWidth,
        int workAreaHeight,
        int flyoutWidth,
        int flyoutHeight,
        int margin = 16)
    {
        var x = position.Column() switch
        {
            0 => workAreaX + margin,
            1 => workAreaX + (workAreaWidth - flyoutWidth) / 2,
            _ => workAreaX + workAreaWidth - flyoutWidth - margin,
        };

        var y = position.Row() switch
        {
            0 => workAreaY + margin,
            1 => workAreaY + (workAreaHeight - flyoutHeight) / 2,
            _ => workAreaY + workAreaHeight - flyoutHeight - margin,
        };

        return (x, y);
    }
}
