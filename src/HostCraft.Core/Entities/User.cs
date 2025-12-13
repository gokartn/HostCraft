namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a user account in HostCraft.
/// </summary>
public class User
{
    public int Id { get; set; }
    
    public Guid Uuid { get; set; }
    
    public required string Email { get; set; }
    
    public required string PasswordHash { get; set; }
    
    public string? Name { get; set; }
    
    public bool IsAdmin { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public DateTime? LastLoginAt { get; set; }
}
