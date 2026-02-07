using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a backup - either application-specific or system-wide.
/// Supports complete HostCraft instance backups for disaster recovery.
/// </summary>
public class Backup
{
    public int Id { get; set; }

    public Guid Uuid { get; set; }

    /// <summary>
    /// Application ID (null for system-wide backups)
    /// </summary>
    public int? ApplicationId { get; set; }

    /// <summary>
    /// Backup configuration used for this backup
    /// </summary>
    public int? BackupConfigurationId { get; set; }

    public BackupType Type { get; set; }

    public BackupStatus Status { get; set; }

    /// <summary>
    /// What was included in this backup (for system backups)
    /// </summary>
    public BackupScope Scope { get; set; } = BackupScope.Complete;

    /// <summary>
    /// Local temporary storage path during backup creation
    /// </summary>
    public string? StoragePath { get; set; }

    /// <summary>
    /// Remote storage path/key where backup is stored
    /// </summary>
    public string? RemoteStoragePath { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>
    /// Legacy S3 bucket (deprecated - use BackupConfiguration instead)
    /// </summary>
    public string? S3Bucket { get; set; }

    /// <summary>
    /// Legacy S3 key (deprecated - use RemoteStoragePath instead)
    /// </summary>
    public string? S3Key { get; set; }

    /// <summary>
    /// Backup manifest (metadata about what's in the backup)
    /// Stored as JSON
    /// </summary>
    public string? ManifestJson { get; set; }

    /// <summary>
    /// SHA256 checksum of the backup package for integrity verification
    /// </summary>
    public string? Checksum { get; set; }

    /// <summary>
    /// Is this backup verified as valid and restorable?
    /// </summary>
    public bool IsVerified { get; set; }

    /// <summary>
    /// Is this backup encrypted?
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// Is this backup compressed?
    /// </summary>
    public bool IsCompressed { get; set; }

    /// <summary>
    /// Number of projects included (for system backups)
    /// </summary>
    public int ProjectCount { get; set; }

    /// <summary>
    /// Number of applications included
    /// </summary>
    public int ApplicationCount { get; set; }

    /// <summary>
    /// Number of servers included (for system backups)
    /// </summary>
    public int ServerCount { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    public int? RetentionDays { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Who triggered this backup (username or "system" for automated backups)
    /// </summary>
    public string? TriggeredBy { get; set; }

    // Navigation properties
    public Application? Application { get; set; }
    public BackupConfiguration? BackupConfiguration { get; set; }
}
