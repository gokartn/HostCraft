namespace HostCraft.Api.Models.Deployments;

public record DeploymentLogResponseDto
{
    public int Id { get; init; }
    public string Message { get; init; } = string.Empty;
    public string LogLevel { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}
