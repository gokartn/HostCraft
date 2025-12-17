using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a Docker host (server) that can run containers or Swarm services.
/// </summary>
public class Server
{
    public int Id { get; set; }
    
    public required string Name { get; set; }
    
    public required string Host { get; set; }
    
    public int Port { get; set; } = 22;
    
    public string Username { get; set; } = "root";
    
    public int? PrivateKeyId { get; set; }
    
    public ServerType Type { get; set; } = ServerType.Standalone;
    
    public ServerStatus Status { get; set; } = ServerStatus.Offline;
    
    public ProxyType ProxyType { get; set; } = ProxyType.None;
    
    /// <summary>
    /// Default Let's Encrypt email for all applications on this server
    /// </summary>
    public string? DefaultLetsEncryptEmail { get; set; }
    
    /// <summary>
    /// Proxy/web server version (e.g., "v3.2.1" for Traefik)
    /// </summary>
    public string? ProxyVersion { get; set; }
    
    /// <summary>
    /// When the proxy was last deployed/updated
    /// </summary>
    public DateTime? ProxyDeployedAt { get; set; }
    
    public string? SwarmJoinToken { get; set; }
    
    public string? SwarmManagerAddress { get; set; }
    
    public int? RegionId { get; set; }
    
    public bool IsSwarmManager { get; set; }
    
    public int? SwarmManagerCount { get; set; } // Total managers in cluster
    
    public int? SwarmWorkerCount { get; set; } // Total workers in cluster
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? LastHealthCheck { get; set; }
    
    public int ConsecutiveFailures { get; set; }
    
    public DateTime? LastFailureAt { get; set; }
    
    // Navigation properties
    public PrivateKey? PrivateKey { get; set; }
    
    public Region? Region { get; set; }
    
    public ICollection<Application> Applications { get; set; } = new List<Application>();
    
    // Computed properties
    public bool IsSwarm => Type == ServerType.SwarmManager || Type == ServerType.SwarmWorker;
    
    public bool CanDeployApplications => Type != ServerType.SwarmWorker;
    
    public bool CanManageSwarm => Type == ServerType.SwarmManager;
    
    /// <summary>
    /// Whether this server can deploy applications as Swarm services.
    /// </summary>
    public bool CanDeployAsService => Type == ServerType.SwarmManager;
    
    /// <summary>
    /// Whether this server can deploy applications as standalone containers.
    /// Swarm workers cannot deploy standalone containers (only services assigned by managers).
    /// </summary>
    public bool CanDeployAsContainer => Type == ServerType.Standalone || Type == ServerType.SwarmManager;
}
