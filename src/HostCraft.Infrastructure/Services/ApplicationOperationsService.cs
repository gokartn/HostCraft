using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Docker.DotNet;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models;
using HostCraft.Core.Models.Applications.Operations;
using HostCraft.Core.Models.Results;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Handles runtime application operations that should not live inside controllers.
/// </summary>
public class ApplicationOperationsService : IApplicationOperationsService
{
    private readonly IApplicationRepository _applicationRepository;
    private readonly IServerRepository _serverRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly IDeploymentRepository _deploymentRepository;
    private readonly IComposeEnvironmentVariableRepository _composeEnvVarRepository;
    private readonly IComposeParser _composeParser;
    private readonly IStackService _stackService;
    private readonly IDockerService _dockerService;
    private readonly IDeploymentJobQueue _deploymentJobQueue;
    private readonly ILogger<ApplicationOperationsService> _logger;

    public ApplicationOperationsService(
        IApplicationRepository applicationRepository,
        IServerRepository serverRepository,
        IProjectRepository projectRepository,
        IDeploymentRepository deploymentRepository,
        IComposeEnvironmentVariableRepository composeEnvVarRepository,
        IComposeParser composeParser,
        IStackService stackService,
        IDockerService dockerService,
        IDeploymentJobQueue deploymentJobQueue,
        ILogger<ApplicationOperationsService> logger)
    {
        _applicationRepository = applicationRepository;
        _serverRepository = serverRepository;
        _projectRepository = projectRepository;
        _deploymentRepository = deploymentRepository;
        _composeEnvVarRepository = composeEnvVarRepository;
        _composeParser = composeParser;
        _stackService = stackService;
        _dockerService = dockerService;
        _deploymentJobQueue = deploymentJobQueue;
        _logger = logger;
    }

    public async Task<OperationResult<DeploymentQueueResult>> RedeployAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithServerAndEnvironmentAsync(applicationId, cancellationToken);

        if (app == null)
            return OperationResult.Failure<DeploymentQueueResult>("Application not found");

        var deployment = new Deployment
        {
            Uuid = Guid.NewGuid(),
            ApplicationId = app.Id,
            Status = DeploymentStatus.Queued,
            StartedAt = DateTime.UtcNow
        };

        await _deploymentRepository.AddAsync(deployment, cancellationToken);
        await _deploymentJobQueue.EnqueueAsync(new Core.Models.DeploymentJob(Core.Models.DeploymentJobType.Deploy, deployment.Id), cancellationToken);

