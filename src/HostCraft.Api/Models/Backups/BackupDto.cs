using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Models;

namespace HostCraft.Api.Models.Backups;

public class BackupDto
{
    public int Id { get; set; }
    public Guid Uuid { get; set; }
    public int? ApplicationId { get; set; }
    public int? BackupConfigurationId { get; set; }
    public required string Type { get; set; }
    public required string Status { get; set; }
    public required string Scope { get; set; }
    public string? StoragePath { get; set; }
    public string? RemoteStoragePath { get; set; }
    public long SizeBytes { get; set; }
    public string? Checksum { get; set; }
    public bool IsVerified { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsCompressed { get; set; }
    public int ProjectCount { get; set; }
    public int ApplicationCount { get; set; }
    public int ServerCount { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int? RetentionDays { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? TriggeredBy { get; set; }

    public static BackupDto FromEntity(Backup backup)
    {
        return new BackupDto
        {
            Id = backup.Id,
            Uuid = backup.Uuid,
            ApplicationId = backup.ApplicationId,
            BackupConfigurationId = backup.BackupConfigurationId,
            Type = backup.Type.ToString(),
            Status = backup.Status.ToString(),
            Scope = backup.Scope.ToString(),
            StoragePath = backup.StoragePath,
            RemoteStoragePath = backup.RemoteStoragePath,
            SizeBytes = backup.SizeBytes,
            Checksum = backup.Checksum,
            IsVerified = backup.IsVerified,
            IsEncrypted = backup.IsEncrypted,
            IsCompressed = backup.IsCompressed,
            ProjectCount = backup.ProjectCount,
            ApplicationCount = backup.ApplicationCount,
            ServerCount = backup.ServerCount,
            StartedAt = backup.StartedAt,
            CompletedAt = backup.CompletedAt,
            ErrorMessage = backup.ErrorMessage,
            RetentionDays = backup.RetentionDays,
            ExpiresAt = backup.ExpiresAt,
            TriggeredBy = backup.TriggeredBy
        };
    }
}

public class CreateSystemBackupRequest
{
    public BackupScope Scope { get; set; } = BackupScope.Complete;
    public int? BackupConfigurationId { get; set; }
}

public class RestoreRequest
{
    public BackupScope RestoreScope { get; set; } = BackupScope.Complete;
    public RestoreStrategy Strategy { get; set; } = RestoreStrategy.FailOnConflict;
    public RestoreMapping? Mapping { get; set; }
}

public class UploadToStorageRequest
{
    public int BackupConfigurationId { get; set; }
}
