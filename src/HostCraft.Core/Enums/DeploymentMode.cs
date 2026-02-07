namespace HostCraft.Core.Enums;

/// <summary>
/// Deployment mode for applications on Docker hosts.
/// </summary>
public enum DeploymentMode
{
    /// <summary>
    /// Deploy as a standalone Docker container on a specific node.
    /// Simple, direct container deployment without orchestration.
    /// </summary>
    Container = 0,
    
    /// <summary>
    /// Deploy as a Docker Swarm service across the cluster.
    /// Provides HA, scaling, rolling updates, and load balancing.
    /// Only available on Swarm Manager nodes.
    /// </summary>
    Service = 1
}
