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
                    // Use a timeout for metrics gathering to prevent hanging
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var metrics = await GetServerMetrics(server.Id, cts.Token);
                    serverMetrics.Add(metrics);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Timeout getting metrics for server {ServerId} - {ServerName}", server.Id, server.Name);
                    // Add placeholder metrics for timeout
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

    private async Task<ServerHealthMetrics> GetServerMetrics(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _context.Servers.FindAsync(new object[] { serverId }, cancellationToken);
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
            var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
            
            // Get container counts
            var containers = await _dockerService.ListContainersAsync(server, showAll: true, cancellationToken);
            var containerList = containers.ToList();
            metrics.ContainerCount = containerList.Count;
            metrics.RunningContainers = containerList.Count(c => c.State == "running");
            
            // Get CPU and Memory usage from the server
            try
            {
                var cpuMemory = await GetServerResourceUsageAsync(server, cancellationToken);
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
        GetServerResourceUsageAsync(HostCraft.Core.Entities.Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if this is localhost
            bool isLocalhost = server.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
                               server.Host == "127.0.0.1" || 
                               server.Host == "::1";

            if (isLocalhost)
            {
                // For localhost, read directly from host /proc filesystem (if running in container)
                // or use system APIs
                return await GetLocalHostMetricsAsync(cancellationToken);
            }
            else
            {
                // For remote servers, use SSH
                return await GetRemoteHostMetricsAsync(server, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resource usage for server {ServerId}", server.Id);
            return (0, 0, 0, 0, 0);
        }
    }

    private async Task<(double CpuUsage, double MemoryUsage, double DiskUsage, long TotalMemoryMB, long UsedMemoryMB)>
        GetLocalHostMetricsAsync(CancellationToken cancellationToken)
    {
        // Check if we're running in a container with host /proc mounted
        bool hasHostProc = Directory.Exists("/host/proc");
        string procPath = hasHostProc ? "/host/proc" : "/proc";
        
        _logger.LogInformation("Reading metrics from {ProcPath} (container: {IsContainer})", procPath, hasHostProc);

        double cpuUsage = 0;
        long totalMemory = 0;
        long usedMemory = 0;
        double memoryUsage = 0;
        double diskUsage = 0;

        // Read CPU usage from /proc/stat
        try
        {
            var statFile = Path.Combine(procPath, "stat");
            if (System.IO.File.Exists(statFile))
            {
                var lines = await System.IO.File.ReadAllLinesAsync(statFile, cancellationToken);
                var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
                if (cpuLine != null)
                {
                    var values = cpuLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
                    if (values.Length >= 4)
                    {
                        var idle = values[3];
                        var total = values.Sum();
                        cpuUsage = total > 0 ? (double)(total - idle) / total * 100.0 : 0;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read CPU from {Path}", procPath);
        }

        // Read memory from /proc/meminfo
        try
        {
            var meminfoFile = Path.Combine(procPath, "meminfo");
            if (System.IO.File.Exists(meminfoFile))
            {
                var lines = await System.IO.File.ReadAllLinesAsync(meminfoFile, cancellationToken);
                var memTotal = lines.FirstOrDefault(l => l.StartsWith("MemTotal:"));
                var memAvailable = lines.FirstOrDefault(l => l.StartsWith("MemAvailable:"));
                
                if (memTotal != null && memAvailable != null)
                {
                    var totalKB = long.Parse(memTotal.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);
                    var availableKB = long.Parse(memAvailable.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)[1]);
                    
                    totalMemory = totalKB / 1024; // Convert to MB
                    usedMemory = (totalKB - availableKB) / 1024;
                    memoryUsage = totalMemory > 0 ? (double)usedMemory / totalMemory * 100.0 : 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read memory from {Path}", procPath);
        }

        // For disk usage, read from actual host root mount
        try
        {
            var hostRoot = hasHostProc ? "/host/proc/../.." : "/";
            var driveInfo = new System.IO.DriveInfo(hostRoot);
            if (driveInfo.IsReady)
            {
                diskUsage = (1.0 - (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize) * 100.0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read disk usage");
        }

        return (cpuUsage, memoryUsage, diskUsage, totalMemory, usedMemory);
    }

    private async Task<(double CpuUsage, double MemoryUsage, double DiskUsage, long TotalMemoryMB, long UsedMemoryMB)>
        GetRemoteHostMetricsAsync(HostCraft.Core.Entities.Server server, CancellationToken cancellationToken)
    {
        // Use SSH for remote servers
        var cpuResult = await _sshService.ExecuteCommandAsync(server, "top -bn1 | grep 'Cpu(s)' | head -1", cancellationToken);
        var memResult = await _sshService.ExecuteCommandAsync(server, "free -m | grep 'Mem:'", cancellationToken);
        var diskResult = await _sshService.ExecuteCommandAsync(server, "df -h / | tail -1", cancellationToken);

        double cpuUsage = 0;
        if (cpuResult.ExitCode == 0)
        {
            var match = System.Text.RegularExpressions.Regex.Match(cpuResult.Output, @"(\d+\.?\d*)\s+id");
            if (match.Success && double.TryParse(match.Groups[1].Value, out var idlePercent))
            {
                cpuUsage = 100.0 - idlePercent;
            }
        }
        
        long totalMemory = 0;
        long usedMemory = 0;
        double memoryUsage = 0;
        
        if (memResult.ExitCode == 0)
        {
            var parts = memResult.Output.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                long.TryParse(parts[1], out totalMemory);
                long.TryParse(parts[2], out usedMemory);
                if (totalMemory > 0)
                {
                    memoryUsage = (double)usedMemory / totalMemory * 100.0;
                }
            }
        }
        
        double diskUsage = 0;
        if (diskResult.ExitCode == 0)
        {
            var parts = diskResult.Output.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                var percentStr = parts[4].TrimEnd('%');
                double.TryParse(percentStr, out diskUsage);
            }
        }
        
        return (cpuUsage, memoryUsage, diskUsage, totalMemory, usedMemory);
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
