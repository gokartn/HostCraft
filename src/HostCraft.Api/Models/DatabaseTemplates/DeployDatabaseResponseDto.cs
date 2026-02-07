namespace HostCraft.Api.Models.DatabaseTemplates;

public class DeployDatabaseResponseDto
{
    public int ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; }
    public int? PublishedPort { get; set; }
    public string? DockerImage { get; set; }
    public List<ResolvedEnvironmentVariableDto> EnvironmentVariables { get; set; } = new();
}
