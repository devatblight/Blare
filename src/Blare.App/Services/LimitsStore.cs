using Blight.Blare.Core.Safety;
using Blight.Blare.Core.Settings;

namespace Blight.Blare.App.Services;

/// <summary>
/// Persists per-app ceilings and quiet hours.
///
/// Saves on every change rather than at shutdown. A limit is a rule the user set
/// deliberately, and a rule that quietly disappears because the process was
/// killed is worse than one that was never offered.
/// </summary>
public sealed class LimitsStore
{
    private const string StorageKey = "limits";

    private sealed record Saved(Dictionary<string, double> Caps, QuietHours QuietHours);

    private readonly ISettingsStore _store;

    public LimitsStore(ISettingsStore store)
    {
        _store = store;
        Limits.Changed += (_, _) => CrashLog.FireAndForget(SaveAsync());
    }

    public ListeningLimits Limits { get; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<Saved>(StorageKey, cancellationToken);

        if (saved is not null)
        {
            Limits.Restore(saved.Caps, saved.QuietHours);
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(
            StorageKey,
            new Saved(new Dictionary<string, double>(Limits.Snapshot()), Limits.QuietHours),
            cancellationToken);
}
