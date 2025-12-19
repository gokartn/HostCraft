namespace HostCraft.Core.Entities;

/// <summary>
/// System-wide settings for HostCraft platform configuration.
/// </summary>
public class SystemSettings
{
    public int Id { get; set; }

    /// <summary>
    /// Domain for accessing the HostCraft control panel (Web UI)
    /// </summary>
    public string? HostCraftDomain { get; set; }

    /// <summary>
    /// Domain for accessing the HostCraft API (for OAuth callbacks, webhooks, etc.)
    /// If not set, defaults to HostCraftDomain with port 5100 or assumes API is at same domain /api path
    /// </summary>
    public string? HostCraftApiDomain { get; set; }

    /// <summary>
    /// Enable HTTPS for HostCraft UI with Let's Encrypt
    /// </summary>
    public bool HostCraftEnableHttps { get; set; } = true;
    
    /// <summary>
    /// Email for Let's Encrypt certificate notifications
    /// </summary>
    public string? HostCraftLetsEncryptEmail { get; set; }
    
    /// <summary>
    /// When the HostCraft domain was configured
    /// </summary>
    public DateTime? ConfiguredAt { get; set; }
    
    /// <summary>
    /// When the proxy was last updated with this configuration
    /// </summary>
    public DateTime? ProxyUpdatedAt { get; set; }
    
    /// <summary>
    /// Status of the HostCraft SSL certificate
    /// </summary>
    public string? CertificateStatus { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
