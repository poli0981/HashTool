using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace CheckHash.Services;

public class UpdateService
{
    // URL GitHub Repo
    private const string RepoUrl = "https://github.com/poli0981/CheckHash-Multiflatform";
    
    private UpdateManager? _manager;
    private readonly HttpClient _httpClient;

    public string CurrentVersion => _manager?.CurrentVersion?.ToString() ?? "0.0.0 (Debug)";

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

    public async Task<UpdateInfo?> CheckForUpdatesAsync(bool allowPrerelease)
    {
        if (_manager == null) return null;

        if (allowPrerelease)
        {
             _manager = new UpdateManager(new GithubSource(RepoUrl, null, true));
        }
        else
        {
             _manager = new UpdateManager(new GithubSource(RepoUrl, null, false));
        }

        return await _manager.CheckForUpdatesAsync();
    }

    public async Task DownloadAndInstallAsync(UpdateInfo info)
    {
        if (_manager == null) return;

        await _manager.DownloadUpdatesAsync(info);
        _manager.ApplyUpdatesAndRestart(info);
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
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                url = $"{apiUrlBase}/releases/tags/v{version}";
                response = await _httpClient.GetAsync(url);
            }

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);
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