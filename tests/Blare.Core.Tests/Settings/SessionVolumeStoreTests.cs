using Blight.Blare.Core.Settings;

namespace Blare.Core.Tests.Settings;

public class SessionVolumeStoreTests
{
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, object> _values = new();

        public Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : default);

        public Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value!;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetVolume_ReturnsNullWhenNeverSet()
    {
        var store = new SessionVolumeStore(new InMemorySettingsStore());
        await store.LoadAsync();

        Assert.Null(store.GetVolume(@"C:\apps\music.exe"));
    }

    [Fact]
    public async Task SetVolume_IsRetrievableImmediately()
    {
        var store = new SessionVolumeStore(new InMemorySettingsStore());
        await store.LoadAsync();

        await store.SetVolumeAsync(@"C:\apps\music.exe", 65);

        Assert.Equal(65, store.GetVolume(@"C:\apps\music.exe"));
    }

    [Fact]
    public async Task Keys_AreCaseInsensitive()
    {
        var store = new SessionVolumeStore(new InMemorySettingsStore());
        await store.LoadAsync();

        await store.SetVolumeAsync(@"C:\Apps\Music.exe", 40);

        Assert.Equal(40, store.GetVolume(@"c:\apps\music.exe"));
    }

    [Fact]
    public async Task PersistedValues_SurviveReloadFromTheSameBackingStore()
    {
        var backing = new InMemorySettingsStore();

        var first = new SessionVolumeStore(backing);
        await first.LoadAsync();
        await first.SetVolumeAsync(@"C:\apps\music.exe", 55);

        var second = new SessionVolumeStore(backing);
        await second.LoadAsync();

        Assert.Equal(55, second.GetVolume(@"C:\apps\music.exe"));
    }

    [Fact]
    public async Task SetVolume_OverwritesPreviousValueForSameKey()
    {
        var store = new SessionVolumeStore(new InMemorySettingsStore());
        await store.LoadAsync();

        await store.SetVolumeAsync(@"C:\apps\music.exe", 30);
        await store.SetVolumeAsync(@"C:\apps\music.exe", 90);

        Assert.Equal(90, store.GetVolume(@"C:\apps\music.exe"));
    }
}
