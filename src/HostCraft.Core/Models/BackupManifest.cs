using HostCraft.Core.Enums;

namespace HostCraft.Core.Models;

/// <summary>
/// Manifest describing the contents and metadata of a backup package.
/// This is the first file read during restore to understand what's in the backup.
/// </summary>
public class BackupManifest
{
    /// <summary>
    /// Unique identifier for this backup
    /// </summary>
    public required string BackupId { get; set; }

    /// <summary>
    /// Backup format version (for compatibility checking during restore)
    /// </summary>
    public required string FormatVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Timestamp when backup was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// HostCraft version that created this backup
    /// </summary>
    public required string HostCraftVersion { get; set; }

    /// <summary>
    /// What was included in this backup
    /// </summary>
    public BackupScope Scope { get; set; }

    /// <summary>
    /// Source server information (for reference, not used during restore)
    /// </summary>
    public BackupSourceInfo? SourceInfo { get; set; }

    /// <summary>
    /// Files included in this backup package
    /// </summary>
    public List<BackupFileEntry> Files { get; set; } = new();

    /// <summary>
    /// Total size of backup in bytes
    /// </summary>
    public long TotalSizeBytes { get; set; }

    /// <summary>
    /// Number of projects included
    /// </summary>
    public int ProjectCount { get; set; }

    /// <summary>
    /// Number of applications included
    /// </summary>
    public int ApplicationCount { get; set; }

    /// <summary>
    /// Number of servers included
    /// </summary>
    public int ServerCount { get; set; }

    /// <summary>
    /// Encryption information (if backup is encrypted)
    /// </summary>
    public BackupEncryptionInfo? Encryption { get; set; }

    /// <summary>
    /// Checksums for integrity verification
    /// </summary>
    public Dictionary<string, string> Checksums { get; set; } = new();
}

/// <summary>
/// Information about the source server/instance that created the backup
/// </summary>
public class BackupSourceInfo
{
    public string? Hostname { get; set; }
    public string? InstanceId { get; set; }
    public string? Version { get; set; }
    public DateTime BackupDate { get; set; }
}

/// <summary>
/// Represents a file in the backup package
/// </summary>
public class BackupFileEntry
{
    /// <summary>
    /// Relative path within backup package
    /// </summary>
    public required string Path { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA256 checksum for integrity verification
    /// </summary>
    public required string Checksum { get; set; }

    /// <summary>
    /// What this file contains
    /// </summary>
    public required BackupFileType Type { get; set; }

    /// <summary>
    /// Is this file compressed?
    /// </summary>
    public bool IsCompressed { get; set; }

    /// <summary>
    /// Is this file encrypted?
    /// </summary>
    public bool IsEncrypted { get; set; }
}

/// <summary>
/// Type of backup file
/// </summary>
public enum BackupFileType
{
    DatabaseDump,
    VolumeArchive,
    Configuration,
    Certificate,
    Secret,
    Metadata
}

/// <summary>
/// Encryption information for backup
/// </summary>
public class BackupEncryptionInfo
{
    public required string Algorithm { get; set; }
    public required string KeyDerivationFunction { get; set; }
    public byte[]? Salt { get; set; }
}
