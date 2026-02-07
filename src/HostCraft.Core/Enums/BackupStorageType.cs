namespace HostCraft.Core.Enums;

/// <summary>
/// Types of backup storage providers supported by HostCraft.
/// </summary>
public enum BackupStorageType
{
    /// <summary>
    /// S3-compatible storage (AWS S3, MinIO, DigitalOcean Spaces, Backblaze B2, etc.)
    /// </summary>
    S3Compatible = 0,

    /// <summary>
    /// Google Drive cloud storage
    /// </summary>
    GoogleDrive = 1,

    /// <summary>
    /// Local filesystem (for testing or local backups)
    /// </summary>
    LocalFileSystem = 2
}
