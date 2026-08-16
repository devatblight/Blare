using BLight.Blare.Audio.Analysis;

namespace Blare.Audio.Tests.Analysis;

public class SpectrumAnalyzerTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void Silence_ProducesNoBandActivity()
    {
        var analyzer = new SpectrumAnalyzer(sampleRateHz: SampleRate);

        FeedTone(analyzer, frequencyHz: 0, amplitude: 0f, frames: 4096);

        Assert.All(analyzer.Bands.ToArray(), level => Assert.True(level < 0.05, $"expected near-zero, got {level}"));
    }

    [Fact]
    public void BassTone_LightsUpLowerBandsNotUpperBands()
    {
        var analyzer = new SpectrumAnalyzer(bandCount: 14, sampleRateHz: SampleRate);

        FeedTone(analyzer, frequencyHz: 80, amplitude: 0.8f, frames: 8192);

        var bands = analyzer.Bands.ToArray();
        var lowerHalfPeak = bands.Take(bands.Length / 2).Max();
        var upperHalfPeak = bands.Skip(bands.Length / 2).Max();

        Assert.True(lowerHalfPeak > upperHalfPeak, $"bass tone should favour low bands (low={lowerHalfPeak}, high={upperHalfPeak})");
    }

    [Fact]
    public void TrebleTone_LightsUpUpperBandsNotLowerBands()
    {
        var analyzer = new SpectrumAnalyzer(bandCount: 14, sampleRateHz: SampleRate);

        FeedTone(analyzer, frequencyHz: 9000, amplitude: 0.8f, frames: 8192);

        var bands = analyzer.Bands.ToArray();
        var lowerHalfPeak = bands.Take(bands.Length / 2).Max();
        var upperHalfPeak = bands.Skip(bands.Length / 2).Max();

        Assert.True(upperHalfPeak > lowerHalfPeak, $"treble tone should favour high bands (low={lowerHalfPeak}, high={upperHalfPeak})");
    }

    [Fact]
    public void LouderSignal_ProducesHigherBandLevelsThanQuieterSignal()
    {
        var loud = new SpectrumAnalyzer(sampleRateHz: SampleRate);
        var quiet = new SpectrumAnalyzer(sampleRateHz: SampleRate);

        FeedTone(loud, frequencyHz: 1000, amplitude: 0.9f, frames: 8192);
        FeedTone(quiet, frequencyHz: 1000, amplitude: 0.05f, frames: 8192);

        Assert.True(loud.Bands.ToArray().Max() > quiet.Bands.ToArray().Max());
    }

    [Fact]
    public void AllBandLevels_StayWithinZeroToOne()
    {
        var analyzer = new SpectrumAnalyzer(sampleRateHz: SampleRate);

        // Deliberately over-unity input — clipping-loud content must not push bands out of range.
        FeedTone(analyzer, frequencyHz: 500, amplitude: 4.0f, frames: 8192);

        Assert.All(analyzer.Bands.ToArray(), level => Assert.InRange(level, 0.0, 1.0));
    }

    [Fact]
    public void Decay_PullsBandsDownTowardZero()
    {
        var analyzer = new SpectrumAnalyzer(sampleRateHz: SampleRate);
        FeedTone(analyzer, frequencyHz: 1000, amplitude: 0.9f, frames: 8192);

        var before = analyzer.Bands.ToArray().Max();
        for (var i = 0; i < 50; i++)
        {
            analyzer.Decay();
        }

        var after = analyzer.Bands.ToArray().Max();

        Assert.True(after < before, $"decay should reduce levels ({before} -> {after})");
    }

    [Fact]
    public void AddSamples_ReportsWhenAFullFrameWasProcessed()
    {
        var analyzer = new SpectrumAnalyzer(fftSize: 1024, sampleRateHz: SampleRate, channels: 2);

        // 100 stereo frames is far short of the 1024 needed for a transform.
        Assert.False(analyzer.AddSamples(new float[200]));

        // Topping past the FFT size must trigger one.
        Assert.True(analyzer.AddSamples(new float[4096]));
    }

    [Fact]
    public void NonPowerOfTwoFftSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => new SpectrumAnalyzer(fftSize: 1000));
    }

    private static void FeedTone(SpectrumAnalyzer analyzer, double frequencyHz, float amplitude, int frames)
    {
        const int channels = 2;
        var samples = new float[frames * channels];

        for (var frame = 0; frame < frames; frame++)
        {
            var value = amplitude * (float)Math.Sin(2.0 * Math.PI * frequencyHz * frame / SampleRate);
            samples[frame * channels] = value;
            samples[frame * channels + 1] = value;
        }

        analyzer.AddSamples(samples);
    }
}
