using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace HashTool.App.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,

    /// <summary>Running from a build that Velopack did not install (e.g. <c>dotnet run</c>): updates are unavailable.</summary>
    NotInstalled,
    NetworkError,
    RateLimited,
    Failed
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null, string? Version = null,
    string? ReleaseNotes = null, Exception? Error = null);

/// <summary>Wraps Velopack's <see cref="UpdateManager"/> for the stable GitHub release channel.</summary>
public sealed class UpdateService
{
    public const string RepositoryUrl = "https://github.com/poli0981/HashTool";

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager? _manager;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        try
        {
            _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Velopack update manager is unavailable");
        }
    }

    public bool IsInstalled => _manager?.IsInstalled ?? false;

    public bool IsPortable => _manager?.IsPortable ?? false;

    public bool IsUpdatePendingRestart => _manager?.UpdatePendingRestart is not null;

    /// <summary>Installed version, or the assembly's version when running an uninstalled build.</summary>
    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? typeof(UpdateService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !_manager.IsInstalled) return new UpdateCheckResult(UpdateCheckStatus.NotInstalled);

        try
        {
            var update = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (update is null)
            {
                _logger.LogInformation("No update available (current {Version})", CurrentVersion);
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            }

            var version = update.TargetFullRelease.Version.ToString();
            var notes = update.TargetFullRelease.NotesMarkdown;
            if (string.IsNullOrWhiteSpace(notes)) notes = await GetReleaseNotesAsync(version, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Update {Version} is available", version);
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, update, version, notes);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning(ex, "GitHub rate limit reached while checking for updates");
            return new UpdateCheckResult(UpdateCheckStatus.RateLimited, Error: ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error while checking for updates");
            return new UpdateCheckResult(UpdateCheckStatus.NetworkError, Error: ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update check failed");
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Error: ex);
        }
    }

    /// <summary>Downloads the update. <paramref name="progress"/> is called on a background thread.</summary>
    public Task DownloadAsync(UpdateInfo update, Action<int>? progress, CancellationToken cancellationToken) =>
        _manager is null
            ? Task.CompletedTask
            : _manager.DownloadUpdatesAsync(update, progress, cancellationToken);

    public void ApplyAndRestart(UpdateInfo update)
    {
        _logger.LogInformation("Restarting to install {Version}", update.TargetFullRelease.Version);
        _manager?.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }

    /// <summary>Installs the downloaded update after the app exits, without restarting it.</summary>
    public void ApplyOnExit(UpdateInfo update)
    {
        _logger.LogInformation("Update {Version} will be installed on exit", update.TargetFullRelease.Version);
        _manager?.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
    }

    /// <summary>Release notes from the GitHub release body (for releases packed without notes in the feed).</summary>
    private async Task<string?> GetReleaseNotesAsync(string version, CancellationToken cancellationToken)
    {
        foreach (var tag in new[] { version, "v" + version })
        {
            try
            {
                using var response = await Http.GetAsync($"https://api.github.com/repos/poli0981/HashTool/releases/tags/{Uri.EscapeDataString(tag)}",
                    cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.TryGetProperty("body", out var body)) return body.GetString();
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                _logger.LogDebug(ex, "Could not fetch release notes for {Tag}", tag);
            }
        }

        return null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HashTool", version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
