namespace HostCraft.Core.Models;

/// <summary>
/// Request to deploy a Docker Compose application
/// </summary>
public class DeployComposeRequest
{
    /// <summary>
    /// Application name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Project ID
    /// </summary>
    public int ProjectId { get; set; }

    /// <summary>
    /// Server ID (must be swarm manager)
    /// </summary>
    public int ServerId { get; set; }

    /// <summary>
    /// Docker Compose YAML content
    /// </summary>
    public required string ComposeFile { get; set; }

    /// <summary>
    /// Environment variables for substitution
    /// </summary>
    public List<ComposeEnvironmentVariableRequest> EnvironmentVariables { get; set; } = new();
}

/// <summary>
/// Environment variable for Docker Compose deployment
/// </summary>
public class ComposeEnvironmentVariableRequest
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public bool IsSecret { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Request to validate Docker Compose YAML
/// </summary>
public class ValidateComposeRequest
{
    public required string ComposeFile { get; set; }
}
