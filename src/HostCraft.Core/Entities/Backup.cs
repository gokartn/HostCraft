using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a backup of application data, configuration, or volumes.
/// </summary>
public class Backup
{
    public int Id { get; set; }
    
    public Guid Uuid { get; set; }
    
    public int ApplicationId { get; set; }
    
    public BackupType Type { get; set; }
    
    public BackupStatus Status { get; set; }
    
    public string? StoragePath { get; set; }
    
    public long SizeBytes { get; set; }
    
    public string? S3Bucket { get; set; }
    
    public string? S3Key { get; set; }
    
    public DateTime StartedAt { get; set; }
    
    public DateTime? CompletedAt { get; set; }
    
    public string? ErrorMessage { get; set; }
    
    public int? RetentionDays { get; set; }
    
    public DateTime? ExpiresAt { get; set; }
    
    // Navigation properties
    public Application Application { get; set; } = null!;
}
