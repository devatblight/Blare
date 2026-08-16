using BLight.Blare.Audio.Boost;

namespace Blare.Audio.Tests.Boost;

public class GainProcessorTests
{
    [Fact]
    public void ApplyGain_ScalesEachSample()
    {
        var samples = new float[] { 0.1f, -0.2f, 0.3f };

        GainProcessor.ApplyGain(samples, 2.0f);

        Assert.Equal(0.2f, samples[0], precision: 5);
        Assert.Equal(-0.4f, samples[1], precision: 5);
        Assert.Equal(0.6f, samples[2], precision: 5);
    }

    [Fact]
    public void ApplyGain_UnityGain_LeavesSamplesUnchanged()
    {
        var samples = new float[] { 0.5f, -0.5f };

        GainProcessor.ApplyGain(samples, 1.0f);

        Assert.Equal(0.5f, samples[0]);
        Assert.Equal(-0.5f, samples[1]);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(6.0206f, 2f)]
    [InlineData(-6.0206f, 0.5f)]
    public void DecibelsToLinear_MatchesKnownConversions(float decibels, float expectedLinear)
    {
        var linear = GainProcessor.DecibelsToLinear(decibels);

        Assert.Equal(expectedLinear, linear, precision: 3);
    }

    [Fact]
    public void LinearToDecibels_RoundTripsWithDecibelsToLinear()
    {
        const float originalDb = 12f;

        var linear = GainProcessor.DecibelsToLinear(originalDb);
        var backToDb = GainProcessor.LinearToDecibels(linear);

        Assert.Equal(originalDb, backToDb, precision: 3);
    }

    [Fact]
    public void LinearToDecibels_Zero_ReturnsNegativeInfinity()
    {
        Assert.Equal(float.NegativeInfinity, GainProcessor.LinearToDecibels(0f));
    }
}
