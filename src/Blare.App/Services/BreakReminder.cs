using Blight.Blare.App.Views;
using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;
using Microsoft.UI.Dispatching;

namespace Blight.Blare.App.Services;

/// <summary>
/// Suggests a break after a long stretch of loud listening, with a button that
/// actually turns it down.
///
/// This is the cheapest expression of what Blare is for. A warning that only
/// says "you have been loud for a while" puts the work back on the person it
/// just interrupted; the action has to be one click, or it is decoration.
///
/// Runs off the same effective-level signal as the safety monitor, so it can't
/// fire for an app sitting at 100% playing silence.
/// </summary>
public sealed class BreakReminder
{
    /// <summary>Checked rarely — this measures a habit over an hour, not a moment.</summary>
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(1);

    /// <summary>How long the quiet spell has to be before the timer starts over.</summary>
    private static readonly TimeSpan ResetAfter = TimeSpan.FromMinutes(5);

    /// <summary>What the "Turn it down" button drops the device to, as a fraction of where it was.</summary>
    private const double TurnDownTo = 0.6;

    private readonly SafetyMonitor _safety;
    private readonly AudioSessionManager _sessions;
    private readonly AudioDeviceManager _devices;
    private readonly FlyoutService _flyout;

    private DispatcherQueueTimer? _timer;
    private TimeSpan _loudFor = TimeSpan.Zero;
    private TimeSpan _quietFor = TimeSpan.Zero;
    private DateTimeOffset _lastReminder = DateTimeOffset.MinValue;

    public BreakReminder(
        SafetyMonitor safety,
        AudioSessionManager sessions,
        AudioDeviceManager devices,
        FlyoutService flyout)
    {
        _safety = safety;
        _sessions = sessions;
        _devices = devices;
        _flyout = flyout;
    }

    /// <summary>How long to listen loud before being asked to take a break.</summary>
    public TimeSpan RemindAfter { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>Never nag more often than this, however long the session runs.</summary>
    public TimeSpan MinimumGap { get; set; } = TimeSpan.FromMinutes(30);

    public bool IsRunning => _timer is not null;

    /// <summary>Continuous loud listening so far, for the UI to show.</summary>
    public TimeSpan LoudStretch => _loudFor;

    public void Start()
    {
        if (_timer is not null)
        {
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread();

        if (queue is null)
        {
            return;
        }

        _timer = queue.CreateTimer();
        _timer.Interval = SampleInterval;
        _timer.Tick += (_, _) => Tick(DateTimeOffset.UtcNow);
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>One sampling step. Takes the clock so the accumulation logic stays deterministic.</summary>
    internal void Tick(DateTimeOffset now)
    {
        if (IsAnythingLoud())
        {
            _loudFor += SampleInterval;
            _quietFor = TimeSpan.Zero;
        }
        else
        {
            _quietFor += SampleInterval;

            // A short gap between tracks isn't a break; a real one resets it.
            if (_quietFor >= ResetAfter)
            {
                _loudFor = TimeSpan.Zero;
            }
        }

        if (_loudFor < RemindAfter || _safety.WarningsDisabled(now) || now - _lastReminder < MinimumGap)
        {
            return;
        }

        _lastReminder = now;
        _loudFor = TimeSpan.Zero;

        _flyout.Show(
            "Time for a break",
            $"You've been listening loud for about {(int)RemindAfter.TotalMinutes} minutes. This is a relative level, not a measurement at your ears.",
            FlyoutTone.Caution,
            TimeSpan.FromSeconds(12),
            actionLabel: "Turn it down",
            action: TurnDown);
    }

    private bool IsAnythingLoud()
    {
        try
        {
            var defaultDevice = _devices.GetRenderDevices().FirstOrDefault(device => device.IsDefault);

            if (defaultDevice is null)
            {
                return false;
            }

            var masterVolume = _devices.GetMasterVolume(defaultDevice.DeviceId);

            return _sessions.GetSessionsForDefaultDevice()
                .Where(session => !session.IsSystemSoundsSession)
                .Any(session => _safety.IsLoud(session.PeakLevel, masterVolume));
        }
        catch (Exception ex)
        {
            // A device disappearing mid-sample must not take a background timer,
            // and therefore the app, down with it.
            CrashLog.Write(ex);
            return false;
        }
    }

    private void TurnDown()
    {
        var defaultDevice = _devices.GetRenderDevices().FirstOrDefault(device => device.IsDefault);

        if (defaultDevice is null)
        {
            return;
        }

        var current = _devices.GetMasterVolume(defaultDevice.DeviceId);
        var reduced = Math.Clamp(current * TurnDownTo, 0, 1);

        _devices.SetMasterVolume(defaultDevice.DeviceId, (float)reduced);

        _flyout.Show(defaultDevice.DisplayName, $"{reduced * 100:F0}%", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
    }
}
