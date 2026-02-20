using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Domains;

public record UpdateDomainRequest(
    string? Host = null,
    string? Path = null,
    int? Port = null,
    int? TargetPort = null,
    bool? HttpsEnabled = null,
    bool? ForceHttps = null,
    string? CertificateType = null,
    bool? IsPrimary = null,
    bool? WebSocketEnabled = null,
    bool? CompressionEnabled = null,
    bool? BasicAuthEnabled = null,
    string? BasicAuthUsers = null,
    int? RateLimitRps = null,
    string? IpWhitelist = null,
    int? MaxBodySizeMb = null,
    bool? StripPathPrefix = null,
    bool? PathBasedRouting = null,
    string? CustomHeaders = null,
    bool? IsActive = null,
    ProxyProtocol? Protocol = null
);
