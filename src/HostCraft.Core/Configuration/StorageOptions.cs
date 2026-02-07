namespace HostCraft.Core.Configuration;

/// <summary>
/// Configuration options for distributed storage (GlusterFS, NFS, cloud volumes).
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Storage backend type: "local", "glusterfs", "nfs", "cloud"
    /// </summary>
    public string Type { get; set; } = "local";

    /// <summary>
    /// GlusterFS-specific configuration
    /// </summary>
    public GlusterFsOptions GlusterFs { get; set; } = new();

    /// <summary>
    /// NFS-specific configuration
    /// </summary>
    public NfsOptions Nfs { get; set; } = new();
}

/// <summary>
/// GlusterFS configuration for replicated storage across manager nodes.
/// </summary>
public class GlusterFsOptions
{
    /// <summary>
    /// Enable GlusterFS auto-setup and management.
    /// When true, HostCraft will install and configure GlusterFS on all manager nodes.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Name of the GlusterFS volume for HostCraft data.
    /// Default: "hostcraft-volumes"
    /// </summary>
    public string VolumeName { get; set; } = "hostcraft-volumes";

    /// <summary>
    /// Replica count (should match number of managers for full HA).
    /// Default: 3 for standard 3-manager setup.
    /// </summary>
    public int ReplicaCount { get; set; } = 3;

    /// <summary>
    /// Brick path on each manager node where GlusterFS stores data.
    /// Default: /data/gluster/hostcraft
    /// </summary>
    public string BrickPath { get; set; } = "/data/gluster/hostcraft";

    /// <summary>
    /// Mount point on nodes where GlusterFS volume is mounted.
    /// Default: /mnt/gluster/hostcraft
    /// </summary>
    public string MountPath { get; set; } = "/mnt/gluster/hostcraft";

    /// <summary>
    /// Auto-install glusterfs-server on manager nodes when they join.
    /// </summary>
    public bool AutoInstallServer { get; set; } = true;

    /// <summary>
    /// Auto-install glusterfs-client on all nodes (managers and workers).
    /// </summary>
    public bool AutoInstallClient { get; set; } = true;
}

/// <summary>
/// NFS configuration for simple shared storage (not replicated).
/// </summary>
public class NfsOptions
{
    /// <summary>
    /// Enable NFS (simple, non-replicated storage).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// NFS server hostname or IP address.
    /// Example: "10.0.0.1" or "nfs-server.local"
    /// </summary>
    public string Server { get; set; } = "localhost";

    /// <summary>
    /// NFS export path on the server.
    /// Example: "/nfs/hostcraft"
    /// </summary>
    public string ExportPath { get; set; } = "/nfs/hostcraft";

    /// <summary>
    /// NFS version (3 or 4).
    /// </summary>
    public int Version { get; set; } = 4;

    /// <summary>
    /// Auto-install nfs-common on all nodes.
    /// </summary>
    public bool AutoInstallClient { get; set; } = true;
}
