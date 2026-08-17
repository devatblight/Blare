namespace Blight.Blare.Audio.Boost;

/// <summary>
/// Last line of defence on the boost pipeline.
///
/// Everything upstream — gain maths, the limiter, the fader — is expected to
/// behave, but this is amplified audio going into someone's ears, so nothing
/// upstream is trusted. Every sample is checked immediately before it reaches
/// the audio device, and anything that isn't a finite value inside the ceiling
/// is corrected rather than played.
///
/// The failure this exists for is not a slightly-too-loud sample. It is a NaN
/// or infinity reaching the DAC, which is reproduced as full-scale noise — the
/// loudest sound the hardware can make, from a single bad arithmetic result.
/// </summary>
public static class BoostSafety
{
    /// <summary>Absolute output ceiling. Nothing is ever written above this, whatever the gain says.</summary>
    public const float AbsoluteCeiling = 0.98f;

    /// <summary>
    /// Hard cap on the total multiplier applied to captured audio. Boost tops
    /// out at 3x, and the residual attenuation costs a further 50x to undo, so
    /// legitimate values stay well under this. Anything larger means the maths
    /// went wrong.
    /// </summary>
    public const float MaxTotalGain = 200f;

    /// <summary>
    /// Validates a gain multiplier before it is ever applied. Non-finite or
    /// negative values collapse to unity rather than propagating: a NaN gain
    /// would turn every subsequent sample into NaN.
    /// </summary>
    public static float SanitizeGain(float gain)
    {
        if (!float.IsFinite(gain) || gain <= 0f)
        {
            return 1f;
        }

        return Math.Min(gain, MaxTotalGain);
    }

    /// <summary>
    /// Final check on a block about to be rendered. Replaces non-finite samples
    /// with silence and hard-clamps everything else to the ceiling. Returns how
    /// many samples had to be corrected, so a persistently misbehaving pipeline
    /// can be shut down instead of left running.
    /// </summary>
    public static int Enforce(Span<float> samples)
    {
        var corrections = 0;

        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];

            if (!float.IsFinite(sample))
            {
                // Silence is the only safe substitute — a NaN reaching the
                // device is reproduced as full-scale noise.
                samples[i] = 0f;
                corrections++;
                continue;
            }

            if (sample > AbsoluteCeiling)
            {
                samples[i] = AbsoluteCeiling;
                corrections++;
            }
            else if (sample < -AbsoluteCeiling)
            {
                samples[i] = -AbsoluteCeiling;
                corrections++;
            }
        }

        return corrections;
    }

    /// <summary>
    /// Whether sustained clamping means the pipeline should be abandoned.
    /// Occasional corrections are normal — the limiter and this guard disagree
    /// slightly at transients. A large share of a block being clamped means
    /// something upstream is broken, and continuing would be gambling with the
    /// listener's hearing.
    /// </summary>
    public static bool IsRunaway(int corrections, int sampleCount) =>
        sampleCount > 0 && (double)corrections / sampleCount > 0.5;
}
