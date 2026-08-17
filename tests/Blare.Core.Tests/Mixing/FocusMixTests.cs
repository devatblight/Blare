using Blight.Blare.Core.Mixing;

namespace Blare.Core.Tests.Mixing;

public class FocusMixTests
{
    private static readonly FocusLevel[] Desk =
    [
        new("spotify", 80),
        new("discord", 90),
        new("chrome", 15),
    ];

    [Fact]
    public void FocusedApp_GoesToFullLevel()
    {
        var result = FocusMix.Apply(Desk, "spotify");

        Assert.Equal(100, result.Single(l => l.AppKey == "spotify").VolumePercent);
    }

    [Fact]
    public void OtherApps_AreDuckedToTheTarget()
    {
        var result = FocusMix.Apply(Desk, "spotify", duckToPercent: 25);

        Assert.Equal(25, result.Single(l => l.AppKey == "discord").VolumePercent);
    }

    [Fact]
    public void AppsAlreadyQuieterThanTheTarget_AreLeftAlone()
    {
        // chrome sits at 15, below the 25 duck target — focusing something else
        // must not raise it.
        var result = FocusMix.Apply(Desk, "spotify", duckToPercent: 25);

        Assert.Equal(15, result.Single(l => l.AppKey == "chrome").VolumePercent);
    }

    [Fact]
    public void EveryAppIsAccountedFor()
    {
        var result = FocusMix.Apply(Desk, "spotify");

        Assert.Equal(Desk.Length, result.Count);
    }

    [Fact]
    public void DuckTarget_IsClampedIntoRange()
    {
        var result = FocusMix.Apply(Desk, "spotify", duckToPercent: 500);

        // A nonsensical duck target must not push other apps above their level.
        Assert.Equal(90, result.Single(l => l.AppKey == "discord").VolumePercent);
    }

    [Fact]
    public void FocusingWithNoKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => FocusMix.Apply(Desk, string.Empty));
    }

    [Fact]
    public void Restore_ReturnsSavedLevelsForAppsStillPresent()
    {
        var restored = FocusMix.Restore(Desk, ["spotify", "discord", "chrome"]);

        Assert.Equal(3, restored.Count);
        Assert.Equal(80, restored.Single(l => l.AppKey == "spotify").VolumePercent);
    }

    [Fact]
    public void Restore_DropsAppsThatClosedWhileFocused()
    {
        var restored = FocusMix.Restore(Desk, ["spotify"]);

        Assert.Single(restored);
        Assert.Equal("spotify", restored[0].AppKey);
    }
}
