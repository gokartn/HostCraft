using HostCraft.Core.Interfaces;

namespace HostCraft.Api.Models.Servers;

public record ServerValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
    public SystemInfo? SystemInfo { get; init; }
}
