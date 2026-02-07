using System.ComponentModel.DataAnnotations;

namespace HostCraft.Api.Models.Applications;

public record TraefikOverridesRequest
{
    /// <summary>
    /// JSON object of Traefik labels to merge into the generated defaults.
    /// Example: {"traefik.http.routers.web.priority":"10"}
    /// </summary>
    public string? Overrides { get; init; }
}
