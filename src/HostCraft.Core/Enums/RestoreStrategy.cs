namespace HostCraft.Core.Enums;

/// <summary>
/// Strategy for handling conflicts during restore.
/// </summary>
public enum RestoreStrategy
{
    /// <summary>
    /// Fail restore if any conflicts are detected (safest)
    /// </summary>
    FailOnConflict = 0,

    /// <summary>
    /// Skip items that already exist, restore only new items
    /// </summary>
    SkipExisting = 1,

    /// <summary>
    /// Overwrite existing items with backup data
    /// </summary>
    OverwriteExisting = 2,

    /// <summary>
    /// Merge existing and backup data (where possible)
    /// </summary>
    Merge = 3
}
