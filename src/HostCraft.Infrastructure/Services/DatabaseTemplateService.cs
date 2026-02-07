using System.Text.Json;
using System.Linq;
using HostCraft.Core.Entities;
using HostCraft.Core.Enums;
using HostCraft.Core.Interfaces;
using HostCraft.Core.Models;
using HostCraft.Infrastructure.Persistence;
using HostCraft.Infrastructure.Docker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CreateContainerRequest = HostCraft.Core.Interfaces.CreateContainerRequest;
using CreateServiceRequest = HostCraft.Core.Interfaces.CreateServiceRequest;
using ServicePortMapping = HostCraft.Core.Interfaces.ServicePortMapping;

namespace HostCraft.Infrastructure.Services;

public class DatabaseTemplateService : IDatabaseTemplateService
{
    private readonly HostCraftDbContext _context;
    private readonly IDockerService _dockerService;
    private readonly ISecretManager _secretManager;
    private readonly ILogger<DatabaseTemplateService> _logger;

    public DatabaseTemplateService(
        HostCraftDbContext context,
        IDockerService dockerService,
        ISecretManager secretManager,
        ILogger<DatabaseTemplateService> logger)
    {
        _context = context;
        _dockerService = dockerService;
        _secretManager = secretManager;
        _logger = logger;
    }

    public async Task<List<DatabaseTemplate>> GetAllTemplatesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.DatabaseTemplates
            .Where(t => t.IsActive)
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<DatabaseTemplate?> GetTemplateByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.DatabaseTemplates
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<DatabaseDeploymentResult> DeployDatabaseAsync(
        int templateId,
        string name,
        int serverId,
        int projectId,
        string? customDockerImage = null,
        Dictionary<string, string>? customEnvVars = null,
        long? memoryLimitBytes = null,
        double? cpuLimit = null,
        int? publishedPort = null,
        CancellationToken cancellationToken = default)
    {
        var template = await GetTemplateByIdAsync(templateId, cancellationToken);
        if (template == null)
        {
            throw new InvalidOperationException($"Database template {templateId} not found");
        }

        var server = await _context.Servers
            .FirstOrDefaultAsync(s => s.Id == serverId, cancellationToken);
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (server == null)
        {
            throw new InvalidOperationException($"Server {serverId} not found");
        }

        if (project == null)
        {
            throw new InvalidOperationException($"Project {projectId} not found");
        }

        var applicationName = name?.Trim();
        if (string.IsNullOrWhiteSpace(applicationName))
        {
            throw new InvalidOperationException("Database name is required.");
        }

        await EnsureApplicationNameAvailableAsync(server, applicationName, cancellationToken);

        _logger.LogInformation("Deploying database {Name} from template {Template} to server {Server}",
            applicationName, template.Name, server.Name);

        var publishedPortValue = await AllocatePublishedPortAsync(
            server,
            publishedPort ?? template.DefaultPort,
            allowIncrement: !publishedPort.HasValue,
            cancellationToken);
        var containerPort = template.DefaultPort > 0 ? template.DefaultPort : publishedPortValue;

        // Parse default environment variables
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(template.DefaultEnvironmentVariables))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(template.DefaultEnvironmentVariables);
                if (parsed != null)
                {
                    foreach (var entry in parsed)
                    {
                        envVars[entry.Key] = entry.Value;
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse default environment variables for template {Template}", template.Name);
            }
        }

        // Override with custom environment variables
        if (customEnvVars != null)
        {
            foreach (var kvp in customEnvVars)
            {
                envVars[kvp.Key] = kvp.Value;
            }
        }

        var resolvedEnv = DatabaseTemplateBestPractices.ResolveEnvironmentVariables(template, applicationName, envVars);
        envVars = new Dictionary<string, string>(resolvedEnv.EffectiveVariables, StringComparer.OrdinalIgnoreCase);
        var definitionLookup = resolvedEnv.ResolvedDefinitions.ToDictionary(
            d => d.Key,
            d => d,
            StringComparer.OrdinalIgnoreCase);

        // Log resolved environment variables for debugging
        _logger.LogInformation("Deploying {Template} with {Count} environment variables:", template.Name, envVars.Count);
        foreach (var env in envVars)
        {
            var displayValue = definitionLookup.TryGetValue(env.Key, out var def) && def.IsSecret
                ? "***REDACTED***"
                : env.Value;
            _logger.LogInformation("  {Key} = {Value}", env.Key, displayValue);
        }

        var dockerImage = string.IsNullOrWhiteSpace(customDockerImage)
            ? template.DockerImage
            : customDockerImage.Trim();

