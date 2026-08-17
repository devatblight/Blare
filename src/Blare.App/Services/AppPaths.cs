namespace Blight.Blare.App.Services;

/// <summary>Where Blare keeps its local state. Surfaced in Diagnostics so it's never a mystery where settings live.</summary>
public sealed record AppPaths(string SettingsDirectory);
