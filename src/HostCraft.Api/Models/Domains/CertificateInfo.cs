using HostCraft.Core.Entities;

namespace HostCraft.Api.Models.Domains;

public record CertificateInfo
{
    public int Id { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public CertificateStatus Status { get; init; }
    public DateTime? IssuedAt { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public int DaysUntilExpiry { get; init; }
    public bool AutoRenew { get; init; }
    public string? ErrorMessage { get; init; }
}
