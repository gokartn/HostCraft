using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HostCraft.Api.Services;
using HostCraft.Core.Models;
using Microsoft.AspNetCore.Http;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/ha")]
[Authorize]
public class HADashboardController : ControllerBase
{
    private readonly IHADashboardWorkflowService _dashboardWorkflow;

    public HADashboardController(IHADashboardWorkflowService dashboardWorkflow)
    {
        _dashboardWorkflow = dashboardWorkflow;
    }

    /// <summary>
    /// Get comprehensive cluster status for HA dashboard
    /// </summary>
    [HttpGet("cluster-status")]
    public async Task<ActionResult<HAClusterStatusDto>> GetClusterStatus()
    {
        var result = await _dashboardWorkflow.GetClusterStatusAsync(HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    /// <summary>
    /// Get historical metrics for trend charts (last 24 hours)
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<HAHistoricalDataDto>> GetHistory([FromQuery] int hours = 24)
    {
        var result = await _dashboardWorkflow.GetHistoryAsync(hours, HttpContext.RequestAborted);
        return ToActionResult(result);
    }
    /// <summary>
    /// Get node metrics for a specific server
    /// </summary>
    [HttpGet("nodes/{serverId}/metrics")]
    public async Task<ActionResult<HANodeMetricsDto>> GetNodeMetrics(int serverId)
    {
        var result = await _dashboardWorkflow.GetNodeMetricsAsync(serverId, HttpContext.RequestAborted);
        return ToActionResult(result);
    }

    private ActionResult ToActionResult(ApiActionResult result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }

    private ActionResult<T> ToActionResult<T>(ApiActionResult<T> result)
    {
        if (result.Success)
        {
            if (result.StatusCode == StatusCodes.Status204NoContent)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return StatusCode(result.StatusCode, result.Data);
        }

        return StatusCode(result.StatusCode, new { error = result.Error ?? "Request failed" });
    }
}
