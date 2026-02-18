using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Infrastructure.Proxy;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Service for managing application CRUD operations with validation.
/// Extracted from ApplicationsController to follow single responsibility principle.
/// </summary>
public class ApplicationManagementService : IApplicationManagementService
{
        private readonly IApplicationRepository _applicationRepository;
        private readonly IServerRepository _serverRepository;
        private readonly IProjectRepository _projectRepository;
        private readonly IDomainRepository _domainRepository;
        private readonly IDeploymentRepository _deploymentRepository;
    private readonly IDockerService _dockerService;
    private readonly IDeploymentJobQueue _deploymentJobQueue;
    private readonly IGitProviderService _gitProviderService;
    private readonly ISystemSettingsService _systemSettingsService;
    private readonly ILogger<ApplicationManagementService> _logger;

    public ApplicationManagementService(
            IApplicationRepository applicationRepository,
            IServerRepository serverRepository,
            IProjectRepository projectRepository,
            IDomainRepository domainRepository,
            IDeploymentRepository deploymentRepository,
            IDockerService dockerService,
        IDeploymentJobQueue deploymentJobQueue,
        IGitProviderService gitProviderService,
        ISystemSettingsService systemSettingsService,
        ILogger<ApplicationManagementService> logger)
    {
            _applicationRepository = applicationRepository;
            _serverRepository = serverRepository;
            _projectRepository = projectRepository;
            _domainRepository = domainRepository;
            _deploymentRepository = deploymentRepository;
        _dockerService = dockerService;
        _deploymentJobQueue = deploymentJobQueue;
        _gitProviderService = gitProviderService;
        _systemSettingsService = systemSettingsService;
        _logger = logger;
    }

    public async Task<ApplicationCreationResult> CreateApplicationAsync(ApplicationCreationRequest request, CancellationToken cancellationToken = default)
    {
        // Validate server exists
        var server = await _serverRepository.GetByIdAsync(request.ServerId, cancellationToken);
        if (server == null)
            return new ApplicationCreationResult(false, "Server not found");

        // Docker Swarm Best Practice: Services must be deployed to manager nodes
        if (server.Type == ServerType.SwarmWorker)
            return new ApplicationCreationResult(false, "Cannot deploy applications to worker nodes. Please select a manager node or standalone server.");

        // Validate project exists
        if (await _projectRepository.GetByIdAsync(request.ProjectId, cancellationToken) == null)
            return new ApplicationCreationResult(false, "Project not found");

        // Validate source type specific requirements
        var isGitDeployment = request.SourceType == "Git";
        if (isGitDeployment)
        {
            if (!request.GitProviderId.HasValue)
                return new ApplicationCreationResult(false, "Git provider is required for Git deployments");
            if (string.IsNullOrWhiteSpace(request.GitRepository))
                return new ApplicationCreationResult(false, "Git repository is required for Git deployments");
            if (string.IsNullOrWhiteSpace(request.GitBranch))
                return new ApplicationCreationResult(false, "Git branch is required for Git deployments");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Image))
                return new ApplicationCreationResult(false, "Docker image is required");

