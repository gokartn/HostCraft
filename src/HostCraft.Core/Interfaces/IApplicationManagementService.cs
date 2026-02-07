using System.Collections.Generic;

namespace HostCraft.Core.Interfaces;

/// <summary>
/// Service for managing application CRUD operations with validation.
/// </summary>
public interface IApplicationManagementService
{
    /// <summary>
    /// Create a new application with validation.
    /// </summary>
    Task<ApplicationCreationResult> CreateApplicationAsync(ApplicationCreationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing application with validation.
    /// </summary>
    Task<ApplicationUpdateResult> UpdateApplicationAsync(int applicationId, ApplicationUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete an application and cleanup resources.
    /// </summary>
    Task<ApplicationDeletionResult> DeleteApplicationAsync(int applicationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scale an application (Swarm services only).
    /// </summary>
    Task<ApplicationScaleResult> ScaleApplicationAsync(int applicationId, int replicas, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a new application.
/// </summary>
public record ApplicationCreationRequest(
    string Name,
    int ServerId,
    int ProjectId,
    string? Image,
    string SourceType,
    int? Replicas,
    Dictionary<string, string>? EnvironmentVariables,
    int? Port,
    string? PortMappings,
    string? Domain,
    string? AdditionalDomains,
    string? TraefikLabelOverrides,
    bool EnableHttps,
    bool ForceHttps,
    string? LetsEncryptEmail,
    int? GitProviderId,
    string? GitRepository,
    string? GitBranch,
    string? DockerfilePath,
    string? BuildContext,
    string? DockerBuildTarget,
    string? BuildArgs,
    bool UsePrivateRegistry,
    string? RegistryServer,
    string? RegistryUsername,
    string? RegistryPassword,
    bool CloneSubmodules,
    bool EnableGitLfs,
    bool AutoDeployOnPush,
    bool EnablePreviewDeployments,
    string? PreviewUrlTemplate);

/// <summary>
/// Request to update an existing application.
/// </summary>
public record ApplicationUpdateRequest(
    string? Name,
    string? Description,
    string? Image,
    int? Replicas,
    int? Port,
    string? PortMappings,
    string? Domain,
    string? AdditionalDomains,
    string? TraefikLabelOverrides,
    bool? EnableHttps,
    bool? ForceHttps,
    string? LetsEncryptEmail,
    string? GitRepository,
    string? GitBranch,
    int? GitProviderId,
    string? DockerfilePath,
    string? BuildContext,
    string? DockerBuildTarget,
    string? BuildArgs,
    bool? UsePrivateRegistry,
    string? RegistryServer,
    string? RegistryUsername,
    string? RegistryPassword,
    bool? AutoDeployOnPush,
    bool? EnablePreviewDeployments,
    string? PreviewUrlTemplate,
    int? MemoryLimitMb,
    long? CpuLimit,
    Dictionary<string, string>? EnvironmentVariables);

/// <summary>
/// Result of application creation.
/// </summary>
public record ApplicationCreationResult(
    bool Success,
    string Message,
    int? ApplicationId = null,
    int? DeploymentId = null,
    string? ErrorDetails = null);

/// <summary>
/// Result of application update.
/// </summary>
public record ApplicationUpdateResult(
    bool Success,
    string Message,
    string? ErrorDetails = null);

/// <summary>
/// Result of application deletion.
/// </summary>
public record ApplicationDeletionResult(
    bool Success,
    string Message,
    string? ErrorDetails = null);

/// <summary>
/// Result of application scaling.
/// </summary>
public record ApplicationScaleResult(
    bool Success,
    string Message,
    int? NewReplicas = null,
    string? ErrorDetails = null);
