using System.Text.Json;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Proxy;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Implementation of Docker Swarm service deployment.
/// Handles service creation, updates, scaling, and health monitoring.
/// </summary>
public class SwarmDeploymentService : ISwarmDeploymentService
{
    private readonly IDockerService _dockerService;
    private readonly ILogger<SwarmDeploymentService> _logger;
    
    public SwarmDeploymentService(
        IDockerService dockerService,
        ILogger<SwarmDeploymentService> logger)
    {
        _dockerService = dockerService;
        _logger = logger;
    }
    
    public async Task<ServiceDeploymentResult> DeployToSwarmAsync(
        Application application, 
        string imageTag, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deploying {AppName} (service: {ServiceName}) to swarm with image {Image}",
                application.Name, application.ServiceName, imageTag);

            // Check if service already exists
            var services = await _dockerService.ListServicesAsync(application.Server, cancellationToken);
            var existingService = services.FirstOrDefault(s => s.Name == application.ServiceName);

            if (existingService != null)
            {
                _logger.LogInformation("Service {ServiceName} already exists, performing rolling update",
                    application.ServiceName);
                
                var updated = await UpdateSwarmServiceAsync(application, imageTag, cancellationToken);
                
                return new ServiceDeploymentResult(
                    updated,
                    existingService.Id,
                    updated ? "Service updated successfully" : "Service update failed");
            }
            else
            {
                _logger.LogInformation("Creating new service {ServiceName}", application.ServiceName);
                return await CreateSwarmServiceAsync(application, imageTag, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy {AppName} to swarm", application.Name);
            return new ServiceDeploymentResult(false, null, "Deployment failed", ex.Message);
        }
    }
    
    private async Task<ServiceDeploymentResult> CreateSwarmServiceAsync(
        Application application,
        string imageTag,
        CancellationToken cancellationToken)
    {
        var replicas = application.SwarmReplicas ?? application.Replicas;

        // Build port mappings from JSON or legacy port field
        List<ServicePortMapping>? portMappings = null;
        int? legacyPort = null;

        if (!string.IsNullOrEmpty(application.PortMappings))
        {
            try
            {
                var mappings = JsonSerializer.Deserialize<List<PortMappingJson>>(application.PortMappings);
                if (mappings != null && mappings.Count > 0)
                {
                    portMappings = mappings.Select(m => new ServicePortMapping(
                        m.HostPort,
                        m.ContainerPort,
                        m.Protocol ?? "tcp"
                    )).ToList();

                    _logger.LogInformation("Service {ServiceName} using {Count} port mappings",
                        application.ServiceName, portMappings.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse port mappings for {AppName}, falling back to legacy port",
                    application.Name);
            }
        }

        // Fallback to legacy port field
        if (portMappings == null && application.Port.HasValue)
        {
            legacyPort = application.PublishedPort ?? application.Port.Value;
            _logger.LogInformation("Service {ServiceName} using legacy port {Port}", application.ServiceName, legacyPort);
        }

        var networks = BuildNetworks(application);

        foreach (var network in networks)
        {
            await _dockerService.EnsureNetworkExistsAsync(
                application.Server,
                network,
                cancellationToken);
        }

        // Build health check configuration for zero-downtime rolling updates
        var healthCheck = BuildHealthCheckConfig(application);

        // Build update and rollback configs based on deployment strategy
        var (updateConfig, rollbackConfig) = BuildDeploymentConfigs(application);

        // Build placement configuration for HA/DR replica distribution
        var placementConfig = BuildPlacementConfig(application);

        var request = new CreateServiceRequest(
            Name: application.ServiceName,
            Image: imageTag,
            Replicas: replicas,
            EnvironmentVariables: BuildEnvironmentVariables(application),
            Labels: BuildLabels(application),
            Networks: networks,
            Port: legacyPort,
            PortMappings: portMappings,
            MemoryLimit: application.MemoryLimitBytes,
            CpuLimit: application.CpuLimit,
            UpdateConfig: updateConfig,
            RollbackConfig: rollbackConfig,
            HealthCheck: healthCheck,
            PlacementConfig: placementConfig
        );

        var serviceId = await _dockerService.CreateServiceAsync(
            application.Server,
            request,
            cancellationToken);

        // Store service ID in application
        application.SwarmServiceId = serviceId;

        return new ServiceDeploymentResult(
            true,
            serviceId,
            $"Service {application.Name} created successfully with {replicas} replicas");
    }

    // Helper record for deserializing port mappings from JSON
    private record PortMappingJson(int HostPort, int ContainerPort, string? Protocol);
    
    public async Task<bool> UpdateSwarmServiceAsync(
        Application application, 
        string imageTag, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(application.SwarmServiceId))
            {
                _logger.LogWarning("No service ID found for {AppName}", application.Name);
                return false;
            }
            
            var replicas = application.SwarmReplicas ?? application.Replicas;
            var networks = BuildNetworks(application);

            foreach (var network in networks)
            {
                await _dockerService.EnsureNetworkExistsAsync(application.Server, network, cancellationToken);
            }

            // CRITICAL: Build update/rollback configs and health check for zero-downtime updates
            var healthCheck = BuildHealthCheckConfig(application);
            var (updateConfig, rollbackConfig) = BuildDeploymentConfigs(application);

            var request = new UpdateServiceRequest(
                Image: imageTag,
                Replicas: replicas,
                EnvironmentVariables: BuildEnvironmentVariables(application),
                Labels: BuildLabels(application),
                Networks: networks,
                UpdateConfig: updateConfig,
                RollbackConfig: rollbackConfig,
                HealthCheck: healthCheck
            );
            
            return await _dockerService.UpdateServiceAsync(
                application.Server, 
                application.SwarmServiceId, 
                request, 
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update service {ServiceId}", application.SwarmServiceId);
            return false;
        }
    }
    
