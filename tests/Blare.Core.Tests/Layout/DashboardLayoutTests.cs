using Blight.Blare.Core.Layout;

namespace Blare.Core.Tests.Layout;

public class DashboardLayoutTests
{
    private static DashboardCard Card(string id, int column, int row, int columnSpan = 3, int rowSpan = 3) =>
        new(id, CardKind.QuickActions, column, row, columnSpan, rowSpan);

    /// <summary>A card covering the whole grid. Only the mixer is allowed to be this large.</summary>
    private static DashboardCard FullGridCard(string id) =>
        new(id, CardKind.AppMixer, 0, 0, DashboardLayout.Columns, DashboardLayout.Rows);

    [Fact]
    public void Resize_WillNotShrinkACardBelowWhatItsContentNeeds()
    {
        // The mixer clipped its own faders after being dragged short.
        var layout = new DashboardLayout();
        layout.Add(new DashboardCard("mixer", CardKind.AppMixer, 0, 0, 12, 9));

        layout.Resize("mixer", 2, 2);

        var bounds = CardSizing.For(CardKind.AppMixer);
        var mixer = layout.Get("mixer")!;

        Assert.Equal(bounds.MinColumns, mixer.ColumnSpan);
        Assert.Equal(bounds.MinRows, mixer.RowSpan);
    }

    [Fact]
    public void Resize_WillNotGrowACardBeyondItsUsefulSize()
    {
        var layout = new DashboardLayout();
        layout.Add(new DashboardCard("now", CardKind.NowPlaying, 0, 0, 3, 3));

        layout.Resize("now", 12, 12);

        var bounds = CardSizing.For(CardKind.NowPlaying);
        var card = layout.Get("now")!;

        Assert.Equal(bounds.MaxColumns, card.ColumnSpan);
        Assert.Equal(bounds.MaxRows, card.RowSpan);
    }

    [Fact]
    public void EveryCardKind_HasBoundsThatFitTheGridAndAgreeWithEachOther()
    {
        foreach (var kind in Enum.GetValues<CardKind>())
        {
            var bounds = CardSizing.For(kind);

            Assert.True(bounds.MinColumns >= DashboardLayout.MinimumSpan, $"{kind} minimum width is below the grid minimum");
            Assert.True(bounds.MinRows >= DashboardLayout.MinimumSpan, $"{kind} minimum height is below the grid minimum");
            Assert.True(bounds.MaxColumns <= DashboardLayout.Columns, $"{kind} can grow off the grid");
            Assert.True(bounds.MaxRows <= DashboardLayout.Rows, $"{kind} can grow off the grid");
            Assert.True(bounds.MinColumns <= bounds.MaxColumns, $"{kind} cannot be both at least and at most that wide");
            Assert.True(bounds.MinRows <= bounds.MaxRows, $"{kind} cannot be both at least and at most that tall");
        }
    }

    [Fact]
    public void DefaultLayout_RespectsEveryCardsOwnBounds()
    {
        foreach (var card in DashboardLayout.CreateDefault().Cards)
        {
            var bounds = CardSizing.For(card.Kind);

            Assert.InRange(card.ColumnSpan, bounds.MinColumns, bounds.MaxColumns);
            Assert.InRange(card.RowSpan, bounds.MinRows, bounds.MaxRows);
        }
    }

    [Fact]
    public void FromCards_SeparatesCardsThatLoadBackOverlapping()
    {
        // A layout saved when the mixer could be three rows tall loads back four
        // rows tall, which would otherwise leave it sitting on its neighbour.
        var layout = DashboardLayout.FromCards(
        [
            new DashboardCard("mixer", CardKind.AppMixer, 0, 0, 12, 3),
            new DashboardCard("now", CardKind.NowPlaying, 0, 3, 4, 3),
        ]);

        var mixer = layout.Get("mixer")!;
        var now = layout.Get("now")!;

        Assert.False(mixer.Overlaps(now));
    }

    [Fact]
    public void DefaultLayout_HasNoOverlappingCards()
    {
        var cards = DashboardLayout.CreateDefault().Cards;

        for (var i = 0; i < cards.Count; i++)
        {
            for (var j = i + 1; j < cards.Count; j++)
            {
                Assert.False(cards[i].Overlaps(cards[j]), $"{cards[i].Id} overlaps {cards[j].Id}");
            }
        }
    }

