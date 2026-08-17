namespace Blight.Blare.Core.Models;

/// <summary>
/// A mixer entry for one app, identified by a stable key (executable path or
/// AUMID) rather than an ephemeral audio session id, so volume/consent state
/// survives across process restarts.
/// </summary>
public sealed record AppSession(
    string AppKey,
    string DisplayName,
    string ExecutablePath);

public sealed record OutputDevice(
    string DeviceId,
    string DisplayName,
    bool IsDefault);

/// <summary>Level is 0.0-1.0, the range Windows' session volume accepts. There is no path above unity.</summary>
public sealed record VolumeState(double Level, bool IsMuted)
{
    public static VolumeState Unity => new(1.0, false);
}
