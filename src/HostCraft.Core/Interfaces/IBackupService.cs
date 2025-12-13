using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for backup and restore operations with S3 support.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a backup of application configuration.
    /// </summary>
    Task<Backup> BackupConfigurationAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a backup of application volumes.
    /// </summary>
    Task<Backup> BackupVolumesAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a full backup (configuration + volumes).
    /// </summary>
    Task<Backup> CreateFullBackupAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Restores application from backup.
    /// </summary>
    Task<bool> RestoreFromBackupAsync(int backupId, int targetServerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads backup to S3-compatible storage.
    /// </summary>
    Task<bool> UploadToS3Async(int backupId, string bucket, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads backup from S3-compatible storage.
    /// </summary>
    Task<bool> DownloadFromS3Async(int backupId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes expired backups based on retention policy.
    /// </summary>
    Task<int> PruneExpiredBackupsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all backups for an application.
    /// </summary>
    Task<IEnumerable<Backup>> GetBackupsAsync(int applicationId, CancellationToken cancellationToken = default);
}
