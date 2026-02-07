using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for managing server CRUD operations with validation.
/// Extracted from ServersController to follow single responsibility principle.
/// </summary>
public class ServerManagementService : IServerManagementService
{
    private readonly IServerRepository _serverRepository;
    private readonly IPrivateKeyRepository _privateKeyRepository;
    private readonly IRegionRepository _regionRepository;
    private readonly IDockerService _dockerService;
    private readonly ISshService _sshService;
    private readonly ILogger<ServerManagementService> _logger;

    public ServerManagementService(
        IServerRepository serverRepository,
        IPrivateKeyRepository privateKeyRepository,
        IRegionRepository regionRepository,
        IDockerService dockerService,
        ISshService sshService,
        ILogger<ServerManagementService> logger)
    {
        _serverRepository = serverRepository;
        _privateKeyRepository = privateKeyRepository;
        _regionRepository = regionRepository;
        _dockerService = dockerService;
        _sshService = sshService;
        _logger = logger;
    }

    public async Task<ServerCreationResult> CreateServerAsync(ServerCreationRequest request, CancellationToken cancellationToken = default)
    {
        // Validate required fields
        if (string.IsNullOrWhiteSpace(request.Name))
            return new ServerCreationResult(false, "Server name is required");

        if (string.IsNullOrWhiteSpace(request.Host))
            return new ServerCreationResult(false, "Host/IP address is required");

        if (string.IsNullOrWhiteSpace(request.User))
            return new ServerCreationResult(false, "Username is required");

        // Check if this is a localhost connection
        var isLocalhost = request.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                         request.Host == "127.0.0.1" ||
                         request.Host == "::1";

        // SSH private key is required for remote servers, but optional for localhost
        if (!isLocalhost && string.IsNullOrWhiteSpace(request.PrivateKeyContent))
            return new ServerCreationResult(false, "SSH private key is required for remote servers");

        // Validate private key format if provided
        if (!string.IsNullOrWhiteSpace(request.PrivateKeyContent) &&
            (!request.PrivateKeyContent.Contains("BEGIN") || !request.PrivateKeyContent.Contains("PRIVATE KEY")))
            return new ServerCreationResult(false, "Invalid SSH private key format. Key must contain BEGIN and PRIVATE KEY markers.");

        // Check for duplicate server name
        var existingServer = await _serverRepository.ExistsByNameAsync(request.Name, cancellationToken);
        if (existingServer)
            return new ServerCreationResult(false, $"A server with the name '{request.Name}' already exists");

        try
        {
            // Create PrivateKey entity if provided
            PrivateKey? privateKey = null;
            if (!string.IsNullOrEmpty(request.PrivateKeyContent))
            {
                privateKey = new PrivateKey
                {
                    Name = $"{request.Name} SSH Key - {DateTime.UtcNow:yyyyMMddHHmmss}",
                    KeyData = request.PrivateKeyContent,
                    CreatedAt = DateTime.UtcNow
                };
                await _privateKeyRepository.AddAsync(privateKey, cancellationToken);
            }

            // Find or create Region if provided
            Region? region = null;
            if (!string.IsNullOrEmpty(request.Region))
            {
                region = await _regionRepository.GetByNameOrCodeAsync(request.Region, cancellationToken);

                if (region == null)
                {
                    region = new Region
                    {
                        Name = request.Region,
                        Code = request.Region.ToLower().Replace(" ", "-"),
                        CreatedAt = DateTime.UtcNow
                    };
                    await _regionRepository.AddAsync(region, cancellationToken);
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
                DefaultLetsEncryptEmail = request.DefaultLetsEncryptEmail,
                Status = ServerStatus.Validating,
                CreatedAt = DateTime.UtcNow,
                PrivateKey = privateKey,
                Region = region
            };

            await _serverRepository.AddAsync(server, cancellationToken);

            _logger.LogInformation("Created server {ServerName} (ID: {ServerId})", server.Name, server.Id);

            return new ServerCreationResult(true, "Server created successfully", server.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating server {ServerName}", request.Name);
            return new ServerCreationResult(false, "Failed to create server", ErrorDetails: ex.Message);
        }
    }

    public async Task<ServerUpdateResult> UpdateServerAsync(int serverId, ServerUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
            return new ServerUpdateResult(false, "Server not found");

        try
        {
            // Update basic fields
            if (request.Name != null)
                server.Name = request.Name;
            if (request.Host != null)
                server.Host = request.Host;
            if (request.Port.HasValue)
                server.Port = request.Port.Value;
            if (request.User != null)
                server.Username = request.User;
            if (request.Type.HasValue)
                server.Type = request.Type.Value;
            if (request.ProxyType.HasValue)
                server.ProxyType = request.ProxyType.Value;
            if (request.DefaultLetsEncryptEmail != null)
                server.DefaultLetsEncryptEmail = request.DefaultLetsEncryptEmail;

            // Update SSH private key if provided
            if (request.PrivateKeyContent != null)
            {
                if (server.PrivateKey != null)
                {
                    server.PrivateKey.KeyData = request.PrivateKeyContent;
                    await _privateKeyRepository.UpdateAsync(server.PrivateKey, cancellationToken);
                }
                else
                {
                    var privateKey = new PrivateKey
                    {
                        Name = $"{server.Name} SSH Key - {DateTime.UtcNow:yyyyMMddHHmmss}",
                        KeyData = request.PrivateKeyContent,
                        CreatedAt = DateTime.UtcNow
                    };
                    var createdKey = await _privateKeyRepository.AddAsync(privateKey, cancellationToken);
                    server.PrivateKey = createdKey;
                }
            }

            await _serverRepository.UpdateAsync(server, cancellationToken);

            _logger.LogInformation("Updated server {ServerId} - {ServerName}", server.Id, server.Name);

            return new ServerUpdateResult(true, "Server updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating server {ServerId}", serverId);
            return new ServerUpdateResult(false, "Failed to update server", ex.Message);
        }
    }

    public async Task<ServerDeletionResult> DeleteServerAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithApplicationsAsync(serverId, cancellationToken);

        if (server == null)
            return new ServerDeletionResult(false, "Server not found");

        // Check if server has applications
        if (server.Applications.Any())
            return new ServerDeletionResult(false, $"Cannot delete server with {server.Applications.Count} application(s). Delete applications first.");

        try
        {
            await _serverRepository.DeleteAsync(server, cancellationToken);

            _logger.LogInformation("Deleted server {ServerName} (ID: {ServerId})", server.Name, serverId);

            return new ServerDeletionResult(true, "Server deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting server {ServerId}", serverId);
            return new ServerDeletionResult(false, "Failed to delete server", ex.Message);
        }
    }

    public async Task<ServerConnectionValidation> ValidateServerAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
            return new ServerConnectionValidation(false, "Server not found");

        try
        {
            _logger.LogInformation("Validating server {ServerName} (ID: {ServerId})", server.Name, serverId);

            var isValid = await _dockerService.ValidateConnectionAsync(server, cancellationToken);
            var newStatus = isValid ? ServerStatus.Online : ServerStatus.Offline;

            server.Status = newStatus;
            server.LastHealthCheck = DateTime.UtcNow;

            string? dockerVersion = null;
            bool? isSwarm = null;

            if (isValid)
            {
                try
                {
                    var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
                    dockerVersion = systemInfo.DockerVersion;
                    isSwarm = systemInfo.SwarmActive;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get system info for server {ServerId}", serverId);
                }
            }

            await _serverRepository.UpdateAsync(server, cancellationToken);

            var message = isValid ? "Server is online and Docker is accessible" : "Server is offline or Docker is not accessible";

            return new ServerConnectionValidation(true, message, newStatus, dockerVersion, isSwarm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating server {ServerId}", serverId);
            return new ServerConnectionValidation(false, "Failed to validate server", ErrorDetails: ex.Message);
        }
    }

    public async Task<ServerValidationOutcome> ValidateNewServerAsync(ServerCreationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            PrivateKey? tempKey = null;
            if (!string.IsNullOrEmpty(request.PrivateKeyContent))
            {
                tempKey = new PrivateKey
                {
                    Name = "Temp Validation Key",
                    KeyData = request.PrivateKeyContent
                };
            }

            var tempServer = new Server
            {
                Name = request.Name,
                Host = request.Host,
                Port = request.Port,
                Username = request.User,
                PrivateKey = tempKey,
                Type = request.Type
            };

            var isValid = await _dockerService.ValidateConnectionAsync(tempServer, cancellationToken);

            if (!isValid)
            {
                return new ServerValidationOutcome(false, "Cannot connect to Docker daemon. Check credentials and network access.");
            }

            var systemInfo = await _dockerService.GetSystemInfoAsync(tempServer, cancellationToken);

            return new ServerValidationOutcome(true, "Connection successful! Docker daemon is accessible.", systemInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating new server connection");
            return new ServerValidationOutcome(false, $"Connection failed: {ex.Message}", ErrorDetails: ex.Message);
        }
    }

    public async Task<ServerValidationOutcome> ValidateExistingServerAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
        {
            return new ServerValidationOutcome(false, "Server not found", NotFound: true);
        }

        try
        {
            _logger.LogInformation("Manually validating server {ServerId} - {ServerName}", serverId, server.Name);

            var isValid = await _dockerService.ValidateConnectionAsync(server, cancellationToken);
            var previousType = server.Type;
            var rejoined = false;
            string? rejoinError = null;
            SystemInfo? systemInfo = null;

            if (isValid)
            {
                systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
                server.Status = ServerStatus.Online;
                server.LastHealthCheck = DateTime.UtcNow;

                var wasSwarmWorker = server.IsSwarmWorker || server.Type == ServerType.SwarmWorker;
                if (!systemInfo.SwarmActive && wasSwarmWorker &&
                    !string.IsNullOrEmpty(server.SwarmJoinToken) &&
                    !string.IsNullOrEmpty(server.SwarmManagerAddress))
                {
                    _logger.LogWarning(
                        "Server {ServerName} was a swarm worker but lost connection. Attempting to rejoin...",
                        server.Name);

                    try
                    {
                        await _sshService.ExecuteCommandAsync(server, "docker swarm leave --force", cancellationToken);

                        var joinCommand = $"docker swarm join --token {server.SwarmJoinToken} {server.SwarmManagerAddress}";
                        var joinResult = await _sshService.ExecuteCommandAsync(server, joinCommand, cancellationToken);

                        if (joinResult.ExitCode == 0 || joinResult.Output.Contains("This node joined a swarm"))
                        {
                            _logger.LogInformation("Successfully rejoined {ServerName} to swarm", server.Name);
                            rejoined = true;
                            systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
                        }
                        else
                        {
                            rejoinError = joinResult.Error ?? joinResult.Output;
                        }
                    }
                    catch (Exception rejoinEx)
                    {
                        rejoinError = rejoinEx.Message;
                        _logger.LogError(rejoinEx, "Failed to rejoin {ServerName} to swarm", server.Name);
                    }
                }

                if (systemInfo != null && systemInfo.SwarmActive)
                {
                    if (server.Type == ServerType.Standalone)
                    {
                        server.Type = ServerType.SwarmManager;
                        server.IsSwarmManager = true;
                    }

                    server.SwarmNodeId = systemInfo.SwarmNodeId;
                    server.SwarmNodeState = systemInfo.SwarmNodeState;
                    server.SwarmNodeAvailability = systemInfo.SwarmNodeAvailability;
                    server.ActualHostname = systemInfo.Hostname;
                    server.SwarmAdvertiseAddress = systemInfo.SwarmNodeAddress;
                }

                await _serverRepository.UpdateAsync(server, cancellationToken);

                var message = rejoined
                    ? "Server rejoined swarm successfully"
                    : "Server is online and accessible";

                return new ServerValidationOutcome(
                    true,
                    message,
                    systemInfo,
                    Rejoined: rejoined,
                    RejoinError: rejoinError,
                    UpdatedStatus: server.Status,
                    PreviousType: previousType,
                    UpdatedType: server.Type);
            }

            server.Status = ServerStatus.Offline;
            await _serverRepository.UpdateAsync(server, cancellationToken);

            return new ServerValidationOutcome(false, "Cannot connect to Docker daemon", UpdatedStatus: server.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating server {ServerId}", serverId);
            server.Status = ServerStatus.Error;
            await _serverRepository.UpdateAsync(server, cancellationToken);

            return new ServerValidationOutcome(false, $"Error: {ex.Message}", UpdatedStatus: server.Status, ErrorDetails: ex.Message);
        }
    }

    public async Task<LocalhostConfigurationResult> ConfigureLocalhostAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var existingLocalhost = (await _serverRepository.GetAllWithRegionAsync(cancellationToken))
                .FirstOrDefault(s => s.Host == "localhost" || s.Host == "127.0.0.1");

            if (existingLocalhost != null)
            {
                try
                {
                    var systemInfo = await _dockerService.GetSystemInfoAsync(existingLocalhost, cancellationToken);
                    UpdateServerFromSystemInfo(existingLocalhost, systemInfo);
                    await _serverRepository.UpdateAsync(existingLocalhost, cancellationToken);
                    _logger.LogInformation("Updated existing localhost with swarm info");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not detect swarm on existing localhost");
                }

                return new LocalhostConfigurationResult(true, "Localhost server updated", existingLocalhost);
            }

            if (!IsDockerAvailable())
            {
                return new LocalhostConfigurationResult(false, "Docker is not available on this system. Please install Docker first.", DockerAvailable: false);
            }

            var primaryRegion = await _regionRepository.GetPrimaryAsync(cancellationToken);

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
                primaryRegion = await _regionRepository.AddAsync(primaryRegion, cancellationToken);
            }

