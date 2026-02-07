namespace HostCraft.Core.Models;

/// <summary>
/// Strategy used to generate or recommend environment variable values for database templates.
/// </summary>
public enum TemplateEnvironmentValueStrategy
{
    None = 0,
    Literal = 1,
    ApplicationSlug = 2,
    RandomSecure = 3,
    RandomSecureWithSymbols = 4
}

/// <summary>
/// Describes a best-practice environment variable that should be applied to a template-driven deployment.
/// </summary>
public record class TemplateEnvironmentVariableDefinition
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Description { get; init; } = string.Empty;
    public bool IsSecret { get; init; }
    public bool IsRequired { get; init; }
    public TemplateEnvironmentValueStrategy Strategy { get; init; } = TemplateEnvironmentValueStrategy.None;
    public string? DefaultValue { get; init; }
    public int? Length { get; init; }
    public string? Prefix { get; init; }
    public string? Suffix { get; init; }
    public bool DisplayInWizard { get; init; } = true;
    public bool AllowOverride { get; init; } = true;

    public TemplateEnvironmentVariableDefinition CreateCopy() => this with { };
}
