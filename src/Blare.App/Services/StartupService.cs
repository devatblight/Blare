using Microsoft.Win32;
using Blight.Blare.Core.Settings;

namespace Blight.Blare.App.Services;

/// <summary>
/// Controls whether Blare launches with Windows, and whether it starts
/// straight to the tray.
///
/// Registered under the per-user Run key rather than a scheduled task or a
/// machine-wide entry: it needs no elevation, is visible to the user in Task
/// Manager's Startup tab where they'd expect to find it, and uninstalls
/// cleanly. A tray utility that can't be disabled from the obvious place is
/// the kind of thing people rightly resent.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Blare";
    private const string StartHiddenKey = "start-hidden";

    /// <summary>Passed to a startup launch so the window doesn't appear over whatever you were doing at sign-in.</summary>
    public const string TrayArgument = "--tray";

    private readonly ISettingsStore _store;

    public StartupService(ISettingsStore store)
    {
        _store = store;
    }

    public bool StartHidden { get; private set; } = true;

    /// <summary>Whether Windows is currently set to launch Blare. Read from the registry rather than cached, so an external change is reflected.</summary>
    public bool RunsAtStartup
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is not null;
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex);
                return false;
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        StartHidden = await _store.LoadAsync<bool?>(StartHiddenKey, cancellationToken) ?? true;
    }

    public async Task SetStartHiddenAsync(bool startHidden, CancellationToken cancellationToken = default)
    {
        StartHidden = startHidden;
        await _store.SaveAsync(StartHiddenKey, startHidden, cancellationToken);

        // The registry command carries the flag, so rewrite it if registered.
        if (RunsAtStartup)
        {
            SetRunAtStartup(true);
        }
    }

    /// <summary>Adds or removes the Run entry. Returns false if the registry refused.</summary>
    public bool SetRunAtStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (key is null)
            {
                return false;
            }

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return false;
            }

            var command = StartHidden
                ? $"\"{executable}\" {TrayArgument}"
                : $"\"{executable}\"";

            key.SetValue(ValueName, command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            return false;
        }
    }

    /// <summary>Whether this launch should stay in the tray — either Windows started it hidden, or the user asked for that.</summary>
    public static bool LaunchedToTray(string[] args) =>
        args.Any(a => string.Equals(a, TrayArgument, StringComparison.OrdinalIgnoreCase));
}
