namespace BLight.Blare.Audio.Analysis;

/// <summary>
/// Turns a stream of interleaved PCM float samples into a small set of
/// log-spaced frequency band levels (0..1) suitable for driving a spectrum
/// display.
///
/// Bands are log-spaced because pitch perception is logarithmic — linear
/// bins would put almost every visible band in the treble and squash all
/// the musically interesting bass/mid detail into the first bar.
///
/// Levels are smoothed with a fast attack / slow decay envelope so bars
/// snap up on transients but fall back gracefully, which is what makes a
/// meter read as a meter rather than as noise.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private const double MinimumDecibels = -70.0;

    private readonly int _fftSize;
    private readonly int _sampleRateHz;
    private readonly int _channels;
    private readonly double[] _window;
    private readonly double[] _sampleBuffer;
    private readonly double[] _real;
    private readonly double[] _imaginary;
    private readonly int[] _bandEdgeBins;
    private readonly double[] _bandLevels;
    private readonly double _attack;
    private readonly double _decay;

    private int _bufferedSamples;

    public SpectrumAnalyzer(
        int bandCount = 14,
        int fftSize = 1024,
        int sampleRateHz = 48000,
        int channels = 2,
        double attack = 0.6,
        double decay = 0.12)
    {
        if (fftSize <= 0 || (fftSize & (fftSize - 1)) != 0)
        {
            throw new ArgumentException("FFT size must be a positive power of two.", nameof(fftSize));
        }

        if (bandCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bandCount));
        }

        _fftSize = fftSize;
        _sampleRateHz = sampleRateHz;
        _channels = Math.Max(1, channels);
        _attack = attack;
        _decay = decay;

        _sampleBuffer = new double[fftSize];
        _real = new double[fftSize];
        _imaginary = new double[fftSize];
        _bandLevels = new double[bandCount];

        // Hann window — cheap and good enough to keep a steady tone from
        // smearing across neighbouring bins.
        _window = new double[fftSize];
        for (var i = 0; i < fftSize; i++)
        {
            _window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1)));
        }

        _bandEdgeBins = BuildLogBandEdges(bandCount, fftSize, sampleRateHz);
    }

    public int BandCount => _bandLevels.Length;

    /// <summary>Current smoothed band levels, each 0..1. The returned span is reused between calls — copy it if you need to retain it.</summary>
    public ReadOnlySpan<double> Bands => _bandLevels;

    /// <summary>
    /// Feeds interleaved samples. Channels are downmixed to mono. Returns
    /// true when at least one full FFT frame was processed (i.e. the band
    /// levels changed).
    /// </summary>
    public bool AddSamples(ReadOnlySpan<float> interleavedSamples)
    {
        var processedAnyFrame = false;

        for (var i = 0; i + _channels <= interleavedSamples.Length; i += _channels)
        {
            double sum = 0;
            for (var c = 0; c < _channels; c++)
            {
                sum += interleavedSamples[i + c];
            }

            _sampleBuffer[_bufferedSamples++] = sum / _channels;

            if (_bufferedSamples == _fftSize)
            {
                ProcessFrame();
                _bufferedSamples = 0;
                processedAnyFrame = true;
            }
        }

        return processedAnyFrame;
    }

    /// <summary>Decays all bands toward zero — call when an app goes silent so bars fall instead of freezing.</summary>
    public void Decay()
    {
        for (var i = 0; i < _bandLevels.Length; i++)
        {
            _bandLevels[i] *= 1.0 - _decay;
        }
    }

    private void ProcessFrame()
    {
        for (var i = 0; i < _fftSize; i++)
        {
            _real[i] = _sampleBuffer[i] * _window[i];
            _imaginary[i] = 0.0;
        }

        Fft.Transform(_real, _imaginary);

        for (var band = 0; band < _bandLevels.Length; band++)
        {
            var startBin = _bandEdgeBins[band];
            var endBin = _bandEdgeBins[band + 1];

            // Peak-within-band rather than average: averaging washes out
            // narrow tones sitting inside a wide high-frequency band.
            double peakMagnitude = 0;
            for (var bin = startBin; bin < endBin; bin++)
            {
                var magnitude = Math.Sqrt(_real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]);
                if (magnitude > peakMagnitude)
                {
                    peakMagnitude = magnitude;
                }
            }

            // Normalise by window gain and transform length, then to dB.
            var normalized = peakMagnitude / (_fftSize * 0.25);
            var decibels = normalized <= 0 ? MinimumDecibels : 20.0 * Math.Log10(normalized);
            var target = Math.Clamp((decibels - MinimumDecibels) / -MinimumDecibels, 0.0, 1.0);

            var coefficient = target > _bandLevels[band] ? _attack : _decay;
            _bandLevels[band] += (target - _bandLevels[band]) * coefficient;
        }
    }

    private static int[] BuildLogBandEdges(int bandCount, int fftSize, int sampleRateHz)
    {
        const double lowestHz = 40.0;
        var highestHz = Math.Min(16000.0, sampleRateHz / 2.0);

        var usableBins = fftSize / 2;
        var binHz = (double)sampleRateHz / fftSize;

        var edges = new int[bandCount + 1];
        for (var i = 0; i <= bandCount; i++)
        {
            var fraction = (double)i / bandCount;
            var hz = lowestHz * Math.Pow(highestHz / lowestHz, fraction);
            edges[i] = Math.Clamp((int)Math.Round(hz / binHz), 1, usableBins);
        }

        // Guarantee every band spans at least one bin, otherwise the lowest
        // bands collapse onto the same bin and render as dead bars.
        for (var i = 1; i <= bandCount; i++)
        {
            if (edges[i] <= edges[i - 1])
            {
                edges[i] = Math.Min(edges[i - 1] + 1, usableBins);
            }
        }

        return edges;
    }
}
