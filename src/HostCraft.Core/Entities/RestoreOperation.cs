using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Tracks a restore operation from a backup.
/// </summary>
public class RestoreOperation
{
    public int Id { get; set; }

    public Guid Uuid { get; set; }

    /// <summary>
    /// Backup being restored
    /// </summary>
    public int BackupId { get; set; }

    /// <summary>
    /// What parts of the backup to restore
    /// </summary>
    public BackupScope RestoreScope { get; set; }

    /// <summary>
    /// Strategy for handling conflicts during restore
    /// </summary>
    public RestoreStrategy Strategy { get; set; } = RestoreStrategy.FailOnConflict;

    /// <summary>
    /// Restore mapping configuration (JSON)
    /// Maps old server IDs/IPs/domains to new values
    /// </summary>
    public string? RestoreMappingJson { get; set; }

    public BackupStatus Status { get; set; } = BackupStatus.Queued;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Detailed progress log of restore operation
    /// </summary>
    public string? ProgressLog { get; set; }

    /// <summary>
    /// Items that were skipped during restore (due to conflicts/errors)
    /// </summary>
    public string? SkippedItems { get; set; }

    /// <summary>
    /// Items that were successfully restored
    /// </summary>
    public int ProjectsRestored { get; set; }
    public int ApplicationsRestored { get; set; }
    public int ServersRestored { get; set; }

    /// <summary>
    /// Who triggered this restore
    /// </summary>
    public string? TriggeredBy { get; set; }

    // Navigation properties
    public Backup Backup { get; set; } = null!;
}
