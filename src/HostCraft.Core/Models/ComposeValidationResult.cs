namespace HostCraft.Core.Models;

/// <summary>
/// Result of validating Docker Compose YAML syntax
/// </summary>
public class ComposeValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
