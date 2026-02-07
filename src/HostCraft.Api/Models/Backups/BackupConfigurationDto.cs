using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using System.Text.Json;

namespace HostCraft.Api.Models.Backups;

public class BackupConfigurationDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string StorageType { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public required string ProviderConfiguration { get; set; }  // JSON string
    public int RetentionDays { get; set; }
    public bool AutoBackupEnabled { get; set; }
    public string? AutoBackupSchedule { get; set; }
    public required string AutoBackupScope { get; set; }
    public bool EnableCompression { get; set; }
    public bool EnableEncryption { get; set; }
    public string? EncryptionKey { get; set; }
    public bool VerifyAfterUpload { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastBackupAt { get; set; }

    public static BackupConfigurationDto FromEntity(BackupConfiguration entity)
    {
        return new BackupConfigurationDto
        {
            Id = entity.Id,
            Name = entity.Name,
            StorageType = entity.StorageType.ToString(),
            IsActive = entity.IsActive,
            IsDefault = entity.IsDefault,
            ProviderConfiguration = entity.ProviderConfiguration,
            RetentionDays = entity.RetentionDays,
            AutoBackupEnabled = entity.AutoBackupEnabled,
            AutoBackupSchedule = entity.AutoBackupSchedule,
            AutoBackupScope = entity.AutoBackupScope.ToString(),
            EnableCompression = entity.EnableCompression,
            EnableEncryption = entity.EnableEncryption,
            EncryptionKey = entity.EncryptionKey,
            VerifyAfterUpload = entity.VerifyAfterUpload,
            CreatedAt = entity.CreatedAt,
            LastBackupAt = entity.LastBackupAt
        };
    }
}

public class CreateBackupConfigurationRequest
{
    public required string Name { get; set; }
    public BackupStorageType StorageType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public int RetentionDays { get; set; } = 30;
    public bool AutoBackupEnabled { get; set; } = false;
    public string? AutoBackupSchedule { get; set; }
    public BackupScope AutoBackupScope { get; set; } = BackupScope.Complete;
    public bool EnableCompression { get; set; } = true;
    public bool EnableEncryption { get; set; } = false;
    public string? EncryptionKey { get; set; }
    public bool VerifyAfterUpload { get; set; } = true;

    // Provider-specific configurations (only one should be populated based on StorageType)
    public S3BackupConfig? S3Config { get; set; }
    public GoogleDriveBackupConfig? GoogleDriveConfig { get; set; }
    public LocalFileSystemBackupConfig? LocalFileSystemConfig { get; set; }
}

public class UpdateBackupConfigurationRequest
{
    public required string Name { get; set; }
    public BackupStorageType StorageType { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public int RetentionDays { get; set; }
    public bool AutoBackupEnabled { get; set; }
    public string? AutoBackupSchedule { get; set; }
    public BackupScope AutoBackupScope { get; set; }
    public bool EnableCompression { get; set; }
    public bool EnableEncryption { get; set; }
    public string? EncryptionKey { get; set; }
    public bool VerifyAfterUpload { get; set; }

    // Provider-specific configurations (only one should be populated based on StorageType)
    public S3BackupConfig? S3Config { get; set; }
    public GoogleDriveBackupConfig? GoogleDriveConfig { get; set; }
    public LocalFileSystemBackupConfig? LocalFileSystemConfig { get; set; }
}
