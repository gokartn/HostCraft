namespace HostCraft.Api.Models.Projects;

public record ProjectApplicationDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? DockerImage { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public DateTime? LastDeployedAt { get; init; }
}
