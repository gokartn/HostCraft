namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a logical grouping of applications (tenant/workspace).
/// </summary>
public class Project
{
    public int Id { get; set; }
    
    public Guid Uuid { get; set; }
    
    public required string Name { get; set; }
    
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
