using System.Text.RegularExpressions;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Service for parsing and validating Docker Compose files
/// </summary>
public class ComposeParser : IComposeParser
{
    private readonly ILogger<ComposeParser> _logger;
    private readonly IDeserializer _yamlDeserializer;
    private readonly ISerializer _yamlSerializer;

    public ComposeParser(ILogger<ComposeParser> logger)
    {
        _logger = logger;

        // Configure YAML deserializer for Docker Compose format
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    public async Task<ComposeParseResult> ParseComposeAsync(string composeYaml)
    {
        var result = new ComposeParseResult();

        try
        {
            // Parse YAML
            var parsedData = _yamlDeserializer.Deserialize<Dictionary<string, object>>(composeYaml);
            result.ParsedYaml = parsedData;

            // Extract version
            if (parsedData.TryGetValue("version", out var version))
            {
                result.ComposeVersion = version?.ToString();
            }

            // Extract service names
            if (parsedData.TryGetValue("services", out var servicesObj) && servicesObj is Dictionary<object, object> services)
            {
                result.ServiceNames = services.Keys.Select(k => k.ToString() ?? string.Empty).ToList();
            }

            // Validate required fields
            if (result.ServiceNames.Count == 0)
            {
                result.Errors.Add("No services defined in Docker Compose file");
            }

            result.IsValid = result.Errors.Count == 0;
        }
        catch (YamlException ex)
        {
            _logger.LogError(ex, "YAML parsing error: {Message}", ex.Message);
            result.IsValid = false;
            result.Errors.Add($"YAML syntax error: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing Docker Compose file");
            result.IsValid = false;
            result.Errors.Add($"Parse error: {ex.Message}");
        }

        return await Task.FromResult(result);
    }

    public async Task<ComposeValidationResult> ValidateYamlAsync(string composeYaml)
    {
        var result = new ComposeValidationResult();

        try
        {
            // Attempt to parse YAML
            var parsedData = _yamlDeserializer.Deserialize<Dictionary<string, object>>(composeYaml);

            // Check for services section
            if (!parsedData.ContainsKey("services"))
            {
                result.Errors.Add("Missing 'services' section");
            }
            else if (parsedData["services"] is Dictionary<object, object> services && services.Count == 0)
            {
                result.Errors.Add("'services' section is empty");
            }

            // Check version (optional but good practice)
            if (!parsedData.ContainsKey("version"))
            {
                result.Warnings.Add("No 'version' specified - assuming Compose v3 format");
            }

            result.IsValid = result.Errors.Count == 0;
        }
        catch (YamlException ex)
        {
            _logger.LogError(ex, "YAML validation error: {Message}", ex.Message);
            result.IsValid = false;
            result.Errors.Add($"YAML syntax error: {ex.InnerException?.Message ?? ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating YAML");
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return await Task.FromResult(result);
    }

    public async Task<string> SubstituteEnvironmentVariablesAsync(string composeYaml, Dictionary<string, string> variables)
    {
        var result = composeYaml;

        foreach (var (key, value) in variables)
        {
            // Replace ${VAR_NAME} and $VAR_NAME patterns
            var patterns = new[]
            {
                $"${{${key}}}",  // ${VAR_NAME}
                $"${key}",       // $VAR_NAME (only if followed by non-alphanumeric)
            };

            foreach (var pattern in patterns)
            {
                result = result.Replace(pattern, value);
            }

            // Use regex for $VAR_NAME pattern to avoid partial replacements
            var regex = new Regex($@"\${Regex.Escape(key)}\b");
            result = regex.Replace(result, value);
        }

        _logger.LogInformation("Substituted {Count} environment variables in Docker Compose file", variables.Count);
        return await Task.FromResult(result);
    }

    public async Task<List<string>> ExtractServiceNamesAsync(string composeYaml)
    {
        var serviceNames = new List<string>();

        try
        {
            var parsedData = _yamlDeserializer.Deserialize<Dictionary<string, object>>(composeYaml);

            if (parsedData.TryGetValue("services", out var servicesObj) && servicesObj is Dictionary<object, object> services)
            {
                serviceNames = services.Keys.Select(k => k.ToString() ?? string.Empty).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting service names from Docker Compose file");
        }

        return await Task.FromResult(serviceNames);
    }

    public async Task<List<string>> ExtractVariablePlaceholdersAsync(string composeYaml)
    {
        var variables = new HashSet<string>();

        // Match ${VAR_NAME} pattern
        var bracePattern = new Regex(@"\$\{([A-Z_][A-Z0-9_]*)\}", RegexOptions.IgnoreCase);
        var braceMatches = bracePattern.Matches(composeYaml);
        foreach (Match match in braceMatches)
        {
            variables.Add(match.Groups[1].Value);
        }

        // Match $VAR_NAME pattern (must be followed by non-alphanumeric or end of line)
        var dollarPattern = new Regex(@"\$([A-Z_][A-Z0-9_]*)(?=[^A-Z0-9_]|$)", RegexOptions.IgnoreCase);
        var dollarMatches = dollarPattern.Matches(composeYaml);
        foreach (Match match in dollarMatches)
        {
            variables.Add(match.Groups[1].Value);
        }

        _logger.LogInformation("Extracted {Count} unique variable placeholders from Docker Compose file", variables.Count);
        return await Task.FromResult(variables.ToList());
    }
}
