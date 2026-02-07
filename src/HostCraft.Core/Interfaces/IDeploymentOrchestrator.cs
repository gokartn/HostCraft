namespace HostCraft.Core.Interfaces;

/// <summary>
/// Orchestrates application deployments including Git cloning, Docker builds, and service deployment.
/// Extracts complex deployment logic from ApplicationsController.
/// </summary>
public interface IDeploymentOrchestrator
{
    /// <summary>
    /// Deploys an application based on the deployment ID.
    /// Handles Git deployments, Docker image deployments, Swarm services, and standalone containers.
    /// </summary>
    /// <param name="deploymentId">The deployment record ID to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeployAsync(int deploymentId, CancellationToken cancellationToken = default);
}
