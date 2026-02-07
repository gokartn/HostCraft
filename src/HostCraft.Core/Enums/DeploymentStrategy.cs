namespace HostCraft.Core.Enums;

/// <summary>
/// Deployment strategy for application updates.
/// Determines how new versions replace old versions in production.
/// </summary>
public enum DeploymentStrategy
{
    /// <summary>
    /// Rolling update: Gradually replace old replicas with new ones.
    /// - Start-first order for zero downtime (HA/DR default)
    /// - Health checks ensure new replicas are healthy before stopping old ones
    /// - Automatic rollback on failure
    /// - Best for: stateless applications, most production workloads
    /// </summary>
    Rolling = 0,

    /// <summary>
    /// Blue/Green deployment: Run new version alongside old, then switch traffic.
    /// - Deploy new service with different name (e.g., app-green)
    /// - Health check new service
    /// - Switch Traefik routing to new service
    /// - Keep old service running for instant rollback
    /// - Best for: critical applications requiring instant rollback capability
    /// </summary>
    BlueGreen = 1,

    /// <summary>
    /// Recreate: Stop all old replicas, then start new ones.
    /// - Causes downtime during deployment
    /// - Simplest strategy but not suitable for HA/DR
    /// - Only use for non-production or maintenance windows
    /// </summary>
    Recreate = 2
}
