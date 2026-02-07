using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.AspNetCore.Http;

namespace HostCraft.Api.Services;

public class HADashboardWorkflowService : IHADashboardWorkflowService
{
    private readonly IDashboardService _dashboardService;

    public HADashboardWorkflowService(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    public async Task<ApiActionResult<HAClusterStatusDto>> GetClusterStatusAsync(CancellationToken cancellationToken)
    {
        var result = await _dashboardService.GetClusterStatusAsync(cancellationToken);
        return ApiActionResult<HAClusterStatusDto>.Ok(result);
    }

    public async Task<ApiActionResult<HAHistoricalDataDto>> GetHistoryAsync(int hours, CancellationToken cancellationToken)
    {
        var history = await _dashboardService.GetHistoryAsync(hours, cancellationToken);
        return ApiActionResult<HAHistoricalDataDto>.Ok(history);
    }

    public async Task<ApiActionResult<HANodeMetricsDto>> GetNodeMetricsAsync(int serverId, CancellationToken cancellationToken)
    {
        var metrics = await _dashboardService.GetNodeMetricsAsync(serverId, cancellationToken);
        if (metrics == null)
        {
            return ApiActionResult<HANodeMetricsDto>.Fail(StatusCodes.Status404NotFound, "Could not collect metrics for this server");
        }

        return ApiActionResult<HANodeMetricsDto>.Ok(metrics);
    }
}
