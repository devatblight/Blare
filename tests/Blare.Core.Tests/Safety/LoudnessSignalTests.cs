using Blight.Blare.Core.Safety;

namespace Blare.Core.Tests.Safety;

/// <summary>
/// Guards the definition of "loud", which is the whole basis of the health
/// feature. The rule that matters most: Windows defaults every app session to
/// 100%, so an unattenuated app must never be treated as loud on its own.
/// </summary>
public class LoudnessSignalTests
{
    private const double LoudLevel = 0.5;
    private const double SilenceFloor = 0.02;

    // Mirrors SafetyMonitor's rule. Kept here because SafetyMonitor lives in the
    // WinUI app project, which can't be referenced from a plain test assembly.
    private static bool IsLoud(double peakLevel, double masterVolume) =>
        peakLevel > SilenceFloor && Math.Clamp(peakLevel, 0, 1) * Math.Clamp(masterVolume, 0, 1) >= LoudLevel;

    [Fact]
    public void SilentAppAtDefaultVolume_IsNotLoud()
    {
        // The regression that mattered: every Windows app sits at 100% session
        // volume by default. Playing nothing must never count as loud.
        Assert.False(IsLoud(peakLevel: 0.0, masterVolume: 1.0));
    }

    [Fact]
    public void BarelyAudibleApp_IsNotLoud()
    {
        Assert.False(IsLoud(peakLevel: 0.01, masterVolume: 1.0));
    }

    [Fact]
    public void QuietContentAtFullDeviceVolume_IsNotLoud()
    {
        Assert.False(IsLoud(peakLevel: 0.15, masterVolume: 1.0));
    }

    [Fact]
    public void LoudContentAtLowDeviceVolume_IsNotLoud()
    {
        // Turning the speakers down genuinely makes things safer, so a hot
        // stream through a quiet device must not be flagged.
        Assert.False(IsLoud(peakLevel: 0.95, masterVolume: 0.2));
    }

    [Fact]
    public void LoudContentAtFullDeviceVolume_IsLoud()
    {
        Assert.True(IsLoud(peakLevel: 0.9, masterVolume: 1.0));
    }

    [Fact]
    public void ModerateContentAtFullDeviceVolume_CrossesTheThresholdExactly()
    {
        Assert.True(IsLoud(peakLevel: 0.5, masterVolume: 1.0));
        Assert.False(IsLoud(peakLevel: 0.49, masterVolume: 1.0));
    }

    [Fact]
    public void LoweringDeviceVolume_TakesAnAppOutOfTheLoudState()
    {
        Assert.True(IsLoud(peakLevel: 0.8, masterVolume: 1.0));
        Assert.False(IsLoud(peakLevel: 0.8, masterVolume: 0.5));
    }

    [Fact]
    public void LoudTime_OnlyAccumulatesWhileActuallyLoud()
    {
        var tracker = new LoudnessTracker();
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        // An app sitting at Windows' default 100% but playing nothing.
        tracker.RecordSample(new LoudnessSample(start, "idle.exe", 0f, IsLoud(0, 1.0)));
        var window = tracker.RecordSample(
            new LoudnessSample(start + TimeSpan.FromMinutes(30), "idle.exe", 0f, IsLoud(0, 1.0)));

        Assert.Equal(TimeSpan.Zero, window.TimeAboveThreshold);
        Assert.Equal(TimeSpan.FromMinutes(30), window.TotalDuration);
    }
}
