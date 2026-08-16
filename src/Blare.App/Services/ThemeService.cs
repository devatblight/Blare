using BLight.Blare.Core.Settings;
using Microsoft.UI.Xaml;

namespace BLight.Blare.App.Services;

public enum BlareTheme
{
    /// <summary>Stock Windows 11 look — system accent, standard Fluent surfaces, Mica.</summary>
    NativeFluent,

    /// <summary>Darker panels, tighter density, amber/red reserved for boost and danger. Reads like pro audio software.</summary>
    StudioDark,
}

/// <summary>
/// Applies and persists the user's chosen visual theme. The two themes
/// differ by a small set of brush/scalar resource overrides layered on top
/// of Fluent, rather than by a wholly separate control template set — that
/// keeps every control native and accessible while still changing the feel.
/// </summary>
public sealed class ThemeService
{
    private const string StorageKey = "theme";

    private readonly ISettingsStore _store;

    public ThemeService(ISettingsStore store)
    {
        _store = store;
    }

    public BlareTheme Current { get; private set; } = BlareTheme.StudioDark;

    public event EventHandler<BlareTheme>? ThemeChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<string>(StorageKey, cancellationToken);

        if (Enum.TryParse<BlareTheme>(saved, out var theme))
        {
            Current = theme;
        }

        Apply(Current);
    }

    public async Task SetAsync(BlareTheme theme, CancellationToken cancellationToken = default)
    {
        Current = theme;
        Apply(theme);
        await _store.SaveAsync(StorageKey, theme.ToString(), cancellationToken);
    }

    public void Apply(BlareTheme theme)
    {
        var resources = Application.Current.Resources;

        switch (theme)
        {
            case BlareTheme.StudioDark:
                resources["BlarePanelBackground"] = Brush(0x14, 0x16, 0x1A);
                resources["BlareStripBackground"] = Brush(0x1B, 0x1E, 0x24);
                resources["BlareStripBorder"] = Brush(0x2A, 0x2E, 0x36);
                resources["BlareMeterLow"] = Brush(0x3D, 0xD6, 0x8C);
                resources["BlareMeterMid"] = Brush(0xF5, 0xC2, 0x42);
                resources["BlareMeterHigh"] = Brush(0xE8, 0x5D, 0x3A);
                resources["BlareAccent"] = Brush(0x5A, 0x9F, 0xF5);
                resources["BlareStripCornerRadius"] = new CornerRadius(6);
                break;

            case BlareTheme.NativeFluent:
            default:
                resources["BlarePanelBackground"] = resources["LayerFillColorDefaultBrush"];
                resources["BlareStripBackground"] = resources["CardBackgroundFillColorDefaultBrush"];
                resources["BlareStripBorder"] = resources["CardStrokeColorDefaultBrush"];
                resources["BlareMeterLow"] = resources["SystemFillColorSuccessBrush"];
                resources["BlareMeterMid"] = resources["SystemFillColorCautionBrush"];
                resources["BlareMeterHigh"] = resources["SystemFillColorCriticalBrush"];
                resources["BlareAccent"] = resources["AccentFillColorDefaultBrush"];
                resources["BlareStripCornerRadius"] = new CornerRadius(8);
                break;
        }

        ThemeChanged?.Invoke(this, theme);
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Windows.UI.Color.FromArgb(255, red, green, blue));
}
