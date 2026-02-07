using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing Docker Swarm high availability.
/// </summary>
public interface ISwarmHAService
{
    /// <summary>
    /// Initializes a Swarm cluster with proper quorum (3, 5, or 7 managers).
    /// </summary>
    Task<bool> InitializeHAClusterAsync(Server primaryManager, int managerCount = 3, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Adds a manager node to achieve quorum.
    /// </summary>
    Task<bool> PromoteWorkerToManagerAsync(int serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes a manager node from the cluster.
    /// </summary>
    Task<bool> DemoteManagerToWorkerAsync(int serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks Swarm cluster health and quorum status.
    /// </summary>
    Task<SwarmClusterHealth> GetClusterHealthAsync(int managerServerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Performs automatic leader election if current leader fails.
    /// </summary>
    Task<bool> TriggerLeaderElectionAsync(int managerServerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Drains a node (moves all tasks to other nodes) for maintenance.
    /// </summary>
    Task<bool> DrainNodeAsync(int serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Activates a drained node to accept tasks again.
    /// </summary>
    Task<bool> ActivateNodeAsync(int serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Rebalances services across nodes for optimal distribution.
    /// </summary>
    Task<bool> RebalanceServicesAsync(int managerServerId, CancellationToken cancellationToken = default);
}

public record SwarmClusterHealth(
    bool HasQuorum,
    int ManagerCount,
    int WorkerCount,
    int HealthyManagers,
    int HealthyWorkers,
    string? LeaderNodeId,
    IEnumerable<string> UnhealthyNodes);
