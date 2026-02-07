using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a deployed application (container, service, or stack).
/// </summary>
public class Application
{
    public int Id { get; set; }

    /// <summary>
    /// When populated, identifies the database template type that created this application.
    /// Allows downstream systems to tailor proxy behavior.
    /// </summary>
    public DatabaseType? DatabaseType { get; set; }
    
    public Guid Uuid { get; set; }
    
    public required string Name { get; set; }
    
    public string? Description { get; set; }
    
    public int ProjectId { get; set; }
    
    public int ServerId { get; set; }
    
    public int? GitProviderId { get; set; }
    
    // Source configuration
    public ApplicationSourceType SourceType { get; set; }
    
    public string? GitRepository { get; set; }
    
    public string? GitBranch { get; set; } = "main";
    
    /// <summary>
    /// Repository owner/organization name
    /// </summary>
    public string? GitOwner { get; set; }
    
    /// <summary>
    /// Repository name (without owner)
    /// </summary>
    public string? GitRepoName { get; set; }
    
    public string? DockerImage { get; set; }

    /// <summary>
    /// When true, the image must be pulled from a private registry using stored credentials.
    /// </summary>
    public bool UsePrivateRegistry { get; set; }

    /// <summary>
    /// Registry server/hostname (e.g., registry.example.com or ghcr.io).
    /// </summary>
    public string? RegistryServer { get; set; }

    /// <summary>
    /// Username for the private registry (if required).
    /// </summary>
    public string? RegistryUsername { get; set; }

    /// <summary>
    /// Password or token for the private registry. Stored encrypted at rest.
    /// </summary>
    public string? RegistryPassword { get; set; }
    
    public string? DockerComposeFile { get; set; }
    
    public string? Dockerfile { get; set; } = "Dockerfile";
    
    public string? BuildContext { get; set; } = ".";
    
    /// <summary>
    /// Build args to pass to Docker build (format: KEY1=VALUE1,KEY2=VALUE2)
    /// </summary>
    public string? BuildArgs { get; set; }

    /// <summary>
    /// Target stage for multi-stage Docker builds (e.g., "production", "builder")
    /// </summary>
    public string? DockerBuildTarget { get; set; }

    /// <summary>
    /// Build-time secrets that should not be persisted in image history.
    /// Format: KEY1=VALUE1,KEY2=VALUE2. Injected via --secret during build.
    /// </summary>
    public string? BuildSecrets { get; set; }

    /// <summary>
    /// Enable Git LFS (Large File Storage) when cloning
    /// </summary>
    public bool EnableGitLfs { get; set; } = true;

    /// <summary>
    /// Watch specific paths for changes (comma-separated). If null, watches all files.
    /// </summary>
    public string? WatchPaths { get; set; }
    
    /// <summary>
    /// Auto-deploy on push to configured branch
    /// </summary>
    public bool AutoDeployOnPush { get; set; } = true;
    
    /// <summary>
    /// Enable preview deployments for pull requests
    /// </summary>
    public bool EnablePreviewDeployments { get; set; } = false;

    /// <summary>
    /// URL template for preview deployments. Supports: {{pr_id}}, {{branch}}, {{commit}}, {{app_name}}
    /// Example: "{{pr_id}}.preview.example.com" or "preview-{{pr_id}}-{{app_name}}.example.com"
    /// </summary>
    public string? PreviewUrlTemplate { get; set; }

    /// <summary>
    /// Maximum number of concurrent preview deployments per application
    /// </summary>
    public int MaxPreviewDeployments { get; set; } = 3;

    /// <summary>
    /// Only create preview deployments for PRs with these labels (comma-separated).
    /// Empty means all PRs get previews.
    /// </summary>
    public string? PreviewLabels { get; set; }

    /// <summary>
    /// Unique token for webhook authentication
    /// </summary>
    public string? WebhookSecret { get; set; }
    
    /// <summary>
    /// Last commit SHA deployed
    /// </summary>
    public string? LastCommitSha { get; set; }
    
    /// <summary>
    /// Last commit message
    /// </summary>
    public string? LastCommitMessage { get; set; }
    
    /// <summary>
    /// Clone submodules when pulling from Git
    /// </summary>
    public bool CloneSubmodules { get; set; } = false;
    
    // Deployment configuration
    public string? Domain { get; set; }
    
    /// <summary>
    /// Additional domains (aliases) for this application
    /// </summary>
    public string? AdditionalDomains { get; set; } // Comma-separated
    
