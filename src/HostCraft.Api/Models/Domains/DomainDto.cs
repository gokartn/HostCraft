using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Domains;

public record DomainDto
{
    public int Id { get; init; }
    public Guid Uuid { get; init; }
    public int ApplicationId { get; init; }
    public string Host { get; init; } = string.Empty;
    public string Path { get; init; } = "/";
    public int Port { get; init; }
    public bool HttpsEnabled { get; init; }
    public bool ForceHttps { get; init; }
    public string CertificateType { get; init; } = string.Empty;
    public string DnsStatus { get; init; } = string.Empty;
    public string? DnsError { get; init; }
    public DateTime? LastDnsCheck { get; init; }
    public bool IsPrimary { get; init; }
    public bool WebSocketEnabled { get; init; }
    public bool CompressionEnabled { get; init; }
    public bool BasicAuthEnabled { get; init; }
    public int RateLimitRps { get; init; }
    public string? IpWhitelist { get; init; }
    public int MaxBodySizeMb { get; init; }
    public bool StripPathPrefix { get; init; }
    public bool PathBasedRouting { get; init; }
    public bool IsActive { get; init; }
    public ProxyProtocol Protocol { get; init; }
    public string Url { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
