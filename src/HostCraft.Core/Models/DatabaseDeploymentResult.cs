using System.Collections.Generic;
using HostCraft.Core.Entities;

namespace HostCraft.Core.Models;

/// <summary>
/// Result of deploying a database template, including resolved credentials.
/// </summary>
public record class DatabaseDeploymentResult(
    Application Application,
    IReadOnlyList<ResolvedTemplateEnvironmentVariable> ResolvedEnvironmentVariables,
    IReadOnlyDictionary<string, string> EffectiveEnvironmentVariables);
