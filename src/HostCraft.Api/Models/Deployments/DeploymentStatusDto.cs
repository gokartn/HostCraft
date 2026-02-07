namespace HostCraft.Api.Models.Deployments;

public record DeploymentStatusDto
{
    public int Id { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
}
