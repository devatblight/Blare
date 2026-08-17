namespace Blight.Blare.Core.Mixing;

/// <summary>
/// Converts between where a fader sits and how loud that is.
///
/// Windows' session volume is a linear amplitude scalar, but hearing is roughly
/// logarithmic: dropping a linear fader from 100% to 50% sounds like a small
/// change, while 10% to 5% is dramatic. On a linear fader the entire useful
/// range of quiet listening is crammed into the bottom centimetre.
///
/// A taper spreads it out — the top half of the fader covers the top 12 dB, and
/// fine control at low levels gets the room it needs. This is what mixing desks
/// have always done and why their faders are not evenly spaced.
/// </summary>
public static class FaderTaper
{
    /// <summary>Fader travel in decibels. Below this the fader is silent rather than merely very quiet.</summary>
    public const double RangeDb = 60;

    /// <summary>Amplitude scalar (0..1) for a fader position (0..1).</summary>
    public static double ToGain(double position)
    {
        var clamped = Math.Clamp(position, 0, 1);

        if (clamped <= 0)
        {
            return 0;
        }

        // Full travel is 0 dB at the top down to -RangeDb at the bottom.
        var decibels = RangeDb * (clamped - 1);
        return Math.Pow(10, decibels / 20);
    }

    /// <summary>Fader position (0..1) that produces a given amplitude scalar.</summary>
    public static double ToPosition(double gain)
    {
        var clamped = Math.Clamp(gain, 0, 1);

        if (clamped <= 0)
        {
            return 0;
        }

        var decibels = 20 * Math.Log10(clamped);

        // Anything below the fader's range lands at the bottom rather than
        // going negative.
        return Math.Clamp(1 + (decibels / RangeDb), 0, 1);
    }

    /// <summary>Percent-in, percent-out convenience for the UI, which works in 0-100.</summary>
    public static double GainPercentFor(double positionPercent) =>
        ToGain(positionPercent / 100.0) * 100.0;

    public static double PositionPercentFor(double gainPercent) =>
        ToPosition(gainPercent / 100.0) * 100.0;
}
