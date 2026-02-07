using System.Collections.Generic;

namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Aggregates orphaned containers and services across servers.
/// </summary>
public record OrphanedResourcesInfo
{
    public List<OrphanedContainerInfo> Containers { get; init; } = new();
    public List<OrphanedServiceInfo> Services { get; init; } = new();
}
