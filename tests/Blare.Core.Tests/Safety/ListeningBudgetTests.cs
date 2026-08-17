using Blight.Blare.Core.Safety;

namespace Blare.Core.Tests.Safety;

public class ListeningBudgetTests
{
    private static readonly ListeningBudget TwoHours = new(TimeSpan.FromMinutes(120));

    [Fact]
    public void NothingUsed_IsAnEmptyRing()
    {
        Assert.Equal(0, TwoHours.FractionUsed(TimeSpan.Zero));
        Assert.Equal(BudgetState.Healthy, TwoHours.StateFor(TimeSpan.Zero));
    }

    [Fact]
    public void HalfUsed_IsHalfTheRing()
    {
        Assert.Equal(0.5, TwoHours.FractionUsed(TimeSpan.FromMinutes(60)), precision: 6);
    }

    [Fact]
    public void GoingOverDoesNotOverdrawTheRing()
    {
        // A ring drawn past full wraps around and reads as nearly empty.
        Assert.Equal(1, TwoHours.FractionUsed(TimeSpan.FromMinutes(400)));
    }

    [Fact]
    public void RemainingNeverGoesNegative()
    {
        Assert.Equal(TimeSpan.Zero, TwoHours.Remaining(TimeSpan.FromMinutes(500)));
    }

    [Fact]
    public void ThreeQuartersIn_StartsWarning()
    {
        Assert.Equal(BudgetState.Healthy, TwoHours.StateFor(TimeSpan.FromMinutes(89)));
        Assert.Equal(BudgetState.Nearing, TwoHours.StateFor(TimeSpan.FromMinutes(90)));
    }

    [Fact]
    public void ReachingTheAllowanceIsExceededNotNearing()
    {
        Assert.Equal(BudgetState.Exceeded, TwoHours.StateFor(TimeSpan.FromMinutes(120)));
    }

    [Fact]
    public void AZeroAllowanceIsAlwaysSpentRatherThanDividingByZero()
    {
        var none = new ListeningBudget(TimeSpan.Zero);

        Assert.Equal(1, none.FractionUsed(TimeSpan.Zero));
        Assert.Equal(BudgetState.Exceeded, none.StateFor(TimeSpan.Zero));
    }

    [Fact]
    public void TheDescriptionChangesWithTheState()
    {
        Assert.Contains("used today", TwoHours.Describe(TimeSpan.FromMinutes(10)));
        Assert.Contains("left today", TwoHours.Describe(TimeSpan.FromMinutes(100)));
        Assert.Contains("used today's", TwoHours.Describe(TimeSpan.FromMinutes(200)));
    }
}
