using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Domains;

public record CreateDomainRequest(
    string Host,
    string? Path = "/",
    int Port = 80,
    bool? HttpsEnabled = true,
    bool? ForceHttps = true,
    string? CertificateType = "letsencrypt",
    bool IsPrimary = false,
    bool? WebSocketEnabled = true,
    bool? CompressionEnabled = true,
    bool? BasicAuthEnabled = false,
    string? BasicAuthUsers = null,
    int? RateLimitRps = 0,
    string? IpWhitelist = null,
    int? MaxBodySizeMb = 0,
    bool? StripPathPrefix = false,
    bool? PathBasedRouting = false,
    string? CustomHeaders = null,
    ProxyProtocol? Protocol = null
);
