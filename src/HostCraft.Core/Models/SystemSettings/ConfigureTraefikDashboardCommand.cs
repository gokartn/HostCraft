namespace HostCraft.Core.Models.SystemSettings;

public record ConfigureTraefikDashboardCommand(
    string? DashboardDomain,
    bool EnableAuth,
    string? Username,
    string? Password);
