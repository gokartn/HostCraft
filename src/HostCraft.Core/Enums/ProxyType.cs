namespace HostCraft.Core.Enums;

/// <summary>
/// Defines the reverse proxy type used on a server.
/// </summary>
public enum ProxyType
{
    /// <summary>
    /// No reverse proxy configured.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Traefik reverse proxy with automatic service discovery.
    /// </summary>
    Traefik = 1,
    
    /// <summary>
    /// Caddy reverse proxy with automatic HTTPS.
    /// </summary>
    Caddy = 2,
    
    /// <summary>
    /// Nginx reverse proxy with SSL termination.
    /// </summary>
    Nginx = 3,
    
    /// <summary>
    /// YARP (Yet Another Reverse Proxy) built into HostCraft.
    /// </summary>
    Yarp = 4
}
