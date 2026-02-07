namespace HostCraft.Api.Models.SystemSettings;

public record ConfigureTraefikDashboardResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? DashboardDomain { get; init; }
    public bool AuthEnabled { get; init; }
}
