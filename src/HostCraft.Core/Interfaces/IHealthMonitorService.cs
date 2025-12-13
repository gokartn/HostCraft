using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for health monitoring and auto-recovery.
/// </summary>
public interface IHealthMonitorService
{
    /// <summary>
    /// Performs health check on an application.
    /// </summary>
    Task<HealthCheck> CheckApplicationHealthAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Performs health check on a server (Docker daemon).
    /// </summary>
    Task<HealthCheck> CheckServerHealthAsync(int serverId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks health of all applications and triggers recovery if needed.
    /// </summary>
    Task<IEnumerable<HealthCheck>> MonitorAllApplicationsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks health of all servers.
    /// </summary>
    Task<IEnumerable<HealthCheck>> MonitorAllServersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Attempts automatic recovery for unhealthy application.
    /// </summary>
    Task<bool> AttemptRecoveryAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets health check history for an application.
    /// </summary>
    Task<IEnumerable<HealthCheck>> GetHealthHistoryAsync(int applicationId, int limit = 100, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Calculates uptime percentage for an application.
    /// </summary>
    Task<double> GetUptimePercentageAsync(int applicationId, TimeSpan period, CancellationToken cancellationToken = default);
}
