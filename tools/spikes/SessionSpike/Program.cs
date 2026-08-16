using BLight.Blare.Audio.Boost;
using BLight.Blare.Audio.Sessions;

// Follow-up diagnostic: mute is confirmed to zero the loopback capture.
// Does session VOLUME do the same, or is volume applied after the capture
// tap? If capture survives volume=0, boost can silence the original to the
// speakers while still capturing full-strength signal to amplify.

var manager = new AudioSessionManager();
var capture = new ProcessLoopbackCapture();

var sessions = manager.GetSessionsForDefaultDevice()
    .Where(s => !s.IsSystemSoundsSession)
    .ToList();

var target = sessions.OrderByDescending(s => s.PeakLevel).FirstOrDefault();

if (target is null || target.PeakLevel <= 0.0001f)
{
    Console.WriteLine("Nothing is producing audio — play something and re-run.");
    return;
}

Console.WriteLine($"Target: pid={target.ProcessId} (current peak {target.PeakLevel:F4})\n");

var originalVolume = target.Volume;

try
{
    foreach (var level in new[] { 1.0f, 0.5f, 0.0f })
    {
        manager.SetMute(target.ProcessId, false);
        manager.SetVolume(target.ProcessId, level);
        await Task.Delay(400);

        var result = await capture.CaptureAsync(target.ProcessId, TimeSpan.FromSeconds(2));
        Console.WriteLine($"volume={level,-5:P0} capture peak={result.PeakAmplitude:F6}");
    }

    Console.WriteLine();
    Console.WriteLine("If peak scales with volume  -> capture is POST-volume; volume=0 also captures silence.");
    Console.WriteLine("If peak stays constant      -> capture is PRE-volume; we can silence via volume=0 and still boost.");
}
finally
{
    manager.SetVolume(target.ProcessId, originalVolume);
    manager.SetMute(target.ProcessId, false);
    Console.WriteLine($"\nRestored volume to {originalVolume:P0}, unmuted.");
}
