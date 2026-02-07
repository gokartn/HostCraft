namespace HostCraft.Core.Enums;

/// <summary>
/// Defines the type of Docker server/host.
/// Critical for determining correct network type (bridge vs overlay).
/// </summary>
public enum ServerType
{
    /// <summary>
    /// Regular Docker host running in standalone mode.
    /// Uses bridge networks for container connectivity.
    /// </summary>
    Standalone = 0,
    
    /// <summary>
    /// Docker Swarm manager node.
    /// Can deploy services, manage swarm, uses overlay networks.
    /// </summary>
    SwarmManager = 1,
    
    /// <summary>
    /// Docker Swarm worker node.
    /// Managed by manager, executes tasks, doesn't accept direct deploys.
    /// </summary>
    SwarmWorker = 2
}
