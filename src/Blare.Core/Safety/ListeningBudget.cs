namespace Blight.Blare.Core.Safety;

public enum BudgetState
{
    /// <summary>Plenty left.</summary>
    Healthy,

    /// <summary>Most of it is gone — worth knowing before it runs out.</summary>
    Nearing,

    /// <summary>Spent.</summary>
    Exceeded,
}

/// <summary>
/// A daily allowance of loud listening, and how much of it is gone.
///
/// This is a self-set budget, not a clinical dose. Windows cannot see the gain
/// on your headphones, so no app on it can tell you a real noise dose — saying
/// otherwise would be the most harmful thing a hearing feature could do. What it
/// can do is let you decide "two hours of loud a day is enough for me" and then
/// be honest about where you are against it.
/// </summary>
public sealed record ListeningBudget(TimeSpan Allowance)
{
    /// <summary>Around the point most people would want a nudge rather than a verdict.</summary>
    public static ListeningBudget Default => new(TimeSpan.FromMinutes(120));

    /// <summary>Fraction of the allowance used, clamped to 0..1 so a ring never overdraws.</summary>
    public double FractionUsed(TimeSpan used) =>
        Allowance <= TimeSpan.Zero ? 1 : Math.Clamp(used.TotalMinutes / Allowance.TotalMinutes, 0, 1);

    /// <summary>What is left, never negative — "minus twenty minutes" is not a thing anyone can act on.</summary>
    public TimeSpan Remaining(TimeSpan used)
    {
        var remaining = Allowance - used;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public BudgetState StateFor(TimeSpan used)
    {
        if (Allowance <= TimeSpan.Zero || used >= Allowance)
        {
            return BudgetState.Exceeded;
        }

        return FractionUsed(used) >= 0.75 ? BudgetState.Nearing : BudgetState.Healthy;
    }

    /// <summary>A plain-language line for the UI. Deliberately avoids anything that sounds like a measurement.</summary>
    public string Describe(TimeSpan used) => StateFor(used) switch
    {
        BudgetState.Exceeded => "You've used today's loud-listening budget.",
        BudgetState.Nearing => $"{Remaining(used).TotalMinutes:F0} min of your budget left today.",
        _ => $"{used.TotalMinutes:F0} of {Allowance.TotalMinutes:F0} min used today.",
    };
}
