using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Orchestrates the complete deployment process for applications.
/// </summary>
public interface IDeploymentService
{
    Task<Deployment> DeployApplicationAsync(int applicationId, string? commitHash = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> StopApplicationAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<bool> RestartApplicationAsync(int applicationId, CancellationToken cancellationToken = default);
    Task<bool> ScaleApplicationAsync(int applicationId, int replicas, CancellationToken cancellationToken = default);
    Task<bool> RollbackDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default);
    Task<Stream> GetApplicationLogsAsync(int applicationId, CancellationToken cancellationToken = default);
}
