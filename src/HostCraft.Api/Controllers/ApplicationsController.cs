using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ApplicationsController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly IProxyService _proxyService;
    private readonly ILogger<ApplicationsController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    
    public ApplicationsController(
        HostCraftDbContext context,
        IDockerService dockerService,
        IProxyService proxyService,
        ILogger<ApplicationsController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _dockerService = dockerService;
        _proxyService = proxyService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ApplicationDto>>> GetApplications(
        [FromQuery] int? serverId = null,
        [FromQuery] int? projectId = null)
    {
        var query = _context.Applications
            .Include(a => a.Server)
            .Include(a => a.Project)
            .Include(a => a.Deployments.OrderByDescending(d => d.StartedAt).Take(1))
            .AsQueryable();
        
        if (serverId.HasValue)
            query = query.Where(a => a.ServerId == serverId.Value);
        
        if (projectId.HasValue)
            query = query.Where(a => a.ProjectId == projectId.Value);
        
        var apps = await query.ToListAsync();
        
        return Ok(apps.Select(a => new ApplicationDto
        {
            Id = a.Id,
            Name = a.Name,
            Description = a.Description,
            ServerId = a.ServerId,
            ServerName = a.Server.Name,
            ProjectId = a.ProjectId,
            ProjectName = a.Project.Name,
            DockerImage = a.DockerImage,
            Status = a.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault()?.Status ?? DeploymentStatus.Queued,
            LastDeployedAt = a.LastDeployedAt,
            CreatedAt = a.CreatedAt
        }));
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ApplicationWithDeploymentsDto>> GetApplication(int id)
    {
        var app = await _context.Applications
            .Include(a => a.Server)
            .Include(a => a.Project)
            .Include(a => a.Deployments.OrderByDescending(d => d.StartedAt))
            .FirstOrDefaultAsync(a => a.Id == id);
        
        if (app == null)
            return NotFound();
        
        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
        
        return new ApplicationWithDeploymentsDto
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            ServerId = app.ServerId,
            ServerName = app.Server.Name,
            ProjectId = app.ProjectId,
            ProjectName = app.Project.Name,
            DockerImage = app.DockerImage,
            Port = app.Port,
            Replicas = app.Replicas,
            EnvironmentVariables = app.EnvironmentVariables.ToDictionary(e => e.Key, e => e.Value),
            Domain = app.Domain,
            AdditionalDomains = app.AdditionalDomains,
            EnableHttps = app.EnableHttps,
            ForceHttps = app.ForceHttps,
            LetsEncryptEmail = app.LetsEncryptEmail,
            Status = latestDeployment?.Status ?? DeploymentStatus.Queued,
            ContainerId = latestDeployment?.ContainerId,
            ServiceId = latestDeployment?.ServiceId,
            LastDeployedAt = app.LastDeployedAt,
            CreatedAt = app.CreatedAt,
            Deployments = app.Deployments.Select(d => new DeploymentDto
            {
                Id = d.Id,
                Status = d.Status,
                ContainerId = d.ContainerId,
                ServiceId = d.ServiceId,
                StartedAt = d.StartedAt,
                FinishedAt = d.FinishedAt,
                ErrorMessage = d.ErrorMessage
            }).ToList()
        };
    }
    
    [HttpGet("servers/{serverId}")]
    public async Task<ActionResult<IEnumerable<ServerResponseDto>>> GetServers()
    {
        // Docker Swarm Best Practice: Services must be deployed to manager nodes only
        // Filter out worker nodes - they cannot accept service deployments
        var servers = await _context.Servers
            .Where(s => s.Type != ServerType.SwarmWorker)
            .ToListAsync();
        return Ok(servers.Select(s => new ServerResponseDto(s.Id, s.Name, s.Host, s.Port, s.Username, s.IsSwarm, s.Status.ToString())));
    }
    
    [HttpGet("projects")]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetProjects()
    {
        var projects = await _context.Projects.ToListAsync();
        return Ok(projects.Select(p => new ProjectDto(p.Id, p.Name, p.Description)));
    }
    
    [HttpPost]
    public async Task<ActionResult<ApplicationDto>> CreateApplication(CreateApplicationRequest request)
    {
        var server = await _context.Servers.FindAsync(request.ServerId);
        if (server == null)
            return BadRequest(new { error = "Server not found" });

        // Docker Swarm Best Practice: Services must be deployed to manager nodes
        if (server.Type == ServerType.SwarmWorker)
            return BadRequest(new { error = "Cannot deploy applications to worker nodes. Please select a manager node or standalone server." });

        var project = await _context.Projects.FindAsync(request.ProjectId);
        if (project == null)
            return BadRequest(new { error = "Project not found" });

        // Validate source type specific requirements
        var isGitDeployment = request.SourceType == "Git";
        if (isGitDeployment)
        {
            if (!request.GitProviderId.HasValue)
                return BadRequest(new { error = "Git provider is required for Git deployments" });
            if (string.IsNullOrWhiteSpace(request.GitRepository))
                return BadRequest(new { error = "Git repository is required for Git deployments" });
            if (string.IsNullOrWhiteSpace(request.GitBranch))
                return BadRequest(new { error = "Git branch is required for Git deployments" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Image))
                return BadRequest(new { error = "Docker image is required" });
        }

        // Check for duplicate application name on the same server
        var existingApp = await _context.Applications
            .FirstOrDefaultAsync(a => a.ServerId == request.ServerId && a.Name == request.Name);
        if (existingApp != null)
            return BadRequest(new { error = $"An application named '{request.Name}' already exists on this server. Please choose a different name." });

        var app = new Application
        {
            Uuid = Guid.NewGuid(),
            Name = request.Name,
            DockerImage = request.Image,
            ServerId = request.ServerId,
            ProjectId = request.ProjectId,
            SourceType = isGitDeployment ? ApplicationSourceType.Git : ApplicationSourceType.DockerImage,
            Replicas = request.Replicas ?? 1,
            Port = request.Port,
            Domain = request.Domain,
            AdditionalDomains = request.AdditionalDomains,
            EnableHttps = request.EnableHttps,
            ForceHttps = request.ForceHttps,
            LetsEncryptEmail = request.LetsEncryptEmail,
            GitProviderId = request.GitProviderId,
            GitRepository = request.GitRepository,
            GitBranch = request.GitBranch,
            Dockerfile = request.DockerfilePath ?? "Dockerfile",
            AutoDeployOnPush = request.AutoDeployOnPush,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Applications.Add(app);
        await _context.SaveChangesAsync();
        
        // Create deployment
        var deployment = new Deployment
        {
            Uuid = Guid.NewGuid(),
            ApplicationId = app.Id,
            Status = DeploymentStatus.Queued,
            StartedAt = DateTime.UtcNow
        };
        
        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync();
        
        // Deploy in background
        _ = Task.Run(async () => await DeployApplicationAsync(deployment.Id, request));
        
        return CreatedAtAction(nameof(GetApplication), new { id = app.Id }, new ApplicationDto
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            ServerId = app.ServerId,
            ServerName = server.Name,
            ProjectId = app.ProjectId,
            ProjectName = project.Name,
            DockerImage = app.DockerImage,
            Status = DeploymentStatus.Queued,
            CreatedAt = app.CreatedAt
        });
    }
    
    [HttpPost("{id}/scale")]
    public async Task<ActionResult> ScaleApplication(int id, [FromQuery] int replicas)
    {
        if (replicas < 1)
            return BadRequest(new { error = "Replicas must be at least 1" });
        
        var app = await _context.Applications
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Deployments.OrderByDescending(d => d.StartedAt))
            .FirstOrDefaultAsync(a => a.Id == id);
        
        if (app == null)
            return NotFound();
        
        // Only Swarm services can be scaled
        if (!app.Server.IsSwarm)
            return BadRequest(new { error = "Only Swarm services can be scaled" });
        
        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
        if (latestDeployment == null || string.IsNullOrEmpty(latestDeployment.ServiceId))
            return BadRequest(new { error = "No service found to scale" });
        
        try
        {
            _logger.LogInformation("Scaling application {AppName} to {Replicas} replicas", app.Name, replicas);
            
            // Use UpdateServiceAsync to change replica count
            var updateRequest = new UpdateServiceRequest(Replicas: replicas);
            await _dockerService.UpdateServiceAsync(app.Server, latestDeployment.ServiceId, updateRequest);
            
            // Update the application's replica count
            app.Replicas = replicas;
            await _context.SaveChangesAsync();
            
            return Ok(new { message = $"Application scaled to {replicas} replicas" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling application {AppId} to {Replicas} replicas", id, replicas);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("{id}/deploy")]
    public async Task<ActionResult> RedeployApplication(int id)
    {
        var app = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == id);
        
        if (app == null)
            return NotFound();
        
        // Create new deployment
        var deployment = new Deployment
        {
            Uuid = Guid.NewGuid(),
            ApplicationId = app.Id,
            Status = DeploymentStatus.Queued,
            StartedAt = DateTime.UtcNow
        };
        
        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync();
        
        // Redeploy in background
        var request = new CreateApplicationRequest(
            app.Name,
            app.ServerId,
            app.ProjectId,
            app.DockerImage ?? "",
            app.Replicas,
            app.EnvironmentVariables?.ToDictionary(e => e.Key, e => e.Value),
            null,
            app.Port,
            app.Domain,
            app.AdditionalDomains,
            app.EnableHttps,
            app.ForceHttps,
            app.LetsEncryptEmail
        );
        
        _ = Task.Run(async () => await DeployApplicationAsync(deployment.Id, request));
        
        return Ok(new { message = "Deployment queued", deploymentId = deployment.Id });
    }
    
    [HttpGet("{id}/logs")]
    public async Task<ActionResult> GetApplicationLogs(int id)
    {
        var app = await _context.Applications
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Deployments.OrderByDescending(d => d.StartedAt))
            .FirstOrDefaultAsync(a => a.Id == id);
        
        if (app == null)
            return NotFound();
        
        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
        
        if (latestDeployment == null)
            return BadRequest(new { error = "No deployments found" });
        
        try
        {
            Stream logStream;
            
            if (!string.IsNullOrEmpty(latestDeployment.ServiceId))
            {
                logStream = await _dockerService.GetServiceLogsAsync(app.Server, latestDeployment.ServiceId);
            }
            else if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
            {
                logStream = await _dockerService.GetContainerLogsAsync(app.Server, latestDeployment.ContainerId);
            }
            else
            {
                return BadRequest(new { error = "No container or service ID found" });
            }
            
            return File(logStream, "text/plain");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs for application {AppId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteApplication(int id)
    {
        var app = await _context.Applications
            .Include(a => a.Server)
            .Include(a => a.Deployments)
            .FirstOrDefaultAsync(a => a.Id == id);
        
        if (app == null)
            return NotFound();
        
        try
        {
            // Get latest deployment to find container/service IDs
            var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
            
            if (latestDeployment != null)
            {
                // Remove from Docker
                if (app.Server.IsSwarm && !string.IsNullOrEmpty(latestDeployment.ServiceId))
                {
                    try
                    {
                        await _dockerService.RemoveServiceAsync(app.Server, latestDeployment.ServiceId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove service {ServiceId}", latestDeployment.ServiceId);
                    }
                }
                else if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
                {
                    try
                    {
                        await _dockerService.RemoveContainerAsync(app.Server, latestDeployment.ContainerId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove container {ContainerId}", latestDeployment.ContainerId);
                    }
                }
            }
            
            _context.Applications.Remove(app);
            await _context.SaveChangesAsync();
            
            return Ok(new { message = "Application deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting application {AppId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("{id}/status")]
    public async Task<ActionResult<ApplicationStatusDto>> GetApplicationStatus(int id)
    {
        var app = await _context.Applications
            .Include(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(a => a.Deployments.OrderByDescending(d => d.StartedAt))
            .FirstOrDefaultAsync(a => a.Id == id);
        
        if (app == null)
            return NotFound();
        
        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
        
        if (latestDeployment == null)
        {
            return new ApplicationStatusDto
            {
                ApplicationId = app.Id,
                Status = "not-deployed",
                IsRunning = false
            };
        }
        
        try
        {
            bool isRunning = false;
            string? actualState = null;
            
            if (!string.IsNullOrEmpty(latestDeployment.ServiceId))
            {
                var serviceInfo = await _dockerService.InspectServiceAsync(app.Server, latestDeployment.ServiceId);
                isRunning = serviceInfo != null;
                actualState = isRunning ? "running" : "not-found";
            }
            else if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
            {
                var containerInfo = await _dockerService.InspectContainerAsync(app.Server, latestDeployment.ContainerId);
                isRunning = containerInfo?.State?.ToLower() == "running";
                actualState = containerInfo?.State ?? "not-found";
            }
            
            return new ApplicationStatusDto
            {
                ApplicationId = app.Id,
                Status = latestDeployment.Status.ToString().ToLower(),
                IsRunning = isRunning,
                ActualState = actualState,
                ContainerId = latestDeployment.ContainerId,
                ServiceId = latestDeployment.ServiceId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status for application {AppId}", id);
            return new ApplicationStatusDto
            {
                ApplicationId = app.Id,
                Status = "error",
                IsRunning = false,
                ActualState = "error: " + ex.Message
            };
        }
    }
    
    [HttpGet("orphans")]
    public async Task<ActionResult<OrphanedResourcesDto>> GetOrphanedResources([FromQuery] int? serverId = null)
    {
        try
        {
            var servers = serverId.HasValue
                ? await _context.Servers.Include(s => s.PrivateKey).Where(s => s.Id == serverId.Value).ToListAsync()
                : await _context.Servers.Include(s => s.PrivateKey).ToListAsync();
            
            var orphanedContainers = new List<OrphanedContainerDto>();
            var orphanedServices = new List<OrphanedServiceDto>();
            
            foreach (var server in servers)
            {
                try
                {
                    // Skip worker nodes - they can't manage resources independently
                    if (server.Type == ServerType.SwarmWorker)
                    {
                        _logger.LogDebug("Skipping worker node {ServerName} for orphan check", server.Name);
                        continue;
                    }
                    
                    // Check containers
                    var containers = await _dockerService.ListContainersAsync(server, true);
                    foreach (var container in containers)
                    {
                        try
                        {
                            var inspect = await _dockerService.InspectContainerAsync(server, container.Id);
                            if (inspect != null)
                            {
                                // Check if container has HostCraft labels
                                var isManaged = inspect.Labels.TryGetValue("hostcraft.managed", out var managed) && managed == "true";

                                if (isManaged)
                                {
                                    // Check if application exists in database
                                    inspect.Labels.TryGetValue("hostcraft.application.id", out var appIdStr);
                                    if (int.TryParse(appIdStr, out var appId))
                                    {
                                        var appExists = await _context.Applications.AnyAsync(a => a.Id == appId);
                                        if (!appExists)
                                        {
                                            orphanedContainers.Add(new OrphanedContainerDto
                                            {
                                                ContainerId = container.Id,
                                                ContainerName = container.Name,
                                                Image = container.Image,
                                                State = container.State,
                                                ServerId = server.Id,
                                                ServerName = server.Name,
                                                ApplicationId = appId,
                                                Labels = inspect.Labels
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        catch (Docker.DotNet.DockerContainerNotFoundException)
                        {
                            // Container was removed between list and inspect - skip it
                            _logger.LogDebug("Container {ContainerId} was removed during orphan check, skipping", container.Id);
                        }
                    }
                    
                    // Check services if Swarm Manager (only managers can list services)
                    if (server.Type == ServerType.SwarmManager)
                    {
                        var services = await _dockerService.ListServicesAsync(server);
                        foreach (var service in services)
                        {
                            var inspect = await _dockerService.InspectServiceAsync(server, service.Id);
                            if (inspect != null)
                            {
                                // Check if service has HostCraft labels
                                var isManaged = inspect.Labels.TryGetValue("hostcraft.managed", out var managed) && managed == "true";
                                
                                if (isManaged)
                                {
                                    // Check if application exists in database
                                    inspect.Labels.TryGetValue("hostcraft.application.id", out var appIdStr);
                                    if (int.TryParse(appIdStr, out var appId))
                                    {
                                        var appExists = await _context.Applications.AnyAsync(a => a.Id == appId);
                                        if (!appExists)
                                        {
                                            orphanedServices.Add(new OrphanedServiceDto
                                            {
                                                ServiceId = service.Id,
                                                ServiceName = service.Name,
                                                Image = service.Image,
                                                Replicas = service.Replicas,
                                                ServerId = server.Id,
                                                ServerName = server.Name,
                                                ApplicationId = appId,
                                                Labels = inspect.Labels
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking orphans on server {ServerId}", server.Id);
                }
            }
            
            return new OrphanedResourcesDto
            {
                OrphanedContainers = orphanedContainers,
                OrphanedServices = orphanedServices
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orphaned resources");
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("orphans/{containerId}/cleanup")]
    public async Task<IActionResult> CleanupOrphanedContainer(string containerId, [FromQuery] int serverId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.RemoveContainerAsync(server, containerId);
            return Ok(new { message = "Orphaned container removed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing orphaned container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("orphans/services/{serviceId}/cleanup")]
    public async Task<IActionResult> CleanupOrphanedService(string serviceId, [FromQuery] int serverId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.RemoveServiceAsync(server, serviceId);
            return Ok(new { message = "Orphaned service removed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing orphaned service {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    private async Task DeployApplicationAsync(int deploymentId, CreateApplicationRequest request)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApplicationsController>>();
        
        var deployment = await context.Deployments
            .Include(d => d.Application)
            .ThenInclude(a => a.Server)
                .ThenInclude(s => s.PrivateKey)
            .Include(d => d.Application)
            .ThenInclude(a => a.Deployments)
            .FirstOrDefaultAsync(d => d.Id == deploymentId);
        
        if (deployment == null) return;
        
        try
        {
            var app = deployment.Application;
            
            // Docker Swarm Best Practice: Services can only be created on manager nodes
            if (app.Server.IsSwarm && app.Server.Type == ServerType.SwarmWorker)
            {
                deployment.Status = DeploymentStatus.Failed;
                deployment.ErrorMessage = "Node is not part of a swarm. Applications must be deployed to manager nodes, not worker nodes.";
                deployment.FinishedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                logger.LogError("Attempted to deploy to worker node {ServerName}. Worker nodes cannot accept service deployments.", app.Server.Name);
                return;
            }
            
            deployment.Status = DeploymentStatus.Running;
            await context.SaveChangesAsync();
            
            logger.LogInformation("Deploying application {AppName} to server {ServerName}", app.Name, app.Server.Name);
            
            // Get the container/service name (consistent naming)
            var containerName = app.Name.ToLowerInvariant().Replace(" ", "-");
            
            // IMPORTANT: Stop and remove any existing container/service with the same name
            // This handles redeployments properly
            var previousDeployment = app.Deployments
                .Where(d => d.Id != deployment.Id)
                .OrderByDescending(d => d.StartedAt)
                .FirstOrDefault();
            
            if (previousDeployment != null)
            {
                logger.LogInformation("Found previous deployment, cleaning up old resources");
                
                if (!string.IsNullOrEmpty(previousDeployment.ServiceId))
                {
                    try
                    {
                        logger.LogInformation("Removing old service {ServiceId}", previousDeployment.ServiceId);
                        await dockerService.RemoveServiceAsync(app.Server, previousDeployment.ServiceId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to remove old service {ServiceId}, continuing", previousDeployment.ServiceId);
                    }
                }
                
                if (!string.IsNullOrEmpty(previousDeployment.ContainerId))
                {
                    try
                    {
                        logger.LogInformation("Stopping and removing old container {ContainerId}", previousDeployment.ContainerId);
                        await dockerService.StopContainerAsync(app.Server, previousDeployment.ContainerId);
                        await dockerService.RemoveContainerAsync(app.Server, previousDeployment.ContainerId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to remove old container {ContainerId}, continuing", previousDeployment.ContainerId);
                    }
                }
            }
            
            // Also try to remove by name in case there's an orphaned container with the same name
            try
            {
                var existingContainers = await dockerService.ListContainersAsync(app.Server, true);
                var existingContainer = existingContainers.FirstOrDefault(c =>
                    c.Name.TrimStart('/').Equals(containerName, StringComparison.OrdinalIgnoreCase));

                if (existingContainer != null)
                {
                    logger.LogInformation("Found existing container with name {ContainerName}, removing it", containerName);
                    try
                    {
                        await dockerService.StopContainerAsync(app.Server, existingContainer.Id);
                    }
                    catch { /* May already be stopped */ }
                    await dockerService.RemoveContainerAsync(app.Server, existingContainer.Id);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error checking for existing containers, continuing with deployment");
            }

            // Determine the Docker image to use
            string imageToUse;

            if (app.SourceType == ApplicationSourceType.Git)
            {
                // Git deployment: Clone repo, build image from Dockerfile
                logger.LogInformation("Git deployment: Cloning {Repository} branch {Branch}", app.GitRepository, app.GitBranch);

                var gitService = scope.ServiceProvider.GetRequiredService<IGitService>();

                // Get the Git provider with access token
                var gitProvider = await context.GitProviders.FindAsync(app.GitProviderId);
                if (gitProvider == null)
                {
                    throw new InvalidOperationException($"Git provider {app.GitProviderId} not found");
                }

                // Clone the repository
                var clonePath = Path.Combine(Path.GetTempPath(), "hostcraft-builds", app.Uuid.ToString());
                if (Directory.Exists(clonePath))
                {
                    Directory.Delete(clonePath, true);
                }
                Directory.CreateDirectory(clonePath);

                var cloneUrl = $"https://github.com/{app.GitRepository}.git";
                logger.LogInformation("Cloning {CloneUrl} to {ClonePath}", cloneUrl, clonePath);

                await gitService.CloneRepositoryAsync(cloneUrl, clonePath, app.GitBranch ?? "main", gitProvider.AccessToken);

                // Build the Docker image
                var imageName = $"{containerName}:{deployment.Id}";
                var dockerfilePath = app.Dockerfile ?? "Dockerfile";
                var buildContext = app.BuildContext ?? ".";

                logger.LogInformation("Building Docker image {ImageName} from {Dockerfile}", imageName, dockerfilePath);

                var buildRequest = new BuildImageRequest(
                    Dockerfile: dockerfilePath,
                    Context: clonePath,
                    Tag: imageName,
                    BuildArgs: null
                );

                await dockerService.BuildImageAsync(app.Server, buildRequest);

                imageToUse = imageName;

                // Clean up the clone directory
                try
                {
                    Directory.Delete(clonePath, true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up clone directory {ClonePath}", clonePath);
                }
            }
            else
            {
                // Docker Image deployment: Pull the specified image
                imageToUse = request.Image!;
                logger.LogInformation("Pulling Docker image {Image}", imageToUse);
                await dockerService.PullImageAsync(app.Server, imageToUse);
            }
            
            var envVars = request.EnvironmentVariables ?? new Dictionary<string, string>();
            
            // Add labels to track this application in Docker
            var labels = new Dictionary<string, string>
            {
                { "hostcraft.managed", "true" },
                { "hostcraft.application.id", app.Id.ToString() },
                { "hostcraft.application.uuid", app.Uuid.ToString() },
                { "hostcraft.application.name", app.Name },
                { "hostcraft.project.id", app.ProjectId.ToString() },
                { "hostcraft.deployment.id", deployment.Id.ToString() },
                { "hostcraft.server.id", app.ServerId.ToString() }
            };
            
            if (app.Server.IsSwarm)
            {
                // For Swarm, check if service exists and update it instead of creating new
                var existingServices = await dockerService.ListServicesAsync(app.Server);
                var existingService = existingServices.FirstOrDefault(s =>
                    s.Name.Equals(containerName, StringComparison.OrdinalIgnoreCase));

                if (existingService != null)
                {
                    // Update existing service
                    logger.LogInformation("Updating existing service {ServiceName}", containerName);
                    var updateRequest = new UpdateServiceRequest(
                        Image: imageToUse,
                        Replicas: request.Replicas,
                        EnvironmentVariables: envVars,
                        Labels: labels);

                    await dockerService.UpdateServiceAsync(app.Server, existingService.Id, updateRequest);
                    deployment.ServiceId = existingService.Id;
                }
                else
                {
                    // Deploy as new Swarm service
                    var serviceRequest = new CreateServiceRequest(
                        containerName,
                        imageToUse,
                        request.Replicas ?? 1,
                        envVars,
                        labels,
                        request.Networks ?? new List<string>(),
                        request.Port);

                    var serviceId = await dockerService.CreateServiceAsync(app.Server, serviceRequest);
                    deployment.ServiceId = serviceId;
                }
            }
            else
            {
                // Deploy as container
                var portBindings = request.Port.HasValue
                    ? new Dictionary<int, int> { { request.Port.Value, request.Port.Value } }
                    : null;

                var containerRequest = new CreateContainerRequest(
                    containerName,
                    imageToUse,
                    envVars,
                    labels,
                    request.Networks ?? new List<string>(),
                    portBindings);
                
                var containerId = await dockerService.CreateContainerAsync(app.Server, containerRequest);
                await dockerService.StartContainerAsync(app.Server, containerId);
                deployment.ContainerId = containerId;
            }
            
            deployment.Status = DeploymentStatus.Success;
            deployment.FinishedAt = DateTime.UtcNow;
            app.LastDeployedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            
            // Configure reverse proxy if enabled
            if (app.Server?.ProxyType != null && app.Server.ProxyType != ProxyType.None)
            {
                logger.LogInformation("Configuring {ProxyType} for application {AppName}", 
                    app.Server.ProxyType, app.Name);
                await proxyService.ConfigureApplicationAsync(app);
            }
            
            logger.LogInformation("Application {AppName} deployed successfully", app.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deploying application deployment {DeploymentId}", deploymentId);
            
            deployment.Status = DeploymentStatus.Failed;
            deployment.ErrorMessage = ex.Message;
            deployment.FinishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
    }
}

public record CreateApplicationRequest(
    string Name,
    int ServerId,
    int ProjectId,
    string? Image,
    int? Replicas = 1,
    Dictionary<string, string>? EnvironmentVariables = null,
    List<string>? Networks = null,
    int? Port = null,
    string? Domain = null,
    string? AdditionalDomains = null,
    bool EnableHttps = true,
    bool ForceHttps = true,
    string? LetsEncryptEmail = null,
    // Git deployment fields
    string? SourceType = "DockerImage",
    int? GitProviderId = null,
    string? GitRepository = null,
    string? GitBranch = null,
    string? DockerfilePath = "Dockerfile",
    bool AutoDeployOnPush = true);

public record ApplicationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string? DockerImage { get; init; }
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime? LastDeployedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public record ApplicationWithDeploymentsDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string? DockerImage { get; init; }
    public int? Port { get; init; }
    public int Replicas { get; init; }
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
    public string? Domain { get; init; }
    public string? AdditionalDomains { get; init; }
    public bool EnableHttps { get; init; }
    public bool ForceHttps { get; init; }
    public string? LetsEncryptEmail { get; init; }
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime? LastDeployedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<DeploymentDto> Deployments { get; init; } = new();
}

public record DeploymentDto
{
    public int Id { get; init; }
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
}

public record ServerResponseDto(int Id, string Name, string Host, int Port, string User, bool IsSwarm, string Status);
public record ProjectDto(int Id, string Name, string? Description);

public record ApplicationStatusDto
{
    public int ApplicationId { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public string? ActualState { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
}

public record OrphanedResourcesDto
{
    public List<OrphanedContainerDto> OrphanedContainers { get; init; } = new();
    public List<OrphanedServiceDto> OrphanedServices { get; init; } = new();
}

public record OrphanedContainerDto
{
    public string ContainerId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int ApplicationId { get; init; }
    public Dictionary<string, string> Labels { get; init; } = new();
}

public record OrphanedServiceDto
{
    public string ServiceId { get; init; } = string.Empty;
    public string ServiceName { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public int Replicas { get; init; }
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int ApplicationId { get; init; }
    public Dictionary<string, string> Labels { get; init; } = new();
}
