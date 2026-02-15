namespace HostCraft.Core.Interfaces;

public interface IUpdateService
{
    Task<UpdateInfo> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<UpdateTriggerResult> TriggerUpdateAsync(string version, CancellationToken cancellationToken = default);
    UpdateProgress GetUpdateProgress();
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
    public string? ApiImageUrl { get; set; }
    public string? WebImageUrl { get; set; }
}

public class UpdateTriggerResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public UpdateMode Mode { get; set; }
}

public class UpdateProgress
{
    public bool InProgress { get; set; }
    public string? TargetVersion { get; set; }
    public UpdateStep CurrentStep { get; set; }
    public string? StatusMessage { get; set; }
    public string? Error { get; set; }
    public DateTime? StartedAt { get; set; }
}

public enum UpdateStep
{
    Idle,
    PullingApiImage,
    PullingWebImage,
    UpdatingWebService,
    UpdatingApiService,
    WaitingForHealthy,
    Completed,
    Failed
}

public enum UpdateMode
{
    Swarm,
    Standalone
}
