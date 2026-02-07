namespace HostCraft.Api.Models.SystemSettings;

public record ConfigureTraefikDashboardRequest(
    string? DashboardDomain,
    bool EnableAuth,
    string? Username,
    string? Password);
