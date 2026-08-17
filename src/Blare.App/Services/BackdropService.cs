using Blight.Blare.Core.Settings;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;

namespace Blight.Blare.App.Services;

public enum BackdropKind
{
    /// <summary>Standard Mica — the Windows 11 app-window material.</summary>
    Mica,

    /// <summary>Mica Alt, the stronger tint Windows uses for tabbed shells.</summary>
    MicaAlt,

    /// <summary>Desktop Acrylic — more translucent, blurs what's behind the window.</summary>
    Acrylic,

    /// <summary>No material. A flat solid surface, which is also the Windows 10 reality.</summary>
    None,
}

/// <summary>
/// Applies and persists the window backdrop material.
///
/// Mica needs Windows 11 (build 22000+); on Windows 10 the controllers report
/// unsupported and we fall back rather than showing a transparent window with
/// nothing behind it. <see cref="EffectiveKind"/> reports what actually got
/// applied, which may differ from what was asked for — Diagnostics shows both.
/// </summary>
public sealed class BackdropService
{
    private const string StorageKey = "backdrop";

    private readonly ISettingsStore _store;

    private Window? _window;
    private MicaController? _micaController;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _configuration;

    public BackdropService(ISettingsStore store)
    {
        _store = store;
    }

    public BackdropKind Requested { get; private set; } = BackdropKind.Mica;

    /// <summary>What is actually on screen — falls back when the requested material isn't available on this OS.</summary>
    public BackdropKind EffectiveKind { get; private set; } = BackdropKind.None;

    public static bool MicaSupported => MicaController.IsSupported();

    public static bool AcrylicSupported => DesktopAcrylicController.IsSupported();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<string>(StorageKey, cancellationToken);

        if (Enum.TryParse<BackdropKind>(saved, out var kind))
        {
            Requested = kind;
        }
    }

    /// <summary>Attaches to a window. Call once per window, after it exists.</summary>
    public void Attach(Window window)
    {
        _window = window;
        _configuration = new SystemBackdropConfiguration { IsInputActive = true };
        Apply(Requested);
    }

    public async Task SetAsync(BackdropKind kind, CancellationToken cancellationToken = default)
    {
        Requested = kind;
        Apply(kind);
        await _store.SaveAsync(StorageKey, kind.ToString(), cancellationToken);
    }

    private void Apply(BackdropKind kind)
    {
        if (_window is null || _configuration is null)
        {
            return;
        }

        Detach();

        var target = _window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();

        switch (kind)
        {
            case BackdropKind.Mica or BackdropKind.MicaAlt when MicaSupported:
                _micaController = new MicaController
                {
                    Kind = kind == BackdropKind.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base,
                };
                _micaController.AddSystemBackdropTarget(target);
                _micaController.SetSystemBackdropConfiguration(_configuration);
                EffectiveKind = kind;
                return;

            case BackdropKind.Acrylic when AcrylicSupported:
                _acrylicController = new DesktopAcrylicController();
                _acrylicController.AddSystemBackdropTarget(target);
                _acrylicController.SetSystemBackdropConfiguration(_configuration);
                EffectiveKind = BackdropKind.Acrylic;
                return;

            case BackdropKind.None:
                EffectiveKind = BackdropKind.None;
                return;

            default:
                // Asked for something this OS can't do — try Acrylic, else go flat.
                if (AcrylicSupported)
                {
                    _acrylicController = new DesktopAcrylicController();
                    _acrylicController.AddSystemBackdropTarget(target);
                    _acrylicController.SetSystemBackdropConfiguration(_configuration);
                    EffectiveKind = BackdropKind.Acrylic;
                }
                else
                {
                    EffectiveKind = BackdropKind.None;
                }

                return;
        }
    }

    private void Detach()
    {
        _micaController?.Dispose();
        _micaController = null;
        _acrylicController?.Dispose();
        _acrylicController = null;
    }
}
