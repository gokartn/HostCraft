using HostCraft.Core.Interfaces;
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

    /// <summary>
    /// Get all backups for an application.
    /// </summary>
    [HttpGet("applications/{applicationId}")]
    public async Task<IActionResult> GetBackups(int applicationId, CancellationToken cancellationToken)
    {
        var backups = await _backupService.GetBackupsAsync(applicationId, cancellationToken);
        return Ok(backups.Select(b => new BackupDto
        {
            Id = b.Id,
            Uuid = b.Uuid,
            ApplicationId = b.ApplicationId,
            Type = b.Type.ToString(),
            Status = b.Status.ToString(),
            StoragePath = b.StoragePath,
            SizeBytes = b.SizeBytes,
            S3Bucket = b.S3Bucket,
            S3Key = b.S3Key,
            StartedAt = b.StartedAt,
            CompletedAt = b.CompletedAt,
            ErrorMessage = b.ErrorMessage,
            RetentionDays = b.RetentionDays,
            ExpiresAt = b.ExpiresAt
        }));
    }

    /// <summary>
    /// Create a configuration backup.
    /// </summary>
    [HttpPost("applications/{applicationId}/configuration")]
    public async Task<IActionResult> BackupConfiguration(int applicationId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.BackupConfigurationAsync(applicationId, cancellationToken);
        return Ok(new BackupDto
        {
            Id = backup.Id,
            Uuid = backup.Uuid,
            ApplicationId = backup.ApplicationId,
            Type = backup.Type.ToString(),
            Status = backup.Status.ToString(),
            StoragePath = backup.StoragePath,
            SizeBytes = backup.SizeBytes,
            StartedAt = backup.StartedAt,
            CompletedAt = backup.CompletedAt,
            ErrorMessage = backup.ErrorMessage,
            ExpiresAt = backup.ExpiresAt
        });
    }

    /// <summary>
    /// Create a volume backup.
    /// </summary>
    [HttpPost("applications/{applicationId}/volumes")]
    public async Task<IActionResult> BackupVolumes(int applicationId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.BackupVolumesAsync(applicationId, cancellationToken);
        return Ok(new BackupDto
        {
            Id = backup.Id,
            Uuid = backup.Uuid,
            ApplicationId = backup.ApplicationId,
            Type = backup.Type.ToString(),
            Status = backup.Status.ToString(),
            StoragePath = backup.StoragePath,
            SizeBytes = backup.SizeBytes,
            StartedAt = backup.StartedAt,
            CompletedAt = backup.CompletedAt,
            ErrorMessage = backup.ErrorMessage,
            ExpiresAt = backup.ExpiresAt
        });
    }

    /// <summary>
    /// Create a full backup (configuration + volumes).
    /// </summary>
    [HttpPost("applications/{applicationId}/full")]
    public async Task<IActionResult> CreateFullBackup(int applicationId, CancellationToken cancellationToken)
    {
        var backup = await _backupService.CreateFullBackupAsync(applicationId, cancellationToken);
        return Ok(new BackupDto
        {
            Id = backup.Id,
            Uuid = backup.Uuid,
            ApplicationId = backup.ApplicationId,
            Type = backup.Type.ToString(),
            Status = backup.Status.ToString(),
            StoragePath = backup.StoragePath,
            SizeBytes = backup.SizeBytes,
            StartedAt = backup.StartedAt,
            CompletedAt = backup.CompletedAt,
            ErrorMessage = backup.ErrorMessage,
            ExpiresAt = backup.ExpiresAt
        });
    }

    /// <summary>
    /// Restore from a backup.
    /// </summary>
    [HttpPost("{backupId}/restore")]
    public async Task<IActionResult> RestoreFromBackup(int backupId, [FromQuery] int targetServerId, CancellationToken cancellationToken)
    {
        var success = await _backupService.RestoreFromBackupAsync(backupId, targetServerId, cancellationToken);
        return Ok(new { success, message = success ? "Restore completed" : "Restore failed" });
    }

    /// <summary>
    /// Upload backup to S3.
    /// </summary>
    [HttpPost("{backupId}/upload-s3")]
    public async Task<IActionResult> UploadToS3(int backupId, [FromBody] S3UploadRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Bucket))
        {
            return BadRequest(new { error = "Bucket is required" });
        }

        var success = await _backupService.UploadToS3Async(backupId, request.Bucket, cancellationToken);
        return Ok(new { success, message = success ? "Upload completed" : "Upload failed" });
    }

    /// <summary>
    /// Download backup from S3.
    /// </summary>
    [HttpPost("{backupId}/download-s3")]
    public async Task<IActionResult> DownloadFromS3(int backupId, CancellationToken cancellationToken)
    {
        var success = await _backupService.DownloadFromS3Async(backupId, cancellationToken);
        return Ok(new { success, message = success ? "Download completed" : "Download failed" });
    }

    /// <summary>
    /// Prune expired backups.
    /// </summary>
    [HttpPost("prune")]
    public async Task<IActionResult> PruneExpiredBackups(CancellationToken cancellationToken)
    {
        var deletedCount = await _backupService.PruneExpiredBackupsAsync(cancellationToken);
        return Ok(new { deletedCount, message = $"Pruned {deletedCount} expired backups" });
    }
}

public class BackupDto
{
    public int Id { get; set; }
    public Guid Uuid { get; set; }
    public int ApplicationId { get; set; }
    public required string Type { get; set; }
    public required string Status { get; set; }
    public string? StoragePath { get; set; }
    public long SizeBytes { get; set; }
    public string? S3Bucket { get; set; }
    public string? S3Key { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RetentionDays { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public record S3UploadRequest(string Bucket);
