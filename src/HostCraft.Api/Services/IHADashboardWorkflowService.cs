using HostCraft.Core.Models;

namespace HostCraft.Api.Services;

public interface IHADashboardWorkflowService
{
    Task<ApiActionResult<HAClusterStatusDto>> GetClusterStatusAsync(CancellationToken cancellationToken);
    Task<ApiActionResult<HAHistoricalDataDto>> GetHistoryAsync(int hours, CancellationToken cancellationToken);
    Task<ApiActionResult<HANodeMetricsDto>> GetNodeMetricsAsync(int serverId, CancellationToken cancellationToken);
}
