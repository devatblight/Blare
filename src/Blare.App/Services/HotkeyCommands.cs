using System.Diagnostics;
using Blight.Blare.App.Views;
using Blight.Blare.Audio.Sessions;

namespace Blight.Blare.App.Services;

/// <summary>
/// The hotkeys Blare ships with, and what they do.
///
/// All of these act on the app in front of you rather than the default device,
/// which is the gap Windows leaves: its volume keys can only move everything at
/// once. Each one reports what it did through the flyout, because a key press
/// that silently changes the volume of something off-screen is indistinguishable
/// from a broken key.
/// </summary>
public sealed class HotkeyCommands : IDisposable
{
    private const uint VkM = 0x4D;
    private const uint VkUp = 0x26;
    private const uint VkDown = 0x28;
    private const uint VkNumpad0 = 0x60;

    private const double NudgeStep = 5;

    private readonly HotkeyService _hotkeys;
    private readonly AudioSessionManager _sessions;
    private readonly VolumeCoordinator _volume;
    private readonly FlyoutService _flyout;

    public HotkeyCommands(
        HotkeyService hotkeys,
        AudioSessionManager sessions,
        VolumeCoordinator volume,
        FlyoutService flyout)
    {
        _hotkeys = hotkeys;
        _sessions = sessions;
        _volume = volume;
        _flyout = flyout;
    }

    /// <summary>Combinations that could not be claimed, so the user can be told rather than left wondering.</summary>
    public IReadOnlyList<string> Unavailable { get; private set; } = [];

    public void RegisterDefaults()
    {
        var unavailable = new List<string>();

        Claim(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkM, "Ctrl+Alt+M", ToggleMuteForeground);
        Claim(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkUp, "Ctrl+Alt+Up", () => NudgeForeground(NudgeStep));
        Claim(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkDown, "Ctrl+Alt+Down", () => NudgeForeground(-NudgeStep));
        Claim(HotkeyModifiers.Control | HotkeyModifiers.Alt, VkNumpad0, "Ctrl+Alt+Numpad0", MuteEverything);

        Unavailable = unavailable;

        void Claim(HotkeyModifiers modifiers, uint key, string label, Action action)
        {
            if (_hotkeys.Register(modifiers, key, action) is null)
            {
                unavailable.Add(label);
            }
        }
    }

    /// <summary>Mutes or unmutes every session belonging to the app in the foreground.</summary>
    public void ToggleMuteForeground()
    {
        var target = ForegroundSessions();

        if (target.Count == 0)
        {
            _flyout.Show("Nothing to mute", "The app in front isn't playing any audio.", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
            return;
        }

        // One session decides for the whole app, so a browser with several
        // audio processes toggles as a unit rather than half-muting.
        var muted = !target[0].IsMuted;

        foreach (var session in target)
        {
            _volume.SetMute(session.ProcessId, muted);
        }

        _flyout.Show(
            NameFor(target[0]),
            muted ? "Muted" : "Unmuted",
            FlyoutTone.Neutral,
            TimeSpan.FromSeconds(2));
    }

    /// <summary>Moves the foreground app's level by a step, unmuting it the way dragging a fader does.</summary>
    public void NudgeForeground(double deltaPercent)
    {
        var target = ForegroundSessions();

        if (target.Count == 0)
        {
            _flyout.Show("Nothing to adjust", "The app in front isn't playing any audio.", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
            return;
        }

        var level = Math.Clamp((target[0].Volume * 100) + deltaPercent, 0, VolumeCoordinator.MaximumPercent);

        foreach (var session in target)
        {
            _volume.SetVolumePercent(session.ProcessId, level);
            _volume.SetMute(session.ProcessId, false);
        }

        _flyout.Show(NameFor(target[0]), $"{level:F0}%", FlyoutTone.Neutral, TimeSpan.FromSeconds(2));
    }

    public void MuteEverything()
    {
        var sessions = _sessions.GetSessionsForDefaultDevice()
            .Where(session => !session.IsSystemSoundsSession)
            .ToList();

        foreach (var session in sessions)
        {
            _volume.SetMute(session.ProcessId, true);
        }

        _flyout.Show("Everything muted", $"{sessions.Count} apps silenced.", FlyoutTone.Caution, TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Every session belonging to the foreground app.
    ///
    /// Matched by executable rather than process id: the window you are looking
    /// at is often not the process making the sound — a browser plays audio from
    /// a renderer child, and matching only the exact id finds nothing at all.
    /// </summary>
    private List<AudioSessionInfo> ForegroundSessions()
    {
        if (ForegroundApp.ProcessId() is not { } foregroundId)
        {
            return [];
        }

        var sessions = _sessions.GetSessionsForDefaultDevice()
            .Where(session => !session.IsSystemSoundsSession)
            .ToList();

        var exact = sessions.Where(session => session.ProcessId == foregroundId).ToList();

        if (exact.Count > 0)
        {
            return exact;
        }

        var name = ProcessName(foregroundId);

        return string.IsNullOrEmpty(name)
            ? []
            : sessions
                .Where(session => string.Equals(ProcessName(session.ProcessId), name, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private static string NameFor(AudioSessionInfo session) =>
        string.IsNullOrWhiteSpace(session.DisplayName)
            ? ProcessName(session.ProcessId) is { Length: > 0 } name ? name : $"pid {session.ProcessId}"
            : session.DisplayName;

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Exited between enumeration and lookup, or protected.
            return string.Empty;
        }
    }

    public void Dispose() => _hotkeys.UnregisterAll();
}
