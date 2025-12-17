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
    
    /// <summary>
    /// Git commit SHA being deployed (alias for CommitHash)
    /// </summary>
    public string? CommitSha
    {
        get => CommitHash;
        set => CommitHash = value;
    }
    
    /// <summary>
    /// Git commit message
    /// </summary>
    public string? CommitMessage { get; set; }
    
    /// <summary>
    /// Git commit author
    /// </summary>
    public string? CommitAuthor { get; set; }
    
    /// <summary>
    /// Who/what triggered this deployment
    /// </summary>
    public string? TriggeredBy { get; set; }
    
    /// <summary>
    /// Whether this is a preview deployment (e.g., for a PR)
    /// </summary>
    public bool IsPreview { get; set; } = false;
    
    /// <summary>
    /// Preview identifier (e.g., "pr-123")
    /// </summary>
    public string? PreviewId { get; set; }
    
    /// <summary>
    /// Timestamp when deployment was created/queued
    /// </summary>
    public DateTime CreatedAt { get; set; }
    
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