    [Fact]
    public void DefaultLayout_FitsEntirelyOnTheGrid()
    {
        foreach (var card in DashboardLayout.CreateDefault().Cards)
        {
            Assert.True(card.Right <= DashboardLayout.Columns, $"{card.Id} runs off the right");
            Assert.True(card.Bottom <= DashboardLayout.Rows, $"{card.Id} runs off the bottom");
            Assert.True(card.Column >= 0 && card.Row >= 0);
        }
    }

    [Fact]
    public void DefaultLayout_GivesTheMixerMostOfTheHeight()
    {
        // The complaint this answers: full-width sliders eating the top while
        // the strips float in dead space.
        var mixer = DashboardLayout.CreateDefault().Cards.Single(c => c.Kind == CardKind.AppMixer);

        Assert.True(mixer.RowSpan >= DashboardLayout.Rows / 2);
        Assert.Equal(DashboardLayout.Columns, mixer.ColumnSpan);
    }

    [Fact]
    public void DefaultLayout_DoesNotStretchMasterAcrossTheFullWidth()
    {
        var master = DashboardLayout.CreateDefault().Cards.Single(c => c.Kind == CardKind.MasterOutput);

        Assert.True(master.ColumnSpan < DashboardLayout.Columns);
    }

    [Fact]
    public void MovingPastAnEdge_SlidesBackOnScreen()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Move("master", column: 99, row: 99);
        var master = layout.Get("master")!;

