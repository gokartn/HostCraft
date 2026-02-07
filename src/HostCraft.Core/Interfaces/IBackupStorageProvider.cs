using HostCraft.Core.Entities;
using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Abstraction for backup storage providers (S3, Google Drive, Local FS, etc.)
/// </summary>
public interface IBackupStorageProvider
{
    /// <summary>
    /// Upload a backup package to remote storage
    /// </summary>
    Task<string> UploadBackupAsync(
        string localBackupPath,
        BackupManifest manifest,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a backup package from remote storage
    /// </summary>
    Task<string> DownloadBackupAsync(
        string remoteBackupPath,
        string localDestinationPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List available backups in storage
    /// </summary>
    Task<List<RemoteBackupInfo>> ListBackupsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a backup from storage
    /// </summary>
    Task<bool> DeleteBackupAsync(
        string remoteBackupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a backup exists and is accessible
    /// </summary>
    Task<bool> VerifyBackupExistsAsync(
        string remoteBackupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get backup manifest without downloading full backup
    /// </summary>
    Task<BackupManifest?> GetBackupManifestAsync(
        string remoteBackupPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Test connection to storage provider
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a remote backup
/// </summary>
public record RemoteBackupInfo(
    string Path,
    string BackupId,
    DateTime CreatedAt,
    long SizeBytes,
    string? Checksum);

/// <summary>
/// Progress information for backup/restore operations
/// </summary>
public record BackupProgress(
    long BytesProcessed,
    long TotalBytes,
    string CurrentFile,
    string Status);
