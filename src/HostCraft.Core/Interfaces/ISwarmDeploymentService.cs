using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for deploying and managing Docker Swarm services.
/// </summary>
public interface ISwarmDeploymentService
{
    /// <summary>
    /// Deploy an application as a Docker Swarm service.
    /// Creates a new service or updates existing one.
    /// </summary>
    Task<ServiceDeploymentResult> DeployToSwarmAsync(Application application, string imageTag, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update an existing swarm service (rolling update).
    /// </summary>
    Task<bool> UpdateSwarmServiceAsync(Application application, string imageTag, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Scale a swarm service to the specified number of replicas.
    /// </summary>
    Task<bool> ScaleServiceAsync(Application application, int replicas, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rollback a swarm service to the previous version.
    /// </summary>
    Task<bool> RollbackServiceAsync(Application application, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Remove a swarm service.
    /// </summary>
    Task<bool> RemoveServiceAsync(Application application, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the health status of a swarm service.
    /// </summary>
    Task<ServiceHealth> GetServiceHealthAsync(Application application, CancellationToken cancellationToken = default);
}

public record ServiceDeploymentResult(
    bool Success,
    string? ServiceId,
    string Message,
    string? Error = null);

public record ServiceHealth(
    int DesiredReplicas,
    int RunningReplicas,
    int FailedTasks,
    string Status); // running, degraded, unhealthy, down
