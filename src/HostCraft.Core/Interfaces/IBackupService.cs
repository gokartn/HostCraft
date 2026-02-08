using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for comprehensive backup and restore operations.
/// Supports application-level and system-wide backups with multiple storage providers.
/// </summary>
public interface IBackupService
{
    // Application-level backups

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
    /// Lists all backups for an application.
    /// </summary>
    Task<IEnumerable<Backup>> GetBackupsAsync(int applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all system-wide backups (ApplicationId is null).
    /// </summary>
    Task<IEnumerable<Backup>> GetSystemBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific backup by ID.
    /// </summary>
    Task<Backup?> GetBackupAsync(int backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a file stream for downloading a backup file from the server.
    /// </summary>
    Task<Stream?> GetBackupFileStreamAsync(int backupId, CancellationToken cancellationToken = default);

    // System-wide backups (new comprehensive backup system)

    /// <summary>
    /// Creates a complete system backup with specified scope.
    /// </summary>
    Task<Backup> CreateSystemBackupAsync(
        BackupScope scope,
        int? backupConfigurationId = null,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a backup manifest describing the contents of a backup.
    /// </summary>
    Task<BackupManifest> GenerateManifestAsync(
        Backup backup,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies backup integrity using checksums.
    /// </summary>
    Task<bool> VerifyBackupIntegrityAsync(
        int backupId,
        CancellationToken cancellationToken = default);

    // Restore operations

    /// <summary>
    /// Restores from a backup with server configuration mapping.
    /// </summary>
    Task<RestoreOperation> RestoreFromBackupAsync(
        int backupId,
        BackupScope restoreScope,
        RestoreStrategy strategy,
        RestoreMapping? mapping = null,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes a backup and determines required user inputs for restore.
    /// </summary>
    Task<RestoreRequiredInput> AnalyzeRestoreRequirementsAsync(
        int backupId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets restore operation status and progress.
    /// </summary>
    Task<RestoreOperation?> GetRestoreOperationAsync(
        int restoreOperationId,
        CancellationToken cancellationToken = default);

    // Storage provider operations

    /// <summary>
    /// Uploads backup to configured storage provider.
    /// </summary>
    Task<bool> UploadToStorageAsync(
        int backupId,
        int backupConfigurationId,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads backup from storage provider.
    /// </summary>
    Task<bool> DownloadFromStorageAsync(
        int backupId,
        IProgress<BackupProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists backups available in storage provider.
    /// </summary>
    Task<List<RemoteBackupInfo>> ListRemoteBackupsAsync(
        int backupConfigurationId,
        CancellationToken cancellationToken = default);

    // Backup configuration management

    /// <summary>
    /// Gets all backup configurations.
    /// </summary>
    Task<List<BackupConfiguration>> GetBackupConfigurationsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific backup configuration by ID.
    /// </summary>
    Task<BackupConfiguration?> GetBackupConfigurationAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new backup configuration.
    /// </summary>
    Task<BackupConfiguration> CreateBackupConfigurationAsync(
        BackupConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing backup configuration.
    /// </summary>
    Task<BackupConfiguration> UpdateBackupConfigurationAsync(
        BackupConfiguration configuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a backup configuration.
    /// </summary>
    Task<bool> DeleteBackupConfigurationAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests connection to a backup storage provider.
    /// </summary>
    Task<bool> TestBackupConfigurationAsync(
        int id,
        CancellationToken cancellationToken = default);

    // Maintenance operations

    /// <summary>
    /// Deletes a backup and its associated file from disk.
    /// </summary>
    Task<bool> DeleteBackupAsync(int backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes expired backups based on retention policy.
    /// </summary>
    Task<int> PruneExpiredBackupsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates SHA256 checksum for a backup file.
    /// </summary>
    Task<string> CalculateChecksumAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports an uploaded backup file into the system.
    /// </summary>
    Task<Backup> ImportUploadedBackupAsync(
        string uploadedFilePath,
        string originalFileName,
        string uploadedBy,
        CancellationToken cancellationToken = default);
}
