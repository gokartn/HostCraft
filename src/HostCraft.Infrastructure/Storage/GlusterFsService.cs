using HostCraft.Core.Configuration;
using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HostCraft.Infrastructure.Storage;

/// <summary>
/// Implementation of GlusterFS management for distributed storage across Swarm managers.
/// </summary>
public class GlusterFsService : IGlusterFsService
{
    private readonly ISshService _sshService;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<GlusterFsService> _logger;

    public GlusterFsService(
        ISshService sshService,
        IOptions<StorageOptions> storageOptions,
        ILogger<GlusterFsService> logger)
    {
        _sshService = sshService;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public async Task<bool> InstallServerAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Installing GlusterFS server on {ServerName}", server.Name);

            // Check if already installed
            if (await IsServerInstalledAsync(server, cancellationToken))
            {
                _logger.LogInformation("GlusterFS server already installed on {ServerName}", server.Name);
                return true;
            }

            // Install glusterfs-server (Ubuntu/Debian)
            var installCommand = @"
sudo DEBIAN_FRONTEND=noninteractive apt-get update && \
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y glusterfs-server && \
sudo systemctl enable glusterd && \
sudo systemctl start glusterd
";

            var result = await _sshService.ExecuteCommandAsync(server, installCommand, cancellationToken);

            if (result.ExitCode == 0)
            {
                _logger.LogInformation("Successfully installed GlusterFS server on {ServerName}", server.Name);

                // Wait for glusterd to be fully ready
                await Task.Delay(2000, cancellationToken);
                return true;
            }
            else
            {
                _logger.LogError("Failed to install GlusterFS server on {ServerName}: {Error}",
                    server.Name, result.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing GlusterFS server on {ServerName}", server.Name);
            return false;
        }
    }

    public async Task<bool> InstallClientAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Installing GlusterFS client on {ServerName}", server.Name);

            // Check if already installed
            if (await IsClientInstalledAsync(server, cancellationToken))
            {
                _logger.LogInformation("GlusterFS client already installed on {ServerName}", server.Name);
                return true;
            }

            // Install glusterfs-client
            var installCommand = @"
sudo DEBIAN_FRONTEND=noninteractive apt-get update && \
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y glusterfs-client
";

            var result = await _sshService.ExecuteCommandAsync(server, installCommand, cancellationToken);

            if (result.ExitCode == 0)
            {
                _logger.LogInformation("Successfully installed GlusterFS client on {ServerName}", server.Name);
                return true;
            }
            else
            {
                _logger.LogError("Failed to install GlusterFS client on {ServerName}: {Error}",
                    server.Name, result.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing GlusterFS client on {ServerName}", server.Name);
            return false;
        }
    }

    public async Task<bool> IsServerInstalledAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _sshService.ExecuteCommandAsync(
                server,
                "which glusterd && systemctl is-active glusterd",
                cancellationToken);

            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsClientInstalledAsync(Server server, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _sshService.ExecuteCommandAsync(
                server,
                "which glusterfs",
                cancellationToken);

            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CreateTrustedPoolAsync(List<Server> managers, CancellationToken cancellationToken = default)
    {
        if (managers.Count < 2)
        {
            _logger.LogWarning("Cannot create trusted pool with less than 2 managers");
            return true; // Not an error, just nothing to do
        }

        try
        {
            var firstManager = managers[0];
            _logger.LogInformation("Creating GlusterFS trusted pool from {FirstManager}", firstManager.Name);

            // Peer probe all other managers from the first manager
            foreach (var manager in managers.Skip(1))
            {
                _logger.LogInformation("Peering {ManagerName} ({ManagerHost}) from {FirstManager}",
                    manager.Name, manager.Host, firstManager.Name);

                var probeCommand = $"sudo gluster peer probe {manager.Host}";
                var result = await _sshService.ExecuteCommandAsync(firstManager, probeCommand, cancellationToken);

                if (result.ExitCode != 0 && !result.Output.Contains("already in peer list"))
                {
                    _logger.LogError("Failed to peer {ManagerName}: {Error}", manager.Name, result.Error);
                    return false;
                }

                _logger.LogInformation("Successfully peered {ManagerName}", manager.Name);
            }

            // Wait for peer probe to complete
            await Task.Delay(2000, cancellationToken);

            // Verify pool status
            var statusResult = await _sshService.ExecuteCommandAsync(
                firstManager,
                "sudo gluster peer status",
                cancellationToken);

            _logger.LogInformation("GlusterFS peer status:\n{Status}", statusResult.Output);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating GlusterFS trusted pool");
            return false;
        }
    }

    public async Task<bool> CreateReplicatedVolumeAsync(
        List<Server> managers,
        string volumeName,
        int replicaCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var firstManager = managers[0];
            _logger.LogInformation("Creating GlusterFS replicated volume {VolumeName} with {ReplicaCount} replicas",
                volumeName, replicaCount);

            // Create brick directories on all managers
            var brickPath = _storageOptions.GlusterFs.BrickPath;
            foreach (var manager in managers)
            {
                var mkdirCommand = $"sudo mkdir -p {brickPath}";
                var result = await _sshService.ExecuteCommandAsync(manager, mkdirCommand, cancellationToken);

                if (result.ExitCode != 0)
                {
                    _logger.LogError("Failed to create brick directory on {ManagerName}: {Error}",
                        manager.Name, result.Error);
                    return false;
                }
            }

            // Build brick list: manager1:/path manager2:/path manager3:/path
            var bricks = string.Join(" ", managers.Select(m => $"{m.Host}:{brickPath}"));

            // Create volume
            var createVolumeCommand = $"sudo gluster volume create {volumeName} replica {replicaCount} {bricks} force";
            var createResult = await _sshService.ExecuteCommandAsync(firstManager, createVolumeCommand, cancellationToken);

            if (createResult.ExitCode != 0 && !createResult.Output.Contains("already exists"))
            {
                _logger.LogError("Failed to create GlusterFS volume {VolumeName}: {Error}",
                    volumeName, createResult.Error);
                return false;
            }

            _logger.LogInformation("Successfully created GlusterFS volume {VolumeName}", volumeName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating GlusterFS replicated volume {VolumeName}", volumeName);
            return false;
        }
    }

    public async Task<bool> StartVolumeAsync(Server managerNode, string volumeName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting GlusterFS volume {VolumeName} on {ServerName}",
                volumeName, managerNode.Name);

            var startCommand = $"sudo gluster volume start {volumeName}";
            var result = await _sshService.ExecuteCommandAsync(managerNode, startCommand, cancellationToken);

            if (result.ExitCode != 0 && !result.Output.Contains("already started"))
            {
                _logger.LogError("Failed to start GlusterFS volume {VolumeName}: {Error}",
                    volumeName, result.Error);
                return false;
            }

            _logger.LogInformation("Successfully started GlusterFS volume {VolumeName}", volumeName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting GlusterFS volume {VolumeName}", volumeName);
            return false;
        }
    }

    public async Task<bool> StopVolumeAsync(Server managerNode, string volumeName, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopCommand = $"sudo gluster volume stop {volumeName}";
            var result = await _sshService.ExecuteCommandAsync(managerNode, stopCommand, cancellationToken);

            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping GlusterFS volume {VolumeName}", volumeName);
            return false;
        }
    }

