namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing Docker Swarm operations (initialization, joining, promotion).
/// </summary>
public interface ISwarmManagementService
{
    /// <summary>
    /// Initialize a new Docker Swarm on the specified server.
    /// </summary>
    Task InitializeSwarmAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get swarm join tokens (worker and manager) from a manager node.
    /// </summary>
    Task<(string WorkerToken, string ManagerToken)> GetJoinTokensAsync(int managerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Join a standalone server to an existing swarm as a manager node.
    /// </summary>
    Task<SwarmJoinResult> JoinAsManagerAsync(int existingManagerId, int serverIdToJoin, CancellationToken cancellationToken = default);

    /// <summary>
    /// Promote a worker node to manager status.
    /// </summary>
    Task<SwarmPromotionResult> PromoteToManagerAsync(int workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh swarm status for a server by querying Docker and updating database.
    /// </summary>
    Task RefreshSwarmStatusAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh swarm status with recovery attempts (auto-rejoin workers) and details.
    /// </summary>
    Task<SwarmRefreshResult> RefreshSwarmStatusWithRecoveryAsync(int serverId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of joining a server as a manager.
/// </summary>
public record SwarmJoinResult(
    bool Success,
    string Message,
    string? SwarmId = null,
    string? ManagerAddress = null,
    int? ManagerCount = null,
    string? QuorumWarning = null,
    string? ErrorDetails = null);

/// <summary>
/// Result of promoting a worker to manager.
/// </summary>
public record SwarmPromotionResult(
    bool Success,
    string Message,
    int? ManagerCount = null,
    string? QuorumWarning = null,
    string? ErrorDetails = null);

/// <summary>
/// Result of refreshing swarm status with optional recovery details.
/// </summary>
public record SwarmRefreshResult(
    bool Success,
    string Message,
    bool NotFound = false,
    bool SwarmActive = false,
    string? Hostname = null,
    string? NodeId = null,
    string? NodeAddress = null,
    bool Rejoined = false,
    string? RejoinError = null,
    Core.Enums.ServerType? PreviousType = null,
    Core.Enums.ServerType? UpdatedType = null,
    string? ErrorDetails = null);
