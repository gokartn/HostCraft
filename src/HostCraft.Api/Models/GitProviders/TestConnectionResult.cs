namespace HostCraft.Api.Models.GitProviders;

public record TestConnectionResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = string.Empty;
}
