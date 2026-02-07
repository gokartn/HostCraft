using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for managing deployment logs with database persistence.
/// Uses IServiceProvider to create scopes and avoid entity tracking conflicts when multiple logs are added concurrently.
/// </summary>
public class DeploymentLogService : IDeploymentLogService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeploymentLogService> _logger;

    public DeploymentLogService(
        IServiceProvider serviceProvider,
        ILogger<DeploymentLogService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task AddLogAsync(int deploymentId, string message, string level = "Info")
    {
        if (deploymentId <= 0)
        {
            _logger.LogWarning("Attempted to add log with invalid deployment ID: {DeploymentId}", deploymentId);
            return;
        }

        try
        {
            // Create a new scope and DbContext for each log to avoid tracking conflicts
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();

            var log = new DeploymentLog
            {
                DeploymentId = deploymentId,
                Message = message,
                Level = level,
                Timestamp = DateTime.UtcNow
            };

            context.DeploymentLogs.Add(log);
            await context.SaveChangesAsync();

            _logger.LogDebug("[Deployment {DeploymentId}] [{Level}] {Message}", deploymentId, level, message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add deployment log for deployment {DeploymentId}", deploymentId);
        }
    }

    public async Task AddLogsAsync(int deploymentId, IEnumerable<(string message, string level)> logs)
    {
        if (deploymentId <= 0) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();

            var logEntries = logs.Select(l => new DeploymentLog
            {
                DeploymentId = deploymentId,
                Message = l.message,
                Level = l.level,
                Timestamp = DateTime.UtcNow
            }).ToList();

            context.DeploymentLogs.AddRange(logEntries);
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add deployment logs for deployment {DeploymentId}", deploymentId);
        }
    }

    public IProgress<string> CreateProgressReporter(int deploymentId, string level = "Info")
    {
        return new DeploymentProgressReporter(this, deploymentId, level);
    }

    /// <summary>
    /// Progress reporter that logs to deployment.
    /// </summary>
    private class DeploymentProgressReporter : IProgress<string>
    {
        private readonly DeploymentLogService _logService;
        private readonly int _deploymentId;
        private readonly string _level;

        public DeploymentProgressReporter(DeploymentLogService logService, int deploymentId, string level)
        {
            _logService = logService;
            _deploymentId = deploymentId;
            _level = level;
        }

        public void Report(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                // Fire and forget - we don't want to block the caller
                _ = _logService.AddLogAsync(_deploymentId, value, _level);
            }
        }
    }
}
