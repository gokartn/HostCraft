using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using HostCraft.Core.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Orchestrates application deployments including Git cloning, Docker builds, and service deployment.
/// Extracted from ApplicationsController to follow single responsibility principle.
/// </summary>
public class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeploymentOrchestrator> _logger;

    public DeploymentOrchestrator(
        IServiceScopeFactory scopeFactory,
        ILogger<DeploymentOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task DeployAsync(int deploymentId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var deploymentRepository = scope.ServiceProvider.GetRequiredService<IDeploymentRepository>();
        var applicationRepository = scope.ServiceProvider.GetRequiredService<IApplicationRepository>();
        var gitProviderRepository = scope.ServiceProvider.GetRequiredService<IGitProviderRepository>();
        var dockerService = scope.ServiceProvider.GetRequiredService<IDockerService>();
        var proxyService = scope.ServiceProvider.GetRequiredService<IProxyService>();
        var logService = scope.ServiceProvider.GetRequiredService<IDeploymentLogService>();

        var deployment = await deploymentRepository.GetByIdWithApplicationDetailsAsync(deploymentId, cancellationToken);

        if (deployment == null)
        {
            _logger.LogError("Deployment {DeploymentId} not found", deploymentId);
            return;
        }

        try
        {
            var app = deployment.Application;

            // Log deployment start
            await logService.AddLogAsync(deploymentId, $"Starting deployment for {app.Name}", "Info");
            await logService.AddLogAsync(deploymentId, $"Target server: {app.Server.Name} ({app.Server.Host})", "Info");
            _logger.LogInformation("Starting deployment {DeploymentId} for application {AppName} on server {ServerName}",
                deploymentId, app.Name, app.Server.Name);

            // Docker Swarm Best Practice: Services can only be created on manager nodes
            if (app.Server.IsSwarm && app.Server.Type == ServerType.SwarmWorker)
            {
                deployment.Status = DeploymentStatus.Failed;
                deployment.ErrorMessage = "Cannot deploy to worker nodes. Applications must be deployed to manager nodes.";
                deployment.FinishedAt = DateTime.UtcNow;
                await deploymentRepository.UpdateAsync(deployment, cancellationToken);
                await logService.AddLogAsync(deploymentId, "ERROR: Cannot deploy to worker node. Use a manager node instead.", "Error");
                _logger.LogError("Attempted to deploy to worker node {ServerName}. Worker nodes cannot accept service deployments.", app.Server.Name);
                return;
            }

            deployment.Status = DeploymentStatus.Running;
            await deploymentRepository.UpdateAsync(deployment, cancellationToken);

            var containerName = app.ServiceName;
            var registryAuth = GetRegistryAuth(app);

            // ── Safe deployment: build/pull image FIRST, keep old running ──
            // The previous deployment stays alive until the new one is confirmed working.
            await logService.AddLogAsync(deploymentId, "Safe deployment: keeping current version running during build...", "Info");

            // Determine and prepare the Docker image (clone + build or pull)
            string imageToUse = await PrepareImageAsync(app, deployment, containerName, dockerService, logService, scope, registryAuth, deploymentId, cancellationToken);

            // Prepare environment and labels
            var envVars = await GetEnvironmentVariablesAsync(app.Id, scope, cancellationToken);
            var labels = BuildApplicationLabels(app, deployment);
            var traefikLabels = HostCraft.Infrastructure.Proxy.TraefikLabelBuilder.BuildLabels(app, "hostcraft_hostcraft-network");

            foreach (var label in traefikLabels)
            {
                labels[label.Key] = label.Value;
            }

            await logService.AddLogAsync(deploymentId, $"Generated {traefikLabels.Count} Traefik labels", "Info");

            // Configure networks
            var networks = new List<string>();

            // Add Traefik network if domains are configured
            if (traefikLabels.Count > 0 && !networks.Contains("hostcraft_hostcraft-network"))
            {
                networks.Add("hostcraft_hostcraft-network");
                await logService.AddLogAsync(deploymentId, "Adding hostcraft_hostcraft-network network for domain routing", "Info");
            }

            // Ensure project-specific network exists and add it
            var projectNetworkName = $"{HostCraft.Infrastructure.Docker.DockerNameHelper.NormalizeNetworkName(app.Project.Name)}-network";
            await dockerService.EnsureNetworkExistsAsync(app.Server, projectNetworkName, cancellationToken);

            if (!networks.Contains(projectNetworkName))
            {
                networks.Add(projectNetworkName);
                await logService.AddLogAsync(deploymentId, $"Adding project network: {projectNetworkName}", "Info");
            }

            // ── Image is ready. Now deploy the new version. ──
            // For Swarm services: update-in-place handles rolling updates natively.
            // For standalone containers: deploy with a staging name, verify, then swap.

            if (app.Server.IsSwarm)
            {
                await DeploySwarmServiceAsync(app, deployment, imageToUse, containerName, envVars, labels, networks, traefikLabels.Count, registryAuth, dockerService, logService, deploymentId, cancellationToken);
            }
            else
            {
                // Safe standalone deployment: use a staging container name
                var stagingName = $"{containerName}-staging-{deployment.Id}";
                await logService.AddLogAsync(deploymentId, $"Deploying new version as staging container: {stagingName}", "Info");

                await DeployStandaloneContainerAsync(app, deployment, imageToUse, stagingName, envVars, labels, networks, dockerService, logService, deploymentId, cancellationToken);

                // Verify staging container is running
                await logService.AddLogAsync(deploymentId, "Verifying new container is running...", "Info");
                await Task.Delay(2000, cancellationToken); // Brief stabilisation window

                var allContainers = await dockerService.ListContainersAsync(app.Server, false);
                var stagingContainer = allContainers.FirstOrDefault(c =>
                    c.Name.TrimStart('/').Equals(stagingName, StringComparison.OrdinalIgnoreCase));

                if (stagingContainer == null || !string.Equals(stagingContainer.State, "running", StringComparison.OrdinalIgnoreCase))
                {
                    // New container failed to start - clean it up and abort
                    await logService.AddLogAsync(deploymentId, "ERROR: New container failed to start. Rolling back - keeping current version running.", "Error");
                    try
                    {
                        if (stagingContainer != null)
                        {
                            await dockerService.StopContainerAsync(app.Server, stagingContainer.Id);
                            await dockerService.RemoveContainerAsync(app.Server, stagingContainer.Id);
                        }
                        // Also try by deployment's container ID in case listing didn't find it
                        else if (!string.IsNullOrEmpty(deployment.ContainerId))
                        {
                            try { await dockerService.StopContainerAsync(app.Server, deployment.ContainerId); } catch { }
                            try { await dockerService.RemoveContainerAsync(app.Server, deployment.ContainerId); } catch { }
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(cleanupEx, "Failed to clean up failed staging container");
                    }

                    throw new InvalidOperationException("New container failed to start. Previous version is still running.");
                }

                await logService.AddLogAsync(deploymentId, "New container verified running. Removing old version...", "Success");

                // Now safe to remove old deployment resources
                await CleanupPreviousDeploymentAsync(app, deployment, containerName, dockerService, logService, deploymentId, cancellationToken);

                // Rename staging container to the real name by stopping staging, removing any
                // orphan with the target name, and recreating with the correct name.
                // Docker doesn't support rename over API in all backends, so we stop staging
                // and redeploy with the final name using the same image.
                await logService.AddLogAsync(deploymentId, "Promoting staging container to production...", "Info");
                try
                {
                    await dockerService.StopContainerAsync(app.Server, stagingContainer.Id);
                    await dockerService.RemoveContainerAsync(app.Server, stagingContainer.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove staging container during promotion");
                }

                // Deploy final container with the correct name
                await DeployStandaloneContainerAsync(app, deployment, imageToUse, containerName, envVars, labels, networks, dockerService, logService, deploymentId, cancellationToken);
            }

            deployment.Status = DeploymentStatus.Success;
            deployment.FinishedAt = DateTime.UtcNow;
            app.LastDeployedAt = DateTime.UtcNow;
            await deploymentRepository.UpdateAsync(deployment, cancellationToken);
            await applicationRepository.UpdateAsync(app, cancellationToken);

            // Configure reverse proxy if enabled
            if (app.Server?.ProxyType != null && app.Server.ProxyType != ProxyType.None)
            {
                await logService.AddLogAsync(deploymentId, $"Configuring {app.Server.ProxyType} reverse proxy...", "Info");
                _logger.LogInformation("Configuring {ProxyType} for application {AppName}", app.Server.ProxyType, app.Name);
                await proxyService.ConfigureApplicationAsync(app);
                await logService.AddLogAsync(deploymentId, "Reverse proxy configured", "Success");
            }

            await logService.AddLogAsync(deploymentId, "Deployment completed successfully!", "Success");
            _logger.LogInformation("Application {AppName} deployed successfully", app.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying application deployment {DeploymentId}", deploymentId);
            await logService.AddLogAsync(deploymentId, $"DEPLOYMENT FAILED: {ex.Message}", "Error");

            deployment.Status = DeploymentStatus.Failed;
            deployment.ErrorMessage = ex.Message;
            deployment.FinishedAt = DateTime.UtcNow;
            await deploymentRepository.UpdateAsync(deployment, cancellationToken);
        }
    }

    private async Task CleanupPreviousDeploymentAsync(
        Application app,
        Deployment currentDeployment,
        string containerName,
        IDockerService dockerService,
        IDeploymentLogService logService,
        int deploymentId,
        CancellationToken cancellationToken)
    {
        var previousDeployment = app.Deployments
            .Where(d => d.Id != currentDeployment.Id)
            .OrderByDescending(d => d.StartedAt)
            .FirstOrDefault();

        if (previousDeployment != null)
        {
            await logService.AddLogAsync(deploymentId, "Cleaning up previous deployment resources...", "Info");

            if (!string.IsNullOrEmpty(previousDeployment.ServiceId))
            {
                try
                {
                    await logService.AddLogAsync(deploymentId, $"Removing old service {previousDeployment.ServiceId[..Math.Min(12, previousDeployment.ServiceId.Length)]}...", "Info");
                    await dockerService.RemoveServiceAsync(app.Server, previousDeployment.ServiceId);
                    await logService.AddLogAsync(deploymentId, "Old service removed", "Success");
                }
                catch (Exception ex)
                {
                    await logService.AddLogAsync(deploymentId, $"Warning: Failed to remove old service: {ex.Message}", "Warning");
                    _logger.LogWarning(ex, "Failed to remove old service {ServiceId}", previousDeployment.ServiceId);
                }
            }

            if (!string.IsNullOrEmpty(previousDeployment.ContainerId))
            {
                try
                {
                    await logService.AddLogAsync(deploymentId, $"Stopping old container {previousDeployment.ContainerId[..Math.Min(12, previousDeployment.ContainerId.Length)]}...", "Info");
                    await dockerService.StopContainerAsync(app.Server, previousDeployment.ContainerId);
                    await dockerService.RemoveContainerAsync(app.Server, previousDeployment.ContainerId);
                    await logService.AddLogAsync(deploymentId, "Old container removed", "Success");
                }
                catch (Exception ex)
                {
                    await logService.AddLogAsync(deploymentId, $"Warning: Failed to remove old container: {ex.Message}", "Warning");
                    _logger.LogWarning(ex, "Failed to remove old container {ContainerId}", previousDeployment.ContainerId);
                }
            }
        }

        // Remove orphaned containers by name
        try
        {
            var existingContainers = await dockerService.ListContainersAsync(app.Server, true);
            var existingContainer = existingContainers.FirstOrDefault(c =>
                c.Name.TrimStart('/').Equals(containerName, StringComparison.OrdinalIgnoreCase));

            if (existingContainer != null)
            {
                _logger.LogInformation("Found existing container with name {ContainerName}, removing it", containerName);
                try
                {
                    await dockerService.StopContainerAsync(app.Server, existingContainer.Id);
                }
                catch { /* May already be stopped */ }
                await dockerService.RemoveContainerAsync(app.Server, existingContainer.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for existing containers");
        }
    }

    private async Task<string> PrepareImageAsync(
        Application app,
        Deployment deployment,
        string containerName,
        IDockerService dockerService,
        IDeploymentLogService logService,
        IServiceScope scope,
        RegistryAuthConfig? registryAuth,
        int deploymentId,
        CancellationToken cancellationToken)
    {
        if (app.SourceType == ApplicationSourceType.Git)
        {
            return await BuildFromGitAsync(app, deployment, containerName, dockerService, logService, scope, registryAuth, deploymentId, cancellationToken);
        }
        else
        {
            return await PullDockerImageAsync(app, dockerService, logService, registryAuth, deploymentId, cancellationToken);
        }
    }

    private async Task<string> BuildFromGitAsync(
        Application app,
        Deployment deployment,
        string containerName,
        IDockerService dockerService,
        IDeploymentLogService logService,
        IServiceScope scope,
        RegistryAuthConfig? registryAuth,
        int deploymentId,
        CancellationToken cancellationToken)
    {
        await logService.AddLogAsync(deploymentId, $"Git deployment: {app.GitRepository}", "Info");
        await logService.AddLogAsync(deploymentId, $"Branch: {app.GitBranch}", "Info");

        var gitService = scope.ServiceProvider.GetRequiredService<IGitService>();

        // Load Git provider if not loaded
        if (app.GitProvider == null && app.GitProviderId.HasValue)
        {
            app.GitProvider = await scope.ServiceProvider.GetRequiredService<IGitProviderRepository>()
                .GetByIdAsync(app.GitProviderId.Value, cancellationToken);
        }

        if (app.GitProvider == null)
        {
            await logService.AddLogAsync(deploymentId, "ERROR: Git provider not found", "Error");
            throw new InvalidOperationException($"Git provider {app.GitProviderId} not found");
        }

        // Clone repository
        string clonePath;
        try
        {
            await logService.AddLogAsync(deploymentId, "Cloning repository...", "Info");
            clonePath = await gitService.CloneApplicationRepositoryAsync(app);
            await logService.AddLogAsync(deploymentId, "Repository cloned successfully", "Success");
        }
        catch (Exception ex)
        {
            await logService.AddLogAsync(deploymentId, $"ERROR: Failed to clone repository: {ex.Message}", "Error");
            _logger.LogError(ex, "Failed to clone repository");
            throw new InvalidOperationException($"Failed to clone repository: {ex.Message}", ex);
        }

        // Build Docker image
        var imageName = $"{containerName}:{deployment.Id}";
        var dockerfilePath = app.Dockerfile ?? "Dockerfile";
        var buildContext = app.BuildContext ?? ".";
        var fullBuildContext = buildContext == "." ? clonePath : Path.Combine(clonePath, buildContext.TrimStart('.', '/', '\\'));

        // Parse build args from comma-separated KEY=VALUE format
        Dictionary<string, string>? buildArgs = null;
        if (!string.IsNullOrWhiteSpace(app.BuildArgs))
        {
            buildArgs = new Dictionary<string, string>();
            foreach (var arg in app.BuildArgs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eqIndex = arg.IndexOf('=');
                if (eqIndex > 0)
                {
                    buildArgs[arg[..eqIndex]] = arg[(eqIndex + 1)..];
                }
            }
        }

        var buildTarget = string.IsNullOrWhiteSpace(app.DockerBuildTarget) ? null : app.DockerBuildTarget;

        await logService.AddLogAsync(deploymentId, $"Building Docker image: {imageName}", "Info");
        await logService.AddLogAsync(deploymentId, $"Dockerfile: {dockerfilePath}", "Info");
        if (buildTarget != null)
            await logService.AddLogAsync(deploymentId, $"Build target: {buildTarget}", "Info");
        if (buildArgs?.Count > 0)
            await logService.AddLogAsync(deploymentId, $"Build args: {string.Join(", ", buildArgs.Keys)}", "Info");

        var buildRequest = new BuildImageRequest(dockerfilePath, fullBuildContext, imageName, buildArgs, buildTarget);
        var buildProgress = new Progress<string>(async msg =>
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                var level = msg.Contains("error", StringComparison.OrdinalIgnoreCase) || msg.Contains("failed", StringComparison.OrdinalIgnoreCase) ? "Error"
                    : msg.Contains("warning", StringComparison.OrdinalIgnoreCase) ? "Warning"
                    : "Info";
                await logService.AddLogAsync(deploymentId, msg, level);
            }
        });

        await dockerService.BuildImageAsync(app.Server, buildRequest, buildProgress);
        await logService.AddLogAsync(deploymentId, $"Docker image built successfully: {imageName}", "Success");

        var finalImageName = imageName;

        if (registryAuth != null)
        {
            await logService.AddLogAsync(deploymentId, "Pushing image to private registry...", "Info");
            var buildService = scope.ServiceProvider.GetRequiredService<IBuildService>();
            var pushResult = await buildService.PushImageAsync(imageName, registryAuth.ServerAddress!, registryAuth.Username, registryAuth.Password);

            if (!pushResult)
            {
                await logService.AddLogAsync(deploymentId, "ERROR: Failed to push image to private registry", "Error");
                throw new InvalidOperationException("Failed to push image to private registry");
            }

            finalImageName = $"{registryAuth.ServerAddress}/{imageName}";
            await logService.AddLogAsync(deploymentId, $"Image pushed to private registry: {finalImageName}", "Success");
        }

        // Cleanup clone directory
        try
        {
            await logService.AddLogAsync(deploymentId, "Cleaning up repository...", "Info");
            await gitService.CleanupRepositoryAsync(clonePath);
        }
        catch (Exception ex)
        {
            await logService.AddLogAsync(deploymentId, $"Warning: Failed to clean up clone directory: {ex.Message}", "Warning");
            _logger.LogWarning(ex, "Failed to clean up clone directory {ClonePath}", clonePath);
        }

        return finalImageName;
    }

    private async Task<string> PullDockerImageAsync(
        Application app,
        IDockerService dockerService,
        IDeploymentLogService logService,
        RegistryAuthConfig? registryAuth,
        int deploymentId,
        CancellationToken cancellationToken)
    {
        var imageToUse = app.DockerImage!;
        await logService.AddLogAsync(deploymentId, $"Pulling Docker image: {imageToUse}", "Info");

        var pullProgress = new Progress<string>(async msg =>
        {
            if (!string.IsNullOrWhiteSpace(msg))
            {
                await logService.AddLogAsync(deploymentId, msg, "Info");
            }
        });

        await dockerService.PullImageAsync(app.Server, imageToUse, pullProgress, registryAuth);
        await logService.AddLogAsync(deploymentId, $"Image pulled successfully: {imageToUse}", "Success");

        return imageToUse;
    }

    private async Task DeploySwarmServiceAsync(
        Application app,
        Deployment deployment,
        string imageToUse,
        string containerName,
        Dictionary<string, string> envVars,
        Dictionary<string, string> labels,
        List<string> networks,
        int traefikLabelCount,
        RegistryAuthConfig? registryAuth,
        IDockerService dockerService,
        IDeploymentLogService logService,
        int deploymentId,
        CancellationToken cancellationToken)
    {
        await logService.AddLogAsync(deploymentId, "Deploying as Docker Swarm service...", "Info");

        var existingServices = await dockerService.ListServicesAsync(app.Server);
        var existingService = existingServices.FirstOrDefault(s =>
            s.Name.Equals(containerName, StringComparison.OrdinalIgnoreCase));

        if (existingService != null)
        {
            await logService.AddLogAsync(deploymentId, $"Updating existing service: {containerName}", "Info");
            var updateRequest = new UpdateServiceRequest(imageToUse, app.Replicas, envVars, labels, networks, registryAuth);
            await dockerService.UpdateServiceAsync(app.Server, existingService.Id, updateRequest);
            deployment.ServiceId = existingService.Id;
            await logService.AddLogAsync(deploymentId, $"Service updated: {existingService.Id}", "Success");
        }
        else
        {
            await logService.AddLogAsync(deploymentId, $"Creating new service: {containerName}", "Info");
            await logService.AddLogAsync(deploymentId, $"Replicas: {app.Replicas}", "Info");

            List<ServicePortMapping>? servicePortMappings = null;
            int? legacyPort = null;

            if (traefikLabelCount == 0)
            {
                if (!string.IsNullOrEmpty(app.PortMappings))
                {
                    try
                    {
                        var mappings = System.Text.Json.JsonSerializer.Deserialize<List<PortMapping>>(app.PortMappings);
                        if (mappings != null && mappings.Count > 0)
                        {
                            servicePortMappings = mappings.Select(m => new ServicePortMapping(m.HostPort, m.ContainerPort, m.Protocol)).ToList();
                            foreach (var mapping in mappings)
                            {
                                await logService.AddLogAsync(deploymentId, $"Port mapping: {mapping.HostPort}:{mapping.ContainerPort}/{mapping.Protocol}", "Info");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse port mappings");
                    }
                }

                if (servicePortMappings == null && app.Port.HasValue)
                {
                    legacyPort = app.PublishedPort ?? app.Port.Value;
                    await logService.AddLogAsync(deploymentId, $"Port: {legacyPort}", "Info");
                }
            }
            else
            {
                await logService.AddLogAsync(deploymentId, "Traefik routing configured - skipping port publishing", "Info");
            }

            var serviceRequest = new CreateServiceRequest(containerName, imageToUse, app.Replicas, envVars, labels, networks, legacyPort, servicePortMappings, null, null, null, registryAuth);
            var serviceId = await dockerService.CreateServiceAsync(app.Server, serviceRequest);
            deployment.ServiceId = serviceId;
            await logService.AddLogAsync(deploymentId, $"Service created: {serviceId}", "Success");
        }
    }

    private async Task DeployStandaloneContainerAsync(
        Application app,
        Deployment deployment,
        string imageToUse,
        string containerName,
        Dictionary<string, string> envVars,
        Dictionary<string, string> labels,
        List<string> networks,
        IDockerService dockerService,
        IDeploymentLogService logService,
        int deploymentId,
        CancellationToken cancellationToken)
    {
        await logService.AddLogAsync(deploymentId, "Deploying as standalone container...", "Info");

        Dictionary<int, int>? portBindings = null;

        if (!string.IsNullOrEmpty(app.PortMappings))
        {
            try
            {
                var mappings = System.Text.Json.JsonSerializer.Deserialize<List<PortMapping>>(app.PortMappings);
                if (mappings != null && mappings.Count > 0)
                {
                    portBindings = mappings.ToDictionary(m => m.ContainerPort, m => m.HostPort);
                    foreach (var mapping in mappings)
                    {
                        await logService.AddLogAsync(deploymentId, $"Port mapping: {mapping.HostPort}:{mapping.ContainerPort}/{mapping.Protocol}", "Info");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse port mappings");
            }
        }

        if (portBindings == null && app.Port.HasValue)
        {
            var hostPort = app.PublishedPort ?? app.Port.Value;
            portBindings = new Dictionary<int, int> { { app.Port.Value, hostPort } };
            await logService.AddLogAsync(deploymentId, $"Port mapping: {hostPort}:{app.Port.Value}/tcp", "Info");
        }

        var containerRequest = new CreateContainerRequest(containerName, imageToUse, envVars, labels, networks, portBindings);

        await logService.AddLogAsync(deploymentId, $"Creating container: {containerName}", "Info");
        var containerId = await dockerService.CreateContainerAsync(app.Server, containerRequest);
        await logService.AddLogAsync(deploymentId, $"Starting container: {containerId[..Math.Min(12, containerId.Length)]}", "Info");
        await dockerService.StartContainerAsync(app.Server, containerId);
        deployment.ContainerId = containerId;
        await logService.AddLogAsync(deploymentId, "Container started successfully", "Success");
    }

    private static Dictionary<string, string> BuildApplicationLabels(Application app, Deployment deployment)
    {
        return new Dictionary<string, string>
        {
            { "hostcraft.managed", "true" },
            { "hostcraft.application.id", app.Id.ToString() },
            { "hostcraft.application.uuid", app.Uuid.ToString() },
            { "hostcraft.application.name", app.Name },
            { "hostcraft.project.id", app.ProjectId.ToString() },
            { "hostcraft.deployment.id", deployment.Id.ToString() },
            { "hostcraft.server.id", app.ServerId.ToString() }
        };
    }

    private static RegistryAuthConfig? GetRegistryAuth(Application app)
    {
        if (!app.UsePrivateRegistry || string.IsNullOrWhiteSpace(app.RegistryServer))
        {
            return null;
        }

        return new RegistryAuthConfig(app.RegistryServer, app.RegistryUsername, app.RegistryPassword);
    }

    private static async Task<Dictionary<string, string>> GetEnvironmentVariablesAsync(
        int applicationId,
        IServiceScope scope,
        CancellationToken cancellationToken)
    {
        var secretManager = scope.ServiceProvider.GetRequiredService<ISecretManager>();
        var environmentVariables = await secretManager.GetEnvironmentVariablesAsync(applicationId);
        return environmentVariables.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
    }
}
