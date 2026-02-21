using HostCraft.Core.Entities;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Interfaces.Repositories;
using HostCraft.Core.Models;
using HostCraft.Infrastructure.Proxy;
using Microsoft.Extensions.Logging;

namespace HostCraft.Infrastructure.Services;

/// <summary>
/// Manages Traefik proxy routing configuration for Docker Swarm services.
/// Extracts logic previously embedded in DomainsController and ApplicationsController.
/// </summary>
public class TraefikService : ITraefikService
{
    private readonly IDockerService _dockerService;
    private readonly IApplicationRepository _applicationRepository;
    private readonly ILogger<TraefikService> _logger;

    public TraefikService(
        IDockerService dockerService,
        IApplicationRepository applicationRepository,
        ILogger<TraefikService> logger)
    {
        _dockerService = dockerService;
        _applicationRepository = applicationRepository;
        _logger = logger;
    }

    public async Task UpdateServiceLabelsAsync(int applicationId, CancellationToken cancellationToken = default)
    {
        try
        {
            var app = await _applicationRepository.GetByIdWithServerAndDomainsAsync(applicationId, cancellationToken);

            if (app == null)
            {
                _logger.LogWarning("Application {AppId} not found for Traefik label update", applicationId);
                return;
            }

            if (!app.Server.IsSwarm)
            {
                _logger.LogDebug("Application {AppName} is not on a Swarm server, skipping Traefik update", app.Name);
                return;
            }

            // Find the Docker service for this application
            var services = await _dockerService.ListServicesAsync(app.Server, cancellationToken);
            var serviceName = NormalizeServiceName(app.Name);
            var service = services.FirstOrDefault(s =>
                string.Equals(s.Name, serviceName, StringComparison.OrdinalIgnoreCase));

            if (service == null)
            {
                _logger.LogWarning("Docker service {ServiceName} not found for application {AppId}, cannot update Traefik labels",
                    serviceName, app.Id);
                return;
            }

            _logger.LogInformation("Updating Traefik labels for service {ServiceName} (application {AppId})",
                service.Name, app.Id);

            // Build Traefik routing labels based on application domains
            var traefikLabels = TraefikLabelBuilder.BuildLabels(app, "hostcraft_hostcraft-network");

            // Build HostCraft management labels
            var allLabels = BuildHostCraftLabels(app);

            // Merge Traefik labels
            foreach (var label in traefikLabels)
            {
                allLabels[label.Key] = label.Value;
            }

            // Build network list: always preserve the project network for internal DNS,
            // and add Traefik network when routing labels are present.
            var networks = new List<string>();

            // Always include the project-scoped overlay network so inter-service DNS is preserved
            if (app.Project != null)
            {
                var projectNetworkName = $"{Docker.DockerNameHelper.NormalizeNetworkName(app.Project.Name)}-network";
                networks.Add(projectNetworkName);
            }

            if (traefikLabels.Any())
            {
                networks.Add("hostcraft_hostcraft-network");
            }

            // Update the Docker service
            var updateRequest = new UpdateServiceRequest(
                Image: null,
                Replicas: null,
                EnvironmentVariables: null,
                Labels: allLabels,
                Networks: networks);

            await _dockerService.UpdateServiceAsync(app.Server, service.Id, updateRequest, cancellationToken);

            _logger.LogInformation("Service {ServiceName} updated with {LabelCount} Traefik labels and {NetworkCount} networks",
                service.Name, traefikLabels.Count, networks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update Traefik labels for application {AppId}", applicationId);
            // Don't throw - Traefik label update failures should not break the calling operation
        }
    }

    private static Dictionary<string, string> BuildHostCraftLabels(Application app)
    {
        return new Dictionary<string, string>
        {
            { "hostcraft.managed", "true" },
            { "hostcraft.application.id", app.Id.ToString() },
            { "hostcraft.application.uuid", app.Uuid.ToString() },
            { "hostcraft.application.name", app.Name },
            { "hostcraft.project.id", app.ProjectId.ToString() },
            { "hostcraft.server.id", app.ServerId.ToString() }
        };
    }

    private static string NormalizeServiceName(string name)
    {
        return name.ToLowerInvariant().Replace(" ", "-").Replace("_", "-");
    }
}
