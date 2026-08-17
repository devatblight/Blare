using Blight.Blare.App.Views;
using Blight.Blare.Audio.Devices;
using Microsoft.UI.Dispatching;

namespace Blight.Blare.App.Services;

/// <summary>
/// Fades everything to silence over a chosen period, then mutes.
///
/// Not on the roadmap, and the most obvious thing missing from an app about
/// hearing: falling asleep to something is exactly when audio runs for hours
/// unattended at whatever level was set while awake. Every media app solves this
/// for itself and none of them solve it for the machine.
///
/// It fades rather than cutting. A hard stop wakes you up, which defeats the
/// point, and a slow ramp is also the gentler thing to do to your ears. The
/// original level is restored on cancel so the next morning isn't a surprise
/// either way.
/// </summary>
public sealed class SleepTimer
{
    /// <summary>Fine enough that the fade is inaudible as steps rather than a slide.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    /// <summary>The last stretch is where the fade actually happens; before that the level is left alone.</summary>
    private static readonly TimeSpan FadeWindow = TimeSpan.FromMinutes(5);

    private readonly AudioDeviceManager _devices;
    private readonly FlyoutService _flyout;

    private DispatcherQueueTimer? _timer;
    private DateTimeOffset _endsAt;
    private string? _deviceId;
    private float _volumeBefore;

    public SleepTimer(AudioDeviceManager devices, FlyoutService flyout)
    {
        _devices = devices;
        _flyout = flyout;
    }

    public bool IsRunning => _timer is not null;

    /// <summary>How long is left, or zero when nothing is scheduled.</summary>
    public TimeSpan Remaining =>
        IsRunning ? Max(_endsAt - DateTimeOffset.UtcNow, TimeSpan.Zero) : TimeSpan.Zero;

    public TimeSpan Total { get; private set; }

    /// <summary>Raised on every tick so a card can show the countdown.</summary>
    public event EventHandler? Ticked;

    public void Start(TimeSpan duration)
    {
        Cancel(restoreVolume: true, announce: false);

        var queue = DispatcherQueue.GetForCurrentThread();
        var defaultDevice = _devices.GetRenderDevices().FirstOrDefault(device => device.IsDefault);

        if (queue is null || defaultDevice is null || duration <= TimeSpan.Zero)
        {
            return;
        }

        _deviceId = defaultDevice.DeviceId;
        _volumeBefore = _devices.GetMasterVolume(_deviceId);
        _endsAt = DateTimeOffset.UtcNow + duration;
        Total = duration;

        _timer = queue.CreateTimer();
        _timer.Interval = TickInterval;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _flyout.Show(
            "Sleep timer set",
            $"Audio fades out over the last few minutes and stops in {duration.TotalMinutes:F0} min.",
            FlyoutTone.Neutral,
            TimeSpan.FromSeconds(4));

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    /// <param name="restoreVolume">Puts the device back to where it was before the fade started.</param>
    public void Cancel(bool restoreVolume = true, bool announce = true)
    {
        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer = null;

        if (restoreVolume && _deviceId is not null)
        {
            _devices.SetMasterVolume(_deviceId, _volumeBefore);
        }

        _deviceId = null;
        Total = TimeSpan.Zero;

        if (announce)
        {
            _flyout.Show("Sleep timer cancelled", "Volume is back where it was.", FlyoutTone.Neutral, TimeSpan.FromSeconds(3));
        }

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private void Tick()
    {
        var remaining = Remaining;

        if (_deviceId is null)
        {
            return;
        }

        if (remaining <= TimeSpan.Zero)
        {
            Finish();
            return;
        }

        // Left alone until the fade window, so a two-hour timer isn't quietly
        // getting quieter for the first hour and fifty-five minutes.
        if (remaining < FadeWindow)
        {
            var fraction = remaining.TotalSeconds / FadeWindow.TotalSeconds;
            _devices.SetMasterVolume(_deviceId, (float)Math.Clamp(_volumeBefore * fraction, 0, 1));
        }

        Ticked?.Invoke(this, EventArgs.Empty);
    }

    private void Finish()
    {
        var deviceId = _deviceId;
        var restore = _volumeBefore;

        // Stop the timer without restoring — the fade is the point.
        Cancel(restoreVolume: false, announce: false);

        if (deviceId is not null)
        {
            _devices.SetMasterVolume(deviceId, 0);
        }

        _flyout.Show(
            "Sleep timer finished",
            "Audio faded out. Turn the volume back up when you're ready.",
            FlyoutTone.Neutral,
            TimeSpan.FromSeconds(8),
            actionLabel: "Restore volume",
            action: () =>
            {
                if (deviceId is not null)
                {
                    _devices.SetMasterVolume(deviceId, restore);
                }
            });
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;
}
