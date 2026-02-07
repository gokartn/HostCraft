using System.Collections.Concurrent;
using System.Diagnostics;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for collecting node metrics (CPU, Memory, Disk) with lightweight caching
/// </summary>
public class NodeMetricsService : INodeMetricsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDockerService _dockerService;
    private readonly ISshService _sshService;
    private readonly ILogger<NodeMetricsService> _logger;
    
    // Cache with TTL
    private readonly ConcurrentDictionary<int, CachedMetrics> _metricsCache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);
    
    private record CachedMetrics(HANodeMetricsDto Metrics, DateTime ExpiresAt);
    
    public NodeMetricsService(
        IServiceScopeFactory scopeFactory,
        IDockerService dockerService,
        ISshService sshService,
        ILogger<NodeMetricsService> logger)
    {
        _scopeFactory = scopeFactory;
        _dockerService = dockerService;
        _sshService = sshService;
        _logger = logger;
    }
    
    public async Task<HANodeMetricsDto?> GetNodeMetricsAsync(int serverId, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_metricsCache.TryGetValue(serverId, out var cached))
        {
            if (DateTime.UtcNow < cached.ExpiresAt)
            {
                return cached.Metrics;
            }
            
            // Remove expired entry
            _metricsCache.TryRemove(serverId, out _);
        }
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
            var server = await context.Servers
                .Include(s => s.PrivateKey)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == serverId, cancellationToken);
            
            if (server == null)
            {
                _logger.LogWarning("Server {ServerId} not found", serverId);
                return null;
            }
            
            // Get system info from Docker API
            var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
            
            // Collect metrics via SSH
            var metrics = await CollectMetricsViaSshAsync(server, systemInfo?.SwarmNodeId ?? "unknown", cancellationToken);
            
            if (metrics != null)
            {
                // Cache the result
                var expiresAt = DateTime.UtcNow.Add(CacheDuration);
                _metricsCache[serverId] = new CachedMetrics(metrics, expiresAt);
            }
            
            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect metrics for server {ServerId}", serverId);
            return null;
        }
    }
    
    public async Task<Dictionary<int, HANodeMetricsDto>> GetAllNodeMetricsAsync(
        IEnumerable<int> serverIds, 
        CancellationToken cancellationToken = default)
    {
        var tasks = serverIds.Select(id => GetNodeMetricsAsync(id, cancellationToken));
        var results = await Task.WhenAll(tasks);
        
        return serverIds
            .Zip(results, (id, metrics) => new { Id = id, Metrics = metrics })
            .Where(x => x.Metrics != null)
            .ToDictionary(x => x.Id, x => x.Metrics!);
    }
    
    public void ClearCache(int serverId)
    {
        _metricsCache.TryRemove(serverId, out _);
    }
    
    public void ClearAllCache()
    {
        _metricsCache.Clear();
    }
    
    /// <summary>
    /// Collect metrics from server via SSH commands
    /// </summary>
    private async Task<HANodeMetricsDto?> CollectMetricsViaSshAsync(
        Server server,
        string nodeId,
        CancellationToken cancellationToken)
    {
        try
        {
            // CPU usage (5-second average via /proc/stat)
            var cpuPercent = await GetCpuUsageAsync(server, cancellationToken);
            
            // Memory usage
            var (memUsed, memTotal) = await GetMemoryUsageAsync(server, cancellationToken);
            
            // Disk usage (root filesystem)
            var (diskUsed, diskTotal) = await GetDiskUsageAsync(server, cancellationToken);
            
            return new HANodeMetricsDto(
                nodeId,
                server.Id,
                cpuPercent,
                memUsed,
                memTotal,
                diskUsed,
                diskTotal,
                DateTime.UtcNow
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to collect SSH metrics for server {ServerId}", server.Id);
            return null;
        }
    }
    
    /// <summary>
    /// Get CPU usage percentage (1-second sampling)
    /// </summary>
    private async Task<double> GetCpuUsageAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            // Read /proc/stat twice with 1 second delay to calculate usage
            var result1 = await _sshService.ExecuteCommandAsync(server, "cat /proc/stat | grep '^cpu ' | awk '{print $2,$3,$4,$5}'", cancellationToken);
            if (result1.ExitCode != 0) return 0;
            
            var values1 = result1.Output.Trim().Split(' ').Select(long.Parse).ToArray();
            
            await Task.Delay(1000, cancellationToken);
            
            var result2 = await _sshService.ExecuteCommandAsync(server, "cat /proc/stat | grep '^cpu ' | awk '{print $2,$3,$4,$5}'", cancellationToken);
            if (result2.ExitCode != 0) return 0;
            
            var values2 = result2.Output.Trim().Split(' ').Select(long.Parse).ToArray();
            
            // Calculate CPU usage: ((user+nice+system)_diff / total_diff) * 100
            var active1 = values1[0] + values1[1] + values1[2];
            var total1 = values1.Sum();
            var active2 = values2[0] + values2[1] + values2[2];
            var total2 = values2.Sum();
            
            var activeDiff = active2 - active1;
            var totalDiff = total2 - total1;
            
            if (totalDiff == 0) return 0;
            
            return Math.Round((activeDiff / (double)totalDiff) * 100, 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get CPU usage via SSH");
            return 0;
        }
    }
    
    /// <summary>
    /// Get memory usage in bytes
    /// </summary>
    private async Task<(long used, long total)> GetMemoryUsageAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sshService.ExecuteCommandAsync(server, "free -b | grep Mem | awk '{print $3,$2}'", cancellationToken);
            if (result.ExitCode != 0) return (0, 0);
            
            var values = result.Output.Trim().Split(' ').Select(long.Parse).ToArray();
            
            return (values[0], values[1]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get memory usage via SSH");
            return (0, 0);
        }
    }
    
    /// <summary>
    /// Get disk usage in bytes (root filesystem)
    /// </summary>
    private async Task<(long used, long total)> GetDiskUsageAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sshService.ExecuteCommandAsync(server, "df -B1 / | tail -1 | awk '{print $3,$2}'", cancellationToken);
            if (result.ExitCode != 0) return (0, 0);
            
            var values = result.Output.Trim().Split(' ').Select(long.Parse).ToArray();
            
            return (values[0], values[1]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get disk usage via SSH");
            return (0, 0);
        }
    }
}
