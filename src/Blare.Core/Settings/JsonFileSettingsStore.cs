using System.Text.Json;

namespace Blight.Blare.Core.Settings;

/// <summary>
/// Settings persistence contract. Deliberately just a directory-backed file
/// store with no network dependency anywhere in this project — Blare keeps
/// everything local (see Blight privacy stance: no network access except
/// app updates, handled elsewhere).
/// </summary>
public interface ISettingsStore
{
    Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default);
}

public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly string _directory;

    /// <param name="directory">
    /// Caller-supplied local directory (the App project passes
    /// ApplicationData.Current.LocalFolder.Path) — kept out of this project
    /// so Blare.Core has no WinRT/Win32 dependency.
    /// </param>
    public JsonFileSettingsStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public async Task<T?> LoadAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Writes to a temporary file and moves it into place.
    ///
    /// Writing straight to the destination truncates it first, so losing power
    /// or being killed mid-write leaves a half-written file where the settings
    /// used to be — and the app comes back with its consent record, saved levels
    /// or dashboard gone. The move is the only step that touches the real file.
    /// </summary>
    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        var temporary = path + ".tmp";

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private string GetPath(string key) => Path.Combine(_directory, $"{key}.json");
}