    public async Task<bool> ScaleServiceAsync(
        Application application, 
        int replicas, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(application.SwarmServiceId))
            {
                _logger.LogWarning("No service ID found for {AppName}", application.Name);
                return false;
            }
            
            _logger.LogInformation("Scaling service {ServiceName} to {Replicas} replicas",
                application.ServiceName, replicas);
            
            var request = new UpdateServiceRequest(Replicas: replicas);
            
            var result = await _dockerService.UpdateServiceAsync(
                application.Server, 
                application.SwarmServiceId, 
                request, 
                cancellationToken);
            
            if (result)
            {
                application.SwarmReplicas = replicas;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scale service {ServiceId}", application.SwarmServiceId);
            return false;
        }
    }
    
    public async Task<bool> RollbackServiceAsync(
        Application application,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(application.SwarmServiceId))
            {
                _logger.LogWarning("No service ID found for {AppName}", application.Name);
                return false;
            }

            _logger.LogInformation("Rolling back service {ServiceName} (ID: {ServiceId})",
                application.ServiceName, application.SwarmServiceId);

            var result = await _dockerService.RollbackServiceAsync(
                application.Server,
                application.SwarmServiceId,
                cancellationToken);

            if (result)
            {
                _logger.LogInformation("Successfully rolled back service {ServiceName}", application.ServiceName);
            }
            else
            {
                _logger.LogWarning("Rollback failed for service {ServiceName} - no previous spec available",
                    application.ServiceName);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback service {ServiceId}", application.SwarmServiceId);
            return false;
        }
    }
    
    public async Task<bool> RemoveServiceAsync(
        Application application, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(application.SwarmServiceId))
            {
                _logger.LogWarning("No service ID found for {AppName}", application.Name);
                return false;
            }
            
            _logger.LogInformation("Removing service {ServiceName}", application.ServiceName);
            
            return await _dockerService.RemoveServiceAsync(
                application.Server, 
                application.SwarmServiceId, 
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove service {ServiceId}", application.SwarmServiceId);
            return false;
        }
    }
    
    public async Task<ServiceHealth> GetServiceHealthAsync(
        Application application, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(application.SwarmServiceId))
            {
                return new ServiceHealth(0, 0, 0, "unknown");
            }
            
            var serviceInfo = await _dockerService.InspectServiceAsync(
                application.Server, 
                application.SwarmServiceId, 
                cancellationToken);
            
            if (serviceInfo == null)
            {
                return new ServiceHealth(0, 0, 0, "down");
            }
            
            var desiredReplicas = application.SwarmReplicas ?? application.Replicas;
            var runningReplicas = serviceInfo.Replicas;
            
            // Determine status
            string status;
            if (runningReplicas == 0)
            {
                status = "down";
            }
            else if (runningReplicas < desiredReplicas)
            {
                status = "degraded";
            }
            else
            {
                status = "running";
            }
            
            return new ServiceHealth(
                desiredReplicas,
                runningReplicas,
                0, // Failed tasks count would require additional API call
                status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health for service {ServiceId}", application.SwarmServiceId);
            return new ServiceHealth(0, 0, 0, "error");
        }
    }
    
    private Dictionary<string, string> BuildEnvironmentVariables(Application application)
    {
        return application.EnvironmentVariables
            .Where(ev => !ev.IsSecret) // Secrets should be handled separately in swarm
            .ToDictionary(ev => ev.Key, ev => ev.Value);
    }
    
    private Dictionary<string, string> BuildLabels(Application application)
    {
        var labels = new Dictionary<string, string>
        {
            ["hostcraft.app.id"] = application.Id.ToString(),
            ["hostcraft.app.uuid"] = application.Uuid.ToString(),
            ["hostcraft.app.name"] = application.Name,
            ["hostcraft.project.id"] = application.ProjectId.ToString(),
            ["hostcraft.project.name"] = application.Project.Name,
            ["com.docker.stack.namespace"] = application.Project.Name
        };

        // Add Traefik labels for routing (uses TraefikLabelBuilder for complete configuration)
        var traefikLabels = TraefikLabelBuilder.BuildLabels(application, "hostcraft_hostcraft-network");
        foreach (var label in traefikLabels)
        {
            labels[label.Key] = label.Value;
        }

        return labels;
    }
    
    private List<string> BuildNetworks(Application application)
    {
        var networks = new List<string>();

        // Use project-specific network or default overlay network
        var networkName = $"{DockerNameHelper.NormalizeNetworkName(application.Project.Name)}-network";

        if (!string.IsNullOrEmpty(application.SwarmNetworks))
        {
            try
            {
                var customNetworks = JsonSerializer.Deserialize<List<string>>(application.SwarmNetworks);
                if (customNetworks != null && customNetworks.Any())
                {
                    networks.AddRange(customNetworks);
                }
            }
            catch
            {
                _logger.LogWarning("Invalid SwarmNetworks JSON for {AppName}", application.Name);
            }
        }

        // Always include project network
        if (!networks.Contains(networkName))
        {
            networks.Add(networkName);
        }

        // If domain is configured (legacy or new multi-domain), also connect to hostcraft_hostcraft-network for routing
        bool hasDomain = !string.IsNullOrEmpty(application.Domain) || 
                        (application.Domains != null && application.Domains.Any(d => d.IsActive));
        
        if (hasDomain && !networks.Contains("hostcraft_hostcraft-network"))
        {
            networks.Add("hostcraft_hostcraft-network");
        }

        return networks;
    }

    /// <summary>
    /// Build health check configuration from application settings.
    /// Required for start-first zero-downtime deployments.
    /// </summary>
    private ServiceHealthCheckConfig? BuildHealthCheckConfig(Application application)
    {
        // If application has a health check URL, build HTTP health check
        if (!string.IsNullOrEmpty(application.HealthCheckUrl))
        {
            var healthCheckUrl = application.HealthCheckUrl;

            // Ensure URL has a protocol
            if (!healthCheckUrl.StartsWith("http://") && !healthCheckUrl.StartsWith("https://"))
            {
                healthCheckUrl = $"http://localhost:{application.Port ?? 80}{healthCheckUrl}";
            }

            return new ServiceHealthCheckConfig(
                Test: new List<string> { "CMD-SHELL", $"curl -f {healthCheckUrl} || exit 1" },
                IntervalSeconds: application.HealthCheckIntervalSeconds,
                TimeoutSeconds: application.HealthCheckTimeoutSeconds,
                Retries: application.MaxConsecutiveFailures,
                StartPeriodSeconds: 60 // 60 seconds startup grace period
            );
        }

        // If no explicit health check, use a simple TCP check on the primary port
        if (application.Port.HasValue)
        {
            // For databases, use database-specific health checks
            if (application.DatabaseType.HasValue)
            {
                return BuildDatabaseHealthCheck(application);
            }

            // Generic TCP health check (just check if port is open)
            return new ServiceHealthCheckConfig(
                Test: new List<string> { "CMD-SHELL", $"timeout 5 bash -c '</dev/tcp/localhost/{application.Port.Value}' || exit 1" },
                IntervalSeconds: 30, // 30 seconds
                TimeoutSeconds: 10,  // 10 seconds
                Retries: 3,
                StartPeriodSeconds: 60 // 60 seconds
            );
        }

        // No health check if no port or URL configured
        return null;
    }

    /// <summary>
    /// Build database-specific health check commands.
    /// </summary>
    private ServiceHealthCheckConfig BuildDatabaseHealthCheck(Application application)
    {
        var test = application.DatabaseType switch
        {
            DatabaseType.PostgreSQL => new List<string> { "CMD-SHELL", "pg_isready -U postgres || exit 1" },
            DatabaseType.MySQL => new List<string> { "CMD", "mysqladmin", "ping", "-h", "localhost" },
            DatabaseType.MariaDB => new List<string> { "CMD", "healthcheck.sh", "--connect", "--innodb_initialized" },
            DatabaseType.MongoDB => new List<string> { "CMD", "mongosh", "--eval", "db.adminCommand('ping')" },
            DatabaseType.Redis => new List<string> { "CMD", "redis-cli", "ping" },
            DatabaseType.KeyDB => new List<string> { "CMD", "keydb-cli", "ping" },
            DatabaseType.DragonFly => new List<string> { "CMD", "redis-cli", "ping" },
            DatabaseType.Clickhouse => new List<string> { "CMD-SHELL", "wget --spider -q localhost:8123/ping" },
            _ => new List<string> { "CMD-SHELL", $"timeout 5 bash -c '</dev/tcp/localhost/{application.Port ?? 80}' || exit 1" }
        };

        return new ServiceHealthCheckConfig(
            Test: test,
            IntervalSeconds: 30, // 30 seconds
            TimeoutSeconds: 10,  // 10 seconds
            Retries: 3,
            StartPeriodSeconds: 60 // 60 seconds
        );
    }

    /// <summary>
    /// Build update and rollback configurations based on deployment strategy.
    /// Returns tuple of (UpdateConfig, RollbackConfig).
    /// </summary>
    private (ServiceUpdateConfig?, ServiceRollbackConfig?) BuildDeploymentConfigs(Application application)
    {
        return application.DeploymentStrategy switch
        {
            // Rolling deployment: Zero-downtime start-first strategy (HA/DR default)
            DeploymentStrategy.Rolling => (
                new ServiceUpdateConfig(
                    Order: "start-first",           // CRITICAL: Start new replicas before stopping old
                    Parallelism: 1,                  // Update 1 replica at a time for controlled rollout
                    DelaySeconds: 10,               // 10 seconds between updates
                    FailureAction: "rollback",       // Auto-rollback on failure
                    MaxFailureRatio: 0.2f            // Rollback if >20% of updates fail
                ),
                new ServiceRollbackConfig(
                    Parallelism: 1,                  // Rollback 1 replica at a time
                    DelaySeconds: 5,                // 5 seconds between rollbacks (faster recovery)
                    FailureAction: "pause",          // Pause rollback on failure for investigation
                    MaxFailureRatio: 0.0f            // Any failure pauses rollback
                )
            ),

            // Recreate: Stop-first strategy (causes downtime, not recommended for HA/DR)
            DeploymentStrategy.Recreate => (
                new ServiceUpdateConfig(
                    Order: "stop-first",             // Stop old replicas before starting new (downtime!)
                    Parallelism: 0,                  // Update all replicas at once
                    DelaySeconds: 0,                 // No delay
                    FailureAction: "pause",          // Pause on failure
                    MaxFailureRatio: 0.0f
                ),
                null // No rollback config for recreate
            ),

            // BlueGreen: Not implemented yet (use rolling for now)
            // TODO: Implement Blue/Green by creating new service and switching Traefik routing
            DeploymentStrategy.BlueGreen => (
                new ServiceUpdateConfig(
                    Order: "start-first",
                    Parallelism: 0,                  // Deploy all replicas at once
                    DelaySeconds: 0,
                    FailureAction: "rollback",
                    MaxFailureRatio: 0.1f
                ),
                new ServiceRollbackConfig(
                    Parallelism: 0,                  // Rollback all at once
                    DelaySeconds: 0,
                    FailureAction: "pause",
                    MaxFailureRatio: 0.0f
                )
            ),

            // Default to rolling
            _ => (
                new ServiceUpdateConfig(
                    Order: "start-first",
                    Parallelism: 1,
                    DelaySeconds: 10,
                    FailureAction: "rollback",
                    MaxFailureRatio: 0.2f
                ),
                new ServiceRollbackConfig(
                    Parallelism: 1,
                    DelaySeconds: 5,
                    FailureAction: "pause",
                    MaxFailureRatio: 0.0f
                )
            )
        };
    }

    private ServicePlacementConfig BuildPlacementConfig(Application application)
    {
        List<string>? constraints = null;
        List<HostCraft.Core.Interfaces.PlacementPreference>? preferences = null;
        ulong? maxReplicasPerNode = null;

        var replicas = application.SwarmReplicas ?? application.Replicas;

        // Parse placement constraints from JSON (for Custom strategy)
        if (!string.IsNullOrWhiteSpace(application.SwarmPlacementConstraints))
        {
            try
            {
                constraints = System.Text.Json.JsonSerializer.Deserialize<List<string>>(application.SwarmPlacementConstraints);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse SwarmPlacementConstraints for application {AppId}", application.Id);
            }
        }

        // Parse placement preferences from JSON (if explicitly set)
        if (!string.IsNullOrWhiteSpace(application.SwarmPlacementPreferences))
        {
            try
            {
                var prefsJson = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(application.SwarmPlacementPreferences);
                if (prefsJson != null)
                {
                    preferences = prefsJson
                        .Where(p => p.ContainsKey("Spread"))
                        .Select(p => new HostCraft.Core.Interfaces.PlacementPreference(p["Spread"]))
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse SwarmPlacementPreferences for application {AppId}", application.Id);
            }
        }

        // Apply placement strategy if preferences not explicitly set
        if (preferences == null || preferences.Count == 0)
        {
            switch (application.PlacementStrategy)
            {
                case Core.Enums.PlacementStrategy.Spread:
                    // HA/DR default: Spread across nodes evenly
                    preferences = new List<HostCraft.Core.Interfaces.PlacementPreference>
                    {
                        new HostCraft.Core.Interfaces.PlacementPreference("node.id")
                    };

                    // For HA, default to max 1 replica per node if not set
                    if (!application.MaxReplicasPerNode.HasValue && replicas >= 2)
                    {
                        maxReplicasPerNode = 1;
                        _logger.LogInformation("Applying HA default: MaxReplicasPerNode=1 for {AppName} with {Replicas} replicas",
                            application.Name, replicas);
                    }
                    break;

                case Core.Enums.PlacementStrategy.Binpack:
                    // No spread preference - Swarm will binpack by default
                    // This fills nodes before moving to next node
                    _logger.LogInformation("Using Binpack strategy for {AppName} - replicas will be packed onto nodes",
                        application.Name);
                    preferences = null;
                    break;

                case Core.Enums.PlacementStrategy.Random:
                    // No preferences - Swarm decides based on resources
                    _logger.LogInformation("Using Random strategy for {AppName} - Swarm will decide placement",
                        application.Name);
                    preferences = null;
                    break;

                case Core.Enums.PlacementStrategy.Custom:
                    // Use custom constraints only (no preferences)
                    _logger.LogInformation("Using Custom strategy for {AppName} with constraints: {Constraints}",
                        application.Name, string.Join(", ", constraints ?? new List<string>()));
                    preferences = null;
                    break;
            }
        }

        // Apply max replicas per node if explicitly set
        if (application.MaxReplicasPerNode.HasValue)
        {
            maxReplicasPerNode = (ulong)application.MaxReplicasPerNode.Value;
        }

        // Log placement configuration for debugging
        if (preferences != null && preferences.Count > 0)
        {
            _logger.LogInformation("Placement config for {AppName}: Strategy={Strategy}, Spread={Spread}, MaxPerNode={Max}",
                application.Name,
                application.PlacementStrategy,
                string.Join(", ", preferences.Select(p => p.Spread)),
                maxReplicasPerNode?.ToString() ?? "unlimited");
        }

        return new ServicePlacementConfig(
            Constraints: constraints,
            Preferences: preferences,
            MaxReplicasPerNode: maxReplicasPerNode
        );
    }

}
