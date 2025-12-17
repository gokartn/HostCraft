using HostCraft.Core.Entities;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing reverse proxy configurations (Traefik, Caddy, etc.).
/// </summary>
public interface IProxyService
{
    Task<bool> ConfigureApplicationAsync(Application application, CancellationToken cancellationToken = default);
    Task<bool> RemoveApplicationAsync(Application application, CancellationToken cancellationToken = default);
    Task<bool> ReloadConfigurationAsync(Server server, CancellationToken cancellationToken = default);
    Task<string> GenerateConfigAsync(Application application, CancellationToken cancellationToken = default);
    Task<bool> EnsureProxyDeployedAsync(Server server, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Configure proxy to route a domain to the HostCraft web UI
    /// </summary>
    Task<bool> ConfigureHostCraftDomainAsync(string domain, bool enableHttps, string? letsEncryptEmail, CancellationToken cancellationToken = default);
}
