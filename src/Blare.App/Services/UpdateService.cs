using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Blight.Blare.Core.Settings;
using Blight.Blare.Core.Updates;

namespace Blight.Blare.App.Services;

public sealed record AvailableUpdate(ReleaseVersion Version, string DownloadUrl, string ReleaseUrl);

/// <summary>
/// Checks GitHub for a newer release.
///
/// This is the only network call Blare makes. Nothing about the user, their
/// audio, their apps or their listening leaves the machine — the request is an
/// anonymous GET of a public releases endpoint, and the only thing sent is the
/// user agent GitHub requires. It can be turned off entirely, and it never
/// installs anything without being asked.
/// </summary>
public sealed class UpdateService
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/devatblight/Blare/releases/latest";
    private const string EnabledKey = "update-checks-enabled";

    private readonly ISettingsStore _store;
    private readonly FlyoutService _flyout;

    public UpdateService(ISettingsStore store, FlyoutService flyout)
    {
        _store = store;
        _flyout = flyout;
    }

    public bool ChecksEnabled { get; private set; } = true;

    public DateTimeOffset? LastChecked { get; private set; }

    public string? LastError { get; private set; }

    public AvailableUpdate? Available { get; private set; }

    public static ReleaseVersion CurrentVersion =>
        ReleaseVersion.TryParse(
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(), out var version)
            ? version
            : new ReleaseVersion(0, 0, 0);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var saved = await _store.LoadAsync<bool?>(EnabledKey, cancellationToken);
        ChecksEnabled = saved ?? true;
    }

    public async Task SetChecksEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ChecksEnabled = enabled;
        await _store.SaveAsync(EnabledKey, enabled, cancellationToken);
    }

    /// <summary>Asks GitHub for the latest release. Returns null when up to date, checks are off, or the check failed.</summary>
    public async Task<AvailableUpdate?> CheckAsync(bool notify, CancellationToken cancellationToken = default)
    {
        if (!ChecksEnabled)
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Blare", CurrentVersion.ToString()));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await client.GetAsync(ReleasesEndpoint, cancellationToken);

            LastChecked = DateTimeOffset.Now;

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // GitHub returns 404 from releases/latest when a repository has
                // no releases at all. That's the normal state before the first
                // one is tagged, not a failure, and reporting it as an error
                // makes a working install look broken.
                LastError = null;
                Available = null;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                LastError = $"GitHub returned {(int)response.StatusCode}";
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (!root.TryGetProperty("tag_name", out var tagElement)
                || !ReleaseVersion.TryParse(tagElement.GetString(), out var latest))
            {
                LastError = "Could not read the latest version";
                return null;
            }

            LastError = null;

            if (latest <= CurrentVersion)
            {
                Available = null;
                return null;
            }

            var releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            Available = new AvailableUpdate(latest, FindInstaller(root) ?? releaseUrl, releaseUrl);

            if (notify)
            {
                var update = Available;
                _flyout.Show(
                    $"Blare {latest} is available",
                    $"You're on {CurrentVersion}.",
                    Views.FlyoutTone.Neutral,
                    TimeSpan.FromSeconds(10),
                    "Get it",
                    () => OpenInBrowser(update.ReleaseUrl));
            }

            return Available;
        }
        catch (Exception ex)
        {
            // Being offline is not an error worth interrupting anyone over.
            LastError = ex.Message;
            LastChecked = DateTimeOffset.Now;
            return null;
        }
    }

    /// <summary>
    /// Opens the release page rather than installing silently.
    ///
    /// Deliberately not a background auto-install: replacing a running audio
    /// application's binaries underneath the user, unprompted, is the kind of
    /// thing that interrupts a call or a game. The check is automatic; applying
    /// it is the user's decision.
    /// </summary>
    public static void OpenInBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
    }

    private static string? FindInstaller(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;

            if (name is not null && name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)
                && asset.TryGetProperty("browser_download_url", out var url))
            {
                return url.GetString();
            }
        }

        return null;
    }
}
