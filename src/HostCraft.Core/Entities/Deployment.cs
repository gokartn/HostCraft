using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a deployment operation for an application.
/// </summary>
public class Deployment
{
    public int Id { get; set; }
    
    public Guid Uuid { get; set; }
    
    public int ApplicationId { get; set; }
    
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Queued;
    
    public string? CommitHash { get; set; }
    
    public string? ImageTag { get; set; }
    
    public string? ImageDigest { get; set; }
    
    public DateTime StartedAt { get; set; }
    
    public DateTime? FinishedAt { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public string? ContainerId { get; set; }
    
    public string? ServiceId { get; set; }
    
    // Navigation properties
    public Application Application { get; set; } = null!;
    
    public ICollection<DeploymentLog> Logs { get; set; } = new List<DeploymentLog>();
    
    // Computed property
    public TimeSpan? Duration => FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;
}
