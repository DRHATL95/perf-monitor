using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerfMonitor.App.Services;

/// <summary>
/// Background service that polls the GitHub Releases API for a newer
/// version of PerfMonitor. Fires <see cref="UpdateAvailable"/> when one
/// is found. Stays silent (no events, no prompts) on network failures,
/// rate-limit hits, or when running from a dev path.
/// </summary>
public sealed class UpdateChecker : BackgroundService
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/DRHATL95/perf-monitor/releases/latest";

    private static readonly TimeSpan StartupDelay  = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<UpdateChecker> _log;
    private readonly HttpClient _http;

    public event EventHandler<UpdateInfo>? UpdateAvailable;

    public UpdateChecker(ILogger<UpdateChecker> log)
    {
        _log = log;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub requires a User-Agent header for unauthenticated requests.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PerfMonitor-UpdateCheck/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ShouldSkipUpdates())
        {
            _log.LogInformation("Update check disabled (debugger attached or running from non-installed path).");
            return;
        }

        try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await CheckOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Update check failed (non-fatal, will retry)."); }

            try { await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync(ReleasesEndpoint, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _log.LogDebug("GitHub returned {Status} — skipping this tick.", resp.StatusCode);
            return;
        }

        var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
        if (release is null || string.IsNullOrWhiteSpace(release.TagName)) return;

        var current = NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version);
        var latest  = ParseTagVersion(release.TagName);
        if (latest is null) return;

        _log.LogInformation("Update check: current={Current}, latest={Latest}", current, latest);

        if (latest > current)
        {
            UpdateAvailable?.Invoke(this, new UpdateInfo(release.TagName, release.HtmlUrl ?? ""));
        }
    }

    /// <summary>
    /// Skip update checks for non-production runs:
    /// - Debugger attached -> in IDE.
    /// - Running .exe outside %LOCALAPPDATA%\Programs -> portable / dev build.
    /// </summary>
    private static bool ShouldSkipUpdates()
    {
        if (Debugger.IsAttached) return true;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return true;
        var programsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs");
        return !exe.StartsWith(programsDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parse a GitHub tag like "v0.1.5" or "v0.2.0-beta.1" to a Version
    /// for ordering. Pre-release suffixes are stripped before parsing —
    /// "v0.2.0-beta.1" therefore compares equal to "v0.2.0", which is
    /// imperfect but acceptable for "should we prompt the user?" decisions.
    /// </summary>
    private static Version? ParseTagVersion(string tag)
    {
        var trimmed = tag.TrimStart('v', 'V');
        var dash = trimmed.IndexOf('-');
        if (dash > 0) trimmed = trimmed[..dash];
        return Version.TryParse(trimmed, out var parsed) ? NormalizeVersion(parsed) : null;
    }

    /// <summary>
    /// Drop the revision dimension so a stamped 0.1.4.0 compares correctly
    /// to a parsed 0.1.4 (otherwise the .0-vs-(-1) revision misorders them).
    /// </summary>
    private static Version NormalizeVersion(Version? v)
    {
        if (v is null) return new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public override void Dispose()
    {
        try { _http.Dispose(); } catch { }
        base.Dispose();
    }

    public sealed record UpdateInfo(string Tag, string HtmlUrl);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
