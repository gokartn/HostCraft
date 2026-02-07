using System;
using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Applications;

public record DeploymentDto
{
    public int Id { get; init; }
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
}
