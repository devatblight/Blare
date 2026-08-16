using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace BLight.Blare.Audio.Sessions;

public sealed record AudioSessionInfo(
    string SessionKey,
    uint ProcessId,
    string DisplayName,
    Guid GroupingParam,
    float Volume,
    bool IsMuted,
    float PeakLevel,
    bool IsSystemSoundsSession);

/// <summary>
/// Phase 1 session enumeration/control (see plan §2): enumerates sessions
/// on the current default render device, reads their volume/mute/peak
/// state, and lets the UI push volume/mute changes back. Grouping (see
/// <see cref="SessionGroupTracker"/>) and live event subscriptions
/// (IAudioSessionEvents/IMMNotificationClient) are follow-up work — this
/// re-enumerates on every call rather than holding COM pointers across
/// calls, which is simpler and safer for now given how infrequently the UI
/// actually pushes a change.
/// </summary>
public sealed class AudioSessionManager
{
    public unsafe IReadOnlyList<AudioSessionInfo> GetSessionsForDefaultDevice()
    {
        return EnumerateSessions(control =>
        {
            var control2 = (IAudioSessionControl2)control;

            control2.GetProcessId(out var processId);
            // IsSystemSoundsSession returns S_OK (0) for true, S_FALSE (1) for false — both are
            // "successful" HRESULTs, so this has to check the raw value, not .Succeeded.
            var isSystemSounds = control2.IsSystemSoundsSession().Value == 0;

            control2.GetSessionIdentifier(out var sessionKeyPtr);
            var sessionKey = FreeAndReadString(sessionKeyPtr);

            control2.GetDisplayName(out var displayNamePtr);
            var displayName = FreeAndReadString(displayNamePtr);

            var groupingParam = GetGroupingParam(control2);

            var simpleVolume = (ISimpleAudioVolume)control;
            simpleVolume.GetMasterVolume(out var volume);
            simpleVolume.GetMute(out var isMuted);

            var meter = (IAudioMeterInformation)control;
            meter.GetPeakValue(out var peak);

            return new AudioSessionInfo(
                sessionKey,
                processId,
                displayName,
                groupingParam,
                volume,
                isMuted != 0,
                peak,
                isSystemSounds);
        });
    }

    public unsafe void SetVolume(uint processId, float level) =>
        WithSimpleVolumeForProcess(processId, sv => sv.SetMasterVolume(level, null));

    public unsafe void SetMute(uint processId, bool isMuted) =>
        WithSimpleVolumeForProcess(processId, sv => sv.SetMute(isMuted, null));

    private unsafe List<T> EnumerateSessions<T>(Func<IAudioSessionControl, T> select)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

        var sessionManagerIid = typeof(IAudioSessionManager2).GUID;
        device.Activate(&sessionManagerIid, CLSCTX.CLSCTX_ALL, null, out var sessionManagerObj);
        var sessionManager = (IAudioSessionManager2)sessionManagerObj;

        var sessionEnumerator = sessionManager.GetSessionEnumerator();
        sessionEnumerator.GetCount(out var count);

        var results = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            sessionEnumerator.GetSession(i, out var control);
            results.Add(select(control));
        }

        return results;
    }

    private unsafe void WithSimpleVolumeForProcess(uint processId, Action<ISimpleAudioVolume> action)
    {
        EnumerateSessions<object?>(control =>
        {
            var control2 = (IAudioSessionControl2)control;
            control2.GetProcessId(out var pid);

            if (pid == processId)
            {
                action((ISimpleAudioVolume)control);
            }

            return null;
        });
    }

    private static unsafe Guid GetGroupingParam(IAudioSessionControl2 control2)
    {
        Guid groupingParam;
        control2.GetGroupingParam(&groupingParam);
        return groupingParam;
    }

    private static unsafe string FreeAndReadString(Windows.Win32.Foundation.PWSTR ptr)
    {
        var value = ptr.ToString();
        if (ptr.Value is not null)
        {
            Marshal.FreeCoTaskMem((IntPtr)ptr.Value);
        }

        return value;
    }
}
