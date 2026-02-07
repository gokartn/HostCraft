namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Aggregated runtime metrics for an application (container or swarm service).
/// </summary>
public record ApplicationRuntimeMetrics(
    string Mode,
    double TotalCpuPercent,
    double TotalMemoryPercent,
    long TotalMemoryUsageBytes,
    long TotalMemoryLimitBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    IReadOnlyList<ApplicationContainerMetric> Containers,
    DateTime Timestamp);

/// <summary>
/// Per-container runtime metrics snapshot.
/// </summary>
public record ApplicationContainerMetric(
    string ContainerId,
    string? Name,
    string? NodeName,
    double CpuPercent,
    long MemoryUsageBytes,
    long MemoryLimitBytes,
    double MemoryPercent,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    DateTime Timestamp);
