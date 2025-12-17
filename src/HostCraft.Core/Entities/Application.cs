using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a deployed application (container, service, or stack).
/// </summary>
public class Application
{
    public int Id { get; set; }
    
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
    
    public string? DockerComposeFile { get; set; }
    
    public string? Dockerfile { get; set; } = "Dockerfile";
    
    public string? BuildContext { get; set; } = ".";
    
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
    
    public int? Port { get; set; }
    
    public int Replicas { get; set; } = 1;
    
    public DeploymentMode DeploymentMode { get; set; } = DeploymentMode.Container;
    
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
}
