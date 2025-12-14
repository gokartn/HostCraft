using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Core.Enums;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<HealthController> _logger;
    
    public HealthController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<HealthController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardHealthResponse>> GetDashboardHealth()
    {
        try
        {
            var servers = await _context.Servers.ToListAsync();
            var serverMetrics = new List<ServerHealthMetrics>();

            foreach (var server in servers)
            {
                try
                {
                    var metrics = await GetServerMetrics(server.Id);
                    serverMetrics.Add(metrics);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get metrics for server {ServerId}", server.Id);
                    // Add placeholder metrics for failed server
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
            var metrics = await GetServerMetrics(serverId);
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health for server {ServerId}", serverId);
            return StatusCode(500, new { error = $"Failed to retrieve server health: {ex.Message}" });
        }
    }

    private async Task<ServerHealthMetrics> GetServerMetrics(int serverId)
    {
        var server = await _context.Servers.FindAsync(serverId);
        if (server == null)
        {
            throw new Exception($"Server {serverId} not found");
        }

        var metrics = new ServerHealthMetrics
        {
            ServerId = serverId,
            ServerName = server.Name,
            Status = server.Status,
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Get container stats
            var containers = await _dockerService.ListContainersAsync(server, showAll: true);
            var containersList = containers.ToList();
            metrics.ContainerCount = containersList.Count;
            metrics.RunningContainers = containersList.Count(c => c.State == "running");

            // Get Docker stats (CPU, memory, etc.)
            var systemInfo = await _dockerService.GetSystemInfoAsync(server);
            
            // Calculate resource usage (simulated for now - would need real Docker stats API)
            metrics.CpuUsagePercent = CalculateCpuUsage(systemInfo);
            metrics.MemoryUsagePercent = CalculateMemoryUsage(systemInfo);
            metrics.DiskUsagePercent = CalculateDiskUsage(systemInfo);
            
            metrics.TotalMemoryMB = GetTotalMemory(systemInfo);
            metrics.UsedMemoryMB = (long)(metrics.TotalMemoryMB * metrics.MemoryUsagePercent / 100);
            
            metrics.Status = ServerStatus.Online;
        }
        catch (Exception ex)
        {
            metrics.Status = ServerStatus.Error;
            metrics.ErrorMessage = ex.Message;
            _logger.LogWarning(ex, "Failed to get metrics for server {ServerId}", serverId);
        }

        return metrics;
    }

    private double CalculateCpuUsage(object systemInfo)
    {
        // In a real implementation, you'd get actual CPU stats from Docker
        // For now, return a simulated value
        var random = new Random();
        return random.Next(5, 60); // 5-60% CPU usage
    }

    private double CalculateMemoryUsage(object systemInfo)
    {
        // In a real implementation, you'd calculate from Docker stats
        var random = new Random();
        return random.Next(30, 80); // 30-80% memory usage
    }

    private double CalculateDiskUsage(object systemInfo)
    {
        // In a real implementation, you'd get disk stats
        var random = new Random();
        return random.Next(20, 70); // 20-70% disk usage
    }

    private long GetTotalMemory(object systemInfo)
    {
        // In a real implementation, extract from Docker system info
        return 16384; // Default 16GB for simulation
    }
}

public class DashboardHealthResponse
{
    public int TotalServers { get; set; }
    public int OnlineServers { get; set; }
    public int TotalContainers { get; set; }
    public int RunningContainers { get; set; }
    public double AverageCpuUsage { get; set; }
    public double AverageMemoryUsage { get; set; }
    public List<ServerHealthMetrics> ServerMetrics { get; set; } = new();
}

public class ServerHealthMetrics
{
    public int ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public ServerStatus Status { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsagePercent { get; set; }
    public double DiskUsagePercent { get; set; }
    public long TotalMemoryMB { get; set; }
    public long UsedMemoryMB { get; set; }
    public int ContainerCount { get; set; }
    public int RunningContainers { get; set; }
    public DateTime LastChecked { get; set; }
    public string? ErrorMessage { get; set; }
}
