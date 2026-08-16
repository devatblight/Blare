using System.Text.Json;

namespace BLight.Blare.Core.Settings;

/// <summary>
/// Settings persistence contract. Deliberately just a directory-backed file
/// store with no network dependency anywhere in this project — Blare keeps
/// everything local (see BLight privacy stance: no network access except
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

    public async Task SaveAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        var path = GetPath(key);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: cancellationToken);
    }

    private string GetPath(string key) => Path.Combine(_directory, $"{key}.json");
}
