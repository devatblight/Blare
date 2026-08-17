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

/// <summary>
/// Level is 0.0-1.0 in Phase 1 (native ISimpleAudioVolume ceiling). Phase 2
/// boost is tracked separately via <see cref="BoostState"/> rather than by
/// letting Level exceed 1.0, so the two mechanisms can't be conflated.
/// </summary>
public sealed record VolumeState(double Level, bool IsMuted)
{
    public static VolumeState Unity => new(1.0, false);
}

/// <summary>GainLinear is 1.0 at unity; anything above 1.0 is a boost.</summary>
public sealed record BoostState(bool IsBoosted, double GainLinear)
{
    public static BoostState None => new(false, 1.0);
}
