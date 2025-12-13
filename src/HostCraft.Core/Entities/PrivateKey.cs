namespace HostCraft.Core.Entities;

/// <summary>
/// Represents an SSH private key used for server authentication.
/// </summary>
public class PrivateKey
{
    public int Id { get; set; }
    
    public required string Name { get; set; }
    
    public required string KeyData { get; set; }
    
    public string? Passphrase { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<Server> Servers { get; set; } = new List<Server>();
}