            if (request.UsePrivateRegistry)
            {
                if (string.IsNullOrWhiteSpace(request.RegistryServer))
                    return new ApplicationCreationResult(false, "Registry server is required when using a private registry");
                if (string.IsNullOrWhiteSpace(request.RegistryPassword))
                    return new ApplicationCreationResult(false, "Registry password/token is required when using a private registry");
            }
        }

        // Check for duplicate application name on the same server
        var existingApp = await _applicationRepository.GetByServerAndNameAsync(request.ServerId, request.Name, cancellationToken);
        if (existingApp != null)
            return new ApplicationCreationResult(false, $"An application named '{request.Name}' already exists on this server. Please choose a different name.");

        if (!TraefikLabelBuilder.TryParseOverrides(request.TraefikLabelOverrides, out var parsedOverrides, out var overrideError, null))
        {
            return new ApplicationCreationResult(false, "Invalid Traefik label overrides", ErrorDetails: overrideError);
        }

        var normalizedOverrides = parsedOverrides.Count == 0
            ? null
            : JsonSerializer.Serialize(parsedOverrides, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            // Parse GitOwner and GitRepoName from GitRepository (format: "owner/repo")
            string? gitOwner = null;
            string? gitRepoName = null;
            if (!string.IsNullOrWhiteSpace(request.GitRepository))
            {
                var parts = request.GitRepository.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    gitOwner = parts[0];
                    gitRepoName = parts[1];
                }
            }

            // Parse port mappings if provided
            int? port = null;
            int? publishedPort = null;
            if (!string.IsNullOrEmpty(request.PortMappings))
            {
                try
                {
                    var mappings = System.Text.Json.JsonSerializer.Deserialize<List<Core.Models.PortMapping>>(request.PortMappings);
                    if (mappings?.Count > 0)
                    {
                        port = mappings[0].ContainerPort;
                        publishedPort = mappings[0].HostPort;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse port mappings, using fallback");
                }
            }

            port ??= request.Port;
            publishedPort ??= request.Port;

            var app = new Application
            {
                Uuid = Guid.NewGuid(),
                Name = request.Name,
                DockerImage = request.Image,
                UsePrivateRegistry = request.UsePrivateRegistry,
                RegistryServer = string.IsNullOrWhiteSpace(request.RegistryServer) ? null : request.RegistryServer,
                RegistryUsername = string.IsNullOrWhiteSpace(request.RegistryUsername) ? null : request.RegistryUsername,
                RegistryPassword = string.IsNullOrWhiteSpace(request.RegistryPassword) ? null : request.RegistryPassword,
                ServerId = request.ServerId,
                ProjectId = request.ProjectId,
                SourceType = isGitDeployment ? ApplicationSourceType.Git : ApplicationSourceType.DockerImage,
                Replicas = request.Replicas ?? 1,
                Port = port,
                PublishedPort = publishedPort,
                PortMappings = request.PortMappings,
                Domain = request.Domain,
                AdditionalDomains = request.AdditionalDomains,
                TraefikLabelOverrides = normalizedOverrides,
                EnableHttps = request.EnableHttps,
                ForceHttps = request.ForceHttps,
                LetsEncryptEmail = request.LetsEncryptEmail,
                // Git settings
                GitProviderId = request.GitProviderId,
                GitRepository = request.GitRepository,
                GitBranch = request.GitBranch,
                GitOwner = gitOwner,
                GitRepoName = gitRepoName,
                // Docker build settings
                Dockerfile = request.DockerfilePath ?? "Dockerfile",
                BuildContext = request.BuildContext ?? ".",
                DockerBuildTarget = request.DockerBuildTarget,
                BuildArgs = request.BuildArgs,
                // Git clone options
                CloneSubmodules = request.CloneSubmodules,
                EnableGitLfs = request.EnableGitLfs,
                // Auto-deploy settings
                AutoDeployOnPush = request.AutoDeployOnPush,
                // Preview deployment settings
                EnablePreviewDeployments = request.EnablePreviewDeployments,
                PreviewUrlTemplate = request.PreviewUrlTemplate,
                CreatedAt = DateTime.UtcNow
            };

            await _applicationRepository.AddAsync(app, cancellationToken);

            // Create Domain entity if domain was provided
            if (!string.IsNullOrEmpty(request.Domain))
            {
                var domain = new Domain
                {
                    ApplicationId = app.Id,
                    Host = request.Domain,
                    Port = port ?? 80,
                    Path = "/",
                    HttpsEnabled = request.EnableHttps,
                    ForceHttps = request.ForceHttps,
                    IsPrimary = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                await _domainRepository.AddAsync(domain, cancellationToken);
            }

            if (request.EnvironmentVariables != null && request.EnvironmentVariables.Count > 0)
            {
                await _applicationRepository.ReplaceNonSecretEnvironmentVariablesAsync(app, request.EnvironmentVariables, cancellationToken);
            }

            // Create deployment
            var deployment = new Deployment
            {
                Uuid = Guid.NewGuid(),
                ApplicationId = app.Id,
                Status = DeploymentStatus.Queued,
                StartedAt = DateTime.UtcNow
            };

            await _deploymentRepository.AddAsync(deployment, cancellationToken);

            // Queue deployment for background processing with retries and logging
            await _deploymentJobQueue.EnqueueAsync(new HostCraft.Core.Models.DeploymentJob(HostCraft.Core.Models.DeploymentJobType.Deploy, deployment.Id), cancellationToken);

            // Register webhook on GitHub if auto-deploy is enabled for Git deployments
            if (isGitDeployment && request.AutoDeployOnPush && request.GitProviderId.HasValue)
            {
                await RegisterWebhookForApplicationAsync(app, cancellationToken);
            }

            _logger.LogInformation("Created application {AppName} (ID: {AppId}) with deployment {DeploymentId}",
                app.Name, app.Id, deployment.Id);

            return new ApplicationCreationResult(true, "Application created successfully", app.Id, deployment.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating application {AppName}", request.Name);
            return new ApplicationCreationResult(false, "Failed to create application", ErrorDetails: ex.Message);
        }
    }

    public async Task<ApplicationUpdateResult> UpdateApplicationAsync(int applicationId, ApplicationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithAllRelationsAsync(applicationId, cancellationToken);

        if (app == null)
            return new ApplicationUpdateResult(false, "Application not found");

        Dictionary<string, string>? parsedOverridePayload = null;
        if (request.TraefikLabelOverrides != null &&
            !TraefikLabelBuilder.TryParseOverrides(request.TraefikLabelOverrides, out parsedOverridePayload, out var overrideError, null))
        {
            return new ApplicationUpdateResult(false, overrideError ?? "Invalid Traefik label overrides");
        }

        try
        {
            // Track AutoDeployOnPush change for webhook lifecycle
            var previousAutoDeployOnPush = app.AutoDeployOnPush;

            // Update basic fields
            if (request.Name != null)
                app.Name = request.Name;
            if (request.Description != null)
                app.Description = request.Description;
            if (request.Image != null)
                app.DockerImage = request.Image;
            if (request.Port.HasValue)
                app.Port = request.Port.Value;
            if (request.Replicas.HasValue)
                app.Replicas = request.Replicas.Value;
            if (request.UsePrivateRegistry.HasValue)
                app.UsePrivateRegistry = request.UsePrivateRegistry.Value;
            if (request.RegistryServer != null)
                app.RegistryServer = string.IsNullOrWhiteSpace(request.RegistryServer) ? null : request.RegistryServer;
            if (request.RegistryUsername != null)
                app.RegistryUsername = string.IsNullOrWhiteSpace(request.RegistryUsername) ? null : request.RegistryUsername;
            if (request.RegistryPassword != null)
                app.RegistryPassword = string.IsNullOrWhiteSpace(request.RegistryPassword) ? null : request.RegistryPassword;

            if (app.UsePrivateRegistry == false)
            {
                // Clear stored credentials when private registry is disabled
                app.RegistryServer = null;
                app.RegistryUsername = null;
                app.RegistryPassword = null;
            }

            // Update port mappings
            if (request.PortMappings != null)
                app.PortMappings = request.PortMappings;

            // Update domain configuration
            if (request.Domain != null)
                app.Domain = string.IsNullOrWhiteSpace(request.Domain) ? null : request.Domain;
            if (request.AdditionalDomains != null)
                app.AdditionalDomains = string.IsNullOrWhiteSpace(request.AdditionalDomains) ? null : request.AdditionalDomains;
            if (request.TraefikLabelOverrides != null)
            {
                app.TraefikLabelOverrides = parsedOverridePayload == null || parsedOverridePayload.Count == 0
                    ? null
                    : JsonSerializer.Serialize(parsedOverridePayload, new JsonSerializerOptions { WriteIndented = true });
            }
            if (request.EnableHttps.HasValue)
                app.EnableHttps = request.EnableHttps.Value;
            if (request.ForceHttps.HasValue)
                app.ForceHttps = request.ForceHttps.Value;
            if (request.LetsEncryptEmail != null)
                app.LetsEncryptEmail = string.IsNullOrWhiteSpace(request.LetsEncryptEmail) ? null : request.LetsEncryptEmail;

            // Update Git/build configuration
            if (request.GitRepository != null)
                app.GitRepository = request.GitRepository;
            if (request.GitBranch != null)
                app.GitBranch = request.GitBranch;
            if (request.GitProviderId.HasValue)
                app.GitProviderId = request.GitProviderId.Value == 0 ? null : request.GitProviderId;
            if (request.DockerfilePath != null)
                app.Dockerfile = request.DockerfilePath;
            if (request.BuildContext != null)
                app.BuildContext = request.BuildContext;
            if (request.DockerBuildTarget != null)
                app.DockerBuildTarget = request.DockerBuildTarget;
            if (request.BuildArgs != null)
                app.BuildArgs = request.BuildArgs;
            if (request.AutoDeployOnPush.HasValue)
                app.AutoDeployOnPush = request.AutoDeployOnPush.Value;
            if (request.CloneSubmodules.HasValue)
                app.CloneSubmodules = request.CloneSubmodules.Value;
            if (request.EnableGitLfs.HasValue)
                app.EnableGitLfs = request.EnableGitLfs.Value;
            if (request.EnablePreviewDeployments.HasValue)
                app.EnablePreviewDeployments = request.EnablePreviewDeployments.Value;
            if (request.PreviewUrlTemplate != null)
                app.PreviewUrlTemplate = request.PreviewUrlTemplate;

            // Update resource limits
            if (request.MemoryLimitMb.HasValue)
                app.MemoryLimitBytes = request.MemoryLimitMb.Value * 1024 * 1024;
            if (request.CpuLimit.HasValue)
                app.CpuLimit = request.CpuLimit.Value;

            // Update environment variables if provided
            if (request.EnvironmentVariables != null)
            {
                // Remove existing non-secret env vars
                await _applicationRepository.ReplaceNonSecretEnvironmentVariablesAsync(app, request.EnvironmentVariables, cancellationToken);
            }

            if (app.UsePrivateRegistry)
            {
                if (string.IsNullOrWhiteSpace(app.RegistryServer))
                    return new ApplicationUpdateResult(false, "Registry server is required when enabling a private registry");
                if (string.IsNullOrWhiteSpace(app.RegistryPassword))
                    return new ApplicationUpdateResult(false, "Registry password/token is required when enabling a private registry");
            }

            await _applicationRepository.UpdateAsync(app, cancellationToken);

            // Handle webhook lifecycle when AutoDeployOnPush is toggled
            if (request.AutoDeployOnPush.HasValue && app.SourceType == ApplicationSourceType.Git && app.GitProviderId.HasValue)
            {
                if (app.AutoDeployOnPush && !previousAutoDeployOnPush)
                {
                    // Toggled ON - register webhook
                    await RegisterWebhookForApplicationAsync(app, cancellationToken);
                }
                else if (!app.AutoDeployOnPush && previousAutoDeployOnPush)
                {
                    // Toggled OFF - unregister webhook
                    await UnregisterWebhookForApplicationAsync(app, cancellationToken);
                }
            }

            _logger.LogInformation("Updated application {AppId} - {AppName}", app.Id, app.Name);

            return new ApplicationUpdateResult(true, "Application updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating application {AppId}", applicationId);
            return new ApplicationUpdateResult(false, "Failed to update application", ex.Message);
        }
    }

    public async Task<ApplicationDeletionResult> DeleteApplicationAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        var app = await _applicationRepository.GetByIdWithAllRelationsAsync(applicationId, cancellationToken);

        if (app == null)
            return new ApplicationDeletionResult(false, "Application not found");

        try
        {
            // Unregister webhook from Git provider before deleting
            if (app.SourceType == ApplicationSourceType.Git && app.GitProviderId.HasValue && app.AutoDeployOnPush)
            {
                await UnregisterWebhookForApplicationAsync(app, cancellationToken);
            }

            // Remove running containers/services
            var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
            if (latestDeployment != null)
            {
                if (!string.IsNullOrEmpty(latestDeployment.ServiceId))
                {
                    try
                    {
                        await _dockerService.RemoveServiceAsync(app.Server, latestDeployment.ServiceId, cancellationToken);
                        _logger.LogInformation("Removed service {ServiceId} for application {AppName}", latestDeployment.ServiceId, app.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove service {ServiceId}", latestDeployment.ServiceId);
                    }
                }

                if (!string.IsNullOrEmpty(latestDeployment.ContainerId))
                {
                    try
                    {
                        await _dockerService.StopContainerAsync(app.Server, latestDeployment.ContainerId, cancellationToken);
                        await _dockerService.RemoveContainerAsync(app.Server, latestDeployment.ContainerId, cancellationToken);
                        _logger.LogInformation("Removed container {ContainerId} for application {AppName}", latestDeployment.ContainerId, app.Name);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to remove container {ContainerId}", latestDeployment.ContainerId);
                    }
                }
            }

            // Delete from database
            await _applicationRepository.DeleteAsync(app, cancellationToken);

            _logger.LogInformation("Deleted application {AppName} (ID: {AppId})", app.Name, app.Id);

            return new ApplicationDeletionResult(true, "Application deleted successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting application {AppId}", applicationId);
            return new ApplicationDeletionResult(false, "Failed to delete application", ex.Message);
        }
    }

    public async Task<ApplicationScaleResult> ScaleApplicationAsync(int applicationId, int replicas, CancellationToken cancellationToken = default)
    {
        if (replicas < 1)
            return new ApplicationScaleResult(false, "Replicas must be at least 1");

        var app = await _applicationRepository.GetByIdWithServerAndDeploymentsAsync(applicationId, cancellationToken);

        if (app == null)
            return new ApplicationScaleResult(false, "Application not found");

        // Only Swarm services can be scaled
        if (!app.Server.IsSwarm)
            return new ApplicationScaleResult(false, "Only Swarm services can be scaled");

        var latestDeployment = app.Deployments.OrderByDescending(d => d.StartedAt).FirstOrDefault();
        if (latestDeployment == null || string.IsNullOrEmpty(latestDeployment.ServiceId))
            return new ApplicationScaleResult(false, "No service found to scale");

        try
        {
            _logger.LogInformation("Scaling application {AppName} to {Replicas} replicas", app.Name, replicas);

            // Use UpdateServiceAsync to change replica count
            var updateRequest = new UpdateServiceRequest(Replicas: replicas);
            await _dockerService.UpdateServiceAsync(app.Server, latestDeployment.ServiceId, updateRequest, cancellationToken);

            // Persist desired replica count for both generic and swarm-specific fields
            app.Replicas = replicas;
            app.SwarmReplicas = replicas;
            await _applicationRepository.UpdateAsync(app, cancellationToken);

            return new ApplicationScaleResult(true, $"Application scaled to {replicas} replicas", replicas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scaling application {AppId} to {Replicas} replicas", applicationId, replicas);
            return new ApplicationScaleResult(false, "Failed to scale application", ErrorDetails: ex.Message);
        }
    }

    /// <summary>
    /// Generate a webhook secret, construct the callback URL, register the webhook on the Git provider,
    /// and persist the secret on the application entity.
    /// </summary>
    private async Task RegisterWebhookForApplicationAsync(Application app, CancellationToken cancellationToken)
    {
        try
        {
            var webhookBaseUrl = await GetWebhookBaseUrlAsync(cancellationToken);
            if (webhookBaseUrl == null)
            {
                _logger.LogWarning(
                    "Cannot register webhook for application {AppName}: HostCraft domain is not configured in System Settings. " +
                    "Configure a domain in Settings so GitHub can deliver push events.",
                    app.Name);
                return;
            }

            // Generate a cryptographically secure webhook secret
            var webhookSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var webhookUrl = $"{webhookBaseUrl}/api/webhooks/github/{app.Uuid}";

            var registered = await _gitProviderService.RegisterWebhookAsync(app, webhookUrl, webhookSecret);
            if (registered)
            {
                app.WebhookSecret = webhookSecret;
                await _applicationRepository.UpdateAsync(app, cancellationToken);
                _logger.LogInformation("Registered webhook for application {AppName} at {WebhookUrl}", app.Name, webhookUrl);
            }
            else
            {
                _logger.LogWarning("Failed to register webhook for application {AppName}. Auto-deploy on push will not work until the webhook is registered.", app.Name);
            }
        }
        catch (Exception ex)
        {
            // Webhook registration failure should not block app creation/update
            _logger.LogError(ex, "Error registering webhook for application {AppName}. Auto-deploy on push will not work until the webhook is registered.", app.Name);
        }
    }

    /// <summary>
    /// Unregister the webhook from the Git provider and clear the secret.
    /// </summary>
    private async Task UnregisterWebhookForApplicationAsync(Application app, CancellationToken cancellationToken)
    {
        try
        {
            await _gitProviderService.UnregisterWebhookAsync(app);
            app.WebhookSecret = null;
            await _applicationRepository.UpdateAsync(app, cancellationToken);
            _logger.LogInformation("Unregistered webhook for application {AppName}", app.Name);
        }
        catch (Exception ex)
        {
            // Webhook unregistration failure should not block app update/deletion
            _logger.LogError(ex, "Error unregistering webhook for application {AppName}", app.Name);
        }
    }

    /// <summary>
    /// Get the external base URL for HostCraft from system settings.
    /// Returns null if no domain is configured.
    /// </summary>
    private async Task<string?> GetWebhookBaseUrlAsync(CancellationToken cancellationToken)
    {
        var settings = await _systemSettingsService.GetSettingsAsync(cancellationToken);
        if (settings == null)
            return null;

        // Prefer the API domain if configured, otherwise use the main HostCraft domain
        // (the Web UI proxies /api/* to the API via YARP)
        var domain = settings.HostCraftApiDomain ?? settings.HostCraftDomain;
        if (string.IsNullOrWhiteSpace(domain))
            return null;

        var scheme = settings.HostCraftEnableHttps ? "https" : "http";

        // Handle domains that already include scheme
        if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return domain.TrimEnd('/');
        }

        return $"{scheme}://{domain.TrimEnd('/')}";
    }
}
