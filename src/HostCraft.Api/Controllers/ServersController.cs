using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ServersController : ControllerBase
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ILogger<ServersController> _logger;
    
    public ServersController(
        HostCraftDbContext context,
        IDockerService dockerService,
        ILogger<ServersController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _logger = logger;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Server>>> GetServers()
    {
        return await _context.Servers.ToListAsync();
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<Server>> GetServer(int id)
    {
        var server = await _context.Servers.FindAsync(id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        return server;
    }
    
    [HttpPost]
    public async Task<ActionResult<Server>> CreateServer(CreateServerRequest request)
    {
        var server = new Server
        {
            Name = request.Name,
            Host = request.Host,
            Port = request.Port,
            Username = request.Username,
            Type = request.Type,
            ProxyType = request.ProxyType,
            Status = ServerStatus.Validating,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Servers.Add(server);
        await _context.SaveChangesAsync();
        
        // Validate connection in background
        _ = Task.Run(async () => await ValidateServerAsync(server.Id));
        
        return CreatedAtAction(nameof(GetServer), new { id = server.Id }, server);
    }
    
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateServer(int id, UpdateServerRequest request)
    {
        var server = await _context.Servers.FindAsync(id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        if (!string.IsNullOrEmpty(request.Name))
            server.Name = request.Name;
        
        if (!string.IsNullOrEmpty(request.Host))
            server.Host = request.Host;
        
        if (request.Port.HasValue)
            server.Port = request.Port.Value;
        
        if (request.Type.HasValue)
            server.Type = request.Type.Value;
        
        if (request.ProxyType.HasValue)
            server.ProxyType = request.ProxyType.Value;
        
        await _context.SaveChangesAsync();
        
        return NoContent();
    }
    
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteServer(int id)
    {
        var server = await _context.Servers.FindAsync(id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        _context.Servers.Remove(server);
        await _context.SaveChangesAsync();
        
        return NoContent();
    }
    
    [HttpPost("{id}/validate")]
    public async Task<ActionResult<ServerValidationResult>> ValidateServer(int id)
    {
        var server = await _context.Servers.FindAsync(id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        try
        {
            var isValid = await _dockerService.ValidateConnectionAsync(server);
            
            if (isValid)
            {
                var systemInfo = await _dockerService.GetSystemInfoAsync(server);
                server.Status = ServerStatus.Online;
                server.LastHealthCheck = DateTime.UtcNow;
                
                // Update server type if Swarm is detected
                if (systemInfo.SwarmActive && server.Type == ServerType.Standalone)
                {
                    _logger.LogInformation("Swarm detected on server {ServerName}, updating type", server.Name);
                    server.Type = ServerType.SwarmManager;
                }
                
                await _context.SaveChangesAsync();
                
                return new ServerValidationResult
                {
                    IsValid = true,
                    SystemInfo = systemInfo,
                    Message = "Server is online and accessible"
                };
            }
            else
            {
                server.Status = ServerStatus.Offline;
                await _context.SaveChangesAsync();
                
                return new ServerValidationResult
                {
                    IsValid = false,
                    Message = "Cannot connect to Docker daemon"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating server {ServerId}", id);
            server.Status = ServerStatus.Error;
            await _context.SaveChangesAsync();
            
            return new ServerValidationResult
            {
                IsValid = false,
                Message = $"Error: {ex.Message}"
            };
        }
    }
    
    [HttpGet("{id}/containers")]
    public async Task<ActionResult<IEnumerable<ContainerInfo>>> GetContainers(int id)
    {
        var server = await _context.Servers.FindAsync(id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        try
        {
            var containers = await _dockerService.ListContainersAsync(server);
            return Ok(containers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing containers for server {ServerId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    [HttpGet("{id}/services")]
    public async Task<ActionResult<IEnumerable<ServiceInfo>>> GetServices(int id)
    {
        var server = await _context.Servers.FindAsync(id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        if (!server.IsSwarm)
        {
            return BadRequest(new { error = "Server is not in Swarm mode" });
        }
        
        try
        {
            var services = await _dockerService.ListServicesAsync(server);
            return Ok(services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing services for server {ServerId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }
    
    private async Task ValidateServerAsync(int serverId)
    {
        try
        {
            await Task.Delay(1000); // Small delay
            
            var server = await _context.Servers.FindAsync(serverId);
            if (server == null) return;
            
            var isValid = await _dockerService.ValidateConnectionAsync(server);
            server.Status = isValid ? ServerStatus.Online : ServerStatus.Offline;
            server.LastHealthCheck = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background validation failed for server {ServerId}", serverId);
        }
    }
}

public record CreateServerRequest(
    string Name,
    string Host,
    int Port = 22,
    string Username = "root",
    ServerType Type = ServerType.Standalone,
    ProxyType ProxyType = ProxyType.None);

public record UpdateServerRequest(
    string? Name = null,
    string? Host = null,
    int? Port = null,
    ServerType? Type = null,
    ProxyType? ProxyType = null);

public record ServerValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public SystemInfo? SystemInfo { get; init; }
}
