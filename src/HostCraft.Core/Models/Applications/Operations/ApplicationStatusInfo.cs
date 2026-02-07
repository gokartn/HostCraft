namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Represents runtime status for an application deployment.
/// </summary>
public record ApplicationStatusInfo(
    int ApplicationId,
    string Status,
    bool IsRunning,
    string? ActualState = null,
    string? ContainerId = null,
    string? ServiceId = null,
    IReadOnlyList<ServiceReplicaPlacementInfo>? Placements = null);
