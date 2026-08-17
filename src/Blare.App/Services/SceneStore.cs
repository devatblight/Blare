using System.Text.Json;
using Blight.Blare.Core.Scenes;
using Blight.Blare.Core.Settings;

namespace Blight.Blare.App.Services;

/// <summary>
/// Persists the user's scenes, and reads and writes them as plain files.
///
/// Export is a local file, not an account: a scene is a handful of app names and
/// numbers, and there is no reason for it to leave the machine to be shared.
/// </summary>
public sealed class SceneStore
{
    private const string StorageKey = "scenes";

    private static readonly JsonSerializerOptions ExportOptions = new() { WriteIndented = true };

    private readonly ISettingsStore _store;

    public SceneStore(ISettingsStore store)
    {
        _store = store;
        Book.Changed += (_, _) => CrashLog.FireAndForget(SaveAsync());
    }

    public SceneBook Book { get; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<List<Scene>>(StorageKey, cancellationToken);

        if (saved is not null)
        {
            Book.Restore(saved);
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) =>
        _store.SaveAsync(StorageKey, Book.Scenes.ToList(), cancellationToken);

    public async Task ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, Book.Scenes.ToList(), ExportOptions, cancellationToken);
    }

    /// <summary>
    /// Merges scenes from a file. Returns how many were taken.
    ///
    /// Merges rather than replaces, so importing one shared scene doesn't wipe
    /// everything the user already had.
    /// </summary>
    public async Task<int> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var scenes = await JsonSerializer.DeserializeAsync<List<Scene>>(stream, cancellationToken: cancellationToken);

        if (scenes is null)
        {
            return 0;
        }

        var imported = 0;

        foreach (var scene in scenes.Where(scene => !string.IsNullOrWhiteSpace(scene.Name)))
        {
            Book.Save(scene);
            imported++;
        }

        return imported;
    }
}