        // Create application entity
        var application = new Application
        {
            Name = applicationName,
            Description = $"{template.Name} database",
            ProjectId = projectId,
            ServerId = serverId,
            SourceType = ApplicationSourceType.DatabaseTemplate,
            DatabaseType = template.Type,
            DockerImage = dockerImage,
            Port = containerPort,
            PublishedPort = publishedPortValue,
            DeploymentMode = server.Type == ServerType.SwarmManager ? DeploymentMode.Service : DeploymentMode.Container,
            Replicas = 1,
            MemoryLimitBytes = memoryLimitBytes ?? template.RecommendedMemoryBytes,
            CpuLimit = (long?)((cpuLimit ?? template.RecommendedCpuLimit ?? 0.5) * 1000000000), // Convert to NanoCpus
            AutoRestart = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Applications.Add(application);
        await _context.SaveChangesAsync(cancellationToken);

        // Add environment variables
        foreach (var envVar in envVars)
        {
            var isSecret = definitionLookup.TryGetValue(envVar.Key, out var resolvedDefinition)
                ? resolvedDefinition.IsSecret
                : LooksSensitive(envVar.Key);

            await _secretManager.SetEnvironmentVariableAsync(
                application.Id,
                envVar.Key,
                envVar.Value,
                isSecret,
                cancellationToken);
        }

        // Create volume for data persistence
        // NOTE: Docker named volumes are created with root ownership by default.
        // For containers running as non-root users (like postgres, mongodb, etc.):
        // - Use environment variables to specify subdirectories (e.g., PGDATA for PostgreSQL)
        // - Official database images handle permissions via entrypoint scripts
        // - The subdirectory approach lets the container create dirs with correct ownership
        var normalizedName = applicationName
            .ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
        var volumeName = $"{normalizedName}-data-{application.Id}";
        _context.Volumes.Add(new Volume
        {
            Name = volumeName,
            ApplicationId = application.Id,
            ServerId = serverId,
            Driver = "local",
            MountPoint = template.DefaultVolumePath,
            SizeBytes = 0,
            IsBackedUp = false
        });

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created application record for database {Name} with ID {Id}", applicationName, application.Id);

        // Track deployment lifecycle so UI reflects real status instead of staying queued
        var deployment = new Deployment
        {
            ApplicationId = application.Id,
            Status = DeploymentStatus.Running,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            TriggeredBy = "DatabaseTemplate",
            ImageTag = dockerImage
        };
        _context.Deployments.Add(deployment);
        await _context.SaveChangesAsync(cancellationToken);

        // Deploy the container/service
        try
        {
            await DeployDatabaseContainerAsync(
                application,
                server,
                template,
                dockerImage,
                volumeName,
                envVars,
                deployment,
                project.Name,
                cancellationToken);

            var completedAt = DateTime.UtcNow;
            application.LastDeployedAt = completedAt;
            deployment.Status = DeploymentStatus.Success;
            deployment.FinishedAt = completedAt;

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Successfully deployed database {Name}", applicationName);
        }
        catch (Exception ex)
        {
            deployment.Status = DeploymentStatus.Failed;
            deployment.FinishedAt = DateTime.UtcNow;
            deployment.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogError(ex, "Failed to deploy database {Name}", name);
            throw;
        }

        return new DatabaseDeploymentResult(
            application,
            resolvedEnv.ResolvedDefinitions,
            new Dictionary<string, string>(envVars, StringComparer.OrdinalIgnoreCase));
    }

    private static string NormalizeDeploymentName(string name)
    {
        return name.ToLowerInvariant().Replace(" ", "-");
    }

    private async Task EnsureApplicationNameAvailableAsync(
        Server server,
        string name,
        CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        var exists = await _context.Applications
            .AsNoTracking()
            .AnyAsync(a => a.ServerId == server.Id && a.Name == trimmed, cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException(
                $"An application named '{trimmed}' already exists on server '{server.Name}'. Delete it or choose a different database name.");
        }
    }

