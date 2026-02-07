namespace HostCraft.Core.Models;

/// <summary>
/// Port mapping for container/service port configuration.
/// </summary>
public record PortMapping(
    int HostPort,
    int ContainerPort,
    string Protocol = "tcp");
