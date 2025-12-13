namespace HostCraft.Core.Enums;

/// <summary>
/// Docker network driver types.
/// CRITICAL: Swarm services require overlay, standalone containers use bridge.
/// </summary>
public enum NetworkType
{
    /// <summary>
    /// Bridge network - for standalone Docker containers on a single host.
    /// Default for regular containers.
    /// </summary>
    Bridge = 0,
    
    /// <summary>
    /// Overlay network - for Docker Swarm services spanning multiple nodes.
    /// Required for Swarm mode deployments.
    /// </summary>
    Overlay = 1,
    
    /// <summary>
    /// Host network - container shares host's network stack.
    /// </summary>
    Host = 2,
    
    /// <summary>
    /// None - container has no networking.
    /// </summary>
    None = 3
}
