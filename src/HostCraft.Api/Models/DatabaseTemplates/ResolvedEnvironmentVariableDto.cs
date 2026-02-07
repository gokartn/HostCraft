namespace HostCraft.Api.Models.DatabaseTemplates;

public class ResolvedEnvironmentVariableDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
    public bool IsUserProvided { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool DisplayInWizard { get; set; }
}
