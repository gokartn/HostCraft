namespace HostCraft.Api.Models.SystemSettings;

public record ConfigureHostCraftResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Domain { get; init; }
    public bool HttpsEnabled { get; init; }
}