        Assert.True(master.Right <= DashboardLayout.Columns);
        Assert.True(master.Bottom <= DashboardLayout.Rows);
    }

    [Fact]
    public void MovingToNegativeCoordinates_ClampsToTheOrigin()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Move("master", column: -5, row: -5);
        var master = layout.Get("master")!;

        Assert.Equal(0, master.Column);
        Assert.Equal(0, master.Row);
    }

    [Fact]
    public void ResizingBelowTheMinimum_KeepsACardUsable()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Resize("status", columnSpan: 0, rowSpan: 0);
        var status = layout.Get("status")!;

        Assert.Equal(DashboardLayout.MinimumSpan, status.ColumnSpan);
        Assert.Equal(DashboardLayout.MinimumSpan, status.RowSpan);
    }

    [Fact]
    public void GrowingACardAtTheEdge_PullsItBackRatherThanOverflowing()
    {
        var layout = new DashboardLayout();
        layout.Add(Card("a", column: 10, row: 10, columnSpan: 2, rowSpan: 2));

        layout.Resize("a", columnSpan: 6, rowSpan: 6);
        var card = layout.Get("a")!;

        Assert.Equal(6, card.ColumnSpan);
        Assert.True(card.Right <= DashboardLayout.Columns);
        Assert.True(card.Bottom <= DashboardLayout.Rows);
    }

    [Fact]
    public void TheSameKindCanBePlacedMoreThanOnce()
    {
        var layout = new DashboardLayout();
        layout.Add(new DashboardCard("one", CardKind.MasterOutput, 0, 0, 3, 3));
        layout.Add(new DashboardCard("two", CardKind.MasterOutput, 3, 0, 3, 3));

        Assert.Equal(2, layout.Cards.Count);
    }

    [Fact]
    public void RemovingACard_LeavesTheRest()
    {
        var layout = DashboardLayout.CreateDefault();
        var before = layout.Cards.Count;

        layout.Remove("status");

        Assert.Equal(before - 1, layout.Cards.Count);
        Assert.Null(layout.Get("status"));
    }

    [Fact]
    public void FindFreeSlot_AvoidsExistingCards()
    {
        var layout = new DashboardLayout();
        layout.Add(Card("a", 0, 0, columnSpan: 6, rowSpan: 6));

        var slot = FindSlotOrFail(layout, 3, 3);
        var candidate = new DashboardCard("new", CardKind.QuickActions, slot.Column, slot.Row, 3, 3);

        Assert.DoesNotContain(layout.Cards, existing => existing.Overlaps(candidate));
    }

    [Fact]
    public void FindFreeSlot_ReturnsNullWhenTheGridIsFull()
    {
        var layout = new DashboardLayout();
        layout.Add(FullGridCard("full"));

        Assert.Null(layout.FindFreeSlot(3, 3));
    }

    [Fact]
    public void FindFreeSlot_OnAnEmptyGridStartsAtTheOrigin()
    {
        var slot = FindSlotOrFail(new DashboardLayout(), 4, 4);

        Assert.Equal((0, 0), slot);
    }

    [Fact]
    public void DroppingOnAnotherCard_PushesItOutOfTheWay()
    {
        var layout = new DashboardLayout();
        layout.Add(Card("a", 0, 0, columnSpan: 4, rowSpan: 4));
        layout.Add(Card("b", 4, 0, columnSpan: 4, rowSpan: 4));

        // Drop "a" squarely on top of "b".
        Assert.True(layout.Place("a", 4, 0));

        var a = layout.Get("a")!;
        var b = layout.Get("b")!;

        Assert.Equal((4, 0), (a.Column, a.Row));
        Assert.False(a.Overlaps(b), "the displaced card should have moved clear");
    }

    [Fact]
    public void DroppingLeavesNoOverlapsAnywhere()
    {
        var layout = DashboardLayout.CreateDefault();

        layout.Place("status", 0, 0);

        var cards = layout.Cards;
        for (var i = 0; i < cards.Count; i++)
        {
            for (var j = i + 1; j < cards.Count; j++)
            {
                Assert.False(cards[i].Overlaps(cards[j]), $"{cards[i].Id} still overlaps {cards[j].Id}");
            }
        }
    }

    [Fact]
    public void DroppingWithNowhereToDisplaceTo_IsRefusedAndChangesNothing()
    {
        var layout = new DashboardLayout();
        layout.Add(Card("big", 0, 0, DashboardLayout.Columns, 10));
        layout.Add(Card("small", 0, 10, DashboardLayout.Columns, 2));

        // Dropping into the middle splits the grid into two five-row gaps, and
        // the ten-row card fits in neither, so the drop must be refused rather
        // than leaving cards stacked on top of each other.
        Assert.False(layout.Place("small", 0, 5));

        var small = layout.Get("small")!;
        var big = layout.Get("big")!;

        Assert.Equal((0, 10), (small.Column, small.Row));
        Assert.Equal((0, 0), (big.Column, big.Row));
    }

    [Fact]
    public void DroppingOnEmptySpace_JustMoves()
    {
        var layout = new DashboardLayout();
        layout.Add(Card("a", 0, 0, columnSpan: 3, rowSpan: 3));

        Assert.True(layout.Place("a", 6, 6));

        var a = layout.Get("a")!;
        Assert.Equal((6, 6), (a.Column, a.Row));
    }

    [Fact]
    public void FindFreeSlot_CanIgnoreTheCardBeingRelocated()
    {
        var layout = new DashboardLayout();
        layout.Add(FullGridCard("only"));

        // Blocked by itself unless excluded.
        Assert.Null(layout.FindFreeSlot(3, 3));
        Assert.NotNull(layout.FindFreeSlot(3, 3, ignoreId: "only"));
    }

    [Fact]
    public void SavedLayout_SurvivesARoundTrip()
    {
        var original = DashboardLayout.CreateDefault();
        original.Move("mixer", column: 0, row: 4);

        var restored = DashboardLayout.FromCards(original.Cards);

        Assert.Equal(original.Get("mixer"), restored.Get("mixer"));
    }

    [Fact]
    public void EmptySavedLayout_FallsBackToTheDefault()
    {
        // An empty dashboard would be a blank screen with no obvious way back.
        var restored = DashboardLayout.FromCards([]);

        Assert.NotEmpty(restored.Cards);
    }

    [Fact]
    public void OverlapDetection_TreatsSharedEdgesAsSeparate()
    {
        var left = Card("a", 0, 0, columnSpan: 4, rowSpan: 4);
        var right = Card("b", 4, 0, columnSpan: 4, rowSpan: 4);
        var covering = Card("c", 2, 2, columnSpan: 4, rowSpan: 4);

        Assert.False(left.Overlaps(right));
        Assert.False(right.Overlaps(left));
        Assert.True(left.Overlaps(covering));
        Assert.True(covering.Overlaps(left));
    }

    private static (int Column, int Row) FindSlotOrFail(DashboardLayout layout, int columnSpan, int rowSpan)
    {
        var slot = layout.FindFreeSlot(columnSpan, rowSpan);
        Assert.NotNull(slot);
        return slot!.Value;
    }
}
