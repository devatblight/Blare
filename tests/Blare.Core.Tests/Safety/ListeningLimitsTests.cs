using Blight.Blare.Core.Safety;

namespace Blare.Core.Tests.Safety;

public class QuietHoursTests
{
    private static QuietHours Overnight(double ceiling = 40) =>
        new(true, new TimeOnly(23, 0), new TimeOnly(7, 0), ceiling);

    [Fact]
    public void AWindowRunningPastMidnight_CoversBothSidesOfIt()
    {
        var quiet = Overnight();

        Assert.True(quiet.Contains(new TimeOnly(23, 30)));
        Assert.True(quiet.Contains(new TimeOnly(2, 0)));
        Assert.True(quiet.Contains(new TimeOnly(6, 59)));
    }

    [Fact]
    public void AWindowRunningPastMidnight_ExcludesTheDay()
    {
        var quiet = Overnight();

        Assert.False(quiet.Contains(new TimeOnly(7, 0)));
        Assert.False(quiet.Contains(new TimeOnly(14, 0)));
        Assert.False(quiet.Contains(new TimeOnly(22, 59)));
    }

    [Fact]
    public void AWindowInsideOneDay_BehavesNormally()
    {
        var quiet = new QuietHours(true, new TimeOnly(13, 0), new TimeOnly(14, 0), 30);

        Assert.True(quiet.Contains(new TimeOnly(13, 30)));
        Assert.False(quiet.Contains(new TimeOnly(12, 59)));
        Assert.False(quiet.Contains(new TimeOnly(14, 0)));
    }

    [Fact]
    public void AZeroLengthWindow_IsOffRatherThanAlwaysOn()
    {
        var quiet = new QuietHours(true, new TimeOnly(3, 0), new TimeOnly(3, 0), 10);

        Assert.False(quiet.Contains(new TimeOnly(3, 0)));
        Assert.False(quiet.Contains(new TimeOnly(15, 0)));
    }

    [Fact]
    public void DisabledQuietHours_NeverApply()
    {
        var quiet = Overnight() with { Enabled = false };

        Assert.False(quiet.Contains(new TimeOnly(2, 0)));
        Assert.Equal(100, quiet.CeilingAt(new TimeOnly(2, 0)));
    }

    [Fact]
    public void OutsideTheWindow_ThereIsNoCeiling()
    {
        Assert.Equal(100, Overnight().CeilingAt(new TimeOnly(12, 0)));
    }
}

public class ListeningLimitsTests
{
    [Fact]
    public void WithNoLimits_AnythingIsAllowed()
    {
        var limits = new ListeningLimits();

        Assert.Equal(100, limits.Apply("discord", 100, new TimeOnly(12, 0)));
    }

    [Fact]
    public void APerAppCap_HoldsWhateverIsRequested()
    {
        var limits = new ListeningLimits();
        limits.SetCap("discord", 60);

        Assert.Equal(60, limits.Apply("discord", 100, new TimeOnly(12, 0)));
        Assert.Equal(30, limits.Apply("discord", 30, new TimeOnly(12, 0)));
    }

    [Fact]
    public void ACapAppliesOnlyToItsOwnApp()
    {
        var limits = new ListeningLimits();
        limits.SetCap("discord", 60);

        Assert.Equal(100, limits.Apply("spotify", 100, new TimeOnly(12, 0)));
    }

    [Fact]
    public void AppKeysAreMatchedIgnoringCase()
    {
        // App keys come from executable paths, whose casing Windows does not
        // preserve consistently.
        var limits = new ListeningLimits();
        limits.SetCap(@"C:\Apps\Discord.exe", 55);

        Assert.Equal(55, limits.CapFor(@"c:\apps\discord.exe"));
    }

    [Fact]
    public void TheLowestLimitWins()
    {
        var limits = new ListeningLimits();
        limits.SetCap("discord", 80);
        limits.SetQuietHours(new QuietHours(true, new TimeOnly(23, 0), new TimeOnly(7, 0), 40));

        // Quiet hours are stricter than the app's own cap.
        Assert.Equal(40, limits.Apply("discord", 100, new TimeOnly(2, 0)));

        // And outside them the app's cap is still the limit.
        Assert.Equal(80, limits.Apply("discord", 100, new TimeOnly(12, 0)));
    }

    [Fact]
    public void APermissiveAppCap_DoesNotOverrideQuietHours()
    {
        var limits = new ListeningLimits();
        limits.SetCap("spotify", 100);
        limits.SetQuietHours(new QuietHours(true, new TimeOnly(23, 0), new TimeOnly(7, 0), 25));

        Assert.Equal(25, limits.Apply("spotify", 100, new TimeOnly(1, 0)));
    }

    [Fact]
    public void ClearingACap_RemovesTheLimit()
    {
        var limits = new ListeningLimits();
        limits.SetCap("discord", 40);
        limits.ClearCap("discord");

        Assert.Null(limits.CapFor("discord"));
        Assert.Equal(100, limits.Apply("discord", 100, new TimeOnly(12, 0)));
    }

    [Fact]
    public void CapsAreClampedToSomethingSettable()
    {
        var limits = new ListeningLimits();
        limits.SetCap("a", 500);
        limits.SetCap("b", -20);

        Assert.Equal(100, limits.CapFor("a"));
        Assert.Equal(0, limits.CapFor("b"));
    }

    [Fact]
    public void ChangingALimit_RaisesChangedSoItCanBePersisted()
    {
        var limits = new ListeningLimits();
        var changes = 0;
        limits.Changed += (_, _) => changes++;

        limits.SetCap("discord", 50);
        limits.ClearCap("discord");
        limits.SetQuietHours(QuietHours.Off);

        Assert.Equal(3, changes);
    }

    [Fact]
    public void ClearingACapThatWasNeverSet_ChangesNothing()
    {
        var limits = new ListeningLimits();
        var changes = 0;
        limits.Changed += (_, _) => changes++;

        limits.ClearCap("nothing");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void LimitsSurviveARoundTrip()
    {
        var limits = new ListeningLimits();
        limits.SetCap("discord", 45);
        var quiet = new QuietHours(true, new TimeOnly(22, 0), new TimeOnly(6, 0), 20);

        var restored = new ListeningLimits();
        restored.Restore(limits.Snapshot(), quiet);

        Assert.Equal(45, restored.CapFor("discord"));
        Assert.Equal(20, restored.QuietHours.CeilingPercent);
    }
}
