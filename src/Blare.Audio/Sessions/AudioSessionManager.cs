using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace BLight.Blare.Audio.Sessions;

public sealed record AudioSessionInfo(
    uint ProcessId,
    string DisplayName,
    Guid GroupingParam,
    float Volume,
    bool IsMuted,
    float PeakLevel,
    bool IsSystemSoundsSession);

/// <summary>
/// Phase 1 session enumeration/control (see plan §2). This is the
/// Milestone-0-spike-derived first pass: enumerates sessions on the
/// current default render device and reads their volume/mute/peak state.
/// Grouping-into-one-row, expiry debounce, and live event subscriptions
/// (IAudioSessionEvents/IMMNotificationClient) are follow-up work, not
/// re-derived here.
/// </summary>
public sealed class AudioSessionManager
{
    public unsafe IReadOnlyList<AudioSessionInfo> GetSessionsForDefaultDevice()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);
        return GetSessions(device);
    }

    internal unsafe IReadOnlyList<AudioSessionInfo> GetSessions(IMMDevice device)
    {
        var sessionManagerIid = typeof(IAudioSessionManager2).GUID;
        device.Activate(&sessionManagerIid, CLSCTX.CLSCTX_ALL, null, out var sessionManagerObj);
        var sessionManager = (IAudioSessionManager2)sessionManagerObj;

        var sessionEnumerator = sessionManager.GetSessionEnumerator();
        sessionEnumerator.GetCount(out var count);

        var results = new List<AudioSessionInfo>(count);
        for (var i = 0; i < count; i++)
        {
            sessionEnumerator.GetSession(i, out var control);
            var control2 = (IAudioSessionControl2)control;

            control2.GetProcessId(out var processId);
            // IsSystemSoundsSession returns S_OK (0) for true, S_FALSE (1) for false — both are
            // "successful" HRESULTs, so this has to check the raw value, not .Succeeded.
            var isSystemSounds = control2.IsSystemSoundsSession().Value == 0;

            control2.GetDisplayName(out var displayNamePtr);
            var displayName = displayNamePtr.ToString();
            if (displayNamePtr.Value is not null)
            {
                Marshal.FreeCoTaskMem((IntPtr)displayNamePtr.Value);
            }

            Guid groupingParam;
            control2.GetGroupingParam(&groupingParam);

            var simpleVolume = (ISimpleAudioVolume)control;
            simpleVolume.GetMasterVolume(out var volume);
            simpleVolume.GetMute(out var isMuted);

            var meter = (IAudioMeterInformation)control;
            meter.GetPeakValue(out var peak);

            results.Add(new AudioSessionInfo(
                processId,
                displayName,
                groupingParam,
                volume,
                isMuted != 0,
                peak,
                isSystemSounds));
        }

        return results;
    }
}
