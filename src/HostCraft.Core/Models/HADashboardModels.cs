namespace HostCraft.Core.Models;

/// <summary>
/// Complete cluster status for HA dashboard
/// </summary>
public record HAClusterStatusDto(
    string? ClusterId,
    int TotalManagers,
    int OnlineManagers,
    int TotalWorkers,
    int OnlineWorkers,
    bool HasQuorum,
    string? LeaderNodeId,
    string? LeaderHostname,
    string QuorumStatus, // "healthy" | "degraded" | "critical"
    List<HANodeDto> Nodes,
    List<HARegionDto> Regions,
    List<HAServiceStatusDto> Services,
    List<string> Recommendations,
    DateTime Timestamp);

/// <summary>
/// Individual node information for HA dashboard
/// </summary>
public record HANodeDto(
    string NodeId,
    string Hostname,
    string Role, // "manager" | "worker"
    string State, // "ready" | "down" | "unknown"
    string Availability, // "active" | "drain" | "pause"
    bool IsLeader,
    string? ServerName,
    int? ServerId,
    string? Region,
    string Address,
    long NanoCPUs,
    long MemoryBytes,
    string EngineVersion,
    int RunningServices,
    DateTime? LastSeen,
    HANodeMetricsDto? Metrics = null);

/// <summary>
/// Regional distribution of nodes
/// </summary>
public record HARegionDto(
    string Name,
    int ManagerCount,
    int WorkerCount,
    int OnlineManagers,
    int OnlineWorkers,
    List<HANodeDto> Nodes);

/// <summary>
/// Service health and replica distribution
/// </summary>
public record HAServiceStatusDto(
    string ServiceId,
    string Name,
    string Image,
    int DesiredReplicas,
    int RunningReplicas,
    string Status, // "healthy" | "degraded" | "critical"
    bool IsHAReady,
    Dictionary<string, int> ReplicasByNode,
    DateTime UpdatedAt);

/// <summary>
/// Historical metrics for trend charts
/// </summary>
public record HAHistoricalDataDto(
    List<HAMetricPoint> ManagerAvailability,
    List<HAMetricPoint> WorkerAvailability,
    List<HAMetricPoint> QuorumStatus,
    List<HAMetricPoint> TotalNodes,
    List<HAMetricPoint> ServiceHealth,
    DateTime StartTime,
    DateTime EndTime,
    int IntervalMinutes);

/// <summary>
/// Single time-series data point
/// </summary>
public record HAMetricPoint(
    DateTime Timestamp,
    double Value,
    string? Label);

/// <summary>
/// Node resource metrics (CPU, Memory, Disk)
/// </summary>
public record HANodeMetricsDto(
    string NodeId,
    int ServerId,
    double CpuPercent,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    long DiskUsedBytes,
    long DiskTotalBytes,
    DateTime CollectedAt)
{
    public double MemoryPercent => MemoryTotalBytes > 0 ? (MemoryUsedBytes / (double)MemoryTotalBytes * 100) : 0;
    public double DiskPercent => DiskTotalBytes > 0 ? (DiskUsedBytes / (double)DiskTotalBytes * 100) : 0;
}
