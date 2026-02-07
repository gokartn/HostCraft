using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.GitProviderSettings;

public record GitProviderSettingsDetailDto(
    int Id,
    GitProviderType Type,
    string Name,
    string? ClientId,
    string? ClientSecretMasked,
    string? ApiUrl,
    bool IsEnabled,
    bool IsConfigured);
