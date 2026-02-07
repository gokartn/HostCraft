using System.Collections.Generic;

namespace HostCraft.Api.Models.Applications;

public record OrphanedResourcesDto
{
    public List<OrphanedContainerDto> OrphanedContainers { get; init; } = new();
    public List<OrphanedServiceDto> OrphanedServices { get; init; } = new();
}
