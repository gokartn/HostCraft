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
    private readonly ISshService _sshService;
    private readonly ILogger<ServersController> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    
    public ServersController(
        HostCraftDbContext context,
        IDockerService dockerService,
        IProxyService proxyService,
        ISshService sshService,
        ILogger<ServersController> logger,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _dockerService = dockerService;
        _proxyService = proxyService;
        _sshService = sshService;
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
            
            // Check if this is a localhost connection
            var isLocalhost = request.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
                             request.Host == "127.0.0.1" || 
                             request.Host == "::1";
            
            // SSH private key is required for remote servers, but optional for localhost
            if (!isLocalhost && string.IsNullOrWhiteSpace(request.PrivateKeyContent))
            {
                return BadRequest(new { error = "SSH private key is required for remote servers" });
            }
            
            // Validate private key format if provided
            if (!string.IsNullOrWhiteSpace(request.PrivateKeyContent) && 
                (!request.PrivateKeyContent.Contains("BEGIN") || !request.PrivateKeyContent.Contains("PRIVATE KEY")))
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
            var serverType = server.Type;
            
            // Validate connection and deploy proxy in background with proper scope
            _ = Task.Run(async () => 
            {
                using var scope = _scopeFactory.CreateScope();
                var scopedContext = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
                var scopedDockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
                var scopedProxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
                var scopedSshService = scope.ServiceProvider.GetRequiredService<ISshService>();
                
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
                    
                    // If server is online and marked as SwarmWorker, try to join it to an existing swarm
                    if (serverType == ServerType.SwarmWorker && serverToValidate.Status == ServerStatus.Online)
                    {
                        _logger.LogInformation("Server {ServerName} marked as SwarmWorker, attempting to join swarm", serverToValidate.Name);
                        
                        // Wait a bit for swarm detection to complete on other servers
                        await Task.Delay(2000);
                        
                        // Find an active swarm manager (try multiple times due to race conditions)
                        Server? swarmManager = null;
                        for (int retry = 0; retry < 3 && swarmManager == null; retry++)
                        {
                            if (retry > 0) await Task.Delay(2000);
                            swarmManager = await scopedContext.Servers
                                .Include(s => s.PrivateKey)
                                .FirstOrDefaultAsync(s => s.IsSwarmManager && s.Status == ServerStatus.Online);
                        }
                        
                        if (swarmManager != null)
                        {
                            try
                            {
                                // Check if worker is already part of a swarm - if yes, leave it first
                                var workerSystemInfo = await scopedDockerService.GetSystemInfoAsync(serverToValidate);
                                if (workerSystemInfo.SwarmActive)
                                {
                                    _logger.LogInformation("Server {ServerName} is already part of a swarm, leaving it first", 
                                        serverToValidate.Name);
                                    
                                    try
                                    {
                                        await scopedDockerService.LeaveSwarmAsync(serverToValidate, force: true);
                                        _logger.LogInformation("Successfully left old swarm");
                                        
                                        // Wait a moment for the leave operation to complete
                                        await Task.Delay(2000);
                                    }
                                    catch (Exception leaveEx)
                                    {
                                        _logger.LogWarning(leaveEx, "Failed to leave old swarm, will attempt join anyway");
                                    }
                                }
                                
                                // Get manager's advertise address from swarm nodes
                                var nodes = await scopedDockerService.ListNodesAsync(swarmManager);
                                var managerNode = nodes.FirstOrDefault(n => n.Role == "manager" && n.Availability == "active");
                                var managerAddress = managerNode?.Address ?? $"{swarmManager.Host}:2377";
                                
                                _logger.LogInformation("Using swarm manager address: {Address} for {ManagerName}", 
                                    managerAddress, swarmManager.Name);
                                
                                // Get worker join token from manager
                                var (workerToken, _) = await scopedDockerService.GetJoinTokensAsync(swarmManager);
                                
                                if (!string.IsNullOrEmpty(workerToken))
                                {
                                    // Execute join command on the worker
                                    var joinCommand = $"docker swarm join --token {workerToken} {managerAddress}";
                                    
                                    _logger.LogInformation("Executing swarm join on {ServerName} to manager {ManagerAddress}", 
                                        serverToValidate.Name, managerAddress);
                                    var result = await scopedSshService.ExecuteCommandAsync(serverToValidate, joinCommand);
                                    
                                    if (result.ExitCode == 0)
                                    {
                                        _logger.LogInformation("Successfully joined {ServerName} to swarm", serverToValidate.Name);
                                        serverToValidate.SwarmNodeState = "ready";
                                        serverToValidate.SwarmNodeAvailability = "active";
                                        await scopedContext.SaveChangesAsync();
                                    }
                                    else
                                    {
                                        _logger.LogError("Failed to join swarm: {Output}", result.Output + result.Error);
                                    }
                                }
                            }
                            catch (Exception joinEx)
                            {
                                _logger.LogError(joinEx, "Error joining server {ServerName} to swarm", serverToValidate.Name);
                            }
                        }
                        else
                        {
                            _logger.LogWarning("No active swarm manager found to join {ServerName}", serverToValidate.Name);
                        }
                    }
                    
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
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
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
        var server = await _context.Servers
            .Include(s => s.Applications)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound(new { error = "Server not found" });
        }
        
        // Check if server has any applications
        if (server.Applications.Any())
        {
            return Conflict(new { error = $"Cannot delete server. It has {server.Applications.Count} application(s). Please remove all applications first." });
        }
        
        try
        {
            _context.Servers.Remove(server);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Deleted server {ServerName} (ID: {ServerId})", server.Name, server.Id);
            
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting server {ServerId}", id);
            return StatusCode(500, new { error = "Failed to delete server: " + ex.Message });
        }
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
            // Update swarm status if it exists
            try
            {
                var systemInfo = await _dockerService.GetSystemInfoAsync(existingLocalhost);
                if (systemInfo.SwarmActive)
                {
                    existingLocalhost.Type = ServerType.SwarmManager;
                    existingLocalhost.IsSwarmManager = true;
                    existingLocalhost.Status = ServerStatus.Online;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Updated existing localhost to SwarmManager");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not detect swarm on existing localhost");
            }
            
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
            Name = "localhost",
            Host = "localhost",
            Port = 22, // Not actually used for localhost
            Username = Environment.UserName,
            Status = ServerStatus.Online,
            Type = ServerType.Standalone,
            ProxyType = ProxyType.None,
            RegionId = primaryRegion.Id,
            PrivateKeyId = null,
            IsSwarmManager = false,
            CreatedAt = DateTime.UtcNow
        };
        
        // Detect swarm immediately after creation
        try
        {
            var systemInfo = await _dockerService.GetSystemInfoAsync(localhostServer);
            if (systemInfo.SwarmActive)
            {
                localhostServer.Type = ServerType.SwarmManager;
                localhostServer.IsSwarmManager = true;
                _logger.LogInformation("Detected swarm on localhost during creation");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not detect swarm on localhost during creation");
        }
        
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
            // IMPORTANT: When running in container, check if HOST's Docker socket is mounted
            // The mounted /var/run/docker.sock gives us access to the HOST's Docker
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
                // If we're in a container, this checks for the MOUNTED socket from host
                // which is exactly what we want - it means we CAN access Docker (the host's)
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
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
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
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
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
    
    /// <summary>
    /// Refresh server's swarm status detection
    /// </summary>
    [HttpPost("{id}/refresh-swarm-status")]
    public async Task<IActionResult> RefreshSwarmStatus(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {id} not found" });
        }
        
        try
        {
            var systemInfo = await _dockerService.GetSystemInfoAsync(server);
            
            if (systemInfo.SwarmActive)
            {
                if (!server.IsSwarmManager)
                {
                    _logger.LogInformation("Detected swarm on server {ServerName}, updating to SwarmManager", server.Name);
                    server.Type = ServerType.SwarmManager;
                    server.IsSwarmManager = true;
                    server.Status = ServerStatus.Online;
                    await _context.SaveChangesAsync();
                    return Ok(new { message = "Server updated to SwarmManager", swarmActive = true });
                }
                return Ok(new { message = "Server is already a SwarmManager", swarmActive = true });
            }
            else
            {
                if (server.IsSwarmManager)
                {
                    _logger.LogInformation("Swarm no longer active on server {ServerName}, updating to Standalone", server.Name);
                    server.Type = ServerType.Standalone;
                    server.IsSwarmManager = false;
                    await _context.SaveChangesAsync();
                    return Ok(new { message = "Server updated to Standalone", swarmActive = false });
                }
                return Ok(new { message = "Server is Standalone", swarmActive = false });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing swarm status for server {ServerId}", id);
            return StatusCode(500, new { error = "Failed to refresh swarm status", message = ex.Message });
        }
    }
    
    /// <summary>
    /// Initialize Docker Swarm on a server
    /// </summary>
    [HttpPost("{id}/swarm/init")]
    public async Task<IActionResult> InitializeSwarm(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {id} not found" });
        }
        
        try
        {
            // Use server's host as advertise address
            var advertiseAddress = server.Host;
            await _dockerService.InitializeSwarmAsync(server, advertiseAddress);
            
            // Update server type
            server.Type = ServerType.SwarmManager;
            server.IsSwarmManager = true;
            await _context.SaveChangesAsync();
            
            return Ok(new { message = "Swarm initialized successfully", advertiseAddress });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing swarm on server {ServerId}", id);
            return StatusCode(500, new { error = "Failed to initialize swarm", message = ex.Message });
        }
    }
    
    /// <summary>
    /// Get swarm join tokens
    /// </summary>
    [HttpGet("{id}/swarm/tokens")]
    public async Task<ActionResult> GetJoinTokens(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {id} not found" });
        }
        
        if (!server.IsSwarmManager)
        {
            return BadRequest(new { error = "Server is not a swarm manager" });
        }
        
        try
        {
            var (workerToken, managerToken) = await _dockerService.GetJoinTokensAsync(server);
            
            return Ok(new
            {
                WorkerToken = workerToken,
                ManagerToken = managerToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting join tokens for server {ServerId}", id);
            return StatusCode(500, new { error = "Failed to get join tokens", message = ex.Message });
        }
    }
    
    /// <summary>
    /// Auto-configure server by installing Docker and prerequisites
    /// </summary>
    [HttpPost("{id}/auto-configure")]
    public async Task<ActionResult> AutoConfigureServer(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {id} not found" });
        }
        
        if (server.Host == "localhost" || server.Host == "127.0.0.1")
        {
            return BadRequest(new { error = "Cannot auto-configure localhost. Docker should be installed locally." });
        }
        
        try
        {
            _logger.LogInformation("Starting auto-configuration for server {ServerName} ({Host})", server.Name, server.Host);
            
            // Run the configuration in background
            _ = Task.Run(async () => await AutoConfigureServerAsync(server.Id));
            
            return Ok(new { message = "Auto-configuration started. This may take several minutes. Check server status for progress." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting auto-configuration for server {ServerId}", id);
            return StatusCode(500, new { error = "Failed to start auto-configuration", message = ex.Message });
        }
    }
    
    private async Task AutoConfigureServerAsync(int serverId)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<HostCraftDbContext>();
        var sshService = scope.ServiceProvider.GetRequiredService<ISshService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ServersController>>();
        
        var server = await context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == serverId);
        
        if (server == null) return;
        
        try
        {
            logger.LogInformation("Auto-configuring server {ServerName}...", server.Name);
            
            // Read the install-docker.sh script
            var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "install-docker.sh");
            if (!System.IO.File.Exists(scriptPath))
            {
                // Try relative path if running in development
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "scripts", "install-docker.sh");
                if (!System.IO.File.Exists(scriptPath))
                {
                    logger.LogError("install-docker.sh script not found at {Path}", scriptPath);
                    return;
                }
            }
            
            var installScript = await System.IO.File.ReadAllTextAsync(scriptPath);
            
            // Upload script to remote server
            logger.LogInformation("Uploading installation script to {ServerName}...", server.Name);
            var remoteScriptPath = "/tmp/install-docker-hostcraft.sh";
            
            // Create script on remote server using echo
            var uploadCommand = $"cat > {remoteScriptPath} << 'HOSTCRAFT_EOF'\n{installScript}\nHOSTCRAFT_EOF\nchmod +x {remoteScriptPath}";
            var uploadResult = await sshService.ExecuteCommandAsync(server, uploadCommand);
            
            if (uploadResult.ExitCode != 0)
            {
                logger.LogError("Failed to upload script: {Error}", uploadResult.Error);
                return;
            }
            
            logger.LogInformation("Running Docker installation on {ServerName}...", server.Name);
            
            // Execute the script with sudo (non-interactive mode)
            var installCommand = $"sudo DEBIAN_FRONTEND=noninteractive bash {remoteScriptPath} 2>&1";
            var installResult = await sshService.ExecuteCommandAsync(server, installCommand);
            
            logger.LogInformation("Installation output: {Output}", installResult.Output);
            
            if (installResult.ExitCode == 0)
            {
                logger.LogInformation("Docker installed successfully on {ServerName}", server.Name);
                
                // Clean up the script
                try
                {
                    await sshService.ExecuteCommandAsync(server, $"rm -f {remoteScriptPath}");
                }
                catch
                {
                    // Ignore cleanup errors
                }
                
                // Wait for Docker to fully initialize
                logger.LogInformation("Waiting for Docker daemon to be ready...");
                await Task.Delay(10000);
                
                // Try to reconnect and validate (server might have restarted)
                var maxRetries = 5;
                var retryDelay = 5000;
                var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
                
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    try
                    {
                        logger.LogInformation("Validating Docker installation (attempt {Attempt}/{Max})...", retry + 1, maxRetries);
                        
                        // Test SSH connection first
                        var sshConnected = await sshService.ValidateConnectionAsync(server);
                        if (!sshConnected)
                        {
                            logger.LogWarning("SSH connection lost, server may have restarted. Waiting...");
                            await Task.Delay(retryDelay);
                            continue;
                        }
                        
                        // Test Docker connection
                        var isValid = await dockerService.ValidateConnectionAsync(server);
                        if (isValid)
                        {
                            server.Status = ServerStatus.Online;
                            await context.SaveChangesAsync();
                            logger.LogInformation("✅ Docker successfully validated on {ServerName}", server.Name);
                            break;
                        }
                        else
                        {
                            logger.LogWarning("Docker not ready yet, retrying...");
                            await Task.Delay(retryDelay);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Validation attempt {Attempt} failed, will retry", retry + 1);
                        if (retry < maxRetries - 1)
                        {
                            await Task.Delay(retryDelay);
                        }
                    }
                }
                
                // Final status check
                try
                {
                    var isValid = await dockerService.ValidateConnectionAsync(server);
                    server.Status = isValid ? ServerStatus.Online : ServerStatus.Offline;
                    await context.SaveChangesAsync();
                    
                    // If server is now online and marked as SwarmWorker, try to join it to an existing swarm
                    if (isValid && server.Type == ServerType.SwarmWorker)
                    {
                        logger.LogInformation("Server {ServerName} marked as SwarmWorker, attempting to join swarm after auto-configure", server.Name);
                        
                        try
                        {
                            // Find an active swarm manager
                            var swarmManager = await context.Servers
                                .Include(s => s.PrivateKey)
                                .Where(s => s.Type == ServerType.SwarmManager && s.Status == ServerStatus.Online)
                                .FirstOrDefaultAsync();
                            
                            if (swarmManager != null)
                            {
                                logger.LogInformation("Found swarm manager: {ManagerName}", swarmManager.Name);
                                
                                // Get join tokens from the manager
                                var (workerToken, _) = await dockerService.GetJoinTokensAsync(swarmManager);
                                
                                if (!string.IsNullOrEmpty(workerToken))
                                {
                                    // Get manager's IP address
                                    var managerAddress = $"{swarmManager.Host}:2377";
                                    var joinCommand = $"docker swarm join --token {workerToken} {managerAddress}";
                                    
                                    logger.LogInformation("Joining {ServerName} to swarm at {ManagerAddress}", server.Name, managerAddress);
                                    
                                    var result = await sshService.ExecuteCommandAsync(server, joinCommand);
                                    
                                    if (result.ExitCode == 0 || result.Output.Contains("This node joined a swarm"))
                                    {
                                        logger.LogInformation("Successfully joined {ServerName} to swarm after auto-configure", server.Name);
                                    }
                                    else if (result.Output.Contains("This node is already part of a swarm"))
                                    {
                                        logger.LogInformation("{ServerName} is already part of the swarm", server.Name);
                                    }
                                    else
                                    {
                                        logger.LogError("Failed to join swarm after auto-configure: {Output}", result.Output + result.Error);
                                    }
                                }
                                else
                                {
                                    logger.LogWarning("Could not retrieve worker join token from swarm manager");
                                }
                            }
                            else
                            {
                                logger.LogWarning("No active swarm manager found to join {ServerName} to swarm", server.Name);
                            }
                        }
                        catch (Exception joinEx)
                        {
                            logger.LogError(joinEx, "Error joining server {ServerName} to swarm after auto-configure", server.Name);
                            // Don't fail the auto-configure if swarm join fails
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Docker installed but validation inconclusive for {ServerName}", server.Name);
                    server.Status = ServerStatus.Offline;
                    await context.SaveChangesAsync();
                }
            }
            else
            {
                logger.LogError("Docker installation failed on {ServerName}: {Error}", server.Name, installResult.Error);
                server.Status = ServerStatus.Error;
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during auto-configuration of server {ServerId}", serverId);
        }
    }
    
    /// <summary>
    /// Get system information including swarm status
    /// </summary>
    [HttpGet("{id}/info")]
    public async Task<ActionResult<SystemInfo>> GetSystemInfo(int id)
    {
        var server = await _context.Servers
            .Include(s => s.PrivateKey)
            .FirstOrDefaultAsync(s => s.Id == id);
        
        if (server == null)
        {
            return NotFound(new { error = $"Server {id} not found" });
        }
        
        try
        {
            var systemInfo = await _dockerService.GetSystemInfoAsync(server);
            return Ok(systemInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system info for server {ServerId}", id);
            return StatusCode(500, new { error = "Failed to get system info", message = ex.Message });
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
