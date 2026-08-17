using Blight.Blare.Audio.Boost;

namespace Blare.Audio.Tests.Boost;

/// <summary>
/// These guard the last thing standing between a maths error and someone's
/// hearing, so they cover the ugly inputs rather than the happy path.
/// </summary>
public class BoostSafetyTests
{
    [Fact]
    public void NaN_IsReplacedWithSilence()
    {
        // The critical case: a NaN reaching the audio device is reproduced as
        // full-scale noise — the loudest sound the hardware can produce.
        var samples = new[] { float.NaN, 0.1f };

        BoostSafety.Enforce(samples);

        Assert.Equal(0f, samples[0]);
    }

    [Fact]
    public void Infinities_AreReplacedWithSilence()
    {
        var samples = new[] { float.PositiveInfinity, float.NegativeInfinity };

        BoostSafety.Enforce(samples);

        Assert.Equal(0f, samples[0]);
        Assert.Equal(0f, samples[1]);
    }

    [Fact]
    public void LoudSamples_AreClampedToTheCeiling()
    {
        var samples = new[] { 5.0f, -5.0f, 0.99f };

        BoostSafety.Enforce(samples);

        Assert.Equal(BoostSafety.AbsoluteCeiling, samples[0]);
        Assert.Equal(-BoostSafety.AbsoluteCeiling, samples[1]);
        Assert.Equal(BoostSafety.AbsoluteCeiling, samples[2]);
    }

    [Fact]
    public void NothingEverExceedsTheCeiling_ForAnyInput()
    {
        var samples = new[] { 0f, 1f, -1f, 1e30f, -1e30f, float.NaN, float.Epsilon, -0.5f, 12345.6f };

        BoostSafety.Enforce(samples);

        Assert.All(samples, s =>
        {
            Assert.True(float.IsFinite(s), $"non-finite sample survived: {s}");
            Assert.True(Math.Abs(s) <= BoostSafety.AbsoluteCeiling, $"sample exceeded ceiling: {s}");
        });
    }

    [Fact]
    public void QuietAudio_PassesThroughUntouched()
    {
        var samples = new[] { 0.1f, -0.2f, 0.3f };
        var original = (float[])samples.Clone();

        var corrections = BoostSafety.Enforce(samples);

        Assert.Equal(0, corrections);
        Assert.Equal(original, samples);
    }

    [Fact]
    public void Corrections_AreCounted()
    {
        var samples = new[] { 5f, 0.1f, float.NaN, 0.2f };

        Assert.Equal(2, BoostSafety.Enforce(samples));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0f)]
    [InlineData(-3f)]
    public void BadGain_CollapsesToUnity(float gain)
    {
        // A NaN gain would turn every subsequent sample into NaN, so it must
        // never be applied.
        Assert.Equal(1f, BoostSafety.SanitizeGain(gain));
    }

    [Fact]
    public void ExcessiveGain_IsCappedNotApplied()
    {
        Assert.Equal(BoostSafety.MaxTotalGain, BoostSafety.SanitizeGain(1e9f));
    }

    [Fact]
    public void LegitimateBoostGain_SurvivesSanitising()
    {
        // 3x boost undoing a 50x residual attenuation — the real worst case.
        const float realistic = 3f / 0.02f;

        Assert.Equal(realistic, BoostSafety.SanitizeGain(realistic));
        Assert.True(realistic < BoostSafety.MaxTotalGain, "the legitimate maximum must sit inside the hard cap");
    }

    [Fact]
    public void OccasionalClamping_IsNotTreatedAsRunaway()
    {
        Assert.False(BoostSafety.IsRunaway(corrections: 5, sampleCount: 1000));
    }

    [Fact]
    public void MostOfABlockClamping_IsARunaway()
    {
        Assert.True(BoostSafety.IsRunaway(corrections: 900, sampleCount: 1000));
    }

    [Fact]
    public void EmptyBlock_IsNeverARunaway()
    {
        Assert.False(BoostSafety.IsRunaway(corrections: 0, sampleCount: 0));
    }

    [Fact]
    public void GainThenLimiterThenGuard_NeverExceedsTheCeiling()
    {
        // End-to-end on the real chain, with content already at full scale and
        // a large boost on top.
        var limiter = new Limiter();
        var samples = Enumerable.Range(0, 2000)
            .Select(i => 0.95f * MathF.Sin(i * 0.05f))
            .ToArray();

        GainProcessor.ApplyGain(samples, BoostSafety.SanitizeGain(3f / BoostEngine.ResidualLevel));
        limiter.Process(samples);
        BoostSafety.Enforce(samples);

        Assert.All(samples, s => Assert.True(
            float.IsFinite(s) && Math.Abs(s) <= BoostSafety.AbsoluteCeiling,
            $"sample escaped the chain at {s}"));
    }
}
