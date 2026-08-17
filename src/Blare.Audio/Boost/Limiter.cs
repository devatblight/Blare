namespace Blight.Blare.Audio.Boost;

/// <summary>
/// Brick-wall lookahead peak limiter — sits after <see cref="GainProcessor"/>
/// and before re-render (see plan §3), since any gain above unity on
/// already-normalized content will clip without one. Fast attack (instant,
/// driven by scanning the lookahead window so reduction lands before the
/// peak does, not after) and a slower release so gain recovers smoothly
/// rather than pumping. "Sufficient for v1" per the plan — not a
/// production mastering limiter.
/// </summary>
public sealed class Limiter
{
    private readonly float _ceiling;
    private readonly float _releasePerSample;
    private readonly float[] _lookaheadBuffer;
    private int _writeIndex;
    private float _currentGain = 1f;

    public Limiter(float ceilingLinear = 0.98f, int lookaheadSamples = 240, float releaseTimeSeconds = 0.08f, int sampleRateHz = 48000)
    {
        if (ceilingLinear is <= 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(ceilingLinear), "Ceiling must be in (0, 1].");
        }

        _ceiling = ceilingLinear;
        _lookaheadBuffer = new float[Math.Max(1, lookaheadSamples)];
        _releasePerSample = 1f / MathF.Max(1f, releaseTimeSeconds * sampleRateHz);
    }

    /// <summary>Processes interleaved samples in place. Output is delayed by the lookahead window length.</summary>
    public void Process(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var incoming = samples[i];

            var windowPeak = MathF.Abs(incoming);
            foreach (var buffered in _lookaheadBuffer)
            {
                var abs = MathF.Abs(buffered);
                if (abs > windowPeak)
                {
                    windowPeak = abs;
                }
            }

            var requiredGain = windowPeak > _ceiling ? _ceiling / windowPeak : 1f;

            _currentGain = requiredGain < _currentGain
                ? requiredGain // instant attack — never let a peak through under-attenuated
                : MathF.Min(requiredGain, _currentGain + _releasePerSample); // release, but never past what's currently required

            var outgoing = _lookaheadBuffer[_writeIndex];
            _lookaheadBuffer[_writeIndex] = incoming;
            _writeIndex = (_writeIndex + 1) % _lookaheadBuffer.Length;

            samples[i] = outgoing * _currentGain;
        }
    }
}
