using BLight.Blare.Core.Settings;

namespace BLight.Blare.Core.Safety;

/// <summary>
/// Persists <see cref="ConsentState"/> across restarts.
///
/// This is load-bearing for safety, not a convenience: without it, disabling
/// health warnings silently resets every time the app restarts, which both
/// surprises the user (protection they turned off comes back) and defeats the
/// 30-day re-confirmation design (an opt-out can never actually reach its
/// expiry). Saved automatically whenever consent changes.
/// </summary>
public sealed class ConsentStore
{
    private const string StorageKey = "consent";

    private readonly ISettingsStore _store;
    private readonly ConsentState _state;

    public ConsentStore(ISettingsStore store, ConsentState state)
    {
        _store = store;
        _state = state;
        _state.Changed += (_, _) => _ = SaveAsync();
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<List<ConsentRecord>>(StorageKey, cancellationToken);

        if (saved is { Count: > 0 })
        {
            _state.Restore(saved);
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(StorageKey, _state.Records.ToList(), cancellationToken);
}
