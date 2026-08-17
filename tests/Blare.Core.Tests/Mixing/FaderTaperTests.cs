using Blight.Blare.Core.Mixing;

namespace Blare.Core.Tests.Mixing;

public class FaderTaperTests
{
    [Fact]
    public void TopOfTheFader_IsUnityGain()
    {
        Assert.Equal(1.0, FaderTaper.ToGain(1), precision: 6);
    }

    [Fact]
    public void BottomOfTheFader_IsSilentRatherThanVeryQuiet()
    {
        // -60 dB is inaudible but not zero, and a fader at the floor must
        // actually mute rather than leave a whisper.
        Assert.Equal(0, FaderTaper.ToGain(0));
    }

    [Fact]
    public void Halfway_IsThirtyDecibelsDown()
    {
        var expected = Math.Pow(10, -30.0 / 20);

        Assert.Equal(expected, FaderTaper.ToGain(0.5), precision: 6);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(1.0)]
    public void PositionAndGain_RoundTrip(double position)
    {
        Assert.Equal(position, FaderTaper.ToPosition(FaderTaper.ToGain(position)), precision: 6);
    }

    [Fact]
    public void TheTaper_GivesQuietLevelsMoreTravelThanLinearWould()
    {
        // The whole point: the bottom of a linear fader is unusable because
        // every quiet level is squeezed into it.
        var lowerHalf = FaderTaper.ToGain(0.5) - FaderTaper.ToGain(0.25);
        var upperHalf = FaderTaper.ToGain(1.0) - FaderTaper.ToGain(0.75);

        Assert.True(upperHalf > lowerHalf,
            "the top of the fader should cover more amplitude than the bottom");
    }

    [Fact]
    public void GainNeverLeavesTheValidRange()
    {
        foreach (var position in new[] { -5.0, 0, 0.3, 1, 4 })
        {
            var gain = FaderTaper.ToGain(position);

            Assert.InRange(gain, 0, 1);
        }
    }

    [Fact]
    public void PercentHelpers_MatchTheScalarVersions()
    {
        Assert.Equal(FaderTaper.ToGain(0.4) * 100, FaderTaper.GainPercentFor(40), precision: 6);
        Assert.Equal(FaderTaper.ToPosition(0.4) * 100, FaderTaper.PositionPercentFor(40), precision: 6);
    }
}
