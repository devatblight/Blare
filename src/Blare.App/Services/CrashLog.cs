namespace BLight.Blare.App.Services;

/// <summary>
/// Appends unhandled exceptions to a local log file. Static and dependency-free
/// on purpose: it has to work even when the exception happened while building
/// or resolving services, which is exactly when a DI-injected logger wouldn't
/// be available.
/// </summary>
public static class CrashLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BLight",
        "Blare",
        "crash.log");

    /// <summary>
    /// Runs a task without awaiting it, but still observes its exceptions.
    /// A bare <c>_ = SomeAsync()</c> leaves failures unobserved, and .NET
    /// rethrows those on the finalizer thread — which kills the process at an
    /// unrelated moment and makes the crash almost impossible to attribute.
    /// </summary>
    public static void FireAndForget(Task task) =>
        task.ContinueWith(t => Write(t.Exception!), TaskContinuationOptions.OnlyOnFaulted);

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(
                FilePath,
                $"""

                ===== {DateTimeOffset.Now:u} =====
                {exception}

                """);
        }
        catch
        {
            // Logging a crash must never itself crash the app.
        }
    }

    public static string ReadRecent(int maxCharacters = 4000)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return "(no crashes recorded)";
            }

            var text = File.ReadAllText(FilePath);
            return text.Length <= maxCharacters ? text : text[^maxCharacters..];
        }
        catch (Exception ex)
        {
            return $"(could not read crash log: {ex.Message})";
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // Nothing useful to do if it won't delete.
        }
    }
}
