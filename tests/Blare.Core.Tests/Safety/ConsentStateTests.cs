using BLight.Blare.Core.Safety;

namespace Blare.Core.Tests.Safety;

public class ConsentStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoRecord_IsNotActive()
    {
        var state = new ConsentState();

        Assert.False(state.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
    }

    [Fact]
    public void Grant_IsActiveImmediately()
    {
        var state = new ConsentState();

        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        Assert.True(state.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
    }

    [Fact]
    public void Grant_RemainsActiveWithinReconfirmationInterval()
    {
        var state = new ConsentState(reconfirmationInterval: TimeSpan.FromDays(30));
        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        var stillWithinWindow = Start + TimeSpan.FromDays(29);

        Assert.True(state.IsActive(ConsentKind.SafetyWarningsDisabled, stillWithinWindow));
    }

    [Fact]
    public void Grant_ExpiresAfterReconfirmationInterval_RevertsToSafeDefault()
    {
        var state = new ConsentState(reconfirmationInterval: TimeSpan.FromDays(30));
        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        var pastWindow = Start + TimeSpan.FromDays(31);

        Assert.False(state.IsActive(ConsentKind.SafetyWarningsDisabled, pastWindow));
    }

    [Fact]
    public void HasExpired_TrueOnlyOnceGrantedConsentCrossesInterval()
    {
        var state = new ConsentState(reconfirmationInterval: TimeSpan.FromDays(30));
        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        Assert.False(state.HasExpired(ConsentKind.SafetyWarningsDisabled, Start + TimeSpan.FromDays(10)));
        Assert.True(state.HasExpired(ConsentKind.SafetyWarningsDisabled, Start + TimeSpan.FromDays(31)));
    }

    [Fact]
    public void HasExpired_FalseWhenNeverGranted()
    {
        var state = new ConsentState();

        Assert.False(state.HasExpired(ConsentKind.SafetyWarningsDisabled, Start));
    }

    [Fact]
    public void Revoke_ImmediatelyDeactivates()
    {
        var state = new ConsentState();
        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        state.Revoke(ConsentKind.SafetyWarningsDisabled);

        Assert.False(state.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
    }

    [Fact]
    public void DifferentConsentKinds_AreTrackedIndependently()
    {
        var state = new ConsentState();
        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        Assert.True(state.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
        Assert.False(state.IsActive(ConsentKind.SafeBoostCeilingOverride, Start));
    }
}
