namespace HostCraft.Api.Models.SystemSettings;

public record ConfigureHostCraftRequest(
    string Domain,
    string? ApiDomain,
    bool EnableHttps,
    string? LetsEncryptEmail);