    private async Task EnsureDeploymentNameAvailableAsync(
        Server server,
        string deploymentName,
        CancellationToken cancellationToken)
    {
        var existingContainers = await _dockerService.ListContainersAsync(server, showAll: true, cancellationToken);
        if (existingContainers.Any(c => string.Equals(c.Name, deploymentName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"A container named '{deploymentName}' already exists on server '{server.Name}'. Remove it (e.g., docker rm -f {deploymentName}) or choose a different database name.");
        }

        if (server.Type == ServerType.SwarmManager)
        {
            var existingServices = await _dockerService.ListServicesAsync(server, cancellationToken);
            if (existingServices.Any(s => string.Equals(s.Name, deploymentName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"A service named '{deploymentName}' already exists on server '{server.Name}'. Remove it (docker service rm {deploymentName}) or choose a different database name.");
            }
        }
    }

    private static bool LooksSensitive(string key)
    {
        return key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
               key.Contains("KEY", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<int> AllocatePublishedPortAsync(
        Server server,
        int desiredPort,
        bool allowIncrement,
        CancellationToken cancellationToken)
    {
        if (desiredPort <= 0)
        {
            desiredPort = 1024;
        }

        var usedPorts = await _context.Applications
            .Where(a => a.ServerId == server.Id && a.PublishedPort.HasValue)
            .Select(a => a.PublishedPort!.Value)
            .ToListAsync(cancellationToken);

        var reserved = new HashSet<int>(usedPorts);
        var activePorts = await GetActiveHostPortsAsync(server, cancellationToken);
        reserved.UnionWith(activePorts);
        var port = desiredPort;
        const int maxPort = 65535;

        if (!allowIncrement && reserved.Contains(port))
        {
            throw new InvalidOperationException(
                $"Port {port} is already in use on server {server.Id}. Choose a different port or allow automatic allocation.");
        }

        while (reserved.Contains(port))
        {
            port++;
            if (port > maxPort)
            {
                throw new InvalidOperationException($"Unable to allocate an available port on server {server.Id}");
            }
        }

        return port;
    }

    private async Task<HashSet<int>> GetActiveHostPortsAsync(Server server, CancellationToken cancellationToken)
    {
        try
        {
            var ports = new HashSet<int>();

            var containers = await _dockerService.ListContainersAsync(server, showAll: true, cancellationToken);
            foreach (var port in containers
                .SelectMany(c => c.PublishedPorts)
                .Where(p => p.PublicPort.HasValue)
                .Select(p => p.PublicPort!.Value))
            {
                ports.Add(port);
            }

            if (server.Type == ServerType.SwarmManager)
            {
                var services = await _dockerService.ListServicesAsync(server, cancellationToken);
                foreach (var published in services.SelectMany(s => s.PublishedPorts))
                {
                    ports.Add(published.PublishedPort);
                }
            }

            return ports;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to inspect active ports on server {ServerId}", server.Id);
            return new HashSet<int>();
        }
    }

    private async Task DeployDatabaseContainerAsync(
        Application application,
        Server server,
        DatabaseTemplate template,
        string dockerImage,
        string volumeName,
        Dictionary<string, string> envVars,
        Deployment deployment,
        string projectName,
        CancellationToken cancellationToken)
    {
        var deploymentName = application.ServiceName;
        var containerPort = application.Port ?? template.DefaultPort;
        var hostPort = application.PublishedPort ?? containerPort;

        await EnsureDeploymentNameAvailableAsync(server, deploymentName, cancellationToken);

        // Pull the image
        _logger.LogInformation("Pulling image {Image}", dockerImage);
        await _dockerService.PullImageAsync(server, dockerImage, cancellationToken: cancellationToken);

        // Ensure project-scoped network exists so the database shares overlay with its apps
        var networkName = $"{DockerNameHelper.NormalizeNetworkName(projectName)}-network";
        await _dockerService.EnsureNetworkExistsAsync(server, networkName, cancellationToken);

        if (server.Type == ServerType.SwarmManager && application.DeploymentMode == DeploymentMode.Service)
        {
            // Deploy as Swarm service
            _logger.LogInformation("Deploying as Swarm service");

            var mounts = new Dictionary<string, string>
            {
                { volumeName, template.DefaultVolumePath }
            };

            var serviceRequest = new CreateServiceRequest(
                Name: deploymentName,
                Image: dockerImage,
                Replicas: 1,
                EnvironmentVariables: envVars,
                Labels: null,
                Networks: new List<string> { networkName },
                Port: null,
                PortMappings: new List<ServicePortMapping>
                {
                    new ServicePortMapping(hostPort, containerPort)
                },
                Mounts: mounts,
                MemoryLimit: application.MemoryLimitBytes,
                CpuLimit: application.CpuLimit
            );

            var serviceId = await _dockerService.CreateServiceAsync(server, serviceRequest, cancellationToken);
            application.SwarmServiceId = serviceId;
            deployment.ServiceId = serviceId;

            _logger.LogInformation("Created Swarm service {ServiceId} for database {Name}", serviceId, application.Name);
        }
        else
        {
            // Deploy as standalone container
            _logger.LogInformation("Deploying as standalone container");

            var volumes = new Dictionary<string, string>
            {
                { volumeName, template.DefaultVolumePath }
            };

            var containerRequest = new CreateContainerRequest(
                Name: deploymentName,
                Image: dockerImage,
                EnvironmentVariables: envVars,
                Labels: null,
                Networks: new List<string> { networkName },
                PortBindings: new Dictionary<int, int>
                {
                    { containerPort, hostPort }
                },
                Volumes: volumes,
                MemoryLimit: application.MemoryLimitBytes,
                CpuLimit: application.CpuLimit
            );

            var containerId = await _dockerService.CreateContainerAsync(server, containerRequest, cancellationToken);
            await _dockerService.StartContainerAsync(server, containerId, cancellationToken);
            deployment.ContainerId = containerId;

            _logger.LogInformation("Created and started container {ContainerId} for database {Name}", containerId, application.Name);
        }
    }
}
