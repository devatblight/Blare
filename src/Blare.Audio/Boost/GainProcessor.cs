namespace Blight.Blare.Audio.Boost;

/// <summary>Applies a scalar linear gain to interleaved float samples in place.</summary>
public static class GainProcessor
{
    public static void ApplyGain(Span<float> samples, float gainLinear)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] *= gainLinear;
        }
    }

    public static float DecibelsToLinear(float decibels) => MathF.Pow(10f, decibels / 20f);

    public static float LinearToDecibels(float linear) =>
        linear <= 0f ? float.NegativeInfinity : 20f * MathF.Log10(linear);
}
