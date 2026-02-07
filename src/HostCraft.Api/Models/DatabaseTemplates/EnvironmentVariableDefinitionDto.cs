namespace HostCraft.Api.Models.DatabaseTemplates;

public class EnvironmentVariableDefinitionDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSecret { get; set; }
    public bool IsRequired { get; set; }
    public string Strategy { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public string? SuggestedValue { get; set; }
    public int? Length { get; set; }
    public string? Prefix { get; set; }
    public string? Suffix { get; set; }
    public bool DisplayInWizard { get; set; } = true;
}
