namespace HostCraft.Api.Models.DatabaseTemplates;

public class DeployDatabaseRequest
{
    public string Name { get; set; } = string.Empty;
    public int ServerId { get; set; }
    public int ProjectId { get; set; }
    public string? DockerImage { get; set; }
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
    public long? MemoryLimitMB { get; set; }
    public double? CpuLimitCores { get; set; }
}
