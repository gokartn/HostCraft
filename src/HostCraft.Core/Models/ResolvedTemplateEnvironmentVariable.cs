using System.Collections.Generic;

namespace HostCraft.Core.Models;

/// <summary>
/// Captures the resolved value for a best-practice environment variable during deployment.
/// </summary>
public record class ResolvedTemplateEnvironmentVariable(
    string Key,
    string Label,
    string Value,
    bool IsSecret,
    bool IsUserProvided,
    string Description,
    bool DisplayInWizard);

/// <summary>
/// Contains both the resolved environment variables and the metadata that drove them.
/// </summary>
public record class ResolvedEnvironmentVariablesResult(
    IReadOnlyDictionary<string, string> EffectiveVariables,
    IReadOnlyList<ResolvedTemplateEnvironmentVariable> ResolvedDefinitions);
