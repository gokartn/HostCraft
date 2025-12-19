using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Health;

/// <summary>
/// Service for health monitoring and auto-recovery of applications and servers.
/// </summary>
public class HealthMonitorService : IHealthMonitorService
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<HealthMonitorService> _logger;
    private readonly HttpClient _httpClient;

    public HealthMonitorService(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<HealthMonitorService> logger,
        HttpClient httpClient)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
        _httpClient = httpClient;

        // Configure HttpClient for health checks
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<HealthCheck> CheckApplicationHealthAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            _logger.LogWarning("Application {ApplicationId} not found for health check", applicationId);
            return CreateHealthCheck(applicationId, null, HealthStatus.Unknown, 0, null, "Application not found");
        }

        var stopwatch = Stopwatch.StartNew();
        HealthStatus status;
        string? statusCode = null;
        string? errorMessage = null;

        try
        {
            // Determine check type based on application configuration
            if (!string.IsNullOrEmpty(application.HealthCheckUrl))
            {
                // HTTP health check
                (status, statusCode, errorMessage) = await PerformHttpHealthCheckAsync(
                    application.HealthCheckUrl,
                    application.HealthCheckTimeoutSeconds,
                    cancellationToken);
            }
            else if (application.Port.HasValue && !string.IsNullOrEmpty(application.Domain))
            {
                // TCP health check to the application port
                (status, errorMessage) = await PerformTcpHealthCheckAsync(
                    application.Domain,
                    application.Port.Value,
                    application.HealthCheckTimeoutSeconds,
                    cancellationToken);
                statusCode = status == HealthStatus.Healthy ? "TCP_OK" : "TCP_FAIL";
            }
            else
            {
                // Container/service state check
                (status, errorMessage) = await CheckContainerOrServiceStateAsync(
                    application,
                    cancellationToken);
                statusCode = status == HealthStatus.Healthy ? "RUNNING" : "NOT_RUNNING";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for application {ApplicationId}", applicationId);
            status = HealthStatus.Unknown;
            errorMessage = ex.Message;
        }

        stopwatch.Stop();
        var responseTimeMs = (int)stopwatch.ElapsedMilliseconds;

        // Update application health tracking
        application.LastHealthCheckAt = DateTime.UtcNow;
        if (status == HealthStatus.Healthy)
        {
            application.ConsecutiveHealthCheckFailures = 0;
        }
        else
        {
            application.ConsecutiveHealthCheckFailures++;
            _logger.LogWarning("Application {ApplicationId} health check failed ({Failures} consecutive failures)",
                applicationId, application.ConsecutiveHealthCheckFailures);
        }

        // Save health check to database
        var healthCheck = CreateHealthCheck(applicationId, null, status, responseTimeMs, statusCode, errorMessage);
        _context.HealthChecks.Add(healthCheck);
        await _context.SaveChangesAsync(cancellationToken);

        // Trigger auto-recovery if needed
        if (status != HealthStatus.Healthy &&
            application.AutoRestart &&
            application.ConsecutiveHealthCheckFailures >= application.MaxConsecutiveFailures)
        {
            _logger.LogWarning("Triggering auto-recovery for application {ApplicationId} after {Failures} consecutive failures",
                applicationId, application.ConsecutiveHealthCheckFailures);
            _ = AttemptRecoveryAsync(applicationId, cancellationToken);
        }

        return healthCheck;
    }

    public async Task<HealthCheck> CheckServerHealthAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId, cancellationToken);

        if (server == null)
        {
            _logger.LogWarning("Server {ServerId} not found for health check", serverId);
            return CreateHealthCheck(null, serverId, HealthStatus.Unknown, 0, null, "Server not found");
        }

        var stopwatch = Stopwatch.StartNew();
        HealthStatus status;
        string? statusCode = null;
        string? errorMessage = null;

        try
        {
            // Check Docker daemon connectivity
            var isConnected = await _dockerService.ValidateConnectionAsync(server, cancellationToken);

            if (isConnected)
            {
                var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
                status = HealthStatus.Healthy;
                statusCode = $"Docker {systemInfo.DockerVersion}";

                // Update server status
                server.Status = ServerStatus.Online;
                server.ConsecutiveFailures = 0;
            }
            else
            {
                status = HealthStatus.Unhealthy;
                statusCode = "CONNECTION_FAILED";
                errorMessage = "Cannot connect to Docker daemon";

                server.Status = ServerStatus.Offline;
                server.ConsecutiveFailures++;
                server.LastFailureAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Server health check failed for {ServerId}", serverId);
            status = HealthStatus.Unknown;
            errorMessage = ex.Message;

            server.Status = ServerStatus.Offline;
            server.ConsecutiveFailures++;
            server.LastFailureAt = DateTime.UtcNow;
        }

        stopwatch.Stop();
        var responseTimeMs = (int)stopwatch.ElapsedMilliseconds;

        server.LastHealthCheck = DateTime.UtcNow;

        var healthCheck = CreateHealthCheck(null, serverId, status, responseTimeMs, statusCode, errorMessage);
        _context.HealthChecks.Add(healthCheck);
        await _context.SaveChangesAsync(cancellationToken);

        return healthCheck;
    }

    public async Task<IEnumerable<HealthCheck>> MonitorAllApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var applications = await _context.Applications
            .Include(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .Where(a => a.LastDeployedAt != null) // Only check deployed applications
            .ToListAsync(cancellationToken);

        var results = new List<HealthCheck>();

        foreach (var app in applications)
        {
            try
            {
                // Check if it's time for a health check
                var timeSinceLastCheck = DateTime.UtcNow - (app.LastHealthCheckAt ?? DateTime.MinValue);
                if (timeSinceLastCheck.TotalSeconds >= app.HealthCheckIntervalSeconds)
                {
                    var result = await CheckApplicationHealthAsync(app.Id, cancellationToken);
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring application {ApplicationId}", app.Id);
            }
        }

        return results;
    }

    public async Task<IEnumerable<HealthCheck>> MonitorAllServersAsync(CancellationToken cancellationToken = default)
    {
        var servers = await _context.Servers
            .Include(s => s.PrivateKey)
            .ToListAsync(cancellationToken);

        var results = new List<HealthCheck>();

        foreach (var server in servers)
        {
            try
            {
                var result = await CheckServerHealthAsync(server.Id, cancellationToken);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring server {ServerId}", server.Id);
            }
        }

        return results;
    }

    public async Task<bool> AttemptRecoveryAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .ThenInclude(s => s.PrivateKey)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            _logger.LogWarning("Cannot recover application {ApplicationId}: not found", applicationId);
            return false;
        }

        _logger.LogInformation("Attempting recovery for application {ApplicationId} ({ApplicationName})",
            applicationId, application.Name);

        try
        {
            if (application.DeployAsService)
            {
                // For swarm services, we can force an update to restart tasks
                if (!string.IsNullOrEmpty(application.SwarmServiceId))
                {
                    var service = await _dockerService.InspectServiceAsync(application.Server, application.SwarmServiceId, cancellationToken);
                    if (service != null)
                    {
                        // Force update to restart the service
                        var updateRequest = new UpdateServiceRequest(Image: application.DockerImage);
                        var updated = await _dockerService.UpdateServiceAsync(
                            application.Server,
                            application.SwarmServiceId,
                            updateRequest,
                            cancellationToken);

                        if (updated)
                        {
                            _logger.LogInformation("Recovery successful: restarted service {ServiceId}", application.SwarmServiceId);
                            application.ConsecutiveHealthCheckFailures = 0;
                            await _context.SaveChangesAsync(cancellationToken);
                            return true;
                        }
                    }
                }
            }
            else
            {
                // For standalone containers, try to restart
                var containers = await _dockerService.ListContainersAsync(application.Server, showAll: true, cancellationToken);
                var container = containers.FirstOrDefault(c =>
                    c.Name.Contains(application.Name, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(application.Uuid.ToString(), StringComparison.OrdinalIgnoreCase));

                if (container != null)
                {
                    // Stop and start the container
                    await _dockerService.StopContainerAsync(application.Server, container.Id, cancellationToken);
                    await Task.Delay(1000, cancellationToken); // Brief pause
                    var started = await _dockerService.StartContainerAsync(application.Server, container.Id, cancellationToken);

                    if (started)
                    {
                        _logger.LogInformation("Recovery successful: restarted container {ContainerId}", container.Id);
                        application.ConsecutiveHealthCheckFailures = 0;
                        await _context.SaveChangesAsync(cancellationToken);
                        return true;
                    }
                }
            }

            _logger.LogWarning("Recovery failed for application {ApplicationId}: could not find or restart container/service",
                applicationId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery failed for application {ApplicationId}", applicationId);
            return false;
        }
    }

    public async Task<IEnumerable<HealthCheck>> GetHealthHistoryAsync(int applicationId, int limit = 100, CancellationToken cancellationToken = default)
    {
        return await _context.HealthChecks
            .Where(h => h.ApplicationId == applicationId)
            .OrderByDescending(h => h.CheckedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<double> GetUptimePercentageAsync(int applicationId, TimeSpan period, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - period;

        var healthChecks = await _context.HealthChecks
            .Where(h => h.ApplicationId == applicationId && h.CheckedAt >= cutoff)
            .ToListAsync(cancellationToken);

        if (healthChecks.Count == 0)
        {
            return 100.0; // No data, assume 100% uptime
        }

        var healthyCount = healthChecks.Count(h => h.Status == HealthStatus.Healthy);
        return (double)healthyCount / healthChecks.Count * 100.0;
    }

    private async Task<(HealthStatus status, string? statusCode, string? errorMessage)> PerformHttpHealthCheckAsync(
        string url,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var response = await _httpClient.GetAsync(url, cts.Token);
            var statusCode = ((int)response.StatusCode).ToString();

            if (response.IsSuccessStatusCode)
            {
                return (HealthStatus.Healthy, statusCode, null);
            }
            else if ((int)response.StatusCode >= 500)
            {
                return (HealthStatus.Unhealthy, statusCode, $"Server error: {response.StatusCode}");
            }
            else
            {
                return (HealthStatus.Degraded, statusCode, $"Non-success status: {response.StatusCode}");
            }
        }
        catch (OperationCanceledException)
        {
            return (HealthStatus.Unhealthy, "TIMEOUT", $"Health check timed out after {timeoutSeconds}s");
        }
        catch (HttpRequestException ex)
        {
            return (HealthStatus.Unhealthy, "ERROR", ex.Message);
        }
    }

    private async Task<(HealthStatus status, string? errorMessage)> PerformTcpHealthCheckAsync(
        string host,
        int port,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            await client.ConnectAsync(host, port, cts.Token);
            return (HealthStatus.Healthy, null);
        }
        catch (OperationCanceledException)
        {
            return (HealthStatus.Unhealthy, $"TCP connection timed out after {timeoutSeconds}s");
        }
        catch (SocketException ex)
        {
            return (HealthStatus.Unhealthy, $"TCP connection failed: {ex.Message}");
        }
    }

    private async Task<(HealthStatus status, string? errorMessage)> CheckContainerOrServiceStateAsync(
        Application application,
        CancellationToken cancellationToken)
    {
        try
        {
            if (application.DeployAsService && !string.IsNullOrEmpty(application.SwarmServiceId))
            {
                var service = await _dockerService.InspectServiceAsync(
                    application.Server,
                    application.SwarmServiceId,
                    cancellationToken);

                if (service == null)
                {
                    return (HealthStatus.Unhealthy, "Service not found");
                }

                // Check if replicas match desired count
                var desiredReplicas = application.SwarmReplicas ?? application.Replicas;
                if (service.Replicas < desiredReplicas)
                {
                    return (HealthStatus.Degraded, $"Only {service.Replicas}/{desiredReplicas} replicas running");
                }

                return (HealthStatus.Healthy, null);
            }
            else
            {
                // Check container state
                var containers = await _dockerService.ListContainersAsync(application.Server, showAll: true, cancellationToken);
                var container = containers.FirstOrDefault(c =>
                    c.Name.Contains(application.Name, StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Contains(application.Uuid.ToString(), StringComparison.OrdinalIgnoreCase));

                if (container == null)
                {
                    return (HealthStatus.Unhealthy, "Container not found");
                }

                if (container.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                {
                    return (HealthStatus.Healthy, null);
                }
                else if (container.State.Equals("paused", StringComparison.OrdinalIgnoreCase))
                {
                    return (HealthStatus.Degraded, "Container is paused");
                }
                else
                {
                    return (HealthStatus.Unhealthy, $"Container state: {container.State}");
                }
            }
        }
        catch (Exception ex)
        {
            return (HealthStatus.Unknown, ex.Message);
        }
    }

    private static HealthCheck CreateHealthCheck(
        int? applicationId,
        int? serverId,
        HealthStatus status,
        int responseTimeMs,
        string? statusCode,
        string? errorMessage)
    {
        return new HealthCheck
        {
            ApplicationId = applicationId,
            ServerId = serverId,
            Status = status,
            ResponseTimeMs = responseTimeMs,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            CheckedAt = DateTime.UtcNow
        };
    }
}
