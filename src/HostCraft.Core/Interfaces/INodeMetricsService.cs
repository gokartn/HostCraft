using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for collecting and caching node metrics (CPU, Memory, Disk) from Docker hosts
/// </summary>
public interface INodeMetricsService
{
    /// <summary>
    /// Get metrics for a specific node with 10-second cache
    /// </summary>
    /// <param name="serverId">Server ID from database</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Node metrics or null if collection fails</returns>
    Task<HANodeMetricsDto?> GetNodeMetricsAsync(int serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get metrics for all servers in parallel
    /// </summary>
    /// <param name="serverIds">List of server IDs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of server ID to metrics</returns>
    Task<Dictionary<int, HANodeMetricsDto>> GetAllNodeMetricsAsync(IEnumerable<int> serverIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Clear cached metrics for a specific node
    /// </summary>
    /// <param name="serverId">Server ID to clear</param>
    void ClearCache(int serverId);
    
    /// <summary>
    /// Clear all cached metrics
    /// </summary>
    void ClearAllCache();
}
