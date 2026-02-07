using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing GlusterFS distributed storage across Swarm manager nodes.
/// Handles installation, volume creation, and replication setup.
/// </summary>
public interface IGlusterFsService
{
    /// <summary>
    /// Install GlusterFS server on a manager node.
    /// </summary>
    Task<bool> InstallServerAsync(Server server, CancellationToken cancellationToken = default);

    /// <summary>
    /// Install GlusterFS client on a node (manager or worker).
    /// </summary>
    Task<bool> InstallClientAsync(Server server, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if GlusterFS server is installed on a node.
    /// </summary>
    Task<bool> IsServerInstalledAsync(Server server, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if GlusterFS client is installed on a node.
    /// </summary>
    Task<bool> IsClientInstalledAsync(Server server, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a GlusterFS trusted pool by peering all manager nodes.
    /// Should be called after all managers have glusterfs-server installed.
    /// </summary>
    Task<bool> CreateTrustedPoolAsync(List<Server> managers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a replicated GlusterFS volume across manager nodes.
    /// </summary>
    Task<bool> CreateReplicatedVolumeAsync(
        List<Server> managers,
        string volumeName,
        int replicaCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Start a GlusterFS volume.
    /// </summary>
    Task<bool> StartVolumeAsync(Server managerNode, string volumeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop a GlusterFS volume.
    /// </summary>
    Task<bool> StopVolumeAsync(Server managerNode, string volumeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get status of a GlusterFS volume.
    /// </summary>
    Task<GlusterVolumeStatus?> GetVolumeStatusAsync(Server managerNode, string volumeName, CancellationToken cancellationToken = default);

    /// <summary>
    /// List all GlusterFS volumes.
    /// </summary>
    Task<List<string>> ListVolumesAsync(Server managerNode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initialize GlusterFS on the first manager (install, create volume, start).
    /// This is called when initializing a Swarm on the first manager.
    /// </summary>
    Task<GlusterSetupResult> InitializeOnFirstManagerAsync(Server firstManager, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new manager to the GlusterFS cluster.
    /// This is called when a new manager joins the Swarm.
    /// </summary>
    Task<GlusterSetupResult> AddManagerToClusterAsync(Server newManager, List<Server> existingManagers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get GlusterFS volume driver options for Docker volume creation.
    /// Returns driver_opts dictionary for use with Docker volumes.
    /// </summary>
    Dictionary<string, string> GetVolumeDriverOptions(string volumePath, string? glusterVolumeName = null);
}

/// <summary>
/// Status of a GlusterFS volume.
/// </summary>
public record GlusterVolumeStatus(
    string Name,
    string Type,
    string Status,
    int BrickCount,
    int ReplicaCount);

/// <summary>
/// Result of GlusterFS setup operation.
/// </summary>
public record GlusterSetupResult(
    bool Success,
    string Message,
    bool ServerInstalled = false,
    bool ClientInstalled = false,
    bool PoolCreated = false,
    bool VolumeCreated = false,
    bool VolumeStarted = false,
    string? ErrorDetails = null);
