using System;
using System.Collections.Generic;
using HostCraft.Core.Enums;

namespace HostCraft.Api.Models.Applications;

public record ApplicationWithDeploymentsDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int ServerId { get; init; }
    public string ServerName { get; init; } = string.Empty;
    public string ServerHost { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public ApplicationSourceType SourceType { get; init; }
    public DatabaseType? DatabaseType { get; init; }
    public string? DockerImage { get; init; }
    public bool UsePrivateRegistry { get; init; }
    public string? RegistryServer { get; init; }
    public string? RegistryUsername { get; init; }
    public bool HasRegistryPassword { get; init; }
    public int? GitProviderId { get; init; }
    public string? GitRepository { get; init; }
    public string? GitBranch { get; init; }
    public string? GitOwner { get; init; }
    public string? GitRepoName { get; init; }
    public string? Dockerfile { get; init; }
    public string? BuildContext { get; init; }
    public string? DockerBuildTarget { get; init; }
    public string? BuildArgs { get; init; }
    public bool CloneSubmodules { get; init; }
    public bool EnableGitLfs { get; init; }
    public bool AutoDeployOnPush { get; init; }
    public bool EnablePreviewDeployments { get; init; }
    public string? PreviewUrlTemplate { get; init; }
    public int? Port { get; init; }
    public int? PublishedPort { get; init; }
    public int Replicas { get; init; }
    public Dictionary<string, string>? EnvironmentVariables { get; init; }
    public string? Domain { get; init; }
    public string? AdditionalDomains { get; init; }
    public string? TraefikLabelOverrides { get; init; }
    public bool EnableHttps { get; init; }
    public bool ForceHttps { get; init; }
    public string? LetsEncryptEmail { get; init; }
    public DeploymentStatus Status { get; init; }
    public string? ContainerId { get; init; }
    public string? ServiceId { get; init; }
    public DateTime? LastDeployedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? ServiceName { get; init; }
    public List<DeploymentDto> Deployments { get; init; } = new();
}
