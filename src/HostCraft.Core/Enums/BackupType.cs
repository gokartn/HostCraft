namespace HostCraft.Core.Enums;

/// <summary>
/// Types of backups that can be performed.
/// </summary>
public enum BackupType
{
    /// <summary>
    /// Full backup of application configuration and metadata.
    /// </summary>
    Configuration = 0,
    
    /// <summary>
    /// Backup of Docker volumes containing persistent data.
    /// </summary>
    Volume = 1,
    
    /// <summary>
    /// Database backup (for managed databases).
    /// </summary>
    Database = 2,
    
    /// <summary>
    /// Complete snapshot including config, volumes, and databases.
    /// </summary>
    Full = 3
}
