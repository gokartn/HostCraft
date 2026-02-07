using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Infrastructure.Proxy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Docker;

/// <summary>
/// Orchestrates application deployments with real-time log streaming.
/// Routes to SwarmDeploymentService for swarm servers, standalone for others.
/// </summary>
public class DeploymentService : IDeploymentService
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ISwarmDeploymentService _swarmDeploymentService;
    private readonly IGitService _gitService;
    private readonly IBuildService _buildService;
    private readonly IStackService _stackService;
    private readonly IComposeParser _composeParser;
    private readonly IDeploymentLogService _logService;
    private readonly ILogger<DeploymentService> _logger;

    public DeploymentService(
        HostCraftDbContext context,
        IDockerService dockerService,
        ISwarmDeploymentService swarmDeploymentService,
        IGitService gitService,
        IBuildService buildService,
        IStackService stackService,
        IComposeParser composeParser,
        IDeploymentLogService logService,
        ILogger<DeploymentService> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _swarmDeploymentService = swarmDeploymentService;
        _gitService = gitService;
        _buildService = buildService;
        _stackService = stackService;
        _composeParser = composeParser;
        _logService = logService;
        _logger = logger;
    }

    public async Task<Deployment> DeployApplicationAsync(
        int applicationId,
        string? commitHash = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .Include(a => a.Project)
            .Include(a => a.EnvironmentVariables)
            .Include(a => a.ComposeVariables)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }

        // Create deployment record
        var deployment = new Deployment
        {
            ApplicationId = applicationId,
            CommitSha = commitHash ?? application.LastCommitSha,
            Status = DeploymentStatus.Queued,
            TriggeredBy = "Manual",
            CreatedAt = DateTime.UtcNow
        };

        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync(cancellationToken);

        var deploymentId = deployment.Id;

        try
        {
            // Log deployment start
            await _logService.AddLogAsync(deploymentId, $"Starting deployment for {application.Name}", "Info");
            progress?.Report($"Starting deployment for {application.Name}");

            deployment.Status = DeploymentStatus.Running;
            deployment.StartedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            // Handle Docker Compose deployments separately
            if (application.SourceType == ApplicationSourceType.DockerCompose)
            {
                await DeployComposeStackAsync(application, deploymentId, progress, cancellationToken);

                deployment.Status = DeploymentStatus.Success;
                deployment.FinishedAt = DateTime.UtcNow;
                application.LastDeployedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                await _logService.AddLogAsync(deploymentId, "Docker Compose stack deployed successfully", "Success");
                progress?.Report("Docker Compose stack deployed successfully");

                return deployment;
            }

            string imageTag;

            // Build or pull image based on source type
            if (application.SourceType == ApplicationSourceType.Git)
            {
                // Clone repository
                await _logService.AddLogAsync(deploymentId, "Cloning Git repository...", "Info");
                progress?.Report("Cloning repository...");

                var repoPath = await _gitService.CloneApplicationRepositoryAsync(
                    application,
                    commitHash);

                await _logService.AddLogAsync(deploymentId, $"Repository cloned to {repoPath}", "Success");

                // Build Docker image (BuildService will add its own detailed logs)
                await _logService.AddLogAsync(deploymentId, "Building Docker image...", "Info");
                progress?.Report("Building Docker image...");

                imageTag = await _buildService.BuildImageAsync(
                    application,
                    repoPath,
                    commitHash);

                // Cleanup repository
                await _logService.AddLogAsync(deploymentId, "Cleaning up repository...", "Info");
                progress?.Report("Cleaning up repository...");
                await _gitService.CleanupRepositoryAsync(repoPath);
                await _logService.AddLogAsync(deploymentId, "Repository cleaned up", "Success");
            }
            else if (application.SourceType == ApplicationSourceType.Dockerfile)
            {
                // Build from Dockerfile in configured path
                await _logService.AddLogAsync(deploymentId, "Building from Dockerfile...", "Info");
                progress?.Report("Building from Dockerfile...");

                var sourcePath = application.BuildContext ?? ".";
                imageTag = await _buildService.BuildImageAsync(
                    application,
                    sourcePath,
                    commitHash);
            }
            else if (application.SourceType == ApplicationSourceType.DockerImage || application.SourceType == ApplicationSourceType.DatabaseTemplate)
            {
                if (string.IsNullOrEmpty(application.DockerImage))
                {
                    await _logService.AddLogAsync(deploymentId, "Docker image not specified", "Error");
                    throw new InvalidOperationException("Docker image not specified");
                }

                await _logService.AddLogAsync(deploymentId, $"Pulling image {application.DockerImage}...", "Info");
                progress?.Report($"Pulling image {application.DockerImage}...");

                // Create a progress reporter that logs to both places
                var pullProgress = new Progress<string>(async msg =>
                {
                    await _logService.AddLogAsync(deploymentId, msg, "Info");
                    progress?.Report(msg);
                });

                var registryAuth = application.UsePrivateRegistry && !string.IsNullOrWhiteSpace(application.RegistryServer)
                    ? new RegistryAuthConfig(application.RegistryServer, application.RegistryUsername, application.RegistryPassword)
                    : null;

                await _dockerService.PullImageAsync(
                    application.Server,
                    application.DockerImage,
                    pullProgress,
                    registryAuth,
                    cancellationToken);

                imageTag = application.DockerImage;
                await _logService.AddLogAsync(deploymentId, $"Successfully pulled image {imageTag}", "Success");
            }
            else
            {
                await _logService.AddLogAsync(deploymentId, $"Source type {application.SourceType} not yet implemented", "Error");
                throw new NotImplementedException($"Source type {application.SourceType} not yet implemented");
            }

            // Route to appropriate deployment method
            bool success;

            if (application.Server.CanDeployAsService && application.DeployAsService)
            {
                await _logService.AddLogAsync(deploymentId, "Deploying as Docker Swarm service...", "Info");
                progress?.Report("Deploying as Docker Swarm service...");

                var result = await _swarmDeploymentService.DeployToSwarmAsync(
                    application,
                    imageTag,
                    cancellationToken);

                success = result.Success;

                if (!success)
                {
                    await _logService.AddLogAsync(deploymentId, $"Swarm deployment failed: {result.Error}", "Error");
                    throw new Exception(result.Error ?? "Swarm deployment failed");
                }

                await _logService.AddLogAsync(deploymentId, result.Message ?? "Swarm service deployed", "Success");
                progress?.Report(result.Message ?? "Swarm service deployed");
            }
            else
            {
                await _logService.AddLogAsync(deploymentId, "Deploying as standalone container...", "Info");
                progress?.Report("Deploying as standalone container...");

                // Warn about downtime for standalone deployments (not HA/DR compatible)
                await _logService.AddLogAsync(deploymentId, "WARNING: Standalone container deployment will cause downtime during updates (stop-first strategy)", "Warning");
                _logger.LogWarning("Deploying {AppName} as standalone container - this will cause downtime. Consider using Docker Swarm for zero-downtime deployments.", application.Name);

                success = await DeployStandaloneContainerAsync(
                    application,
                    imageTag,
                    deploymentId,
                    progress,
                    cancellationToken);
            }

            if (success)
            {
                deployment.Status = DeploymentStatus.Success;
                deployment.FinishedAt = DateTime.UtcNow;
                application.LastDeployedAt = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(commitHash))
                {
                    application.LastCommitSha = commitHash;
                }

                await _logService.AddLogAsync(deploymentId, "Deployment completed successfully!", "Success");
                progress?.Report("Deployment completed successfully!");
            }
            else
            {
                deployment.Status = DeploymentStatus.Failed;
                deployment.FinishedAt = DateTime.UtcNow;
                await _logService.AddLogAsync(deploymentId, "Deployment failed", "Error");
                progress?.Report("Deployment failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deployment failed for application {ApplicationId}", applicationId);
            deployment.Status = DeploymentStatus.Failed;
            deployment.FinishedAt = DateTime.UtcNow;
            deployment.ErrorMessage = ex.Message;
            await _logService.AddLogAsync(deploymentId, $"Deployment error: {ex.Message}", "Error");
            progress?.Report($"Error: {ex.Message}");
        }

        await _context.SaveChangesAsync(cancellationToken);
        return deployment;
    }

    private async Task<bool> DeployStandaloneContainerAsync(
        Application application,
        string imageTag,
        int deploymentId,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            // Stop existing container if running
            var containers = await _dockerService.ListContainersAsync(
                application.Server,
                showAll: true,
                cancellationToken);

            var existingContainer = containers.FirstOrDefault(c => c.Name.Contains(application.ServiceName));

            if (existingContainer != null)
            {
                await _logService.AddLogAsync(deploymentId, $"Stopping existing container {existingContainer.Id.Substring(0, 12)}...", "Info");
                progress?.Report($"Stopping existing container {existingContainer.Id}...");

                await _dockerService.StopContainerAsync(
                    application.Server,
                    existingContainer.Id,
                    cancellationToken);

                await _logService.AddLogAsync(deploymentId, "Removing old container...", "Info");
                await _dockerService.RemoveContainerAsync(
                    application.Server,
                    existingContainer.Id,
                    cancellationToken);

                await _logService.AddLogAsync(deploymentId, "Old container removed", "Success");
            }

            // Create new container
            await _logService.AddLogAsync(deploymentId, "Creating new container...", "Info");
            progress?.Report("Creating container...");

            var envVars = application.EnvironmentVariables
                .Where(ev => !ev.IsSecret)
                .ToDictionary(ev => ev.Key, ev => ev.Value);

            var labels = new Dictionary<string, string>
            {
                ["hostcraft.app.id"] = application.Id.ToString(),
                ["hostcraft.app.name"] = application.Name,
                ["hostcraft.project.id"] = application.ProjectId.ToString()
            };

            // Add Traefik labels for routing (uses TraefikLabelBuilder for complete configuration)
            var traefikLabels = TraefikLabelBuilder.BuildLabels(application, "hostcraft_hostcraft-network");
            foreach (var label in traefikLabels)
            {
                labels[label.Key] = label.Value;
            }

            if (!string.IsNullOrEmpty(application.Domain))
            {
                await _logService.AddLogAsync(deploymentId, $"Configured domain: {application.Domain}", "Info");
                if (!string.IsNullOrEmpty(application.AdditionalDomains))
                {
                    await _logService.AddLogAsync(deploymentId, $"Additional domains: {application.AdditionalDomains}", "Info");
                }
                await _logService.AddLogAsync(deploymentId, $"HTTPS: {(application.EnableHttps ? "enabled" : "disabled")}, Force HTTPS: {(application.ForceHttps ? "yes" : "no")}", "Info");
            }

            // Build port bindings: map container port to published/host port
            Dictionary<int, int>? portBindings = null;
            if (application.Port.HasValue)
            {
                var hostPort = application.PublishedPort ?? application.Port.Value;
                portBindings = new Dictionary<int, int> { [application.Port.Value] = hostPort };
            }

            var projectNetwork = $"{DockerNameHelper.NormalizeNetworkName(application.Project.Name)}-network";

            await _dockerService.EnsureNetworkExistsAsync(
                application.Server,
                projectNetwork,
                cancellationToken);

            var request = new CreateContainerRequest(
                Name: application.ServiceName,
                Image: imageTag,
                EnvironmentVariables: envVars,
                Labels: labels,
                Networks: new List<string> { projectNetwork },
                PortBindings: portBindings,
                MemoryLimit: application.MemoryLimitBytes,
                CpuLimit: application.CpuLimit);

            var containerId = await _dockerService.CreateContainerAsync(
                application.Server,
                request,
                cancellationToken);

            await _logService.AddLogAsync(deploymentId, $"Container created: {containerId.Substring(0, 12)}", "Success");

            // Connect container to hostcraft_hostcraft-network network for domain routing
            if (!string.IsNullOrEmpty(application.Domain))
            {
                try
                {
                    await _dockerService.ConnectContainerToNetworkAsync(
                        application.Server,
                        containerId,
                        "hostcraft_hostcraft-network",
                        cancellationToken);
                    await _logService.AddLogAsync(deploymentId, "Connected to hostcraft_hostcraft-network network for domain routing", "Info");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect container to hostcraft_hostcraft-network network. Domain routing may not work.");
                    await _logService.AddLogAsync(deploymentId, $"Warning: Could not connect to hostcraft_hostcraft-network network: {ex.Message}", "Warning");
                }
            }

            await _logService.AddLogAsync(deploymentId, "Starting container...", "Info");
            progress?.Report($"Starting container {containerId}...");

            await _dockerService.StartContainerAsync(
                application.Server,
                containerId,
                cancellationToken);

            await _logService.AddLogAsync(deploymentId, "Container started successfully", "Success");
            progress?.Report("Container started successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deploy standalone container for {AppName}", application.Name);
            await _logService.AddLogAsync(deploymentId, $"Container deployment failed: {ex.Message}", "Error");
            progress?.Report($"Container deployment failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopApplicationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            return false;
        }

        if (application.DeployAsService && !string.IsNullOrEmpty(application.SwarmServiceId))
        {
            return await _swarmDeploymentService.RemoveServiceAsync(application, cancellationToken);
        }
        else
        {
            var containers = await _dockerService.ListContainersAsync(
                application.Server,
                showAll: false,
                cancellationToken);

            var container = containers.FirstOrDefault(c => c.Name.Contains(application.Name));

            if (container != null)
            {
                return await _dockerService.StopContainerAsync(
                    application.Server,
                    container.Id,
                    cancellationToken);
            }

            return false;
        }
    }

    public async Task<bool> RestartApplicationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            return false;
        }

        // For swarm services, we can trigger a restart by updating the service
        // For containers, stop and redeploy
        await StopApplicationAsync(applicationId, cancellationToken);
        await Task.Delay(2000, cancellationToken); // Wait for graceful shutdown

        var deployment = await DeployApplicationAsync(applicationId, null, null, cancellationToken);
        return deployment.Status == DeploymentStatus.Success;
    }

    public async Task<bool> ScaleApplicationAsync(int applicationId, int replicas, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            return false;
        }

        if (application.DeployAsService)
        {
            return await _swarmDeploymentService.ScaleServiceAsync(
                application,
                replicas,
                cancellationToken);
        }
        else
        {
            _logger.LogWarning("Scaling is only supported for swarm services");
            return false;
        }
    }

    public async Task<bool> RollbackDeploymentAsync(int deploymentId, CancellationToken cancellationToken = default)
    {
        var deployment = await _context.Deployments
            .Include(d => d.Application)
                .ThenInclude(a => a.Server)
            .FirstOrDefaultAsync(d => d.Id == deploymentId, cancellationToken);

        if (deployment == null)
        {
            return false;
        }

        var application = deployment.Application;

        if (application.DeployAsService)
        {
            return await _swarmDeploymentService.RollbackServiceAsync(
                application,
                cancellationToken);
        }
        else
        {
            // Find previous successful deployment
            var previousDeployment = await _context.Deployments
                .Where(d => d.ApplicationId == application.Id &&
                            d.Id < deploymentId &&
                            d.Status == DeploymentStatus.Success)
                .OrderByDescending(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (previousDeployment != null && !string.IsNullOrEmpty(previousDeployment.CommitSha))
            {
                await DeployApplicationAsync(
                    application.Id,
                    previousDeployment.CommitSha,
                    null,
                    cancellationToken);
                return true;
            }

            return false;
        }
    }

    public async Task<Stream> GetApplicationLogsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications
            .Include(a => a.Server)
            .FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken);

        if (application == null)
        {
            throw new InvalidOperationException($"Application {applicationId} not found");
        }

        if (application.DeployAsService && !string.IsNullOrEmpty(application.SwarmServiceId))
        {
            return await _dockerService.GetServiceLogsAsync(
                application.Server,
                application.SwarmServiceId,
                cancellationToken);
        }
        else
        {
            var containers = await _dockerService.ListContainersAsync(
                application.Server,
                showAll: false,
                cancellationToken);

            var container = containers.FirstOrDefault(c => c.Name.Contains(application.Name));

            if (container != null)
            {
                return await _dockerService.GetContainerLogsAsync(
                    application.Server,
                    container.Id,
                    cancellationToken);
            }

            throw new InvalidOperationException($"No running container found for application {application.Name}");
        }
    }

    /// <summary>
    /// Deploy a Docker Compose stack using StackService
    /// </summary>
    private async Task DeployComposeStackAsync(
        Application application,
        int deploymentId,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(application.DockerComposeFile))
        {
            await _logService.AddLogAsync(deploymentId, "Docker Compose file not configured", "Error");
            throw new InvalidOperationException("Docker Compose file not configured");
        }

        // Validate YAML
        await _logService.AddLogAsync(deploymentId, "Validating Docker Compose file...", "Info");
        progress?.Report("Validating Docker Compose file...");

        var validationResult = await _composeParser.ValidateYamlAsync(application.DockerComposeFile);
        if (!validationResult.IsValid)
        {
            var errors = string.Join(", ", validationResult.Errors);
            await _logService.AddLogAsync(deploymentId, $"Compose validation failed: {errors}", "Error");
            throw new InvalidOperationException($"Docker Compose validation failed: {errors}");
        }

        await _logService.AddLogAsync(deploymentId, "Docker Compose file validated", "Success");

        // Parse compose file
        var parseResult = await _composeParser.ParseComposeAsync(application.DockerComposeFile);
        if (!parseResult.IsValid)
        {
            var errors = string.Join(", ", parseResult.Errors);
            await _logService.AddLogAsync(deploymentId, $"Compose parsing failed: {errors}", "Error");
            throw new InvalidOperationException($"Docker Compose parsing failed: {errors}");
        }

        await _logService.AddLogAsync(deploymentId, $"Parsed {parseResult.ServiceNames.Count} services from compose file", "Info");

        // Substitute environment variables
        var composeWithEnv = application.DockerComposeFile;
        if (application.ComposeVariables.Any())
        {
            await _logService.AddLogAsync(deploymentId, "Substituting environment variables...", "Info");
            progress?.Report("Substituting environment variables...");

            var envVars = application.ComposeVariables.ToDictionary(v => v.Key, v => v.Value);
            composeWithEnv = await _composeParser.SubstituteEnvironmentVariablesAsync(
                application.DockerComposeFile,
                envVars);

            await _logService.AddLogAsync(deploymentId, $"Substituted {envVars.Count} environment variables", "Success");
        }

        // Deploy stack via StackService
        await _logService.AddLogAsync(deploymentId, $"Deploying stack '{application.Name}'...", "Info");
        progress?.Report($"Deploying stack '{application.Name}'...");

        var stackName = $"app-{application.Id}-{application.Name.ToLowerInvariant().Replace(" ", "-")}";

        var deployResult = await _stackService.DeployStackAsync(
            application.Server,
            stackName,
            composeWithEnv,
            cancellationToken);

        if (!deployResult.Success)
        {
            await _logService.AddLogAsync(deploymentId, $"Stack deployment failed: {deployResult.Error}", "Error");
            throw new Exception($"Stack deployment failed: {deployResult.Error}");
        }

        await _logService.AddLogAsync(deploymentId, $"Stack '{stackName}' deployed successfully", "Success");
        progress?.Report($"Stack '{stackName}' deployed successfully");

        // Store stack info
        application.SwarmServiceId = stackName; // Reuse this field to store stack name
    }}