namespace HostCraft.Api.Models.GitProviders;

public record GitProviderDto(int Id, string Name, string Type, string? Username, bool IsActive);
