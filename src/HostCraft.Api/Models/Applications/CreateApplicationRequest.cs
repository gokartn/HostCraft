using System.Collections.Generic;

namespace HostCraft.Api.Models.Applications;

public record CreateApplicationRequest(
    string Name,
    int ServerId,
    int ProjectId,
    string? Image,
    int? Replicas = 1,
    Dictionary<string, string>? EnvironmentVariables = null,
    List<string>? Networks = null,
    int? Port = null,
    List<PortMappingRequest>? PortMappings = null,
    string? Domain = null,
    string? AdditionalDomains = null,
    string? TraefikLabelOverrides = null,
    bool EnableHttps = true,
    bool ForceHttps = true,
    string? LetsEncryptEmail = null,
    // Git deployment fields
    string? SourceType = "DockerImage",
    int? GitProviderId = null,
    string? GitRepository = null,
    string? GitBranch = null,
    string? DockerfilePath = "Dockerfile",
    string? BuildContext = ".",
    // Docker build options
    string? DockerBuildTarget = null,
    string? BuildArgs = null,
    // Registry auth for private images
    bool UsePrivateRegistry = false,
    string? RegistryServer = null,
    string? RegistryUsername = null,
    string? RegistryPassword = null,
    // Git clone options
    bool CloneSubmodules = false,
    bool EnableGitLfs = true,
    // Auto-deploy options
    bool AutoDeployOnPush = true,
    // Preview deployment options
    bool EnablePreviewDeployments = false,
    string? PreviewUrlTemplate = null);
