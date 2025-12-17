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
    private readonly IServiceScopeFactory _scopeFactory;
    
    public ServersController(
        HostCraftDbContext context,
        IDockerService dockerService,
        IProxyService proxyService,
        ILogger<ServersController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _dockerService = dockerService;
        _proxyService = proxyService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }
    
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Server>>> GetServers()
    {
        var servers = await _context.Servers
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .AsNoTracking()
            .ToListAsync();
            
        // Clear navigation properties that might cause serialization issues
        foreach (var server in servers)
        {
            server.Applications = new List<Application>();
        }
            
        return servers;
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<Server>> GetServer(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .Include(s => s.Region)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        return server;
    }
    
    [HttpPost]
    public async Task<ActionResult<Server>> CreateServer(CreateServerRequest request)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { error = "Server name is required" });
            }
            
            if (string.IsNullOrWhiteSpace(request.Host))
            {
                return BadRequest(new { error = "Host/IP address is required" });
            }
            
            if (string.IsNullOrWhiteSpace(request.User))
            {
                return BadRequest(new { error = "Username is required" });
            }
            
            if (string.IsNullOrWhiteSpace(request.PrivateKeyContent))
            {
                return BadRequest(new { error = "SSH private key is required" });
            }
            
            // Validate private key format
            if (!request.PrivateKeyContent.Contains("BEGIN") || !request.PrivateKeyContent.Contains("PRIVATE KEY"))
            {
                return BadRequest(new { error = "Invalid SSH private key format. Key must contain BEGIN and PRIVATE KEY markers." });
            }
            
            // Check for duplicate server name
            var existingServer = await _context.Servers
                .FirstOrDefaultAsync(s => s.Name == request.Name);
            if (existingServer != null)
            {
                return BadRequest(new { error = $"A server with the name '{request.Name}' already exists" });
            }
        
            // Create PrivateKey entity if provided
            PrivateKey? privateKey = null;
            if (!string.IsNullOrEmpty(request.PrivateKeyContent))
            {
                // Make the name unique with a timestamp to avoid conflicts
                privateKey = new PrivateKey
                {
                    Name = $"{request.Name} SSH Key - {DateTime.UtcNow:yyyyMMddHHmmss}",
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
            
            var serverId = server.Id;
            var proxyType = server.ProxyType;
            
            // Validate connection and deploy proxy in background with proper scope
            _ = Task.Run(async () => 
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedContext = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
                var scopedDockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
                var scopedProxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
                
                try
                {
                    await Task.Delay(1000);
                    
                    var serverToValidate = await scopedContext.Servers
                        .Include(s => s.PrivateKey)
                        .FirstOrDefaultAsync(s => s.Id == serverId);
                    
                    if (serverToValidate == null) return;
                    
                    var isValid = await scopedDockerService.ValidateConnectionAsync(serverToValidate);
                    serverToValidate.Status = isValid ? ServerStatus.Online : ServerStatus.Offline;
                    serverToValidate.LastHealthCheck = DateTime.UtcNow;
                    
                    await scopedContext.SaveChangesAsync();
                    
                    // Deploy reverse proxy if configured and online
                    if (proxyType != ProxyType.None && serverToValidate.Status == ServerStatus.Online)
                    {
                        _logger.LogInformation("Deploying {ProxyType} on server {ServerName}", 
                            proxyType, serverToValidate.Name);
                        
                        await scopedProxyService.EnsureProxyDeployedAsync(serverToValidate);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background validation failed for server {ServerId}", serverId);
                    
                    var serverToUpdate = await scopedContext.Servers.FindAsync(serverId);
                    if (serverToUpdate != null)
                    {
                        serverToUpdate.Status = ServerStatus.Error;
                        await scopedContext.SaveChangesAsync();
                    }
                }
            });
            
            return CreatedAtAction(nameof(GetServer), new { id = server.Id }, server);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database error while creating server");
            return BadRequest(new { error = "Database error: " + ex.InnerException?.Message ?? ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while creating server");
            return StatusCode(500, new { error = "Server error: " + ex.Message });
        }
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
            
            // Create new key with unique name
            var privateKey = new PrivateKey
            {
                Name = $"{server.Name} SSH Key - {DateTime.UtcNow:yyyyMMddHHmmss}",
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
        
        // Set to validating before background check
        server.Status = ServerStatus.Validating;
        
        await _context.SaveChangesAsync();
        
        // Trigger background validation after update
        var serverId = server.Id;
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var scopedContext = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
            var scopedDockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
            
            try
            {
                await Task.Delay(1000);
                
                var serverToValidate = await scopedContext.Servers
                    .Include(s => s.PrivateKey)
                    .FirstOrDefaultAsync(s => s.Id == serverId);
                
                if (serverToValidate == null) return;
                
                _logger.LogInformation("Re-validating updated server {ServerName} ({ServerId})", serverToValidate.Name, serverId);
                
                var isValid = await scopedDockerService.ValidateConnectionAsync(serverToValidate);
                serverToValidate.Status = isValid ? ServerStatus.Online : ServerStatus.Offline;
                serverToValidate.LastHealthCheck = DateTime.UtcNow;
                
                // Detect Swarm mode if connection is valid
                if (isValid)
                {
                    try
                    {
                        var systemInfo = await scopedDockerService.GetSystemInfoAsync(serverToValidate);
                        if (systemInfo.SwarmActive && serverToValidate.Type == ServerType.Standalone)
                        {
                            _logger.LogInformation("Swarm detected on updated server {ServerName}, updating type to SwarmManager", serverToValidate.Name);
                            serverToValidate.Type = ServerType.SwarmManager;
                            serverToValidate.IsSwarmManager = true;
                        }
                    }
                    catch (Exception swarmEx)
                    {
                        _logger.LogWarning(swarmEx, "Failed to detect Swarm on server {ServerName}", serverToValidate.Name);
                    }
                }
                
                await scopedContext.SaveChangesAsync();
                
                _logger.LogInformation("Server {ServerName} validation result: {Status}", serverToValidate.Name, serverToValidate.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background re-validation failed for server {ServerId}", serverId);
                
                var serverToUpdate = await scopedContext.Servers.FindAsync(serverId);
                if (serverToUpdate != null)
                {
                    serverToUpdate.Status = ServerStatus.Error;
                    await scopedContext.SaveChangesAsync();
                }
            }
        });
        
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
    
    [HttpPost("configure-localhost")]
    public async Task<ActionResult<Server>> ConfigureLocalhostServer()
    {
        // Check if localhost server already exists
        var existingLocalhost = await _context.Servers
            .Include(s => s.Region)
            .FirstOrDefaultAsync(s => s.Host == "localhost" || s.Host == "127.0.0.1");
        
        if (existingLocalhost != null)
        {
            return Ok(existingLocalhost);
        }
        
        // Check if Docker is available
        if (!IsDockerAvailable())
        {
            return BadRequest(new { message = "Docker is not available on this system. Please install Docker first." });
        }
        
        // Get primary region or create one
        var primaryRegion = await _context.Regions.FirstOrDefaultAsync(r => r.IsPrimary);
        
        if (primaryRegion == null)
        {
            primaryRegion = new Region
            {
                Name = "Local",
                Code = "local",
                IsPrimary = true,
                Priority = 1,
                CreatedAt = DateTime.UtcNow
            };
            _context.Regions.Add(primaryRegion);
            await _context.SaveChangesAsync();
        }
        
        // Create localhost server
        var localhostServer = new Server
        {
            Name = "Local Server",
            Host = "localhost",
            Port = 22, // Not actually used for localhost
            Username = Environment.UserName,
            Status = ServerStatus.Online,
            Type = ServerType.Standalone,
            ProxyType = ProxyType.None,
            RegionId = primaryRegion.Id,
            PrivateKeyId = null,
            CreatedAt = DateTime.UtcNow
        };
        
        _context.Servers.Add(localhostServer);
        await _context.SaveChangesAsync();
        
        // Reload with relationships
        var server = await _context.Servers
            .Include(s => s.Region)
            .FirstOrDefaultAsync(s => s.Id == localhostServer.Id);
        
        return Ok(server);
    }
    
    private static bool IsDockerAvailable()
    {
        try
        {
            // Check if Docker socket exists (works inside Docker containers)
            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            var dockerSocket = isWindows ? "//./pipe/docker_engine" : "/var/run/docker.sock";
            
            if (isWindows)
            {
                // On Windows, check if named pipe exists (difficult to check directly)
                // Try to run docker command if available
                try
                {
                    var processStartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = "info",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    
                    using var process = System.Diagnostics.Process.Start(processStartInfo);
                    if (process != null)
                    {
                        process.WaitForExit(5000);
                        return process.ExitCode == 0;
                    }
                }
                catch
                {
                    // Fall through to return false
                }
                return false;
            }
            else
            {
                // On Linux/Unix, check if Docker socket file exists
                return System.IO.File.Exists(dockerSocket);
            }
        }
        catch
        {
            return false;
        }
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
                return BadRequest(new ServerValidationResult
                {
                    IsValid = false,
                    Message = "Cannot connect to Docker daemon. Check credentials and network access."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating server connection");
            
            return BadRequest(new ServerValidationResult
            {
                IsValid = false,
                Message = $"Connection failed: {ex.Message}"
            });
        }
    }
    
    [HttpPost("{id}/validate")]
    public async Task<ActionResult<ServerValidationResult>> ValidateExistingServer(int id)
    {
        // Load server with PrivateKey for SSH authentication
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        try
        {
            _logger.LogInformation("Manually validating server {ServerId} - {ServerName}", id, server.Name);
            var isValid = await _dockerService.ValidateConnectionAsync(server);
            
            if (isValid)
            {
                var systemInfo = await _dockerService.GetSystemInfoAsync(server);
                server.Status = ServerStatus.Online;
                server.LastHealthCheck = DateTime.UtcNow;
                
                // Update server type if Swarm is detected
                if (systemInfo.SwarmActive && server.Type == ServerType.Standalone)
                {
                    _logger.LogInformation("Swarm detected on server {ServerName}, updating type to SwarmManager", server.Name);
                    server.Type = ServerType.SwarmManager;
                    server.IsSwarmManager = true;
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

    [HttpGet("{id}/public-key")]
    public async Task<ActionResult<object>> GetPublicKey(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound();
        }
        
        if (server.PrivateKey == null)
        {
            return NotFound(new { error = "No private key configured for this server" });
        }
        
        try
        {
            // Extract public key from private key using SSH.NET
            var keyFile = new Renci.SshNet.PrivateKeyFile(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(server.PrivateKey.KeyData)),
                server.PrivateKey.Passphrase
            );
            
            // Get the public key in OpenSSH format
            using var publicKeyStream = new MemoryStream();
            using var writer = new BinaryWriter(publicKeyStream);
            
            // Write algorithm name
            var algorithm = System.Text.Encoding.ASCII.GetBytes("ssh-rsa");
            writer.Write(algorithm.Length);
            writer.Write(algorithm);
            
            // Write exponent and modulus for RSA key
            var key = keyFile.Key as Renci.SshNet.Security.RsaKey;
            if (key != null)
            {
                var exponent = key.Exponent.ToByteArray().Reverse().SkipWhile(b => b == 0).Reverse().ToArray();
                writer.Write(exponent.Length);
                writer.Write(exponent);
                
                var modulus = key.Modulus.ToByteArray().Reverse().SkipWhile(b => b == 0).Reverse().ToArray();
                writer.Write(modulus.Length);
                writer.Write(modulus);
            }
            
            var publicKeyBytes = publicKeyStream.ToArray();
            var publicKey = $"ssh-rsa {Convert.ToBase64String(publicKeyBytes)} HostCraft-{server.Name}";
            
            return Ok(new 
            { 
                publicKey,
                instruction = $"Copy this public key and add it to ~/.ssh/authorized_keys on {server.Host}",
                manualCommand = $"ssh {server.Username}@{server.Host} -p {server.Port}\nmkdir -p ~/.ssh\necho '{publicKey}' >> ~/.ssh/authorized_keys\nchmod 600 ~/.ssh/authorized_keys\nchmod 700 ~/.ssh"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting public key for server {ServerId}", id);
            return StatusCode(500, new { error = ex.Message });
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
