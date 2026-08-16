using BLight.Blare.Audio.Sessions;

var manager = new AudioSessionManager();
var sessions = manager.GetSessionsForDefaultDevice();

Console.WriteLine($"Found {sessions.Count} session(s) on the default render device:\n");

foreach (var session in sessions)
{
    Console.WriteLine(
        $"pid={session.ProcessId,-6} name=\"{session.DisplayName}\" " +
        $"volume={session.Volume:P0} muted={session.IsMuted} peak={session.PeakLevel:F3} " +
        $"systemSounds={session.IsSystemSoundsSession} grouping={session.GroupingParam}");
}
