using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Updates;

public class UpdateService : IUpdateService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UpdateService> _logger;
    private const string GitHubApiUrl = "https://api.github.com/repos/gokartn/hostcraft/releases/latest";
    
    public UpdateService(HttpClient httpClient, ILogger<UpdateService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // GitHub API requires User-Agent header
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HostCraft-Update-Checker");
        }
    }
    
    public string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version?.ToString(3) ?? "0.0.1";
    }
    
    public async Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();
        
        try
        {
            var response = await _httpClient.GetAsync(GitHubApiUrl, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                // Only log warning for server errors, not 404 (repo might not exist yet)
                if ((int)response.StatusCode >= 500)
                {
                    _logger.LogWarning("Failed to check for updates: {StatusCode}", response.StatusCode);
                }
                else
                {
                    _logger.LogDebug("Update check returned {StatusCode} - repository may not exist yet", response.StatusCode);
                }
                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    UpdateAvailable = false
                };
            }
            
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(content);
            
            if (release == null)
            {
                return new UpdateInfo
                {
                    CurrentVersion = currentVersion,
                    UpdateAvailable = false
                };
            }
            
            var latestVersion = release.TagName?.TrimStart('v') ?? currentVersion;
            var updateAvailable = CompareVersions(currentVersion, latestVersion) < 0;
            
            return new UpdateInfo
            {
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                UpdateAvailable = updateAvailable,
                PublishedAt = release.PublishedAt,
                ReleaseNotes = release.Body,
                DownloadUrl = release.Assets?.FirstOrDefault()?.BrowserDownloadUrl,
                HtmlUrl = release.HtmlUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for updates");
            return new UpdateInfo
            {
                CurrentVersion = currentVersion,
                UpdateAvailable = false
            };
        }
    }
    
    public async Task<bool> TriggerUpdateAsync(string version, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Triggering update to version {Version}", version);
            
            // For Docker deployments, the update would typically involve:
            // 1. Pulling the new Docker image
            // 2. Stopping the current container
            // 3. Starting a new container with the new image
            // 4. Cleaning up old image
            
            // For non-Docker deployments, this might involve:
            // 1. Downloading the new version
            // 2. Extracting files
            // 3. Restarting the application
            
            // This is a placeholder - actual implementation depends on deployment method
            _logger.LogWarning("Update trigger not fully implemented - requires deployment-specific logic");
            
            return await Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering update");
            return false;
        }
    }
    
    private int CompareVersions(string current, string latest)
    {
        try
        {
            var currentParts = current.Split('.').Select(int.Parse).ToArray();
            var latestParts = latest.Split('.').Select(int.Parse).ToArray();
            
            for (int i = 0; i < Math.Min(currentParts.Length, latestParts.Length); i++)
            {
                if (currentParts[i] < latestParts[i]) return -1;
                if (currentParts[i] > latestParts[i]) return 1;
            }
            
            return currentParts.Length.CompareTo(latestParts.Length);
        }
        catch
        {
            return 0;
        }
    }
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("body")]
    public string? Body { get; set; }
    
    [JsonPropertyName("published_at")]
    public DateTime? PublishedAt { get; set; }
    
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }
    
    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
