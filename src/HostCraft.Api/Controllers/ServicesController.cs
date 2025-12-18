using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/servers/{serverId}/[controller]")]
public class ServicesController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<ServicesController> _logger;
    
    public ServicesController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<ServicesController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceInfo>>> ListServices(int serverId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        if (!server.IsSwarm)
            return BadRequest(new { error = "Server is not in Swarm mode" });
        
        try
        {
            var services = await _dockerService.ListServicesAsync(server);
            return Ok(services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing services on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("{serviceId}")]
    public async Task<ActionResult<ServiceInspectInfo>> InspectService(
        int serverId,
        string serviceId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var service = await _dockerService.InspectServiceAsync(server, serviceId);
            if (service == null)
                return NotFound(new { error = "Service not found" });
            
            return Ok(service);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inspecting service {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost]
    public async Task<ActionResult<CreateServiceResponse>> CreateService(
        int serverId,
        [FromBody] CreateServiceRequest request)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        if (!server.IsSwarm)
            return BadRequest(new { error = "Server is not in Swarm mode" });
        
        try
        {
            // Pull image first
            _logger.LogInformation("Pulling image {Image} on server {ServerName}", request.Image, server.Name);
            await _dockerService.PullImageAsync(server, request.Image);
            
            // Ensure network exists if specified
            if (request.Networks != null && request.Networks.Any())
            {
                foreach (var networkName in request.Networks)
                {
                    await _dockerService.EnsureNetworkExistsAsync(server, networkName);
                }
            }
            
            // Create service
            var serviceId = await _dockerService.CreateServiceAsync(server, request);
            
            return CreatedAtAction(
                nameof(InspectService),
                new { serverId, serviceId },
                new CreateServiceResponse { Id = serviceId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service on server {ServerId}", serverId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPut("{serviceId}")]
    public async Task<IActionResult> UpdateService(
        int serverId,
        string serviceId,
        [FromBody] UpdateServiceRequest request)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            // Pull new image if specified
            if (!string.IsNullOrEmpty(request.Image))
            {
                _logger.LogInformation("Pulling image {Image} for service update", request.Image);
                await _dockerService.PullImageAsync(server, request.Image);
            }
            
            await _dockerService.UpdateServiceAsync(server, serviceId, request);
            return Ok(new { message = "Service updated" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating service {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpDelete("{serviceId}")]
    public async Task<IActionResult> RemoveService(int serverId, string serviceId)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.RemoveServiceAsync(server, serviceId);
            return Ok(new { message = "Service removed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing service {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpPost("{serviceId}/scale")]
    public async Task<IActionResult> ScaleService(
        int serverId,
        string serviceId,
        [FromBody] ScaleServiceRequest request)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            await _dockerService.UpdateServiceAsync(
                server,
                serviceId,
                new UpdateServiceRequest { Replicas = request.Replicas });
            
            return Ok(new { message = $"Service scaled to {request.Replicas} replicas" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling service {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("{serviceId}/logs")]
    public async Task<IActionResult> GetServiceLogs(
        int serverId,
        string serviceId,
        [FromQuery] bool follow = false)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        if (server == null)
            return NotFound(new { error = "Server not found" });
        
        try
        {
            var logsStream = await _dockerService.GetServiceLogsAsync(server, serviceId);
            
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
            _logger.LogError(ex, "Error getting logs for service {ServiceId}", serviceId);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public record CreateServiceResponse
{
    public string Id { get; init; } = string.Empty;
}

public record ScaleServiceRequest
{
    public int Replicas { get; init; }
}
