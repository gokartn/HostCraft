using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for gathering server system metrics (CPU, memory, disk).
/// Extracted from HealthController to follow single responsibility principle.
/// </summary>
public class ServerMetricsService : IServerMetricsService
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ISshService _sshService;
    private readonly ILogger<ServerMetricsService> _logger;

    public ServerMetricsService(
        HostCraftDbContext context,
        IDockerService dockerService,
        ISshService sshService,
        ILogger<ServerMetricsService> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _sshService = sshService;
        _logger = logger;
    }

    public async Task<ServerHealthMetricsResult> GetServerMetricsAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId, cancellationToken);

        if (server == null)
        {
            throw new Exception($"Server {serverId} not found");
        }

        var lastChecked = DateTime.UtcNow;

        try
        {
            // Skip detailed metrics if server is not online
            if (server.Status != ServerStatus.Online)
            {
                return new ServerHealthMetricsResult(
                    serverId,
                    server.Name,
                    server.Status,
                    0, 0, 0, 0, 0, 0, 0,
                    lastChecked,
                    "Server is not online");
            }

            // Get actual system metrics
            var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);

            // Get container counts
            var containers = await _dockerService.ListContainersAsync(server, showAll: true, cancellationToken);
            var containerList = containers.ToList();
            var containerCount = containerList.Count;
            var runningContainers = containerList.Count(c => c.State == "running");

            // Get CPU and Memory usage from the server
            ResourceUsageResult resourceUsage;
            try
            {
                resourceUsage = await GetServerResourceUsageAsync(server, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get resource usage for server {ServerId}, using defaults", serverId);
                resourceUsage = new ResourceUsageResult(0, 0, 0, 0, 0);
            }

            return new ServerHealthMetricsResult(
                serverId,
                server.Name,
                ServerStatus.Online,
                resourceUsage.CpuUsage,
                resourceUsage.MemoryUsage,
                resourceUsage.DiskUsage,
                resourceUsage.TotalMemoryMB,
                resourceUsage.UsedMemoryMB,
                containerCount,
                runningContainers,
                lastChecked);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metrics for server {ServerId}", serverId);
            return new ServerHealthMetricsResult(
                serverId,
                server.Name,
                ServerStatus.Error,
                0, 0, 0, 0, 0, 0, 0,
                lastChecked,
                ex.Message);
        }
    }

    public async Task<ResourceUsageResult> GetServerResourceUsageAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if this is localhost
            bool isLocalhost = server.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                               server.Host == "127.0.0.1" ||
                               server.Host == "::1";

            if (isLocalhost)
            {
                return await GetLocalHostMetricsAsync(cancellationToken);
            }
            else
            {
                return await GetRemoteHostMetricsAsync(server, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get resource usage for server {ServerId}", server.Id);
            return new ResourceUsageResult(0, 0, 0, 0, 0);
        }
    }

    private async Task<ResourceUsageResult> GetLocalHostMetricsAsync(CancellationToken cancellationToken)
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
            if (File.Exists(statFile))
            {
                var lines = await File.ReadAllLinesAsync(statFile, cancellationToken);
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
            if (File.Exists(meminfoFile))
            {
                var lines = await File.ReadAllLinesAsync(meminfoFile, cancellationToken);
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
            var driveInfo = new DriveInfo(hostRoot);
            if (driveInfo.IsReady)
            {
                diskUsage = (1.0 - (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize) * 100.0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read disk usage");
        }

        return new ResourceUsageResult(cpuUsage, memoryUsage, diskUsage, totalMemory, usedMemory);
    }

    private async Task<ResourceUsageResult> GetRemoteHostMetricsAsync(Server server, CancellationToken cancellationToken)
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

        return new ResourceUsageResult(cpuUsage, memoryUsage, diskUsage, totalMemory, usedMemory);
    }
}
