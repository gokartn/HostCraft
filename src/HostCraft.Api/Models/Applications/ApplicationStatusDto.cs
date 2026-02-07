namespace HostCraft.Api.Models.Applications;

public record ApplicationStatusDto
{
    public int ApplicationId { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsRunning { get; init; }
    public string? ActualState { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public IEnumerable<ReplicaPlacementDto> Placements { get; init; } = Array.Empty<ReplicaPlacementDto>();
}

public record ReplicaPlacementDto(
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
