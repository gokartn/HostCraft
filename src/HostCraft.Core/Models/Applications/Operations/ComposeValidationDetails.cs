using System.Collections.Generic;

namespace HostCraft.Core.Models.Applications.Operations;

/// <summary>
/// Detailed validation output for Docker Compose content.
/// </summary>
public record ComposeValidationDetails
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public List<string> ServiceNames { get; init; } = new();
    public List<string> RequiredVariables { get; init; } = new();
    public string? ComposeVersion { get; init; }
}
