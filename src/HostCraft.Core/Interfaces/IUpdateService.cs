namespace HostCraft.Core.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<bool> TriggerUpdateAsync(string version, CancellationToken cancellationToken = default);
    string GetCurrentVersion();
}

public class UpdateInfo
{
    public string CurrentVersion { get; set; } = "";
    public string? LatestVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? DownloadUrl { get; set; }
    public string? HtmlUrl { get; set; }
}
