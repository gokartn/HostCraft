using HostCraft.Api.Models.Backups;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace HostCraft.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BackupConfigurationsController : ControllerBase
{
    private readonly IBackupService _backupService;
    private readonly ILogger<BackupConfigurationsController> _logger;

    public BackupConfigurationsController(
        IBackupService backupService,
        ILogger<BackupConfigurationsController> logger)
    {
        _backupService = backupService;
        _logger = logger;
    }

    /// <summary>
    /// Get all backup configurations.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var configurations = await _backupService.GetBackupConfigurationsAsync(cancellationToken);
        return Ok(configurations.Select(c => BackupConfigurationDto.FromEntity(c)));
    }

    /// <summary>
    /// Get a specific backup configuration by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var configuration = await _backupService.GetBackupConfigurationAsync(id, cancellationToken);
        if (configuration == null)
        {
            return NotFound(new { error = "Backup configuration not found" });
        }

        return Ok(BackupConfigurationDto.FromEntity(configuration));
    }

    /// <summary>
    /// Create a new backup configuration.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBackupConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and serialize the provider configuration
            string providerConfigJson = request.StorageType switch
            {
                BackupStorageType.S3Compatible => JsonSerializer.Serialize(request.S3Config),
                BackupStorageType.GoogleDrive => JsonSerializer.Serialize(request.GoogleDriveConfig),
                BackupStorageType.LocalFileSystem => JsonSerializer.Serialize(request.LocalFileSystemConfig),
                _ => throw new ArgumentException($"Unsupported storage type: {request.StorageType}")
            };

            var configuration = new BackupConfiguration
            {
                Name = request.Name,
                StorageType = request.StorageType,
                IsActive = request.IsActive,
                IsDefault = request.IsDefault,
                ProviderConfiguration = providerConfigJson,
                RetentionDays = request.RetentionDays,
                AutoBackupEnabled = request.AutoBackupEnabled,
                AutoBackupSchedule = request.AutoBackupSchedule,
                AutoBackupScope = request.AutoBackupScope,
                EnableCompression = request.EnableCompression,
                EnableEncryption = request.EnableEncryption,
                EncryptionKey = request.EncryptionKey,
                VerifyAfterUpload = request.VerifyAfterUpload
            };

            var created = await _backupService.CreateBackupConfigurationAsync(configuration, cancellationToken);

            return CreatedAtAction(
                nameof(Get),
                new { id = created.Id },
                BackupConfigurationDto.FromEntity(created));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup configuration");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing backup configuration.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateBackupConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate and serialize the provider configuration
            string providerConfigJson = request.StorageType switch
            {
                BackupStorageType.S3Compatible => JsonSerializer.Serialize(request.S3Config),
                BackupStorageType.GoogleDrive => JsonSerializer.Serialize(request.GoogleDriveConfig),
                BackupStorageType.LocalFileSystem => JsonSerializer.Serialize(request.LocalFileSystemConfig),
                _ => throw new ArgumentException($"Unsupported storage type: {request.StorageType}")
            };

            var configuration = new BackupConfiguration
            {
                Id = id,
                Name = request.Name,
                StorageType = request.StorageType,
                IsActive = request.IsActive,
                IsDefault = request.IsDefault,
                ProviderConfiguration = providerConfigJson,
                RetentionDays = request.RetentionDays,
                AutoBackupEnabled = request.AutoBackupEnabled,
                AutoBackupSchedule = request.AutoBackupSchedule,
                AutoBackupScope = request.AutoBackupScope,
                EnableCompression = request.EnableCompression,
                EnableEncryption = request.EnableEncryption,
                EncryptionKey = request.EncryptionKey,
                VerifyAfterUpload = request.VerifyAfterUpload
            };

            var updated = await _backupService.UpdateBackupConfigurationAsync(configuration, cancellationToken);

            return Ok(BackupConfigurationDto.FromEntity(updated));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update backup configuration {Id}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a backup configuration.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _backupService.DeleteBackupConfigurationAsync(id, cancellationToken);
            if (!deleted)
            {
                return NotFound(new { error = "Backup configuration not found" });
            }

            return Ok(new { message = "Backup configuration deleted successfully" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete backup configuration {Id}", id);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Test connection to a backup storage provider.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<IActionResult> TestConnection(int id, CancellationToken cancellationToken)
    {
        try
        {
            var success = await _backupService.TestBackupConfigurationAsync(id, cancellationToken);

            return Ok(new
            {
                success,
                message = success
                    ? "Connection test successful"
                    : "Connection test failed - check logs for details"
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to test backup configuration {Id}", id);
            return Ok(new
            {
                success = false,
                message = $"Connection test failed: {ex.Message}"
            });
        }
    }
}
