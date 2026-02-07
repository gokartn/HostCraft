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
    /// Backup is currently in progress (collecting data, creating archives).
    /// </summary>
    Running = 1,

    /// <summary>
    /// Backup is being uploaded to remote storage (S3, Google Drive).
    /// </summary>
    Uploading = 2,

    /// <summary>
    /// Backup is being verified (checksums, integrity checks).
    /// </summary>
    Verifying = 3,

    /// <summary>
    /// Backup completed successfully and verified.
    /// </summary>
    Success = 4,

    /// <summary>
    /// Backup failed with errors.
    /// </summary>
    Failed = 5,

    /// <summary>
    /// Backup verification failed (corrupt backup).
    /// </summary>
    VerificationFailed = 6,

    /// <summary>
    /// Backup was cancelled by user.
    /// </summary>
    Cancelled = 7,

    /// <summary>
    /// Backup expired and was deleted per retention policy.
    /// </summary>
    Expired = 8
}
