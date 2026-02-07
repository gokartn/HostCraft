using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.GitProviderSettings;

public record SaveGitProviderSettingsRequest(
    GitProviderType Type,
    string? Name,
    string ClientId,
    string? ClientSecret,
    string? ApiUrl,
    bool IsEnabled);
