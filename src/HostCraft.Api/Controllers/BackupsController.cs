using HostCraft.Api.Models.Backups;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BackupsController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupsController> _logger;

    public BackupsController(IBackupService backupService, ILogger<BackupsController> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    #region Application-Level Backups

    /// <summary>
    /// Get all backups for an application.
    /// </summary>
    [HttpGet("applications/{applicationId}")]
    public async Task<IActionResult> GetBackups(int applicationId, CancellationToken cancellationToken)
    {
        var backups = await _backupService.GetBackupsAsync(applicationId, cancellationToken);
        return Ok(backups.Select(b => BackupDto.FromEntity(b)));
    }

    /// <summary>
    /// Create a configuration backup for an application.
    /// </summary>
    [HttpPost("applications/{applicationId}/configuration")]
    public async Task<IActionResult> BackupConfiguration(int applicationId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.BackupConfigurationAsync(applicationId, cancellationToken);
        return Ok(BackupDto.FromEntity(backup));
    }

    /// <summary>
    /// Create a volume backup for an application.
    /// </summary>
    [HttpPost("applications/{applicationId}/volumes")]
    public async Task<IActionResult> BackupVolumes(int applicationId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.BackupVolumesAsync(applicationId, cancellationToken);
        return Ok(BackupDto.FromEntity(backup));
    }

    /// <summary>
    /// Create a full backup (configuration + volumes) for an application.
    /// </summary>
    [HttpPost("applications/{applicationId}/full")]
    public async Task<IActionResult> CreateFullBackup(int applicationId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.CreateFullBackupAsync(applicationId, cancellationToken);
        return Ok(BackupDto.FromEntity(backup));
    }

    #endregion

    #region System-Wide Backups

    /// <summary>
    /// Create a system-wide backup with specified scope.
    /// </summary>
    [HttpPost("system")]
    public async Task<IActionResult> CreateSystemBackup(
        [FromBody] CreateSystemBackupRequest request,
        CancellationToken cancellationToken)
    {
        var backup = await _backupService.CreateSystemBackupAsync(
            request.Scope,
            request.BackupConfigurationId,
            User.Identity?.Name ?? "api",
            cancellationToken);

        return Ok(BackupDto.FromEntity(backup));
    }

    /// <summary>
    /// Get backup manifest (metadata about backup contents).
    /// </summary>
    [HttpGet("{backupId}/manifest")]
    public async Task<IActionResult> GetBackupManifest(int backupId, CancellationToken cancellationToken)
    {
        // The manifest is stored in the Backup entity's ManifestJson field
        // We'll need to add a method to IBackupService to retrieve it
        // For now, return a placeholder
        return Ok(new { message = "Manifest retrieval not yet implemented" });
    }

    /// <summary>
    /// Verify backup integrity using checksums.
    /// </summary>
    [HttpPost("{backupId}/verify")]
    public async Task<IActionResult> VerifyBackup(int backupId, CancellationToken cancellationToken)
    {
        var isValid = await _backupService.VerifyBackupIntegrityAsync(backupId, cancellationToken);
        return Ok(new
        {
            backupId,
            isValid,
            message = isValid ? "Backup integrity verified" : "Backup integrity check failed"
        });
    }

    #endregion

    #region Restore Operations

    /// <summary>
    /// Analyze restore requirements (what inputs are needed from user).
    /// </summary>
    [HttpGet("{backupId}/restore/analyze")]
    public async Task<IActionResult> AnalyzeRestoreRequirements(int backupId, CancellationToken cancellationToken)
    {
        var requirements = await _backupService.AnalyzeRestoreRequirementsAsync(backupId, cancellationToken);
        return Ok(requirements);
    }

    /// <summary>
    /// Restore from a backup with full configuration options.
    /// </summary>
    [HttpPost("{backupId}/restore")]
    public async Task<IActionResult> RestoreFromBackup(
        int backupId,
        [FromBody] RestoreRequest request,
        CancellationToken cancellationToken)
    {
        var restoreOperation = await _backupService.RestoreFromBackupAsync(
            backupId,
            request.RestoreScope,
            request.Strategy,
            request.Mapping,
            User.Identity?.Name ?? "api",
            cancellationToken);

        return Ok(new
        {
            restoreOperationId = restoreOperation.Id,
            uuid = restoreOperation.Uuid,
            status = restoreOperation.Status.ToString(),
            message = "Restore operation started"
        });
    }

    /// <summary>
    /// Get restore operation status and progress.
    /// </summary>
    [HttpGet("restore/{restoreOperationId}")]
    public async Task<IActionResult> GetRestoreOperation(int restoreOperationId, CancellationToken cancellationToken)
    {
        var operation = await _backupService.GetRestoreOperationAsync(restoreOperationId, cancellationToken);
        if (operation == null)
        {
            return NotFound(new { error = "Restore operation not found" });
        }

        return Ok(new
        {
            id = operation.Id,
            uuid = operation.Uuid,
            backupId = operation.BackupId,
            restoreScope = operation.RestoreScope.ToString(),
            strategy = operation.Strategy.ToString(),
            status = operation.Status.ToString(),
            projectsRestored = operation.ProjectsRestored,
            applicationsRestored = operation.ApplicationsRestored,
            serversRestored = operation.ServersRestored,
            startedAt = operation.StartedAt,
            completedAt = operation.CompletedAt,
            errorMessage = operation.ErrorMessage,
            triggeredBy = operation.TriggeredBy
        });
    }

    #endregion

    #region Storage Provider Operations

    /// <summary>
    /// Upload backup to configured storage provider.
    /// </summary>
    [HttpPost("{backupId}/upload")]
    public async Task<IActionResult> UploadToStorage(
        int backupId,
        [FromBody] UploadToStorageRequest request,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<BackupProgress>(p =>
        {
            _logger.LogInformation("Upload progress: {BytesProcessed}/{TotalBytes} - {Status}",
                p.BytesProcessed, p.TotalBytes, p.Status);
        });

        var success = await _backupService.UploadToStorageAsync(
            backupId,
            request.BackupConfigurationId,
            progress,
            cancellationToken);

        return Ok(new
        {
            success,
            message = success ? "Upload completed successfully" : "Upload failed"
        });
    }

    /// <summary>
    /// Download backup from storage provider.
    /// </summary>
    [HttpPost("{backupId}/download")]
    public async Task<IActionResult> DownloadFromStorage(int backupId, CancellationToken cancellationToken)
    {
        var progress = new Progress<BackupProgress>(p =>
        {
            _logger.LogInformation("Download progress: {BytesProcessed}/{TotalBytes} - {Status}",
                p.BytesProcessed, p.TotalBytes, p.Status);
        });

        var success = await _backupService.DownloadFromStorageAsync(backupId, progress, cancellationToken);

        return Ok(new
        {
            success,
            message = success ? "Download completed successfully" : "Download failed"
        });
    }

    /// <summary>
    /// List backups available in remote storage provider.
    /// </summary>
    [HttpGet("storage/{backupConfigurationId}/list")]
    public async Task<IActionResult> ListRemoteBackups(int backupConfigurationId, CancellationToken cancellationToken)
    {
        var remoteBackups = await _backupService.ListRemoteBackupsAsync(backupConfigurationId, cancellationToken);
        return Ok(remoteBackups);
    }

    #endregion

    #region Backup Listing & Management

    /// <summary>
    /// Get all system-wide backups (excludes application-specific backups).
    /// </summary>
    [HttpGet("system/list")]
    public async Task<IActionResult> ListSystemBackups(CancellationToken cancellationToken)
    {
        var backups = await _backupService.GetSystemBackupsAsync(cancellationToken);
        return Ok(backups.Select(b => BackupDto.FromEntity(b)));
    }

    /// <summary>
    /// Get a specific backup by ID.
    /// </summary>
    [HttpGet("{backupId}")]
    public async Task<IActionResult> GetBackup(int backupId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);
        if (backup == null)
        {
            return NotFound(new { error = "Backup not found" });
        }

        return Ok(BackupDto.FromEntity(backup));
    }

    /// <summary>
    /// Upload a backup file to import into the system.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(10L * 1024 * 1024 * 1024)] // 10GB max
    public async Task<IActionResult> UploadBackup(IFormFile file, CancellationToken cancellationToken)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }

            // Validate file extension
            if (!file.FileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Only .tar.gz backup files are supported" });
            }

            // Create temporary file to store upload
            var tempPath = Path.Combine(Path.GetTempPath(), $"backup-upload-{Guid.NewGuid()}.tar.gz");

            try
            {
                // Save uploaded file to temp location
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                // Register the backup in the system
                var backup = await _backupService.ImportUploadedBackupAsync(
                    tempPath,
                    file.FileName,
                    User.Identity?.Name ?? "upload",
                    cancellationToken);

                return Ok(new
                {
                    backupId = backup.Id,
                    uuid = backup.Uuid,
                    message = "Backup uploaded and registered successfully"
                });
            }
            finally
            {
                // Clean up temp file
                if (System.IO.File.Exists(tempPath))
                {
                    System.IO.File.Delete(tempPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading backup file");
            return StatusCode(500, new { error = "Failed to upload backup", details = ex.Message });
        }
    }

    /// <summary>
    /// Download a local backup file to the user's computer.
    /// </summary>
    [HttpGet("{backupId}/download-file")]
    public async Task<IActionResult> DownloadBackupFile(int backupId, CancellationToken cancellationToken)
    {
        try
        {
            // Get backup metadata
            var backup = await _backupService.GetBackupAsync(backupId, cancellationToken);
            if (backup == null)
            {
                return NotFound(new { error = "Backup not found" });
            }

            // Verify backup integrity before download
            var isValid = await _backupService.VerifyBackupIntegrityAsync(backupId, cancellationToken);
            if (!isValid)
            {
                return BadRequest(new { error = "Backup integrity check failed. File may be corrupted." });
            }

            // Get file path and stream to browser
            var fileStream = await _backupService.GetBackupFileStreamAsync(backupId, cancellationToken);
            if (fileStream == null)
            {
                return NotFound(new { error = "Backup file not found on server" });
            }

            // Determine filename from backup path or generate from metadata
            var fileName = !string.IsNullOrEmpty(backup.StoragePath)
                ? System.IO.Path.GetFileName(backup.StoragePath)
                : $"hostcraft-backup-{backup.Id}-{backup.StartedAt:yyyyMMdd-HHmmss}.tar.gz";

            // Stream file to browser with proper headers
            return File(fileStream, "application/gzip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading backup file {BackupId}", backupId);
            return StatusCode(500, new { error = "Failed to download backup file", details = ex.Message });
        }
    }

    #endregion

    #region Maintenance Operations

    /// <summary>
    /// Prune expired backups based on retention policy.
    /// </summary>
    [HttpPost("prune")]
    public async Task<IActionResult> PruneExpiredBackups(CancellationToken cancellationToken)
    {
        var deletedCount = await _backupService.PruneExpiredBackupsAsync(cancellationToken);
        return Ok(new
        {
            deletedCount,
            message = $"Pruned {deletedCount} expired backups"
        });
    }

    #endregion
}
