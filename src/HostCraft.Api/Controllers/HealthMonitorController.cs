using HostCraft.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HealthMonitorController : ControllerBase
{
    private readonly IHealthMonitorService _healthMonitorService;
    private readonly ILogger<HealthMonitorController> _logger;

    public HealthMonitorController(
        IHealthMonitorService healthMonitorService,
        ILogger<HealthMonitorController> logger)
    {
        _healthMonitorService = healthMonitorService;
        _logger = logger;
    }

    /// <summary>
    /// Perform health check on a specific application.
    /// </summary>
    [HttpPost("applications/{applicationId}/check")]
    public async Task<IActionResult> CheckApplicationHealth(int applicationId, CancellationToken cancellationToken)
    {
        var result = await _healthMonitorService.CheckApplicationHealthAsync(applicationId, cancellationToken);
        return Ok(new HealthCheckResponse
        {
            Status = result.Status.ToString(),
            ResponseTimeMs = result.ResponseTimeMs,
            StatusCode = result.StatusCode,
            ErrorMessage = result.ErrorMessage,
            CheckedAt = result.CheckedAt
        });
    }

    /// <summary>
    /// Perform health check on a specific server.
    /// </summary>
    [HttpPost("servers/{serverId}/check")]
    public async Task<IActionResult> CheckServerHealth(int serverId, CancellationToken cancellationToken)
    {
        var result = await _healthMonitorService.CheckServerHealthAsync(serverId, cancellationToken);
        return Ok(new HealthCheckResponse
        {
            Status = result.Status.ToString(),
            ResponseTimeMs = result.ResponseTimeMs,
            StatusCode = result.StatusCode,
            ErrorMessage = result.ErrorMessage,
            CheckedAt = result.CheckedAt
        });
    }

    /// <summary>
    /// Monitor all deployed applications.
    /// </summary>
    [HttpPost("applications/monitor-all")]
    public async Task<IActionResult> MonitorAllApplications(CancellationToken cancellationToken)
    {
        var results = await _healthMonitorService.MonitorAllApplicationsAsync(cancellationToken);
        return Ok(results.Select(r => new HealthCheckResponse
        {
            ApplicationId = r.ApplicationId,
            Status = r.Status.ToString(),
            ResponseTimeMs = r.ResponseTimeMs,
            StatusCode = r.StatusCode,
            ErrorMessage = r.ErrorMessage,
            CheckedAt = r.CheckedAt
        }));
    }

    /// <summary>
    /// Monitor all servers.
    /// </summary>
    [HttpPost("servers/monitor-all")]
    public async Task<IActionResult> MonitorAllServers(CancellationToken cancellationToken)
    {
        var results = await _healthMonitorService.MonitorAllServersAsync(cancellationToken);
        return Ok(results.Select(r => new HealthCheckResponse
        {
            ServerId = r.ServerId,
            Status = r.Status.ToString(),
            ResponseTimeMs = r.ResponseTimeMs,
            StatusCode = r.StatusCode,
            ErrorMessage = r.ErrorMessage,
            CheckedAt = r.CheckedAt
        }));
    }

    /// <summary>
    /// Manually trigger recovery for an unhealthy application.
    /// </summary>
    [HttpPost("applications/{applicationId}/recover")]
    public async Task<IActionResult> TriggerRecovery(int applicationId, CancellationToken cancellationToken)
    {
        var success = await _healthMonitorService.AttemptRecoveryAsync(applicationId, cancellationToken);
        return Ok(new { success, message = success ? "Recovery initiated" : "Recovery failed" });
    }

    /// <summary>
    /// Get health check history for an application.
    /// </summary>
    [HttpGet("applications/{applicationId}/history")]
    public async Task<IActionResult> GetHealthHistory(int applicationId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var history = await _healthMonitorService.GetHealthHistoryAsync(applicationId, limit, cancellationToken);
        return Ok(history.Select(r => new HealthCheckResponse
        {
            Id = r.Id,
            ApplicationId = r.ApplicationId,
            Status = r.Status.ToString(),
            ResponseTimeMs = r.ResponseTimeMs,
            StatusCode = r.StatusCode,
            ErrorMessage = r.ErrorMessage,
            CheckedAt = r.CheckedAt
        }));
    }

    /// <summary>
    /// Get uptime percentage for an application.
    /// </summary>
    [HttpGet("applications/{applicationId}/uptime")]
    public async Task<IActionResult> GetUptime(int applicationId, [FromQuery] int hours = 24, CancellationToken cancellationToken = default)
    {
        var uptime = await _healthMonitorService.GetUptimePercentageAsync(applicationId, TimeSpan.FromHours(hours), cancellationToken);
        return Ok(new { applicationId, uptimePercentage = uptime, periodHours = hours });
    }
}

public class HealthCheckResponse
{
    public int? Id { get; set; }
    public int? ApplicationId { get; set; }
    public int? ServerId { get; set; }
    public required string Status { get; set; }
    public int ResponseTimeMs { get; set; }
    public string? StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CheckedAt { get; set; }
}
