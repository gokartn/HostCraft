namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Deployment queue result returned when enqueuing application deployments.
/// </summary>
public record DeploymentQueueResult(int DeploymentId, string Message);
