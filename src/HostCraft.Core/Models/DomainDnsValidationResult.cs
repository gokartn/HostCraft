namespace HostCraft.Core.Models;

/// <summary>
/// Represents the outcome of validating a domain's DNS configuration.
/// </summary>
public class DomainDnsValidationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public string? ExpectedIp { get; init; }
    public string? ActualIp { get; init; }
    public IReadOnlyList<string> AllResolvedIps { get; init; } = Array.Empty<string>();
}
