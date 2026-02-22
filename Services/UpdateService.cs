using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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
    // URL GitHub Repo
    private const string RepoUrl = "https://github.com/poli0981/HashTool";
    private static UpdateService? _instance;
    private readonly HttpClient _httpClient;

    private UpdateManager? _manager;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HashTool", version));

        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }
        catch
        {
            // Ignore initialization errors
        }
    }

    public static UpdateService Instance => _instance ??= new UpdateService();

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

    public async Task<bool> HasPreReleasesAsync()
    {
        try
        {
            var apiUrlBase = RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/");
            var url = $"{apiUrlBase}/releases?per_page=10";

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                var node = await JsonNode.ParseAsync(stream);
                if (node is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        if (item?["prerelease"]?.GetValue<bool>() == true)
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    public async Task<List<string>> GetAvailableVersionsAsync()
    {
        var versions = new List<string>();
        try
        {
            var apiUrlBase = RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/");
            var url = $"{apiUrlBase}/releases?per_page=30"; // Get last 30 releases

            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                var node = await JsonNode.ParseAsync(stream);
                if (node is JsonArray array)
                {
                    foreach (var item in array)
                    {
                        var tagName = item?["tag_name"]?.ToString();
                        if (!string.IsNullOrEmpty(tagName))
                        {
                            versions.Add(tagName);
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore
        }

        return versions;
    }

    public async Task<string?> GetSpecificVersionUrlAsync(string version)
    {
        try
        {
            var tag = version;
            var node = await GetReleaseNode(tag);
            if (node == null && !tag.StartsWith("v"))
            {
                tag = "v" + version;
                node = await GetReleaseNode(tag);
            }

            if (node == null) return null;

            var assets = node["assets"]?.AsArray();
            if (assets == null) return null;

            foreach (var asset in assets)
            {
                var name = asset?["name"]?.ToString();
                if (name != null)
                {
                    if (OperatingSystem.IsWindows() && name.EndsWith("Setup.exe"))
                        return asset?["browser_download_url"]?.ToString();
                    if (OperatingSystem.IsMacOS() && name.EndsWith(".pkg"))
                        return asset?["browser_download_url"]?.ToString();
                }
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    public async Task DownloadAndRunInstallerAsync(string url, Action<int>? progress = null)
    {
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1 && progress != null;

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream =
                new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                if (canReportProgress)
                {
                    progress?.Invoke((int)((double)totalRead / totalBytes * 100));
                }
            }

            if (OperatingSystem.IsWindows())
            {
                var process = Process.Start(new ProcessStartInfo(tempPath) { UseShellExecute = true });
                if (process != null)
                {
                    // Wait for the installer to start properly
                    await Task.Delay(2000);

                    // Fire and forget task to delete the installer after it exits
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await process.WaitForExitAsync();
                            if (File.Exists(tempPath))
                            {
                                File.Delete(tempPath);
                            }
                        }
                        catch
                        {
                            // Best effort deletion
                        }
                    });

                    // Exit the application to allow the installer to overwrite files
                    Environment.Exit(0);
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", $"\"{tempPath}\"");
            }
        }
        catch
        {
            // Ensure cleanup on error
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    private async Task<JsonNode?> GetReleaseNode(string tag)
    {
        try
        {
            var apiUrlBase = RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/");
            var url = $"{apiUrlBase}/releases/tags/{tag}";
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                await using var stream = await response.Content.ReadAsStreamAsync();
                return await JsonNode.ParseAsync(stream);
            }
        }
        catch
        {
            // Ignore
        }

        return null;
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
            var apiUrlBase = RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/");

            var url = $"{apiUrlBase}/releases/tags/{version}";

            var response = await _httpClient.GetAsync(url);

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