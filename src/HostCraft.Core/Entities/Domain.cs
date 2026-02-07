using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Represents a domain configuration for an application.
/// Supports multiple domains per application with individual routing and SSL settings.
/// </summary>
public class Domain
{
    public int Id { get; set; }

    public Guid Uuid { get; set; } = Guid.NewGuid();

    public int ApplicationId { get; set; }

    /// <summary>
    /// The domain hostname (e.g., "app.example.com", "*.example.com" for wildcard)
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// Path prefix for routing (e.g., "/", "/api", "/app")
    /// </summary>
    public string Path { get; set; } = "/";

    /// <summary>
    /// Target port on the container/service
    /// </summary>
    public int Port { get; set; } = 80;

    /// <summary>
    /// Enable HTTPS for this domain
    /// </summary>
    public bool HttpsEnabled { get; set; } = true;

    /// <summary>
    /// Force HTTP to HTTPS redirect
    /// </summary>
    public bool ForceHttps { get; set; } = true;

    /// <summary>
    /// Certificate type: "letsencrypt", "custom", or "none"
    /// </summary>
    public string CertificateType { get; set; } = "letsencrypt";

    /// <summary>
    /// Custom certificate ID (if CertificateType is "custom")
    /// </summary>
    public int? CertificateId { get; set; }

    /// <summary>
    /// DNS validation status: "pending", "valid", "invalid"
    /// </summary>
    public string DnsStatus { get; set; } = "pending";

    /// <summary>
    /// Last DNS validation check timestamp
    /// </summary>
    public DateTime? LastDnsCheck { get; set; }

    /// <summary>
    /// DNS validation error message (if any)
    /// </summary>
    public string? DnsError { get; set; }

    /// <summary>
    /// Whether this is the primary domain for the application
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Enable path-based routing (vs host-based)
    /// </summary>
    public bool PathBasedRouting { get; set; }

    /// <summary>
    /// Strip the path prefix before forwarding to the application
    /// </summary>
    public bool StripPathPrefix { get; set; }

    /// <summary>
    /// Custom headers to add (JSON format)
    /// </summary>
    public string? CustomHeaders { get; set; }

    /// <summary>
    /// Enable basic auth for this domain
    /// </summary>
    public bool BasicAuthEnabled { get; set; }

    /// <summary>
    /// Basic auth users (format: user1:password1,user2:password2)
    /// </summary>
    public string? BasicAuthUsers { get; set; }

    /// <summary>
    /// Rate limiting requests per second (0 = unlimited)
    /// </summary>
    public int RateLimitRps { get; set; }

    /// <summary>
    /// IP whitelist (comma-separated CIDRs, empty = allow all)
    /// </summary>
    public string? IpWhitelist { get; set; }

    /// <summary>
    /// Enable WebSocket support for this domain
    /// </summary>
    public bool WebSocketEnabled { get; set; } = true;

    /// <summary>
    /// Request body size limit in MB (0 = default)
    /// </summary>
    public int MaxBodySizeMb { get; set; }

    /// <summary>
    /// Enable gzip compression
    /// </summary>
    public bool CompressionEnabled { get; set; } = true;

    /// <summary>
    /// Determines whether this domain should be routed via HTTP middleware or raw TCP stream.
    /// </summary>
    public ProxyProtocol ProxyProtocol { get; set; } = ProxyProtocol.Http;

    /// <summary>
    /// Domain is active and should be routed
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Application Application { get; set; } = null!;

    public Certificate? Certificate { get; set; }

    /// <summary>
    /// Get the full URL for this domain
    /// </summary>
    public string GetUrl()
    {
        if (ProxyProtocol == ProxyProtocol.Tcp)
        {
            var scheme = HttpsEnabled ? "tls" : "tcp";
            return $"{scheme}://{Host}:{Port}";
        }

        var httpScheme = HttpsEnabled ? "https" : "http";
        var path = Path == "/" ? "" : Path;
        return $"{httpScheme}://{Host}{path}";
    }
}
