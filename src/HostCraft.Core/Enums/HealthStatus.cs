namespace HostCraft.Core.Enums;

/// <summary>
/// Health check result status.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Health check passed successfully.
    /// </summary>
    Healthy = 0,
    
    /// <summary>
    /// Health check returned warning but service is operational.
    /// </summary>
    Degraded = 1,
    
    /// <summary>
    /// Health check failed - service is unhealthy.
    /// </summary>
    Unhealthy = 2,
    
    /// <summary>
    /// Could not perform health check (timeout, network error, etc.).
    /// </summary>
    Unknown = 3
}
