using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.Display;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace BLight.Blare.Audio.Devices;

public sealed record MonitorAudioControl(
    int Index,
    string Description,
    bool SupportsVolume,
    uint Volume,
    uint MaximumVolume)
{
    public double VolumePercent => MaximumVolume == 0 ? 0 : (double)Volume / MaximumVolume * 100;

    /// <summary>Windows commonly reports every display as "Generic PnP Monitor", so the index is what actually distinguishes them.</summary>
    public string DisplayName => $"{Description} {Index + 1}";
}

/// <summary>
/// Reads and writes the speaker volume built into a display, over DDC/CI.
///
/// This reaches a control Windows' own mixer doesn't expose: a monitor with
/// built-in speakers has its own amplifier volume, separate from both the app
/// session volume and the Windows endpoint volume. Monitors expose it as VCP
/// register 0x62 ("audio speaker volume") on the display data channel.
///
/// Only displays can be reached this way. A powered speaker box on a 3.5mm
/// jack has a purely analogue knob downstream of the sound card, with no data
/// path back to the PC — that one genuinely cannot be read by any software.
///
/// DDC/CI is also widely half-implemented: plenty of monitors advertise the
/// capability and then ignore writes, or report a maximum of zero, so every
/// call here is treated as fallible rather than assumed to work.
/// </summary>
public sealed class MonitorVolumeController
{
    /// <summary>VCP code for audio speaker volume, from the MCCS specification.</summary>
    private const byte AudioSpeakerVolumeCode = 0x62;

    public unsafe IReadOnlyList<MonitorAudioControl> GetControls()
    {
        var results = new List<MonitorAudioControl>();
        var index = 0;

        foreach (var (handle, description) in EnumeratePhysicalMonitors())
        {
            try
            {
                uint current = 0;
                uint maximum = 0;
                MC_VCP_CODE_TYPE codeType;

                var ok = PInvoke.GetVCPFeatureAndVCPFeatureReply(
                    handle,
                    AudioSpeakerVolumeCode,
                    &codeType,
                    &current,
                    &maximum) != 0;

                results.Add(new MonitorAudioControl(index, description, ok && maximum > 0, current, maximum));
            }
            catch (Exception)
            {
                results.Add(new MonitorAudioControl(index, description, false, 0, 0));
            }
            finally
            {
                DestroyMonitor(handle);
                index++;
            }
        }

        return results;
    }

    /// <summary>Sets a display's speaker volume as a percentage. Returns false when the display refuses or doesn't support it.</summary>
    public unsafe bool TrySetVolumePercent(int index, double percent)
    {
        var currentIndex = 0;

        foreach (var (handle, _) in EnumeratePhysicalMonitors())
        {
            try
            {
                if (currentIndex != index)
                {
                    continue;
                }

                uint value = 0;
                uint maximum = 0;
                MC_VCP_CODE_TYPE codeType;

                if (PInvoke.GetVCPFeatureAndVCPFeatureReply(handle, AudioSpeakerVolumeCode, &codeType, &value, &maximum) == 0
                    || maximum == 0)
                {
                    return false;
                }

                var target = (uint)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * maximum);
                return PInvoke.SetVCPFeature(handle, AudioSpeakerVolumeCode, target) != 0;
            }
            finally
            {
                DestroyMonitor(handle);
                currentIndex++;
            }
        }

        return false;
    }

    private static unsafe List<(HANDLE Handle, string Description)> EnumeratePhysicalMonitors()
    {
        var monitors = new List<HMONITOR>();

        PInvoke.EnumDisplayMonitors(
            new HDC(IntPtr.Zero),
            (RECT?)null,
            (HMONITOR monitor, HDC _, RECT* _, LPARAM _) =>
            {
                monitors.Add(monitor);
                return true;
            },
            new LPARAM(0));

        var results = new List<(HANDLE, string)>();

        foreach (var monitor in monitors)
        {
            uint count = 0;
            if (!PInvoke.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, &count) || count == 0)
            {
                continue;
            }

            var physical = new PHYSICAL_MONITOR[count];
            fixed (PHYSICAL_MONITOR* physicalPtr = physical)
            {
                if (!PInvoke.GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalPtr))
                {
                    continue;
                }
            }

            foreach (var entry in physical)
            {
                results.Add((entry.hPhysicalMonitor, DescriptionOf(entry)));
            }
        }

        return results;
    }

    private static unsafe string DescriptionOf(PHYSICAL_MONITOR monitor)
    {
        var description = monitor.szPhysicalMonitorDescription.ToString();
        return string.IsNullOrWhiteSpace(description) ? "Display" : description;
    }

    private static unsafe void DestroyMonitor(HANDLE handle)
    {
        var single = new PHYSICAL_MONITOR { hPhysicalMonitor = handle };
        PInvoke.DestroyPhysicalMonitors(1, &single);
    }
}
