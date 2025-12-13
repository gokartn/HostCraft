using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for disaster recovery operations across regions.
/// </summary>
public interface IDisasterRecoveryService
{
    /// <summary>
    /// Replicates application deployment to another region.
    /// </summary>
    Task<Application> ReplicateToRegionAsync(int applicationId, int targetRegionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Performs failover of application to secondary region.
    /// </summary>
    Task<bool> FailoverToRegionAsync(int applicationId, int targetRegionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Fails back to primary region after disaster is resolved.
    /// </summary>
    Task<bool> FailbackToPrimaryAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Tests DR plan by performing dry-run failover.
    /// </summary>
    Task<DisasterRecoveryTestResult> TestFailoverAsync(int applicationId, int targetRegionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets DR readiness status for an application.
    /// </summary>
    Task<DisasterRecoveryStatus> GetDRStatusAsync(int applicationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Synchronizes application state across regions.
    /// </summary>
    Task<bool> SynchronizeRegionsAsync(int applicationId, CancellationToken cancellationToken = default);
}

public record DisasterRecoveryTestResult(
    bool Success,
    TimeSpan FailoverTime,
    string? ErrorMessage,
    IEnumerable<string> Warnings);

public record DisasterRecoveryStatus(
    bool IsConfigured,
    int PrimaryRegionId,
    IEnumerable<int> SecondaryRegionIds,
    DateTime? LastSync,
    TimeSpan? EstimatedRTO, // Recovery Time Objective
    TimeSpan? EstimatedRPO); // Recovery Point Objective
