namespace HostCraft.Core.Enums;

/// <summary>
/// Represents the current operational status of a server.
/// </summary>
public enum ServerStatus
{
    /// <summary>
    /// Server is reachable and operational.
    /// </summary>
    Online = 0,
    
    /// <summary>
    /// Server is not reachable or not responding.
    /// </summary>
    Offline = 1,
    
    /// <summary>
    /// Server configuration is invalid or incomplete.
    /// </summary>
    Error = 2,
    
    /// <summary>
    /// Server validation is in progress.
    /// </summary>
    Validating = 3
}
