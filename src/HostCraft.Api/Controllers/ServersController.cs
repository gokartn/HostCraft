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
    private readonly IProxyService _proxyService;
    private readonly ILogger<ServersController> _logger;
    
    public ServersController(
        HostCraftDbContext context,
        IDockerService dockerService,
        IProxyService proxyService,
        ILogger<ServersController> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _proxyService = proxyService;
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
        // Create PrivateKey entity if provided
        PrivateKey? privateKey = null;
        if (!string.IsNullOrEmpty(request.PrivateKeyContent))
        {
            privateKey = new PrivateKey
            {
                Name = $"{request.Name} SSH Key",
                KeyData = request.PrivateKeyContent,
                CreatedAt = DateTime.UtcNow
            };
            _context.PrivateKeys.Add(privateKey);
        }
        
        // Find or create Region if provided
        Region? region = null;
        if (!string.IsNullOrEmpty(request.Region))
        {
            // Try to find existing region by name or code
            region = await _context.Regions
                .FirstOrDefaultAsync(r => r.Name == request.Region || r.Code == request.Region);
            
            // Create new region if not found
            if (region == null)
            {
                region = new Region
                {
                    Name = request.Region,
                    Code = request.Region.ToLower().Replace(" ", "-"),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Regions.Add(region);
            }
        }
        
        var server = new Server
        {
            Name = request.Name,
            Host = request.Host,
            Port = request.Port,
            Username = request.User,
            Type = request.Type,
            ProxyType = request.ProxyType,
            Status = ServerStatus.Validating,
            CreatedAt = DateTime.UtcNow,
            PrivateKey = privateKey,
            Region = region
        };
        
        _context.Servers.Add(server);
        await _context.SaveChangesAsync();
        
        // Validate connection and deploy proxy in background
        _ = Task.Run(async () => 
        {
            await ValidateServerAsync(server.Id);
            
            // Deploy reverse proxy if configured
            if (server.ProxyType != ProxyType.None)
            {
                var serverWithKey = await _context.Servers
                    .Include(s => s.PrivateKey)
                    .FirstOrDefaultAsync(s => s.Id == server.Id);
                    
                if (serverWithKey != null && serverWithKey.Status == ServerStatus.Online)
                {
                    _logger.LogInformation("Deploying {ProxyType} on server {ServerName}", 
                        serverWithKey.ProxyType, serverWithKey.Name);
                    
                    await _proxyService.EnsureProxyDeployedAsync(serverWithKey);
                }
            }
        });
        
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
        
        if (!string.IsNullOrEmpty(request.User))
            server.Username = request.User;
        
        // Update Region
        if (!string.IsNullOrEmpty(request.Region))
        {
            var region = await _context.Regions
                .FirstOrDefaultAsync(r => r.Name == request.Region || r.Code == request.Region);
            
            if (region == null)
            {
                region = new Region
                {
                    Name = request.Region,
                    Code = request.Region.ToLower().Replace(" ", "-"),
                    CreatedAt = DateTime.UtcNow
                };
                _context.Regions.Add(region);
            }
            
            server.Region = region;
        }
        
        // Update PrivateKey
        if (!string.IsNullOrEmpty(request.PrivateKeyContent))
        {
            // Remove old key if exists
            if (server.PrivateKey != null)
            {
                _context.PrivateKeys.Remove(server.PrivateKey);
            }
            
            // Create new key
            var privateKey = new PrivateKey
            {
                Name = $"{server.Name} SSH Key",
                KeyData = request.PrivateKeyContent,
                CreatedAt = DateTime.UtcNow
            };
            _context.PrivateKeys.Add(privateKey);
            server.PrivateKey = privateKey;
        }
        
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
    
    [HttpPost("validate")]
    public async Task<ActionResult<ServerValidationResult>> ValidateServerConnection(CreateServerRequest request)
    {
        try
        {
            // Create temporary PrivateKey object for validation
            PrivateKey? tempKey = null;
            if (!string.IsNullOrEmpty(request.PrivateKeyContent))
            {
                tempKey = new PrivateKey
                {
                    Name = "Temp Validation Key",
                    KeyData = request.PrivateKeyContent
                };
            }
            
            // Create a temporary server object for validation (don't save it)
            var tempServer = new Server
            {
                Name = request.Name,
                Host = request.Host,
                Port = request.Port,
                Username = request.User,
                PrivateKey = tempKey,
                Type = request.Type
            };
            
            var isValid = await _dockerService.ValidateConnectionAsync(tempServer);
            
            if (isValid)
            {
                var systemInfo = await _dockerService.GetSystemInfoAsync(tempServer);
                
                return new ServerValidationResult
                {
                    IsValid = true,
                    SystemInfo = systemInfo,
                    Message = "Connection successful! Docker daemon is accessible."
                };
            }
            else
            {
                return new ServerValidationResult
                {
                    IsValid = false,
                    Message = "Cannot connect to Docker daemon. Check credentials and network access."
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating server connection");
            
            return new ServerValidationResult
            {
                IsValid = false,
                Message = $"Connection failed: {ex.Message}"
            };
        }
    }
    
    [HttpPost("{id}/validate")]
    public async Task<ActionResult<ServerValidationResult>> ValidateExistingServer(int id)
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
    string User = "root",
    string? Region = null,
    string? PrivateKeyContent = null,
    ServerType Type = ServerType.Standalone,
    ProxyType ProxyType = ProxyType.None);

public record UpdateServerRequest(
    string? Name = null,
    string? Host = null,
    int? Port = null,
    string? User = null,
    string? Region = null,
    string? PrivateKeyContent = null,
    ServerType? Type = null,
    ProxyType? ProxyType = null);

public record ServerValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public SystemInfo? SystemInfo { get; init; }
}
