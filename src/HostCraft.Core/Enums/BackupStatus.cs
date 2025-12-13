namespace HostCraft.Core.Enums;

/// <summary>
/// Status of a backup operation.
/// </summary>
public enum BackupStatus
{
    /// <summary>
    /// Backup is queued and waiting to start.
    /// </summary>
    Queued = 0,
    
    /// <summary>
    /// Backup is currently in progress.
    /// </summary>
    Running = 1,
    
    /// <summary>
    /// Backup completed successfully.
    /// </summary>
    Success = 2,
    
    /// <summary>
    /// Backup failed with errors.
    /// </summary>
    Failed = 3,
    
    /// <summary>
    /// Backup is being uploaded to remote storage.
    /// </summary>
    Uploading = 4,
    
    /// <summary>
    /// Backup expired and was deleted.
    /// </summary>
    Expired = 5
}
