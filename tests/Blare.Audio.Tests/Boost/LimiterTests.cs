using Blight.Blare.Audio.Boost;

namespace Blare.Audio.Tests.Boost;

public class LimiterTests
{
    [Fact]
    public void LoudConstantSignal_NeverExceedsCeilingInOutput()
    {
        var limiter = new Limiter(ceilingLinear: 0.9f, lookaheadSamples: 32, releaseTimeSeconds: 0.05f, sampleRateHz: 1000);
        var samples = Enumerable.Repeat(2.0f, 500).ToArray(); // way over ceiling, sustained

        limiter.Process(samples);

        Assert.All(samples, s => Assert.True(MathF.Abs(s) <= 0.9f + 1e-4f));
    }

    [Fact]
    public void SuddenLoudSpike_IsCaughtBeforeItReachesOutput()
    {
        // Lookahead should catch the spike ahead of time — the standard
        // "attack after the fact" limiter would let at least one full-amplitude
        // sample through; this one shouldn't.
        var limiter = new Limiter(ceilingLinear: 0.9f, lookaheadSamples: 64, releaseTimeSeconds: 0.05f, sampleRateHz: 1000);
        var samples = new float[300];
        Array.Fill(samples, 0.1f);
        samples[200] = 5.0f; // one huge spike buried in an otherwise quiet signal

        limiter.Process(samples);

        Assert.All(samples, s => Assert.True(MathF.Abs(s) <= 0.9f + 1e-4f));
    }

    [Fact]
    public void QuietSignal_PassesThroughAtUnityGain()
    {
        var limiter = new Limiter(ceilingLinear: 0.9f, lookaheadSamples: 16, releaseTimeSeconds: 0.05f, sampleRateHz: 1000);
        var input = Enumerable.Range(0, 200).Select(i => 0.1f * MathF.Sin(i * 0.1f)).ToArray();
        var samples = (float[])input.Clone();

        limiter.Process(samples);

        // After the lookahead delay has fully flushed, output should match the (delayed) input closely.
        for (var i = 100; i < 200; i++)
        {
            Assert.Equal(input[i - 16], samples[i], precision: 3);
        }
    }

    [Fact]
    public void GainRecoversAfterTransientPasses()
    {
        var limiter = new Limiter(ceilingLinear: 0.9f, lookaheadSamples: 16, releaseTimeSeconds: 0.02f, sampleRateHz: 1000);
        var samples = new float[400];
        Array.Fill(samples, 0.1f, 0, 50);
        Array.Fill(samples, 3.0f, 50, 20); // loud burst
        Array.Fill(samples, 0.1f, 70, samples.Length - 70); // back to quiet, should recover

        limiter.Process(samples);

        // well after the burst and its lookahead window have passed, gain should have
        // recovered enough that quiet samples are no longer being crushed
        var tailSample = samples[^1];
        Assert.True(MathF.Abs(tailSample) > 0.08f, $"expected recovery close to unity gain, got {tailSample}");
    }

    [Fact]
    public void Ceiling_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Limiter(ceilingLinear: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Limiter(ceilingLinear: 1.5f));
    }
}
