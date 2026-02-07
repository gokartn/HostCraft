using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Configuration for backup storage provider (S3, Google Drive, etc.)
/// </summary>
public class BackupConfiguration
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public BackupStorageType StorageType { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// JSON configuration specific to storage provider
    /// For S3: {endpoint, bucket, region, accessKey, secretKey}
    /// For Google Drive: {folderId, refreshToken, clientId, clientSecret}
    /// </summary>
    public required string ProviderConfiguration { get; set; }

    /// <summary>
    /// Backup retention policy in days (0 = keep forever)
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Enable automatic backups on this storage
    /// </summary>
    public bool AutoBackupEnabled { get; set; } = false;

    /// <summary>
    /// Cron expression for automatic backups (e.g., "0 2 * * *" = daily at 2 AM)
    /// </summary>
    public string? AutoBackupSchedule { get; set; }

    /// <summary>
    /// What to include in automatic backups
    /// </summary>
    public BackupScope AutoBackupScope { get; set; } = BackupScope.Complete;

    /// <summary>
    /// Enable backup compression
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Enable backup encryption
    /// </summary>
    public bool EnableEncryption { get; set; } = true;

    /// <summary>
    /// Encryption key (stored encrypted in database)
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Verify backups after upload
    /// </summary>
    public bool VerifyAfterUpload { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime? LastBackupAt { get; set; }

    // Navigation properties
    public ICollection<Backup> Backups { get; set; } = new List<Backup>();
}

/// <summary>
/// S3-compatible storage configuration
/// </summary>
public class S3BackupConfig
{
    public required string Endpoint { get; set; } // e.g., s3.amazonaws.com, play.min.io
    public required string BucketName { get; set; }
    public required string Region { get; set; } // e.g., us-east-1
    public required string AccessKeyId { get; set; }
    public required string SecretAccessKey { get; set; }
    public bool UsePathStyle { get; set; } = false; // MinIO typically needs true
    public bool UseSsl { get; set; } = true;
    public string? Prefix { get; set; } // Optional folder prefix in bucket
}

/// <summary>
/// Google Drive storage configuration
/// </summary>
public class GoogleDriveBackupConfig
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public required string RefreshToken { get; set; }
    public required string FolderId { get; set; } // Parent folder for backups
    public string? ServiceAccountJson { get; set; } // Alternative: service account auth
}

/// <summary>
/// Local filesystem storage configuration
/// </summary>
public class LocalFileSystemBackupConfig
{
    public required string StoragePath { get; set; }
}
