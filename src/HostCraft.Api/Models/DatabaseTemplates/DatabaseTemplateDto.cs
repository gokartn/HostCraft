namespace HostCraft.Api.Models.DatabaseTemplates;

public class DatabaseTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string DockerImage { get; set; } = string.Empty;
    public int DefaultPort { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
    public long? RecommendedMemoryMB { get; set; }
    public double? RecommendedCpuCores { get; set; }

    /// <summary>
    /// Version extracted from DockerImage tag (e.g., "16", "8.0")
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Display name with version (e.g., "PostgreSQL 16")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
}
