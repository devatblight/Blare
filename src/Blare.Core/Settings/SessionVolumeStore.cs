namespace BLight.Blare.Core.Settings;

/// <summary>
/// Remembers the last volume the user set per app (keyed by a stable
/// identity such as executable path, not the ephemeral session id) so it
/// can be reapplied the next time that app starts — see plan §2.
/// </summary>
public sealed class SessionVolumeStore
{
    private const string StorageKey = "session-volumes";

    private readonly ISettingsStore _store;
    private Dictionary<string, double> _volumes = new();

    public SessionVolumeStore(ISettingsStore store)
    {
        _store = store;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _volumes = await _store.LoadAsync<Dictionary<string, double>>(StorageKey, cancellationToken)
                   ?? new Dictionary<string, double>();
    }

    public double? GetVolume(string appKey) =>
        _volumes.TryGetValue(NormalizeKey(appKey), out var volume) ? volume : null;

    public async Task SetVolumeAsync(string appKey, double volumePercent, CancellationToken cancellationToken = default)
    {
        _volumes[NormalizeKey(appKey)] = volumePercent;
        await _store.SaveAsync(StorageKey, _volumes, cancellationToken);
    }

    private static string NormalizeKey(string appKey) => appKey.Trim().ToLowerInvariant();
}