        return OperationResult.Success(new DeploymentQueueResult(deployment.Id, "Deployment queued"));
    }

    public async Task<OperationResult<Stream>> GetLogsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDeploymentsAsync(applicationId, cancellationToken);

        if (app == null)
            return OperationResult.Failure<Stream>("Application not found");

        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();

        if (latestDeployment == null)
            return OperationResult.Failure<Stream>("No deployments found");

        try
        {
            Stream logStream;

            if (!string.IsNullOrEmpty(latestDeployment.ServiceId))
            {
                logStream = await _dockerService.GetServiceLogsAsync(app.Server, latestDeployment.ServiceId);
            }
            else if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
            {
                logStream = await _dockerService.GetContainerLogsAsync(app.Server, latestDeployment.ContainerId);
            }
            else
            {
                return OperationResult.Failure<Stream>("No container or service ID found");
            }

            return OperationResult.Success(logStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs for application {AppId}", applicationId);
            return OperationResult.Failure<Stream>(ex.Message);
        }
    }

    public async Task<OperationResult<IReadOnlyList<ServiceTaskContainerRef>>> GetServiceTasksAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDeploymentsAsync(applicationId, cancellationToken);

        if (app == null)
            return OperationResult.Failure<IReadOnlyList<ServiceTaskContainerRef>>("Application not found");

        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();

        if (latestDeployment == null)
            return OperationResult.Failure<IReadOnlyList<ServiceTaskContainerRef>>("No deployments found");

        if (string.IsNullOrEmpty(latestDeployment.ServiceId))
            return OperationResult.Failure<IReadOnlyList<ServiceTaskContainerRef>>("Application is not deployed as a service");

        try
        {
            var tasks = await _dockerService.ListServiceTaskContainersAsync(app.Server, latestDeployment.ServiceId, cancellationToken);
            return OperationResult.Success(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service tasks for application {AppId}", applicationId);
            return OperationResult.Failure<IReadOnlyList<ServiceTaskContainerRef>>(ex.Message);
        }
    }

    public async Task<OperationResult<Stream>> GetTaskLogsAsync(int applicationId, string taskId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDeploymentsAsync(applicationId, cancellationToken);

        if (app == null)
            return OperationResult.Failure<Stream>("Application not found");

        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();

        if (latestDeployment == null)
            return OperationResult.Failure<Stream>("No deployments found");

        if (string.IsNullOrEmpty(latestDeployment.ServiceId))
            return OperationResult.Failure<Stream>("Application is not deployed as a service");

        try
        {
            var logStream = await _dockerService.GetTaskLogsAsync(app.Server, taskId, cancellationToken);
            return OperationResult.Success(logStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task logs for task {TaskId} in application {AppId}", taskId, applicationId);
            return OperationResult.Failure<Stream>(ex.Message);
        }
    }

    public async Task<OperationResult<ApplicationComposeResult>> DeployComposeAsync(DeployComposeRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var server = await _serverRepository.GetByIdAsync(request.ServerId, cancellationToken);
            if (server == null)
                return OperationResult.Failure<ApplicationComposeResult>("Server not found");

            if (!server.IsSwarmManager)
                return OperationResult.Failure<ApplicationComposeResult>("Docker Compose deployments require a Swarm manager server");

            var project = await _projectRepository.GetByIdAsync(request.ProjectId, cancellationToken);
            if (project == null)
                return OperationResult.Failure<ApplicationComposeResult>("Project not found");

            var validationResult = await _composeParser.ValidateYamlAsync(request.ComposeFile);
            if (!validationResult.IsValid)
                return OperationResult.Failure<ApplicationComposeResult>("Invalid Docker Compose file");

            var application = new Application
            {
                Name = request.Name,
                Description = request.Description,
                ProjectId = request.ProjectId,
                ServerId = request.ServerId,
                SourceType = ApplicationSourceType.DockerCompose,
                DockerComposeFile = request.ComposeFile,
                DeploymentMode = DeploymentMode.Service,
                CreatedAt = DateTime.UtcNow
            };

            application = await _applicationRepository.AddAsync(application, cancellationToken);

            if (request.EnvironmentVariables.Any())
            {
                var composeVars = request.EnvironmentVariables.Select(envVar => new ComposeEnvironmentVariable
                {
                    ApplicationId = application.Id,
                    Key = envVar.Key,
                    Value = envVar.Value,
                    IsSecret = envVar.IsSecret,
                    Description = envVar.Description
                });
                await _composeEnvVarRepository.AddRangeAsync(composeVars, cancellationToken);
            }

            var result = new ApplicationComposeResult(
                application.Id,
                application.Name,
                application.Description,
                application.ServerId,
                server.Name,
                application.ProjectId,
                project.Name,
                application.CreatedAt);

            return OperationResult.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying Docker Compose application");
            return OperationResult.Failure<ApplicationComposeResult>(ex.Message);
        }
    }

    public async Task<OperationResult<ComposeValidationDetails>> ValidateComposeAsync(string composeFile, CancellationToken cancellationToken = default)
    {
        try
        {
            var validationResult = await _composeParser.ValidateYamlAsync(composeFile);
            var parseResult = await _composeParser.ParseComposeAsync(composeFile);
            var placeholders = await _composeParser.ExtractVariablePlaceholdersAsync(composeFile);

            var result = new ComposeValidationDetails
            {
                IsValid = validationResult.IsValid,
                Errors = validationResult.Errors,
                Warnings = validationResult.Warnings,
                ServiceNames = parseResult.ServiceNames,
                RequiredVariables = placeholders,
                ComposeVersion = parseResult.ComposeVersion
            };

            return OperationResult.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Docker Compose file");
            return OperationResult.Failure<ComposeValidationDetails>(ex.Message);
        }
    }

    public async Task<OperationResult<IEnumerable<StackSummary>>> ListStacksAsync(int? serverId, CancellationToken cancellationToken = default)
    {
        try
        {
            IEnumerable<Server> servers;

            if (serverId.HasValue)
            {
                var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId.Value, cancellationToken);
                if (server == null)
                    return OperationResult.Failure<IEnumerable<StackSummary>>("Server not found");

                if (!server.IsSwarmManager)
                    return OperationResult.Failure<IEnumerable<StackSummary>>("Docker stacks can only be listed on swarm manager servers");

                servers = new[] { server };
            }
            else
            {
                servers = await _serverRepository.GetSwarmManagersAsync(cancellationToken);
            }

            var allStacks = new List<StackSummary>();

            foreach (var server in servers)
            {
                try
                {
                    var stacks = await _stackService.ListStacksAsync(server, cancellationToken);
                    allStacks.AddRange(stacks.Select(stack => new StackSummary(
                        server.Id,
                        server.Name,
                        stack.Name,
                        stack.ServiceCount,
                        stack.CreatedAt)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to list stacks on server {ServerId}", server.Id);
                }
            }

            return OperationResult.Success<IEnumerable<StackSummary>>(allStacks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing Docker stacks");
            return OperationResult.Failure<IEnumerable<StackSummary>>(ex.Message);
        }
    }

    public async Task<OperationResult<bool>> RemoveStackAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var app = await _applicationRepository.GetByIdWithServerAsync(applicationId, cancellationToken);

            if (app == null)
                return OperationResult.Failure<bool>("Application not found");

            if (app.SourceType != ApplicationSourceType.DockerCompose)
                return OperationResult.Failure<bool>("Application is not a Docker Compose deployment");

            if (string.IsNullOrEmpty(app.SwarmServiceId))
                return OperationResult.Failure<bool>("No stack name found for application");

            var success = await _stackService.RemoveStackAsync(app.Server, app.SwarmServiceId, cancellationToken);

            return success
                ? OperationResult.Success(true)
                : OperationResult.Failure<bool>("Failed to remove stack");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing Docker stack for application {AppId}", applicationId);
            return OperationResult.Failure<bool>(ex.Message);
        }
    }

    public async Task<OperationResult<ApplicationStatusInfo>> GetStatusAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDeploymentsAsync(applicationId, cancellationToken);

        if (app == null)
            return OperationResult.Failure<ApplicationStatusInfo>("Application not found");

        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();

        if (latestDeployment == null)
        {
            return OperationResult.Success(new ApplicationStatusInfo(
                app.Id,
                "not-deployed",
                false));
        }

        try
        {
            bool isRunning = false;
            string? actualState = null;
            List<ServiceReplicaPlacementInfo> placements = new();

            if (!string.IsNullOrEmpty(latestDeployment.ServiceId))
            {
                var serviceInfo = await _dockerService.InspectServiceAsync(app.Server, latestDeployment.ServiceId, cancellationToken);
                isRunning = serviceInfo != null;
                actualState = isRunning ? "running" : "not-found";

                if (serviceInfo != null)
                {
                    var tasks = await _dockerService.ListServiceTasksAsync(app.Server, latestDeployment.ServiceId, cancellationToken);
                    var nodes = await _dockerService.ListNodesAsync(app.Server, cancellationToken);
                    var nodeLookup = nodes.ToDictionary(n => n.Id, n => n);

                    foreach (var task in tasks)
                    {
                        nodeLookup.TryGetValue(task.NodeId, out var node);
                        placements.Add(new ServiceReplicaPlacementInfo(
                            task.Id,
                            task.NodeId,
                            node?.Hostname ?? "unknown",
                            node?.Role ?? "unknown",
                            node?.Availability ?? "unknown",
                            task.DesiredState ?? "unknown",
                            task.CurrentState ?? "unknown",
                            task.Error,
                            task.Slot,
                            task.UpdatedAt));
                    }
                }
            }
            else if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
            {
                var containerInfo = await _dockerService.InspectContainerAsync(app.Server, latestDeployment.ContainerId, cancellationToken);
                isRunning = containerInfo?.State?.ToLower() == "running";
                actualState = containerInfo?.State ?? "not-found";
            }

            var status = new ApplicationStatusInfo(
                app.Id,
                latestDeployment.Status.ToString().ToLower(),
                isRunning,
                actualState,
                latestDeployment.ContainerId,
                latestDeployment.ServiceId,
                placements);

            return OperationResult.Success(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting status for application {AppId}", applicationId);
            return OperationResult.Success(new ApplicationStatusInfo(
                app.Id,
                "error",
                false,
                "error: " + ex.Message,
                latestDeployment?.ContainerId,
                latestDeployment?.ServiceId,
                Array.Empty<ServiceReplicaPlacementInfo>()));
        }
    }

    public async Task<OperationResult<ApplicationRuntimeMetrics>> GetMetricsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithServerAndDeploymentsAsync(applicationId, cancellationToken);

        if (app == null)
        {
            return OperationResult.Failure<ApplicationRuntimeMetrics>("Application not found");
        }

        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
        if (latestDeployment == null)
        {
            return OperationResult.Failure<ApplicationRuntimeMetrics>("No deployments found for this application");
        }

        var containerMetrics = new List<ApplicationContainerMetric>();

        try
        {
            if (!string.IsNullOrEmpty(latestDeployment.ServiceId))
            {
                var taskRefs = await _dockerService.ListServiceTaskContainersAsync(app.Server, latestDeployment.ServiceId!, cancellationToken);
                foreach (var task in taskRefs)
                {
                    if (string.IsNullOrWhiteSpace(task.ContainerId))
                    {
                        continue;
                    }

                    var stats = await _dockerService.GetContainerStatsAsync(app.Server, task.ContainerId!, cancellationToken);
                    if (stats == null)
                    {
                        continue;
                    }

                    containerMetrics.Add(new ApplicationContainerMetric(
                        task.ContainerId!,
                        BuildContainerDisplayName(app.Name, task.ContainerId!, task.Slot),
                        task.NodeName,
                        stats.CpuPercent,
                        stats.MemoryUsageBytes,
                        stats.MemoryLimitBytes,
                        stats.MemoryPercent,
                        stats.NetworkRxBytes,
                        stats.NetworkTxBytes,
                        stats.BlockReadBytes,
                        stats.BlockWriteBytes,
                        stats.Timestamp));
                }
            }
            else if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
            {
                var stats = await _dockerService.GetContainerStatsAsync(app.Server, latestDeployment.ContainerId!, cancellationToken);
                if (stats != null)
                {
                    containerMetrics.Add(new ApplicationContainerMetric(
                        latestDeployment.ContainerId!,
                        BuildContainerDisplayName(app.Name, latestDeployment.ContainerId!, null),
                        app.Server.Name,
                        stats.CpuPercent,
                        stats.MemoryUsageBytes,
                        stats.MemoryLimitBytes,
                        stats.MemoryPercent,
                        stats.NetworkRxBytes,
                        stats.NetworkTxBytes,
                        stats.BlockReadBytes,
                        stats.BlockWriteBytes,
                        stats.Timestamp));
                }
            }
            else
            {
                return OperationResult.Failure<ApplicationRuntimeMetrics>("No runtime instances available for this application");
            }

            if (containerMetrics.Count == 0)
            {
                return OperationResult.Failure<ApplicationRuntimeMetrics>("No running containers found for this application");
            }

            var totalCpu = containerMetrics.Sum(c => c.CpuPercent);
            var totalMemoryUsage = containerMetrics.Sum(c => c.MemoryUsageBytes);
            var totalMemoryLimit = containerMetrics.Sum(c => c.MemoryLimitBytes);
            var totalMemoryPercent = totalMemoryLimit > 0
                ? Math.Round((double)totalMemoryUsage / totalMemoryLimit * 100, 2)
                : Math.Round(containerMetrics.Average(c => c.MemoryPercent), 2);

            var metrics = new ApplicationRuntimeMetrics(
                string.IsNullOrEmpty(latestDeployment.ServiceId) ? "container" : "service",
                Math.Round(totalCpu, 2),
                totalMemoryPercent,
                totalMemoryUsage,
                totalMemoryLimit,
                containerMetrics.Sum(c => c.NetworkRxBytes),
                containerMetrics.Sum(c => c.NetworkTxBytes),
                containerMetrics.Sum(c => c.BlockReadBytes),
                containerMetrics.Sum(c => c.BlockWriteBytes),
                containerMetrics,
                DateTime.UtcNow);

            return OperationResult.Success(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting runtime metrics for application {AppId}", applicationId);
            return OperationResult.Failure<ApplicationRuntimeMetrics>(ex.Message);
        }
    }

    private static string BuildContainerDisplayName(string appName, string containerId, int? slot)
    {
        var shortId = containerId.Length > 12 ? containerId[..12] : containerId;
        return slot.HasValue ? $"{appName}-r{slot.Value} ({shortId})" : $"{appName} ({shortId})";
    }

    public async Task<OperationResult<OrphanedResourcesInfo>> GetOrphanedResourcesAsync(int? serverId, CancellationToken cancellationToken = default)
    {
        try
        {
            IEnumerable<Server> servers;

            if (serverId.HasValue)
            {
                var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId.Value, cancellationToken);
                if (server == null)
                    return OperationResult.Failure<OrphanedResourcesInfo>("Server not found");

                servers = new[] { server };
            }
            else
            {
                servers = await _serverRepository.GetAllAsync(cancellationToken);
            }

            var orphanedContainers = new List<OrphanedContainerInfo>();
            var orphanedServices = new List<OrphanedServiceInfo>();

            foreach (var server in servers)
            {
                try
                {
                    if (server.Type == ServerType.SwarmWorker)
                    {
                        _logger.LogDebug("Skipping worker node {ServerName} for orphan check", server.Name);
                        continue;
                    }

                    var containers = await _dockerService.ListContainersAsync(server, true, cancellationToken);
                    foreach (var container in containers)
                    {
                        try
                        {
                            var inspect = await _dockerService.InspectContainerAsync(server, container.Id, cancellationToken);
                            if (inspect != null)
                            {
                                var isManaged = inspect.Labels.TryGetValue("hostcraft.managed", out var managed) && managed == "true";

                                if (isManaged)
                                {
                                    inspect.Labels.TryGetValue("hostcraft.application.id", out var appIdStr);
                                    if (int.TryParse(appIdStr, out var appId))
                                    {
                                        var appExists = await _applicationRepository.ExistsAsync(appId, cancellationToken);
                                        if (!appExists)
                                        {
                                            orphanedContainers.Add(new OrphanedContainerInfo
                                            {
                                                ContainerId = container.Id,
                                                ContainerName = container.Name,
                                                Image = container.Image,
                                                State = container.State,
                                                ServerId = server.Id,
                                                ServerName = server.Name,
                                                ApplicationId = appId,
                                                Labels = inspect.Labels
                                            });
                                        }
                                    }
                                }
                            }
                        }
                        catch (DockerContainerNotFoundException)
                        {
                            _logger.LogDebug("Container {ContainerId} was removed during orphan check, skipping", container.Id);
                        }
                    }

                    if (server.Type == ServerType.SwarmManager)
                    {
                        var services = await _dockerService.ListServicesAsync(server, cancellationToken);
                        foreach (var service in services)
                        {
                            var inspect = await _dockerService.InspectServiceAsync(server, service.Id, cancellationToken);
                            if (inspect != null)
                            {
                                var isManaged = inspect.Labels.TryGetValue("hostcraft.managed", out var managed) && managed == "true";

                                if (isManaged)
                                {
                                    inspect.Labels.TryGetValue("hostcraft.application.id", out var appIdStr);
                                    if (int.TryParse(appIdStr, out var appId))
                                    {
                                        var appExists = await _applicationRepository.ExistsAsync(appId, cancellationToken);
                                        if (!appExists)
                                        {
                                            orphanedServices.Add(new OrphanedServiceInfo
                                            {
                                                ServiceId = service.Id,
                                                ServiceName = service.Name,
                                                Image = service.Image,
                                                Replicas = service.Replicas,
                                                ServerId = server.Id,
                                                ServerName = server.Name,
                                                ApplicationId = appId,
                                                Labels = inspect.Labels
                                            });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking orphans on server {ServerId}", server.Id);
                }
            }

            return OperationResult.Success(new OrphanedResourcesInfo
            {
                Containers = orphanedContainers,
                Services = orphanedServices
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting orphaned resources");
            return OperationResult.Failure<OrphanedResourcesInfo>(ex.Message);
        }
    }

    public async Task<OperationResult<bool>> CleanupOrphanedContainerAsync(string containerId, int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
            return OperationResult.Failure<bool>("Server not found");

        try
        {
            await _dockerService.RemoveContainerAsync(server, containerId, cancellationToken);
            return OperationResult.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing orphaned container {ContainerId}", containerId);
            return OperationResult.Failure<bool>(ex.Message);
        }
    }

    public async Task<OperationResult<bool>> CleanupOrphanedServiceAsync(string serviceId, int serverId, CancellationToken cancellationToken = default)
    {
        var server = await _serverRepository.GetByIdWithPrivateKeyAsync(serverId, cancellationToken);

        if (server == null)
            return OperationResult.Failure<bool>("Server not found");

        try
        {
            await _dockerService.RemoveServiceAsync(server, serviceId, cancellationToken);
            return OperationResult.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing orphaned service {ServiceId}", serviceId);
            return OperationResult.Failure<bool>(ex.Message);
        }
    }
}
