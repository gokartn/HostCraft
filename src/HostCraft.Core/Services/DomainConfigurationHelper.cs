using HostCraft.Core.Entities;
using HostCraft.Core.Enums;

namespace HostCraft.Core.Services;

/// <summary>
/// Helper service for domain configuration logic.
/// </summary>
public static class DomainConfigurationHelper
{
    /// <summary>
    /// Determines the default protocol based on application type and requested port.
    /// </summary>
    public static ProxyProtocol DetermineDefaultProtocol(Application application, int? requestedPort)
    {
        if (application.DatabaseType.HasValue)
        {
            return application.DatabaseType.Value switch
            {
                DatabaseType.Clickhouse => ProxyProtocol.Http,
                _ => ProxyProtocol.Tcp
            };
        }

        if (requestedPort.HasValue)
        {
            return requestedPort.Value switch
            {
                80 or 443 or 8080 or 3000 => ProxyProtocol.Http,
                _ => ProxyProtocol.Http
            };
        }

        return ProxyProtocol.Http;
    }

    /// <summary>
    /// Determines if HTTPS should be enabled by default for the protocol.
    /// </summary>
    public static bool DetermineDefaultHttps(ProxyProtocol protocol)
    {
        return protocol == ProxyProtocol.Http;
    }

    /// <summary>
    /// Determines if HTTPS should be forced by default.
    /// </summary>
    public static bool DetermineDefaultForceHttps(ProxyProtocol protocol, bool httpsEnabled)
    {
        return protocol == ProxyProtocol.Http && httpsEnabled;
    }

    /// <summary>
    /// Normalizes domain configuration based on protocol.
    /// </summary>
    public static void NormalizeDomainForProtocol(Domain domain)
    {
        // For TCP protocols, HTTP-specific settings don't apply
        if (domain.ProxyProtocol == ProxyProtocol.Tcp)
        {
            domain.HttpsEnabled = false;
            domain.ForceHttps = false;
            domain.Path = string.Empty; // TCP doesn't support path routing
        }

        // For HTTP, ensure path is set (default to /)
        if (domain.ProxyProtocol == ProxyProtocol.Http && string.IsNullOrWhiteSpace(domain.Path))
        {
            domain.Path = "/";
        }

        // Normalize path (ensure it starts with /)
        if (!string.IsNullOrWhiteSpace(domain.Path) && !domain.Path.StartsWith('/'))
        {
            domain.Path = $"/{domain.Path}";
        }
    }
}
