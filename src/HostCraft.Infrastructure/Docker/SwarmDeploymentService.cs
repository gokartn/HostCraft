using System.Text.Json;
using Docker.DotNet.Models;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
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
            _logger.LogInformation("Deploying {AppName} to swarm as service with image {Image}", 
                application.Name, imageTag);
            
            // Check if service already exists
            var services = await _dockerService.ListServicesAsync(application.Server, cancellationToken);
            var existingService = services.FirstOrDefault(s => s.Name == application.Name);
            
            if (existingService != null)
            {
                _logger.LogInformation("Service {ServiceName} already exists, performing rolling update", 
                    application.Name);
                
                var updated = await UpdateSwarmServiceAsync(application, imageTag, cancellationToken);
                
                return new ServiceDeploymentResult(
                    updated,
                    existingService.Id,
                    updated ? "Service updated successfully" : "Service update failed");
            }
            else
            {
                _logger.LogInformation("Creating new service {ServiceName}", application.Name);
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
        
        var request = new CreateServiceRequest(
            Name: application.Name,
            Image: imageTag,
            Replicas: replicas,
            EnvironmentVariables: BuildEnvironmentVariables(application),
            Labels: BuildLabels(application),
            Networks: BuildNetworks(application),
            Port: application.Port,
            MemoryLimit: application.MemoryLimitBytes,
            CpuLimit: application.CpuLimit
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
            
            var request = new UpdateServiceRequest(
                Image: imageTag,
                Replicas: replicas,
                EnvironmentVariables: BuildEnvironmentVariables(application),
                Labels: BuildLabels(application)
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
                application.Name, replicas);
            
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
                application.Name, application.SwarmServiceId);

            var result = await _dockerService.RollbackServiceAsync(
                application.Server,
                application.SwarmServiceId,
                cancellationToken);

            if (result)
            {
                _logger.LogInformation("Successfully rolled back service {ServiceName}", application.Name);
            }
            else
            {
                _logger.LogWarning("Rollback failed for service {ServiceName} - no previous spec available",
                    application.Name);
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
            
            _logger.LogInformation("Removing service {ServiceName}", application.Name);
            
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
        
        if (!string.IsNullOrEmpty(application.Domain))
        {
            // Add Traefik labels for routing
            labels["traefik.enable"] = "true";
            labels[$"traefik.http.routers.{application.Name}.rule"] = $"Host(`{application.Domain}`)";
            labels[$"traefik.http.services.{application.Name}.loadbalancer.server.port"] = 
                application.Port?.ToString() ?? "80";
            
            if (application.EnableHttps)
            {
                labels[$"traefik.http.routers.{application.Name}.entrypoints"] = "websecure";
                labels[$"traefik.http.routers.{application.Name}.tls"] = "true";
                labels[$"traefik.http.routers.{application.Name}.tls.certresolver"] = "letsencrypt";
            }
        }
        
        return labels;
    }
    
    private List<string> BuildNetworks(Application application)
    {
        // Use project-specific network or default overlay network
        var networkName = $"{application.Project.Name}-network";
        
        if (!string.IsNullOrEmpty(application.SwarmNetworks))
        {
            try
            {
                var networks = JsonSerializer.Deserialize<List<string>>(application.SwarmNetworks);
                if (networks != null && networks.Any())
                {
                    return networks;
                }
            }
            catch
            {
                _logger.LogWarning("Invalid SwarmNetworks JSON for {AppName}", application.Name);
            }
        }
        
        return new List<string> { networkName };
    }
}
