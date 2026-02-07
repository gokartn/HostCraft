namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for gathering server system metrics (CPU, memory, disk).
/// </summary>
public interface IServerMetricsService
{
    /// <summary>
    /// Get comprehensive health metrics for a specific server.
    /// </summary>
    Task<ServerHealthMetricsResult> GetServerMetricsAsync(int serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get resource usage (CPU, memory, disk) for a specific server.
    /// </summary>
    Task<ResourceUsageResult> GetServerResourceUsageAsync(Core.Entities.Server server, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of server health metrics gathering.
/// </summary>
public record ServerHealthMetricsResult(
    int ServerId,
    string ServerName,
    Core.Enums.ServerStatus Status,
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double DiskUsagePercent,
    long TotalMemoryMB,
    long UsedMemoryMB,
    int ContainerCount,
    int RunningContainers,
    DateTime LastChecked,
    string? ErrorMessage = null);

/// <summary>
/// Result of resource usage gathering.
/// </summary>
public record ResourceUsageResult(
    double CpuUsage,
    double MemoryUsage,
    double DiskUsage,
    long TotalMemoryMB,
    long UsedMemoryMB);
