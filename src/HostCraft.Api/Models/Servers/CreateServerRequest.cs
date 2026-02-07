using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Servers;

public record CreateServerRequest(
    string Name,
    string Host,
    int Port = 22,
    string User = "root",
    string? Region = null,
    string? PrivateKeyContent = null,
    ServerType Type = ServerType.Standalone,
    ProxyType ProxyType = ProxyType.None,
    string? DefaultLetsEncryptEmail = null);
