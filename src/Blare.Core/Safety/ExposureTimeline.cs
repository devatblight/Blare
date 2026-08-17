namespace Blight.Blare.Core.Safety;

/// <summary>One hour of the recent past, and how much of it was spent listening loud.</summary>
public readonly record struct ExposureBucket(DateTimeOffset Hour, TimeSpan TimeLoud);

/// <summary>
/// When listening was loud, not just how much of it there was.
///
/// A single running total answers "have I been loud today" but not the question
/// people actually change behaviour over: whether it was one long stretch or a
/// dozen short ones, and whether it is happening late at night. A total of two
/// hours means something very different spread across a day than it does in one
/// sitting.
///
/// Buckets are hourly and kept for a rolling window. Nothing here leaves the
/// machine, and nothing is written that identifies what was playing — only how
/// long the output was loud.
/// </summary>
public sealed class ExposureTimeline
{
    private readonly Dictionary<DateTimeOffset, TimeSpan> _buckets = new();

    /// <param name="window">How far back to keep. Older buckets are dropped as time moves on.</param>
    public ExposureTimeline(TimeSpan? window = null)
    {
        Window = window ?? TimeSpan.FromHours(24);
    }

    public TimeSpan Window { get; }

    /// <summary>Buckets in the window, oldest first. Hours with no loud listening are absent rather than zero.</summary>
    public IReadOnlyList<ExposureBucket> Buckets =>
        _buckets
            .OrderBy(pair => pair.Key)
            .Select(pair => new ExposureBucket(pair.Key, pair.Value))
            .ToList();

    public TimeSpan TotalInWindow => _buckets.Values.Aggregate(TimeSpan.Zero, (total, span) => total + span);

    /// <summary>Adds time to the hour a sample falls in. Quiet samples are recorded as nothing rather than skipped, so pruning still runs.</summary>
    public void Record(DateTimeOffset when, bool loud, TimeSpan duration)
    {
        Prune(when);

        if (!loud || duration <= TimeSpan.Zero)
        {
            return;
        }

        var hour = FloorToHour(when);
        _buckets[hour] = _buckets.GetValueOrDefault(hour) + duration;
    }

    /// <summary>The longest run of consecutive hours that had any loud listening.</summary>
    public int LongestStretchHours()
    {
        var hours = _buckets.Keys.OrderBy(hour => hour).ToList();

        if (hours.Count == 0)
        {
            return 0;
        }

        var longest = 1;
        var current = 1;

        for (var index = 1; index < hours.Count; index++)
        {
            if (hours[index] - hours[index - 1] == TimeSpan.FromHours(1))
            {
                current++;
                longest = Math.Max(longest, current);
            }
            else
            {
                current = 1;
            }
        }

        return longest;
    }

    /// <summary>The hour with the most loud listening, for the UI to call out.</summary>
    public ExposureBucket? Busiest()
    {
        if (_buckets.Count == 0)
        {
            return null;
        }

        var busiest = _buckets.OrderByDescending(pair => pair.Value).First();
        return new ExposureBucket(busiest.Key, busiest.Value);
    }

    public void Prune(DateTimeOffset now)
    {
        var oldest = FloorToHour(now - Window);

        foreach (var hour in _buckets.Keys.Where(hour => hour < oldest).ToList())
        {
            _buckets.Remove(hour);
        }
    }

    public IReadOnlyDictionary<DateTimeOffset, TimeSpan> Snapshot() =>
        new Dictionary<DateTimeOffset, TimeSpan>(_buckets);

    public void Restore(IReadOnlyDictionary<DateTimeOffset, TimeSpan>? buckets)
    {
        _buckets.Clear();

        if (buckets is null)
        {
            return;
        }

        foreach (var (hour, span) in buckets)
        {
            _buckets[FloorToHour(hour)] = span;
        }
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, 0, 0, value.Offset);
}
