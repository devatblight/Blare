namespace Blight.Blare.Core.Scenes;

/// <summary>One app's place in a scene.</summary>
public sealed record SceneLevel(string AppKey, double VolumePercent, bool IsMuted);

/// <summary>
/// A named set of levels the user can recall.
///
/// The recurring case is a desk that has to be two different things: gaming
/// wants the game up and the browser down, a call wants the opposite, and
/// rebuilding either by hand every time is exactly the chore a mixer should
/// remove.
/// </summary>
public sealed record Scene(string Name, IReadOnlyList<SceneLevel> Levels)
{
    public SceneLevel? For(string appKey) =>
        Levels.FirstOrDefault(level => string.Equals(level.AppKey, appKey, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The user's scenes, keyed by name.
///
/// Names are compared case-insensitively so "Gaming" and "gaming" are one scene
/// rather than two that shadow each other.
/// </summary>
public sealed class SceneBook
{
    private readonly List<Scene> _scenes = new();

    public IReadOnlyList<Scene> Scenes => _scenes;

    public event EventHandler? Changed;

    public Scene? Get(string name) =>
        _scenes.FirstOrDefault(scene => string.Equals(scene.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds a scene, or replaces one of the same name in place so its position in the list is kept.</summary>
    public void Save(Scene scene)
    {
        if (string.IsNullOrWhiteSpace(scene.Name))
        {
            return;
        }

        var index = _scenes.FindIndex(existing =>
            string.Equals(existing.Name, scene.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _scenes[index] = scene;
        }
        else
        {
            _scenes.Add(scene);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string name)
    {
        if (_scenes.RemoveAll(scene => string.Equals(scene.Name, name, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Rename(string from, string to)
    {
        if (string.IsNullOrWhiteSpace(to) || Get(from) is not { } scene)
        {
            return;
        }

        Remove(from);
        Save(scene with { Name = to });
    }

    public void Restore(IEnumerable<Scene>? scenes)
    {
        _scenes.Clear();

        if (scenes is null)
        {
            return;
        }

        foreach (var scene in scenes)
        {
            if (!string.IsNullOrWhiteSpace(scene.Name))
            {
                _scenes.Add(scene);
            }
        }
    }
}