    /// <summary>
    /// Enable HTTPS with automatic SSL certificate provisioning
    /// </summary>
    public bool EnableHttps { get; set; } = true;
    
    /// <summary>
    /// Force redirect HTTP to HTTPS
    /// </summary>
    public bool ForceHttps { get; set; } = true;
    
    /// <summary>
    /// Let's Encrypt email for certificate notifications
    /// </summary>
    public string? LetsEncryptEmail { get; set; }
    
    /// <summary>
    /// Path for HTTP-01 challenge (used by cert providers)
    /// </summary>
    public string? CertificateChallengePath { get; set; }
    
    /// <summary>
    /// Primary internal container port (legacy, use PortMappings for multiple ports)
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// External/published port on the host (legacy, use PortMappings for multiple ports)
    /// </summary>
    public int? PublishedPort { get; set; }

    /// <summary>
    /// Port mappings as JSON array: [{"HostPort":8080,"ContainerPort":80,"Protocol":"tcp"}]
    /// </summary>
    public string? PortMappings { get; set; }

    public int Replicas { get; set; } = 1;

    public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.Container;

    /// <summary>
    /// Deployment strategy: Rolling (zero-downtime, HA/DR default), BlueGreen (instant rollback), or Recreate (causes downtime)
    /// </summary>
    public DeploymentStrategy DeploymentStrategy { get; set; } = DeploymentStrategy.Rolling;

    public long? MemoryLimitBytes { get; set; }
    
    public long? CpuLimit { get; set; }
    
    public bool AutoDeploy { get; set; }
    
    public string? HealthCheckUrl { get; set; }
    
    public int HealthCheckIntervalSeconds { get; set; } = 60;
    
    public int HealthCheckTimeoutSeconds { get; set; } = 10;
    
    public int MaxConsecutiveFailures { get; set; } = 3;
    
    public bool AutoRestart { get; set; } = true;
    
    public bool AutoRollback { get; set; } = true;
    
    public string? BackupSchedule { get; set; } // Cron expression
    
    public int? BackupRetentionDays { get; set; } = 30;
    
    // Docker Swarm configuration
    /// <summary>
    /// Number of service replicas (overrides Replicas for swarm mode)
    /// </summary>
    public int? SwarmReplicas { get; set; }

    /// <summary>
    /// Placement strategy for distributing replicas across nodes.
    /// Default: Spread (HA/DR recommended - distributes replicas evenly)
    /// </summary>
    public PlacementStrategy PlacementStrategy { get; set; } = PlacementStrategy.Spread;

    /// <summary>
    /// Placement constraints for swarm services (JSON array of strings)
    /// Example: ["node.role==manager", "node.labels.region==us-east"]
    /// Used when PlacementStrategy = Custom
    /// </summary>
    public string? SwarmPlacementConstraints { get; set; }

    /// <summary>
    /// Placement preferences for distributing replicas (JSON array)
    /// Example: [{"Spread":"node.id"}] for even distribution across all nodes
    /// Example: [{"Spread":"node.labels.zone"}] for distribution across availability zones
    /// Auto-populated based on PlacementStrategy if not specified.
    /// </summary>
    public string? SwarmPlacementPreferences { get; set; }

    /// <summary>
    /// Maximum number of replicas per node (null = unlimited)
    /// Default: 1 for HA/DR (ensures maximum distribution)
    /// Set to null or higher value to allow multiple replicas per node
    /// </summary>
    public int? MaxReplicasPerNode { get; set; } = 1;
    
    /// <summary>
    /// Update configuration for swarm services (JSON object)
    /// Controls rolling update behavior
    /// </summary>
    public string? SwarmUpdateConfig { get; set; }
    
    /// <summary>
    /// Rollback configuration for swarm services (JSON object)
    /// Controls automatic rollback on failure
    /// </summary>
    public string? SwarmRollbackConfig { get; set; }
    
    /// <summary>
    /// Service mode: "replicated" or "global"
    /// </summary>
    public string? SwarmMode { get; set; } = "replicated";
    
    /// <summary>
    /// Endpoint specification for swarm services (JSON object)
    /// Controls port publishing mode (ingress, host)
    /// </summary>
    public string? SwarmEndpointSpec { get; set; }
    
    /// <summary>
    /// Network configuration for swarm services (JSON array)
    /// </summary>
    public string? SwarmNetworks { get; set; }
    