            var localhostServer = new Server
            {
                Name = "localhost",
                Host = "localhost",
                Port = 22,
                Username = Environment.UserName,
                Status = ServerStatus.Online,
                Type = ServerType.Standalone,
                ProxyType = ProxyType.None,
                RegionId = primaryRegion.Id,
                PrivateKeyId = null,
                IsSwarmManager = false,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                var systemInfo = await _dockerService.GetSystemInfoAsync(localhostServer, cancellationToken);
                UpdateServerFromSystemInfo(localhostServer, systemInfo);
                _logger.LogInformation("Configured localhost with hostname: {Hostname}, SwarmNodeId: {NodeId}", systemInfo.Hostname, systemInfo.SwarmNodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not detect swarm on localhost during creation");
            }

            localhostServer = await _serverRepository.AddAsync(localhostServer, cancellationToken);

            var server = await _serverRepository.GetByIdWithPrivateKeyAndRegionAsync(localhostServer.Id, cancellationToken);
            return new LocalhostConfigurationResult(true, "Localhost server configured", server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring localhost server");
            return new LocalhostConfigurationResult(false, "Failed to configure localhost", ErrorDetails: ex.Message);
        }
    }

    private static void UpdateServerFromSystemInfo(Server server, SystemInfo systemInfo)
    {
        server.Status = ServerStatus.Online;
        server.ActualHostname = systemInfo.Hostname;
        server.SwarmNodeId = systemInfo.SwarmNodeId;
        server.SwarmId = systemInfo.SwarmId;
        server.SwarmAdvertiseAddress = systemInfo.SwarmNodeAddress;
        server.SwarmNodeState = systemInfo.SwarmNodeState;
        server.SwarmNodeAvailability = systemInfo.SwarmNodeAvailability;

        if (systemInfo.SwarmActive)
        {
            if (systemInfo.IsSwarmManager)
            {
                server.Type = ServerType.SwarmManager;
                server.IsSwarmManager = true;
                server.IsSwarmWorker = false;
            }
            else
            {
                server.Type = ServerType.SwarmWorker;
                server.IsSwarmManager = false;
                server.IsSwarmWorker = true;
            }
        }
        else
        {
            server.Type = ServerType.Standalone;
            server.IsSwarmManager = false;
            server.IsSwarmWorker = false;
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var dockerSocket = isWindows ? "//./pipe/docker_engine" : "/var/run/docker.sock";

            if (isWindows)
            {
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
                }
                return false;
            }
            else
            {
                return System.IO.File.Exists(dockerSocket);
            }
        }
        catch
        {
            return false;
        }
    }
}
