namespace HostCraft.Core.Entities;

/// <summary>
/// Represents an environment variable for an application.
/// </summary>
public class EnvironmentVariable
{
    public int Id { get; set; }
    
    public int ApplicationId { get; set; }
    
    public required string Key { get; set; }
    
    public required string Value { get; set; }
    
    public bool IsSecret { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public Application Application { get; set; } = null!;
}
