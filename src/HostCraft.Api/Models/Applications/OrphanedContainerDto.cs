using System.Collections.Generic;

namespace HostCraft.Api.Models.Applications;

public record OrphanedContainerDto
{
    public string ContainerId { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public int ApplicationId { get; init; }
    public Dictionary<string, string> Labels { get; init; } = new();
}
