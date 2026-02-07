using HostCraft.Core.Models;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for parsing and validating Docker Compose files
/// </summary>
public interface IComposeParser
{
    /// <summary>
    /// Parse and validate Docker Compose YAML content
    /// </summary>
    /// <param name="composeYaml">Docker Compose file content</param>
    /// <returns>Result containing parsed compose data or validation errors</returns>
    Task<ComposeParseResult> ParseComposeAsync(string composeYaml);

    /// <summary>
    /// Validate Docker Compose YAML syntax without full parsing
    /// </summary>
    /// <param name="composeYaml">Docker Compose file content</param>
    /// <returns>Result indicating if YAML is valid with any errors</returns>
    Task<ComposeValidationResult> ValidateYamlAsync(string composeYaml);

    /// <summary>
    /// Substitute environment variables in Docker Compose file
    /// </summary>
    /// <param name="composeYaml">Docker Compose file content with ${VAR} placeholders</param>
    /// <param name="variables">Dictionary of variable names to values</param>
    /// <returns>Compose file with substituted values</returns>
    Task<string> SubstituteEnvironmentVariablesAsync(string composeYaml, Dictionary<string, string> variables);

    /// <summary>
    /// Extract all service names from Docker Compose file
    /// </summary>
    /// <param name="composeYaml">Docker Compose file content</param>
    /// <returns>List of service names defined in the compose file</returns>
    Task<List<string>> ExtractServiceNamesAsync(string composeYaml);

    /// <summary>
    /// Extract all environment variable placeholders from Docker Compose file
    /// </summary>
    /// <param name="composeYaml">Docker Compose file content</param>
    /// <returns>List of unique variable names used in ${VAR} format</returns>
    Task<List<string>> ExtractVariablePlaceholdersAsync(string composeYaml);
}
