namespace HostCraft.Core.Models;

public enum DeploymentJobType
{
    Deploy,
    CleanupPreview
}

public record DeploymentJob(
    DeploymentJobType Type,
    int DeploymentId,
    int? ApplicationId = null,
    string? PreviewId = null);
