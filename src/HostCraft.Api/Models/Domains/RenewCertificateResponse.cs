namespace HostCraft.Api.Models.Domains;

/// <summary>
/// Legacy DTO for backward compatibility with old API endpoints
/// </summary>
public record RenewCertificateResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
}
