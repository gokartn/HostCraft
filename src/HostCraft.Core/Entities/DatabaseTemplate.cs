using HostCraft.Core.Enums;

namespace HostCraft.Core.Entities;

/// <summary>
/// Pre-configured database template for one-click deployment
/// </summary>
public class DatabaseTemplate
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public DatabaseType Type { get; set; }

    public required string DockerImage { get; set; }

    public int DefaultPort { get; set; }

    /// <summary>
    /// JSON object of default environment variables (e.g., {"POSTGRES_PASSWORD": "auto"})
    /// </summary>
    public string? DefaultEnvironmentVariables { get; set; }

    /// <summary>
    /// Default container path for data persistence (e.g., /var/lib/postgresql/data)
    /// </summary>
    public required string DefaultVolumePath { get; set; }

    /// <summary>
    /// Docker health check command (e.g., ["CMD", "pg_isready", "-U", "postgres"])
    /// </summary>
    public string? HealthCheckCommand { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Category for grouping (SQL, NoSQL, Cache, Analytics)
    /// </summary>
    public required string Category { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// URL to icon/logo for UI display
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Recommended memory limit in bytes
    /// </summary>
    public long? RecommendedMemoryBytes { get; set; }

    /// <summary>
    /// Recommended CPU limit (cores)
    /// </summary>
    public double? RecommendedCpuLimit { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Extracts the version number from the Docker image tag.
    /// Examples: "postgres:16-alpine" -> "16", "mysql:8.0" -> "8.0", "redis:7" -> "7"
    /// </summary>
    public string Version
    {
        get
        {
            if (string.IsNullOrEmpty(DockerImage))
                return "latest";

            // Split by : to get tag part
            var parts = DockerImage.Split(':', 2);
            if (parts.Length < 2)
                return "latest";

            var tag = parts[1];

            // Extract version number from tag (handle formats like "16-alpine", "8.0", "7")
            var match = System.Text.RegularExpressions.Regex.Match(tag, @"^(\d+(?:\.\d+)?)");
            return match.Success ? match.Groups[1].Value : tag;
        }
    }

    /// <summary>
    /// Display name with version (e.g., "PostgreSQL 16" or "MySQL 8.0")
    /// </summary>
    public string DisplayName => $"{Type} {Version}";
}
