namespace HostCraft.Api.Models.Domains;

public record DnsValidationResult
{
    public int? DomainId { get; set; }
    public string? Host { get; set; }
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public string? ExpectedIp { get; init; }
    public string? ActualIp { get; init; }
    public List<string>? AllResolvedIps { get; init; }
}
