namespace HostCraft.Core.Models;

/// <summary>
/// Result of parsing a Docker Compose file
/// </summary>
public class ComposeParseResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> ServiceNames { get; set; } = new();
    public Dictionary<string, object>? ParsedYaml { get; set; }
    public string? ComposeVersion { get; set; }
}
