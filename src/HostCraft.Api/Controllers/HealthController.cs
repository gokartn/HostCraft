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
    private readonly ISshService _sshService;
    private readonly ILogger<HealthController> _logger;
    
    public HealthController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ISshService sshService,
        ILogger<HealthController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _sshService = sshService;
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
            // Skip detailed metrics if server is not online
            if (server.Status != ServerStatus.Online)
            {
                metrics.Status = server.Status;
                metrics.ContainerCount = 0;
                metrics.RunningContainers = 0;
                return metrics;
            }
            
            // Get actual system metrics
            var systemInfo = await _dockerService.GetSystemInfoAsync(server);
            
            // Get container counts
            var containers = await _dockerService.ListContainersAsync(server, showAll: true);
            var containerList = containers.ToList();
            metrics.ContainerCount = containerList.Count;
            metrics.RunningContainers = containerList.Count(c => c.State == "running");
            
            // Get CPU and Memory usage from the server
            try
            {
                var cpuMemory = await GetServerResourceUsageAsync(server);
                metrics.CpuUsagePercent = cpuMemory.CpuUsage;
                metrics.MemoryUsagePercent = cpuMemory.MemoryUsage;
                metrics.TotalMemoryMB = cpuMemory.TotalMemoryMB;
                metrics.UsedMemoryMB = cpuMemory.UsedMemoryMB;
                metrics.DiskUsagePercent = cpuMemory.DiskUsage;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get resource usage for server {ServerId}, using defaults", serverId);
                metrics.CpuUsagePercent = 0;
                metrics.MemoryUsagePercent = 0;
                metrics.DiskUsagePercent = 0;
                metrics.TotalMemoryMB = 0;
                metrics.UsedMemoryMB = 0;
            }
            
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

    private async Task<(double CpuUsage, double MemoryUsage, double DiskUsage, long TotalMemoryMB, long UsedMemoryMB)> 
        GetServerResourceUsageAsync(HostCraft.Core.Entities.Server server)
    {
        try
        {
            // Get CPU usage (1 second average)
            var cpuCommand = "top -bn1 | grep 'Cpu(s)' | sed 's/.*, *\\([0-9.]*\\)%* id.*/\\1/' | awk '{print 100 - $1}'";
            var cpuResult = await _sshService.ExecuteCommandAsync(server, cpuCommand);
            var cpuUsage = cpuResult.ExitCode == 0 && double.TryParse(cpuResult.Output.Trim(), out var cpu) ? cpu : 0;
            
            // Get memory usage
            var memCommand = "free -m | awk 'NR==2{printf \"%s %s\", $2,$3}'";
            var memResult = await _sshService.ExecuteCommandAsync(server, memCommand);
            var memParts = memResult.Output.Trim().Split(' ');
            var totalMemory = memParts.Length > 0 && long.TryParse(memParts[0], out var total) ? total : 0;
            var usedMemory = memParts.Length > 1 && long.TryParse(memParts[1], out var used) ? used : 0;
            var memoryUsage = totalMemory > 0 ? (double)usedMemory / totalMemory * 100 : 0;
            
            // Get disk usage
            var diskCommand = "df -h / | awk 'NR==2{print $5}' | sed 's/%//'";
            var diskResult = await _sshService.ExecuteCommandAsync(server, diskCommand);
            var diskUsage = diskResult.ExitCode == 0 && double.TryParse(diskResult.Output.Trim(), out var disk) ? disk : 0;
            
            return (cpuUsage, memoryUsage, diskUsage, totalMemory, usedMemory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resource usage for server {ServerId}", server.Id);
            return (0, 0, 0, 0, 0);
        }
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
