namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a Docker volume for persistent data storage.
/// </summary>
public class Volume
{
    public int Id { get; set; }
    
    public Guid Uuid { get; set; }
    
    public required string Name { get; set; }
    
    public int? ApplicationId { get; set; }
    
    public int ServerId { get; set; }
    
    public string? Driver { get; set; } = "local";
    
    public string? MountPoint { get; set; }
    
    public long SizeBytes { get; set; }
    
    public bool IsBackedUp { get; set; }
    
    public string? BackupSchedule { get; set; } // Cron expression
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public Application? Application { get; set; }
    
    public Server Server { get; set; } = null!;
    
    public ICollection<Backup> Backups { get; set; } = new List<Backup>();
}
