using Blight.Blare.Core.Settings;

namespace Blight.Blare.Core.Layout;

/// <summary>Persists the user's dashboard arrangement locally.</summary>
public sealed class DashboardStore
{
    private const string StorageKey = "dashboard";

    private readonly ISettingsStore _store;

    public DashboardStore(ISettingsStore store)
    {
        _store = store;
    }

    public DashboardLayout Layout { get; private set; } = DashboardLayout.CreateDefault();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<List<DashboardCard>>(StorageKey, cancellationToken);

        Layout = saved is { Count: > 0 }
            ? DashboardLayout.FromCards(saved)
            : DashboardLayout.CreateDefault();
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(StorageKey, Layout.Cards.ToList(), cancellationToken);

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        Layout = DashboardLayout.CreateDefault();
        await SaveAsync(cancellationToken);
    }
}
