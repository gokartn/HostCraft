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
    
    public ApplicationsController(
        HostCraftDbContext context,
        IDockerService dockerService,
        IProxyService proxyService,
        ILogger<ApplicationsController> _logger)
    {
        _context = context;
        _dockerService = dockerService;
        _proxyService = proxyService;
        this._logger = _logger;
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
        var servers = await _context.Servers.ToListAsync();
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
        
        var project = await _context.Projects.FindAsync(request.ProjectId);
        if (project == null)
            return BadRequest(new { error = "Project not found" });
        
        var app = new Application
        {
            Uuid = Guid.NewGuid(),
            Name = request.Name,
            DockerImage = request.Image,
            ServerId = request.ServerId,
            ProjectId = request.ProjectId,
            SourceType = ApplicationSourceType.DockerImage,
            Replicas = request.Replicas ?? 1,
            Port = request.Port,
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
    
    private async Task DeployApplicationAsync(int deploymentId, CreateApplicationRequest request)
    {
        var deployment = await _context.Deployments
            .Include(d => d.Application)
            .ThenInclude(a => a.Server)
            .FirstOrDefaultAsync(d => d.Id == deploymentId);
        
        if (deployment == null) return;
        
        try
        {
            var app = deployment.Application;
            deployment.Status = DeploymentStatus.Running;
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Deploying application {AppName} to server {ServerName}", app.Name, app.Server.Name);
            
            // Pull image
            await _dockerService.PullImageAsync(app.Server, request.Image);
            
            var envVars = request.EnvironmentVariables ?? new Dictionary<string, string>();
            
            if (app.Server.IsSwarm)
            {
                // Deploy as Swarm service
                var serviceRequest = new CreateServiceRequest(
                    app.Name.ToLowerInvariant().Replace(" ", "-"),
                    request.Image,
                    request.Replicas ?? 1,
                    envVars,
                    new Dictionary<string, string>(), // Labels
                    request.Networks ?? new List<string>(),
                    request.Port);
                
                var serviceId = await _dockerService.CreateServiceAsync(app.Server, serviceRequest);
                deployment.ServiceId = serviceId;
            }
            else
            {
                // Deploy as container
                var portBindings = request.Port.HasValue
                    ? new Dictionary<string, string> { { request.Port.Value.ToString(), request.Port.Value.ToString() } }
                    : null;
                
                var containerRequest = new CreateContainerRequest(
                    app.Name.ToLowerInvariant().Replace(" ", "-"),
                    request.Image,
                    envVars,
                    null, // Labels
                    request.Networks ?? new List<string>(),
                    null); // Port bindings - using null for now, will implement properly later
                
                var containerId = await _dockerService.CreateContainerAsync(app.Server, containerRequest);
                await _dockerService.StartContainerAsync(app.Server, containerId);
                deployment.ContainerId = containerId;
            }
            
            deployment.Status = DeploymentStatus.Success;
            deployment.FinishedAt = DateTime.UtcNow;
            app.LastDeployedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            
            // Configure reverse proxy if enabled
            if (app.Server?.ProxyType != ProxyType.None)
            {
                _logger.LogInformation("Configuring {ProxyType} for application {AppName}", 
                    app.Server.ProxyType, app.Name);
                await _proxyService.ConfigureApplicationAsync(app);
            }
            
            _logger.LogInformation("Application {AppName} deployed successfully", app.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying application deployment {DeploymentId}", deploymentId);
            
            deployment.Status = DeploymentStatus.Failed;
            deployment.ErrorMessage = ex.Message;
            deployment.FinishedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}

public record CreateApplicationRequest(
    string Name,
    int ServerId,
    int ProjectId,
    string Image,
    int? Replicas = 1,
    Dictionary<string, string>? EnvironmentVariables = null,
    List<string>? Networks = null,
    int? Port = null);

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
