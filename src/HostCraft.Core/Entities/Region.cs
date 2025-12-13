namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a geographic region or datacenter for HA/DR deployments.
/// </summary>
public class Region
{
    public int Id { get; set; }
    
    public required string Name { get; set; }
    
    public required string Code { get; set; } // e.g., "eu-west-1", "us-east-1"
    
    public string? Description { get; set; }
    
    public bool IsPrimary { get; set; }
    
    public int Priority { get; set; } // For failover ordering
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}
