namespace HostCraft.Api.Models.Applications;

public record PortMappingRequest(
    int HostPort,
    int ContainerPort,
    string Protocol = "tcp");
