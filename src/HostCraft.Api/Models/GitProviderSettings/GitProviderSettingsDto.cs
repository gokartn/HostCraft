using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.GitProviderSettings;

public record GitProviderSettingsDto(
    int Id,
    GitProviderType Type,
    string Name,
    bool IsConfigured,
    bool IsEnabled,
    string? ApiUrl);
