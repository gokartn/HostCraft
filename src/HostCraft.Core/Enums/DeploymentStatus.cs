namespace HostCraft.Core.Enums;

/// <summary>
/// Represents the status of a deployment operation.
/// </summary>
public enum DeploymentStatus
{
    /// <summary>
    /// Deployment is queued and waiting to start.
    /// </summary>
    Queued = 0,
    
    /// <summary>
    /// Deployment is currently running.
    /// </summary>
    Running = 1,
    
    /// <summary>
    /// Deployment completed successfully.
    /// </summary>
    Success = 2,
    
    /// <summary>
    /// Deployment failed with errors.
    /// </summary>
    Failed = 3,
    
    /// <summary>
    /// Deployment was cancelled by user.
    /// </summary>
    Cancelled = 4,
    
    /// <summary>
    /// Deployment is being rolled back.
    /// </summary>
    RollingBack = 5
}
