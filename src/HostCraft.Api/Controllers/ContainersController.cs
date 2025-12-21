using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
[Authorize]
public class ContainersController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<ContainersController> _logger;
    
    public ContainersController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<ContainersController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ContainerInfo>>> ListContainers(
        int serverId,
        [FromQuery] bool all = true)
    {
        var server = await _context.Servers.FindAsync(serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var containers = await _dockerService.ListContainersAsync(server, all);
            return Ok(containers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing containers on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("{containerId}")]
    public async Task<ActionResult<ContainerInspectInfo>> InspectContainer(
        int serverId,
        string containerId)
    {
        var server = await _context.Servers.FindAsync(serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var container = await _dockerService.InspectContainerAsync(server, containerId);
            if (container == null)
                return NotFound(new { error = "Container not found" });
            
            return Ok(container);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inspecting container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<ActionResult<CreateContainerResponse>> CreateContainer(
        int serverId,
        [FromBody] CreateContainerRequest request)
    {
        var server = await _context.Servers.FindAsync(serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            // Pull image first if needed
            _logger.LogInformation("Pulling image {Image} on server {ServerName}", request.Image, server.Name);
            await _dockerService.PullImageAsync(server, request.Image);
            
            // Create container
            var containerId = await _dockerService.CreateContainerAsync(server, request);
            
            // Auto-start the container
            await _dockerService.StartContainerAsync(server, containerId);
            
            return CreatedAtAction(
                nameof(InspectContainer),
                new { serverId, containerId },
                new CreateContainerResponse { Id = containerId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating container on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("{containerId}/start")]
    public async Task<IActionResult> StartContainer(int serverId, string containerId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.StartContainerAsync(server, containerId);
            return Ok(new { message = "Container started" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("{containerId}/stop")]
    public async Task<IActionResult> StopContainer(int serverId, string containerId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.StopContainerAsync(server, containerId);
            return Ok(new { message = "Container stopped" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("{containerId}/restart")]
    public async Task<IActionResult> RestartContainer(int serverId, string containerId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.StopContainerAsync(server, containerId);
            await Task.Delay(1000); // Brief pause
            await _dockerService.StartContainerAsync(server, containerId);
            return Ok(new { message = "Container restarted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restarting container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpDelete("{containerId}")]
    public async Task<IActionResult> RemoveContainer(
        int serverId,
        string containerId,
        [FromQuery] bool force = false)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            if (force)
            {
                try
                {
                    await _dockerService.StopContainerAsync(server, containerId);
                }
                catch
                {
                    // Ignore stop errors when forcing
                }
            }
            
            await _dockerService.RemoveContainerAsync(server, containerId);
            return Ok(new { message = "Container removed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("{containerId}/logs")]
    public async Task<IActionResult> GetContainerLogs(
        int serverId,
        string containerId,
        [FromQuery] bool follow = false)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var logsStream = await _dockerService.GetContainerLogsAsync(server, containerId);
            
            if (follow)
            {
                return new FileStreamResult(logsStream, "text/plain");
            }
            else
            {
                using var reader = new StreamReader(logsStream);
                var logs = await reader.ReadToEndAsync();
                return Ok(new { logs });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs for container {ContainerId}", containerId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record CreateContainerResponse
{
    public string Id { get; init; } = string.Empty;
}
