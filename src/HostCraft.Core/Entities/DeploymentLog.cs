namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a log entry from a deployment operation.
/// </summary>
public class DeploymentLog
{
    public int Id { get; set; }
    
    public int DeploymentId { get; set; }
    
    public required string Message { get; set; }
    
    public string Level { get; set; } = "Info";
    
    public DateTime Timestamp { get; set; }
    
    // Navigation properties
    public Deployment Deployment { get; set; } = null!;
}
