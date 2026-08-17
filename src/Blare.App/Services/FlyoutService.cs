using Blight.Blare.App.Views;
using Blight.Blare.Core.Settings;

namespace Blight.Blare.App.Services;

/// <summary>
/// Blare's single channel for talking to the user. Everything — boost notices,
/// safety warnings, device changes — goes through here so messages always
/// appear in the one place the user picked, instead of each feature inventing
/// its own surface.
/// </summary>
public sealed class FlyoutService
{
    private const string StorageKey = "flyout-position";

    private readonly ISettingsStore _store;
    private NotificationFlyoutWindow? _window;

    public FlyoutService(ISettingsStore store)
    {
        _store = store;
    }

    public FlyoutPosition Position { get; private set; } = FlyoutPosition.BottomRight;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<string>(StorageKey, cancellationToken);

        if (Enum.TryParse<FlyoutPosition>(saved, out var position))
        {
            Position = position;
        }
    }

    public async Task SetPositionAsync(FlyoutPosition position, CancellationToken cancellationToken = default)
    {
        Position = position;
        await _store.SaveAsync(StorageKey, position.ToString(), cancellationToken);

        // Show where it lands, so choosing a position is self-demonstrating.
        Show("Flyout position", "Messages from Blare will appear here.", FlyoutTone.Neutral, TimeSpan.FromSeconds(2));
    }

    public void Show(
        string title,
        string message,
        FlyoutTone tone = FlyoutTone.Neutral,
        TimeSpan? duration = null,
        string? actionLabel = null,
        Action? action = null)
    {
        try
        {
            // Created lazily and reused — a fresh window per message would
            // flicker and lose its place.
            _window ??= new NotificationFlyoutWindow();

            _window.ShowMessage(
                title,
                message,
                Position,
                tone,
                duration ?? TimeSpan.FromSeconds(6),
                actionLabel,
                action);
        }
        catch (Exception ex)
        {
            // A failed notification must never break whatever triggered it.
            CrashLog.Write(ex);
        }
    }
}