    public async Task<GlusterVolumeStatus?> GetVolumeStatusAsync(Server managerNode, string volumeName, CancellationToken cancellationToken = default)
    {
        try
        {
            var infoCommand = $"sudo gluster volume info {volumeName}";
            var result = await _sshService.ExecuteCommandAsync(managerNode, infoCommand, cancellationToken);

            if (result.ExitCode != 0)
                return null;

            // Parse output (simple parsing, could be improved)
            var output = result.Output;
            var status = output.Contains("Status: Started") ? "Started" : "Stopped";
            var type = output.Contains("Type: Replicate") ? "Replicate" : "Unknown";

            return new GlusterVolumeStatus(
                Name: volumeName,
                Type: type,
                Status: status,
                BrickCount: 0, // Could parse from output
                ReplicaCount: _storageOptions.GlusterFs.ReplicaCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting GlusterFS volume status for {VolumeName}", volumeName);
            return null;
        }
    }

    public async Task<List<string>> ListVolumesAsync(Server managerNode, CancellationToken cancellationToken = default)
    {
        try
        {
            var listCommand = "sudo gluster volume list";
            var result = await _sshService.ExecuteCommandAsync(managerNode, listCommand, cancellationToken);

            if (result.ExitCode != 0)
                return new List<string>();

            return result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing GlusterFS volumes");
            return new List<string>();
        }
    }

    public async Task<GlusterSetupResult> InitializeOnFirstManagerAsync(Server firstManager, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing GlusterFS on first manager {ServerName}", firstManager.Name);

            // Install server
            var serverInstalled = await InstallServerAsync(firstManager, cancellationToken);
            if (!serverInstalled)
            {
                return new GlusterSetupResult(
                    false,
                    "Failed to install GlusterFS server",
                    ServerInstalled: false);
            }

            // Install client (for mounting)
            var clientInstalled = await InstallClientAsync(firstManager, cancellationToken);
            if (!clientInstalled)
            {
                return new GlusterSetupResult(
                    false,
                    "Failed to install GlusterFS client",
                    ServerInstalled: true,
                    ClientInstalled: false);
            }

            _logger.LogInformation("GlusterFS installed on first manager {ServerName}. Waiting for more managers to create replicated volume.", firstManager.Name);

            return new GlusterSetupResult(
                true,
                "GlusterFS installed on first manager. Add more managers to create replicated volume.",
                ServerInstalled: true,
                ClientInstalled: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing GlusterFS on first manager");
            return new GlusterSetupResult(
                false,
                "Error initializing GlusterFS",
                ErrorDetails: ex.Message);
        }
    }

    public async Task<GlusterSetupResult> AddManagerToClusterAsync(
        Server newManager,
        List<Server> existingManagers,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Adding {ServerName} to GlusterFS cluster", newManager.Name);

            // Install server on new manager
            var serverInstalled = await InstallServerAsync(newManager, cancellationToken);
            if (!serverInstalled)
            {
                return new GlusterSetupResult(
                    false,
                    "Failed to install GlusterFS server on new manager",
                    ServerInstalled: false);
            }

            // Install client
            var clientInstalled = await InstallClientAsync(newManager, cancellationToken);
            if (!clientInstalled)
            {
                return new GlusterSetupResult(
                    false,
                    "Failed to install GlusterFS client on new manager",
                    ServerInstalled: true,
                    ClientInstalled: false);
            }

            // Combine all managers (existing + new)
            var allManagers = existingManagers.Concat(new[] { newManager }).ToList();

            // Create trusted pool
            var poolCreated = await CreateTrustedPoolAsync(allManagers, cancellationToken);
            if (!poolCreated)
            {
                return new GlusterSetupResult(
                    false,
                    "Failed to create GlusterFS trusted pool",
                    ServerInstalled: true,
                    ClientInstalled: true,
                    PoolCreated: false);
            }

            // Check if we have enough managers for the configured replica count
            var volumeName = _storageOptions.GlusterFs.VolumeName;
            var replicaCount = _storageOptions.GlusterFs.ReplicaCount;

            if (allManagers.Count >= replicaCount)
            {
                // Check if volume already exists
                var existingVolumes = await ListVolumesAsync(existingManagers[0], cancellationToken);
                var volumeExists = existingVolumes.Contains(volumeName);

                if (!volumeExists)
                {
                    // Create replicated volume
                    _logger.LogInformation("Creating GlusterFS volume {VolumeName} with {Count} managers",
                        volumeName, allManagers.Count);

                    var volumeCreated = await CreateReplicatedVolumeAsync(
                        allManagers.Take(replicaCount).ToList(),
                        volumeName,
                        replicaCount,
                        cancellationToken);

                    if (!volumeCreated)
                    {
                        return new GlusterSetupResult(
                            false,
                            "Failed to create GlusterFS volume",
                            ServerInstalled: true,
                            ClientInstalled: true,
                            PoolCreated: true,
                            VolumeCreated: false);
                    }

                    // Start volume
                    var volumeStarted = await StartVolumeAsync(existingManagers[0], volumeName, cancellationToken);
                    if (!volumeStarted)
                    {
                        return new GlusterSetupResult(
                            false,
                            "Failed to start GlusterFS volume",
                            ServerInstalled: true,
                            ClientInstalled: true,
                            PoolCreated: true,
                            VolumeCreated: true,
                            VolumeStarted: false);
                    }

                    return new GlusterSetupResult(
                        true,
                        $"GlusterFS cluster ready with {replicaCount}-way replication across {allManagers.Count} managers",
                        ServerInstalled: true,
                        ClientInstalled: true,
                        PoolCreated: true,
                        VolumeCreated: true,
                        VolumeStarted: true);
                }
                else
                {
                    _logger.LogInformation("GlusterFS volume {VolumeName} already exists", volumeName);

                    return new GlusterSetupResult(
                        true,
                        "Manager added to existing GlusterFS cluster",
                        ServerInstalled: true,
                        ClientInstalled: true,
                        PoolCreated: true,
                        VolumeCreated: true,
                        VolumeStarted: true);
                }
            }
            else
            {
                _logger.LogInformation(
                    "GlusterFS cluster has {CurrentCount} managers, need {RequiredCount} for configured replica count",
                    allManagers.Count, replicaCount);

                return new GlusterSetupResult(
                    true,
                    $"Manager added to cluster. Need {replicaCount - allManagers.Count} more managers for {replicaCount}-way replication.",
                    ServerInstalled: true,
                    ClientInstalled: true,
                    PoolCreated: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding manager to GlusterFS cluster");
            return new GlusterSetupResult(
                false,
                "Error adding manager to GlusterFS cluster",
                ErrorDetails: ex.Message);
        }
    }

    public Dictionary<string, string> GetVolumeDriverOptions(string volumePath, string? glusterVolumeName = null)
    {
        var volumeName = glusterVolumeName ?? _storageOptions.GlusterFs.VolumeName;

        // For local driver with GlusterFS type
        return new Dictionary<string, string>
        {
            ["type"] = "glusterfs",
            ["o"] = "rw",
            ["device"] = $"localhost:/{volumeName}/{volumePath}"
        };
    }
}
