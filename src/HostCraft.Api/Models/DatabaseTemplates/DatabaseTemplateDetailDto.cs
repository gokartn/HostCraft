namespace HostCraft.Api.Models.DatabaseTemplates;

public class DatabaseTemplateDetailDto : DatabaseTemplateDto
{
    public string? DefaultEnvironmentVariables { get; set; }
    public string DefaultVolumePath { get; set; } = string.Empty;
    public string? HealthCheckCommand { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<EnvironmentVariableDefinitionDto> EnvironmentVariables { get; set; } = new();
}
