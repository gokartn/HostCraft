using System.IO;
using HostCraft.Api.Models.Applications;
using HostCraft.Api.Models.Shared;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models;
using HostCraft.Core.Models.Applications.Operations;
using Microsoft.AspNetCore.Http;
using HostCraft.Infrastructure.Proxy;

namespace HostCraft.Api.Services;

public class ApplicationsWorkflowService : IApplicationsWorkflowService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IServerRepository _serverRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IApplicationManagementService _applicationManagementService;
    private readonly IApplicationOperationsService _applicationOperationsService;
    private readonly ISecretManager _secretManager;

    public ApplicationsWorkflowService(
        IApplicationRepository applicationRepository,
        IServerRepository serverRepository,
        IProjectRepository projectRepository,
        IApplicationManagementService applicationManagementService,
        IApplicationOperationsService applicationOperationsService,
        ISecretManager secretManager)
    {
        _applicationRepository = applicationRepository;
        _serverRepository = serverRepository;
        _projectRepository = projectRepository;
        _applicationManagementService = applicationManagementService;
        _applicationOperationsService = applicationOperationsService;
        _secretManager = secretManager;
    }

    public async Task<ApiActionResult<IEnumerable<ApplicationDto>>> GetApplicationsAsync(int? serverId, int? projectId, CancellationToken cancellationToken)
    {
        var apps = await _applicationRepository.GetAllWithServerProjectAndLatestDeploymentAsync(serverId, projectId);
        var mapped = MapApplications(apps);
        return ApiActionResult<IEnumerable<ApplicationDto>>.Ok(mapped);
    }

    public async Task<ApiActionResult<PagedResult<ApplicationDto>>> GetApplicationsPagedAsync(int? serverId, int? projectId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _applicationRepository.GetPagedWithServerProjectAndLatestDeploymentAsync(serverId, projectId, page, pageSize);
        var mapped = MapApplications(items);
        return ApiActionResult<PagedResult<ApplicationDto>>.Ok(new PagedResult<ApplicationDto>(mapped, totalCount, page, pageSize));
    }

    public async Task<ApiActionResult<ApplicationWithDeploymentsDto>> GetApplicationAsync(int id, CancellationToken cancellationToken)
    {
        var app = await _applicationRepository.GetByIdWithServerProjectAndDeploymentsAsync(id);

        if (app == null)
            return ApiActionResult<ApplicationWithDeploymentsDto>.Fail(StatusCodes.Status404NotFound, "Application not found");

        var latestDeployment = app.Deployments.OrderByDescending(static d => d.StartedAt).FirstOrDefault();
        var environmentVariables = await _secretManager.GetEnvironmentVariablesAsync(app.Id);
        var envDictionary = environmentVariables
            .ToDictionary(static e => e.Key, static e => e.Value, StringComparer.OrdinalIgnoreCase);

        var dto = new ApplicationWithDeploymentsDto
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            ServerId = app.ServerId,
            ServerName = app.Server.Name,
            ServerHost = app.Server.Host,
            ProjectId = app.ProjectId,
            ProjectName = app.Project.Name,
            SourceType = app.SourceType,
            DatabaseType = app.DatabaseType,
            DockerImage = app.DockerImage,
            UsePrivateRegistry = app.UsePrivateRegistry,
            RegistryServer = app.RegistryServer,
            RegistryUsername = app.RegistryUsername,
            HasRegistryPassword = !string.IsNullOrEmpty(app.RegistryPassword),
            GitProviderId = app.GitProviderId,
            GitRepository = app.GitRepository,
            GitBranch = app.GitBranch,
            GitOwner = app.GitOwner,
            GitRepoName = app.GitRepoName,
            Dockerfile = app.Dockerfile,
            BuildContext = app.BuildContext,
            DockerBuildTarget = app.DockerBuildTarget,
            BuildArgs = app.BuildArgs,
            CloneSubmodules = app.CloneSubmodules,
            EnableGitLfs = app.EnableGitLfs,
            AutoDeployOnPush = app.AutoDeployOnPush,
            EnablePreviewDeployments = app.EnablePreviewDeployments,
            PreviewUrlTemplate = app.PreviewUrlTemplate,
            Port = app.Port,
            PublishedPort = app.PublishedPort,
            Replicas = app.Replicas,
            EnvironmentVariables = envDictionary,
            Domain = app.Domain,
            AdditionalDomains = app.AdditionalDomains,
            TraefikLabelOverrides = app.TraefikLabelOverrides,
            EnableHttps = app.EnableHttps,
            ForceHttps = app.ForceHttps,
            LetsEncryptEmail = app.LetsEncryptEmail,
            Status = latestDeployment?.Status ?? DeploymentStatus.Queued,
            ContainerId = latestDeployment?.ContainerId,
            ServiceId = latestDeployment?.ServiceId,
            LastDeployedAt = app.LastDeployedAt,
            CreatedAt = app.CreatedAt,
            ServiceName = app.ServiceName,
            Deployments = app.Deployments.Select(static d => new DeploymentDto
            {
                Id = d.Id,
                Status = d.Status,
                ContainerId = d.ContainerId,
                ServiceId = d.ServiceId,
                StartedAt = d.StartedAt,
                FinishedAt = d.FinishedAt,
                ErrorMessage = d.ErrorMessage
            }).ToList()
        };

        return ApiActionResult<ApplicationWithDeploymentsDto>.Ok(dto);
    }

    public async Task<ApiActionResult<IEnumerable<ServerResponseDto>>> GetServersAsync(CancellationToken cancellationToken)
    {
        var servers = await _serverRepository.GetNonWorkersWithRegionAsync();
        var mapped = servers.Select(static s => new ServerResponseDto(s.Id, s.Name, s.Host, s.Port, s.Username, s.IsSwarm, s.Status.ToString()));
        return ApiActionResult<IEnumerable<ServerResponseDto>>.Ok(mapped);
    }

    public async Task<ApiActionResult<IEnumerable<ProjectDto>>> GetProjectsAsync(CancellationToken cancellationToken)
    {
        var projects = await _projectRepository.GetAllAsync();
        var mapped = projects.Select(static p => new ProjectDto(p.Id, p.Name, p.Description));
        return ApiActionResult<IEnumerable<ProjectDto>>.Ok(mapped);
    }

    public async Task<ApiActionResult<ApplicationDto>> CreateApplicationAsync(CreateApplicationRequest request, CancellationToken cancellationToken)
    {
        var serviceRequest = new ApplicationCreationRequest(
            Name: request.Name,
            ServerId: request.ServerId,
            ProjectId: request.ProjectId,
            Image: request.Image,
            SourceType: request.SourceType ?? "DockerImage",
            Replicas: request.Replicas,
            EnvironmentVariables: request.EnvironmentVariables,
            Port: request.Port,
            PortMappings: request.PortMappings != null
                ? System.Text.Json.JsonSerializer.Serialize(request.PortMappings)
                : null,
            Domain: request.Domain,
            AdditionalDomains: request.AdditionalDomains,
            EnableHttps: request.EnableHttps,
            ForceHttps: request.ForceHttps,
            LetsEncryptEmail: request.LetsEncryptEmail,
            GitProviderId: request.GitProviderId,
            GitRepository: request.GitRepository,
            GitBranch: request.GitBranch,
            DockerfilePath: request.DockerfilePath,
            BuildContext: request.BuildContext,
            DockerBuildTarget: request.DockerBuildTarget,
            BuildArgs: request.BuildArgs,
            UsePrivateRegistry: request.UsePrivateRegistry,
            RegistryServer: request.RegistryServer,
            RegistryUsername: request.RegistryUsername,
            RegistryPassword: request.RegistryPassword,
            CloneSubmodules: request.CloneSubmodules,
            EnableGitLfs: request.EnableGitLfs,
            AutoDeployOnPush: request.AutoDeployOnPush,
            EnablePreviewDeployments: request.EnablePreviewDeployments,
            PreviewUrlTemplate: request.PreviewUrlTemplate,
            TraefikLabelOverrides: request.TraefikLabelOverrides);

        var result = await _applicationManagementService.CreateApplicationAsync(serviceRequest);

        if (!result.Success)
            return ApiActionResult<ApplicationDto>.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Failed to create application");

        if (result.ApplicationId is null)
            return ApiActionResult<ApplicationDto>.Fail(StatusCodes.Status500InternalServerError, "Application created but ID was not returned");

        var app = await _applicationRepository.GetByIdWithServerProjectAndDeploymentsAsync(result.ApplicationId.Value);

        if (app == null)
            return ApiActionResult<ApplicationDto>.Fail(StatusCodes.Status500InternalServerError, "Application created but could not be retrieved");

        return ApiActionResult<ApplicationDto>.Ok(new ApplicationDto
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            ServerId = app.ServerId,
            ServerName = app.Server.Name,
            ProjectId = app.ProjectId,
            ProjectName = app.Project.Name,
            DockerImage = app.DockerImage,
            UsePrivateRegistry = app.UsePrivateRegistry,
            RegistryServer = app.RegistryServer,
            RegistryUsername = app.RegistryUsername,
            HasRegistryPassword = !string.IsNullOrEmpty(app.RegistryPassword),
            Status = DeploymentStatus.Queued,
            CreatedAt = app.CreatedAt
        }, StatusCodes.Status201Created);
    }

    public async Task<ApiActionResult> ScaleApplicationAsync(int id, int replicas, CancellationToken cancellationToken)
    {
        var result = await _applicationManagementService.ScaleApplicationAsync(id, replicas);

        if (!result.Success)
            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Failed to scale application");

        return ApiActionResult.Ok();
    }

    public async Task<ApiActionResult> RedeployAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.RedeployAsync(id, cancellationToken);

        if (!result.Success)
        {
            if (string.Equals(result.ErrorMessage, "Application not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.ErrorMessage ?? "Application not found");

            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, result.ErrorMessage ?? "Failed to queue deployment");
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<string>> GetApplicationLogsAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.GetLogsAsync(id, cancellationToken);

        if (!result.Success)
        {
            if (string.Equals(result.ErrorMessage, "Application not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult<string>.Fail(StatusCodes.Status404NotFound, result.ErrorMessage ?? "Application not found");

            return ApiActionResult<string>.Fail(StatusCodes.Status400BadRequest, result.ErrorMessage ?? "Unable to retrieve logs");
        }

        if (result.Data == null)
            return ApiActionResult<string>.Fail(StatusCodes.Status500InternalServerError, "Log stream was empty");

        using var reader = new StreamReader(result.Data, leaveOpen: false);
        var content = await reader.ReadToEndAsync();

        return ApiActionResult<string>.Ok(content, StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<ApplicationWithDeploymentsDto>> UpdateApplicationAsync(int id, UpdateApplicationRequest request, CancellationToken cancellationToken)
    {
        var serviceRequest = new ApplicationUpdateRequest(
            Name: request.Name,
            Description: request.Description,
            Image: request.DockerImage,
            Replicas: request.Replicas,
            Port: request.Port,
            PortMappings: null,
            Domain: request.Domain,
            AdditionalDomains: request.AdditionalDomains,
            TraefikLabelOverrides: request.TraefikLabelOverrides,
            EnableHttps: request.EnableHttps,
            ForceHttps: request.ForceHttps,
            LetsEncryptEmail: request.LetsEncryptEmail,
            GitRepository: request.GitRepository,
            GitBranch: request.GitBranch,
            GitProviderId: request.GitProviderId,
            DockerfilePath: request.Dockerfile,
            BuildContext: request.BuildContext,
            DockerBuildTarget: request.DockerBuildTarget,
            BuildArgs: request.BuildArgs,
            UsePrivateRegistry: request.UsePrivateRegistry,
            RegistryServer: request.RegistryServer,
            RegistryUsername: request.RegistryUsername,
            RegistryPassword: request.RegistryPassword,
            AutoDeployOnPush: request.AutoDeployOnPush,
            CloneSubmodules: request.CloneSubmodules,
            EnableGitLfs: request.EnableGitLfs,
            EnablePreviewDeployments: request.EnablePreviewDeployments,
            PreviewUrlTemplate: request.PreviewUrlTemplate,
            MemoryLimitMb: request.MemoryLimitMb,
            CpuLimit: request.CpuLimit,
            EnvironmentVariables: request.EnvironmentVariables
        );

        var result = await _applicationManagementService.UpdateApplicationAsync(id, serviceRequest);

        if (!result.Success)
            return ApiActionResult<ApplicationWithDeploymentsDto>.Fail(StatusCodes.Status400BadRequest, result.Message ?? "Failed to update application");

        var app = await _applicationRepository.GetByIdWithServerProjectAndDeploymentsAsync(id);

        if (app == null)
            return ApiActionResult<ApplicationWithDeploymentsDto>.Fail(StatusCodes.Status404NotFound, "Application not found");

        var latestDeployment = app.Deployments.OrderByDescending(static d => d.StartedAt).FirstOrDefault();
        var environmentVariables = await _secretManager.GetEnvironmentVariablesAsync(app.Id);
        var envDictionary = environmentVariables
            .ToDictionary(static e => e.Key, static e => e.Value, StringComparer.OrdinalIgnoreCase);

        var dto = new ApplicationWithDeploymentsDto
        {
            Id = app.Id,
            Name = app.Name,
            Description = app.Description,
            ServerId = app.ServerId,
            ServerName = app.Server.Name,
            ServerHost = app.Server.Host,
            ProjectId = app.ProjectId,
            ProjectName = app.Project.Name,
            SourceType = app.SourceType,
            DatabaseType = app.DatabaseType,
            DockerImage = app.DockerImage,
            UsePrivateRegistry = app.UsePrivateRegistry,
            RegistryServer = app.RegistryServer,
            RegistryUsername = app.RegistryUsername,
            HasRegistryPassword = !string.IsNullOrEmpty(app.RegistryPassword),
            GitProviderId = app.GitProviderId,
            GitRepository = app.GitRepository,
            GitBranch = app.GitBranch,
            GitOwner = app.GitOwner,
            GitRepoName = app.GitRepoName,
            Dockerfile = app.Dockerfile,
            BuildContext = app.BuildContext,
            DockerBuildTarget = app.DockerBuildTarget,
            BuildArgs = app.BuildArgs,
            CloneSubmodules = app.CloneSubmodules,
            EnableGitLfs = app.EnableGitLfs,
            AutoDeployOnPush = app.AutoDeployOnPush,
            EnablePreviewDeployments = app.EnablePreviewDeployments,
            PreviewUrlTemplate = app.PreviewUrlTemplate,
            Port = app.Port,
            Replicas = app.Replicas,
            EnvironmentVariables = envDictionary,
            Domain = app.Domain,
            AdditionalDomains = app.AdditionalDomains,
            TraefikLabelOverrides = app.TraefikLabelOverrides,
            EnableHttps = app.EnableHttps,
            ForceHttps = app.ForceHttps,
            LetsEncryptEmail = app.LetsEncryptEmail,
            Status = latestDeployment?.Status ?? DeploymentStatus.Queued,
            ContainerId = latestDeployment?.ContainerId,
            ServiceId = latestDeployment?.ServiceId,
            PublishedPort = app.PublishedPort,
            LastDeployedAt = app.LastDeployedAt,
            CreatedAt = app.CreatedAt,
            Deployments = app.Deployments.Select(static d => new DeploymentDto
            {
                Id = d.Id,
                Status = d.Status,
                ContainerId = d.ContainerId,
                ServiceId = d.ServiceId,
                StartedAt = d.StartedAt,
                FinishedAt = d.FinishedAt,
                ErrorMessage = d.ErrorMessage
            }).ToList()
        };

        return ApiActionResult<ApplicationWithDeploymentsDto>.Ok(dto);
    }

    public async Task<ApiActionResult<TraefikPreviewResponse>> GetTraefikPreviewAsync(int id, CancellationToken cancellationToken)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDomainsAsync(id, cancellationToken);

        if (app == null)
            return ApiActionResult<TraefikPreviewResponse>.Fail(StatusCodes.Status404NotFound, "Application not found");

        var warnings = new List<string>();
        var baseLabels = TraefikLabelBuilder.BuildLabels(app, "hostcraft_hostcraft-network", includeApplicationOverrides: false);

        if (!TraefikLabelBuilder.TryParseOverrides(app.TraefikLabelOverrides, out var overrides, out var error, warnings))
        {
            return ApiActionResult<TraefikPreviewResponse>.Fail(StatusCodes.Status400BadRequest, error ?? "Invalid Traefik overrides");
        }

        var merged = TraefikLabelBuilder.MergeWithOverrides(baseLabels, overrides, warnings);

        return ApiActionResult<TraefikPreviewResponse>.Ok(new TraefikPreviewResponse(baseLabels, overrides, merged, warnings));
    }

    public async Task<ApiActionResult<TraefikPreviewResponse>> PreviewTraefikOverridesAsync(int id, TraefikOverridesRequest request, CancellationToken cancellationToken)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDomainsAsync(id, cancellationToken);

        if (app == null)
            return ApiActionResult<TraefikPreviewResponse>.Fail(StatusCodes.Status404NotFound, "Application not found");

        var warnings = new List<string>();
        var baseLabels = TraefikLabelBuilder.BuildLabels(app, "hostcraft_hostcraft-network", includeApplicationOverrides: false);

        if (!TraefikLabelBuilder.TryParseOverrides(request.Overrides, out var overrides, out var error, warnings))
        {
            return ApiActionResult<TraefikPreviewResponse>.Fail(StatusCodes.Status400BadRequest, error ?? "Invalid Traefik overrides");
        }

        var merged = TraefikLabelBuilder.MergeWithOverrides(baseLabels, overrides, warnings);

        return ApiActionResult<TraefikPreviewResponse>.Ok(new TraefikPreviewResponse(baseLabels, overrides, merged, warnings));
    }

    public async Task<ApiActionResult> UpdateTraefikOverridesAsync(int id, TraefikOverridesRequest request, CancellationToken cancellationToken)
    {
        var app = await _applicationRepository.GetByIdWithServerAndEnvironmentAsync(id, cancellationToken);

        if (app == null)
            return ApiActionResult.Fail(StatusCodes.Status404NotFound, "Application not found");

        var warnings = new List<string>();
        if (!TraefikLabelBuilder.TryParseOverrides(request.Overrides, out var overrides, out var error, warnings))
        {
            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, error ?? "Invalid Traefik overrides");
        }

        app.TraefikLabelOverrides = overrides.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(overrides, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await _applicationRepository.UpdateAsync(app, cancellationToken);
        return ApiActionResult.Ok(StatusCodes.Status204NoContent);
    }

    public async Task<ApiActionResult> DeleteApplicationAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _applicationManagementService.DeleteApplicationAsync(id, cancellationToken);

        if (!result.Success)
        {
            if (string.Equals(result.Message, "Application not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, result.Message ?? "Application not found");

            return ApiActionResult.Fail(StatusCodes.Status500InternalServerError, result.Message ?? "Failed to delete application");
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<ApplicationDto>> DeployComposeAsync(Core.Models.DeployComposeRequest request, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.DeployComposeAsync(request, cancellationToken);

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Failed to deploy compose application";
            if (string.Equals(message, "Server not found", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(message, "Project not found", StringComparison.OrdinalIgnoreCase))
            {
                return ApiActionResult<ApplicationDto>.Fail(StatusCodes.Status404NotFound, message);
            }

            return ApiActionResult<ApplicationDto>.Fail(StatusCodes.Status400BadRequest, message);
        }

        var data = result.Data!;
        return ApiActionResult<ApplicationDto>.Ok(new ApplicationDto
        {
            Id = data.ApplicationId,
            Name = data.Name,
            Description = data.Description,
            ServerId = data.ServerId,
            ServerName = data.ServerName,
            ProjectId = data.ProjectId,
            ProjectName = data.ProjectName,
            Status = DeploymentStatus.Queued,
            CreatedAt = data.CreatedAt
        }, StatusCodes.Status201Created);
    }

    public async Task<ApiActionResult<ValidateComposeResponse>> ValidateComposeAsync(Core.Models.ValidateComposeRequest request, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.ValidateComposeAsync(request.ComposeFile, cancellationToken);

        if (!result.Success)
            return ApiActionResult<ValidateComposeResponse>.Fail(StatusCodes.Status400BadRequest, result.ErrorMessage ?? "Failed to validate compose file");

        var data = result.Data!;
        var response = new ValidateComposeResponse
        {
            IsValid = data.IsValid,
            Errors = data.Errors,
            Warnings = data.Warnings,
            ServiceNames = data.ServiceNames,
            RequiredVariables = data.RequiredVariables,
            ComposeVersion = data.ComposeVersion
        };

        return ApiActionResult<ValidateComposeResponse>.Ok(response);
    }

    public async Task<ApiActionResult<IEnumerable<StackInfoDto>>> ListStacksAsync(int? serverId, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.ListStacksAsync(serverId, cancellationToken);

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Failed to list stacks";
            if (string.Equals(message, "Server not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult<IEnumerable<StackInfoDto>>.Fail(StatusCodes.Status404NotFound, message);

            return ApiActionResult<IEnumerable<StackInfoDto>>.Fail(StatusCodes.Status400BadRequest, message);
        }

        var data = result.Data ?? Enumerable.Empty<StackSummary>();
        var mapped = data.Select(static s => new StackInfoDto(s.StackName, s.ServiceCount, s.CreatedAt ?? DateTime.MinValue));
        return ApiActionResult<IEnumerable<StackInfoDto>>.Ok(mapped);
    }

    public async Task<ApiActionResult> RemoveStackAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.RemoveStackAsync(id, cancellationToken);

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Failed to remove stack";
            if (string.Equals(message, "Application not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, message);

            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, message);
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult<ApplicationStatusDto>> GetApplicationStatusAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.GetStatusAsync(id, cancellationToken);

        if (!result.Success)
        {
            if (string.Equals(result.ErrorMessage, "Application not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult<ApplicationStatusDto>.Fail(StatusCodes.Status404NotFound, result.ErrorMessage ?? "Application not found");

            return ApiActionResult<ApplicationStatusDto>.Fail(StatusCodes.Status400BadRequest, result.ErrorMessage ?? "Unable to fetch status");
        }

        var data = result.Data!;
        var dto = new ApplicationStatusDto
        {
            ApplicationId = data.ApplicationId,
            Status = data.Status,
            IsRunning = data.IsRunning,
            ActualState = data.ActualState,
            ContainerId = data.ContainerId,
            ServiceId = data.ServiceId,
            Placements = data.Placements?.Select(p => new ReplicaPlacementDto(
                p.TaskId,
                p.NodeId,
                p.NodeName,
                p.Role,
                p.Availability,
                p.DesiredState,
                p.CurrentState,
                p.Error,
                p.Slot,
                p.UpdatedAt)) ?? Array.Empty<ReplicaPlacementDto>()
        };

        return ApiActionResult<ApplicationStatusDto>.Ok(dto);
    }

    public async Task<ApiActionResult<ApplicationMetricsDto>> GetApplicationMetricsAsync(int id, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.GetMetricsAsync(id, cancellationToken);

        if (!result.Success)
        {
            var statusCode = string.Equals(result.ErrorMessage, "Application not found", StringComparison.OrdinalIgnoreCase)
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;

            return ApiActionResult<ApplicationMetricsDto>.Fail(statusCode, result.ErrorMessage ?? "Unable to fetch metrics");
        }

        var data = result.Data!;

        var dto = new ApplicationMetricsDto(
            data.Mode,
            data.TotalCpuPercent,
            data.TotalMemoryPercent,
            data.TotalMemoryUsageBytes,
            data.TotalMemoryLimitBytes,
            data.NetworkRxBytes,
            data.NetworkTxBytes,
            data.BlockReadBytes,
            data.BlockWriteBytes,
            data.Timestamp,
            data.Containers.Select(c => new ApplicationContainerMetricsDto(
                c.ContainerId,
                c.Name,
                c.NodeName,
                c.CpuPercent,
                c.MemoryUsageBytes,
                c.MemoryLimitBytes,
                c.MemoryPercent,
                c.NetworkRxBytes,
                c.NetworkTxBytes,
                c.BlockReadBytes,
                c.BlockWriteBytes,
                c.Timestamp)).ToList());

        return ApiActionResult<ApplicationMetricsDto>.Ok(dto);
    }

    public async Task<ApiActionResult<OrphanedResourcesDto>> GetOrphanedResourcesAsync(int? serverId, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.GetOrphanedResourcesAsync(serverId, cancellationToken);

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Failed to get orphaned resources";
            if (string.Equals(message, "Server not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult<OrphanedResourcesDto>.Fail(StatusCodes.Status404NotFound, message);

            return ApiActionResult<OrphanedResourcesDto>.Fail(StatusCodes.Status400BadRequest, message);
        }

        var data = result.Data!;
        var dto = new OrphanedResourcesDto
        {
            OrphanedContainers = data.Containers.Select(static c => new OrphanedContainerDto
            {
                ContainerId = c.ContainerId,
                ContainerName = c.ContainerName,
                Image = c.Image,
                State = c.State,
                ServerId = c.ServerId,
                ServerName = c.ServerName,
                ApplicationId = c.ApplicationId ?? 0,
                Labels = c.Labels
            }).ToList(),
            OrphanedServices = data.Services.Select(static s => new OrphanedServiceDto
            {
                ServiceId = s.ServiceId,
                ServiceName = s.ServiceName,
                Image = s.Image,
                Replicas = s.Replicas,
                ServerId = s.ServerId,
                ServerName = s.ServerName,
                ApplicationId = s.ApplicationId ?? 0,
                Labels = s.Labels
            }).ToList()
        };

        return ApiActionResult<OrphanedResourcesDto>.Ok(dto);
    }

    public async Task<ApiActionResult> CleanupOrphanedContainerAsync(string containerId, int serverId, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.CleanupOrphanedContainerAsync(containerId, serverId, cancellationToken);

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Failed to remove orphaned container";
            if (string.Equals(message, "Server not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, message);

            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, message);
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    public async Task<ApiActionResult> CleanupOrphanedServiceAsync(string serviceId, int serverId, CancellationToken cancellationToken)
    {
        var result = await _applicationOperationsService.CleanupOrphanedServiceAsync(serviceId, serverId, cancellationToken);

        if (!result.Success)
        {
            var message = result.ErrorMessage ?? "Failed to remove orphaned service";
            if (string.Equals(message, "Server not found", StringComparison.OrdinalIgnoreCase))
                return ApiActionResult.Fail(StatusCodes.Status404NotFound, message);

            return ApiActionResult.Fail(StatusCodes.Status400BadRequest, message);
        }

        return ApiActionResult.Ok(StatusCodes.Status200OK);
    }

    private static List<ApplicationDto> MapApplications(IEnumerable<Application> applications)
    {
        return applications.Select(static a => new ApplicationDto
        {
            Id = a.Id,
            Name = a.Name,
            Description = a.Description,
            ServerId = a.ServerId,
            ServerName = a.Server.Name,
            ProjectId = a.ProjectId,
            ProjectName = a.Project.Name,
            DockerImage = a.DockerImage,
            UsePrivateRegistry = a.UsePrivateRegistry,
            RegistryServer = a.RegistryServer,
            RegistryUsername = a.RegistryUsername,
            HasRegistryPassword = !string.IsNullOrEmpty(a.RegistryPassword),
            Status = a.Deployments.OrderByDescending(static d => d.StartedAt).FirstOrDefault()?.Status ?? DeploymentStatus.Queued,
            LastDeployedAt = a.LastDeployedAt,
            CreatedAt = a.CreatedAt
        }).ToList();
    }
}
