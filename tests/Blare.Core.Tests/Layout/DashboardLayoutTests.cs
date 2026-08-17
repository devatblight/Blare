using Blight.Blare.Core.Layout;

namespace Blare.Core.Tests.Layout;

public class DashboardLayoutTests
{
    [Fact]
    public void DefaultLayout_ContainsEveryPanel()
    {
        var layout = DashboardLayout.CreateDefault();

        Assert.NotNull(layout.Get(DashboardLayout.MasterPanel));
        Assert.NotNull(layout.Get(DashboardLayout.DisplaysPanel));
        Assert.NotNull(layout.Get(DashboardLayout.StatusPanel));
        Assert.NotNull(layout.Get(DashboardLayout.DeskPanel));
    }

    [Fact]
    public void DefaultLayout_HasNoOverlappingPanels()
    {
        var panels = DashboardLayout.CreateDefault().ToList();

        for (var i = 0; i < panels.Count; i++)
        {
            for (var j = i + 1; j < panels.Count; j++)
            {
                Assert.False(
                    panels[i].Overlaps(panels[j]),
                    $"{panels[i].PanelId} overlaps {panels[j].PanelId}");
            }
        }
    }

    [Fact]
    public void DefaultLayout_FitsEntirelyOnTheGrid()
    {
        foreach (var panel in DashboardLayout.CreateDefault().Panels)
        {
            Assert.InRange(panel.Column, 0, DashboardLayout.Columns - 1);
            Assert.InRange(panel.Row, 0, DashboardLayout.Rows - 1);
            Assert.True(panel.Right <= DashboardLayout.Columns, $"{panel.PanelId} runs off the right");
            Assert.True(panel.Bottom <= DashboardLayout.Rows, $"{panel.PanelId} runs off the bottom");
        }
    }

    [Fact]
    public void DefaultLayout_GivesTheDeskMostOfTheHeight()
    {
        // The complaint this default answers: full-width sliders eating the top
        // while the strips float in dead space.
        var desk = DashboardLayout.CreateDefault().Get(DashboardLayout.DeskPanel)!;

        Assert.True(desk.RowSpan >= DashboardLayout.Rows / 2, "the desk should own the majority of the height");
        Assert.Equal(DashboardLayout.Columns, desk.ColumnSpan);
    }

    [Fact]
    public void DefaultLayout_DoesNotStretchMasterAcrossTheFullWidth()
    {
        var master = DashboardLayout.CreateDefault().Get(DashboardLayout.MasterPanel)!;

        Assert.True(master.ColumnSpan < DashboardLayout.Columns);
    }

    [Fact]
    public void MovingPastTheRightEdge_SlidesBackOnScreen()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Move(DashboardLayout.MasterPanel, column: 50, row: 0);
        var master = layout.Get(DashboardLayout.MasterPanel)!;

        Assert.True(master.Right <= DashboardLayout.Columns);
    }

    [Fact]
    public void MovingToNegativeCoordinates_ClampsToTheOrigin()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Move(DashboardLayout.MasterPanel, column: -8, row: -3);
        var master = layout.Get(DashboardLayout.MasterPanel)!;

        Assert.Equal(0, master.Column);
        Assert.Equal(0, master.Row);
    }

    [Fact]
    public void ResizingBelowTheMinimum_KeepsAPanelUsable()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Resize(DashboardLayout.StatusPanel, columnSpan: 0, rowSpan: 0);
        var status = layout.Get(DashboardLayout.StatusPanel)!;

        Assert.Equal(DashboardLayout.MinimumSpan, status.ColumnSpan);
        Assert.Equal(DashboardLayout.MinimumSpan, status.RowSpan);
    }

    [Fact]
    public void ResizingBeyondTheGrid_IsClampedToFit()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Resize(DashboardLayout.DeskPanel, columnSpan: 99, rowSpan: 99);
        var desk = layout.Get(DashboardLayout.DeskPanel)!;

        Assert.True(desk.Right <= DashboardLayout.Columns);
        Assert.True(desk.Bottom <= DashboardLayout.Rows);
    }

    [Fact]
    public void GrowingAPanelAtTheEdge_PullsItBackRatherThanOverflowing()
    {
        var layout = DashboardLayout.CreateDefault();
        layout.Move(DashboardLayout.StatusPanel, column: 10, row: 10);

        layout.Resize(DashboardLayout.StatusPanel, columnSpan: 6, rowSpan: 6);
        var status = layout.Get(DashboardLayout.StatusPanel)!;

        Assert.Equal(6, status.ColumnSpan);
        Assert.True(status.Right <= DashboardLayout.Columns);
        Assert.True(status.Bottom <= DashboardLayout.Rows);
    }

    [Fact]
    public void SavedLayout_SurvivesARoundTrip()
    {
        var original = DashboardLayout.CreateDefault();
        original.Move(DashboardLayout.DeskPanel, column: 0, row: 4);

        var restored = DashboardLayout.FromPanels(original.ToList());

        Assert.Equal(original.Get(DashboardLayout.DeskPanel), restored.Get(DashboardLayout.DeskPanel));
    }

    [Fact]
    public void LayoutSavedBeforeAPanelExisted_GainsItFromTheDefault()
    {
        // Guards the upgrade path: a layout saved by an older version must not
        // make newer panels disappear.
        var partial = new[] { new PanelLayout(DashboardLayout.MasterPanel, 0, 0, 4, 3) };

        var restored = DashboardLayout.FromPanels(partial);

        Assert.NotNull(restored.Get(DashboardLayout.DeskPanel));
        Assert.NotNull(restored.Get(DashboardLayout.StatusPanel));
    }

    [Fact]
    public void OverlapDetection_IsSymmetricAndExcludesTouchingEdges()
    {
        var left = new PanelLayout("a", 0, 0, 4, 4);
        var right = new PanelLayout("b", 4, 0, 4, 4);
        var covering = new PanelLayout("c", 2, 2, 4, 4);

        Assert.False(left.Overlaps(right), "panels sharing an edge do not overlap");
        Assert.False(right.Overlaps(left));
        Assert.True(left.Overlaps(covering));
        Assert.True(covering.Overlaps(left));
    }
}
