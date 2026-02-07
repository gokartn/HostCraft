namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Placement and state details for a swarm service task/replica.
/// </summary>
public record ServiceReplicaPlacementInfo(
    string TaskId,
    string NodeId,
    string NodeName,
    string Role,
    string Availability,
    string DesiredState,
    string CurrentState,
    string? Error,
    int Slot,
    DateTime? UpdatedAt);
