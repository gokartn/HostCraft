namespace HostCraft.Api.Models.Applications;

public record ApplicationMetricsDto(
    string Mode,
    double TotalCpuPercent,
    double TotalMemoryPercent,
    long TotalMemoryUsageBytes,
    long TotalMemoryLimitBytes,
    long NetworkRxBytes,
    long NetworkTxBytes,
    long BlockReadBytes,
    long BlockWriteBytes,
    DateTime Timestamp,
    IReadOnlyList<ApplicationContainerMetricsDto> Containers);

public record ApplicationContainerMetricsDto(
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
