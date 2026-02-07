using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Enums;
using Microsoft.AspNetCore.Authorization;
using HostCraft.Api.Models.Health;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class HealthController : ControllerBase
{
    private readonly IServerRepository _serverRepository;
    private readonly IServerMetricsService _serverMetricsService;
    private readonly ILogger<HealthController> _logger;
    
    public HealthController(
        IServerRepository serverRepository,
        IServerMetricsService serverMetricsService,
        ILogger<HealthController> logger)
    {
        _serverRepository = serverRepository;
        _serverMetricsService = serverMetricsService;
        _logger = logger;
    }
    
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardHealthResponse>> GetDashboardHealth()
    {
        try
        {
            var servers = (await _serverRepository.GetAllAsync()).ToList();
            var serverMetrics = new List<ServerHealthMetrics>();

            foreach (var server in servers)
            {
                try
                {
                    // Use a timeout for metrics gathering to prevent hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var result = await _serverMetricsService.GetServerMetricsAsync(server.Id, cts.Token);
                    
                    serverMetrics.Add(new ServerHealthMetrics
                    {
                        ServerId = result.ServerId,
                        ServerName = result.ServerName,
                        Status = result.Status,
                        CpuUsagePercent = result.CpuUsagePercent,
                        MemoryUsagePercent = result.MemoryUsagePercent,
                        DiskUsagePercent = result.DiskUsagePercent,
                        TotalMemoryMB = result.TotalMemoryMB,
                        UsedMemoryMB = result.UsedMemoryMB,
                        ContainerCount = result.ContainerCount,
                        RunningContainers = result.RunningContainers,
                        LastChecked = result.LastChecked,
                        ErrorMessage = result.ErrorMessage
                    });
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Timeout getting metrics for server {ServerId} - {ServerName}", server.Id, server.Name);
                    serverMetrics.Add(new ServerHealthMetrics
                    {
                        ServerId = server.Id,
                        ServerName = server.Name,
                        Status = ServerStatus.Error,
                        CpuUsagePercent = 0,
                        MemoryUsagePercent = 0,
                        DiskUsagePercent = 0,
                        ContainerCount = 0,
                        RunningContainers = 0,
                        ErrorMessage = "Timeout connecting to server"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get metrics for server {ServerId}", server.Id);
                    serverMetrics.Add(new ServerHealthMetrics
                    {
                        ServerId = server.Id,
                        ServerName = server.Name,
                        Status = ServerStatus.Error,
                        CpuUsagePercent = 0,
                        MemoryUsagePercent = 0,
                        DiskUsagePercent = 0,
                        ContainerCount = 0,
                        RunningContainers = 0,
                        ErrorMessage = ex.Message
                    });
                }
            }

            var response = new DashboardHealthResponse
            {
                TotalServers = servers.Count,
                OnlineServers = serverMetrics.Count(m => m.Status == ServerStatus.Online),
                TotalContainers = serverMetrics.Sum(m => m.ContainerCount),
                RunningContainers = serverMetrics.Sum(m => m.RunningContainers),
                AverageCpuUsage = serverMetrics.Any() ? serverMetrics.Average(m => m.CpuUsagePercent) : 0,
                AverageMemoryUsage = serverMetrics.Any() ? serverMetrics.Average(m => m.MemoryUsagePercent) : 0,
                ServerMetrics = serverMetrics
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get dashboard health");
            return StatusCode(500, new { error = "Failed to retrieve dashboard health" });
        }
    }

    [HttpGet("server/{serverId}")]
    public async Task<ActionResult<ServerHealthMetrics>> GetServerHealth(int serverId)
    {
        try
        {
            var result = await _serverMetricsService.GetServerMetricsAsync(serverId);
            
            var metrics = new ServerHealthMetrics
            {
                ServerId = result.ServerId,
                ServerName = result.ServerName,
                Status = result.Status,
                CpuUsagePercent = result.CpuUsagePercent,
                MemoryUsagePercent = result.MemoryUsagePercent,
                DiskUsagePercent = result.DiskUsagePercent,
                TotalMemoryMB = result.TotalMemoryMB,
                UsedMemoryMB = result.UsedMemoryMB,
                ContainerCount = result.ContainerCount,
                RunningContainers = result.RunningContainers,
                LastChecked = result.LastChecked,
                ErrorMessage = result.ErrorMessage
            };
            
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health for server {ServerId}", serverId);
            return StatusCode(500, new { error = $"Failed to retrieve server health: {ex.Message}" });
        }
    }
}
