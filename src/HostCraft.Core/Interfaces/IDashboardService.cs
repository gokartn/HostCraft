using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

public interface IDashboardService
{
    Task<HAClusterStatusDto> GetClusterStatusAsync(CancellationToken cancellationToken = default);
    Task<HAHistoricalDataDto> GetHistoryAsync(int hours, CancellationToken cancellationToken = default);
    Task<HANodeMetricsDto?> GetNodeMetricsAsync(int serverId, CancellationToken cancellationToken = default);
}
