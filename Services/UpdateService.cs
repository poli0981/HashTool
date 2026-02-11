using System;
using System.Net;
using System.Net.Http;
using System.Linq;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CheckHash.Services;

public enum NetworkStatus
{
    Connected,
    NoConnection,
    ClientError,
    ServerError,
    ApiLimitExceeded
}

public class UpdateService
{
    private static UpdateService? _instance;
    public static UpdateService Instance => _instance ??= new UpdateService();
    // URL GitHub Repo
    private const string RepoUrl = "https://github.com/poli0981/CheckHash-Multiflatform";
    private readonly HttpClient _httpClient;

    private UpdateManager? _manager;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        // GitHub API requires a User-Agent header
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CheckHash", "1.0"));

        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }
        catch
        {
            // Ignore initialization errors
        }
    }

    public string CurrentVersion => _manager?.CurrentVersion?.ToString() ?? "0.0.0 (Debug)";

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool allowPrerelease)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, allowPrerelease));
            return await _manager.CheckForUpdatesAsync();
        }
        catch
        {
            return null;
        }
    }

    public async Task DownloadUpdatesAsync(UpdateInfo info, Action<int>? progress = null)
    {
        if (_manager == null) return;

        await _manager.DownloadUpdatesAsync(info, progress);
    }

    public void ApplyUpdatesAndRestart(UpdateInfo info)
    {
        _manager?.ApplyUpdatesAndRestart(info);
    }

    public async Task<NetworkStatus> CheckConnectivityAsync()
    {
        try
        {
            var apiUrlBase = RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/");
            using var request = new HttpRequestMessage(HttpMethod.Head, apiUrlBase);
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode) return NetworkStatus.Connected;

            var code = (int)response.StatusCode;

            if (code == 403 || code == 429) // 403 Forbidden (Rate Limit) or 429 Too Many Requests
            {
                if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var values))
                {
                    var remaining = values.FirstOrDefault();
                    if (remaining == "0") return NetworkStatus.ApiLimitExceeded;
                }

                if (code == 429) return NetworkStatus.ApiLimitExceeded;
            }

            if (code >= 500) return NetworkStatus.ServerError;
            if (code >= 400) return NetworkStatus.ClientError;

            return NetworkStatus.NoConnection;
        }
        catch (HttpRequestException)
        {
            return NetworkStatus.NoConnection;
        }
        catch
        {
            return NetworkStatus.NoConnection;
        }
    }

    // Release Notes from GitHub API
    public async Task<string> GetReleaseNotesAsync(string version)
    {
        try
        {
            // Build GitHub API URL of the release by tag name
            // From https://github.com/user/repo
            // To https://api.github.com/repos/user/repo/releases/tags/{version}

            var apiUrlBase = RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/");

            // Try without 'v' prefix (e.g: 1.0.1)
            var url = $"{apiUrlBase}/releases/tags/{version}";

            var response = await _httpClient.GetAsync(url);

            // If url not found, try with 'v' prefix (e.g: v1.0.1)
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                url = $"{apiUrlBase}/releases/tags/v{version}";
                response = await _httpClient.GetAsync(url);
            }

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                var node = await JsonNode.ParseAsync(stream);
                return node?["body"]?.ToString() ?? "No release notes available.";
            }
        }
        catch
        {
            // Ignore network errors
        }

        return "Don't download information on GitHub";
    }
}