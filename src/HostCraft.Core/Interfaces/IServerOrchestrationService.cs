using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for orchestrating complex server operations including background validation,
/// swarm joining, and proxy deployment
/// </summary>
public interface IServerOrchestrationService
{
    /// <summary>
    /// Validates a server connection and optionally joins it to a swarm and deploys proxy
    /// </summary>
    Task ValidateAndConfigureServerAsync(int serverId);
    
    /// <summary>
    /// Re-validates a server after updates
    /// </summary>
    Task RevalidateServerAsync(int serverId);
    
    /// <summary>
    /// Attempts to join a worker server to an existing swarm
    /// </summary>
    Task<bool> JoinWorkerToSwarmAsync(Server worker, Server manager);
    
    /// <summary>
    /// Removes stale swarm nodes for a given server
    /// </summary>
    Task RemoveStaleSwarmNodesAsync(Server server);
}
