using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Servers;

public record UpdateServerRequest(
    string? Name = null,
    string? Host = null,
    int? Port = null,
    string? User = null,
    string? Region = null,
    string? PrivateKeyContent = null,
    ServerType? Type = null,
    ProxyType? ProxyType = null,
    string? DefaultLetsEncryptEmail = null);
