using Blight.Blare.Audio.Boost;
using Blight.Blare.Audio.Sessions;

// Spike: can boost work by ATTENUATING the original instead of muting it?
//
// Muting is fatal — capture is applied after session volume, so a muted app
// captures pure silence. But capture scales linearly with volume, so leaving
// the original at a tiny level should still yield a usable signal that can be
// amplified back up. The direct leak at that level should be inaudible next to
// the boosted re-render.
//
// Checks: does a very low volume still capture real signal, and does the
// captured level track the volume closely enough to compensate for exactly?

var manager = new AudioSessionManager();
var capture = new ProcessLoopbackCapture();

var target = manager.GetSessionsForDefaultDevice()
    .Where(s => !s.IsSystemSoundsSession)
    .OrderByDescending(s => s.PeakLevel)
    .FirstOrDefault();

if (target is null || target.PeakLevel <= 0.0001f)
{
    Console.WriteLine("Nothing is producing audio — play something and re-run.");
    return;
}

Console.WriteLine($"Target pid {target.ProcessId}, current peak {target.PeakLevel:F4}\n");

var originalVolume = target.Volume;

try
{
    Console.WriteLine("volume   captured peak   x50 headroom   usable?");
    Console.WriteLine("------   -------------   ------------   -------");

    foreach (var level in new[] { 1.0f, 0.10f, 0.05f, 0.02f, 0.01f })
    {
        manager.SetMute(target.ProcessId, false);
        manager.SetVolume(target.ProcessId, level);
        await Task.Delay(500);

        var result = await capture.CaptureAsync(target.ProcessId, TimeSpan.FromSeconds(2));

        // What the signal looks like once scaled back up to unity.
        var compensated = result.PeakAmplitude / level;
        var usable = result.PeakAmplitude > 0.0001f;

        Console.WriteLine(
            $"{level,6:P0}   {result.PeakAmplitude,13:F6}   {compensated,12:F4}   {(usable ? "yes" : "NO")}");
    }

    Console.WriteLine();
    Console.WriteLine("If the compensated column stays roughly constant, attenuate-and-amplify");
    Console.WriteLine("reconstructs the original faithfully and boost is achievable this way.");
}
finally
{
    manager.SetVolume(target.ProcessId, originalVolume);
    manager.SetMute(target.ProcessId, false);
    Console.WriteLine($"\nRestored volume to {originalVolume:P0}.");
}
