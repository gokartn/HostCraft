namespace HostCraft.Core.Models.SystemSettings;

public record ConfigureHostCraftCommand(
    string Domain,
    string? ApiDomain,
    bool EnableHttps,
    string? LetsEncryptEmail);
