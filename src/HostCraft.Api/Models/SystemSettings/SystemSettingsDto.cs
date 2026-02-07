namespace HostCraft.Api.Models.SystemSettings;

public record SystemSettingsDto
{
    public string? HostCraftDomain { get; init; }
    public string? HostCraftApiDomain { get; init; }
    public bool HostCraftEnableHttps { get; init; }
    public string? HostCraftLetsEncryptEmail { get; init; }
    public string? CertificateStatus { get; init; }
    public DateTime? ConfiguredAt { get; init; }
    public DateTime? ProxyUpdatedAt { get; init; }
    public string? TraefikDashboardDomain { get; init; }
    public bool TraefikDashboardAuthEnabled { get; init; }
    public string? TraefikDashboardUsername { get; init; }
}
