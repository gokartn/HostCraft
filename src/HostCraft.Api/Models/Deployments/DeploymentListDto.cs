using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Deployments;

public record DeploymentListDto
{
    public int Id { get; init; }
    public int ApplicationId { get; init; }
    public string ApplicationName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public string? ErrorMessage { get; init; }
    public string? TriggeredBy { get; init; }
    public string? CommitSha { get; init; }
    public string? CommitMessage { get; init; }
    public string? CommitAuthor { get; init; }
}
