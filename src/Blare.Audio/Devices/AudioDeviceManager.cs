using System.Runtime.InteropServices;
using Blight.Blare.Core.Models;
using Windows.Win32;
using Windows.Win32.Media.Audio;
using Windows.Win32.Media.Audio.Endpoints;
using Windows.Win32.System.Com;

namespace Blight.Blare.Audio.Devices;

/// <summary>
/// Phase 1 device enumeration and per-device/master volume (see plan §2).
/// "Master volume" is presented as the current default render device's
/// endpoint volume, matching how Windows' own mixer treats it — there's no
/// separate systemwide master API beyond the default device's endpoint.
/// </summary>
public sealed class AudioDeviceManager
{
    public unsafe IReadOnlyList<OutputDevice> GetRenderDevices()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();

        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDevice);
        var defaultId = GetDeviceId(defaultDevice);

        enumerator.EnumAudioEndpoints(EDataFlow.eRender, DEVICE_STATE.DEVICE_STATE_ACTIVE, out var collection);
        collection.GetCount(out var count);

        var devices = new List<OutputDevice>((int)count);
        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            var id = GetDeviceId(device);
            var name = GetFriendlyName(device);
            devices.Add(new OutputDevice(id, name, id == defaultId));
        }

        return devices;
    }

    public unsafe float GetMasterVolume(string deviceId)
    {
        var endpointVolume = ActivateEndpointVolume(ActivateDeviceById(deviceId));
        endpointVolume.GetMasterVolumeLevelScalar(out var level);
        return level;
    }

    public unsafe void SetMasterVolume(string deviceId, float level)
    {
        // Same 0..1 contract as session volume — clamp rather than throw.
        var endpointVolume = ActivateEndpointVolume(ActivateDeviceById(deviceId));
        endpointVolume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), null);
    }

    private static unsafe IMMDevice ActivateDeviceById(string deviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        fixed (char* idPtr = deviceId)
        {
            enumerator.GetDevice(idPtr, out var device);
            return device;
        }
    }

    private static unsafe IAudioEndpointVolume ActivateEndpointVolume(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        device.Activate(&iid, CLSCTX.CLSCTX_ALL, null, out var obj);
        return (IAudioEndpointVolume)obj;
    }

    private static unsafe string GetDeviceId(IMMDevice device)
    {
        device.GetId(out var idPtr);
        var id = idPtr.ToString();
        if (idPtr.Value is not null)
        {
            Marshal.FreeCoTaskMem((IntPtr)idPtr.Value);
        }

        return id;
    }

    private static unsafe string GetFriendlyName(IMMDevice device)
    {
        device.OpenPropertyStore(0 /* STGM_READ */, out var store);

        var key = PInvoke.PKEY_Device_FriendlyName;
        store.GetValue(&key, out var value);
        var name = value.Anonymous.Anonymous.Anonymous.pwszVal.ToString();
        PInvoke.PropVariantClear(ref value);

        return name;
    }
}
