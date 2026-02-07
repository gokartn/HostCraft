using System.Collections.Generic;

namespace HostCraft.Api.Models.Applications;

/// <summary>
/// Request to update an application. All fields are optional - only provided fields will be updated.
/// </summary>
public record UpdateApplicationRequest(
    // Basic settings
    string? Name = null,
    string? Description = null,
    int? Port = null,
    int? Replicas = null,

    // Domain & SSL configuration
    string? Domain = null,
    string? AdditionalDomains = null,
    string? TraefikLabelOverrides = null,
    bool? EnableHttps = null,
    bool? ForceHttps = null,
    string? LetsEncryptEmail = null,

    // Docker configuration
    string? DockerImage = null,
    string? Dockerfile = null,
    string? BuildContext = null,
    string? DockerBuildTarget = null,
    string? BuildArgs = null,
    bool? UsePrivateRegistry = null,
    string? RegistryServer = null,
    string? RegistryUsername = null,
    string? RegistryPassword = null,

    // Git configuration
    string? GitRepository = null,
    string? GitBranch = null,
    int? GitProviderId = null,
    bool? AutoDeployOnPush = null,

    // Resource limits
    int? MemoryLimitMb = null,
    long? CpuLimit = null,

    // Environment variables
    Dictionary<string, string>? EnvironmentVariables = null
);
