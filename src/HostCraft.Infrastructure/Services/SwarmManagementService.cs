using System.Linq;
using System.Text.Json;
using HostCraft.Core.Configuration;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for managing Docker Swarm operations.
/// Extracted from ServersController to follow single responsibility principle.
/// </summary>
public class SwarmManagementService : ISwarmManagementService
{
    private readonly IServerRepository _serverRepository;
    private readonly IDockerService _dockerService;
    private readonly ISshService _sshService;
    private readonly IGlusterFsService _glusterFsService;
    private readonly DockerRegistryOptions _registryOptions;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<SwarmManagementService> _logger;

    public SwarmManagementService(
        IServerRepository serverRepository,
        IDockerService dockerService,
        ISshService sshService,
        IGlusterFsService glusterFsService,
        IOptions<DockerRegistryOptions> registryOptions,
        IOptions<StorageOptions> storageOptions,
        ILogger<SwarmManagementService> logger)
    {
        _serverRepository = serverRepository;
        _dockerService = dockerService;
        _sshService = sshService;
        _glusterFsService = glusterFsService;
        _registryOptions = registryOptions.Value;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task InitializeSwarmAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
        {
            throw new InvalidOperationException($"Server {serverId} not found");
        }

        try
        {
            // Configure insecure registry before initializing swarm
            await ConfigureInsecureRegistryAsync(server, cancellationToken);

            // Use server's host as advertise address
            var advertiseAddress = server.Host;
            await _dockerService.InitializeSwarmAsync(server, advertiseAddress, cancellationToken);

            // Update server type
            server.Type = ServerType.SwarmManager;
            server.IsSwarmManager = true;
            await _serverRepository.UpdateAsync(server, cancellationToken);

            _logger.LogInformation("Successfully initialized swarm on server {ServerName} with advertise address {AdvertiseAddress}",
                server.Name, advertiseAddress);

            // Wait a moment for Swarm to fully initialize and get node ID
            await Task.Delay(2000, cancellationToken);

            // Refresh server to get SwarmNodeId
            var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
            server.SwarmNodeId = systemInfo.SwarmNodeId;
            await _serverRepository.UpdateAsync(server, cancellationToken);

            // Label the node with zone/datacenter if configured
            await LabelSwarmNodeAsync(server, server, cancellationToken);

            // Initialize GlusterFS if enabled
            if (_storageOptions.Type == "glusterfs" && _storageOptions.GlusterFs.Enabled)
            {
                _logger.LogInformation("Initializing GlusterFS on first manager {ServerName}", server.Name);
                var glusterResult = await _glusterFsService.InitializeOnFirstManagerAsync(server, cancellationToken);

                if (glusterResult.Success)
                {
                    _logger.LogInformation("GlusterFS initialization: {Message}", glusterResult.Message);
                }
                else
                {
                    _logger.LogWarning("GlusterFS initialization failed: {Message}. You can set up GlusterFS manually later.", glusterResult.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing swarm on server {ServerId}", serverId);
            throw new InvalidOperationException($"Failed to initialize swarm: {ex.Message}", ex);
        }
    }

    public async Task<(string WorkerToken, string ManagerToken)> GetJoinTokensAsync(int managerId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(managerId, cancellationToken);

        if (server == null)
        {
            throw new InvalidOperationException($"Server {managerId} not found");
        }

        if (!server.IsSwarmManager)
        {
            throw new InvalidOperationException("Server is not a swarm manager");
        }

        try
        {
            var (workerToken, managerToken) = await _dockerService.GetJoinTokensAsync(server, cancellationToken);
            return (workerToken, managerToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting join tokens for server {ServerId}", managerId);
            throw new InvalidOperationException($"Failed to get join tokens: {ex.Message}", ex);
        }
    }

    public async Task<SwarmJoinResult> JoinAsManagerAsync(int existingManagerId, int serverIdToJoin, CancellationToken cancellationToken = default)
    {
        // 1. Load existing manager (with PrivateKey for SSH)
        var existingManager = await _serverRepository.GetByIdWithPrivateKeyAsync(existingManagerId, cancellationToken);

        if (existingManager == null)
            return new SwarmJoinResult(false, "Existing manager not found");

        if (!existingManager.IsSwarmManager)
            return new SwarmJoinResult(false, "Server is not a swarm manager");

        if (existingManager.Status != ServerStatus.Online)
            return new SwarmJoinResult(false, "Existing manager is not online");

        // 2. Load server to join (with PrivateKey)
        var serverToJoin = await _serverRepository.GetByIdWithPrivateKeyAsync(serverIdToJoin, cancellationToken);

        if (serverToJoin == null)
            return new SwarmJoinResult(false, "Server to join not found");

        if (serverToJoin.Type != ServerType.Standalone)
            return new SwarmJoinResult(false, $"Server must be standalone to join as manager. Current type: {serverToJoin.Type}");

        if (serverToJoin.Status != ServerStatus.Online)
            return new SwarmJoinResult(false, "Server to join is not online");

        try
        {
            // 3. Check current manager count and issue warning
            var currentManagerCount = await _serverRepository.CountReadyManagersAsync(cancellationToken);

            string? quorumWarning = null;
            if (currentManagerCount == 1)
            {
                quorumWarning = "Adding a 2nd manager provides NO fault tolerance. Both managers must be up for swarm to work. Add a 3rd manager for true HA.";
            }
            else if (currentManagerCount % 2 == 0)
            {
                quorumWarning = $"You will have {currentManagerCount + 1} managers. Odd numbers are recommended for proper quorum.";
            }

            // 4. Check if server is already in a swarm, leave if needed
            var serverSystemInfo = await _dockerService.GetSystemInfoAsync(serverToJoin, cancellationToken);
            if (serverSystemInfo.SwarmActive)
            {
                _logger.LogInformation("Server {ServerName} is already in a swarm, leaving first...", serverToJoin.Name);
                await _dockerService.LeaveSwarmAsync(serverToJoin, force: true, cancellationToken);
                await Task.Delay(2000, cancellationToken); // Wait for leave to complete
            }

            // 5. Clean up stale nodes from manager
            try
            {
                var allNodes = await _dockerService.ListNodesAsync(existingManager, cancellationToken);
                var staleNodes = allNodes.Where(n =>
                    (n.Hostname?.Equals(serverToJoin.Name, StringComparison.OrdinalIgnoreCase) == true ||
                     n.Address?.Contains(serverToJoin.Host, StringComparison.OrdinalIgnoreCase) == true) &&
                    n.State == "down").ToList();

                foreach (var staleNode in staleNodes)
                {
                    _logger.LogInformation("Removing stale node {NodeId} ({Hostname}) from swarm", staleNode.Id, staleNode.Hostname);
                    await _dockerService.RemoveNodeAsync(existingManager, staleNode.Id, force: true, cancellationToken);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up stale nodes, continuing with join");
            }

            // 6. Get manager join token from existing manager
            var (_, managerToken) = await _dockerService.GetJoinTokensAsync(existingManager, cancellationToken);

            // 7. Get manager's advertise address
            var nodes = await _dockerService.ListNodesAsync(existingManager, cancellationToken);
            var managerNode = nodes.FirstOrDefault(n => n.Role == "manager" && n.Availability == "active");
            var managerAddress = managerNode?.Address ?? $"{existingManager.Host}:2377";

            _logger.LogInformation("Joining {ServerName} as manager to swarm at {ManagerAddress}",
                serverToJoin.Name, managerAddress);

            // 8. Configure insecure registry before joining
            await ConfigureInsecureRegistryAsync(serverToJoin, cancellationToken);

            // 9. Execute join command on new manager via SSH
            var joinCommand = $"docker swarm join --token {managerToken} {managerAddress}";
            var result = await _sshService.ExecuteCommandAsync(serverToJoin, joinCommand, cancellationToken);

            if (result.ExitCode == 0)
            {
                // 10. Update server entity
                serverToJoin.Type = ServerType.SwarmManager;
                serverToJoin.IsSwarmManager = true;
                serverToJoin.IsSwarmWorker = false;
                serverToJoin.SwarmJoinToken = managerToken;
                serverToJoin.SwarmManagerAddress = managerAddress;
                serverToJoin.SwarmNodeState = "ready";
                serverToJoin.SwarmNodeAvailability = "active";
                serverToJoin.SwarmId = existingManager.SwarmId;

                await _serverRepository.UpdateAsync(serverToJoin, cancellationToken);

                _logger.LogInformation("Successfully joined {ServerName} as manager", serverToJoin.Name);

                // Label the node with zone/datacenter if configured
                await LabelSwarmNodeAsync(serverToJoin, existingManager, cancellationToken);

                // Add to GlusterFS cluster if enabled
                if (_storageOptions.Type == "glusterfs" && _storageOptions.GlusterFs.Enabled)
                {
                    _logger.LogInformation("Adding {ServerName} to GlusterFS cluster", serverToJoin.Name);

                    // Get all existing manager nodes
                    var allManagers = await _serverRepository.GetSwarmManagersAsync(cancellationToken);
                    var existingGlusterManagers = allManagers
                        .Where(m => m.Id != serverToJoin.Id && m.Status == ServerStatus.Online)
                        .ToList();

                    var glusterResult = await _glusterFsService.AddManagerToClusterAsync(
                        serverToJoin,
                        existingGlusterManagers,
                        cancellationToken);

                    if (glusterResult.Success)
                    {
                        _logger.LogInformation("GlusterFS cluster updated: {Message}", glusterResult.Message);
                    }
                    else
                    {
                        _logger.LogWarning("GlusterFS cluster update failed: {Message}. GlusterFS can be configured manually later.", glusterResult.Message);
                    }
                }

                return new SwarmJoinResult(
                    true,
                    "Successfully joined swarm as manager",
                    existingManager.SwarmId,
                    managerAddress,
                    currentManagerCount + 1,
                    quorumWarning);
            }
            else
            {
                _logger.LogError("Failed to join {ServerName} as manager: {Output}",
                    serverToJoin.Name, result.Output + result.Error);
                return new SwarmJoinResult(
                    false,
                    "Failed to join swarm as manager",
                    ErrorDetails: result.Output + result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining server {ServerId} as manager", serverIdToJoin);
            return new SwarmJoinResult(false, "Failed to join as manager", ErrorDetails: ex.Message);
        }
    }

    public async Task<SwarmPromotionResult> PromoteToManagerAsync(int workerId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(workerId, cancellationToken);

        if (server == null)
            return new SwarmPromotionResult(false, "Server not found");

        if (!server.IsSwarmWorker)
            return new SwarmPromotionResult(false, $"Server must be a swarm worker to promote. Current type: {server.Type}");

        if (string.IsNullOrEmpty(server.SwarmNodeId))
            return new SwarmPromotionResult(false, "Server does not have a swarm node ID");

        try
        {
            // 1. Find an active manager to execute the promotion
            var activeManager = await _serverRepository.GetFirstReadyManagerAsync(cancellationToken);

            if (activeManager == null)
                return new SwarmPromotionResult(false, "No active swarm manager found to execute promotion");

            // 2. Check current manager count and issue warning
            var currentManagerCount = await _serverRepository.CountReadyManagersAsync(cancellationToken);

            string? quorumWarning = null;
            if (currentManagerCount == 1)
            {
                quorumWarning = "Adding a 2nd manager provides NO fault tolerance. Both managers must be up for swarm to work. Add a 3rd manager for true HA.";
            }
            else if (currentManagerCount % 2 == 0)
            {
                quorumWarning = $"You will have {currentManagerCount + 1} managers. Odd numbers are recommended for proper quorum.";
            }

            // 3. Promote node via Docker API
            var updateRequest = new NodeUpdateRequest(Role: "manager", Availability: null);
            await _dockerService.UpdateNodeAsync(activeManager, server.SwarmNodeId, updateRequest, cancellationToken);

            // 4. Update server entity
            server.Type = ServerType.SwarmManager;
            server.IsSwarmManager = true;
            server.IsSwarmWorker = false;

            await _serverRepository.UpdateAsync(server, cancellationToken);

            _logger.LogInformation("Successfully promoted {ServerName} to manager", server.Name);

            return new SwarmPromotionResult(
                true,
                "Successfully promoted to manager",
                currentManagerCount + 1,
                quorumWarning);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error promoting server {ServerId} to manager", workerId);
            return new SwarmPromotionResult(false, "Failed to promote to manager", ErrorDetails: ex.Message);
        }
    }

    public async Task RefreshSwarmStatusAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
        {
            throw new InvalidOperationException($"Server {serverId} not found");
        }

        try
        {
            // Get system info to check swarm status
            var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);

            if (systemInfo.SwarmActive)
            {
                server.SwarmId = systemInfo.SwarmId;

                // Get node details
                var nodes = await _dockerService.ListNodesAsync(server, cancellationToken);
                var currentNode = nodes.FirstOrDefault(n => n.Id == systemInfo.SwarmNodeId);

                if (currentNode != null)
                {
                    server.SwarmNodeId = currentNode.Id;
                    server.SwarmNodeState = currentNode.State;
                    server.SwarmNodeAvailability = currentNode.Availability;

                    // Update manager/worker status
                    server.IsSwarmManager = currentNode.Role == "manager";
                    server.IsSwarmWorker = currentNode.Role == "worker";
                    server.Type = currentNode.Role == "manager" ? ServerType.SwarmManager : ServerType.SwarmWorker;
                }

                // Count managers and workers
                server.SwarmManagerCount = nodes.Count(n => n.Role == "manager" && n.State == "ready");
                server.SwarmWorkerCount = nodes.Count(n => n.Role == "worker" && n.State == "ready");
            }
            else
            {
                // Server is not in a swarm
                server.IsSwarmManager = false;
                server.IsSwarmWorker = false;
                server.SwarmId = null;
                server.SwarmNodeId = null;
                server.SwarmNodeState = null;
                server.SwarmNodeAvailability = null;
                server.SwarmManagerCount = 0;
                server.SwarmWorkerCount = 0;
                server.Type = ServerType.Standalone;
            }

            await _serverRepository.UpdateAsync(server, cancellationToken);

            _logger.LogInformation("Refreshed swarm status for server {ServerName}: IsSwarm={IsSwarm}, Role={Role}",
                server.Name, server.IsSwarm, server.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing swarm status for server {ServerId}", serverId);
            throw new InvalidOperationException($"Failed to refresh swarm status: {ex.Message}", ex);
        }
    }

    public async Task<SwarmRefreshResult> RefreshSwarmStatusWithRecoveryAsync(int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
        {
            return new SwarmRefreshResult(false, $"Server {serverId} not found", NotFound: true);
        }

        try
        {
            var systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
            var previousType = server.Type;
            var wasSwarmWorker = server.IsSwarmWorker || server.Type == ServerType.SwarmWorker;
            var rejoined = false;
            string? rejoinError = null;

            if (!systemInfo.SwarmActive && wasSwarmWorker &&
                !string.IsNullOrEmpty(server.SwarmJoinToken) &&
                !string.IsNullOrEmpty(server.SwarmManagerAddress))
            {
                _logger.LogWarning(
                    "Server {ServerName} was a swarm worker but lost connection. Attempting to rejoin using stored token...",
                    server.Name);

                try
                {
                    try
                    {
                        var leaveResult = await _sshService.ExecuteCommandAsync(server, "docker swarm leave --force", cancellationToken);
                        _logger.LogInformation("Left stale swarm state: {Output}", leaveResult.Output);
                    }
                    catch (Exception leaveEx)
                    {
                        _logger.LogDebug(leaveEx, "Swarm leave failed (may not have been in swarm)");
                    }

                    var joinCommand = $"docker swarm join --token {server.SwarmJoinToken} {server.SwarmManagerAddress}";
                    var joinResult = await _sshService.ExecuteCommandAsync(server, joinCommand, cancellationToken);

                    if (joinResult.ExitCode == 0 || joinResult.Output.Contains("This node joined a swarm"))
                    {
                        _logger.LogInformation("Successfully rejoined {ServerName} to swarm at {ManagerAddress}",
                            server.Name, server.SwarmManagerAddress);
                        rejoined = true;
                        systemInfo = await _dockerService.GetSystemInfoAsync(server, cancellationToken);
                    }
                    else
                    {
                        rejoinError = joinResult.Error ?? joinResult.Output;
                        _logger.LogError("Failed to rejoin {ServerName} to swarm: {Error}", server.Name, rejoinError);
                    }
                }
                catch (Exception rejoinEx)
                {
                    rejoinError = rejoinEx.Message;
                    _logger.LogError(rejoinEx, "Error during swarm rejoin for {ServerName}", server.Name);
                }
            }

            await RefreshSwarmStatusAsync(serverId, cancellationToken);

            server = await _serverRepository.GetByIdAsync(serverId, cancellationToken) ?? server;

            var message = rejoined
                ? "Server rejoined swarm successfully"
                : server.Type != previousType
                    ? $"Server updated from {previousType} to {server.Type}"
                    : $"Server is {server.Type}";

            return new SwarmRefreshResult(
                true,
                message,
                SwarmActive: server.IsSwarm,
                Hostname: systemInfo.Hostname,
                NodeId: server.SwarmNodeId,
                NodeAddress: systemInfo.SwarmNodeAddress,
                Rejoined: rejoined,
                RejoinError: rejoinError,
                PreviousType: previousType,
                UpdatedType: server.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing swarm status for server {ServerId}", serverId);
            return new SwarmRefreshResult(false, "Failed to refresh swarm status", ErrorDetails: ex.Message);
        }
    }

    /// <summary>
    /// Configures Docker daemon on a node to trust the internal registry as insecure.
    /// Required for nodes to pull images from the internal registry without HTTPS.
    /// </summary>
    private async Task ConfigureInsecureRegistryAsync(Server server, CancellationToken cancellationToken = default)
    {
        if (!_registryOptions.Enabled)
        {
            _logger.LogDebug("Registry is disabled, skipping insecure-registry configuration for {ServerName}", server.Name);
            return;
        }

        if (_registryOptions.Secure)
        {
            _logger.LogDebug("Registry uses HTTPS, skipping insecure-registry configuration for {ServerName}", server.Name);
            return;
        }

        try
        {
            _logger.LogInformation("Configuring insecure-registry {RegistryUrl} on {ServerName}",
                _registryOptions.Url, server.Name);

            // Read current daemon.json
            var readDaemonConfig = await _sshService.ExecuteCommandAsync(
                server,
                "cat /etc/docker/daemon.json 2>/dev/null || echo '{}'",
                cancellationToken);

            var daemonConfigJson = string.IsNullOrWhiteSpace(readDaemonConfig.Output) ? "{}" : readDaemonConfig.Output.Trim();

            // Parse as JSON
            var daemonConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(daemonConfigJson)
                ?? new Dictionary<string, JsonElement>();

            // Get or create insecure-registries array
            List<string> insecureRegistries;
            if (daemonConfig.TryGetValue("insecure-registries", out var existingRegistries)
                && existingRegistries.ValueKind == JsonValueKind.Array)
            {
                insecureRegistries = JsonSerializer.Deserialize<List<string>>(existingRegistries.GetRawText())
                    ?? new List<string>();
            }
            else
            {
                insecureRegistries = new List<string>();
            }

            // Add our registry if not already present
            if (!insecureRegistries.Contains(_registryOptions.Url))
            {
                insecureRegistries.Add(_registryOptions.Url);
                _logger.LogInformation("Adding {RegistryUrl} to insecure-registries on {ServerName}",
                    _registryOptions.Url, server.Name);
            }
            else
            {
                _logger.LogDebug("Registry {RegistryUrl} already in insecure-registries on {ServerName}",
                    _registryOptions.Url, server.Name);
                return; // No change needed
            }

            // Update daemon config
            daemonConfig["insecure-registries"] = JsonSerializer.SerializeToElement(insecureRegistries);

            // Serialize back to JSON
            var updatedDaemonConfigJson = JsonSerializer.Serialize(daemonConfig, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Write updated daemon.json
            var escapedJson = updatedDaemonConfigJson.Replace("\"", "\\\"").Replace("\n", "\\n");
            var writeDaemonConfig = await _sshService.ExecuteCommandAsync(
                server,
                $"echo \"{escapedJson}\" | sudo tee /etc/docker/daemon.json > /dev/null",
                cancellationToken);

            if (writeDaemonConfig.ExitCode != 0)
            {
                _logger.LogError("Failed to write daemon.json on {ServerName}: {Error}",
                    server.Name, writeDaemonConfig.Error);
                return;
            }

            // Restart Docker daemon
            _logger.LogInformation("Restarting Docker daemon on {ServerName} to apply insecure-registry configuration",
                server.Name);

            var restartDocker = await _sshService.ExecuteCommandAsync(
                server,
                "sudo systemctl restart docker",
                cancellationToken);

            if (restartDocker.ExitCode == 0)
            {
                _logger.LogInformation("Successfully configured insecure-registry on {ServerName}", server.Name);

                // Wait for Docker to be ready after restart
                await Task.Delay(3000, cancellationToken);
            }
            else
            {
                _logger.LogError("Failed to restart Docker on {ServerName}: {Error}",
                    server.Name, restartDocker.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring insecure-registry on {ServerName}", server.Name);
            // Don't throw - this is a best-effort configuration
        }
    }

    /// <summary>
    /// Labels a Swarm node with availability zone and datacenter for HA placement.
    /// Labels are used by placement strategies to distribute replicas across zones/datacenters.
    /// </summary>
    private async Task LabelSwarmNodeAsync(Server server, Server managerNode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(server.SwarmNodeId))
        {
            _logger.LogWarning("Cannot label node {ServerName} - SwarmNodeId not set", server.Name);
            return;
        }

        try
        {
            var labelsToApply = new Dictionary<string, string>();

            // Add availability zone label
            if (!string.IsNullOrWhiteSpace(server.AvailabilityZone))
            {
                labelsToApply["zone"] = server.AvailabilityZone;
            }

            // Add datacenter label
            if (!string.IsNullOrWhiteSpace(server.Datacenter))
            {
                labelsToApply["datacenter"] = server.Datacenter;
            }

            if (labelsToApply.Count == 0)
            {
                _logger.LogDebug("No zone/datacenter configured for {ServerName}, skipping node labels", server.Name);
                return;
            }

            _logger.LogInformation("Labeling node {ServerName} ({NodeId}) with: {Labels}",
                server.Name, server.SwarmNodeId, string.Join(", ", labelsToApply.Select(kv => $"{kv.Key}={kv.Value}")));

            // Apply labels via SSH
            foreach (var label in labelsToApply)
            {
                var labelCommand = $"docker node update --label-add {label.Key}={label.Value} {server.SwarmNodeId}";
                var result = await _sshService.ExecuteCommandAsync(managerNode, labelCommand, cancellationToken);

                if (result.ExitCode != 0)
                {
                    _logger.LogWarning("Failed to label node {ServerName} with {Label}: {Error}",
                        server.Name, $"{label.Key}={label.Value}", result.Error);
                }
                else
                {
                    _logger.LogInformation("Successfully labeled node {ServerName} with {Label}",
                        server.Name, $"{label.Key}={label.Value}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error labeling node {ServerName}", server.Name);
            // Don't throw - labeling is best-effort
        }
    }
}
