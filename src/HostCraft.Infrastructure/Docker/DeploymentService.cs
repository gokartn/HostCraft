using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Orchestrates application deployments.
/// Routes to SwarmDeploymentService for swarm servers, standalone for others.
/// </summary>
public class DeploymentService : IDeploymentService
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ISwarmDeploymentService _swarmDeploymentService;
    private readonly IGitService _gitService;
    private readonly IBuildService _buildService;
    private readonly ILogger<DeploymentService> _logger;
    
    public DeploymentService(
        HostCraftDbContext context,
        IDockerService dockerService,
        ISwarmDeploymentService swarmDeploymentService,
        IGitService gitService,
        IBuildService buildService,
        ILogger<DeploymentService> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _swarmDeploymentService = swarmDeploymentService;
        _gitService = gitService;
        _buildService = buildService;
        _logger = logger;
    }
    
    public async Task<Deployment> DeployApplicationAsync(
        int applicationId, 
        string? commitHash = null, 
        IProgress<string>? progress = null, 
        CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .Include(a => a.Project)
            .Include(a => a.EnvironmentVariables)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        
        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }
        
        // Create deployment record
        var deployment = new Deployment
        {
            ApplicationId = applicationId,
            CommitSha = commitHash ?? application.LastCommitSha,
            Status = DeploymentStatus.Queued,
            TriggeredBy = "Manual",
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync(cancellationToken);
        
        try
        {
            progress?.Report($"Starting deployment for {application.Name}");
            deployment.Status = DeploymentStatus.Running;
            await _context.SaveChangesAsync(cancellationToken);
            
            string imageTag;
            
            // Build or pull image
            if (application.SourceType == ApplicationSourceType.Git)
            {
                progress?.Report("Cloning repository...");
                var repoPath = await _gitService.CloneApplicationRepositoryAsync(
                    application, 
                    commitHash);
                
                progress?.Report("Building Docker image...");
                imageTag = await _buildService.BuildImageAsync(
                    application, 
                    repoPath, 
                    commitHash);
                
                progress?.Report("Cleaning up repository...");
                await _gitService.CleanupRepositoryAsync(repoPath);
            }
            else if (application.SourceType == ApplicationSourceType.DockerImage)
            {
                if (string.IsNullOrEmpty(application.DockerImage))
                {
                    throw new InvalidOperationException("Docker image not specified");
                }
                
                progress?.Report($"Pulling image {application.DockerImage}...");
                await _dockerService.PullImageAsync(
                    application.Server, 
                    application.DockerImage, 
                    progress, 
                    cancellationToken);
                
                imageTag = application.DockerImage;
            }
            else
            {
                throw new NotImplementedException($"Source type {application.SourceType} not yet implemented");
            }
            
            // Route to appropriate deployment method
            bool success;
            
            if (application.Server.CanDeployAsService && application.DeployAsService)
            {
                progress?.Report("Deploying as Docker Swarm service...");
                var result = await _swarmDeploymentService.DeployToSwarmAsync(
                    application, 
                    imageTag, 
                    cancellationToken);
                
                success = result.Success;
                
                if (!success)
                {
                    throw new Exception(result.Error ?? "Swarm deployment failed");
                }
                
                progress?.Report(result.Message);
            }
            else
            {
                progress?.Report("Deploying as standalone container...");
                success = await DeployStandaloneContainerAsync(
                    application, 
                    imageTag, 
                    progress, 
                    cancellationToken);
            }
            
            if (success)
            {
                deployment.Status = DeploymentStatus.Success;
                application.LastDeployedAt = DateTime.UtcNow;
                
                if (!string.IsNullOrEmpty(commitHash))
                {
                    application.LastCommitSha = commitHash;
                }
                
                progress?.Report("Deployment completed successfully!");
            }
            else
            {
                deployment.Status = DeploymentStatus.Failed;
                progress?.Report("Deployment failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment failed for application {ApplicationId}", applicationId);
            deployment.Status = DeploymentStatus.Failed;
            progress?.Report($"Error: {ex.Message}");
        }
        
        await _context.SaveChangesAsync(cancellationToken);
        return deployment;
    }
    
    private async Task<bool> DeployStandaloneContainerAsync(
        Application application, 
        string imageTag, 
        IProgress<string>? progress, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Stop existing container if running
            var containers = await _dockerService.ListContainersAsync(
                application.Server, 
                showAll: true, 
                cancellationToken);
            
            var existingContainer = containers.FirstOrDefault(c => c.Name.Contains(application.Name));
            
            if (existingContainer != null)
            {
                progress?.Report($"Stopping existing container {existingContainer.Id}...");
                await _dockerService.StopContainerAsync(
                    application.Server, 
                    existingContainer.Id, 
                    cancellationToken);
                
                await _dockerService.RemoveContainerAsync(
                    application.Server, 
                    existingContainer.Id, 
                    cancellationToken);
            }
            
            // Create new container
            progress?.Report("Creating container...");
            
            var envVars = application.EnvironmentVariables
                .Where(ev => !ev.IsSecret)
                .ToDictionary(ev => ev.Key, ev => ev.Value);
            
            var labels = new Dictionary<string, string>
            {
                ["hostcraft.app.id"] = application.Id.ToString(),
                ["hostcraft.app.name"] = application.Name,
                ["hostcraft.project.id"] = application.ProjectId.ToString()
            };
            
            // Add Traefik labels if domain is configured
            if (!string.IsNullOrEmpty(application.Domain))
            {
                labels["traefik.enable"] = "true";
                labels[$"traefik.http.routers.{application.Name}.rule"] = $"Host(`{application.Domain}`)";
                
                if (application.Port.HasValue)
                {
                    labels[$"traefik.http.services.{application.Name}.loadbalancer.server.port"] = 
                        application.Port.Value.ToString();
                }
                
                if (application.EnableHttps)
                {
                    labels[$"traefik.http.routers.{application.Name}.entrypoints"] = "websecure";
                    labels[$"traefik.http.routers.{application.Name}.tls"] = "true";
                    labels[$"traefik.http.routers.{application.Name}.tls.certresolver"] = "letsencrypt";
                }
            }
            
            var request = new CreateContainerRequest(
                Name: application.Name,
                Image: imageTag,
                EnvironmentVariables: envVars,
                Labels: labels,
                Networks: new List<string> { $"{application.Project.Name}-network" },
                PortBindings: application.Port.HasValue 
                    ? new Dictionary<int, int> { [application.Port.Value] = application.Port.Value } 
                    : null,
                MemoryLimit: application.MemoryLimitBytes,
                CpuLimit: application.CpuLimit);
            
            var containerId = await _dockerService.CreateContainerAsync(
                application.Server, 
                request, 
                cancellationToken);
            
            progress?.Report($"Starting container {containerId}...");
            await _dockerService.StartContainerAsync(
                application.Server, 
                containerId, 
                cancellationToken);
            
            progress?.Report("Container started successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy standalone container for {AppName}", application.Name);
            progress?.Report($"Container deployment failed: {ex.Message}");
            return false;
        }
    }
    
    public async Task<bool> StopApplicationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        
        if (application == null)
        {
            return false;
        }
        
        if (application.DeployAsService && !string.IsNullOrEmpty(application.SwarmServiceId))
        {
            return await _swarmDeploymentService.RemoveServiceAsync(application, cancellationToken);
        }
        else
        {
            var containers = await _dockerService.ListContainersAsync(
                application.Server, 
                showAll: false, 
                cancellationToken);
            
            var container = containers.FirstOrDefault(c => c.Name.Contains(application.Name));
            
            if (container != null)
            {
                return await _dockerService.StopContainerAsync(
                    application.Server, 
                    container.Id, 
                    cancellationToken);
            }
            
            return false;
        }
    }
    
    public async Task<bool> RestartApplicationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        
        if (application == null)
        {
            return false;
        }
        
        // For swarm services, we can trigger a restart by updating the service
        // For containers, stop and redeploy
        await StopApplicationAsync(applicationId, cancellationToken);
        await Task.Delay(2000, cancellationToken); // Wait for graceful shutdown
        
        var deployment = await DeployApplicationAsync(applicationId, null, null, cancellationToken);
        return deployment.Status == DeploymentStatus.Success;
    }
    
    public async Task<bool> ScaleApplicationAsync(int applicationId, int replicas, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        
        if (application == null)
        {
            return false;
        }
        
        if (application.DeployAsService)
        {
            return await _swarmDeploymentService.ScaleServiceAsync(
                application, 
                replicas, 
                cancellationToken);
        }
        else
        {
            _logger.LogWarning("Scaling is only supported for swarm services");
            return false;
        }
    }
    
    public async Task<bool> RollbackDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default)
    {
        var deployment = await _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
            .FirstOrDefaultAsync(d => d.Id == deploymentId, cancellationToken);
        
        if (deployment == null)
        {
            return false;
        }
        
        var application = deployment.Application;
        
        if (application.DeployAsService)
        {
            return await _swarmDeploymentService.RollbackServiceAsync(
                application, 
                cancellationToken);
        }
        else
        {
            // Find previous successful deployment
            var previousDeployment = await _context.Deployments
                .Where(d => d.ApplicationId == application.Id && 
                            d.Id < deploymentId && 
                            d.Status == DeploymentStatus.Success)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
            
            if (previousDeployment != null && !string.IsNullOrEmpty(previousDeployment.CommitSha))
            {
                await DeployApplicationAsync(
                    application.Id, 
                    previousDeployment.CommitSha, 
                    null, 
                    cancellationToken);
                return true;
            }
            
            return false;
        }
    }
    
    public async Task<Stream> GetApplicationLogsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);
        
        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }
        
        if (application.DeployAsService && !string.IsNullOrEmpty(application.SwarmServiceId))
        {
            return await _dockerService.GetServiceLogsAsync(
                application.Server, 
                application.SwarmServiceId, 
                cancellationToken);
        }
        else
        {
            var containers = await _dockerService.ListContainersAsync(
                application.Server, 
                showAll: false, 
                cancellationToken);
            
            var container = containers.FirstOrDefault(c => c.Name.Contains(application.Name));
            
            if (container != null)
            {
                return await _dockerService.GetContainerLogsAsync(
                    application.Server, 
                    container.Id, 
                    cancellationToken);
            }
            
            throw new InvalidOperationException($"No running container found for application {application.Name}");
        }
    }
}
