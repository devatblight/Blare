using Blight.Blare.Audio.Devices;
using Blight.Blare.Audio.Sessions;

// Scratch harness for checking audio behaviour against real hardware.
//
// It only ever reads. Blare no longer renders audio at all — the boost
// experiments this was written for produced a loud burst of noise on a real
// machine and were removed — so nothing here should ever produce sound.

var manager = new AudioSessionManager();

Console.WriteLine("Sessions on the default render device:\n");

foreach (var session in manager.GetSessionsForDefaultDevice())
{
    Console.WriteLine(
        $"  pid {session.ProcessId,-7} vol {session.Volume,6:P0}  peak {session.PeakLevel,7:F4}  " +
        $"{(session.IsMuted ? "muted" : "     ")}  {session.DisplayName}");
}

Console.WriteLine("\nRender devices:\n");

var devices = new AudioDeviceManager();
foreach (var device in devices.GetRenderDevices())
{
    Console.WriteLine(
        $"  {(device.IsDefault ? "*" : " ")} {devices.GetMasterVolume(device.DeviceId),6:P0}  {device.DisplayName}");
}
