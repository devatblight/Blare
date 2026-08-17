using BLight.Blare.Core.Safety;
using BLight.Blare.Core.Settings;

namespace Blare.Core.Tests.Safety;

public class ConsentStoreTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
    public async Task DisabledWarnings_SurviveARestart()
    {
        var backing = new InMemorySettingsStore();

        var firstRun = new ConsentState();
        var firstStore = new ConsentStore(backing, firstRun);
        await firstStore.LoadAsync();
        firstRun.Grant(ConsentKind.SafetyWarningsDisabled, Start);
        await firstStore.SaveAsync();

        // Simulate relaunching the app.
        var secondRun = new ConsentState();
        var secondStore = new ConsentStore(backing, secondRun);
        await secondStore.LoadAsync();

        Assert.True(secondRun.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
    }

    [Fact]
    public async Task RestoredConsent_StillExpiresOnTheOriginalSchedule()
    {
        var backing = new InMemorySettingsStore();

        var firstRun = new ConsentState(TimeSpan.FromDays(30));
        var firstStore = new ConsentStore(backing, firstRun);
        firstRun.Grant(ConsentKind.SafetyWarningsDisabled, Start);
        await firstStore.SaveAsync();

        var secondRun = new ConsentState(TimeSpan.FromDays(30));
        var secondStore = new ConsentStore(backing, secondRun);
        await secondStore.LoadAsync();

        // Expiry is measured from the original grant, not from when it was reloaded.
        Assert.True(secondRun.IsActive(ConsentKind.SafetyWarningsDisabled, Start + TimeSpan.FromDays(29)));
        Assert.False(secondRun.IsActive(ConsentKind.SafetyWarningsDisabled, Start + TimeSpan.FromDays(31)));
    }

    [Fact]
    public async Task RevokedConsent_StaysRevokedAfterRestart()
    {
        var backing = new InMemorySettingsStore();

        var firstRun = new ConsentState();
        var firstStore = new ConsentStore(backing, firstRun);
        firstRun.Grant(ConsentKind.SafetyWarningsDisabled, Start);
        firstRun.Revoke(ConsentKind.SafetyWarningsDisabled);
        await firstStore.SaveAsync();

        var secondRun = new ConsentState();
        var secondStore = new ConsentStore(backing, secondRun);
        await secondStore.LoadAsync();

        Assert.False(secondRun.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
    }

    [Fact]
    public async Task GrantingConsent_PersistsWithoutAnExplicitSave()
    {
        var backing = new InMemorySettingsStore();

        var firstRun = new ConsentState();
        _ = new ConsentStore(backing, firstRun);
        firstRun.Grant(ConsentKind.SafeBoostCeilingOverride, Start);

        var secondRun = new ConsentState();
        var secondStore = new ConsentStore(backing, secondRun);
        await secondStore.LoadAsync();

        Assert.True(secondRun.IsActive(ConsentKind.SafeBoostCeilingOverride, Start));
    }

    [Fact]
    public async Task NothingSaved_LeavesTheSafeDefaults()
    {
        var state = new ConsentState();
        var store = new ConsentStore(new InMemorySettingsStore(), state);

        await store.LoadAsync();

        Assert.False(state.IsActive(ConsentKind.SafetyWarningsDisabled, Start));
        Assert.False(state.IsActive(ConsentKind.SafeBoostCeilingOverride, Start));
    }

    [Fact]
    public void TimeUntilExpiry_CountsDownFromTheGrant()
    {
        var state = new ConsentState(TimeSpan.FromDays(30));
        state.Grant(ConsentKind.SafetyWarningsDisabled, Start);

        var remaining = state.TimeUntilExpiry(ConsentKind.SafetyWarningsDisabled, Start + TimeSpan.FromDays(10));

        Assert.Equal(TimeSpan.FromDays(20), remaining);
    }

    [Fact]
    public void TimeUntilExpiry_IsNullWhenNotActive()
    {
        var state = new ConsentState();

        Assert.Null(state.TimeUntilExpiry(ConsentKind.SafetyWarningsDisabled, Start));
    }
}
