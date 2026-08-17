namespace Blight.Blare.Core.Mixing;

public sealed record FocusLevel(string AppKey, double VolumePercent);

/// <summary>
/// Works out the levels for "make this app dominant".
///
/// True above-unity boost isn't available (per-process loopback capture is
/// applied after session volume, so an app can't be silenced and re-amplified),
/// so dominance is produced the way a mixing engineer would: bring the focused
/// channel to the top of its travel, duck everything else, and let the user
/// raise master to recover absolute level.
///
/// Pure maths, no audio APIs, so the behaviour is testable without a device.
/// </summary>
public static class FocusMix
{
    /// <param name="duckToPercent">Where non-focused apps are pushed. Lower means a more dramatic difference.</param>
    public static IReadOnlyList<FocusLevel> Apply(
        IEnumerable<FocusLevel> current,
        string focusedAppKey,
        double duckToPercent = 25)
    {
        if (string.IsNullOrEmpty(focusedAppKey))
        {
            throw new ArgumentException("A focused app key is required.", nameof(focusedAppKey));
        }

        var clampedDuck = Math.Clamp(duckToPercent, 0, 100);

        return current
            .Select(level => level.AppKey == focusedAppKey
                ? level with { VolumePercent = 100 }
                // Never push an app *up* to the duck level — something already
                // quieter than the target was deliberately set that way.
                : level with { VolumePercent = Math.Min(level.VolumePercent, clampedDuck) })
            .ToList();
    }

    /// <summary>Restores the levels captured before focus was engaged, dropping apps that have since disappeared.</summary>
    public static IReadOnlyList<FocusLevel> Restore(
        IEnumerable<FocusLevel> saved,
        IEnumerable<string> currentAppKeys)
    {
        var present = currentAppKeys.ToHashSet();
        return saved.Where(level => present.Contains(level.AppKey)).ToList();
    }
}