    /// <summary>
    /// Stop grace period in nanoseconds for swarm services
    /// Time to wait before force-killing a container
    /// </summary>
    public long? SwarmStopGracePeriod { get; set; }
    
    /// <summary>
    /// Docker Swarm service ID (if deployed as a service)
    /// </summary>
    public string? SwarmServiceId { get; set; }

    // Volume/Storage configuration
    /// <summary>
    /// Volume driver type for persistent storage: "local", "glusterfs", "nfs"
    /// - local: Node-local storage (default, no HA)
    /// - glusterfs: Replicated storage across manager nodes (HA/DR recommended)
    /// - nfs: NFS shared storage (simple HA, single point of failure)
    /// </summary>
    public string VolumeDriver { get; set; } = "local";

    /// <summary>
    /// Volume driver options as JSON object.
    /// For GlusterFS: {"glusterserver":"manager1.local","glustervolume":"hostcraft-volumes","path":"app-data"}
    /// For NFS: {"type":"nfs","o":"addr=nfs-server,rw,nfsvers=4","device":":/nfs/hostcraft/app-data"}
    /// </summary>
    public string? VolumeDriverOptions { get; set; }

    /// <summary>
    /// Whether to automatically use shared storage for Swarm deployments.
    /// When true and Storage.Type is glusterfs/nfs, volumes will use shared storage.
    /// </summary>
    public bool UseSharedStorage { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    
    public DateTime? LastDeployedAt { get; set; }
    
    public DateTime? LastHealthCheckAt { get; set; }
    
    public int ConsecutiveHealthCheckFailures { get; set; }
    
    // Navigation properties
    public Project Project { get; set; } = null!;
    
    public Server Server { get; set; } = null!;
    
    public GitProvider? GitProvider { get; set; }
    
    public ICollection<EnvironmentVariable> EnvironmentVariables { get; set; } = new List<EnvironmentVariable>();
    
    public ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();
    
    public ICollection<Volume> Volumes { get; set; } = new List<Volume>();
    
    public ICollection<Backup> Backups { get; set; } = new List<Backup>();
    
    public ICollection<HealthCheck> HealthChecks { get; set; } = new List<HealthCheck>();
    
    public ICollection<Certificate> Certificates { get; set; } = new List<Certificate>();

    public ICollection<Domain> Domains { get; set; } = new List<Domain>();

    /// <summary>
    /// Environment variables for Docker Compose deployments
    /// </summary>
    public ICollection<ComposeEnvironmentVariable> ComposeVariables { get; set; } = new List<ComposeEnvironmentVariable>();

    /// <summary>
    /// Optional user-provided Traefik label overrides stored as a JSON object.
    /// Applied on top of generated labels for advanced routing needs.
    /// </summary>
    public string? TraefikLabelOverrides { get; set; }

    // Computed properties
    public bool IsSwarmMode => Server.IsSwarm;

    /// <summary>
    /// Whether this application should be deployed as a Swarm service.
    /// </summary>
    public bool DeployAsService => DeploymentMode == DeploymentMode.Service && Server.Type == ServerType.SwarmManager;

    /// <summary>
    /// Whether replicas/scaling is supported for this deployment.
    /// </summary>
    public bool SupportsScaling => DeployAsService;

    /// <summary>
    /// Unique service/container name used in Docker.
    /// Format: {project-name}-{app-name}-{short-uuid}
    /// Ensures no naming collisions in Docker Swarm or standalone deployments.
    /// </summary>
    public string ServiceName
    {
        get
        {
            var projectName = NormalizeForDocker(Project?.Name ?? "default");
            var appName = NormalizeForDocker(Name);
            var shortId = Uuid.ToString("N")[..8]; // First 8 chars of UUID (no hyphens)
            return $"{projectName}-{appName}-{shortId}";
        }
    }

    /// <summary>
    /// Normalizes a name for Docker compatibility.
    /// Docker service/container names must match: [a-zA-Z0-9][a-zA-Z0-9_.-]*
    /// </summary>
    private static string NormalizeForDocker(string name)
    {
        // Convert to lowercase and replace invalid chars with hyphen
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            name.ToLowerInvariant(),
            "[^a-z0-9_.-]",
            "-");
        // Collapse multiple hyphens and trim edges
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, "-+", "-").Trim('-');
        // Ensure we have a valid name
        return string.IsNullOrWhiteSpace(normalized) ? "app" : normalized;
    }
}
