using Blight.Blare.Core.Settings;

namespace Blare.Core.Tests.Settings;

public class FlyoutPositionTests
{
    private const int ScreenWidth = 1920;
    private const int ScreenHeight = 1080;
    private const int FlyoutWidth = 360;
    private const int FlyoutHeight = 120;
    private const int Margin = 16;

    private static (int X, int Y) Locate(FlyoutPosition position) =>
        position.Locate(0, 0, ScreenWidth, ScreenHeight, FlyoutWidth, FlyoutHeight, Margin);

    [Fact]
    public void TopLeft_SitsAgainstTheTopLeftMargin()
    {
        Assert.Equal((Margin, Margin), Locate(FlyoutPosition.TopLeft));
    }

    [Fact]
    public void BottomRight_SitsAgainstTheBottomRightMargin()
    {
        var (x, y) = Locate(FlyoutPosition.BottomRight);

        Assert.Equal(ScreenWidth - FlyoutWidth - Margin, x);
        Assert.Equal(ScreenHeight - FlyoutHeight - Margin, y);
    }

    [Fact]
    public void MiddleCenter_IsCentredOnBothAxes()
    {
        var (x, y) = Locate(FlyoutPosition.MiddleCenter);

        Assert.Equal((ScreenWidth - FlyoutWidth) / 2, x);
        Assert.Equal((ScreenHeight - FlyoutHeight) / 2, y);
    }

    [Fact]
    public void TopCenter_IsHorizontallyCentredAndAtTheTop()
    {
        var (x, y) = Locate(FlyoutPosition.TopCenter);

        Assert.Equal((ScreenWidth - FlyoutWidth) / 2, x);
        Assert.Equal(Margin, y);
    }

    [Fact]
    public void EveryPosition_KeepsTheFlyoutFullyOnScreen()
    {
        foreach (var position in Enum.GetValues<FlyoutPosition>())
        {
            var (x, y) = Locate(position);

            Assert.InRange(x, 0, ScreenWidth - FlyoutWidth);
            Assert.InRange(y, 0, ScreenHeight - FlyoutHeight);
        }
    }

    [Fact]
    public void PositionsRespectAMonitorOffset()
    {
        // A second monitor to the right of the primary.
        var (x, _) = FlyoutPosition.TopLeft.Locate(1920, 0, ScreenWidth, ScreenHeight, FlyoutWidth, FlyoutHeight, Margin);

        Assert.Equal(1920 + Margin, x);
    }

    [Theory]
    [InlineData(FlyoutPosition.TopLeft, 0, 0)]
    [InlineData(FlyoutPosition.TopRight, 0, 2)]
    [InlineData(FlyoutPosition.MiddleCenter, 1, 1)]
    [InlineData(FlyoutPosition.BottomRight, 2, 2)]
    public void RowAndColumnMapToTheGrid(FlyoutPosition position, int row, int column)
    {
        Assert.Equal(row, position.Row());
        Assert.Equal(column, position.Column());
    }

    [Fact]
    public void FromCell_RoundTripsEveryPosition()
    {
        foreach (var position in Enum.GetValues<FlyoutPosition>())
        {
            Assert.Equal(position, FlyoutPositionExtensions.FromCell(position.Row(), position.Column()));
        }
    }
}
