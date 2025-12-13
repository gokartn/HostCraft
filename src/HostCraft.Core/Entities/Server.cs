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
}
