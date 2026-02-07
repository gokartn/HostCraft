using System;
using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Applications;

public record ApplicationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string? DockerImage { get; init; }
    public bool UsePrivateRegistry { get; init; }
    public string? RegistryServer { get; init; }
    public string? RegistryUsername { get; init; }
    public bool HasRegistryPassword { get; init; }
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime? LastDeployedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
